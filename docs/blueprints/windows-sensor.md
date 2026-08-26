# Windows sensor

## Purpose

Answers the one question a DNS filter fundamentally cannot: **which program**
is talking to which destination. Auspex sees "this machine asked for
`graph.microsoft.com`"; which of the seventy running programs that was is
written nowhere, because the operating system sits in between. The sensor runs
on the machine, reads its TCP connection table and reports.

Optional and opt-in. Nothing depends on it being installed.

## Files

| Path | Role |
|------|------|
| `sensor/Auspex.Sensor/Program.cs` | The loop, `--show`, and the startup lines |
| `sensor/Auspex.Sensor/ConnectionTable.cs` | `GetExtendedTcpTable` for IPv4 and IPv6 |
| `sensor/Auspex.Sensor/ByteCounter.cs` | TCP ESTATS per connection — the part that needs administrator rights |
| `sensor/Auspex.Sensor/Ledger.cs` | Folds what was seen into relations with counts and first/last |
| `sensor/Auspex.Sensor/Reporter.cs` | Sends a batch, in the exact shape the API binds |
| `sensor/Auspex.Sensor/Settings.cs` | `sensor.json` or environment, with the pre-0.9 keys as a fallback |
| `sensor/Auspex.Sensor/Journal.cs` | Writes the startup lines to a file, because a scheduled task has no console |
| `sensor/Auspex.Sensor/SensorJson.cs` | Source-generated serialisation, for trimming |
| `sensor/setup.ps1` | Installs it as a task: at logon, highest privileges, no window |
| `control/…/Services/Extension/SensorApi.cs` | Takes the reports in and folds them into the database |
| `control/…/Services/Extension/SensorPackage.cs` | Hands the executable out from the dashboard |

## Dependencies

### Internal

- **[Browser extension](./browser-extension.md)** — shares the token and the
  rule that the sender address decides the device.
- **[Destinations and the dossier](./destinations-and-dossier.md)** — the
  program column on the "where to?" page.

### External

- Win32: `GetExtendedTcpTable`, `SetPerTcpConnectionEStats`,
  `GetPerTcpConnectionEStats`. No package.

## Public interface

`POST /api/ext/connections` with

```json
{"connections": [{"process": "…", "destination": "…", "port": 443,
                  "protocol": "tcp", "count": 3, "first": "…", "last": "…",
                  "bytesOut": 1234, "bytesIn": 5678}]}
```

and the answer `{"accepted": n, "device": "…"}`.

CLI: `auspex-sensor.exe --show` lists the open connections grouped by program
and exits, so you can see whether it finds anything before the first report
goes out. `setup.ps1 -Remove` takes the task and the files away again.

## Data flow

1. Every two seconds the connection table is read. Only **established**
   connections count.
2. `Ledger` folds them into relations keyed by process, destination, port and
   protocol, and carries counts and first/last forward across polls.
3. Every 30 seconds a batch goes out. The same key can occur several times in
   one batch and returns in every following batch, so both sides fold: the
   sensor before sending and the API before writing. Two rows with the same key
   would violate the unique index.
4. The API asks the resolver who the sender address is and stores the device
   name with the rows. **The sensor never says which device it is.**

### What it cannot see, and says so

- **No content.** It reads a table, not the traffic.
- **No UDP and therefore no QUIC.** Windows keeps no remote end for UDP —
  there is no kernel table saying where a datagram went.
- **Short connections.** Anything that opens and closes between two polls is
  missed.
- **Bytes only with administrator rights**, and then as a lower bound:
  counting starts when the sensor first sees the connection. Null means "not
  counted"; a zero would look like a measurement and would claim the program
  sends nothing.

### Two things that cost a day each

- **`TCP_ESTATS_TYPE` 0 is `SynOpts`, not `Data`.** The code enabled type 0
  and every call came back `ERROR_INVALID_USER_BUFFER`, because SynOpts has no
  read-write structure at all. `Data` is 1. Measured across every buffer size
  and version before changing anything, so size was never the cause.
- **Windows locks the executable of a running process.** `setup.ps1` copied
  over it before stopping the sensor, the copy failed and the script aborted,
  and because an elevated window closes on abort, a failed update looked
  exactly like a successful one. It now stops first, waits for the lock to
  clear, and holds the window open on failure.

### Who runs the task

Three things are wanted at once: highest privileges, no visible window, no
stored password. `S4U` can do all three, but only with Kerberos and therefore only in a
domain. On a standalone machine the task registers and then fails at start
with `0x80070002`, which Windows reports as "file not found" although the file
is there. Outside a domain `SYSTEM` takes over instead; the connection table
applies to the whole machine anyway, not per session.

## Open questions

- The byte count is per connection and TCP-only, so it answers "what is this
  program sending" rather than "who is filling the line". That is point 10 in
  [`open-points.md`](../open-points.md).
