"""Depth aggregation must not make deep evidence shallow by combining unrelated records."""
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools'))
from bake_ore_heatmaps import combine_deep
import numpy as np
w=np.array([200,80,200,0],dtype=np.uint8)
d=np.array([900,200,900,0],dtype=np.uint16)
new=np.array([80,200,200,200],dtype=np.uint8)
actual_w,actual_d=combine_deep(w,d,new,400)
assert list(actual_w)==[200,200,200,200]
assert list(actual_d)==[900,400,400,400]
# Order-independent for strongest-record selection, including equal-strength records.
for rows in [[(80,200),(200,900),(200,700)],[(200,900),(200,700),(80,200)]]:
    weight=np.array([0],dtype=np.uint8);depth=np.array([0],dtype=np.uint16)
    for v,z in rows:weight,depth=combine_deep(weight,depth,np.array([v],dtype=np.uint8),z)
    assert weight[0]==200 and depth[0]==700
print('PASS: paired deep evidence and deterministic aggregation')
