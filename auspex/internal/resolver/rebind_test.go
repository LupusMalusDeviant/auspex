package resolver

import (
	"net"
	"net/netip"
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/config"
)

// answering builds an upstream that answers the queried name with one address.
func answering(ip string) func(*dns.Msg) *dns.Msg {
	return func(msg *dns.Msg) *dns.Msg {
		reply := new(dns.Msg)
		reply.SetReply(msg)
		addr := net.ParseIP(ip)
		hdr := dns.RR_Header{Name: msg.Question[0].Name, Class: dns.ClassINET, Ttl: 60}
		if v4 := addr.To4(); v4 != nil {
			hdr.Rrtype = dns.TypeA
			reply.Answer = []dns.RR{&dns.A{Hdr: hdr, A: v4}}
		} else {
			hdr.Rrtype = dns.TypeAAAA
			reply.Answer = []dns.RR{&dns.AAAA{Hdr: hdr, AAAA: addr.To16()}}
		}
		return reply
	}
}

// The attack itself: a name from the internet answers with the address of the
// router. Without this check a script in the browser talks to the router
// under the attacker's origin.
func TestAPublicNamePointingIntoTheNetworkIsBlocked(t *testing.T) {
	for _, ip := range []string{
		"192.168.1.1", "10.0.0.5", "172.16.4.2", "127.0.0.1",
		"169.254.1.1", "100.64.0.1", "fd00::1", "::1",
	} {
		up := &fakeUpstream{response: answering(ip)}
		res := resolverWithUpstream(t, config.Default(), up)

		msg := query(t, res, "10.0.9.7", "attacker.example")
		if msg.Rcode != dns.RcodeNameError {
			t.Errorf("%s: rcode = %s, expected NXDOMAIN", ip, dns.RcodeToString[msg.Rcode])
			continue
		}
		entry := res.QueryLog().Recent(1)[0]
		if entry.Source != "rebind" {
			t.Errorf("%s: source = %q, expected rebind", ip, entry.Source)
		}
		// Without the address in the log the block is inexplicable: the name
		// is on no list.
		if entry.Rule == "" {
			t.Errorf("%s: the offending address is not in the log", ip)
		}
	}
}

// And the other half, which matters just as much: ordinary answers must pass.
func TestPublicAddressesArePassedThrough(t *testing.T) {
	for _, ip := range []string{"93.184.216.34", "1.1.1.1", "2606:4700::1111", "172.217.18.4"} {
		up := &fakeUpstream{response: answering(ip)}
		res := resolverWithUpstream(t, config.Default(), up)

		msg := query(t, res, "10.0.9.7", "example.com")
		if msg.Rcode != dns.RcodeSuccess || len(msg.Answer) == 0 {
			t.Errorf("%s was blocked although it is a public address", ip)
		}
	}
}

// 172.16.0.0/12 ends at 172.31. Google sits at 172.217 — a check written with
// string prefixes gets this wrong, and this test exists because the first
// version of the query I used to survey the live data got it wrong exactly
// that way.
func TestTheBoundaryOf172IsRespected(t *testing.T) {
	cases := []struct {
		ip       string
		internal bool
	}{
		{"172.15.255.255", false},
		{"172.16.0.0", true},
		{"172.31.255.255", true},
		{"172.32.0.0", false},
		{"172.217.18.4", false},
		{"100.63.255.255", false},
		{"100.64.0.0", true},
		{"100.127.255.255", true},
		{"100.128.0.0", false},
	}
	for _, c := range cases {
		got := isInternal(netip.MustParseAddr(c.ip))
		if got != c.internal {
			t.Errorf("isInternal(%s) = %v, expected %v", c.ip, got, c.internal)
		}
	}
}

// The three names that would have been broken by a naive version. Found by
// reading the resolutions of a live installation, not by thinking hard.
func TestTheLegitimateCasesKeepWorking(t *testing.T) {
	cases := []struct{ name, ip string }{
		// RFC 7050 — without this an IPv6-only mobile network stops working.
		{"ipv4only.arpa", "192.0.0.170"},
		// Windows decides its IPv6 connectivity by this one.
		{"dns.msftncsi.com", "fd3e:4f5a:5b81::1"},
		// AWS publishes a private address for its own diagnostics.
		{"testscenarios.us-east-1.gamma.diagnostic.networking.aws.dev", "10.0.200.10"},
		// Plex streams locally over a public name.
		{"abc123.plex.direct", "192.168.1.44"},
		{"anything.home.arpa", "192.168.1.9"},
	}
	for _, c := range cases {
		up := &fakeUpstream{response: answering(c.ip)}
		res := resolverWithUpstream(t, config.Default(), up)

		msg := query(t, res, "10.0.9.7", c.name)
		if msg.Rcode != dns.RcodeSuccess {
			t.Errorf("%s (%s) was blocked: %s", c.name, c.ip, dns.RcodeToString[msg.Rcode])
		}
	}
}

// A developer running nip.io or lvh.me has to be able to say so.
func TestTheAllowlistFromTheConfigurationIsHonoured(t *testing.T) {
	cfg := config.Default()
	cfg.Filter.RebindAllow = []string{"NIP.IO ", "lvh.me."}

	for _, name := range []string{"app.nip.io", "sub.lvh.me", "lvh.me"} {
		up := &fakeUpstream{response: answering("192.168.1.20")}
		res := resolverWithUpstream(t, cfg, up)

		if msg := query(t, res, "10.0.9.7", name); msg.Rcode != dns.RcodeSuccess {
			t.Errorf("%s was blocked although it is on the allowlist", name)
		}
	}

	// And a name that merely looks similar is not exempt: "evil-nip.io" must
	// not slip through on a suffix match done wrong.
	up := &fakeUpstream{response: answering("192.168.1.20")}
	res := resolverWithUpstream(t, cfg, up)
	if msg := query(t, res, "10.0.9.7", "evil-nip.io"); msg.Rcode != dns.RcodeNameError {
		t.Error("evil-nip.io was treated as part of nip.io")
	}
}

// Switching it off has to work — somebody with a setup we did not foresee
// should not have to patch the binary.
func TestItCanBeSwitchedOff(t *testing.T) {
	cfg := config.Default()
	cfg.Filter.RebindProtection = false

	up := &fakeUpstream{response: answering("192.168.1.1")}
	res := resolverWithUpstream(t, cfg, up)

	if msg := query(t, res, "10.0.9.7", "attacker.example"); msg.Rcode != dns.RcodeSuccess {
		t.Error("blocked although the protection is switched off")
	}
}

// The trap the CNAME check already taught us: an answer lands in the cache,
// and without a check on the hit path the first query is blocked and every
// one after it comes through unchecked.
func TestTheBlockAppliesFromTheCacheToo(t *testing.T) {
	up := &fakeUpstream{response: answering("192.168.1.1")}
	res := resolverWithUpstream(t, config.Default(), up)

	first := query(t, res, "10.0.9.7", "attacker.example")
	second := query(t, res, "10.0.9.7", "attacker.example")

	if first.Rcode != dns.RcodeNameError || second.Rcode != dns.RcodeNameError {
		t.Fatalf("first = %s, second = %s — both have to be blocked",
			dns.RcodeToString[first.Rcode], dns.RcodeToString[second.Rcode])
	}
}

// Split-horizon DNS is the deliberate version of exactly this pattern and has
// to keep working. It is answered before the query goes upstream, so this
// guards the construction rather than a branch.
func TestOurOwnRewritesAreUnaffected(t *testing.T) {
	cfg := config.Default()
	cfg.Rewrites = []config.Rewrite{{Domain: "*.home.example.com", A: "192.168.1.10"}}

	res := resolverWithUpstream(t, cfg, &fakeUpstream{})

	msg := query(t, res, "10.0.9.7", "nas.home.example.com")
	if msg.Rcode != dns.RcodeSuccess || len(msg.Answer) == 0 {
		t.Fatalf("the rewrite was blocked: %s", dns.RcodeToString[msg.Rcode])
	}
	if a, ok := msg.Answer[0].(*dns.A); !ok || a.A.String() != "192.168.1.10" {
		t.Errorf("answer = %v", msg.Answer[0])
	}
}
