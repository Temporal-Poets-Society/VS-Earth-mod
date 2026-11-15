#if MAKE_RC13
using EarthMap;
using EarthMap.Core;
static partial class Digest
{
    // A new 2.0.0-rc.13 world at 1:100 with the default Earth tab (full GLO-90, real seam, height 384), as rc.13's ComputeParams
    // and EarthWorldOptions.Freeze make it, from rc.13's own constants. Only what the terrain model reads is filled in.
    static partial void MakeParams(EarthData data, ref EarthParams p)
    {
        var cfg = new EarthMapConfig();
        string scale = Environment.GetEnvironmentVariable("SCALE") ?? "100";
        SpawnSearch.SizeFor(scale, out int W, out int L, out _);
        int H = int.Parse(Environment.GetEnvironmentVariable("HEIGHT") ?? "384");
        var inp = new LayoutInput { WorldWidth = W, GutterMargin = cfg.GutterMargin, Gutter = false, CwRingStart = (W - 2 * L) / 2, CwRingLength = 2 * L, CwBandInner = 608, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48 };
        var geo = EarthLayout.Build(inp, data, out string report);
        Console.WriteLine(report);
        var terrain = new VerticalTerrainOptions { LocalEcology = true, PhysicalRock = false, Curve = H >= 512 ? "linear" : VerticalTerrainOptions.DefaultCurve };
        int sea = terrain.SeaLevel(H, 165);
        var vs = terrain.Build(H, sea, cfg.TopMargin, cfg.LandCurve, cfg.SeaCurve, cfg.MaxSeaDepth);
        data.SetFineRain(true); data.SetSummits(true); data.SetGlaciers(true); data.SetPenmanAridity(true);
        p = new EarthParams
        {
            Version = EarthParams.CurrentVersion, Enabled = true,
            RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
            EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
            Cylinder = true, GutterBridges = true, GutterSeabed = TerrainModel.RealSeamVersion,
            RiverFalls = RiverFalls.Version, RiverChannels = SeaChannels.Version, RiverEnds = EarthMap.Core.RiverEnds.Version, RiverDeltas = EarthMap.Core.RiverDeltas.Version,
            RiverWidths = RiverNetwork.WidthRulesVersion, RiverPoolLayout = RiverNetwork.PoolLayoutVersion,
            MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = ShoreData.FileVersion,
            ClimateRules = terrain.Experimental ? TerrainModel.RealRainRules : TerrainModel.RealLapseRules,
            LinearLand = vs.LinearLand, LocalAlpineEcology = terrain.LocalEcology, PhysicalRockExposure = terrain.PhysicalRock,
            TerrainProfile = terrain.Budget + "/" + terrain.Curve + "/" + terrain.Detail,
            FineRain = data.FineRain ? 1 : 0, Summits = data.HasSummits ? KnownSummits.Version : 0,
            AridRiverRules = AridHydrology.Version, StraitRules = NavigableStraits.Version, ReservoirRules = EarthParams.ReservoirRulesVersion,
            LakeOutlets = data.Outlets != null ? OutletData.FileVersion : 0, SeaReferee = data.SeaRefereeAvailable,
            Works1318 = data.Works1318 != null ? Works1318Data.RulesVersion : 0, PerennialRules = data.NonPerennial != null ? NonPerennialData.RulesVersion : 0,
            AlpineSurfaceRules = MountainEcology.SurfaceVersion, SoilRules = SoilFertility.RulesVersion,
            GroundCoverRules = GroundCover.RulesVersion, ZoneRules = ClimateZones.RulesVersion, MoistureRules = GroundMoisture.RulesVersion,
            GeothermalRules = Geothermal.RulesVersion, GrassWinterRules = GrassWinter.RulesVersion, FireRules = FireRegime.RulesVersion, WetlandRules = WetlandGrid.RulesVersion,
            AridityRules = data.PenmanAridity ? EarthData.PenmanAridityVersion : 0, WaterTableRules = WaterTable.RulesVersion,
            PeakCatalogSignature = data.HasSummits ? data.ReadPeaks()?.Signature : null,
            Glaciers = data.HasGlaciers ? 1 : 0, ClimateData = data.ClimateFixAvailable,
            Canals = "", Islands = IslandLayer.RulesVersion,
            Coasts = cfg.Coasts && CoastLayer.CellBlocks(geo) >= cfg.CoastMinCellBlocks ? CoastData.FileVersion : 0,
            MinIslandBlocks = cfg.MinIslandBlocks,
            MapHeight = H, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
            GutterSeabedY = (int)(sea - 1 - vs.MaxDepth), NoiseSeed = 386217, DetailScale = cfg.DetailScale * terrain.DetailMultiplier,
            Rivers = cfg.Rivers, RiverMinDischarge = cfg.RiverMinDischarge, RiverWidthScale = cfg.RiverWidthScale,
            StreamMode = string.Equals((cfg.Streams ?? "").Trim(), "auto", StringComparison.OrdinalIgnoreCase) ? (geo.RingLength > 0 && SpawnSearch.EquatorMetres / geo.RingLength < 37.5 ? "all" : "ends") : cfg.Streams,
            StreamMinDischarge = cfg.StreamMinDischarge, RiverStraighten = cfg.RiverStraighten, RiverSmoothWidths = cfg.RiverSmoothWidths, RiverRelief = cfg.RiverRelief,
            CreationOptionsVersion = 1, SummitMinElevation = 3000, TemperatureRangeFrozen = 1,
        };
        // EarthWorldOptions.Freeze (elevation "auto" = full GLO-90)
        p.FineElevation = FineElevation.Version;
        p.CoarseElevation = data.Coarse != null ? CoarseElevation.Version : 0;
        p.RiverCourses = data.Glo90RiverCourses;
        p.LakeOutlines = LakeOutlineData.FileVersion;
        p.DamRules = p.RiverCourses == DamRemovalData.CourseRules2 ? DamRemovalData.NewestRules : p.RiverCourses == DamRemovalData.CourseRules ? DamRemovalData.RulesVersion : 0;
        p.RiverOutlines = p.DamRules >= DamRemovalData.Rules3 ? 4 : p.DamRules >= DamRemovalData.Rules2 ? 3 : p.DamRules >= 1 ? 2 : 1;
        p.ChannelRules = p.RiverOutlines >= 1 && p.RiverCourses >= 1 ? TerrainModel.ChannelRulesVersion : 0;
        p.ShoreZoneRules = ShoreZones.RulesVersion;
        p.ShoreRules = ShoreMaterial.RulesVersion;
        p.FlowRules = p.RiverOutlines >= 4 && p.ChannelRules >= 1 && p.RiverCourses == DamRemovalData.CourseRules2 ? RiverFlowData.FileVersion : 0;
        p.DetailRules = TerrainModel.DetailRulesVersion;
        p.GenFixRules = TerrainModel.GenFixRulesVersion;
        p.SpringRules = EarthParams.SpringRulesCaves;
        p.PondRules = EarthParams.PondRulesNone;
    }
}
#endif
