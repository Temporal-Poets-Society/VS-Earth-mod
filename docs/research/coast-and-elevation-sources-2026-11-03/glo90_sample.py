"""Pick a random, repeatable sample of Copernicus DEM GLO-90 tiles and download their elevation files.
usage: glo90_sample.py <out dir> <count> <seed>
Writes <out dir>/tiles.txt (every tile in the bucket) and <out dir>/sample.txt (the chosen ones); skips files already there."""
import os, random, sys, urllib.parse, urllib.request
import xml.etree.ElementTree as ET

BUCKET = "https://copernicus-dem-90m.s3.eu-central-1.amazonaws.com/"
NS = {"s3": "http://s3.amazonaws.com/doc/2006-03-01/"}
out, count, seed = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
os.makedirs(out, exist_ok=True)

listing = os.path.join(out, "tiles.txt")
if not os.path.exists(listing):
    tiles, token = [], None
    while True:   # one "folder" per tile, listed 1000 at a time
        q = {"list-type": "2", "delimiter": "/", "max-keys": "1000"}
        if token:
            q["continuation-token"] = token
        root = ET.fromstring(urllib.request.urlopen(BUCKET + "?" + urllib.parse.urlencode(q), timeout=60).read())
        tiles += [p.find("s3:Prefix", NS).text.rstrip("/") for p in root.findall("s3:CommonPrefixes", NS)]
        if root.find("s3:IsTruncated", NS).text != "true":
            break
        token = root.find("s3:NextContinuationToken", NS).text
    tiles = sorted(t for t in tiles if t.startswith("Copernicus_DSM_COG_30_"))
    open(listing, "w").write("\n".join(tiles) + "\n")
tiles = open(listing).read().split()
sample = sorted(random.Random(seed).sample(tiles, count))
open(os.path.join(out, "sample.txt"), "w").write("\n".join(sample) + "\n")
print(f"{len(tiles)} tiles in the bucket; {len(sample)} chosen with seed {seed}")

total = 0
for i, t in enumerate(sample, 1):
    dst = os.path.join(out, t + ".tif")
    if not os.path.exists(dst):
        tmp = dst + ".part"
        urllib.request.urlretrieve(f"{BUCKET}{t}/{t}.tif", tmp)
        os.replace(tmp, dst)
    total += os.path.getsize(dst)
    if i % 10 == 0:
        print(f"  {i}/{len(sample)}  {total/1e6:.0f} MB", flush=True)
print(f"done: {total/1e6:.0f} MB")
