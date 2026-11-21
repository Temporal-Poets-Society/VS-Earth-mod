# Approximate child-deposit (pointcloud) yield per spawned child, sheet model.
import random
rng = random.Random(3)

def nat(avg, var, dist="uniform"):
    r = rng.random
    if dist == "uniform":
        return avg + (r() - 0.5) * 2 * var
    if dist == "gaussian":
        return avg + ((r() + r() + r()) / 3 - 0.5) * 2 * var
    if dist == "invexp":
        return avg + r() * r() * var
    if dist == "stronginvexp":
        return avg + r() * r() * r() * var
    raise ValueError

def local_thickness(T):
    # parent column thickness = depositThickness * taper; taper area-weighted ~ uniform mix (0.4 at 1, 0.5 linear 1->0)
    t = nat(*T)
    dt = int(t) + (1 if rng.random() < t - int(t) else 0)
    u = rng.random() * 0.9
    taper = 1.0 if u < 0.3 else max(0.0, min(1.0, 2 * (0.9 - u) - 0.2))
    x = dt * taper
    return int(x) + (1 if rng.random() < x - int(x) else 0)

def child_yield(parentT, childR, tries, N=40000, sheet=True, dome=False):
    tot = 0
    for _ in range(N):
        # pick a parent block: size-biased by column thickness
        while True:
            th = local_thickness(parentT)
            if th > 0 and rng.random() < th / 12.0:
                break
        r = int(nat(*childR))
        if r <= 0:
            continue
        r += 1
        k = int(nat(*tries)) if isinstance(tries, tuple) else tries
        nt = nat(*tries) if isinstance(tries, tuple) else tries
        ylevel = rng.randrange(th)  # position of child centre within sheet (0=top)
        hit = set()
        i = 0
        while i < nt:
            dx = rng.randrange(2 * r + 1) - r
            dy = rng.randrange(2 * r + 1) - r
            dz = rng.randrange(2 * r + 1) - r
            yy = ylevel + dy
            if dome or (0 <= yy < th):
                hit.add((dx, dy, dz))
            i += 1
        tot += len(hit)
    return tot / N

print("quartz->gold/silver", child_yield((1.2, 3, "invexp"), (2, 2, "gaussian"), 20))
print("olivine->peridot", child_yield((1.2, 3, "invexp"), (2, 2, "gaussian"), 20))
print("galena->silver", child_yield((1, 0, "uniform"), (2, 2, "gaussian"), 10))
print("halite dome->sylvite", child_yield((150, 1, "uniform"), (2, 0, "gaussian"), (40, 5, "uniform"), N=4000, dome=True))
print("halite lake->sylvite", child_yield((3.3, 1, "uniform"), (3, 2, "gaussian"), 20))
