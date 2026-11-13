# Creates and runs an isolated test server: mkrun.py <run dir> <control|rw> <port> [scenarios file, default scenarios.json]
# Needs Earth built in Debug (bin/Debug/mod), wateraudit built (dotnet build -c Release in wateraudit/), and the two ModDB zips in dl/ (see the report).
import json, sys, os, shutil, subprocess
run, mode, port = sys.argv[1], sys.argv[2], int(sys.argv[3])
root = r"C:\VS mods\Earth"; game = os.path.join(os.environ["APPDATA"], "Vintagestory")
here = os.path.dirname(os.path.abspath(__file__))
assert not os.path.exists(run), run
for d in ("Mods", "ModConfig", "Saves"): os.makedirs(os.path.join(run, d))
shutil.copytree(os.path.join(here, "wateraudit", "bin", "mod"), os.path.join(run, "Mods", "wateraudit"))
shutil.copytree(os.path.join(root, "bin", "Debug", "mod"), os.path.join(run, "Mods", "earthmap"))
if mode == "rw":
    for z in ("realisticwater_0.4.0.zip", "realisticrapids_0.1.3.zip"): shutil.copy(os.path.join(here, "dl", z), os.path.join(run, "Mods"))
json.dump({"MoveStoryLocations": False, "SelfTest": {"Enabled": False}}, open(os.path.join(run, "ModConfig", "earthmap.json"), "w"), indent=2)
sc = json.load(open(sys.argv[4] if len(sys.argv) > 4 else os.path.join(here, "scenarios.json")))
json.dump({"Enabled": True, "StartDelayMs": 5000, "Shutdown": True, "Scenarios": sc}, open(os.path.join(run, "ModConfig", "wateraudit.json"), "w"), indent=2)
d = json.load(open(os.path.join(root, "testserver", "serverconfig.json"), encoding="utf-8-sig"))
d["Ip"] = "127.0.0.1"; d["Port"] = port; d["AdvertiseServer"] = False; d["Upnp"] = False; d["LastLaunchMods"] = None
d["ModPaths"] = ["Mods", os.path.join(run, "Mods")]
wc = d["WorldConfig"]
wc["Seed"] = "386217"; wc["SaveFileLocation"] = os.path.join(run, "Saves", "watertest.vcdbs"); wc["WorldName"] = "Earth water test " + mode
wc["WorldConfiguration"] = {"worldWidth": "256000", "worldLength": "102400", "worldEdge": "blocked", "startingClimate": "temperate", "gameMode": "survival"}
json.dump(d, open(os.path.join(run, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
with open(os.path.join(run, "stdout.log"), "w") as out:
    rc = subprocess.run([os.path.join(game, "VintagestoryServer.exe"), "--dataPath", run], cwd=game, stdout=out, stderr=subprocess.STDOUT, timeout=5400).returncode
print("done", run, "exit", rc)
