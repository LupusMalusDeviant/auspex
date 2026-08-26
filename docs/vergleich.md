# Auspex neben Pi-hole und AdGuard Home

***Deutsch** · [English](comparison.md)*

Wer ein drittes Werkzeug in einem gelösten Feld baut, schuldet eine Antwort
darauf, warum. Dieses Dokument gibt sie und sagt genauso deutlich, wo die
beiden anderen besser sind.

> **Zum Stand der Zahlen:** Der Abschnitt [Was hier *nicht*
> gemessen ist](#was-hier-nicht-gemessen-ist) am Ende ist kein Kleingedrucktes,
> sondern der wichtigste Teil. Leistungsvergleiche aus dem Netz stammen von
> fremder Hardware mit fremden Listen und sind wertlos. Was hier an Zahlen
> steht, ist auf **einer** Anlage gemessen und nur mit sich selbst
> vergleichbar.

## Was Auspex kann und die beiden nicht

### Der Router gehört zum Werkzeug

Pi-hole und AdGuard Home wissen nichts von deinem Router. Auspex liest beim
Verbinden jede Dienstbeschreibung der Fritz!Box und leitet daraus ab, was sie
kann. Bei einer 5690 Pro sind das 39 Dienste mit 468 Aktionen, davon 212
verändernd. Bedienbar sind daraus WLAN, Gastnetz, Portfreigaben,
Internetzugang je Gerät und das Ereignisprotokoll.

Daraus folgt der eigentliche Unterschied: **Durchsetzung auf zwei Ebenen.**
Eine DNS-Sperre umgeht jedes Gerät, das eine Adresse fest einträgt oder DoH
benutzt. Am Router lässt sich derselben Kiste das Internet tatsächlich
abdrehen (`X_AVM-DE_HostFilter`). Beides zusammen ist etwas anderes als
Filtern.

Bewusst entdeckend statt handgeschrieben: 468 Aufrufe von Hand nachzupflegen
wäre in dem Moment veraltet, in dem es fertig ist.

### Geräteidentität ohne DHCP-Server zu sein

AdGuard Home ordnet eine MAC nur dann zu, wenn es **selbst** DHCP-Server ist.
Wer das nicht will, identifiziert Geräte an der IP-Adresse, und die wechselt.

Auspex liest die Nachbartabelle des Kernels über Netlink und geht
Adresse → MAC → Name aus der Router-Geräteliste. Deshalb überlebt die
Zuordnung die DHCP-Erneuerung **und** rotierende IPv6-Privacy-Adressen. Real
gemessen: dasselbe Gerät unter `192.168.1.43` und unter einer temporären
IPv6-Adresse ist eine Zeile im Log, nicht zwei.

### Auspex meldet sich selbst

Elf Detektoren durchsuchen das Abfragelog stündlich nach Mustern: neue
Domains, gehäufte NXDOMAIN-Antworten, plötzliche Wiederholungen, Geräte die
stur weitersenden, Verdacht auf DNS-Tunnel, gleichzeitiges Verhalten mehrerer
Geräte, mutmaßliche Fehlalarme, neue Portfreigaben, neue Geräte, umgeleitete
Rebinding-Versuche und Verbindungen ohne passende Auflösung.

Ein Diagramm zeigt dieselben Daten, wartet aber darauf, dass jemand es
ansieht. Ein Detektor meldet sich stattdessen von selbst. Damit man seinen
Befund prüfen kann, nennt jeder von ihnen seine Schwellenwerte und die Zahlen,
auf denen er beruht.

### Ausnahmen ohne Umweg über die Verwaltung

Eine Browser-Erweiterung sieht über `webRequest`, welche Anfragen auf der
**gerade geöffneten Seite** an der Namensauflösung gescheitert sind, und gibt
sie auf Klick frei: für 15 Minuten, eine Stunde oder dauerhaft, und für genau dieses
Gerät. Im Query-Log stünde dasselbe zwischen den Anfragen von dreißig anderen
Geräten.

Welches Gerät gemeint ist, sagt **nicht** die Erweiterung, sondern ihre
Absenderadresse. Mit einem entwendeten Zeichen lässt sich deshalb kein fremdes
Gerät verändern.

## Wo die beiden besser sind

Diese Liste ist nicht aus Höflichkeit da. Wer nur den ersten Teil liest,
trifft eine falsche Entscheidung.

### Verschlüsseltes DNS als Server — der echte Rückstand

AdGuard Home nimmt DoH, DoT und DoQ von Clients an. Damit filtert das Handy
auch außerhalb des Hauses.

Auspex hat die DoT- und DoH-Listener gebaut; was fehlt, ist ein Zertifikat.
In der Praxis läuft DoH deshalb im Klartext auf Loopback und DoT gar nicht.
DoQ fehlt tatsächlich ganz. Das ist Punkt 1 der
[offenen Liste](open-points.md) und bleibt der einzige Punkt, bei dem ein
Umstieg auf Auspex etwas *wegnimmt*.

Bevor jemand dafür ein Zertifikat kauft: ein WireGuard-Tunnel, ob über
Tailscale oder über den, den die Fritz!Box mitbringt, leistet bereits das,
wofür DoT da
wäre, und braucht weder Zertifikat noch offenen Port.

### Reife

Pi-hole läuft seit Jahren auf Hunderttausenden Geräten; sein Kern (FTL) ist
in C geschrieben und entsprechend durchgeprügelt. AdGuard Home ist ein
einzelnes Go-Programm mit Installer, Mobil-Apps und einem Unternehmen
dahinter.

Auspex ist ~26 000 Zeilen aus einem Haushalt, seit Wochen in Betrieb.
Funktionsumfang ist nicht Reife, und die Verwechslung ist teuer.

### DNSSEC und DHCP

Pi-hole validiert DNSSEC selbst, über das mitgebrachte dnsmasq, und nur
wenn `dnssec=true` eingeschaltet ist. **AdGuard Home tut es nicht.** Es liest
das AD-Bit des Upstreams und reicht es weiter, also dasselbe, was Auspex macht.
Eine frühere Fassung dieses Dokuments hat das anders behauptet; das war
falsch. Einem Mitbewerber eine Eigenschaft anzudichten, die er nicht hat, ist
derselbe Fehler wie sie sich selbst anzudichten.

Auspex verlangt Validierung beim Upstream und zeigt den Status an. Eigene
Validierungslogik ist sicherheitskritischer Code, den man nicht nebenbei
richtig hinbekommt. Ihn schreiben zu können ist außerdem nicht dasselbe, wie ihm
trauen zu dürfen.

Beide bringen einen DHCP-Server mit. Auspex nicht: ein zweiter DHCP im selben
Netz kann einen ganzen Haushalt vom Netz nehmen, und der Ausfall lässt sich
nicht aus der Ferne beheben, weil man selbst keine Adresse mehr bekommt.
[Die Überlegung dazu steht in der offenen Liste.](open-points.md)

### Ökosystem

Sperrlisten, Anleitungen, Foren und fertige Integrationen gibt es für
Pi-hole in einer Menge, die ein Einzelprojekt nicht erreicht.

## Gegenüberstellung

Verifizierbar aus den Handbüchern der Projekte, Stand August 2026.

| | Auspex | Pi-hole | AdGuard Home |
|---|---|---|---|
| Sperrlisten und Ausnahmen | ✓ | ✓ | ✓ |
| Regex-Regeln | ✗ (werden beim Listenlesen übersprungen) | ✓ | ✓ |
| Query-Log, Statistik | ✓ | ✓ | ✓ |
| Profile je Gerät | ✓ | ✓ (Gruppen) | ✓ |
| CNAME-Tarnung erkennen | ✓ auch im Zwischenspeicher | ✓ | ✓ |
| DoT/DoH als **Client** | ✓ | über Zusatzdienst | ✓ |
| DoT und DoH als **Server** | ✓ (gebaut, braucht Zertifikat) | ✗ | ✓ |
| DoQ als **Server** | ✗ | ✗ | ✓ |
| DNSSEC selbst validieren | ✗ (Upstream) | ✓ (ab Werk aus) | ✗ (Upstream) |
| DHCP-Server | ✗ bewusst | ✓ | ✓ |
| Router lesen und stellen | ✓ | ✗ | ✗ |
| Gerät am Router aussperren | ✓ | ✗ | ✗ |
| MAC-Identität ohne eigenen DHCP | ✓ | ✗ | ✗ |
| Anomalie-Erkennung mit Meldung | ✓ 11 Detektoren | ✗ | ✗ |
| DNS-Rebinding gesperrt | ✓ **und als Befund gemeldet** | ✓ stillschweigend | ✓ stillschweigend |
| Welches Programm spricht mit welcher Domain | ✓ (Sensor) | ✗ | ✗ |
| Verkehr, der am Resolver vorbeiging | ✓ (Sensor) | ✗ *strukturell* | ✗ *strukturell* |
| Gerät aus einem Befund heraus isolieren | ✓ zeitlich begrenzt | ✗ | ✗ |
| Regel gegen die Historie rechnen | ✓ | ✗ | ✗ |
| Browser-Erweiterung am Resolver | ✓ | ✗ | ✗ |
| Lernbetrieb für IoT | ✓ | ✗ | ✗ |
| Gefilterte Suche | ✓ je Profil *und* je Zeitfenster | ✗ | ✓ je Client |
| Mobil-App | ✗ | ✗ | ✓ |

## Was hier *nicht* gemessen ist

**Es gibt in diesem Dokument keinen Leistungsvergleich, und das ist Absicht.**

Was über Antwortzeiten und Durchsatz der drei Projekte im Netz steht, ist auf
verschiedener Hardware, mit verschiedenen Listen, verschiedenen Upstreams und
verschiedenen Lastprofilen entstanden. Solche Zahlen nebeneinanderzustellen
sähe aus wie ein Vergleich, wäre aber keiner, und ausgerechnet zugunsten des
eigenen Projekts ausgewählt wäre es unredlich.

Gemessen ist bisher nur Auspex, auf einer Anlage, gegen sich selbst:

- 2 296 816 Regeln geladen, ~700 MB Speicher, 0,07 % CPU im Ruhezustand
- 1 000 Anfragen im Log → 241 Zeilen nach dem Zusammenfassen
- von 3 000 Anfragen verließen 35 das Haus (1,2 %) — der Rest kam aus
  Filter, Zwischenspeicher oder veralteter Antwort

Diese Zahlen sagen etwas über Auspex und **nichts** über die anderen beiden.

### Was ein ehrlicher Vergleich bräuchte

1. Dieselbe Maschine, nacheinander, sonst nichts darauf laufend.
2. Derselbe Listensatz in allen drei — was allein schon Arbeit ist, weil die
   Formate abweichen.
3. Derselbe Upstream, damit nicht die Antwortzeit fremder Server gemessen
   wird.
4. Dasselbe Lastprofil aus einem echten Query-Log, nicht aus zufälligen
   Namen: das Verhältnis von Treffern im Zwischenspeicher zu neuen Anfragen
   entscheidet über alles.
5. Kalt und warm getrennt: Startzeit mit zwei Millionen Regeln ist eine
   eigene Zahl.
6. Gemessen wird, was zählt: Antwortzeit im 50., 95. und 99. Perzentil,
   Speicher unter Last, Verhalten bei ausgefallenem Upstream.

Bis das gelaufen ist, steht hier keine Tabelle mit Millisekunden. Wer eine
sieht, in irgendeinem Vergleich und für irgendein Projekt, sollte zuerst nach
diesen sechs Punkten fragen.
