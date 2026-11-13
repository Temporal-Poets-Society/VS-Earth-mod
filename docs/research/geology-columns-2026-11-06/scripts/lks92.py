import math
# EPSG:25884 ETRS89 / TM Baltic93: lon0 24, k0 0.9996, FE 500000, FN 0, GRS80
a=6378137.0; f=1/298.257222101; e2=2*f-f*f; ep2=e2/(1-e2); k0=0.9996; lon0=math.radians(24)
def M(p):
    return a*((1-e2/4-3*e2**2/64-5*e2**3/256)*p-(3*e2/8+3*e2**2/32+45*e2**3/1024)*math.sin(2*p)+(15*e2**2/256+45*e2**3/1024)*math.sin(4*p)-(35*e2**3/3072)*math.sin(6*p))
def fwd(lat,lon):
    p=math.radians(lat); l=math.radians(lon)-lon0
    N=a/math.sqrt(1-e2*math.sin(p)**2); T=math.tan(p)**2; C=ep2*math.cos(p)**2; A=math.cos(p)*l
    x=k0*N*(A+(1-T+C)*A**3/6+(5-18*T+T*T+72*C-58*ep2)*A**5/120)
    y=k0*(M(p)+N*math.tan(p)*(A*A/2+(5-T+9*C+4*C*C)*A**4/24+(61-58*T+T*T+600*C-330*ep2)*A**6/720))
    return x+500000, y
