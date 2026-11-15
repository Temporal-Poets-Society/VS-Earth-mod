import re,subprocess,sys
repo=sys.argv[1]
revs=sys.argv[2:]
def table(rev):
    try: src=subprocess.run(['git','-C',repo,'show',rev+':src/EarthParams.cs'],capture_output=True,check=True).stdout.decode('utf-8','replace')
    except subprocess.CalledProcessError: return None
    # only the EarthParams class (before FlatZoneData)
    cut=src.find('class FlatZoneData')
    main=src[:cut] if cut>0 else src
    t={}
    for m in re.finditer(r'\[ProtoMember\((\d+)\)\]\s*public\s+([\w<>\.]+)\s+(\w+)',main):
        n=int(m.group(1)); 
        if n in t: print(rev,'DUPLICATE',n,t[n],m.group(3))
        t[n]=(m.group(3),m.group(2))
    return t
head=table('fef6166')
for rev in revs:
    t=table(rev)
    if t is None: print(rev,'no EarthParams.cs'); continue
    bad=[]
    for n,(name,typ) in sorted(t.items()):
        if n not in head: bad.append(f'{n} {name} {typ}: GONE at head')
        elif head[n]!=(name,typ): bad.append(f'{n} {name} {typ}: head has {head[n]}')
    print(rev,len(t),'fields;','OK' if not bad else '; '.join(bad))
