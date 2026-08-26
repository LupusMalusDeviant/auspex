// Package rules parses filter lists and decides whether a name is blocked.
//
// Supported formats:
//
//	0.0.0.0 tracker.example      hosts file        -> exact match
//	tracker.example              bare domain       -> domain + subdomains
//	||tracker.example^           AdBlock/AdGuard   -> domain + subdomains
//	@@||shop.example^            AdBlock exception -> allow, domain + subdomains
//	*.tracker.example            wildcard          -> subdomains only
//
// Deliberately NOT supported: element/cosmetic filters and rule modifiers
// ($third-party, $script …). Those need the HTTP context a DNS resolver by
// definition does not have — such lines are counted and skipped rather than
// applied half wrongly.
package rules

import "strings"

type Action uint8

const (
	ActionNone Action = iota
	ActionBlock
	ActionAllow
)

func (a Action) String() string {
	switch a {
	case ActionBlock:
		return "block"
	case ActionAllow:
		return "allow"
	default:
		return "none"
	}
}

type MatchKind uint8

const (
	// MatchExact hits the name itself and nothing else.
	MatchExact MatchKind = iota
	// MatchSuffix hits the name and every subdomain.
	MatchSuffix
	// MatchSubOnly hits subdomains only, not the name itself.
	MatchSubOnly
)

// Rule is a single rule together with its origin — the origin is why "why
// was this blocked?" can be answered at all.
//
// The original line is deliberately NOT stored but reconstructed from
// pattern, kind and effect. At two million rules every extra string held
// costs dozens of megabytes, and the benefit would be small: the canonical
// form is uniform across lists, and anyone needing the original line finds
// it through list and line number.
type Rule struct {
	Pattern string    `json:"pattern"`
	Kind    MatchKind `json:"-"`
	Action  Action    `json:"-"`
	List    string    `json:"list"`
	Line    int       `json:"line"`
}

// Text is the canonical spelling of the rule.
func (r *Rule) Text() string {
	if r == nil {
		return ""
	}
	prefix := ""
	if r.Action == ActionAllow {
		prefix = "@@"
	}
	switch r.Kind {
	case MatchSuffix:
		return prefix + "||" + r.Pattern + "^"
	case MatchSubOnly:
		return prefix + "*." + r.Pattern
	default:
		return prefix + "0.0.0.0 " + r.Pattern
	}
}

// KindString makes MatchKind readable for the API.
func (r *Rule) KindString() string {
	switch r.Kind {
	case MatchSuffix:
		return "suffix"
	case MatchSubOnly:
		return "subdomains"
	default:
		return "exact"
	}
}

func (r *Rule) ActionString() string { return r.Action.String() }

// normalizeName brings a queried name into comparison form.
func normalizeName(name string) string {
	name = strings.TrimSuffix(strings.ToLower(strings.TrimSpace(name)), ".")
	return name
}
