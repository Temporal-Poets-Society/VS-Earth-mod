"""Builds assets/earth/config/ore-deposits-SOURCES.txt from the groups' ATTRIBUTION.txt files (decision 0065).
Run after a group changes its sources: C:\\VS mods\\Earth\\.venv\\Scripts\\python.exe -I tools/ore-data/build_sources.py
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GROUPS = ['coal-evaporites', 'base-metals', 'iron-ferro', 'precious-gems']
TITLES = {'coal-evaporites': 'Coal and evaporites', 'base-metals': 'Base metals',
          'iron-ferro': 'Iron, ferroalloys and aluminium', 'precious-gems': 'Precious metals, gems and industrial minerals'}

parts = ["Earth ore deposit catalogues (decision 0065) and their heat-map evidence (ore-heatmaps-v2.json)",
         "",
         "The catalogues in assets/earth/config/ore-deposits-v1/ list real deposits and basins with tonnage, grade, depth and",
         "footprint taken or derived from the sources below. Positions, sizes and shapes are adapted for the game; estimated",
         "values are marked in each entry (\"est\", confidence \"q\"). They are not outputs or endorsements of the source authors",
         "or the organisations named. The same sources, as point and area evidence in tools/ore-data/<group>/evidence.geojson,",
         "feed ore-heatmaps-v2.json together with the sources in ore-heatmaps-SOURCES.txt.",
         ""]
for g in GROUPS:
    text = (ROOT / 'tools/ore-data' / g / 'ATTRIBUTION.txt').read_text(encoding='utf-8').strip()
    parts += ['=' * 100, TITLES[g] + ' (tools/ore-data/' + g + '/)', '=' * 100, '', text, '']
out = ROOT / 'assets/earth/config/ore-deposits-SOURCES.txt'
out.write_text('\n'.join(parts), encoding='utf-8')
print(out, out.stat().st_size)
