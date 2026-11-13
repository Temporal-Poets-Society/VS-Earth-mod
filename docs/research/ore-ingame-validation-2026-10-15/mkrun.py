# Creates an isolated test server data folder: mkrun.py <run dir> <earth|vanilla> <port> <sites.json> [selftest places ;-separated]
import json, sys, os, shutil
run, mode, port, sites = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
places = sys.argv[5].split(';') if len(sys.argv) > 5 and sys.argv[5] else []
root = r"C:\VS mods\Earth"; appdata = os.environ["APPDATA"]
scratch = os.path.dirname(os.path.abspath(__file__))
assert not os.path.exists(run), run
for d in ("Mods", "ModConfig", "Saves"): os.makedirs(os.path.join(run, d))
shutil.copytree(os.path.join(scratch, "oreaudit", "bin", "mod"), os.path.join(run, "Mods", "oreaudit"))
if mode == "earth":
    shutil.copytree(os.path.join(root, "bin", "Debug", "mod"), os.path.join(run, "Mods", "earthmap"))
    shutil.copy(os.path.join(root, "..", "Cworld", "release", "cylinderworld-0.11.3.zip"), os.path.join(run, "Mods"))
    shutil.copy(os.path.join(appdata, "VintagestoryData", "ModConfig", "cylinderworld.json"), os.path.join(run, "ModConfig"))
    c = json.load(open(os.path.join(appdata, "VintagestoryData", "ModConfig", "earthmap.json"), encoding="utf-8-sig"))
    c["SelfTest"] = {"Enabled": bool(places), "Shutdown": False, "RadiusChunks": 1, "RenderWorldMap": False, "CheckRivers": False, "Places": places}
    json.dump(c, open(os.path.join(run, "ModConfig", "earthmap.json"), "w", encoding="utf-8"), indent=2)
d = json.load(open(os.path.join(root, "testserver", "serverconfig.json"), encoding="utf-8-sig"))
d["Ip"] = "127.0.0.1"; d["Port"] = port; d["AdvertiseServer"] = False; d["Upnp"] = False; d["LastLaunchMods"] = None
d["MapSizeX"], d["MapSizeY"], d["MapSizeZ"] = 512000, 384, 204800
d["ModPaths"] = ["Mods", os.path.join(run, "Mods")]
wc = d["WorldConfig"]
wc["Seed"] = "386217"; wc["SaveFileLocation"] = os.path.join(run, "Saves", "oretest.vcdbs"); wc["WorldName"] = "Ore in-game validation " + mode
wc["MapSizeY"] = 384
wc["WorldConfiguration"] = {"worldWidth": "512000", "worldLength": "204800", "worldEdge": "blocked", "startingClimate": "temperate", "gameMode": "survival"}
json.dump(d, open(os.path.join(run, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
a = {"Enabled": True, "StartDelayMs": 5000, "WaitForSelfTest": bool(places), "Shutdown": True, "Sites": json.load(open(sites))}
json.dump(a, open(os.path.join(run, "ModConfig", "oreaudit.json"), "w", encoding="utf-8"), indent=2)
print("ready", run)
