# The Auspex sensor

Answers the one question a DNS filter fundamentally cannot: **which program**
is talking to which destination.

Auspex sees "this machine asked for `graph.microsoft.com`". Which of the
seventy running programs that was is written nowhere — the operating system
sits between the filter and the program. So the sensor runs on the machine
itself, reads its TCP connection table and reports who is talking to whom.
The dashboard hangs the name and the operator off that through the
IP↔name map.

Optional and opt-in. Nothing in Auspex depends on it being installed; the
settings page says plainly whether it is active, idle or missing.

## What it does not see

This belongs at the beginning, not the end:

- **No content.** It reads the connection table, not the traffic. GET, POST,
  headers and body live in the encrypted part.
- **No UDP and therefore no QUIC.** Windows keeps no remote end for UDP —
  there is no table in the kernel saying where a datagram went. Whatever
  Chrome, Edge and the Google services do over HTTP/3 is missing here.
- **Short connections.** It reads every two seconds. Whatever opens and
  closes in between is not seen.
- **Bytes only with administrator rights**, and even then as a lower bound:
  counting starts the moment the sensor sees the connection.

## What it reports — and what it does not

Program name, destination address, port, number of connections, optionally
bytes.

**No path, no window title, no command line.** The name answers "who is
transmitting here?"; the path would give away user names and installation
locations without answering the question any better.

Which device is meant is not something the sensor says but its sender
address — the same rule as for the browser extension. Nobody can describe
somebody else's device through this route.

## Building

```
dotnet publish sensor/Auspex.Sensor -c Release -r win-x64 --self-contained false
```

The result is `auspex-sensor.exe`. The dashboard offers the same file under
**Settings → Sensor**, so you do not need the repository on the machine.

## Setting it up

The address and the token are in the dashboard under **Settings** — the same
token as for the browser extension. Either in a `sensor.json` next to the
executable:

```json
{
  "base": "http://192.168.1.61:5390",
  "token": "…"
}
```

or as the environment variables `AUSPEX_BASE` and `AUSPEX_TOKEN`.

> Up to version 0.9 the keys were called `basis` and `zeichen` and the
> variables `AUSPEX_BASIS` and `AUSPEX_ZEICHEN`. Both are still read, so an
> existing `sensor.json` does not suddenly look incomplete after an update.

> `sensor.json` is in the `.gitignore`. It carries a token that gives access
> to the dashboard.

## Checking without reporting

```
auspex-sensor.exe --show
```

Shows the open connections grouped by program and exits. That way you can see
whether the sensor finds anything at all before the first report goes out.

## Running it

```
auspex-sensor.exe
```

Stop with Ctrl+C; whatever is still outstanding goes out first.

For permanent operation `setup.ps1` sets everything up — see below.

## Autostart

```
.\setup.ps1
```

Extract the archive first — a script started from inside a ZIP view lands in
a temporary folder, and `auspex-sensor.exe` is then not next to it.

On an upgrade nothing has to be typed: the script reads the settings already
installed under `%LOCALAPPDATA%\Auspex\sensor.json` (including the pre-0.9.0
key names) before it elevates, so the address and the token come along. The
token is shown exactly once, and this is the only place it still exists.

The script elevates itself, puts the executable and the settings into
`%LOCALAPPDATA%\Auspex`, protects `sensor.json` with an access list of its
own and registers a task: **at logon, highest privileges, no visible
window.** Afterwards it starts the task and checks whether a process is
really running — registered is not the same as running.

Highest privileges not out of convenience: Windows only hands out
per-connection byte counters through TCP-ESTATS, and those demand them.

**Who runs the task depends on whether the machine is in a domain.** Three
things are wanted at once: highest privileges, no window, no stored
password. `S4U` can do all three — but only with Kerberos, so only in a
domain. On a standalone machine the task can be registered, but it then
fails at start with `0x80070002` ("file not found") although the file is
there. Outside a domain `SYSTEM` therefore takes over. That costs the
binding to the signed-in user — no loss for this sensor, because the
connection table applies to the whole machine and not per session.

## When nothing arrives

As a task the sensor runs without a window. So that its startup message does
not go nowhere, it also writes it to

```
%LOCALAPPDATA%\Auspex\auspex-sensor.log
```

The first lines there say where it reports to and whether bytes are being
counted — and if not, why not. The file is overwritten on every start; it
shows the state, not the history. No token is in it.

Getting rid of it again:

```
.\setup.ps1 -Remove
```

## Settings

| Key | Default | Meaning |
|---|---|---|
| `base` | — | The dashboard's address |
| `token` | — | The token from the settings |
| `pollSeconds` | 2 | Interval between two looks at the table |
| `reportSeconds` | 30 | Interval between two reports |
| `bytes` | true | Attempt the byte counters (needs admin rights) |
| `verbose` | false | Write every poll to the console |
