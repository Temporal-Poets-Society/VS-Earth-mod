"""Build runtime categorical GLiM/GUM grids. No interpolation, extrapolation or source edits.

geology-grids-v1.json is shared by geology rules v1 and v2 and MUST stay byte-identical: 0.12 saves fingerprint
SHA-256(grid text + rules-v1 text) = 18D3663E...F300 and the v1 strata are frozen. Rules v2 derives its coastal fill
and Voronoi contacts from this file at load time (RegionalGeologyCatalog), so no v2 grid is baked.
Run with --check to verify an existing file instead of overwriting it."""
import sys
from pathlib import Path
import base64, hashlib, json, zipfile, math
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'docs/research/regional-geology-2026-09-29'
DEST=ROOT/'assets/earth/config'
DEST.mkdir(parents=True,exist_ok=True)
def grid(text):
    lines=text.splitlines()
    header={l.split()[0].lower():float(l.split()[1]) for l in lines[:6]}
    assert header['ncols']==720 and header['cellsize']==.5
    values=[int(v) for l in lines[6:] for v in l.split()]
    assert len(values)==int(header['ncols']*header['nrows'])
    # GUM starts at 83.514743N and -179.999981E. Regrid by categorical containing cell,
    # retaining nodata beyond that actual coverage instead of shifting rows or interpolating IDs.
    output=[]
    north=header['yllcorner']+header['nrows']*.5
    for row in range(360):
        sy=math.floor((north-(89.75-row*.5))/.5)
        for col in range(720):
            sx=math.floor((-179.75+col*.5-header['xllcorner'])/.5)
            v=values[sy*720+sx] if 0<=sx<720 and 0<=sy<header['nrows'] else header['nodata_value']
            output.append(0 if v==header['nodata_value'] else v)
    return bytes(output)
with zipfile.ZipFile(SRC/'glim-download.zip') as z:
    a=grid(z.read('glim_wgs84_0point5deg.txt.asc').decode())
b=grid((SRC/'gum_v1.0_0point5deg.txt.asc').read_text())
obj={'version':1,'width':720,'height':360,'cellSize':.5,'west':-180,'north':90,
     'lithology':base64.b64encode(a).decode(),'sediment':base64.b64encode(b).decode()}
out=DEST/'geology-grids-v1.json'
baked=(json.dumps(obj,separators=(',',':'))+'\n').encode()
if '--check' in sys.argv:
    same=out.exists() and out.read_bytes().replace(b'\r\n',b'\n')==baked
    print('geology-grids-v1.json', 'matches the sources' if same else 'DIFFERS from the sources')
    sys.exit(0 if same else 1)
if out.exists():
    if out.read_bytes().replace(b'\r\n',b'\n')!=baked:
        sys.exit('Refusing to overwrite geology-grids-v1.json with different content: v1 saves depend on it. Bake a new versioned file instead.')
    print('geology-grids-v1.json already matches the sources; existing bytes (incl. line ending) kept')
else:
    out.write_bytes(baked)
manifest={'version':1,'runtimeSha256':hashlib.sha256(out.read_bytes()).hexdigest(),
 'sources':[{'name':'GLiM 0.5 degree v1.0','doi':'10.1594/PANGAEA.788537','license':'CC-BY-3.0','file':'glim-download.zip'},
            {'name':'GUM 0.5 degree v1.0','doi':'10.1594/PANGAEA.884822','license':'CC-BY-3.0','file':'gum_v1.0_0point5deg.txt.asc'}],
 'classCounts':{'glim':{str(v):a.count(v) for v in sorted(set(a))},'gum':{str(v):b.count(v) for v in sorted(set(b))}}}
for source in manifest['sources']:
    source['sha256']=hashlib.sha256((SRC/source['file']).read_bytes()).hexdigest()
(SRC/'bake-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(manifest,indent=2))
