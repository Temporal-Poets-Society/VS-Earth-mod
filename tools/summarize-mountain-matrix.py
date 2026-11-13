"""Summarize completed test rows without requiring game libraries or editing test evidence."""
import argparse
import csv
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("matrix", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
rows = json.loads(args.matrix.read_text(encoding="utf-8-sig"))
if isinstance(rows, dict):
    rows = [rows]
root = Path(__file__).resolve().parent.parent
sites = json.loads((root / "tests/fixtures/mountain-sites.json").read_text(encoding="utf-8"))
lookup = {(round(s["latitude"], 6), round(s["longitude"], 6)): s for s in sites}
args.output.mkdir(parents=True, exist_ok=True)
site_rows = []
for row in rows:
    for line in row.get("SiteSummaries", []):
        matched = re.match(r"(-?[\d.]+),(-?[\d.]+) at .*?: (\d+) columns in ([\d.]+)s;.*?terrain y min/10%/50%/90%/max ([\d/]+);.*?water ([\d.]+)%; top blocks: ([^;]+)", line)
        if not matched:
            continue
        lat, lon, columns, seconds, heights, water, cover = matched.groups()
        site = lookup.get((round(float(lat), 6), round(float(lon), 6)))
        if not site:
            raise ValueError(f"Unknown test target: {lat},{lon}")
        families = {name: float(percent) for name, percent in re.findall(r"([\w-]+) ([\d.]+)%", cover)}
        minimum, p10, median, p90, maximum = map(int, heights.split("/"))
        site_rows.append(dict(case=row["Case"], height=row["Height"], repeat=row["Repeat"], continent=site["continent"], site=site["name"], columns=int(columns), seconds=float(seconds), terrain_min=minimum, terrain_median=median, terrain_max=maximum, terrain_range=maximum-minimum, water_percent=float(water), canopy_percent=sum(v for k,v in families.items() if k.startswith("leaves") or k=="log"), rock_percent=sum(v for k,v in families.items() if k in ("rock","crackedrock")), soil_percent=families.get("soil",0), forestfloor_percent=families.get("forestfloor",0), sand_percent=families.get("sand",0), snow_ice_percent=sum(v for k,v in families.items() if k in ("snowlayer","snowblock","glacierice")), reported_cover=cover))
if len(site_rows) != len(rows)*len(sites):
    raise ValueError(f"Expected {len(rows)*len(sites)} site rows, parsed {len(site_rows)}")
with (args.output/"site-comparisons.csv").open("w", newline="", encoding="utf-8") as file:
    writer=csv.DictWriter(file,fieldnames=site_rows[0].keys())
    writer.writeheader(); writer.writerows(site_rows)
summary=dict(worlds=len(rows), passed=sum(bool(r["Pass"]) for r in rows), failed=sum(not r["Pass"] for r in rows), sites=len(site_rows), generated_columns=sum(r["columns"] for r in site_rows), continental_coverage=sorted({s["continent"] for s in sites}), assembly_hashes=sorted({r["AssemblySHA256"] for r in rows}), terrain_seconds_by_case_height=[dict(case=r["Case"],height=r["Height"],repeat=r["Repeat"],total_seconds=r["ElapsedSeconds"],site_seconds=round(sum(s["seconds"] for s in site_rows if s["case"]==r["Case"] and s["height"]==r["Height"] and s["repeat"]==r["Repeat"]),2),save_bytes=r.get("SaveBytes"),passed=r["Pass"]) for r in rows])
(args.output/"summary.json").write_text(json.dumps(summary,indent=2)+"\n",encoding="utf-8")
print(json.dumps({key:value for key,value in summary.items() if key!="terrain_seconds_by_case_height"},indent=2))
