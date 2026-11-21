import os, sys
sys.stdout.reconfigure(encoding='utf-8')
d = 'outDig'
names = sorted({f.rsplit('_', 2)[0] for f in os.listdir(d) if f.endswith('.bin')})
same, diff = [], []
for n in names:
    for s in (10, 25):
        a = open(os.path.join(d, f'{n}_{s}_base.bin'), 'rb').read()
        b = open(os.path.join(d, f'{n}_{s}_w5.bin'), 'rb').read()
        ba, bb = a[a.index(b'\n') + 1:], b[b.index(b'\n') + 1:]
        # the probe's own flags differ by design inside works areas (65536/131072): compare the columns, then the flags
        nb = len(ba)
        (same if ba == bb else diff).append(f'{n} 1:{s}')
print(f'identical ({len(same)}):', ', '.join(same))
print(f'different ({len(diff)}):', ', '.join(diff))
