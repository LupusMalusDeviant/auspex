# Auspex

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

🇬🇧 **English** · 🇩🇪 [Deutsch](README.de.md)

Auspex is a DNS resolver for a home network. It filters the way Pi-hole or
AdGuard Home do, and it also analyses the queries and speaks up when something
looks wrong.

The name comes from the Roman official who read the flight of birds and
reported what he saw: from *avis* (bird) and *specere* (to look).

## What Auspex adds

Blocklists, rules and exceptions are things Pi-hole and AdGuard Home handle
just as well. The following five points are where Auspex differs from both.

### The router is part of the tool

On connecting, Auspex reads the service descriptions the Fritz!Box publishes
and learns which functions it offers: Wi-Fi, guest network, port forwarding,
and internet access per device.

That allows a block to be enforced on two levels. A device that hardcodes its
own DNS server bypasses the resolver completely. At the router, Auspex can
still cut off its internet access.

### Devices keep their identity

AdGuard Home can only recognise a device by its MAC address if it runs the
DHCP server itself. Auspex reads the kernel's neighbour table instead and
follows the path IP address → MAC address → device name.

The mapping therefore survives a new lease from the router, and it survives a
device rotating its IPv6 address daily. In statistics and in the query log the
device shows up as one row instead of three.

### Auspex speaks up on its own

Eleven detectors search the query log for suspicious patterns: DNS tunnelling,
bursts of NXDOMAIN answers, devices that keep sending regardless, or a port
forwarding on the router that nobody set up.

Each detector states its thresholds and includes the numbers its finding rests
on, so you can check why it fired.

### Exceptions from inside the browser

A browser extension shows which requests on the current page failed at name
resolution. One click allows one of them through, for a limited time and
only for that device.

Which device is meant comes from the sender address of the request. The
extension cannot choose it, so nobody can use the extension to unblock
something on a different device.

### Optional: which program is talking

A sensor for Windows reports which process holds a connection. That is the one
piece of information DNS data can never contain.

The sensor is opt-in, reads TCP connections only, and transmits no content.
Its limits are stated on the page that shows its figures.

### What Auspex does not do

It brings no DHCP server of its own, does not validate DNSSEC itself, and does
not accept encrypted queries as a server. The first two are deliberate, the
third is not finished yet. The reasons are in
[docs/product.md](docs/product.md#what-is-deliberately-not-built).

The dashboard is available in German and English. The language can be switched
in the header and is remembered per browser; the extension follows whatever the
dashboard is set to. Which parts of the code deliberately stay German is
explained in [`docs/codemap.md`](docs/codemap.md).

## Quick start

```bash
cd auspex
go build -o auspex.exe ./cmd/auspex
cp config.example.yaml config.yaml   # adjust
./auspex.exe -config config.yaml
```

Dashboard (creates its database on first start):

```bash
cd control/Auspex.Control
dotnet run
```

Check why a domain is blocked, without starting a server:

```bash
./auspex.exe -config config.yaml -explain ads.doubleclick.net
```

```
Domain:      ads.doubleclick.net
Blocked:     true
Rule:        ||doubleclick.net^ (suffix)
Origin:      hagezi-multi-pro:14823
Reason:      blocked by a rule from list hagezi-multi-pro
```

Test queries against a running instance:

```bash
go build -o auspexdig.exe ./cmd/auspexdig
./auspexdig.exe -server 127.0.0.1:53 example.com ads.doubleclick.net
```

## Further reading

| | |
|---|---|
| [`docs/comparison.md`](docs/comparison.md) | Auspex next to Pi-hole and AdGuard Home — including where they are better |
| [`docs/product.md`](docs/product.md) | Everything in detail: features, measurements, operation |
| [`docs/codemap.md`](docs/codemap.md) | Map of the codebase — what lives where, and why |
| [`docs/open-points.md`](docs/open-points.md) | What is pending, and what is deliberately **not** planned |
| [`docs/blueprints/INDEX.md`](docs/blueprints/INDEX.md) | Per-feature blueprints |
| [`extension/README.md`](extension/README.md) | The browser extension |
| [`sensor/README.md`](sensor/README.md) | The Windows sensor |
| [`SECURITY.md`](SECURITY.md) | Security model, its limits, and how to report a hole |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Build, verify, contribute |
| [`CHANGELOG.md`](CHANGELOG.md) | What changed, per version |

## License

[Apache License 2.0](LICENSE). Auspex ships no blocklists of its own. It reads the
ones you configure, and their contents remain under the terms set by whoever
publishes them. "Fritz!Box" and "AVM" are trademarks of AVM GmbH; this
project is not affiliated with, endorsed by, or supported by AVM. It talks to
the router over TR-064, an open specification of the Broadband Forum, and over
the device's own web interface.
