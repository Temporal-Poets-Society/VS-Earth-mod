using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EarthMap;
using EarthMap.Core;

namespace Probe
{
    /// <summary>A model as a new Earth world of a scale builds it (EarthMapSystem.ComputeParams + EarthWorldOptions.Freeze +
    /// EarthElevationSystem.AttachServer), offline. GLO-90 tiles are read from a scratch copy of the squares asked for.</summary>
    public sealed class World
    {
        public EarthParams P;
        public EarthData Data;
        public TerrainModel Model;
        public EarthGeometry Geo;
        public FineElevation Fine;
        public string Root, Store, FineFolder;
        public RiverFlowData Flow;
        public DamRemovalData Dams;

        public static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public static World Build(string root, int scale, string scratch, IEnumerable<(double lat, double lon)> places, bool glo90 = true, bool log = true)
        {
            var w = new World { Root = root, Store = Path.Combine(AppData, "VintagestoryData", "ModData", "earth") };
            var sw = Stopwatch.StartNew();
            w.Data = EarthData.Load(name => EarthData.OpenDataFile(root, name) ?? EarthDataStore.Open(w.Store, name), true);
            if (log) Console.WriteLine($"data loaded in {sw.ElapsedMilliseconds} ms");
            var data = w.Data;
            var cfg = new EarthMapConfig();
            // decision 0068: length = equator / (2 x scale) in whole 512-block regions; ring = 2 x length from x = 16384
            const double Equator = 40075017;
            int length = (int)Math.Round(Equator / (2.0 * scale) / 512) * 512;
            int width = 2 * length + 2 * 16384;
            var inp = new LayoutInput
            {
                WorldWidth = width, GutterMargin = cfg.GutterMargin, Gutter = false,
                CwRingStart = 16384, CwRingLength = 2 * length, CwBandInner = 608, CwEquatorZ = length / 2.0, CwPoleDistance = length / 2.0 - 48,
            };
            var geo = EarthLayout.Build(inp, data, out string report);
            if (log) Console.WriteLine(report);
            int H = 384, sea = (int)(0.4313725490196078 * H);
            var vs = VerticalScale.ForWorld(H, sea, cfg.TopMargin, cfg.LandCurve, cfg.SeaCurve, cfg.MaxSeaDepth);
            string elevation = glo90 ? "glo90" : "builtin";
            var p = new EarthParams
            {
                Version = EarthParams.CurrentVersion, Enabled = true,
                RingStart = geo.RingStart, RingLength = geo.RingLength, Wraps = geo.Wraps, Gutter = geo.Gutter,
                EquatorZ = geo.EquatorZ, BlocksPerDegreeZ = geo.BlocksPerDegreeZ, SeamLon0 = geo.SeamLon0,
                Cylinder = true, GutterBridges = true, GutterSeabed = TerrainModel.RealSeamVersion,
                RiverFalls = RiverFalls.Version, RiverChannels = SeaChannels.Version, RiverEnds = RiverEnds.Version, RiverDeltas = RiverDeltas.Version,
                RiverWidths = RiverNetwork.WidthRulesVersion, RiverPoolLayout = RiverNetwork.PoolLayoutVersion,
                MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = ShoreData.FileVersion,
                ClimateRules = TerrainModel.RealLapseRules,
                AridRiverRules = AridHydrology.Version, StraitRules = NavigableStraits.Version, ReservoirRules = EarthParams.ReservoirRulesVersion,
                LakeOutlets = data.Outlets != null ? OutletData.FileVersion : 0,
                SeaReferee = data.SeaRefereeAvailable,
                Works1318 = data.Works1318 != null ? Works1318Data.RulesVersion : 0,
                PerennialRules = data.NonPerennial != null ? NonPerennialData.RulesVersion : 0,
                ClimateData = data.ClimateFixAvailable,
                Islands = cfg.Islands ? IslandLayer.RulesVersion : 0,
                Coasts = cfg.Coasts && cfg.Islands && CoastLayer.CellBlocks(geo) >= cfg.CoastMinCellBlocks ? CoastData.FileVersion : 0,
                MinIslandBlocks = cfg.MinIslandBlocks,
                MapHeight = H, SeaLevel = sea, TopY = vs.TopY, LandH0 = vs.LandH0, MaxDepth = vs.MaxDepth, SeaD0 = vs.SeaD0,
                GutterSeabedY = (int)(sea - 1 - vs.MaxDepth),
                NoiseSeed = 12345,
                DetailScale = cfg.DetailScale,
                Rivers = cfg.Rivers, RiverMinDischarge = cfg.RiverMinDischarge, RiverWidthScale = cfg.RiverWidthScale,
                StreamMode = Equator / geo.RingLength < 37.5 ? "all" : "ends",
                StreamMinDischarge = cfg.StreamMinDischarge, RiverStraighten = cfg.RiverStraighten, RiverSmoothWidths = cfg.RiverSmoothWidths,
                RiverRelief = cfg.RiverRelief,
            };
            // EarthWorldOptions.Freeze's terrain rules
            p.FineElevation = elevation == "glo90" ? FineElevation.Version : 0;
            p.CoarseElevation = elevation != "builtin" && data.Coarse != null ? CoarseElevation.Version : 0;
            p.RiverCourses = elevation != "builtin" ? data.Glo90RiverCourses : 0;
            p.LakeOutlines = elevation == "glo90" ? LakeOutlineData.FileVersion : 0;
            p.DamRules = elevation != "glo90" ? 0 : p.RiverCourses == DamRemovalData.CourseRules2 ? DamRemovalData.NewestRules
                : p.RiverCourses == DamRemovalData.CourseRules ? DamRemovalData.RulesVersion : 0;
            p.RiverOutlines = elevation == "glo90" ? (p.DamRules >= DamRemovalData.Rules3 ? 4 : p.DamRules >= DamRemovalData.Rules2 ? 3 : p.DamRules >= 1 ? 2 : 1) : 0;
            p.ChannelRules = p.RiverOutlines >= 1 && p.RiverCourses >= 1 ? TerrainModel.ChannelRulesVersion : 0;
            p.ShoreZoneRules = ShoreZones.RulesVersion;
            p.ShoreRules = ShoreMaterial.RulesVersion;
            p.FlowRules = p.RiverOutlines >= 4 && p.ChannelRules >= 1 && p.RiverCourses == DamRemovalData.CourseRules2 ? RiverFlowData.FileVersion : 0;
            p.DetailRules = TerrainModel.DetailRulesVersion;
            p.GenFixRules = TerrainModel.GenFixRulesVersion;
            w.P = p;
            w.Geo = geo;
            if (log) Console.WriteLine($"1:{scale}: world {width} x {length}, ring {geo.RingLength:0} from {geo.RingStart:0}, {Equator / geo.RingLength:0.00} m/block; coasts {p.Coasts} courses {p.RiverCourses} channelRules {p.ChannelRules} " +
                $"flowRules {p.FlowRules} dams {p.DamRules} outlines L{p.LakeOutlines} R{p.RiverOutlines} streams {p.StreamMode} works {p.Works1318} referee {p.SeaReferee} coarse {p.CoarseElevation}");

            string glo = Path.Combine(w.Store, "glo90");
            DamRemovalData dams = null;
            RiverFlowData flow = null;
            if (p.DamRules >= 1) { dams = DamRemovalData.Read(File.ReadAllBytes(Path.Combine(glo, DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules))))); }
            if (p.FlowRules >= 1) { flow = RiverFlowData.Read(File.ReadAllBytes(Path.Combine(glo, RiverFlowData.FileName))); }
            w.Flow = flow; w.Dams = dams;
            sw.Restart();
            string cacheFolder = Path.Combine(scratch, "cache");
            var cache = ModelCache.Open(cacheFolder, System.Text.Encoding.UTF8.GetBytes($"probe-fef6166-{scale}-{elevation}"));
            var model = p.BuildModel(data, dams, cache, flow);
            cache.Save();
            if (log) Console.WriteLine($"model built in {sw.ElapsedMilliseconds} ms (cache reused [{string.Join(",", cache.Reused)}] built [{string.Join(",", cache.Built)}]); rivers {model.Rivers?.SegmentCount} pieces; " +
                $"managed {GC.GetTotalMemory(false) / 1e6:0} MB, working set {Process.GetCurrentProcess().WorkingSet64 / 1e6:0} MB");
            w.Model = model;
            if (p.FineElevation >= 1)
            {
                // a scratch copy of the squares the places need (FineElevation sets a tile it can't read aside as .bad: never in the player's store)
                w.FineFolder = Path.Combine(scratch, "glo90");
                Directory.CreateDirectory(w.FineFolder);
                var fine = new FineElevation(w.FineFolder, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
                w.Fine = fine;
                foreach (var (lat, lon) in places) w.Need(lat, lon, 1);
                if (dams != null && p.DamCourseError == null) fine.Dams = dams;
                model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
                model.Fine = fine;
                if (p.LakeOutlines >= 1)
                    model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(glo, LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion))))), geo);
                if (p.RiverOutlines >= 1)
                {
                    int version = RiverOutlineData.Usable(p.RiverOutlines);
                    model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(glo, RiverOutlineData.FileName(version)))), geo, p.DropsNarrowRiverPieces,
                        model.CourseDrawsAt, p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
                }
                if (flow != null && p.FlowRules >= 1) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
            }
            FineElevation.FetchAllowed = false;
            return w;
        }

        /// <summary>Copies the GLO-90 squares within r squares of a place into the scratch store.</summary>
        public void Need(double lat, double lon, int r)
        {
            if (Fine == null) return;
            string glo = Path.Combine(Store, "glo90");
            int latS = (int)Math.Ceiling(lat) - 1, lonW = (int)Math.Floor(lon);
            for (int a = latS - r; a <= latS + r; a++)
                for (int o = lonW - r; o <= lonW + r; o++)
                {
                    if (a < -90 || a > 89) continue;
                    int ow = ((o + 180) % 360 + 360) % 360 - 180;
                    if (!Fine.SourceHas(a, ow)) continue;
                    string name = DemTiles.ShortName(a, ow) + FineElevation.FileExtension;
                    string to = Path.Combine(FineFolder, name), from = Path.Combine(glo, name);
                    if (!File.Exists(to) && File.Exists(from)) { File.Copy(from, to); Fine.TileArrived(a, ow); }
                }
        }

        public (double x, double z) At(double lat, double lon) { Geo.FromGeo(lat, lon, out double x, out double z); return (Math.Floor(x), Math.Floor(z)); }
    }
}
