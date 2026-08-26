# Operations: sign-in, backup, prerequisites

## Purpose

The parts that are not about DNS at all but about running the thing: who may
in, what happens when the disk dies, and how the interface says what is
missing rather than showing an empty column.

## Files

| Path | Role |
|------|------|
| `control/Auspex.Control/Services/PasswordAuth.cs` | Hashing and checking, and the generated password when none is configured |
| `control/Auspex.Control/Services/AuthOptions.cs` | Enabled, user, password hash |
| `control/Auspex.Control/Services/BackupService.cs` | Writes the archive: database, own rules, managed lists, learned state |
| `control/Auspex.Control/Services/RestoreService.cs` | Reads it back — merging, not replacing |
| `control/Auspex.Control/Services/Prerequisites.cs` | Per part: active, idle or missing |
| `control/Auspex.Control/Services/Seams.cs` | The interfaces at the boundaries, and why they are where they are |
| `control/Auspex.Control/Program.cs` | Wiring, the endpoints, the guards |
| `compose.yml`, `control/Auspex.Control/Dockerfile` | How it runs |

## Dependencies

### Internal

- **[Control API](./control-api.md)** — lists and learned state come from the
  resolver, which is why a backup is not a copying of files.
- **[Ingest and storage](./ingest-and-storage.md)** — the database.

### External

- `Microsoft.AspNetCore.DataProtection` — encrypting the router account.
- `System.IO.Compression` — the archive.

## Public interface

```csharp
static string PasswordAuth.Hash(string password);
static bool PasswordAuth.Verify(string password, string stored);
Task<byte[]> BackupService.CreateAsync(CancellationToken ct);
Task<RestoreResult> RestoreService.RestoreAsync(Stream archive, CancellationToken ct);
IReadOnlyList<Part> Prerequisites.Parts();
```

Endpoints: `POST /signin`, `POST /logout`, `GET/POST /backup`,
`GET /healthz`, and `--hash-password` on the command line.

## Data flow

### Sign-in

PBKDF2-SHA256, 210,000 rounds, a random salt per hash, compared in constant
time. The algorithm and the round count sit **inside** the hash — otherwise it
could not be moved to stronger parameters in two years.

Separated with a colon rather than a dollar as in the usual PHC format: the
value ends up in `.env` files and YAML, and there a dollar sign is a variable
that Docker Compose silently expands away. That happened on the first
deployment; sign-in failed with no error message. Old dollar-format hashes
stay valid.

With **no** password configured the application generates one at startup and
writes it to the log. That fails towards "shut" without locking anybody out —
a dashboard standing open without configuration would be the worse default.

### Backup

The database is written out **consistently**, not copied raw: a file copy
would lose whatever has not been checkpointed. Lists and learned state are
fetched through the resolver's API; if it is unreachable, those drop out and
the rest still has to carry.

**Restoring merges.** Whoever restores after a loss usually has hours of new
data again, and deleting that would be a second loss. Duplicates fall away
through the unique indexes — restoring twice changes nothing. A backup from a
different schema version is rejected rather than bent into shape, and paths
inside the archive are checked before anything is extracted.

### Prerequisites

Five parts — analysis, router, extension, sensor, origin lookup — each
reported as **active**, **idle** or **missing**. The distinction is the whole
point: "never set up" and "set up but silent for a day" are different problems
with different answers, and an interface that shows one empty column for both
sends you looking in the wrong place.

The sensor's state comes from the **data** (has anything arrived in the last
24 hours?), not from the configuration. Derived state that gets stored goes
stale.

### The seams

Interfaces sit where something is reached across: the resolver, the router,
the rule files, the range database, the three stores. Not one interface per
class — pure computation has no outside world, and a fake in front of it only
tests the fake. `Seams.cs` says which is which and why, so the next person
does not add six more out of habit.

## Open questions

- The resolver's `config.yaml` is **not** in the backup. It belongs to the
  operator, is only mounted into the resolver container, and can contain an
  API token — a tool that puts its own credentials into a downloadable archive
  is a bad idea. It therefore needs a backup of its own; see
  [`open-points.md`](../open-points.md).
- Sign-in through Authentik instead of a local password is point 5 there.
