package rules

import "testing"

func TestParseLine(t *testing.T) {
	cases := []struct {
		in      string
		wantOK  bool
		pattern string
		kind    MatchKind
		action  Action
	}{
		{"0.0.0.0 tracker.example", true, "tracker.example", MatchExact, ActionBlock},
		{"127.0.0.1 localhost", false, "", 0, 0},
		{"tracker.example", true, "tracker.example", MatchSuffix, ActionBlock},
		{"||tracker.example^", true, "tracker.example", MatchSuffix, ActionBlock},
		{"@@||shop.example^", true, "shop.example", MatchSuffix, ActionAllow},
		{"*.ads.example", true, "ads.example", MatchSubOnly, ActionBlock},
		{"! Kommentar", false, "", 0, 0},
		{"# Kommentar", false, "", 0, 0},
		{"", false, "", 0, 0},
		{"||example.com^$third-party", false, "", 0, 0}, // modifier: not expressible in DNS
		{"example.com##.banner", false, "", 0, 0},       // Cosmetic-Filter
		{"192.168.1.1", false, "", 0, 0},                // a bare IP is not a domain
		{"localhost", false, "", 0, 0},                  // no dot: not a candidate
	}
	for _, c := range cases {
		rule, ok := ParseLine(c.in)
		if ok != c.wantOK {
			t.Fatalf("ParseLine(%q): ok=%v, expected %v", c.in, ok, c.wantOK)
		}
		if !ok {
			continue
		}
		if rule.Pattern != c.pattern || rule.Kind != c.kind || rule.Action != c.action {
			t.Errorf("ParseLine(%q) = %q/%v/%v, expected %q/%v/%v",
				c.in, rule.Pattern, rule.Kind, rule.Action, c.pattern, c.kind, c.action)
		}
	}
}

func TestMatch(t *testing.T) {
	b := NewBuilder()
	b.AddLines("test", `
||ads.example^
0.0.0.0 exact.example
*.wild.example
@@||ok.ads.example^
naked.example
`, false)
	e := b.Build()

	cases := []struct {
		name   string
		action Action
	}{
		{"ads.example", ActionBlock},          // Suffixregel trifft die Domain selbst
		{"deep.sub.ads.example", ActionBlock}, // and everything below it
		{"ok.ads.example", ActionAllow},       // exception beats block
		{"sub.ok.ads.example", ActionAllow},   // the exception is inherited downwards
		{"exact.example", ActionBlock},        // Hosts-Eintrag
		{"sub.exact.example", ActionNone},     // but exactly, and only exactly
		{"wild.example", ActionNone},          // *.wild does not hit the apex
		{"sub.wild.example", ActionBlock},     // subdomains only
		{"naked.example", ActionBlock},        // nackte Domain: Domain ...
		{"a.b.naked.example", ActionBlock},    // ... samt allem darunter
		{"harmlos.example", ActionNone},
		{"ADS.EXAMPLE", ActionBlock},   // case does not matter
		{"ads.example.", ActionBlock},  // a trailing dot does not matter
		{"notads.example", ActionNone}, // no prefix false positive
	}
	for _, c := range cases {
		if got := e.Match(c.name); got.Action != c.action {
			t.Errorf("Match(%q) = %v, expected %v", c.name, got.Action, c.action)
		}
	}
}

func TestMatchKeepsProvenance(t *testing.T) {
	b := NewBuilder()
	b.AddLines("meineliste", "! Kopf\n||tracker.example^\n", false)
	d := b.Build().Match("sub.tracker.example")

	if d.Rule == nil {
		t.Fatal("no rule returned")
	}
	if d.Rule.List != "meineliste" || d.Rule.Line != 2 {
		t.Errorf("origin = %s:%d, expected mylist:2", d.Rule.List, d.Rule.Line)
	}
}

func TestBuilderCountsConflictsAndDuplicates(t *testing.T) {
	b := NewBuilder()
	b.AddLines("a", "||dup.example^\n||dup.example^\n||konflikt.example^\n", false)
	b.AddLines("b", "@@||konflikt.example^\n", false)
	s := b.Build().Stats()

	if s.Duplicates != 1 {
		t.Errorf("duplicates = %d, expected 1", s.Duplicates)
	}
	if len(s.Conflicts) != 1 || s.Conflicts[0] != "konflikt.example" {
		t.Errorf("conflicts = %v, expected [konflikt.example]", s.Conflicts)
	}
}

// The original line is not stored but reconstructed - at millions of rules
// every extra string held costs dozens of megabytes. The canonical form has
// to carry the same meaning.
func TestTextReconstructsTheRule(t *testing.T) {
	cases := []struct {
		in   string
		want string
	}{
		{"||tracker.example^", "||tracker.example^"},
		{"@@||shop.example^", "@@||shop.example^"},
		{"*.ads.example", "*.ads.example"},
		{"nackt.example", "||nackt.example^"},
		{"0.0.0.0 exakt.example", "0.0.0.0 exakt.example"},
		{"127.0.0.1 exakt.example", "0.0.0.0 exakt.example"}, // vereinheitlicht
	}
	for _, c := range cases {
		rule, ok := ParseLine(c.in)
		if !ok {
			t.Fatalf("ParseLine(%q) failed", c.in)
		}
		got := rule.Text()
		if got != c.want {
			t.Errorf("Text() for %q = %q, expected %q", c.in, got, c.want)
		}
		// The point: the reconstructed form has to produce the same thing again.
		wieder, ok := ParseLine(got)
		if !ok || wieder.Pattern != rule.Pattern || wieder.Kind != rule.Kind || wieder.Action != rule.Action {
			t.Errorf("Text() for %q cannot be translated back: %q", c.in, got)
		}
	}
}
