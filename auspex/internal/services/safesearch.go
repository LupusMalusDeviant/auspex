package services

import (
	"strings"

	"golang.org/x/net/publicsuffix"
)

// SafeSearch redirects a search engine to the address it serves filtered
// results from.
//
// The mechanism is the providers' own: every one of them runs a host that
// answers with the safe variant, and points the ordinary host there when you
// send a CNAME. Nothing here inspects traffic or rewrites content — the
// filtering happens at the provider, and Auspex only decides which door the
// device is sent through.
//
// # Why this belongs to the profile and not to the network
//
// A household is not one setting. The children's tablet wants filtered
// results, the workshop computer needs to be able to search for a drill bit
// without the picture search deciding otherwise. A global switch would force
// the strictest common denominator on everybody, and the usual outcome of
// that is that somebody turns it off for everybody.
//
// So the provider list hangs off the client profile, and off the schedule
// inside it — which makes "filtered while the children are awake" a thing
// that can be expressed. Neither Pi-hole nor AdGuard Home can do it per
// profile *and* per time window.
//
// # What it cannot do
//
// It only bites on the search engines listed here, and only while the device
// asks Auspex. A browser with its own DNS-over-HTTPS goes round it — which is
// what the canary domain and the blocked DoH endpoints are for. And nothing
// stops somebody typing the unfiltered address of a search engine that is not
// in this table. A speed bump, not a lock, and it says so.
type safeSearch struct {
	// key is the name used in the configuration.
	key string
	// name is what the interface shows.
	name string
	// target is the host the query is redirected to.
	target string
	// hosts are matched exactly, without a trailing dot.
	hosts []string
	// registrable, when set, matches any name whose registrable domain
	// begins with this label — google.de, google.co.uk, google.com.au and
	// the roughly 190 others. Listing them would be a table that is out of
	// date the day it is finished; the public suffix list already knows
	// where the domain ends.
	registrable string
	// prefixes are the host labels allowed in front of a registrable match.
	// Only the search entry points, so that maps.google.de or
	// accounts.google.com keep working.
	prefixes []string
}

var safeSearchProviders = []safeSearch{
	{
		key: "google", name: "Google", target: "forcesafesearch.google.com",
		registrable: "google", prefixes: []string{"", "www", "images", "ipv4", "ipv6"},
	},
	{
		key: "youtube", name: "YouTube (moderate)", target: "restrictmoderate.youtube.com",
		hosts: youtubeHosts,
	},
	{
		// Strict hides more and breaks more. It is a separate entry rather
		// than a flag so the interface can offer the choice instead of
		// burying it in a boolean.
		key: "youtube-strict", name: "YouTube (strict)", target: "restrict.youtube.com",
		hosts: youtubeHosts,
	},
	{
		key: "bing", name: "Bing", target: "strict.bing.com",
		hosts: []string{"bing.com", "www.bing.com"},
	},
	{
		key: "duckduckgo", name: "DuckDuckGo", target: "safe.duckduckgo.com",
		hosts: []string{"duckduckgo.com", "www.duckduckgo.com", "start.duckduckgo.com"},
	},
	{
		key: "yandex", name: "Yandex", target: "familysearch.yandex.ru",
		registrable: "yandex", prefixes: []string{"", "www"},
	},
	{
		key: "pixabay", name: "Pixabay", target: "safesearch.pixabay.com",
		hosts: []string{"pixabay.com", "www.pixabay.com"},
	},
}

var youtubeHosts = []string{
	"youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com",
	"youtubei.googleapis.com", "youtube.googleapis.com",
	"youtube-nocookie.com", "www.youtube-nocookie.com",
}

// SafeSearchProvider is one entry, for the interface and the API.
type SafeSearchProvider struct {
	Key  string `json:"key"`
	Name string `json:"name"`
}

// SafeSearchProviders returns the catalogue, in the order it is offered.
func SafeSearchProviders() []SafeSearchProvider {
	out := make([]SafeSearchProvider, 0, len(safeSearchProviders))
	for _, p := range safeSearchProviders {
		out = append(out, SafeSearchProvider{Key: p.key, Name: p.name})
	}
	return out
}

// UnknownSafeSearch returns the keys that are not in the catalogue.
//
// A typo has to fail at startup rather than ending up as a provider that is
// silently not enforced — the same rule as for block_services. Somebody who
// writes "youtube_strict" would otherwise believe their children's tablet is
// filtered.
func UnknownSafeSearch(keys []string) []string {
	var unknown []string
	for _, k := range keys {
		if !knownSafeSearch(strings.ToLower(strings.TrimSpace(k))) {
			unknown = append(unknown, k)
		}
	}
	return unknown
}

func knownSafeSearch(key string) bool {
	for _, p := range safeSearchProviders {
		if p.key == key {
			return true
		}
	}
	return false
}

// SafeSearchTarget returns the host the name has to be redirected to, or "".
//
// With both YouTube entries enabled the strict one wins: whoever ticked both
// meant the stricter of the two, and the alternative — order deciding — would
// depend on how the list happened to be written down.
func SafeSearchTarget(name string, keys []string) string {
	if len(keys) == 0 {
		return ""
	}
	name = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(name), "."))
	if name == "" {
		return ""
	}

	enabled := make(map[string]bool, len(keys))
	for _, k := range keys {
		enabled[strings.ToLower(strings.TrimSpace(k))] = true
	}
	if enabled["youtube-strict"] {
		delete(enabled, "youtube")
	}

	for _, p := range safeSearchProviders {
		if !enabled[p.key] || !p.matches(name) {
			continue
		}
		// A name that is already the target must not be redirected onto
		// itself - that would be a loop the client resolves until it gives
		// up.
		if name == p.target {
			return ""
		}
		return p.target
	}
	return ""
}

func (p safeSearch) matches(name string) bool {
	for _, h := range p.hosts {
		if name == h {
			return true
		}
	}
	if p.registrable == "" {
		return false
	}

	// EffectiveTLDPlusOne gives "google.co.uk" for "www.google.co.uk". What
	// is left in front has to be one of the search entry points, or
	// accounts.google.com would be redirected too and signing in would
	// break.
	domain, err := publicsuffix.EffectiveTLDPlusOne(name)
	if err != nil {
		return false
	}
	label, _, ok := strings.Cut(domain, ".")
	if !ok || label != p.registrable {
		return false
	}

	prefix := strings.TrimSuffix(strings.TrimSuffix(name, domain), ".")
	for _, allowed := range p.prefixes {
		if prefix == allowed {
			return true
		}
	}
	return false
}
