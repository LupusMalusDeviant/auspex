// Package config loads and validates the Auspex configuration.
package config

import (
	"fmt"
	"net"
	"net/netip"
	"os"
	"strings"
	"time"

	"gopkg.in/yaml.v3"

	"auspex/internal/services"
)

// Duration allows "5s"/"1h" in YAML instead of raw nanoseconds.
type Duration time.Duration

func (d *Duration) UnmarshalYAML(value *yaml.Node) error {
	var s string
	if err := value.Decode(&s); err != nil {
		return err
	}
	parsed, err := time.ParseDuration(s)
	if err != nil {
		return fmt.Errorf("invalid duration %q: %w", s, err)
	}
	*d = Duration(parsed)
	return nil
}

// D returns the duration as a time.Duration.
func (d Duration) D() time.Duration { return time.Duration(d) }

type Config struct {
	Listen   Listen    `yaml:"listen"`
	Upstream Upstream  `yaml:"upstream"`
	Cache    Cache     `yaml:"cache"`
	Filter   Filter    `yaml:"filter"`
	Clients  []Client  `yaml:"clients"`
	Hosts    Hosts     `yaml:"hosts"`
	Local    Local     `yaml:"local"`
	Learning Learning  `yaml:"learning"`
	Rewrites []Rewrite `yaml:"rewrites"`
	QueryLog QueryLog  `yaml:"querylog"`
	API      API       `yaml:"api"`
}

// Address is one address to listen on.
//
// In the ordinary case a plain string. The long form marks an address as
// optional:
//
//	udp:
//	  - "192.168.1.61:53"
//	  - address: "100.64.0.5:53"
//	    optional: true
type Address struct {
	Addr string
	// Optional keeps a failed bind from taking the resolver down with it.
	//
	// For addresses that are not reliably there at startup. A VPN interface
	// comes up on its own schedule, and losing the whole household's DNS
	// because the tunnel was three seconds late is the wrong trade.
	//
	// Optional does not mean "give up quietly": the address is retried in
	// the background until it appears. Without that, "it was not there at
	// boot" would silently become "it is gone until somebody restarts the
	// container" — which is worse than the crash it replaces, because a
	// crash at least heals itself through the restart policy.
	Optional bool
}

func (a *Address) UnmarshalYAML(value *yaml.Node) error {
	var short string
	if err := value.Decode(&short); err == nil {
		a.Addr = strings.TrimSpace(short)
		return nil
	}
	var long struct {
		Address  string `yaml:"address"`
		Optional bool   `yaml:"optional"`
	}
	if err := value.Decode(&long); err != nil {
		return fmt.Errorf(`listen address: expected "host:port" or {address, optional}: %w`, err)
	}
	a.Addr = strings.TrimSpace(long.Address)
	a.Optional = long.Optional
	return nil
}

// Addresses takes either a single address or a list in YAML.
// Several addresses are not a convenience: on a host with a global IPv6
// address a wildcard bind turns the resolver into an open resolver, and that
// is a tool for amplification attacks. Bind deliberately instead of 0.0.0.0.
type Addresses []Address

func (a *Addresses) UnmarshalYAML(value *yaml.Node) error {
	if value.Kind == yaml.ScalarNode || value.Kind == yaml.MappingNode {
		var single Address
		if err := value.Decode(&single); err != nil {
			return err
		}
		if single.Addr == "" {
			*a = nil
			return nil
		}
		*a = Addresses{single}
		return nil
	}
	var many []Address
	if err := value.Decode(&many); err != nil {
		return fmt.Errorf("listen: expected an address or a list: %w", err)
	}
	*a = many
	return nil
}

// Bind builds a list of required addresses — the ordinary case in code and
// in tests. Optional ones are set explicitly, because they should never be
// the thing that happens by accident.
func Bind(addrs ...string) Addresses {
	out := make(Addresses, 0, len(addrs))
	for _, a := range addrs {
		out = append(out, Address{Addr: a})
	}
	return out
}

// Required reports whether at least one address in the list has to come up.
func (a Addresses) Required() int {
	n := 0
	for _, addr := range a {
		if !addr.Optional {
			n++
		}
	}
	return n
}

type Listen struct {
	UDP Addresses `yaml:"udp"`
	TCP Addresses `yaml:"tcp"`
	// TLS is DNS-over-TLS, usually on port 853.
	TLS Addresses `yaml:"tls"`
	// HTTPS is DNS-over-HTTPS. Without cert_file/key_file only plaintext
	// HTTP is spoken - meant for running behind a reverse proxy that
	// terminates TLS. It does not belong straight on the internet then.
	HTTPS Addresses `yaml:"https"`

	CertFile string `yaml:"cert_file"`
	KeyFile  string `yaml:"key_file"`
	DoHPath  string `yaml:"doh_path"`

	// TrustedProxies are networks whose X-Forwarded-For is believed. Without
	// this list every request behind a proxy would arrive carrying the
	// proxy's address; with too wide a list any client could invent an
	// origin for itself.
	TrustedProxies []string `yaml:"trusted_proxies"`
}

type Upstream struct {
	// Servers accepts udp://host:port, tcp://, tls://host:853 (DoT)
	// and https://host/dns-query (DoH). Bare IPs are read as udp://ip:53.
	Servers []string `yaml:"servers"`
	// Bootstrap resolves the host names of DoT/DoH upstreams. Without it
	// Auspex would ask itself at startup — the classic chicken-and-egg loop.
	Bootstrap []string `yaml:"bootstrap"`
	Timeout   Duration `yaml:"timeout"`
	// DNSSEC governs how validation is handled:
	//   enforce      Requires the upstream to validate (CD=0) and asks with
	//                AD=1 whether the answer was validated.
	//   passthrough  The query is passed through unchanged.
	// Auspex does not validate itself - a chain of its own would be
	// security-critical code you do not get right on the side. Anyone wanting
	// local validation puts a validating resolver upstream.
	DNSSEC string `yaml:"dnssec"`
	// Strategy: "failover" (in order) or "race" (all in parallel, first one wins).
	Strategy string `yaml:"strategy"`
	// FailureThreshold consecutive errors put an upstream on the bench for
	// FailureCooldown.
	FailureThreshold int      `yaml:"failure_threshold"`
	FailureCooldown  Duration `yaml:"failure_cooldown"`
}

type Cache struct {
	Enabled     bool     `yaml:"enabled"`
	MaxEntries  int      `yaml:"max_entries"`
	MinTTL      Duration `yaml:"min_ttl"`
	MaxTTL      Duration `yaml:"max_ttl"`
	NegativeTTL Duration `yaml:"negative_ttl"`
	// Prefetch renews frequently asked entries before they expire.
	Prefetch bool `yaml:"prefetch"`
	// PrefetchThreshold: at which fraction of remaining TTL (0..1) to prefetch.
	PrefetchThreshold float64 `yaml:"prefetch_threshold"`
	PrefetchMinHits   int     `yaml:"prefetch_min_hits"`
	// ServeStale hands out expired answers when every upstream is dead.
	ServeStale Duration `yaml:"serve_stale"`
}

type Filter struct {
	Lists      []List   `yaml:"lists"`
	BlockRules []string `yaml:"block_rules"`
	AllowRules []string `yaml:"allow_rules"`
	// BlockMode: nxdomain | zeroip | refused | custom
	BlockMode string   `yaml:"block_mode"`
	BlockIPv4 string   `yaml:"block_ipv4"`
	BlockIPv6 string   `yaml:"block_ipv6"`
	BlockTTL  Duration `yaml:"block_ttl"`
	// CheckCNAME checks an answer's CNAME chain against the rule set.
	//
	// The most common trick against DNS filters: the site creates a subdomain
	// of its own and points it by CNAME at the tracker. No block list carries
	// the first-party subdomain - the tracker gets through even though its
	// actual target would be blocked.
	CheckCNAME bool `yaml:"check_cname"`
	// BlockServices blocks services from the catalogue for every client.
	BlockServices []string `yaml:"block_services"`
	// RebindProtection blocks answers that point a public name at an address
	// inside the network — the DNS-rebinding attack. On by default: both
	// comparable projects do it that way, and a household that does not know
	// the attack is exactly the one that needs the protection.
	//
	// Names from local zones and from the rewrite table are unaffected. They
	// are answered before the query goes upstream, so they never reach the
	// check — split-horizon DNS is the deliberate version of the same
	// pattern and must keep working.
	RebindProtection bool `yaml:"rebind_protection"`
	// RebindAllow are name suffixes exempt from it, on top of the built-in
	// list. For anyone running nip.io, sslip.io, lvh.me or a similar
	// developer helper that resolves to private addresses on purpose.
	RebindAllow []string `yaml:"rebind_allow"`
	// DoHCanary answers use-application-dns.net with NXDOMAIN. Firefox reads
	// that as "this network filters" and switches off its own encrypted
	// resolution instead of walking past the resolver.
	//
	// Always NXDOMAIN, whatever the configured block mode: this is the only
	// answer Firefox understands as a signal.
	DoHCanary      bool     `yaml:"doh_canary"`
	UpdateInterval Duration `yaml:"update_interval"`
	CacheDir       string   `yaml:"cache_dir"`
}

type List struct {
	Name    string `yaml:"name"`
	URL     string `yaml:"url"`
	Path    string `yaml:"path"`
	Enabled *bool  `yaml:"enabled"`
	// Allow turns the whole list into an allowlist.
	Allow bool `yaml:"allow"`
}

func (l List) IsEnabled() bool { return l.Enabled == nil || *l.Enabled }

// Client also carries JSON tags: this profile arrives over the control API
// as well. Without them the JSON decoder would silently ignore snake_case
// fields - the result being a profile that exists but does nothing.
type Client struct {
	Name  string   `yaml:"name" json:"name"`
	Match []string `yaml:"match" json:"match"` // IPs or CIDRs
	// Macs binds the profile to devices rather than to addresses.
	//
	// Necessary since Auspex became the network's resolver: Windows and
	// Android rotate their temporary IPv6 addresses daily. A profile hanging
	// off an address stops applying tomorrow - silently, which is the worst
	// kind of failure. The MAC stays.
	Macs []string `yaml:"macs" json:"macs,omitempty"`
	// Policy governs learn mode:
	//   open    Normal operation, only block lists apply.
	//   learn   Everything is resolved and recorded.
	//   enforce Deny by default: only what was learned or allowed survives.
	Policy string `yaml:"policy" json:"policy,omitempty"`
	// Filtering=false switches filtering off entirely for this client.
	Filtering  *bool    `yaml:"filtering" json:"filtering,omitempty"`
	BlockRules []string `yaml:"block_rules" json:"block_rules,omitempty"`
	AllowRules []string `yaml:"allow_rules" json:"allow_rules,omitempty"`
	// BlockServices blocks whole services through the built-in catalogue,
	// without having to know their domains.
	BlockServices []string `yaml:"block_services" json:"block_services,omitempty"`
	// SafeSearch sends the listed search engines to the host they serve
	// filtered results from. Per profile, not per network: the children's
	// tablet and the workshop computer do not want the same setting, and a
	// global switch ends up being turned off for everybody.
	SafeSearch []string   `yaml:"safe_search" json:"safe_search,omitempty"`
	Schedules  []Schedule `yaml:"schedules" json:"schedules,omitempty"`
}

func (c Client) FilteringEnabled() bool { return c.Filtering == nil || *c.Filtering }

// Hosts turns client IPs into device names.
type Hosts struct {
	// Static maps an IP or CIDR to a name and beats everything else.
	Static map[string]string `yaml:"static"`
	// Resolve enables the reverse lookup. A Fritz!Box answers PTR for its
	// DHCP clients - which makes the names from its home-network menu
	// available with no further upkeep.
	Resolve bool   `yaml:"resolve"`
	Via     string `yaml:"via"`
	// ReverseVia sends the reverse lookup for certain ranges elsewhere.
	//
	// The router answers for its own network and knows nothing beyond it. A
	// device reached over a tunnel arrives with an address the router has
	// never seen, and stays nameless for good. Tailscale's own resolver
	// answers PTR for 100.64.0.0/10:
	//
	//   reverse_via:
	//     "100.64.0.0/10": "100.100.100.100"
	ReverseVia map[string]string `yaml:"reverse_via"`

	// Neighbors enables the route via the host's neighbour table:
	// address -> MAC -> device name. The only one that works with temporary
	// IPv6 addresses - the router answers no reverse lookup for those,
	// because it does not know them at all.
	Neighbors bool `yaml:"neighbors"`
	// DeviceNamePath is the file the control plane writes the router's device
	// list into.
	DeviceNamePath string `yaml:"device_names"`

	TTL         Duration `yaml:"ttl"`
	NegativeTTL Duration `yaml:"negative_ttl"`
	Timeout     Duration `yaml:"timeout"`
}

// Local describes the names only the home network knows.
//
// Once Auspex is the network's resolver it has to take on part of the
// router's job: "fritz.box" is a real public domain - resolved outwards it
// returns the address of somebody else's server, and whoever types their
// router credentials in there has given them away. Device names and the
// reverse resolution of private addresses are unknown outside anyway.
type Local struct {
	// Zones are name suffixes that go to the router.
	Zones []string `yaml:"zones"`
	// Via is the router. Empty means: take hosts.via.
	Via string `yaml:"via"`
	// Reverse additionally sends the reverse resolution of private address
	// ranges there.
	Reverse bool     `yaml:"reverse"`
	Timeout Duration `yaml:"timeout"`
}

// Learning configures learn mode for every profile.
type Learning struct {
	Dir string `yaml:"dir"`
	// Granularity: "domain" allows the registrable domain (needed with CDNs
	// that use changing host names), "exact" only the exact name.
	Granularity string `yaml:"granularity"`
	// MaxEntries caps one store. A device generating random names (or doing
	// DNS tunnelling) should not be able to flood the allowlist.
	MaxEntries   int      `yaml:"max_entries"`
	SaveInterval Duration `yaml:"save_interval"`
}

// Schedule is time-based filtering: focus hours, children's hours, quiet nights.
type Schedule struct {
	Name string `yaml:"name" json:"name"`
	// Days: mon,tue,wed,thu,fri,sat,sun or "all" / "weekdays" / "weekend".
	Days []string `yaml:"days"`
	From string   `yaml:"from"` // "22:00"
	To   string   `yaml:"to"`   // "06:00", may run past midnight

	Block []string `yaml:"block" json:"block,omitempty"`
	Allow []string `yaml:"allow" json:"allow,omitempty"`
	// BlockServices applies only inside the time window.
	BlockServices []string `yaml:"block_services" json:"block_services,omitempty"`
	// SafeSearch applies only inside the time window, on top of whatever the
	// profile already asks for. "Filtered results while the children are
	// awake" is the case; neither Pi-hole nor AdGuard Home can express it.
	SafeSearch []string `yaml:"safe_search" json:"safe_search,omitempty"`
}

// Rewrite maps internal names onto internal addresses (split-horizon DNS).
type Rewrite struct {
	Domain string   `yaml:"domain"` // exact or *.example.com
	A      string   `yaml:"a"`
	AAAA   string   `yaml:"aaaa"`
	CNAME  string   `yaml:"cname"`
	TTL    Duration `yaml:"ttl"`
}

type QueryLog struct {
	Enabled bool `yaml:"enabled"`
	// Size is the ring buffer in memory that the control plane reads.
	Size int    `yaml:"size"`
	File string `yaml:"file"` // optional: JSONL transcript
	// AnonymizeClients truncates client IPs (last octet / last 80 bits).
	AnonymizeClients bool `yaml:"anonymize_clients"`
}

type API struct {
	Enabled bool   `yaml:"enabled"`
	Listen  string `yaml:"listen"`
	// Token protects the API. Empty = no protection (bind to loopback only!).
	Token string `yaml:"token"`
}

// TrustedPrefixes returns the trusted proxy networks.
func (l Listen) TrustedPrefixes() []netip.Prefix {
	out := make([]netip.Prefix, 0, len(l.TrustedProxies))
	for _, raw := range l.TrustedProxies {
		if p, err := parseTrusted(raw); err == nil {
			out = append(out, p)
		}
	}
	return out
}

func parseTrusted(raw string) (netip.Prefix, error) {
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

// Default returns a working configuration with no YAML file at all.
func Default() Config {
	return Config{
		Listen: Listen{UDP: Bind("127.0.0.1:53"), TCP: Bind("127.0.0.1:53")},
		Local: Local{
			Zones:   []string{"fritz.box"},
			Reverse: true,
			Timeout: Duration(2 * time.Second),
		},
		Upstream: Upstream{
			Servers:          []string{"https://dns.quad9.net/dns-query", "tls://one.one.one.one:853"},
			Bootstrap:        []string{"9.9.9.9:53", "1.1.1.1:53"},
			Timeout:          Duration(5 * time.Second),
			Strategy:         "failover",
			FailureThreshold: 3,
			FailureCooldown:  Duration(30 * time.Second),
			DNSSEC:           "enforce",
		},
		Cache: Cache{
			Enabled:           true,
			MaxEntries:        100_000,
			MinTTL:            Duration(30 * time.Second),
			MaxTTL:            Duration(24 * time.Hour),
			NegativeTTL:       Duration(5 * time.Minute),
			Prefetch:          true,
			PrefetchThreshold: 0.15,
			PrefetchMinHits:   3,
			ServeStale:        Duration(1 * time.Hour),
		},
		Filter: Filter{
			BlockMode:  "nxdomain",
			BlockIPv4:  "0.0.0.0",
			BlockIPv6:  "::",
			BlockTTL:   Duration(10 * time.Second),
			CheckCNAME: true,
			DoHCanary:  true,
			// On by default. Verified against a live installation before
			// making that call: of 13,393 recorded resolutions, twelve
			// pointed at an internal address, and every one of them is
			// either a local zone (answered before this check) or on the
			// built-in allowlist.
			RebindProtection: true,
			UpdateInterval:   Duration(24 * time.Hour),
			CacheDir:         "var/lists",
		},
		Learning: Learning{
			Dir:          "var/learn",
			Granularity:  "domain",
			MaxEntries:   5000,
			SaveInterval: Duration(30 * time.Second),
		},
		Hosts: Hosts{
			TTL:         Duration(1 * time.Hour),
			NegativeTTL: Duration(10 * time.Minute),
			Timeout:     Duration(2 * time.Second),
			// On as soon as the host supports it: without this route devices
			// stay nameless under IPv6.
			Neighbors:      true,
			DeviceNamePath: "/var/lib/auspex-shared/devices.json",
		},
		QueryLog: QueryLog{Enabled: true, Size: 10_000},
		API:      API{Enabled: true, Listen: "127.0.0.1:5380"},
	}
}

// Load reads a YAML file over the defaults.
func Load(path string) (Config, error) {
	cfg := Default()
	raw, err := os.ReadFile(path)
	if err != nil {
		return cfg, err
	}
	if err := yaml.Unmarshal(raw, &cfg); err != nil {
		return cfg, fmt.Errorf("%s: %w", path, err)
	}
	return cfg, cfg.Validate()
}

// ValidateClient checks a single profile - at startup as well as when one is
// created through the interface. The same check in both places, so a profile
// coming from the browser is not treated more leniently than one from the
// file.
func (c Config) ValidateClient(client Client) error {
	switch client.Policy {
	case "", "open", "learn", "enforce", "quarantine":
	default:
		return fmt.Errorf("client %q: unknown policy %q (open|learn|enforce|quarantine)",
			client.Name, client.Policy)
	}
	// The same for the MACs: one that cannot be read as such would produce a
	// profile that never applies - and nobody notices, because nothing
	// breaks.
	for _, m := range client.Macs {
		if _, err := ParseMac(m); err != nil {
			return fmt.Errorf("Client %q: %w", client.Name, err)
		}
	}
	// A typo in a service name should be noticed rather than ending up as a
	// silently permitted service.
	if _, unknown := services.Rules(client.BlockServices); len(unknown) > 0 {
		return fmt.Errorf("client %q: unknown services %v", client.Name, unknown)
	}
	// And the same again for SafeSearch, for a worse reason: a misspelt
	// provider does not break anything visibly. It simply is not enforced,
	// and somebody goes on believing the tablet is filtered.
	if unknown := services.UnknownSafeSearch(client.SafeSearch); len(unknown) > 0 {
		return fmt.Errorf("client %q: unknown safe_search providers %v", client.Name, unknown)
	}
	for _, sched := range client.Schedules {
		if _, unknown := services.Rules(sched.BlockServices); len(unknown) > 0 {
			return fmt.Errorf("client %q, schedule %q: unknown services %v",
				client.Name, sched.Name, unknown)
		}
		if unknown := services.UnknownSafeSearch(sched.SafeSearch); len(unknown) > 0 {
			return fmt.Errorf("client %q, schedule %q: unknown safe_search providers %v",
				client.Name, sched.Name, unknown)
		}
	}
	return nil
}

func (c Config) Validate() error {
	if _, unknown := services.Rules(c.Filter.BlockServices); len(unknown) > 0 {
		return fmt.Errorf("filter.block_services: unknown services %v", unknown)
	}
	switch c.Filter.BlockMode {
	case "nxdomain", "zeroip", "refused", "custom":
	default:
		return fmt.Errorf("unknown block_mode %q (nxdomain|zeroip|refused|custom)", c.Filter.BlockMode)
	}
	switch c.Upstream.DNSSEC {
	case "", "enforce", "passthrough":
	default:
		return fmt.Errorf("unknown upstream.dnssec %q (enforce|passthrough)", c.Upstream.DNSSEC)
	}
	switch c.Upstream.Strategy {
	case "", "failover", "race":
	default:
		return fmt.Errorf("unknown upstream.strategy %q (failover|race)", c.Upstream.Strategy)
	}
	if len(c.Upstream.Servers) == 0 {
		return fmt.Errorf("upstream.servers is empty")
	}
	switch c.Learning.Granularity {
	case "", "domain", "exact":
	default:
		return fmt.Errorf("unknown learning.granularity %q (domain|exact)", c.Learning.Granularity)
	}
	for _, client := range c.Clients {
		if err := c.ValidateClient(client); err != nil {
			return err
		}
	}
	if len(c.Listen.UDP) == 0 && len(c.Listen.TCP) == 0 {
		return fmt.Errorf("neither listen.udp nor listen.tcp is set")
	}
	// At least one address has to be one whose failure counts. All of them
	// optional would mean a resolver that comes up, binds nothing, reports
	// itself healthy and answers not a single query — exactly the state the
	// fatal listener exists to prevent.
	if c.Listen.UDP.Required()+c.Listen.TCP.Required() == 0 {
		return fmt.Errorf("every listen address is optional: at least one has to be required")
	}
	all := append(Addresses{}, c.Listen.UDP...)
	all = append(all, c.Listen.TCP...)
	all = append(all, c.Listen.TLS...)
	all = append(all, c.Listen.HTTPS...)
	for _, addr := range all {
		if _, _, err := net.SplitHostPort(addr.Addr); err != nil {
			return fmt.Errorf("listen address %q: expected host:port", addr.Addr)
		}
	}
	// DoT without a certificate cannot even start - that should show at
	// startup and not when the first client connects.
	if len(c.Listen.TLS) > 0 && (c.Listen.CertFile == "" || c.Listen.KeyFile == "") {
		return fmt.Errorf("listen.tls is set but cert_file/key_file are missing")
	}
	for _, p := range c.Listen.TrustedProxies {
		if _, err := parseTrusted(p); err != nil {
			return fmt.Errorf("listen.trusted_proxies: %w", err)
		}
	}
	return nil
}

// ParseMac normalises a MAC to lower case with colons.
//
// It gets typed sometimes one way, sometimes with dashes, sometimes in upper
// case. A profile that fails to apply because of spelling would be a silent
// fault: nothing breaks, the device is simply unfiltered from then on.
func ParseMac(raw string) (string, error) {
	s := strings.ToLower(strings.TrimSpace(raw))
	s = strings.ReplaceAll(s, "-", ":")
	s = strings.TrimPrefix(s, "mac:")

	hw, err := net.ParseMAC(s)
	if err != nil {
		return "", fmt.Errorf("%q is not a MAC address: %w", raw, err)
	}
	// net.ParseMAC also accepts EUI-64 with eight bytes. Nothing like that
	// turns up on an Ethernet frame in a home network.
	if len(hw) != 6 {
		return "", fmt.Errorf("%q has %d bytes, 6 expected", raw, len(hw))
	}
	return strings.ToLower(hw.String()), nil
}
