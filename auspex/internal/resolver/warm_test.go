package resolver

import (
	"context"
	"testing"
	"time"

	"github.com/miekg/dns"

	"auspex/internal/cache"
	"auspex/internal/config"
)

func TestWarmFetchesNamesInAdvance(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, config.Default(), up)

	fetched := res.Warm(context.Background(), []string{"eins.example", "zwei.example"}, 2)

	if fetched != 2 {
		t.Fatalf("fetched = %d, expected 2", fetched)
	}
	// And afterwards they are in the cache.
	q := dns.Question{Name: "eins.example.", Qtype: dns.TypeA, Qclass: dns.ClassINET}
	if msg, _ := res.Cache().Get(cache.Key(q, false)); msg == nil {
		t.Error("the prefetched name is not in the cache")
	}
}

// Warming must not feed the analysis the list came from - otherwise the
// system would be confirming itself.
func TestWarmDoesNotAppearInTheQueryLog(t *testing.T) {
	res := resolverWithUpstream(t, config.Default(), &fakeUpstream{})

	res.Warm(context.Background(), []string{"still.example"}, 1)

	if summary := res.QueryLog().Summary(); summary.Total != 0 {
		t.Errorf("the query log has %d entries, expected 0", summary.Total)
	}
	if res.Stats().Queries != 0 {
		t.Error("warming must not count as a query")
	}
}

// Geblockte Namen vorzuholen waere verschwendeter Verkehr nach oben.
func TestWarmSkipsBlockedNames(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, config.Default(), up)
	res.SetEngine(ruleSet(t, "||tracker.example^"))

	fetched := res.Warm(context.Background(), []string{"tracker.example", "good.example"}, 2)

	if fetched != 1 {
		t.Errorf("fetched = %d, expected 1 (the blocked one is skipped)", fetched)
	}
}

// What is fresh in the cache does not need fetching again.
func TestWarmSkipsFreshEntries(t *testing.T) {
	up := &fakeUpstream{}
	res := resolverWithUpstream(t, config.Default(), up)

	res.Warm(context.Background(), []string{"already.example"}, 1)
	zweiter := res.Warm(context.Background(), []string{"already.example"}, 1)

	if zweiter != 0 {
		t.Errorf("the second pass fetched %d, expected 0", zweiter)
	}
}

func TestWarmStopsWhenCancelled(t *testing.T) {
	res := resolverWithUpstream(t, config.Default(), &fakeUpstream{})

	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	done := make(chan int, 1)
	go func() {
		names := make([]string, 500)
		for i := range names {
			names[i] = "viele.example"
		}
		done <- res.Warm(ctx, names, 4)
	}()

	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("warm does not react to cancellation")
	}
}
