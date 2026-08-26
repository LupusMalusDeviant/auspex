# Open points

As of 25 August 2026. Sorted by usefulness, with the reason each is still
open — not as a wish list, but so that nobody later has to guess whether
something was forgotten or was a decision.

## 1. A certificate for DoH and DoT

DoH currently runs in the clear on loopback only. Without a certificate the
feature that sends a phone through the filter while it is out of the house is
unusable — which is exactly why it was built.

Two routes: a certificate directly in `listen.cert_file`/`key_file`, or the
nginx-proxy-manager that is running anyway terminates TLS. In the second case
its networks have to be in `listen.trusted_proxies`, or every query arrives
with the proxy's address and the per-device analysis is worthless.

## ~~DoH bypass by browsers~~ — done

Done on 23 August 2026: the canary domain plus the catalogue entry
`doh-anbieter`. Original description:

Firefox switches DoH on to its own provider and ignores the resolver on the
network entirely. The remedy: answer the canary domain
`use-application-dns.net` with NXDOMAIN — Firefox reads that as "the network
filters" and leaves it alone — plus block the endpoints of the known DoH
providers, so that a manually switched browser falls back.

Would be one catalogue entry plus one rule, so not much work.

## ~~Cache warming from history~~ — done

Done on 23 August 2026. Original description:

The analysis knows which domains the network asks for constantly. Pulling
those in before the TTL expires — instead of from the third hit as now —
would raise the hit rate. None of the other tools can do that, because none
of them analyses history.

## 2. A device register with roles and quarantine

The biggest single idea so far, and the only one that brings a new capability
rather than improving an existing one: devices get a role, and a device
without a role resolves nothing until the operator lets it in. Signing on to
the Wi-Fi is then no longer enough.

**The limit first, so nobody overlooks it later:** DNS is not network access.
A device with a DHCP address talks to every other device on the LAN directly
over its IP, with mDNS, with SMB — none of which needs name resolution. It
gets outwards over hard-coded IPs or a resolver it enters itself. Real device
control would be 802.1X or MAC-bound VLANs; the Fritz!Box can do neither.
What would come into being here is a turnstile, not a wall: in practice a
television or a vacuum cleaner without name resolution is dead, but a device
that sets out to get past will.

**The one real gap is device identity.** Auspex today knows only the source
IP, and an IP is not an identity — DHCP hands it out again. A register needs
the MAC.

Measured against the Fritz!Box 5690 Pro on 23 August 2026, and the result
makes the most expensive part unnecessary: **TR-064 hands out the complete
device list without credentials.** 48 devices, 20 of them online, each with
MAC, IP, hostname, online status and connection type. No ARP tables to read
and no credentials to manage. A poller with 48 SOAP calls per pass is enough:

    POST http://192.168.1.1:49000/upnp/control/hosts
    SoapAction: urn:dslforum-org:service:Hosts:1#GetGenericHostEntry
    Body: <NewIndex>0..n</NewIndex>

`GetHostNumberOfEntries` gives the count. The bulk query
`X_AVM-DE_GetHostListPath` does demand a sign-in — the individual query by
index does not.

Device names already come from the Fritz!Box today, over reverse lookup
(`hosts.resolve`), and are live on the test installation. So the register
only adds the stable identifier, not the name.

**What the design hangs on:** of the 48 MACs, **11 are randomised** — phones
and tablets roll a private MAC per Wi-Fi. It is stable as long as the device
knows the network; after "forget network" the same device turns up with a new
MAC. So the approval flow has to be able to merge "known device, new MAC" in
one click, or your own phone lands in the waiting room after a network reset
and the whole thing annoys precisely the people who live in the house.

Built on top of that:

- **Roles instead of individual profiles** (`Kind`, `IoT`, `Guest`, `Full`,
  `Quarantine`). One rule set plus time windows per role, devices get
  assigned. Today's client profiles are the layer beneath.
- **Quarantine by default.** New MAC → role `unknown` → everything NXDOMAIN
  except a short allow list. Plus a finding "new device, assign a role" that
  arrives as a notification through the existing escalation.
- **Approval on a click**, the same shape as an exception from a finding
  today.
- **A "no purchases" category** in the service catalogue. Clean for dedicated
  payment and checkout hosts (PayPal, Klarna, Stripe checkout,
  `buy.itunes.apple.com`, Google Play billing). Not possible where the
  purchase runs over the same host as the app itself — Amazon, Steam, every
  in-app shop on the normal API. That would mean looking inside the TLS
  connection, and you do not want that at home. A speed bump, not a lock.

**If it is to have teeth:** couple the quarantine to the Fritz!Box. When
Auspex puts a device in quarantine, it cuts that device's internet access
over TR-064 at the same time. Then it is enforcement rather than a DNS brake
— Auspex the brain, the Fritz!Box the hand.

**A precondition that easily goes wrong:** Auspex has to see the devices
individually. There are two places in the Fritz!Box for a DNS server and only
one of them is any good. Under *Internet → Account information → DNS server*
the Fritz!Box would use Auspex as its own upstream — then every query arrives
with its address and device profiles are worthless, the same mistake as
`network_mode: bridge`. The right one is *Home network → Network → Network
settings*, where the Fritz!Box hands Auspex out to the devices over DHCP as
their resolver directly. See also the point about port 53 further down —
without that this feature has no effect.

## ~~3. Router connection: pages of its own for everyday use~~ — done

Overview, devices, Wi-Fi, port mappings, IPv4, events and the catalogue exist
as pages of their own with a shared tab bar.

Followed up in August: on a rejected sign-in the calls returned an empty
list, indistinguishable from "there is nothing" — the port mappings page then
reported "0 mappings, no door leads in from outside". A false statement about
the security of the network. `RouterList<T>` now carries the reason with it.

## 4. Push instead of polling for the ingest

The control plane fetches the query log every five seconds from a ring buffer
of 10,000 entries. Under load that overflows: in the load test, at 41,000
queries/s, 486,000 entries were missed — correctly reported, but lost.

Meaningless for home traffic. Whoever wants to fix it: the resolver pushes
instead of being polled (SSE, or a JSONL file the control plane follows). As
a side effect the delay until detection drops.

## 5. Sign-in through Authentik instead of a local password

The dashboard has a password sign-in of its own. Authentik is running anyway;
OIDC or forward auth would be the more suitable route and would save one more
password.

`Auth:Enabled: false` already exists for the forward-auth case.

## 6. Following up the detectors' thresholds

The numbers in the detector table are set, not learned, and deliberately
chosen to stay quiet rather than nag. Whether that holds only shows after a
few weeks of real data.

**Partly done.** `fehlalarm-verdacht` accounted for 123 of 131 findings and
buried the rest — it now falls silent for a pair that has already been
reported on several days. New is `dauersender` for the case
`wiederholungssturm` cannot see: a storm that was always there and therefore
has no spike.

**Still open:** how often `neue-domain` reports (probably too often), and
whether `gleichlauf` stays usable during update waves. Both need weeks of
real data — the baseline was not even mature by 24 August (48 hours of
lead-in), and four detectors had never fired by then.

## 7. Measuring dashboard speed

Not measurable so far: with a few thousand rows in the database every number
would be meaningless. Catch up after a few weeks, especially the analysis
page over 30 days and the impact analysis.

## Three things the other projects cannot copy

These are not gaps to be closed but capabilities Pi-hole and AdGuard Home
cannot reproduce, because they lack the instruments rather than the code.
Auspex draws on three independent sources of information: the resolver, the
router and the sensor on the endpoint. The other two projects have one each.

### ~~A. Connections that no lookup explains~~ — built 2026-08-26

The sensor reports that `msedge` connected to `104.18.x.x`. Auspex knows which
addresses it ever handed out. If a connection goes to an address that never
came out of a lookup, that connection bypassed the filter — because of
DNS-over-HTTPS in the browser, an address compiled into a program, or an app
with its own resolver.

Pi-hole and AdGuard Home cannot ask this question at all. They see the queries
that reach them, and a query that was never sent leaves no trace.

The data needed was already in the database: `Connection.Destination` matched
against `Resolution.Ip`, per device and time window. It became the detector
`unerklaerte-verbindung`, which produces a finding rather than a log line.

One limit worth knowing: the sensor runs on Windows and reads TCP connections
only, so this says nothing about phones and nothing about QUIC. The finding
text states that itself.

### ~~B. Which program asks for which domain~~ — built 2026-08-26

The same join in the other direction: `Connection.Process` over the address to
`Resolution.Name`. The result is a sentence like "Chrome talked to 40 tracking
domains, the vacuum cleaner to three endpoints abroad".

Auspex already recorded both halves separately — which device asked for a
name, and which program opened a connection. Putting them together produces a
statement usually only available from commercial tooling. The result is the
**Programs** page.

### C. Detect, cut off, prove — half built 2026-08-26

Auspex is the only one of the three projects that has all three pieces: it can
spot an anomaly, lock a device out at the router, and use the impact analysis
to show in advance what a rule would have done. Until now these were three
separate features rather than one sequence.

**Built so far:** the `quarantine` policy in the resolver, a button on any
finding that names a device, a visible list of what is currently held, and an
expiry that releases it after an hour. The reasoning behind those three
choices is in `QuarantineService`.

**Still missing, deliberately for now:** the router half and the proof half.
Cutting a device off at the router remains a separate, explicit step, because
that block lives in the Fritz!Box and would outlive an Auspex that stopped
running. And the impact analysis is not yet connected to the quarantine, so
the question "what did this cost" still has to be answered by hand.

## SafeSearch is built but not switched on anywhere

The feature shipped and was deployed on 2026-08-26, and deliberately has not
been enabled on a single profile yet. It should be tried on one device first
rather than rolled out across a household.

**Providers to use when trying it:** `google`, `youtube` in moderate mode and
`duckduckgo`. Moderate rather than strict, because strict hides more and
breaks more; embedded videos stop working in places.

**Which profiles are candidates:** the ones belonging to devices that have a
browser. Phones, laptops and desktops.

**Which are not:** smart speakers and appliances. They would accept the
setting and never act on it, because SafeSearch only takes effect when a
browser looks up a search engine. Enabling it there would create the
appearance of protection without the protection, which is worse than leaving
it off.

**What to watch once it is on:** whether the filtered image search gets in the
way on a machine used for work. That possibility is the reason the setting
belongs to a device profile rather than to the network as a whole.

## 8. Smaller points

- **A per-device breakdown** in the interface: which domains a single device
  asks for is currently only visible through the query-log filter. The
  "where to?" page answers part of it since the destinations were added.
- **Editing schedules in the browser**: device profiles work, the time
  windows inside them do not yet — those stay in the configuration file for
  now.
- **Setting `GOMEMLIMIT`** if memory needs a hard ceiling: under load the
  heap grows from 375 to 570 MB.
- **Extending the service catalogue**: 32 entries against AdGuard's several
  hundred.
- **The state of optional listeners is only in the log.** An address marked
  `optional` reports failure and recovery through ERROR/WARN/INFO lines, but
  `/api/v1/status` does not carry it and the dashboard cannot show it. "It
  has been retrying for two hours" should not live in the log alone.
- ~~**Upstream per zone** for tailnet device names~~ — built 2026-08-26 as
  `hosts.reverse_via`, scoped to reverse lookups, which is the case that
  needed it. Forward resolution per zone is still open, and still only a
  nice-to-have.
- **The tailnet listener is not used by anything yet.** Auspex listens on the
  Tailscale address since 2026-08-26, but only servers are in that tailnet —
  no phone. Two routes for that: Tailscale on the phone (then restricted by
  ACL to `badwolf:53`, or a lost phone reaches every production server), or
  WireGuard on the Fritz!Box, after which the phone arrives as an ordinary
  home-network client and profiles, names and MAC binding keep working
  unchanged.
- ~~**No DNS rebinding protection.**~~ — built on 2026-08-26, on by default,
  and it produces a finding rather than a silent drop. Verified live: two
  `nip.io` names blocked, the allowlist held for `dns.msftncsi.com` and
  `ipv4only.arpa`, and the detector turned the blocks into two warnings.
- **Filtering cannot be paused.** Both comparable projects can switch the
  filter off for a few minutes; Auspex cannot. The version worth having is
  ours rather than theirs: per profile, with an automatic end, and an entry
  saying who paused what and when — theirs is global and leaves no trace.
- **No rate limit per client.** AdGuard Home discards above a threshold. The
  more useful reading is as a signal: a device suddenly asking fifty times its
  usual rate is a detector case (tunnelling, malware, a broken app), and the
  brake is the side effect.
- **No regex rules.** Both others have them, and until 2026-08-26 our own
  comparison table claimed we did too. Not free: at 2.3 million rules regex
  must not go into the hash map but into a separate small list evaluated only
  on a miss, or it costs every single query.

## ~~9. Watching port mappings~~ — done

`RouterWatchService` remembers port mappings and devices and reports
deviations as a finding. Open to arbitrary remote ends is classified as
`high`, otherwise as `warn`. The first run deliberately only takes stock.

Learned in the process: the Fritz!Box writes "from anywhere" not as an empty
value but as `0.0.0.0` — the classification would otherwise have led the
mapping open to the whole world as the more harmless one of the two.

## 10. Traffic per device

Auspex knows *which names* a device asks for, but not *how much data* it
pulls. So it cannot answer "who is filling the line". The Fritz!Box keeps the
numbers per device; which route gets them out cleanly has not been checked
yet.

The Windows sensor delivers a byte count per connection, but only for TCP,
only with administrator rights and only as a lower bound — that answers "what
is this program sending", not "who is filling the line".

## ~~11. Downloading the extension from the dashboard~~ — done

The dashboard packs the archive at run time from the sources and delivers it
with the version in the file name. The container's build context sits one
level higher for that; whether the sources really arrive in the image is
checked by the CI job *build the container* — without it their absence would
have stayed silent.

## 12. DHCP — the objection is answered, a second one stands

Originally DHCP was under "deliberately not planned", with a reason: two DHCP
servers on the same network hand out contradictory addresses and take a
household off the air.

**The proposal answers that:** on switching on, Auspex turns the Fritz!Box's
own DHCP server off. Then there are not two. And Auspex *can* do that — the
web-interface connection already sets IPv4 settings, and the DHCP switch sits
on the same page.

### The second objection

It concerns not normal operation but failure.

If Auspex switches the Fritz DHCP off and then does not start itself — wrong
configuration, an occupied port, a crash, a container restart gone wrong —
then **no device in the house** gets an address any more. Not the computer
you would repair it with either. Not the phone you would look up how with.
You stand there with a network cable and a statically assigned address, and
few people in a household can manage that.

That is the one failure you do not fix remotely — and the password rotation
in August showed how quickly such a state comes about: a password changed on
the box, not in Auspex, and the router connection was dead. Had DHCP hung off
it, the house would have been offline.

### If it happens, then like this

It only gets built with a safety net that makes the failure impossible:

1. **Run first, switch over second.** Auspex starts its DHCP service and
   demonstrably answers a test request of its own. Only then is the Fritz
   DHCP switched off — not before, not at the same time.
2. **A dead man's switch.** Auspex renews a marker regularly. If it stops,
   the Fritz DHCP gets switched back on. The route to that has to work
   without Auspex — so a small watcher of its own, not the same process that
   has just died.
3. **A clean exit.** On an orderly shutdown Auspex switches the Fritz DHCP
   back before it stops itself.
4. **Short lease times at first.** The first leases with a few minutes'
   validity: if Auspex fails, the devices ask again quickly — and then get an
   answer from the box that has been switched back on. Longer times only
   after a week that has proved itself.
5. **A way back without a network.** A file or a switch on the container that
   at startup turns the Fritz DHCP back on and leaves Auspex's own disabled.
   Whoever is standing in the dark needs one action, not a web interface.

### What it would bring

Honestly, little that Auspex does not already have:

- **Device names** already come from the router over reverse lookup.
- **Forced DNS** is already achieved, because the box hands Auspex out as the
  local DNS server.
- **Stable identity** comes from the neighbour table over the MAC.

What would genuinely be added: fixed address assignment per device from
within Auspex, the assignment itself visible in the query log, and a device
in the waiting room could be turned away at address assignment rather than
only at name resolution.

That is a real gain for the device register (point 2) — but it does not
outweigh the outage as long as the five safeguards above are not in place.

## ~~13. A bilingual interface~~ — done

German and English, switchable in the appearance panel. English as `en-GB`
and not `en-US`: a log with "2:05 PM" is harder to read than one with
"14:05".

**Built differently from the plan here.** The plan called for `.resx` and
`IStringLocalizer` — the usual route. Two things spoke against it that only
showed on looking closely. First, a dictionary of keys falls over silently:
if one is missing, `IStringLocalizer` returns the key name, and the page
reads "Strom_Zusammenfassung". Second, `{0}` and `{1}` do not say what they
mean — a swapped pair yields a grammatically flawless, wrong sentence.

Instead an abstract class `Strings` with two derivations. Whoever adds a
sentence has to add it in *every* language, or the build breaks. The
half-translated interface this point warned about cannot come into being at
all — the planned point 4 (a CI check) was made unnecessary by the compiler.

**Not `Accept-Language` as the default**, contrary to what was proposed here.
The header travels with every browser unasked, and a browser that happens to
be set to English should not switch the installation over without anybody
wanting that. Only what somebody set explicitly decides: a cookie for the
interface, the header `X-Auspex-Language` for the browser extension.

### What the translation turned up on the way

Three places where text was misused as an identifier. All three worked as
long as there was one language, and would have broken silently with the
second:

* **The keyboard operation in the query log** found its buttons by their
  caption ("freigeben"). In English `f` would have grasped at nothing. Now
  through `data-action`.
* **The extension** fished the name of a blocked redirect back out of the
  message text with `IndexOf("Weiterleitung auf ")`. It now goes out as a
  field of its own.
* **`RouterSearch.Area`** returned "Heimnetz" as display text *and* as the
  grouping key *and* as the input for sorting. Now a key, with the name
  coming from `Strings`.

Plus a fourth, larger point: **the detectors were writing finished sentences
into the database.** That no longer worked out, and the reason is older than
the translation — detection runs every five minutes in the background,
without anybody having opened a page. There is no reader at that moment and
therefore no language. A finding now carries only its measurements
(`FindingValues`); the sentence comes into being at display time. Findings
from before that keep their stored German text until the retention period
clears them away.

And three pages were English even in German: `NotFound`, `Error` and the
reconnect dialog were still in Blazor's shipped state. Noticed while
translating — whoever builds bilingually reads every line once on purpose.

## ~~14. English names in the code~~ — done in 0.9.0

Types, members, locals, functions, files, CSS classes and our own API routes
are English. What stays German, and why, is in
[`codemap.md`](codemap.md#naming-and-where-it-stops).

The rename turned up five places where two sides of the same contract had
already drifted apart — each of them a feature that was quietly broken:

* `/sprache/{kuerzel}` bound its parameter by name while the parameter was
  already called `code`, and the navigation had long linked
  `/language/{code}`. The language switch did nothing.
* The extension read `bekannt`, `geraet`, `profil`, `ausnahmen`, `treffer`
  and `meldung`; the API had been returning `known`, `device`, `profile`,
  `exceptions`, `hits` and `report` for a while.
* The sensor sent `{"verbindungen": [{"prozess": …}]}` while the control
  plane bound `Connections` and `Process`. Nothing bound at all.
* The markup wrote `data-value`, the appearance script read `data-wert`.
  Every click on a colour, a density or a font size set `undefined`.
* The query log wrote `data-tat="profile"`, the keyboard handler looked for
  `"profil"`.

None of the five was found by a test. All five were found by having to say,
for every name, what it means.

## Open in operation, not in the code

These points are not building work but decisions and hand movements. They are
here because "what is open" would otherwise have two answers.

- ~~The test installation runs on port 15353~~ — **done on 23 August 2026.**
  Port 53 was free, `systemd-resolved` inactive. Two things were learned in
  the process that nobody had on their list: `fritz.box` is a genuine public
  domain and pointed at a foreign server after the switch (fixed through the
  new `local` section), and **IPv6 has to be switched over too** — the
  Fritz!Box announces itself as the DNSv6 server through router
  advertisements, Windows prefers that one, and the queries run silently past
  the filter. Recognisable with `nslookup`: if there is an IPv6 address under
  "Server", the IPv4 setting is not taking effect.
- **The earlier point about port 53.** Taking over port 53 is the step after
  which the thing really gets used — and the one that produces every further
  insight. It includes the entry in the Fritz!Box under *Home network →
  Network → Network settings*.
- ~~No permanent password~~ — done on 23 August 2026, the hash is in
  `~/auspex-test/.env`.
- ~~Switch `strategy: "race"` on~~ — **checked and rejected** on 23 August
  2026. Quad9 wins 295 of 300 races; the percentiles stay the same or get
  worse while the query load doubles and both providers see every query.
  Worth it only when two upstreams genuinely win in turn.
- **A single instance.** Needs a second machine on the LAN — the cloud
  servers are no good for it.
- ~~**`~/auspex-test` is a test directory.**~~ — done: the installation lives
  under `/home/bw/auspex` and is a working copy of the repository; a
  `git pull` is enough to update it. The old note read: belongs in
  `/opt/auspex`, and `teardown.sh` should go.

## The resolver's configuration is not backed up

The backup covers data: history, findings, daily totals, own rules, managed
lists, learned state. **Not** the resolver's `config.yaml` — that is a file
the operator owns and that is only mounted into the resolver container; the
control plane does not see it.

On the test installation it currently exists **only there**. It belongs in
version control or in a backup of its own. Deliberately not through the
application: the file can contain an API token, and a tool that puts its own
credentials into a downloadable archive is a bad idea.

## Deliberately not planned

- **A DHCP server.** ~~A second DHCP on the same network can take a whole
  household off the air.~~ The objection is answered, see point 12 — but a
  second one still stands.
- **Our own DNSSEC validation.** Security-critical code you do not get right
  on the side. Instead: enforced validation at the upstream and a visible
  status.
- **Upstreams and listen addresses in the browser.** You can lock yourself
  out with those.
- **Filtering ads inside YouTube videos.** Fundamentally impossible over DNS:
  YouTube serves ads from the same hosts as the video (`*.googlevideo.com`),
  increasingly in the same stream. There is no name that is only ads —
  whoever blocks it blocks YouTube. The same holds for Pi-hole and AdGuard
  Home. Filtering in the page content could do it, but that would mean
  rebuilding uBlock Origin.
- **Caching videos or other files.** A resolver sees no data — it names an
  address, and after that the device talks to the server directly. A proxy in
  front would need its own root CA on every device, and YouTube is built
  against caching anyway (per-session signed URLs, DASH segments per
  quality). The goal "watch it later without traffic" is reached with yt-dlp
  and a media server (Tube Archivist, Pinchflat) as a service of its own. For
  operating-system and game updates `lancache` would make sense — likewise a
  service of its own, not Auspex.
- **PostgreSQL.** SQLite carries a home network effortlessly.
