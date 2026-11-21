"""Macrostrat map units (carto tiles, CC BY 4.0) translated into GLiM's 6-letter code vocabulary, so one rules table
(geology-rules-v4.json, "litho") serves both maps (EA-0198).

GLiM's code: first level the class (su ss sm sc ev py va vi vb pa pi pb mt ...), second level a sub-type (pu carbonate, sh
fine, ss coarse, mx mixed, py pyroclastics, am mafic metamorphics, gr greenstone; for su: ad alluvial, ds dune sand, lo
loess, la laterite, or organic), third level a rock the unit mentions besides its class (ev evaporites, cl coal, fe iron
formation, gl glacial, ...); "__" is blank.

A unit's free-text `lith` (the source map's words, main rock first) decides; Macrostrat's lith_type1..4 stand in when the
words name no rock. Following GLiM's own rules: carbonate first -> sc; carbonate mentioned after other sediments -> sm;
sediments without carbonate -> ss; metamorphics mentioning marble or carbonate -> mtpu. A unit that names no rock at all
("sedimentary rocks, undivided") gives None, and GLiM answers there.
"""
import re

WORDS = {
    # unconsolidated (GLiM su), with the second level they give
    "su": ["alluvium", "alluvial", "fluvial", "colluvium", "colluvial", "gravel", "sand", "silt", "mud", "clay", "till",
           "drift", "diamicton", "moraine", "loess", "dune", "eolian", "aeolian", "regolith", "soil", "eluvium",
           "laterite", "lateritic", "peat", "gyttja", "unconsolidated", "surficial", "lacustrine deposits", "outwash"],
    "carb": ["limestone", "limes", "dolostone", "dolomite", "marl", "chalk", "carbonate", "calcarenite", "calcilutite", "calcisiltite",
             "micrite", "oolite", "coquina", "travertine", "tufa", "wackestone", "packstone", "grainstone", "boundstone",
             "reef", "calcareous", "lime mudstone", "biosparite", "biomicrite", "encrinite", "siderite", "ankerite"],
    "fine": ["shale", "mudstone", "siltstone", "claystone", "argillite", "pelite", "mudrock", "flysch"],
    "coarse": ["sandstone", "conglomerate", "arkose", "greywacke", "graywacke", "arenite", "grit", "breccia", "tillite",
               "wacke", "molasse", "greensand", "diamictite", "red beds"],
    "clastic": ["siliciclastic", "clastic", "turbidite", "terrigenous"],
    "evap": ["evaporite", "gypsum", "anhydrite", "halite", "rock salt", "salt", "trona", "potash"],
    "coal": ["coal", "lignite", "anthracite"],
    "fe": ["iron formation", "banded iron", "ironstone", "jaspilite", "taconite", "itabirite"],
    "chert": ["chert", "flint", "radiolarite", "novaculite", "porcellanite", "diatomite", "siliceous ooze"],
    "marble": ["marble", "skarn", "calc-silicate", "calcsilicate", "cipolin"],
    "metabasic": ["amphibolite", "metagabbro", "metabasite", "eclogite", "granulite"],
    "greenstone": ["greenstone", "greenschist", "metabasalt", "metavolcanic", "spilite", "komatiite"],
    "meta": ["gneiss", "schist", "migmatite", "mylonite", "slate", "phyllite", "quartzite", "hornfels", "paragneiss",
             "orthogneiss", "metasedimentary", "metamorphic", "metapelite", "metagraywacke", "metaconglomerate",
             "metasiltstone", "granofels", "serpentinite", "metarhyolite", "metaigneous", "blueschist", "cataclasite",
             "diatexite", "phyllonite"],
    "pyro": ["tuff", "ignimbrite", "tephra", "pumice", "agglomerate", "volcaniclastic", "pyroclastic", "ash", "tuffite",
             "scoria", "hyaloclastite", "bentonite"],
    "vb": ["basic igneous extrusive", "mafic igneous extrusive", "basic extrusive", "mafic extrusive", "basic volcanic", "basalt", "basaltic", "basanite", "hawaiite", "picrite", "ankaramite", "tholeiite", "mafic volcanic", "pillow lava",
           "tephrite", "foidite", "mugearite"],
    "vi": ["intermediate igneous extrusive", "intermediate extrusive", "andesite", "andesitic", "dacite", "trachyandesite", "latite", "trachyte", "phonolite", "benmoreite", "adakite",
           "intermediate volcanic"],
    "va": ["acidic igneous extrusive", "acid igneous extrusive", "felsic igneous extrusive", "felsic extrusive", "acid volcanic",
           "acidic volcanic", "rhyolite", "rhyolitic", "rhyodacite", "felsite", "obsidian", "comendite", "felsic volcanic", "volcanic glass"],
    "pa": ["acidic igneous intrusive", "acid igneous intrusive", "felsic igneous intrusive", "felsic intrusive", "acid plutonic",
           "felsic plutonic", "granite", "granitic", "granodiorite", "tonalite", "monzogranite", "syenogranite", "leucogranite", "aplite",
           "pegmatite", "alaskite", "trondhjemite", "charnockite", "quartz monzonite", "granitoid", "granophyre"],
    "pi": ["intermediate igneous intrusive", "intermediate intrusive", "intermediate plutonic", "diorite", "monzonite", "syenite", "monzodiorite", "quartz diorite", "foidolite"],
    "pb": ["basic igneous intrusive", "mafic igneous intrusive", "basic intrusive", "mafic intrusive", "basic plutonic",
           "mafic plutonic", "gabbro", "norite", "troctolite", "anorthosite", "peridotite", "dunite", "pyroxenite", "ultramafic", "harzburgite",
           "lherzolite", "wehrlite", "websterite", "hornblendite", "diabase", "dolerite", "kimberlite", "carbonatite", "lamprophyre"],
    "volc": ["volcanic", "lava", "volcanics"],
    "sed": ["sedimentary"],
    "plut": ["plutonic", "intrusive", "intrusion"],
}
_RE = {k: re.compile(r"\b(" + "|".join(re.escape(w) for w in sorted(v, key=len, reverse=True)) + r")s?\b", re.I) for k, v in WORDS.items()}
# words that contain an unconsolidated word but are rock ("sandstone" holds "sand", "mudstone" "mud", "claystone" "clay")
_ROCKY = re.compile(r"\b(sandstone|mudstone|claystone|siltstone|mudrock|lime mudstone|tillite)s?\b", re.I)
GENERIC = ("sed", "volc", "plut")   # used only when the text names no particular rock
_RARE = re.compile(r"\brare\s*:\s*\{[^}]*\}", re.I)
SED = ("carb", "fine", "coarse", "clastic", "evap", "coal", "fe", "chert")
TYPE_GROUP = {"carbonate": "carb", "siliciclastic": "clastic", "evaporite": "evap", "organic": "coal", "chemical": "chert",
              "regolith": "su", "volcanic": "volc", "plutonic": "plut", "metamorphic": "meta", "metasedimentary": "meta",
              "metaigneous": "metabasic", "metavolcanic": "greenstone", "cataclastic": "meta"}


def _hits(text):
    """Groups named in the text, each with the position of its first word (main rock first)."""
    rocky = _ROCKY.sub(lambda m: "#" * len(m.group(0)), text)   # mask rock words before matching su words
    out = {}
    for k, r in _RE.items():
        m = r.search(rocky if k == "su" else text)
        if m:
            out[k] = m.start()
    return out


def code(lith, types=()):
    """GLiM-style code of a Macrostrat unit, or None when it names no rock. Rocks listed as "rare:{...}" count only as
    mentioned (the third level), never for the class or the sub-type."""
    text = (lith or "").lower()
    every = _hits(text)
    h = _hits(_RARE.sub(" ", text))
    named = bool(h)
    for i, t in enumerate(types):
        g = TYPE_GROUP.get((t or "").lower())
        # with no words, the types stand in; with words, a carbonate or evaporite type the words leave out still counts as
        # mentioned (Missouri's Ozark dolomites are "chert, sandstone" in words and typed carbonate)
        if g and g not in h and (not named or g in ("carb", "evap")):
            h[g] = 1000 + i
    if not h:
        return None
    specific = {k: v for k, v in h.items() if k not in GENERIC}
    first = min(specific or h, key=(specific or h).get)
    if first == "sed":
        return None
    for k in ("evap", "coal", "fe"):
        if k in every:
            h.setdefault(k, 10 ** 6)
    third = "ev" if "evap" in h else "cl" if "coal" in h else "fe" if "fe" in h else "__"
    if first == "su":
        sub = "lo" if re.search(r"\bloess", lith or "", re.I) else "ds" if re.search(r"\b(dune|eolian|aeolian)", lith or "", re.I) \
            else "la" if re.search(r"\blaterit", lith or "", re.I) else "or" if re.search(r"\b(peat|gyttja|organic)", lith or "", re.I) \
            else "ad" if re.search(r"\b(alluvi|fluvial)", lith or "", re.I) else "mx"
        gl = "gl" if re.search(r"\b(till|drift|moraine|glacial|outwash|diamicton)", lith or "", re.I) else third
        return "su" + sub + gl
    if first in ("marble",):
        return "mtpu" + third
    if first in ("meta", "metabasic", "greenstone"):
        sub = "pu" if ("marble" in h or "carb" in h) else "am" if first == "metabasic" else "gr" if first == "greenstone" else "__"
        return "mt" + sub + ("fe" if "fe" in h else "__")
    if first in ("pyro",):
        return "py" + ("pu" if "carb" in h else "__") + "__"
    if first in ("vb", "vi", "va"):
        return first + ("py" if "pyro" in h else "__") + "__"
    if first == "volc":
        return "vi" + ("py" if "pyro" in h else "__") + "__"
    if first in ("pa", "pi", "pb"):
        return first + "____"
    if first == "plut":
        return "pa____"
    if first == "evap" and not ({"carb", "fine", "coarse", "clastic"} & h.keys()):
        return "ev____"
    if first == "chert" and not ({"carb", "fine", "coarse", "clastic"} & h.keys()):
        return "sc__" + third if "carb" in h else "ssmx" + third
    # sediments
    fine, coarse = "fine" in h, "coarse" in h
    clastic_sub = "sh" if fine and not coarse else "ss" if coarse and not fine else "mx"
    if "pyro" in h or "volc" in h:
        clastic_sub = "py"
    if first == "carb":
        sub = "pu" if not (fine or coarse or "clastic" in h) else "sh" if fine and not coarse else "ss" if coarse and not fine else "mx"
        return "sc" + sub + third
    if "carb" in h:
        return "sm" + clastic_sub + third
    if first in SED or first in ("fine", "coarse", "clastic"):
        return "ss" + clastic_sub + third
    return None


def named(lith):
    """Whether the unit's own words name a rock. A unit known only by its lithology types is weak evidence: the rock set
    uses it only where GLiM maps no bedrock (bake_rock_30s.py)."""
    return bool(_hits((lith or "").lower()))
