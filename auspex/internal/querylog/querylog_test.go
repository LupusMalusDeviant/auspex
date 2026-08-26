package querylog

import "testing"

func fill(t *testing.T, size, n int) *Log {
	t.Helper()
	l, err := New(Options{Enabled: true, Size: size})
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < n; i++ {
		l.Add(Entry{Name: "eintrag", Action: "allowed"})
	}
	return l
}

func TestSinceReturnsAscending(t *testing.T) {
	l := fill(t, 10, 5)
	b := l.Since(0, 100)

	if len(b.Entries) != 5 {
		t.Fatalf("entries = %d, expected 5", len(b.Entries))
	}
	for i, e := range b.Entries {
		if e.Seq != int64(i+1) {
			t.Fatalf("wrong order: position %d has seq %d", i, e.Seq)
		}
	}
	if b.Next != 5 {
		t.Errorf("next = %d, expected 5", b.Next)
	}
	if b.Lost != 0 {
		t.Errorf("lost = %d, expected 0", b.Lost)
	}
}

func TestSinceIsIncremental(t *testing.T) {
	l := fill(t, 10, 5)
	first := l.Since(0, 100)

	l.Add(Entry{Name: "neu", Action: "blocked"})
	second := l.Since(first.Next, 100)

	if len(second.Entries) != 1 || second.Entries[0].Name != "neu" {
		t.Fatalf("second fetch = %v, expected only the new entry", second.Entries)
	}
	if second.Next != 6 {
		t.Errorf("next = %d, expected 6", second.Next)
	}
}

func TestSinceRespectsLimit(t *testing.T) {
	l := fill(t, 100, 50)
	b := l.Since(0, 10)

	if len(b.Entries) != 10 {
		t.Fatalf("entries = %d, expected 10 (the limit)", len(b.Entries))
	}
	if b.Next != 10 {
		t.Errorf("next = %d, expected 10 - otherwise the next fetch skips data", b.Next)
	}
}

// The actual reason for the Lost field: when the collector is too slow the
// ring buffer overwrites entries. That has to be noticed rather than
// happening silently.
func TestSinceReportsLostEntries(t *testing.T) {
	l := fill(t, 5, 5)
	b := l.Since(0, 100)
	if b.Lost != 0 {
		t.Fatalf("nothing lost yet, lost = %d", b.Lost)
	}

	// Overwrite the buffer completely without collecting in between.
	for i := 0; i < 7; i++ {
		l.Add(Entry{Name: "flut"})
	}
	after := l.Since(b.Next, 100)

	// Seq 6 and 7 have fallen out, the buffer holds 8..12.
	if after.Lost != 2 {
		t.Errorf("lost = %d, expected 2", after.Lost)
	}
	if len(after.Entries) != 5 || after.Entries[0].Seq != 8 {
		t.Errorf("entries start at seq %d, expected 8", after.Entries[0].Seq)
	}
}

func TestBootIdentifiesRestart(t *testing.T) {
	first := fill(t, 5, 1)
	second := fill(t, 5, 1)

	if first.Since(0, 10).Boot == second.Since(0, 10).Boot {
		t.Error("two instances have to have different boot ids, or the control plane misses a restart")
	}
}

func TestEmptyLog(t *testing.T) {
	l := fill(t, 5, 0)
	b := l.Since(0, 10)

	if len(b.Entries) != 0 || b.Next != 0 || b.Lost != 0 {
		t.Errorf("leerer Log lieferte %+v", b)
	}
}
