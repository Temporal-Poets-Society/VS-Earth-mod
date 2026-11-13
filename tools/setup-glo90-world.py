"""Creates a dedicated-server data folder for a GLO-90 test world (decision 0051; a test setup, not a release one).
   setup-glo90-world.py <template serverconfig.json> <earth worldconfig.json> <data dir> <spawn "lat, lon"> <builtin|glo90> [places]
World 5 120 000 x 2 048 000 (ring 4 096 000, about 9.8 m per block), height 1024 from the server config (release/1.0 has no
Earth-tab height; decision 0050's branch adds one), sea level 128, linear land curve. The self-test generates the places
(default: spawn), reports GLO-90 against the built-in heights and stops the server. Tiles come from the public bucket into
<data dir>/ModData/earth/glo90."""
import json, os, re, sys

template, earthcfg, ts, spawn, elevation = sys.argv[1:6]
places = sys.argv[6].split(";") if len(sys.argv) > 6 else ["spawn"]
width, length, height = 5120000, 2048000, 1024
os.makedirs(os.path.join(ts, "ModConfig"), exist_ok=True)
d = json.load(open(template, encoding="utf-8-sig"))
d["ServerName"] = "Earth GLO-90 test"
d["Ip"] = "127.0.0.1"
d["Port"] = 42683
d["DefaultRoleCode"] = "admin"
d["MapSizeX"], d["MapSizeZ"], d["MapSizeY"] = width, length, height
d["ModPaths"] = ["Mods", os.path.join(ts, "Mods")]
d["LastLaunchMods"] = None
wc = d["WorldConfig"]
wc["Seed"] = "12345"
wc["SaveFileLocation"] = os.path.join(ts, "Saves", "earth-glo90.vcdbs")
wc["WorldName"] = "Earth GLO-90 test"
wc["PlayStyle"] = "earth"
wc["PlayStyleLangCode"] = "preset-earth"
wc["WorldType"] = "standard"
wc["MapSizeY"] = height
style = json.loads(re.sub(r"//.*", "", open(earthcfg, encoding="utf-8-sig").read()))["playStyles"][0]["worldConfig"]
conf = dict(style)
conf.update({"worldWidth": str(width), "worldLength": str(length), "worldEdge": "blocked",
             "earthWorldSize": "vanilla", "earthVerticalBudget": "128", "earthHeightCurve": "linear",
             "earthSpawn": "custom", "earthSpawnCoordinates": spawn, "earthElevation": elevation})
wc["WorldConfiguration"] = conf
json.dump(d, open(os.path.join(ts, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
cfg = {"SelfTest": {"Enabled": True, "Shutdown": True, "RenderWorldMap": False, "CheckRivers": False,
                    "RadiusChunks": 6, "Places": places}}
json.dump(cfg, open(os.path.join(ts, "ModConfig", "earth.json"), "w", encoding="utf-8"), indent=2)
print(ts, json.dumps({k: conf[k] for k in ("earthWorldSize", "earthVerticalBudget", "earthHeightCurve", "earthSpawnCoordinates", "earthElevation")}), places)
