# 0105: Earth soil (soil rules 4) over vanilla's block layers, in two tier sets

Status: **in Earth 2.2.0** (21 November 2026), built on `claude/soil-rules-4` (EA-0199, from `claude/terrain-2.2` at 2.2.0-rc.11) for
2.2.0-rc.12; the author looked at it in game in rc.12 and rc.14. Date: 2026-11-20. Asked by the author after rc.11 ("Ok now with vanilla ground, almost all
the world gets low fertility soil, very few places gets medium. Why?", then "ok can we then topsoil have real soil with
two options and below vanilla rocks? While regional rocks are built?"). The class table and the two tier sets were
agreed with the author on 19 November 2026 (ground columns 3 proposal, section 4.1). Climate only was the default until the
author's look (the author's 19 November decision); from 2.2.0-rc.13 the default is realistic tiers (the author, 21 November
2026: "Earth soil, realistic tiers - should be default"), on `claude/terrain-2.2`. How it is built is Claude's, as
maintainer (D72).

## Context

In worlds whose soil vanilla's block layers lay (vanilla rock, the default, and regional rock without ground columns),
the fertility comes from one formula, `Climate.GetFertilityFromUnscaledTemp`. That is about half the rain, plus rain
times warmth. Vanilla lays medium topsoil from 0.5 and low from 0.25, and never lays high. On Earth's real rainfall,
Europe's moderate rain and cool summers fall between the two, so most land gets low soil. The warm, wet tropics reach
medium, although their real soils are among the poorest. Earth's rule 0048 only lifts land that isn't arid to low.
Ground columns lay soil from the real soil map, but they are off the screen in 2.2 ([0104](0104-ground-columns-and-ore-maps-off-the-screen.md)).

## Decision

1. **"Soil" on the Earth tab** (`earthSoil`, Terrain · Soil). The rocks underneath don't change.
   - "Climate only" (`climate`): today's rule (the default up to 2.2.0-rc.12).
   - "Earth soil, vanilla tiers" (`vanilla`).
   - "Earth soil, realistic tiers" (`realistic`): the default from 2.2.0-rc.13, in the attribute and every Earth play
     style. A world configuration without the key, or with another value, gets it too: a world from the menu always has
     the key, so a missing one means a hand-typed or older server configuration, and it gets what the menu gives.

   A ground-column world keeps its own soil whatever the choice. Saved per world:
   - `EarthParams.Topsoil` (221: 0 climate only, 1 vanilla tiers, 2 realistic tiers);
   - `SoilRules` 4 with Earth soil. Climate only keeps soil rules 3, so its new worlds' params are as before.
2. **One decision per column** (`Core.Topsoil`, rules in `assets/earth/config/soil-rules-v4.json`). Inputs:
   - the WRB soil class from SoilGrids 2.0: the 30″ set of release egts-v1, the 0.01° Baltic map, else the 2′ world map
     of Earth's data store, with the tiles' coherent warp at class edges;
   - the climate;
   - the floodplain grid, at the column's real height;
   - GUM's 0.5° wind-blown sand, for dunes.

   The rules, in order:
   - colder than vanilla's grassed topsoil (−10 °C), no class mapped, or the shore strip's beach: vanilla's rule (soil
     rules 3);
   - arid land (the real aridity index on the ambient rain and on the river's green belt, as ground columns' soil rules
     2): no topsoil, except in a river's natural flood, where the flood's level against each column's ground draws the
     edge. rc.12 also kept topsoil at "oases" (fresh wetland of at least 25 % of GLWD's 0.1° cell). That drew
     dead-straight edges across the Nile's desert plateau and acted nowhere but beside the Nile and in Mesopotamia, so
     it was removed after the author's look (21 November 2026);
   - otherwise the class's tier, below;
   - barren on polar and alpine ground beyond the tree line (warmest month below 10 °C at the column's height), in both
     sets, whatever the class. SoilGrids calls much of it Cambisols or Leptosols.
   - In a flood, river silt is at least medium: Fluvisols high, salt and gypsum soils low.

   | Tier | Vanilla tiers | Realistic tiers |
   |---|---|---|
   | Black earth (`soil-high`, "Terra preta", 80) | — | Chernozems |
   | High (`soil-compost`, 65) | Chernozems, Phaeozems, Fluvisols in a flood | Phaeozems, Andosols, Nitisols, Fluvisols in a flood |
   | Medium (50) | Andosols, Nitisols, Cambisols, Luvisols, Kastanozems, Vertisols, Umbrisols, Fluvisols | Cambisols, Luvisols, Kastanozems, Vertisols, Umbrisols, Fluvisols |
   | Low (25) | Acrisols, Cryosols, Regosols, Calcisols, Albeluvisols, Gleysols, Stagnosols, Lixisols, Alisols, Planosols, Solonetz, Ferralsols, Podzols, Arenosols, Leptosols, Plinthosols, Durisols, Histosols | Acrisols, Cryosols, Regosols, Calcisols, Albeluvisols, Gleysols, Stagnosols, Lixisols, Alisols, Planosols, Solonetz, Podzols |
   | Barren (`soil-verylow`, 5) | Gypsisols, Solonchaks; Arenosols on dune sand; polar and alpine ground | Ferralsols, Arenosols, Leptosols, Plinthosols, Durisols, Histosols, Gypsisols, Solonchaks; polar and alpine ground |

   Within a class every place gets the class's tier. The measured-properties index (SoilGrids' organic carbon, cation
   exchange, pH, nitrogen and texture) comes after 2.2. It would tell Zemgale's clay loams from Latgale's sandy till,
   and boreal Cambisols (Canada's Brunisols) from rich ones.
3. **How vanilla lays it** (`BlockLayerAdjustments`, bound by `PatchVanilla` as feature `Topsoil`):
   - **Fertility:** the fertility hook of 0048 gives vanilla's layers the tier's fertility (`layerFertility`). Vanilla
     still picks the grass, the subsoil, the depth and the tall grass.
     - Barren and low are laid as low over low subsoil.
     - Medium, high and black earth are laid as medium over medium subsoil.
     - Arid land without topsoil gets at most 50/255, below vanilla's soil layer, so it is sand or gravel.
   - **Rain in floods:** vanilla lays no soil below a rain of 0.08, nor grassed soil below 0.12. A prefix on
     `LoadBlockLayers` gives a flooded arid column a rain of 0.30 there, and nowhere else (not the climate map, the
     grass or the plants).
   - **Top blocks:** a prefix and postfix on vanilla's chunk method swap the top soil block after vanilla's layers,
     keeping its grass: barren becomes `soil-verylow`, high `soil-compost`, black earth `soil-high`. The bare block under
     it gets the same grade. A top block of another kind or grade is left as it is.
   - **Forest floor** (the author, 20 November 2026: "Yes, soil beneath"): vanilla's forest floor replaces the top soil
     block under trees and always drops low soil. In an Earth-soil world it drops the bare soil of the block it lies
     on, which now has the column's grade. Where no soil lies beneath, it gives the closest sensible block (the
     coordinator for the author): low soil on clay; on peat the grade the tier set gives peat soils (low in vanilla tiers,
     barren in realistic tiers, as Histosols; the author asked why barren); barren soil on rock, sand or gravel; and
     vanilla's drop over air or water. Breaking peat gives peat bricks, so no peat block comes from here. It looks past a loose stone or another
     forest floor, up to three blocks down. A block behaviour, `EarthForestFloorSoil`, is added to vanilla's
     `forestfloor` by `assets/earth/patches/forestfloor-soil.json`. Climate-only worlds and worlds made before keep
     vanilla's drop, and the forest floor's own fertility for plants is untouched.
4. **The 30″ map:** a new world with Earth soil decides once, before its first chunk, whether it reads the 30″ map
   (`EarthParams.GroundData`, as ground columns do). Earth fetches the soil-group set of release egts-v1 (21 MB, already
   published) from `GroundRepo`.
   - If the source answers that it doesn't have the set, the world uses the 2′ map for good.
   - If the download fails for any other reason, the start stops and nothing is saved.

   The 2′ maps, the dune unit and the floodplains are files of the data store (ed-v1). No new data and no upload.
5. **No parallel system.** Ground columns keep their soils table, and soil rules 1–3 stay for the worlds that saved them.
   Nothing is added to either. `soil-site.bin` stays the plants' view of the same classes. The ground columns 3 plan
   still has ground-column worlds call the same decision later.

## Checks

- **SoilTests**: the class table in both sets, deserts, floods, oases, the tree line, the cold limit, and the layers'
  fertility against vanilla's `blocklayers.json` thresholds.
- **Census mode** (`SoilTests <root> census <ModData/earth>`), offline, on the 30″ map:
  - Poltava steppe 94 % black earth (realistic) or high (vanilla tiers).
  - Zemgale plain 68 % medium; central Europe 93 % medium.
  - Finland's podzols (81 % of it) low in both tier sets (barren in realistic tiers until 2.2.0-rc.14).
  - The Amazon low or barren; the Sahara 100 % none.
  - The Nile at Luxor and Baghdad fertile across the flood and the irrigated (oasis) ground, bare beyond it.
  - Iceland's interior and the high Alps barren.
  - Shares of land with topsoil:
    - vanilla tiers: barren 6, low 49, medium 40, high 5 % (section 4.1's estimate 10, 48, 37, 5);
    - realistic: barren 29, low 26, medium 38, high 4, black earth 3 % (30, 27, 35, 5, 3).
- **EarthWorldOptionsTests**: the choice per world kind and field 221 pinned. The 320 earlier new-world cases are
  unchanged, plus 10 soil cases.
- **SavedWorldDigestTests**: the 12 saved worlds unchanged.
- **PatchVanillaTests**: the forest floor's grade for each block beneath, the patch, and vanilla's forest floor as the
  patch expects it.
- **Forest floor in game** (dedicated servers, 1:100 and 1:10, both sets; the game's own drop code). Drops matched the
  block beneath:
  - central Europe (Göttingen, Zemgale): medium on medium soil, low on clay; on peat low (vanilla tiers) or barren
    (realistic tiers);
  - boreal Finland (Jyväskylä): barren (realistic) or low (vanilla tiers);
  - the Amazon: low;
  - over air, about 1 % of forest floors: vanilla's low soil.

## Consequences

- From 2.2.0-rc.13 every new world from the menu has Earth soil, realistic tiers, unless the player picks another. Its new-world
  pins change by `Topsoil` 2 and `SoilRules` 4 alone: with those set back to climate only, all 330 earlier cases are
  2.2.0-rc.12's, and a new "soil climate only" case per Earth size equals rc.12's play-style world.

- A world made with Earth soil keeps it. Climate only is exactly what worlds made before have.
- Under forests the forest floor stays on top, as in vanilla. What lies under it, and what it drops, is the column's
  grade.
- Crests and hollows (thinner and thicker soil) are not part of this: in these worlds the depth is vanilla's.

## Podzols low in realistic tiers (21 November 2026)

The author's rc.14 look found barren soil near Riga ("barren feels bit overused"). It was on Podzols, which realistic tiers
made barren. Podzols are poor and acid, but people have farmed them for centuries with manure (rye and potatoes in the
Baltic, Finland and northern Germany), so realistic tiers now make them low, as vanilla tiers do. Beyond the tree line
they stay barren in both tier sets.

Barren in realistic tiers now means:
- thin soil on rock (Leptosols);
- salt and gypsum soils;
- dune sand and other Arenosols;
- peat (Histosols);
- the tropical laterites (Ferralsols, Plinthosols);
- Durisols;
- ground beyond the tree line.

Census at 0.25°, realistic tiers: barren 29 % → 25 % of land with topsoil, low 26 % → 30 %. Finland goes from barren 82 %
to low 88 %, Karelia from barren 22 % to 2 %. The vanilla tiers and the other regions are unchanged.

A world made with an earlier test build keeps its soil in chunks already made, and lays the new tier in new chunks.
Released worlds are not affected, since soil rules 4 has not been released.
