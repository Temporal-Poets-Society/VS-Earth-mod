#!/usr/bin/env bash
# Creates a fresh test world on a local dedicated server, with Earth and what it requires (Cylinder World, Hydrology),
# runs Earth's self-test and stops. Renders and the report land in <test server>/EarthMap/.
#   tools/run_selftest.sh [--style earth|earth-10|...] [--height N] [--size WxL] [--seed S] [--earth "earthSpawn=riga;..."]
#   --style picks the Earth play style (default earth, 1:100); the world takes that style's Earth tab settings from
#   worldconfig.json. --height overrides its world height. --size sets the World generation tab's width and length and the
#   Earth tab's size to "vanilla"; without it the play style's scale decides. --earth sets more Earth tab options
#   (decision 0023).
# Packages (each may also be a mod folder):
#   EARTH_ZIP  default this checkout's release/earth-<modinfo version>.zip, else its bin/Debug/mod (dotnet build)
#   CW_ZIP     default the newest cylinderworld-*.zip in ../Cworld/release or the player's VintagestoryData/Mods
#   HYDRO_ZIP  default the newest hydrology-*.zip in ../Hydrology/release or the player's VintagestoryData/Mods
#   EXTRA_MODS="../Sea/release/sea-0.6.0.zip;..."  more mods for combination tests
# TESTSERVER (default <this checkout>/testserver) holds the server's data; PORT overrides its config's port.
# SELFTEST='{"Digest": true, "DigestPasses": ["Terrain"], "MeasureStorage": true}' adds self-test settings (EarthMapConfig
# SelfTestConfig) to the run's earth.json: a digest of every generated chunk column to compare two builds, the memory.
# EARTH_CONFIG='{"StreamsLayer": false}' adds other earth.json settings to the run's config.
# Earth's data store, ModData/earth, is set up once from EARTH_DATA (default the player's
# VintagestoryData/ModData/earth): data/, ground/ and cache/ are copied, glo90/ (8.5 GB of heights) is linked as a
# directory junction. Earth's download addresses are switched off, so a missing file fails the run instead of
# downloading. Never delete the test server folder recursively while the junction is in it: remove
# <TESTSERVER>/ModData/earth/glo90 first (cmd //c rmdir), or the delete may follow it into the player's heights.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TS="${TESTSERVER:-$ROOT/testserver}"
VS="${VINTAGE_STORY:-$APPDATA/Vintagestory}"
PLAYER="$(cygpath -u "$APPDATA")/VintagestoryData"
# the suite root: the first folder above this checkout with suite.json (a worktree sits deeper than the main checkout)
SUITE="$ROOT"; while [ "$SUITE" != "/" ] && [ ! -f "$SUITE/suite.json" ]; do SUITE="$(dirname "$SUITE")"; done
PY="$ROOT/.venv/Scripts/python"; [ -x "$PY" ] || PY="$SUITE/Earth/.venv/Scripts/python"; [ -x "$PY" ] || PY=python
# the newest <id>-<version>.zip in the given folders, by version (a pre-release sorts below its release)
newest() {
  "$PY" - "$@" <<'EOF'
import glob, os, re, sys
mod, dirs = sys.argv[1], sys.argv[2:]
def key(p):
    m = re.match(re.escape(mod) + r"-(\d+)\.(\d+)\.(\d+)(?:-(.+))?\.zip$", os.path.basename(p))
    if not m: return ((0,), 0, ())
    pre = m.group(4)
    return (tuple(int(g) for g in m.group(1, 2, 3)), 0 if pre else 1,
            tuple((0, int(t), "") if t.isdigit() else (1, 0, t) for t in re.split(r"[.-]", pre or "")))
found = [p for d in dirs for p in glob.glob(os.path.join(d, mod + "-*.zip"))]
print(max(found, key=key) if found else "")
EOF
}
version() { sed -n 's/.*"version"[^"]*"\([^"]*\)".*/\1/p' "$1/modinfo.json" | head -1; }
EARTH_ZIP="${EARTH_ZIP:-$ROOT/release/earth-$(version "$ROOT").zip}"
[ -e "$EARTH_ZIP" ] || EARTH_ZIP="$ROOT/bin/Debug/mod"
CW_ZIP="${CW_ZIP:-$(newest cylinderworld "$SUITE/Cworld/release" "$PLAYER/Mods")}"
HYDRO_ZIP="${HYDRO_ZIP:-$(newest hydrology "$SUITE/Hydrology/release" "$PLAYER/Mods")}"
for z in "$EARTH_ZIP" "$CW_ZIP" "$HYDRO_ZIP"; do
  [ -n "$z" ] && [ -e "$z" ] || { echo "missing package: '$z' (build it, or set EARTH_ZIP / CW_ZIP / HYDRO_ZIP)" >&2; exit 1; }
done
echo "Earth: $EARTH_ZIP"; echo "Cylinder World: $CW_ZIP"; echo "Hydrology: $HYDRO_ZIP"

STYLE=earth; HEIGHT=0; W=256000; L=102400; SIZE=0; SEED=12345; KEEP=0; PLACES=""; EARTH=""
while [ $# -gt 0 ]; do
  case "$1" in
    --style) STYLE="$2"; shift ;;
    --height) HEIGHT="$2"; shift ;;
    --size) W="${2%x*}"; L="${2#*x}"; SIZE=1; shift ;;
    --earth) EARTH="$2"; shift ;;
    --seed) SEED="$2"; shift ;;
    --keep) KEEP=1 ;;
    --places) PLACES="$2"; shift ;;
  esac
  shift
done

mkdir -p "$TS/Mods" "$TS/ModConfig"
[ -f "$TS/serverconfig.json" ] || cp "$SUITE/Earth/testserver/serverconfig.json" "$TS/serverconfig.json"
rm -f "$TS/Mods/"*.zip
rm -rf "$TS/Mods/earth" "$TS/Mods/cylinderworld" "$TS/Mods/hydrology"
# a package is copied as it is: a zip, or a mod folder under its own name
add() { if [ -d "$1" ]; then cp -r "$1" "$TS/Mods/$2"; else cp "$1" "$TS/Mods/"; fi; }
add "$EARTH_ZIP" earth; add "$CW_ZIP" cylinderworld; add "$HYDRO_ZIP" hydrology
# more mods for combination tests (e.g. Sea): EXTRA_MODS="path/a.zip;path/b.zip" (paths may contain spaces)
IFS=';' read -r -a EXTRA <<< "${EXTRA_MODS:-}"
for z in "${EXTRA[@]}"; do [ -n "$z" ] && add "$z" "$(basename "$z")"; done
if [ "$KEEP" = 1 ]; then rm -rf "$TS/EarthMap" "$TS/Logs"; else rm -rf "$TS/Saves" "$TS/EarthMap" "$TS/Logs"; fi
DATA="${EARTH_DATA-$PLAYER/ModData/earth}"
if [ -n "$DATA" ] && [ -d "$DATA" ]; then
  mkdir -p "$TS/ModData/earth"
  for sub in data ground cache; do
    [ -d "$DATA/$sub" ] && [ ! -e "$TS/ModData/earth/$sub" ] && cp -r "$DATA/$sub" "$TS/ModData/earth/" && echo "Earth $sub copied from $DATA"
  done
  if [ -d "$DATA/glo90" ] && [ ! -e "$TS/ModData/earth/glo90" ]; then
    LINK="$(cygpath -w "$TS/ModData/earth/glo90")" TARGET="$(cygpath -w "$DATA/glo90")" \
      powershell -NoProfile -Command 'New-Item -ItemType Junction -Path $env:LINK -Target $env:TARGET | Out-Null'
    [ -e "$TS/ModData/earth/glo90" ] || { echo "could not link glo90" >&2; exit 1; }
    echo "Earth glo90 linked from $DATA"
  fi
fi

"$PY" - "$TS" "$W" "$L" "$HEIGHT" "$SEED" "$PLACES" "$SIZE" "$EARTH" "$STYLE" "$EARTH_ZIP" "${PORT:-}" "${SELFTEST:-}" <<'EOF'
import json, os, sys, zipfile
ts, w, l, h, seed, places = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), sys.argv[5], sys.argv[6]
size_set, earth, style, package = sys.argv[7] == "1", sys.argv[8], sys.argv[9], sys.argv[10]
port = sys.argv[11] if len(sys.argv) > 11 else ""
selftest = json.loads(sys.argv[12]) if len(sys.argv) > 12 and sys.argv[12] else {}
if os.path.isdir(package): text = open(os.path.join(package, "worldconfig.json"), encoding="utf-8-sig").read()
else: text = zipfile.ZipFile(package).read("worldconfig.json").decode("utf-8-sig")
styles = {s["code"]: s for s in json.loads(text)["playStyles"]}
if style not in styles: sys.exit("Earth has no play style %s (it has %s)" % (style, ", ".join(styles)))
world = dict(styles[style]["worldConfig"])
h = h or int(world.get("earthWorldHeight", 384))
p = ts + "/serverconfig.json"
d = json.load(open(p, encoding="utf-8-sig"))
d["MapSizeX"], d["MapSizeZ"], d["MapSizeY"] = w, l, h
if port: d["Port"] = int(port)
wc = d["WorldConfig"]
wc["Seed"] = seed
wc["SaveFileLocation"] = ts.replace("/", "\\") + "\\Saves\\earthtest.vcdbs"
d["ModPaths"] = ["Mods", ts.replace("/", "\\") + "\\Mods"]
wc["WorldName"] = "Earth test"
wc["PlayStyle"] = style
wc["PlayStyleLangCode"] = styles[style].get("langCode", "preset-" + style)
wc["WorldType"] = "standard"
wc["MapSizeY"] = h
world.update({"worldWidth": str(w), "worldLength": str(l), "worldEdge": "blocked", "startingClimate": "temperate",
              "gameMode": "survival", "earthWorldHeight": h})
if size_set:
    world["earthWorldSize"] = "vanilla"
for kv in filter(None, earth.split(";")):
    k, v = kv.split("=", 1)
    world[k] = {"true": True, "false": False}.get(v.lower(), v)
wc["WorldConfiguration"] = world
json.dump(d, open(p, "w", encoding="utf-8"), indent=2)
cfg = ts + "/ModConfig/earth.json"
# fresh config each run (current defaults), only the self-test switched on and no downloads (a closed local port)
off = "http://127.0.0.1:9/"
c = {"ConfigVersion": 5, "SelfTest": {"Enabled": True, "Shutdown": True}, "DataRepo": off, "ElevationRepo": off,
     "ElevationSource": off, "GroundRepo": off}
if places:
    c["SelfTest"]["Places"] = places.split(";")
c["SelfTest"].update(selftest)
# EARTH_CONFIG='{"StreamsLayer": false}': more earth.json settings for the run (EA-0131's with/without comparison)
c.update(json.loads(os.environ.get("EARTH_CONFIG") or "{}"))
json.dump(c, open(cfg, "w", encoding="utf-8"), indent=2)
print("play style", style, "world", w, "x", l, "height", h, "seed", seed, "size", world.get("earthWorldSize"))
EOF

cd "$VS"
./VintagestoryServer.exe --dataPath "$(cygpath -w "$TS")" > "$TS/server-console.log" 2>&1 || true
echo "server exited"
grep -h "SELFTEST\|Earth Map\|earth\|Exception\|Error" "$TS/Logs/server-main.log" | head -80 || true
