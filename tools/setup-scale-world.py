"""Creates a dedicated-server data folder for an Earth test world at one of the menu's scales (decision 0053; a test
setup, not a release one).
   setup-scale-world.py <template serverconfig.json> <earth worldconfig.json> <data dir> <Earth size> <spawn "lat, lon">
                        <auto|glo90coarse|glo90|builtin> <tile source> [places] [port]
Everything else is the Earth play style's defaults as the menu makes them: World height, Mountain heights and Land heights
on Automatic unless Land heights is given (the World generation tab stays 256), sea level 110, rock floor automatic. The
self-test generates the places, reports and stops the server. Full GLO-90 tiles are converted from <tile source> (the
bucket's URL or a folder of the published .tif files) into <data dir>/ModData/earth/glo90."""
import json, os, re, sys

template, earthcfg, ts, size, spawn, elevation, source = sys.argv[1:8]
places = sys.argv[8].split(";") if len(sys.argv) > 8 and sys.argv[8] else ["spawn"]
port = int(sys.argv[9]) if len(sys.argv) > 9 else 42684
os.makedirs(os.path.join(ts, "ModConfig"), exist_ok=True)
d = json.load(open(template, encoding="utf-8-sig"))
d["ServerName"] = "Earth scale test"
d["Ip"] = "127.0.0.1"        # this computer only
d["Port"] = port
d["DefaultRoleCode"] = "admin"
d["MapSizeY"] = 256
d["ModPaths"] = ["Mods", os.path.join(ts, "Mods")]
d["LastLaunchMods"] = None
wc = d["WorldConfig"]
wc["Seed"] = "12345"
wc["SaveFileLocation"] = os.path.join(ts, "Saves", "earth-scale-test.vcdbs")
wc["WorldName"] = "Earth scale test"
wc["PlayStyle"] = "earth"
wc["PlayStyleLangCode"] = "preset-earth"
wc["WorldType"] = "standard"
wc["MapSizeY"] = 256
style = json.loads(re.sub(r"//.*", "", open(earthcfg, encoding="utf-8-sig").read()))["playStyles"][0]["worldConfig"]
conf = dict(style)
conf.update({"earthWorldSize": size, "earthSpawn": "custom", "earthSpawnCoordinates": spawn, "earthElevation": elevation})
wc["WorldConfiguration"] = conf
json.dump(d, open(os.path.join(ts, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
cfg = {"ElevationSource": source,
       "SelfTest": {"Enabled": True, "Shutdown": True, "RenderWorldMap": False, "CheckRivers": False,
                    "RadiusChunks": 6, "Places": places}}
json.dump(cfg, open(os.path.join(ts, "ModConfig", "earth.json"), "w", encoding="utf-8"), indent=2)
print(ts, port, json.dumps({k: conf.get(k) for k in ("earthWorldSize", "earthWorldHeight", "earthVerticalBudget", "earthHeightCurve",
                                                      "earthRockFloor", "earthSpawnCoordinates", "earthElevation")}), places)
