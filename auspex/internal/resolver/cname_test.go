package resolver

import (
	"net"
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/config"
	"auspex/internal/rules"
)

// ruleSet builds an engine from individual rule lines.
func ruleSet(t *testing.T, lines ...string) *rules.Engine {
	t.Helper()
	var block, allow []string
	for _, z := range lines {
		if len(z) > 1 && z[0] == 64 && z[1] == 64 {
			allow = append(allow, z)
			continue
		}
		block = append(block, z)
	}
	return rules.NewFromRules("test", block, allow)
}

// chain builds an answer of the shape CNAME cloaking returns: the name
// queried is a harmless first-party subdomain, the target the actual
// tracker.
func chain(question string, targets ...string) func(*dns.Msg) *dns.Msg {
	return func(msg *dns.Msg) *dns.Msg {
		reply := new(dns.Msg)
		reply.SetReply(msg)

		current := dns.Fqdn(question)
		for _, target := range targets {
			reply.Answer = append(reply.Answer, &dns.CNAME{
				Hdr:    dns.RR_Header{Name: current, Rrtype: dns.TypeCNAME, Class: dns.ClassINET, Ttl: 300},
				Target: dns.Fqdn(target),
			})
			current = dns.Fqdn(target)
		}
		reply.Answer = append(reply.Answer, &dns.A{
			Hdr: dns.RR_Header{Name: current, Rrtype: dns.TypeA, Class: dns.ClassINET, Ttl: 300},
			A:   net.IPv4(93, 184, 216, 34),
		})
		return reply
	}
}

func question(t *testing.T, res *Resolver, name string) *fakeWriter {
	t.Helper()
	req := new(dns.Msg)
	req.SetQuestion(dns.Fqdn(name), dns.TypeA)
	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1"), Port: 40000}}
	res.ServeDNS(w, req)
	return w
}

// The core case: the name queried is on no list, the CNAME target is.
// Without this check the tracker gets through.
func TestCnameToABlockedTargetIsBlocked(t *testing.T) {
	up := &fakeUpstream{response: chain("metrics.zeitung.example", "zeitung.tracker.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	w := question(t, res, "metrics.zeitung.example")

	if w.msg.Rcode != dns.RcodeNameError {
		t.Fatalf("rcode = %s, expected NXDOMAIN", dns.RcodeToString[w.msg.Rcode])
	}

	entry := res.QueryLog().Recent(1)[0]
	if entry.Source != "cname" {
		t.Errorf("source = %q, expected cname", entry.Source)
	}
	// Without the target in the log a block on a harmless-looking
	// first-party domain would be inexplicable.
	if entry.Cname != "zeitung.tracker.example" {
		t.Errorf("Cname = %q", entry.Cname)
	}
	if entry.Rule != "||tracker.example^" {
		t.Errorf("rule = %q", entry.Rule)
	}
}

// The real trap: the answer lands in the cache. Without a check on the
// cache path the first query would be blocked and every one after it would
// come through unfiltered.
func TestCnameBlockAppliesFromTheCacheToo(t *testing.T) {
	up := &fakeUpstream{response: chain("metrics.zeitung.example", "zeitung.tracker.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	question(t, res, "metrics.zeitung.example")
	second := question(t, res, "metrics.zeitung.example")

	if second.msg.Rcode != dns.RcodeNameError {
		t.Fatalf("second query = %s, expected NXDOMAIN", dns.RcodeToString[second.msg.Rcode])
	}
	if entry := res.QueryLog().Recent(1)[0]; entry.Source != "cname" {
		t.Errorf("the second query came from %q instead of through the CNAME check", entry.Source)
	}
}

func TestAHarmlessChainGetsThrough(t *testing.T) {
	up := &fakeUpstream{response: chain("bilder.zeitung.example", "cdn.anbieter.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	w := question(t, res, "bilder.zeitung.example")

	if w.msg.Rcode != dns.RcodeSuccess {
		t.Errorf("rcode = %s, expected NOERROR", dns.RcodeToString[w.msg.Rcode])
	}
}

func TestALongChainIsCheckedThroughout(t *testing.T) {
	up := &fakeUpstream{response: chain(
		"a.zeitung.example", "b.cdn.example", "c.tracker.example", "d.ende.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	if w := question(t, res, "a.zeitung.example"); w.msg.Rcode != dns.RcodeNameError {
		t.Error("a blocked target in the middle of the chain has to apply")
	}
}

func TestCnameCheckCanBeSwitchedOff(t *testing.T) {
	cfg := config.Default()
	cfg.Filter.CheckCNAME = false
	up := &fakeUpstream{response: chain("metrics.zeitung.example", "zeitung.tracker.example")}
	res := resolverWithUpstream(t, cfg, up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	if w := question(t, res, "metrics.zeitung.example"); w.msg.Rcode != dns.RcodeSuccess {
		t.Error("switched off, the chain must not be checked")
	}
}

// An exception on the target has to count here too - otherwise the CNAME
// check would be a block that cannot be lifted again.
func TestAnExceptionOnTheTargetCountsToo(t *testing.T) {
	up := &fakeUpstream{response: chain("metrics.zeitung.example", "zeitung.tracker.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^", "@@||zeitung.tracker.example^"))

	if w := question(t, res, "metrics.zeitung.example"); w.msg.Rcode != dns.RcodeSuccess {
		t.Error("the exception on the CNAME target should have applied")
	}
}

func TestCounterForCnameBlocks(t *testing.T) {
	up := &fakeUpstream{response: chain("metrics.zeitung.example", "zeitung.tracker.example")}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	question(t, res, "metrics.zeitung.example")

	if got := res.Stats().BlockedCNAME; got != 1 {
		t.Errorf("BlockedCNAME = %d, expected 1", got)
	}
}
