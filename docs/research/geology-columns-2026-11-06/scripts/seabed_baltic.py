import sys,json,collections,math
from shapely.geometry import shape, Point
from shapely.strtree import STRtree
d=json.load(open(sys.argv[1],encoding='utf-8')); fs=d['features']
geoms=[];props=[]
for f in fs:
    g=shape(f['geometry'])
    # GeoServer's JSON comes back in EPSG:3857 (web Mercator): to lon/lat
    from shapely.ops import transform
    R=6378137.0
    g=transform(lambda x,y,z=None:(math.degrees(x/R) if not hasattr(x,'__len__') else [math.degrees(v/R) for v in x], math.degrees(math.atan(math.sinh(y/R))) if not hasattr(y,'__len__') else [math.degrees(math.atan(math.sinh(v/R))) for v in y]),g)
    if not g.is_valid: g=g.buffer(0)
    geoms.append(g); props.append(f['properties'])
def area_km2(g,lat): return g.area*111.32**2*math.cos(math.radians(lat))
for ctry in ['Latvia','Estonia','Lithuania']:
    c=collections.Counter()
    for g,p in zip(geoms,props):
        if p['country']!=ctry: continue
        c[p['folk_7cl_txt']]+=area_km2(g,g.centroid.y)
    t=sum(c.values()); print(f"{ctry} waters, Folk 7 classes by area ({t:,.0f} km2 mapped):"); [print(f"  {k:40s} {v/t*100:5.1f}%") for k,v in c.most_common()]
tree=STRtree(geoms)
pts=[("Liepaja, 2 km off",56.52,20.95),("Liepaja, 20 km off",56.52,20.65),("Pape coast",56.15,20.98),("Jurkalne, 3 km off",56.95,21.30),("Ventspils, 10 km off",57.40,21.38),("Irbe Strait",57.75,22.0),("Gulf of Riga centre",57.3,23.6),("Riga bay off Jurmala",57.02,23.8),("Saulkrasti, 2 km off",57.27,24.35),("Gulf of Riga NE, Salacgriva",57.75,24.25),("Kolka cape",57.78,22.62),("Parnu bay",58.3,24.4),("Tallinn bay",59.5,24.75)]
print("Sites:")
for n,la,lo in pts:
    hit=[props[k] for k in tree.query(Point(lo,la),predicate='intersects')]
    print(f"  {n:28s} "+("; ".join(f"{h['folk_7cl_txt']} ({h['country']}, orig {h['original_substrate']})" for h in hit[:2]) if hit else "unmapped"))
