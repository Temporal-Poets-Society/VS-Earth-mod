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

        /// <summary>Decision 0075: turns a new world's biome rules back to vanilla (the rules Freeze's caller, ComputeParams,
        /// saved). Terrain, water, coasts, rocks, soil and ores keep Earth's rules.</summary>
        public static void FreezeVanillaBiomes(EarthParams p)
        {
            p.VanillaBiomes = EarthParams.VanillaBiomesVersion;
            p.ZoneRules = 0;
            p.FireRules = 0;
            p.WetlandRules = 0;
            p.GroundCoverRules = 0;
            p.MoistureRules = 0;
            p.GrassWinterRules = 0;
            p.AlpineSurfaceRules = 0;
        }

        public static bool RegionalAnimals(ITreeAttribute wc, bool fallback)
            => Choice(wc, "earthAnimals", "regional", "vanilla", fallback);

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

        /// <summary>Creation-only choices. Existing saves never pass through this method.</summary>
        public static void Freeze(ITreeAttribute wc, EarthMapConfig cfg, EarthParams p, EarthData data = null)
        {
            p.CreationOptionsVersion = 1;
            p.SummitMinElevation = SummitMinElevation(wc);
            // Two Earth-tab choices (the author, 7 November). "Rocks and ground": ground columns (the default: regional rock
            // regions, plus real ground columns where the ground data reaches), regional (rock regions only) or vanilla.
            // "Ores": vanilla (the default for now) or Earth's ore maps. Worlds set up with the earlier single "Rocks and
            // ores" key (earthOres) keep its meaning: vanilla is vanilla rocks and ores, regional and ground columns are
            // that rock choice with the ore maps. Without any key the server config decides, as before.
            string legacy = Get(wc, OresKey, "").Trim().ToLowerInvariant();
            bool legacyKnown = legacy is OresRegional or OresGroundColumns or OresVanilla;
            string rocks = Get(wc, RocksKey, "").Trim().ToLowerInvariant();
            if (!(rocks is OresRegional or OresGroundColumns or OresVanilla) && legacyKnown) rocks = legacy;
            string deposits = Get(wc, OreDepositsKey, "").Trim().ToLowerInvariant();
            if (!(deposits is OreDepositsVanilla or OreDepositsMaps) && legacyKnown) deposits = legacy == OresVanilla ? OreDepositsVanilla : OreDepositsMaps;
            bool rocksKnown = rocks is OresRegional or OresGroundColumns or OresVanilla;
            bool ores = deposits is OreDepositsVanilla or OreDepositsMaps ? deposits == OreDepositsMaps : cfg.RegionalOres;
            p.RegionalOres = ores;
            p.RegionalGeology = rocksKnown ? rocks != OresVanilla : cfg.RegionalGeology;
            p.OreRulesVersion = ores ? RegionalOreCatalog.Version : 0;
            p.GeologyRulesVersion = p.RegionalGeology ? RegionalGeologyCatalog.RulesVersion : 0;
            bool ground = rocksKnown ? rocks == OresGroundColumns : cfg.GroundColumnsPrototype;
            p.GroundColumns = ground && p.RegionalGeology ? GroundColumns.RulesVersion : 0;
            p.OreLayers = p.GroundColumns > 0 ? EarthMap.Core.OreLayers.RulesVersion : 0;
            // 0065: the ore maps in a ground-column world use ore rules v4 (catalogue bodies in their layers)
            p.OreRules = ores && p.OreLayers > 0 ? OreBackground.CurrentRules : 0;
            p.DepositSet = EarthDepositSet.Version;
            string depth = Get(wc, GroundDepthKey, "").Trim().ToLowerInvariant();
            p.GroundDepth = p.GroundColumns > 0 ? (Array.IndexOf(GroundColumns.DepthCurve.Choices, depth) >= 0 ? depth : GroundColumns.DepthCurve.Default) : null;
            bool nature = Nature1318(wc, cfg.RegionalFauna);
            p.RegionalFauna = nature;
            p.PlacementRules = nature ? PlacementCatalogue.RulesVersion : 0;
            if (!nature && wc?.HasAttribute(NatureKey) == true)
            {
                // Vanilla by climate alone: no regional tree allocation either.
                p.RegionalTrees = false;
                p.TreeRulesVersion = 0;
            }
            // decision 0075: "Biomes" on its own; either Nature goes with either Biomes
            if (VanillaBiomes(wc)) FreezeVanillaBiomes(p);
            else p.VanillaBiomes = 0;
            p.FaunaRulesVersion = p.RegionalFauna ? RegionalFaunaCatalog.RulesVersion : 0;
            p.VanillaFaunaRanges = p.RegionalFauna ? VanillaFaunaRanges.Version : 0;
            p.IncludeExtinctAnimals = wc?.GetBool("earthIncludeExtinctAnimals", cfg.IncludeExtinctAnimals) ?? cfg.IncludeExtinctAnimals;
            p.ChickensByRange = !(wc?.GetBool(ChickensEverywhereKey, true) ?? true);
            // Decision 0054: worlds placed as around 1318 get animal ranges as around 1318 unless earth.json asks for "current".
            p.FaunaRangeProfile = nature && cfg.FaunaRangeProfile != RegionalFaunaCatalog.CurrentProfile ? RegionalFaunaCatalog.Profile1318 : cfg.FaunaRangeProfile;
            p.Weather = Choice(wc, "earthWeather", "earth", "vanilla", cfg.Weather);
            p.WeatherModelRules = WeatherSettings.Newest;
            p.WeatherPace = cfg.WeatherPace;
            p.RainFrequency = Frequency(wc, "earthRainFrequency", cfg.RainFrequency, 3);
            p.StormFrequency = Frequency(wc, "earthStormFrequency", cfg.StormFrequency, 5);
            p.NoRiverRapids = !Choice(wc, RiverRapidsKey, "steep", "off", true);
            p.SpringRules = SpringRules(wc);
            p.PondRules = PondRules(wc);
            string elevation = Elevation(wc, data);
            p.FineElevation = elevation == "glo90" ? FineElevation.Version : 0;
            // the included GLO-90 heights: the land heights of "glo90coarse", and where full GLO-90 can't be downloaded
            p.CoarseElevation = elevation != "builtin" && data?.Coarse != null ? CoarseElevation.Version : 0;
            // rivers on GLO-90 with either GLO-90 choice (their valleys are in the land then)
            // (2: with the reaches the water atlas moved, 3: and the side arms, where the installed data allows; 0053 items 14, 16)
            p.RiverCourses = elevation != "builtin" && data != null ? data.Glo90RiverCourses : 0;
            // the water mask's lakes from their outlines with full GLO-90 (their levels are its heights'; 0053 item 17)
            p.LakeOutlines = elevation == "glo90" ? LakeOutlineData.FileVersion : 0;
            // modern dams taken out (no dams in 1318: 0053 item 19, proposed), on the courses the dam file was made for
            // (rules 2, 0063: on courses rules 4; rules 1 on courses rules 3)
            p.DamRules = elevation != "glo90" ? 0
                : p.RiverCourses == DamRemovalData.CourseRules2 ? DamRemovalData.NewestRules
                : p.RiverCourses == DamRemovalData.CourseRules ? DamRemovalData.RulesVersion : 0;
            // and its rivers, at the river courses' levels (0053 item 18), through the old reservoirs with the dams out: the
            // outlines baked with that dam file's corridors (dam rules 2: version 3, 0063; rules 1: version 2)
            // (dam rules 3, decision 0069: version 4, on its reservoirs and corridors)
            p.RiverOutlines = elevation == "glo90" ? (p.DamRules >= DamRemovalData.Rules3 ? 4 : p.DamRules >= DamRemovalData.Rules2 ? 3 : p.DamRules >= 1 ? 2 : 1) : 0;
            // the channel rules with them (0063 channel rules 1, "dredging": the course's channel everywhere, at the flow's width
            // or the river's measured one where narrower)
            p.ChannelRules = p.RiverOutlines >= 1 && p.RiverCourses >= 1 ? TerrainModel.ChannelRulesVersion : 0;
            // decision 0069: the flow solved on river outlines 4's water, with its rerouted mouths (on courses rules 4's list)
            p.ShoreZoneRules = Core.ShoreZones.RulesVersion;
            // decision 0070: the shore strip of the coast's own material
            p.ShoreRules = Core.ShoreMaterial.RulesVersion;
            p.FlowRules = p.RiverOutlines >= 4 && p.ChannelRules >= 1 && p.RiverCourses == DamRemovalData.CourseRules2 ? RiverFlowData.FileVersion : 0;
            p.DetailRules = TerrainModel.DetailRulesVersion;
            // the code review of 11 November 2026: dry land below 0 m, springs at whole blocks, crops in the river belt, dry
            // river beds' gravel (TerrainModel.GenFixRules)
            p.GenFixRules = TerrainModel.GenFixRulesVersion;
            p.RockFloorDepth = RockFloorDepth(wc, p.MapHeight);
        }

        /// <summary>"Rocks and ground": OresGroundColumns (default), OresRegional or OresVanilla.</summary>
        public const string RocksKey = "earthRocks";
        /// <summary>"Ores": OreDepositsVanilla (default for now) or OreDepositsMaps.</summary>
        public const string OreDepositsKey = "earthOreDeposits", OreDepositsVanilla = "vanilla", OreDepositsMaps = "maps";
        /// <summary>The earlier single "Rocks and ores" key, read only where the two keys above are missing; its values
        /// are also the rock choices.</summary>
        public const string OresKey = "earthOres", OresRegional = "regional", OresGroundColumns = "groundcolumns", OresVanilla = "vanilla";
        /// <summary>Ground depth for ground columns: GroundColumns.DepthCurve.Choices, "log4000" by default.</summary>
        public const string GroundDepthKey = "earthGroundDepth";
        /// <summary>The Earth tab's world height. 0 keeps the World generation tab's, whose slider stops at 512 outside
        /// creative building.</summary>
        public const string HeightKey = "earthWorldHeight";
        public const int MinHeight = 256, MaxHeight = 1024;
        /// <summary>The slider's former "Automatic (for the Earth size)" (0053): a value no world height can have. The slider
        /// skips it now (worldconfig.json); a world config that holds it (made before, or typed by hand) still resolves.</summary>
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

        /// <summary>0051/0053 (proposed): land heights from Copernicus GLO-90, full ("glo90") or included ("glo90coarse"), or the
        /// built-in grid ("builtin"); "auto" (the default) picks by the Earth size (Elevation).</summary>
        public const string ElevationKey = "earthElevation";

        /// <summary>
        /// 0051: the Earth tab's GLO-90 land heights: "release" (the Earth tab's default) or "copernicus" (download all when
        /// the new world opens, from Earth's data release or from Copernicus's originals), or "needed" (squares as the world
        /// reaches them). A world without the key (made before the row existed) downloads as needed: opening an older world
        /// never starts the full download. The world creation screen runs no mod code, so the download can only start once
        /// the world does.
        /// </summary>
        public const string Glo90DownloadKey = "earthGlo90Download";

        /// <summary>"release" or "copernicus" when the Earth tab asks to download all GLO-90 squares, else null.</summary>
        public static string Glo90Download(ITreeAttribute wc)
        {
            string value = Get(wc, Glo90DownloadKey, "needed").Trim().ToLowerInvariant();
            return value == "release" || value == "copernicus" ? value : null;
        }

        /// <summary>0054: wild chickens wherever the climate suits them (default) instead of their range, so the Americas and
        /// Australia have feathers for arrows until a feather bird covers them.</summary>
        public const string ChickensEverywhereKey = "earthChickensEverywhere";

        /// <summary>0046: rapids reaches where the current is strong ("steep", the default) or none ("off").</summary>
        public const string RiverRapidsKey = "earthRiverRapids";

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

        /// <summary>The canals switched on in this world, as stored in its EarthParams.Canals.</summary>
        public static string CanalCodes(ITreeAttribute wc)
        {
            bool suez = wc?.GetBool(SuezKey, false) == true, panama = wc?.GetBool(PanamaKey, false) == true;
            return suez && panama ? "suez,panama" : suez ? "suez" : panama ? "panama" : "";
        }

        public static string Spawn(ITreeAttribute wc) => Get(wc, SpawnKey, RandomSpawn).Trim().ToLowerInvariant();

        public static string SpawnCoordinates(ITreeAttribute wc) => Get(wc, SpawnCoordinatesKey, "");
    }
}
