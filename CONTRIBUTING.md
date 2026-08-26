# Contributing

Auspex grew out of a home network and is run in one. That shapes how things
are done here: a change has to hold up on a real installation, not only in a
test run. What follows is the way there.

German or English, both are welcome in conversation. **Code is English** —
identifiers, comments, commit-adjacent documentation. Display text exists
twice, German and English, and lives in `Services/Localization/`. Where
German deliberately stays inside the code — stored settings keys, the
Fritz!Box vocabulary — is listed in
[`docs/codemap.md`](docs/codemap.md).

## Before you build something

For anything larger than a typo: **an issue first.** Not as a formality, but
because some of the points in [`docs/open-points.md`](docs/open-points.md)
have already been decided — including a section "Deliberately not planned"
with the reasons. A submitted DHCP server would go back from there unread,
and that would be annoying for both sides.

## Building and running

Three parts, three toolchains:

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

**Tests, and ones with a reason.** The state of play is 432 tests on the
control plane, 24 on the sensor and 13 green Go packages. The comments in
them do not say *what* is being checked — that is in the code — but *why it
could go wrong*. A test whose failure tells nobody anything is ballast.

**Comments that hold on to the why.** The how you read off the code. If a
line looks like a detour, the reason belongs next to it — otherwise somebody
clears it away in a year and finds the bug a second time.

**Evidence for behavioural changes.** Whoever moves a threshold or touches a
detector shows numbers: this many findings before, this many after, and why
that is better. Two of this project's detectors were corrected in exactly
that way — one accounted for 123 of 131 findings, another for not a single
one although it should have.

**No silent truncation.** If something covers only half the case, that
belongs in the comment and in the pull request's description. A limit nobody
mentions reads later like an assurance.

## Commits

A subject line that states something rather than naming a category. The body
says **why**. German or English, consistently within one commit.

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
  itself rather than having them hard-wired — so it should be able to cope
  with other models. It has only been checked against a Fritz!Box 5690 Pro.
  Reports from other devices, failed ones included, are valuable.
- **Wrong findings.** If a detector reports nonsense on your network, that is
  a bug and not a usage problem. The numbers from the finding are report
  enough.
- **Further interface languages.** German and English exist. The mechanism
  is a class per language with an abstract base, so the compiler catches a
  missing string and a test catches an untranslated one.

## What it will fail on

Security-critical things carried along on the side — our own DNSSEC
validation, our own cryptography. Along with dependencies that do not pay
for themselves: SQLite carries a home network, and a second database system
would be a building site with nothing on the other side of it.

Security-relevant findings please **not** as an issue, but through the route
in [SECURITY.md](SECURITY.md).

## Licence

By contributing you place your contribution under the
[Apache License 2.0](LICENSE), under which everything else stands as well.
