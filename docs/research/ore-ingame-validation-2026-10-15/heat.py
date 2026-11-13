import json, base64, gzip, numpy as np, sys
d = json.load(open(r"C:\VS mods\Earth\assets\earthmap\config\ore-heatmaps-v1.json"))
W, H = d['width'], d['height']; C = W*H
L = {}
for l in d['layers']:
    g = np.frombuffer(gzip.decompress(base64.b64decode(l['data'])), dtype=np.uint8)
    L[l['code']] = (g[:C].reshape(H, W)/255., g[C:2*C].reshape(H, W)/255., g[2*C:3*C].reshape(H, W)/255., g[3*C:].view('<u2').reshape(H, W))
def cell(code, lat, lon):
    o, p, dp, m = L[code]; x = int(((lon+180)%360)*2); y = min(H-1, int((90-lat)*2)); return max(o[y,x], p[y,x]), dp[y,x], m[y,x]
def heat(code, lat, lon, depth_m=1e9):
    o, p, dp, m = L[code]
    s = np.maximum(o, p); s = np.where((dp > s) & (depth_m >= m), dp, s)
    fx = (lon+180)*2-.5; fy = (90-lat)*2-.5; x0 = int(np.floor(fx)); y0 = int(np.floor(fy)); tx = fx-x0; ty = fy-y0
    X0, X1 = x0 % W, (x0+1) % W; y1 = min(H-1, y0+1); y0 = max(0, y0)
    a, b, c, e = s[y0, X0], s[y0, X1], s[y1, X0], s[y1, X1]
    return (a+(b-a)*tx)*(1-ty)+(c+(e-c)*tx)*ty
if __name__ == "__main__":
    for code, lat, lon in [("Sn",50.2,-5.2),("Cu",-22.3,-68.9),("Fe",-22.5,118.5),("C_coal",51.5,7.2),("diamond",-28.7,24.8),("Au",-26.2,28.0)]:
        best = max(((heat(code, lat+dy, lon+dx), lat+dy, lon+dx) for dy in np.arange(-.5,.51,.05) for dx in np.arange(-.5,.51,.05)))
        print(code, "at given: %.2f" % heat(code, lat, lon), "cell(shallow,deep,minM)", cell(code, lat, lon), "best within 0.5deg: %.2f at %.2f,%.2f" % best)
    print()
    cands = {"amazon":(-3.5,-62),"congo":(0.5,21),"sahara":(23,15),"kazakh":(48,68),"pampas":(-35,-62),"wsiberia":(62,75),"rubalkhali":(20,50),"gsandy":(-21,125),"nunavut":(66,-100),"yakutia":(66,125),"mongolia":(45,105),"sudan":(13,28),"paraguay":(-22,-60),"bolivia_beni":(-14,-65),"mali":(17,-2),"niger":(17,10),"chad":(15,19)}
    for n,(lat,lon) in cands.items():
        hs = {c: heat(c, lat, lon) for c in L}
        nz = {c: round(v,2) for c, v in hs.items() if v > 0.01}
        print(n, lat, lon, nz)
