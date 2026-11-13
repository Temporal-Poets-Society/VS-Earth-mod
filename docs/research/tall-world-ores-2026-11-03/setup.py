# ores/setup.py <data dir> <port> <earthRockFloor> <earthOres>: a 1:10 world from tools/setup-1to10-world.py with the ore audit
import json, os, subprocess, sys
ts, port, floor, ores = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
wt = r"C:\VS mods\Earth\.claude\worktrees\tall-1to10"
here = os.path.dirname(os.path.abspath(__file__))
subprocess.check_call([sys.executable, os.path.join(wt, "tools", "setup-1to10-world.py"), os.path.join(wt, "testserver", "serverconfig.json") if os.path.exists(os.path.join(wt, "testserver", "serverconfig.json")) else r"C:\VS mods\Earth\testserver\serverconfig.json",
                       os.path.join(wt, "worldconfig.json"), ts, "48.17, 16.9", port])
p = os.path.join(ts, "serverconfig.json"); d = json.load(open(p, encoding="utf-8-sig"))
d["ServerName"] = "Earth 1:10 ore audit"
wc = d["WorldConfig"]; wc["WorldName"] = "Earth 1:10 ore audit " + floor + " " + ores
wc["WorldConfiguration"]["earthRockFloor"] = floor
wc["WorldConfiguration"]["earthOres"] = ores
if len(sys.argv) > 5:  # a standard-height comparison: this height with the play style's own sea level and curve
    style = json.load(open(os.path.join(wt, "worldconfig.json"), encoding="utf-8-sig"))["playStyles"][0]["worldConfig"]
    wc["WorldConfiguration"]["earthWorldHeight"] = int(sys.argv[5])
    for k in ("earthVerticalBudget", "earthHeightCurve"): wc["WorldConfiguration"][k] = style[k]
json.dump(d, open(p, "w", encoding="utf-8"), indent=2)
json.dump({"SelfTest": {"Enabled": True, "Shutdown": False, "RenderWorldMap": False, "CheckRivers": False, "RadiusChunks": 1, "Places": ["spawn"]}},
          open(os.path.join(ts, "ModConfig", "earth.json"), "w", encoding="utf-8"), indent=2)
json.dump({"Enabled": True, "StartDelayMs": 5000, "WaitForSelfTest": True, "Shutdown": True, "Sites": json.load(open(os.path.join(here, "sites.json")))},
          open(os.path.join(ts, "ModConfig", "oreaudit.json"), "w", encoding="utf-8"), indent=2)
print("ready", ts, floor, ores)
