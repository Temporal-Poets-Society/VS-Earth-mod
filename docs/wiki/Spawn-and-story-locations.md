# Spawn and story locations

Where you start, and where the game's story sites and ruins end up on the real Earth.

## Where you start

**Vanilla:** you start near the middle of the world, in a climate you choose on the **Spawn & Death** tab.

**Earth:** **Map · Start** on the **Earth start** tab:
- **Random place at the starting climate** (default): dry lowland somewhere on Earth whose real yearly mean temperature
  matches the starting climate you choose on the Spawn & Death tab. Each part of the world (western Europe, East Asia,
  sub-Saharan Africa, eastern North America and so on) gets an equal chance, so starts don't pile up in the big
  continental interiors. The same seed gives the same place.
- **A capital city:** one of 36 capitals, from London, Paris and Riga to Cairo, Nairobi, Tokyo, Canberra, Washington,
  Lima and Buenos Aires. You start on the nearest dry, gentle land there.
- **Coordinates below:** your own place, in **Map · Start coordinates**.

### Starting at your own coordinates

Set **Map · Start** to **Coordinates below** and paste a place into **Map · Start coordinates**. Earth reads what
Google Maps gives you:
- the numbers it shows when you click a place: `56.949600, 24.105200`;
- degrees, minutes and seconds: `56°56'58.6"N 24°06'18.7"E`;
- a Google Maps link with `@latitude,longitude` in it;
- or type latitude, longitude in degrees yourself, south and west negative: `-33.86, 151.21`.

You start on the nearest dry, gentle land within about 3,000 blocks of that point. If the coordinates can't be read,
the world starts at a random place and tells you why in chat.

What it means for you: the starting climate matters more than in vanilla, because it's the real climate of a real
place. A cold start in Siberia or Canada means a long, hard winter; a hot start may be dry. See
[Climate and seasons](Climate-and-seasons) and [Progression](Progression).

## Story locations

**Vanilla:** the story sites (the village, Tobias' cave, the treasure hunter's camp, the lazaret, the resonance archive,
the devastation area) are placed at set distances from your spawn, and the land around a surface site is levelled.

**Earth:** the story sites keep about vanilla's distances from your spawn, but each moves to land that suits it: dry,
even, ice-free lowland away from rivers, never closer than three quarters of vanilla's distance. Each surface building
stands on a levelled pad the size of its footprint at the height of the land around it, blending into the real
landscape. Tobias' cave, which brings its own hill, looks for hilly land. The devastation area stays at the spot the
game picks when that is dry land, and otherwise moves to the nearest dry land.

What it means for you: the sites are as far away as in vanilla, in whatever direction the land allows, so at fine
scales they may be across a sea or a mountain range. Vanilla leaves them for you to find; server admins can list them with `/earth story` (see [Commands](Commands)).

## Ruins, traders and translocators

**Vanilla:** ruins, trader camps and translocators placed by structures appear anywhere.

**Earth:** they appear only where people lived around 1318: everywhere except places nobody had settled by then
(Antarctica, Svalbard, islands found later such as the Azores, St Helena, the Mascarenes, the Galápagos and Bermuda)
and ice-cap climate (the Greenland ice sheet, the highest summits). Ice-free Arctic coasts keep them.

The ruins themselves are vanilla's.
