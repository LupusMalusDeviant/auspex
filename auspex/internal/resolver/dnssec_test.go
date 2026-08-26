package resolver

import (
	"context"
	"net"
	"sync"
	"testing"

	"github.com/miekg/dns"

	"auspex/internal/cache"
	"auspex/internal/config"
	"auspex/internal/learn"
	"auspex/internal/names"
	"auspex/internal/querylog"
	"auspex/internal/rules"
	"auspex/internal/upstream"
)

// fakeUpstream remembers what was actually sent upstream.
type fakeUpstream struct {
	// Warm() calls Exchange from several goroutines at once. Without this
	// mutex two of them write to got at the same time, and -race turns the
	// test run into a failure - irregularly, because it depends on how the
	// goroutines happen to meet. A fault in the test double, not in the
	// resolver: that has been counting its hits through atomic.Int64 for a
	// long time.
	mu       sync.Mutex
	got      *dns.Msg
	setAD    bool
	response func(*dns.Msg) *dns.Msg
}

func (f *fakeUpstream) Addr() string  { return "fake://upstream" }
func (f *fakeUpstream) Proto() string { return "fake" }

// Last returns the most recently seen query. Through an accessor rather than
// the field, so reading happens under the mutex as well.
func (f *fakeUpstream) Last() *dns.Msg {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.got
}

func (f *fakeUpstream) Exchange(_ context.Context, msg *dns.Msg) (*dns.Msg, error) {
	f.mu.Lock()
	f.got = msg.Copy()
	f.mu.Unlock()
	if f.response != nil {
		return f.response(msg), nil
	}
	reply := new(dns.Msg)
	reply.SetReply(msg)
	reply.AuthenticatedData = f.setAD
	reply.Answer = []dns.RR{&dns.A{
		Hdr: dns.RR_Header{Name: msg.Question[0].Name, Rrtype: dns.TypeA, Class: dns.ClassINET, Ttl: 60},
		A:   net.IPv4(192, 0, 2, 1),
	}}
	return reply, nil
}

func resolverWithUpstream(t *testing.T, cfg config.Config, up *fakeUpstream) *Resolver {
	t.Helper()
	qlog, err := querylog.New(querylog.Options{Enabled: true, Size: 16})
	if err != nil {
		t.Fatal(err)
	}
	mgr, err := learn.NewManager(t.TempDir(), quietLogger())
	if err != nil {
		t.Fatal(err)
	}
	hostNames, _ := names.New(names.Options{})
	pool := upstream.NewPool([]upstream.Upstream{up}, upstream.PoolOptions{})

	res, err := New(cfg, rules.NewFromRules("test", nil, nil),
		cache.New(cache.Options{}), pool, qlog, mgr, hostNames)
	if err != nil {
		t.Fatal(err)
	}
	return res
}

// The core statement: a client must not be able to switch off validation at
// the upstream. Without this any device on the network could take itself out
// of DNSSEC protection.
func TestClientCannotSwitchOffValidation(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, config.Default(), up)

	req := new(dns.Msg)
	req.SetQuestion("example.com.", dns.TypeA)
	req.CheckingDisabled = true // "please do not validate"

	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1"), Port: 40000}}
	res.ServeDNS(w, req)

	if up.Last() == nil {
		t.Fatal("the upstream was not asked")
	}
	if up.Last().CheckingDisabled {
		t.Error("the CD bit was passed through - the client would have switched validation off")
	}
	if !up.Last().AuthenticatedData {
		t.Error("the AD bit is missing from the query - then we never learn whether it was validated")
	}
}

func TestPassthroughLeavesTheQueryAlone(t *testing.T) {
	cfg := config.Default()
	cfg.Upstream.DNSSEC = "passthrough"
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, cfg, up)

	req := new(dns.Msg)
	req.SetQuestion("example.com.", dns.TypeA)
	req.CheckingDisabled = true

	res.ServeDNS(&fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}, req)

	if !up.Last().CheckingDisabled {
		t.Error("with passthrough the query should stay unchanged")
	}
}

// Whoever did not ask for DNSSEC does not get the AD bit set either
// (RFC 6840) - otherwise the answer suggests an assurance the client never
// requested.
func TestAdBitOnlyForClientsThatAskForIt(t *testing.T) {
	up := &fakeUpstream{setAD: true}
	res := resolverWithUpstream(t, config.Default(), up)

	without := new(dns.Msg)
	without.SetQuestion("example.com.", dns.TypeA)
	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}
	res.ServeDNS(w, without)

	if w.msg.AuthenticatedData {
		t.Error("the client did not ask but gets AD set")
	}
	// It is in the log all the same - there it is the information that counts.
	if entries := res.QueryLog().Recent(1); len(entries) != 1 || !entries[0].Validated {
		t.Error("the validation belongs in the query log even when the client did not ask for it")
	}

	with := new(dns.Msg)
	with.SetQuestion("example.com.", dns.TypeA)
	with.AuthenticatedData = true
	w2 := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP("127.0.0.1")}}
	res.ServeDNS(w2, with)

	if !w2.msg.AuthenticatedData {
		t.Error("the client asked but gets no AD")
	}
}
