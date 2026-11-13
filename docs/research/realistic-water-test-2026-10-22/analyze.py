# Compares the wateraudit dumps of one or more runs: analyze.py <runs dir> [world ...] -> summary.json + printed tables
import gzip, json, os, sys
import numpy as np

runs = sys.argv[1]
worlds = sys.argv[2:] or sorted(d for d in os.listdir(runs) if os.path.isdir(os.path.join(runs, d, "wateraudit")))


def load(path):
    with gzip.open(path, "rb") as f:
        b = f.read()
    x0, y0, z0, sx, sy, sz = np.frombuffer(b[:24], dtype=np.int32)
    a = np.frombuffer(b[24:], dtype=np.int32).reshape(sx, sz, sy, 2)
    return (int(x0), int(y0), int(z0)), a[..., 0], a[..., 1]


def family(code):
    if code is None:
        return "none"
    p = code.split(":")[-1]
    if p.startswith(("water-still-7", "saltwater-still-7")):
        return p.split("-")[0] + "-source"
    for f in ("rapidwater", "realisticwater", "saltwater", "water", "boilingwater", "lava"):
        if p.startswith(f + "-"):
            return f + ("-falling" if p.split("-")[1] == "d" else "-source" if "-still-7" in p else "-flowing")
    return "other"


def level(code):
    if code is None:
        return 0
    parts = code.split(":")[-1].split("-")
    try:
        return int(parts[2])
    except (IndexError, ValueError):
        return 0


summary = {}
for w in worlds:
    d = os.path.join(runs, w, "wateraudit")
    codes = {int(k): v for k, v in json.load(open(os.path.join(d, "blocks.json"))).items()}
    plans = json.load(open(os.path.join(d, "plan.json")))
    fam = np.vectorize(lambda i: family(codes.get(int(i))) if i > 0 else "none", otypes=[object])
    lvl = np.vectorize(lambda i: level(codes.get(int(i))) if i > 0 else 0)
    ws = {"world": json.load(open(os.path.join(d, "world.json")))}
    for p in plans:
        name = p["name"]
        tags = sorted((f[len(name) + 1:-7] for f in os.listdir(d) if f.startswith(name + "_") and f.endswith(".bin.gz")), key=lambda t: int(t[1:]))
        (x0, y0, z0), s0, f0 = load(os.path.join(d, f"{name}_t0.bin.gz"))
        cx, cz, wt = p["x"] - x0, p["z"] - z0, p["wt"] - y0
        res = {"plan": p, "tags": {}}

        def cell(fl, x, y, z):
            i = fl[x, z, y]
            return codes.get(int(i)) if i > 0 else None

        def trench_cells(k0, k1, width, y):
            out = []
            for k in range(k0, k1 + 1):
                row = []
                for wi in range(width):
                    x = cx + p["dx"] * k + p["px"] * wi
                    z = cz + p["dz"] * k + p["pz"] * wi
                    row.append(cell(fl, x, y, z))
                out.append(row)
            return out

        for t in tags:
            _, s, fl = load(os.path.join(d, f"{name}_{t}.bin.gz"))
            changed = f0 != fl
            r = {"fluidCellsChanged": int(changed.sum()), "solidCellsChanged": int((s0 != s).sum())}
            ys = np.arange(fl.shape[2])
            wet0 = (f0 > 0)
            wet = (fl > 0)
            top0 = np.where(wet0.any(2), fl.shape[2] - 1 - np.argmax(wet0[:, :, ::-1], axis=2), -1)
            top = np.where(wet.any(2), fl.shape[2] - 1 - np.argmax(wet[:, :, ::-1], axis=2), -1)
            dtop = top - top0
            r["columnsWetter"] = int(((top0 < 0) & (top >= 0)).sum())
            r["columnsDried"] = int(((top0 >= 0) & (top < 0)).sum())
            r["columnsSurfaceUp"] = int(((top0 >= 0) & (top >= 0) & (dtop > 0)).sum())
            r["columnsSurfaceDown"] = int(((top0 >= 0) & (top >= 0) & (dtop < 0)).sum())
            if changed.any():
                xs, zs, _ = np.nonzero(changed)
                r["maxChangeDist"] = int(np.max(np.abs(xs - cx) + np.abs(zs - cz)))
            # fluid families in the box, changes only
            if changed.any():
                a, b = fam(f0[changed]), fam(fl[changed])
                pairs = {}
                for u, v in zip(a, b):
                    pairs[f"{u} -> {v}"] = pairs.get(f"{u} -> {v}", 0) + 1
                r["changes"] = dict(sorted(pairs.items(), key=lambda kv: -kv[1])[:12])
            kind = p["kind"]
            if kind in ("river_trench", "sea_trench"):
                cells = trench_cells(1, p["length"], p["width"], wt)
                r["trenchWet"] = sum(1 for row in cells if any(c for c in row))
                r["trenchSourceRows"] = sum(1 for row in cells if all(c and family(c).endswith("source") for c in row))
                r["trenchProfile"] = "".join("S" if all(c and family(c).endswith("source") for c in row) else
                                             "f" if any(c for c in row) else "." for row in cells)
            if kind == "mill":
                cells = trench_cells(1, 20, 2, wt)
                r["channelProfile"] = "".join("S" if all(c and family(c).endswith("source") for c in row) else "f" if any(c for c in row) else "." for row in cells)
                pit = [cell(fl, cx + p["dx"] * 21, y, cz + p["dz"] * 21) for y in range(wt - 14, wt + 1)]
                r["pitColumn"] = [family(c)[0:6] + str(level(c)) if c else "." for c in pit]
                tun = trench_cells(23, 46, 2, wt - 14)
                r["tunnelProfile"] = "".join("R" if any(c and c.split(":")[-1].startswith("rapidwater") and family(c) == "rapidwater-flowing" for c in row)
                                             else "S" if all(c and family(c).endswith("source") for c in row)
                                             else "f" if any(c for c in row) else "." for row in tun)
                r["tunnelCodes"] = sorted({c.split(":")[-1] for row in tun for c in row if c})[:20]
            if kind == "dam":
                ax, az, fx, fz, lo, hi = p["ax"], p["az"], p["fx"], p["fz"], p["lo"], p["hi"]
                prof = {}
                for f in range(-50, 52, 1):
                    vals = []
                    for a_ in range(lo, hi + 1):
                        x, z = cx + ax * a_ + fx * f, cz + az * a_ + fz * f
                        if 0 <= x < fl.shape[0] and 0 <= z < fl.shape[1]:
                            vals.append(int(dtop[x, z]) if top0[x, z] >= 0 and top[x, z] >= 0 else (-99 if top[x, z] < 0 and top0[x, z] >= 0 else 0))
                    prof[f] = vals
                r["damRowsChanged"] = {f: v for f, v in prof.items() if any(v)}
            if kind == "ramp":
                ramp0 = (fam(f0) == "water-flowing")
                rampt = np.isin(fam(fl), ["water-flowing", "realisticwater-flowing"])
                r["vanillaFlowingCells"] = [int(ramp0.sum()), int((fam(fl) == "water-flowing").sum())]
                r["realisticFlowingCells"] = int((fam(fl) == "realisticwater-flowing").sum())
            fl_f = fam(fl[wet])
            r["boxFamilies"] = {k: int(v) for k, v in zip(*np.unique(fl_f, return_counts=True))}
            res["tags"][t] = r
        ws[name] = res
    wl = os.path.join(d, "wheels.json")
    if os.path.exists(wl):
        ws["wheels"] = json.load(open(wl))
    lg = os.path.join(d, "lag.json")
    if os.path.exists(lg):
        ws["lag"] = json.load(open(lg))
    summary[w] = ws

json.dump(summary, open(os.path.join(runs, "summary.json"), "w"), indent=1, default=str)
for w, ws in summary.items():
    print("=" * 20, w, ws["world"])
    for name, res in ws.items():
        if name in ("world", "wheels", "lag"):
            continue
        p = res["plan"]
        print(f"-- {name} ({p['kind']}) at {p['lat']},{p['lon']} block {p['x']},{p['wt']},{p['z']}")
        for t, r in res["tags"].items():
            short = {k: v for k, v in r.items() if k not in ("boxFamilies", "changes", "damRowsChanged", "tunnelCodes")}
            print("  ", t, short)
        last = list(res["tags"].values())[-1]
        print("    changes:", last.get("changes"))
        if "damRowsChanged" in last:
            print("    dam rows changed:", {k: v for k, v in list(last["damRowsChanged"].items())[:20]})
        if "tunnelCodes" in last:
            print("    tunnel codes:", last["tunnelCodes"])
    print("wheels:", [x for x in ws.get("wheels", []) if x["t"] in (2, 14, 58, 298, 598)])
    print("lag:", ws.get("lag"))
