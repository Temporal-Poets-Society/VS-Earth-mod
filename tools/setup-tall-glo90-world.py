"""Creates a dedicated-server data folder for a tall GLO-90 test world (decisions 0050 and 0051; a test setup, not a release one).
   setup-tall-glo90-world.py <template serverconfig.json> <earth worldconfig.json> <data dir> <spawn "lat, lon"> <builtin|glo90>
                             <tile source> [places] [port]
The author's world of 4 November 2026 as the menu makes it: Earth size 4 096 000 (about 12 m per block), the Earth tab's
World height 1024 (the World generation tab stays 256), sea level 110, linear mountain heights (Everest at y 1022, about
9.7 m per block), rock floor automatic. The self-test generates the places, reports GLO-90 against the built-in heights and
stops the server. Tiles are converted from <tile source> (the bucket's URL or a folder of the published .tif files) into
<data dir>/ModData/earth/glo90."""
import json, os, re, sys

template, earthcfg, ts, spawn, elevation, source = sys.argv[1:7]
places = sys.argv[7].split(";") if len(sys.argv) > 7 and sys.argv[7] else ["spawn"]
port = int(sys.argv[8]) if len(sys.argv) > 8 else 42684
os.makedirs(os.path.join(ts, "ModConfig"), exist_ok=True)
d = json.load(open(template, encoding="utf-8-sig"))
d["ServerName"] = "Earth tall GLO-90 test"
d["Ip"] = "127.0.0.1"        # this computer only
d["Port"] = port
d["DefaultRoleCode"] = "admin"
d["MapSizeY"] = 256
d["ModPaths"] = ["Mods", os.path.join(ts, "Mods")]
d["LastLaunchMods"] = None
wc = d["WorldConfig"]
wc["Seed"] = "12345"
wc["SaveFileLocation"] = os.path.join(ts, "Saves", "earth-tall-glo90.vcdbs")
wc["WorldName"] = "Earth tall GLO-90 test"
wc["PlayStyle"] = "earth"
wc["PlayStyleLangCode"] = "preset-earth"
wc["WorldType"] = "standard"
wc["MapSizeY"] = 256
style = json.loads(re.sub(r"//.*", "", open(earthcfg, encoding="utf-8-sig").read()))["playStyles"][0]["worldConfig"]
conf = dict(style)
conf.update({"earthWorldSize": "4096000", "earthWorldHeight": 1024,  # an int, as the menu's slider stores it
             "earthVerticalBudget": "110", "earthHeightCurve": "linear", "earthRockFloor": "auto",
             "earthSpawn": "custom", "earthSpawnCoordinates": spawn, "earthElevation": elevation})
wc["WorldConfiguration"] = conf
json.dump(d, open(os.path.join(ts, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
cfg = {"ElevationSource": source,
       "SelfTest": {"Enabled": True, "Shutdown": True, "RenderWorldMap": False, "CheckRivers": False,
                    "RadiusChunks": 6, "Places": places}}
json.dump(cfg, open(os.path.join(ts, "ModConfig", "earth.json"), "w", encoding="utf-8"), indent=2)
print(ts, port, json.dumps({k: conf[k] for k in ("earthWorldSize", "earthWorldHeight", "earthVerticalBudget", "earthHeightCurve",
                                                  "earthRockFloor", "earthSpawnCoordinates", "earthElevation")}), places)
