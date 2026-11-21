using System.Globalization;
using System.Reflection;
using System.Text;
using EarthMap;
using EarthMap.Core;

// Decision 0107 (EA-0124, 21 November 2026): the lakes of 1318 (works rules 6, river ends rules 3) against an rc.14
// world (variant "w5": works rules 5, river ends rules 2). Started as the EA-0124 check of how decision 0005's revived terminal lakes (lakes.bin, the grid's lake overlay with
// EarthParams.RiverEnds) look in a new world of Earth 2.2.0-rc.13 on GLO-90 heights, beside the water mask's own lake
// outlines (lakes-glo90-5.bin) and lake rules 1's depths. A throwaway probe on branch claude/ea-0124-probe (never merged),
// trimmed from the EA-0148 probe (../ea-0148-offset-2026-11/probe): the world is built as a server builds it
// (WorldParamsBuilder's params for a new world of the play style, the full GLO-90 store), and sampled in windows on the
// 1:10 world's block grid (EA0124_STRIDE: every Nth block); the 1:100 world at the same latitudes and longitudes.
//
// Variants: "base" (the world as made), "noends" (RiverEnds 0: no lakes.bin overlay), "w5" (works rules 5 and river ends
// rules 2, as rc.14 makes a world).
//
// Usage: dotnet run -c Release --project <this folder> -- <repo root> <out dir> <places file> [scales 10,100] [variants base,noends]
static class Probe
{
    sealed record Place(string Name, double Lat, double Lon, double HalfLat, double HalfLon);

    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[1]);
        var places = File.ReadAllLines(args[2]).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').Select(l =>
        {
            var f = l.Split(';');
            return new Place(f[0].Trim(), D(f[1]), D(f[2]), D(f[3]), D(f[4]));
        }).ToList();
        int[] scales = (args.Length > 3 ? args[3] : "10,100").Split(',').Select(int.Parse).ToArray();
        string[] variants = (args.Length > 4 ? args[4] : "base,noends").Split(',');
        string store = Environment.GetEnvironmentVariable("EARTH_STORE");
        if (string.IsNullOrEmpty(store))
            store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData-agentbridge", "ModData", "earth");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("store: " + store);
        int tiles = Directory.Exists(Path.Combine(store, "glo90")) ? Directory.GetFiles(Path.Combine(store, "glo90"), "*.egl").Length : 0;
        Console.WriteLine("GLO-90 tiles in the store: " + tiles);
        if (tiles < 26000) { Console.WriteLine("the store lacks the GLO-90 tiles: the model would fall back to the built-in heights"); return 2; }
        var data = LoadData(root, store);
        if (data.TerminalLakes != null)
            foreach (var l in data.TerminalLakes.Lakes)
                Console.WriteLine($"  lakes.bin: {l.Name}, {l.Level} m, seed {l.Lat},{l.Lon}, shore {l.ShoreKm} km, revived {l.Revived}, salt {l.Salt}");

        double[][] lats = null, lons = null;
        foreach (int scale in scales)
        {
            var p = MakeParams(root, data, scale);
            Console.WriteLine($"1:{scale}: RiverEnds {p.RiverEnds}, FineElevation {p.FineElevation}, LakeOutlines {p.LakeOutlines}, LakeRules {p.LakeRules}, RiverOutlines {p.RiverOutlines}, " +
                $"ChannelRules {p.ChannelRules}, GenFixRules {p.GenFixRules}, RiverCourses {p.RiverCourses}, Works1318 {p.Works1318}");
            foreach (string variant in variants)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int riverEnds = p.RiverEnds;
                int works = p.Works1318;
                if (variant == "noends") p.RiverEnds = 0;
                if (variant == "w5") { p.RiverEnds = 2; p.Works1318 = Works1318Data.PointRules; }
                var model = Build(root, store, data, p, out string files);
                Console.WriteLine($"  {variant}: model built in {sw.Elapsed.TotalSeconds:0} s; {files}; FineRules {model.FineRules}; lake overlay {data.LakeOverlay}");
                if (lats == null)
                {
                    if (scale != 10) { Console.WriteLine("the first scale must be 10 (the sample grid is its blocks)"); return 2; }
                    lats = new double[places.Count][]; lons = new double[places.Count][];
                    for (int k = 0; k < places.Count; k++) Grid(model.Geometry, places[k], out lats[k], out lons[k]);
                }
                for (int k = 0; k < places.Count; k++)
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    int missed0 = FineElevation.MissedReads;
                    string file = Path.Combine(outDir, $"{places[k].Name}_{scale}_{variant}.bin");
                    Sample(model, data, p, places[k], lats[k], lons[k], scale, variant, file);
                    Console.WriteLine($"    {places[k].Name}: {lats[k].Length} x {lons[k].Length} in {t.Elapsed.TotalSeconds:0} s, built-in height reads {FineElevation.MissedReads - missed0}");
                    if (variant == variants[0]) Courses(model, places[k], Path.Combine(outDir, $"{places[k].Name}_{scale}_courses.csv"));
                }
                p.RiverEnds = riverEnds;
                p.Works1318 = works;
            }
        }
        return 0;
    }

    static double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);

    static void Grid(EarthGeometry geo, Place pl, out double[] lat, out double[] lon)
    {
        geo.FromGeo(pl.Lat + pl.HalfLat, pl.Lon - pl.HalfLon, out double xa, out double za);
        geo.FromGeo(pl.Lat - pl.HalfLat, pl.Lon + pl.HalfLon, out double xb, out double zb);
        long x0 = (long)Math.Floor(xa), z0 = (long)Math.Floor(za);
        int stride = Math.Max(1, int.TryParse(Environment.GetEnvironmentVariable("EA0124_STRIDE"), out int st) ? st : 1);
        int nx = (int)Math.Round((xb - xa) / stride), nz = (int)Math.Round((zb - za) / stride);
        lat = new double[nz]; lon = new double[nx];
        for (int j = 0; j < nz; j++) lat[j] = geo.ToGeo(x0, z0 + (long)j * stride).Lat;
        for (int i = 0; i < nx; i++) lon[i] = geo.ToGeo(x0 + (long)i * stride, z0).Lon;
    }

    // flags: 1 water, 2 river, 4 outline lake column, 8 lake water (fresh or salt), 16 sea, 32 beach, 128 still,
    // 256 river outline, 2048 lake rim, 4096 inside a mask lake outline, 8192 inside a river outline,
    // 16384 the 2' grid cell is a terminal lake cell (lakes.bin), 32768 salt lake water
    static void Sample(TerrainModel model, EarthData data, EarthParams p, Place pl, double[] lat, double[] lon, int scale, string variant, string file)
    {
        int nz = lat.Length, nx = lon.Length, n = nz * nx;
        var flags = new uint[n];
        var top = new short[n];
        var wtop = new short[n];
        var fine = new float[n];
        var elev = new float[n];
        var lakeLevel = new float[n];
        var gridLevel = new float[n];
        var gridShare = new float[n];
        var gridElev = new float[n];
        bool bounded = model.FineRules >= 5;
        int res = data.TerrainRes;
        Parallel.For(0, nz, j =>
        {
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                model.Geometry.FromGeo(lat[j], lon[i], out double x, out double z);
                if (scale == 10) { x = Math.Round(x); z = Math.Round(z); }
                var c = model.GetColumn(x, z);
                uint f = 0;
                if (c.WaterTop >= 0 && c.WaterTop > c.Top) f |= 1;
                if (c.River) f |= 2;
                if (c.OutlineLake) f |= 4;
                if (c.Water == TerrainModel.WaterLake || c.Water == TerrainModel.WaterSaltLake) f |= 8;
                if (c.Water == TerrainModel.WaterSea) f |= 16;
                if (c.Beach) f |= 32;
                if (c.Still) f |= 128;
                if (c.RiverOutline > 0) f |= 256;
                if (c.LakeRim > 0) f |= 2048;
                float lv = float.NaN;
                if (model.LakeOutlines != null) { var h = model.LakeOutlines.At(lat[j], lon[i]); if (h.Inside) { f |= 4096; lv = (float)h.Level; } }
                if (model.RiverOutlines != null) { var h = model.RiverOutlines.At(lat[j], lon[i]); if (h.Inside) { f |= 8192; if (float.IsNaN(lv)) lv = (float)h.Level; } }
                int row = (int)Math.Floor((90 - lat[j]) * res), col = (int)Math.Floor((lon[i] + 180) * res);
                if (data.IsTerminalLakeCell(row, col)) f |= 16384;
                if (c.Water == TerrainModel.WaterSaltLake) f |= 32768;
                if (model.Works != null) { var w = model.Works.At(lat[j], lon[i]); if (w.Inside && (w.Flags & Works1318Data.Lake) != 0) f |= 65536; if (w.Inside && (w.Flags & Works1318Data.Land) != 0) f |= 131072; }
                flags[k] = f;
                top[k] = (short)Math.Clamp(c.Top, short.MinValue, short.MaxValue);
                wtop[k] = (short)Math.Clamp(c.WaterTop, short.MinValue, short.MaxValue);
                elev[k] = (float)c.Elev;
                lakeLevel[k] = lv;
                var s = data.SampleTerrain(lat[j], lon[i], model.Coasts != null);
                gridLevel[k] = s.Lake > 0 ? (float)s.LakeLevel : float.NaN;
                gridShare[k] = (float)s.Lake;
                gridElev[k] = (float)s.Elev;
                fine[k] = model.Fine != null && model.Fine.TrySmoothHeight(lat[j], lon[i], bounded, out double fh) ? (float)fh : float.NaN;
            }
        });
        // the vertical profile: Y of each whole metre from -100 to 1000 m (for converting blocks to metres)
        var ys = new StringBuilder();
        for (int m = -100; m <= 2600; m += 1) ys.Append(FormattableString.Invariant($"{model.Vertical.Y(m):R},"));
        string header = "{" + (
            $"\"place\":\"{pl.Name}\",\"scale\":{scale},\"variant\":\"{variant}\",\"nz\":{nz},\"nx\":{nx},\"lat0\":{lat[0]:R},\"lat1\":{lat[^1]:R},\"lon0\":{lon[0]:R},\"lon1\":{lon[^1]:R}," +
            $"\"works\":{p.Works1318},\"bpdx\":{model.Geometry.BlocksPerDegreeX:R},\"bpdz\":{model.Geometry.BlocksPerDegreeZ:R},\"seaLevel\":{model.SeaLevel},\"riverEnds\":{p.RiverEnds},\"mapHeight\":{model.Vertical.MapHeight}," +
            $"\"yOfMetre0\":-100,\"yOfMetre\":[{ys.ToString().TrimEnd(',')}]") + "}";
        using var fs = File.Create(file);
        using var bw = new BinaryWriter(fs);
        bw.Write(Encoding.UTF8.GetBytes(header + "\n"));
        foreach (var v in lat) bw.Write(v);
        foreach (var v in lon) bw.Write(v);
        foreach (var v in flags) bw.Write(v);
        foreach (var v in top) bw.Write(v);
        foreach (var v in wtop) bw.Write(v);
        foreach (var v in fine) bw.Write(v);
        foreach (var v in elev) bw.Write(v);
        foreach (var v in lakeLevel) bw.Write(v);
        foreach (var v in gridLevel) bw.Write(v);
        foreach (var v in gridShare) bw.Write(v);
        foreach (var v in gridElev) bw.Write(v);
    }

    static void Courses(TerrainModel model, Place pl, string file)
    {
        var sb = new StringBuilder("reach,vertex,lat,lon,q,width_m,level,fixed\n");
        var list = model.Rivers?.Reaches;
        if (list != null)
        {
            double mLat = pl.HalfLat + 0.02, mLon = pl.HalfLon + 0.02 / Math.Max(0.1, Math.Cos(pl.Lat * Math.PI / 180));
            for (int r = 0; r < list.Count; r++)
            {
                var re = list[r];
                if (re.Lat == null) continue;
                bool any = false;
                for (int v = 0; v < re.Lat.Length && !any; v++) any = Math.Abs(re.Lat[v] - pl.Lat) <= mLat && Math.Abs(Geo.Wrap180(re.Lon[v] - pl.Lon)) <= mLon;
                if (!any) continue;
                for (int v = 0; v < re.Lat.Length; v++)
                    sb.Append(FormattableString.Invariant($"{r},{v},{re.Lat[v]:R},{re.Lon[v]:R},{re.Q:R},{(re.Width != null ? re.Width[v] * RiverReach.WidthUnit : -1):R},{re.Level[v]},{(re.Fixed != null && re.Fixed[v] ? 1 : 0)}\n"));
            }
        }
        File.WriteAllText(file, sb.ToString());
    }

    static EarthData LoadData(string root, string store) => EarthData.Load(name =>
    {
        string a = Path.Combine(root, "earthdata", name);
        if (File.Exists(a)) return File.OpenRead(a);
        string b = Path.Combine(store, "data", name);
        return File.Exists(b) ? File.OpenRead(b) : null;
    }, true);

    /// <summary>The EA-0148 probe's Build without its shifts: the model as a server builds it on the GLO-90 store.</summary>
    static TerrainModel Build(string root, string store, EarthData data, EarthParams p, out string files)
    {
        string g = Path.Combine(store, "glo90");
        byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(g, name));
        string damName = DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules));
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(Bytes(damName)) : null;
        string flowName = RiverFlowData.FileNameFor(Math.Min(p.FlowRules, RiverFlowData.FileVersion));
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(Bytes(flowName)) : null;
        var model = p.BuildModel(data, dams, null, flow);
        var fine = new FineElevation(g, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
        fine.SetFallback(Array.Empty<int>());
        model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
        model.Fine = fine;
        if (dams != null && p.DamCourseError == null) fine.Dams = dams;
        string lakeName = LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion));
        int rv = RiverOutlineData.Usable(p.RiverOutlines);
        files = $"{damName}, {flowName}, {lakeName}, {RiverOutlineData.FileName(rv)}";
        if (p.LakeOutlines >= 1)
        {
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(Bytes(lakeName)), model.Geometry);
            if (p.LakeRules >= 1) model.LakeDepths = data.LakeDepthsFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion));
        }
        if (p.RiverOutlines >= 1)
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(Bytes(RiverOutlineData.FileName(rv))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
        return model;
    }

    static EarthParams MakeParams(string root, EarthData data, int scale)
    {
        string code = scale == 100 ? "earth" : "earth-" + scale;
        var wc = new Vintagestory.API.Datastructures.TreeAttribute();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json"))))
            foreach (var kv in doc.RootElement.GetProperty("playStyles").EnumerateArray().First(s => s.GetProperty("code").GetString() == code).GetProperty("worldConfig").EnumerateObject())
                switch (kv.Value.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.True: case System.Text.Json.JsonValueKind.False: wc.SetBool(kv.Name, kv.Value.GetBoolean()); break;
                    case System.Text.Json.JsonValueKind.Number: wc.SetInt(kv.Name, kv.Value.GetInt32()); break;
                    default: wc.SetString(kv.Name, kv.Value.GetString()); break;
                }
        SpawnSearch.SizeFor(scale.ToString(CultureInfo.InvariantCulture), out int W, out int L, out _);
        int H = wc.HasAttribute("earthWorldHeight") ? wc.GetInt("earthWorldHeight", 384) : 384;
        var w = new NewWorld
        {
            MapSizeX = W, MapSizeY = H, SeaLevel = EarthWorldOptions.Terrain(wc, H).SeaLevel(H, 165), Seed = 386217, WorldConfig = wc, HydrologyQuery = 3,
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 672,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 70, CylinderKeepsEarthSeam = () => true,
        };
        var log = DispatchProxy.Create<Vintagestory.API.Common.ILogger, QuietLog>();
        var p = WorldParamsBuilder.Build(w, new EarthMapConfig(), data, log, out _, out string bad);
        if (bad.Length > 0) throw new InvalidOperationException("this build's defaults can't make terrain: " + bad);
        Console.WriteLine($"  params for {code}: {W} x {L}, height {H}");
        return p;
    }
}

public class QuietLog : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
        => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
}
