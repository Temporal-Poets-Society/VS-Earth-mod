import sys,json,numpy as np,tifffile
sys.path.insert(0,sys.argv[1]); from lks92 import fwd
g=tifffile.imread(sys.argv[2]); X0,Y0,px=312750.0,6439000.0,250.0
q=g[...,1].astype(float); q[g[...,1]==-32768]=np.nan
v=q[np.isfinite(q)]
print(f"Latvia Quaternary thickness (Popovs et al. 2022, 250 m): cells {v.size}, median {np.median(v):.0f} m, p10 {np.percentile(v,10):.0f}, p90 {np.percentile(v,90):.0f}, p99 {np.percentile(v,99):.0f}, max {v.max():.0f}; share <=10 m {np.mean(v<=10)*100:.0f}%, <=30 m {np.mean(v<=30)*100:.0f}%, >60 m {np.mean(v>60)*100:.0f}%")
for name,lat,lon in json.load(open(sys.argv[3])):
    x,y=fwd(lat,lon); i=int((x-X0)/px); j=int((Y0-y)/px)
    if 0<=j<q.shape[0] and 0<=i<q.shape[1]:
        w=q[max(0,j-4):j+5,max(0,i-4):i+5]; w=w[np.isfinite(w)]
        if w.size: print(f"  {name:24s} median {np.median(w):5.0f} m (range {w.min():.0f}-{w.max():.0f} within 1 km)")
