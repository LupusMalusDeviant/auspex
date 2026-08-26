# Rules and lists

## Purpose

Turns text from four different rule formats into one decision — blocked,
allowed, rewritten — and says which rule and which line of which list said so.
Without the origin travelling with the decision, the query log could only
report "blocked", and the question "why?" would be unanswerable.

## Files

| Path | Role |
|------|------|
| `auspex/internal/rules/parser.go` | Reads hosts files, bare domains, AdBlock syntax and wildcards; counts and skips what DNS cannot express |
| `auspex/internal/rules/rule.go` | One rule: pattern, kind, allow/block, origin (list and line) |
| `auspex/internal/rules/engine.go` | The lookup — a hash access plus a walk over the labels, independent of list size |
| `auspex/internal/lists/loader.go` | Fetching, caching on disk, refreshing |
| `auspex/internal/lists/managed.go` | Lists added through the dashboard, kept apart from the ones in the configuration |
| `auspex/internal/lists/catalog.go` | The catalogue of proven lists offered in the interface |
| `auspex/internal/services/catalog.go` | The service catalogue — "block TikTok" becomes ordinary block rules |

## Dependencies

### Internal

- **[Resolver pipeline](./resolver-pipeline.md)** — the only consumer of a
  decision.
- **[Control API](./control-api.md)** — adding, switching and removing managed
  lists.

### External

- `golang.org/x/net/publicsuffix` — for the wildcard and public-suffix edge
  cases.

## Public interface

```go
func Parse(line string, source string, lineNo int) (Rule, bool)
func (e *Engine) Match(name string, allowFirst bool) (Rule, bool)
func (e *Engine) Stats() Stats            // rules per list, conflicts
func lists.KnownLists() []Known           // the catalogue
func services.Domains(name string) ([]string, bool)
```

The matching semantics differ per format on purpose, because the formats mean
different things:

| Rule | matches `x.example` | matches `sub.x.example` |
|---|---|---|
| `0.0.0.0 x.example` (hosts) | yes | **no** |
| `x.example` (bare domain) | yes | yes |
| `\|\|x.example^` (AdBlock) | yes | yes |
| `*.x.example` (wildcard) | **no** | yes |

Exceptions (`@@`) always beat block rules, across lists as well. A pattern
appearing on both sides is reported as a conflict at startup rather than
resolved silently.

## Data flow

1. `loader.go` fetches every configured list, from disk cache if it is fresh.
2. Every line goes through `Parse`. What cannot be expressed in DNS — element
   filters, cosmetic filters, regex — is counted and skipped; the count is
   part of the statistics, so "why are there fewer rules than lines" has an
   answer.
3. `engine.go` builds the lookup structure once and is then read-only.
4. A reload builds a **new** engine and swaps the pointer. Nothing mutates in
   place, so a query in flight always sees one consistent rule set.
5. The service catalogue expands to ordinary rules before this point — after
   which there is no special case anywhere else in the system. A typo in a
   service name fails the start rather than becoming a silently permitted
   service.

## Open questions

- The catalogue holds 32 services against AdGuard's several hundred — point 8
  in [`open-points.md`](../open-points.md).
- The list descriptions in `catalog.go` are deliberately German: they are the
  fallback, and the control plane translates them through
  `Strings.ListDescription`. A list the control plane does not know shows its
  German description in the English interface.
