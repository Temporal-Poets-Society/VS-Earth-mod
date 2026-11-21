using System;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// The rule data a world's saved rules read beside the terrain model, loaded once per server start (EarthWorldGen's
    /// InitWorldGen): first the model's own (zones, soil sites, wetlands, floodplains, fire, ponds), set on the model, then the
    /// data the passes and guards read from here. A missing or unreadable file is logged with what the world goes without and
    /// never thrown: the world still generates, without that rule's data.
    /// </summary>
    sealed class WorldGenRules
    {
        /// <summary>Soil rules (decision 0048), as saved.</summary>
        public int SoilRules { get; private set; }
        /// <summary>(Decision 0105) Earth soil, in worlds that chose it (EarthParams.Topsoil), else null; and its tier set.</summary>
        public Topsoil Topsoil { get; private set; }
        public int TopsoilTiers { get; private set; }
        /// <summary>The soil blocks of Earth soil: [tier, grass] (Topsoil.BlockCodes x Core.Topsoil.GrassCodes), and each soil block's
        /// tier and grass (-1 for other blocks); null without Earth soil.</summary>
        public int[,] TopsoilBlocks { get; private set; }
        public sbyte[] SoilTierOf { get; private set; }
        public sbyte[] SoilGrassOf { get; private set; }
        /// <summary>Habitats step 1: the vegetation zones in worlds saved with zone rules, else null.</summary>
        public ClimateZones Zones { get; private set; }
        /// <summary>Ranges step 5a: dry land keeps a share of vanilla's tall grass (Core.GroundCover, ground-cover.json).</summary>
        public int GroundCoverRules { get; private set; }
        public GroundCover GroundCover { get; private set; }
        /// <summary>Alpine surface rules 2: the ice sheets' ground (MountainEcology.IceSheetAsset); null without it.</summary>
        public GroundData IceSheets { get; private set; }
        /// <summary>Where people lived around 1318 (people-1318.json), or null when the file is missing or unreadable.</summary>
        public PeopleRange People { get; private set; }
        /// <summary>The vanilla land animals' ranges and altitude limits (fauna-vanilla.json), or null when the file is missing
        /// or unreadable.</summary>
        public VanillaFaunaRanges FaunaRanges { get; private set; }

        readonly ICoreServerAPI sapi;
        readonly ILogger logger;

        WorldGenRules(ICoreServerAPI sapi, ILogger logger)
        {
            this.sapi = sapi;
            this.logger = logger;
        }

        /// <summary>The soil rules only (InitWorldGen reads them first, before the rest of the rule data).</summary>
        public static WorldGenRules Begin(ICoreServerAPI sapi, ILogger logger, EarthParams p)
        {
            var rules = new WorldGenRules(sapi, logger) { SoilRules = p.SoilRules };
            // (decision 0105) Earth soil says its own line when its rules are loaded (LoadTopsoil)
            if (rules.SoilRules >= 1 && BlockLayerAdjustments.SoilFertilityPatched && !Topsoil.On(p.Topsoil))
                logger.Notification("Earth soil rules v{0}: land with a real aridity index of at least {1} has soil (block-layer fertility at least {2}/255); deserts keep sand and gravel.",
                    rules.SoilRules, SoilFertility.DryLandAridity, SoilFertility.Floor);
            return rules;
        }

        /// <summary>
        /// A rule asset of this mod, parsed; null when it is missing or can't be read, with an error saying what the world
        /// goes without (<paramref name="without"/>). Never throws: the world still generates, without that rule's data.
        /// </summary>
        T LoadAsset<T>(string path, System.Func<IAsset, T> parse, string name, string without) where T : class
        {
            try
            {
                var asset = sapi.Assets.TryGet(new AssetLocation(path));
                if (asset != null) return parse(asset);
                logger.Error("Earth Map: {0} is missing; {1}. Reinstall the mod.", name, without);
                Missing.Add(name + " (missing: " + without + ")");
            }
            catch (Exception e)
            {
                logger.Error("Earth Map: {0} can't be read ({1}); {2}.", name, e.Message, without);
                Missing.Add(name + " (unreadable: " + without + ")");
            }
            return null;
        }

        /// <summary>(Decision 0083) The rule data this world's rules read that is missing or unreadable this session: the
        /// world is refused before its first chunk (EarthWorldGen.CheckFeatures) unless earth.json accepts it.</summary>
        public System.Collections.Generic.List<string> Missing { get; } = new();

        /// <summary>
        /// A binary data grid at <paramref name="path"/>, parsed: the mod's asset if it carries one, else the file of that name
        /// in the server's data store (decision 0066: the soil sites, wetlands, floodplains and the ice sheets' ground left
        /// the mod's zip). Null, with an error saying what the world goes without, when neither has it or it can't be read.
        /// </summary>
        T LoadGrid<T>(string path, System.Func<byte[], T> parse, string name, string without) where T : class
        {
            try
            {
                byte[] bytes = sapi.Assets.TryGet(new AssetLocation(path))?.Data
                    ?? EarthDataStore.Bytes(EarthGround.DataFolder, System.IO.Path.GetFileName(new AssetLocation(path).Path));
                if (bytes != null) return parse(bytes);
                logger.Error("Earth Map: {0} is missing from the mod and from {1}; {2}.", name, EarthDataStore.Folder(EarthGround.DataFolder), without);
                Missing.Add(name + " (missing: " + without + ")");
            }
            catch (Exception e)
            {
                logger.Error("Earth Map: {0} can't be read ({1}); {2}.", name, e.Message, without);
                Missing.Add(name + " (unreadable: " + without + ")");
            }
            return null;
        }

        /// <summary>The data the world's saved rules read beside the model (the model's own rule data first, then the guards').</summary>
        public void Load(TerrainModel model, EarthParams p)
        {
            Zones = null;
            model.Zones = null;
            model.NoTreeFloor = false;
            if (p.VanillaBiomes >= 1)
                logger.Notification("Earth biomes: vanilla (world setting, decision 0075): vanilla's forest and shrub maps and tall grass; no vegetation zones, fire, open wetlands, ground moisture, grass in winter or alpine turf and scree.");
            if (ZoneFacts.Zones(p.ZoneRules))
            {
                Zones = LoadAsset("earth:config/zones.json", a => new ClimateZones(a.ToText(), p.AridityRules >= 1), "zones.json", "forests and shrubs follow the rain alone");
                if (Zones != null)
                {
                    model.Zones = Zones;
                    // zone rules 2: trees as dense as the forest map says, without vanilla's floor (EarthTrees)
                    model.NoTreeFloor = ZoneFacts.NoTreeFloor(p.ZoneRules);
                    logger.Notification("Earth zone rules v{0}: {1} vegetation zones from climate set the forest and shrub density{2}; aridity index with {3} evapotranspiration.", p.ZoneRules, Zones.Count,
                        model.NoTreeFloor ? ", trees without vanilla's floor" : "", model.Data.PenmanAridity ? "Penman-Monteith" : "Holdridge");
                }
            }
            model.Fire = null;
            model.Soils = LoadGrid(SoilSiteGrid.Asset, SoilSiteGrid.Read, "the soil site grid (" + SoilSiteGrid.Asset + ")",
                "trees ignore the soil and the prairie and steppe keep the zones' forest");
            model.Wetlands = LoadGrid(WetlandGrid.Asset, WetlandGrid.Read, "the wetland grid (" + WetlandGrid.Asset + ")",
                "trees ignore them and bogs and marshes keep the zones' forest");
            model.Floodplains = null;
            // ground columns (EarthGeology.GroundAt) and Earth soil (decision 0105, BlockLayerAdjustments) read it
            if (p.SoilRules >= SoilFertility.FloodplainRules && (GroundColumnFacts.Columns(p.GroundColumns) || Topsoil.On(p.Topsoil)))
                model.Floodplains = LoadGrid(FloodplainGrid.Asset, FloodplainGrid.Read, "the floodplain grid (" + FloodplainGrid.Asset + ")",
                    "arid floodplains outside the river belts have no topsoil");
            LoadTopsoil(model, p);
            model.OpenWetlands = p.WetlandRules >= 1 && model.Zones != null && model.Wetlands != null;
            if (model.OpenWetlands)
                logger.Notification("Earth wetland rules v{0}: open bog, marsh and saltmarsh (GLWD v2) stand without forest.", p.WetlandRules);
            model.PondRules = p.PondRules;
            if (p.PondRules >= EarthParams.PondRulesNone)
                logger.Notification("Earth pond rules v{0}: no vanilla surface ponds (surface ponds off); ponds in caves as vanilla.", p.PondRules);
            else if (p.PondRules >= 1)
                logger.Notification("Earth pond rules v{0}: vanilla's surface ponds only in the mapped wetlands (GLWD v2){1}; ponds in caves as vanilla.",
                    p.PondRules, model.Wetlands == null ? " (the wetlands are missing, so none)" : "");
            if (p.SpringRules >= EarthParams.SpringRulesCaves)
                logger.Notification("Earth springs rules v{0}: vanilla's water springs in cave walls only, none in open cliff faces{1}; no vanilla streams " +
                    "breaking out of mountain sides.", p.SpringRules, p.SpringRules == EarthParams.SpringRulesStreams ? "; springs at real stream heads and seeps (EarthSprings)" : "");
            if (p.FireRules >= 1 && model.Zones != null && model.Soils != null)
            {
                model.Fire = new FireRegime(model.Soils, Math.Min(p.FireRules, FireRegime.RulesVersion));
                logger.Notification("Earth fire rules v{0}: grassland soils with hot, dry summers stay open (prairie, parkland, Pampas, forest-steppe){1}.", p.FireRules,
                    model.Fire.Rules >= 2 ? "; the surest grassland keeps no groves (steppe, Pampas, tallgrass prairie)" : "");
            }
            IceSheets = null;
            if (AlpineSurfaceFacts.IceSheetGround(model.AlpineSurfaceRules))
            {
                IceSheets = LoadGrid(MountainEcology.IceSheetAsset, MountainEcology.ReadIceSheets, MountainEcology.IceSheetAsset,
                    "mountains the last ice sheets covered (Scandinavia, Scotland, the Canadian Rockies) get the alpine turf of the others");
                logger.Notification("Earth alpine surface rules v{0}: closed turf from the tree line to a warmest month of about 6 °C, scree towards the snow line and in dry mountains; on the last ice sheets' ground turf closes only near the tree line.",
                    model.AlpineSurfaceRules);
            }
            if (model.Rules.ShoreStrip)
                logger.Notification("Earth shore rules v{0}: the sea's and big lakes' shores are a beach strip at any height (sand {1:0} m, gravel {2:0} m, rock {3:0} m), of OSM's material {4}, else the coast's ground (dunes sand, till gravel and stones, bedrock and cliffs rock, marshes mud); no grass or land plants on it.",
                    model.ShoreRules, ShoreMaterial.WidthMetres(ShoreMaterial.Sand), ShoreMaterial.WidthMetres(ShoreMaterial.Gravel), ShoreMaterial.WidthMetres(ShoreMaterial.Rock),
                    model.ShoreData != null ? $"({model.ShoreData.Squares} squares of beaches)" : "(none: shores-osm-1.bin missing)");
            GroundCoverRules = p.GroundCoverRules;
            GroundCover = null;
            if (GroundCoverRules >= 1)
            {
                GroundCover = LoadAsset("earth:config/ground-cover.json", a => new GroundCover(a.ToText()), "ground-cover.json", "dry land keeps vanilla's tall grass density");
                if (GroundCover != null && BlockLayerAdjustments.TallGrassPatched)
                    logger.Notification("Earth ground cover rules v{0}: dry land keeps a share of vanilla's tall grass by the real aridity index ({1:0}% at 0.2, {2:0}% at 0.5).",
                        GroundCoverRules, 100 * GroundCover.TallGrassKept(.2), 100 * GroundCover.TallGrassKept(.5));
            }
            People = LoadAsset("earth:config/people-1318.json", a => new PeopleRange(a.ToText(), BotanicalRegions.Shared(
                    (sapi.Assets.TryGet(new AssetLocation("earth:config/treeregions.json")) ?? throw new System.IO.FileNotFoundException("treeregions.json is missing")).ToText())),
                "people-1318.json", "vanilla ruins, trader camps and villages are placed anywhere");
            // Ranges (decision 0031) and, in real-altitude worlds, altitude limits: both are fauna-vanilla.json's data.
            if (p.VanillaFaunaRanges >= 1 || model.LocalAlpineEcology)
            {
                FaunaRanges = LoadAsset("earth:config/fauna-vanilla.json", a =>
                {
                    // The region index: ranges given as regions (butterflies: everywhere but Antarctica) and, in worlds placed
                    // as around 1318 (decision 0054), the 1318 ranges (losses, feral domestic animals).
                    var regions = sapi.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"));
                    if (regions == null) logger.Error("Earth Map: treeregions.json is missing; butterflies are not limited by range and vanilla animals keep their natural ranges, not those of 1318.");
                    return new VanillaFaunaRanges(a.ToText(), regions == null ? null : BotanicalRegions.Shared(regions.ToText()),
                        !p.ChickensByRange, use1318: p.PlacementRules >= 1);
                }, "fauna-vanilla.json", "vanilla animals spawn outside their natural ranges and above their altitude limits");
            }
        }
    
        /// <summary>
        /// (Decision 0105) Earth soil: soil-rules-v4.json and the soil maps it lists (the world's 30″ set where it fetched it when
        /// it was made, EarthParams.GroundData, else the 2′ maps of the data store), and the soil blocks' table. A world whose
        /// rules or maps can't be read is refused before its first chunk (Missing), as for any other rule data.
        /// </summary>
        void LoadTopsoil(TerrainModel model, EarthParams p)
        {
            Topsoil = null; TopsoilBlocks = null; SoilTierOf = null; SoilGrassOf = null;
            TopsoilTiers = p.Topsoil;
            if (!Topsoil.On(p.Topsoil)) return;
            string without = "the world's soil keeps vanilla's rain and warmth formula";
            bool thirty = p.GroundData == GroundDownloader.SetsVersion;
            var soil = LoadAsset(Core.Topsoil.RulesAsset, a => Core.Topsoil.Load(a.ToText(),
                asset => sapi.Assets.TryGet(new AssetLocation(asset))?.Data ?? EarthDataStore.Bytes(EarthGround.DataFolder, System.IO.Path.GetFileName(new AssetLocation(asset).Path)),
                p.NoiseSeed, file => thirty ? System.IO.Path.Combine(EarthGround.DataFolder, file) : null), "the soil rules (soil-rules-v4.json)", without);
            if (soil == null) return;
            if (thirty && soil.MissingTiles.Count > 0)
            {
                logger.Error("Earth soil: this world was made with the 30\" soil map, but {0} is missing from {1}; {2}.", string.Join(", ", soil.MissingTiles),
                    System.IO.Path.Combine(EarthGround.DataFolder, GroundDownloader.Subfolder), "its soil would follow the 2' map");
                Missing.Add("the 30\" soil map (missing: its soil would follow the 2' map)");
                return;
            }
            soil.UnknownClass += name => logger.Warning("Earth soil: no rule for soil class {0}; the default tier is used.", name);
            int Id(string code) { try { return sapi.World.GetBlock(new AssetLocation(code))?.Id ?? 0; } catch (Exception) { return 0; } }
            var blocks = new int[Core.Topsoil.BlockCodes.Length, Core.Topsoil.GrassCodes.Length];
            int count = sapi.World.Blocks.Count;
            var tierOf = new sbyte[count]; var grassOf = new sbyte[count];
            Array.Fill(tierOf, (sbyte)-1); Array.Fill(grassOf, (sbyte)-1);
            for (int f = 0; f < Core.Topsoil.BlockCodes.Length; f++)
                for (int g = 0; g < Core.Topsoil.GrassCodes.Length; g++)
                {
                    string code = "game:soil-" + Core.Topsoil.BlockCodes[f] + "-" + Core.Topsoil.GrassCodes[g];
                    int id = Id(code);
                    if (id <= 0)
                    {
                        logger.Error("Earth soil: {0} does not exist; {1}.", code, without);
                        Missing.Add("the soil block " + code + " (missing: " + without + ")");
                        return;
                    }
                    blocks[f, g] = id;
                    if (id < count) { tierOf[id] = (sbyte)f; grassOf[id] = (sbyte)g; }
                }
            Topsoil = soil; TopsoilBlocks = blocks; SoilTierOf = tierOf; SoilGrassOf = grassOf;
            logger.Notification("Earth soil rules v{0} ({1}): topsoil from the real soil map ({2}), {3}; deserts have none except in a river's flood (the floodplain grid{4}) and at oases.",
                p.SoilRules, p.Topsoil == Core.Topsoil.RealisticTiers ? "realistic tiers: barren to black earth" : "vanilla tiers: barren to high",
                thirty ? "SoilGrids 30\"" : "SoilGrids 2'", p.Topsoil == Core.Topsoil.RealisticTiers ? "black earth (Terra preta) on the Chernozems" : "high on the black earths and river silt",
                model.Floodplains != null ? "" : ", missing");
        }
}
}
