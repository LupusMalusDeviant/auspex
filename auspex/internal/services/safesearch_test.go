package services

import "testing"

// The point of the table: Google runs roughly 190 country domains, and a
// hand-written list of them is out of date the day it is finished. The public
// suffix list already knows where the domain ends.
func TestEveryGoogleCountryDomainIsRedirected(t *testing.T) {
	for _, name := range []string{
		"google.com", "www.google.com", "www.google.de", "google.de",
		"www.google.co.uk", "www.google.com.au", "images.google.fr",
		"ipv6.google.com",
	} {
		if got := SafeSearchTarget(name, []string{"google"}); got != "forcesafesearch.google.com" {
			t.Errorf("%q = %q, expected forcesafesearch.google.com", name, got)
		}
	}
}

// And the part that would break the household if it were wrong: signing in,
// mail and maps run on the same registrable domain and must not be
// redirected at a search host.
func TestOnlyTheSearchEntryPointsAreRedirected(t *testing.T) {
	for _, name := range []string{
		"accounts.google.com", "mail.google.com", "maps.google.de",
		"drive.google.com", "fonts.googleapis.com", "notgoogle.com",
		"google.com.evil.example",
	} {
		if got := SafeSearchTarget(name, []string{"google"}); got != "" {
			t.Errorf("%q was redirected to %q and should not have been", name, got)
		}
	}
}

func TestTheOtherProvidersMatchTheirOwnHostsOnly(t *testing.T) {
	cases := []struct {
		name, key, want string
	}{
		{"www.bing.com", "bing", "strict.bing.com"},
		{"bing.com", "bing", "strict.bing.com"},
		{"cn.bing.com", "bing", ""},
		{"duckduckgo.com", "duckduckgo", "safe.duckduckgo.com"},
		{"html.duckduckgo.com", "duckduckgo", ""},
		{"www.youtube.com", "youtube", "restrictmoderate.youtube.com"},
		{"youtubei.googleapis.com", "youtube", "restrictmoderate.youtube.com"},
		{"pixabay.com", "pixabay", "safesearch.pixabay.com"},
		{"www.yandex.ru", "yandex", "familysearch.yandex.ru"},
	}
	for _, c := range cases {
		if got := SafeSearchTarget(c.name, []string{c.key}); got != c.want {
			t.Errorf("%s/%s = %q, expected %q", c.key, c.name, got, c.want)
		}
	}
}

// Both YouTube entries ticked means the stricter one. The alternative would
// be the order in the list deciding, which nobody can see.
func TestStrictWinsOverModerate(t *testing.T) {
	got := SafeSearchTarget("www.youtube.com", []string{"youtube", "youtube-strict"})
	if got != "restrict.youtube.com" {
		t.Errorf("= %q, expected restrict.youtube.com", got)
	}
}

// The target itself must not be redirected onto itself: the client would
// resolve the loop until it gave up, and the page would simply not load.
func TestTheTargetIsNotRedirectedOntoItself(t *testing.T) {
	for _, c := range []struct{ name, key string }{
		{"forcesafesearch.google.com", "google"},
		{"restrict.youtube.com", "youtube-strict"},
		{"safe.duckduckgo.com", "duckduckgo"},
	} {
		if got := SafeSearchTarget(c.name, []string{c.key}); got != "" {
			t.Errorf("%q was redirected to %q", c.name, got)
		}
	}
}

func TestNothingHappensWithoutProviders(t *testing.T) {
	if got := SafeSearchTarget("www.google.com", nil); got != "" {
		t.Errorf("= %q, expected empty", got)
	}
	if got := SafeSearchTarget("www.google.com", []string{"bing"}); got != "" {
		t.Errorf("= %q, expected empty", got)
	}
}

// A typo has to show at startup. Otherwise somebody believes the children's
// tablet is filtered while the entry does nothing.
func TestATypoIsReported(t *testing.T) {
	unknown := UnknownSafeSearch([]string{"google", "youtube_strict", "Bing"})
	if len(unknown) != 1 || unknown[0] != "youtube_strict" {
		t.Errorf("unknown = %v, expected [youtube_strict]", unknown)
	}
}

func TestTheCatalogueIsOffered(t *testing.T) {
	all := SafeSearchProviders()
	if len(all) < 5 {
		t.Fatalf("only %d providers", len(all))
	}
	for _, p := range all {
		if p.Key == "" || p.Name == "" {
			t.Errorf("incomplete entry: %+v", p)
		}
	}
}
