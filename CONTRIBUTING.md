# Contributing

Auspex was written for a home network and is run in one. That shapes how the
project works: a change has to hold up on a real installation, not just in a
test run. This page describes how to get there.

You are welcome to write in German or English. The **code itself is English**:
identifiers, comments and documentation. Display text exists in both languages
and lives in `Services/Localization/`. The parts of the code where German
stays on purpose, such as stored settings keys and the Fritz!Box vocabulary,
are listed in [`docs/codemap.md`](docs/codemap.md).

## Before you build something

For anything larger than a typo, please open an issue first. This is not a
formality. Several points in [`docs/open-points.md`](docs/open-points.md) have
already been decided, and there is a section called "Deliberately not planned"
that gives the reasons. A pull request adding a DHCP server, for example,
would be declined on those grounds, which wastes everyone's time.

## Building and running

The project has three parts and three toolchains:

```bash
# Resolver
cd auspex
go build ./... && go vet ./... && go test ./...

# Control plane
cd control
dotnet build && dotnet test

# Sensor (Windows only)
cd sensor
dotnet test
```

For a run against real queries the example configuration is enough:

```bash
cp auspex/config.example.yaml auspex/config.yaml
```

As long as nothing is listening on port 53 you can start the resolver on a
high port and query it with `dig -p`. Everything else is in the
[README](README.md).

## What a contribution should bring

**Tests with a stated reason.** The project currently has 474 tests on the
control plane, 24 on the sensor and 13 Go packages under test. The comments in
them do not describe *what* is being checked, since the code already shows
that. They describe what could go wrong and why it would matter. A test whose
failure tells nobody anything is not worth keeping.

**Comments that record the reasoning.** How something works can be read off
the code. If a line looks like a detour, write down why it is there. Otherwise
somebody removes it in a year's time and rediscovers the bug it was avoiding.

**Evidence for changes in behaviour.** If you move a threshold or change a
detector, include numbers: how many findings before, how many after, and why
the new figure is better. Two detectors in this project were corrected exactly
that way. One was producing 123 of 131 findings on its own; another produced
none at all although it should have.

**No silent truncation.** If a change only covers half a case, say so in the
comment and in the pull request. A limit that nobody mentions will later be
read as a guarantee.

## Commits

Write a subject line that states what changed, rather than naming a category.
Use the body to explain why. German or English, but stay in one language
within a single commit.

```
Von ueberall schreibt die Fritz!Box als 0.0.0.0

Gemessen an den beiden Freigaben, die real auf der Box standen: TCP/80
und TCP/443 tragen als Gegenstelle nicht den leeren Wert, sondern
0.0.0.0. Die Einstufung haette damit ausgerechnet die weltweit offene
Freigabe als die harmlosere gefuehrt - warn statt high.
```

No `feat:`/`fix:` prefix. One commit, one thing.

## What is especially welcome

- **Other routers.** The connection discovers its capabilities over TR-064
  itself rather than having them hard-wired, so it should be able to cope
  with other models. It has only been checked against a Fritz!Box 5690 Pro.
  Reports from other devices, failed ones included, are valuable.
- **Wrong findings.** If a detector reports nonsense on your network, that is
  a bug and not a usage problem. The numbers from the finding are report
  enough.
- **Further interface languages.** German and English exist. The mechanism
  is a class per language with an abstract base, so the compiler catches a
  missing string and a test catches an untranslated one.

## What it will fail on

Security-critical things added on the side, such as our own DNSSEC
validation, our own cryptography. Along with dependencies that do not pay
for themselves: SQLite carries a home network, and a second database system
would be a building site with nothing on the other side of it.

Security-relevant findings please **not** as an issue, but through the route
in [SECURITY.md](SECURITY.md).

## Licence

By contributing you place your contribution under the
[Apache License 2.0](LICENSE), under which everything else stands as well.
