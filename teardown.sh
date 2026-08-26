#!/bin/sh
# Entfernt einen Testlauf restlos: Container, Volumes, Images.
#
# Der Quellcode bleibt stehen. Eine Vorgängerfassung loeschte ihn mit
# "rm -rf auspex-test" gleich mit — auf einen fest verdrahteten Pfad, der
# nach der Umbenennung des Verzeichnisses gar nicht mehr existierte. Ein
# rm -rf, das ins Leere zeigt, ist Glueck; eines, das danebentrifft, ist der
# schlimmste Fall. Wer den Ordner weghaben will, loescht ihn selbst.
set -e
cd "$(dirname "$0")"

echo "Das entfernt Container, Volumes und Images von Auspex."
echo "Die Volumes enthalten Query-Log, Funde und das Router-Konto."
printf "Wirklich? [tippe: ja] "
read -r antwort
[ "$antwort" = "ja" ] || { echo "Abgebrochen."; exit 1; }

docker compose down -v
docker image rm -f auspex:local auspex-control:local 2>/dev/null || true

echo
echo "Entfernt. Der Quellcode liegt weiterhin in $(pwd)."
echo "Nicht angetastet: .env mit deinen Zugangsdaten."
