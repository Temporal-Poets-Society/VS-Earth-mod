using System;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace EarthMap
{
    /// <summary>
    /// What a new Earth world's params are built from, as the game made the world (EA-0089, CR-A5 of the 14 November 2026
    /// code review): EarthMapSystem reads it from the game and Cylinder World; tests/EarthWorldOptionsTests sets it by hand.
    /// </summary>
    sealed class NewWorld
    {
        public int MapSizeX, MapSizeY, SeaLevel, Seed;
        /// <summary>The world's configuration: the play style's settings and the Earth tab's choices.</summary>
        public ITreeAttribute WorldConfig;
        /// <summary>The river flow query version the installed Hydrology reads (RequiredMods.QueryVersion).</summary>
        public int HydrologyQuery;
        /// <summary>Cylinder World's ring (CylinderCompat).</summary>
        public double CwJumpInset, CwCircumference, CwSeamHalfWidth, CwEquatorZ, CwPoleDistance;
        public int CwSeabedY;
        /// <summary>Whether Cylinder World keeps Earth's own terrain in its seam band (EarthMapSystem.CylinderKeepsEarthSeam):
        /// asked only when earth.json doesn't ask for the gutter, as its answer is logged.</summary>
        public System.Func<bool> CylinderKeepsEarthSeam;
    }

    /// <summary>
    /// A new Earth world's params, off the game API (EA-0089, CR-A5 of the 14 November 2026 code review): one builder, every
    /// field written once, each choice in one order: the Earth tab (EarthWorldOptions.ReadTab), else earth.json, else the
    /// default. Before, EarthMapSystem.ComputeParams wrote them from earth.json and EarthWorldOptions.Freeze wrote the tab's
    /// over eleven of them. tests/EarthWorldOptionsTests pins 280 new worlds' params (new-world-params.txt), made by the two
    /// before this builder replaced them.
    /// </summary>
    static class WorldParamsBuilder
    {
        const string ConfigFile = EarthConfigStore.FileName;

        /// <summary>The coast outlines (EarthParams.Coasts) a world with this geometry gets by the config: none when they are off,
        /// without the islands, or where the grid's cells are under CoastMinCellBlocks wide.</summary>
        public static int Coasts(EarthMapConfig cfg, EarthGeometry geo, bool islands) =>
            cfg.Coasts && islands && CoastLayer.CellBlocks(geo) >= cfg.CoastMinCellBlocks ? CoastData.FileVersion : 0;

        /// <summary>
        /// The Earth's layout on Cylinder World's ring: the real seam where Cylinder World keeps Earth's terrain in its seam
        /// band and earth.json doesn't ask for the gutter (decision 0030), else the gutter of open sea; <paramref name="gutterSeabed"/>
        /// is EarthParams.GutterSeabed's rules for it. A new world's (Compute), and what EarthMapSystem.RepairSaved lays out again
        /// for a saved world whose layout can't place the Earth (EA-0089 B: the layout alone, no data and nothing else of a new
        /// world's).
        /// </summary>
        public static EarthGeometry Layout(NewWorld w, EarthMapConfig cfg, ILogger log, out int gutterSeabed, out string report)
        {
            bool realSeam = !cfg.SeamGutter && w.CylinderKeepsEarthSeam();
            var inp = new LayoutInput
            {
                WorldWidth = w.MapSizeX,
                GutterMargin = cfg.GutterMargin,
                Gutter = !realSeam,
                CwRingStart = w.CwJumpInset,
                CwRingLength = w.CwCircumference,
                CwBandInner = w.CwSeamHalfWidth - w.CwJumpInset,
                CwEquatorZ = w.CwEquatorZ,
                CwPoleDistance = w.CwPoleDistance,
            };
            string badLayout = EarthLayout.Sanitize(inp);
            if (badLayout.Length > 0) log.Warning("{0}: {1} can't place the Earth; this world uses the defaults for them (it saves the layout, so fix the config before creating the next one).", ConfigFile, badLayout);
            gutterSeabed = realSeam ? TerrainModel.RealSeamVersion : 2;
            return EarthLayout.Build(inp, out report);
        }

        /// <summary>A new world's params, as ResolveParams stores them. <paramref name="badSettings"/>: earth.json's settings
        /// that can't make terrain, set back to the defaults (EarthParams.RepairSettings); empty when all were fine.</summary>
        public static EarthParams Build(NewWorld w, EarthMapConfig cfg, EarthData data, ILogger log, out string report, out string badSettings)
        {
            var wc = w.WorldConfig;
            int H = w.MapSizeY;
            int sea = w.SeaLevel;
            // ------------------------------------------------------------------ the layout and the heights (earth.json, the tab)
            var geo = Layout(w, cfg, log, out int gutterSeabedRules, out report);
            double landCurve = cfg.LandCurve, seaCurve = cfg.SeaCurve, topMargin = cfg.TopMargin, seaDepth = cfg.MaxSeaDepth;
            string badCurves = VerticalScale.Sanitize(ref landCurve, ref seaCurve, ref topMargin, ref seaDepth);
            if (badCurves.Length > 0) log.Warning("{0}: {1} can't make terrain; this new world uses the defaults for them (it saves them, so fix the config before creating the next one).", ConfigFile, badCurves);
            var terrain = EarthWorldOptions.Terrain(wc, H);
            var vs = terrain.Build(H, sea, topMargin, landCurve, seaCurve, seaDepth);
            report += string.Format(" World height {0}, sea level {1}: Everest at y={2:0}, deepest sea floor at y={3:0}.", H, sea, vs.TopY, sea - 1 - vs.MaxDepth);
            // ------------------------------------------------------------------ decision 0031's optional data
            // (every world made from the menu reads it: VerticalTerrainOptions.Experimental is false only for the test-only
            // legacy profile)
            // (in this order, before anything else reads the data: each one switches the data on that it reads)
            int fineRain = terrain.Experimental && DataHas(data, d => { d.SetFineRain(true); return d.FineRain; }, d => d.FineRainError, log) ? 1 : 0;
            int summits = terrain.Experimental && DataHas(data, d => { d.SetSummits(true); return d.HasSummits; }, d => d.SummitsError, log) ? KnownSummits.Version : 0;
            int aridity = data != null && DataHas(data, d => { d.SetPenmanAridity(true); return d.PenmanAridity; }, d => d.PenmanError, log) ? EarthData.PenmanAridityVersion : 0;
            string peaks = terrain.Experimental && data?.HasSummits == true ? data.ReadPeaks()?.Signature : null;
            int glaciers = terrain.Experimental && DataHas(data, d => { d.SetGlaciers(true); return d.HasGlaciers; }, d => d.GlaciersError, log) ? 1 : 0;
            // ------------------------------------------------------------------ the Earth tab, else earth.json, else the default
            var tab = EarthWorldOptions.ReadTab(wc, cfg, data, H);
            int geologyRules = tab.RegionalGeology ? RegionalGeologyCatalog.RulesVersion : 0;
            int groundColumns = tab.GroundColumns && tab.RegionalGeology ? GroundColumns.RulesVersion : 0;
            int oreLayers = GroundColumnFacts.Columns(groundColumns) ? OreLayers.RulesVersion : 0;
            // "Nature" vanilla on the tab: placement by climate alone, no regional tree allocation either
            bool regionalTrees = cfg.RegionalTrees && !(tab.NatureOnTab && !tab.Nature1318);
            // decision 0075: "Biomes" on its own; either Nature goes with either Biomes
            bool earthBiomes = !tab.VanillaBiomes;
            // the land heights (0053; GLO-90 full, included or built in) and the waters on them
            int fineElevation = tab.Elevation == "glo90" ? FineElevation.Version : 0;
            // the included GLO-90 heights: the land heights of "glo90coarse", and where full GLO-90 can't be downloaded
            int coarseElevation = tab.Elevation != "builtin" && data?.Coarse != null ? CoarseElevation.Version : 0;
            // rivers on GLO-90 with either GLO-90 choice (their valleys are in the land then)
            // (2: with the reaches the water atlas moved, 3: and the side arms, where the installed data allows; 0053 items 14, 16)
            int riverCourses = tab.Elevation != "builtin" && data != null ? data.Glo90RiverCourses : 0;
            // modern dams taken out (no dams in 1318: 0053 item 19), on the courses the dam file was made for: the newest dam
            // rules (DamRemovalData.NewestRules) on courses rules 4, rules 1 on courses rules 3 (ER-28: the comment named rules 2)
            int damRules = tab.Elevation != "glo90" ? 0
                : riverCourses == DamRemovalData.CourseRules2 ? DamRemovalData.NewestRules
                : riverCourses == DamRemovalData.CourseRules ? DamRemovalData.RulesVersion : 0;
            // and its rivers, at the river courses' levels (0053 item 18), through the old reservoirs with the dams out: the
            // outlines baked with that dam file's corridors (dam rules 2: version 3, 0063; rules 1: version 2)
            // (dam rules 3, decision 0069: version 4, on its reservoirs and corridors; a new outline version is a new arm here,
            // RiverOutlineData.FileVersion and EarthParams.RulesAgainstBuild name the newest)
            int riverOutlines = tab.Elevation == "glo90" ? (damRules >= DamRemovalData.Rules3 ? 4 : damRules >= DamRemovalData.Rules2 ? 3 : damRules >= 1 ? 2 : 1) : 0;
            // the channel rules with them (0063 channel rules 1, "dredging": the course's channel everywhere, at the flow's width
            // or the river's measured one where narrower)
            int channelRules = riverOutlines >= 1 && riverCourses >= 1 ? TerrainModel.ChannelRulesVersion : 0;
            var p = new EarthParams
            {
                Version = EarthParams.CurrentVersion,
                Enabled = true,
                CreationOptionsVersion = 1,
                // ---------------------------------------------------------------- the layout
                RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
                EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
                Cylinder = true,
                GutterBridges = true,
                GutterSeabed = gutterSeabedRules,
                GutterSeabedY = Math.Max(w.CwSeabedY, (int)(sea - 1 - vs.MaxDepth)),
                NoiseSeed = w.Seed,
                // ---------------------------------------------------------------- the heights
                MapHeight = H, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
                LinearLand = vs.LinearLand, LocalAlpineEcology = terrain.LocalEcology, PhysicalRockExposure = terrain.PhysicalRock,
                TerrainProfile = terrain.Experimental ? terrain.Budget + "/" + terrain.Curve + "/" + terrain.Detail : null,
                DetailScale = cfg.DetailScale * terrain.DetailMultiplier,
                DetailRules = TerrainModel.DetailRulesVersion,
                // the code review of 11 November 2026: dry land below 0 m, springs at whole blocks, crops in the river belt, dry
                // river beds' gravel (TerrainModel.GenFixRules)
                GenFixRules = TerrainModel.GenFixRulesVersion,
                SummitMinElevation = tab.SummitMinElevation,
                FineElevation = fineElevation,
                CoarseElevation = coarseElevation,
                RockFloorDepth = tab.RockFloorDepth,
                // ---------------------------------------------------------------- climate (decision 0031's data above)
                // A04: it reaches world generation's temperatures, so the world keeps the value it was made with
                ExtendTemperature = cfg.ExtendTemperatureRange,
                TemperatureRangeFrozen = 1,
                // decision 0031: the real rainfall (rules 3) for every world made from the menu; rules 2 only in the test-only legacy profile
                ClimateRules = terrain.Experimental ? TerrainModel.RealRainRules : TerrainModel.RealLapseRules,
                FineRain = fineRain,
                Summits = summits,
                AridityRules = aridity,
                PeakCatalogSignature = peaks,
                Glaciers = glaciers,
                ClimateData = data?.ClimateFixAvailable ?? 0,
                // A6: the zones and fire rules read weather.bin's monthly normals; a later start compares (BuildAndAttachModel)
                WeatherDataMade = data?.Weather?.Signature,
                // ---------------------------------------------------------------- rivers, lakes, coasts
                Rivers = cfg.Rivers,
                RiverFalls = cfg.Rivers ? RiverFalls.Version : 0,
                RiverChannels = cfg.Rivers ? SeaChannels.Version : 0,
                RiverEnds = cfg.Rivers ? Core.RiverEnds.Version : 0,
                RiverDeltas = cfg.Rivers ? Core.RiverDeltas.Version : 0,
                RiverWidths = cfg.Rivers ? RiverNetwork.WidthRulesVersion : 0,
                // pool layout 8 (steps without sills) only with a Hydrology that reads the river flow query (decision 0027): without
                // it, sill-less steps are not safe from its communicating vessels. Pool layouts 9 and 10 (flow-labelled rapid
                // sources) only with one that reads RiverFlowAt and keeps those labels when the water updates (its query version 2).
                // Pool layout 11 (flow-labelled calm rivers, decision 0046) only with one that keeps them in every river column (3).
                // RequiredMods refuses a new Earth world before this without query version 3 (RiverQueryMinimum, Hydrology
                // 0.6.2), so every new world gets layout 11; the fallback to 10 below is kept for a Hydrology that reports less.
                RiverPoolLayout = !cfg.Rivers ? 0 : w.HydrologyQuery >= 3 ? RiverNetwork.PoolLayoutVersion : Math.Min(10, RiverNetwork.PoolLayoutVersion),
                RiverMinDischarge = cfg.RiverMinDischarge,
                RiverWidthScale = cfg.RiverWidthScale,
                StreamMode = EarthWorldOptions.StreamsFor(cfg.Streams, geo.RingLength),
                StreamMinDischarge = cfg.StreamMinDischarge,
                RiverStraighten = cfg.RiverStraighten,
                RiverSmoothWidths = cfg.RiverSmoothWidths,
                RiverRelief = cfg.RiverRelief,
                NoRiverRapids = tab.NoRiverRapids,
                RiverCourses = riverCourses,
                // the water mask's lakes from their outlines with full GLO-90 (their levels are its heights'; 0053 item 17)
                LakeOutlines = tab.Elevation == "glo90" ? LakeOutlineData.FileVersion : 0,
                DamRules = damRules,
                RiverOutlines = riverOutlines,
                ChannelRules = channelRules,
                // decision 0069: the flow solved on river outlines 4's water, with its rerouted mouths (on courses rules 4's list)
                FlowRules = riverOutlines >= 4 && channelRules >= 1 && riverCourses == DamRemovalData.CourseRules2 ? RiverFlowData.FileVersion : 0,
                MinSeaDepth = TerrainModel.ShipSeaDepth,
                LakeShores = ShoreData.FileVersion,
                AridRiverRules = AridHydrology.Version,
                StraitRules = NavigableStraits.Version,
                ReservoirRules = EarthParams.ReservoirRulesVersion,
                LakeOutlets = data?.Outlets != null ? OutletData.FileVersion : 0,
                SeaReferee = data?.SeaRefereeAvailable ?? 0,
                // decision 0064: coasts and waters as of 1318 where people changed them later
                Works1318 = data?.Works1318 != null ? Works1318Data.RulesVersion : 0,
                PerennialRules = data?.NonPerennial != null ? NonPerennialData.RulesVersion : 0,
                Canals = EarthWorldOptions.CanalCodes(wc),
                Islands = cfg.Islands ? IslandLayer.RulesVersion : 0,
                Coasts = Coasts(cfg, geo, cfg.Islands),
                MinIslandBlocks = cfg.MinIslandBlocks,
                ShoreZoneRules = Core.ShoreZones.RulesVersion,
                // decision 0070: the shore strip of the coast's own material
                ShoreRules = Core.ShoreMaterial.RulesVersion,
                SpringRules = tab.SpringRules,
                PondRules = tab.PondRules,
                WaterTableRules = WaterTable.RulesVersion,
                GeothermalRules = Geothermal.RulesVersion,
                // ---------------------------------------------------------------- rocks, ground and ores ("Rocks and ground", "Ores")
                RegionalGeology = tab.RegionalGeology,
                GeologyRulesVersion = geologyRules,
                GeologyTransitionBlocks = cfg.GeologyTransitionBlocks,
                GroundColumns = groundColumns,
                GroundDepth = GroundColumnFacts.Columns(groundColumns) ? tab.GroundDepth : null,
                OreLayers = oreLayers,
                RegionalOres = tab.RegionalOres,
                OreRulesVersion = tab.RegionalOres ? RegionalOreCatalog.Version : 0,
                // 0065: the ore maps in a ground-column world use ore rules v4 (catalogue bodies in their layers)
                OreRules = tab.RegionalOres && OreLayerFacts.Layers(oreLayers) ? OreBackground.CurrentRules : 0,
                OreSettingsJson = System.Text.Json.JsonSerializer.Serialize(cfg.Ores ?? new OreTuning()),
                // decision 0077: Earth's crafting rules wherever Earth decides where bauxite lies
                CraftingRules = EarthCrafting.RulesFor(groundColumns, tab.RegionalOres),
                DepositSet = EarthDepositSet.Version,
                SoilRules = SoilFertility.RulesVersion,
                // ---------------------------------------------------------------- plants and animals ("Nature", "Biomes")
                RegionalTrees = regionalTrees,
                TreeRulesVersion = regionalTrees ? RegionalTreeCatalog.RulesVersion : 0,
                RegionalFauna = tab.Nature1318,
                FaunaRulesVersion = tab.Nature1318 ? RegionalFaunaCatalog.RulesVersion : 0,
                VanillaFaunaRanges = tab.Nature1318 ? VanillaFaunaRanges.Version : 0,
                PlacementRules = tab.Nature1318 ? PlacementCatalogue.RulesVersion : 0,
                IncludeExtinctAnimals = tab.IncludeExtinctAnimals,
                ChickensByRange = tab.ChickensByRange,
                // Decision 0054: worlds placed as around 1318 get animal ranges as around 1318 unless earth.json asks for "current".
                FaunaRangeProfile = tab.Nature1318 && cfg.FaunaRangeProfile != RegionalFaunaCatalog.CurrentProfile ? RegionalFaunaCatalog.Profile1318 : cfg.FaunaRangeProfile,
                VanillaBiomes = earthBiomes ? 0 : EarthParams.VanillaBiomesVersion,
                // EA-0054 step 2: trees and shrubs by each column's site
                SiteRules = earthBiomes ? Core.SiteNiche.RulesVersion : 0,
                ZoneRules = earthBiomes ? ClimateZones.RulesVersion : 0,
                FireRules = earthBiomes ? FireRegime.RulesVersion : 0,
                WetlandRules = earthBiomes ? WetlandGrid.RulesVersion : 0,
                GroundCoverRules = earthBiomes ? GroundCover.RulesVersion : 0,
                MoistureRules = earthBiomes ? GroundMoisture.RulesVersion : 0,
                GrassWinterRules = earthBiomes ? GrassWinter.RulesVersion : 0,
                AlpineSurfaceRules = earthBiomes && terrain.LocalEcology ? MountainEcology.SurfaceVersion : 0,
                // ---------------------------------------------------------------- weather ("Weather", rain and storms)
                Weather = tab.Weather,
                WeatherModelRules = WeatherSettings.Newest,
                WeatherPace = cfg.WeatherPace,
                RainFrequency = tab.RainFrequency,
                StormFrequency = tab.StormFrequency,
            };
            badSettings = p.RepairSettings(new EarthMapConfig());
            return p;
        }

        /// <summary>Whether an optional data file of decision 0031 is in this Earth Map (a new world records it only then); logs why not.</summary>
        static bool DataHas(EarthData data, System.Func<EarthData, bool> load, System.Func<EarthData, string> error, ILogger log)
        {
            if (data == null) return false;   // (tests that build params without data; a server always has it)
            if (load(data)) return true;
            log.Error("Earth Map: {0}; this new world goes without it. Reinstall the mod.", error(data) ?? "data missing");
            return false;
        }
    }
}
