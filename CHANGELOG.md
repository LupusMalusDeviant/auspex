# Changelog

All notable changes to Auspex. Dates are the day the work landed on `main`.

Auspex has four parts that ship together: the Go resolver, the .NET control
plane, the browser extension and the Windows sensor. They share one version
number, kept in `VERSION` at the repository root; a test fails if the four
places that carry it ever disagree.

## 0.10.0 — 2026-08-26

### Added

- **SafeSearch, per profile and per time window.** A device profile can send
  the large search engines to the host their operator serves filtered results
  from: Google, including all of its country domains via the public suffix
  list, YouTube in moderate and strict mode, Bing, DuckDuckGo, Yandex and
  Pixabay. A schedule
  inside the profile can add more while its window is open, which makes
  "filtered while the children are awake" expressible. AdGuard Home has this
  per client but not per time window; Pi-hole does not have it.

  The answer carries the target's address and not just a CNAME, because a stub
  resolver does not follow the chain itself, and an answer containing only a
  CNAME means the page does not load. HTTPS records (type 65) are redirected
  as well, because Encrypted Client Hello and alternative addresses are
  carried there. MX, NS and other record types pass through unchanged. A block
  takes precedence over the redirect: if you blocked YouTube, you meant
  blocked.

  A misspelt provider makes the resolver refuse to start, rather than quietly
  not filtering. This follows the same rule as `block_services`, and for a
  more important reason: nothing visibly breaks, so nobody would notice.

- **Listen addresses can be marked `optional`.** A listener that does not come
  up terminates Auspex. That is deliberate, because a resolver answering only on
  TCP looks healthy and works for nobody. That rule is right for the LAN
  address and wrong for a tunnel: on a host restart the container can be
  faster than the VPN interface, and then the whole household loses DNS
  because Tailscale was three seconds late.

  `optional: true` narrows the rule to "this one address may be late". It does
  not mean "give up quietly": the address is retried in the background (2 s,
  doubling to a minute) until it appears. Without the retry, optional would be
  worse than the crash it replaces, because a crash heals itself through the
  restart policy, a silently absent listener does not.

  At least one address has to stay required; a configuration in which every
  listener may fail is refused at startup.

- **DNS rebinding protection, as a finding rather than a silent drop.** An
  answer that points a public name at an address inside the network is
  blocked: on the upstream path, on the cache path and along the CNAME
  chain. Pi-hole and AdGuard Home both do the blocking; neither tells anybody,
  so the one event that would reveal a device is being attacked never reaches
  a human. Here it lands in the query log with the offending address, and a
  tenth detector turns it into a warning, or into a hard finding when several
  distinct names hit one device in one window.

  On by default, and that was decided against real data: of 13,393 recorded
  resolutions on a live installation, twelve pointed at an internal address,
  and every one was either a local zone (answered before the check) or on the
  built-in allowlist. Three of those would have broken had the list been
  written from imagination: `ipv4only.arpa` (RFC 7050, without which
  IPv6-only mobile networks stop working), `dns.msftncsi.com` (how Windows
  decides it has IPv6) and an AWS diagnostic endpoint.

- **The chain: detect, act, let go.** A finding that names a device gets a
  button that puts its profile into quarantine, using a new resolver policy that
  blocks everything regardless of what the device has learned, lifted by
  explicit allow rules only. Three decisions, all deliberate: it is triggered
  by a click and never on its own, because a false positive would otherwise
  take a device off the network at night with nobody watching; it acts on DNS
  rather than on the router, so Auspex stays the one holding the switch; and
  it expires by itself after an hour, because a lock whose key lives in a
  process that might die is not a lock but a trap.

  Running quarantines are shown on the findings page with the reason and a
  "lift now" button. The previous policy is written down before the change, or
  lifting would set a device to "open" and quietly throw away a learn mode
  somebody spent two weeks on.

- **Two new detectors.** *Unexplained connections* — the sensor saw a
  connection and no resolution anywhere accounts for the address, so the
  filter was never asked. Pi-hole and AdGuard Home cannot ask this: they see
  what reaches them, and the absence of a query is invisible from there.
  *Rebinding*, described above.

- **A new page, "Programs".** Which program on a device talked to which
  domain, joined over the address. Auspex held both halves already and neither
  answers the question alone. Addresses no lookup explains are counted rather
  than dropped, because on that page they are the most interesting number.

### Fixed

- **Editing a device profile deleted its MAC bindings and its filtering
  switch.** Saving replaces the stored profile whole, and the editor's working
  copy carried neither field. A profile bound to a MAC came back bound to
  nothing, and one with filtering switched off came back with it on. Both
  with a green confirmation message. The copy now lives on the model and a
  test walks its properties, so a field added tomorrow is covered without
  anybody remembering that test exists.

- **German that survived the 0.9.0 translation.** Sixteen log messages, four
  API error texts, the two `-version`/`-v` flag descriptions, a Prometheus
  HELP line and two half-translated explanation strings shown in the
  dashboard (“im Lernspeicher von Profil X not contained”). In production
  code, an identifier spelt with an umlaut. The word-list sweeps of that
  release missed all of it; it was found by starting the binary and reading
  its output.

  The Go tree now has the guard the control plane has had since 0.9.0:
  `TestNoGermanLeftInTheGoSource` walks every `.go` file and fails on an
  umlaut or a German word, with the documented exceptions named explicitly.
  Its first honest run found twenty-five more places. The first version of
  the test passed because it walked nothing at all, which is the failure mode
  a green test hides best.

### Corrected

- **The documentation credited AdGuard Home with DNSSEC validation it does not
  have.** It reads the upstream's AD bit and passes it on, which is exactly
  what Auspex does. Pi-hole does validate in-house, through its bundled
  dnsmasq, and only when `dnssec=true` is switched on. Corrected in
  `docs/comparison.md`, `docs/vergleich.md` and `docs/product.md`.

## 0.9.0 — 2026-08-25

The release that made the codebase legible to someone who doesn't read German,
and stopped it from helping itself to things nobody asked for.

### Changed

- **The whole codebase is now in English.** Types, members, locals, file and
  directory names, namespaces, database tables and columns, CSS classes,
  JavaScript, the PowerShell setup script, log and error messages, and our own
  API routes. `erweiterung/` is `extension/`, `erweiterung/gemeinsam/` is
  `extension/shared/`, and the files inside it are named after what they do.

  What deliberately stayed German is listed in
  [`docs/codemap.md`](docs/codemap.md#naming-and-where-it-stops), each with its
  reason: the page routes, so bookmarks survive; the keys of stored settings
  and the JSON names inside `Findings.Values`, because renaming those would be
  a migration rather than a rename; the migration class names, whose identity
  is the `MigrationId` recorded in `__EFMigrationsHistory`; the German search
  vocabulary of the router catalogue and the patterns that classify a
  Fritz!Box log, because that *is* the input; and the display strings, which
  are content and exist in both languages anyway.
- **Time zone is a setting, not an assumption.** Every clock time in the
  dashboard was two hours early in summer: the container ran on UTC while the
  house is in Berlin, and the UI dutifully called `ToLocalTime()` on it. It is
  now selectable under **Settings → Time zone**, defaulting to the container's
  zone. Night-time findings are judged against the same zone; previously
  "at night" meant 02:00–08:00 local.
- **Prerequisites are opt-in.** Origin lookup no longer downloads ~90 MB of
  routing data (and another ~90 MB for cities) the first time a container
  starts. The decision lives in `compose.yml`, where it belongs to whoever
  runs the thing. Switching it off only stops the refresh; data already on
  disk keeps being used.
- **Sensor configuration keys are English** (`base`, `token`, `pollSeconds`,
  `reportSeconds`, `verbose`). The old German keys and the old
  `AUSPEX_BASIS`/`AUSPEX_ZEICHEN` environment variables are still read, so a
  `sensor.json` already sitting on a machine keeps working.
- **The language header is `X-Auspex-Language`.** The old
  `X-Auspex-Sprache` is still accepted, so an extension already installed in a
  browser only knows the old name.
- **Page routes are English.** `/querylog`, `/devices`, `/dossier`,
  `/findings`, `/lists`, `/learn`, `/analytics`, `/impact`, `/settings`,
  `/backup`, `/router/catalog`, `/router/log`. They were already half
  renamed, which is how the 404 below came to light. Bookmarks from before
  0.9.0 break once.
- **API routes are English.** `/api/ext/{appearance,me,allow,revoke,blocked,connections}`,
  `/api/extension/{token,package}`, `/api/appearance`, `/api/sensor/package`,
  `/api/router/{catalog,call}`, `/language/{code}`, and on the resolver
  `/api/v1/who` and `/api/v1/cache/forget`. Page routes are unchanged.
- **The sensor's command line is English:** `--show` instead of `--zeigen`,
  and `setup.ps1 -Remove` instead of `-Entfernen`.
- **The shared device-name file is `devices.json`.** It was `geraete.json`.
  Both sides change together; on an existing installation the file in
  `auspex-shared` has to be renamed with them, or device names are missing
  until the next export.

### Added

- **Prerequisites panel** on the settings page. Analysis, router account,
  browser extension, sensor and origin lookup, each with its state, what it
  contributes and how to switch it on. An empty column otherwise reads as
  "there is nothing here" rather than "something is missing here". For the
  sensor it distinguishes never set up from set up but silent for a day,
  when something is wrong, that is the whole difference.
- **Interfaces at the seams.** The services that leave the process — resolver,
  router, rule files, origin database and stored settings, are now reached
  through interfaces. Deliberately not one interface per class: pure
  computation has no outside world, and a fake in front of it only tests the
  fake.
- **A journal for the sensor.** It runs as a scheduled task under SYSTEM with
  no window, so the line explaining why byte counters are empty went to a
  console nobody could see. It is now also written to
  `%LOCALAPPDATA%\Auspex\auspex-sensor.log`, without the token, rewritten on
  every start.

### Fixed

Five of these came out of the renaming, and none of them had a failing test.
Each was a contract with two sides where only one side had been renamed
earlier. That is the kind of break which stays invisible until you have to say, for every
name, what it means.

- **The language switch did nothing.** The route template was
  `/sprache/{kuerzel}` while its parameter was already called `code`, so
  ASP.NET bound nothing; the navigation had been linking `/language/{code}`
  for a while.
- **The browser extension was half blind.** It read `bekannt`, `geraet`,
  `profil`, `ausnahmen`, `treffer` and `meldung` from an API that had been
  returning `known`, `device`, `profile`, `exceptions`, `hits` and `report`.
- **The sensor endpoint fell over on every report.** The sensor sent
  `{"verbindungen": [{"prozess": …}]}`; the control plane bound `Connections`
  and `Process`. Nothing matched, and the batch arrived as null.
- **Every appearance button set `undefined`.** The markup wrote `data-value`,
  the script read `data-wert`. Theme, accent, density and font size all went
  through it.
- **The `p` key in the query log grasped at nothing.** The row wrote
  `data-tat="profile"`, the keyboard handler looked for `"profil"`.
- **The query log's "create a profile" button led to a 404.** It navigated to
  `/geraete`; the page had been renamed to `/devices`.
- **`compose.yml` set `Geo__Stadt`,** while the option is called `City`. The
  city lookup could not be switched off, because `GEO_STADT=false` never arrived.
  It is `GEO_CITY` now, and `AUSPEX_ZEITZONE` is `AUSPEX_TIMEZONE`.
- **The extension never showed the profile.** `/api/v1/who` returned the
  field `profil`; the control plane bound `Profile`.
- **The settings page came down on a fresh install** with
  `SQLite Error 14: unable to open database file`. SQLite creates a missing
  file on opening, but only when the directory exists, and before the first
  origin import it does not. The earlier fix in this release covered the
  missing *table*, which is one step later.
- **With `Auth:Enabled=false` every protected endpoint returned 500.** The
  authorization middleware wants to challenge on a failed policy, finds no
  `IAuthenticationService` and gives up. For the backup download, the
  extension token, both packages and every router call. The pages were fine,
  which is why it went unnoticed: the fallback policy is the only thing that
  went away with the switch.
- **The backup download threw.** `ZipArchive` writes its central directory on
  `Dispose`, and it writes it synchronously; Kestrel refuses synchronous
  writes. The tests did not catch it because they write into a
  `MemoryStream`. The archive now goes out through a temporary file.
- **The appearance script died at startup.** Its last line still called
  `anwenden(read())`. Theme, accent, density and font size therefore did
  nothing at all: the page rendered, answered 200, and used the defaults.
- **The font stylesheet 404'd.** The file was still `schriften.css` on disk
  while the markup asked for `fonts.css`. Visible only as a page in the
  fallback font.
- **`setup.ps1` did not parse.** `"$asWhom:"` reads to PowerShell as a scoped
  variable reference, and the whole file stopped parsing. That is the one failure a
  setup script must not have, because it runs elevated in a window that
  closes on abort.
- **A wrong password said nothing.** The sign-in endpoint redirected to
  `/login?fehler=1` while the page bound `?error`. The form came back with no
  message at all, neither "wrong password" nor the separate "your form token
  expired", which is the one people need most.
- **`build.sh` produced one folder called `dist/` instead of two.** A
  half-finished rename left `$ziel` next to `$target`; under `set -e` the
  copy still succeeded, into a directory named after an empty variable.
- **Byte counters were always empty.** The code enabled TCP ESTATS with type
  `0`, labelled `TcpConnectionEstatsData`. `0` is `TcpConnectionEstatsSynOpts`,
  which has no read-write structure at all, so Windows rejected every call
  with `ERROR_INVALID_USER_BUFFER`. `Data` is `1`. Measured before changing:
  type 0 failed identically at every buffer size, so size was never the cause.
- **The setup script overwrote a running file.** `setup.ps1` copied the
  executable before stopping the sensor that was using it. Windows locks it,
  the script aborted, and because the elevated window closes on abort, a
  failed update looked exactly like a successful one.
- **`NetworkRanges.State()` crashed on a fresh install** — it queried a table
  that only exists after the first import. With origin lookup now opt-in, that
  is the normal case, and it would have taken the settings page down with it.
- **Timestamps from SQLite were rendered as local time without conversion.**
  `DateTime` comes back with `Kind.Unspecified`; `ToLocalTime()` on that does
  nothing at all, and the implicit conversion to `DateTimeOffset` is worse, because it
  reads the value as local and staples the local offset onto it.

### Tests

The renaming broke ten contracts and not one of them had a failing test. What
was added, each against the specific hole it fell through:

- **The sensor's wire format**, pinned as a literal on both sides — the
  control plane deserialises it, the sensor serialises to it. Rename a field
  on either side and exactly one of the two goes red.
- **The extension's contract**: every field it reads off an answer, every
  route it calls, and the old German names as a blocklist.
- **The migration**, run against a database that still has the German tables,
  with rows in them, including `Down()`, which nobody had ever executed.
  Everything else here builds its schema with `EnsureCreated`, which never
  touches a migration at all.
- **The front end**: no script may call a function that is not defined in it
  (`node --check` passes that), and every asset the markup asks for has to
  exist.
- **Scripts parse** — PowerShell and shell, in CI.

Each of these was checked by putting the original bug back and watching the
new test fail.

### Documentation

All documentation is English: `docs/codemap.md`, `docs/product.md` (was
`docs/produkt.md`), `docs/open-points.md` (was `docs/offene-punkte.md`),
`CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, the pull-request
template and both component READMEs. The comparison stays bilingual
(`docs/vergleich.md` / `docs/comparison.md`), and so do the two READMEs.

Added: `docs/blueprints/`, one blueprint per feature plus an index.

### Notes for anyone upgrading

Run the migration before serving traffic: tables and columns are renamed, and
the model no longer matches an untouched database. The migration is
hand-written with `RenameTable` and `RenameColumn`, because the one EF scaffolded wanted
to drop and recreate three tables, which would have cost every row in them. It
was rehearsed against a copy of a live database with 321 742 queries; nothing
was lost.

Three files on the volumes have to be renamed in the same window, because
their names are configuration and the new image looks for the new ones:

1. `auspex-control-data/erweiterung.json` → `extension.json` — the extension's
   token. Without this the token is gone, and it is shown exactly once.
2. `auspex-shared/geraete.json` → `devices.json` — and **the resolver's own
   `config.yaml`** with it: `device_names` points at the old name, and that
   file belongs to the operator, so no update touches it.
3. Nothing else. `router.json`, the `keys/` ring and the password hash in
   `.env` keep their names, and the protector purpose for the router account
   is unchanged.

The extension's token itself survives an upgrade even though its protector
purpose was renamed: the store reads the old purpose as a fallback. Without
that fallback, the rename would have made every stored token unreadable,
a purpose string is key material, not a name.

What has to be replaced by hand: the browser extension and the sensor. Both
speak the new field names while an installed build speaks the old ones, so the
old extension calls `/api/ext/ich`, gets a 404 and reports "unreadable
answer".

Nothing has to be **typed** again, though: the extension reads the old
storage keys `basis` and `zeichen` as a fallback and writes the new ones on
the first save. The sensor's `sensor.json` already did the same. So loading
the new build is enough, because the address and the token come along.

Also fixed on the way: `Appearance__Path` now points at the volume. The
default lands next to the executable, which in a container means inside the
image, so the chosen time zone, language and accent were being lost on every
rebuild.

## 0.1.0

Everything before the above: the resolver, filtering, query log, analysis,
findings, device profiles, learn mode, the router integration, the browser
extension, the dossier and the first version of the sensor.
