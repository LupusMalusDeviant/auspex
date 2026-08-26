package resolver

import (
	"net/netip"
	"strings"

	"github.com/miekg/dns"
)

// DNS rebinding: a name from the public internet that answers with an address
// from inside the network.
//
// The attack is short: a page on attacker.example resolves to a public address,
// the browser loads it, and a moment later the same name resolves to
// 192.168.1.1. For the browser it is still the same origin — so the script may
// talk to it, and it is now talking to the router. Everything on the network
// that answers without authentication is reachable that way: the router's
// interface, a printer, a camera, a NAS.
//
// The resolver is the right place to stop it, because it is the only one that
// sees both halves: the name and the address behind it.
//
// # What must not be caught by this
//
// Several perfectly legitimate names answer with internal addresses, and
// blocking them breaks things that have nothing to do with an attack. The
// list below is not guesswork — it comes from reading the resolutions of a
// live installation:
//
//   - ipv4only.arpa (RFC 7050) is how a device discovers NAT64. Blocking it
//     breaks IPv6-only mobile networks.
//   - dns.msftncsi.com answers with a ULA on purpose; it is how Windows
//     decides whether it has IPv6 connectivity.
//   - Plex hands out private addresses under *.plex.direct, with a valid
//     certificate, so the app can stream locally.
//
// Names from local zones and from the rewrite table never get here: they are
// answered before the query goes upstream. That is by construction, not by
// luck — split-horizon DNS is the deliberate version of exactly this pattern.
var builtinRebindAllow = []string{
	// RFC 7050 — NAT64 discovery.
	"ipv4only.arpa",
	// Windows connectivity check, deliberately a ULA.
	"dns.msftncsi.com",
	// RFC 8375 — the domain reserved for home networks.
	"home.arpa",
	"localhost",
	// Plex streams locally over private addresses under a public name.
	"plex.direct",
	// Tailscale's MagicDNS answers with 100.64.0.0/10.
	"ts.net",
	// Seen in a live installation: AWS publishes a private address here for
	// its own diagnostics.
	"diagnostic.networking.aws.dev",
}

// isInternal reports whether an address belongs to the network rather than to
// the internet.
func isInternal(a netip.Addr) bool {
	a = a.Unmap()
	if a.IsPrivate() || a.IsLoopback() || a.IsLinkLocalUnicast() ||
		a.IsLinkLocalMulticast() || a.IsUnspecified() {
		return true
	}
	// Carrier-grade NAT, 100.64.0.0/10. Not "private" in Go's sense, but just
	// as much inside somebody's network — and it is where Tailscale lives,
	// which is why ts.net is on the allowlist above.
	if a.Is4() {
		b := a.As4()
		return b[0] == 100 && b[1] >= 64 && b[1] <= 127
	}
	return false
}

// rebindAllowed reports whether the name is exempt.
func (r *Resolver) rebindAllowed(name string) bool {
	name = strings.TrimSuffix(strings.ToLower(name), ".")
	for _, suffix := range r.rebindAllow {
		if name == suffix || strings.HasSuffix(name, "."+suffix) {
			return true
		}
	}
	return false
}

// rebindBlock returns the offending address if the answer points a public name
// at an address inside the network.
//
// The CNAME chain is followed for the same reason cnameBlock exists: the name
// asked for says nothing about where the chain ends, and the last record is
// what the browser will connect to.
func (r *Resolver) rebindBlock(msg *dns.Msg, name string) (string, bool) {
	if !r.rebindGuard || msg == nil || r.rebindAllowed(name) {
		return "", false
	}
	for _, rr := range msg.Answer {
		var a netip.Addr
		var ok bool
		switch v := rr.(type) {
		case *dns.A:
			a, ok = netip.AddrFromSlice(v.A)
		case *dns.AAAA:
			a, ok = netip.AddrFromSlice(v.AAAA)
		default:
			continue
		}
		if !ok || !isInternal(a) {
			continue
		}
		// A record whose own owner name is exempt is not held against the
		// name that was queried — a CNAME into plex.direct is the normal
		// case, not an attack.
		if r.rebindAllowed(rr.Header().Name) {
			continue
		}
		return a.Unmap().String(), true
	}
	return "", false
}
