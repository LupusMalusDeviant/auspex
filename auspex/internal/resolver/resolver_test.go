package resolver

import (
	"io"
	"log/slog"
	"net/netip"
	"testing"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/cache"
	"auspex/internal/config"
	"auspex/internal/learn"
	"auspex/internal/names"
	"auspex/internal/querylog"
	"auspex/internal/rules"
	"auspex/internal/upstream"
)

func quietLogger() *slog.Logger { return slog.New(slog.NewTextHandler(io.Discard, nil)) }

func buildResolver(t *testing.T, cfg config.Config, block []string) *Resolver {
	t.Helper()
	engine := rules.NewFromRules("test", block, nil)
	qlog, err := querylog.New(querylog.Options{Enabled: true, Size: 16})
	if err != nil {
		t.Fatal(err)
	}
	pool := upstream.NewPool(nil, upstream.PoolOptions{})

	if cfg.Learning.Dir == "" {
		cfg.Learning.Dir = t.TempDir()
	}
	mgr, err := learn.NewManager(cfg.Learning.Dir, quietLogger())
	if err != nil {
		t.Fatal(err)
	}

	hostNames, err := names.New(names.Options{Static: cfg.Hosts.Static})
	if err != nil {
		t.Fatal(err)
	}

	res, err := New(cfg, engine, cache.New(cache.Options{}), pool, qlog, mgr, hostNames)
	if err != nil {
		t.Fatal(err)
	}
	return res
}

func TestScheduleActiveAcrossMidnight(t *testing.T) {
	s, err := compileSchedule("kind", config.Schedule{
		Name: "nachtruhe", Days: []string{"all"}, From: "22:00", To: "06:00",
	})
	if err != nil {
		t.Fatal(err)
	}
	day := time.Date(2026, 8, 22, 0, 0, 0, 0, time.UTC)
	cases := []struct {
		hour, minute int
		want         bool
	}{
		{23, 0, true},  // nach Fensterbeginn
		{2, 0, true},   // past midnight, still inside
		{5, 59, true},  // one minute before the end
		{6, 0, false},  // the end is exclusive
		{12, 0, false}, // mittags zu
		{21, 59, false},
		{22, 0, true}, // the start is inclusive
	}
	for _, c := range cases {
		at := day.Add(time.Duration(c.hour)*time.Hour + time.Duration(c.minute)*time.Minute)
		if got := s.Active(at); got != c.want {
			t.Errorf("Active(%02d:%02d) = %v, expected %v", c.hour, c.minute, got, c.want)
		}
	}
}

func TestScheduleRespectsDays(t *testing.T) {
	s, err := compileSchedule("kind", config.Schedule{Days: []string{"weekend"}, From: "08:00", To: "20:00"})
	if err != nil {
		t.Fatal(err)
	}
	saturday := time.Date(2026, 8, 22, 10, 0, 0, 0, time.UTC) // 2026-08-22 is a Saturday
	monday := time.Date(2026, 8, 24, 10, 0, 0, 0, time.UTC)
	if !s.Active(saturday) {
		t.Error("Saturday should fall inside the weekend window")
	}
	if s.Active(monday) {
		t.Error("Monday must not fall inside the weekend window")
	}
}

func TestProfileMatchingByCIDR(t *testing.T) {
	cfg := config.Default()
	cfg.Clients = []config.Client{{Name: "iot", Match: []string{"10.0.5.0/24", "192.168.1.7"}}}
	res := buildResolver(t, cfg, nil)

	if p := res.profileFor(netip.MustParseAddr("10.0.5.99")); p == nil || p.Name != "iot" {
		t.Error("a CIDR match was expected")
	}
	if p := res.profileFor(netip.MustParseAddr("192.168.1.7")); p == nil {
		t.Error("a single address should match")
	}
	if p := res.profileFor(netip.MustParseAddr("192.168.1.8")); p != nil {
		t.Error("a neighbouring address must not match")
	}
}

func TestClientFilteringDisabledWins(t *testing.T) {
	off := false
	cfg := config.Default()
	cfg.Clients = []config.Client{{Name: "frei", Match: []string{"10.0.0.1"}, Filtering: &off}}
	res := buildResolver(t, cfg, []string{"||tracker.example^"})

	if d, _ := res.decide("tracker.example", nil, time.Now()); !d.Blocked() {
		t.Error("without a profile it has to be blocked globally")
	}
	profile := res.profileFor(netip.MustParseAddr("10.0.0.1"))
	if d, _ := res.decide("tracker.example", profile, time.Now()); d.Blocked() {
		t.Error("a profile with filtering:false must not block anything")
	}
}

func TestClientOverlayBeatsGlobalList(t *testing.T) {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name: "arbeit", Match: []string{"10.0.0.2"}, AllowRules: []string{"||tracker.example^"},
	}}
	res := buildResolver(t, cfg, []string{"||tracker.example^"})

	profile := res.profileFor(netip.MustParseAddr("10.0.0.2"))
	if d, _ := res.decide("tracker.example", profile, time.Now()); d.Blocked() {
		t.Error("a client exception has to beat the global blocklist")
	}
}

func TestBlockResponseModes(t *testing.T) {
	req := new(dns.Msg)
	req.SetQuestion("tracker.example.", dns.TypeA)
	q := req.Question[0]

	t.Run("nxdomain returns an SOA", func(t *testing.T) {
		cfg := config.Default()
		cfg.Filter.BlockMode = "nxdomain"
		reply := buildResolver(t, cfg, nil).blockResponse(req, q)
		if reply.Rcode != dns.RcodeNameError {
			t.Errorf("rcode = %v, expected NXDOMAIN", dns.RcodeToString[reply.Rcode])
		}
		if len(reply.Ns) != 1 {
			t.Error("the SOA in the authority section is missing, or clients will not cache negatively")
		}
	})

	t.Run("zeroip returns 0.0.0.0", func(t *testing.T) {
		cfg := config.Default()
		cfg.Filter.BlockMode = "zeroip"
		reply := buildResolver(t, cfg, nil).blockResponse(req, q)
		if len(reply.Answer) != 1 {
			t.Fatal("an A record was expected")
		}
		if got := reply.Answer[0].(*dns.A).A.String(); got != "0.0.0.0" {
			t.Errorf("A = %s, expected 0.0.0.0", got)
		}
	})

	t.Run("mx under zeroip returns NODATA", func(t *testing.T) {
		cfg := config.Default()
		cfg.Filter.BlockMode = "zeroip"
		mxReq := new(dns.Msg)
		mxReq.SetQuestion("tracker.example.", dns.TypeMX)
		reply := buildResolver(t, cfg, nil).blockResponse(mxReq, mxReq.Question[0])
		if len(reply.Answer) != 0 || reply.Rcode != dns.RcodeSuccess {
			t.Error("no address may be invented for MX")
		}
	})

	t.Run("refused", func(t *testing.T) {
		cfg := config.Default()
		cfg.Filter.BlockMode = "refused"
		if reply := buildResolver(t, cfg, nil).blockResponse(req, q); reply.Rcode != dns.RcodeRefused {
			t.Errorf("rcode = %v, expected REFUSED", dns.RcodeToString[reply.Rcode])
		}
	})
}

func TestRewriteSplitHorizon(t *testing.T) {
	cfg := config.Default()
	cfg.Rewrites = []config.Rewrite{{Domain: "*.home.example", A: "192.168.1.10"}}
	res := buildResolver(t, cfg, nil)

	rw, ok := res.rewrites.lookup("nas.home.example")
	if !ok || rw.A != "192.168.1.10" {
		t.Fatal("a wildcard rewrite should apply")
	}
	if _, ok := res.rewrites.lookup("fremd.example"); ok {
		t.Error("a foreign domain must not be rewritten")
	}

	req := new(dns.Msg)
	req.SetQuestion("nas.home.example.", dns.TypeA)
	reply := res.rewriteResponse(req, req.Question[0], rw)
	if len(reply.Answer) != 1 || reply.Answer[0].(*dns.A).A.String() != "192.168.1.10" {
		t.Error("the rewrite answer is wrong")
	}
}

func TestExplainNamesTheRule(t *testing.T) {
	cfg := config.Default()
	res := buildResolver(t, cfg, []string{"||tracker.example^"})

	exp := res.Explain("sub.tracker.example", "")
	if !exp.Blocked {
		t.Fatal("should be blocked")
	}
	if exp.Rule != "||tracker.example^" || exp.List != "test" {
		t.Errorf("origin = %q from %q, expected ||tracker.example^ from test", exp.Rule, exp.List)
	}
	if exp := res.Explain("harmlos.example", ""); exp.Blocked {
		t.Error("a harmless domain must not be blocked")
	}
}

func TestServicesBecomeBlockRules(t *testing.T) {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name:          "kinder-tablet",
		Match:         []string{"192.168.1.50"},
		BlockServices: []string{"tiktok"},
	}}
	res := buildResolver(t, cfg, nil)
	profile := res.profileFor(netip.MustParseAddr("192.168.1.50"))

	if d, _ := res.decide("www.tiktok.com", profile, time.Now()); !d.Blocked() {
		t.Error("TikTok should be blocked for this profile")
	}
	if d, _ := res.decide("harmlos.example", profile, time.Now()); d.Blocked() {
		t.Error("other domains must not be affected by it")
	}
	// For other devices the block does not apply.
	if d, _ := res.decide("www.tiktok.com", nil, time.Now()); d.Blocked() {
		t.Error("without a profile the service must not be blocked")
	}
}

func TestServicesInsideTheTimeWindow(t *testing.T) {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name:  "kinder-tablet",
		Match: []string{"192.168.1.50"},
		Schedules: []config.Schedule{{
			Name: "nachtruhe", Days: []string{"all"}, From: "21:00", To: "07:00",
			BlockServices: []string{"youtube"},
		}},
	}}
	res := buildResolver(t, cfg, nil)
	profile := res.profileFor(netip.MustParseAddr("192.168.1.50"))

	nachts := time.Date(2026, 8, 22, 23, 0, 0, 0, time.Local)
	tags := time.Date(2026, 8, 22, 15, 0, 0, 0, time.Local)

	if d, sched := res.decide("www.youtube.com", profile, nachts); !d.Blocked() || sched != "nachtruhe" {
		t.Errorf("at night YouTube should be blocked (blocked=%v, window=%q)", d.Blocked(), sched)
	}
	if d, _ := res.decide("www.youtube.com", profile, tags); d.Blocked() {
		t.Error("during the day YouTube must not be blocked")
	}
}

func TestAnUnknownServiceIsRejectedAtStartup(t *testing.T) {
	cfg := config.Default()
	cfg.Clients = []config.Client{{Name: "x", Match: []string{"10.0.0.1"}, BlockServices: []string{"tikttok"}}}

	if err := cfg.Validate(); err == nil {
		t.Error("a typo in a service name has to show at startup")
	}
}

// The self-check has to manage without an upstream: against a slow upstream
// it would otherwise time out and trigger a restart even though the resolver
// is fine.
func TestSelfCheckNeedsNoUpstream(t *testing.T) {
	// The pool is empty - any real query would fail here.
	res := buildResolver(t, config.Default(), []string{"||tracker.example^"})

	done := make(chan error, 1)
	go func() { done <- res.SelfCheck() }()

	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("SelfCheck meldete %v", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("the self-check is hanging")
	}
}

func TestSelfCheckReportsAMissingRuleSet(t *testing.T) {
	res := buildResolver(t, config.Default(), nil)
	res.SetEngine(nil)

	if err := res.SelfCheck(); err == nil {
		t.Error("without a rule set the self-check has to fail")
	}
}
