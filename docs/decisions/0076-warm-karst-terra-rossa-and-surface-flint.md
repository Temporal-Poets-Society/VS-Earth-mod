# 0076: terra rossa only on warm karst; loose flint in the glaciated north, chert on karst

Status: **built** (EA-0062, EA-0064; the author, 13 November 2026: "Fix EA-0062 and Game balance step 1"). New worlds
only (0002). Not seen in game by the author yet.
Date: 2026-11-13.

## Why

The author found bauxite at about 2,400 m in the Alps near the Bernina (46.41 N 9.89 E), on hillsides of orange bands
beside glacier ice (vanilla ores, ground columns, ore layers v2). In Athens it looked right: terra rossa with bauxite
in it. There were two causes:

1. **Vanilla's bauxite strata.** Regional geology keeps them on purpose (its `preserve` list: bauxite, kimberlite,
   suevite), because Earth's ore maps replace them in a pass of their own (`BauxiteStrata`). Under vanilla ores nothing
   did. Ground columns rewrite bauxite only in their own layers (0060). Below a column's cover, in the regional rock, and
   in glacier columns, which the ground pass skips, vanilla's strata stayed: bands of orange bauxite wherever the cover
   is thin, as on Alpine ridges. A self-test census at the Bernina in a rules 2 world without this fix found bauxite in
   the top 8 blocks of 4,277 of 50,176 columns, with no terra rossa at all.
2. **Terra rossa everywhere on karst.** Ground columns map GUM's karst unit (`Rk`) to `residual-karst`, terra rossa
   over limestone rubble, in any climate, and Earth's layer deposits (`earth-layers.json`) put karst bauxite into that
   terra rossa.

Terra rossa is a warm-climate soil: the red clay left behind on limestone in Mediterranean, subtropical and tropical
climates. Cool or high karst carries rendzina, a dark topsoil on limestone rubble, or bare rock.

Flint was the second gap. The ore layers keep flint only in chalk and limestone beds (0060), and vanilla puts loose flint
only above its own flint deposits, so the glaciated north had almost none. In reality the ice sheets spread the flint of
the chalk (Denmark, Rügen, Scania, the North Sea floor) through the till, outwash, river gravel and beach shingle of
northern Europe, and chert nodules weather out of limestone almost everywhere. Panning any sand or gravel still gives
flint in vanilla (11 %), so no world was locked out; this is about where flint lies, not whether it exists.

## Ground rules 2: warm karst only (EA-0062)

- One rules file serves every version (`ground-rules-v1.json`, now `"version": 2`). A rule added later names its first
  version in a `rules` condition [from, to). The world's saved `EarthParams.GroundColumns` decides, so worlds made with
  rules 1 keep their ground exactly. New worlds save 2.
- New condition `temperature` [from, to): the yearly mean (°C) at the place's own height. That is the climate map's
  sea-level mean less 6.5 °C per km of real elevation (`EarthWorldGen.LapsePerMetre`). It is known only in rules 2 worlds,
  on land, beaches and river beds; an unknown temperature fails the condition.
- New region `karst-cool`, after all other regions: `Rk` cooler than 12 °C gets the new recipe `rendzina` (0.8 m of
  limestone rubble under the topsoil, at most 2 m). Warmer karst keeps `residual-karst` and its bauxite.
- 12 °C keeps terra rossa in Attica, Provence, Istria, Dalmatia, Apulia, Jamaica, Yucatán and southern China, and
  removes it from the Alps, the Dinaric highlands, the Jura and central and northern Europe. Hungarian and Austrian
  karst bauxite is a buried, fossil deposit, not a surface soil, so they lose it too; a district-level bauxite rule is
  left for later (EA-0069).

In ground rules 2 worlds, regional geology's strata pass also treats vanilla's bauxite strata as ordinary rock and
replaces it with the column's own beds (`EarthGeology.strataBauxite`), under either ore choice. Under the ore maps their
own replacement then finds nothing left. Kimberlite and suevite stay preserved. Earth's own bauxite is placed later by
the deposits, so it is not touched. Columns without regional bedrock keep vanilla's strata as before.

## Ore layer rules 3: loose stones on the ground (EA-0064)

`ore-layers-v3.json` is v2 with a new `surface` section. v1 and v2 worlds keep their files and lay nothing. A surface
rule matches a column by its cover recipe (`covers`), its top (`tops`: soil, beach, riverbed, "shore gravel" and the
like) and its uppermost material below the topsoil (`materials`). The first rule that matches applies, once per column,
with its `chance` drawn from the world seed and the column's position. The stones are laid after the layer pass and its
indicator check, so a vanilla flint deposit that was put back does not take them with it. Dry rules need air with no
water on the column's top, water rules need water, and nothing already there is replaced.

| Rule | Where | Block | Chance per column |
| --- | --- | --- | --- |
| flint shingle | a gravel shore on glacial cover | `looseflints-chalk-free` | 3 % |
| river flint | river beds in glacial cover | `looseflints-chalk-water` | 1 % |
| till flint | dry land on till, thin till, knob till, outwash, alluvium or marine sediment on till | `looseflints-chalk-free` | 0.4 % |
| karst chert | dry land on rendzina or terra rossa | `loosestones-chert-free` | 0.4 % |

For scale, vanilla's flint deposits lay about 6 loose flints per chunk (0.6 %) where they occur, in sedimentary rock.
Till flint at 0.4 % gives about 4 per chunk across the whole glaciated plain. A missing block code is logged and its
rule skipped.

## Checks

- `GroundColumnsTests`: eight real karst places (Slovenian Karst, Dalmatia, Velebit, Attica, Apulia, Causses, Yucatán,
  Jamaica) keep terra rossa in rules 1, get rendzina at 8 °C and keep terra rossa at 16 °C or with no temperature in
  rules 2.
- `OreLayersTests`: v1, v2 and v3 load; v3 has v2's deposit rules and four surface rules; till, thin till and outwash
  lay till flint, rendzina and terra rossa lay chert, loess, alluvium and residual cover lay nothing, a glacial river bed
  lays river flint and a glacial gravel shore flint shingle, a non-glacial one nothing; a v2 file with surface rules is
  refused.
- The server self-test logs a ground census per place (terra rossa, bauxite, rubble, loose flint and chert) and the layer
  pass's counts, including the stones each surface rule laid.

## Not done

- No bauxite rule by district (Provence, Bakony, Dinarides, Parnassos): bauxite still follows terra rossa.
- No flint for chalk coasts outside the glaciated area (the English Channel); their shingle is not glacial cover.
- No alternative recipes (EA-0069 step 2; built in 0077).
