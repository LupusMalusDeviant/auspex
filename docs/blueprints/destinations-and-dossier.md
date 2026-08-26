# Destinations and the dossier

## Purpose

DNS says which name was asked for. It does not say who is behind the address.
This feature keeps the addresses an answer pointed at, works out the operator
and the country behind them, and turns that into the page that answers "where
does this device actually send things?".

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Services/Geo/DestinationCapture.cs` | Keeps addresses and name↔address pairs out of a batch of log entries |
| `control/Auspex.Control/Services/Geo/NetworkRanges.cs` | The range database: address → AS, operator, country |
| `control/Auspex.Control/Services/Geo/GeoSources.cs` | Where the ranges come from and how the files are read |
| `control/Auspex.Control/Services/Geo/GeoService.cs` | Keeps the data fresh and fills in operators and cities |
| `control/Auspex.Control/Services/Geo/CityLookup.cs` | The city ranges, in their own file with their own format |
| `control/Auspex.Control/Services/Geo/AddressSpace.cs` | Normalising and comparing addresses as numbers |
| `control/Auspex.Control/Services/Geo/DossierService.cs` | The page's figures: per operator, per program, what never left |
| `control/Auspex.Control/Components/Pages/Dossier.razor` | The page |

## Dependencies

### Internal

- **[Ingest and storage](./ingest-and-storage.md)** — the batches it reads
  and the two tables it writes.
- **[Windows sensor](./windows-sensor.md)** — the program column, where a
  sensor is installed.

### External

- `Microsoft.Data.Sqlite` — the range database is a file of its own, queried
  directly rather than through EF.

## Public interface

```csharp
Task<int> DestinationCapture.CarryDestinationsForwardAsync(IEnumerable<QueryRow> rows, CancellationToken ct);
Task<int> DestinationCapture.CarryResolutionsForwardAsync(IEnumerable<QueryRow> rows, CancellationToken ct);
Origin? NetworkRanges.Find(IPAddress address);
PartState NetworkRanges.State();
Task<Dossier> DossierService.ForDeviceAsync(string device, TimeSpan window, CancellationToken ct);
```

## Data flow

1. The resolver already delivers, with every answered query, what the name
   pointed at. That used to be discarded.
2. `DestinationCapture` writes **one row per address** and **one per
   name-and-address pair**, not one per query. At around 140,000 queries a day
   and a few thousand addresses involved, that is the difference between a
   table you can analyse and one that merely grows.
3. Local addresses are recorded as private and never looked up — information
   about your own router would be empty at best and invented at worst.
4. `GeoService` fills in operators in the background from the range database,
   and cities from a second one. Both are opt-in: they are around 90 MB each,
   and downloading that on a first container start is not a decision the
   software should make for the operator.
5. `DossierService` puts it together. **The most important figure stands
   before the list of recipients**: what never left the house at all. Without
   it the list below reads as the device's entire behaviour.

### Two honesty rules in the numbers

- **The city is marked uncertain** wherever the value names a node rather than
  a headquarters — usually the nearest one. A map would turn that into a
  company address. Data-centre operators are exempt: an address at Hetzner
  really is in Falkenstein, and sowing doubt where the value is right
  devalues the marker where it is needed.
- **AS 0 / "Not routed" is not an operator.** The source fills gaps with it;
  showing that would be worse than nothing.

### The bug worth remembering

IPv4 is embedded as `::ffff:a.b.c.d` and therefore lies numerically *inside*
low IPv6 ranges. A range starting at `::` and reaching far enough encloses
every IPv4 address — and would ascribe an operator to it that has nothing to
do with it. The lookup separates the families for exactly that reason, and
there is a test for it.

## Open questions

- `NetworkRanges.State()` has to answer before the first import, when its
  table does not exist. `EXISTS` in the same statement does not help — SQLite
  resolves names at prepare time — so it asks `sqlite_master` separately. Easy
  to undo by accident while tidying.
