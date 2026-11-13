#!/usr/bin/env bash
# tall.4 test server: run.sh name builtin|glo90 "places"
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PY="/c/VS mods/Earth/.venv/Scripts/python"
VS="$APPDATA/Vintagestory"; M="$APPDATA/VintagestoryData/Mods"
WT="/c/VS mods/Earth/.claude/worktrees/tall-glo90"
name=$1; elev=$2; places=$3
TS="$HERE/$name"
rm -rf "$TS"; mkdir -p "$TS/Mods"
cp "$M/earthterraindata-1.0.2.zip" "$M/earthwaterdata-1.0.2.zip" "$M/cylinderworld-1.0.4.zip" "$M/hydrology-0.6.2-rivers.7.zip" "$TS/Mods/"
cp -r "$WT/bin/Release/mod" "$TS/Mods/earth"
"$PY" "$WT/tools/setup-tall-glo90-world.py" "C:\VS mods\Earth\testserver\serverconfig.json" "$(cygpath -w "$WT/worldconfig.json")" "$(cygpath -w "$TS")" "57.17, 24.85" "$elev" "C:\VS mods\Earth\rawdata\copernicus-glo90" "$places" 42684
start=$(date +%s)
(cd "$VS" && timeout 3600 ./VintagestoryServer.exe --dataPath "$(cygpath -w "$TS")" > "$TS/server-console.log" 2>&1)
echo "$name: server ran $(( $(date +%s) - start )) s"
grep -h "Earth height\|Earth size\|river segments\|Earth elevation\|GLO-90\|summit\|column tops\|Exception\|\[Error\]\|SelfTest\|self-test" "$TS/Logs/server-main.log" | cut -c1-400 | head -60
