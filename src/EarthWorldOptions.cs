using System;
using System.Globalization;
using System.Reflection;
using EarthMap.Core;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// The Earth tab of the world creation screen (decision 0023): worldconfig.json at the mod's root declares the options,
    /// assets/game/lang/worldconfig-en.json names them. They are stored in the world's configuration and read once, when a
    /// new Earth world is created.
    /// </summary>
    public static class EarthWorldOptions
    {
        /// <summary>The 1:100 play style's code; the other scales' codes add their ratio (<see cref="PlayStyleCodes"/>).</summary>
        public const string PlayStyleCode = "earth";

        /// <summary>The Earth play styles: "earth" (1:100) and one per other scale (worldconfig.json), each setting the Earth
        /// size with the world height and mountain heights that suit it (the world creation screen runs no mod code, so a
        /// play style is the only way a scale can bring its other settings).</summary>
        public static readonly string[] PlayStyleCodes = { PlayStyleCode, "earth-200", "earth-50", "earth-25", "earth-10" };

        public static bool IsEarthStyle(string code) => code != null && Array.IndexOf(PlayStyleCodes, code) >= 0;

        /// <summary>Only consulted without saved EarthParams. Never infer a style from Earth tab keys: the menu adds
        /// their defaults to every style. A missing/foreign style is deliberately vanilla.</summary>
        public static bool CreatesEarth(bool isNew, string worldType, string playStyle, bool enabled)
            => isNew && enabled && worldType == "standard" && IsEarthStyle(playStyle);

        public const string NatureKey = "earthNature";
        public const string NatureValue1318 = "1318";

        /// <summary>
        /// Decision 0047: plants and animals as around 1318 (default) or vanilla placement by climate alone. Before the
        /// option existed, "earthAnimals" made the same choice for animals; it is read when "earthNature" is missing.
        /// </summary>
        public static bool Nature1318(ITreeAttribute wc, bool fallback)
            => wc?.HasAttribute(NatureKey) == true ? Choice(wc, NatureKey, NatureValue1318, "vanilla", fallback) : RegionalAnimals(wc, fallback);

        public const string BiomesKey = "earthBiomes", BiomesEarth = "earth", BiomesVanilla = "vanilla";

        /// <summary>
        /// EA-0061, decision 0075: the Earth tab's "Biomes". "earth" (the default, and any other or missing value): Earth's
        /// vegetation zones, fire, open wetlands, ground cover, ground moisture, grass in winter and alpine surfaces. "vanilla":
        /// none of them, and vanilla's own forest and shrub maps. Independent of "Nature" (which plants and animals, where).
        /// </summary>
        public static bool VanillaBiomes(ITreeAttribute wc) => Choice(wc, BiomesKey, BiomesVanilla, BiomesEarth, false);

        public static bool RegionalAnimals(ITreeAttribute wc, bool fallback)
            => Choice(wc, "earthAnimals", "regional", "vanilla", fallback);

        /// <summary>Summit height correction (decision 0039): listed peaks at or above this real elevation get their missing height
        /// back. Off the Earth tab from 2.2.0-rc.13 (EA-0204, the author: "if we still use [it] we may take [it] off the GUI"): a
        /// new world takes DefaultSummitMinElevation, unless its world config says otherwise (servers, tests).</summary>
        public const string SummitKey = "earthSummitCorrection";
        public const int DefaultSummitMinElevation = 3000;

        public static int SummitMinElevation(ITreeAttribute wc)
        {
            string value = Get(wc, SummitKey, "3000").Trim().ToLowerInvariant();
            if (value == "off") return -1;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minimum)
                && minimum >= 1000 && minimum <= 8000 && minimum % 1000 == 0 ? minimum : DefaultSummitMinElevation;
        }

        static bool Choice(ITreeAttribute wc, string key, string on, string off, bool fallback)
        {
            string value = Get(wc, key, "").Trim().ToLowerInvariant();
            return value == on ? true : value == off ? false : fallback;
        }

        static double Frequency(ITreeAttribute wc, string key, double fallback, double max)
        {
            return double.TryParse(Get(wc, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value) && value >= 0 && value <= max ? value : fallback;
        }

        /// <summary>
        /// A new world's Earth-tab choices (EA-0089 C, CR-A5 of the 14 November 2026 code review), each resolved the one way:
        /// the tab's key, else earth.json (the server config's switches from before the tab), else the default.
        /// WorldParamsBuilder.Build writes a new world's params from them, each field once. Creation only: existing saves never
        /// pass through this.
        /// </summary>
        public sealed class EarthTab
        {
            /// <summary>Summits corrected from this height (m), -1 off (<see cref="SummitMinElevation(ITreeAttribute)"/>).</summary>
            public int SummitMinElevation;
            /// <summary>"Ores": Earth's ore maps (decision 0077's bauxite with them).</summary>
            public bool RegionalOres;
            /// <summary>"Rocks and ground": regional rock regions (not vanilla).</summary>
            public bool RegionalGeology;
            /// <summary>"Rocks and ground": ground columns on the rock regions.</summary>
            public bool GroundColumns;
            /// <summary>Ground depth for ground columns: one of GroundColumns.DepthCurve.Choices.</summary>
            public string GroundDepth;
            /// <summary>"Soil": Core.Topsoil.ClimateOnly, VanillaTiers or RealisticTiers (<see cref="Soil(ITreeAttribute)"/>).</summary>
            public int Topsoil;
            /// <summary>"Nature": plants and animals as around 1318 (decision 0047).</summary>
            public bool Nature1318;
            /// <summary>"Nature" is on the tab ("vanilla" there also means no regional tree allocation).</summary>
            public bool NatureOnTab;
            /// <summary>"Biomes": vanilla's (decision 0075).</summary>
            public bool VanillaBiomes;
            public bool IncludeExtinctAnimals, ChickensByRange, Weather, NoRiverRapids;
            /// <summary>"River width" as the world config holds it, in % (RiverWidthKey; RiverWidthMultiplier with the world's ring).</summary>
            public string RiverWidth;
            /// <summary>"Smallest stream" as the world config holds it, in m³/s (SmallestStreamKey; SmallestStream with the world's ring).</summary>
            public string SmallestStream;
            public double RainFrequency, StormFrequency;
            public int SpringRules, PondRules, RockFloorDepth;
            /// <summary>Land heights: "glo90", "glo90coarse" or "builtin" (<see cref="Elevation"/>).</summary>
            public string Elevation;
        }

        /// <summary>The Earth tab's choices for a new world of <paramref name="mapHeight"/> blocks (<see cref="EarthTab"/>).</summary>
        public static EarthTab ReadTab(ITreeAttribute wc, EarthMapConfig cfg, EarthData data, int mapHeight)
        {
            // "Rocks and ground" on the Earth tab: vanilla (the menu's default from 2.0.1, the author on 15 November, decision
            // 0084) or regional (rock regions only). In 2.2 (the author, 20 November 2026, decision 0104) the tab no longer
            // offers ground columns or their Ground depth, and "Ores" has one value, vanilla deposits plus Earth's gems and ores:
            // every new world gets them, unless earth.json's experimental switches say otherwise. The keys are still read so a
            // world config from before means what it can: ground columns or the earlier single "Rocks and ores" key (earthOres)
            // give their rock choice. Without a rock key the server config decides, as before.
            string legacy = Get(wc, OresKey, "").Trim().ToLowerInvariant();
            bool legacyKnown = legacy is OresRegional or OresGroundColumns or OresVanilla;
            string rocks = Get(wc, RocksKey, "").Trim().ToLowerInvariant();
            if (!(rocks is OresRegional or OresGroundColumns or OresVanilla) && legacyKnown) rocks = legacy;
            bool rocksKnown = rocks is OresRegional or OresGroundColumns or OresVanilla;
            string depth = Get(wc, GroundDepthKey, "").Trim().ToLowerInvariant();
            bool regional = rocksKnown ? rocks != OresVanilla : cfg.RegionalGeology;
            // (EA-0197, decision 0104) ground columns and Earth's ore maps are off the Earth tab in 2.2 (they need more work): a
            // world config's "groundcolumns" or "maps" (an older template, /worldconfig, the old earthOres key) gives regional rock
            // and vanilla's deposits. Only earth.json's experimental switches give them, for development: ground columns for any
            // world whose rock is regional, the ore maps for every new world. The Ores key isn't read: its one value on the tab,
            // vanilla, is in every new world's config, so it can't tell a choice from the default.
            return new EarthTab
            {
                SummitMinElevation = SummitMinElevation(wc),
                RegionalOres = cfg.ExperimentalOreMaps,
                RegionalGeology = regional,
                GroundColumns = cfg.ExperimentalGroundColumns && regional,
                GroundDepth = Array.IndexOf(GroundColumns.DepthCurve.Choices, depth) >= 0 ? depth : GroundColumns.DepthCurve.Default,
                Topsoil = Soil(wc),
                Nature1318 = Nature1318(wc, cfg.RegionalFauna),
                NatureOnTab = wc?.HasAttribute(NatureKey) == true,
                VanillaBiomes = VanillaBiomes(wc),
                IncludeExtinctAnimals = wc?.GetBool("earthIncludeExtinctAnimals", cfg.IncludeExtinctAnimals) ?? cfg.IncludeExtinctAnimals,
                ChickensByRange = !(wc?.GetBool(ChickensEverywhereKey, true) ?? true),
                Weather = Choice(wc, "earthWeather", "earth", "vanilla", cfg.Weather),
                // (2.0.1, decision 0084) not on the world screen: the attributes' default is empty and no play style sets them,
                // so earth.json decides unless a world config sets a value
                RainFrequency = Frequency(wc, "earthRainFrequency", cfg.RainFrequency, 3),
                StormFrequency = Frequency(wc, "earthStormFrequency", cfg.StormFrequency, 5),
                NoRiverRapids = !Choice(wc, RiverRapidsKey, "steep", "off", true),
                RiverWidth = Get(wc, RiverWidthKey, ""),
                SmallestStream = Get(wc, SmallestStreamKey, ""),
                SpringRules = SpringRules(wc),
                PondRules = PondRules(wc),
                Elevation = Elevation(wc, data),
                RockFloorDepth = RockFloorDepth(wc, mapHeight),
            };
        }

        /// <summary>"Rocks and ground": OresVanilla (the menu's default from 2.0.1, decision 0084) or OresRegional; OresGroundColumns,
        /// no longer on the tab (decision 0104), gives regional rock, with ground columns only under earth.json's
        /// ExperimentalGroundColumns.</summary>
        public const string RocksKey = "earthRocks";
        /// <summary>Ores: on the tab with OreDepositsVanilla only in 2.2 (decision 0104); OreDepositsMaps, from before, is no longer
        /// read. earth.json's ExperimentalOreMaps alone gives the ore maps.</summary>
        public const string OreDepositsKey = "earthOreDeposits", OreDepositsVanilla = "vanilla", OreDepositsMaps = "maps";
        /// <summary>The earlier single "Rocks and ores" key, read only where the two keys above are missing; its values
        /// are also the rock choices.</summary>
        public const string OresKey = "earthOres", OresRegional = "regional", OresGroundColumns = "groundcolumns", OresVanilla = "vanilla";
        /// <summary>Ground depth for ground columns: GroundColumns.DepthCurve.Choices, "log4000" by default.</summary>
        public const string GroundDepthKey = "earthGroundDepth";
        /// <summary>The Earth tab's world height: its slider runs 256 to 1024 in steps of 64 (2.1.2, decision 0089: the Mod DB's
        /// parser refuses the game's skipValues and alarm keys). 0, from a config or /worldconfig, keeps the World generation
        /// tab's, whose slider stops at 512 outside creative building.</summary>
        public const string HeightKey = "earthWorldHeight";
        public const int MinHeight = 256, MaxHeight = 1024;
        /// <summary>The slider's former "Automatic (for the Earth size)" (0053): a value no world height can have. The slider
        /// no longer offers it (worldconfig.json); a world config that holds it (made before, or typed by hand) still resolves.</summary>
        public const int AutoHeight = 64;
        /// <summary>The Equator (m): a world's metres per block across are this over its Cylinder World ring.</summary>
        public const double EquatorMetres = SpawnSearch.EquatorMetres;

        /// <summary>Metres per block across of the world the Earth tab makes: from the Earth size (a scale), or the World
        /// generation tab's width when that is chosen (its ring taken as 0.8 times the width, as before 2.0.0-rc.5).</summary>
        public static double MetresPerBlock(ITreeAttribute wc)
        {
            string value = Get(wc, SizeKey, SpawnSearch.DefaultScale.ToString(CultureInfo.InvariantCulture)).Trim();
            int length;
            if (value.Equals(VanillaSize, StringComparison.OrdinalIgnoreCase))
                length = int.TryParse(Get(wc, "worldWidth", "1024000"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 512
                    ? SpawnSearch.LengthForWidth(w) : SpawnSearch.LengthForWidth(1024000);
            else SpawnSearch.SizeFor(value, out _, out length, out _);
            return EquatorMetres / (2.0 * Math.Max(512, length));
        }

        /// <summary>The world configuration's distance from the equator to a pole in blocks (vanilla's GenMaps latitude and
        /// realistic climate, Astra Terra's longitude fallback, Rough Seas' metres per block).</summary>
        public const string PolarEquatorDistanceKey = "polarEquatorDistance";

        /// <summary>
        /// Decision 0072: an Earth world's equator-to-pole distance in blocks, the 90° of latitude its geometry lays between the
        /// equator and a pole (Cylinder World's pole distance, the same one the calendar's latitude uses). 10,000 km over the
        /// scale to within 0.3% (world lengths are whole 512-block map regions): 50,128 at 1:200, 100,048 at 1:100, 1,001,936 at 1:10 with Cylinder World's 48-block walls.
        /// </summary>
        public static int PolarEquatorDistance(double blocksPerDegreeZ) =>
            double.IsFinite(blocksPerDegreeZ) && blocksPerDegreeZ > 0 ? (int)Math.Round(90 * blocksPerDegreeZ) : 0;

        /// <summary>
        /// For a new Earth world (decision 0072): writes its <see cref="PolarEquatorDistance"/> into the world configuration,
        /// over a play style's or the World generation tab's value, at WorldReady (before the world generators start; vanilla
        /// reads it in GenMaps' InitWorldGenerator). Existing worlds never pass through this. Returns a line for the log, or
        /// null when the geometry gives no distance.
        /// </summary>
        public static string ApplyPolarEquatorDistance(ITreeAttribute wc, double blocksPerDegreeZ)
        {
            int distance = PolarEquatorDistance(blocksPerDegreeZ);
            if (wc == null || distance <= 0) return null;
            string was = Get(wc, PolarEquatorDistanceKey, "").Trim(), now = distance.ToString(CultureInfo.InvariantCulture);
            wc.SetString(PolarEquatorDistanceKey, now);
            return was == now
                ? string.Format(CultureInfo.InvariantCulture, "Earth: polarEquatorDistance {0} blocks (the equator to a pole in this world).", now)
                : string.Format(CultureInfo.InvariantCulture, "Earth: polarEquatorDistance {0} blocks, the equator to a pole in this world (was {1}).", now, was.Length > 0 ? was : "not set");
        }

        /// <summary>
        /// Automatic world height (0053): about as many metres per block up as across, where the height allows it. 1024 at
        /// 1:10 (and the 1:12 of 4,096,000), 512 at 1:25, 384 at 1:50 and smaller scales (Earth's heights are tuned for 384).
        /// </summary>
        public static int AutoHeightFor(double metresPerBlock) => metresPerBlock < 17.5 ? 1024 : metresPerBlock < 37.5 ? 512 : 384;

        /// <summary>Worlds under this many metres per block across (1:25 and finer) are the fine scales of decision 0061.</summary>
        public const double FineScaleMetres = 37.5;

        /// <summary>
        /// 0061's quick step (the author, 8 November 2026: "Quick step first"): earth.json's Streams "auto" (the default) draws
        /// every stream the river data has, down to StreamMinDischarge, in worlds at 1:25 and finer, where GLO-90 shows their
        /// valleys ("all"), and each river's upper main branch in coarser ones ("ends"). Any other value is kept as it is. A
        /// world saves the result, so a later default never changes its rivers.
        /// </summary>
        /// <summary>Whether a ring this long is a world at 1:25 or finer (under FineScaleMetres per block at the equator).</summary>
        public static bool FineScale(double ringLength) => ringLength > 0 && EquatorMetres / ringLength < FineScaleMetres;

        public static string StreamsFor(string configured, double ringLength)
        {
            if (!string.Equals((configured ?? "").Trim(), "auto", StringComparison.OrdinalIgnoreCase)) return configured;
            return ringLength > 0 && EquatorMetres / ringLength < FineScaleMetres ? "all" : "ends";
        }

        /// <summary>Automatic mountain heights (0053, author 5 November: "linear only above 512 or more"): linear (real
        /// proportions, Everest at the second-highest block) in worlds 512 and more blocks high; balanced relief (log 1000 m)
        /// in lower ones, where linear would leave the lowlands flat. By the world's height, not its scale, so a height
        /// chosen by hand counts too.</summary>
        public static string AutoCurveFor(int mapHeight) => mapHeight >= 512 ? "linear" : VerticalTerrainOptions.DefaultCurve;

        /// <summary>The Earth tab's Mountain heights, with Automatic resolved for the world's height: <paramref name="mapHeight"/>
        /// when the caller knows it, otherwise the Earth tab's (0 when that keeps the World generation tab's, read as lower
        /// than 512). The tab offers no "auto" any more; a world config that holds it still resolves.</summary>
        public static string Curve(ITreeAttribute wc, int mapHeight = 0)
        {
            string value = Get(wc, "earthHeightCurve", VerticalTerrainOptions.DefaultCurve).Trim().ToLowerInvariant();
            if (value != "auto") return value;
            return AutoCurveFor(mapHeight > 0 ? mapHeight : Height(wc, out _));
        }

        /// <summary>
        /// The Land heights setting (0053; off the Earth tab since 5 November 2026, Automatic): "glo90" (full, downloaded),
        /// "glo90coarse" (included) or "builtin". Automatic is full GLO-90 at every Earth size, the author's "GLO-90 only": at
        /// 1:400 to 1:50 full GLO-90 was 2–20 times closer to the blocks' true heights than the included grid (scalecheck, eight
        /// test areas), and squares that can't be had keep the included heights. Without the height data packs the included
        /// heights fall back to the built-in ones.
        /// </summary>
        public static string Elevation(ITreeAttribute wc, EarthData data)
        {
            string value = Get(wc, ElevationKey, "auto").Trim().ToLowerInvariant();
            if (value == "auto") value = "glo90";
            if (value != "glo90" && value != "glo90coarse") return "builtin";
            if (value == "glo90coarse" && data != null && data.Coarse == null) return "builtin";
            return value;
        }

        /// <summary>The Earth tab's world height, or 0 to keep the World generation tab's. A value that isn't a height in
        /// steps of 32 also keeps it, and comes back in <paramref name="bad"/>.</summary>
        public static int Height(ITreeAttribute wc, out string bad)
        {
            string value = Get(wc, HeightKey, "0").Trim();
            bad = null;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) && height == AutoHeight)
                return AutoHeightFor(MetresPerBlock(wc));
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
                && (height == 0 || height >= MinHeight && height <= MaxHeight && height % 32 == 0)) return height;
            bad = value;
            return 0;
        }

        /// <summary>
        /// For a new world: the Earth tab's height into the save and the server's map. Runs in StartPre, before any mod reads
        /// the height (vanilla's sea level at AssetsFinalize); the game builds the map from the save again after the start
        /// phase. Returns a line for the log, or null when the World generation tab's height stays.
        /// </summary>
        public static string ApplyHeight(ICoreServerAPI sapi)
        {
            var save = sapi.WorldManager.SaveGame;
            int height = Height(save.WorldConfiguration, out string bad), was = sapi.WorldManager.MapSizeY;
            if (bad != null)
                return string.Format("Earth height '{0}' is not {1} to {2} in steps of 32; this world keeps the World generation tab's {3}.", bad, MinHeight, MaxHeight, was);
            if (height == 0 || height == was) return null;
            var field = save.GetType().GetField("MapSizeY", BindingFlags.Public | BindingFlags.Instance);
            object map = sapi.World.GetType().GetField("WorldMap", BindingFlags.Public | BindingFlags.Instance)?.GetValue(sapi.World);
            var init = map?.GetType().GetMethod("Init", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int), typeof(int), typeof(int) }, null);
            if (field == null || field.FieldType != typeof(int) || init == null)
                throw new MissingMemberException(save.GetType().FullName, "MapSizeY / WorldMap.Init");
            field.SetValue(save, height);
            init.Invoke(map, new object[] { sapi.WorldManager.MapSizeX, height, sapi.WorldManager.MapSizeZ });
            return string.Format("Earth height: world height {0} from the Earth tab (the World generation tab's was {1}).", height, was);
        }

        /// <summary>The Earth tab's rock floor: blocks below each chunk column's lowest surface where plain rock begins.
        /// "auto" is 160 in worlds taller than 512 and none otherwise.</summary>
        public const string RockFloorKey = "earthRockFloor";
        public const int AutoRockFloor = 160;

        public static int RockFloorDepth(ITreeAttribute wc, int mapHeight)
        {
            string value = Get(wc, RockFloorKey, "auto").Trim().ToLowerInvariant();
            if (value == "off") return 0;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth) && depth >= 32 && depth <= 512) return depth;
            return mapHeight > 512 ? AutoRockFloor : 0;
        }

        /// <summary>0051/0053: land heights from Copernicus GLO-90, full ("glo90") or included ("glo90coarse"), or the
        /// built-in grid ("builtin"); "auto" (the default) picks by the Earth size (Elevation).</summary>
        public const string ElevationKey = "earthElevation";

        /// <summary>
        /// 0051: the Earth tab's GLO-90 land heights: "release" (the Earth tab's default) or "copernicus" (download all when
        /// the new world opens, from Earth's data release or from Copernicus's originals), or "needed" (squares as the world
        /// reaches them). A world without the key (made before the row existed) downloads as needed: opening an older world
        /// never starts the full download. The world creation screen runs no mod code, so the download can only start once
        /// the world does. "first" (EA-0133): as "release", but a new single-player world's first opening waits for it on
        /// the game's loading screen (Core.DownloadFirst).
        /// </summary>
        public const string Glo90DownloadKey = "earthGlo90Download";

        /// <summary>"release" or "copernicus" when the Earth tab asks to download all GLO-90 squares ("first" is the release's),
        /// else null.</summary>
        public static string Glo90Download(ITreeAttribute wc)
        {
            string value = Get(wc, Glo90DownloadKey, "needed").Trim().ToLowerInvariant();
            return value == "release" || value == "first" ? "release" : value == "copernicus" ? value : null;
        }

        /// <summary>(EA-0133) The Earth tab asks for every GLO-90 square before the world starts.</summary>
        public static bool Glo90DownloadFirst(ITreeAttribute wc) => Get(wc, Glo90DownloadKey, "needed").Trim().ToLowerInvariant() == "first";

        /// <summary>0054: wild chickens wherever the climate suits them (default) instead of their range, so the Americas and
        /// Australia have feathers for arrows until a feather bird covers them.</summary>
        public const string ChickensEverywhereKey = "earthChickensEverywhere";

        /// <summary>0046: rapids reaches where the current is strong ("steep", the default) or none ("off").</summary>
        public const string RiverRapidsKey = "earthRiverRapids";

        /// <summary>(EA-0159, decision 0095) "River width": a slider in percent, RiverWidthMinPercent to RiverWidthMaxPercent in
        /// steps of RiverWidthStepPercent; 100 % draws rivers as at 1:10, and each Earth size's play style sets its recommended
        /// value (RecommendedRiverWidthPercent). A slider in percent because the Mod DB's parser refuses the game's "multiplier"
        /// key a decimal slider needs (decision 0089).</summary>
        public const string RiverWidthKey = "earthRiverWidth";
        public const int RiverWidthMinPercent = 50, RiverWidthMaxPercent = 1000, RiverWidthStepPercent = 25;

        /// <summary>
        /// The recommended River width (%) at 1:N, each Earth size's play style's value (the author, 18 November 2026: "1:10 does
        /// not needs scaling. but others might"; measured offline, decision 0095): 100 at 1:10 and finer (2.1's widths), 200 at
        /// 1:25, 300 at 1:50, 500 at 1:100 (a river of 100 m³/s 6 blocks, the Daugava at Riga 11), 700 at 1:200; between them
        /// along the logarithm of the scale, coarser than 1:200 as √N, to the slider's step and range.
        /// </summary>
        public static int RecommendedRiverWidthPercent(double scale)
        {
            var t = RecommendedRiverWidths;
            if (!(scale > t[0].Scale)) return t[0].Percent;
            for (int i = 1; i < t.Length; i++)
                if (scale <= t[i].Scale)
                    return SnapRiverWidth(t[i - 1].Percent + Math.Log(scale / t[i - 1].Scale) / Math.Log(t[i].Scale / t[i - 1].Scale) * (t[i].Percent - t[i - 1].Percent));
            return SnapRiverWidth(t[^1].Percent * Math.Sqrt(scale / t[^1].Scale));
        }
        /// <summary>(EA-0179, decision 0098; EA-0200, decision 0098) "Smallest stream" (m³/s), a slider from 1 to 100: the smallest
        /// stream drawn. At 1:50 and coarser the smallest river drawn, as a one-block stream up to the rivers (RiverMinDischarge,
        /// 100), never under the river data's CoarseStreamMin (20). At 1:25 and finer the streams layer's smallest
        /// (EarthParams.StreamLayerMinDischarge): its streams below it are left out; the courses' own streams from 20 stay, so 20 and
        /// more leave out the whole layer. Each Earth size's play style sets RecommendedSmallestStream.</summary>
        public const string SmallestStreamKey = "earthSmallestStream";
        public const int SmallestStreamMin = 1, SmallestStreamMax = 100, SmallestStreamStep = 1;
        /// <summary>The river data's smallest river (m³/s): at 1:50 and coarser no stream is drawn below it, and at 1:25 and finer
        /// the courses' streams start there (EarthMapConfig.StreamMinDischarge), the streams layer below.</summary>
        public const int CoarseStreamMin = 20;
        /// <summary>The recommended values at 1:50 and coarser are rounded to this many m³/s.</summary>
        const int RecommendedStreamStep = 5;

        /// <summary>
        /// The recommended Smallest stream (m³/s) at 1:N. At 1:25 and finer 1: every stream of the streams layer (EA-0200). Coarser,
        /// the discharge whose real width (RiverNetwork.RealWidthMetres, 8·√Q m) is a block, (N ÷ 8)², to 5 m³/s, from the river
        /// data's 20 up to the rivers' 100 (1:100 and coarser: no streams, rivers only): 40 at 1:50, 100 at 1:100 and 1:200. The
        /// author, 18 November 2026: "What is decent river at 1:10 becomes maybe 1 block stream at 1:50 and 1:100 disappears".
        /// </summary>
        public static int RecommendedSmallestStream(double scale) =>
            scale < FineScaleMetres ? SmallestStreamMin
            : Math.Clamp((int)(Math.Round(scale / 8 * (scale / 8) / RecommendedStreamStep) * RecommendedStreamStep), CoarseStreamMin, SmallestStreamMax);

        /// <summary>The smallest stream (m³/s) a new world at 1:50 or coarser saves (EarthParams.StreamMinDischarge): the tab's value
        /// within the slider's range and never above the rivers' riverMinDischarge; a world config without one, or without a number
        /// in it, gets the recommended value for its scale.</summary>
        public static double SmallestStream(string tabValue, double ringLength, double riverMinDischarge)
        {
            // (EA-0200) values under the river data's 20 act as 20: the coarse rules draw no stream smaller
            double q = double.TryParse((tabValue ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
                ? Math.Clamp(v, CoarseStreamMin, SmallestStreamMax) : RecommendedSmallestStream(RiverNetwork.ScaleOf(ringLength));
            return riverMinDischarge > 0 ? Math.Min(q, riverMinDischarge) : q;
        }

        /// <summary>
        /// (EA-0200, decision 0098) The streams layer's smallest stream (m³/s) a new world at 1:25 or finer saves
        /// (EarthParams.StreamLayerMinDischarge): the tab's value, at most the courses' 20 (the layer's streams are all under it, so
        /// 20 leaves out the whole layer). 0, every stream of the layer as before, for 1 (the layer's own smallest, the play
        /// styles' value), less, or no number: a world made so has exactly the params and terrain of one made before.
        /// </summary>
        public static double SmallestLayerStream(string tabValue)
        {
            if (!double.TryParse((tabValue ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v)) return 0;
            return v <= SmallestStreamMin ? 0 : Math.Min(v, CoarseStreamMin);
        }

                static readonly (double Scale, int Percent)[] RecommendedRiverWidths = { (10, 100), (25, 200), (50, 300), (100, 500), (200, 700) };
        static int SnapRiverWidth(double percent) =>
            Math.Clamp((int)(Math.Round(percent / RiverWidthStepPercent) * RiverWidthStepPercent), RiverWidthMinPercent, RiverWidthMaxPercent);

        /// <summary>
        /// The river width multiplier a new world saves (EarthParams.RiverWidthMultiplier): the tab's percent over 100, within the
        /// slider's range; a world config without the key, or without a number in it, gets the recommended value for its scale.
        /// 0 where the multiplier gives exactly 1:10's floor in blocks (×1 at 1:10, ×2.5 at 1:25, ×5 at 1:50): those worlds draw 2.1's widths
        /// by its own code paths, and their params are as before.
        /// </summary>
        public static double RiverWidthMultiplier(string tabValue, double ringLength)
        {
            string value = (tabValue ?? "").Trim().TrimEnd('%').Trim();
            double pct = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
                ? Math.Clamp(v, RiverWidthMinPercent, RiverWidthMaxPercent) : RecommendedRiverWidthPercent(RiverNetwork.ScaleOf(ringLength));
            double multiplier = pct / 100;
            return Math.Abs(RiverNetwork.FloorFactorFor(multiplier, ringLength) - 1) < 1e-9 ? 0 : multiplier;
        }

        /// <summary>Vanilla's springs and mountain-side streams (GenRivulets): "caves" (default: springs in cave walls only),
        /// "streams" (as caves, plus springs at the real streams' heads and where the water table meets a slope) or "vanilla"
        /// (EarthParams.SpringRules).</summary>
        public const string MountainSpringsKey = "earthMountainSprings";

        /// <summary>The springs rules a world's choice gives: 0 vanilla, SpringRulesCaves, SpringRulesStreams. A world config
        /// without the key (hand-typed) or with another value gets the tab's default, cave springs only.</summary>
        public static int SpringRules(ITreeAttribute wc)
        {
            string value = Get(wc, MountainSpringsKey, "caves").Trim().ToLowerInvariant();
            return value == "vanilla" ? 0 : value == "streams" ? EarthParams.SpringRulesStreams : EarthParams.SpringRulesCaves;
        }

        /// <summary>(EA-0199, decision 0105) "Soil": "climate" (vanilla's rain and warmth formula with soil rule 0048's floor,
        /// as before), "vanilla" (Earth soil from the real soil map in vanilla's four tiers, barren only on inhospitable land) or
        /// "realistic" (Earth soil in five tiers, barren on the poorest soils and black earth, vanilla's soil-high, on the
        /// Chernozems), the tab's default from 2.2.0-rc.13 (the author, 21 November 2026: "Earth soil, realistic tiers - should
        /// be default"). EarthParams.Topsoil.</summary>
        public const string SoilKey = "earthSoil", SoilClimate = "climate", SoilVanillaTiers = "vanilla", SoilRealisticTiers = "realistic";

        /// <summary>The topsoil a world's choice gives (Core.Topsoil.ClimateOnly, VanillaTiers or RealisticTiers). A world
        /// config without the key or with another value gets the tab's default, realistic tiers: a world made from the menu always
        /// has the key (the game writes every attribute's default into a new world), so this is a hand-typed or older server
        /// configuration, and it gets what the menu gives.</summary>
        public static int Soil(ITreeAttribute wc)
        {
            string value = Get(wc, SoilKey, SoilRealisticTiers).Trim().ToLowerInvariant();
            return value == SoilVanillaTiers ? Core.Topsoil.VanillaTiers : value == SoilClimate ? Core.Topsoil.ClimateOnly : Core.Topsoil.RealisticTiers;
        }

        /// <summary>Vanilla's surface ponds: "off" (default: none), "wetlands" (only in the mapped wetlands, by their share) or
        /// "vanilla" (EarthParams.PondRules). Ponds in caves stay as vanilla's in every case.</summary>
        public const string SurfacePondsKey = "earthSurfacePonds";

        /// <summary>The pond rules a world's choice gives: 0 vanilla, PondRulesWetlands, PondRulesNone. A world config without
        /// the key (hand-typed) or with another value gets the tab's default, no surface ponds.</summary>
        public static int PondRules(ITreeAttribute wc)
        {
            string value = Get(wc, SurfacePondsKey, "off").Trim().ToLowerInvariant();
            return value == "vanilla" ? 0 : value == "wetlands" ? EarthParams.PondRulesWetlands : EarthParams.PondRulesNone;
        }

        /// <summary>Pre.12 saves retain their restart-config behaviour. New saves retain their creation choices.</summary>
        public static void Runtime(EarthMapConfig cfg, EarthParams p)
        {
            if (p.CreationOptionsVersion != 0) return;
            p.Weather = cfg.Weather;
            p.WeatherPace = cfg.WeatherPace;
            p.RainFrequency = cfg.RainFrequency;
            p.StormFrequency = cfg.StormFrequency;
        }

        public static VerticalTerrainOptions Terrain(ITreeAttribute wc, int mapHeight = 0) => new VerticalTerrainOptions
        {
            Budget = VerticalTerrainOptions.Choice(Get(wc, "earthVerticalBudget", VerticalTerrainOptions.DefaultBudget), VerticalTerrainOptions.DefaultBudget, VerticalTerrainOptions.Budgets),
            Curve = VerticalTerrainOptions.Choice(Curve(wc, mapHeight), VerticalTerrainOptions.DefaultCurve, VerticalTerrainOptions.Curves),
            Detail = VerticalTerrainOptions.Choice(Get(wc, "earthTerrainDetail", "normal"), "normal", "normal", "half", "off"),
            LocalEcology = Get(wc, "earthAlpineEcology", "local") != "legacy",
            PhysicalRock = Get(wc, "earthRockExposure", "legacy") == "physical",
        };

        public const string SizeKey = "earthWorldSize";
        public const string SpawnKey = "earthSpawn";
        public const string SpawnCoordinatesKey = "earthSpawnCoordinates";
        /// <summary>(2.0.1, the author: "panama and Suez can be one setting") on: both canals. Off: <see cref="SuezKey"/> and
        /// <see cref="PanamaKey"/> each, the two switches before 2.0.1, still declared but off the screen, so a dedicated
        /// server's world configuration that names them reaches a new world and a world made before keeps its own (Fable's
        /// re-review of ee0110f, F1: the game copies only declared options into a new world, and its Modify world screen
        /// writes a missing option's default into an older world).</summary>
        public const string CanalsKey = "earthCanals";
        public const string SuezKey = "earthSuezCanal";
        public const string PanamaKey = "earthPanamaCanal";

        /// <summary>The size option's value that keeps vanilla's world width and length.</summary>
        public const string VanillaSize = "vanilla";
        public const string RandomSpawn = "random";
        public const string CustomSpawn = "custom";

        /// <summary>The spawn presets, in the order of the dropdown (worldconfig.json).</summary>
        public static readonly (string code, double lat, double lon, string name)[] Capitals =
        {
            ("london", 51.507, -0.128, "London"),
            ("paris", 48.857, 2.352, "Paris"),
            ("madrid", 40.417, -3.704, "Madrid"),
            ("rome", 41.902, 12.496, "Rome"),
            ("berlin", 52.520, 13.405, "Berlin"),
            ("vienna", 48.208, 16.373, "Vienna"),
            ("warsaw", 52.230, 21.012, "Warsaw"),
            ("athens", 37.984, 23.728, "Athens"),
            ("stockholm", 59.329, 18.069, "Stockholm"),
            ("riga", 56.949, 24.105, "Riga"),
            ("kyiv", 50.450, 30.523, "Kyiv"),
            ("moscow", 55.756, 37.617, "Moscow"),
            ("rabat", 34.020, -6.841, "Rabat"),
            ("cairo", 30.044, 31.236, "Cairo"),
            ("abuja", 9.076, 7.399, "Abuja"),
            ("niamey", 13.512, 2.112, "Niamey"),
            ("addisababa", 9.030, 38.740, "Addis Ababa"),
            ("nairobi", -1.286, 36.817, "Nairobi"),
            ("pretoria", -25.747, 28.229, "Pretoria"),
            ("baghdad", 33.315, 44.366, "Baghdad"),
            ("tehran", 35.689, 51.389, "Tehran"),
            ("newdelhi", 28.614, 77.209, "New Delhi"),
            ("bangkok", 13.756, 100.502, "Bangkok"),
            ("beijing", 39.904, 116.407, "Beijing"),
            ("seoul", 37.567, 126.978, "Seoul"),
            ("tokyo", 35.676, 139.650, "Tokyo"),
            ("jakarta", -6.208, 106.846, "Jakarta"),
            ("canberra", -35.281, 149.130, "Canberra"),
            ("wellington", -41.286, 174.776, "Wellington"),
            ("ottawa", 45.421, -75.697, "Ottawa"),
            ("washington", 38.907, -77.037, "Washington"),
            ("mexicocity", 19.433, -99.133, "Mexico City"),
            ("lima", -12.046, -77.043, "Lima"),
            ("brasilia", -15.794, -47.882, "Brasília"),
            ("buenosaires", -34.604, -58.382, "Buenos Aires"),
            ("santiago", -33.449, -70.669, "Santiago"),
        };

        static string Get(ITreeAttribute wc, string key, string fallback) => wc?.GetAsString(key, fallback) ?? fallback;

        /// <summary>
        /// For a new world: writes the size option into the world's worldWidth / worldLength and the save's map size. The
        /// game reads worldWidth / worldLength into the save before mods start and builds the map from the save after their
        /// start phase, so this runs in StartServerSide. Returns a line for the log.
        /// </summary>
        public static string ApplySize(ICoreServerAPI sapi)
        {
            var save = sapi.WorldManager.SaveGame;
            var wc = save.WorldConfiguration;
            string value = Get(wc, SizeKey, SpawnSearch.DefaultScale.ToString(CultureInfo.InvariantCulture)).Trim();
            if (value.Equals(VanillaSize, StringComparison.OrdinalIgnoreCase))
                return string.Format("Earth size: the World generation tab's {0} × {1}.", Get(wc, "worldWidth", "?"), Get(wc, "worldLength", "?"));
            SpawnSearch.SizeFor(value, out int width, out int length, out bool bad);
            var t = save.GetType();
            var fx = t.GetField("MapSizeX", BindingFlags.Public | BindingFlags.Instance);
            var fz = t.GetField("MapSizeZ", BindingFlags.Public | BindingFlags.Instance);
            if (fx == null || fz == null || fx.FieldType != typeof(int) || fz.FieldType != typeof(int))
                throw new MissingFieldException(t.FullName, "MapSizeX/MapSizeZ");
            fx.SetValue(save, width);
            fz.SetValue(save, length);
            wc.SetString("worldWidth", width.ToString(CultureInfo.InvariantCulture));
            wc.SetString("worldLength", length.ToString(CultureInfo.InvariantCulture));
            return (bad ? string.Format("Earth size '{0}' is not a scale; using 1:{1}. ", value, SpawnSearch.DefaultScale) : "") +
                string.Format(CultureInfo.InvariantCulture, "Earth size: 1:{0:0.#}, world {1} × {2} blocks (Cylinder World's ring {3}, {4} blocks of seam ocean beyond each side).",
                    SpawnSearch.ScaleOf(length), width, length, 2 * length, SpawnSearch.SeamMargin);
        }

        /// <summary>The canals switched on in this world, as stored in its EarthParams.Canals: both where <see cref="CanalsKey"/>
        /// is on, else each from the two keys before 2.0.1 (off in a new world unless a server's configuration sets them; an
        /// existing world keeps its own, so UpgradeExistingWorldCanals reads them as before).</summary>
        public static string CanalCodes(ITreeAttribute wc)
        {
            if (wc?.GetBool(CanalsKey, false) == true) return "suez,panama";
            bool suez = wc?.GetBool(SuezKey, false) == true, panama = wc?.GetBool(PanamaKey, false) == true;
            return suez && panama ? "suez,panama" : suez ? "suez" : panama ? "panama" : "";
        }

        public static string Spawn(ITreeAttribute wc) => Get(wc, SpawnKey, RandomSpawn).Trim().ToLowerInvariant();

        public static string SpawnCoordinates(ITreeAttribute wc) => Get(wc, SpawnCoordinatesKey, "");
    }
}
