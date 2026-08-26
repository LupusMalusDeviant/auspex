package resolver

import (
	"net"
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/config"
)

// Firefox asks for this domain and switches off its own encrypted
// resolution when NXDOMAIN comes back. Any other answer - including a
// blocked 0.0.0.0 - it reads as "no filter" and walks past the resolver.
// Hence NXDOMAIN whatever the configured block mode.
func TestCanaryDomainIsAlwaysNxdomain(t *testing.T) {
	for _, modus := range []string{"nxdomain", "zeroip", "refused", "custom"} {
		t.Run(modus, func(t *testing.T) {
			cfg := config.Default()
			cfg.Filter.BlockMode = modus
			res := buildResolver(t, cfg, nil)

			req := new(dns.Msg)
			req.SetQuestion("use-application-dns.net.", dns.TypeA)
			w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}
			res.ServeDNS(w, req)

			if w.msg.Rcode != dns.RcodeNameError {
				t.Errorf("rcode = %s, expected NXDOMAIN", dns.RcodeToString[w.msg.Rcode])
			}
		})
	}
}

func TestCanaryCanBeSwitchedOff(t *testing.T) {
	cfg := config.Default()
	cfg.Filter.DoHCanary = false
	res := buildResolver(t, cfg, nil)

	req := new(dns.Msg)
	req.SetQuestion("use-application-dns.net.", dns.TypeA)
	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}
	res.ServeDNS(w, req)

	// Without the special case the query takes the normal route - here with
	// no upstream, so SERVFAIL. The point is that it is not NXDOMAIN from
	// the rule.
	if entry := res.QueryLog().Recent(1)[0]; entry.Source == "doh-kanarie" {
		t.Error("switched off, the special handling must not apply")
	}
}

func TestCanaryDetectionHitsOnlyThatDomain(t *testing.T) {
	hits := []string{
		"use-application-dns.net",
		"sub.use-application-dns.net",
	}
	daneben := []string{
		"use-application-dns.net.example.com",
		"not-use-application-dns.net",
		"application-dns.net",
		"",
	}
	for _, n := range hits {
		if !isCanary(n) {
			t.Errorf("%q should be recognised", n)
		}
	}
	for _, n := range daneben {
		if isCanary(n) {
			t.Errorf("%q should NOT be recognised", n)
		}
	}
}

func TestCanaryAppearsInTheQueryLog(t *testing.T) {
	res := buildResolver(t, config.Default(), nil)

	req := new(dns.Msg)
	req.SetQuestion("use-application-dns.net.", dns.TypeA)
	res.ServeDNS(&fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}, req)

	entry := res.QueryLog().Recent(1)[0]
	if entry.Source != "doh-kanarie" || entry.Action != "blocked" {
		t.Errorf("protocol: %s/%s", entry.Action, entry.Source)
	}
}
