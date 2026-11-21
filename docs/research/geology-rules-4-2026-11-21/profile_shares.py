"""Share of GLiM's mapped area per rules-4 profile (by code, before the cover fill)."""
import json, sys, collections
r = json.load(open(sys.argv[1], encoding="utf-8")); L = r["litho"]


def prof(c):
    for p, v in L["patterns"]:
        if all(a == "?" or a == b for a, b in zip(p, c)):
            return v
    return L["classes"].get(c[:2], "unknown")


s = collections.Counter()
for line in open(sys.argv[2], encoding="utf-8"):
    c, share, n = line.rstrip("\n").split("\t")
    s[prof(c)] += float(share)
for k, v in s.most_common():
    print(f"  {k:22s} {v:6.2f} %")
