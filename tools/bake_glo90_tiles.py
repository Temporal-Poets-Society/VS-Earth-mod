"""The list of 1-degree squares Copernicus GLO-90 has tiles for (decision 0051), as a 360 x 180 bitmap (8,100 bytes).
Row 0 is 89..90 N, column 0 is 180..179 W; bit 7 of each byte first. Squares without a tile are open sea.
usage: bake_glo90_tiles.py <tiles.txt> <out.bin>
tiles.txt: one published name per line (Copernicus_DSM_COG_30_N60_00_E006_00_DEM), from the bucket's listing
(aws s3 ls --no-sign-request s3://copernicus-dem-90m/ ; 26,475 tiles on 3 November 2026)."""
import sys


def square(name):
    p = name.strip().split("_")
    la, lo = p[4], p[6]
    lat = int(la[1:]) * (-1 if la[0] == "S" else 1)
    lon = int(lo[1:]) * (-1 if lo[0] == "W" else 1)
    return lat, lon


def main():
    names = [l for l in open(sys.argv[1], encoding="utf-8").read().split() if l.startswith("Copernicus_DSM_COG_30_")]
    bits = bytearray(360 * 180 // 8)
    seen = set()
    for n in names:
        lat, lon = square(n)
        assert -90 <= lat < 90 and -180 <= lon < 180, n
        i = (89 - lat) * 360 + (lon + 180)
        bits[i >> 3] |= 0x80 >> (i & 7)
        seen.add((lat, lon))
    open(sys.argv[2], "wb").write(bytes(bits))
    print(f"{len(names)} names, {len(seen)} squares -> {sys.argv[2]} ({len(bits)} bytes)")
    for label, lat, lon in (("Armenia (Yerevan)", 40, 44), ("Azerbaijan (Baku)", 40, 49), ("Nærøyfjord", 60, 6),
                            ("Everest", 27, 86), ("mid-Pacific", 0, -150), ("Greenland ice", 72, -40), ("Antarctica", -80, 0)):
        print(f"  {label:20} N{lat} E{lon}: {'tile' if (lat, lon) in seen else 'none'}")


if __name__ == "__main__":
    main()
