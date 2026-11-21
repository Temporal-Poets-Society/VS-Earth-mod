"""
The ledger of human works Earth undoes (decision 0064; the author, 21 November 2026: "if we do modifications to data to
change any manmade then we should have some ledger listing changes and how"): writes docs/works-1318-ledger.md from the
entries below and the data files the mod ships, so its tables are what the build does. Its last section, a block of
facts (the files' sizes and SHA-256, the works files' areas by kind, the terminal lakes' levels, the lakes of 1318, the
rules versions), is checked against the files and the code by tests/DataReadersTests (LedgerChecks.cs): change any of
them, or add a change of this kind, and the ledger has to be written again (with its entry here) before the tests pass.

    .venv/Scripts/python -I tools/works_ledger.py           write docs/works-1318-ledger.md
    .venv/Scripts/python -I tools/works_ledger.py --check   exit 1 when the page isn't what this would write
"""
import ast, hashlib, json, math, os, re, struct, sys

import brotli

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAGE = os.path.join(ROOT, "docs", "works-1318-ledger.md")
ED = os.path.join(ROOT, "earthdata")
SEA, LAKE, LAND, FEN, CANAL, LATE, SALT = 16, 32, 64, 128, 8, 4, 1

# Every change Earth makes to its data to undo a human work, or a human work it leaves on purpose. One entry per place or
# group. A new change of this kind needs its entry here (and the page written again) in the same commit.
ENTRIES = [
    dict(id="netherlands", title="The Netherlands: the Zuiderzee, the Delta's arms and the drained lakes",
         work="Land won from the sea and from lakes after 1318: the Zuiderzee works and the Afsluitdijk (1920-1968), the "
              "Wadden and Zeeland polders, the Delta Works' dams, the North Holland lakes pumped dry (Beemster 1612, Purmer "
              "1622, Wormer 1626, Schermer 1635, the Haarlemmermeer 1849-1852); and lakes dug in the peat after 1318 "
              "(Loosdrecht, the Vinkeveen and Reeuwijk lakes, gravel pits).",
         earth="Areas of `works-1318-2.bin` (works rules 1-4) and `works-1318-3.bin` (rules 5): SEA areas are sea over "
               "today's land, LAKE areas lakes at 0 m, LAND areas land over today's peat-digging lakes (table A). "
               "Holland's sunken peat polders need nothing: the game holds land below the sea at the sea's level.",
         source="RCE paleogeographic maps of 1250, 1500 and 2000 AD (Vos et al., Atlas van Nederland in het Holoceen), "
                "CC BY 4.0, compared class by class.", confidence="high (the map nearest 1318 is 1250's; natural change "
                "after it, the storm floods of 1362 and 1421, kept from 1500's)",
         rules="`Works1318` (EarthParams 106) 1-5; on wherever the coasts come from their outlines (1:100 and finer)",
         decision="[0064](decisions/0064-coasts-and-waters-as-of-1318.md) step 1"),
    dict(id="maasvlakte", title="Maasvlakte 2 and the dunes on Maasvlakte 1",
         work="Rotterdam's port land built in the North Sea, 1965-2013 (after the RCE map of 2000).",
         earth="One SEA area (flagged LATE in `works-1318-2.bin`/`-3.bin`): the water mask's land on the sea of 1318 "
               "inside the works' box; the island layer draws no land there.",
         source="Copernicus GLO-90 water mask (as Earth's heights), RCE 1250/1500 for the sea of 1318.",
         confidence="high", rules="`Works1318` 4 and later", decision="0064, works rules 4 (EA-0053)"),
    dict(id="canals", title="Canals dug after 1318 (OpenStreetMap)",
         work="Ship and irrigation canals built from the 18th century on (table B, with the years).",
         earth="LAND|CANAL areas: the water mask's river water inside each canal's corridor is land; lake water a canal "
               "passes through stays (the Kiel Canal's lakes). The Nieuwe Waterweg is kept (table B).",
         source="OpenStreetMap (ODbL; Overpass 12 November 2026, QLever 13 November), cut to the GLO-90 water mask.",
         confidence="high for the 29 named canals; smaller canals the mask draws as river stay (not in the list)",
         rules="`Works1318` 2 and later (the Corinth, White Sea-Baltic and Ismailia canals from 4)",
         decision="[0069](decisions/0069-river-flow-field-canals-and-shore-zones.md) rule 6, 0064"),
    dict(id="suez-panama", title="The Suez and Panama canals, the Bitter Lakes, Lake Timsah and Gatun Lake",
         work="The Suez Canal (1859-1869; the Bitter Lakes and Lake Timsah, dry before, flooded by it) and the Panama "
              "Canal (1904-1914; Gatun Lake, its reservoir).",
         earth="`coasts.bin` closes the canal gaps in GSHHG's outlines (312 km² of water made land at Suez, 424 at "
               "Panama); channel rules 1 and later draw no outline lake or river water and no grid lake in the two canal "
               "zones (`SeaChannels.InCanalZone`): the Bitter Lakes' bed is a dry plain at sea level, no Gatun Lake. "
               "The Earth tab's canal option cuts a gameplay canal (not the real one).",
         source="GSHHG (LGPL), Earth's own canal boxes.", confidence="high (the Bitter Lakes' salt-crust floor, some "
                                                                       "metres below the sea, is not drawn)",
         rules="`Coasts` (outline coasts), `ChannelRules` 1 and later", decision="[0020](decisions/0020-canal-gaps.md), "
         "[0063](decisions/0063-rivers-follow-the-real-channel.md)"),
    dict(id="reservoirs", title="Reservoirs behind dams built after 1318",
         work="Reservoirs worldwide, nearly all of the 19th to 21st centuries.",
         earth="Left out of the water: the GLO-90 lake outlines leave out every HydroLAKES reservoir and Global Dam "
               "Watch's reservoirs that HydroLAKES lacks (24,459 lakes, 283,086 km², in `lakes-glo90-2.bin`; carried on "
               "to `-5`), and the river outlines leave out their river water; the 2' grid drops the Natural Earth lakes "
               "Wikidata calls reservoirs and Lake Razazah (`reservoirs-v1.json`). Lakes a dam only raised (HydroLAKES "
               "counts some, Moosehead Lake) keep their water at today's level.",
         source="HydroLAKES 1.0 (CC BY 4.0), Global Dam Watch 1.0 (CC BY 4.0; barriers built after 1318 with a "
                "reservoir from 0.1 km², 1,015 of them on lakes HydroLAKES has as natural: built from 1900, 15 m or "
                "higher), Wikidata (CC0).", confidence="high for the big ones; reservoirs without a HydroLAKES dam point "
                                                           "and water more than 3 m above the reservoir's level stay river",
         rules="`LakeOutlines` 2 and later (`lakes-glo90-*.bin`), `RiverOutlines`, `ReservoirRules` 1 (the grid's)",
         decision="[0053](decisions/0053-scales-included-glo90-and-glo90-rivers.md) items 17-18, 0069 rule 9"),
    dict(id="dams", title="Dams and the valleys under their reservoirs",
         work="The dam walls and the flat water GLO-90 measured on the reservoirs.",
         earth="`dams-glo90-*.bin` (the data release `egl-v1`): per GLO-90 square, the heights that change: walls taken "
               "down, the valley floor rebuilt under the reservoir with the river's course falling evenly from where it "
               "comes in to the foot of each dam (3,488 squares in rules 3).",
         source="Copernicus GLO-90, HydroLAKES pour points, Global Dam Watch 1.0 (CC BY 4.0).",
         confidence="medium: the old valley is rebuilt, not measured (no pre-dam heights exist)",
         rules="`DamRules` (EarthParams 105) 1-4", decision="0053 item 19, 0069 rule 9, [0092](decisions/0092-water-on-glo90s-own-grid.md)"),
    dict(id="terminal-1960", title="Terminal lakes dried by irrigation and dams in the 20th century (decision 0005)",
         work="Rivers drained into irrigation (the Amu Darya and Syr Darya, the Tarim, the Helmand, the Kor, the Owens "
              "River, the Kings and Kern, ...) left their end lakes dry or shrunk, mostly after 1950 (table C).",
         earth="`lakes.bin` fills each basin on the 2' grid to a level before the drying (\"revived\"), and the rivers "
               "run into it at that level; the natural salt pans in the same file are filled to a wet-year level (not a "
               "human work). Green shores where the lakes had them.",
         source="ETOPO 2022 (the basins), the levels from the lakes' records (decision 0005).",
         confidence="medium: the level is the lake's of about 1960 (1870s for Tulare, 1913 for Owens), not 1318's; for "
                    "the Aral, 1318's replaces it (next entry)",
         rules="`RiverEnds` (EarthParams 57) 1-2 read `lakes.bin`; 3 read `lakes-2.bin`",
         decision="[0005](decisions/0005-river-ends.md)"),
    dict(id="aral-1318", title="The Aral Sea at its level of 1318",
         work="The 20th-century irrigation drying (as above); in 1318 the lake was at its medieval low anyway (the Amu "
              "Darya ran mostly to the Sarykamysh after Khwarezm's dikes were broken in 1221).",
         earth="At 29 m instead of 1960's 53 m: on the 2' grid in `lakes-2.bin` (the western basin, its other 1960 cells "
               "the grid's own again), and in GLO-90 worlds a LAKE area of `works-1318-4.bin` contoured from GLO-90 at "
               "29 m (the western basin and the eastern basin's lagoon, table D). The rivers end in it.",
         source="Krivonogov et al. 2014 (Gondwana Research 26): about 29 m (27-30) in the 12th-15th centuries; "
                "Copernicus GLO-90.", confidence="medium (the level from cores and the medieval delta; no outline exists)",
         rules="`RiverEnds` 3, `Works1318` 6", decision="[0107](decisions/0107-lakes-of-1318.md)"),
    dict(id="drained-1318", title="Lakes drained after 1318",
         work="Lakes pumped or tunnelled dry from the 17th to the 20th century (table D, with the years).",
         earth="LAKE areas of `works-1318-4.bin` at their levels (table D): water wherever the area lies, today's ponds "
               "and reservoirs inside it gone, the land beside held at its top, the rivers inside at its level.",
         source="OpenHistoricalMap (CC0) and OpenStreetMap (ODbL) outlines, or Copernicus GLO-90 contoured at the level "
                "where the ground has not sunk; the levels from the sources in table D.",
         confidence="medium: the outlines are the last state before drainage, which can differ from 1318's (Fucino was "
                    "low in the 16th century; Ogura-ike's is its 1933 remnant)",
         rules="`Works1318` 6", decision="0107"),
    dict(id="salton", title="The Salton Sea",
         work="Made by the Colorado River's breach of an irrigation canal in 1905-1907.",
         earth="A LAND area of `works-1318-4.bin`: its basin is dry land (GLO-90's surface, the sea's flat at about "
               "-71 m).", source="Rockwell et al. 2022: Lake Cahuilla stood high before 1241 and in 1459-1503, not in "
                                 "1318.", confidence="medium", rules="`Works1318` 6", decision="0107"),
]

# Human works Earth leaves as they are, and why (one line each).
LEFT = [
    ("The North Aral at 41 m", "the Kokaral dam of 2005 holds it; the Small Aral of 1318 was low, but its bed under "
     "today's water is unknown (0107)"),
    ("Tulare Lake at 61 m", "decision 0005's 1870s lake; the lake of about 1300 was near a medieval low, with no level or "
     "extent in the sources (0107)"),
    ("Reservoirs older than 1318", "dropped with the rest (Parakrama Samudra, Minneriya): HydroLAKES has no dates; "
     "0064's keep list of them is not built yet"),
    ("Reclamation since 1985", "Palm Jumeirah, the airports at Chek Lap Kok, Kansai and Incheon, Tuas, Caofeidian: "
     "0064 step 2, not built"),
    ("Polders below the sea outside the Netherlands", "0064 step 4 (the polder rule), not built; the Fens and the "
     "Sacramento delta are land at sea level"),
    ("Ponds and pits", "opencast-mine lakes, gravel pits, fish and salt ponds the water mask draws as lakes; no "
     "classified source yet (0064 R3)"),
    ("Lakes made after 1318 not yet listed", "the Aydar-Arnasay lakes (1969), Lake Nasser's Toshka lakes, Mareotis "
     "(dry in 1318), Abu Qir (formed about 1770-1784)"),
    ("Other drained lakes", "Tule and Lower Klamath lakes, La Janda, Kolindsund, Martin Mere, the other Fens meres, "
     "Bientina, Comacchio's reclaimed valli, Dongting and Poyang's polders: no quick open outline or level yet "
     "(docs/research/lakes-1318-2026-11)"),
    ("Rivers moved by works", "the Aare into Lake Biel (1878), the Linth into the Walensee (1811), the Kander into Lake "
     "Thun (1714), the Nene by Morton's Leam, the Syr Darya's and Amu Darya's courses of 1318: course edits, not built"),
    ("The Mesopotamian marshes", "drained in the 1990s; decision 0005's open item"),
    ("The Bitter Lakes' surface", "dry (above), but not its salt crust nor its depth below the sea"),
]

# what drained when, and the source of each lake's level, for table D
DRAINED = {
    "aral": "the Kerderi low stand, 12th-15th c.",
    "texcoco": "Huehuetoca tunnel 1607-08, Tajo de Nochistongo by 1789, Gran Canal 1900",
    "xochimilco": "as Texcoco; springs captured 1900s", "chalco": "as Texcoco; 1890s", "xaltocan": "cut off by 1607-1789",
    "zumpango": "the Cuautitlan diverted 1607", "fucino": "Torlonia tunnel 1855-1878", "copais": "1880s-1931",
    "hula": "1951-1958", "antela": "1958 on", "whittlesey": "1851", "stretham": "by 1804", "ogura": "1933-1941",
    "karla": "tunnel 1962", "amik": "1954-1975", "hachirogata": "1957-1977", "salton": "made 1905-1907",
}


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def varint(b, o):
    v = s = 0
    while True:
        x = b[o]; o += 1
        v |= (x & 0x7F) << s
        s += 7
        if not x & 0x80:
            return v, o


def zz(v):
    return (v >> 1) ^ -(v & 1)


def works_areas(path):
    """Every area piece of a works file: (flags, level m, km²)."""
    data = open(path, "rb").read()
    magic, version, unit, deg, tx, ty = struct.unpack_from("<4siiiii", data, 0)
    states = data[24:24 + tx * ty]
    o = 24 + tx * ty
    out = []
    for t in range(tx * ty):
        if states[t] != 1:
            continue
        off, ln = struct.unpack_from("<ii", data, o)
        o += 8
        raw = brotli.decompress(data[off:off + ln])
        lat0 = t // tx * deg - 90
        n, k = varint(raw, 0)
        for _ in range(n):
            lv, k = varint(raw, k)
            fl, k = varint(raw, k)
            nr, k = varint(raw, k)
            px = py = 0
            area = 0.0
            for _r in range(nr):
                m, k = varint(raw, k)
                pts = []
                for _j in range(m):
                    dx, k = varint(raw, k)
                    dy, k = varint(raw, k)
                    px += zz(dx); py += zz(dy)
                    pts.append((px, py))
                s = 0.0
                for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]):
                    s += x0 * y1 - x1 * y0
                ymid = sum(p[1] for p in pts) / max(1, len(pts))
                area += s / 2 / unit / unit * 111.32 ** 2 * math.cos(math.radians(lat0 + ymid / unit))
            out.append((fl, zz(lv) / 10.0, abs(area)))
    return out


def terminal_lakes(path):
    data = open(path, "rb").read()
    version, w, n, nl = struct.unpack_from("<iiii", data, 4)
    b = brotli.decompress(data[20:])
    o = 0
    idx = []
    acc = 0
    for _ in range(n):
        d, o = varint(b, o)
        acc += d
        idx.append(acc)
    levels = struct.unpack_from(f"<{n}h", b, o + 2 * n)
    flags = b[o + 4 * n:o + 5 * n]
    o += 5 * n
    lakes = []
    while o < len(b):
        level, lat, lon, shore, kind, salt, ln = struct.unpack_from("<ffffBBB", b, o)
        lakes.append({"name": b[o + 19:o + 19 + ln].decode("utf-8"), "level": level, "lat": lat, "lon": lon, "shore": shore,
                      "revived": kind == 1, "salt": salt != 0})
        o += 19 + ln
    return lakes, idx, levels, flags, w


def literal(path, name):
    tree = ast.parse(open(path, encoding="utf-8").read())
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(getattr(t, "id", None) == name for t in node.targets):
            return ast.literal_eval(node.value)
    raise SystemExit(f"{path}: no {name}")


def const(path, pattern):
    m = re.search(pattern, open(os.path.join(ROOT, path), encoding="utf-8").read())
    if not m:
        raise SystemExit(f"{path}: {pattern} not found")
    return int(m.group(1))


def kind_name(fl):
    k = "sea" if fl & SEA else "lake" if fl & LAKE else "land" if fl & LAND else "fen" if fl & FEN else "?"
    return k + ("+canal" if fl & CANAL else "") + ("+late" if fl & LATE else "") + ("+salt" if fl & SALT and fl & LAKE else "")


def build():
    files = ["works-1318-2.bin", "works-1318-3.bin", "works-1318-4.bin", "lakes.bin", "lakes-2.bin", "reservoirs-v1.json"]
    pins = {f: (os.path.getsize(os.path.join(ED, f)), sha(os.path.join(ED, f))) for f in files}
    works = {f: works_areas(os.path.join(ED, f)) for f in files[:3]}
    t1, i1, l1, f1, w = terminal_lakes(os.path.join(ED, "lakes.bin"))
    t2, *_ = terminal_lakes(os.path.join(ED, "lakes-2.bin"))
    boxes = {x[0]: x[6] for x in literal(os.path.join(ROOT, "tools", "bake_earth.py"), "TERMINAL_LAKES")}
    canals = literal(os.path.join(ROOT, "tools", "fetch_osm_canals.py"), "CANALS")
    spec = json.load(open(os.path.join(ROOT, "tools", "lakes-1318.json"), encoding="utf-8"))["areas"]
    report = json.load(open(os.path.join(ROOT, "tools", "lakes-1318", "bake-report.json"), encoding="utf-8"))
    if report["sha256"] != pins["works-1318-4.bin"][1]:
        raise SystemExit("tools/lakes-1318/bake-report.json is not works-1318-4.bin's bake: bake it again with --report")
    km2_of = {a["key"]: a["km2"] for a in report["areas"]}
    rules = {
        "Works1318": const("src/Core/Works1318.cs", r"public const int RulesVersion = (\d+);"),
        "RiverEnds": const("src/Core/RiverEnds.cs", r"public const int Version = (\d+);"),
        "DamRules": const("src/Core/DamRemoval.cs", r"public const int NewestRules = Rules(\d+);"),
        "ChannelRules": const("src/Core/TerrainModel.cs", r"public const int ChannelRulesVersion = (\d+);"),
    }

    L = []
    w_ = L.append
    w_("# The ledger of human works undone")
    w_("")
    w_("Earth builds the world as around 1318 (decision [0064](decisions/0064-coasts-and-waters-as-of-1318.md)): where people "
       "changed the coasts and waters after 1318, the data is changed back, and natural change after 1318 stays. This page "
       "lists every such change Earth makes, and the human works it leaves as they are, with the reason. Every change is "
       "for new worlds only: a saved world keeps the rules versions it was made with (decision 0002), so its land never "
       "changes.")
    w_("")
    w_("Written by `tools/works_ledger.py` from its entries and the files the mod ships; its facts block (the last section) "
       "is checked against the files and the code by `tests/DataReadersTests`. A new change of this kind, or a change to "
       "one of these files, comes with its entry there and this page written again, in the same commit (Earth's AGENTS.md).")
    w_("")
    w_("## The changes")
    w_("")
    for e in ENTRIES:
        w_(f"### {e['title']}")
        w_("")
        w_(f"- **The work:** {e['work']}")
        w_(f"- **What Earth does:** {e['earth']}")
        w_(f"- **Source and licence:** {e['source']}")
        w_(f"- **Confidence:** {e['confidence']}")
        w_(f"- **Rules:** {e['rules']}; new worlds only")
        w_(f"- **Decision:** {e['decision']}")
        w_("")
    w_("## Left as they are")
    w_("")
    w_("| Human work | Why it stays |")
    w_("|---|---|")
    for a, b in LEFT:
        w_(f"| {a} | {b} |")
    w_("")

    # table A: the works files by kind
    w_("## Table A: the works files' areas")
    w_("")
    w_("Area pieces (an area is cut at the 1° tiles) and their surface, by kind. Each file keeps the one before byte for "
       "byte and adds its own after it (`works-1318-3.bin` moves the canals and Maasvlakte 2 onto GLO-90's own grid).")
    w_("")
    w_("| File | Kind | Pieces | km² |")
    w_("|---|---|---|---|")
    for f, areas in works.items():
        by = {}
        for fl, lv, km in areas:
            k = kind_name(fl & ~SALT)
            n, s = by.get(k, (0, 0.0))
            by[k] = (n + 1, s + km)
        for k in sorted(by):
            w_(f"| `{f}` | {k} | {by[k][0]} | {by[k][1]:,.0f} |")
    w_("")

    # table B: the canals
    w_("## Table B: the canals")
    w_("")
    w_("| Canal | Built | Undone | From works rules |")
    w_("|---|---|---|---|")
    for key, regex, built, note, *opt in canals:
        o = opt[0] if opt else {}
        # the canal's own name from the OSM pattern (its first spelling), with the list's note
        local = re.sub(r"\(([^|)]*)(\|[^)]*)?\)\??", r"\1", regex).split("|")[0].strip("^$ ").replace(".*", "-")
        name = local + (f" ({note})" if note and note != local else "")
        w_(f"| {name} | {built} | {'no: the Maas mouth was open water in 1318 (EA-0057)' if o.get('undo') is False else 'yes'} | {o.get('rules', 2)} |")
    w_("")

    # table C: the terminal lakes
    w_("## Table C: the terminal lakes (decision 0005)")
    w_("")
    w_("Each lake's level in `lakes.bin` (river ends rules 1-2) and `lakes-2.bin` (rules 3), whether it is revived (dried "
       "by people) or natural (a salt pan filled to a wet year), and the 2′ grid cells the file makes lake there (Tashk and "
       "Bakhtegan share one basin, so their cells are the same). Only the Aral's level differs between the two files.")
    w_("")
    w_("| Lake | Kind | Level, rules 1-2 (m) | Level, rules 3 (m) | Salt | Green shore (km) | Cells made lake, rules 1-2 (km²) |")
    w_("|---|---|---|---|---|---|---|")
    lv2 = {x["name"]: x["level"] for x in t2}
    res = 30
    for x in t1:
        a0, a1, o0, o1 = boxes.get(x["name"], (0, 0, 0, 0))
        km = 0.0
        for k, idx in enumerate(i1):
            row, col = idx // w, idx % w
            la, lo = 90 - (row + 0.5) / res, (col + 0.5) / res - 180
            if a0 <= la <= a1 and o0 <= lo <= o1 and (f1[k] & 2) and l1[k] == round(x["level"]):
                km += (111.32 / res) ** 2 * math.cos(math.radians(la))
        w_(f"| {x['name']} | {'revived' if x['revived'] else 'natural'} | {x['level']:g} | {lv2.get(x['name'], float('nan')):g} | "
           f"{'yes' if x['salt'] else 'no'} | {x['shore']:g} | {km:,.0f} |")
    w_("")

    # table D: the lakes of 1318
    w_("## Table D: the lakes of 1318 (decision 0107, works rules 6)")
    w_("")
    w_("| Area | Kind | Level (m) | km² | Drained or made | Outline | Source of the level |")
    w_("|---|---|---|---|---|---|---|")
    for a in spec:
        src = a["outline"]
        outline = (f"OpenHistoricalMap {src['ohm']}" if "ohm" in src else f"OpenStreetMap {src['osm']}" if "osm" in src
                   else f"GLO-90 below {src['fill']['below']:g} m" + (f", grown {src['fill']['grow_m']} m" if src["fill"].get("grow_m") else ""))
        level = f"{a['level']:g}" if a["kind"] == "lake" else "-"
        w_(f"| {a['name']} | {a['kind']}{' (salt)' if a.get('salt') else ''} | {level} | {km2_of.get(a['key'], 0):,.1f} | "
           f"{DRAINED.get(a['key'], '')} | {outline} | {a['cite']} |")
    w_("")

    # the facts block, checked by tests/DataReadersTests/LedgerChecks.cs
    w_("## Facts checked by the tests")
    w_("")
    w_("```ledger-facts")
    for f, (size, h) in pins.items():
        w_(f"file {f} {size} {h}")
    for k, v in rules.items():
        w_(f"rules {k} {v}")
    for f, areas in works.items():
        by = {}
        for fl, lv, km in areas:
            by[fl] = by.get(fl, 0) + 1
        for fl in sorted(by):
            w_(f"works {f} {fl} {by[fl]}")
    for f, lakes in (("lakes.bin", t1), ("lakes-2.bin", t2)):
        for x in lakes:
            w_(f"terminal {f} {x['level']:g} {x['name']}")
    for a in spec:
        w_(f"lake1318 {a['key']} {a['kind']} {a.get('level', 0):g}")
    w_("```")
    return "\n".join(L) + "\n"


def main():
    text = build()
    if "--check" in sys.argv:
        old = open(PAGE, encoding="utf-8").read() if os.path.exists(PAGE) else ""
        if old != text:
            print("docs/works-1318-ledger.md is not what tools/works_ledger.py writes: run it and commit the page")
            sys.exit(1)
        print("docs/works-1318-ledger.md is up to date")
        return
    open(PAGE, "w", encoding="utf-8", newline="\n").write(text)
    print(f"wrote {PAGE}")


if __name__ == "__main__":
    main()
