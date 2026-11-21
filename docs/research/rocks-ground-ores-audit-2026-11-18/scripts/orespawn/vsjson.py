"""Lenient reader for Vintage Story asset JSON (comments, unquoted keys, trailing commas)."""
import json, re

def _strip_comments(t):
    out = []; i = 0; n = len(t); ins = False; q = ''
    while i < n:
        c = t[i]
        if ins:
            out.append(c)
            if c == '\\' and i + 1 < n:
                out.append(t[i + 1]); i += 2; continue
            if c == q: ins = False
            i += 1; continue
        if c in '"\'':
            ins = True; q = c; out.append('"' if c == "'" else c); i += 1; continue
        if t.startswith('//', i):
            j = t.find('\n', i); i = n if j < 0 else j; continue
        if t.startswith('/*', i):
            j = t.find('*/', i + 2); i = n if j < 0 else j + 2; continue
        out.append(c); i += 1
    return ''.join(out)

def load(path):
    t = open(path, encoding='utf-8-sig').read()
    t = _strip_comments(t)
    t = re.sub(r'([{,\[]\s*)([A-Za-z_$][A-Za-z0-9_$]*)\s*:', r'\1"\2":', t)
    t = re.sub(r',\s*([}\]])', r'\1', t)
    return json.loads(t)
