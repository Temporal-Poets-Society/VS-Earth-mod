import sys,numpy as np
d=np.load(sys.argv[1]);E=d['elev'];F=d['flags'];I=d['ice']
def at(lat,lon):
    r=int((90-lat)*10);c=int((lon+180)*10)
    return round(float(E[r,c]),1),int(F[r,c]),float(I[r,c])
for n,la,lo in [('Baikal',53.5,108.0),('Superior',47.7,-87.5),('Caspian',42.0,51.0),('Tanganyika',-6.0,29.5),('Ladoga',61.0,31.5),('Greenland interior',72,-40),('Antarctica',-80,0)]:
    print(n,at(la,lo))
