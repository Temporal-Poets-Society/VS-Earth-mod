# Monte Carlo estimate of vanilla VS 1.22.7 deposit volumes (re-implemented from reading, not copied).
import random, math
import numpy as np

rng = random.Random(12345)

def nat(avg, var, dist="uniform", offset=0.0):
    r = rng.random
    if dist == "uniform":
        return offset + avg + (r() - 0.5) * 2 * var
    if dist == "gaussian":
        return offset + avg + ((r() + r() + r()) / 3 - 0.5) * 2 * var
    if dist == "narrowgaussian":
        return offset + avg + (sum(r() for _ in range(6)) / 6 - 0.5) * 2 * var
    if dist == "triangle":
        return offset + avg + ((r() + r()) / 2 - 0.5) * 2 * var
    if dist == "invexp":
        return offset + avg + r() * r() * var
    if dist == "stronginvexp":
        return offset + avg + r() * r() * r() * var
    if dist == "strongerinvexp":
        return offset + avg + r() * r() * r() * r() * var
    raise ValueError(dist)

def disc_volume(R, T, n_mean=0.5, n_sd=0.12, thick_cap=None):
    """Expected placed blocks of one disc deposit assuming host rock everywhere."""
    r = min(64, int(nat(*R)))
    if r <= 0:
        return 0.0
    k = max(-0.25, min(0.25, rng.random() - 0.5))
    rx = r - int(r * k)
    rz = r + int(r * k)
    t = nat(*T)
    dt = int(t) + (1 if rng.random() < t - int(t) else 0)
    if thick_cap is not None:
        dt = min(dt, thick_cap)
    total = 0.0
    for dx in range(-rx, rx):
        a = dx * dx / (rx * rx)
        for dz in range(-rz, rz):
            n = min(1.0, max(0.0, rng.gauss(n_mean, n_sd))) if r > 3 else 0.0
            e = 1.0 - n * 0.2 - (a + dz * dz / (rz * rz))
            if e < 0:
                continue
            total += dt * min(1.0, max(0.0, e * 2 - 0.2))
    return total

def mean_disc(R, T, N=4000, thick_cap=None):
    s = 0.0; s2 = 0.0; rs = []
    for _ in range(N):
        v = disc_volume(R, T, thick_cap=thick_cap)
        s += v
    return s / N

# ---- ore map factor statistics ----
def random_ore(rr):
    num = rr.randrange(1024)
    if num < 30:
        return 255
    if num < 105:
        return 75 + rr.randrange(100)
    if num < 190:
        return 20 + rr.randrange(20)
    return 0

def oremap_stats(contrast=1.0, sub=0.0, cells=400, samples_per_cell=16):
    rr = random.Random(7)
    g = np.array([[random_ore(rr) for _ in range(cells + 1)] for _ in range(cells + 1)], dtype=float)
    u = np.linspace(0, 1, samples_per_cell, endpoint=False) + 0.5 / samples_per_cell
    fx, fz = np.meshgrid(u, u)
    vals = []
    for i in range(cells):
        for j in range(cells):
            a, b, c, d = g[i, j], g[i, j + 1], g[i + 1, j], g[i + 1, j + 1]
            v = (a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz)
            v = np.floor(v)
            v = np.clip((v - 128) * contrast + 128 - sub, 0, 255)
            vals.append(v.ravel())
    v = np.concatenate(vals) / 255.0
    return dict(mean=v.mean(), frac_pos=(v > 0).mean(), frac_gt_025=(v > 0.25).mean(),
                frac_gt_05=(v > 0.5).mean(), frac_gt_08=(v > 0.8).mean(), p90=np.percentile(v, 90), p99=np.percentile(v, 99))

if __name__ == "__main__":
    print("ORE MAP default", oremap_stats())
    print("ORE MAP c1.5 s40", oremap_stats(1.5, 40))
    deposits = {
        # name: (Radius, Thickness, tries, oremap(contrast,sub) or None, thick_cap)
        "nativecopper(deep)": ((7, 3, "uniform"), (1, 2, "stronginvexp"), 25, (1, 0)),
        "surfacecopper": ((4.25, 1, "uniform"), (1, 1, "stronginvexp"), 0.2, None),
        "malachite": ((7, 3, "uniform"), (1, 2, "stronginvexp"), 10, (1, 0)),
        "cassiterite(normal)": ((5, 3, "gaussian"), (1, 0, "uniform"), 7, (1, 0)),
        "cassiterite(massive)": ((12, 3, "gaussian"), (1, 0, "uniform"), 0.1, (1, 0)),
        "surfacecassiterite": ((3, 0.5, "uniform"), (1, 0, "uniform"), 0.007, None),
        "bismuthinite": ((4, 3, "triangle"), (1, 0, "uniform"), 16, (1, 0)),
        "sphalerite": ((4, 3, "gaussian"), (1, 0, "uniform"), 12.5, (1, 0)),
        "galena": ((4, 4, "gaussian"), (1, 0, "uniform"), 40, (1, 0)),
        "limonite": ((26, 10, "uniform"), (1, 0, "uniform"), 0.8, (1, 0)),
        "hematite": ((26, 10, "uniform"), (1, 0, "uniform"), 0.5, (1, 0)),
        "magnetite": ((26, 10, "uniform"), (1, 0, "uniform"), 0.3, (1, 0)),
        "lignite": ((10, 5, "gaussian"), (2, 1.8, "stronginvexp"), 8, (1, 0)),
        "bituminouscoal": ((10, 5, "gaussian"), (2, 1.3, "stronginvexp"), 7, (1, 0)),
        "anthracite": ((8, 5, "gaussian"), (1.5, 0.6, "stronginvexp"), 6, (1.5, 40)),
        "sulfur": ((8, 2, "gaussian"), (1, 0, "uniform"), 15, (1, 0)),
        "saltpeter(in air)": ((10, 10, "gaussian"), (7, 3, "uniform"), 7, None),
        "borax": ((8, 2, "gaussian"), (1, 0, "uniform"), 6, (1, 0)),
        "kernite": ((8, 2, "gaussian"), (1, 0, "uniform"), 2, (1, 0)),
        "fluorite": ((8, 2, "gaussian"), (1, 0, "uniform"), 2, (1, 0)),
        "phosphorite": ((8, 2, "gaussian"), (1, 0, "uniform"), 2, (1, 0)),
        "graphite": ((8, 2, "gaussian"), (1, 0, "uniform"), 6, (1, 0)),
        "halite(dome, cap 78)": ((10, 8, "uniform"), (150, 1, "uniform"), 0.3, (1, 0), 78),
        "halite(dry lake)": ((28, 3, "uniform"), (3.3, 1, "uniform"), 0.07, None),
        "quartz": ((32, 32, "gaussian"), (1.2, 3, "invexp"), 0.5, None),
        "olivine": ((32, 32, "gaussian"), (1.2, 3, "invexp"), 0.5, None),
        "lapis": ((2, 10, "invexp"), (1, 0, "uniform"), 5, (1, 0)),
        "cinnabar": ((8, 2, "gaussian"), (1, 0, "uniform"), 1, (1, 0)),
        "diamond": ((2, 1, "gaussian"), (1, 0, "uniform"), 18, (1, 0)),
        "emerald": ((1, 0.5, "gaussian"), (1, 0, "uniform"), 64, (1, 0)),
        "peridot(standalone)": ((1, 0.5, "gaussian"), (1, 0, "uniform"), 64, (1, 0)),
        "chromite": ((5, 10, "invexp"), (1, 0, "uniform"), 3, (1.5, 40)),
        "ilmenite": ((6, 10, "invexp"), (1, 0, "uniform"), 2, (1.5, 40)),
        "pentlandite": ((9, 3, "uniform"), (1, 2, "stronginvexp"), 5, (1.5, 40)),
        "rhodochrosite": ((3, 10, "invexp"), (1, 0, "uniform"), 5, (1, 0)),
        "flint(surface)": ((3.5, 2, "uniform"), (1, 1, "stronginvexp"), 1.5, None),
        "flint(large)": ((7, 3, "uniform"), (1, 1, "stronginvexp"), 0.05, None),
        "fireclay(small)": ((4, 2, "uniform"), (2, 1, "uniform"), 0.004, None),
        "fireclay(large)": ((12, 5, "uniform"), (3.3, 1, "uniform"), 0.0002, None),
        "obsidian": ((5, 2, "gaussian"), (1, 0, "uniform"), 12, None),
    }
    om = {(1, 0): oremap_stats(1, 0)["mean"], (1.5, 40): oremap_stats(1.5, 40)["mean"]}
    print("mean factors", om)
    print("name | E[blocks/deposit] | tries | mean oremap F | E[blocks/chunk] @meanF | @F=1")
    for name, spec in deposits.items():
        R, T, tries, omk = spec[:4]
        cap = spec[4] if len(spec) > 4 else None
        N = 1500 if R[0] > 20 else 6000
        v = mean_disc(R, T, N=N, thick_cap=cap)
        F = om[omk] if omk else 1.0
        print(f"{name} | {v:.1f} | {tries} | {F:.3f} | {tries*F*v:.1f} | {tries*v:.1f}")
