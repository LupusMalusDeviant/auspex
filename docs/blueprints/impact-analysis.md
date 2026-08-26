# Impact analysis and rule writing

## Purpose

Compute a rule against the stored history before it goes live, and write it
with one click when it looks right. The decisive figure is not "how many
match" but "how many are decided differently from today" — a block rule on
something already blocked changes nothing, and an exception only bites where
things are blocked at present.

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Services/RuleParser.cs` | Reads the same four formats as the resolver, with the same semantics |
| `control/Auspex.Control/Services/ImpactService.cs` | Runs a parsed rule against the history and counts the difference |
| `control/Auspex.Control/Services/RuleWriter.cs` | Appends to the shared rule files and asks the resolver to reload |
| `control/Auspex.Control/Components/Pages/Impact.razor` | The page |
| `control/Auspex.Control/Components/Pages/Explain.razor` | "Why was this blocked?" for one name |

## Dependencies

### Internal

- **[Ingest and storage](./ingest-and-storage.md)** — the history it computes
  against.
- **[Rules and lists](./rules-and-lists.md)** — the resolver reads the written
  files as ordinary lists.
- **[Browser extension](./browser-extension.md)** — writes its exceptions
  through the same writer.

### External

- `Microsoft.EntityFrameworkCore` — the counting is a query.

## Public interface

```csharp
static bool RuleParser.TryParse(string text, out ParsedRule rule);
Task<ImpactResult> ImpactService.EvaluateAsync(ParsedRule rule, TimeSpan window, CancellationToken ct);
Task<WriteResult> RuleWriter.AddAsync(string rule, string reason, RuleTarget target, CancellationToken ct);
Task<bool> RuleWriter.EnsureExistsAsync(CancellationToken ct);
```

`ImpactResult` carries affected, newly decided, and the devices involved —
abbreviated for display but counted in full.

## Data flow

1. The text goes through `RuleParser`, which **mirrors the data plane's
   semantics** — including the differences between a hosts entry, a bare
   domain and a wildcard. A rule read differently here than there would be
   worse than no analysis, so both parsers have the same test cases.
2. `ImpactService` asks the history: which rows does the rule match, and what
   is their current action? Affected minus already-decided-that-way is the
   number that matters.
3. Writing appends to a shared file the resolver reads as a list — exceptions
   with `allow: true`, blocks without. No extra API route was needed; the list
   mechanism could already do it.
4. **Writing and reloading are reported separately.** If the rule is in the
   file while the resolver happens to be unreachable, it applies at the next
   reload, and the interface says exactly that rather than claiming failure.
5. Rules are written with their reason as a comment. Whoever finds the file in
   a year should know where it came from.

## Open questions

- The two parsers are kept in step by shared test cases, not by shared code —
  they are in different languages. A case added on one side and not the other
  is the way they would drift.
