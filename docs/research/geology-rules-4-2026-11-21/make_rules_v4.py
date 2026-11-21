"""Write geology-rules-v4.json from v3: GLiM's three-level codes (30" rock set) mapped to profiles (EA-0198)."""
import json, pathlib, sys
root = pathlib.Path(sys.argv[1]); cfg = root / "assets/earth/config"
r = json.loads((cfg / "geology-rules-v3.json").read_bytes().decode("utf-8"))
r["version"] = 4
r["materials"] += [
    {"id": "marble", "vanilla": "game:rock-whitemarble", "note": "GLiM's metamorphic carbonate (mtpu, and carbonate under metamorphic influence); vanilla's white marble, which vanilla's malachite, lapis and graphite already take as a host."},
    {"id": "phyllite", "vanilla": "game:rock-phyllite"},
]
B = lambda *pairs: [{"material": m, "fraction": f} for m, f in pairs]
new = [
    ("carbonate-pure", "Pure carbonate platform: limestone and dolostone with chert bands (GLiM scpu)", B(("limestone", .16), ("dolostone", .12), ("chert", .015), ("limestone", .14), ("dolostone", .1), ("limestone", .1), ("shale", .04)), "granite"),
    ("carbonate-shaly", "Carbonate with shale and marl (GLiM scsh); claystone stands for marl", B(("limestone", .1), ("shale", .08), ("limestone", .08), ("claystone", .06), ("limestone", .1), ("shale", .08), ("limestone", .08), ("sandstone", .06)), "granite"),
    ("carbonate-sandy", "Carbonate with sandstone (GLiM scss)", B(("limestone", .12), ("sandstone", .1), ("limestone", .1), ("dolostone", .08), ("sandstone", .1), ("limestone", .08), ("conglomerate", .04)), "granite"),
    ("carbonate-evaporite", "Carbonate with gypsum and mudstone (GLiM sc with evaporites); rock salt stays with the salt sites", B(("limestone", .1), ("gypsum", .05), ("dolostone", .12), ("mudstone", .08), ("gypsum", .04), ("limestone", .1), ("dolostone", .08), ("sandstone", .08)), "granite"),
    ("metacarbonate", "Carbonate under metamorphic influence: marble, limestone and phyllite (GLiM sc with mt)", B(("marble", .14), ("phyllite", .08), ("marble", .12), ("limestone", .08), ("schist", .1), ("marble", .1)), "gneiss"),
    ("marble", "Marble in schist and gneiss (GLiM mtpu: metamorphic, carbonate)", B(("marble", .2), ("schist", .1), ("marble", .15), ("gneiss", .15)), "gneiss"),
    ("mafic-metamorphic", "Mafic metamorphics (GLiM mtam): schist and gneiss with gabbro bodies; no amphibolite block", B(("schist", .15), ("gabbro", .1), ("gneiss", .15), ("schist", .1), ("gabbro", .1)), "gneiss"),
    ("greenstone-belt", "Greenstone belt (GLiM mtgr): metabasalt, schist, chert and ultramafic flows among granitoids", B(("basalt", .14), ("schist", .1), ("chert", .02), ("basalt", .12), ("peridotite", .04), ("schist", .1), ("basalt", .08)), "granite"),
    ("iron-formation", "Iron formation (GLiM third level fe): chert bands in slate and schist with metabasalt", B(("slate", .1), ("chert", .06), ("schist", .1), ("chert", .05), ("slate", .1), ("basalt", .08), ("schist", .1)), "gneiss"),
    ("metasedimentary", "Sediments under metamorphic influence (GLiM ss/sm with mt): slate, phyllite and schist; sandstone stands for quartzite", B(("slate", .14), ("phyllite", .1), ("sandstone", .08), ("slate", .1), ("schist", .12), ("phyllite", .1)), "gneiss"),
    ("mixed-fine", "Fine mixed sediments (GLiM smsh): shale, mudstone and claystone with limestone", B(("shale", .14), ("limestone", .08), ("mudstone", .12), ("shale", .1), ("limestone", .06), ("claystone", .08), ("sandstone", .06)), "granite"),
    ("mixed-evaporite", "Sediments with evaporites (GLiM sm/ss with ev): red beds, gypsum and carbonate; rock salt stays with the salt sites", B(("mudstone", .12), ("gypsum", .05), ("limestone", .08), ("sandstone", .1), ("dolostone", .08), ("gypsum", .04), ("shale", .1), ("sandstone", .08)), "granite"),
    ("siliciclastic-fine", "Fine siliciclastics (GLiM sssh): shale and mudstone", B(("shale", .14), ("mudstone", .1), ("sandstone", .08), ("shale", .12), ("claystone", .08), ("sandstone", .06), ("slate", .1)), "granite"),
    ("siliciclastic-coarse", "Coarse siliciclastics (GLiM ssss): sandstone and conglomerate", B(("sandstone", .16), ("conglomerate", .08), ("sandstone", .12), ("shale", .06), ("sandstone", .12), ("conglomerate", .06), ("slate", .08)), "granite"),
    ("volcaniclastic", "Sediments with pyroclastics (GLiM ss/sm py): sandstone, shale and tuff", B(("sandstone", .1), ("tuff", .06), ("shale", .1), ("sandstone", .1), ("tuff", .05), ("andesite", .08), ("mudstone", .1)), "granite"),
    ("coal-measures", "Coal measures (GLiM third level cl): sandstone, shale and claystone cycles; the coal itself is an ore deposit", B(("sandstone", .1), ("shale", .1), ("claystone", .06), ("sandstone", .1), ("shale", .1), ("conglomerate", .04), ("sandstone", .08), ("mudstone", .08)), "granite"),
]
new += [
    ("copperbelt-roan", "Katangan Roan Group of the Central African Copperbelt: dolomites and dolomitic ore shale over sandstone and conglomerate, on a granite-gneiss basement (regional approximation)", B(("dolostone", .1), ("shale", .06), ("dolostone", .12), ("shale", .05), ("dolostone", .08), ("sandstone", .1), ("conglomerate", .05), ("sandstone", .08)), "gneiss"),
    ("otavi-dolomite", "Otavi Group platform dolomites of the Otavi Mountainland (Tsumeb, Kombat, Berg Aukas; regional approximation)", B(("dolostone", .18), ("limestone", .06), ("dolostone", .16), ("chert", .01), ("dolostone", .14), ("shale", .05), ("dolostone", .08)), "granite"),
]
ids = {p["id"] for p in r["profiles"]}
for i, label, beds, basement in new:
    assert i not in ids, i
    assert beds[0]["material"] != "tuff"
    r["profiles"].append({"id": i, "label": label, "beds": beds, "basement": basement})
# Envelopes drawn over 0.5-degree cells keep their rock only where the 30" map agrees on a fitting class, so the finer map
# decides inside them (Mednorudyansk's carbonate inside the Ural platinum belt's outline). Classes: 2 vb, 4 pb, 8 mt, 9 pa.
FIT = {"ophiolite": [2, 4, 8], "layered-intrusion": [4, 8], "felsic-plutonic": [9]}
for prov in r["provinces"]:
    if prov.get("lithology") is None and prov["profile"] in FIT:
        prov["lithology"] = FIT[prov["profile"]]
        prov["note"] = (prov.get("note", "") + " Rules 4: only over " + "/".join({2: "mafic volcanic", 4: "mafic plutonic", 8: "metamorphic", 9: "felsic plutonic"}[c] for c in FIT[prov["profile"]])
                        + " rock of the 30\" map (EA-0198).").strip()
SRC = "Envelope drawn for geology rules 4 after published geological maps (approximate outline; EA-0198)"
SED_META = [1, 3, 5, 6, 8]   # cover, siliciclastic, mixed, carbonate, metamorphic: never mapped granite or volcanics
r["provinces"] += [
    {"id": "central-african-copperbelt", "profile": "copperbelt-roan", "source": SRC, "lithology": SED_META,
     "note": "Lufilian arc from Kolwezi through Likasi and Lubumbashi to the Zambian Copperbelt (Chililabombwe to Luanshya); GLiM and Macrostrat map it as undivided metamorphics.",
     "polygon": [[24.8, -10.9], [25.0, -10.4], [26.0, -10.3], [27.2, -10.7], [28.0, -11.4], [28.9, -12.5], [28.8, -13.5], [28.0, -13.4], [27.3, -12.6], [26.6, -11.6], [25.6, -11.2], [24.8, -10.9]]},
    {"id": "otavi-mountainland", "profile": "otavi-dolomite", "source": SRC, "lithology": SED_META,
     "note": "Otavi Mountainland between Otavi, Tsumeb and Grootfontein; GLiM maps it as metamorphics.",
     "polygon": [[16.9, -19.9], [16.9, -19.4], [17.4, -19.0], [18.2, -19.0], [18.7, -19.3], [18.6, -19.8], [17.8, -19.9], [16.9, -19.9]]},
    {"id": "apuan-alps-marble", "profile": "marble", "source": SRC, "lithology": [3, 5, 6, 8],
     "note": "Apuan Alps metamorphic core (Carrara, Massa, Seravezza); the maps show carbonate, the rock is marble.",
     "polygon": [[10.05, 44.12], [10.2, 44.17], [10.33, 44.08], [10.38, 43.98], [10.28, 43.94], [10.15, 43.98], [10.05, 44.12]]},
]
r["litho"] = {
    "note": ("Rules 4 read GLiM's full 6-letter code from the 30\" rock set (tools/ground/bake_rock_30s.py). The first pattern that "
             "matches wins ('?' any letter, '_' GLiM's blank); codes no pattern matches take their first level's profile from "
             "'classes'. Second level (Hartmann & Moosdorf 2012, table 1): ad alluvial deposits, am mafic metamorphics mentioned, "
             "ds dune sands, gr greenstone mentioned, la laterites, lo loess, mx mixed grain size, or organic sediment, pu (pure) "
             "carbonate, py pyroclastics, sh fine grained, ss coarse grained. Third level, rocks the source unit mentions besides its class (read from the codes' use; Pine Point's unmetamorphosed dolomite is smmxmt): ev evaporites, "
             "mt metamorphics, fe iron formation, cl coal, ch chert, bs black shale, ph phosphorite, vr volcanics, pr "
             "plutonics, sr sediments, su unconsolidated, gl glacial influence, pt peat, we unconfirmed; only ev, fe and cl "
             "change the profile; metacarbonate and metasedimentary are for provinces. The surface code is the set's macro "
             "field (Macrostrat's national maps at 1:1M or finer, translated into this vocabulary by "
             "tools/ground/macrostrat_codes.py) where it has one, else GLiM's litho. Cover (su), water (wb), ice (ig) and no "
             "data (nd) take the set's bedrock field (Macrostrat's or GLiM's bedrock at the cell, else the nearest within 1 "
             "degree), else their class."),
    "patterns": [
        ["mtpu??", "marble"], ["mtam??", "mafic-metamorphic"], ["mtgr??", "greenstone-belt"], ["????fe", "iron-formation"],
        ["sc??ev", "carbonate-evaporite"], ["scpu??", "carbonate-pure"], ["scsh??", "carbonate-shaly"], ["scss??", "carbonate-sandy"],
        ["sm??ev", "mixed-evaporite"], ["ss??ev", "mixed-evaporite"],
        ["sm??cl", "coal-measures"], ["ss??cl", "coal-measures"],
        ["smsh??", "mixed-fine"], ["smpy??", "volcaniclastic"], ["sspy??", "volcaniclastic"],
        ["sssh??", "siliciclastic-fine"], ["ssss??", "siliciclastic-coarse"],
    ],
    "classes": {"su": "basin-fill", "vb": "mafic-volcanic", "ss": "siliciclastic", "pb": "mafic-plutonic", "sm": "mixed-sedimentary", "sc": "carbonate",
                "va": "felsic-volcanic", "mt": "metamorphic", "pa": "felsic-plutonic", "vi": "intermediate-volcanic", "py": "pyroclastic",
                "pi": "intermediate-plutonic", "ev": "evaporitic", "wb": "unknown", "ig": "unknown", "nd": "unknown"},
    "notBedrock": ["su", "wb", "ig", "nd"],
}
(cfg / "geology-rules-v4.json").write_bytes((json.dumps(r, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
print("profiles", len(r["profiles"]))
