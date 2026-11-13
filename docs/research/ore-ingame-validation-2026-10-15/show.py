import json, sys
d = json.load(open(sys.argv[1]))
def r(x): return "  -  " if x is None else f"{x:5.2f}"
print("family              res      class            | land avg: att  xSame xVan v/att | no-evid: n  att  xSame xVan  | hotspot: n att xSame xVan")
for f, g in sorted(d["groups"].items()):
    la, ne, ho = g["landAverage"], g["noEvidence"], g["hotspot"]
    c = ",".join(d["famClass"].get(f, []))
    s = f"{f:19s} {g['resource']:8s} {c:16s} | {r(la['earthAttempts'])} {r(la['xSameRocks'])} {r(la['xVanilla'])} {r(la['volPerAttempt'])} | "
    s += (f"{ne['sites']:3d} {r(ne['earthAttempts'])} {r(ne['xSameRocks'])} {r(ne['xVanilla'])} | " if ne else "  none                    | ")
    s += (f"{ho['sites']:3d} {r(ho['earthAttempts'])} {r(ho['xSameRocks'])} {r(ho['xVanilla'])}" if ho else " none")
    print(s)
