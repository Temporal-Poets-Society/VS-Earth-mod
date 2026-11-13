using System.Diagnostics;
using System.Reflection;
using EarthMap;
using EarthMap.Core;

var p = ProtoBuf.Serializer.Deserialize<EarthParams>(new MemoryStream(File.ReadAllBytes(args[0])));
p.RepairSettings(new EarthMapConfig());
var sw = Stopwatch.StartNew();
long last = 0;
void Lap(string what) { long now = sw.ElapsedMilliseconds; Console.WriteLine($"{what,-34} {now - last,7} ms"); last = now; }
var data = EarthData.LoadFromFolder(args[1], true); Lap("load data");
var geo = p.BuildGeometry();
data.SetClimateFix(p.ClimateData > 0);
data.SetFineTemperature(p.ClimateRules >= TerrainModel.RealLapseRules);
data.SetFineRain(p.FineRain >= 1);
data.SetSummits(p.Summits >= 1);
data.SetGlaciers(p.Glaciers >= 1); Lap("climate, summits, glaciers");
data.SetIslandOverlay(p.Islands > 0);
data.SetLakeOverlay(p.RiverEnds > 0);
data.SetShoreOverlay(p.LakeShores > 0);
data.SetReservoirExclusions(p.ReservoirRules >= 1); Lap("overlays");
var peaks = (PeakCatalog)typeof(EarthParams).GetMethod("LoadPeaks", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, new object[] { data });
var model = new TerrainModel(data, geo, p.BuildVertical(), p.NoiseSeed)
{
    GutterSeabedY = p.GutterSeabedY, LocalAlpineEcology = p.LocalAlpineEcology, Summits = p.Summits >= 1 && data.HasSummits,
    SummitRules = p.Summits, SummitMinElevation = p.SummitMinElevation, Peaks = peaks, AlpineSurfaceRules = p.AlpineSurfaceRules,
    AridRiverRules = p.AridRiverRules, DetailScale = p.DetailScale, RiverRelief = p.RiverRelief > 0 ? p.RiverRelief : 1.0,
    GutterBridges = p.GutterBridges, GutterSeabed = p.GutterSeabed,
    MinSeaDepth = p.MinSeaDepth is > 0 and <= 20 ? p.MinSeaDepth : TerrainModel.OldSeaDepth, LakeShores = data.ShoreOverlay, ClimateRules = p.ClimateRules,
}; Lap("terrain model");
if (data.IslandOverlay) model.Islands = new IslandLayer(data.Islands, geo, p.MinIslandBlocks > 0 ? p.MinIslandBlocks : 4, p.Islands);
Lap("islands");
if (p.Coasts > 0 && data.Coasts != null && data.IslandOverlay) model.Coasts = new CoastLayer(data.Coasts, geo, p.MinIslandBlocks > 0 ? p.MinIslandBlocks : 4);
Lap("coasts");
if (p.StraitRules >= 1) model.Straits = new NavigableStraits(geo, model); Lap("straits");
var canals = string.IsNullOrWhiteSpace(p.Canals) ? null : p.Canals.Split(',');
var source = p.Rivers ? data.RiverReaches() : null; Lap("river reaches (read)");
if (source != null)
{
    var reaches = source; bool[] fade = null;
    if (p.RiverEnds > 0) { var ends = RiverEnds.Prepare(data, source); reaches = ends.Reaches; fade = ends.Fade; Lap("river ends");
        if (p.RiverDeltas > 0) reaches = RiverDeltas.Apply(reaches).Reaches; Lap("river deltas"); }
    double sizeScale = geo.RingLength > 0 ? geo.RingLength / 204800.0 : 1;
    __Lap.Mark("before"); model.Rivers = new RiverNetwork(reaches, geo, p.RiverMinDischarge, p.RiverWidthScale > 0 ? p.RiverWidthScale : 1, model.RiverWaterTop,
        string.IsNullOrEmpty(p.StreamMode) ? "none" : p.StreamMode, p.StreamMinDischarge > 0 ? p.StreamMinDischarge : 20,
        p.StreamMode == null ? 1 : sizeScale, p.RiverStraighten, p.RiverSmoothWidths, falls: p.RiverFalls > 0 ? RiverFalls.All : null,
        seaLevelPiece: SeaChannels.CanalCrossing(SeaChannels.CanalCourses(geo, canals)),
        continueDownstream: p.RiverEnds > 0, fadeEnds: fade, geometrySeams: p.RiverEnds > 0, widthRules: p.RiverWidths,
        poolLayout: p.RiverPoolLayout, rapids: !p.NoRiverRapids);
    Lap($"river network ({model.Rivers.SegmentCount} segments)");
}
if (model.Rivers != null && p.AridRiverRules >= 1) { model.WetSourceFlow = AridHydrology.WetSourceFlow(data, model.Rivers.Reaches); Lap("wet source flow"); model.DryFans = new WadiFans(model); Lap("wadi fans"); }
if ((p.RiverChannels > 0 && model.Rivers != null) || canals != null)
    model.Channels = SeaChannels.Build(model, p.RiverChannels > 0, canals, p.RiverChannels > 0 ? p.RiverChannels : SeaChannels.Version);
Lap("sea channels");
Console.WriteLine($"ring {geo.RingLength} sizeScale {geo.RingLength / 204800.0:F2} total {sw.ElapsedMilliseconds} ms");
