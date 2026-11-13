"""Earth ground tiles (EGT1): self-describing raster tiles for the ground-column generator.

A tile is a latitude/longitude rectangle holding one or more fields on one grid. Layout:
  b"EGT1", uint32 little-endian header length, UTF-8 JSON header, then the field planes (row 0 = north edge, row-major,
  little-endian, in header order) zlib-compressed as one block.
Header: {"name", "source", "licence", "west", "north", "step", "nx", "ny", "fields": [
  {"name", "type": "u8"|"u16"|"i16"|"f32", "scale", "offset", "nodata", "interp": "linear"|"category", "legend": [names]}]}
A stored value v means scale*v + offset; nodata (stored) means no value here, so the next tile in the stack answers.
Categories store an index into "legend"; the rules match legend names, never indices.
See docs/research/geology-columns-2026-11-06/global-design.md.
"""
import json, struct, zlib
import numpy as np

TYPES = {"u8": "<u1", "u16": "<u2", "i16": "<i2", "f32": "<f4"}


def field(name, data, type="u8", scale=1.0, offset=0.0, nodata=None, interp="linear", legend=None):
    """One field: data is a (ny, nx) array of stored values."""
    if nodata is None:
        nodata = {"u8": 255, "u16": 65535, "i16": -32768, "f32": float("nan")}[type]
    f = {"name": name, "type": type, "scale": scale, "offset": offset, "nodata": nodata, "interp": interp, "data": data}
    if legend is not None:
        f["legend"] = list(legend)
        f["interp"] = "category"
    return f


def write(path, name, source, licence, west, north, step, fields):
    blob = pack(name, source, licence, west, north, step, fields)
    with open(path, "wb") as fh:
        fh.write(blob)
    return len(blob)


def write_set(path, blobs):
    """A tile set (EGTS): b"EGTS", uint32 count, then per tile uint32 length and an EGT1 tile. The game unpacks each tile
    only when a column inside it is generated, so a set can cover the world at a fine step."""
    out = [b"EGTS", struct.pack("<I", len(blobs))]
    for b in blobs:
        out += [struct.pack("<I", len(b)), b]
    blob = b"".join(out)
    with open(path, "wb") as fh:
        fh.write(blob)
    return len(blob)


def pack(name, source, licence, west, north, step, fields):
    ny, nx = fields[0]["data"].shape
    head = {"name": name, "source": source, "licence": licence, "west": west, "north": north, "step": step, "nx": nx, "ny": ny,
            "fields": [{k: v for k, v in f.items() if k != "data"} for f in fields]}
    for f in head["fields"]:
        if isinstance(f["nodata"], float) and f["nodata"] != f["nodata"]:
            f["nodata"] = "nan"
    body = b"".join(np.ascontiguousarray(f["data"]).astype(TYPES[f["type"]]).tobytes() for f in fields)
    for f in fields:
        assert f["data"].shape == (ny, nx), (f["name"], f["data"].shape)
    hj = json.dumps(head, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return b"EGT1" + struct.pack("<I", len(hj)) + hj + zlib.compress(body, 9)
