# Auspex in detail

What the tool can do, how it is built, what has been measured and what is
deliberately absent. The [README](../README.md) answers *what and why* and
gets you running; everything else is described here.

- [Router](#router)
- [Architecture](#architecture)
- [What Auspex can do](#what-auspex-can-do)
- [Persistence and analysis](#persistence-and-analysis)
- [Where the traffic goes](#where-the-traffic-goes)
- [Notifying Whiskers](#notifying-whiskers)
- [Measured](#measured)
- [Sign-in](#sign-in)
- [Running in containers](#running-in-containers)
- [Managing lists](#managing-lists)
- [Device profiles in the browser](#device-profiles-in-the-browser)
- [Impact analysis](#impact-analysis)
- [Backup](#backup)
- [The control API](#the-control-api)
- [State of play](#state-of-play)
- [Where Auspex stands](#where-auspex-stands)
- [What is deliberately not built](#what-is-deliberately-not-built)
- [Resilience](#resilience)
- [Operational notes](#operational-notes)

---

## Router

With a router account stored, a section of its own is added: on connecting,
Auspex reads the device description and every SCPD file and derives from them
what the router can do. On a Fritz!Box 5690 Pro that is 39 services with 468
actions, 212 of them changing.

Deliberately discovered rather than hand-written: maintaining 468 calls by
hand would be out of date the moment it was finished. This way Auspex also
covers a model that did not exist while it was being developed, and a new
firmware brings new actions along by itself. The input fields in the
catalogue come from the permitted values, types and limits stated in the
description.

Seven pages: overview; devices, which shows the router's inventory using the
MAC address as a stable identifier rather than the changing source IP of the
query log; Wi-Fi,
port mappings, IPv4, the event log, and the complete catalogue.

**Without a stored account the section does not appear**, neither in the
navigation nor under its address. That is not a security fence but tidiness:
an interface that shows buttons which cannot do anything trains people to
click things away.

Two things there have to be taken seriously. A Fritz!Box knows **no
fine-grained rights per service.** An account with "FRITZ!Box settings" can
do everything the menu can. Up to that point Auspex can only refuse answers;
past it, it holds the network's configuration in its hand. Whoever wants to
watch first sets `ROUTER_READONLY=true`: the complete catalogue stays
visible, but nothing can be triggered. And actions that can cut off access to
the router itself, such as switching off Wi-Fi or DHCP or changing the LAN
IP,
demand an additional confirmation.

Anything signed in goes over TLS on port 49443. Digest sign-in protects the
password, not the content, and the content would hold Wi-Fi keys and the
inventory of the network.

The device list comes from a Fritz!Box without a sign-in,
so a wrongly stored account falls back to the open route for read actions, in
order that the list still stands.

## Architecture

Deliberately split, because the languages have different strengths:

```
                 ┌──────────────────────────┐
   Clients ─DNS──▶  auspex (Go)             │   data plane
                 │  UDP/TCP · filter · cache│   miekg/dns, one binary,
                 │  DoH/DoT upstreams       │   no runtime underneath
                 └───────────┬──────────────┘
                             │ HTTP/JSON (control API)
                 ┌───────────▼──────────────┐
                 │  Auspex.Control (.NET 10)│   control plane
                 │  Blazor · EF Core        │   persistence, analysis,
                 │  ingest · detectors      │   anomaly detection
                 └───────────┬──────────────┘
                             │
                        ┌────▼─────┐
                        │  SQLite  │   query history, findings
                        └──────────┘
```

The resolver runs on its own. If the dashboard goes down, Auspex keeps
filtering. The control plane observes; it is not a precondition.

## What Auspex can do

**Rule formats.** Hosts files, bare domain lists, AdBlock syntax
(`||domain^`, `@@||domain^`) and wildcards (`*.domain`). Element and cosmetic
filters are counted and skipped, because they need HTTP context that a DNS
resolver does not have.

The matching semantics are deliberately different, because the formats mean
different things:

| Rule | matches `x.example` | matches `sub.x.example` |
|---|---|---|
| `0.0.0.0 x.example` (hosts) | yes | **no** |
| `x.example` (bare domain) | yes | yes |
| `\|\|x.example^` (AdBlock) | yes | yes |
| `*.x.example` (wildcard) | **no** | yes |

Exceptions (`@@`) always beat block rules, across lists as well. Patterns
that appear on both sides are reported as a conflict at startup.

**CNAME cloaking.** The most widespread trick against DNS filters: the site
creates a subdomain of its own, such as `metrics.newspaper.example`, and has
it
point at the tracker over CNAME. The first-party subdomain is on no
blocklist, so the tracker gets through although its target would be blocked.

Auspex therefore checks the CNAME chain of every answer against the same rule
set as the name that was asked for, using the same profile, so that client
exceptions and time windows apply here too. Demonstrated live: `www.otto.de`
is on no list but points over CNAME at `www.otto.akadns.net`; block that
target and the first-party domain is blocked, and the query log says why.

The answer is still cached, since it is valid and the check runs again on the
next hit. Without that the first query would be blocked and every following
one would run through unfiltered.

Switchable through `check_cname`: an over-aggressive list can take
first parties with it this way. So the triggering target appears in the query
log and in the dashboard. Without it, a block on a harmless-looking domain
would be inexplicable. `auspex_blocked_cname_total` shows how much the check
catches.

**Browsers going round the filter.** Firefox switches on its own encrypted
resolution and ignores the resolver on the network, so the filter applies
to nothing that happens in that browser, and nobody notices.

Against that, `use-application-dns.net` is answered with NXDOMAIN; Firefox
reads that as "the network filters" and leaves DoH off. NXDOMAIN necessarily,
regardless of the configured block mode, because a blocked `0.0.0.0` is read
by
Firefox as "no filter" and it carries on past.

In addition `block_services: ["doh-anbieter"]` blocks the endpoints of the
known public DoH providers, so that a manually switched browser falls back
too. **Our own upstream is not affected**, although `dns.quad9.net` is on
that list: Auspex resolves its hostname through the bootstrap resolver, not
through its own filter. Verified on a running system: `dns.quad9.net` is
blocked for
clients, and Auspex keeps resolving through it.

**The origin of every decision.** Every rule carries its list and line
number. Query log and `/api/v1/explain` therefore answer "why was this
blocked?" rather than only "it was blocked".

**Cache warming from history.** Prefetch only renews what is hot right now.
Two gaps remain: after a restart the cache is empty, and a name asked for
three times a day never reaches the prefetch threshold within one TTL.

The control plane knows from the stored history which names the network
really needs and hands them to the resolver, on a schedule and immediately
when it notices the resolver has restarted. Blocked names are skipped, and
the queries appear neither in the query log nor in the learning store:
otherwise the analysis the list comes from would be feeding itself.

Live: after a restart 160 names were prefetched, after which `www.golem.de`
answered in 0.6 ms instead of around 100 ms.

**Cache.** TTL-correct (the remaining time, not the original value), negative
caching per RFC 2308 through the SOA minimum TTL, LRU eviction, prefetch for
hot entries before expiry, and `serve_stale` when all upstreams are dead.
SERVFAIL is not cached; the DO bit is part of the cache key.

**Upstreams.** Plain UDP/TCP, DoT and DoH (RFC 8484). Either `failover` or
`race`. Whatever delivers repeated errors lands on the bench for a while. A
bootstrap resolver of its own resolves the hostnames of the DoH and DoT
targets,
otherwise Auspex asks the system at startup, and after setup that points at
Auspex itself.

**Client profiles and time windows.** Rules per IP/CIDR, plus schedules
(night quiet, homework time, focus time) including windows across midnight.
This is the part pure blocklists structurally cannot do.

**Names for devices behind a tunnel.** The router answers reverse lookups for
its own network and knows nothing beyond it. A device reached over Tailscale
arrives with an address from `100.64.0.0/10` that the router has never seen, so
it would stay nameless permanently. Waiting does not help, because there is
nothing for the router to learn. Tailscale's own resolver, however, does answer
reverse lookups for those addresses.

```yaml
hosts:
  via: "192.168.1.1"
  reverse_via:
    "100.64.0.0/10": "100.100.100.100"
```

The longest matching prefix wins, so a narrow range can be routed differently
from a wider one that contains it. If an address matches no range and no
general route is configured, Auspex sends no query at all: asking the router
about a tunnel address would only produce a timeout for every lookup. A
malformed range makes the resolver refuse to start, rather than being ignored
silently.

**Which program asks for which domain.** Auspex records which name produced
which address. The Windows sensor records which program connected to which
address. On its own, neither of those answers the question people actually
have. Joined over the address, they do, and the **Programs** page shows the
result: Chrome talked to forty tracking domains, the vacuum cleaner to three
endpoints abroad.

Addresses that no lookup accounts for are counted separately rather than
dropped. On that page they are the most interesting number, because they
represent traffic that went around the resolver. The same gap feeds a detector
of its own, `unerklaerte-verbindung`: if the sensor saw connections to three or
more addresses that no lookup explains, the filter was never asked. The usual
reasons are a browser using its own DNS-over-HTTPS, addresses compiled into a
program, or an app that ships its own resolver.

Pi-hole and AdGuard Home cannot answer this question at all. They only see the
queries that reach them, and a query that was never sent leaves no trace there.
Answering it requires a second, independent source of information, and the
sensor is that source.

Its limits are stated on the page itself: the sensor runs on Windows only and
reads TCP connections only, so it says nothing about phones and nothing about
traffic over QUIC.

**Quarantine.** Any finding that names a device offers a button to quarantine
it. The button sets the device profile to the `quarantine` policy, which blocks
every lookup for that device no matter what it has learned before. Explicit
allow rules still work, so the device can reach whatever it needs in order to
be repaired.

Three things about how this works were decided deliberately.

*It has to be clicked.* Auspex never quarantines a device by itself. False
positives do happen; there is a detector whose whole job is finding them. An
automatic quarantine would take a device off the network in the middle of the
night with nobody there to notice.

*It acts on DNS, not on the router.* This keeps Auspex in control of the block
and able to release it again. Cutting a device off at the router is possible
too, but stays a separate step you take on purpose: that block lives in the
Fritz!Box and would remain in place even if Auspex stopped running.

*It expires on its own* after an hour. A block that can only be released by a
program that might crash is a trap rather than a safety measure. The expiry
also runs at startup, so anything whose hour ran out while the control plane
was down is released as soon as it comes back.

Before changing the profile, Auspex records which policy the device had.
Without that, lifting the quarantine would set the device to `open` and
silently discard a learn mode that may have taken two weeks to build up.

**IoT learning mode.** Deny by default for devices you do not trust, set up in
three steps per client profile:

```yaml
clients:
  - name: "iot"
    match: ["10.0.5.0/24"]
    policy: "learn"      # 1. observe
```

```bash
auspex -config config.yaml -learn-export iot   # 2. check the allowlist
```

```yaml
    policy: "enforce"    # 3. everything else is shut
```

Three decisions that make the mode usable at all:

- **Only what the filter let through gets learned.** Otherwise the tracker
  that happened to be asked for during the learning window wanders
  permanently into the allowlist, and the whole exercise would be back to
  front.
- **Granularity `domain` through the public suffix list.**
  `cdn-3f8a.vendor.example` is `cdn-91cc.vendor.example` tomorrow; without
  generalising, the allowlist would be broken on day two. Without the public
  suffix list the naive rule "the last two labels" would let a whole country
  TLD through on `foo.co.uk`. Whoever wants it stricter takes `exact`.
- **`max_entries` as a cap.** A device generating random names, whether
  because it is broken or because it is tunnelling, must not be able to flood
  the allowlist. When the cap is reached the
  store reports `overflow` rather than being silently incomplete.

Reverse lookups (`in-addr.arpa`, `ip6.arpa`) are neither learned nor blocked:
they do not belong to the question "which services does this device talk to",
and blocking them turns every diagnosis into a guessing game.

The dashboard shows per profile how long it has been since a new domain was
added. That is the most usable signal that a learning window ran long
enough.

**Encrypted in both directions.** Auspex not only speaks DoH/DoT upwards but
serves it downwards as well: DNS-over-TLS (usually port 853) and
DNS-over-HTTPS per RFC 8484, both methods (POST and `?dns=` as base64url).
That way a device runs through the filter outside the home network too,
without a VPN.

Without a certificate the HTTPS listener speaks plain text only, which is
meant for
running behind a reverse proxy. Its networks then have to be under
`trusted_proxies`, or every query arrives with the proxy's address. But the
list must not be drawn too widely: whoever you believe may invent any origin
they like through `X-Forwarded-For` and thereby hit other people's profiles
and learning stores. Both cases are tested.

**Device names instead of addresses.** Two sources: a fixed mapping in the
configuration, and a reverse lookup against the router. A Fritz!Box answers
PTR for its DHCP clients and thereby delivers exactly the names from the home
network menu. The name runs through everything: query log, statistics,
findings and all the way into the alarm message. "Suspected tunnelling on
living room TV" is usable; an IP address forces you to look it up.

The resolution never runs in the query path: `Name()` always answers
immediately from memory and at most kicks the lookup off in the background.
If anonymisation is switched on in the query log, the name is dropped with the
address, since it
identifies the device just as uniquely as the address.

**DNSSEC, without validating in-house.** Auspex does
not check the signature chain itself: validation of your own is
security-critical code you do not get right on the side, and implemented
wrongly it is worse than none. Two things Auspex does instead:

- **The client cannot switch validation off.** If a device sets the CD bit
  ("please do not validate"), that is not passed on. Without this, any device
  on the network could take itself out of the protection.
- **Making visible what applies.** The query upwards carries AD=1 so that the
  answer reveals whether it was validated (RFC 6840, without asking for the
  signatures themselves). That lands in the query log and as a rate in the
  dashboard. Whoever did not ask for it still does not get the AD bit set in
  their answer, because otherwise the answer would imply a guarantee nobody
  asked for.

Checked live: `cloudflare.com` and `internetsociety.org` come back validated;
`dnssec-failed.org` with a deliberately broken signature is rejected by the
upstream (SERVFAIL). Whoever wants local validation puts a validating
resolver such as Unbound in front as the upstream. The same display then
applies.

For the record, because this document used to say otherwise: of the two
obvious comparisons only Pi-hole validates in-house, through the dnsmasq it
carries with it, and only once `dnssec=true` is switched on. AdGuard Home
reads the upstream's AD bit and passes it on, which is exactly what happens
here.
The difference to Auspex is smaller than it was claimed to be, and it was
claimed in the competitor's favour.

**Service catalogue.** Whoever wants "TikTok on the child's tablet from 9 pm"
should not have to research which domains belong to it. 32 common services
are on file and can be blocked per profile or per time window:

```yaml
  - name: "kids-tablet"
    match: ["192.168.1.50"]
    block_services: ["onlyfans", "tinder"]
    schedules:
      - name: "night"
        from: "21:00"
        to: "07:00"
        block_services: ["youtube", "tiktok", "roblox"]
```

Internally these become ordinary block rules, after which there is
no special case anywhere else in the system. A typo in a service name makes
the start fail rather than ending up as a silently permitted service. The
catalogue is a curated selection; whatever is missing belongs in the
configuration as an ordinary rule.

**SafeSearch, per profile and per time window.** Every large search engine
runs a second host that answers with filtered results. Auspex sends the
device there:

```yaml
  - name: "kids-tablet"
    match: ["192.168.1.50"]
    safe_search: ["google", "youtube-strict", "duckduckgo"]
    schedules:
      - name: "homework"
        days: ["weekdays"]
        from: "16:00"
        to: "18:00"
        safe_search: ["bing"]
```

The filtering itself is done by the search provider. Auspex does not inspect
traffic and does not rewrite any content; it only decides which of the
provider's hosts the device is sent to. The query log records this as
"rewritten · filtered search", together with the target.

**Why this belongs to the profile rather than the network.** A household is
rarely of one mind about this. The children's tablet should get filtered
results, while the workshop computer needs to search for a drill bit without
the image search deciding otherwise. A single switch for the whole network
forces the strictest setting on everyone, and the usual outcome is that
somebody eventually turns it off for everyone.

AdGuard Home offers SafeSearch per client. The time window on top of that is
specific to Auspex, and it is what makes a rule like "filtered while the
children are awake" possible. Pi-hole has no SafeSearch at all.

**What it cannot do.** These limits belong here rather than in a footnote:

- It only affects the search engines in the catalogue: Google, including all
  of its roughly 190 country domains, YouTube in moderate and strict mode,
  Bing, DuckDuckGo, Yandex and Pixabay.
- It only works while the device actually asks Auspex. A browser with its own
  DNS-over-HTTPS bypasses it. The canary domain and the blocked DoH endpoints
  exist to make that harder.
- Nothing prevents someone from typing the address of a search engine that is
  not in the catalogue.

In other words, this is a speed bump rather than a lock, and it is offered as
one.

Three implementation details decide whether it works in practice:

- **The answer contains an address, not just a CNAME.** A stub resolver does
  not follow the chain itself, so an answer containing only a CNAME means the
  page does not load. Auspex resolves the target and returns both records
  together.
- **HTTPS records (type 65) are redirected as well.** These carry Encrypted
  Client Hello and alternative addresses; left alone, a browser could use them
  to reach the very host whose A record was just redirected. MX, NS and other
  record types pass through unchanged, since they have nothing to do with the
  search page.
- **A block takes precedence over the redirect.** If you blocked YouTube
  outright, you meant blocked, not "moderately filtered", so the filter runs
  first.

**Split-horizon DNS.** `*.home.example.com → 192.168.1.10`, so that internal
services are reachable under real names and real certificates.

**Block modes.** `nxdomain` (the default, with an SOA in the authority
section so that clients cache negatively), `zeroip`, `refused`, `custom`. For
types with no sensible substitute address, such as MX and TXT, NODATA is
delivered
rather than an invented answer.

## Persistence and analysis

The data plane's ring buffer holds minutes. The question "what is this device
actually doing all the time" needs weeks of data, and that lives in the
control
plane.

**Ingest.** A background service fetches everything after the cursor
(`/api/v1/querylog/stream?since=N`) every few seconds and writes it to
SQLite. Three things that can go wrong, and what is done about them:

- **The data plane restarts.** The sequence begins at 1 again, an old cursor
  would be too high and would skip everything. So every instance has a boot
  id; when it changes, the cursor is reset. A unique index on (boot, seq)
  prevents duplicates.
- **The collector is too slow.** If the ring buffer overflows, the answer
  reports `lost` with the number of entries lost rather than concealing the
  gap. If the counter stays above zero, `querylog.size` belongs raised or the
  poll interval shortened.
- **The data plane is gone.** The service logs it and carries on. A network
  hiccup must not leave a permanent gap.

Grouping is by registrable domain rather than by hostname. Otherwise every CDN
name
counts on its own. The Go side computes it along the way, because the public
suffix list already lives there.

**Detectors.** Nine heuristics run over a sliding one-hour window (the steady
talker over the day). Every finding names the numbers it rests on, because an
alarm
you cannot recompute gets ignored after the third one:

| Detector | Fires at | Typical false alarms |
|---|---|---|
| `neue-domain` | A domain this device has never asked for (from 5 queries) | a new app, a new CDN |
| `nxdomain-flut` | ≥ 40 % of queries running into nothing at ≥ 50 queries | a wrong search domain, a broken app |
| `wiederholungssturm` | ≥ 100 queries and ≥ 5× above its own baseline | an update loop, a hanging app |
| `dauersender` | A lot, evenly, for days against a block | telemetry that is deliberately blocked |
| `tunneling-verdacht` | ≥ 50 distinct names under one domain with labels ≥ 30 characters | antivirus and CDN services with encoded names |
| `fehlalarm-verdacht` | ≥ 8 blocked queries to the same domain within 5 minutes | a deliberately blocked domain an app keeps trying |
| `gleichlauf` | ≥ 3 devices discovering the same domain, new to all of them, within 15 minutes | an app update, a new CDN of a widespread service |
| `portfreigabe` | A port mapping on the router that nobody here opened | a game console using UPnP |
| `neues-geraet` | A device seen on the network for the first time | a visitor's phone |

**Synchrony** (`gleichlauf`) is the view tools with per-device analysis
cannot have at all. Taken on its own, each of these queries is unremarkable;
it
is only the fact that several devices discover the same domain, unknown to
all of them, at the same time that makes them interesting. Usually it is an
update, sometimes something spreading. If even one of the devices already
knew the domain, it counts as everyday traffic and is not reported.

**False-alarm detection** is the detector that saves the most annoyance. The
commonest reason people switch DNS filters off again is silent breakage:
something does not work and nobody connects it with the filter. The
difference between "adverts while browsing" and "the app is hanging" is
density: eight calls spread over an hour are normal, eight in five minutes
are a repetition loop.

**Steady talkers** (`dauersender`) close that detector's blind spot.
`wiederholungssturm` compares against a device's own history and sees only
spikes; a device that has been running against a block equally loudly for
days produces no spike, so its factor is one. It stayed invisible although it
caused most
of the load. Measured for real: 486 queries for one blocked name in 46
minutes, not a single finding.

It works without a finding too: in the **query log** every row has a button
that blocks or allows the name. Deliberately the name and not the registrable
domain, because somebody who wants to get rid of a single telemetry address
does not want
to block the whole provider.

Every such finding brings the matching exception with it, and as narrow as
possible: if only one name was affected, only that one is allowed. An
exception on the whole registrable domain would open up the complete provider
because of a single telemetry host. One click writes it: the control plane
appends it to a shared file that the resolver reads as a list with
`allow: true`, and triggers a reload. No additional API route is needed; the
list mechanism can already do it. Writing and reloading are reported
separately: if the rule is in the file while the resolver happens to be
unreachable, it applies at the next reload, and the interface says exactly
that.

Two detectors need a baseline and stay silent until there is enough history
(`BaselineWarmup`, two days by default). Without that every domain would be
"new" in the first few hours and every finding therefore worthless. Within
one hour exactly one entry comes into being per detector, client and subject
It grows with the evidence rather than repeating itself every five minutes.

**Roll-up.** As soon as a day is complete it is rolled up once into daily
totals: overall numbers, per device and per domain. After that the raw data
may go without the history being lost: the analysis page additionally offers
"the last 90 days" and "the last year" from that store.

The roll-up happens when the day closes rather than shortly before deletion,
so the
moment does not hang off the retention setting, and a shortened retention
tears no gap. The two sources stay separate and labelled in the interface:
different resolution, and a view that switched silently between them would
not be traceable.

Configuration in `appsettings.json` under `Analytics`: connection, poll
interval, batch size, retention (raw data 90 days by default, daily totals
730), detection interval, warm-up period.

## Where the traffic goes

DNS says which name was asked for. It does not say who is behind the address,
and it says nothing at all about which program on a machine was asking. Two
additions close both:

**Destinations.** For every answered query the resolver also delivers what
the name pointed at. From that the control plane keeps one row per address
and one per name-and-address pair, rather than one per query, which at
around
140,000 queries a day is the difference between a table you can analyse and
one that merely grows. Operator and country come from a range database that
is refreshed in the background; the city is marked as *uncertain* wherever it
names a node rather than a headquarters.

**The sensor**, optionally, on Windows. It reports which process holds which
connection, which is the one thing DNS cannot know. It is opt-in, reads TCP
only, and transmits no content and no
paths and no command lines. What it cannot see, the page says out loud: an
empty program column means "no sensor runs here", not "no program sent
anything".

The "where to?" page puts both together, and the most important figure stands
before the list of recipients: what never left the house at all. Without it
the list below reads as the device's entire behaviour.

## Notifying Whiskers

Whiskers alerts through **rules on container logs**. So it needs no second
notification route and no coupling to an API: Auspex.Control writes every new
finding as a recognisable line to stdout, and a log alarm rule picks it up.

```
AUSPEX-FUND [high] tunneling-verdacht client=10.0.5.20 subject=tunnel-test.example :: Suspected DNS tunnelling over tunnel-test.example :: 70 distinct names, 70 queries, longest label 41 characters
```

It is written as a single line without breaks, because log rules work line by
line and a wrapped finding
would match only halfway.

There are two ways to hang this off the alerting.

**Route 1: without a new rule** (`Notifications__EscalateHigh: "true"`). The
existing rule "real errors (filtered, AI trigger)" listens on *all*
containers and picks up `[ERROR]`, among other things. Hard findings get that
prefix and thereby land in the existing channel:

```
[ERROR] AUSPEX-FUND [high] tunneling-verdacht client=192.168.1.43 :: ...
```

Deliberately only `high`: the general alarm channel loses its value if every
new domain lands in it. That is the same approach as `check-cert-expiry.sh`
on BurgCloud, which likewise writes into the existing rule rather than
demanding a second channel.

**Route 2: a rule of its own.** More cleanly separated, but it has to be
created in the Whiskers UI: over MCP log alarms can only be read
(`list_log_alerts`), not created. The model is the existing rule "new QR
short link created":

| Field | Value |
|---|---|
| Pattern | `AUSPEX-FUND \[high\]` (the hard findings only) or `AUSPEX-FUND` (all) |
| Container | `auspex-control` |
| Severity | warning |

After that `EscalateHigh` belongs on `false`, or both rules alert.

Reporting happens at **warning** level, not error. A finding is not an
application error, and without escalation the error rule should not fire on
it, checked against 2058 lines of real container log from the test
installation: zero hits for that rule, two on `AUSPEX-FUND`. The escalation
is therefore a deliberate decision and not a side effect.

Controlled in `appsettings.json` under `Notifications`: on/off, marker,
minimum severity (`info` | `warn` | `high`, `warn` by default), a cap per
pass and a maximum age. The cap is not a detail: after a longer disturbance
hundreds of lines would otherwise go out at once and the alerting would be
useless for the rest of the day. Whatever runs over the cap is reported as
one collective line and still counts as handled.

Reporting is separate from detecting: every finding carries a timestamp for
when it went out. A crash between the two therefore does not make a finding
disappear silently, and the next pass catches up.

## Measured

On the test installation (4 cores), 2,296,816 rules loaded, the load tool on
the same machine, so the resolver would have more headroom on hardware of its
own:

| | Throughput | Median | 99th percentile |
|---|---|---|---|
| Cache hits, 100 concurrent | 46,000/s | 1.8 ms | 7.7 ms |
| Blocked, every name new, 100 concurrent | 44,000/s | 1.7 ms | 9.7 ms |
| Sustained load, 400,000 queries | 41,000/s | 1.8 ms | 10.5 ms |
| A single query, no competition | — | **0.13 ms** | 0.17 ms |

The full filter path against 2.3 million rules is practically as fast as a
cache hit. The rule lookup is a hash access plus a walk over the labels,
independent of the size of the list.

Under sustained load: 150–175 % CPU (of 400 % available), memory rises from
375 to 570 MB, because Go lets the heap grow under allocation pressure.
Whoever
wants a hard ceiling sets `GOMEMLIMIT`.

**On `strategy: "race"`:** it sounds like a free win, but it only is one if
the upstreams are similarly fast. Measured on the test installation: Quad9
over DoH against Cloudflare over DoT:

| | failover | race |
|---|---|---|
| Median | 12.5 ms | 11.8 ms |
| 95th percentile | 16.9 ms | 16.7 ms |
| 99th percentile | 56.3 ms | 71.6 ms |

Quad9 wins 295 of 300 races. So the second contributes nothing but costs
double the query load, and it lets both providers see every single query
instead of only one. For a tool whose whole reason is privacy that is a bad
trade. `race` is only worth it when two upstreams genuinely win in turn.

**Where it tips over:** at 41,000 queries/s the analysis loses data. The ring
buffer holds 10,000 entries and the ingest fetches every 5 seconds. In the
test 486,000 entries were missed and reported as exactly that. For home
traffic (a few queries per second) that is meaningless; whoever really runs
such loads needs a bigger ring buffer and a shorter interval.

```bash
cd auspex && go build -o auspexload ./cmd/auspexload
./auspexload -server 127.0.0.1:53 -n 20000 -c 100 -random -suffix example.com
```

## Sign-in

The dashboard can change filter lists and create exceptions. Left unprotected,
it
does not belong on the network. So it demands a sign-in by default.

Producing and entering a password hash:

```bash
dotnet run --project control/Auspex.Control -- --hash-password "yourPassword"
```

The hash goes under `Auth:PasswordHash`; for a container install, put it in
a
`.env` next to the `compose.yml`, because it differs per installation.
Separated with a colon rather than a dollar as in the usual PHC format: the
value ends up in .env files and YAML, and there the dollar sign is a variable
Docker Compose silently expands away. Old hashes in the dollar format stay
valid. (PBKDF2-SHA256, 210,000 rounds, a random salt per hash, compared in
constant time.) The algorithm and the round count are stored in the hash
itself,
otherwise it could not be moved to stronger parameters in two years' time.

If **no** password is configured, the application generates a random one at
startup and writes it to the log. That fails towards "shut" without locking
anybody out. A dashboard left open without configuration would be the
worse default.

`Auth:Enabled: false` switches the sign-in off. Only sensible when something
else authenticates in front of it (forward auth through Authentik, say); the
application warns at startup in that case.

## Running in containers

```bash
docker compose up -d --build
```

`network_mode: host` in the `compose.yml` is a condition, not a convenience:
on a bridge network every query arrives with the Docker gateway's IP. Client
profiles, learning mode and the entire per-device analysis would be worthless
with it, because there would be only one single "client". In the bridge test
`client=172.18.0.1` duly stood under every finding.

Port 53 without root works through `cap_net_bind_service` on the binary plus
`cap_add: NET_BIND_SERVICE`. Both containers log with rotation (10 MB, 3
files). Without it the log file eventually grows so large that every query
against it runs into a timeout.

The time zone comes from `TZ` in the compose file and can be overridden per
installation in the dashboard under **Settings → Time zone**. It decides
which clock time appears on an event and whether a finding counts as
happening at night, which is why it sits with the settings rather than with
the
colours.

## Managing lists

Filter lists can be added, switched off and on again and removed in the
dashboard, with a catalogue of proven lists so that nobody has to hunt for
URLs.
The resolver loads a new list immediately and rebuilds the rule set; in the
test: adding brings 17,316 rules, switching off takes them away, switching
back on comes straight from the disk cache.

Two limits are drawn deliberately:

- **Lists from the configuration file stay untouched.** They belong to the
  operator. On a name clash the configuration wins, because otherwise a click
  in the
  browser could override a line in the file.
- **http(s) addresses only.** A file path would let the control plane write
  into the resolver's file system; local lists belong in the configuration.

## Device profiles in the browser

Under "Devices" profiles can be created, changed and removed: addresses or
networks, mode (open/learn/enforce) and blocked services by checkbox. Changes
take effect immediately, since the profiles sit behind a pointer and are
swapped
out while running.

Two limits as with the lists: profiles from the configuration file stay
untouched and win on a name clash. **Upstreams and listen addresses stay in
the file deliberately.** You can lock yourself out with those, and a browser
is the wrong place for a change after which the interface itself is no longer
reachable.

A profile from the browser is validated with the same function as one from
the file; unknown field names are rejected rather than silently ignored.

## Impact analysis

A rule can be computed against the stored history before it goes live, under
"Impact" in the dashboard:

> `||analytics.employer.example^`: **312 affected queries**, of which **312
> would be newly blocked**, spread over 1 device (work laptop).

The decisive figure is not "how many match" but "how many are decided
differently from today". A block rule on something already blocked changes
nothing; an exception only has an effect where things are blocked at present.
That difference is exactly what the analysis shows.

The same formats are understood as in the filter lists. The control plane's
parser mirrors the data plane's semantics, including the differences between
a hosts entry, a bare domain and a wildcard. A rule that were read
differently here than there would be worse than no analysis; so both parsers
have the same test cases.

## Backup

Under "Backup" in the dashboard: a ZIP with history, findings, daily totals,
own rules, managed lists and learned state. The learned state and the lists
live in the resolver and are fetched through its API, which is why the
backup is not a mere copying of files.

The database is written out consistently rather than copied raw: a file copy
would lose the part not yet checkpointed.

**Restoring merges, it does not replace.** Whoever restores after a loss
usually has a few hours of new data again; deleting that would be a second
loss. Duplicates are removed by the unique indexes,
restoring twice changes nothing.

If the backup comes from a different schema version it is rejected rather
than bent into shape. Paths inside the archive are checked before anything is
extracted.

## The control API

By default `127.0.0.1:5380`. As soon as it listens on more than loopback, a
`token` belongs in the configuration (bearer auth, compared in constant
time).

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/status` | counters, cache, rule statistics, upstream health |
| `GET /api/v1/querylog?limit=N` | the last queries including the triggering rule |
| `GET /api/v1/querylog/stream?since=N` | cursor fetch for the ingest, oldest first |
| `GET /api/v1/explain?domain=…&client=…` | the filter decision without a real query |
| `GET /api/v1/who?ip=…` | which device is behind an address |
| `GET /api/v1/upstreams` | upstream health only |
| `POST /api/v1/reload?force=true` | re-read the lists |
| `POST /api/v1/cache/purge` | empty the cache |
| `POST /api/v1/cache/forget?name=…` | drop one name from the cache |
| `GET /api/v1/learn` | learning profiles with their figures |
| `GET /api/v1/learn/{profile}` | the observed names |
| `GET /api/v1/learn/{profile}/allowlist` | finished rules, `granularity=domain\|exact` |
| `POST /api/v1/learn/{profile}/reset` | discard the learned state |
| `POST /api/v1/learn/{profile}/forget?name=…` | remove a single name |
| `GET /metrics` | Prometheus text format |
| `GET /healthz` | health check, always without a token |

`SIGHUP` reloads the rule set as well, without a restart.

**Metrics.** `/metrics` delivers queries, blocks, cache, the DNSSEC rate,
rules per list and the health of every upstream in Prometheus format,
including `auspex_upstream_benched`, from which a failed target can be read
before anybody notices the slower answers. If a token is set it applies here
too; Prometheus and VictoriaMetrics can do `bearer_token` in the scrape
configuration.

## State of play

Tested and running: resolution over DoH, blocking with all four rule formats,
exceptions, wildcards, rewrites, cache hits, TCP and UDP, the control API,
the dashboard against the running data plane.

Persistence and detection have been played through live: ingest over the
cursor, a restart of the data plane (cursor reset, 45 rows, zero duplicates),
analysis over the stored history, and a simulated tunnel of 130 encoded
names that `tunneling-verdacht` and `nxdomain-flut` reported independently of
each other.

The learning cycle has been played through as a whole: learning (blocked
names demonstrably do *not* land in the store), a restart with the state
loaded, export, switching to enforce, blocking for unlearned names.
`raw.githubusercontent.com` drops out in the process although `github.com`
was learned, since it is a different registrable domain.

Test coverage sits on the logic where mistakes hurt: rule parsing and
matching semantics, TTL computation, negative caching, LRU, time windows
across midnight, profile mapping by CIDR, block modes, learning mode
including the public-suffix edge case and the overflow cap, the cursor logic
including ring-buffer overflow, and every detector with one case that should
fire and one that must not.

```bash
cd auspex && go test ./...
```

```bash
cd control && dotnet test
```

The .NET tests run against real SQLite in memory, not against an in-memory
provider. The detectors consist almost entirely of LINQ, and a fake without
real SQL
would check precisely not what matters. Two queries that could not be
translated to SQL only came to light through that.

## Where Auspex stands

Against Pi-hole and AdGuard Home:

| | Auspex | Pi-hole | AdGuard Home |
|---|---|---|---|
| Filtering, lists, exceptions | ✅ | ✅ | ✅ |
| DoH/DoT as an upstream | ✅ | through an extra service | ✅ |
| DoH/DoT for clients | ✅ | ✗ | ✅ |
| Client names | ✅ | ✅ | ✅ |
| Time-controlled filtering | ✅ | ✗ | ✅ |
| Service catalogue | ✅ | ✗ | ✅ |
| SafeSearch | ✅ per profile and time window | ✗ | ✅ per client |
| List management in the browser | ✅ | ✅ | ✅ |
| Device profiles in the browser | ✅ | ✅ | ✅ |
| Sign-in | ✅ | ✅ | ✅ |
| Backup in the browser | ✅ | ✗ | ✗ |
| Metrics for Prometheus | ✅ | through an extra service | ✗ |
| DNSSEC | enforced at the upstream | validates itself (off by default) | enforced at the upstream |
| DHCP server | ✗ (deliberately) | ✅ | ✅ |
| **IoT learning mode** | ✅ | ✗ | ✗ |
| **Anomaly detection** | ✅ | ✗ | ✗ |
| **False-alarm detection** | ✅ | ✗ | ✗ |
| **Impact analysis** | ✅ | ✗ | ✗ |
| **Alerting outwards** | ✅ | ✗ | ✗ |
| **Synchrony across devices** | ✅ | ✗ | ✗ |
| **Long-term daily totals** | ✅ | partly | ✗ |
| **Router as part of the tool** | ✅ | ✗ | ✗ |
| **Which program is talking** | ✅ (sensor) | ✗ | ✗ |
| **Which program asks for which domain** | ✅ (sensor + resolver) | ✗ | ✗ |
| **Traffic that bypassed the resolver** | ✅ (sensor) | ✗ structurally | ✗ structurally |
| **Quarantine from a finding** | ✅ time limited | ✗ | ✗ |

Open points and deliberate omissions are in
[docs/open-points.md](open-points.md).

## What is deliberately not built

- **A DHCP server.** A second DHCP server on the same network is one of the
  few changes that can take a whole household off the air. Auspex reaches the
  goals behind it differently: names over reverse lookup at the router, DNS
  enforcement over the router's DHCP entry. The objection and the conditions
  under which it could still be built are in
  [open-points.md](open-points.md#12-dhcp--the-objection-is-answered-a-second-one-stands).
- **Our own DNSSEC validation.** Security-critical code you do not get right
  on the side; implemented wrongly it is worse than none. Instead: enforced
  validation at the upstream and a visible status. Whoever wants to validate
  locally puts Unbound in front.
- **PostgreSQL.** SQLite carries a home network effortlessly; two providers
  would have meant two migration paths.
- **Upstreams and listen addresses in the browser.** You can lock yourself
  out with those, and the browser is the wrong place for a change after
  which
  the interface itself is no longer reachable.
- **gRPC.** The control API is HTTP/JSON. gRPC is only worth it once the
  control plane needs streaming instead of polling.
- **Regex rules** from AdGuard lists are skipped.
- **Detectors with fixed thresholds.** They are set rather than learned, and
  deliberately so
  that they stay quiet rather than nag. After a few weeks of real data they
  belong followed up.

## Resilience

Once the resolver is entered as the DNS server in the router, the whole
household's internet hangs off it. Two stages against that:

**Health checks.** Both containers report their state to Docker, and the
check is meaningful: it touches every component that has a lock of its own,
namely the rule set, the profiles, the cache and the query log, rather than
only confirming that "the
process is alive". Deliberately without an upstream: a real query would
trigger a restart on a slow upstream although the resolver is fine, and
against a hanging upstream a restart does not help anyway.

**A second instance.** A Fritz!Box takes two local DNS servers. The earlier
advice not to enter a second one applied to an unfiltered second entry,
two Auspex instances are exactly right and survive a reboot of one of them.

Three things you have to know about that:

- **Keep the configuration in sync.** Otherwise the two filter differently,
  and which one a device happens to ask is chance.
- **Learning mode does not take it without further ado.** Each instance
  learns for itself; in `enforce` mode a device would be let through or
  blocked depending on which instance it asked. Either let only one instance
  enforce, or align the learned states through the backup.
- **The control plane runs once.** It analyses the resolver it hangs off; the
  second instance stays invisible in the analysis.

## Operational notes

- **Do not bind to `0.0.0.0`.** `listen.udp`/`listen.tcp` take an address or
  a list. Bind deliberately rather than wholesale: if the host has a global
  IPv6 address (the normal case on German connections, behind a Fritz!Box
  too), a wildcard bind turns the resolver into an open resolver, and that is
  a tool for DNS amplification attacks. So the default binds to loopback
  only; the LAN and Tailscale addresses belong explicitly in the
  configuration.
- Port 53 on Linux needs either root or
  `setcap cap_net_bind_service=+ep`. On Windows port 5353 collides with mDNS
  so use a high port for tests.
- A listener that does not start terminates the process on purpose. A
  resolver that only answers TCP any more looks healthy in the log and does
  not work in practice.
- **An address that is not always there gets `optional: true`.** A VPN
  interface comes up on its own schedule, and the container can be faster
  than the tunnel. Without the marking the whole household briefly loses DNS
  because Tailscale was three seconds late:

  ```yaml
  udp:
    - "192.168.1.61:53"
    - address: "100.64.0.5:53"
      optional: true
  ```

  Optional does not mean "give up quietly". The address is retried in the
  background (2 s, doubling to a minute) until it appears, and the log says
  so. That distinction is the whole point: a crash heals itself through the
  restart policy, a silently missing listener does not, so an optional
  listener without a retry would be *worse* than the fatal one it replaces.

  At least one address has to stay required. A configuration in which every
  listener may fail is refused at startup, because it would let Auspex come
  up,
  bind nothing, report itself healthy and answer no queries at all.
- Do **not** enter a second DNS server in the router unless it is a second
  Auspex. Clients otherwise use both in turn and the filter applies at
  random.
- DNS rebind protection in the router blocks rewrites to private addresses,
  exempt your own domain there.

---
