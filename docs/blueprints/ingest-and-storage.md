# Ingest and storage

## Purpose

The resolver's ring buffer holds minutes. The question "what does this device
do all day" needs weeks. This feature is the bridge: it collects the query log
continuously, writes it to SQLite, rolls completed days up so the history
outlives the raw data, and clears away what is past its retention.

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Services/IngestService.cs` | The collector: cursor, boot id, batching, retention |
| `control/Auspex.Control/Services/RollupService.cs` | Rolls a completed day up into daily totals |
| `control/Auspex.Control/Data/AnalyticsDbContext.cs` | The model, the indexes, the unique constraints |
| `control/Auspex.Control/Data/Entities.cs` | Queries, findings, daily totals, exceptions, router inventory, destinations, connections |
| `control/Auspex.Control/Data/AnalyticsOptions.cs` | Connection, poll interval, batch size, retention, warm-up |
| `control/Auspex.Control/Data/Migrations/` | Schema history — one migration per change, hand-written where a rename was meant |
| `control/Auspex.Control/Services/AnalyticsService.cs` | The read side: time series, top lists, rates |
| `control/Auspex.Control/Services/QueryGrouping.cs` | Folds a call's several record types into one row for display |

## Dependencies

### Internal

- **[Control API](./control-api.md)** — the source.
- **[Detectors](./detectors.md)**, **[Impact analysis](./impact-analysis.md)**,
  **[Where the traffic goes](./destinations-and-dossier.md)** — the consumers.

### External

- `Microsoft.EntityFrameworkCore.Sqlite` — the store.
- `Microsoft.Data.Sqlite` — used directly where EF's translation would get in
  the way.

## Public interface

```csharp
Task<int> IngestOnceAsync(CancellationToken ct);          // for tests
Task<int> RollUpDayAsync(DateOnly day, CancellationToken ct);
Task<Overview> OverviewAsync(TimeSpan window, CancellationToken ct);
IReadOnlyList<QueryGroup> Group(IEnumerable<QueryRow> rows);
```

## Data flow

1. Every few seconds: `GET /api/v1/querylog/stream?since=cursor`.
2. If the boot id changed, the cursor resets — the resolver restarted and its
   sequence begins at 1 again.
3. Rows are written in batches. A **unique index on (boot, seq)** makes a
   repeated batch harmless; catching up after a crash cannot produce
   duplicates.
4. Grouping is by registrable domain, not by hostname — otherwise every CDN
   name counts on its own. The Go side computes it, because the public suffix
   list already lives there.
5. **Roll-up happens when the day closes**, not shortly before deletion. So
   the moment does not hang off the retention setting, and shortening
   retention tears no gap. Today is never rolled up: half a measurement stored
   as a whole one could never be corrected.
6. Retention deletes raw queries (90 days by default), connections and
   destinations; daily totals live far longer (730 days).

### Grouping, and why it is the dangerous half

Grouping too little leaves the log unreadable. Grouping too much makes a
difference disappear that nobody will ever see again. So `QueryGrouping` folds
only rows that agree on **all** of: device, name, second, action and rule. Two
record types that came out differently stay two rows, because that difference is
precisely the interesting one.

The same device under IPv4 and IPv6 is **one** row. A modern client asks over
both families at once, and the log otherwise held two near-identical rows for
a single call.

## Open questions

- Dashboard speed has not been measured: with a few thousand rows every number
  would be meaningless. See point 7 in [`open-points.md`](../open-points.md).
