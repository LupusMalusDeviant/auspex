package learn

import (
	"testing"
	"time"
)

func newStore(t *testing.T, g Granularity, max int) *Store {
	t.Helper()
	s, err := Open(t.TempDir(), "iot", g, max)
	if err != nil {
		t.Fatal(err)
	}
	return s
}

func TestDomainGranularityCoversSubdomains(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("cdn-3f8a.maker.example", "A")

	// This is exactly what domain granularity is for: the same service,
	// under a different host name tomorrow.
	if !s.Allows("cdn-91cc.maker.example") {
		t.Error("a different host of the same domain should be allowed")
	}
	if !s.Allows("maker.example") {
		t.Error("the domain itself should be allowed")
	}
	if s.Allows("tracker.fremd.example") {
		t.Error("a foreign domain must not be allowed")
	}
}

func TestExactGranularityIsStrict(t *testing.T) {
	s := newStore(t, GranularityExact, 100)
	s.Record("api.maker.example", "A")

	if !s.Allows("api.maker.example") {
		t.Error("the exact name should be allowed")
	}
	if s.Allows("cdn.maker.example") {
		t.Error("with exact, the neighbouring domain must not slip through")
	}
}

// Without a public suffix list "the last two labels" would release a whole
// TLD here — the mistake that would make enforce mode worthless.
func TestPublicSuffixIsRespected(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("device.maker.co.uk", "A")

	if !s.Allows("other.maker.co.uk") {
		t.Error("the same registrable domain should be allowed")
	}
	if s.Allows("nasty.tracker.co.uk") {
		t.Fatal("co.uk is a public suffix - a foreign domain beneath it has to stay blocked")
	}
}

func TestReverseLookupsAreIgnoredButAllowed(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("1.1.168.192.in-addr.arpa", "PTR")

	if len(s.Entries()) != 0 {
		t.Error("reverse lookups do not belong in the allowlist")
	}
	if !s.Allows("5.1.168.192.in-addr.arpa") {
		t.Error("reverse lookups should get through in enforce mode")
	}
}

func TestMaxEntriesStopsLearning(t *testing.T) {
	s := newStore(t, GranularityDomain, 2)
	s.Record("a.example", "A")
	s.Record("b.example", "A")
	s.Record("c.example", "A")

	if got := len(s.Entries()); got != 2 {
		t.Errorf("entries = %d, expected 2 (the limit)", got)
	}
	if !s.Stats("learn").Overflow {
		t.Error("the overflow flag has to be set, or nobody notices the gap")
	}
	if s.Allows("c.example") {
		t.Error("whatever was not learned must not be allowed")
	}
}

func TestPersistenceRoundTrip(t *testing.T) {
	dir := t.TempDir()
	first, err := Open(dir, "iot", GranularityDomain, 100)
	if err != nil {
		t.Fatal(err)
	}
	first.Record("api.maker.example", "A")
	first.Record("api.maker.example", "AAAA")
	if err := first.Save(); err != nil {
		t.Fatal(err)
	}

	second, err := Open(dir, "iot", GranularityDomain, 100)
	if err != nil {
		t.Fatal(err)
	}
	entries := second.Entries()
	if len(entries) != 1 {
		t.Fatalf("entries after the restart = %d, expected 1", len(entries))
	}
	if entries[0].Count != 2 {
		t.Errorf("counter = %d, expected 2", entries[0].Count)
	}
	if len(entries[0].Types) != 2 {
		t.Errorf("types = %v, expected A and AAAA", entries[0].Types)
	}
	if !second.Allows("cdn.maker.example") {
		t.Error("the domain mapping has to survive a restart")
	}
}

func TestForget(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("api.maker.example", "A")

	if !s.Forget("api.maker.example") {
		t.Fatal("forget should return true")
	}
	if s.Allows("api.maker.example") {
		t.Error("a forgotten name must no longer be allowed")
	}
	if s.Forget("gibtsnicht.example") {
		t.Error("forget on an unknown name should return false")
	}
}

func TestAllowlistFormat(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("api.maker.example", "A")
	s.Record("cdn.maker.example", "A")
	s.Record("zeit.pool.ntp.org", "A")

	domains := s.Allowlist(GranularityDomain)
	if len(domains) != 2 {
		t.Fatalf("allowlist = %v, expected 2 domains", domains)
	}
	if domains[0] != "@@||maker.example^" {
		t.Errorf("rule = %q, expected AdBlock exception syntax", domains[0])
	}
	if exact := s.Allowlist(GranularityExact); len(exact) != 3 {
		t.Errorf("exact allowlist = %v, expected 3 names", exact)
	}
}

func TestImportMergesRatherThanReplaces(t *testing.T) {
	s := newStore(t, GranularityDomain, 100)
	s.Record("api.maker.example", "A")

	earlier := time.Now().Add(-48 * time.Hour)
	taken := s.Import([]Entry{
		{Name: "api.maker.example", Domain: "maker.example", Count: 5,
			First: earlier, Last: earlier, Types: []string{"AAAA"}},
		{Name: "new.maker.example", Domain: "maker.example", Count: 2},
	})

	if taken != 2 {
		t.Fatalf("taken over = %d, expected 2", taken)
	}

	byName := map[string]Entry{}
	for _, e := range s.Entries() {
		byName[e.Name] = e
	}

	// Merged, not replaced: the counter adds up, the earlier first contact
	// wins, types are unioned.
	existing := byName["api.maker.example"]
	if existing.Count != 6 {
		t.Errorf("counter = %d, expected 6", existing.Count)
	}
	if !existing.First.Equal(earlier) {
		t.Error("the earlier first contact should win")
	}
	if len(existing.Types) != 2 {
		t.Errorf("types = %v, expected A and AAAA", existing.Types)
	}
	if _, ok := byName["new.maker.example"]; !ok {
		t.Error("the new name is missing")
	}
}

func TestImportRespectsTheLimit(t *testing.T) {
	s := newStore(t, GranularityDomain, 2)
	entries := make([]Entry, 10)
	for i := range entries {
		entries[i] = Entry{Name: "host" + string(rune('a'+i)) + ".example", Count: 1}
	}

	s.Import(entries)

	if len(s.Entries()) != 2 {
		t.Errorf("entries = %d, expected 2 (the limit)", len(s.Entries()))
	}
	if !s.Stats("learn").Overflow {
		t.Error("the overflow flag has to be set")
	}
}
