"""Original provisional ruin blockouts; never installed into the production mod.

Run with Python 3.10+ (Pillow for review PNGs). Output remains under docs/earth-ruins.
These are scale studies, not surveyed replicas or a tested worldgen implementation.
"""
from __future__ import annotations

import csv
import hashlib
import json
import math
from collections import Counter, deque
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "earth-ruins" / "prototypes"
PROFILE = "earth-ruins-half-v1"
PALETTE = {
    0: "game:air",
    1: "game:stonebricks-limestone",
    2: "game:stonebricks-granite",
    3: "game:stonebricks-travertine",
}
COLORS = {1: (191, 167, 119), 2: (123, 119, 111), 3: (195, 176, 145)}


class Model:
    def __init__(self, model_id, title, size, envelope_m, notes):
        self.id, self.title, self.size = model_id, title, size
        self.envelope_m, self.notes = envelope_m, notes
        self.blocks = {}

    def put(self, x, y, z, block):
        assert 0 <= x < self.size[0] and 0 <= y < self.size[1] and 0 <= z < self.size[2]
        self.blocks[x, y, z] = block

    def box(self, x0, y0, z0, x1, y1, z1, block):
        for x in range(x0, x1):
            for y in range(y0, y1):
                for z in range(z0, z1):
                    self.put(x, y, z, block)

    def metadata(self):
        return {
            "modelId": self.id, "title": self.title, "status": "blockout",
            "architectureProfile": PROFILE, "blocksPerMetre": 0.5,
            "sizeXYZ": self.size, "groundY": 1,
            "referenceEnvelopeMetresXYZ": self.envelope_m,
            "dimensionEvidence": "rough planning envelope; not a survey or approved replica",
            "axisConvention": "+X east; +Y up; +Z south; local north -Z",
            "placement": "manual-empty-test-area",
            "historicalCutoffCE": 1500,
            "worldgenEnabled": False, "inGameValidated": False,
            "palette": PALETTE, "notes": self.notes,
            "voxelCountsByCode": {PALETTE[k]: v for k, v in sorted(Counter(self.blocks.values()).items())},
        }

    def export(self):
        # Official BlockSchematic packing: dy<<20 | dz<<10 | dx.
        points = sorted(self.blocks, key=lambda p: (p[1], p[2], p[0]))
        used = sorted(set(self.blocks.values()))
        obj = {
            "GameVersion": "1.22.0", "SizeX": self.size[0], "SizeY": self.size[1], "SizeZ": self.size[2],
            "BlockCodes": {str(k): PALETTE[k] for k in used}, "ItemCodes": {},
            "Indices": [(y << 20) | (z << 10) | x for x, y, z in points],
            "BlockIds": [self.blocks[p] for p in points],
            "DecorIndices": [], "DecorIds": [], "BlockEntities": {}, "Entities": [],
            "ReplaceMode": 1, "EntranceRotation": -1,
        }
        path = OUT / (self.id + ".json")
        path.write_text(json.dumps(obj, separators=(",", ":")), encoding="utf-8")
        metadata = self.metadata()
        metadata["schematicSha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        (OUT / (self.id + ".manifest.json")).write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        return path


def pyramid():
    m = Model("giza-khufu-blockout", "Giza / Khufu scale study", (115, 74, 115), (230, 146, 230), [
        "Rounded massing envelope, not a measured reconstruction.",
        "Original height about 146.5 m documented by Egypt's Ministry of Tourism and Antiquities.",
        "Masonry palette is a vanilla visual substitute; smooth casing and weathering are unfinished.",
        "North base entrance, level passage and test chamber are invented gameplay geometry, not Khufu's actual plan.",
        "Other pyramids, Sphinx, enclosure and local Giza layout are pending.",
    ])
    m.box(0, 0, 0, 115, 1, 115, 1)
    for y in range(1, 74):
        inset = int((y - 1) * 57 / 72)
        m.box(inset, y, inset, 115 - inset, y + 1, 115 - inset, 1)
    m.box(56, 1, 0, 59, 4, 59, 0)
    m.box(53, 1, 53, 62, 7, 64, 0)
    m.box(53, 0, 53, 62, 1, 64, 2)
    return m


def amphitheatre():
    m = Model("rome-amphitheatre-blockout", "Rome / amphitheatre scale study", (94, 25, 78), (188, 48, 156), [
        "Colosseum-sized rough envelope; exact plan and dimension sources remain to be reviewed.",
        "Simplified elliptical arcades and seating with designed missing south-east upper fabric.",
        "64 simplified arcade sectors are a modelling study, not a reconstruction of the historical bay count.",
        "Hypogeum, accessible upper stairs, accurate order details and final ruin treatment are pending.",
    ])
    cx, cz = 46.5, 38.5
    for x in range(94):
        for z in range(78):
            dx, dz = x - cx, z - cz
            r = math.sqrt((dx / 47) ** 2 + (dz / 39) ** 2)
            if r > 1:
                continue
            m.put(x, 0, z, 3)
            angle = math.atan2(dz / 39, dx / 47) % math.tau
            roof = 10 if 0.25 < angle < 1.9 else (18 if 1.9 <= angle < 2.5 else 24)
            bay = (angle / math.tau * 64) % 1
            for y in range(1, 25):
                block = 0
                if r >= 0.925 and y <= roof:
                    block = 3
                    for base in (1, 8, 15):
                        h = y - base
                        # Two vertical sides and a stepped curved crown.
                        if 0 <= h <= 4 and abs(bay - 0.5) < (0.30 if h < 3 else 0.30 - (h - 2) * 0.08):
                            block = 0
                elif 0.84 < r < 0.88 and y <= 15:
                    block = 3 if not (0.2 < bay < 0.8 and y % 7 < 4) else 0
                elif 0.48 < r < 0.76:
                    seat_height = 1 + int((r - 0.48) * 37)
                    block = 3 if y <= seat_height else 0
                # North/south entry routes; these cut seating to the arena floor.
                if abs(dx) < 2 and y <= 3:
                    block = 0
                m.put(x, y, z, block)
    return m


def maya_temple():
    m = Model("tikal-temple-blockout", "Tikal / temple silhouette study", (31, 25, 35), (62, 48, 70), [
        "Tikal-inspired silhouette and rough design envelope, not a replica of a particular temple.",
        "Includes nine diminishing platform tiers, north stair, small summit room and roof-comb mass.",
        "Stair uses full-block risers in this massing pass; proper stairs/slabs required before playability approval.",
        "Final plaza, measured temple plan, vegetation, carving and buried content are pending.",
    ])
    m.box(0, 0, 0, 31, 1, 35, 1)
    # Main pyramid sits back to make room for a separate axial approach stair.
    for tier in range(9):
        inset = tier
        m.box(inset, 1 + 2 * tier, 7 + inset, 31 - inset, 3 + 2 * tier, 35 - inset, 1)
    # Ascending approach reaches the room floor at y=19, preserving the entrance.
    for z in range(0, 20):
        height = min(18, z)
        m.box(13, 1, z, 18, height + 1, z + 1, 1)
    m.box(10, 19, 18, 21, 23, 29, 1)
    m.box(12, 19, 20, 19, 22, 27, 0)
    m.box(14, 19, 18, 17, 22, 21, 0)
    m.box(10, 23, 22, 21, 25, 25, 1)
    return m


def verify_exports(models):
    report = []
    for m in models:
        data = json.loads((OUT / (m.id + ".json")).read_text(encoding="utf-8"))
        assert len(data["Indices"]) == len(data["BlockIds"])
        assert len(set(data["Indices"])) == len(data["Indices"])
        decoded = {}
        for index, block in zip(data["Indices"], data["BlockIds"]):
            p = (index & 1023, (index >> 20) & 1023, (index >> 10) & 1023)
            assert all(0 <= p[i] < m.size[i] for i in range(3)), (m.id, p)
            assert str(block) in data["BlockCodes"]
            decoded[p] = block
        assert decoded == m.blocks, m.id
        # Every solid component must touch the foundation; catches floating roof pieces.
        solid = {p for p, b in decoded.items() if b}
        visited = {p for p in solid if p[1] == 0}
        queue = deque(visited)
        while queue:
            x, y, z = queue.popleft()
            for p in ((x-1,y,z),(x+1,y,z),(x,y-1,z),(x,y+1,z),(x,y,z-1),(x,y,z+1)):
                if p in solid and p not in visited:
                    visited.add(p)
                    queue.append(p)
        assert len(visited) == len(solid), (m.id, "floating geometry")
        report.append({"model": m.id, "sizeXYZ": m.size, "solidBlocks": len(solid),
                       "explicitAir": len(decoded)-len(solid), "roundTrip": "pass", "connectedToFoundation": "pass"})
    # Player-sized (2 wide x 3 high) floor route into pyramid test chamber.
    p = models[0]
    assert all(p.blocks.get((x,y,z)) == 0 for z in range(59) for x in (56,57) for y in (1,2,3))
    assert all(p.blocks.get((x,0,z), 0) != 0 for z in range(59) for x in (56,57))
    catalog = list(csv.DictReader((OUT.parent / "site-catalog.csv").open(encoding="utf-8", newline="")))
    assert len({r["site_id"] for r in catalog}) == len(catalog)
    for row in catalog:
        assert None not in row, row
        assert -90 <= float(row["latitude"]) <= 90 and -180 <= float(row["longitude"]) <= 180
    result = {"models": report, "catalogueRows": len(catalog), "regions": len({r["region"] for r in catalog}),
              "pyramidTestPassage": "2-wide x 3-high route and floor pass",
              "notVerified": ["native in-game import", "finished historical accuracy", "worldgen placement",
                              "full circulation and stair usability", "weathering", "block mechanics in game"]}
    (OUT / "validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def render(models):
    from PIL import Image, ImageDraw, ImageFont
    font_path = Path("C:/Windows/Fonts/segoeui.ttf")
    font = ImageFont.truetype(str(font_path), 26)
    small = ImageFont.truetype(str(font_path), 18)
    # Every panel uses the same pixels per block, unlike a fit-to-panel preview.
    scale = 2.7
    canvas = Image.new("RGB", (1800, 750), (242, 238, 227))
    draw = ImageDraw.Draw(canvas)
    draw.text((38, 24), "EARTH RUINS  /  shared-scale starter blockouts", font=font, fill=(43, 48, 41))
    draw.text((38, 66), "0.5 blocks per real metre  |  same camera scale in every panel  |  unfinished geometry studies", font=small, fill=(70, 77, 65))
    faces = [((1,0,0), ((1,0,0),(1,0,1),(1,1,1),(1,1,0)), 0.76),
             ((0,0,-1), ((0,0,0),(1,0,0),(1,1,0),(0,1,0)), 0.90),
             ((0,1,0), ((0,1,0),(1,1,0),(1,1,1),(0,1,1)), 1.12)]
    for col, m in enumerate(models):
        ox, oy = 300 + col * 600, 530
        def project(x, y, z):
            x -= m.size[0] / 2
            z = m.size[2] / 2 - z
            return (ox + (x-z) * 0.866 * scale, oy + ((x+z) * 0.5-y) * scale)
        visible = []
        for (x,y,z), b in m.blocks.items():
            if not b:
                continue
            for normal, verts, shade in faces:
                if not m.blocks.get((x+normal[0],y+normal[1],z+normal[2]), 0):
                    points = [project(x+vx,y+vy,z+vz) for vx,vy,vz in verts]
                    color = tuple(min(255, int(v*shade)) for v in COLORS[b])
                    depth = x-z+y + (normal[0]-normal[2]+normal[1])*0.5
                    visible.append((depth, points, color))
        for _, points, color in sorted(visible, key=lambda f: f[0]):
            draw.polygon(points, fill=color)
        draw.text((38+col*600, 122), m.title, font=font, fill=(43,48,41))
        draw.text((38+col*600, 158), f"{m.size[0]} W x {m.size[2]} D x {m.size[1]-1} H blocks + foundation", font=small, fill=(70,77,65))
        # True block-size scale marker on each panel.
        draw.line((42+col*600, 697, 42+col*600+20*scale, 697), fill=(43,48,41), width=3)
        draw.text((42+col*600, 707), "20 blocks", font=small, fill=(43,48,41))
    canvas.save(OUT / "scale-review.png")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    models = [pyramid(), amphitheatre(), maya_temple()]
    for model in models:
        model.export()
    report = verify_exports(models)
    render(models)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
