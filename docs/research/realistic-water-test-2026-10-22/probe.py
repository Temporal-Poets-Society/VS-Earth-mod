# Reads the probe dumps of a run: probe.py <wateraudit dir> -> per site: water columns, levels, and the best trench/dam spots
import gzip, json, os, sys
import numpy as np

d = sys.argv[1]
codes = {int(k): v for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}
for f in sorted(os.listdir(d)):
    if not f.startswith("probe_"):
        continue
    with gzip.open(os.path.join(d, f)) as g:
        b = g.read()
    x0, z0, n = np.frombuffer(b[:12], dtype=np.int32)
    a = np.frombuffer(b[12:], dtype=np.int32).reshape(n, n, 3)
    ter, top, fid = a[..., 0], a[..., 1], a[..., 2]
    names = np.vectorize(lambda i: codes.get(int(i), "-").split(":")[-1] if i else "-")(fid)
    fresh = np.char.startswith(names.astype(str), "water-")
    still = names == "water-still-7"
    print("==", f, "x0", x0, "z0", z0, "n", n)
    print("  fresh water columns", int(fresh.sum()), "still7", int(still.sum()), "salt", int(np.char.startswith(names.astype(str), "saltwater").sum()))
    if still.any():
        lv, c = np.unique(top[still], return_counts=True)
        print("  still levels", dict(zip(lv.tolist(), c.tolist())))
        wt = int(lv[np.argmax(c)])
        land = (top < 0)
        print("  land columns", int(land.sum()), "land terrain - wt hist", dict(zip(*[v.tolist() for v in np.unique(np.clip(ter[land] - wt, -5, 10), return_counts=True)])))
        # longest straight land run at or above the water level, starting next to still water, per direction
        best = []
        for (dx, dz) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            for i in range(n):
                for j in range(n):
                    if not still[i, j]:
                        continue
                    wt_ = top[i, j]
                    k = 1
                    while 0 <= i + dx * k < n and 0 <= j + dz * k < n and top[i + dx * k, j + dz * k] < 0 and ter[i + dx * k, j + dz * k] >= wt_:
                        k += 1
                    best.append((k - 1, i, j, dx, dz))
        best.sort(reverse=True)
        print("  best land runs from still water:", [(k, int(x0) + i, int(z0) + j, dx, dz) for k, i, j, dx, dz in best[:5]])
        # map: . land, ~ still, f flowing, r rapid, around centre (every 3rd column)
        rows = []
        for j in range(0, n, 4):
            rows.append("".join("~" if still[i, j] else "f" if fresh[i, j] else "r" if names[i, j].startswith("rapid") else "s" if names[i, j].startswith("salt") else
                                ("^" if ter[i, j] >= wt + 1 else "_") for i in range(0, n, 2)))
        print("\n".join("   " + r for r in rows))
