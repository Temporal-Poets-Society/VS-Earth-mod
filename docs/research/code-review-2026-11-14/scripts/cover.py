import re, pathlib, sys
root = pathlib.Path(sys.argv[1])
tests = ''
for p in (root / 'tests').rglob('*.cs'):
    if 'obj' in p.parts or 'bin' in p.parts: continue
    tests += p.read_text(encoding='utf-8', errors='replace') + '\n'
for p in (root / 'tests').rglob('*.py'):
    tests += p.read_text(encoding='utf-8', errors='replace') + '\n'
selftest = (root / 'src' / 'SelfTest.cs').read_text(encoding='utf-8', errors='replace')
rows = []
for p in sorted((root / 'src').rglob('*.cs')):
    text = p.read_text(encoding='utf-8', errors='replace')
    lines = text.count('\n')
    types = set(re.findall(r'\b(?:class|struct|enum|record|interface)\s+([A-Z]\w+)', text))
    types |= set(re.findall(r'\brecord\s+struct\s+([A-Z]\w+)', text))
    hit = sorted(t for t in types if re.search(r'\b' + re.escape(t) + r'\b', tests))
    st = sorted(t for t in types if p.name != 'SelfTest.cs' and re.search(r'\b' + re.escape(t) + r'\b', selftest))
    rows.append((p.relative_to(root / 'src').as_posix(), lines, len(types), hit, st))
none = [r for r in rows if not r[3]]
print('src files', len(rows), 'lines', sum(r[1] for r in rows), '; with no type named in tests:', len(none), 'lines', sum(r[1] for r in none))
for r in sorted(none, key=lambda r: -r[1]):
    print(f'  {r[0]:45s} {r[1]:6d} lines  selftest: {"yes" if r[4] else "no"}')
print('largest files that ARE named in tests:')
for r in sorted([r for r in rows if r[3]], key=lambda r: -r[1])[:12]:
    print(f'  {r[0]:45s} {r[1]:6d} lines  {len(r[3])}/{r[2]} types')
