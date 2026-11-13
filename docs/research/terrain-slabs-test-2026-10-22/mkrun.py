# Creates and runs an isolated test server: mkrun.py <run dir> <control|slabs> <port>
# Needs Earth built in Debug (bin/Debug/mod), slabaudit built (dotnet build -c Release in slabaudit/), and the two ModDB zips in dl/ next to this script (see the report).
import json, sys, os, shutil, subprocess
run, mode, port = sys.argv[1], sys.argv[2], int(sys.argv[3])
root = r"C:\VS mods\Earth"; game = os.path.join(os.environ["APPDATA"], "Vintagestory")
scratch = os.path.dirname(os.path.abspath(__file__))
assert not os.path.exists(run), run
for d in ("Mods", "ModConfig", "Saves"): os.makedirs(os.path.join(run, d))
shutil.copytree(os.path.join(scratch, "slabaudit", "bin", "mod"), os.path.join(run, "Mods", "slabaudit"))
shutil.copytree(os.path.join(root, "bin", "Debug", "mod"), os.path.join(run, "Mods", "earthmap"))
shutil.copy(os.path.join(root, "docs", "research", "regions-2026-09-29", "geoaddons.zip"), os.path.join(run, "Mods"))
if mode == "slabs":
    for z in ("terrainslabs_1.0.17.zip", "placeonslabs_1.2.0.zip"): shutil.copy(os.path.join(scratch, "dl", z), os.path.join(run, "Mods"))
json.dump({"MoveStoryLocations": False, "SelfTest": {"Enabled": False}}, open(os.path.join(run, "ModConfig", "earthmap.json"), "w"), indent=2)
sites = json.load(open(os.path.join(scratch, "sites.json")))
json.dump({"Enabled": True, "StartDelayMs": 5000, "Shutdown": True, "Sites": sites}, open(os.path.join(run, "ModConfig", "slabaudit.json"), "w"), indent=2)
d = json.load(open(os.path.join(root, "testserver", "serverconfig.json"), encoding="utf-8-sig"))
d["Ip"] = "127.0.0.1"; d["Port"] = port; d["AdvertiseServer"] = False; d["Upnp"] = False; d["LastLaunchMods"] = None
d["ModPaths"] = ["Mods", os.path.join(run, "Mods")]
wc = d["WorldConfig"]
wc["Seed"] = "386217"; wc["SaveFileLocation"] = os.path.join(run, "Saves", "slabtest.vcdbs"); wc["WorldName"] = "Earth terrain slabs test " + mode
wc["WorldConfiguration"] = {"worldWidth": "256000", "worldLength": "102400", "worldEdge": "blocked", "startingClimate": "temperate", "gameMode": "survival"}
json.dump(d, open(os.path.join(run, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
with open(os.path.join(run, "stdout.log"), "w") as out:
    rc = subprocess.run([os.path.join(game, "VintagestoryServer.exe"), "--dataPath", run], cwd=game, stdout=out, stderr=subprocess.STDOUT, timeout=5400).returncode
print("done", run, "exit", rc)
