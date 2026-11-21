import json, struct, sys
for path in sys.argv[1:]:
    b = open(path, 'rb').read()
    if b[:4] == b'EGTS':
        n = struct.unpack('<I', b[4:8])[0]; o = 8
        l = struct.unpack('<I', b[o:o+4])[0]; t = b[o+4:o+4+l]
    else:
        n = 1; t = b
    hl = struct.unpack('<I', t[4:8])[0]
    h = json.loads(t[8:8+hl])
    print(path, 'tiles', n, 'step', h['step'], 'nx', h['nx'])
    for f in h['fields']:
        print('  ', f['name'], f['type'], f.get('interp'), 'scale', f.get('scale'), 'curve', f.get('curve'), 'legend', f.get('legend'))
