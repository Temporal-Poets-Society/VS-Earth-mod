"""Sum the object sizes in the public Copernicus DEM buckets with the S3 listing API (metadata only, no data download).
usage: bucket_size.py <bucket> [<bucket> ...]"""
import sys, urllib.parse, urllib.request
import xml.etree.ElementTree as ET

NS = {"s3": "http://s3.amazonaws.com/doc/2006-03-01/"}
for bucket in sys.argv[1:]:
    token, total, count, dems, dem_bytes, other = None, 0, 0, 0, 0, {}
    while True:
        q = {"list-type": "2", "max-keys": "1000"}
        if token:
            q["continuation-token"] = token
        url = f"https://{bucket}.s3.eu-central-1.amazonaws.com/?" + urllib.parse.urlencode(q)
        root = ET.fromstring(urllib.request.urlopen(url, timeout=60).read())
        for c in root.findall("s3:Contents", NS):
            key, size = c.find("s3:Key", NS).text, int(c.find("s3:Size", NS).text)
            total += size; count += 1
            if key.endswith("_DEM.tif"):
                dems += 1; dem_bytes += size
            else:
                ext = key.rsplit(".", 1)[-1] if "." in key else "(none)"
                other[ext] = other.get(ext, 0) + size
        if root.find("s3:IsTruncated", NS).text != "true":
            break
        token = root.find("s3:NextContinuationToken", NS).text
    print(f"{bucket}: {count} objects, {total/1e9:.1f} GB; {dems} DEM tiles {dem_bytes/1e9:.1f} GB "
          f"(mean {dem_bytes/max(dems,1)/1e6:.2f} MB); other: " + ", ".join(f"{k} {v/1e9:.2f} GB" for k, v in sorted(other.items())))
