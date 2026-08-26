package resolver

import (
	"net"
	"sync"
	"testing"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/config"
	"auspex/internal/rules"
)

func safeSearchCfg(providers ...string) config.Config {
	cfg := config.Default()
	cfg.Clients = []config.Client{{
		Name:       "kids",
		Match:      []string{"10.0.5.0/24"},
		SafeSearch: providers,
	}}
	return cfg
}

func ask(t *testing.T, res *Resolver, client, name string, qtype uint16) *dns.Msg {
	t.Helper()
	req := new(dns.Msg)
	req.SetQuestion(dns.Fqdn(name), qtype)
	w := &fakeWriter{remote: &net.UDPAddr{IP: net.ParseIP(client), Port: 40000}}
	res.ServeDNS(w, req)
	return w.msg
}

// The whole point of the feature, and the part that is easy to get wrong:
// the client must receive an address, not just a CNAME. A stub resolver does
// not chase the chain itself — a CNAME-only answer means the search page does
// not load at all.
func TestSafeSearchAnswersWithTheTargetsAddress(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, safeSearchCfg("google"), up)

	msg := ask(t, res, "10.0.5.7", "www.google.com", dns.TypeA)

	if len(msg.Answer) != 2 {
		t.Fatalf("%d answer records, expected CNAME + A: %v", len(msg.Answer), msg.Answer)
	}
	cname, ok := msg.Answer[0].(*dns.CNAME)
	if !ok || cname.Target != "forcesafesearch.google.com." {
		t.Fatalf("first record = %v, expected the CNAME to the filtered host", msg.Answer[0])
	}
	if _, ok := msg.Answer[1].(*dns.A); !ok {
		t.Fatalf("second record = %v, expected an A record", msg.Answer[1])
	}

	// And the query that went out has to be the one for the target. Asking
	// upstream for the original name would resolve exactly the host the
	// device is supposed to be kept away from.
	if got := up.Last().Question[0].Name; got != "forcesafesearch.google.com." {
		t.Errorf("upstream was asked for %q", got)
	}
}

// The redirect is a profile matter. A device that is not in the profile must
// see the unchanged answer — otherwise the setting would be global again,
// which is the thing the feature exists not to be.
func TestSafeSearchAppliesOnlyToTheProfile(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, safeSearchCfg("google"), up)

	msg := ask(t, res, "10.0.9.7", "www.google.com", dns.TypeA)

	for _, rr := range msg.Answer {
		if _, ok := rr.(*dns.CNAME); ok {
			t.Fatalf("a device outside the profile was redirected: %v", rr)
		}
	}
	if got := up.Last().Question[0].Name; got != "www.google.com." {
		t.Errorf("upstream was asked for %q, expected the original name", got)
	}
}

// Order matters and is not obvious: whoever blocked YouTube outright meant
// blocked, not "moderately filtered". The filter therefore runs first.
func TestABlockBeatsTheRedirect(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, safeSearchCfg("youtube"), up)
	res.SetEngine(rules.NewFromRules("test", []string{"||youtube.com^"}, nil))

	msg := ask(t, res, "10.0.5.7", "www.youtube.com", dns.TypeA)

	if msg.Rcode != dns.RcodeNameError {
		t.Fatalf("rcode = %s, expected NXDOMAIN", dns.RcodeToString[msg.Rcode])
	}
	if entry := res.QueryLog().Recent(1)[0]; entry.Source != "filter" {
		t.Errorf("source = %q, expected filter", entry.Source)
	}
}

// HTTPS records (type 65) carry Encrypted Client Hello and alternative
// addresses. Left alone, a browser could use them to reach the host whose A
// record was just redirected.
func TestTheHTTPSRecordIsRedirectedToo(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, safeSearchCfg("google"), up)

	ask(t, res, "10.0.5.7", "www.google.com", dns.TypeHTTPS)

	if got := up.Last().Question[0].Name; got != "forcesafesearch.google.com." {
		t.Errorf("upstream was asked for %q", got)
	}
}

// And the other direction: an MX query for google.com has nothing to do with
// the search page. Answering it from the filtered host would answer a
// question nobody asked, wrongly.
func TestOtherRecordTypesArePassedThrough(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, safeSearchCfg("google"), up)

	ask(t, res, "10.0.5.7", "google.com", dns.TypeMX)

	if got := up.Last().Question[0].Name; got != "google.com." {
		t.Errorf("upstream was asked for %q, expected the original name", got)
	}
}

// The query log has to say where the query went. A search that suddenly
// returns different results and no trace of why would be exactly the kind of
// unexplainable behaviour the log exists to prevent.
func TestTheRedirectIsVisibleInTheQueryLog(t *testing.T) {
	res := resolverWithUpstream(t, safeSearchCfg("duckduckgo"), &fakeUpstream{})

	ask(t, res, "10.0.5.7", "duckduckgo.com", dns.TypeA)

	entry := res.QueryLog().Recent(1)[0]
	if entry.Source != "safesearch" {
		t.Errorf("source = %q, expected safesearch", entry.Source)
	}
	if entry.Action != "rewritten" {
		t.Errorf("action = %q, expected rewritten", entry.Action)
	}
	if entry.Cname != "safe.duckduckgo.com" {
		t.Errorf("Cname = %q", entry.Cname)
	}
	if entry.Profile != "kids" {
		t.Errorf("profile = %q", entry.Profile)
	}
}

// A filter that quietly stops filtering when something goes wrong is worse
// than one that visibly fails: nobody notices the first kind.
func TestAnUnreachableTargetFailsRatherThanFallingBack(t *testing.T) {
	up := &fakeUpstream{response: func(*dns.Msg) *dns.Msg { return nil }}
	res := resolverWithUpstream(t, safeSearchCfg("google"), up)

	msg := ask(t, res, "10.0.5.7", "www.google.com", dns.TypeA)

	if msg.Rcode != dns.RcodeServerFailure {
		t.Fatalf("rcode = %s, expected SERVFAIL", dns.RcodeToString[msg.Rcode])
	}
}

// ---------------------------------------------------------------------------
// Time windows
// ---------------------------------------------------------------------------
//
// Tested against the profile rather than through ServeDNS: the pipeline reads
// the wall clock, and a test that only passes between four and six in the
// afternoon is not a test.

func window(t *testing.T, name, from, to string, providers ...string) Schedule {
	t.Helper()
	sched, err := compileSchedule("kids", config.Schedule{
		Name: name, Days: []string{"all"}, From: from, To: to, SafeSearch: providers,
	})
	if err != nil {
		t.Fatal(err)
	}
	return sched
}

func windowProfile(t *testing.T, always []string, providers ...string) *Profile {
	t.Helper()
	// Deliberately with spare capacity. That is what a slice decoded from
	// YAML or JSON looks like, and it is the shape in which appending to it
	// writes somewhere it should not.
	base := make([]string, 0, 8)
	base = append(base, always...)
	return &Profile{
		Name: "kids", Filtering: true, SafeSearch: base,
		Schedules: []Schedule{window(t, "homework", "16:00", "18:00", providers...)},
	}
}

func TestASchedulesProvidersApplyOnlyInsideTheWindow(t *testing.T) {
	p := windowProfile(t, nil, "youtube-strict")

	inside := time.Date(2026, 8, 26, 17, 0, 0, 0, time.UTC)
	if got := p.safeSearchTarget("www.youtube.com", inside); got != "restrict.youtube.com" {
		t.Errorf("inside the window = %q, expected restrict.youtube.com", got)
	}

	outside := time.Date(2026, 8, 26, 20, 0, 0, 0, time.UTC)
	if got := p.safeSearchTarget("www.youtube.com", outside); got != "" {
		t.Errorf("outside the window = %q, expected nothing", got)
	}
}

// A window adds to the profile, it does not replace it. The other reading is
// wrong in a way that matters: a window called "homework" that switches
// Google's filter off between four and six is not what anybody who wrote it
// down meant.
func TestTheWindowAddsToTheProfileRatherThanReplacingIt(t *testing.T) {
	p := windowProfile(t, []string{"google"}, "youtube-strict")
	inside := time.Date(2026, 8, 26, 17, 0, 0, 0, time.UTC)

	if got := p.safeSearchTarget("www.google.com", inside); got != "forcesafesearch.google.com" {
		t.Errorf("google inside the window = %q, expected it still to apply", got)
	}
	if got := p.safeSearchTarget("www.youtube.com", inside); got != "restrict.youtube.com" {
		t.Errorf("youtube inside the window = %q", got)
	}
}

// The bug this guards against is a data race, and therefore the kind that
// passes every run until it does not. A profile's provider slice has spare
// capacity — that is what a decoded configuration looks like — and appending
// a window's providers to it writes into that spare room. Two queries from
// the same device arriving at once then write the same slot, and one of them
// reads the other's list.
//
// Watching the results would not catch it: every writer writes the same
// values, so the outcome is correct even while the race is happening. So the
// test watches the memory instead. A sentinel in the spare capacity can only
// be overwritten by an append onto the profile's own slice, which makes the
// check deterministic and independent of scheduling. The concurrency is
// there for the second detector — `go test -race`, which CI runs.
func TestQueriesDoNotWriteIntoTheProfile(t *testing.T) {
	base := make([]string, 0, 8)
	base = append(base, "google")
	spare := base[:cap(base)]
	for i := len(base); i < len(spare); i++ {
		spare[i] = "sentinel"
	}

	p := &Profile{
		Name: "kids", Filtering: true, SafeSearch: base,
		Schedules: []Schedule{
			window(t, "homework", "16:00", "18:00", "youtube-strict"),
			window(t, "evening", "16:00", "22:00", "bing"),
		},
	}
	inside := time.Date(2026, 8, 26, 17, 0, 0, 0, time.UTC)

	var wg sync.WaitGroup
	bad := make(chan string, 3*64)
	for i := 0; i < 64; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for _, c := range []struct{ name, want string }{
				{"www.google.com", "forcesafesearch.google.com"},
				{"www.youtube.com", "restrict.youtube.com"},
				{"www.bing.com", "strict.bing.com"},
			} {
				if got := p.safeSearchTarget(c.name, inside); got != c.want {
					bad <- c.name + " = " + got
				}
			}
		}()
	}
	wg.Wait()
	close(bad)
	for msg := range bad {
		t.Error(msg)
	}

	for i := 1; i < len(spare); i++ {
		if spare[i] != "sentinel" {
			t.Fatalf("the profile's own slice was written to at %d: %q", i, spare[i])
		}
	}
	if len(p.SafeSearch) != 1 || p.SafeSearch[0] != "google" {
		t.Errorf("the profile was modified: %v", p.SafeSearch)
	}
}

// A typo has to fail at startup. Otherwise somebody reads "safe_search:
// [youtube_strict]" in their configuration and believes the tablet is
// filtered.
func TestAMisspeltProviderIsRefusedAtStartup(t *testing.T) {
	cfg := config.Default()
	client := config.Client{Name: "kids", SafeSearch: []string{"youtube_strict"}}
	if err := cfg.ValidateClient(client); err == nil {
		t.Fatal("the misspelt provider was accepted")
	}

	client = config.Client{Name: "kids", Schedules: []config.Schedule{
		{Name: "homework", SafeSearch: []string{"gogle"}},
	}}
	if err := cfg.ValidateClient(client); err == nil {
		t.Fatal("the misspelt provider in the schedule was accepted")
	}
}
