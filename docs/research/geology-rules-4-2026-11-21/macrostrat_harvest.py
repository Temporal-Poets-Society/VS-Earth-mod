"""Harvest Macrostrat's carto vector tiles (CC BY 4.0) at one zoom over all land, with resume.
Usage: python -I macrostrat_harvest.py <geology-grids-v1.json> <out dir> [zoom=7] [threads=2]
Land tiles: any 0.5-degree GLiM cell mapped (not 0) inside the tile or one cell around it."""
import sys, os, json, base64, math, time, threading, queue
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mvt

grid_json, out = sys.argv[1], sys.argv[2]
z = int(sys.argv[3]) if len(sys.argv) > 3 else 7
threads = int(sys.argv[4]) if len(sys.argv) > 4 else 2
g = json.load(open(grid_json, encoding="utf-8"))
lith = np.frombuffer(base64.b64decode(next(v for k, v in g.items() if k.lower() == "lithology")), dtype=np.uint8).reshape(360, 720)
land = lith != 0
n = 2 ** z


def lat_of(y):
    return math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * y / n))))


todo = []
for y in range(n):
    north, south = lat_of(y), lat_of(y + 1)
    if south > 85 or north < -85:
        continue
    j0, j1 = max(0, int((90 - north) / .5) - 1), min(360, int((90 - south) / .5) + 2)
    for x in range(n):
        west, east = x / n * 360 - 180, (x + 1) / n * 360 - 180
        i0, i1 = max(0, int((west + 180) / .5) - 1), min(720, int((east + 180) / .5) + 2)
        if land[j0:j1, i0:i1].any():
            todo.append((x, y))
os.makedirs(out, exist_ok=True)
left = [(x, y) for x, y in todo if not os.path.exists(os.path.join(out, f"{z}_{x}_{y}.mvt"))]
print(f"z{z}: {len(todo)} land tiles, {len(left)} to fetch", flush=True)
q = queue.Queue()
for t in left:
    q.put(t)
lock = threading.Lock(); stats = {"ok": 0, "bytes": 0, "fail": []}


def worker():
    while True:
        try:
            x, y = q.get_nowait()
        except queue.Empty:
            return
        for attempt in range(4):
            try:
                data = mvt.fetch("carto", z, x, y)
                p = os.path.join(out, f"{z}_{x}_{y}.mvt")
                with open(p + ".part", "wb") as f:
                    f.write(data)
                os.replace(p + ".part", p)
                with lock:
                    stats["ok"] += 1; stats["bytes"] += len(data)
                    if stats["ok"] % 200 == 0:
                        print(f"  {stats['ok']} tiles, {stats['bytes'] / 1e6:.0f} MB", flush=True)
                break
            except Exception as e:
                time.sleep(3 * (attempt + 1))
        else:
            with lock:
                stats["fail"].append((x, y))
        time.sleep(0.25)


ts = [threading.Thread(target=worker) for _ in range(threads)]
for t in ts: t.start()
for t in ts: t.join()
print(f"done: {stats['ok']} fetched, {stats['bytes'] / 1e6:.0f} MB, failed {len(stats['fail'])}: {stats['fail'][:20]}")
