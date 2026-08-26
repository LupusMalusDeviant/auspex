package resolver

import (
	"net"
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/config"
)

// fakeWriter takes the answer without needing a network.
type fakeWriter struct {
	remote net.Addr
	msg    *dns.Msg
}

func (w *fakeWriter) LocalAddr() net.Addr  { return &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: 53} }
func (w *fakeWriter) RemoteAddr() net.Addr { return w.remote }
func (w *fakeWriter) WriteMsg(m *dns.Msg) error {
	w.msg = m
	return nil
}
func (w *fakeWriter) Write([]byte) (int, error) { return 0, nil }
func (w *fakeWriter) Close() error              { return nil }
func (w *fakeWriter) TsigStatus() error         { return nil }
func (w *fakeWriter) TsigTimersOnly(bool)       {}
func (w *fakeWriter) Hijack()                   {}

func query(t *testing.T, res *Resolver, client, name string) *dns.Msg {
	t.Helper()
	req := new(dns.Msg)
	req.SetQuestion(dns.Fqdn(name), dns.TypeA)
	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP(client), Port: 40000}}
	res.ServeDNS(w, req)
	return w.msg
}

func learnCfg(policy string) config.Config {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name:   "iot",
		Match:  []string{"10.0.5.0/24"},
		Policy: policy,
	}}
	return cfg
}

// The most important test of learn mode: what the filter blocks must not
// end up in the allowlist - otherwise the tracker is permanently exempt once
// learning is done.
func TestLearnDoesNotRecordBlockedNames(t *testing.T) {
	res := buildResolver(t, learnCfg("learn"), []string{"||tracker.example^"})

	query(t, res, "10.0.5.20", "api.hersteller.example")
	query(t, res, "10.0.5.20", "tracker.example")

	store, _, ok := res.LearnStore("iot")
	if !ok {
		t.Fatal("the learn store is missing")
	}
	entries := store.Entries()
	if len(entries) != 1 {
		t.Fatalf("learned names = %v, expected only api.hersteller.example", entries)
	}
	if entries[0].Name != "api.hersteller.example" {
		t.Errorf("what was learned is %q", entries[0].Name)
	}
}

func TestLearnIgnoresOtherClients(t *testing.T) {
	res := buildResolver(t, learnCfg("learn"), nil)

	query(t, res, "192.168.1.99", "fremd.example") // not in the profile
	store, _, _ := res.LearnStore("iot")

	if len(store.Entries()) != 0 {
		t.Error("only the learning profile's queries may be learned")
	}
}

func TestEnforceBlocksUnlearned(t *testing.T) {
	res := buildResolver(t, learnCfg("enforce"), nil)

	reply := query(t, res, "10.0.5.20", "unknown.example")
	if reply.Rcode != dns.RcodeNameError {
		t.Errorf("rcode = %s, expected NXDOMAIN for an unlearned name", dns.RcodeToString[reply.Rcode])
	}

	exp := res.Explain("unknown.example", "10.0.5.20")
	if !exp.Blocked {
		t.Fatal("explain should report blocked")
	}
	if exp.List != LearnListName {
		t.Errorf("origin = %q, expected %q", exp.List, LearnListName)
	}
}

func TestEnforceAllowsLearned(t *testing.T) {
	res := buildResolver(t, learnCfg("enforce"), nil)
	store, _, _ := res.LearnStore("iot")
	store.Record("api.hersteller.example", "A")

	if exp := res.Explain("api.hersteller.example", "10.0.5.20"); exp.Blocked {
		t.Error("a learned name has to get through")
	}
	// Domain granularity: a different host of the same domain counts too.
	if exp := res.Explain("cdn.hersteller.example", "10.0.5.20"); exp.Blocked {
		t.Error("a different host of the same domain has to get through")
	}
}

func TestEnforceRespectsExplicitAllowRule(t *testing.T) {
	cfg := learnCfg("enforce")
	cfg.Clients[0].AllowRules = []string{"@@||nachgereicht.example^"}
	res := buildResolver(t, cfg, nil)

	if exp := res.Explain("nachgereicht.example", "10.0.5.20"); exp.Blocked {
		t.Error("an explicit allow rule has to override deny-by-default")
	}
	if exp := res.Explain("sonstwas.example", "10.0.5.20"); !exp.Blocked {
		t.Error("everything else stays blocked")
	}
}

func TestEnforceLeavesOtherClientsAlone(t *testing.T) {
	res := buildResolver(t, learnCfg("enforce"), nil)

	if exp := res.Explain("beliebig.example", "192.168.1.99"); exp.Blocked {
		t.Error("deny-by-default may only apply to the enforcing profile")
	}
}

// A blocked device still has to be able to do reverse lookups, otherwise
// every diagnosis turns into a guessing game.
func TestEnforceAllowsReverseLookups(t *testing.T) {
	res := buildResolver(t, learnCfg("enforce"), nil)

	if exp := res.Explain("20.5.0.10.in-addr.arpa", "10.0.5.20"); exp.Blocked {
		t.Error("reverse lookups should get through in enforce mode")
	}
}
