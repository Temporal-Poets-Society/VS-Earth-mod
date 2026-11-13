"""Ore blocks and deposit hosts for Earth's added rocks, copied from the vanilla rock each one stands in for.

Earth adds full vanilla-family rocks (patches ground-*-rock.json) that vanilla's ores and deposits do not know. A rock
gets exactly what its stand-in has, so a region keeps the ores it had when it used the stand-in:

    gneiss    <- slate        (regional geology showed gneiss as slate before)
    schist    <- phyllite     (likewise)
    quartzite <- sandstone
    dolostone <- limestone    (ore blocks already in ground-dolostone-ores.json; this adds the deposit hosts)

Writes assets/earth/patches/ground-rocks-ores.json (ore, loose ore and crystallized ore variants) and
ground-rocks-deposits.json (each vanilla deposit whose host list names the stand-in also names the rock, with the
same grades). Deposits that name a single host rock (marble in slate) are left alone.

    python -P tools/make_rock_ore_patches.py [vanilla assets dir]   (default %APPDATA%/Vintagestory/assets)
"""
import json, os, sys, glob
import json5

ROCKS = {"gneiss": "slate", "schist": "phyllite", "quartzite": "sandstone", "dolostone": "limestone"}
NEW_ORE_BLOCKS = ["gneiss", "schist", "quartzite"]  # dolostone's are in ground-dolostone-ores.json
GUARD = {
    "dolostone": [{"modid": "geoaddons", "invert": True}],
    "_": [{"modid": "geoaddons", "invert": True}, {"modid": "advancedgeology", "invert": True}],
}
# what the stand-in hosts but the rock cannot: quartzite is metamorphosed sandstone, so no coal
EXCLUDE = {"quartzite": ("coal", "lignite", "anthracite")}
ORE_FILES = ["blocktypes/stone/ore-graded.json", "blocktypes/stone/ore-ungraded.json", "blocktypes/stone/ore-gem.json",
             "blocktypes/stone/looseores.json", "itemtypes/resource/ore-graded.json", "itemtypes/resource/crystalizedore-graded.json"]


def guard(rock):
    return GUARD.get(rock, GUARD["_"])


def swap(code, src, dst):
    parts = code.split("-")
    return "-".join(dst if p == src else p for p in parts) if src in parts else None


def main():
    assets = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["APPDATA"], "Vintagestory", "assets")
    survival = os.path.join(assets, "survival")
    here = os.path.dirname(os.path.abspath(__file__))
    patches = os.path.join(here, "..", "assets", "earth", "patches")

    ores, check = [], {}
    for f in ORE_FILES:
        allowed = json5.load(open(os.path.join(survival, f), encoding="utf-8"))["allowedVariants"]
        for rock, src in ROCKS.items():
            for code in allowed:
                new = swap(code, src, rock)
                if new is None or new in allowed or any(x in new for x in EXCLUDE.get(rock, ())):
                    continue
                check.setdefault(rock, set()).add(("game:" + f, new))
                if rock in NEW_ORE_BLOCKS:
                    ores.append({"op": "add", "file": "game:" + f, "path": "/allowedVariants/-", "value": new, "dependsOn": guard(rock)})
    # the dolostone patch was written by hand from limestone; it must still match vanilla's limestone list
    have = {(p["file"], p["value"]) for p in json.load(open(os.path.join(patches, "ground-dolostone-ores.json"), encoding="utf-8"))}
    if have != check["dolostone"]:
        sys.exit(f"ground-dolostone-ores.json no longer matches vanilla limestone: {len(have ^ check['dolostone'])} differences")

    deposits, skipped = [], []
    for path in sorted(glob.glob(os.path.join(survival, "worldgen", "deposits", "**", "*.json"), recursive=True)):
        rel = os.path.relpath(path, survival).replace("\\", "/")
        data = json5.load(open(path, encoding="utf-8"))

        def walk(node, pointer):
            if isinstance(node, dict):
                inb = node.get("inblock")
                if isinstance(inb, dict):
                    hosts = inb.get("allowedVariants")
                    if isinstance(hosts, list):
                        for rock, src in ROCKS.items():
                            if src in hosts and rock not in hosts and not any(x in rel for x in EXCLUDE.get(rock, ())):
                                deposits.append({"op": "add", "file": "game:" + rel, "path": pointer + "/inblock/allowedVariants/-", "value": rock, "dependsOn": guard(rock)})
                    elif inb.get("code", "").startswith("rock-") and inb["code"][5:] in ROCKS.values():
                        skipped.append(f"{rel}{pointer}: only {inb['code']}")
                for key, value in node.items():
                    for rock, src in ROCKS.items():
                        if key == "rock-" + src and "rock-" + rock not in node and not any(x in rel for x in EXCLUDE.get(rock, ())):
                            deposits.append({"op": "add", "file": "game:" + rel, "path": pointer + "/rock-" + rock, "value": value, "dependsOn": guard(rock)})
                    walk(value, pointer + "/" + key)
            elif isinstance(node, list):
                for i, value in enumerate(node):
                    walk(value, pointer + "/" + str(i))
        walk(data, "")

    def write(name, items):
        with open(os.path.join(patches, name), "w", encoding="utf-8", newline="\n") as out:
            out.write("[\n" + ",\n".join("  " + json.dumps(p, ensure_ascii=False) for p in items) + "\n]\n")
    write("ground-rocks-ores.json", ores)
    write("ground-rocks-deposits.json", deposits)
    print(f"ore variants: {len(ores)}; deposit hosts: {len(deposits)} ({', '.join(f'{r} {sum(1 for p in deposits if p['value'] == r or p['path'].endswith(r))}' for r in ROCKS)})")
    for s in skipped:
        print("left alone:", s)


if __name__ == "__main__":
    main()
