package upstream

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"sync"
	"time"

	"github.com/miekg/dns"
)

// Health is an upstream's state, for the control plane.
type Health struct {
	Addr        string    `json:"addr"`
	Proto       string    `json:"proto"`
	Failures    int       `json:"failures"`
	Benched     bool      `json:"benched"`
	BenchedTill time.Time `json:"benched_till,omitempty"`
	// Queries are requests sent. With strategy: race they go to every target
	// at once - then this figure says something about the traffic produced,
	// and Wins about who actually answered.
	Queries   int64   `json:"queries"`
	Wins      int64   `json:"wins"`
	Errors    int64   `json:"errors"`
	AvgMillis float64 `json:"avg_ms"`
}

type tracked struct {
	up Upstream

	mu          sync.Mutex
	failures    int
	benchedTill time.Time
	queries     int64
	wins        int64
	errors      int64
	totalMillis float64
}

// Pool spreads queries across the upstreams and remembers who is currently
// on strike.
type Pool struct {
	targets   []*tracked
	strategy  string
	threshold int
	cooldown  time.Duration
	log       *slog.Logger
}

type PoolOptions struct {
	Strategy         string // "failover" | "race"
	FailureThreshold int
	FailureCooldown  time.Duration
	// Log reports outages. Without it an upstream fails silently until it
	// lands on the bench - and nobody knows why the answers suddenly got
	// slower.
	Log *slog.Logger
}

func NewPool(ups []Upstream, opts PoolOptions) *Pool {
	if opts.Strategy == "" {
		opts.Strategy = "failover"
	}
	if opts.FailureThreshold <= 0 {
		opts.FailureThreshold = 3
	}
	if opts.FailureCooldown <= 0 {
		opts.FailureCooldown = 30 * time.Second
	}
	if opts.Log == nil {
		opts.Log = slog.New(slog.NewTextHandler(io.Discard, nil))
	}
	p := &Pool{
		strategy:  opts.Strategy,
		threshold: opts.FailureThreshold,
		cooldown:  opts.FailureCooldown,
		log:       opts.Log,
	}
	for _, u := range ups {
		p.targets = append(p.targets, &tracked{up: u})
	}
	return p
}

var ErrNoUpstream = errors.New("no upstream available")

// Exchange asks the upstreams and returns the first usable answer together
// with the address that answered.
func (p *Pool) Exchange(ctx context.Context, msg *dns.Msg) (*dns.Msg, string, error) {
	if len(p.targets) == 0 {
		return nil, "", ErrNoUpstream
	}
	if p.strategy == "race" {
		return p.race(ctx, msg)
	}
	return p.failover(ctx, msg)
}

// With failover every success is a win as well - only one target is asked.
func (p *Pool) failover(ctx context.Context, msg *dns.Msg) (*dns.Msg, string, error) {
	var lastErr error
	// The healthy ones first, then as a last resort the banished ones too.
	for _, includeBenched := range []bool{false, true} {
		for _, t := range p.targets {
			if !includeBenched && t.isBenched() {
				continue
			}
			started := time.Now()
			resp, err := t.up.Exchange(ctx, msg)
			if err != nil {
				p.noteFailure(t, err)
				lastErr = err
				continue
			}
			t.recordSuccess(time.Since(started))
			t.recordWin()
			return resp, t.up.Addr(), nil
		}
		if lastErr == nil {
			break
		}
	}
	if lastErr == nil {
		lastErr = ErrNoUpstream
	}
	return nil, "", lastErr
}

type raceResult struct {
	msg     *dns.Msg
	addr    string
	err     error
	elapsed time.Duration
	target  *tracked
}

func (p *Pool) race(ctx context.Context, msg *dns.Msg) (*dns.Msg, string, error) {
	ctx, cancel := context.WithCancel(ctx)
	defer cancel()

	results := make(chan raceResult, len(p.targets))
	started := 0
	for _, t := range p.targets {
		if t.isBenched() {
			continue
		}
		started++
		go func(t *tracked) {
			begin := time.Now()
			resp, err := t.up.Exchange(ctx, msg)
			elapsed := time.Since(begin)

			// Every answer is booked here, not first when it is read: the caller
			// only reads up to the first success, and everything after that
			// would otherwise fall off the table.
			switch {
			case err == nil:
				t.recordSuccess(elapsed)

			case ctx.Err() != nil || errors.Is(err, context.Canceled):
				// We cancelled it ourselves because another one was faster.
				// Counting that as a failed attempt would be plainly wrong: the
				// slower target loses constantly under race by definition and
				// would land on the bench after three rounds - race would decay
				// into failover, and the error count would claim an outage that
				// does not exist.
				t.recordCancelled()

			default:
				p.noteFailure(t, err)
			}
			results <- raceResult{msg: resp, addr: t.up.Addr(), err: err, elapsed: elapsed, target: t}
		}(t)
	}
	if started == 0 {
		return p.failover(ctx, msg) // all banished: try anyway
	}

	var lastErr error
	for i := 0; i < started; i++ {
		r := <-results
		if r.err != nil {
			lastErr = r.err
			continue
		}
		// The goroutine has booked it already; all that counts here is who
		// won.
		r.target.recordWin()
		return r.msg, r.addr, nil
	}
	if lastErr == nil {
		lastErr = ErrNoUpstream
	}
	return nil, "", lastErr
}

// noteFailure books an outage and reports it - but only on the first error
// of a run and when banishing. Otherwise a permanently dead target floods
// the log and makes it useless.
func (p *Pool) noteFailure(t *tracked, err error) {
	failures, benched := t.recordFailure(p.threshold, p.cooldown)
	switch {
	case benched:
		p.log.Warn("upstream benched",
			"target", t.up.Addr(), "error", err, "failures", failures, "duration", p.cooldown)
	case failures == 1:
		p.log.Warn("upstream error, moving on", "target", t.up.Addr(), "error", err)
	}
}

func (t *tracked) isBenched() bool {
	t.mu.Lock()
	defer t.mu.Unlock()
	return time.Now().Before(t.benchedTill)
}

// recordFailure returns the error count and whether this error has just
// banished the target - both for the report.
func (t *tracked) recordFailure(threshold int, cooldown time.Duration) (int, bool) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.failures++
	t.errors++
	t.queries++

	justBenched := false
	if t.failures >= threshold && t.benchedTill.Before(time.Now()) {
		t.benchedTill = time.Now().Add(cooldown)
		justBenched = true
	}
	return t.failures, justBenched
}

// recordCancelled books only the request sent - not a success, but not a
// failed attempt either.
func (t *tracked) recordCancelled() {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.queries++
}

func (t *tracked) recordWin() {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.wins++
}

func (t *tracked) recordSuccess(elapsed time.Duration) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.failures = 0
	t.benchedTill = time.Time{}
	t.queries++
	t.totalMillis += float64(elapsed.Microseconds()) / 1000
}

func (p *Pool) Health() []Health {
	out := make([]Health, 0, len(p.targets))
	now := time.Now()
	for _, t := range p.targets {
		t.mu.Lock()
		h := Health{
			Addr:     t.up.Addr(),
			Proto:    t.up.Proto(),
			Failures: t.failures,
			Queries:  t.queries,
			Wins:     t.wins,
			Errors:   t.errors,
		}
		if now.Before(t.benchedTill) {
			h.Benched, h.BenchedTill = true, t.benchedTill
		}
		if ok := t.queries - t.errors; ok > 0 {
			h.AvgMillis = t.totalMillis / float64(ok)
		}
		t.mu.Unlock()
		out = append(out, h)
	}
	return out
}
