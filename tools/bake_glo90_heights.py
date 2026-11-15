"""Bakes data/glo90-heights.bin: the heights checksum of every GLO-90 square of Earth's data release egl-v1.

EA-0106 (Fable's re-review of 255baa7): whether a tile is the release's is judged by its decoded heights, not by its
file's bytes, which depend on the runtime's Brotli encoder (.NET 10 encodes 341 of 602 release tiles to other bytes
than .NET 8). Each tile's header carries the FNV-1a checksum of its heights (DemTiles.Checksum), which DemTiles.Decode
verifies; this table holds those of the release's tiles.

Format (little-endian): "EGLH", u32 version 1, u32 count, 8 bytes of the tile list's SHA-256 (data/glo90-tiles.bin, the
squares in the order below), then count u64 checksums, one per square of the tile list in its bit order (SquareIndex
ascending: north to south, west to east).

    python tools/bake_glo90_heights.py <store's glo90 folder> data/glo90-tiles.bin data/glo90-heights.bin [<manifest>]

The store must hold egl-v1's tiles. (Fable's overview low 10, 14 November 2026) That is checked here, not trusted: the
release's manifest (by default the store's own copy, manifest-egl1.json) must be egl-v1's (the size and SHA-256 that
src/Core/DemDownloader.cs pins), list exactly the tile list's squares, and every tile file must be its entry's length
and SHA-256; any difference stops the bake, naming the square. Then each tile's header is read; ElevationTests decodes
tiles and checks them against this table.
"""
import hashlib
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def short_name(lat_s, lon_w):
    return ('N' if lat_s >= 0 else 'S') + '%02d' % abs(lat_s) + ('E' if lon_w >= 0 else 'W') + '%03d' % abs(lon_w)


def header(data):
    if data[:4] != b'EGLO' or data[4] != 1:
        raise ValueError('not an Earth GLO-90 tile of format 1')
    lat_s, lon_w = struct.unpack_from('<hh', data, 8)
    etag_len = data[28]
    checksum, = struct.unpack_from('<Q', data, 29 + etag_len)
    return lat_s, lon_w, checksum


def pinned_manifest():
    """egl-v1's manifest size and SHA-256 as the mod pins them (Glo90ManifestBytes, Glo90ManifestSha256)."""
    src = open(os.path.join(HERE, '..', 'src', 'Core', 'DemDownloader.cs'), encoding='utf-8').read()
    size = re.search(r'Glo90ManifestBytes\s*=\s*([0-9_]+)', src)
    sha = re.search(r'Glo90ManifestSha256\s*=\s*"([0-9a-f]{64})"', src)
    if not size or not sha:
        raise SystemExit('the pinned manifest (Glo90ManifestBytes, Glo90ManifestSha256) is not in src/Core/DemDownloader.cs')
    return int(size.group(1).replace('_', '')), sha.group(1)


def release_tiles(manifest_path):
    """The release's squares: short name -> (length, SHA-256), from a manifest that is egl-v1's."""
    data = open(manifest_path, 'rb').read()
    size, sha = pinned_manifest()
    if len(data) != size or hashlib.sha256(data).hexdigest() != sha:
        raise SystemExit("%s is not egl-v1's manifest (%d bytes, SHA-256 %s; egl-v1: %d, %s)"
                         % (manifest_path, len(data), hashlib.sha256(data).hexdigest(), size, sha))
    m = json.loads(data)
    return {name: (entry[2], entry[3].lower()) for name, entry in m['tiles'].items()}


def main():
    store, tile_list, out = sys.argv[1], sys.argv[2], sys.argv[3]
    manifest = sys.argv[4] if len(sys.argv) > 4 else os.path.join(store, 'manifest-egl1.json')
    release = release_tiles(manifest)
    bits = open(tile_list, 'rb').read()
    sums = []
    for ix in range(360 * 180):
        if not bits[ix >> 3] & (0x80 >> (ix & 7)):
            continue
        lat_s, lon_w = 89 - ix // 360, ix % 360 - 180
        name = short_name(lat_s, lon_w)
        if name not in release:
            raise SystemExit(name + ' is in the tile list but not in the release')
        length, sha = release.pop(name)
        with open(os.path.join(store, name + '.egl'), 'rb') as f:
            data = f.read()
        if len(data) != length or hashlib.sha256(data).hexdigest() != sha:
            raise SystemExit("%s in %s is not the release's tile (%d bytes; the release's: %d, its SHA-256)" % (name, store, len(data), length))
        la, lo, checksum = header(data)
        if (la, lo) != (lat_s, lon_w):
            raise ValueError(name + ' holds another square')
        sums.append(checksum)
    if release:
        raise SystemExit('%d squares of the release are not in the tile list, %s first' % (len(release), sorted(release)[0]))
    body = b'EGLH' + struct.pack('<II', 1, len(sums)) + hashlib.sha256(bits).digest()[:8] + b''.join(struct.pack('<Q', s) for s in sums)
    with open(out, 'wb') as f:
        f.write(body)
    print(len(sums), "squares, every tile the release's (manifest and SHA-256),", len(body), 'bytes ->', out)


if __name__ == '__main__':
    main()
