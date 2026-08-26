package names

import (
	"net/netip"
	"testing"
)

func TestStaticMapping(t *testing.T) {
	r, err := New(Options{Static: map[string]string{
		"192.168.1.43": "Arbeitsrechner",
		"10.0.5.0/24":  "IoT network",
	}})
	if err != nil {
		t.Fatal(err)
	}

	cases := []struct{ addr, want string }{
		{"192.168.1.43", "Arbeitsrechner"},
		{"10.0.5.99", "IoT network"},
		{"192.168.1.44", ""},
	}
	for _, c := range cases {
		if got := r.Name(netip.MustParseAddr(c.addr)); got != c.want {
			t.Errorf("Name(%s) = %q, expected %q", c.addr, got, c.want)
		}
	}
}

// Without the reverse lookup enabled, Name() must never go to the network -
// the DNS path depends on it.
func TestWithoutResolveThereIsNoLookup(t *testing.T) {
	r, err := New(Options{Resolve: false, Via: "192.168.1.1"})
	if err != nil {
		t.Fatal(err)
	}
	if got := r.Name(netip.MustParseAddr("192.168.1.43")); got != "" {
		t.Errorf("name = %q, expected empty", got)
	}
}

func TestAnInvalidStaticAddressIsRejected(t *testing.T) {
	if _, err := New(Options{Static: map[string]string{"no-host": "X"}}); err == nil {
		t.Error("an invalid address has to show at startup, not in production")
	}
}

func TestTidyCutsOffTheRouterDomain(t *testing.T) {
	cases := map[string]string{
		"Handy-Sarah.fritz.box.": "Handy-Sarah",
		"nas.local.":             "nas",
		"einzeln.":               "einzeln",
		"":                       "",
	}
	for in, want := range cases {
		if got := tidy(in); got != want {
			t.Errorf("tidy(%q) = %q, expected %q", in, got, want)
		}
	}
}

func TestNameIsRobustAgainstAnInvalidAddress(t *testing.T) {
	r, _ := New(Options{})
	if got := r.Name(netip.Addr{}); got != "" {
		t.Errorf("an invalid address returned %q", got)
	}
	var nilResolver *Resolver
	if got := nilResolver.Name(netip.MustParseAddr("1.2.3.4")); got != "" {
		t.Errorf("nil-Resolver lieferte %q", got)
	}
}
