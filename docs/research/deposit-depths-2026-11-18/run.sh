#!/usr/bin/env bash
# EA-0170 census: run.sh case:style:zip ... one dedicated server after another (vanilla rocks, vanilla ores, Vienna spawn)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
W="${EARTH_CHECKOUT:-/c/VS mods/Earth}"
OUT="/c/VS mods/work/ea-0170-census"
PY="/c/VS mods/Earth/.venv/Scripts/python"
for spec in "$@"; do
  IFS=: read -r name style zip <<< "$spec"
  TS="$OUT/$name"; mkdir -p "$TS/ModConfig"
  "$PY" - "$TS/ModConfig/oreaudit.json" "$HERE/sites.json" <<'PYEOF'
import json, sys
json.dump({"Enabled": True, "StartDelayMs": 5000, "WaitForSelfTest": True, "Shutdown": True, "Sites": json.load(open(sys.argv[2]))}, open(sys.argv[1], "w"), indent=2)
PYEOF
  start=$(date +%s)
  TESTSERVER="$TS" PORT=42791 EARTH_ZIP="$zip" EXTRA_MODS="$HERE/oreaudit/bin/mod" \
    SELFTEST='{"Shutdown": false, "Places": ["spawn"], "RadiusChunks": 1, "RenderWorldMap": false, "CheckRivers": false}' \
    timeout 5400 bash "$W/tools/run_selftest.sh" --style "$style" --earth "earthSpawn=vienna" > "$TS/run.log" 2>&1
  echo "$name ($style): $(( $(date +%s) - start )) s, $(wc -l < "$TS/oreaudit/results.jsonl" 2>/dev/null || echo 0) result lines"
  grep -h "deposit depths\|OREAUDIT start\|OREAUDIT done\|Exception\|\[Error\]" "$TS/Logs/server-main.log" 2>/dev/null | head -12
done
echo "census finished"
