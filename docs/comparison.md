# Auspex next to Pi-hole and AdGuard Home

*[Deutsch](vergleich.md) · **English***

Building a third tool in a solved field owes an answer as to why. This
document gives it — and states just as plainly where the other two are
better.

> **On the numbers:** the section [What is *not* measured
> here](#what-is-not-measured-here) at the end is not fine print, it is the
> most important part. Performance comparisons found online come from
> different hardware with different lists and are worthless. What numbers
> appear here were measured on **one** installation and are comparable only
> with themselves.

## What Auspex can do and the other two cannot

### The router is part of the tool

Pi-hole and AdGuard Home know nothing about your router. On connecting,
Auspex reads every service description the Fritz!Box offers and derives what
it can do — on a 5690 Pro that is 39 services with 468 actions, 212 of them
mutating. What that makes operable: Wi-Fi, guest network, port forwardings,
per-device internet access and the event log.

From this follows the real difference: **enforcement on two layers.** A DNS
block is bypassed by any device that hard-codes an address or uses DoH. At
the router, that same box can actually have its internet cut off
(`X_AVM-DE_HostFilter`). The two together are a different thing from
filtering.

Deliberately discovered rather than hand-written: maintaining 468 calls by
hand would be out of date the day it was finished.

### Device identity without being the DHCP server

AdGuard Home maps a MAC only when it **is** the DHCP server. Anyone who does
not want that identifies devices by IP — and the IP changes.

Auspex reads the kernel's neighbour table over netlink and goes
address → MAC → name from the router's device list. That is why the mapping
survives DHCP renewal **and** rotating IPv6 privacy addresses. Measured in
practice: the same device under `192.168.1.43` and under a temporary IPv6
address is one row in the log, not two.

### It speaks up on its own

Eleven detectors search the query log hourly for patterns: new domains, bursts
of NXDOMAIN answers, sudden repetition, devices that keep sending regardless,
suspected DNS tunnelling, several devices behaving alike at the same time,
suspected false alarms, new port forwardings, new devices, redirected
rebinding attempts, and connections with no matching lookup.

A chart shows the same data but waits for somebody to look at it. A detector
reports on its own instead. So that its finding can be checked, each one
states its thresholds and the numbers it rests on.

### Exceptions without the detour through admin

A browser extension sees, via `webRequest`, which requests on the **page you
have open** failed name resolution, and releases them on a click — 15
minutes, one hour, or permanently, for this one device. In the query log the
same information sits between the requests of thirty other devices.

Which device is meant is **not** claimed by the extension but derived from
the address it asks from. A stolen token therefore cannot be used to change a
different device.

## Where the other two are better

This list is not here out of politeness. Reading only the first half leads to
a wrong decision.

### Encrypted DNS as a server — the real gap

AdGuard Home accepts DoH, DoT and DoQ from clients. That means the phone
keeps filtering outside the house.

Auspex has the DoT and DoH listeners built — what is missing is a
certificate, so in practice DoH runs in the clear on loopback and DoT not at
all. DoQ is genuinely absent. It is item 1 on the
[pending list](open-points.md), and it stays the one point where switching to
Auspex *takes something away*.

Worth knowing before anyone buys a certificate for it: a WireGuard tunnel —
Tailscale, or the one the Fritz!Box brings along — already provides what DoT
would, and needs neither a certificate nor an open port.

### Maturity

Pi-hole has run for years on hundreds of thousands of devices; its core
(FTL) is written in C and correspondingly hardened. AdGuard Home is a single
Go binary with an installer, mobile apps and a company behind it.

Auspex is ~26,000 lines out of one household, in service for weeks. Feature
count is not maturity, and confusing the two is expensive.

### DNSSEC and DHCP

Pi-hole validates DNSSEC itself, through the dnsmasq it carries with it, and
only when `dnssec=true` is switched on. **AdGuard Home does not.** It reads
the upstream's AD bit and passes it on — the same thing Auspex does. An
earlier version of this document claimed otherwise; that was wrong, and
crediting a competitor with a property it does not have is a mistake in the
same direction as claiming one for oneself.

Auspex requires validation upstream and surfaces the status. Validation logic
is security-critical code you do not get right in passing, and the ability to
build it is not the same as the ability to be trusted with it.

Both bring a DHCP server. Auspex does not: a second DHCP on the same network
can take a whole household offline, and that outage cannot be fixed remotely
because you no longer get an address yourself. [The reasoning is on the
pending list.](open-points.md)

### Ecosystem

Blocklists, guides, forums, ready-made integrations — Pi-hole has these in a
quantity a single-person project will not reach.

## Side by side

Verifiable from the projects' own documentation, as of August 2026.

| | Auspex | Pi-hole | AdGuard Home |
|---|---|---|---|
| Blocklists and exceptions | ✓ | ✓ | ✓ |
| Regex rules | ✗ (skipped when reading lists) | ✓ | ✓ |
| Query log, statistics | ✓ | ✓ | ✓ |
| Per-device profiles | ✓ | ✓ (groups) | ✓ |
| CNAME cloaking detection | ✓ also on cache hits | ✓ | ✓ |
| DoT/DoH as **client** | ✓ | via sidecar | ✓ |
| DoT and DoH as **server** | ✓ (built, needs a certificate) | ✗ | ✓ |
| DoQ as **server** | ✗ | ✗ | ✓ |
| Validates DNSSEC itself | ✗ (upstream) | ✓ (off by default) | ✗ (upstream) |
| DHCP server | ✗ deliberate | ✓ | ✓ |
| Reads and sets the router | ✓ | ✗ | ✗ |
| Cuts a device off at the router | ✓ | ✗ | ✗ |
| MAC identity without own DHCP | ✓ | ✗ | ✗ |
| Anomaly detection with alerting | ✓ 11 detectors | ✗ | ✗ |
| DNS rebinding blocked | ✓ **and reported as a finding** | ✓ silently | ✓ silently |
| Which program talks to which domain | ✓ (sensor) | ✗ | ✗ |
| Traffic that bypassed the resolver | ✓ (sensor) | ✗ *structurally* | ✗ *structurally* |
| Quarantine a device from a finding | ✓ time limited | ✗ | ✗ |
| Replay a rule against history | ✓ | ✗ | ✗ |
| Browser extension tied to resolver | ✓ | ✗ | ✗ |
| Learn mode for IoT | ✓ | ✗ | ✗ |
| SafeSearch | ✓ per profile *and* per time window | ✗ | ✓ per client |
| Mobile app | ✗ | ✗ | ✓ |

## What is *not* measured here

**There is no performance comparison in this document, and that is
deliberate.**

What circulates online about response times and throughput of these three
projects was produced on different hardware, with different lists, different
upstreams and different load profiles. Placing such numbers side by side
would look like a comparison without being one — and selected in favour of
one's own project, it would be dishonest.

Only Auspex has been measured so far, on one installation, against itself:

- 2,296,816 rules loaded, ~700 MB resident, 0.07 % CPU at rest
- 1,000 queries in the log → 241 rows after grouping
- of 3,000 queries, 35 left the house (1.2 %) — the rest came from the
  filter, the cache, or a stale answer

These numbers say something about Auspex and **nothing** about the other two.

### What an honest comparison would require

1. The same machine, one after another, with nothing else running on it.
2. The same set of lists in all three — work in itself, because the formats
   differ.
3. The same upstream, so that foreign servers' latency is not what gets
   measured.
4. The same load profile taken from a real query log, not from random names:
   the ratio of cache hits to fresh lookups decides everything.
5. Cold and warm kept apart: start-up time with two million rules is a figure
   of its own.
6. Measuring what matters: response time at the 50th, 95th and 99th
   percentile, memory under load, behaviour when an upstream fails.

Until that has been run, no table of milliseconds appears here. Anyone who
sees one — in any comparison, for any project — should ask about these six
points first.
