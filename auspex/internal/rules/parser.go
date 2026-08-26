package rules

import (
	"net/netip"
	"strings"
)

// These names appear in practically every hosts file and must never slip
// into a block list.
var hostsNoise = map[string]bool{
	"localhost":             true,
	"localhost.localdomain": true,
	"local":                 true,
	"broadcasthost":         true,
	"ip6-localhost":         true,
	"ip6-loopback":          true,
	"ip6-localnet":          true,
	"ip6-mcastprefix":       true,
	"ip6-allnodes":          true,
	"ip6-allrouters":        true,
	"ip6-allhosts":          true,
	"0.0.0.0":               true,
}

// ParseLine turns a list line into a rule.
// ok=false means: comment, empty, or not expressible as a DNS rule.
func ParseLine(raw string) (Rule, bool) {
	line := strings.TrimSpace(raw)
	if line == "" {
		return Rule{}, false
	}
	// Kommentare: # (hosts), ! (AdBlock), ; (diverse)
	if line[0] == '#' || line[0] == '!' || line[0] == ';' {
		return Rule{}, false
	}
	// [Adblock Plus 2.0] and similar headers
	if line[0] == '[' {
		return Rule{}, false
	}

	action := ActionBlock
	if strings.HasPrefix(line, "@@") {
		action = ActionAllow
		line = line[2:]
	}

	// AdBlock-Syntax
	if strings.HasPrefix(line, "||") {
		body := line[2:]
		// Modifiers ($…) cannot be evaluated in DNS.
		if i := strings.IndexByte(body, '$'); i >= 0 {
			return Rule{}, false
		}
		body = strings.TrimSuffix(body, "^")
		body = strings.TrimSuffix(body, "|")
		if !isPlausibleDomain(body) {
			return Rule{}, false
		}
		return Rule{Pattern: normalizeName(body), Kind: MatchSuffix, Action: action}, true
	}

	// Anything else carrying AdBlock metacharacters is an element or URL filter.
	if strings.ContainsAny(line, "$#@/^|") {
		return Rule{}, false
	}

	// Wildcard: *.tracker.example
	if strings.HasPrefix(line, "*.") {
		body := line[2:]
		if !isPlausibleDomain(body) {
			return Rule{}, false
		}
		return Rule{Pattern: normalizeName(body), Kind: MatchSubOnly, Action: action}, true
	}

	fields := strings.Fields(line)
	switch {
	case len(fields) >= 2:
		// Hosts format: the first column has to be an IP, otherwise the line is junk.
		if _, err := netip.ParseAddr(fields[0]); err != nil {
			return Rule{}, false
		}
		host := fields[1]
		if hostsNoise[strings.ToLower(host)] || !isPlausibleDomain(host) {
			return Rule{}, false
		}
		// Hosts entries apply exactly — 0.0.0.0 ads.example does not
		// automatically block a.b.ads.example as well.
		return Rule{Pattern: normalizeName(host), Kind: MatchExact, Action: action}, true

	case len(fields) == 1:
		host := fields[0]
		if hostsNoise[strings.ToLower(host)] || !isPlausibleDomain(host) {
			return Rule{}, false
		}
		// Nackte Domain-Listen meinen die Domain samt allem darunter.
		return Rule{Pattern: normalizeName(host), Kind: MatchSuffix, Action: action}, true
	}
	return Rule{}, false
}

// isPlausibleDomain filters out IPs, empty labels and character junk.
func isPlausibleDomain(s string) bool {
	s = strings.TrimSuffix(strings.TrimSpace(s), ".")
	if s == "" || len(s) > 253 {
		return false
	}
	if _, err := netip.ParseAddr(s); err == nil {
		return false // a bare IP is not a domain
	}
	if !strings.Contains(s, ".") {
		return false // TLD-wide rules are almost always a mistake in the list
	}
	for _, label := range strings.Split(s, ".") {
		if label == "" || len(label) > 63 {
			return false
		}
		for i := 0; i < len(label); i++ {
			c := label[i]
			switch {
			case c >= 'a' && c <= 'z', c >= 'A' && c <= 'Z', c >= '0' && c <= '9', c == '-', c == '_':
			default:
				return false
			}
		}
	}
	return true
}
