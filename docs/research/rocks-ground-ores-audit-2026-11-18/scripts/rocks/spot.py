import sys,numpy as np
d=np.load(sys.argv[1]);E=d['elev'];F=d['flags']
def at(lat,lon):
    r=int((90-lat)*10);c=int((lon+180)*10)
    return float(E[r,c]),int(F[r,c])
for n,la,lo in [('DeadSea',31.5,35.5),('Lhasa',29.65,91.1),('Denver',39.74,-104.99),('Atlantic',30,-40),('Zermatt',46.02,7.75),('Everest',27.99,86.93),('GrandCanyonRim',36.06,-112.14)]:
    print(n,at(la,lo))
