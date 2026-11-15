#if MAKE_V102
using EarthMap;
using EarthMap.Core;
static partial class Digest
{
    // A new Earth 1.0.2 world as its ComputeParams and Freeze make it with the default Earth tab (512000 x 204800, height 384,
    // real seam), from 1.0.2's own constants.
    static partial void MakeParams(EarthData data, ref EarthParams p)
    {
        var cfg = new EarthMapConfig();
        int W = 512000, L = 204800, H = 384;
        var inp = new LayoutInput { WorldWidth = W, GutterMargin = cfg.GutterMargin, Gutter = false, CwRingStart = 51200, CwRingLength = 409600, CwBandInner = 608, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48 };
        var geo = EarthLayout.Build(inp, data, out _);
        var terrain = new VerticalTerrainOptions { LocalEcology = true, PhysicalRock = false };
        int sea = terrain.SeaLevel(H, 165);
        var vs = terrain.Build(H, sea, cfg.TopMargin, cfg.LandCurve, cfg.SeaCurve, cfg.MaxSeaDepth);
        data.SetFineRain(true); data.SetSummits(true); data.SetGlaciers(true);
        p = new EarthParams
        {
            Version = EarthParams.CurrentVersion, Enabled = true,
            RegionalTrees = cfg.RegionalTrees, TreeRulesVersion = cfg.RegionalTrees ? RegionalTreeCatalog.RulesVersion : 0,
            RegionalGeology = true, RegionalOres = true, OreRulesVersion = RegionalOreCatalog.Version, GeologyRulesVersion = RegionalGeologyCatalog.RulesVersion,
            GeologyTransitionBlocks = cfg.GeologyTransitionBlocks, RegionalFauna = true, FaunaRulesVersion = RegionalFaunaCatalog.RulesVersion,
            RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
            EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
            Cylinder = true, GutterBridges = true, GutterSeabed = TerrainModel.RealSeamVersion,
            RiverFalls = RiverFalls.Version, RiverChannels = SeaChannels.Version, RiverEnds = EarthMap.Core.RiverEnds.Version, RiverDeltas = EarthMap.Core.RiverDeltas.Version,
            RiverWidths = RiverNetwork.WidthRulesVersion, RiverPoolLayout = RiverNetwork.PoolLayoutVersion,
            MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = ShoreData.FileVersion,
            ClimateRules = terrain.Experimental ? TerrainModel.RealRainRules : TerrainModel.RealLapseRules,
            LinearLand = vs.LinearLand, LocalAlpineEcology = terrain.LocalEcology, PhysicalRockExposure = terrain.PhysicalRock,
            TerrainProfile = terrain.Budget + "/" + terrain.Curve + "/" + terrain.Detail,
            VanillaFaunaRanges = VanillaFaunaRanges.Version, FineRain = data.FineRain ? 1 : 0, Summits = data.HasSummits ? KnownSummits.Version : 0,
            AridRiverRules = AridHydrology.Version, StraitRules = NavigableStraits.Version, ReservoirRules = 1,
            AlpineSurfaceRules = MountainEcology.SurfaceVersion, PeakCatalogSignature = data.HasSummits ? data.ReadPeaks()?.Signature : null,
            Glaciers = data.HasGlaciers ? 1 : 0, ClimateData = data.HasClimateFix ? EarthData.ClimateFixVersion : 0,
            Canals = "", Islands = IslandLayer.RulesVersion,
            Coasts = cfg.Coasts && CoastLayer.CellBlocks(geo) >= cfg.CoastMinCellBlocks ? CoastData.FileVersion : 0,
            MinIslandBlocks = cfg.MinIslandBlocks,
            MapHeight = H, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
            GutterSeabedY = (int)(sea - 1 - vs.MaxDepth), NoiseSeed = 386217, DetailScale = cfg.DetailScale * terrain.DetailMultiplier,
            Rivers = cfg.Rivers, RiverMinDischarge = cfg.RiverMinDischarge, RiverWidthScale = cfg.RiverWidthScale, StreamMode = cfg.Streams,
            StreamMinDischarge = cfg.StreamMinDischarge, RiverStraighten = cfg.RiverStraighten, RiverSmoothWidths = cfg.RiverSmoothWidths, RiverRelief = cfg.RiverRelief,
            CreationOptionsVersion = 1, SummitMinElevation = 3000,
        };
    }
}
#endif
