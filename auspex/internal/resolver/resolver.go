// Package resolver is the data plane: query in, decision out.
package resolver

import (
	"context"
	"net"
	"net/netip"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/cache"
	"auspex/internal/config"
	"auspex/internal/learn"
	"auspex/internal/names"
	"auspex/internal/neigh"
	"auspex/internal/querylog"
	"auspex/internal/rules"
	"auspex/internal/upstream"
)

type Stats struct {
	Queries    int64 `json:"queries"`
	Blocked    int64 `json:"blocked"`
	Rewritten  int64 `json:"rewritten"`
	CacheHits  int64 `json:"cache_hits"`
	Errors     int64 `json:"errors"`
	Prefetches int64 `json:"prefetches"`
	Learned    int64 `json:"learned"`
	// Validated counts answers whose signature chain the upstream checked.
	Validated int64 `json:"validated"`
	// BlockedCNAME are blocks that only the CNAME chain triggered.
	BlockedCNAME int64 `json:"blocked_cname"`
	// BlockedRebind are answers that pointed a public name inside the network.
	BlockedRebind int64 `json:"blocked_rebind"`
}

type Resolver struct {
	cfg    config.Config
	engine atomic.Pointer[rules.Engine]
	cache  *cache.Cache
	pool   *upstream.Pool
	qlog   *querylog.Log
	// Profiles sit behind a pointer so they can be swapped while running -
	// without that, every change to a device profile would be a restart.
	profiles atomic.Pointer[[]Profile]
	rewrites *rewriteSet
	learn    *learn.Manager
	names    *names.Resolver
	// enforceDNSSEC stops a client switching off upstream validation via the
	// CD bit.
	enforceDNSSEC bool
	checkCNAME    bool
	dohCanary     bool
	// rebindGuard blocks answers that point a public name at an internal
	// address; rebindAllow are the names exempt from it.
	rebindGuard bool
	rebindAllow []string

	// Local zones go to the router instead of outwards. See local.go -
	// without it "fritz.box" points at somebody else's server.
	// neigh resolves addresses to MACs - for profiles bound to a device
	// rather than to an address.
	neigh *neigh.Table

	localZones   []string
	localVia     string
	localReverse bool
	localTimeout time.Duration

	queries       atomic.Int64
	blocked       atomic.Int64
	rewritten     atomic.Int64
	cacheHits     atomic.Int64
	errors        atomic.Int64
	prefetches    atomic.Int64
	learned       atomic.Int64
	validated     atomic.Int64
	blockedCNAME  atomic.Int64
	blockedRebind atomic.Int64

	started time.Time
}

func New(cfg config.Config, engine *rules.Engine, c *cache.Cache, pool *upstream.Pool, qlog *querylog.Log, learnMgr *learn.Manager, hostNames *names.Resolver) (*Resolver, error) {
	r := &Resolver{
		cfg:      cfg,
		cache:    c,
		pool:     pool,
		qlog:     qlog,
		rewrites: compileRewrites(cfg.Rewrites),
		learn:    learnMgr,
		names:    hostNames,

		enforceDNSSEC: cfg.Upstream.DNSSEC == "" || cfg.Upstream.DNSSEC == "enforce",
		checkCNAME:    cfg.Filter.CheckCNAME,
		dohCanary:     cfg.Filter.DoHCanary,
		rebindGuard:   cfg.Filter.RebindProtection,
		rebindAllow:   append(append([]string{}, builtinRebindAllow...), lowerAll(cfg.Filter.RebindAllow)...),
		neigh:         hostNames.Neighbors(),
		localZones:    localZones(cfg),
		localVia:      localRouter(cfg),
		localReverse:  cfg.Local.Reverse,
		localTimeout:  time.Duration(cfg.Local.Timeout),
		started:       time.Now(),
	}
	r.engine.Store(engine)

	if err := r.SetClients(cfg.Clients); err != nil {
		return nil, err
	}
	return r, nil
}

// SetClients recompiles device profiles and swaps them in. If compilation
// fails the previous set stays: one broken profile must not end up meaning
// that none apply at all.
func (r *Resolver) SetClients(clients []config.Client) error {
	profiles, err := compileProfiles(clients, r.cfg.Learning, r.learn)
	if err != nil {
		return err
	}
	r.profiles.Store(&profiles)
	return nil
}

// Warm resolves names ahead of time and puts them in the cache.
//
// Prefetch only renews what is hot anyway — after a restart the cache is
// empty, and the first session pays the full trip upstream for every name.
// The control plane knows from history which names the network asks for
// constantly; those are exactly the ones worth fetching in advance.
//
// Deliberately NOT through ServeDNS: these queries should land neither in
// the query log nor in the learn store. Otherwise the analysis the list came
// from would be feeding itself.
func (r *Resolver) Warm(ctx context.Context, names []string, concurrent int) int {
	if concurrent <= 0 {
		concurrent = 8
	}
	engine := r.engine.Load()

	jobs := make(chan string)
	var fetched atomic.Int64
	var wg sync.WaitGroup

	for i := 0; i < concurrent; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for name := range jobs {
				if r.warmOne(ctx, name) {
					fetched.Add(1)
				}
			}
		}()
	}

	for _, name := range names {
		name = strings.TrimSuffix(strings.ToLower(strings.TrimSpace(name)), ".")
		if name == "" {
			continue
		}
		// Prefetching blocked names would be wasted traffic upstream.
		if engine != nil && engine.Match(name).Blocked() {
			continue
		}
		select {
		case jobs <- name:
		case <-ctx.Done():
			close(jobs)
			wg.Wait()
			return int(fetched.Load())
		}
	}
	close(jobs)
	wg.Wait()

	return int(fetched.Load())
}

func (r *Resolver) warmOne(ctx context.Context, name string) bool {
	msg := new(dns.Msg)
	msg.SetQuestion(dns.Fqdn(name), dns.TypeA)

	key := cache.Key(msg.Question[0], false)
	// Whatever is still fresh in the cache does not need fetching again.
	if cached, meta := r.cache.Get(key); cached != nil && !meta.Stale {
		return false
	}

	ctx, cancel := context.WithTimeout(ctx, r.cfg.Upstream.Timeout.D())
	defer cancel()

	resp, _, err := r.pool.Exchange(ctx, msg)
	if err != nil {
		return false
	}
	if r.cfg.Cache.Enabled {
		r.cache.Set(key, resp)
	}
	return true
}

// SelfCheck touches every part that could block while running: rule set,
// profiles, cache and query log. Each has a lock of its own — if one hangs,
// the whole resolver hangs, and that is exactly what a health check should
// notice.
//
// Deliberately WITHOUT the upstream: a real query would time out against a
// slow upstream and trigger a restart even though the resolver is perfectly
// fine. A restart does not help against a hanging upstream anyway. That the
// listeners are up is already guaranteed by aborting at startup.
func (r *Resolver) SelfCheck() error {
	engine := r.engine.Load()
	if engine == nil {
		return errNoRuleSet
	}
	engine.Match("healthcheck.auspex.invalid")

	r.profileFor(netip.MustParseAddr("127.0.0.1"))

	q := dns.Question{Name: "healthcheck.auspex.invalid.", Qtype: dns.TypeA, Qclass: dns.ClassINET}
	r.cache.Get(cache.Key(q, false))
	r.qlog.Summary()

	return nil
}

var errNoRuleSet = errNoRules{}

type errNoRules struct{}

func (errNoRules) Error() string { return "no rule set loaded" }

// SetEngine swaps the rule set while running (list update, reload).
func (r *Resolver) SetEngine(e *rules.Engine) { r.engine.Store(e) }
func (r *Resolver) Engine() *rules.Engine     { return r.engine.Load() }
func (r *Resolver) Cache() *cache.Cache       { return r.cache }
func (r *Resolver) Pool() *upstream.Pool      { return r.pool }
func (r *Resolver) QueryLog() *querylog.Log   { return r.qlog }

// HostNames returns the known device names, for the control plane.
func (r *Resolver) HostNames() map[string]string {
	if r.names == nil {
		return map[string]string{}
	}
	return r.names.Known()
}
func (r *Resolver) Uptime() time.Duration { return time.Since(r.started) }

func (r *Resolver) Stats() Stats {
	return Stats{
		Queries:       r.queries.Load(),
		Blocked:       r.blocked.Load(),
		Rewritten:     r.rewritten.Load(),
		CacheHits:     r.cacheHits.Load(),
		Errors:        r.errors.Load(),
		Prefetches:    r.prefetches.Load(),
		Learned:       r.learned.Load(),
		Validated:     r.validated.Load(),
		BlockedCNAME:  r.blockedCNAME.Load(),
		BlockedRebind: r.blockedRebind.Load(),
	}
}

// LearnStats sums up the learn stores of every profile.
func (r *Resolver) LearnStats() []learn.Stats {
	profiles := *r.profiles.Load()
	out := make([]learn.Stats, 0, len(profiles))
	for i := range profiles {
		p := &profiles[i]
		if p.Learn == nil {
			continue
		}
		out = append(out, p.Learn.Stats(string(p.Policy)))
	}
	return out
}

// LearnStore returns a profile's learn store.
func (r *Resolver) LearnStore(profile string) (*learn.Store, Policy, bool) {
	profiles := *r.profiles.Load()
	for i := range profiles {
		p := &profiles[i]
		if p.Name == profile && p.Learn != nil {
			return p.Learn, p.Policy, true
		}
	}
	return nil, "", false
}

// QuarantineListName marks blocks that came out of a quarantine.
const QuarantineListName = "quarantine"

// LearnListName marks blocks that came out of learn mode.
const LearnListName = "lernmodus"

// quarantineRule is the synthetic rule behind a quarantine block. Its own
// list name, so the query log separates "quarantined" from "not learned" —
// they mean very different things to whoever reads it.
func quarantineRule(profile string) *rules.Rule {
	return &rules.Rule{
		Pattern: profile,
		Kind:    rules.MatchExact,
		Action:  rules.ActionBlock,
		List:    QuarantineListName,
	}
}

// learnRule is the synthetic rule behind a deny-by-default block.
// Without it the query log would say "blocked" with no reason at all.
func learnRule(profile string) *rules.Rule {
	return &rules.Rule{
		Pattern: profile,
		Kind:    rules.MatchExact,
		Action:  rules.ActionBlock,
		List:    LearnListName,
	}
}

func (r *Resolver) ServeDNS(w dns.ResponseWriter, req *dns.Msg) {
	start := time.Now()
	r.queries.Add(1)

	// Exactly one question per packet. Anything else in the wild is either
	// broken or an attempt at something.
	if len(req.Question) != 1 || req.Opcode != dns.OpcodeQuery {
		reply := new(dns.Msg)
		reply.SetRcode(req, dns.RcodeFormatError)
		w.WriteMsg(reply)
		return
	}

	q := req.Question[0]
	name := strings.TrimSuffix(strings.ToLower(q.Name), ".")
	clientAddr := clientAddrOf(w.RemoteAddr())

	entry := querylog.Entry{
		Time:       start,
		Client:     clientAddr.String(),
		ClientName: r.names.Name(clientAddr),
		Name:       name,
		Domain:     learn.RegistrableDomain(name),
		Type:       dns.TypeToString[q.Qtype],
	}

	profile := r.profileFor(clientAddr)
	if profile != nil {
		entry.Profile = profile.Name
	}

	// 1. Rewrites: internal names point at internal addresses.
	if rw, ok := r.rewrites.lookup(name); ok {
		reply := r.rewriteResponse(req, q, rw)
		entry.Action, entry.Source = "rewritten", "rewrite"
		entry.Rcode = dns.RcodeToString[reply.Rcode]
		entry.Answers = answerStrings(reply)
		r.rewritten.Add(1)
		r.finish(w, req, reply, entry, start)
		return
	}

	// 1b. Local zones: names only the router knows.
	//
	// Before the filter, because a device name in the home network is on no
	// block list and has no business being on one. And before the cache,
	// because these answers change with every DHCP lease.
	if r.localVia != "" && r.isLocalName(name) {
		reply, err := r.askLocal(req)
		if err != nil {
			r.errors.Add(1)
			entry.Action, entry.Source = "error", "lokal"
			entry.Rcode = "SERVFAIL"
			reply := new(dns.Msg)
			reply.SetRcode(req, dns.RcodeServerFailure)
			r.finish(w, req, reply, entry, start)
			return
		}
		// SetReply clears the answer section and the rcode - both have to be
		// carried across the adaptation to the query, otherwise the router's
		// NXDOMAIN turns into a NOERROR with no answer.
		answers, authority, extra := reply.Answer, reply.Ns, reply.Extra
		rcode := reply.Rcode
		reply.SetReply(req)
		reply.Rcode = rcode
		reply.Answer, reply.Ns, reply.Extra = answers, authority, extra

		entry.Action, entry.Source = "allowed", "lokal"
		entry.Rcode = dns.RcodeToString[reply.Rcode]
		entry.Answers = answerStrings(reply)
		r.finish(w, req, reply, entry, start)
		return
	}

	// 2. Canary domain for Firefox's own encrypted resolution.
	// NXDOMAIN is mandatory here, whatever the block mode: this is the only
	// answer Firefox reads as "the network filters, I will leave it alone".
	if r.dohCanary && isCanary(name) {
		reply := new(dns.Msg)
		reply.SetRcode(req, dns.RcodeNameError)
		reply.Authoritative = true

		entry.Action, entry.Source = "blocked", "doh-kanarie"
		entry.Rcode = "NXDOMAIN"
		entry.Rule = "use-application-dns.net"
		entry.List = "doh-kanarie"
		r.blocked.Add(1)
		r.finish(w, req, reply, entry, start)
		return
	}

	// 3. Filter.
	if decision, schedule := r.decide(name, profile, start); decision.Blocked() {
		reply := r.blockResponse(req, q)
		entry.Action, entry.Source = "blocked", "filter"
		entry.Rcode = dns.RcodeToString[reply.Rcode]
		entry.Schedule = schedule
		if decision.Rule != nil {
			entry.Rule = decision.Rule.Text()
			entry.RuleKind = decision.Rule.KindString()
			entry.List = decision.Rule.List
		}
		entry.Answers = answerStrings(reply)
		r.blocked.Add(1)
		r.finish(w, req, reply, entry, start)
		return
	}

	// Learning happens only here: names the filter let through. Otherwise the
	// tracker that happened to be asked for during the learn window ends up
	// in the allowlist permanently.
	if profile != nil && profile.Policy == PolicyLearn && profile.Learn != nil {
		profile.Learn.Record(name, entry.Type)
		r.learned.Add(1)
	}

	// 3b. SafeSearch: the profile sends a search engine to the host its
	// operator serves filtered results from.
	//
	// After the filter, not before it: whoever blocked YouTube outright meant
	// blocked, not "moderately filtered". And before the cache, because the
	// answer depends on who is asking — the children's tablet and the
	// workshop computer must not be served each other's.
	if safeTarget := profile.safeSearchTarget(name, start); safeTarget != "" && safeSearchApplies(q.Qtype) {
		reply, via, err := r.safeSearchResponse(req, q, safeTarget)
		if err != nil {
			// The target did not resolve. SERVFAIL rather than falling back
			// to the unfiltered answer: a filter that quietly stops
			// filtering when something goes wrong is worse than one that
			// visibly fails.
			r.errors.Add(1)
			reply := new(dns.Msg)
			reply.SetRcode(req, dns.RcodeServerFailure)
			entry.Action, entry.Source = "error", "safesearch"
			entry.Rcode, entry.Error = "SERVFAIL", err.Error()
			entry.Cname = safeTarget
			r.finish(w, req, reply, entry, start)
			return
		}
		entry.Action, entry.Source, entry.Upstream = "rewritten", "safesearch", via
		entry.Cname = safeTarget
		entry.Rcode = dns.RcodeToString[reply.Rcode]
		entry.Answers = answerStrings(reply)
		r.rewritten.Add(1)
		r.finish(w, req, reply, entry, start)
		return
	}

	// 4. Cache.
	dnssecOK := false
	if opt := req.IsEdns0(); opt != nil {
		dnssecOK = opt.Do()
	}
	key := cache.Key(q, dnssecOK)

	if r.cfg.Cache.Enabled {
		if cached, meta := r.cache.Get(key); cached != nil {
			answers, authority, extra := cached.Answer, cached.Ns, cached.Extra
			rcode := cached.Rcode
			cached.SetReply(req)
			cached.Rcode = rcode
			cached.Answer, cached.Ns, cached.Extra = answers, authority, extra
			r.cacheHits.Add(1)
			entry.Action = "allowed"
			entry.Rcode = dns.RcodeToString[cached.Rcode]
			entry.Source = "cache"
			if meta.Stale {
				entry.Source = "stale"
			}
			entry.Validated = cached.AuthenticatedData
			if !(req.AuthenticatedData || dnssecOK) {
				cached.AuthenticatedData = false
			}
			// Check on the cache path too: otherwise the first query would be
			// blocked and every one after it would come unfiltered from cache.
			if decision, target := r.cnameBlock(cached, profile, start); decision.Blocked() {
				r.blockDueToCNAME(w, req, q, entry, decision, target, start)
				return
			}
			// On the cache path too: without it the first answer would be
			// blocked and every one after it would come through unchecked.
			if addr, ok := r.rebindBlock(cached, name); ok {
				r.blockDueToRebind(w, req, q, entry, addr, start)
				return
			}
			entry.Answers = answerStrings(cached)
			if meta.Prefetch {
				r.prefetches.Add(1)
				go r.prefetch(key, req)
			}
			r.finish(w, req, cached, entry, start)
			return
		}
	}

	// 5. Upstream.
	ctx, cancel := context.WithTimeout(context.Background(), r.cfg.Upstream.Timeout.D())
	defer cancel()

	// DNSSEC: a client setting CD=1 would switch off validation at the
	// upstream - that is not its call. AD=1 in the query lets us learn
	// whether it was validated without asking for the signatures ourselves
	// (RFC 6840).
	outbound := req
	clientWantsAD := req.AuthenticatedData || dnssecOK
	if r.enforceDNSSEC {
		outbound = req.Copy()
		outbound.CheckingDisabled = false
		outbound.AuthenticatedData = true
	}

	resp, via, err := r.pool.Exchange(ctx, outbound)
	if err != nil {
		r.errors.Add(1)
		reply := new(dns.Msg)
		reply.SetRcode(req, dns.RcodeServerFailure)
		entry.Action, entry.Source = "error", "upstream"
		entry.Rcode, entry.Error = "SERVFAIL", err.Error()
		r.finish(w, req, reply, entry, start)
		return
	}

	// The answer is cached anyway: it is valid, and the check runs again on
	// the next hit. Not caching would mean asking upstream forever for every
	// blocked first-party domain.
	if r.cfg.Cache.Enabled {
		r.cache.Set(key, resp)
	}
	if decision, target := r.cnameBlock(resp, profile, start); decision.Blocked() {
		r.blockDueToCNAME(w, req, q, entry, decision, target, start)
		return
	}
	if addr, ok := r.rebindBlock(resp, name); ok {
		r.blockDueToRebind(w, req, q, entry, addr, start)
		return
	}
	resp.Id = req.Id
	entry.Validated = resp.AuthenticatedData
	// Whoever did not ask for it does not get the AD bit set either.
	if !clientWantsAD {
		resp.AuthenticatedData = false
	}

	entry.Action, entry.Source, entry.Upstream = "allowed", "upstream", via
	entry.Rcode = dns.RcodeToString[resp.Rcode]
	entry.Answers = answerStrings(resp)
	r.finish(w, req, resp, entry, start)
}

// cnameBlock checks an answer's CNAME chain against the same rule set as the
// name that was queried.
//
// This is the counter to the most common trick against DNS filters: a
// first-party subdomain like metrics.newspaper.com points by CNAME at
// newspaper.eulerian.net. Only the target is on the block list — without this
// check the tracker gets through, because the name queried is harmless.
//
// Checked with the same profile as the original query, so client exceptions
// and time windows apply here too.
func (r *Resolver) cnameBlock(msg *dns.Msg, profile *Profile, now time.Time) (rules.Decision, string) {
	if !r.checkCNAME || msg == nil {
		return rules.Decision{}, ""
	}
	for _, rr := range msg.Answer {
		cname, ok := rr.(*dns.CNAME)
		if !ok {
			continue
		}
		target := strings.TrimSuffix(strings.ToLower(cname.Target), ".")
		if target == "" {
			continue
		}
		if decision, _ := r.decide(target, profile, now); decision.Blocked() {
			return decision, target
		}
	}
	return rules.Decision{}, ""
}

// blockDueToCNAME builds the answer and the log entry for a block that only
// the chain triggered.
func (r *Resolver) blockDueToCNAME(
	w dns.ResponseWriter, req *dns.Msg, q dns.Question,
	entry querylog.Entry, decision rules.Decision, target string, start time.Time,
) {
	reply := r.blockResponse(req, q)
	entry.Action, entry.Source = "blocked", "cname"
	entry.Rcode = dns.RcodeToString[reply.Rcode]
	entry.Cname = target
	if decision.Rule != nil {
		entry.Rule = decision.Rule.Text()
		entry.RuleKind = decision.Rule.KindString()
		entry.List = decision.Rule.List
	}
	entry.Answers = answerStrings(reply)
	r.blocked.Add(1)
	r.blockedCNAME.Add(1)
	r.finish(w, req, reply, entry, start)
}

// blockDueToRebind builds the answer and the log entry for a rebinding block.
//
// The offending address goes into the log. Without it the block would be
// inexplicable: the name is on no list, and "blocked" with no reason is the
// kind of entry that gets the whole feature switched off.
func (r *Resolver) blockDueToRebind(
	w dns.ResponseWriter, req *dns.Msg, q dns.Question,
	entry querylog.Entry, addr string, start time.Time,
) {
	reply := r.blockResponse(req, q)
	entry.Action, entry.Source = "blocked", "rebind"
	entry.Rcode = dns.RcodeToString[reply.Rcode]
	entry.Rule = addr
	entry.RuleKind = "rebind"
	entry.List = "rebind-protection"
	entry.Answers = answerStrings(reply)
	r.blocked.Add(1)
	r.blockedRebind.Add(1)
	r.finish(w, req, reply, entry, start)
}

// lowerAll trims and lowercases a list of name suffixes.
func lowerAll(in []string) []string {
	out := make([]string, 0, len(in))
	for _, s := range in {
		if s = strings.ToLower(strings.TrimSpace(s)); s != "" {
			out = append(out, strings.TrimSuffix(s, "."))
		}
	}
	return out
}

// decide combines time windows, client rules and the global rule set.
// The order is specificity: the narrowest rule set first.
func (r *Resolver) decide(name string, profile *Profile, now time.Time) (rules.Decision, string) {
	if profile != nil {
		if !profile.Filtering {
			return rules.Decision{Action: rules.ActionNone}, ""
		}
		// Quarantine comes before everything: it is the strongest statement
		// there is, and it has to hold even against a schedule that would
		// otherwise allow something. Explicit allow rules still lift it —
		// without an escape hatch a quarantined device cannot even reach
		// what it needs to be repaired.
		if profile.Policy == PolicyQuarantine {
			if profile.Overlay == nil || profile.Overlay.Match(name).Action != rules.ActionAllow {
				return rules.Decision{Action: rules.ActionBlock, Rule: quarantineRule(profile.Name)}, ""
			}
			return rules.Decision{Action: rules.ActionNone}, ""
		}
		// Enforce comes next: deny-by-default is the strongest statement in
		// the profile. Explicit allow rules lift it, so a device can still be
		// topped up after learning.
		if profile.Policy == PolicyEnforce && profile.Learn != nil && !profile.Learn.Allows(name) {
			if profile.Overlay == nil || profile.Overlay.Match(name).Action != rules.ActionAllow {
				return rules.Decision{Action: rules.ActionBlock, Rule: learnRule(profile.Name)}, ""
			}
		}
		for _, s := range profile.Schedules {
			if !s.Active(now) {
				continue
			}
			if d := s.Engine.Match(name); d.Action != rules.ActionNone {
				if d.Action == rules.ActionAllow {
					return rules.Decision{Action: rules.ActionNone, Rule: d.Rule}, s.Name
				}
				return d, s.Name
			}
		}
		if profile.Overlay != nil {
			if d := profile.Overlay.Match(name); d.Action != rules.ActionNone {
				if d.Action == rules.ActionAllow {
					return rules.Decision{Action: rules.ActionNone, Rule: d.Rule}, ""
				}
				return d, ""
			}
		}
	}
	d := r.engine.Load().Match(name)
	if d.Action == rules.ActionAllow {
		return rules.Decision{Action: rules.ActionNone, Rule: d.Rule}, ""
	}
	return d, ""
}

// prefetch renews a hot cache entry before it expires.
func (r *Resolver) prefetch(key string, req *dns.Msg) {
	defer r.cache.ClearFetching(key)
	ctx, cancel := context.WithTimeout(context.Background(), r.cfg.Upstream.Timeout.D())
	defer cancel()

	probe := req.Copy()
	probe.Id = dns.Id()
	if resp, _, err := r.pool.Exchange(ctx, probe); err == nil {
		r.cache.Set(key, resp)
	}
}

func (r *Resolver) finish(w dns.ResponseWriter, req, reply *dns.Msg, entry querylog.Entry, start time.Time) {
	if entry.Validated {
		r.validated.Add(1)
	}
	entry.Millis = float64(time.Since(start).Microseconds()) / 1000
	r.qlog.Add(entry)
	writeMsg(w, req, reply)
}

// writeMsg trims the answer to what the client can accept over UDP.
func writeMsg(w dns.ResponseWriter, req, reply *dns.Msg) {
	if _, isUDP := w.RemoteAddr().(*net.UDPAddr); isUDP {
		size := dns.MinMsgSize
		if opt := req.IsEdns0(); opt != nil {
			if s := int(opt.UDPSize()); s > size {
				size = s
			}
		}
		reply.Truncate(size)
	}
	_ = w.WriteMsg(reply)
}

// isCanary recognises the domain Firefox uses to ask whether the network
// filters. Subdomains too: some setups ask below it.
func isCanary(name string) bool {
	const canary = "use-application-dns.net"
	return name == canary || strings.HasSuffix(name, "."+canary)
}

func clientAddrOf(a net.Addr) netip.Addr {
	switch v := a.(type) {
	case *net.UDPAddr:
		if addr, ok := netip.AddrFromSlice(v.IP); ok {
			return addr.Unmap()
		}
	case *net.TCPAddr:
		if addr, ok := netip.AddrFromSlice(v.IP); ok {
			return addr.Unmap()
		}
	}
	host, _, err := net.SplitHostPort(a.String())
	if err != nil {
		return netip.Addr{}
	}
	addr, _ := netip.ParseAddr(host)
	return addr.Unmap()
}

func answerStrings(m *dns.Msg) []string {
	if len(m.Answer) == 0 {
		return nil
	}
	out := make([]string, 0, len(m.Answer))
	for _, rr := range m.Answer {
		switch v := rr.(type) {
		case *dns.A:
			out = append(out, v.A.String())
		case *dns.AAAA:
			out = append(out, v.AAAA.String())
		case *dns.CNAME:
			out = append(out, strings.TrimSuffix(v.Target, "."))
		case *dns.PTR:
			out = append(out, strings.TrimSuffix(v.Ptr, "."))
		case *dns.MX:
			out = append(out, strings.TrimSuffix(v.Mx, "."))
		case *dns.TXT:
			out = append(out, strings.Join(v.Txt, " "))
		}
	}
	return out
}

// localZones normalises the zones from the configuration.
func localZones(cfg config.Config) []string {
	zones := make([]string, 0, len(cfg.Local.Zones))
	for _, z := range cfg.Local.Zones {
		z = strings.TrimSuffix(strings.ToLower(strings.TrimSpace(z)), ".")
		if z != "" {
			zones = append(zones, z)
		}
	}
	return zones
}

// localRouter picks who Auspex asks about local names.
//
// With nothing specified, the same one that already supplies device names:
// whoever set hosts.via means the same router. Two places for the same
// address would be an invitation to let them drift apart.
func localRouter(cfg config.Config) string {
	via := strings.TrimSpace(cfg.Local.Via)
	if via == "" {
		via = strings.TrimSpace(cfg.Hosts.Via)
	}
	if via == "" {
		return ""
	}
	if _, _, err := net.SplitHostPort(via); err != nil {
		via = net.JoinHostPort(via, "53")
	}
	return via
}

// NameOf, MacOf and ProfileNameOf answer the question "who is behind this
// address?" - for the control plane, which on an HTTP request only sees the
// sender's address. The resolver has the answer anyway.
func (r *Resolver) NameOf(addr netip.Addr) string {
	if r == nil || r.names == nil {
		return ""
	}
	return r.names.Name(addr)
}

func (r *Resolver) MacOf(addr netip.Addr) string {
	if r == nil || r.neigh == nil {
		return ""
	}
	return r.neigh.Mac(addr)
}

func (r *Resolver) ProfileNameOf(addr netip.Addr) string {
	if r == nil {
		return ""
	}
	if p := r.profileFor(addr); p != nil {
		return p.Name
	}
	return ""
}

// Forget throws away the cached answers for a name.
// To be called when a rule has changed.
func (r *Resolver) Forget(name string) int {
	if r == nil || r.cache == nil {
		return 0
	}
	return r.cache.Forget(name)
}
