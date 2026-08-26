package names

import (
	"net/netip"
	"testing"
)

// The case this exists for: a device reached over a tunnel arrives with an
// address the router has never seen. The router will never learn it, so
// waiting does not help — somebody else has to be asked.
func TestATunnelRangeGoesToItsOwnServer(t *testing.T) {
	r, err := New(Options{
		Resolve:    true,
		Via:        "192.168.1.1",
		ReverseVia: map[string]string{"100.64.0.0/10": "100.100.100.100"},
	})
	if err != nil {
		t.Fatal(err)
	}

	cases := []struct{ addr, want string }{
		{"100.64.0.5", "100.100.100.100:53"},
		{"100.127.255.255", "100.100.100.100:53"},
		// Outside the range: the ordinary route, which is the router.
		{"100.63.255.255", "192.168.1.1:53"},
		{"100.128.0.1", "192.168.1.1:53"},
		{"192.168.1.44", "192.168.1.1:53"},
	}
	for _, c := range cases {
		got := r.serverFor(netip.MustParseAddr(c.addr))
		if got != c.want {
			t.Errorf("%s -> %s, expected %s", c.addr, got, c.want)
		}
	}
}

// The narrowest rule decides, or a wide range could never have an exception
// carved out of it.
func TestTheNarrowestRangeWins(t *testing.T) {
	r, err := New(Options{
		Resolve: true,
		Via:     "192.168.1.1",
		ReverseVia: map[string]string{
			"10.0.0.0/8":     "10.0.0.1",
			"10.5.0.0/16":    "10.5.0.1",
			"10.5.9.0/24":    "10.5.9.1",
			"fd00::/8":       "fd00::1",
			"fd71:7881::/32": "fd71:7881::1",
		},
	})
	if err != nil {
		t.Fatal(err)
	}

	cases := []struct{ addr, want string }{
		{"10.1.2.3", "10.0.0.1:53"},
		{"10.5.1.2", "10.5.0.1:53"},
		{"10.5.9.7", "10.5.9.1:53"},
		{"fd00::9", "[fd00::1]:53"},
		{"fd71:7881::9", "[fd71:7881::1]:53"},
	}
	for _, c := range cases {
		got := r.serverFor(netip.MustParseAddr(c.addr))
		if got != c.want {
			t.Errorf("%s -> %s, expected %s", c.addr, got, c.want)
		}
	}
}

// A port stays as written; without one, 53 is added — on both the ordinary
// route and the per-range ones.
func TestAPortIsKeptAndOtherwiseAdded(t *testing.T) {
	r, err := New(Options{
		Resolve: true,
		Via:     "192.168.1.1:5353",
		ReverseVia: map[string]string{
			"100.64.0.0/10": "100.100.100.100:5300",
			"10.0.0.0/8":    "10.0.0.1",
		},
	})
	if err != nil {
		t.Fatal(err)
	}

	for _, c := range []struct{ addr, want string }{
		{"192.168.1.9", "192.168.1.1:5353"},
		{"100.64.0.1", "100.100.100.100:5300"},
		{"10.1.1.1", "10.0.0.1:53"},
	} {
		if got := r.serverFor(netip.MustParseAddr(c.addr)); got != c.want {
			t.Errorf("%s -> %s, expected %s", c.addr, got, c.want)
		}
	}
}

// A range without an ordinary route has to work: somebody who only wants
// tunnel names should not have to name a router as well.
func TestARangeAloneIsEnough(t *testing.T) {
	r, err := New(Options{
		Resolve:    true,
		ReverseVia: map[string]string{"100.64.0.0/10": "100.100.100.100"},
	})
	if err != nil {
		t.Fatal(err)
	}
	if !r.resolve {
		t.Fatal("reverse lookup was switched off although a range was configured")
	}
	if got := r.serverFor(netip.MustParseAddr("100.64.0.1")); got != "100.100.100.100:53" {
		t.Errorf("in the range -> %s", got)
	}
	// And outside it: nobody to ask. Empty, so no query goes out at all —
	// asking the router about a tunnel address only buys a timeout per query.
	if got := r.serverFor(netip.MustParseAddr("192.168.1.9")); got != "" {
		t.Errorf("outside the range -> %q, expected nothing", got)
	}
}

// A typo has to fail at startup. Otherwise the range is silently not applied
// and the devices stay nameless with no explanation.
func TestABrokenRangeIsRefused(t *testing.T) {
	if _, err := New(Options{
		Resolve:    true,
		Via:        "192.168.1.1",
		ReverseVia: map[string]string{"100.64.0.0": "100.100.100.100"},
	}); err == nil {
		t.Error("a range without a prefix length was accepted")
	}

	if _, err := New(Options{
		Resolve:    true,
		Via:        "192.168.1.1",
		ReverseVia: map[string]string{"100.64.0.0/10": "  "},
	}); err == nil {
		t.Error("a range without a server was accepted")
	}
}

// Without any configuration nothing changes — the router keeps answering
// everything, as before.
func TestWithoutRangesEverythingGoesToTheRouter(t *testing.T) {
	r, err := New(Options{Resolve: true, Via: "192.168.1.1"})
	if err != nil {
		t.Fatal(err)
	}
	for _, addr := range []string{"192.168.1.9", "100.64.0.1", "fd00::1"} {
		if got := r.serverFor(netip.MustParseAddr(addr)); got != "192.168.1.1:53" {
			t.Errorf("%s -> %s", addr, got)
		}
	}
}
