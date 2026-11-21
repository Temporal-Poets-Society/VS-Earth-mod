"""Credit lines for every Macrostrat source map a rock set draws from (CC BY 4.0 asks for each source).
Usage: python -I tools/ground/macrostrat_credits.py <macrostrat_units.py output dir> <sources.json> <out .txt>"""
import json, sys

units, sources, out = sys.argv[1:4]
used = json.load(open(units + "/codes.json", encoding="utf-8"))["sources"]
meta = {s["source_id"]: s for s in json.load(open(sources, encoding="utf-8"))["success"]["data"]}
lines = ["Macrostrat source maps of the 30\" rock set (geology rules 4, decision 0106). Macrostrat, https://macrostrat.org, "
         "carto map; licensed CC BY 4.0. Each map below is credited as Macrostrat lists it; the unit descriptions were "
         "translated into GLiM-style rock codes by tools/ground/macrostrat_codes.py and rasterised at 30 arc-seconds.", ""]
for sid in sorted(used):
    s = meta.get(sid, {})
    def g(k):
        return " ".join(str(s.get(k) or "").split())
    parts = [g("name") or f"source {sid}"]
    ref = ", ".join(x for x in (g("authors"), g("ref_year")) if x)
    if g("ref_title") and g("ref_title") != g("name"):
        parts.append(g("ref_title"))
    if ref:
        parts.append(ref)
    if g("ref_source"):
        parts.append(g("ref_source"))
    if g("isbn_doi"):
        parts.append(g("isbn_doi"))
    if g("url"):
        parts.append(g("url"))
    lines.append(f"Macrostrat source {sid} ({g('scale') or '?'}): " + "; ".join(parts))
open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
print(len(used), "sources ->", out)
