# Water

How Earth's rivers, springs and ponds behave in play, and how Earth works with Hydrate or Diedrate. Where the rivers and
lakes are is on [Terrain](Terrain).

## Rivers in play

**Vanilla:** water spreads from every source block and flows downhill. A long river built from vanilla water floods its
banks, drains away where it is opened, and every source block you take out leaves a hole.

**Earth (with Hydrology):** Earth tells Hydrology where its rivers are, how high each stretch stands and which way it
flows. Hydrology, which Earth needs, then keeps the water where it belongs:
- **Calm rivers flow.** A calm river's surface is gently flowing water: it carries the game's gentle current, and it
  sounds like a creek. Placing or breaking a block in it doesn't leave patches of still water around it.
- **Steps and rapids.** A river steps down its valley as a staircase of flat pools. Where it falls steeply for its size,
  the drops between the pools are white water (rapid water), which pushes boats harder and is loud. Between the drops,
  and on every other river, the current is gentle.
- **Water stays put.** Water stands where it is held. Dig a channel two blocks wide from a river or a lake and it fills (a one-wide channel carries
  flowing water only a few blocks); open a pond's
  bank and it drains downhill over time. Build a dam across a river and the pool behind it fills up to the crest.
- Water wheels on rapids turn with the river's real fall and flow.

How all of this works, and the **Water flooding** option on the Earth tab, are explained on Hydrology's own pages.
Hydrology strongly recommends the game's own setting that stops buckets from carrying source blocks
(`noLiquidSourceTransport`), so water can't be moved around for free.

### River rapids

**Terrain · River rapids** on the Earth tab:
- **At the drops of steep rivers** (default): white water at the drops of rivers whose real fall is strong for their size,
  such as the Congo's Livingstone rapids, the Three Gorges and mountain rivers.
- **Off:** no white water on river steps. Waterfalls stay.

## Springs and mountain streams

**Vanilla:** scatters springs in stone faces and digs short streams out of mountain sides. On Earth's real heights each
of those streams comes out as rapid water at a random spot, not where water would drain.

**Earth:** **Terrain · Mountain springs and streams** on the Earth tab:
- **Cave springs only** (default): vanilla's springs in cave walls below the surface; none in open cliffs and no
  streams out of mountain sides.
- **Real stream heads:** as cave springs only, plus a spring where each real stream that runs all year starts, and
  small springs where the ground water meets a slope in wet climates. None in deserts.
- **Vanilla:** all of them, as the game makes them.

Earth's rivers, lakes and waterfalls are the same with every choice.

## Ponds

**Vanilla:** digs small ponds into the surface wherever it finds a hollow: round pools on hillsides and fields, and
silt rings in river beds.

**Earth:** **Terrain · Surface ponds** on the Earth tab:
- **Off** (default): no surface ponds. Earth's own lakes, rivers, wetlands and the sea are there as always.
- **Wetlands only:** ponds only in the mapped bogs, marshes and swamps, as often as their share of the ground.
- **Vanilla:** wherever the game makes them.

Ponds in caves are the game's in every case.

## Hot springs and lava

Hot springs come only where real ones are, and lava lies only in and under real volcanoes, never springing out of a
mountain side. See [Terrain](Terrain#volcanoes-and-hot-springs).

## Hydrate or Diedrate

Hydrate or Diedrate (HoD) gives the ground aquifers: water you find when you dig a well, more in some places than
others, and salty in some.

- **With Rocks and ground set to Vanilla** (the default) or **Biomes set to Vanilla**, HoD works exactly as it does
  without Earth, with its own aquifers.
- **With Earth's own rocks and ground** (Regional, or ground columns in a world that has them, and Earth biomes), Earth
  gives HoD its aquifers from a real water table: the ground water lies close under the surface near rivers, lakes and
  wetlands, and deeper under hills and in dry land. How much water a place holds comes from the real ground there:
  sand, gravel, karst and
  fractured rock hold a lot, clay and unbroken granite little. Water is salty under salt flats and salt lakes, under the sea floor and near
  the coast below the fresh water, and everywhere deeper than about 500 m.
- This needs a HoD that uses aquifer data version 2 (2.5.8 does); with another, HoD makes its own aquifers and the log
  says so.
- Only ground made while HoD is installed gets Earth's aquifers. Add HoD to an existing world and only new land has them.

HoD's wells have their own rules. In HoD 2.5.8 only a well dug with a pickaxe in rock reads Earth's aquifers. A well dug
with a shovel in soil, sand or gravel is a shallow well: it ignores the aquifer and needs surface water within 3 blocks,
so the water held in sand and gravel doesn't reach it. Some of Earth's ground blocks, such as till, silt, marl and
loess, don't take a well in HoD yet.
