# Map of the codebase

This document explains where things live in the codebase and why they were
put there. It is meant for someone reading the project for the first time, and
for the author six months later.

## How the parts are separated

Auspex consists of three programs written in three languages, and the split
between them is deliberate.

The **resolver** (Go) answers DNS queries. Everything slow is kept out of it:
it holds no database, renders no pages and never waits for another component.

The **control plane** (.NET) does everything that is allowed to take time —
the web interface, the analysis, the router connection and the APIs for the
extension and the sensor. It pulls its data from the resolver rather than
being pushed to, so the resolver keeps working even when the control plane is
down.

The **sensor** (C#, Windows) is optional. It supplies the one piece of
information that DNS cannot contain: which program on a machine opened a
given connection.

```
   Devices on the network
        │ DNS (53)
        ▼
┌─────────────────┐   HTTP    ┌──────────────────┐
│    auspex       │◄──────────│  auspex-control  │
│    (Go)         │   fetch   │    (.NET)        │
│                 │           │                  │
│ resolve         │           │ interface        │──► browser
│ filter          │           │ analysis         │
│ cache           │           │ router (TR-064)  │──► Fritz!Box
│ record          │           │ extension API    │◄── plugin
└─────────────────┘           │ sensor API       │◄── Windows sensor
        │                     └──────────────────┘
        ▼                              │
   upstreams (DoT/DoH)                 ▼
                                    SQLite
```

## Naming, and where it stops

Since 0.9.0 every **name in the code** is English — types, members, locals,
functions, files, CSS classes and API routes of our own.

Page routes are English as well. They had been half renamed already:
`/devices`, `/settings` and `/querylog` were switched at some point while
`/analyse`, `/wohin` and `/sicherung` were not, and the query log still linked
to `/geraete`, which no longer existed. Bookmarks from before 0.9.0 therefore
break once. That was accepted, because the alternative was a set of routes in
two languages that nobody could remember.

What is **not** renamed is the **shape of stored data**, because renaming
that would be a migration rather than a rename:

| Stays as it is | Why |
|---|---|
| The appearance vocabulary (`fassung`, `akzent`, `dichte`, `schrift` and their values) | Sits in the settings file on the server and in every user's `localStorage` |
| The JSON names in `Findings.Values` | Sits in the live database, one row per finding |
| The German search terms in `RouterSearch` | They *are* the input: people search a Fritz!Box in German |
| The patterns in `RouterLog` | They match the German firmware's own log |
| `"täglich"`, `"werktags"`, `"wochenende"` in the schedule config | Accepted configuration words, next to their English equivalents |
| The list descriptions in `internal/lists/catalog.go` | Deliberately the German fallback; the control plane translates them through `Strings.ListDescription` |

Display text is handled differently again. It lives in
`Services/Localization/` and exists twice, in German and English. A test keeps
the two in step: `LanguageTests` fails if an English field still contains a
German sentence.

The Go side has an equivalent guard, added after it turned out to be needed.
`TestNoGermanLeftInTheGoSource` in `cmd/auspex/language_test.go` walks every
`.go` file and fails on an umlaut or on a word from a list of German terms,
with the exceptions above named explicitly. It was written because release
0.9.0 claimed the log messages had been translated while sixteen of them had
not. They were found by starting the binary and reading its output, which is
not a reliable way to check.

## Where to start reading

| Question | File |
|---|---|
| How does a query get answered? | [`auspex/internal/resolver/resolver.go`](../auspex/internal/resolver/resolver.go) |
| What does "blocked" mean? | [`auspex/internal/rules/engine.go`](../auspex/internal/rules/engine.go) |
| How does it all hang together? | [`control/Auspex.Control/Program.cs`](../control/Auspex.Control/Program.cs) |
| Where are the seams? | [`control/Auspex.Control/Services/Seams.cs`](../control/Auspex.Control/Services/Seams.cs) |
| What can the router do? | [`control/Auspex.Control/Services/Router/Tr064Client.cs`](../control/Auspex.Control/Services/Router/Tr064Client.cs) |
| Which knobs are there? | [`auspex/config.example.yaml`](../auspex/config.example.yaml) |

## The resolver — `auspex/`

The module `auspex` contains 14 packages under `internal/` and three programs
under `cmd/`.

### The way a query travels

The order of these steps matters, and each step is where it is for a reason.
It is defined in `resolver.go`:

1. **Special cases** — Firefox's canary `use-application-dns.net`, so the
   browser switches its own DoH off and does not bypass the filtering.
2. **Local zones** (`local.go`) — `fritz.box` and private reverse lookups go
   to the router. This happens before the filter and before the cache, so
   that an internal name can never be sent to a public server.
3. **Device profile** (`policy.go`) — who is asking, and which rules apply
   to them.
4. **Filter** (`rules/`) — block and allow rules.
5. **Cache** (`cache/`) — the CNAME check runs on cache hits too. Without
   that, a cloaking chain could be stored once and then reused without
   further checks.
6. **Upstream** (`upstream/`, `doh/`) — DoT or DoH, racing or in order.
7. **Recording** (`querylog/`) — a ring buffer in memory, which the control
   plane collects.

### The packages

| Package | Lines | For |
|---|---:|---|
| `resolver` | 1296 | The pipeline. `resolver.go` leads, `policy.go` maps devices, `local.go` keeps internal names in the house, `response.go` builds answers including the SOA for negative caching |
| `api` | 687 | The HTTP interface inwards: status, query log, rules, "who is this address?", dropping cache entries |
| `learn` | 506 | Learning mode for IoT: open → learn → enforce. A device first shows what it needs, afterwards exactly that applies |
| `config` | 505 | Configuration including validation. Whatever slips through here only shows in production |
| `upstream` | 476 | Connections outwards, failover, bootstrap |
| `rules` | 460 | The rule set: hosts format, bare domains, AdBlock syntax `\|\|domain^`, exceptions `@@\|\|domain^` |
| `lists` | 412 | Loading, checking and managing blocklists |
| `names` | 384 | Device names — from the neighbour table, from PTR, from the router's list |
| `cache` | 297 | TTL handling, negative caching per RFC 2308, eviction, prefetch, serving stale |
| `querylog` | 270 | The ring buffer of queries |
| `neigh` | 267 | The kernel's neighbour table over netlink: address → MAC. The reason a device stays the same one after an address change |
| `doh` | 187 | DNS over HTTPS |
| `services` | 172 | The service catalogue ("block TikTok" rather than thirty domains) |
| `clients` | 136 | Device profiles |

### The programs

- `cmd/auspex` — the resolver itself.
- `cmd/auspexdig` — a `dig` that asks through Auspex and shows which rule
  applied. For following up a finding.
- `cmd/auspexload` — a load tool.

### Why netlink by hand

`neigh/netlink_linux.go` reads the attributes out itself rather than taking
`syscall.ParseNetlinkRouteAttr`. That is not for its own sake: the function
does not know `RTM_NEWNEIGH` and silently returns nothing. Whoever does not
know that goes looking for a permissions problem.

## The control plane — `control/`

.NET 10, Blazor Server, EF Core on SQLite.

### The seams — `Services/Seams.cs`

Interfaces sit where something is *reached across*: the resolver, the
router, the rule files, the network-range database, the three stores. Not
one interface per class — an interface that has exactly one implementation
and one caller documents nothing and only adds a file to open. The comment
in `Seams.cs` says which is which and why.

### What runs in the background

| Service | Beat | Job |
|---|---|---|
| `IngestService` | continuous | Collects the query log from the resolver. Catches up after a crash without writing twice |
| `DetectionService` | hourly | Runs the detectors |
| `RollupService` | daily | Rolls up days, so the analysis outlives the deletion of the raw data |
| `CacheWarmingService` | at startup | Warms the cache from history |
| `RouterWarmupService` | at startup | Pre-reads the router catalogue — a good forty description files, or the first router page waits for them |
| `RouterWatchService` | 5 min | Watches port mappings and new devices |
| `DeviceNameExportService` | continuous | Writes the router's device list out for the resolver |
| `ExceptionCleanupService` | continuous | Clears away expired temporary exceptions |

### The detectors — `Services/Detectors.cs`

Heuristics, not truth. Each one lays its thresholds open and delivers the
numbers with the finding; a finding you cannot check the arithmetic on is
worthless.

| Detector | Asks |
|---|---|
| `neue-domain` | Something this device has never asked for |
| `nxdomain-flut` | Many names with no answer — typical of malware looking for a meeting point |
| `wiederholungssturm` | Markedly more than usual, measured against its own history |
| `dauersender` | A lot, evenly, for days — the case the storm detector **cannot** see, because a steady state has no spike |
| `tunneling-verdacht` | Conspicuously long names: data inside DNS |
| `fehlalarm-verdacht` | A block a device keeps running into — with an exception as the suggestion |
| `gleichlauf` | Several devices, the same new domain, in quick succession |
| `portfreigabe` | A door to the outside that nobody here opened |
| `neues-geraet` | Seen on the network for the first time |

The identifiers stay as they are: they are stored in the `Findings` table
and are the key the interface looks its text up by. All of them write into
that same table with a fingerprint against repetition. The beat is
selectable: most per hour, `dauersender` per day.

### Where the traffic goes — `Services/Geo/`

DNS tells you which name was asked for, but not who is behind the address.
For each answered query, `DestinationCapture` stores one row per address and
one per name-and-address pair. It deliberately does not store one row per
query: at roughly 140,000 queries a day, that difference decides whether the
table stays small enough to analyse. `NetworkRanges` holds the address ranges,
`GeoService` adds the operator and city, and `DossierService` turns all of it
into the page that answers "where does this device actually send data?".

`ProgramService` reads the same two tables from the other end. Joined over the
address, `Connections` and `Resolutions` answer the question "which program
asks for which domain". Addresses that no lookup explains are counted
separately instead of being dropped, because that number represents traffic
which went around the resolver. The detector `unerklaerte-verbindung` reports
on it.

`QuarantineService` is the acting half: it switches a profile to the resolver
policy `quarantine` and back. The list of what is held lives in
`var/quarantine.json` rather than in the database, because a restart is
exactly the moment a forgotten quarantine would become a device that is off
the network with nothing left to say why.

The city is marked as *uncertain* wherever it names a node rather than a
headquarters. Leaving that out would be more convenient and less honest.

### The router connection — `Services/Router/`

Auspex uses two channels, because one is not enough.

**TR-064** (`Tr064Client.cs`) is the supported route: SOAP over port 49000
with digest authentication. On connecting, Auspex reads every SCPD file and
works out what the router can do, instead of keeping a list that would be out
of date the day it was written. On a Fritz!Box 5690 Pro this yields 39
services with 468 actions.

**The web interface** (`FritzWebClient.cs`) covers what TR-064 does not offer,
most importantly the local DNS server the box hands out over DHCP. For those
settings Auspex signs in the way a browser does, using two-stage
PBKDF2-SHA256, and posts the complete form back. This is fragile and is marked
as such in the code.

`RouterAdmin.cs` provides a shorter path alongside the general one: the
handful of operations needed day to day, under names that describe what they
do rather than repeating SOAP vocabulary.

### The extension — `Services/Extension/` and `extension/`

The extension has a deliberately narrow scope: it can create exceptions for
the device the request came from, and for no other. The extension does not get
to name that device; Auspex determines it from the sender address of the
request, resolved through `/api/v1/who`. A stolen token therefore cannot be
used to change another device's rules.

The browser side under `extension/` shares one source tree in `shared/`, from
which `build.sh` produces the packages for Chrome (MV3 service worker) and
Firefox (background scripts). The dashboard can build the same package on
request through `ExtensionPackage.cs`, so you do not need a copy of the
repository on the machine you are working from.

### The sensor — `sensor/` and `Services/Extension/SensorApi.cs`

The sensor is optional, runs on Windows and has to be installed deliberately.
It reports which program on that machine holds which connection, so the pages
about destinations can name what sent something instead of only recording that
something was sent. It sees TCP connections only: Windows does not record a
remote address for UDP, which means traffic over QUIC stays invisible. The
pages say so rather than presenting their figures as complete.

Nothing depends on it being installed. `Services/Prerequisites.cs` reports
per part whether it is **active**, **idle** or **missing**, and the settings
page says which is which. The sensor's state comes from the data (has
anything been reported in the last 24 hours?) and not from the
configuration: derived state that gets stored goes stale.

## What is stored where

| Place | Content | Survives a restart |
|---|---|---|
| Memory (Go) | Rules, cache, query ring buffer | no |
| `analytics.db` | Queries, findings, daily totals, exceptions, router inventory, destinations, connections | yes |
| `router.json` | Credentials, encrypted with Data Protection | yes |
| `extension.json` | The extension's token | yes |
| `darstellung.json` | Appearance, language and display time zone | yes |
| `auspex-shared/devices.json` | Device names, from the control plane to the resolver | yes |

## Looking things up

- [`../README.md`](../README.md) — what it is and how to run it
- [`../README.de.md`](../README.de.md) — the same in German
- [`open-points.md`](open-points.md) — what is coming, and what deliberately is not
- [`../extension/README.md`](../extension/README.md) — the extension
- [`../sensor/README.md`](../sensor/README.md) — the Windows sensor
- [`../SECURITY.md`](../SECURITY.md) — the security model and its limits
- [`blueprints/INDEX.md`](blueprints/INDEX.md) — per-feature blueprints
