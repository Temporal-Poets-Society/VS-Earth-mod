"""Train the learned codec on random GLO-90 tiles (not the sample or test tiles).
train.py <out.pt> --beta B [--steps S] [--patch P] [--batch N] [--tiles T] [--threads K] [--n 64] [--m 96] [--init ckpt]
Loss = bits per point + beta * (land MSE in km^2)."""
import argparse, json, math, os, random, sys, time
import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from model import Codec
import baselines as B

ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--beta", type=float, required=True)
ap.add_argument("--steps", type=int, default=6000)
ap.add_argument("--patch", type=int, default=128)
ap.add_argument("--batch", type=int, default=16)
ap.add_argument("--tiles", type=int, default=600)
ap.add_argument("--threads", type=int, default=16)
ap.add_argument("--n", type=int, default=64)
ap.add_argument("--m", type=int, default=96)
ap.add_argument("--lr", type=float, default=3e-4)
ap.add_argument("--init", default=None)
ap.add_argument("--seed", type=int, default=1)
a = ap.parse_args()
torch.set_num_threads(a.threads)
random.seed(a.seed); np.random.seed(a.seed); torch.manual_seed(a.seed)

import tifffile
tiles = B.all_tiles()
held = set(tiles[::40]) | {t for v in B.TEST.values() for t in v}
pool = [t for t in tiles if t not in held and abs(t[0] + 0.5) < 80]
random.Random(7).shuffle(pool)
data = []
t0 = time.time()
for la, lo in pool[: a.tiles * 2]:
    h = tifffile.imread(os.path.join(B.GLO, B.tile_name(la, lo)))
    if (h != 0).mean() < 0.05 or min(h.shape) < a.patch:
        continue
    data.append(np.round(h).astype(np.int16))
    if len(data) >= a.tiles:
        break
print(f"{len(data)} tiles in {time.time() - t0:.0f} s", flush=True)

model = Codec(a.n, a.m)
if a.init:
    model.load_state_dict(torch.load(a.init)["model"])
opt = torch.optim.Adam(model.parameters(), lr=a.lr)
sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, a.steps, a.lr * 0.05)


def batch():
    xs = []
    for _ in range(a.batch):
        h = data[random.randrange(len(data))]
        r = random.randrange(h.shape[0] - a.patch + 1)
        c = random.randrange(h.shape[1] - a.patch + 1)
        p = h[r:r + a.patch, c:c + a.patch].astype(np.float32)
        if random.random() < 0.5:
            p = p[:, ::-1]
        if random.random() < 0.5:
            p = p[::-1, :]
        xs.append(p.copy())
    x = torch.from_numpy(np.stack(xs))[:, None] / 1000.0
    return x


log = []
t0 = time.time()
model.train()
for step in range(1, a.steps + 1):
    x = batch()
    land = (x != 0).float()
    x_hat, lik = model(x)
    bits = -torch.log2(lik).sum() / x.numel()
    mse = (((x_hat - x) ** 2) * (land + 0.01 * (1 - land))).sum() / land.sum().clamp_min(1)
    loss = bits + a.beta * mse
    opt.zero_grad()
    loss.backward()
    torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
    opt.step()
    sched.step()
    if step % 100 == 0:
        rms = math.sqrt(mse.item()) * 1000
        bpl = bits.item() * x.numel() / land.sum().item()
        log.append((step, bpl, rms))
        print(f"{step} bits/land point {bpl:.4f} rms {rms:.1f} m  {time.time() - t0:.0f} s", flush=True)
    if step % 1000 == 0 or step == a.steps:
        torch.save({"model": model.state_dict(), "args": vars(a), "log": log}, a.out)
