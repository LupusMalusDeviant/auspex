package resolver

import (
	"net/netip"
	"strconv"
	"strings"

	"github.com/miekg/dns"
)

// Local zones: names only the home network knows.
//
// Without this your own router becomes a stranger. "fritz.box" is a real,
// publicly registered domain - resolve it outwards and you get back the
// address of a completely unrelated server, and type your router credentials
// into it. And names like "TV.fritz.box", or the reverse resolution of
// private addresses, are unknown outside anyway; they would come back empty
// where the router used to answer.
//
// While the Fritz!Box itself was the network's resolver this did not show -
// it answers these names by itself. As soon as Auspex takes its place it has
// to take on the same job: such queries belong forwarded to the router, not
// sent to the internet.

// isLocalName says whether a name falls into one of the local zones.
func (r *Resolver) isLocalName(name string) bool {
	for _, zone := range r.localZones {
		if name == zone || strings.HasSuffix(name, "."+zone) {
			return true
		}
	}
	return r.localReverse && isPrivateReverse(name)
}

// isPrivateReverse recognises the reverse resolution of private addresses.
//
// Deliberately via the actual address rather than suffix comparisons:
// checking "168.192.in-addr.arpa" by hand misses 172.16/12, where only part
// of the range is private.
func isPrivateReverse(name string) bool {
	rest, ok := strings.CutSuffix(name, ".in-addr.arpa")
	if !ok {
		return false
	}

	parts := strings.Split(rest, ".")
	if len(parts) == 0 || len(parts) > 4 {
		return false
	}

	// Partial zones like "1.168.192.in-addr.arpa" mean 192.168.1.0/24 and
	// belong to the home network too. Padding goes at the front: in
	// in-addr.arpa the last octet stands on the left, so it is the low-order
	// positions that are missing. Padding at the back would give 0.192.168.1
	// - a completely different address.
	for len(parts) < 4 {
		parts = append([]string{"0"}, parts...)
	}

	// in-addr.arpa reads backwards: 43.178.168.192 means 192.168.1.43.
	var oktette [4]byte
	for i, t := range parts {
		n, err := strconv.Atoi(t)
		if err != nil || n < 0 || n > 255 {
			return false
		}
		oktette[3-i] = byte(n)
	}

	address := netip.AddrFrom4(oktette)
	return address.IsPrivate() || address.IsLinkLocalUnicast()
}

// askLocal forwards the query to the router.
//
// Without a cache and without the detour through the upstream pool: this is
// a question to a device on the same network, answered in microseconds, and
// its answer changes with every DHCP lease.
func (r *Resolver) askLocal(req *dns.Msg) (*dns.Msg, error) {
	client := &dns.Client{Net: "udp", Timeout: r.localTimeout}
	reply, _, err := client.Exchange(req.Copy(), r.localVia)
	if err != nil {
		return nil, err
	}

	// Answers truncated over UDP get fetched again over TCP - a device list
	// in a home network can be longer than one packet.
	if reply.Truncated {
		tcp := &dns.Client{Net: "tcp", Timeout: r.localTimeout}
		if viaTCP, _, err := tcp.Exchange(req.Copy(), r.localVia); err == nil {
			return viaTCP, nil
		}
	}

	return reply, nil
}
