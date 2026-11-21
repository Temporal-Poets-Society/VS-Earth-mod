# Terrain

The land, the sea floor, coasts, rivers, lakes, ice and volcanoes. How water behaves once it's there is on
[Water](Water); rocks and soil are on [Rocks, soil and ores](Rocks-soil-and-ores).

Some of this needs a fine enough scale: real coastlines, one-bed river mouths and the coasts and waters "as around 1318"
need **1:100 or finer**, the streams down to the smallest brooks **1:25 or finer**. See [The world](The-world) for the
scales.

## Heights and mountains

**Vanilla:** the game makes its landscape from noise: rolling hills, mountains and oceans wherever the noise puts them.

**Earth:** every column comes from real elevation data. Dry land takes its height from Copernicus GLO-90, a satellite
survey of the whole Earth at about 90 m. Between GLO-90's points the land is a smooth surface, and at 1:100 and 1:200
each block is the mean of the points under it. The sea floor and the ice sheets come from ETOPO; detail noise shapes only
the sea floor, and land whose GLO-90 square can't be had. Listed summits of 3,000 m and more are lifted to their surveyed
heights where the data rounds them off (a server can change that threshold before the world is made:
`earthSummitCorrection` on [Settings](Settings#hidden-world-options)).

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
lakes a strip as wide as real beaches are (about 40 m of sand, 20 m of gravel, less on rock, by OpenStreetMap where it
maps the beach), at whatever height the shore stands. With vanilla or regional rocks it is sand, and gravel on cold
coasts. In a world with ground columns (experimental, see [Rocks, soil and ores](Rocks-soil-and-ores#ground-columns))
the strip is the real material: sand, gravel, mud or bare rock.

## Rivers

**Vanilla:** no rivers worth the name: a few streams and lakes in hollows.

**Earth:** the real rivers, along their real courses, on the same grid as the land heights, so their banks sit on the
land they belong to. The big ones (the Rhine, the Danube, the Volga, the Nile, the Amazon) are wide and deep enough for
boats; smaller rivers are narrower. A river's water steps down its valley as a staircase of pools, each a level stretch
of gently flowing water, with a drop between them where the valley falls. Only dead-end arms and backwaters are still.
Rivers run into their lakes and seas at the right level, and tributaries meet their main river at its level.

- **How wide.** **Terrain · River width** on the Earth tab sets it, and each play style sets that for its scale. At
  1:10 a river is at least as wide as boats need (a river of 100 m³/s 12 blocks), and as wide as the real river where
  that is wider; at 1:100 a river of 100 m³/s is 6 blocks and the Daugava at Riga 11. No river is narrower than its real
  width or one block.
- **Streams at 1:25 and finer.** Every stream from 1 m³/s, the smallest brooks, up to the rivers: one block wide, in its
  real valley, stepping down in single steps and small falls with a plunge pool, and joining the rivers, the lakes and
  the sea. A stream leaving a lake starts at the lake's level, two streams join where they first meet, and very steep
  ones come down their cliffs in falls. Streams that dry up in summer are left out, frozen northern ones kept, and their
  banks keep the land's own grass or soil. **Terrain · Smallest stream** can leave out the smaller headwaters.
- **Rivers by scale at 1:50 and coarser.** A river is drawn from 100 m³/s, a smaller one down to **Terrain · Smallest
  stream** as a one-block stream (from 40 m³/s in the 1:50 play style; the 1:100 and 1:200 play styles draw rivers
  only), and nothing smaller. A river starts from a one-block stream at its head instead of out of nothing at full
  width, and a river that loses its water to dry land (the Darling below Wilcannia) stays a river to its end.
- **Rapids and waterfalls.** Where a river falls steeply for its size, its drops are rapids: white water at each step.
  Calm rivers have calm water. The great waterfalls (Victoria, Niagara, Iguazu, Kaieteur, the Rhine Falls,
  Dettifoss, Gullfoss and others) drop by their real heights at the world's height scale. See [Water](Water) for how
  they behave and the **River rapids** setting.
- **River mouths.** At 1:100 and finer, a river meets the sea in one bed: its channel eases into the sea floor, so boats
  can sail in. At 1:200 rivers reach the sea through a dredged channel. A river ends where it reaches the sea: on a
  fjord coast its course doesn't run on over the headlands.
- **Branches.** At Old River the Atchafalaya takes 30 % of the Mississippi's and the Red River's water (about
  6,500 m³/s) to Atchafalaya Bay.
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

Each lake has its real mean depth, its bed falling steeply from the shore to a broad floor: Lake Geneva averages 162 m,
15 blocks at 1:10, Constance 96 m, Ladoga 54 m, Peipus 8 m. Baikal and the Great Lakes keep their measured beds. A river
that flows through a lake doesn't cut a channel into its bed. The game's own water plants grow only in shallow water,
so in a deep lake they keep to the shelf near the shore.

Lakes that irrigation dried up in the 20th century are back, 11 in all, with green shores around the Aral and Lake
Chad: Lake Chad, Lop Nur, Owens Lake and others at their level before they dried, and the Aral Sea at 29 m, its low of
the 14th century, with its western basin full and a lagoon in the eastern basin that the Amu Darya runs into. Seven
salt lakes and pans that fill in wet years (among them Kati Thanda–Lake Eyre, Etosha and Makgadikgadi) are filled too.

## As around 1318

At 1:100 and finer, Earth's coasts and waters are those of around 1318, before most of the great human works. (An
Earth · 1:200 world has today's Netherlands, canals and drained lakes.)
- **The Netherlands:** land won from the sea is sea again: the Zuiderzee with its polders, the Wieringermeer, the arms
  of the delta. Drained lakes such as the Haarlemmermeer and the Beemster are lakes again. The peat polders below sea
  level stay dry land.
- **Canals** dug after 1318 are land again: the Kiel Canal, the North Sea Canal, the Karakum Canal, the Grand Canal
  d'Alsace, the Corinth Canal, the White Sea–Baltic Canal and others. Canals older than 1318 stay.
- **Suez and Panama** are land, and the Bitter Lakes are dry, unless you turn on **Map · Suez and Panama canals** on the
  Earth tab: then wide cuts at sea level run from the Mediterranean to the Red Sea and from the Caribbean to the Pacific,
  with lakes for the Bitter Lakes, Gatun and Miraflores and no locks.
- **Reservoirs** are gone, as above.
- **Drained lakes** are lakes again: Lake Texcoco and the Valley of Mexico's other lakes at their old levels
  (Tenochtitlan's island is lake), Fucino, Copais, Karla, Amik, Hachirōgata, Antela, Hula and Ogura-ike, and the Fens'
  Whittlesey and Stretham meres.
- **The Salton Sea**, filled in 1905, is dry land.

Natural change after 1318 (storm floods, rivers that changed course) stays as today's maps have it. Known gaps, among
others: land won from the sea outside the Netherlands is still land; some drained lakes (Tule Lake, Martin Mere, the
Fens' other meres) aren't back yet; the North Aral stands at today's level and Tulare Lake at its level of the 1870s;
rivers that people moved (the Aare into Lake Biel) keep today's courses.

## Ice sheets

**Vanilla:** a layer of glacier ice wherever the climate is cold enough; no ice sheets with a real shape or thickness.

**Earth:** Greenland and Antarctica carry their real ice sheets: glacier ice from the rock up to the real ice surface,
with rock below it. Mountains that stand above the ice show as bare rock. In Greenland the ice can be mined like vanilla's
glacier ice; south of 60° S Cylinder World turns it into its own harder ice, which drops nothing.

## Volcanoes and hot springs

**Vanilla:** lava and hot springs come from a noise map of "geologic activity": lava streams out of mountain sides and
pools under caves wherever the noise is high, and a hot spring forms over a wide cave where the noise is high.

**Earth:**
- **Hot springs** are only where real ones are: about 5,500 known hot springs and geysers from OpenStreetMap and GeoNames,
  each placed by the game's hot spring generator where the ground allows.
- **Volcanoes** stand where the real ones are. Each of about 825 volcanic mountains has a body inside it: a magma chamber
  of lava deep under the summit and a conduit up toward the vent.
  - The 332 that were active in the game's time (about 1420 to 1520) have lava in their conduit up to 3 blocks under the
    vent. Cones and shields on land also have a summit crater and fresh volcanic ash (tephra) around it.
  - Eight have **lava lakes** in their craters, as in the historical record: Erta Ale, Nyiragongo, Kīlauea, Erebus,
    Masaya, Villarrica, Ambrym and Nyamulagira.
  - Dormant volcanoes have no crater and a plugged conduit, but still a smaller core of lava in their chamber.
- **Lava** lies only in and under the real volcanoes, not in random caves, and no lava springs out of mountain sides.

Be careful at active craters, and when you dig into a volcano: the crust over the lava is thin.
