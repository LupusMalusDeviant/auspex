# Detectors

## Purpose

The half that makes Auspex more than a filter: it watches the stored history
for patterns and speaks up on its own, instead of drawing a chart and waiting
for somebody to look at it. Nine heuristics, each of which lays its thresholds
open and ships the numbers with the finding — a finding you cannot recompute
gets ignored after the third one.

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Services/Detectors.cs` | Nine of the eleven detectors, their thresholds and the fingerprints |
| `control/Auspex.Control/Services/DetectionService.cs` | Runs them on a beat and writes what is new |
| `control/Auspex.Control/Services/FindingNotifier.cs` | One line per finding to stdout, for the log-alarm rule |
| `control/Auspex.Control/Services/NotificationOptions.cs` | On/off, marker, minimum severity, cap per pass, maximum age |
| `control/Auspex.Control/Services/Localization/FindingValues.cs` | The measurements a finding stores |
| `control/Auspex.Control/Services/Localization/FindingTexts.cs` | The sentence, built at display time from those values |

## Dependencies

### Internal

- **[Ingest and storage](./ingest-and-storage.md)** — the history it reads and
  the `Findings` table it writes.
- **[Router connection](./router-connection.md)** — `portfreigabe` and
  `neues-geraet` come from the router watch, not from DNS.
- **[Localization](./localization.md)** — the sentence for a finding.

### External

- `Microsoft.EntityFrameworkCore` — the detectors are almost nothing but LINQ.

## Public interface

```csharp
Task<IReadOnlyList<Finding>> RunAsync(DateTime now, CancellationToken ct);
```

The nine, with their identifiers as they are stored in `Findings.Kind`:

| Kind | Fires at |
|---|---|
| `neue-domain` | A domain this device has never asked for (from 5 queries) |
| `nxdomain-flut` | ≥ 40 % of queries running into nothing at ≥ 50 queries |
| `wiederholungssturm` | ≥ 100 queries and ≥ 5× above its own baseline |
| `dauersender` | A lot, evenly, for days, against a block |
| `tunneling-verdacht` | ≥ 50 distinct names under one domain with labels ≥ 30 characters |
| `fehlalarm-verdacht` | ≥ 8 blocked queries to the same domain within 5 minutes |
| `gleichlauf` | ≥ 3 devices discovering the same new domain within 15 minutes |
| `portfreigabe` | A port mapping on the router nobody here opened |
| `neues-geraet` | A device seen on the network for the first time |
| `rebind` | A public name that answered with an address inside the network. Hard finding from 3 distinct names on one device in one window |
| `unerklaerte-verbindung` | The sensor saw connections to ≥ 3 addresses no resolution anywhere accounts for — traffic that went around the resolver |

The identifiers stay German: they are stored per row and are the key the
interface looks the text up by. Renaming them would be a data migration.

## Data flow

1. `DetectionService` runs hourly and hands each detector the same `now`.
2. Two detectors need a baseline and stay silent until there is enough history
   (`BaselineWarmup`, two days by default). Without that every domain would be
   "new" in the first hours and every finding worthless.
3. Each finding gets a **fingerprint** — detector, client, subject, and a time
   bucket. Within one bucket the finding grows rather than repeating itself.
   `dauersender` uses the day, not the hour: a state that reports itself hourly
   becomes wallpaper.
4. **A finding stores only its measurements, not a sentence.** Detection runs
   in the background with nobody having opened a page — there is no reader at
   that moment and therefore no language. The sentence comes into being at
   display time from `FindingValues`.
5. `FindingNotifier` writes one line per new finding, capped per pass. What
   runs over the cap is reported as one collective line and still counts as
   handled — otherwise the flood repeats on the next pass.
6. Reporting is separate from detecting: a finding carries the timestamp for
   when it went out. A crash between the two does not lose it.

### Two corrections, both from measurement

`fehlalarm-verdacht` accounted for 123 of 131 findings and buried the other
five. It now falls silent for a pair already reported on several days.

`dauersender` was added for the case `wiederholungssturm` cannot see. That one
compares against a device's own history and sees only spikes; a device running
against a block equally loudly for days has no spike — factor one. Measured:
486 queries for one blocked name in 46 minutes, not a single finding.

## Open questions

- How often `neue-domain` reports (probably too often) and whether
  `gleichlauf` stays usable during update waves. Both need weeks of real data
  — point 6 in [`open-points.md`](../open-points.md).
