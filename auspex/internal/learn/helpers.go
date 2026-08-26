package learn

import (
	"strings"

	"golang.org/x/net/publicsuffix"
)

func normalize(name string) string {
	return strings.TrimSuffix(strings.ToLower(strings.TrimSpace(name)), ".")
}

// RegistrableDomain is the exported variant for callers outside learn mode —
// the analysis groups by it.
func RegistrableDomain(name string) string {
	return registrableDomain(normalize(name))
}

// registrableDomain returns eTLD+1. Without a public suffix list the naive
// assumption "the last two labels" would be plainly wrong for foo.co.uk or
// bar.com.au — and would release entire country TLDs in enforce mode.
func registrableDomain(name string) string {
	domain, err := publicsuffix.EffectiveTLDPlusOne(name)
	if err != nil || domain == "" {
		return name
	}
	return domain
}

// isReverseZone erkennt PTR-Lookups.
func isReverseZone(name string) bool {
	return strings.HasSuffix(name, ".in-addr.arpa") || strings.HasSuffix(name, ".ip6.arpa")
}

func contains(list []string, v string) bool {
	for _, item := range list {
		if item == v {
			return true
		}
	}
	return false
}

func sanitize(name string) string {
	out := make([]rune, 0, len(name))
	for _, r := range name {
		switch {
		case r >= 'a' && r <= 'z', r >= 'A' && r <= 'Z', r >= '0' && r <= '9', r == '-', r == '_':
			out = append(out, r)
		default:
			out = append(out, '-')
		}
	}
	if len(out) == 0 {
		return "profile"
	}
	return string(out)
}
