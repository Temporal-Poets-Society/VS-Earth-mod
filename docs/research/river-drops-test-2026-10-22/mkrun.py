# Creates and runs an isolated test server with Earth (Debug build, bin/Debug/mod) and Cylinder World, dumping the
# blocks around river drops with the slabaudit helper: mkrun.py <absolute run dir> <port> [map height] [Earth mod folder] [sites file]
# Needs slabaudit built (dotnet build -c Release in ../terrain-slabs-test-2026-10-22/slabaudit).
import json, sys, os, shutil, subprocess
run, port = sys.argv[1], int(sys.argv[2])
height = int(sys.argv[3]) if len(sys.argv) > 3 else 256
root = r"C:\VS mods\Earth"; game = os.path.join(os.environ["APPDATA"], "Vintagestory")
here = os.path.dirname(os.path.abspath(__file__))
assert os.path.isabs(run) and not os.path.exists(run), run
for d in ("Mods", "ModConfig", "Saves"): os.makedirs(os.path.join(run, d))
shutil.copytree(os.path.join(here, "..", "terrain-slabs-test-2026-10-22", "slabaudit", "bin", "mod"), os.path.join(run, "Mods", "slabaudit"))
shutil.copytree(sys.argv[4] if len(sys.argv) > 4 else os.path.join(root, "bin", "Debug", "mod"), os.path.join(run, "Mods", "earthmap"))
cw = os.path.join(root, "..", "Cworld", "release", "cylinderworld-0.11.12.zip")
shutil.copy(cw, os.path.join(run, "Mods"))
json.dump({"SelfTest": {"Enabled": False}}, open(os.path.join(run, "ModConfig", "earthmap.json"), "w"), indent=2)
sites = json.load(open(sys.argv[5] if len(sys.argv) > 5 else os.path.join(here, "sites.json")))
json.dump({"Enabled": True, "StartDelayMs": 5000, "Shutdown": True, "Sites": sites}, open(os.path.join(run, "ModConfig", "slabaudit.json"), "w"), indent=2)
d = json.load(open(os.path.join(root, "testserver", "serverconfig.json"), encoding="utf-8-sig"))
d["Ip"] = "127.0.0.1"; d["Port"] = port; d["AdvertiseServer"] = False; d["Upnp"] = False; d["LastLaunchMods"] = None
d["ModPaths"] = ["Mods", os.path.join(run, "Mods")]
# WORLD=<width> picks the Earth tab's world size (length 0.4 x width), e.g. WORLD=1024000; default 256000
width = int(os.environ.get("WORLD", "256000")); length = width * 2 // 5
d["MapSizeX"], d["MapSizeZ"], d["MapSizeY"] = width, length, height
wc = d["WorldConfig"]
wc["Seed"] = "386217"; wc["SaveFileLocation"] = os.path.join(run, "Saves", "drops.vcdbs"); wc["WorldName"] = "Earth river drops test"
wc["MapSizeY"] = height
wc["WorldConfiguration"] = {"worldWidth": str(width), "worldLength": str(length), "worldEdge": "blocked", "startingClimate": "temperate", "gameMode": "survival", "earthWorldSize": str(width)}
json.dump(d, open(os.path.join(run, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
with open(os.path.join(run, "stdout.log"), "w") as out:
    rc = subprocess.run([os.path.join(game, "VintagestoryServer.exe"), "--dataPath", run], cwd=game, stdout=out, stderr=subprocess.STDOUT, timeout=3600).returncode
print("done", run, "exit", rc)
