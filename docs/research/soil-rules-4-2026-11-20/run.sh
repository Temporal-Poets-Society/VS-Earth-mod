#!/usr/bin/env bash
# EA-0199 checks on dedicated servers, one after another:
#   audit:<name>:<style>:<soil>   Earth soil at the census sites (SoilAudit test mod)
#   digest:<name>:<style>:<zip>   climate only, a digest of the Terrain pass at spawn and two places, to compare builds
set -uo pipefail
HERE="/c/Users/andri/AppData/Local/Temp/claude/9657fcaf66b21d0e/e50c4000-94fa-4cd0-928f-d0ea081e8aa4/scratchpad/soil4"
W="/c/VS mods/Earth/.claude/worktrees/soil-rules-4"
OUT="/c/VS mods/work/soil4"
PY="/c/VS mods/Earth/.venv/Scripts/python"
BRANCH_ZIP="$OUT/earth-2.2.0-rc.11-soil4.3.zip"
RC11_ZIP="$APPDATA/VintagestoryData/Mods/earth-2.2.0-rc.11.zip"
CW="$(cygpath -u "$APPDATA")/VintagestoryData/Mods/cylinderworld-1.1.2.zip"
HY="$(cygpath -u "$APPDATA")/VintagestoryData/Mods/hydrology-0.6.2.zip"
for spec in "$@"; do
  IFS=: read -r kind name style arg <<< "$spec"
  TS="$OUT/$name"; mkdir -p "$TS/ModConfig"
  start=$(date +%s)
  if [ "$kind" = audit ]; then
    "$PY" - "$TS/ModConfig/soilaudit.json" "$HERE/sites.json" <<'PYEOF'
import json, sys
json.dump({"Enabled": True, "StartDelayMs": 5000, "WaitForSelfTest": True, "Shutdown": True, "Sites": json.load(open(sys.argv[2]))}, open(sys.argv[1], "w"), indent=2)
PYEOF
    TESTSERVER="$TS" PORT=42793 EARTH_ZIP="$BRANCH_ZIP" CW_ZIP="$CW" HYDRO_ZIP="$HY" EXTRA_MODS="$HERE/soilaudit/bin/mod" \
      SELFTEST='{"Shutdown": false, "Places": ["spawn"], "RadiusChunks": 1, "RenderWorldMap": false, "CheckRivers": false}' \
      timeout 5400 bash "$W/tools/run_selftest.sh" --style "$style" --earth "earthSpawn=kyiv;earthSoil=$arg" > "$TS/run.log" 2>&1
    echo "$name ($style, soil $arg): $(( $(date +%s) - start )) s, $(wc -l < "$TS/soilaudit/results.jsonl" 2>/dev/null || echo 0) sites"
    grep -h "Earth soil\|SOILAUDIT\|Exception\|\[Error\]" "$TS/Logs/server-main.log" 2>/dev/null | cut -c1-240 | head -20
  else
    zip="$BRANCH_ZIP"; [ "$arg" = rc11 ] && zip="$(cygpath -u "$RC11_ZIP")"
    TESTSERVER="$TS" PORT=42793 EARTH_ZIP="$zip" CW_ZIP="$CW" HYDRO_ZIP="$HY" \
      SELFTEST='{"Shutdown": true, "Places": ["spawn", "49.60,34.55", "25.70,32.62"], "RadiusChunks": 1, "RenderWorldMap": false, "CheckRivers": false, "Digest": true, "DigestPasses": ["Terrain"]}' \
      timeout 5400 bash "$W/tools/run_selftest.sh" --style "$style" --earth "earthSpawn=kyiv" > "$TS/run.log" 2>&1
    echo "$name ($style, $arg): $(( $(date +%s) - start )) s, $(ls "$TS/EarthMap"/digest-*.Terrain.txt 2>/dev/null | wc -l) digest files"
    grep -h "Earth soil\|SELFTEST\|Exception\|\[Error\]" "$TS/Logs/server-main.log" 2>/dev/null | cut -c1-200 | head -8
  fi
done
echo "checks finished"
