package upstream

import (
	"bytes"
	"context"
	"errors"
	"log/slog"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/miekg/dns"
)

type stubUpstream struct {
	addr string
	err  error
	// In a race the pool calls every target at once - that is its purpose. An
	// ordinary counter would be a data race in the test here, not in
	// production code; under "go test -race" that is exactly what shows up.
	hits  atomic.Int64
	delay time.Duration
}

func (s *stubUpstream) Addr() string  { return s.addr }
func (s *stubUpstream) Proto() string { return "stub" }
func (s *stubUpstream) Exchange(ctx context.Context, msg *dns.Msg) (*dns.Msg, error) {
	s.hits.Add(1)
	if s.err != nil {
		return nil, s.err
	}
	if s.delay > 0 {
		select {
		case <-time.After(s.delay):
		case <-ctx.Done():
			return nil, ctx.Err()
		}
	}
	reply := new(dns.Msg)
	reply.SetReply(msg)
	return reply, nil
}

func question() *dns.Msg {
	m := new(dns.Msg)
	m.SetQuestion("example.com.", dns.TypeA)
	return m
}

func TestFailoverTakesTheNextOne(t *testing.T) {
	broken := &stubUpstream{addr: "broken", err: errors.New("unreachable")}
	good := &stubUpstream{addr: "good"}
	pool := NewPool([]Upstream{broken, good}, PoolOptions{})

	resp, via, err := pool.Exchange(context.Background(), question())
	if err != nil || resp == nil {
		t.Fatalf("no answer: %v", err)
	}
	if via != "good" {
		t.Errorf("answered by %q, expected the good one", via)
	}
}

// An outage must not happen silently - otherwise you go looking later for
// why the answers got slower.
func TestAnOutageIsReported(t *testing.T) {
	var buf bytes.Buffer
	broken := &stubUpstream{addr: "broken", err: errors.New("unreachable")}
	good := &stubUpstream{addr: "good"}
	pool := NewPool([]Upstream{broken, good}, PoolOptions{
		Log: slog.New(slog.NewTextHandler(&buf, &slog.HandlerOptions{Level: slog.LevelWarn})),
	})

	pool.Exchange(context.Background(), question())

	if !strings.Contains(buf.String(), "broken") || !strings.Contains(buf.String(), "unreachable") {
		t.Errorf("outage not reported: %q", buf.String())
	}
}

// But only once per run: a permanently dead target must not flood the log.
func TestNoFloodOnAPermanentOutage(t *testing.T) {
	var buf bytes.Buffer
	broken := &stubUpstream{addr: "broken", err: errors.New("gone")}
	good := &stubUpstream{addr: "good"}
	pool := NewPool([]Upstream{broken, good}, PoolOptions{
		FailureThreshold: 3,
		FailureCooldown:  time.Hour,
		Log:              slog.New(slog.NewTextHandler(&buf, &slog.HandlerOptions{Level: slog.LevelWarn})),
	})

	for i := 0; i < 20; i++ {
		pool.Exchange(context.Background(), question())
	}

	// Expected: one report on the first error, one on banishing.
	if got := strings.Count(buf.String(), "broken"); got > 3 {
		t.Errorf("%d messages for 20 failures - that floods the log", got)
	}
	if !strings.Contains(buf.String(), "benched") {
		t.Error("the banishment should be reported")
	}
}

func TestABanishedTargetIsSkipped(t *testing.T) {
	broken := &stubUpstream{addr: "broken", err: errors.New("gone")}
	good := &stubUpstream{addr: "good"}
	pool := NewPool([]Upstream{broken, good}, PoolOptions{
		FailureThreshold: 2, FailureCooldown: time.Hour,
	})

	for i := 0; i < 5; i++ {
		pool.Exchange(context.Background(), question())
	}

	// Once banished the broken target must not be asked again.
	before := broken.hits.Load()
	pool.Exchange(context.Background(), question())
	if broken.hits.Load() != before {
		t.Error("a banished target was asked again")
	}
}

func TestAllDeadReturnsAnError(t *testing.T) {
	pool := NewPool([]Upstream{
		&stubUpstream{addr: "a", err: errors.New("gone")},
		&stubUpstream{addr: "b", err: errors.New("gone as well")},
	}, PoolOptions{})

	if _, _, err := pool.Exchange(context.Background(), question()); err == nil {
		t.Error("with dead upstreams an error has to come back")
	}
}

// Under race the requests go to every target. If only the winner were
// booked it would look as though only one target was asked - and the traffic
// actually produced would be invisible.
func TestRaceBooksEveryTarget(t *testing.T) {
	fast := &stubUpstream{addr: "fast"}
	slow := &stubUpstream{addr: "slow"}
	pool := NewPool([]Upstream{fast, slow}, PoolOptions{Strategy: "race"})

	for i := 0; i < 10; i++ {
		if _, _, err := pool.Exchange(context.Background(), question()); err != nil {
			t.Fatal(err)
		}
	}

	// The losing target books asynchronously: race returns on the first
	// success while the other goroutine is still running. So wait rather
	// than reading immediately - otherwise the test checks the runtime's
	// scheduling instead of the bookkeeping.
	var total, gewinne int64
	for versuch := 0; versuch < 100; versuch++ {
		total, gewinne = 0, 0
		for _, h := range pool.Health() {
			total += h.Queries
			gewinne += h.Wins
		}
		if total == 20 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}

	// Ten queries to two targets: twenty sent, ten used.
	if total != 20 {
		t.Errorf("queries sent = %d, expected 20", total)
	}
	if gewinne != 10 {
		t.Errorf("answers used = %d, expected 10", gewinne)
	}
}

func TestFailoverCountsWinsToo(t *testing.T) {
	good := &stubUpstream{addr: "good"}
	pool := NewPool([]Upstream{good}, PoolOptions{})

	for i := 0; i < 5; i++ {
		pool.Exchange(context.Background(), question())
	}

	h := pool.Health()[0]
	if h.Queries != 5 || h.Wins != 5 {
		t.Errorf("queries=%d wins=%d, expected 5 each", h.Queries, h.Wins)
	}
}

// Under race the slower target loses constantly and gets cancelled by us in
// the process. Counting that as a failed attempt would send it to the bench
// after three rounds - race would decay into failover, and the error count
// would claim an outage that does not exist.
func TestRaceDoesNotPenaliseTheSlowerTarget(t *testing.T) {
	fast := &stubUpstream{addr: "fast"}
	slow := &stubUpstream{addr: "slow", delay: 40 * time.Millisecond}
	pool := NewPool([]Upstream{fast, slow}, PoolOptions{
		Strategy: "race", FailureThreshold: 3, FailureCooldown: time.Hour,
	})

	for i := 0; i < 10; i++ {
		pool.Exchange(context.Background(), question())
	}
	time.Sleep(200 * time.Millisecond)

	for _, h := range pool.Health() {
		if h.Addr != "slow" {
			continue
		}
		if h.Benched {
			t.Error("the slower target must not end up on the bench")
		}
		if h.Errors != 0 {
			t.Errorf("failures = %d, expected 0 - a cancellation is not an outage", h.Errors)
		}
		if h.Queries == 0 {
			t.Error("the slower target should still have been asked")
		}
	}
}
