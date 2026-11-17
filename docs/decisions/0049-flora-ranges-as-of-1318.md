# 0049: Plants grow where they grew around 1318

Date: 3 November 2026. Status: **Agreed**; built, in Earth 2.0.0: fruit trees, wild crops, berry bushes, vanilla flowers and cacti through the placement catalogue of [0047](0047-placement-catalogue-and-audit.md#implementation-first-slice). Other ground plants are not covered.

## Report

A Mod DB comment (UncleTony, 3 November) found no olive trees around Athens and struggled to find any fruit trees.
Earth 1.0.2 lets vanilla choose the fruit-tree type with thresholds made for vanilla's climate scale. Olive needs a
yearly mean of at least 22 °C, so it grows in African, Australian and Indian savannas and nowhere in Greece. No
fruit type at all passes in lowland Attica (17.1 °C, rain 0.25). Measurements:
[fruit-tree research](../research/fruit-trees-2026-11-03/README.md).

## Decision

The author, 3 November: **"we should stick to 13th-14th century ranges. No potatoes in Europe or corn or tomatoes"**.

Natural world generation places each plant (trees, shrubs, ground plants, berry bushes, fruit trees and wild crops)
only within its range as it stood around **1318**. That range is the native range plus the introductions people had
established by then: orchards, field crops and naturalised archaeophytes. Later introductions are left out, above
all the Columbian exchange in both directions. The date follows the suite's cutoff for ruins and lore (suite
decision D21, [overview](../../../overview/DECISIONS.md)). Climate still applies on top, measured as Earth's real
climate and not on vanilla's scale.

What this means, to be confirmed taxon by taxon with sources in the catalogue (0047):

- **Crops from the Americas stay in the Americas:** maize, potato, tomato, common bean, squash and pumpkin, sunflower,
  peanut, cassava, pineapple, cacao and chili, plus any spread they had before Columbus within the Americas.
  Vanilla's wild cassava, pineapple, peanut and sunflower, and Floral Zones' maize, sweet potato and bean (if it is
  *Phaseolus*), fall under this. Sweet potato also reached Polynesia before Columbus; that needs a dated source.
  Grain amaranth is American, while leaf amaranths are also Old World. Which one vanilla's amaranth is must be decided.
- **Old World crops stay out of the Americas:** spelt and wheat, rye, flax, rice, soybean, turnip, carrot, onion,
  parsnip, fennel, licorice, olive, citrus, apple, pear, peach, pomegranate and grape.
- **Fruit trees follow their 1318 orchards:**
  - **Olive** across the Mediterranean basin.
  - **Pomegranate** in the Mediterranean and southwest Asia.
  - **Apple, pear, peach and cherry** in the European, west Asian and Chinese regions where they were grown by then.
  - **Orange** needs a decision about what the vanilla tree stands for. The sweet orange reached the Mediterranean
    only after 1318, while bitter orange, lemon and citron were already in the Arab Mediterranean.
  - **Mango**, **lychee** and **breadfruit** follow their Asian and Pacific cultivation, including Polynesian
    voyaging for breadfruit. The edges of each range need sources.
- **Wild crops are no longer climate-only.** This replaces the rule in the regional ground-plants implementation that
  wild crops stay climate-only "so their seeds stay obtainable everywhere". A European start finds no wild maize or
  cassava. Getting them means travel or trade, which is the intended consequence.

## Scope

- Natural world generation of plants only. Players can still plant anything they carry, anywhere the game allows.
- Trader stock, loot, story-structure contents and recipes are not covered. Whether traders in Europe should sell
  maize seed is an open question.
- Animals are not decided here. Regional fauna's default (natural range without human impact, extinct animals on;
  [regional fauna](../regional-fauna-implementation.md)) predates this record. Vanilla animals are not controlled
  at all, so wild boar, chickens, sheep and goats can appear in the Americas. Whether the 1318 rule extends to
  animals is open. *(Correction, 3 November 2026: wrong. Since [0031](0031-mountain-height-playtest.md) vanilla land
  animals follow PHYLACINE present-natural ranges, `fauna-vanilla.json`, in worlds with real animal ranges. What is
  missing is a 1318 range for animals people had spread by then, which present-natural ranges do not give.)*
- Existing worlds: following [0002](0002-save-compatibility.md), the profile is saved with the world, and chunks
  already generated never change. Whether older Earth worlds adopt it for new chunks is decided at implementation.

## History

- 3 November 2026: agreed by the author in the session that investigated the olive report.
- 3 November 2026: the author extends the rule to animals ("yes world as till 14th century"), with a player setting
  back to vanilla placement by climate alone. Both are recorded in [0047](0047-placement-catalogue-and-audit.md); the
  Scope's statement that animals are undecided is superseded by that record.
- 3 November 2026: first slice implemented (0047's Implementation section). Correction added to Scope: vanilla land
  animals already have natural ranges since 0031.
- 3 November 2026: renumbered from 0046 to 0049 when merged with calm rivers, which had taken 0046 in an installed
  playtest build and in Hydrology.
- 17 November 2026 (EA-0156, Earth 2.1.4): the eight Floral Zones packs reviewed at their newest 1.22 releases
  (Cape 1.0.39, Caribbean 1.0.31, Central Australian 1.0.29, Circumboreal 1.0.5, Cosmopolitan 1.0.15, East Asiatic
  1.0.26, Mediterranean 1.0.26, Neozeylandic 1.0.27). No worldgen file changed: the same 275 block patches and tree
  generators, so no new plant to map; only the reviewed versions were raised
  ([fauna-packs-2026-11-17](../research/fauna-packs-2026-11-17/README.md)).

## Status before 15 November 2026

The status line as it stood before the 2.0.0 check of the decisions' status (EA-0092 D1):

Status: **Agreed**; first slice **Implemented** 3 November 2026 (fruit trees, vanilla wild crops
and berry bushes, Floral Zones wild crops) through the placement catalogue of
[0047](0047-placement-catalogue-and-audit.md#implementation-first-slice). Other ground plants are not covered yet.
