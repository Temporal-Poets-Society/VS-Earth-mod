using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using EarthMap;
using EarthMap.Core;

// EA-0148 step 1: where a new world of this build puts lake and river water, sampled block by block in windows around
// places, with the GLO-90 outlines as shipped ("base") and with every outline vertex moved half a mask cell north-west in
// memory ("shift": X - unit/(2 cols), Y + unit/(2 rows), vertices on a tile's edge kept on it), and with the works of
// 1318's canal areas (built from the same mask) moved too ("shiftall"). Nothing is written to the data store; the shifted
// files exist only in this process.
//
// The world is built as a server builds it (SavedWorldDigestTests' Build, from WorldParamsBuilder's params for a new world
// of the play style: earth-10 at 1:10, earth at 1:100), on the full GLO-90 store (FineElevation): EARTH_STORE, else the
// AgentBridge bench's ModData\earth. Each window is sampled on the 1:10 world's block grid; the 1:100 world is sampled at
// the same latitudes and longitudes (a tenth of a block apart). Per sample the probe writes the column's water and the
// GLO-90 surface there (FineElevation, as the world reads it); analyse.py compares them with the raw mask and heights.
//
// Further variants: "shiftflow" (and the flow file read on the pixel-is-point grid, ShiftFlow), "shiftcourses" (and the
// snapped courses' inner vertices moved, the network rebuilt); a suffix "-allnarrow" keeps every narrow river piece and
// "-ch4" builds with channel rules 4 (controls). Mode "courses" dumps the 1:10 world's course vertices in 1° squares.
//
// Usage: dotnet run -c Release --project <this folder> -- <repo root> <out dir> <places file> [scales, e.g. 10,100] [variants base,shift]
//        ... -- <repo root> <out dir> <places file> courses "lat,lon;lat,lon"     (squares by south-west corner)
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
        foreach (var pl in places)
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    FlowTiles.Add(((int)Math.Floor(pl.Lat) + a + 90) * 360 + ((int)Math.Floor(pl.Lon) + b + 180 + 360) % 360);
        int[] scales = args.Length > 3 && args[3] == "courses" ? Array.Empty<int>() : (args.Length > 3 ? args[3] : "10,100").Split(',').Select(int.Parse).ToArray();
        string[] variants = (args.Length > 4 ? args[4] : "base,shift").Split(',');
        string store = Environment.GetEnvironmentVariable("EARTH_STORE");
        if (string.IsNullOrEmpty(store))
            store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VintagestoryData-agentbridge", "ModData", "earth");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("store: " + store);
        int tiles = Directory.Exists(Path.Combine(store, "glo90")) ? Directory.GetFiles(Path.Combine(store, "glo90"), "*.egl").Length : 0;
        Console.WriteLine("GLO-90 tiles in the store: " + tiles);
        if (tiles < 26000) { Console.WriteLine("the store lacks the GLO-90 tiles: the model would fall back to the built-in heights"); return 2; }
        var data = LoadData(root, store);
        if (args.Length > 4 && args[3] == "courses")
        {
            // every course vertex of the 1:10 world's list in the named 1° squares (south-west corners, "56,24;-4,-60")
            var p10 = MakeParams(root, data, 10);
            var m10 = Build(root, store, data, p10, false, false, false, out _);
            var squares = args[4].Split(';').Select(s => s.Split(',')).Select(f => (int.Parse(f[0]), int.Parse(f[1]))).ToHashSet();
            // kind: unmoved (rivers-glo90.bin's own vertices), moved (rivers-glo90-snap-2.bin moved the reach), arm
            // (rivers-glo90-arms-2.bin's); prepared: the world's prepared list has the same vertices
            var sb = new StringBuilder("reach,vertex,last,lat,lon,q,width_m,kind,prepared\n");
            var plain = data.RiverReaches(1);
            var src = data.RiverReaches(p10.RiverCourses);
            var list = m10.Rivers.Reaches;
            static bool Same(RiverReach a, RiverReach b) => a.Lat != null && b.Lat != null && a.Lat.AsSpan().SequenceEqual(b.Lat) && a.Lon.AsSpan().SequenceEqual(b.Lon);
            for (int r = 0; r < src.Count; r++)
            {
                var re = src[r];
                if (re.Lat == null) continue;
                string kind = r >= plain.Count ? "arm" : Same(re, plain[r]) ? "unmoved" : "moved";
                bool prepared = r < list.Count && Same(re, list[r]);
                for (int v = 0; v < re.Lat.Length; v++)
                    if (squares.Contains(((int)Math.Floor(re.Lat[v]), (int)Math.Floor(re.Lon[v]))))
                        sb.Append($"{r},{v},{re.Lat.Length - 1},{re.Lat[v]:R},{re.Lon[v]:R},{re.Q:R},{(re.Width != null ? re.Width[v] * RiverReach.WidthUnit : -1):R},{kind},{(prepared ? 1 : 0)}\n");
            }
            Console.WriteLine($"courses: rivers-glo90.bin {plain.Count} reaches, with its courses rules' snap and arms {src.Count}, prepared {list.Count}");
            File.WriteAllText(Path.Combine(outDir, "courses-squares.csv"), sb.ToString());
            // EA-0160: how many reaches river ends rules 2 ends at the sea (cut) or leaves out (past a mouth), worldwide
            var srcEnds = p10.RiverSource(data, null, out _, out _, null, out _, out _);
            var released = OutletData.ReleaseRiverPoints(data, srcEnds);
            var e1 = EarthMap.Core.RiverEnds.Prepare(data, released, null);
            var e2 = EarthMap.Core.RiverEnds.Prepare(data, released, m10.CoastSign);
            var sbE = new StringBuilder("reach,end,q,lat0,lon0,lat1,lon1,vertices_before,vertices_after\n");
            int atSea = 0, pastMouth = 0;
            foreach (var (r, end) in e2.Ends)
            {
                if (end != EarthMap.Core.RiverEnds.End.AtSea && end != EarthMap.Core.RiverEnds.End.PastMouth) continue;
                if (end == EarthMap.Core.RiverEnds.End.AtSea) atSea++; else pastMouth++;
                var b = e1.Reaches[r]; var a = e2.Reaches[r];
                sbE.Append($"{r},{end},{b.Q:R},{b.Lat[0]:R},{b.Lon[0]:R},{b.Lat[^1]:R},{b.Lon[^1]:R},{b.Lat.Length},{a.Lat.Length}\n");
            }
            File.WriteAllText(Path.Combine(outDir, "river-ends-2.csv"), sbE.ToString());
            // and the vertices of those in the named squares, under both rules
            var sbV = new StringBuilder("reach,rules,vertex,lat,lon,level,fixed\n");
            foreach (var (r, end) in e2.Ends)
            {
                if (end != EarthMap.Core.RiverEnds.End.AtSea && end != EarthMap.Core.RiverEnds.End.PastMouth) continue;
                var b = e1.Reaches[r];
                bool inside = false;
                for (int v = 0; v < b.Lat.Length && !inside; v++) inside = squares.Contains(((int)Math.Floor(b.Lat[v]), (int)Math.Floor(b.Lon[v])));
                if (!inside) continue;
                foreach (var (rules, re) in new[] { (1, b), (2, e2.Reaches[r]) })
                    for (int v = 0; v < re.Lat.Length; v++)
                        sbV.Append($"{r},{rules},{v},{re.Lat[v]:R},{re.Lon[v]:R},{re.Level[v]},{(re.Fixed != null && re.Fixed[v] ? 1 : 0)}\n");
            }
            File.WriteAllText(Path.Combine(outDir, "river-ends-2-vertices.csv"), sbV.ToString());
            // EA-0183: the courses inside the water mask's lakes (the 1:10 world's lake outlines), worldwide: every reach of
            // the prepared list looked at every 50 m; per lake piece (tile, index) the length inside and the reaches
            if (m10.LakeOutlines != null)
            {
                var inLake = new System.Collections.Concurrent.ConcurrentDictionary<(int, int), (double km, int reaches, float lat, float lon, float level)>();
                var reachesIn = new int[1];
                var list2 = e2.Reaches;
                // lake rules 1 (decision 0100): where the course still draws inside a lake (within its channel and bank of
                // the shore, where a river comes in or leaves), as TerrainModel.CourseInLake decides it (by the reach's flow)
                double stillKm = 0, offKm = 0;
                var stillLakes = new System.Collections.Concurrent.ConcurrentDictionary<(int, int), double>();
                object sum = new object();
                Parallel.For(0, list2.Count, r =>
                {
                    var re = list2[r];
                    if (re.Lat == null || re.Lat.Length < 2 || re.Q <= 0) return;
                    var seen = new HashSet<(int, int)>();
                    double margin = m10.Rivers != null ? Math.Min(m10.Rivers.HalfWidth(re.Q) + m10.Rivers.Bank(re.Q), m10.LakeOutlines.Reach - 1) : 0;
                    double still = 0, off = 0;
                    for (int v = 0; v + 1 < re.Lat.Length; v++)
                    {
                        double la0 = re.Lat[v], lo0 = re.Lon[v], la1 = re.Lat[v + 1], lo1 = re.Lon[v + 1];
                        if (Math.Abs(lo1 - lo0) > 180) continue;
                        double len = Math.Sqrt(Math.Pow((la1 - la0) * Geo.MetresPerDegree, 2) + Math.Pow((lo1 - lo0) * Geo.MetresPerDegree * Math.Cos(la0 * Math.PI / 180), 2));
                        int m = Math.Max(1, (int)Math.Ceiling(len / 50));
                        for (int i = 0; i < m; i++)
                        {
                            double t = (i + 0.5) / m, la = la0 + (la1 - la0) * t, lo = lo0 + (lo1 - lo0) * t;
                            var h = m10.LakeOutlines.At(la, lo);
                            if (!h.Inside) continue;
                            var key = ((int)Math.Round(h.Level * 10), (int)Math.Min(int.MaxValue, Math.Round(h.Area)));   // (a piece: its level and area; the tile and index are internal)
                            bool first = seen.Add(key);
                            if (h.Dist < margin) { still += len / m / 1000; stillLakes.AddOrUpdate(key, len / m / 1000, (_, o) => o + len / m / 1000); }
                            else off += len / m / 1000;
                            inLake.AddOrUpdate(key, _ => (len / m / 1000, 1, (float)la, (float)lo, (float)h.Level),
                                (_, o) => (o.km + len / m / 1000, o.reaches + (first ? 1 : 0), o.lat, o.lon, o.level));
                        }
                    }
                    if (seen.Count > 0) Interlocked.Increment(ref reachesIn[0]);
                    lock (sum) { stillKm += still; offKm += off; }
                });
                var sbL = new StringBuilder("level10,area_blocks2,km,reaches,lat,lon,level\n");
                foreach (var kv in inLake.OrderByDescending(kv => kv.Value.km))
                    sbL.Append($"{kv.Key.Item1},{kv.Key.Item2},{kv.Value.km:F3},{kv.Value.reaches},{kv.Value.lat:R},{kv.Value.lon:R},{kv.Value.level:R}\n");
                File.WriteAllText(Path.Combine(outDir, "courses-in-lakes.csv"), sbL.ToString());
                Console.WriteLine($"courses in the mask's lakes: {inLake.Count} lake pieces, {inLake.Values.Sum(v => v.km):F0} km, {reachesIn[0]} reaches");
                Console.WriteLine($"lake rules 1: the courses draw nothing along {offKm:F0} km of that; they still draw along {stillKm:F0} km within their channel and bank of a shore, in {stillLakes.Count} lake pieces ({stillLakes.Count(kv => kv.Value >= 1)} with a kilometre or more)");
            }
            Console.WriteLine($"river ends rules 2: {atSea} reaches end at the sea (cut), {pastMouth} left out past a mouth, of {released.Count}");
            Console.WriteLine($"courses: {list.Count} reaches in the 1:10 world's list (RiverCourses {p10.RiverCourses})");
            return 0;
        }

        // the sample grid: the 1:10 world's blocks
        double[][] lats = null, lons = null;
        foreach (int scale in scales)
        {
            var p = MakeParams(root, data, scale);
            Console.WriteLine($"1:{scale}: FineElevation {p.FineElevation}, LakeOutlines {p.LakeOutlines}, RiverOutlines {p.RiverOutlines}, DamRules {p.DamRules}, FlowRules {p.FlowRules}, " +
                $"ChannelRules {p.ChannelRules}, GenFixRules {p.GenFixRules}, RiverCourses {p.RiverCourses}, DropsNarrow {p.DropsNarrowRiverPieces}");
            foreach (string variant in variants)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // a variant ending in "-allnarrow" keeps every narrow river piece (no course or flow test): a control
                bool allNarrow = variant.EndsWith("-allnarrow");
                string v0 = allNarrow ? variant[..^"-allnarrow".Length] : variant;
                // a variant ending in "-ch4" builds the world with channel rules 4 (no pond trimming, decision 0074): a control
                bool ch4 = v0.EndsWith("-ch4");
                if (ch4) v0 = v0[..^"-ch4".Length];
                int channelRules = p.ChannelRules;
                if (ch4) p.ChannelRules = Math.Min(p.ChannelRules, 4);
                // "-ends1": river ends rules 1 (no end at the sea, EA-0160): a control
                bool ends1 = v0.EndsWith("-ends1");
                if (ends1) v0 = v0[..^"-ends1".Length];
                int riverEnds = p.RiverEnds;
                if (ends1) p.RiverEnds = Math.Min(p.RiverEnds, 1);
                // "-lake0": lake rules 0 (courses cut through lakes, decision 0100's control)
                bool lake0 = v0.EndsWith("-lake0");
                if (lake0) v0 = v0[..^"-lake0".Length];
                int lakeRules = p.LakeRules;
                if (lake0) p.LakeRules = 0;
                var model = Build(root, store, data, p, v0 != "base", v0 is "shiftall" or "shiftflow" or "shiftcourses", allNarrow, out string files,
                    v0 is "shiftflow" or "shiftcourses", v0 == "shiftcourses");
                p.ChannelRules = channelRules;
                p.RiverEnds = riverEnds;
                p.LakeRules = lakeRules;
                Console.WriteLine($"  {variant}: model built in {sw.Elapsed.TotalSeconds:0} s; {files}; FineRules {model.FineRules}, fine {(model.Fine != null ? "GLO-90 store" : "none")}");
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
                    Sample(model, p, places[k], lats[k], lons[k], scale, variant, file);
                    int missed = FineElevation.MissedReads - missed0;
                    Console.WriteLine($"    {places[k].Name}: {lats[k].Length} x {lons[k].Length} in {t.Elapsed.TotalSeconds:0} s, built-in height reads {missed}");
                    if (variant == variants[0]) Courses(model, places[k], Path.Combine(outDir, $"{places[k].Name}_{scale}_courses.csv"));
                }
            }
        }
        return 0;
    }

    static double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);

    /// <summary>The window's rows (north first) and columns as latitudes and longitudes of the 1:10 world's blocks.</summary>
    static void Grid(EarthGeometry geo, Place pl, out double[] lat, out double[] lon)
    {
        geo.FromGeo(pl.Lat + pl.HalfLat, pl.Lon - pl.HalfLon, out double xa, out double za);
        geo.FromGeo(pl.Lat - pl.HalfLat, pl.Lon + pl.HalfLon, out double xb, out double zb);
        long x0 = (long)Math.Floor(xa), z0 = (long)Math.Floor(za);
        // EA0148_STRIDE: every Nth block of the 1:10 world (whole lakes in one window)
        int stride = Math.Max(1, int.TryParse(Environment.GetEnvironmentVariable("EA0148_STRIDE"), out int st) ? st : 1);
        int nx = (int)Math.Round((xb - xa) / stride), nz = (int)Math.Round((zb - za) / stride);
        lat = new double[nz]; lon = new double[nx];
        for (int j = 0; j < nz; j++) lat[j] = geo.ToGeo(x0, z0 + (long)j * stride).Lat;
        for (int i = 0; i < nx; i++) lon[i] = geo.ToGeo(x0 + (long)i * stride, z0).Lon;
    }

    // ------------------------------------------------------------------ sampling

    static void Sample(TerrainModel model, EarthParams p, Place pl, double[] lat, double[] lon, int scale, string variant, string file)
    {
        int nz = lat.Length, nx = lon.Length, n = nz * nx;
        var flags = new ushort[n];
        var top = new short[n];
        var wtop = new short[n];
        var fine = new float[n];
        var elev = new float[n];
        var lakeLevel = new float[n];
        bool bounded = model.FineRules >= 5;
        Parallel.For(0, nz, j =>
        {
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                model.Geometry.FromGeo(lat[j], lon[i], out double x, out double z);
                if (scale == 10) { x = Math.Round(x); z = Math.Round(z); }
                var c = model.GetColumn(x, z);
                ushort f = 0;
                if (c.WaterTop >= 0 && c.WaterTop > c.Top) f |= 1;
                if (c.River) f |= 2;
                if (c.OutlineLake) f |= 4;
                if (c.Water == TerrainModel.WaterLake || c.Water == 3) f |= 8;
                if (c.Water == TerrainModel.WaterSea) f |= 16;
                if (c.Beach) f |= 32;
                if (model.CourseDrawsAt(lat[j], lon[i])) f |= 64;
                if (c.Still) f |= 128;
                if (c.RiverOutline > 0) f |= 256;
                if (model.FlowCarriesAt(lat[j], lon[i])) f |= 512;
                if (c.NearRiver) f |= 1024;
                if (c.LakeRim > 0) f |= 2048;
                float lv = float.NaN;
                if (model.LakeOutlines != null) { var h = model.LakeOutlines.At(lat[j], lon[i]); if (h.Inside) { f |= 4096; lv = (float)h.Level; } }
                if (model.RiverOutlines != null) { var h = model.RiverOutlines.At(lat[j], lon[i]); if (h.Inside) { f |= 8192; if (float.IsNaN(lv)) lv = (float)h.Level; } }
                flags[k] = f;
                top[k] = (short)Math.Clamp(c.Top, short.MinValue, short.MaxValue);
                wtop[k] = (short)Math.Clamp(c.WaterTop, short.MinValue, short.MaxValue);
                elev[k] = (float)c.Elev;
                lakeLevel[k] = lv;
                fine[k] = model.Fine != null && model.Fine.TrySmoothHeight(lat[j], lon[i], bounded, out double fh) ? (float)fh : float.NaN;
            }
        });
        // the GLO-90 heights at the grid points nearest the place's centre, as the world reads them (pixel-is-point):
        // analyse.py checks them against the raw DEM's cells
        var pts = new StringBuilder();
        int latS = (int)Math.Ceiling(pl.Lat) - 1, cols = DemTiles.Glo90Cols(latS);
        int r0 = (int)Math.Round((latS + 1 - pl.Lat) * DemTiles.Glo90Rows), c0 = (int)Math.Round((pl.Lon - Math.Floor(pl.Lon)) * cols);
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                int r = r0 + dr, c = c0 + dc;
                double la = latS + 1 - (double)r / DemTiles.Glo90Rows, lo = Math.Floor(pl.Lon) + (double)c / cols;
                double h = double.NaN;
                bool ok = model.Fine != null && model.Fine.TryHeight(la, lo, out h);
                pts.Append(FormattableString.Invariant($"[{r},{c},{(ok ? h : double.NaN):R}],"));
            }
        // (the culture is the invariant one: Main sets it)
        string header = "{" + (
            $"\"place\":\"{pl.Name}\",\"scale\":{scale},\"variant\":\"{variant}\",\"nz\":{nz},\"nx\":{nx},\"lat0\":{lat[0]:R},\"lat1\":{lat[^1]:R},\"lon0\":{lon[0]:R},\"lon1\":{lon[^1]:R}," +
            $"\"bpdx\":{model.Geometry.BlocksPerDegreeX:R},\"bpdz\":{model.Geometry.BlocksPerDegreeZ:R},\"fineRules\":{model.FineRules},\"lakeOutlines\":{p.LakeOutlines},\"riverOutlines\":{p.RiverOutlines}," +
            $"\"y0\":{model.Vertical.Y(0):R},\"y100\":{model.Vertical.Y(100):R},\"yTrench\":{model.Vertical.Y(-VerticalScale.Trench):R},\"mapHeight\":{model.Vertical.MapHeight},\"latS\":{latS},\"gridPoints\":[{pts.ToString().TrimEnd(',')}]") + "}";
        using var fs = File.Create(file);
        using var bw = new BinaryWriter(fs);
        byte[] hb = Encoding.UTF8.GetBytes(header + "\n");
        bw.Write(hb);
        foreach (var v in lat) bw.Write(v);
        foreach (var v in lon) bw.Write(v);
        foreach (var v in flags) bw.Write(v);
        foreach (var v in top) bw.Write(v);
        foreach (var v in wtop) bw.Write(v);
        foreach (var v in fine) bw.Write(v);
        foreach (var v in elev) bw.Write(v);
        foreach (var v in lakeLevel) bw.Write(v);
    }

    /// <summary>The river courses (the world's own list, as prepared) with a vertex within the window or 2 km of it.</summary>
    static void Courses(TerrainModel model, Place pl, string file)
    {
        var sb = new StringBuilder("reach,vertex,lat,lon,q,width_m\n");
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
                    sb.Append(FormattableString.Invariant($"{r},{v},{re.Lat[v]:R},{re.Lon[v]:R},{re.Q:R},{(re.Width != null ? re.Width[v] * RiverReach.WidthUnit : -1):R}\n"));
            }
        }
        File.WriteAllText(file, sb.ToString());
    }

    // ------------------------------------------------------------------ the model as a server builds it

    static EarthData LoadData(string root, string store) => EarthData.Load(name =>
    {
        string a = Path.Combine(root, "earthdata", name);
        if (File.Exists(a)) return File.OpenRead(a);
        string b = Path.Combine(store, "data", name);
        return File.Exists(b) ? File.OpenRead(b) : null;
    }, true);

    /// <summary>SavedWorldDigestTests' Build; with shift, the lake and river outlines moved half a mask cell north-west, with
    /// works, the works of 1318's canal areas too.</summary>
    static TerrainModel Build(string root, string store, EarthData data, EarthParams p, bool shift, bool works, bool allNarrow, out string files, bool shiftFlow = false, bool shiftCourses = false)
    {
        string g = Path.Combine(store, "glo90");
        // EA0148_FILES: a folder with newly baked files (lakes, rivers, dams, flow), read before the store's
        string extra = Environment.GetEnvironmentVariable("EA0148_FILES");
        byte[] Bytes(string name) => !string.IsNullOrEmpty(extra) && File.Exists(Path.Combine(extra, name)) ? File.ReadAllBytes(Path.Combine(extra, name)) : File.ReadAllBytes(Path.Combine(g, name));
        string damName = DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules));
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(Bytes(damName)) : null;
        string flowName = RiverFlowData.FileNameFor(Math.Min(p.FlowRules, RiverFlowData.FileVersion));
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(shiftFlow ? ShiftFlow(Bytes(flowName), FlowTiles) : Bytes(flowName)) : null;
        var model = p.BuildModel(data, dams, null, flow);
        if (shiftCourses && model.Rivers != null)
        {
            // the courses rivers-glo90-snap-2.bin and -arms-2.bin moved (their inner vertices, made at pixel centres + 0.5)
            // half a cell north-west, the reach ends (HydroRIVERS' confluences and mouths) kept: what a re-snap on the
            // pixel-is-point grid would give away from the ends. The network is rebuilt from them as BuildModel builds it.
            var plain = data.RiverReaches(1);
            var src = p.RiverSource(data, dams, out _, out _, p.FlowRules >= 1 ? flow : null, out _, out _);
            int movedReaches = 0;
            for (int r = 0; r < src.Count; r++)
            {
                var re = src[r];
                if (re.Lat == null || re.Lat.Length < 3) continue;
                if (r < plain.Count && plain[r].Lat != null && re.Lat.AsSpan().SequenceEqual(plain[r].Lat) && re.Lon.AsSpan().SequenceEqual(plain[r].Lon)) continue;
                var la = (float[])re.Lat.Clone();
                var lo = (float[])re.Lon.Clone();
                for (int v = 1; v < la.Length - 1; v++)
                {
                    int latS = (int)Math.Floor(la[v]);
                    la[v] = (float)(la[v] + 0.5 / DemTiles.Glo90Rows);
                    lo[v] = (float)(lo[v] - 0.5 / DemTiles.Glo90Cols(latS));
                }
                re.Lat = la; re.Lon = lo;
                movedReaches++;
            }
            model.Rivers = p.BuildRivers(data, model, src);
            Console.WriteLine($"    courses: {movedReaches} snapped or armed reaches moved half a cell north-west");
        }
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
            byte[] b = Bytes(lakeName);
            if (shift) b = Shift(b);
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(b), model.Geometry);
            // lake rules 1 (decision 0100): the lakes' real depths, as EarthElevationSystem attaches them
            if (p.LakeRules >= 1) model.LakeDepths = data.LakeDepthsFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion));
        }
        if (p.RiverOutlines >= 1)
        {
            byte[] b = Bytes(RiverOutlineData.FileName(rv));
            if (shift) b = Shift(b);
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(b), model.Geometry, p.DropsNarrowRiverPieces && !allNarrow, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
        }
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
        if (works && model.Works != null)
        {
            // as EarthParams.BuildModel makes the layer, from the canal areas moved (the RCE areas are vectors of their own)
            byte[] b = Shift(File.ReadAllBytes(Path.Combine(root, "earthdata", Works1318Data.FileName)), Works1318Data.Canal);
            model.Works = new LakeOutlineLayer(Works1318Data.Read(b), model.Geometry, model.Coasts.Reach, skipFlags: Works1318Data.SkipFor(p.Works1318));
            files += ", works-1318-2.bin (canals moved)";
        }
        return model;
    }

    // ------------------------------------------------------------------ the flow file half a cell north-west (in memory)

    /// <summary>The flow tiles the trial moves (the places' squares and their neighbours); the rest are copied as they are.</summary>
    static readonly HashSet<int> FlowTiles = new();

    /// <summary>
    /// rivers-flow-glo90-1.bin as a reader on GLO-90's pixel-is-point grid would read it: RiverFlowData.At takes pixel (i, j)
    /// as the cell from i/H to (i + 1)/H below the tile's north edge (centre (i + 0.5)/H), where the mask's cell i is
    /// centred on i/H. Each moved tile is written at twice the resolution with each of its pixels from the mask cell whose
    /// centre is nearest (pixel i' of 2H takes cell (i' + 1) / 2; the last half cell, which belongs to the next tile's first
    /// row or column, repeats the tile's last), so the unchanged reader puts every cell on its own centre. The reroutes'
    /// vertices (made at pixel centres + 0.5) move half a cell north-west too.
    /// </summary>
    static byte[] ShiftFlow(byte[] file, HashSet<int> moved)
    {
        int tx = BitConverter.ToInt32(file, 12), ty = BitConverter.ToInt32(file, 16), trailer = BitConverter.ToInt32(file, 28);
        var states = new byte[tx * ty];
        Buffer.BlockCopy(file, 32, states, 0, states.Length);
        int o = 32 + states.Length;
        var blobs = new byte[states.Length][];
        for (int t = 0; t < states.Length; t++)
        {
            if (states[t] != RiverFlowData.HasFlow) continue;
            int off = BitConverter.ToInt32(file, o), len = BitConverter.ToInt32(file, o + 4);
            o += 8;
            var blob = new byte[len];
            Buffer.BlockCopy(file, off, blob, 0, len);
            blobs[t] = blob;
        }
        Parallel.ForEach(moved.Where(t => t >= 0 && t < states.Length && blobs[t] != null).ToArray(), t =>
        {
            byte[] raw;
            using (var ms = new MemoryStream(blobs[t])) using (var br = new BrotliStream(ms, CompressionMode.Decompress)) using (var mo = new MemoryStream()) { br.CopyTo(mo); raw = mo.ToArray(); }
            int h = BitConverter.ToInt32(raw, 0), w = BitConverter.ToInt32(raw, 4), H = 2 * h, W = 2 * w;
            var nraw = new byte[8 + 2 * H * W];
            BitConverter.GetBytes(H).CopyTo(nraw, 0);
            BitConverter.GetBytes(W).CopyTo(nraw, 4);
            for (int i = 0; i < H; i++)
                for (int j = 0; j < W; j++)
                {
                    int si = Math.Min((i + 1) / 2, h - 1), sj = Math.Min((j + 1) / 2, w - 1);
                    nraw[8 + i * W + j] = raw[8 + si * w + sj];
                    nraw[8 + H * W + i * W + j] = raw[8 + h * w + si * w + sj];
                }
            var outMs = new MemoryStream();
            using (var bw = new BrotliStream(outMs, CompressionLevel.Fastest, true)) bw.Write(nraw, 0, nraw.Length);
            blobs[t] = outMs.ToArray();
        });
        var res = new MemoryStream();
        var wr = new BinaryWriter(res);
        wr.Write(file, 0, 28);
        int count = blobs.Count(b => b != null);
        int data0 = 32 + states.Length + 8 * count;
        int newTrailer = data0 + blobs.Where(b => b != null).Sum(b => b.Length);
        wr.Write(newTrailer);
        wr.Write(states);
        int pos = data0;
        for (int t = 0; t < blobs.Length; t++) if (blobs[t] != null) { wr.Write(pos); wr.Write(blobs[t].Length); pos += blobs[t].Length; }
        for (int t = 0; t < blobs.Length; t++) if (blobs[t] != null) wr.Write(blobs[t]);
        // the reroutes: count, then (reach, after, m, m x (lat, lon))
        int n = BitConverter.ToInt32(file, trailer), q = trailer + 4;
        wr.Write(n);
        for (int k = 0; k < n; k++)
        {
            int reach = BitConverter.ToInt32(file, q), after = BitConverter.ToInt32(file, q + 4), m = BitConverter.ToInt32(file, q + 8);
            q += 12;
            wr.Write(reach); wr.Write(after); wr.Write(m);
            for (int i = 0; i < m; i++, q += 8)
            {
                float la = BitConverter.ToSingle(file, q), lo = BitConverter.ToSingle(file, q + 4);
                int latS = (int)Math.Floor(la);
                wr.Write((float)(la + 0.5 / DemTiles.Glo90Rows));
                wr.Write((float)(lo - 0.5 / DemTiles.Glo90Cols(latS)));
            }
        }
        wr.Flush();
        return res.ToArray();
    }

    // ------------------------------------------------------------------ the half-cell shift (in memory)

    /// <summary>
    /// An outline file (EMLO or EMRO) with every vertex half a GLO-90 cell north-west: Y + unit/(2 x 1200), X - unit/(2 x the
    /// tile's columns). Vertices on a tile's edge stay on it (they move along it only), and none leaves its tile: what a
    /// re-contour on the pixel-is-point grid, with the neighbours' facing cells and clipped to the tile, would give to within
    /// half a cell of the edge. Where half a cell is not a whole number of units (2,400 a degree at 800 columns) the unit
    /// is doubled.
    /// </summary>
    static byte[] Shift(byte[] file, byte onlyFlags = 0)
    {
        string magic = Encoding.ASCII.GetString(file, 0, 4);
        var d = magic == "EMRO" ? RiverOutlineData.Read(file) : magic == Works1318Data.Magic ? Works1318Data.Read(file) : LakeOutlineData.Read(file);
        int unit = d.Unit, deg = d.TileDeg, tx = d.TilesX, ty = d.TilesY;
        int k = new[] { 1200, 800, 600, 400, 240, 120 }.All(c => unit % (2 * c) == 0) && unit % (2 * DemTiles.Glo90Rows) == 0 ? 1 : 2;
        int nu = unit * k, full = nu * deg;
        var states = new byte[tx * ty];
        Buffer.BlockCopy(file, 24, states, 0, states.Length);
        var blobs = new byte[tx * ty][];
        Parallel.For(0, tx * ty, t =>
        {
            if (d.State(t) != LakeOutlineData.HasLakes) return;
            d.Decode(t, out float[] level, out byte[] flags, out int[] ringLake, out int[] ringLength, out int[] vx, out int[] vy);
            int latS = t / tx * deg - 90;
            int hx = nu / (2 * DemTiles.Glo90Cols(latS)), hy = nu / (2 * DemTiles.Glo90Rows);
            var ms = new MemoryStream();
            Varint(ms, (ulong)level.Length);
            int ring = 0, v = 0;
            for (int lake = 0; lake < level.Length; lake++)
            {
                Varint(ms, Zig((long)Math.Round(level[lake] * 10.0)));
                Varint(ms, flags[lake]);
                int first = ring;
                while (ring < ringLake.Length && ringLake[ring] == lake) ring++;
                Varint(ms, (ulong)(ring - first));
                bool move = onlyFlags == 0 || (flags[lake] & onlyFlags) != 0;
                long px = 0, py = 0;
                for (int r = first; r < ring; r++)
                {
                    Varint(ms, (ulong)ringLength[r]);
                    for (int j = 0; j < ringLength[r]; j++, v++)
                    {
                        long x = (long)vx[v] * k, y = (long)vy[v] * k;
                        long sx = !move || x == 0 || x == full ? x : Math.Clamp(x - hx, 0, full);
                        long sy = !move || y == 0 || y == full ? y : Math.Clamp(y + hy, 0, full);
                        Varint(ms, Zig(sx - px)); Varint(ms, Zig(sy - py));
                        px = sx; py = sy;
                    }
                }
            }
            if (ring != ringLake.Length || v != vx.Length) throw new InvalidDataException("rings out of lake order in tile " + t);
            var outMs = new MemoryStream();
            using (var br = new BrotliStream(outMs, CompressionLevel.Fastest, true)) br.Write(ms.GetBuffer(), 0, (int)ms.Length);
            blobs[t] = outMs.ToArray();
        });
        var o = new MemoryStream();
        var w = new BinaryWriter(o);
        w.Write(file, 0, 8);   // magic, version
        w.Write(nu); w.Write(deg); w.Write(tx); w.Write(ty);
        w.Write(states);
        int off = 24 + states.Length + 8 * blobs.Count(b => b != null);
        for (int t = 0; t < blobs.Length; t++)
            if (blobs[t] != null) { w.Write(off); w.Write(blobs[t].Length); off += blobs[t].Length; }
        for (int t = 0; t < blobs.Length; t++) if (blobs[t] != null) w.Write(blobs[t]);
        w.Flush();
        return o.ToArray();
    }

    static ulong Zig(long v) => (ulong)((v << 1) ^ (v >> 63));

    static void Varint(Stream s, ulong v)
    {
        while (v >= 0x80) { s.WriteByte((byte)(v | 0x80)); v >>= 7; }
        s.WriteByte((byte)v);
    }

    // ------------------------------------------------------------------ a new world's params

    /// <summary>A new world of the play style for this scale (earth-10 at 1:10, earth at 1:100, earth-&lt;n&gt; else), seed
    /// 386217, from WorldParamsBuilder (SavedWorldDigestTests' Make, with the play style's height).</summary>
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
