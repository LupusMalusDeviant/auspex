# syntax=docker/dockerfile:1

# SDK-Version festgenagelt, nicht "10.0": das gleitende Etikett zog
# inzwischen 10.0.400, waehrend lokal 10.0.200 baut - und unter 10.0.400
# landet blazor.web.js nicht mehr im Asset-Manifest. Die Oberflaeche kam
# dadurch statisch heraus, ohne dass beim Bauen irgendetwas auffiel.
# "Laeuft bei mir" war woertlich wahr. Beim Anheben der Version hier gehoert
# eine Pruefung im Browser dazu, nicht nur ein gruener Build.
FROM mcr.microsoft.com/dotnet/sdk:10.0.200 AS build
WORKDIR /src
COPY control/Auspex.Control/Auspex.Control.csproj control/Auspex.Control/
RUN dotnet restore control/Auspex.Control/Auspex.Control.csproj
COPY control/Auspex.Control/ control/Auspex.Control/
# Die Quellen der Erweiterung: das Projekt verweist mit ../../ darauf, um
# daraus zur Laufzeit ein Paket zu schnueren. Fehlten sie, blieben die
# Content-Verweise leer und die Dateien fehlten still im Abbild - der
# CI-Auftrag "Container bauen" prueft deshalb, dass sie ankommen.
COPY extension/ extension/
# Ohne --no-restore: mit der vorgezogenen Restore-Schicht fehlten im Ergebnis
# die Blazor-Framework-Dateien komplett - kein wwwroot/_framework, kein
# Eintrag im Asset-Manifest. Die Oberflaeche kam dadurch statisch heraus,
# und beim Bauen deutete nichts darauf hin. Die eingesparte Bauzeit ist den
# Preis nicht wert.
RUN dotnet publish control/Auspex.Control/Auspex.Control.csproj -c Release -o /app

# Der Sensor fuer Windows, mit erzeugt.
#
# Er soll aus dem Dashboard herunterladbar sein wie die Erweiterung - nur ist
# er kein Bundel Quelldateien, sondern eine Programmdatei. Zur Laufzeit laesst
# sie sich nicht schnueren: das Laufzeit-Abbild hat kein SDK, und eines
# hineinzulegen kostet ein halbes Gigabyte fuer etwas, das sich beim Bauen
# einmal erledigen laesst.
#
# Eigenstaendig und getrimmt: rund 15 MB, eine Datei, keine Vorbedingung auf
# dem Zielrechner. Ungetrimmt waeren es 70, framework-abhaengig muesste dort
# erst .NET installiert werden - und "erst noch etwas installieren" ist genau
# das, was ein Knopf im Dashboard ersparen soll.
#
# Von Linux nach win-x64 geht das, weil dabei nichts nativ gebunden wird.
COPY sensor/ sensor/
RUN dotnet publish sensor/Auspex.Sensor/Auspex.Sensor.csproj \
        -c Release -r win-x64 --self-contained true -o /sensor \
    && cp sensor/setup.ps1 sensor/README.md /sensor/ \
    && rm -f /sensor/*.pdb

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl nur fuer den Healthcheck: Docker braucht ein Kommando im Container,
# und das Laufzeit-Image bringt keines mit, das HTTP spricht.
RUN apt-get update && apt-get install -y --no-install-recommends curl tzdata     && rm -rf /var/lib/apt/lists/*     && useradd --uid 10002 --no-create-home --user-group auspex
WORKDIR /app
COPY --from=build /app .
# Der fertige Sensor, damit die Oberflaeche ihn ausliefern kann.
COPY --from=build /sensor ./sensor
# Beide Verzeichnisse gehoeren dem Dienstbenutzer, BEVOR ein Volume darauf
# gelegt wird: Docker uebernimmt Besitzer und Rechte aus dem Image in ein
# neues, leeres Named Volume. Ohne das koennte der Dienst dort nichts anlegen.
# /var/lib/auspex-shared legt auch das Resolver-Image an, mit derselben UID -
# welcher Container das Volume zuerst mountet, haengt an der Startreihenfolge.
RUN mkdir -p /var/lib/auspex-control /var/lib/auspex-shared &&     chown 10002:10002 /var/lib/auspex-control /var/lib/auspex-shared
USER auspex
EXPOSE 5390
ENV ASPNETCORE_URLS=http://0.0.0.0:5390 \
    Analytics__ConnectionString="Data Source=/var/lib/auspex-control/analytics.db" \
    Rules__AllowPath=/var/lib/auspex-shared/allowlist.txt \
    Rules__BlockPath=/var/lib/auspex-shared/blocklist.txt \
    Router__SettingsPath=/var/lib/auspex-control/router.json \
    Router__DeviceNamePath=/var/lib/auspex-shared/devices.json \
    Extension__TokenPath=/var/lib/auspex-control/extension.json \
    Appearance__Path=/var/lib/auspex-control/appearance.json \
    Quarantine__Path=/var/lib/auspex-control/quarantine.json
ENTRYPOINT ["dotnet", "Auspex.Control.dll"]
