"""Creates the dedicated-server data folder for a 1:10 test world (tall-world test, not a release setup).
   setup-1to10-world.py <template serverconfig.json> <earth worldconfig.json> <data dir> <spawn "lat, lon"> [port]
World 5 120 000 x 2 048 000 (ring 4 096 000, ~9.8 m per block). The World generation tab's height stays vanilla's 256 and
the Earth tab's World height sets 1024, as a singleplayer world created from the menu would; sea level 160, linear land curve
(Everest at y 1022, ~10.3 m per block), Rock floor automatic (plain rock from 160 blocks below the surface)."""
import json, os, re, sys

template, earthcfg, ts, spawn = sys.argv[1:5]
port = int(sys.argv[5]) if len(sys.argv) > 5 else 42681
width, length, height = 5120000, 2048000, 1024
os.makedirs(os.path.join(ts, "ModConfig"), exist_ok=True)
d = json.load(open(template, encoding="utf-8-sig"))
d["ServerName"] = "Earth 1:10 test"
d["Ip"] = "127.0.0.1"        # this computer only
d["Port"] = port
d["DefaultRoleCode"] = "admin"  # local test server: fly, teleport, /earth tp
d["MapSizeX"], d["MapSizeZ"], d["MapSizeY"] = width, length, 256
d["ModPaths"] = ["Mods", os.path.join(ts, "Mods")]
d["LastLaunchMods"] = None
wc = d["WorldConfig"]
wc["Seed"] = "12345"
wc["SaveFileLocation"] = os.path.join(ts, "Saves", "earth-1to10.vcdbs")
wc["WorldName"] = "Earth 1:10 test"
wc["PlayStyle"] = "earth"
wc["PlayStyleLangCode"] = "preset-earth"
wc["WorldType"] = "standard"
wc["MapSizeY"] = 256
# the Earth play style's own defaults, then this world's choices
style = json.loads(re.sub(r"//.*", "", open(earthcfg, encoding="utf-8-sig").read()))["playStyles"][0]["worldConfig"]
conf = dict(style)
conf.update({"worldWidth": str(width), "worldLength": str(length), "worldEdge": "blocked",
             "earthWorldSize": "vanilla", "earthWorldHeight": height,  # an int, as the menu's slider stores it
             "earthVerticalBudget": "160", "earthHeightCurve": "linear", "earthRockFloor": "auto",
             "earthSpawn": "custom", "earthSpawnCoordinates": spawn})
wc["WorldConfiguration"] = conf
json.dump(d, open(os.path.join(ts, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
cfg = {"SelfTest": {"Enabled": True, "Shutdown": False, "RenderWorldMap": False, "CheckRivers": False,
                    "RadiusChunks": 8, "Places": ["spawn"], "MeasureStorage": True}}
json.dump(cfg, open(os.path.join(ts, "ModConfig", "earth.json"), "w", encoding="utf-8"), indent=2)
print(ts, port, json.dumps({k: conf[k] for k in ("earthWorldSize", "earthWorldHeight", "earthVerticalBudget", "earthHeightCurve",
                                                  "earthRockFloor", "earthSpawnCoordinates")}))
