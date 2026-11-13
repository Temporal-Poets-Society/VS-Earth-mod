# Draft: AstraTerra pull request "let a map mod say where the meridians are"

Status: **draft, not sent.** Branch `feat/world-longitude-provider` in `C:\VS mods\AstraTerra` (one commit, `ea20a93`,
on top of upstream `main` at `316518a`). It has not been pushed anywhere. You don't have a fork of
`lalmei/astraterra` on GitHub yet.

Upstream activity: the author (lalmei, "Leo") merged PRs #222–#227 on 6–8 October 2026 and opened issues #219/#221 on
6 October. GitHub is where they are active; Discord has been quiet for over 30 days.

Related upstream issue: **#205** "Cache world longitude scale outside frequent solar and observer queries" (open,
by the author). It touches the same `MapWorldLongitude` and asks for the same per-world, weakly held pattern this PR
uses. The PR doesn't fix #205, but the two don't conflict.

---

## PR title

```
✨ api: let a map mod say where the meridians are
```

## PR body

```markdown
Hi Leo, thanks for AstraTerra. The longitude-aware sun is what made real day and night possible for us.

I make **Earth Map**, a Vintage Story worldgen mod that lays the real Earth onto the world (its own meridians,
with a wrap at the Bering Strait when used with Cylinder World). Right now the sun follows AstraTerra's centred
mapping, so on an Earth world the meridians the sky uses don't match the map. For example, x = 128 000 is
14.15° E on the map but 0° for the sun. Earth already knows every X's longitude, so this PR adds a small hook
that lets a map mod give it to AstraTerra.

### What it adds

- `AstraTerraModSystem.SetLongitudeProvider(Func<double, double?>? provider)`: degrees east for a world X,
  or null for "no opinion". Pass `null` to clear it.
- `Astronomy/WorldLongitude`: holds the provider **per world** (a `ConditionalWeakTable`). An integrated server
  and its client keep separate maps, and a world that is gone takes its provider with it.
- `LatitudeMapper.MapWorldLongitude(x, world)` asks the provider first. Everything that reads longitude goes
  through that one method, so the longitude-aware sun and its daylight, the local moon, the star field,
  near-body light and the displayed clock all move together.

### What it doesn't change

- `LongitudeAwareSun` still decides whether longitude is used at all. The provider only says where the
  meridians are.
- With no provider registered, the behaviour is exactly as before.
- AstraTerra falls back to the centred mapping for an X when the provider answers null, a non-finite number or
  throws. Answers are normalised to -180..180, so a broken provider can't break the sun.

### Tests and docs

- `WorldLongitudeTests`: the provider wins, normalisation, every fallback, clearing, per-world isolation, and
  `ObserverLongitude` still gated on the sun. The full `make test` suite passes (1,209 tests).
- `docs/dev/extending.md` gets a new section, "Longitude from your own map". `latitude-and-longitude.md` gets a
  one-line pointer to it.

### How Earth Map calls it

Earth calls the method **through reflection only** (Earth is GPL-3.0 and never references the AstraTerra assembly).
It calls it on both sides once that side has its map layout, and passes `null` on dispose. Older AstraTerra
versions without the method are detected and skipped. So there's no hard dependency either way, and nothing for
AstraTerra to maintain beyond this one method.

### Relation to #205

This touches `MapWorldLongitude` too, but it doesn't cache the scale. When a provider answers, the
`GetPolarEquatorDistance` lookup is skipped for that call. A scale cache for #205 can still sit behind the
fallback path unchanged.

Happy to rename anything, move the method somewhere you prefer, or split the docs out. If you'd rather discuss
the API in an issue first, I can open one instead.
```

---

## Shorter message (if you'd rather ask before opening the PR)

To post as a GitHub issue ("✨ API for a map mod to supply world longitude") or on Discord:

```
Hi Leo, I make Earth Map, a worldgen mod that lays the real Earth onto the world. I'd like AstraTerra's sun to
use Earth's real meridians instead of the centred mapping. I have a small, tested change ready: a
SetLongitudeProvider(Func<double, double?>) on AstraTerraModSystem, held per world. MapWorldLongitude asks it
first, and AstraTerra falls back to its own mapping when the answer is null, non-finite or throws.
LongitudeAwareSun still controls whether longitude is used. Would you take a PR for that?
```

---

## Steps to open it (only when you decide; nothing here has been run)

```bash
gh repo fork lalmei/astraterra --remote --remote-name origin --clone=false
```

```bash
git -C "C:/VS mods/AstraTerra" push -u origin feat/world-longitude-provider
```

```bash
gh pr create -R lalmei/astraterra --base main --head andrisberzins-1:feat/world-longitude-provider --title "✨ api: let a map mod say where the meridians are" --body-file pr-body.md
```

Before pushing: the commit message ends with a `Co-Authored-By: Claude` line. Keep it or amend it as you prefer.
