"""Keep fresh-world process timing and existing-save reload timing in separate cohorts."""
import argparse
import csv
import json
import re
import statistics
from collections import defaultdict
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument("fresh",type=Path)
parser.add_argument("reload",type=Path)
parser.add_argument("output",type=Path)
args=parser.parse_args()
fresh=json.loads(args.fresh.read_text(encoding="utf-8-sig"))
reload=json.loads(args.reload.read_text(encoding="utf-8-sig"))
if len(fresh)!=12 or len(reload)!=12 or not all(row["Pass"] for row in fresh+reload):
    raise ValueError("Expected two passing twelve-run cohorts")
args.output.mkdir(parents=True,exist_ok=True)
def groups(rows):
    grouped=defaultdict(list)
    for row in rows: grouped[(row["Height"],row["Case"])].append(row)
    output=[]
    for (height,case),values in sorted(grouped.items()):
        if len(values)!=3: raise ValueError("Expected three repeats per case and height")
        times=[row["ElapsedSeconds"] for row in values]
        item=dict(height=height,case=case,repeats=len(values),median_seconds=statistics.median(times),min_seconds=min(times),max_seconds=max(times),assembly_hashes=sorted({row["AssemblySHA256"] for row in values}))
        site_times=[]
        for row in values:
            matches=[float(m.group(1)) for line in row.get("SiteSummaries",[]) if (m:=re.search(r"columns in ([\d.]+)s;",line))]
            if len(matches)!=14: raise ValueError("Expected fourteen separately timed target regions")
            site_times.append(round(sum(matches),2))
        item["median_target_region_seconds"]=statistics.median(site_times)
        if "SaveBytes" in values[0]:
            item["mean_save_mib"]=round(statistics.mean(row["SaveBytes"] for row in values)/1048576,3)
            item["peak_working_set_range_mib"]=[round(min(row["PeakWorkingSetBytes"] for row in values)/1048576,1),round(max(row["PeakWorkingSetBytes"] for row in values)/1048576,1)]
        output.append(item)
    return output
summary=dict(fresh_world=groups(fresh),existing_save_reload=groups(reload))
with (args.output/"timing-runs.csv").open("w",newline="",encoding="utf-8") as file:
    writer=csv.DictWriter(file,fieldnames=["cohort","height","case","repeat","elapsed_seconds","save_bytes","peak_working_set_bytes","observed_cpu_seconds","assembly_sha256"])
    writer.writeheader()
    for cohort,rows in [("fresh_world",fresh),("existing_save_reload",reload)]:
        for row in rows:
            writer.writerow(dict(cohort=cohort,height=row["Height"],case=row["Case"],repeat=row["Repeat"],elapsed_seconds=row["ElapsedSeconds"],save_bytes=row.get("SaveBytes"),peak_working_set_bytes=row.get("PeakWorkingSetBytes"),observed_cpu_seconds=row.get("ObservedCpuSeconds"),assembly_sha256=row["AssemblySHA256"]))
(args.output/"timing-summary.json").write_text(json.dumps(summary,indent=2)+"\n",encoding="utf-8")
lines=["# Generation and reload timing results","","These are separate dedicated-server cohorts with three repeats per case and height. Each world uses seed 386217,","width 512000, fourteen target areas, vanilla vegetation providers and Cylinder World 0.11.14. Case A retains current","defaults; B uses sea 128 and the current height curve with climate rules 2. These compare complete profiles, not air","volume alone.","","## Fresh world process timing","","The clean pre.2 cohort ran sequentially after the debugging/provider servers finished. Totals include test setup,","mod/data loading, spawn and target-area generation, analysis and shutdown. They are not client loading-screen or FPS","measurements. Filesystem caches were not flushed.","","| Height | Case | Median seconds | Range in seconds | Mean save MiB |","| --- | --- | ---: | ---: | ---: |"]
for row in summary["fresh_world"]:
    lines.append(f'| {row["height"]} | {row["case"]} | {row["median_seconds"]:.2f} | {row["min_seconds"]:.2f} to {row["max_seconds"]:.2f} | {row["mean_save_mib"]:.2f} |')
lines.extend(["","The server also times the fourteen requested target regions separately. These sums include their generation,",
"region analysis and surface rendering; they exclude most process startup and are not pure terrain-fill timings.",
"","| Height | Case | Median target-region seconds |","| --- | --- | ---: |"])
for row in summary["fresh_world"]:
    lines.append(f'| {row["height"]} | {row["case"]} | {row["median_target_region_seconds"]:.2f} |')
lines.extend(["","No end-to-end speedup appears in this sample. Case B is about two percent slower, while saved size changes by less","than one percent. Climate encoding and terrain differences prevent attributing those small changes solely to the","underground budget. Observed peak working sets varied substantially between repeats; they do not establish a","predictable memory saving.","","## Existing save reload timing","","The separate pre.1 cohort reopened each already-generated save using the same original build and targets. Its fresh","reports and server logs were archived before reopening. This cohort is not combined with pre.2 absolute timings.","","| Height | Case | Median seconds | Range in seconds |","| --- | --- | ---: | ---: |"])
for row in summary["existing_save_reload"]:
    lines.append(f'| {row["height"]} | {row["case"]} | {row["median_seconds"]:.2f} | {row["min_seconds"]:.2f} to {row["max_seconds"]:.2f} |')
lines.extend(["","Reloading B is also slightly slower in this sample. The practical reason to choose the new layout is its terrain","proportions and available height; a faster general load time has not been demonstrated. A 512-high column still has","16 vertical chunk slots, versus 12 at height 384.","","[Exact run values](timing-runs.csv) and [grouped values with DLL hashes](timing-summary.json) preserve the measurements.","The earlier development timing series overlapped activation probes and is excluded from these fresh-world figures."])
(args.output/"performance.md").write_text("\n".join(lines)+"\n",encoding="utf-8")
print(json.dumps(summary,indent=2))
