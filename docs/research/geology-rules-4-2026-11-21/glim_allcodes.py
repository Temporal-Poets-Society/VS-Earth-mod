"""All GLiM Litho codes with their share of land area, and the geodatabase's layers."""
import sys, collections, pyogrio, pyogrio.raw as pr
p = sys.argv[1]
print(pyogrio.list_layers(p))
meta, fids, geom, fields = pr.read(p, layer="GLiM_export", read_geometry=False, columns=["Litho", "Shape_Area"])
lith, area = fields
tot = area.sum(); c = collections.Counter(); n = collections.Counter()
for l, a in zip(lith, area): c[l or ""] += a; n[l or ""] += 1
with open(sys.argv[2], "w", encoding="utf-8") as f:
    for k, v in sorted(c.items()): f.write(f"{k}\t{100*v/tot:.4f}\t{n[k]}\n")
print(len(c), "codes")
