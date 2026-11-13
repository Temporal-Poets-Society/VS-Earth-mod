import math
a=6378137.0; f=1/298.257222101; e=math.sqrt(2*f-f*f)
lat1,lat2,lat0,lon0=map(math.radians,(35.0,65.0,52.0,10.0)); FE,FN=4000000.0,2800000.0
def _m(p): return math.cos(p)/math.sqrt(1-e*e*math.sin(p)**2)
def _t(p): return math.tan(math.pi/4-p/2)/((1-e*math.sin(p))/(1+e*math.sin(p)))**(e/2)
n=(math.log(_m(lat1))-math.log(_m(lat2)))/(math.log(_t(lat1))-math.log(_t(lat2)))
F=_m(lat1)/(n*_t(lat1)**n); r0=a*F*_t(lat0)**n
def fwd(lat,lon):
    p=math.radians(lat); r=a*F*_t(p)**n; th=n*(math.radians(lon)-lon0)
    return FE+r*math.sin(th), FN+r0-r*math.cos(th)
