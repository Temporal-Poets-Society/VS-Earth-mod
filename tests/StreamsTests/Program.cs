using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using EarthMap;
using EarthMap.Core;

// EA-0131 (decision 0061): the streams layer.
//   dotnet run -c Release --project tests\StreamsTests -- <repo root>
//       the file's layout read back (StreamData) and the layer's rules offline
//   ... -- <repo root> courses <out.bin>
//       the frozen course list a stream bake snaps against: rivers-glo90.bin with the newest courses rules' snap applied
//       through the game's own reader (RiverSnapData; courses rules 5: rivers-glo90-snap-3.bin), in rivers-glo90.bin's
//       order with its reach ends, written as a rivers file of record version 3 (tools/streams/build_stream_input.py)
//   ... -- <repo root> probe <streams file> [name=lat,lon[,size]] ...
//       a new 1:10 world's model (this build's defaults, the earth-10 preset, full GLO-90; the data store as
//       SavedWorldDigestTests finds it) with the streams file: per place a window of size x size blocks (default 1024) drawn,
//       settled as the generator settles chunks, its stream water checked for water standing beside lower dry land (a leak),
//       its steps and falls counted, the cost of its columns with and without the streams, and a map in work\streams-layer;
//       chart=lat,lon[,size]: the window's streams the same after a thread named as World Map's world chart (built-in heights)
//       asked about its tiles first.
static class Program
{
    static int checks, failures;
    static void Check(bool ok, string what)
    {
        checks++;
        if (ok) return;
        failures++;
        Console.WriteLine("FAIL: " + what);
    }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = args.Length > 0 ? args[0] : FindRoot();
        FileRoundTrip();
        KeepFromChecks();
        if (args.Length > 2 && args[1] == "probe") return Probe(root, args[2], args.Skip(3).ToArray());
        if (args.Length > 2 && args[1] == "courses") return Courses(root, args[2]);
        // with the stream file at hand (STREAMS_FILE, else the data store's GLO-90 folder): the layer in a new 1:10 world at two
        // places, its columns the same whatever order they are asked in, and no leaks
        string store = FindStore();
        string streams = Environment.GetEnvironmentVariable("STREAMS_FILE");
        if (string.IsNullOrEmpty(streams) && store != null) streams = Path.Combine(store, "glo90", StreamData.FileName);
        bool skipped = store == null || streams == null || !File.Exists(streams);
        if (skipped) Console.WriteLine("SKIP: the layer in a world needs the data store and " + StreamData.FileName + " (EARTH_STORE, STREAMS_FILE)");
        else Probe(root, streams, new[] { "order=46.62,8.04,256", "order=60.416,7.132,256", "chart=55.175,-3.06,1024", "lauterbrunnen=46.595,7.905,256", "dolomites=46.4145,11.8783,256" });
        Console.WriteLine(failures == 0 ? $"StreamsTests: {checks} checks passed" + (skipped ? " (the layer in a world skipped)" : "") : $"StreamsTests: {failures} of {checks} checks failed");
        return failures > 0 ? 1 : skipped ? 3 : 0;
    }

    /// <summary>(EA-0200, decision 0098) The Earth tab's Smallest stream: a system's headwaters below it go, and a stream below it
    /// that a kept one flows into stays, so every stream left still reaches the outlet.</summary>
    static void KeepFromChecks()
    {
        // two headwaters (0.5 and 3 m³/s) into a stretch that loses water (2), then a side stream (1.5) and the outlet (10)
        RiverReach R(float q, int next) => new RiverReach { Q = q, Next = next, Lon = new float[2], Lat = new float[2], Level = new short[2], Fixed = new bool[2] };
        var sys = new List<RiverReach> { R(0.5f, 2), R(3, 2), R(2, 4), R(1.5f, 4), R(10, -1) };
        var kept = StreamLayer.KeepFrom(sys, 2.5);
        Check(kept.Count == 3 && kept[0].Q == 3 && kept[1].Q == 2 && kept[2].Q == 10, "Smallest stream 2.5: the 0.5 and 1.5 headwaters go, the 2 m³/s stretch below a kept one stays");
        Check(kept[0].Next == 1 && kept[1].Next == 2 && kept[2].Next == -1, "Smallest stream: the kept reaches still flow to the outlet");
        sys = new List<RiverReach> { R(0.5f, 1), R(1.5f, -1) };
        Check(StreamLayer.KeepFrom(sys, 2).Count == 0, "Smallest stream above a whole system leaves none of it");
    }

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "EarthMap.csproj"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    // ------------------------------------------------------------------ the file

    static void Var(List<byte> b, ulong v) { while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; } b.Add((byte)v); }
    static void Zig(List<byte> b, long v) => Var(b, (ulong)((v << 1) ^ (v >> 63)));

    /// <summary>A streams file as tools/streams/bake_stream_tiles.py writes it: one tile (57° N, 24° E) with a system of two
    /// reaches joining a large river, and one visitor (the tile to the west); read back exactly.</summary>
    static void FileRoundTrip()
    {
        const int unit = 100000;
        var body = new List<byte>();
        Var(body, 1);                         // systems
        Var(body, StreamData.EndRiver);
        Var(body, 2);                         // reaches
        // reach 0: 1.25 m³/s into reach 1, three vertices
        Var(body, 125); Var(body, 2); Var(body, 3);
        long[] x0 = { 1000, 1500, 2500 }, y0 = { 50000, 49000, 48000 }; short[] l0 = { 80, 75, 60 };
        long px = 0, py = 0;
        for (int k = 0; k < 3; k++) { Zig(body, x0[k] - px); Zig(body, y0[k] - py); px = x0[k]; py = y0[k]; }
        long pl = 0;
        for (int k = 0; k < 3; k++) { Zig(body, l0[k] - pl); pl = l0[k]; }
        // reach 1: 4.5 m³/s, the outlet, two vertices (the first west of the tile)
        Var(body, 450); Var(body, 0); Var(body, 2);
        long[] x1 = { 2500, -300 }, y1 = { 48000, 47000 }; short[] l1 = { 60, 12 };
        px = py = pl = 0;
        for (int k = 0; k < 2; k++) { Zig(body, x1[k] - px); Zig(body, y1[k] - py); px = x1[k]; py = y1[k]; }
        for (int k = 0; k < 2; k++) { Zig(body, l1[k] - pl); pl = l1[k]; }
        Var(body, 1); Zig(body, -1); Zig(body, 0);   // a visitor: the tile to the west
        byte[] packed;
        using (var ms = new MemoryStream())
        {
            using (var bs = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) bs.Write(body.ToArray());
            packed = ms.ToArray();
        }
        int tile = (57 + 90) * 360 + (24 + 180);
        var file = new List<byte>();
        file.AddRange("EMST"u8.ToArray());
        foreach (int v in new[] { 1, unit, 1, 360, 180 }) file.AddRange(BitConverter.GetBytes(v));
        var states = new byte[360 * 180];
        states[tile] = StreamData.HasData;
        file.AddRange(states);
        file.AddRange(BitConverter.GetBytes(file.Count + 8));
        file.AddRange(BitConverter.GetBytes(packed.Length));
        file.AddRange(packed);
        var d = StreamData.Read(file.ToArray());
        Check(d.TileAt(57.5, 24.5) == tile, "file: the tile at 57.5° N 24.5° E");
        Check(d.TileAt(57.5, 24.5 - 360) == tile, "file: longitudes wrap");
        Check(d.Step(tile, -1, 0) == tile - 1 && d.Step((90 + 10) * 360 + 0, -1, 0) == (90 + 10) * 360 + 359, "file: steps west, across the antimeridian");
        Check(d.State(tile - 1) == StreamData.NoData && d.Decode(tile - 1).Systems.Count == 0, "file: a tile without data decodes empty");
        var t = d.Decode(tile);
        Check(t.Systems.Count == 1 && t.Systems[0].EndKind == StreamData.EndRiver && t.Systems[0].Reaches.Count == 2, "file: one system of two reaches ending at a river");
        var r0 = t.Systems[0].Reaches[0];
        var r1 = t.Systems[0].Reaches[1];
        Check(Math.Abs(r0.Q - 1.25f) < 1e-6 && r0.Next == 1 && Math.Abs(r1.Q - 4.5f) < 1e-6 && r1.Next == -1, "file: discharges and the downstream reach");
        Check(r0.Lon.Length == 3 && Math.Abs(r0.Lon[1] - (24 + 1500.0 / unit)) < 1e-5 && Math.Abs(r0.Lat[2] - (57 + 48000.0 / unit)) < 1e-5, "file: vertices from the tile's corner");
        Check(r0.Level.SequenceEqual(l0) && r1.Level.SequenceEqual(l1), "file: levels");
        Check(Math.Abs(r1.Lon[1] - (24 - 300.0 / unit)) < 1e-5, "file: a vertex west of the tile");
        Check(r0.Fixed.All(f => !f), "file: no fixed vertices");
        Check(t.Visitors.Length == 1 && t.Visitors[0] == (-1, 0), "file: the visitor");
        var bad = file.ToArray();
        bad[4] = 9;
        bool threw = false;
        try { StreamData.Read(bad); } catch (InvalidDataException) { threw = true; }
        Check(threw, "file: another version is refused");
    }

    // ------------------------------------------------------------------ the probe

    static string FindStore()
    {
        string env = Environment.GetEnvironmentVariable("EARTH_STORE");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (string s in new[] { env, Path.Combine(appData, "VintagestoryData", "ModData", "earth"), Path.Combine(appData, "VintagestoryData-agentbridge", "ModData", "earth") })
            if (!string.IsNullOrEmpty(s) && Directory.Exists(Path.Combine(s, "data")) && Directory.Exists(Path.Combine(s, "glo90"))) return s;
        return null;
    }

    static EarthData LoadData(string root, string store) => EarthData.Load(name =>
    {
        string a = Path.Combine(root, "earthdata", name);
        if (File.Exists(a)) return File.OpenRead(a);
        string b = Path.Combine(store, "data", name);
        return File.Exists(b) ? File.OpenRead(b) : null;
    }, true);

    /// <summary>A new world's params of this build at a scale (the earth-10 preset's world config, Hydrology's query 3).</summary>
    static EarthParams MakeParams(string root, EarthData data, string scale, int height, string preset)
    {
        var wc = new Vintagestory.API.Datastructures.TreeAttribute();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json"))))
            foreach (var kv in doc.RootElement.GetProperty("playStyles").EnumerateArray().First(s => s.GetProperty("code").GetString() == preset).GetProperty("worldConfig").EnumerateObject())
                switch (kv.Value.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.True: case System.Text.Json.JsonValueKind.False: wc.SetBool(kv.Name, kv.Value.GetBoolean()); break;
                    case System.Text.Json.JsonValueKind.Number: wc.SetInt(kv.Name, kv.Value.GetInt32()); break;
                    default: wc.SetString(kv.Name, kv.Value.GetString()); break;
                }
        // STREAMS_RIVERWIDTH=250: the preset's River width (earthRiverWidth, %) replaced
        if (Environment.GetEnvironmentVariable("STREAMS_RIVERWIDTH") is { Length: > 0 } rw) wc.SetInt("earthRiverWidth", int.Parse(rw));
        // STREAMS_SMALLEST=20: the preset's Smallest stream (earthSmallestStream, m³/s; decision 0098) replaced
        if (Environment.GetEnvironmentVariable("STREAMS_SMALLEST") is { Length: > 0 } sm) wc.SetInt("earthSmallestStream", int.Parse(sm));
        SpawnSearch.SizeFor(scale, out int W, out int L, out _);
        var w = new NewWorld
        {
            MapSizeX = W, MapSizeY = height, SeaLevel = EarthWorldOptions.Terrain(wc, height).SeaLevel(height, 165), Seed = 386217, WorldConfig = wc, HydrologyQuery = 3,
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 672,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 70, CylinderKeepsEarthSeam = () => true,
        };
        var log = DispatchProxy.Create<Vintagestory.API.Common.ILogger, QuietLog>();
        var p = WorldParamsBuilder.Build(w, new EarthMapConfig(), data, log, out _, out string bad);
        if (bad.Length > 0) throw new InvalidOperationException("this build's defaults can't make terrain: " + bad);
        return p;
    }

    /// <summary>EarthMapSystem's model build and EarthElevationSystem.AttachServer, offline (as SavedWorldDigestTests).</summary>
    static TerrainModel Build(string root, string store, EarthData data, EarthParams p)
    {
        string g = Path.Combine(store, "glo90");
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(File.ReadAllBytes(Path.Combine(g, DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules))))) : null;
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(File.ReadAllBytes(Path.Combine(g, RiverFlowData.FileName))) : null;
        var model = p.BuildModel(data, dams, null, flow);
        var fine = new FineElevation(g, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
        fine.SetFallback(Array.Empty<int>());
        model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
        model.Fine = fine;
        if (dams != null && p.DamCourseError == null) fine.Dams = dams;
        if (p.LakeOutlines >= 1)
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(g, LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion))))), model.Geometry);
        // lake rules 1 (decision 0100): the lakes' real depths, as EarthElevationSystem attaches them
        if (p.LakeOutlines >= 1 && p.LakeRules >= 1) model.LakeDepths = data.LakeDepthsFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion));
        if (p.RiverOutlines >= 1)
        {
            int v = RiverOutlineData.Usable(p.RiverOutlines);
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(g, RiverOutlineData.FileName(v)))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
        }
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
        return model;
    }

    /// <summary>
    /// The course list the stream bake holds frozen, as a new world on GLO-90 lays its rivers out before the side arms: the
    /// store's rivers-glo90.bin with the newest courses rules' snap applied by RiverSnapData (the snap checks it was made
    /// for that rivers-glo90.bin), written as EMRV record version 3. The arms are left out: they come first in the world's
    /// list and would shift rivers.bin's order, which the bake matches junctions on; the streams join the arms as drawn.
    /// </summary>
    static int Courses(string root, string outPath)
    {
        string store = FindStore();
        if (store == null) { Console.WriteLine("courses: no data store (EARTH_STORE or ModData\\earth)"); return 3; }
        var data = LoadData(root, store);
        int rules = data.Glo90RiverCourses;
        Console.WriteLine($"courses: store {store}, courses rules {rules} (newest {RiverNetwork.Glo90CoursesVersion})");
        Check(rules == RiverNetwork.Glo90CoursesVersion, "the store gives a new world the newest courses rules");
        if (rules != RiverNetwork.Glo90CoursesVersion) return 1;
        var file = File.ReadAllBytes(Path.Combine(store, "data", "rivers-glo90.bin"));
        var list = RiverData.Read(file, "rivers-glo90.bin");
        int before = list.Count;
        var snap = rules >= RiverNetwork.Glo90PointCourses ? data.RiverSnap3 : data.RiverSnap2;
        snap.ApplyTo(list, EarthBinary.Sha8(file));
        Check(list.Count == before, "the snap keeps rivers-glo90.bin's reaches and order");
        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
            foreach (var r in list)
            {
                w.Write(r.Q);
                w.Write(r.Next);
                w.Write(r.Lon.Length);
                for (int k = 0; k < r.Lon.Length; k++)
                {
                    w.Write(r.Lon[k]); w.Write(r.Lat[k]); w.Write(r.Level[k]);
                    w.Write((byte)(r.Fixed != null && r.Fixed[k] ? 1 : 0));
                    w.Write(r.Width != null ? r.Width[k] : (byte)0);
                }
            }
        using (var o = File.Create(outPath))
        {
            o.Write("EMRV"u8);
            o.Write(BitConverter.GetBytes(3));
            o.Write(BitConverter.GetBytes(list.Count));
            using var b = new System.IO.Compression.BrotliStream(o, System.IO.Compression.CompressionLevel.Optimal, true);
            raw.Position = 0;
            raw.CopyTo(b);
        }
        // read back by the game's reader: the same reaches
        var back = RiverData.Read(File.ReadAllBytes(outPath), Path.GetFileName(outPath));
        bool same = back.Count == list.Count;
        for (int i = 0; same && i < list.Count; i++)
            same = back[i].Q == list[i].Q && back[i].Next == list[i].Next && back[i].Lon.AsSpan().SequenceEqual(list[i].Lon) && back[i].Lat.AsSpan().SequenceEqual(list[i].Lat)
                && back[i].Level.AsSpan().SequenceEqual(list[i].Level);
        Check(same, "the written course list reads back the same");
        Console.WriteLine($"courses: {list.Count} reaches written to {outPath} ({new FileInfo(outPath).Length:N0} bytes; rivers-glo90.bin {EarthBinary.Sha8(file):x16})");
        return same ? 0 : 1;
    }

    static int Probe(string root, string streamsFile, string[] places)
    {
        string store = FindStore();
        if (store == null) { Console.WriteLine("probe: no data store with GLO-90 (EARTH_STORE or ModData\\earth)"); return 3; }
        string outDir = Environment.GetEnvironmentVariable("STREAMS_OUT") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "..", "..", "..", "..", "work", "streams-layer");
        outDir = Path.GetFullPath(Directory.Exists(Path.Combine("C:\\VS mods", "work")) ? Path.Combine("C:\\VS mods", "work", "streams-layer") : outDir);
        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();
        var data = LoadData(root, store);
        // STREAMS_SCALE=25: the earth-25 preset's world instead of earth-10's (1:100's play style is "earth")
        string scale = Environment.GetEnvironmentVariable("STREAMS_SCALE") is { Length: > 0 } sc ? sc : "10";
        var p = MakeParams(root, data, scale, 1024, scale == "100" ? "earth" : "earth-" + scale);
        // STREAMS_LAKERULES=0: lake rules 0 (decision 0100's control: the streams outside lakes are the same)
        if (int.TryParse(Environment.GetEnvironmentVariable("STREAMS_LAKERULES"), out int lakeRules)) p.LakeRules = lakeRules;
        Console.WriteLine($"params: lake rules {p.LakeRules}, scale 1:{scale}, streams '{p.StreamMode}', stream rules {p.StreamRules}, pool layout {p.RiverPoolLayout}, courses {p.RiverCourses}, channel rules {p.ChannelRules} ({sw.Elapsed.TotalSeconds:F0} s)");
        // (1:50 and coarser have no streams layer: their windows measure the world network's own water, Window)
        if (scale is "10" or "25") Check(p.StreamRules == StreamLayer.RulesVersion, "a new 1:10 world gets the streams layer");
        var model = Build(root, store, data, p);
        Console.WriteLine($"model built ({sw.Elapsed.TotalSeconds:F0} s): {model.Rivers?.SegmentCount} river pieces");
        var file = File.ReadAllBytes(streamsFile);
        if (p.StreamRules >= 1) model.Streams = new StreamLayer(StreamData.Read(file, Path.GetFileName(streamsFile)), model, p.RiverOptions(data, model.Geometry))
            { SmallestQ = p.StreamLayerMinDischarge };
        if (p.StreamLayerMinDischarge > 0) Console.WriteLine($"  the streams layer from {p.StreamLayerMinDischarge} m³/s (Smallest stream, EA-0200)");
        if (Environment.GetEnvironmentVariable("RIDGELOG") == "1") StreamLayer.DebugLog = m => Console.WriteLine("  " + m);
        // DEBUGAT=lat,lon: the levels after each of the land fit's passes near that place
        if (Environment.GetEnvironmentVariable("DEBUGAT") is { Length: > 0 } at)
        {
            var dv = at.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            StreamLayer.DebugLat = dv[0]; StreamLayer.DebugLon = dv[1];
            StreamLayer.DebugLog ??= m => Console.WriteLine("  " + m);
        }
        if (places.Length == 0)
            places = new[] { "lauterbrunnen=46.595,7.905", "grindelwald=46.625,8.04", "bolzano=46.52,11.36", "eidfjord=60.44,7.10", "odda=60.06,6.56", "amata=57.255,25.14", "brasla=57.27,24.93" };
        foreach (string place in places)
        {
            var kv = place.Split('=');
            if (kv[0].StartsWith("order"))
            {
                var ov = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                OrderIndependent(model, StreamData.Read(file, Path.GetFileName(streamsFile)), p.RiverOptions(data, model.Geometry), ov[0], ov[1], ov.Length > 2 ? (int)ov[2] : 256);
                continue;
            }
            if (kv[0].StartsWith("chart"))
            {
                var hv = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                ChartFirst(model, StreamData.Read(file, Path.GetFileName(streamsFile)), p.RiverOptions(data, model.Geometry), hv[0], hv[1], hv.Length > 2 ? (int)hv[2] : 1024);
                continue;
            }
            if (kv[0].StartsWith("rawreach"))
            {
                // the stream file's own vertices (as baked, before the layer fits them) of the reach nearest a place
                var rv = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                double bestD = double.MaxValue; RiverReach bestR = null;
                foreach (int h in model.Streams.HomesAt(rv[0], rv[1]))
                    foreach (var sys in model.Streams.Data.Decode(h).Systems)
                        foreach (var re in sys.Reaches)
                            for (int k = 0; k < re.Lon.Length; k++)
                            {
                                double dy = (re.Lat[k] - rv[0]) * 111320, dx = (re.Lon[k] - rv[1]) * 111320 * Math.Cos(rv[0] * Math.PI / 180);
                                double d = dx * dx + dy * dy;
                                if (d < bestD) { bestD = d; bestR = re; }
                            }
                if (bestR == null) { Console.WriteLine("rawreach: none"); continue; }
                Console.WriteLine($"rawreach near {rv[0]},{rv[1]}: Q {bestR.Q:F2}, {bestR.Lon.Length} vertices, nearest {Math.Sqrt(bestD):F0} m");
                for (int k = 0; k < bestR.Lon.Length; k++)
                {
                    double gh = model.Fine != null && model.Fine.TryHeight(bestR.Lat[k], bestR.Lon[k], out double hh) ? hh : double.NaN;
                    Console.WriteLine($"  vertex {k,3} {bestR.Lat[k]:F5},{bestR.Lon[k]:F5}  baked level {bestR.Level[k],5} m  GLO-90 {gh,7:F1} m");
                }
                continue;
            }
            if (kv[0].StartsWith("shape"))
            {
                var sv = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                ShapeAt(model, sv[0], sv[1], sv.Length > 2 ? (int)sv[2] : 1024);
                continue;
            }
            if (kv[0] == "sweep")
            {
                Sweep(model, StreamData.Read(file, Path.GetFileName(streamsFile)), p.RiverOptions(data, model.Geometry), int.Parse(kv[1]));
                continue;
            }
            if (kv[0].StartsWith("tiles"))
            {
                // tiles=lat0,lat1,lon0,lon1: every 1° tile in the box asked at its centre, one thread, failures named
                var bv = kv[1].Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                int failed = 0, asked = 0;
                for (int la = bv[0]; la < bv[1]; la++)
                    for (int lo = bv[2]; lo < bv[3]; lo++)
                    {
                        asked++;
                        model.Geometry.FromGeo(la + 0.5, lo + 0.5, out double tx, out double tz);
                        try { model.GetColumn(tx, tz); }
                        catch (Exception e)
                        {
                            failed++;
                            Console.WriteLine($"  tile {la} N {lo} E: {e.GetType().Name}: {e.Message}");
                            Console.WriteLine("    " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim());
                        }
                    }
                Console.WriteLine($"tiles: {asked} asked, {failed} failed");
                Check(failed == 0, "every tile in the box builds");
                continue;
            }
            if (kv[0].StartsWith("survey"))
            {
                Survey(model, StreamData.Read(file, Path.GetFileName(streamsFile)), int.Parse(kv[1]));
                continue;
            }
            if (kv[0].StartsWith("chunks"))
            {
                var cv = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                Chunks(model, cv[0], cv[1], cv.Length > 2 ? (int)cv[2] : 3);
                continue;
            }
            if (kv[0].StartsWith("profile"))
            {
                var pv = kv[1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                Profile(model, pv[0], pv[1]);
                continue;
            }
            var v = kv[1].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Window(model, outDir, kv[0], v[0], v[1], v.Length > 2 ? (int)v[2] : 1024);
        }
        if (model.Streams != null) Console.WriteLine($"layer: {model.Streams.TilesBuilt} home tiles built, {model.Streams.ReachesBuilt} reaches, {model.Streams.BuildMs:F0} ms building ({model.Streams.FitMs:F0} ms fitting to the land: {model.Streams.VerticesRaised} vertices raised, {model.Streams.VerticesLowered} vertices lowered, {model.Streams.RidgeCuts} reaches cut at a ridge, {model.Streams.ReachesRefined} refined, {model.Streams.Relayouts} laid out again; {model.Streams.JoinMs:F0} ms joining; {model.Streams.JunctionsMoved} junctions moved up to a crossing)");
        Console.WriteLine(failures == 0 ? $"StreamsTests probe: {checks} checks passed" : $"StreamsTests probe: {failures} of {checks} checks failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>A window's columns without the streams layer (MainColumn); in a world without the layer (1:50 and coarser, the
    /// coarse river rules' one-block streams, EA-0179) without the world's rivers either, so the window's checks measure the
    /// rivers' and streams' own water against the land as drawn without them.</summary>
    static void MainPass(TerrainModel model, TerrainModel.Column[] main, int x0, int z0, int size)
    {
        var rivers = model.Rivers;
        if (model.Streams == null) model.Rivers = null;
        try { Parallel.For(0, size, j => { for (int i = 0; i < size; i++) main[j * size + i] = model.MainColumn(x0 + i, z0 + j); }); }
        finally { model.Rivers = rivers; }
    }

    static void Window(TerrainModel model, string outDir, string name, double lat, double lon, int size)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double cx, out double cz);
        int x0 = (int)Math.Floor(cx) - size / 2, z0 = (int)Math.Floor(cz) - size / 2;
        var main = new TerrainModel.Column[size * size];
        var full = new TerrainModel.Column[size * size];
        var sw = Stopwatch.StartNew();
        MainPass(model, main, x0, z0, size);
        double tMain = sw.Elapsed.TotalSeconds;
        double b0 = model.Streams?.BuildMs ?? 0;
        var gc0 = GC.GetTotalPauseDuration();
        sw.Restart();
        Parallel.For(0, size, j => { for (int i = 0; i < size; i++) full[j * size + i] = model.GetColumn(x0 + i, z0 + j); });
        double tCold = sw.Elapsed.TotalSeconds;
        double gcCold = (GC.GetTotalPauseDuration() - gc0).TotalSeconds;
        sw.Restart();
        Parallel.For(0, size, j => { for (int i = 0; i < size; i++) full[j * size + i] = model.GetColumn(x0 + i, z0 + j); });
        double tWarm = sw.Elapsed.TotalSeconds;
        double built = (model.Streams?.BuildMs ?? 0) - b0;
        // the main columns again after the layer was built (the first pass read the GLO-90 tiles cold)
        sw.Restart();
        MainPass(model, main, x0, z0, size);
        double tMain2 = sw.Elapsed.TotalSeconds;
        var stream = new bool[size * size];
        int streamWater = 0, changed = 0;
        // how far the stream's water stands above the land as drawn without it (positive: held by built-up banks), and how
        // much land was raised or cut beside it
        var above = new List<int>();
        var raised = new List<int>();
        int maxAbove = int.MinValue, maxAboveAt = -1;
        int cut = 0, bareBanks = 0, streamBanks = 0;
        for (int k = 0; k < full.Length; k++)
        {
            stream[k] = full[k].River && full[k].Water != TerrainModel.WaterNone && !(main[k].River && main[k].Water != TerrainModel.WaterNone);
            if (stream[k]) streamWater++;
            if (full[k].Top != main[k].Top || full[k].WaterTop != main[k].WaterTop) changed++;
            if (stream[k])
            {
                above.Add(full[k].WaterTop - main[k].Top);
                if (full[k].WaterTop - main[k].Top > maxAbove) { maxAbove = full[k].WaterTop - main[k].Top; maxAboveAt = k; }
            }
            if (full[k].Water == TerrainModel.WaterNone && full[k].Beach && !main[k].Beach) bareBanks++;
            if (full[k].Water == TerrainModel.WaterNone && main[k].Water == TerrainModel.WaterNone && full[k].NearRiver && !main[k].NearRiver && full[k].Top != main[k].Top) streamBanks++;
            if (!stream[k] && full[k].Water == TerrainModel.WaterNone && main[k].Water == TerrainModel.WaterNone && full[k].Top != main[k].Top)
            {
                if (full[k].Top > main[k].Top) raised.Add(full[k].Top - main[k].Top); else cut++;
            }
        }
        string Pct(List<int> l) { if (l.Count == 0) return "-"; l.Sort(); return $"p50 {l[l.Count / 2]}, p90 {l[(int)(l.Count * 0.9)]}, p99 {l[(int)(l.Count * 0.99)]}, max {l[^1]}"; }
        int pl = model.Rivers.PoolLayout;
        TerrainModel.SettleSteps(full, size, size, pl >= 1, pl >= 4, pl >= 5, pl >= 6, pl >= 7, pl >= 8, pl >= 10);
        // (EA-0183, EA-0184) the lakes: the depth of the water where the world draws a lake (not a river or stream), and trench
        // columns, a bed more than 3 blocks under the median bed of the lake columns within 6 blocks round it (every 2nd column)
        {
            var lake = new bool[full.Length];
            var depths = new List<int>();
            for (int k = 0; k < full.Length; k++)
            {
                ref var c = ref full[k];
                lake[k] = c.Water == TerrainModel.WaterLake && !c.River && c.WaterTop > c.Top;
                if (lake[k]) depths.Add(c.WaterTop - c.Top);
            }
            if (depths.Count > 0)
            {
                int trench = 0, looked = 0;
                var near = new List<int>(169);
                for (int j = 6; j < size - 6; j += 2)
                    for (int i = 6; i < size - 6; i += 2)
                    {
                        int k = j * size + i;
                        if (!lake[k]) continue;
                        near.Clear();
                        for (int dj = -6; dj <= 6; dj++)
                            for (int di = -6; di <= 6; di++)
                            {
                                int n = k + dj * size + di;
                                if (lake[n]) near.Add(full[n].Top);
                            }
                        near.Sort();
                        looked++;
                        if (full[k].Top < near[near.Count / 2] - 3) trench++;
                    }
                double mean = depths.Average();
                depths.Sort();
                Console.WriteLine($"  {name}: lake water {depths.Count} columns, depth mean {mean:F1}, p50 {depths[depths.Count / 2]}, p90 {depths[(int)(depths.Count * 0.9)]}, " +
                    $"max {depths[^1]}; trench columns {trench} of {looked} looked at (a bed 3+ under the median bed within 6 blocks)");
            }
        }
        // the window's columns as settled, all of them and the stream columns alone, for comparing builds
        {
            var all = new List<byte>(full.Length * 12);
            var str = new List<byte>();
            for (int k = 0; k < full.Length; k++)
            {
                ref var c = ref full[k];
                var b = new List<byte>(12);
                b.AddRange(BitConverter.GetBytes(c.Top)); b.AddRange(BitConverter.GetBytes(c.WaterTop));
                b.Add(c.Water); b.Add(c.River ? (byte)1 : (byte)0); b.Add(c.Ramp); b.Add(c.Beach ? (byte)1 : (byte)0);
                all.AddRange(b);
                if (stream[k]) { str.AddRange(BitConverter.GetBytes(k)); str.AddRange(b); }
            }
            string H(List<byte> x) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(x.ToArray()))[..16].ToLowerInvariant();
            Console.WriteLine($"  {name}: digest of all columns {H(all)}, of the {streamWater} stream columns {H(str)}");
        }
        // leaks: water (or a ramp's flowing layer) beside dry land lower than it; steps between stream columns
        var leak = new bool[size * size];
        var fall = new bool[size * size];
        int leaksStream = 0, leaksMain = 0, steps1 = 0, steps2 = 0, steps5 = 0, maxDrop = 0;
        int maxDropAt = -1;
        for (int j = 1; j < size - 1; j++)
            for (int i = 1; i < size - 1; i++)
            {
                int k = j * size + i;
                ref var c = ref full[k];
                if (c.Water == TerrainModel.WaterNone || !c.River || c.WaterTop <= c.Top) continue;
                int top = c.WaterTop + (c.Ramp > 0 ? 1 : 0);
                for (int d = 0; d < 4; d++)
                {
                    int n = k + (d == 0 ? -1 : d == 1 ? 1 : d == 2 ? -size : size);
                    ref var o = ref full[n];
                    if (o.Water == TerrainModel.WaterNone)
                    {
                        if (o.Top < top)
                        {
                            leak[k] = true;
                            if (stream[k]) leaksStream++; else leaksMain++;
                        }
                        continue;
                    }
                    if (stream[k] && stream[n] && o.WaterTop < c.WaterTop && (d == 1 || d == 3 || true))
                    {
                        int drop = c.WaterTop - o.WaterTop;
                        if (drop > maxDrop) { maxDrop = drop; maxDropAt = k; }
                        if (drop == 1) steps1++; else if (drop <= 4) steps2++; else steps5++;
                        if (drop >= 2) fall[k] = true;
                    }
                }
            }
        Check(leaksStream == 0, $"{name}: no stream water beside lower dry land ({leaksStream})");
        Console.WriteLine($"{name} ({lat:F3}, {lon:F3}), {size} x {size} blocks: stream water {streamWater} columns, columns changed {changed}; " +
            $"leaks: stream {leaksStream}, rivers {leaksMain}; stream steps 1: {steps1}, 2-4: {steps2}, 5+: {steps5} (largest {maxDrop}); " +
            $"water above the land without it: {Pct(above)}; land raised beside it {raised.Count} columns ({Pct(raised)}), cut {cut}; " +
            $"bare banks {bareBanks} of {streamBanks} shaped columns; " +
            $"a column {1e6 * tMain2 * Environment.ProcessorCount / full.Length:F1} us a thread without the streams, {1e6 * tWarm * Environment.ProcessorCount / full.Length:F1} us with (cold pass {tCold:F1} s, " +
            $"{built:F0} ms of it building, GC pauses {gcCold:F1} s)");
        if (maxAboveAt >= 0 && maxAbove > 1)
        {
            var ag = geo.ToGeo(x0 + maxAboveAt % size, z0 + maxAboveAt / size);
            Console.WriteLine($"  {name}: water {maxAbove} above the land at {ag.Lat:F5}, {ag.Lon:F5}");
        }
        if (maxDropAt >= 0)
        {
            var fg = geo.ToGeo(x0 + maxDropAt % size, z0 + maxDropAt / size);
            Console.WriteLine($"  {name}: largest stream drop {maxDrop} at {fg.Lat:F5}, {fg.Lon:F5} (x {x0 + maxDropAt % size}, z {z0 + maxDropAt / size}, water top {full[maxDropAt].WaterTop})");
        }
        Render(Path.Combine(outDir, "probe-" + name + ".png"), full, stream, leak, fall, size);
        Dump(Path.Combine(outDir, "probe-" + name + ".txt"), full, main, stream, leak, size, x0, z0);
    }

    /// <summary>Along the stream nearest a place, its whole system downstream from that reach: every 4 blocks along each
    /// reach's laid-out course, the land as drawn without the streams, the stream's water top there, and the water top of
    /// its snapped level (the course's level interpolated), so where the water stands above the land shows.</summary>
    static void Profile(TerrainModel model, double lat, double lon)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double x, out double z);
        var g = geo.ToGeo(x, z);
        if (!model.Streams.Nearest(x, z, g.Lat, g.Lon, out var hit, out var net)) { Console.WriteLine($"profile {lat},{lon}: no stream"); return; }
        Console.WriteLine($"profile from {lat:F4},{lon:F4}: reach {hit.Reach} Q {hit.Q:F2} dist {hit.Dist:F1}");
        {
            // the reach's own (snapped) vertices: GLO-90 there against the level, and the laid-out course's distance from them
            var rr = net.Reaches[hit.Reach];
            net.ReachLine(hit.Reach, out var lx, out var lz);
            for (int k = 0; k < rr.Lon.Length; k++)
            {
                geo.FromGeo(rr.Lat[k], rr.Lon[k], out double vx, out double vz);
                double gh = model.Fine != null && model.Fine.TryHeight(rr.Lat[k], rr.Lon[k], out double hh) ? hh : double.NaN;
                double dmin = double.MaxValue;
                for (int j = 1; j < lx.Length; j++)
                {
                    double ax = lx[j - 1], az = lz[j - 1], bx = lx[j] - ax, bz = lz[j] - az, l2 = bx * bx + bz * bz;
                    double t = l2 > 0 ? Math.Clamp(((vx - ax) * bx + (vz - az) * bz) / l2, 0, 1) : 0;
                    double ex = ax + t * bx - vx, ez = az + t * bz - vz;
                    dmin = Math.Min(dmin, Math.Sqrt(ex * ex + ez * ez));
                }
                Console.WriteLine($"  vertex {k,3} {rr.Lat[k]:F5},{rr.Lon[k]:F5}  level {rr.Level[k],5} m  GLO-90 {gh,7:F1} m  land {model.MainColumn(vx, vz).Top,4}  laid-out course {dmin,5:F1} blocks away");
            }
        }
        int r = hit.Reach;
        double s0 = 0;
        for (int guard = 0; r >= 0 && guard < 50; guard++)
        {
            var re = net.Reaches[r];
            if (!net.ReachLine(r, out var xs, out var zs)) break;
            // the reach's levels by distance along its own vertices (lon/lat), mapped onto the laid-out line by share of length
            int n = re.Lon.Length;
            var cum = new double[n];
            for (int k = 1; k < n; k++)
            {
                geo.FromGeo(re.Lat[k - 1], re.Lon[k - 1], out double ax, out double az);
                geo.FromGeo(re.Lat[k], re.Lon[k], out double bx, out double bz);
                cum[k] = cum[k - 1] + Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            }
            double total = 0;
            for (int k = 1; k < xs.Length; k++) total += Math.Sqrt((xs[k] - xs[k - 1]) * (xs[k] - xs[k - 1]) + (zs[k] - zs[k - 1]) * (zs[k] - zs[k - 1]));
            double along = 0;
            for (int k = 1; k < xs.Length; k++)
            {
                double dx = xs[k] - xs[k - 1], dz = zs[k] - zs[k - 1], len = Math.Sqrt(dx * dx + dz * dz);
                for (double t = 0; t < len; t += 4)
                {
                    double px = xs[k - 1] + dx * t / len, pz = zs[k - 1] + dz * t / len;
                    double share = total > 0 ? (along + t) / total : 0, sv = share * cum[n - 1];
                    int v = 1;
                    while (v < n - 1 && cum[v] < sv) v++;
                    double f = cum[v] > cum[v - 1] ? (sv - cum[v - 1]) / (cum[v] - cum[v - 1]) : 0;
                    double level = re.Level[v - 1] + Math.Clamp(f, 0, 1) * (re.Level[v] - re.Level[v - 1]);
                    var land = model.MainColumn(px, pz);
                    var gg = geo.ToGeo(px, pz);
                    model.Streams.Nearest(px, pz, gg.Lat, gg.Lon, out var h, out _);
                    var full = model.GetColumn(px, pz);
                    Console.WriteLine($"  s {s0 + along + t,7:F0}  reach {r,4}  land {land.Top,5}{(land.Water != TerrainModel.WaterNone ? "w" : " ")}  water {(full.Water != TerrainModel.WaterNone ? full.WaterTop : -1),5}  " +
                        $"pool {h.WaterTop,5}  level {level,7:F1} m -> {model.RiverWaterTop(level),5}  ramp {h.Ramp} chute {(h.Chute ? 1 : 0)} lip {(h.Lip ? 1 : 0)} plunge {h.Plunge}");
                }
                along += len;
            }
            s0 += along;
            r = re.Next;
        }
    }

    /// <summary>
    /// The same window's columns from two fresh layers asked in different orders (the first in parallel, row by row; the
    /// second after building a tile north-east of it first, one thread, from the last column back): every field of every
    /// column must be equal, as a world's chunks must be whatever order they are made in.
    /// </summary>
    static void OrderIndependent(TerrainModel model, StreamData data, RiverBuildOptions options, double lat, double lon, int size)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double cx, out double cz);
        int x0 = (int)Math.Floor(cx) - size / 2, z0 = (int)Math.Floor(cz) - size / 2;
        var keep = model.Streams;
        model.Streams = new StreamLayer(data, model, options);
        var a = new TerrainModel.Column[size * size];
        Parallel.For(0, size, j => { for (int i = 0; i < size; i++) a[j * size + i] = model.GetColumn(x0 + i, z0 + j); });
        model.Streams = new StreamLayer(data, model, options);
        geo.FromGeo(lat + 1.2, lon + 1.2, out double fx, out double fz);
        model.GetColumn(fx, fz);
        var b = new TerrainModel.Column[size * size];
        for (int k = size * size - 1; k >= 0; k--) b[k] = model.GetColumn(x0 + k % size, z0 + k / size);
        var fields = typeof(TerrainModel.Column).GetFields(BindingFlags.Public | BindingFlags.Instance);
        int differ = 0, streams = 0;
        string first = null;
        for (int k = 0; k < a.Length; k++)
        {
            if (a[k].River && a[k].Water != TerrainModel.WaterNone) streams++;
            foreach (var f in fields)
            {
                if (Equals(f.GetValue(a[k]), f.GetValue(b[k]))) continue;
                differ++;
                first ??= $"{f.Name} at ({x0 + k % size}, {z0 + k / size}): {f.GetValue(a[k])} / {f.GetValue(b[k])}";
                break;
            }
        }
        Console.WriteLine($"order ({lat:F3}, {lon:F3}), {size} x {size}: {streams} river or stream water columns; {differ} columns differ" + (first != null ? ", first " + first : ""));
        Check(differ == 0 && streams > 0, $"the streams at {lat:F3}, {lon:F3} are the same whatever order their columns are asked in");
        model.Streams = keep;
    }

    /// <summary>
    /// (Langholm, 18 November 2026) A window's streams from a fresh layer, against a layer whose tiles were first asked about
    /// on a thread named as World Map's world chart, which takes the built-in heights (FineElevation.BuiltInOnly): a tile's
    /// streams fitted to those heights cut the Border Esk at "ridges" above Langholm, and the layer kept them for the
    /// session, chunk generation included. Every stream column must be the same.
    /// </summary>
    static void ChartFirst(TerrainModel model, StreamData data, RiverBuildOptions options, double lat, double lon, int size)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double cx, out double cz);
        int x0 = (int)Math.Floor(cx) - size / 2, z0 = (int)Math.Floor(cz) - size / 2;
        var keep = model.Streams;
        model.Streams = new StreamLayer(data, model, options);
        var a = new TerrainModel.Column[size * size];
        Parallel.For(0, size, j => { for (int i = 0; i < size; i++) a[j * size + i] = model.GetColumn(x0 + i, z0 + j); });
        model.Streams = new StreamLayer(data, model, options);
        // the chart's samples: every 489 blocks over the window and a tile's width around it, on its own named thread
        int span = size + 2 * (int)geo.BlocksPerDegreeZ;
        var chart = new Thread(() =>
        {
            for (int j = -span / 2; j <= span / 2; j += 489)
                for (int i = -span / 2; i <= span / 2; i += 489) model.GetSurfaceColumn(cx + i, cz + j);
        }) { Name = FineElevation.SweepThreadNames[0] };
        chart.Start();
        chart.Join();
        long built = model.Streams.TilesBuilt;
        var b = new TerrainModel.Column[size * size];
        Parallel.For(0, size, j => { for (int i = 0; i < size; i++) b[j * size + i] = model.GetColumn(x0 + i, z0 + j); });
        int streamsA = 0, missing = 0, differ = 0;
        string first = null;
        for (int k = 0; k < a.Length; k++)
        {
            bool sa = a[k].River && a[k].Water != TerrainModel.WaterNone, sb = b[k].River && b[k].Water != TerrainModel.WaterNone;
            if (sa) streamsA++;
            if (sa && !sb) missing++;
            if (sa != sb || a[k].Top != b[k].Top || a[k].WaterTop != b[k].WaterTop)
            {
                differ++;
                if (first == null)
                {
                    var g = geo.ToGeo(x0 + k % size, z0 + k / size);
                    first = $"{g.Lat:F5}, {g.Lon:F5}: water {(sa ? a[k].WaterTop : -1)} / {(sb ? b[k].WaterTop : -1)}, top {a[k].Top} / {b[k].Top}";
                }
            }
        }
        Console.WriteLine($"chart first ({lat:F3}, {lon:F3}), {size} x {size}: {streamsA} river or stream water columns; after the chart's thread asked first " +
            $"({built} home tiles built on it): {missing} of them dry, {differ} columns differ" + (first != null ? ", first at " + first : ""));
        Check(differ == 0 && streamsA > 0, $"the streams at {lat:F3}, {lon:F3} are the same after World Map's world chart asked about their tiles first");
        Check(built == 0, "a thread on the built-in heights builds no stream tile");
        model.Streams = keep;
    }

    /// <summary>(EA-0164, EA-0165) What Measure and Crossings found in one or more networks.</summary>
    sealed class Shape
    {
        public long Samples, Deep, DeepReaches, Reaches, Crossings, Siblings, Into, Hidden, Deep9, Deep17, DeepPools, DeepPoolReaches;
        public int MaxCut = int.MinValue;
        public string MaxAt = "-";
        public readonly List<string> Where = new();
        public void Add(Shape o)
        {
            Samples += o.Samples; Deep += o.Deep; DeepReaches += o.DeepReaches; Reaches += o.Reaches; Crossings += o.Crossings; Siblings += o.Siblings; Into += o.Into; Hidden += o.Hidden; Deep9 += o.Deep9; Deep17 += o.Deep17; DeepPools += o.DeepPools; DeepPoolReaches += o.DeepPoolReaches;
            if (o.MaxCut > MaxCut) { MaxCut = o.MaxCut; MaxAt = o.MaxAt; }
            foreach (var w in o.Where) if (Where.Count < 12) Where.Add(w);
        }
        public override string ToString() =>
            $"{Reaches} reaches, {Samples} samples: {Deep} with the water more than {DeepCut} blocks under gentle land ({Deep9} more than 8, {Deep17} more than 16; " +
            $"{DeepPools} of them with the pools 5 or more under the reach's own levels, on {DeepPoolReaches} reaches), on {DeepReaches} reaches " +
            $"(deepest {(MaxCut == int.MinValue ? "-" : MaxCut.ToString())} at {MaxAt}); {Crossings} reaches crossing another away from a junction " +
            $"({Siblings} of them the two inflows of one junction, {Into} a reach and the one it flows into); {Hidden} more where the world draws water of its own (a lake, a river, the sea: no stream drawn)";
    }

    /// <summary>A cut deeper than this many blocks under land rising less than 1 in 10 counts (EA-0165's measure).</summary>
    const int DeepCut = 4;

    /// <summary>
    /// (EA-0165) A network's streams against the land as the world draws it without them, every <paramref name="step"/> blocks
    /// along each reach's laid-out course inside the box: how far the water stands under that land where the land is gentle
    /// (rising less than 1 in 10 across 16 blocks either way): the samples deeper than DeepCut, and the deepest.
    /// </summary>
    static Shape Measure(TerrainModel model, RiverNetwork net, double x0, double z0, double x1, double z1, double step)
    {
        var s = new Shape();
        var geo = model.Geometry;
        int Top(double x, double z) => model.MainColumn(x, z).Top;
        for (int r = 0; r < net.Reaches.Count; r++)
        {
            if (!net.ReachLine(r, out var xs, out var zs)) continue;
            s.Reaches++;
            bool deep = false, poolsDeep = false;
            for (int k = 1; k < xs.Length; k++)
            {
                double dx = xs[k] - xs[k - 1], dz = zs[k] - zs[k - 1], len = Math.Sqrt(dx * dx + dz * dz);
                for (double t = 0; t < len; t += step)
                {
                    double px = xs[k - 1] + dx * t / len, pz = zs[k - 1] + dz * t / len;
                    if (px < x0 || px >= x1 || pz < z0 || pz >= z1) continue;
                    var land = model.MainColumn(px, pz);
                    if (land.Water != TerrainModel.WaterNone || !net.Nearest(px, pz, out var h)) continue;
                    s.Samples++;
                    int cut = land.Top - h.WaterTop;
                    if (cut <= DeepCut) continue;
                    int rise = Math.Max(Math.Abs(Top(px + 8, pz) - Top(px - 8, pz)), Math.Abs(Top(px, pz + 8) - Top(px, pz - 8)));
                    if (rise * 10 >= 16) continue;
                    s.Deep++;
                    deep = true;
                    if (cut > 8) s.Deep9++;
                    if (cut > 16) s.Deep17++;
                    // the pools laid out under the reach's own (fitted) levels: the level of its vertex nearest here
                    var hr = net.Reaches[h.Reach];
                    int nk = 0; double nd = double.MaxValue;
                    for (int v = 0; v < hr.Lon.Length; v++)
                    {
                        geo.FromGeo(hr.Lat[v], hr.Lon[v], out double vx, out double vz);
                        double d = (vx - px) * (vx - px) + (vz - pz) * (vz - pz);
                        if (d < nd) { nd = d; nk = v; }
                    }
                    if (h.WaterTop <= model.RiverWaterTop(hr.Level[nk]) - 5) { s.DeepPools++; poolsDeep = true; }
                    if (cut > s.MaxCut)
                    {
                        var g = geo.ToGeo(px, pz);
                        s.MaxCut = cut;
                        s.MaxAt = $"{g.Lat:F5}, {g.Lon:F5} (land {land.Top}, water {h.WaterTop}, Q {h.Q:F1})";
                    }
                }
            }
            if (deep) s.DeepReaches++;
            if (poolsDeep) s.DeepPoolReaches++;
        }
        return s;
    }

    /// <summary>
    /// (EA-0164) The reaches whose laid-out courses cross another reach's in the same network inside the box, away from where they
    /// join (a crossing within 3 blocks of an end of both is a junction): each pair counted once, and whether the two are the
    /// inflows of one junction.
    /// </summary>
    static void Crossings(TerrainModel model, RiverNetwork net, double x0, double z0, double x1, double z1, Shape s)
    {
        var reaches = net.Reaches;
        int n = reaches.Count;
        var lx = new double[n][];
        var lz = new double[n][];
        var cells = new Dictionary<long, List<(int R, int K)>>();
        for (int r = 0; r < n; r++)
        {
            if (!net.ReachLine(r, out lx[r], out lz[r])) continue;
            for (int k = 1; k < lx[r].Length; k++)
            {
                int cx0 = (int)Math.Floor(Math.Min(lx[r][k - 1], lx[r][k]) / 32), cx1 = (int)Math.Floor(Math.Max(lx[r][k - 1], lx[r][k]) / 32);
                int cz0 = (int)Math.Floor(Math.Min(lz[r][k - 1], lz[r][k]) / 32), cz1 = (int)Math.Floor(Math.Max(lz[r][k - 1], lz[r][k]) / 32);
                for (int cz = cz0; cz <= cz1; cz++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        long key = ((long)cx << 32) ^ (uint)cz;
                        if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<(int, int)>();
                        list.Add((r, k));
                    }
            }
        }
        bool NearEnd(int r, double px, double pz)
        {
            var xs = lx[r]; var zs = lz[r];
            double a = (xs[0] - px) * (xs[0] - px) + (zs[0] - pz) * (zs[0] - pz), b = (xs[^1] - px) * (xs[^1] - px) + (zs[^1] - pz) * (zs[^1] - pz);
            return Math.Min(a, b) <= 9;
        }
        var seen = new HashSet<(int, int)>();
        foreach (var list in cells.Values)
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var (r, k) = list[i];
                    var (q, m) = list[j];
                    if (r == q || seen.Contains(r < q ? (r, q) : (q, r))) continue;
                    double ax = lx[r][k - 1], az = lz[r][k - 1], bx = lx[r][k] - ax, bz = lz[r][k] - az;
                    double cx = lx[q][m - 1], cz = lz[q][m - 1], dx = lx[q][m] - cx, dz = lz[q][m] - cz;
                    double den = bx * dz - bz * dx;
                    if (Math.Abs(den) < 1e-12) continue;
                    double t = ((cx - ax) * dz - (cz - az) * dx) / den, u = ((cx - ax) * bz - (cz - az) * bx) / den;
                    if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                    double px = ax + t * bx, pz = az + t * bz;
                    if (px < x0 || px >= x1 || pz < z0 || pz >= z1) continue;
                    if (NearEnd(r, px, pz) && NearEnd(q, px, pz)) continue;
                    seen.Add(r < q ? (r, q) : (q, r));
                    // in water the world draws itself, no stream is drawn: not seen
                    if (model.MainColumn(px, pz).Water != TerrainModel.WaterNone) { s.Hidden++; continue; }
                    bool sib = reaches[r].Next >= 0 && reaches[r].Next == reaches[q].Next;
                    bool into = reaches[r].Next == q || reaches[q].Next == r;
                    s.Crossings++;
                    if (sib) s.Siblings++;
                    if (into) s.Into++;
                    if (s.Where.Count < 12)
                    {
                        var g = model.Geometry.ToGeo(px, pz);
                        s.Where.Add($"{(sib ? "inflows of one junction" : into ? "into the next" : "other")}, Q {reaches[r].Q:F1} and {reaches[q].Q:F1}, at {g.Lat:F5}, {g.Lon:F5}");
                    }
                }
    }

    /// <summary>
    /// (EA-0164, EA-0165) The streams' cuts under gentle land and their crossings in a window round a place, for the networks of
    /// its home tiles as the layer builds them (GLO-90) and as a thread on the built-in heights built them before the Langholm
    /// fix.
    /// </summary>
    static void ShapeAt(TerrainModel model, double lat, double lon, int size)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double cx, out double cz);
        double x0 = cx - size / 2, z0 = cz - size / 2, x1 = x0 + size, z1 = z0 + size;
        var glo = new Shape();
        var builtIn = new Shape();
        foreach (int h in model.Streams.HomesAt(lat, lon))
        {
            var net = model.Streams.HomeNetwork(h);
            if (net == null) continue;
            var a = Measure(model, net, x0, z0, x1, z1, 2);
            Crossings(model, net, x0, z0, x1, z1, a);
            glo.Add(a);
            RiverNetwork coarse = null;
            var th = new Thread(() => { FineElevation.BuiltInOnly = true; coarse = model.Streams.BuildDetached(h); });
            th.Start();
            th.Join();
            if (coarse == null) continue;
            var b = Measure(model, coarse, x0, z0, x1, z1, 2);
            Crossings(model, coarse, x0, z0, x1, z1, b);
            builtIn.Add(b);
        }
        Console.WriteLine($"shape ({lat:F4}, {lon:F4}), {size} x {size} blocks, fitted to GLO-90: {glo}");
        foreach (var w in glo.Where) Console.WriteLine("  crossing: " + w);
        Console.WriteLine($"shape ({lat:F4}, {lon:F4}), fitted to the built-in heights (World Map's chart before the fix): {builtIn}");
    }

    /// <summary>
    /// The terrain pass's settling per chunk (ChunkSettler.Settle, without the column store): a chunk whose columns hold river
    /// water or a river's bank is settled with a margin of 20 columns from the model; per chunk of (2r+1)^2 around a place,
    /// with and without the streams layer, one thread: how many chunks settle and what it costs.
    /// </summary>
    static void Chunks(TerrainModel model, double lat, double lon, int r)
    {
        var geo = model.Geometry;
        geo.FromGeo(lat, lon, out double px, out double pz);
        int ccx = (int)Math.Floor(px / 32), ccz = (int)Math.Floor(pz / 32);
        const int margin = 20, size = 32 + 2 * margin;
        var layer = model.Streams;
        foreach (bool with in new[] { false, true, false, true })
        {
            model.Streams = with ? layer : null;
            int settled = 0;
            var ext = new TerrainModel.Column[size * size];
            var sw = Stopwatch.StartNew();
            double own = 0, marginTime = 0, settleTime = 0;
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    bool near = false;
                    for (int lz = 0; lz < 32; lz++)
                        for (int lx = 0; lx < 32; lx++)
                        {
                            var c = model.GetColumn(cx * 32 + lx, cz * 32 + lz);
                            near |= c.NearRiver || c.River;
                            ext[(lz + margin) * size + lx + margin] = c;
                        }
                    long t1 = Stopwatch.GetTimestamp();
                    own += (t1 - t0) / (double)Stopwatch.Frequency;
                    if (!near) continue;
                    settled++;
                    for (int ez = 0; ez < size; ez++)
                        for (int ex = 0; ex < size; ex++)
                        {
                            if (ex >= margin && ex < margin + 32 && ez >= margin && ez < margin + 32) continue;
                            ext[ez * size + ex] = model.GetColumn(cx * 32 + ex - margin, cz * 32 + ez - margin);
                        }
                    long t2 = Stopwatch.GetTimestamp();
                    marginTime += (t2 - t1) / (double)Stopwatch.Frequency;
                    int pl = model.Rivers.PoolLayout;
                    TerrainModel.SettleSteps(ext, size, size, pl >= 1, pl >= 4, pl >= 5, pl >= 6, pl >= 7, pl >= 8, pl >= 10);
                    settleTime += (Stopwatch.GetTimestamp() - t2) / (double)Stopwatch.Frequency;
                }
            int n = (2 * r + 1) * (2 * r + 1);
            Console.WriteLine($"chunks ({lat:F3}, {lon:F3}) {(with ? "with" : "without")} streams: {settled} of {n} chunks settled; {1000 * sw.Elapsed.TotalSeconds / n:F1} ms a chunk " +
                $"(own columns {1000 * own / n:F1}, margins {1000 * marginTime / n:F1}, settling {1000 * settleTime / n:F1})");
        }
        model.Streams = layer;
    }

    /// <summary>
    /// The land fit over random tiles with streams (seeded): each tile's networks built by asking a column at its centre, the
    /// ridge cuts and lowered vertices counted per tile and by latitude band, the tiles with the most cuts named.
    /// </summary>
    static void Survey(TerrainModel model, StreamData data, int count)
    {
        var tiles = new List<int>();
        for (int t = 0; t < data.TilesX * data.TilesY; t++) if (data.State(t) == StreamData.HasData) tiles.Add(t);
        var rnd = new Random(7);
        var pick = tiles.OrderBy(_ => rnd.Next()).Take(count).ToList();
        var cuts = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        var log = StreamLayer.DebugLog;
        StreamLayer.DebugLog = m =>
        {
            // "ridge: reach Q .. at lat,lon, ..."
            int at = m.IndexOf(" at ") + 4, comma = m.IndexOf(',', at);
            double la = double.Parse(m[at..comma], CultureInfo.InvariantCulture);
            cuts.AddOrUpdate((int)Math.Floor(la / 10) * 10, 1, (_, v) => v + 1);
        };
        var perTile = new List<(int Tile, long Cuts, long Lowered, long Reaches)>();
        foreach (int t in pick)
        {
            long c0 = model.Streams.RidgeCuts, l0 = model.Streams.VerticesLowered, r0 = model.Streams.ReachesBuilt;
            double lat = t / data.TilesX - 90 + 0.5, lon = t % data.TilesX - 180 + 0.5;
            model.Geometry.FromGeo(lat, lon, out double x, out double z);
            try { model.GetColumn(x, z); } catch (Exception e) { Console.WriteLine($"  tile {lat - 0.5},{lon - 0.5}: {e.GetType().Name} {e.Message}"); }
            perTile.Add((t, model.Streams.RidgeCuts - c0, model.Streams.VerticesLowered - l0, model.Streams.ReachesBuilt - r0));
        }
        StreamLayer.DebugLog = log;
        long rc = perTile.Sum(p => p.Cuts), rr = perTile.Sum(p => p.Reaches);
        Console.WriteLine($"survey: {pick.Count} tiles asked, {rr} reaches built, {rc} cut at a ridge ({100.0 * rc / Math.Max(1, rr):F1} %), {perTile.Sum(p => p.Lowered)} vertices lowered");
        Console.WriteLine("  cuts by latitude band: " + string.Join(", ", cuts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}..{kv.Key + 10}: {kv.Value}")));
        foreach (var p in perTile.OrderByDescending(p => p.Cuts).Take(12))
            Console.WriteLine($"  tile {p.Tile / data.TilesX - 90} N {p.Tile % data.TilesX - 180} E: {p.Cuts} cut of {p.Reaches} reaches, {p.Lowered} vertices lowered");
    }

    /// <summary>
    /// Every home tile of the file built once (its streams' land fit, joins and network, as the first column near it would),
    /// on <paramref name="threads"/> threads, a fresh layer every 256 tiles so memory stays bounded: any exception fails it,
    /// named with the tile. Before a merge, so that no spawn or explored tile anywhere can fail chunk generation.
    /// </summary>
    static void Sweep(TerrainModel model, StreamData data, RiverBuildOptions options, int threads)
    {
        var tiles = new List<int>();
        for (int t = 0; t < data.TilesX * data.TilesY; t++) if (data.State(t) == StreamData.HasData && data.Decode(t).Systems.Count > 0) tiles.Add(t);
        Console.WriteLine($"sweep: {tiles.Count} home tiles on {threads} threads");
        var sw = Stopwatch.StartNew();
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        long reaches = 0, ridges = 0, refined = 0, relayouts = 0;
        double buildMs = 0;
        var shape = new Shape();
        for (int b = 0; b < tiles.Count; b += 256)
        {
            var layer = new StreamLayer(data, model, options);
            Parallel.ForEach(tiles.Skip(b).Take(256), new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                try
                {
                    var net = layer.HomeNetwork(t);
                    // (EA-0164, EA-0165) its cuts under gentle land, every 6 blocks, and its crossings
                    if (net == null) return;
                    var s = Measure(model, net, double.MinValue, double.MinValue, double.MaxValue, double.MaxValue, 6);
                    Crossings(model, net, double.MinValue, double.MinValue, double.MaxValue, double.MaxValue, s);
                    lock (shape) shape.Add(s);
                }
                catch (Exception e) { failed.Add($"tile {t / data.TilesX - 90} N {t % data.TilesX - 180} E: {e.GetType().Name}: {e.Message} {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
            });
            reaches += layer.ReachesBuilt; ridges += layer.RidgeCuts; refined += layer.ReachesRefined; relayouts += layer.Relayouts; buildMs += layer.BuildMs;
            if ((b / 256) % 8 == 7) Console.WriteLine($"  {Math.Min(tiles.Count, b + 256)} of {tiles.Count} tiles, {sw.Elapsed.TotalMinutes:F1} min, {failed.Count} failed");
        }
        foreach (var f in failed.Take(20)) Console.WriteLine("  FAILED " + f);
        Console.WriteLine($"sweep: {tiles.Count} tiles, {reaches} reaches, {ridges} cut at a ridge ({100.0 * ridges / Math.Max(1, reaches):F2} %), {refined} refined, " +
            $"{relayouts} tiles laid out again; {failed.Count} failed; {sw.Elapsed.TotalMinutes:F1} min ({buildMs / Math.Max(1, tiles.Count):F0} ms a tile, summed over threads)");
        Console.WriteLine("sweep shape: " + shape);
        foreach (var w in shape.Where) Console.WriteLine("  crossing: " + w);
        Check(failed.IsEmpty, "every home tile's streams build");
    }

    static void Render(string path, TerrainModel.Column[] cols, bool[] stream, bool[] leak, bool[] fall, int size)
    {
        var rgb = new byte[3 * size * size];
        int mn = int.MaxValue, mx = int.MinValue;
        foreach (var c in cols) { mn = Math.Min(mn, c.Top); mx = Math.Max(mx, c.Top); }
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                int k = j * size + i;
                var c = cols[k];
                double r, g, b;
                if (leak[k]) { r = 255; g = 0; b = 0; }
                else if (fall[k]) { r = 255; g = 0; b = 255; }
                else if (stream[k]) { r = 0; g = 210; b = 255; }
                else if (c.Water == TerrainModel.WaterSea) { r = 15; g = 40; b = 100; }
                else if (c.Water != TerrainModel.WaterNone && c.River) { r = 30; g = 80; b = 230; }
                else if (c.Water != TerrainModel.WaterNone) { r = 25; g = 50; b = 150; }
                else
                {
                    double t = (c.Top - mn) / Math.Max(1.0, mx - mn);
                    int e = i + 1 < size ? cols[k + 1].Top : c.Top, s = j + 1 < size ? cols[k + size].Top : c.Top;
                    double shade = Math.Clamp(0.75 + 0.12 * ((c.Top - e) + (c.Top - s)), 0.3, 1.15);
                    r = (110 + 120 * t) * shade; g = (140 + 80 * t - 50 * t * t) * shade; b = (90 + 130 * t * t) * shade;
                    if (c.NearRiver) { r *= 0.85; g *= 0.85; }
                }
                rgb[3 * k] = (byte)Math.Clamp(r, 0, 255); rgb[3 * k + 1] = (byte)Math.Clamp(g, 0, 255); rgb[3 * k + 2] = (byte)Math.Clamp(b, 0, 255);
            }
        Png.WriteRgb(path, size, size, rgb);
    }

    /// <summary>The leaking stream columns with their neighbourhood (tops and water tops), for a look at why.</summary>
    static void Dump(string path, TerrainModel.Column[] full, TerrainModel.Column[] main, bool[] stream, bool[] leak, int size, int x0, int z0)
    {
        using var w = new StreamWriter(path);
        int shown = 0;
        for (int k = 0; k < full.Length && shown < 40; k++)
        {
            if (!leak[k] || !stream[k]) continue;
            shown++;
            int i = k % size, j = k / size;
            w.WriteLine($"leak at x {x0 + i} z {z0 + j}: water top {full[k].WaterTop} ramp {full[k].Ramp} bed {full[k].Top}");
            for (int dj = -2; dj <= 2; dj++)
            {
                var sb = new System.Text.StringBuilder("   ");
                for (int di = -2; di <= 2; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= size || jj >= size) { sb.Append("      ----     "); continue; }
                    var c = full[jj * size + ii];
                    string kind = c.Water == TerrainModel.WaterNone ? "land" : stream[jj * size + ii] ? "strm" : c.River ? "rivr" : "watr";
                    sb.Append($" {kind} {c.Top,4}/{(c.Water == TerrainModel.WaterNone ? -1 : c.WaterTop),4}{(c.Ramp > 0 ? "r" : " ")}");
                }
                w.WriteLine(sb.ToString());
            }
        }
    }
}

/// <summary>An ILogger that says nothing.</summary>
public class QuietLog : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
        => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
}
