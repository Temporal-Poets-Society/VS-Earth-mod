namespace EarthMap
{
    /// <summary>
    /// Server config, ModConfig/earth.json. Everything that shapes the terrain is frozen into the savegame the
    /// first time a new world loads with the mod, so editing it later only affects new worlds.
    /// </summary>
    public class EarthMapConfig
    {
        /// <summary>Config layout version; older files get the new defaults for settings whose meaning changed.</summary>
        public int ConfigVersion = 0;
        public const int CurrentConfigVersion = 5;

        /// <summary>Allow new worlds with the earth play style to get Earth. Existing worlds without Earth Map data are never changed.</summary>
        public bool Enabled = true;

        /// <summary>(Decision 0083) An Earth world generates only with every part of generation its saved rules use (regional
        /// geology, ground columns, ore layers, Earth's ores, springs, the water table, plants as around 1318, volcanoes, the
        /// rule data, Earth's guards on vanilla's generation): when one is off this session (a game update changed a hook, a
        /// competing mod, a missing file), the server stops before the first chunk and the log names it. True: the world
        /// generates without those parts, so new chunks get vanilla's blocks there and may meet other chunks with a seam.</summary>
        public bool AcceptMissingFeatures = false;

        /// <summary>Keep the slow parts of a world's model build (the straits' courses, the sea channels' ways) in
        /// ModData/earth/cache between starts (Core.ModelCache): later starts of the world skip them. Off: every start builds
        /// them; the world is the same either way.</summary>
        public bool ModelCache = true;

        /// <summary>Native geographic ranges for natural trees and shrubs in NEW Earth worlds.</summary>
        public bool RegionalTrees = true;
        /// <summary>One-shot opt-in to decision 0032's summit heights and alpine surfaces in future chunks.
        /// Existing chunks stay unchanged and can meet new heights with a seam. No vertical datum changes.</summary>
        public bool UpgradeExistingWorldMountainTerrain = false;
        /// <summary>One-shot terrain opt-in: dry wadis, audited reservoir removal and shipping straits. New chunks only.</summary>
        public bool UpgradeExistingWorldWaterTerrain = false;
        /// <summary>One-shot: the next Earth world started applies RegionalTrees (on or off) to its save, then this resets to
        /// false. Tree rule fixes reach existing regional-tree saves automatically; only new chunks change.</summary>
        public bool UpgradeExistingWorldTreeRules = false;
        /// <summary>Native geographic ranges for ground plants from regional plant packs (Floral Zones etc.), in Earth worlds
        /// with regional trees. Applied at every start (new chunks only); vanilla ground plants keep their climate rules.</summary>
        public bool RegionalPlants = true;

        /// <summary>Regional rock sequences and surface materials in NEW Earth worlds.</summary>
        public bool RegionalGeology = true;
        /// <summary>Ground columns for NEW Earth worlds with regional geology (soil by soil type, Quaternary cover, bedrock to the
        /// basement, in metres on a log depth curve) where the ground data reaches. Worlds made from the menu choose them in the
        /// Earth tab ("Rocks and ground": ground columns, the default); this flag only decides for worlds made without that
        /// option, and is off for them.</summary>
        public bool GroundColumnsPrototype = false;
        /// <summary>One-shot: the next Earth world started adopts RegionalGeology (on or off), the current rock-layer rules and
        /// GeologyTransitionBlocks for new chunks, then this resets to false. Rock layers in new chunks may not line up with
        /// already generated ones. Resource-eligibility fixes reach regional-geology saves without this.</summary>
        public bool UpgradeExistingWorldGeologyRules = false;
        /// <summary>World-coordinate transition zone half-width. 0 retains sharp source-grid contacts; range 0..512.</summary>
        public double GeologyTransitionBlocks = 96;

        /// <summary>Evidence-based resources in NEW worlds. Existing worlds retain their saved ore rules.</summary>
        public bool RegionalOres = true;
        /// <summary>One-shot: the next Earth world started adopts RegionalOres (on or off) and the Ores tuning below for new
        /// generation, then this resets to false. Rule fixes and new evidence data reach regional-ore saves without this.</summary>
        public bool UpgradeExistingWorldOreRules = false;
        public Core.OreTuning Ores = new Core.OreTuning();

        /// <summary>Native geographic ranges for supported natural animal spawns in NEW Earth worlds.</summary>
        public bool RegionalFauna = true;
        /// <summary>Creation fallback for extinct species. Pre.12+ worlds freeze their choice; older saves apply at restart.</summary>
        public bool IncludeExtinctAnimals = false;
        /// <summary>One-shot: the next Earth world started applies RegionalFauna (on or off) to its save, then this resets to false.</summary>
        public bool UpgradeExistingWorldFaunaRules = false;
        /// <summary>One-shot: the next Earth world started (with Cylinder World) carries Antarctica across the seam gutter in
        /// chunks generated from then on, instead of cutting a strait through it, then this resets to false. New worlds from
        /// 0.13.2 always do. Chunks already generated in the Antarctic gutter keep the strait and may meet the bridge with a step.</summary>
        public bool UpgradeExistingWorldGutterBridges = false;
        /// <summary>One-shot: the next Earth world started gets the waterfalls (Victoria, Niagara, Iguazu, ...) on its rivers in chunks
        /// generated from then on, then this resets to false. New worlds from 0.13.3 always have them. Chunks already generated
        /// near a fall keep the old river levels and may meet the new ones with a step.</summary>
        public bool UpgradeExistingWorldRiverFalls = false;
        /// <summary>One-shot: the next Earth world started gets the river exit channels (rivers' ways out to the sea deepened for
        /// ships) in chunks generated from then on, then this resets to false. New worlds from 0.13.3 always have them. Chunks
        /// already generated keep their shallower water and may meet the channel with a step on the sea bed.</summary>
        public bool UpgradeExistingWorldRiverChannels = false;
        /// <summary>One-shot: the next Earth world started gets the river ends of 0.14.1 in chunks generated from then on, then this
        /// resets to false: rivers continue downstream to their real end (no gaps where a river loses water), end in the
        /// terminal lakes (the Aral Sea, Lop Nur, the Hamun lakes, Lake Chad, ... as before irrigation dried them; Lake Eyre,
        /// Etosha and other salt lakes filled), are led on to a sea or lake they stop short of, and fade out in dry basins.
        /// New worlds from 0.14.1 always have them. Chunks already generated keep their land and rivers and may meet the new
        /// lakes and river courses with a step.</summary>
        public bool UpgradeExistingWorldRiverEnds = false;
        /// <summary>One-shot: the next Earth world started gets the delta branches of 0.14.7 in chunks generated from then on, then
        /// this resets to false: where a river splits into branches (the Mekong at Vàm Nao) they share its water, instead of one
        /// branch taking all of it as a canal-wide river. Needs the river ends. New worlds from 0.14.7 always have them. Chunks
        /// already generated keep their rivers and may meet the new widths with a step.</summary>
        public bool UpgradeExistingWorldRiverDeltas = false;
        /// <summary>One-shot: the next Earth world started gets the river widths of 0.14.7 in chunks generated from then on, then
        /// this resets to false: the standard world's gameplay widths in blocks on any bigger map, real widths where those are
        /// wider (the Amazon on very big maps). Worlds up to the standard 204 800 ring are unchanged; in bigger ones rivers
        /// become narrower and chunks already generated may meet them with a step. New worlds from 0.14.7 always have them.</summary>
        public bool UpgradeExistingWorldRiverWidths = false;
        /// <summary>One-shot: the next Earth world started gets the sea floor of 0.14.1 in chunks generated from then on, then this
        /// resets to false: at least 6 blocks of water away from the shore (was 5), so a carrack has room over shallow banks
        /// such as the Bahamas. New worlds from 0.14.1 always have it. Chunks already generated keep their sea floor and may
        /// meet the new one with a one-block step.</summary>
        public bool UpgradeExistingWorldSeaDepth = false;
        /// <summary>One-shot: the next Earth world started gets the lake shores of 0.14.3 in chunks generated from then on, then this
        /// resets to false: land around a lake that is lower than the lake slopes down to the real ground instead of standing flat
        /// at the lake's level with a cliff where that ends (south of Lake Bolsena and Lake Bracciano). New worlds from 0.14.3
        /// always have it. Chunks already generated keep their land and may meet the new slopes with a step.</summary>
        public bool UpgradeExistingWorldLakeShores = false;
        /// <summary>One-shot: give the Earth world being started the newest climate fix (decision 0018: v2, the complete UDel
        /// normals, without the one-row climate stripes and with December's precipitation over the land) for chunks generated
        /// from now on. Chunks already generated keep their climate, so forests, soil and grass may change with a straight edge
        /// where new chunks meet old ones. Reset to false once applied.</summary>
        public bool UpgradeExistingWorldClimateData = false;
        /// <summary>One-shot: the next Earth world started takes its own canal options (earthSuezCanal and earthPanamaCanal on
        /// the world creation screen's Earth tab; change them with /worldconfig first) for chunks generated from then on, then
        /// this resets to false. Chunks already generated on a canal's course keep their land: dig through them or regenerate
        /// them.</summary>
        public bool UpgradeExistingWorldCanals = false;

        /// <summary>Small islands (under 1000 km²) in NEW worlds from their real outlines (GSHHG), at their true size, instead
        /// of the elevation grid's blobs; ETOPO's false islands on reef rims (Bahamas, Chagos, the Spratlys) become shallow reef.</summary>
        public bool Islands = true;
        /// <summary>For new worlds: islands narrower than this many blocks are left out, so how many show depends on the
        /// world's size (with the default 4: about 40 m wide with a 4 million block ring, 300 m with 512 000, 800 m with
        /// 204 800). Range 1..64.</summary>
        public double MinIslandBlocks = 4;
        /// <summary>One-shot: the next Earth world started takes Islands (on or off), the current island rules (rounded
        /// corners) and MinIslandBlocks for chunks generated from then on, then this resets to false. Chunks already
        /// generated keep their old coasts: near small islands and reefs they may meet the new ones with a step.</summary>
        public bool UpgradeExistingWorldIslands = false;
        /// <summary>The big land's coasts in NEW worlds from their real outlines (GSHHG) instead of the 2' elevation grid's cells,
        /// where those cells are at least CoastMinCellBlocks wide (a 2' cell is ~47 blocks with a 512 000 block ring, ~370 with
        /// 4 million): headlands, coves, fjords and spits at their true size. Needs Islands.</summary>
        public bool Coasts = true;
        /// <summary>For new worlds: the outline coasts only where a 2' grid cell is at least this many blocks wide (32: rings from
        /// about 345 600 blocks). Range 8..1000.</summary>
        public double CoastMinCellBlocks = 32;
        /// <summary>One-shot: the next Earth world started takes Coasts (on or off; on also needs the world's islands and its
        /// cells to be CoastMinCellBlocks wide) for chunks generated from then on, then this resets to false. Chunks already
        /// generated keep their old coasts and meet the new ones with a step along the coast where they differ.</summary>
        public bool UpgradeExistingWorldCoasts = false;

        /// <summary>"natural": ranges without human impact (PHYLACINE present-natural, e.g. lions in southern Europe);
        /// "current": present-day ranges where the data has them; "1318": natural ranges without what each animal had lost by
        /// about 1318 (decision 0054). Worlds placed as around 1318 use "1318" unless this says "current". Frozen in pre.12+
        /// worlds; older saves apply it at restart.</summary>
        public string FaunaRangeProfile = "natural";

        /// <summary>How far (blocks) to look for dry land around the start chosen on the Earth tab (a capital or coordinates).</summary>
        public double SpawnSearchRadius = 3000;

        /// <summary>Share of the world height kept free above Mount Everest. Only the test-only legacy height curve reads it
        /// (VerticalTerrainOptions): the Earth tab's curves put Everest at the second-highest block.</summary>
        public double TopMargin = 0.0625;
        /// <summary>Land curve (m): smaller exaggerates lowlands more and compresses mountains. y = sea + S·ln(1 + h/LandCurve).
        /// 200 puts the Alps at ~85% of Everest's height.</summary>
        public double LandCurve = 200;
        /// <summary>Sea floor curve (m): smaller makes shallow seas deeper relative to the abyss (20: the Gulf of Riga ~10 blocks deep).</summary>
        public double SeaCurve = 20;
        /// <summary>Deepest sea floor as a share of the sea level's height above the world bottom (0.43: 71 blocks at world height
        /// 384). Only the test-only legacy vertical budget reads it (VerticalTerrainOptions): the Earth tab's budgets keep the
        /// deepest water near 47 blocks.</summary>
        public double MaxSeaDepth = 0.43;
        /// <summary>Strength of the detail noise added to the 2' elevation data (1 = default).</summary>
        public double DetailScale = 1.0;

        /// <summary>
        /// New worlds: insert the seam gutter, a strip of open sea around the jump line that is not part of the Earth (the
        /// layout of worlds before decision 0030, for custom worlds that want sea at the jump). False (default): the real
        /// Earth runs across the jump line, so St. Lawrence Island keeps its shape, when Cylinder World keeps Earth's terrain
        /// in its seam band (its PolarIceMode "earth"); otherwise new worlds keep the gutter anyway.
        /// </summary>
        public bool SeamGutter = false;

        /// <summary>With SeamGutter: open sea added beyond Cylinder World's seam band on each side of the jump line.</summary>
        public double GutterMargin = 512;

        /// <summary>Carve the large rivers (HydroRIVERS) into the terrain.</summary>
        public bool Rivers = true;
        /// <summary>Smallest navigable river, by long-term average discharge (m³/s). 100: Daugava, Rhine, Seine, Elbe, Danube…</summary>
        public double RiverMinDischarge = 100;
        /// <summary>
        /// Multiplier on river widths (1.5: Daugava ~21 blocks at its mouth, Rhine ~31, Danube ~42, Amazon ~72, in a 256k
        /// world). Widths also grow with the world's size, so a larger world keeps the same proportions.
        /// </summary>
        public double RiverWidthScale = 1.5;
        /// <summary>
        /// Streams below the navigable size: "ends" continues each river upstream along its main branch, narrowing to a
        /// one-block stream (the rivers don't start abruptly); "all" adds every stream down to StreamMinDischarge (many
        /// more, for larger worlds); "none" only the navigable rivers. "auto" (default, decision 0061): "all" in worlds at
        /// 1:25 and finer, "ends" in coarser ones.
        /// </summary>
        public string Streams = "auto";
        /// <summary>Smallest stream (m³/s), where it is one block wide. The data holds rivers down to 20.</summary>
        public double StreamMinDischarge = 20;
        /// <summary>
        /// 0..1: loop cut-offs for the rivers' exaggerated width. Where a tight bend would make the river touch itself
        /// (slivers of land, steps between its arms) the loop is cut off like a meander neck, earlier the higher this is.
        /// 0 keeps every loop.
        /// </summary>
        public double RiverStraighten = 0.5;
        /// <summary>Smoothing: wiggles smaller than this many river widths are straightened out. 0 keeps the real courses.</summary>
        public double RiverSmoothWidths = 2;
        /// <summary>Share of the land's relief the river surface follows. 1 keeps rivers level with the lakes they pass through;
        /// lower values give deeper valleys and fewer steps, but a step where a river leaves a lake.</summary>
        public double RiverRelief = 1.0;

        /// <summary>Temperatures below -20 °C / above 40 °C where the real climate is that extreme (vanilla caps them), live and in
        /// world generation. Frozen into NEW worlds at creation (EarthParams.TemperatureRangeFrozen); worlds made before take it
        /// at every start.</summary>
        public bool ExtendTemperatureRange = true;
        /// <summary>Each place's real seasonal swing (coldest to warmest month) instead of vanilla's latitude-only one.</summary>
        public bool RealSeasons = true;

        /// <summary>Move the story locations to suitable real land near where vanilla would put them.</summary>
        public bool MoveStoryLocations = true;
        /// <summary>How far (blocks, 64..50 000) to look for land around where vanilla would put a story location; the search
        /// widens twice (to 4x).</summary>
        public double StorySearchRadius = 3000;

        /// <summary>Earth's weather (decision 0017): rain, clouds, winds and storms from the real climate of each place and
        /// month (the monsoons, dry Mediterranean summers, the trade winds and westerlies, tropical cyclones in their seasons),
        /// the same on the server and every client. Off: vanilla's weather. Creation fallback in pre.12+; older saves apply at every start.</summary>
        public bool Weather = true;
        /// <summary>Weather hours per game hour (0.25..8). 1: fronts and storms move at their real speed over the map's real
        /// kilometres, so a rainy morning lasts a morning and a tropical cyclone takes days to cross an ocean. The game's
        /// year is short (108 days by default), so each season sees fewer weather systems than the real one; higher values
        /// give more of them, but the weather at a place changes faster.</summary>
        public double WeatherPace = 1;
        /// <summary>Multiplies how often it rains everywhere (0..3; 1 = the real monthly rain chance).</summary>
        public double RainFrequency = 1;
        /// <summary>Multiplies how often storms form (0..5; 1 = the real yearly numbers, 0 = no storms).</summary>
        public double StormFrequency = 1;

        /// <summary>Decision 0051: Earth's data release, where GLO-90 squares come from first (already converted, one square per
        /// range request). A local folder holding the release's manifest.json and parts also works (tests, a copy on disk);
        /// empty: none.</summary>
        public string ElevationRepo = Core.DemDownloader.Glo90Repo;
        /// <summary>Decision 0051: Copernicus's originals, for squares the release can't supply and for downloading all from
        /// Copernicus; the public bucket by default. A local folder holding the published .tif files also works; empty: none.</summary>
        public string ElevationSource = Core.DemDownloader.Glo90Bucket;
        /// <summary>Tiles fetched at the same time (1..8).</summary>
        public int ElevationDownloads = 3;
        /// <summary>Where the 30″ ground sets come from (soil groups, depth to bedrock, permafrost; 172 MB): Earth's data release
        /// on GitHub. A new ground-columns world fetches them into ModData/earth/ground before its first chunk. A local folder
        /// holding the three .egts files also works; empty: never fetched (sets already on the machine are still used).</summary>
        public string GroundRepo = Core.GroundDownloader.Release;
        /// <summary>Where the server's data files come from (decision 0066: terrain, water and ground, 273 MB): Earth's data
        /// release on GitHub. The server fetches the missing ones into ModData/earth/data before it builds a world. A local
        /// folder or a mirror holding the files also works; empty: never fetched (files already there are still used).</summary>
        public string DataRepo = Core.EarthDataStore.Release;
        /// <summary>How far around online players (blocks) GLO-90 tiles are fetched ahead of generation; 0: none.</summary>
        public int ElevationPrefetchBlocks = 4096;

        public SelfTestConfig SelfTest = new SelfTestConfig();
    }

    /// <summary>For automated testing on a dedicated server: generate a few places, render them, report, stop the server.</summary>
    public class SelfTestConfig
    {
        public bool Enabled = false;
        public int RadiusChunks = 3;
        public bool Shutdown = true;
        public bool RenderWorldMap = true;
        public bool CheckRivers = true;
        /// <summary>"spawn", a story location code, or "lat,lon".</summary>
        public string[] Places = { "spawn", "treasurehunter", "village", "45.83,6.86", "52.37,4.9", "31.5,35.5", "65.85,-168.4", "72.5,-38.5",
            "27.99,86.93", "25.3,55.0", "29.8,27.0", "42.0,51.0", "61.1,6.5", "jumpline", "-85.5,20.0", "-3.1,-60.0",
            "54.72,17.42", "57.25,23.1", "56.95,23.78", "54.9,20.6" };
        /// <summary>Log how each place's chunks are stored (empty, one block, mixed; bytes unpacked and compressed) and the
        /// server's memory.</summary>
        public bool MeasureStorage = false;
        /// <summary>Write a SHA-256 of every block and fluid of each generated chunk column at each place (EarthMap/digest-*.txt
        /// and one DIGEST line per place), so two builds can be checked to generate byte-identical worlds.</summary>
        public bool Digest = false;
        /// <summary>With Digest: places whose chunk columns are also dumped raw (block and fluid ids, EarthMap/dump-*.bin), with
        /// the block code table, to find which blocks two runs disagree on.</summary>
        public string[] DigestDumpPlaces = new string[0];
        /// <summary>With Digest: the generation passes at whose end each column is hashed (Terrain, TerrainFeatures, Vegetation,
        /// NeighbourSunLightFlood, PreDone). PreDone is the whole of generation; the earlier ones isolate Earth's terrain,
        /// rock and ore output from the vegetation pass.</summary>
        public string[] DigestPasses = { "PreDone" };
        /// <summary>With Digest: chunk columns ("cx,cz") dumped raw as each digest pass leaves them (EarthMap/gen/pass/dump-*.bin).</summary>
        public string[] DigestDumpColumns = new string[0];
    }
}
