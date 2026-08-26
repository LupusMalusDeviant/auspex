# Security

Auspex answers DNS for an entire network, and if router credentials are
stored, it can change the router's configuration. In both areas a mistake does
more damage than a crash. This document explains how to report a problem, and
lists what is already known so that nobody spends time writing up something
described here.

## Reporting a vulnerability

Please **no public issue** for security-relevant findings.

Use [private vulnerability reporting][pvr] instead, under
*Security → Report a vulnerability* in the repository. That opens a private
thread only the maintainers can read.

[pvr]: https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability

Helpful in a report:

- What happens, and what would have to happen instead.
- The shortest way to reproduce it. A single `dig` command is more useful
  than three paragraphs of description.
- The version or commit you checked against.
- Whether you believe it is exploitable from the network or needs local
  access.

You will get an acknowledgement as soon as someone has looked at the report.
This is a spare-time project without an on-call rotation, so no fixed response
time is promised; promising one would not be honest.

## What is in scope

Everything in this repository: the resolver, the control plane, the browser
extension, the Windows sensor, the example configuration and the container
recipes.

Out of scope:

- **The blocklists.** Auspex ships none. A wrong entry in somebody else's
  list belongs to its publisher.
- **The Fritz!Box itself.** Firmware bugs go to AVM.
- **Running it outside the documented preconditions**, described below.

## Known limits

The following are deliberate decisions rather than open bugs. They are listed
so that the assumptions behind the security model are visible.

### Auspex does not belong on the open internet

Without a proxy in front, the interface speaks **HTTP**, not HTTPS. The
resolver accepts unencrypted DNS on port 53. Both are meant for a home
network, behind a router that passes none of it through from outside.

Exposing this to the internet would send the sign-in over the network in
plain text and would create an open resolver that can be abused for
amplification attacks. If it does have to be reachable from outside, put a
reverse proxy with TLS in front of it and set `TRUSTED_PROXY`, so that client
addresses are still recorded correctly.

### Whoever is on the network may ask

Auspex does not verify who is asking when a DNS query arrives, because DNS
provides no way to do so. Device profiles are matched by address and MAC
address, and both can be spoofed by anything already on the network. The
purpose of that matching is to apply the right rules and to make the logs
readable. It is not access control.

A device can only be shut out reliably at the router, through
`X_AVM-DE_HostFilter`, which is one of the reasons the router connection
exists.

### DNSSEC is not validated in-house

Auspex does not validate itself; it demands validation at the upstream and
shows the status. Validation logic of your own is security-critical code you
do not get right on the side. Whoever needs the check in the house puts a
validating resolver in front.

### Credentials

The router account sits next to the database, encrypted with ASP.NET Data
Protection. The keys live on the same volume, so whoever can read that gets at
the credentials. The interface's sign-in password is stored as PBKDF2-SHA256
with 210,000 rounds and cannot be computed back.

Give the router account only the rights you really need. Without write
rights, Auspex can read and report but change nothing, which for most purposes
that is enough.

### The browser extension and the sensor

Both sign in with the same bearer token, and the token has a narrow cut: it
may set exceptions **for the device the request comes from**. Which device
that is follows from the sender address, not from anything the extension
says, so a stolen token cannot change somebody else's device unless the
attacker is sitting on it anyway.

Still: the token is a means of signing in. If one becomes known, issue a new
one in the settings; that invalidates the old one.

The sensor is opt-in and reports process names and connection endpoints from
the machine it runs on. It reads them through the Windows TCP tables; it
opens nothing, changes nothing and needs no administrator rights. Without
them the byte counters simply stay empty.

## What Auspex deliberately does not do

- **No DHCP server.** A second DHCP on the same network can take a whole
  household off the air.
- **No amplification.** It answers queries from the configured networks only.
- **No telemetry.** Auspex reports nothing outwards. What leaves the house
  are the DNS queries to the configured upstreams, and the notifications to
  a recipient you enter yourself.
