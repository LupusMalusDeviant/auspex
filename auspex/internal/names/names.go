// Package names turns client IPs into device names.
//
// Without it every finding and every statistic carries a bare IP. "Suspected
// DNS tunnelling at 192.168.1.43" forces a lookup; "at living room TV" can
// be acted on straight away.
//
// Two sources, in this order:
//
//  1. Fixed mapping from the configuration - beats everything.
//  2. Reverse lookup against the router. A Fritz!Box answers PTR for its
//     DHCP clients and thereby supplies exactly the names shown in its
//     home-network menu.
//
// The DNS path must never be held up by any of this: Name() always answers
// immediately from memory and at most kicks off resolution in the background.
package names

import (
	"context"
	"fmt"
	"net"
	"net/netip"
	"sort"
	"strings"
	"sync"
	"time"

	"auspex/internal/neigh"

	"github.com/miekg/dns"
)

type Options struct {
	// Static maps an IP or CIDR to a name.
	Static map[string]string
	// Resolve enables the reverse lookup.
	Resolve bool
	// Via is the server answering PTR (usually the router).
	Via string
	// ReverseVia sends the reverse lookup for certain address ranges
	// somewhere else, keyed by prefix.
	//
	// The router answers for its own network and knows nothing beyond it. A
	// device reached over a tunnel arrives with an address the router has
	// never seen — 100.64.0.0/10 for Tailscale — and stays nameless for
	// good, because no amount of waiting will teach the router about it.
	// Somebody else knows: Tailscale's own resolver answers PTR for those.
	//
	// Longest prefix wins, so a narrow range can be steered out of a wide
	// one.
	ReverseVia map[string]string
	// TTL decides how long a resolved name applies.
	TTL time.Duration
	// NegativeTTL bremst erfolglose Versuche aus.
	NegativeTTL time.Duration
	Timeout     time.Duration

	// Neighbors maps local addresses to MACs, DeviceNames maps MACs to
	// device names. Together they give the route that is missing without
	// them: attributing a temporary IPv6 address to a device. The router
	// cannot do that - it does not know those addresses at all.
	Neighbors   *neigh.Table
	DeviceNames *DeviceNames
}

type entry struct {
	name    string
	expires time.Time
}

type Resolver struct {
	opts    Options
	static  []staticRule
	client  *dns.Client
	via     string
	routes  []reverseRoute
	resolve bool
	neigh   *neigh.Table
	devices *DeviceNames

	mu      sync.RWMutex
	cache   map[netip.Addr]entry
	pending map[netip.Addr]bool
}

type staticRule struct {
	prefix netip.Prefix
	name   string
}

func New(opts Options) (*Resolver, error) {
	if opts.TTL <= 0 {
		opts.TTL = time.Hour
	}
	if opts.NegativeTTL <= 0 {
		opts.NegativeTTL = 10 * time.Minute
	}
	if opts.Timeout <= 0 {
		opts.Timeout = 2 * time.Second
	}

	r := &Resolver{
		opts:    opts,
		cache:   map[netip.Addr]entry{},
		pending: map[netip.Addr]bool{},
		resolve: opts.Resolve && (opts.Via != "" || len(opts.ReverseVia) > 0),
		neigh:   opts.Neighbors,
		devices: opts.DeviceNames,
	}
	for raw, name := range opts.Static {
		prefix, err := parsePrefix(raw)
		if err != nil {
			return nil, err
		}
		r.static = append(r.static, staticRule{prefix: prefix, name: name})
	}
	if r.resolve {
		if opts.Via != "" {
			r.via = withPort(opts.Via)
		}
		for raw, via := range opts.ReverseVia {
			prefix, err := netip.ParsePrefix(strings.TrimSpace(raw))
			if err != nil {
				return nil, fmt.Errorf("hosts.reverse_via: %q is not a network: %w", raw, err)
			}
			if strings.TrimSpace(via) == "" {
				return nil, fmt.Errorf("hosts.reverse_via: no server for %q", raw)
			}
			r.routes = append(r.routes, reverseRoute{
				prefix: prefix.Masked(),
				via:    withPort(via),
			})
		}
		// Longest prefix first, so the narrowest rule decides.
		sort.Slice(r.routes, func(i, j int) bool {
			return r.routes[i].prefix.Bits() > r.routes[j].prefix.Bits()
		})
		r.client = &dns.Client{Net: "udp", Timeout: opts.Timeout}
	}
	return r, nil
}

// reverseRoute sends the reverse lookup for one address range to one server.
type reverseRoute struct {
	prefix netip.Prefix
	via    string
}

// serverFor picks who to ask. The narrowest matching range wins; without a
// match it is the ordinary route, which is usually the router.
func (r *Resolver) serverFor(addr netip.Addr) string {
	for _, route := range r.routes {
		if route.prefix.Contains(addr) {
			return route.via
		}
	}
	return r.via
}

func withPort(server string) string {
	server = strings.TrimSpace(server)
	if _, _, err := net.SplitHostPort(server); err != nil {
		return net.JoinHostPort(server, "53")
	}
	return server
}

func parsePrefix(raw string) (netip.Prefix, error) {
	raw = strings.TrimSpace(raw)
	if strings.Contains(raw, "/") {
		p, err := netip.ParsePrefix(raw)
		if err != nil {
			return netip.Prefix{}, err
		}
		return p.Masked(), nil
	}
	addr, err := netip.ParseAddr(raw)
	if err != nil {
		return netip.Prefix{}, err
	}
	return netip.PrefixFrom(addr, addr.BitLen()), nil
}

// Name returns the known name or "". Never blocks.
func (r *Resolver) Name(addr netip.Addr) string {
	if r == nil || !addr.IsValid() {
		return ""
	}
	// Fixed mapping first: it is the operator's intent and beats any name a
	// device reports for itself.
	for _, rule := range r.static {
		if rule.prefix.Contains(addr) {
			return rule.name
		}
	}
	// Via the neighbour table: address -> MAC -> name.
	//
	// Before the reverse lookup, because this route is the only one that
	// works at all with temporary IPv6 addresses - the router answers no PTR
	// for them, because it does not know them. And it is the faster one at
	// the same time: a lookup in memory instead of a query to the network.
	if name := r.viaNeighbours(addr); name != "" {
		return name
	}

	if !r.resolve {
		return ""
	}

	r.mu.RLock()
	e, ok := r.cache[addr]
	r.mu.RUnlock()
	if ok && time.Now().Before(e.expires) {
		return e.name
	}

	r.lookupAsync(addr)
	if ok {
		return e.name // expired, but better than nothing
	}
	return ""
}

// Neighbors exposes the neighbour table so profile matching can use it too.
// Keeping it twice would mean asking the same kernel twice.
func (r *Resolver) Neighbors() *neigh.Table {
	if r == nil {
		return nil
	}
	return r.neigh
}

// viaNeighbours resolves a local address to a name through its MAC.
func (r *Resolver) viaNeighbours(addr netip.Addr) string {
	if r.neigh == nil || r.devices == nil {
		return ""
	}
	mac := r.neigh.Mac(addr)
	if mac == "" {
		return ""
	}
	return r.devices.Name(mac)
}

// lookupAsync starts exactly one attempt per address at a time.
func (r *Resolver) lookupAsync(addr netip.Addr) {
	r.mu.Lock()
	if r.pending[addr] {
		r.mu.Unlock()
		return
	}
	r.pending[addr] = true
	r.mu.Unlock()

	go func() {
		name := r.lookup(addr)
		ttl := r.opts.TTL
		if name == "" {
			ttl = r.opts.NegativeTTL
		}
		r.mu.Lock()
		r.cache[addr] = entry{name: name, expires: time.Now().Add(ttl)}
		delete(r.pending, addr)
		r.mu.Unlock()
	}()
}

func (r *Resolver) lookup(addr netip.Addr) string {
	arpa, err := dns.ReverseAddr(addr.String())
	if err != nil {
		return ""
	}
	msg := new(dns.Msg)
	msg.SetQuestion(arpa, dns.TypePTR)

	ctx, cancel := context.WithTimeout(context.Background(), r.opts.Timeout)
	defer cancel()

	server := r.serverFor(addr)
	if server == "" {
		// An address in no range and no ordinary route: asking the router
		// about a tunnel address would only produce a timeout per query.
		return ""
	}

	resp, _, err := r.client.ExchangeContext(ctx, msg, server)
	if err != nil || resp == nil {
		return ""
	}
	for _, rr := range resp.Answer {
		if ptr, ok := rr.(*dns.PTR); ok {
			return tidy(ptr.Ptr)
		}
	}
	return ""
}

// tidy cuts off the router domain: "Phone.fritz.box." becomes "Phone".
func tidy(ptr string) string {
	name := strings.TrimSuffix(ptr, ".")
	if name == "" {
		return ""
	}
	if i := strings.IndexByte(name, '.'); i > 0 {
		name = name[:i]
	}
	return name
}

// Known returns the names resolved so far (for the control plane).
func (r *Resolver) Known() map[string]string {
	out := map[string]string{}
	if r == nil {
		return out
	}
	for _, rule := range r.static {
		out[rule.prefix.Addr().String()] = rule.name
	}
	r.mu.RLock()
	defer r.mu.RUnlock()
	for addr, e := range r.cache {
		if e.name != "" {
			out[addr.String()] = e.name
		}
	}
	return out
}
