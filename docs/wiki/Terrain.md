# Terrain

The land, the sea floor, coasts, rivers, lakes, ice and volcanoes. How water behaves once it's there is on
[Water](Water); rocks and soil are on [Rocks, soil and ores](Rocks-soil-and-ores).

## Heights and mountains

**Vanilla:** the game makes its landscape from noise: rolling hills, mountains and oceans wherever the noise puts them.

**Earth:** every column comes from real elevation data. Dry land takes its height from Copernicus GLO-90, a satellite
survey of the whole Earth at about 90 m. The sea floor and the ice sheets come from ETOPO. On top, a little detail noise
gives slopes their texture; it grows with how rugged the real land is, so plains stay plains and the Alps get crags.
Listed mountain peaks keep their surveyed heights.

What it means for you:
- Every mountain range, valley, plateau and plain is where it is on Earth, at its real shape. You can navigate by a
  real atlas or by Google Maps.
- How tall mountains look depends on the scale (see [The world](The-world)).
- The land heights are a download of their own (8.6 GB, all of it by default). See
  [Data and performance](Data-and-performance).

## Coasts and islands

**Vanilla:** coasts are wherever the noise meets sea level.

**Earth:** at 1:100 and finer, the coasts of the continents follow the real coastline: headlands, coves, fjords, spits
and estuaries at their true size, cliffs on rugged coasts and beaches on flat ones. Islands come from their real
outlines and appear when they are at least 4 blocks wide at the world's scale, so the finer the scale, the more small
islands there are. At 1:200, coasts are smoother.

**Beaches:** vanilla puts a beach where the land meets the sea at sea level. Earth makes the shore of the sea and of big
lakes a beach of the real material: sand, gravel, or rock where the real coast is rocky, taken from OpenStreetMap where
it maps the beach, and a strip as wide as real beaches are (about 40 m of sand, 20 m of gravel, less on rock). Cold coasts
get gravel.

## Rivers

**Vanilla:** no rivers worth the name: a few streams and lakes in hollows.

**Earth:** the real rivers, along their real courses. The big ones (the Rhine, the Danube, the Volga, the Nile, the
Amazon) are wide and deep enough for boats; smaller rivers are narrower; at 1:25 and finer the small streams are there
too. A river's water steps down its valley as a staircase of pools, each a flat stretch of still water, with a drop
between them where the valley falls. Rivers run into their lakes and seas at the right level, and tributaries meet their
main river at its level.

- **Rapids and waterfalls.** Where a river falls steeply for its size, its drops are rapids: white water at each step.
  Calm rivers have calm water. The great waterfalls (Victoria, Niagara, Iguazu, Kaieteur, the Rhine Falls,
  Dettifoss, Gullfoss and others) drop by their real heights. See [Water](Water) for how they behave and
  the **River rapids** setting.
- **River mouths.** A river meets the sea in one bed: its channel eases into the sea floor, so boats can sail in.
- **No modern dams.** Reservoirs behind modern dams (Kariba on the Zambezi, the dams of the Volga and the Daugava and
  many more) aren't there: the river runs through its natural valley instead. Natural lakes that a dam only regulates
  stay.
- **Rivers in deserts.** A river fed from wetter land keeps its water across the desert, with trees along it (the Nile,
  the Euphrates, the Indus). Desert rivers that only run after rain are dry beds of sand and gravel that end in a fan of
  sediment.

## Lakes

**Vanilla:** small lakes wherever the noise leaves a hollow.

**Earth:** the real lakes at their real levels, from Superior and Baikal to Titicaca high in the Andes and the Caspian
below sea level. Salt lakes are salt water.

Lakes that irrigation dried up in the 20th century are back at their level before 1960: the Aral Sea, Lake Chad, Lop
Nur, Owens Lake and others, 18 in all, with green shores around the Aral and Lake Chad. Salt pans that fill in wet years
(Kati Thanda–Lake Eyre, Etosha, Makgadikgadi) are filled too.

## As around 1318

Earth's world is the Earth of around 1318, before most of the great human works:
- **The Netherlands** have the Zuiderzee back; the Dutch polders are sea, as they were before the dikes.
- **Canals** dug after 1318 are land again: the Kiel Canal, the North Sea Canal, the Karakum Canal, the Grand Canal
  d'Alsace, the Corinth Canal, the White Sea–Baltic Canal and others. Canals older than 1318 stay.
- **Suez and Panama** are land, and the Bitter Lakes are dry, unless you turn on **Map · Suez and Panama canals** on the
  Earth tab: then wide cuts at sea level run from the Mediterranean to the Red Sea and from the Caribbean to the Pacific,
  with lakes for the Bitter Lakes, Gatun and Miraflores and no locks.
- **Reservoirs** are gone, as above.

Natural change after 1318 (storm floods, rivers that changed course) stays as today's maps have it. Two known gaps: the
Aral Sea stands at its 1960 level, not its lower level of 1318, and lakes drained after 1318 outside the Netherlands
(the Fucino, the lakes of the Valley of Mexico) are still land. In the Netherlands the drained lakes such as the
Haarlemmermeer are back.

## Ice sheets

**Vanilla:** no ice sheets; glaciers are snow on high ground.

**Earth:** Greenland and Antarctica carry their real ice sheets: glacier ice from the rock up to the real ice surface.
The ice can be mined like vanilla's glacier ice, and there is rock below it. Mountains that stand above the ice show as
bare rock.

## Volcanoes and hot springs

**Vanilla:** lava and hot springs come from a noise map of "geologic activity": lava streams out of mountain sides and
pools under caves wherever the noise is high, and hot springs anywhere.

**Earth:**
- **Hot springs** are only where real ones are: about 5,500 known hot springs and geysers from OpenStreetMap and GeoNames,
  each placed by the game's hot spring generator where the ground allows.
- **Volcanoes** stand where the real ones are. Each of about 825 volcanic mountains has a body inside it: a magma chamber
  of lava deep under the summit and a conduit up toward the vent. The 332 that were active in the game's time
  (about 1420 to 1520) have a crater at the top with lava under a thin crust, and fresh volcanic ash
  (tephra) on the ground around them. Eight have **lava lakes** in their craters, as in the historical record: Erta Ale,
  Nyiragongo, Kīlauea, Erebus, Masaya, Villarrica, Ambrym and Nyamulagira. Dormant volcanoes are solid inside, with no crater.
- **Lava** deep underground lies under the real volcanoes, not in random caves, and no lava springs out of mountain sides.

Be careful at active craters: the crust over the lava is thin.
