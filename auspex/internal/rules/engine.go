package rules

import (
	"sort"
	"strings"
	"sync"
)

// Decision is the result of a check, including the rule that triggered it.
type Decision struct {
	Action Action
	Rule   *Rule
}

func (d Decision) Blocked() bool { return d.Action == ActionBlock }

// ListStats describes what actually became of a list.
type ListStats struct {
	Name       string `json:"name"`
	Lines      int    `json:"lines"`
	Rules      int    `json:"rules"`
	Skipped    int    `json:"skipped"`
	Duplicates int    `json:"duplicates"`
}

// Stats is the overall tally of a build.
type Stats struct {
	BlockRules int `json:"block_rules"`
	AllowRules int `json:"allow_rules"`
	Skipped    int `json:"skipped"`
	Duplicates int `json:"duplicates"`
	// Conflicts are patterns blocked in one list and allowed in another.
	// Allow wins — but you want to know which ones.
	Conflicts []string    `json:"conflicts"`
	Lists     []ListStats `json:"lists"`
}

// Engine is the compiled, read-only rule set.
type Engine struct {
	blockExact  map[string]*Rule
	blockSuffix map[string]*Rule
	allowExact  map[string]*Rule
	allowSuffix map[string]*Rule
	stats       Stats
}

func (e *Engine) Stats() Stats { return e.stats }

// Match walks from the full name upwards label by label. Allow beats block —
// an exception on shop.example.com wins against ||example.com^.
func (e *Engine) Match(qname string) Decision {
	if e == nil {
		return Decision{Action: ActionNone}
	}
	name := normalizeName(qname)
	if name == "" {
		return Decision{Action: ActionNone}
	}
	if r := lookup(e.allowExact, e.allowSuffix, name); r != nil {
		return Decision{Action: ActionAllow, Rule: r}
	}
	if r := lookup(e.blockExact, e.blockSuffix, name); r != nil {
		return Decision{Action: ActionBlock, Rule: r}
	}
	return Decision{Action: ActionNone}
}

func lookup(exact, suffix map[string]*Rule, name string) *Rule {
	if r, ok := exact[name]; ok {
		return r
	}
	rest := name
	self := true
	for {
		if r, ok := suffix[rest]; ok {
			// MatchSubOnly (*.foo) must not hit foo itself.
			if !(self && r.Kind == MatchSubOnly) {
				return r
			}
		}
		i := strings.IndexByte(rest, '.')
		if i < 0 {
			return nil
		}
		rest = rest[i+1:]
		self = false
	}
}

// Builder gathers rules from several sources into one engine.
type Builder struct {
	mu          sync.Mutex
	blockExact  map[string]*Rule
	blockSuffix map[string]*Rule
	allowExact  map[string]*Rule
	allowSuffix map[string]*Rule
	lists       []ListStats
	byList      map[string]int
}

func NewBuilder() *Builder {
	return &Builder{
		blockExact:  map[string]*Rule{},
		blockSuffix: map[string]*Rule{},
		allowExact:  map[string]*Rule{},
		allowSuffix: map[string]*Rule{},
		byList:      map[string]int{},
	}
}

// AddLines processes the contents of a list. forceAllow flips every rule in
// the list to allow (for pure exception lists).
func (b *Builder) AddLines(listName string, content string, forceAllow bool) ListStats {
	st := ListStats{Name: listName}
	for i, raw := range strings.Split(content, "\n") {
		raw = strings.TrimRight(raw, "\r")
		if strings.TrimSpace(raw) == "" {
			continue
		}
		st.Lines++
		rule, ok := ParseLine(raw)
		if !ok {
			st.Skipped++
			continue
		}
		if forceAllow {
			rule.Action = ActionAllow
		}
		rule.List = listName
		rule.Line = i + 1
		if b.add(rule) {
			st.Rules++
		} else {
			st.Duplicates++
		}
	}
	b.mu.Lock()
	b.lists = append(b.lists, st)
	b.byList[listName] = st.Rules
	b.mu.Unlock()
	return st
}

// AddRules takes individual rule lines from the configuration.
func (b *Builder) AddRules(listName string, lines []string, forceAllow bool) {
	for i, raw := range lines {
		rule, ok := ParseLine(raw)
		if !ok {
			continue
		}
		if forceAllow {
			rule.Action = ActionAllow
		}
		rule.List = listName
		rule.Line = i + 1
		b.add(rule)
	}
}

// add enters a rule; false = the pattern was already in this category.
// The first rule wins, so the origin stays stable.
func (b *Builder) add(r Rule) bool {
	b.mu.Lock()
	defer b.mu.Unlock()

	var target map[string]*Rule
	switch {
	case r.Action == ActionAllow && r.Kind == MatchExact:
		target = b.allowExact
	case r.Action == ActionAllow:
		target = b.allowSuffix
	case r.Kind == MatchExact:
		target = b.blockExact
	default:
		target = b.blockSuffix
	}
	if _, exists := target[r.Pattern]; exists {
		return false
	}
	cp := r
	target[r.Pattern] = &cp
	return true
}

func (b *Builder) Build() *Engine {
	b.mu.Lock()
	defer b.mu.Unlock()

	stats := Stats{
		BlockRules: len(b.blockExact) + len(b.blockSuffix),
		AllowRules: len(b.allowExact) + len(b.allowSuffix),
		Lists:      append([]ListStats(nil), b.lists...),
	}
	for _, ls := range b.lists {
		stats.Skipped += ls.Skipped
		stats.Duplicates += ls.Duplicates
	}
	// Conflicts: the same pattern appears on both sides.
	seen := map[string]bool{}
	for pattern := range b.blockExact {
		if _, ok := b.allowExact[pattern]; ok {
			seen[pattern] = true
		}
	}
	for pattern := range b.blockSuffix {
		if _, ok := b.allowSuffix[pattern]; ok {
			seen[pattern] = true
		}
	}
	for pattern := range seen {
		stats.Conflicts = append(stats.Conflicts, pattern)
	}
	sort.Strings(stats.Conflicts)

	return &Engine{
		blockExact:  b.blockExact,
		blockSuffix: b.blockSuffix,
		allowExact:  b.allowExact,
		allowSuffix: b.allowSuffix,
		stats:       stats,
	}
}

// NewFromRules builds a small engine for client or schedule rules.
func NewFromRules(listName string, block, allow []string) *Engine {
	b := NewBuilder()
	b.AddRules(listName, block, false)
	b.AddRules(listName, allow, true)
	return b.Build()
}
