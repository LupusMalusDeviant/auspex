# Auspex

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![Lizenz: Apache 2.0](https://img.shields.io/badge/Lizenz-Apache%202.0-blue.svg)](LICENSE)

🇩🇪 **Deutsch** · 🇬🇧 [English](README.md)

Auspex ist ein DNS-Resolver für das Heimnetz. Er filtert wie Pi-hole oder
AdGuard Home, wertet die Anfragen darüber hinaus aus und meldet sich, wenn ihm
etwas auffällt.

Der Name kommt vom römischen Beamten, der die Vogelzeichen deutete und Meldung
machte: aus *avis* (Vogel) und *specere* (schauen).

## Was Auspex zusätzlich kann

Sperrlisten, Regeln und Ausnahmen beherrschen Pi-hole und AdGuard Home
genauso gut. Die folgenden fünf Punkte unterscheiden Auspex von beiden.

### Der Router gehört dazu

Beim Verbinden liest Auspex die Dienstbeschreibungen der Fritz!Box aus und
weiß danach, welche Funktionen sie anbietet: WLAN, Gastnetz, Portfreigaben und
den Internetzugang je Gerät.

Damit lässt sich eine Sperre auf zwei Ebenen durchsetzen. Ein Gerät, das
seinen DNS-Server fest einträgt, umgeht den Resolver vollständig. Am Router
kann Auspex ihm den Internetzugang trotzdem entziehen.

### Geräte behalten ihre Identität

AdGuard Home erkennt ein Gerät an seiner MAC-Adresse nur dann, wenn es selbst
den DHCP-Server stellt. Auspex liest stattdessen die Nachbartabelle des Kernels
und geht den Weg IP-Adresse → MAC-Adresse → Gerätename.

Deshalb bleibt die Zuordnung bestehen, wenn der Router eine neue Adresse
vergibt oder ein Gerät seine IPv6-Adresse täglich wechselt. In Statistik und
Abfragelog erscheint es als eine Zeile statt als drei.

### Auspex meldet sich selbst

Elf Detektoren durchsuchen das Abfragelog nach auffälligen Mustern: DNS-Tunnel,
gehäufte NXDOMAIN-Antworten, Geräte die stur weitersenden, oder eine
Portfreigabe am Router, die niemand angelegt hat.

Jeder Detektor nennt seine Schwellenwerte und liefert die Zahlen mit, auf denen
sein Befund beruht. So lässt sich nachvollziehen, warum er angeschlagen hat.

### Ausnahmen direkt im Browser

Eine Browser-Erweiterung zeigt, welche Anfragen der geöffneten Seite an der
Namensauflösung gescheitert sind. Auf Klick gibt sie eine davon frei —
befristet und nur für dieses eine Gerät.

Welches Gerät gemeint ist, entnimmt Auspex der Absenderadresse der Anfrage.
Die Erweiterung kann es nicht selbst bestimmen, also auch niemand über sie ein
fremdes Gerät freischalten.

### Optional: welches Programm gerade spricht

Ein Sensor für Windows meldet, welcher Prozess eine Verbindung hält. Das ist
die eine Angabe, die aus DNS-Daten grundsätzlich nicht hervorgeht.

Der Sensor ist freiwillig, liest nur TCP-Verbindungen und überträgt keine
Inhalte. Wo seine Grenzen liegen, steht auf der Seite, die seine Zahlen zeigt.

### Was Auspex nicht kann

Es bringt keinen eigenen DHCP-Server mit, validiert DNSSEC nicht selbst und
nimmt keine verschlüsselten Anfragen als Server entgegen. Die ersten beiden
Punkte sind Absicht, der dritte ist noch nicht fertig. Die Gründe stehen in
[docs/product.md](docs/product.md#what-is-deliberately-not-built).

Das Dashboard gibt es auf Deutsch und Englisch. Die Sprache lässt sich in der
Kopfzeile umschalten und wird pro Browser gemerkt; die Erweiterung übernimmt
die Einstellung des Dashboards. Welche Teile des Codes bewusst deutsch bleiben,
erklärt [`docs/codemap.md`](docs/codemap.md).

## Schnellstart

```bash
cd auspex
go build -o auspex.exe ./cmd/auspex
cp config.example.yaml config.yaml   # anpassen
./auspex.exe -config config.yaml
```

Dashboard (legt seine Datenbank beim ersten Start selbst an):

```bash
cd control/Auspex.Control
dotnet run
```

Ohne Serverstart prüfen, warum eine Domain blockiert wird:

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

Testabfragen gegen eine laufende Instanz:

```bash
go build -o auspexdig.exe ./cmd/auspexdig
./auspexdig.exe -server 127.0.0.1:53 example.com ads.doubleclick.net
```

## Zum Nachschlagen

| | |
|---|---|
| [`docs/vergleich.md`](docs/vergleich.md) | Neben Pi-hole und AdGuard Home — auch, wo die besser sind |
| [`docs/product.md`](docs/product.md) | Alles im Einzelnen: Funktionen, Messwerte, Betrieb *(englisch)* |
| [`docs/codemap.md`](docs/codemap.md) | Karte der Codebasis — wo was liegt und warum *(englisch)* |
| [`docs/open-points.md`](docs/open-points.md) | Was ansteht, und was bewusst **nicht** geplant ist *(englisch)* |
| [`docs/blueprints/INDEX.md`](docs/blueprints/INDEX.md) | Baupläne je Feature *(englisch)* |
| [`extension/README.md`](extension/README.md) | Die Browser-Erweiterung *(englisch)* |
| [`sensor/README.md`](sensor/README.md) | Der Windows-Sensor *(englisch)* |
| [`SECURITY.md`](SECURITY.md) | Sicherheitsmodell, seine Grenzen, und wie man eine Lücke meldet *(englisch)* |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Aufbauen, prüfen, beitragen *(englisch)* |
| [`CHANGELOG.md`](CHANGELOG.md) | Was sich je Fassung geändert hat *(englisch)* |

## Lizenz

[Apache License 2.0](LICENSE). Auspex liefert **keine** Sperrlisten mit — es
liest die, die du einträgst; für deren Inhalt gelten die Bedingungen ihrer
Herausgeber. „Fritz!Box" und „AVM" sind Marken der AVM GmbH; dieses Projekt
gehört nicht zu AVM und wird von dort nicht unterstützt. Es spricht mit dem
Router über TR-064, eine offene Spezifikation des Broadband Forum, und über
dessen eigene Weboberfläche.
