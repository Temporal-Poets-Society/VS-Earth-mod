#!/usr/bin/env bash
# Ore audit in 1:10 worlds: run.sh name:floor:ores ... (one dedicated server after another)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PY="/c/VS mods/Earth/.venv/Scripts/python"
VS="$APPDATA/Vintagestory"; M="$APPDATA/VintagestoryData/Mods"
for case in "$@"; do
  IFS=: read -r name floor ores height <<< "$case"
  TS="$HERE/$name"
  rm -rf "$TS"; mkdir -p "$TS/Mods"
  cp "$M/earth-1.0.3-tall.2.zip" "$M/earthterraindata-1.0.2.zip" "$M/earthwaterdata-1.0.2.zip" "$M/cylinderworld-1.0.4.zip" "$M/hydrology-0.6.2-rivers.7.zip" "$TS/Mods/"
  cp -r "$HERE/oreaudit/bin/mod" "$TS/Mods/oreaudit"
  "$PY" "$HERE/setup.py" "$(cygpath -w "$TS")" 42683 "$floor" "$ores" $height || continue
  start=$(date +%s)
  (cd "$VS" && timeout 3600 ./VintagestoryServer.exe --dataPath "$(cygpath -w "$TS")" > "$TS/server-console.log" 2>&1)
  echo "$name: server ran $(( $(date +%s) - start )) s"
  grep -h "Earth height\|rescaled\|deposit sea\|OREAUDIT\|Exception\|\[Error\]" "$TS/Logs/server-main.log" | head -40
done
