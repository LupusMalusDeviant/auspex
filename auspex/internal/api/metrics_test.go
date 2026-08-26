package api

import (
	"strconv"
	"strings"
	"testing"
)

// List names and upstream addresses come from the configuration - a quote
// mark in one of them would break the text format and make the whole scrape
// useless.
func TestLabelsAreDefused(t *testing.T) {
	cases := map[string]string{
		`normal`:         `normal`,
		`mit"Anfuehrung`: `mit\"Anfuehrung`,
		`mit\Schraeg`:    `mit\Schraeg`,
		"mit\nUmbruch":   `mit\nUmbruch`,
	}
	for in, want := range cases {
		if got := escapeLabel(in); got != want {
			t.Errorf("escapeLabel(%q) = %q, expected %q", in, got, want)
		}
	}
}

func TestLineFormat(t *testing.T) {
	var b strings.Builder
	line(&b, "auspex_test", nil, 42)
	if got := b.String(); got != "auspex_test 42\n" {
		t.Errorf("without a label: %q", got)
	}

	b.Reset()
	line(&b, "auspex_test", map[string]string{"a": "1"}, 3.5)
	if got := b.String(); got != "auspex_test{a=\"1\"} 3.5\n" {
		t.Errorf("mit Label: %q", got)
	}
}

func TestCounterWritesHelpAndType(t *testing.T) {
	var b strings.Builder
	counter(&b, "auspex_test_total", "Description.", 7)

	out := b.String()
	for _, expected := range []string{
		"# HELP auspex_test_total Description.",
		"# TYPE auspex_test_total counter",
		"auspex_test_total 7",
	} {
		if !strings.Contains(out, expected) {
			t.Errorf("missing: %q in %q", expected, out)
		}
	}
}

// What matters is not the spelling but that the value can be read back
// intact - Prometheus accepts either notation, but a rounded rule count
// would be wrong information.
func TestNumbersSurviveIntact(t *testing.T) {
	values := []float64{0, 1, 2296816, 12345678901, 59.716, 0.0001}

	for _, want := range values {
		var b strings.Builder
		line(&b, "auspex_test", nil, want)

		field := strings.TrimSpace(strings.TrimPrefix(b.String(), "auspex_test"))
		got, err := strconv.ParseFloat(field, 64)
		if err != nil {
			t.Fatalf("%v produced an unreadable field %q: %v", want, field, err)
		}
		if got != want {
			t.Errorf("%v was written as %q and read back as %v", want, field, got)
		}
	}
}
