package resolver

import "testing"

// Recognising local names decides whether a query goes to the router or to
// the internet. Both directions of error are expensive: too narrow, and
// "fritz.box" points at somebody else's server; too wide, and names from the
// internet get sent to a router that does not know them.

func TestPrivateReverseIsRecognised(t *testing.T) {
	cases := []struct {
		name     string
		erwartet bool
		warum    string
	}{
		{"43.1.168.192.in-addr.arpa", true, "192.168.1.43 is private"},
		{"1.0.0.10.in-addr.arpa", true, "10.0.0.1 is private"},
		{"1.0.16.172.in-addr.arpa", true, "172.16.0.1 liegt im privaten Teil"},
		{"1.0.31.172.in-addr.arpa", true, "172.31.0.1 is the upper bound"},
		{"1.168.192.in-addr.arpa", true, "a sub-zone of the home network"},
		{"168.192.in-addr.arpa", true, "groessere Teilzone"},

		// This is exactly where a suffix comparison fails: 172.15 and 172.32
		// look like the private range and are not.
		{"1.0.15.172.in-addr.arpa", false, "172.15.0.1 is public"},
		{"1.0.32.172.in-addr.arpa", false, "172.32.0.1 is public"},
		{"1.1.1.1.in-addr.arpa", false, "1.1.1.1 is public"},
		{"129.84.247.77.in-addr.arpa", false, "a real server address"},

		{"example.com", false, "no reverse name at all"},
		{"in-addr.arpa", false, "die Zone selbst"},
		{"a.b.c.d.in-addr.arpa", false, "no numbers"},
		{"999.1.1.1.in-addr.arpa", false, "not a valid octet"},
		{"1.2.3.4.5.in-addr.arpa", false, "zu viele Stellen"},
	}

	for _, f := range cases {
		if got := isPrivateReverse(f.name); got != f.erwartet {
			t.Errorf("%s: expected %v, got %v (%s)", f.name, f.erwartet, got, f.warum)
		}
	}
}

func TestLocalZonesMatch(t *testing.T) {
	r := &Resolver{localZones: []string{"fritz.box", "heim.lan"}, localReverse: true}

	lokal := []string{
		"fritz.box",
		"fritz.nas",                // no hit via the zone...
		"arbeitsrechner.fritz.box", // ...but this one does
		"heim.lan",
		"drucker.heim.lan",
		"43.1.168.192.in-addr.arpa",
	}
	// "fritz.nas" deliberately falls through: it is not a subdomain of
	// "fritz.box". Whoever needs the name enters it as a zone of its own.
	erwartet := map[string]bool{
		"fritz.box":                 true,
		"fritz.nas":                 false,
		"arbeitsrechner.fritz.box":  true,
		"heim.lan":                  true,
		"drucker.heim.lan":          true,
		"43.1.168.192.in-addr.arpa": true,
	}

	for _, n := range lokal {
		if got := r.isLocalName(n); got != erwartet[n] {
			t.Errorf("%s: expected %v, got %v", n, erwartet[n], got)
		}
	}
}

func TestPublicNamesStayOut(t *testing.T) {
	r := &Resolver{localZones: []string{"fritz.box"}, localReverse: true}

	for _, n := range []string{
		"www.golem.de",
		"analytics.tiktok.com",
		// The nasty case: a public name that merely contains the zone instead
		// of ending on it.
		"fritz.box.example.com",
		"nichtfritz.box",
	} {
		if r.isLocalName(n) {
			t.Errorf("%s was wrongly classified as local", n)
		}
	}
}

func TestReverseCanBeSwitchedOff(t *testing.T) {
	r := &Resolver{localZones: []string{"fritz.box"}, localReverse: false}
	if r.isLocalName("43.1.168.192.in-addr.arpa") {
		t.Error("reverse is switched off but it was still classified as local")
	}
	if !r.isLocalName("fritz.box") {
		t.Error("the zone itself has to keep applying")
	}
}

func TestWithoutZonesNothingIsLocal(t *testing.T) {
	r := &Resolver{localReverse: false}
	if r.isLocalName("fritz.box") {
		t.Error("with no zones configured nothing may count as local")
	}
}
