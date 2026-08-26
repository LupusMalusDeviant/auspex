#!/bin/sh
# Assembles both extensions from the shared core.
#
# Two manifests, one core: Chrome demands a service_worker, Firefox takes an
# ordinary background script - otherwise the code is identical. Maintaining
# the same files twice would be the sure way to let them drift apart.
set -e
cd "$(dirname "$0")"

for target in chrome firefox; do
  rm -rf "dist/$target"
  mkdir -p "dist/$target"
  # -r, because shared/ now has icons/ under it
  cp -r shared/* "dist/$target/"
  cp "$target/manifest.json" "dist/$target/"
  echo "  dist/$target  ($(find "dist/$target" -type f | wc -l) files)"
done

echo
echo "Loading:"
echo "  Chrome/Edge  chrome://extensions -> developer mode -> load unpacked -> dist/chrome"
echo "  Firefox      about:debugging -> This Firefox -> load temporary add-on -> dist/firefox/manifest.json"
