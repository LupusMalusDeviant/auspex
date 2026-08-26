# The Auspex sensor

The sensor answers a question a DNS filter cannot answer on its own: which
program is talking to which destination.

Auspex can see that a machine asked for `graph.microsoft.com`. Which of the
seventy programs running on that machine sent the request is not recorded
anywhere, because the operating system sits between the filter and the
program. The sensor therefore runs on the machine itself, reads its TCP
connection table and reports which program is connected to which address. The
dashboard then adds the domain name and the operator behind it, using the
mapping between addresses and names it already has.

The sensor is optional and has to be installed deliberately. Nothing in Auspex
depends on it, and the settings page states whether it is active, idle or not
installed.

## What it cannot see

These limits belong at the start rather than in a footnote.

- **No content.** The sensor reads the connection table, not the traffic
  itself. Request methods, headers and bodies are inside the encrypted part
  of the connection.
- **No UDP, and therefore no QUIC.** Windows does not record a remote address
  for UDP, so the kernel has no table saying where a datagram went. Whatever
  Chrome, Edge and the Google services send over HTTP/3 is invisible here.
- **Short connections.** The sensor reads the table every two seconds.
  Anything that opens and closes between two reads is missed.
- **Byte counts only with administrator rights**, and even then they are a
  lower bound, because counting starts the moment the sensor first sees the
  connection.

## What it reports

Program name, destination address, port, number of connections, optionally
bytes.

It deliberately does not report the executable path, the window title or the
command line. The program name already answers the question "what is sending
this"; the path would additionally reveal user names and installation
locations without adding anything useful.

The sensor also does not state which device it is reporting for. Auspex takes
that from the sender address of the report, the same way it does for the
browser extension, so a report cannot be used to describe someone else's
device.

## Building

```
dotnet publish sensor/Auspex.Sensor -c Release -r win-x64 --self-contained false
```

The result is `auspex-sensor.exe`. The dashboard offers the same file under
**Settings → Sensor**, so you do not need the repository on the machine.

## Setting it up

The address and the token are shown in the dashboard under **Settings**; it is
the same token the browser extension uses. Put them either in a `sensor.json`
next to the executable:

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

For permanent operation, `setup.ps1` sets everything up. See below.

## Autostart

```
.\setup.ps1
```

Extract the archive first. A script started from inside a ZIP preview runs
from a temporary folder, where `auspex-sensor.exe` is not next to it.

On an upgrade nothing has to be typed: the script reads the settings already
installed under `%LOCALAPPDATA%\Auspex\sensor.json` (including the pre-0.9.0
key names) before it elevates, so the address and the token come along. The
token is shown exactly once, and this is the only place it still exists.

The script elevates itself, puts the executable and the settings into
`%LOCALAPPDATA%\Auspex`, protects `sensor.json` with an access list of its
own and registers a task: **at logon, highest privileges, no visible
window.** Afterwards it starts the task and checks whether a process is
really running, since a registered task is not necessarily a running one.

Highest privileges not out of convenience: Windows only hands out
per-connection byte counters through TCP-ESTATS, and those demand them.

**Who runs the task depends on whether the machine is in a domain.** Three
things are wanted at once: highest privileges, no window, no stored
password. `S4U` can do all three, but only with Kerberos, and therefore only
in a domain. On a standalone machine the task can be registered, but it then
fails at start with `0x80070002` ("file not found") although the file is
there. Outside a domain `SYSTEM` therefore takes over. That costs the
binding to the signed-in user. That costs nothing here, because the
connection table covers the whole machine rather than a single session.

## When nothing arrives

As a task the sensor runs without a window. So that its startup message does
not go nowhere, it also writes it to

```
%LOCALAPPDATA%\Auspex\auspex-sensor.log
```

The first lines there say where it reports to and whether bytes are being
counted, and if not, why not. The file is overwritten on every start, so it
shows the current state rather than a history. It contains no token.

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
