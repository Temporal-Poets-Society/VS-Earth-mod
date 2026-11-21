# Decode Earth's terrain.bin (2' grid) into a 0.1-degree elevation grid (every 3rd cell) + ocean flag + ice; save npz.
import sys,struct,numpy as np,brotli
src,out=sys.argv[1],sys.argv[2]
b=open(src,'rb').read()
assert b[:4]==b'EMTR'
ver,w,h,t,count=struct.unpack_from('<iiiii',b,4)
print(ver,w,h,t,count)
step=3
E=np.zeros((h//step,w//step),np.float32);O=np.zeros((h//step,w//step),np.uint8);I=np.zeros((h//step,w//step),np.float32)
tx=w//t
for k in range(count):
    off,ln=struct.unpack_from('<ii',b,24+8*k)
    raw=brotli.decompress(b[off:off+ln])
    res=np.frombuffer(raw[:2*t*t],'<i2').reshape(t,t).astype(np.int32)
    # undo paeth: q = res + a + b - c, sequential
    q=np.zeros((t,t),np.int32)
    q[0,0]=res[0,0]
    for x in range(1,t): q[0,x]=res[0,x]+q[0,x-1]
    for y in range(1,t):
        q[y,0]=res[y,0]+q[y-1,0]
        # row recurrence q[y,x]=res+q[y,x-1]+q[y-1,x]-q[y-1,x-1]; define d[x]=q[y,x]-q[y-1,x] => d[x]=res[y,x]+d[x-1]
        d=np.cumsum(res[y,:])  # d[0]=res[y,0]
        q[y,:]=q[y-1,:]+d
    flags=np.frombuffer(raw[4*t*t:5*t*t],np.uint8).reshape(t,t)
    iceres=np.frombuffer(raw[5*t*t:7*t*t],'<i2').reshape(t,t).astype(np.int32)
    iq=np.zeros((t,t),np.int32); iq[0,:]=np.cumsum(iceres[0,:])
    for y in range(1,t): iq[y,:]=iq[y-1,:]+np.cumsum(iceres[y,:])
    e=np.where(q>=0,50*np.expm1(q/700.0),-50*np.expm1(-q/150.0))
    r,c=divmod(k,tx)
    E[r*t//step:(r+1)*t//step,c*t//step:(c+1)*t//step]=e[1::step,1::step]
    O[r*t//step:(r+1)*t//step,c*t//step:(c+1)*t//step]=flags[1::step,1::step]
    I[r*t//step:(r+1)*t//step,c*t//step:(c+1)*t//step]=iq[1::step,1::step]*4
np.savez_compressed(out,elev=E,flags=O,ice=I)
print('done',E.shape,float(E.min()),float(E.max()))
