using EarthMap.Core;

// Decision 0069 (rc.7): the river water rules. Rule 8: GIRES' non-perennial rivers decide which rivers the arid rule may
// dry (NonPerennialData): the file's checks, the match by place and discharge, side arms taking their river's class, and
// the real file's classes at the reported places.
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
        string root = args.Length > 0 ? args[0] : FindRoot();
        NonPerennialFile();
        NonPerennialMatch();
        NonPerennialArms();
        NonPerennialRealFile(Path.Combine(root, "earthdata", NonPerennialData.FileName));
        FlowFile();
        FlowReroute();
        WorksCanals();
        ShoreFile();
        ShoreRealFile(Path.Combine(root, "earthdata", ShoreMaterialData.FileName));
        OneWidth();
        WidthMultiplier();
        OneBlockStreams();
        Console.WriteLine(failures == 0 ? $"RiverWaterTests: {checks} checks passed" : $"RiverWaterTests: {failures} of {checks} checks failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Decision 0070: a shores-osm-1.bin as tools/bake_shore_material.py writes it (one square, 4 cells a degree).</summary>
    static void ShoreFile()
    {
        const int cpd = 4;
        var cells = new byte[cpd * cpd];
        cells[0 * cpd + 1] = ShoreMaterial.Sand;   // the north-west part, second column
        cells[3 * cpd + 3] = ShoreMaterial.Rock;   // the south-east corner
        byte[] body;
        using (var bs = new MemoryStream())
        {
            using (var br = new System.IO.Compression.BrotliStream(bs, System.IO.Compression.CompressionLevel.Optimal, true)) br.Write(cells);
            body = bs.ToArray();
        }
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("EMSH"u8.ToArray()); w.Write(1); w.Write(cpd); w.Write(1);
        w.Write((short)56); w.Write((short)23); w.Write(16 + 12); w.Write(body.Length);
        w.Write(body);
        var d = ShoreMaterialData.Read(ms.ToArray());
        Check(d.Squares == 1, "shore file: one square");
        Check(d.At(56.9, 23.3) == ShoreMaterial.Sand, "shore file: the sand cell (north row, second column)");
        Check(d.At(56.1, 23.9) == ShoreMaterial.Rock, "shore file: the rock cell (south-east)");
        Check(d.At(56.5, 23.5) == ShoreMaterial.None, "shore file: an empty cell");
        Check(d.At(57.5, 23.5) == ShoreMaterial.None, "shore file: a square it hasn't");
        bool threw = false;
        var bad = ms.ToArray(); bad[0] = (byte)'X';
        try { ShoreMaterialData.Read(bad); } catch (Exception) { threw = true; }
        Check(threw, "shore file: a bad header is refused");
    }

    /// <summary>The baked file at reported places: Jūrmala's sand (OSM: natural=beach, surface=sand along the gulf) and
    /// Chesil Beach's shingle.</summary>
    static void ShoreRealFile(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine("(no " + path + ": real shore file checks skipped)"); return; }
        var d = ShoreMaterialData.Read(File.ReadAllBytes(path));
        Check(d.Squares > 5000, $"real shore file: {d.Squares} squares");
        byte Near(double lat, double lon, double span)
        {
            for (double a = -span; a <= span; a += 0.002)
                for (double o = -span; o <= span; o += 0.002)
                {
                    byte c = d.At(lat + a, lon + o);
                    if (c != ShoreMaterial.None) return c;
                }
            return ShoreMaterial.None;
        }
        byte j = Near(56.975, 23.80, 0.01);
        Check(j == ShoreMaterial.Sand || j == ShoreMaterial.Beach, $"real shore file: Jūrmala's shore is a beach ({j})");
        byte c = Near(50.62, -2.57, 0.01);
        Check(c == ShoreMaterial.Gravel || c == ShoreMaterial.Mixed, $"real shore file: Chesil Beach is gravel ({c})");
    }

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "modinfo.json"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }

    /// <summary>A rivers-nonperennial-2.bin as tools/bake_nonperennial.py writes it: the course list's key, one class per
    /// reach, brotli.</summary>
    static byte[] File2(ulong key, params byte[] classes)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("EMNP"u8.ToArray());
        w.Write(2);
        w.Write(key);
        w.Write(classes.Length);
        using (var bs = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) bs.Write(classes);
        return ms.ToArray();
    }

    static bool Throws(Action a)
    {
        try { a(); return false; } catch (InvalidOperationException) { return true; } catch (InvalidDataException) { return true; }
    }

    /// <summary>The file's layout is checked: magic, version, a count that fits the classes, known classes.</summary>
    static void NonPerennialFile()
    {
        var ok = File2(0x9693b65a3a265a62, 0, 2, 1, 0);
        var d = NonPerennialData.Read(ok);
        Check(d.Count == 4 && d.CourseKey == 0x9693b65a3a265a62, "a four-reach file reads four reaches and its key");
        var badMagic = (byte[])ok.Clone(); badMagic[0] = (byte)'X';
        Check(Throws(() => NonPerennialData.Read(badMagic)), "a wrong magic is refused");
        var badVersion = (byte[])ok.Clone(); badVersion[4] = 1;
        Check(Throws(() => NonPerennialData.Read(badVersion)), "version 1 (never released) is refused");
        var badCount = (byte[])ok.Clone(); badCount[16] = 5;
        Check(Throws(() => NonPerennialData.Read(badCount)), "a count that doesn't fit the classes is refused");
        Check(Throws(() => NonPerennialData.Read(File2(1, 0, 3))), "an unknown class is refused");
    }

    /// <summary>Classes by reach; beyond the list year-round.</summary>
    static void NonPerennialMatch()
    {
        var d = NonPerennialData.Read(File2(7, 0, 2, 1));
        Check(d.ClassOf(1) == NonPerennialData.OftenDry && d.ClassOf(2) == NonPerennialData.SomeDaysDry && d.ClassOf(0) == NonPerennialData.Perennial, "each reach's class");
        Check(d.ClassOf(3) == NonPerennialData.Perennial && d.ClassOf(-1) == NonPerennialData.Perennial, "beyond the list: year-round");
    }

    static RiverReach Reach(double lat, double lon, float q, int next) =>
        new() { Lat = new[] { (float)lat, (float)lat }, Lon = new[] { (float)lon, (float)(lon + 0.001) }, Q = q, Next = next };

    /// <summary>The wet-source flow is none for an often-dry reach and all of its flow for any other (the side arms' classes
    /// are the bake's: their river's).</summary>
    static void NonPerennialArms()
    {
        var d = NonPerennialData.Read(File2(7, 2, 1, 0));
        var reaches = new List<RiverReach> { Reach(30, -5.8, 73.2f, -1), Reach(31, 10, 40f, -1), Reach(45, 7, 300f, -1), Reach(46, 7, 50f, -1) };
        var wet = d.WetSourceFlow(reaches);
        Check(wet[0] == 0, "an often-dry reach has no wet-source flow");
        Check(wet[1] == 40 && wet[2] == 300 && wet[3] == 50, "every other reach has all of its flow, beyond the file's list too");
    }

    /// <summary>A flow file as tools/bake_river_flow.py writes it: one tile (N10E020, 4 x 4 pixels) with flow, one without
    /// (N10E021), a reroute of reach 2 after its vertex 1.</summary>
    static byte[] FlowFile1(ulong key = 0x1234, int version = 1)
    {
        int TX = 360, TY = 180, t1 = (10 + 90) * TX + 20 + 180, t2 = t1 + 1;
        var states = new byte[TX * TY];
        Array.Fill(states, RiverFlowData.NoData);
        states[t1] = RiverFlowData.HasFlow;
        states[t2] = RiverFlowData.NoFlow;
        var raw = new MemoryStream();
        var rw = new BinaryWriter(raw);
        rw.Write(4); rw.Write(4);
        // speed: 50 cm/s in the top-left 2 x 2, else 0; direction: north (64)
        for (int i = 0; i < 16; i++) rw.Write((byte)((i / 4) < 2 && (i % 4) < 2 ? 50 : 0));
        for (int i = 0; i < 16; i++) rw.Write((byte)64);
        byte[] blob;
        using (var bs = new MemoryStream())
        {
            using (var br = new System.IO.Compression.BrotliStream(bs, System.IO.Compression.CompressionLevel.Optimal, true)) br.Write(raw.ToArray());
            blob = bs.ToArray();
        }
        int head = 32, index = 8, offset = head + TX * TY + index;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("EMFL"u8.ToArray()); w.Write(version); w.Write(1); w.Write(TX); w.Write(TY); w.Write(key);
        w.Write(offset + blob.Length);
        w.Write(states);
        w.Write(offset); w.Write(blob.Length);
        w.Write(blob);
        w.Write(1);                       // one reroute
        w.Write(2); w.Write(1); w.Write(2);
        w.Write(10.5f); w.Write(20.5f); w.Write(10.6f); w.Write(20.6f);
        return ms.ToArray();
    }

    /// <summary>The flow file: its tiles, the bilinear flow between pixel centres (none counts as zero), a tile without
    /// flow (still everywhere), no tile (not covered), the reroutes, and a damaged file refused.</summary>
    static void FlowFile()
    {
        var f = RiverFlowData.Read(FlowFile1());
        Check(f.CourseKey == 0x1234, "the course key is read");
        // the centre of pixel (0, 0): lat 11 - 0.125, lon 20.125
        Check(f.At(10.875, 20.125, out double e, out double n) && Math.Abs(n - 0.5) < 1e-6 && Math.Abs(e) < 1e-6, $"a pixel centre's flow: 0.5 m/s north ({e:0.000}, {n:0.000})");
        // between pixel (1, 1) (flow) and (2, 2) (none): half way, a quarter of the speed
        Check(f.At(10.5, 20.5, out e, out n) && Math.Abs(n - 0.125) < 1e-6, $"half way to still water: a quarter ({n:0.000})");
        Check(f.At(10.1, 20.9, out e, out n) && n == 0 && e == 0, "a pixel without flow: none");
        Check(f.At(10.5, 21.5, out e, out n) && n == 0 && e == 0, "a tile without flow: still water, covered");
        Check(!f.At(10.5, 22.5, out _, out _), "no tile: not covered");
        Check(f.Reroutes.Count == 1 && f.Reroutes[2].After == 1 && f.Reroutes[2].Lat.Length == 2, "the reroute is read");
        var bad = FlowFile1();
        BitConverter.GetBytes(bad.Length + 10).CopyTo(bad, 28);
        Check(Throws(() => RiverFlowData.Read(bad)), "a reroute offset past the end is refused");
        bad = FlowFile1();
        bad[4] = (byte)(RiverFlowData.FileVersion + 1);
        Check(Throws(() => RiverFlowData.Read(bad)), "a newer version is refused");

        // decision 0092 (EA-0148): version 2's pixel (i, j) lies on (lat0 + 1 - i/H, lon0 + j/W), GLO-90's grid point
        var g = RiverFlowData.Read(FlowFile1(version: 2));
        Check(g.Version == 2 && f.Version == 1, "the file's version is read");
        Check(g.At(10.75, 20.25, out e, out n) && Math.Abs(n - 0.5) < 1e-6 && Math.Abs(e) < 1e-6, $"version 2: pixel (1, 1)'s flow on its grid point ({n:0.000})");
        Check(g.At(10.5, 20.5, out e, out n) && n == 0 && e == 0, $"version 2: pixel (2, 2) on its grid point, no flow ({n:0.000}; version 1 reads a quarter there)");
        Check(g.At(10.625, 20.375, out e, out n) && Math.Abs(n - 0.125) < 1e-6, $"version 2: half way between pixels (1, 1) and (2, 2), a quarter ({n:0.000})");
        Check(f.At(10.625, 20.375, out e, out n) && Math.Abs(n - 0.5) < 1e-6, $"version 1: the same place is pixel (1, 1)'s centre ({n:0.000})");
    }

    /// <summary>A rerouted mouth reach keeps its vertices up to the cut, then follows the new end; levels ease to the old
    /// mouth's; the last vertex is fixed as the old mouth was; the widths go on.</summary>
    static void FlowReroute()
    {
        var f = RiverFlowData.Read(FlowFile1());
        RiverReach R(int n) => new()
        {
            Q = 100, Next = -1, Lat = Enumerable.Range(0, n).Select(i => 10f + 0.01f * i).ToArray(), Lon = Enumerable.Repeat(20f, n).ToArray(),
            Level = Enumerable.Range(0, n).Select(i => (short)(4 - i)).ToArray(), Fixed = Enumerable.Range(0, n).Select(i => i == n - 1).ToArray(),
            Width = Enumerable.Repeat((byte)20, n).ToArray(),
        };
        var list = new List<RiverReach> { R(3), R(3), R(5) };
        int done = RiverFlowData.Reroute(list, f);
        var re = list[2];
        Check(done == 1 && re.Lat.Length == 4, $"reach 2 rerouted: 2 kept + 2 new ({re.Lat.Length})");
        Check(re.Lat[2] == 10.5f && re.Lon[3] == 20.6f, "the new end's points");
        Check(re.Level[1] == 3 && re.Level[3] == 0 && re.Level[2] == 2, $"levels ease from the cut's (3) to the old mouth's (0): {string.Join(",", re.Level)}");
        Check(re.Fixed[3] && !re.Fixed[2], "the last vertex fixed as the old mouth was");
        Check(re.Width.Length == 4 && re.Width[3] == 20, "widths go on");
        Check(list[0].Lat.Length == 3, "other reaches are left as they are");
    }

    /// <summary>Decision 0074 (channel rules 5): a reach's measured half widths averaged along it. A river the mask has 2 and
    /// 3 pixels wide by turns comes out one width; a widening much longer than the average's reach keeps its width in its
    /// middle; a vertex without a measure keeps none.</summary>
    static void OneWidth()
    {
        int n = 200;
        var lens = Enumerable.Repeat(4.0, n - 1).ToArray();
        var m = new double[n];
        for (int k = 0; k < n; k++) m[k] = (k / 10) % 2 == 0 ? 8 : 12;
        m[50] = 0;
        var o = RiverCoursePreparer.OneWidth(m, lens, 2 * 2 * 10);
        double lo = double.MaxValue, hi = 0;
        for (int k = 40; k < 160; k++) if (o[k] > 0) { lo = Math.Min(lo, o[k]); hi = Math.Max(hi, o[k]); }
        Check(hi - lo < 0.5 && Math.Abs(0.5 * (lo + hi) - 10) < 0.5, $"2 and 3 pixels by turns: one width ({lo:0.00}-{hi:0.00})");
        Check(o[50] == 0, "a vertex without a measure keeps none");
        var w = new double[n];
        for (int k = 0; k < n; k++) w[k] = k >= 60 && k < 140 ? 20 : 10;
        o = RiverCoursePreparer.OneWidth(w, lens, 2 * 2 * 10);
        Check(o[100] > 19.5 && o[10] < 10.5, $"a widening 8 widths long keeps its width in the middle ({o[100]:0.00}, outside {o[10]:0.00})");
    }

    /// <summary>
    /// EA-0159 (decision 0095): the river width multiplier. Without one (0) the widths are width rules 1's as before. The
    /// floor factor is multiplier × 10 / N at 1:N (x1 at 1:10 and x2.5 at 1:25 give 1, x5 at 1:100 a half); the mask's ratio
    /// moves the floor fully at 1, not at all at a half or less; a multiplied world's rivers are never under the smallest
    /// stream and its channels no deeper than their half width; the ships' least width shrinks with the floor; next to x1 at
    /// 1:10 the rule meets 2.1's; a built network draws the measured width as the rule says.
    /// </summary>
    static void WidthMultiplier()
    {
        // the rings SpawnSearch lays out for each scale: length = Equator / (2 × scale) in whole 512-block regions
        double RingOf(int scale) => 2 * 512 * Math.Round(SpawnSearch.EquatorMetres / (2.0 * scale) / 512);
        EarthGeometry Ring(double ring) => new EarthGeometry { RingLength = ring, Gutter = 0, BlocksPerDegreeZ = ring / 2 / 180, EquatorZ = ring / 4 };
        RiverNetwork Net(EarthGeometry geo, double multiplier, int widthRules = 1, List<RiverReach> reaches = null) =>
            new RiverNetwork(reaches ?? new List<RiverReach>(), geo, _ => 100, new RiverBuildOptions(100, 1.5)
            {
                StreamMode = "ends", StreamMinQ = 20, SizeScale = geo.RingLength / 204800.0, WidthRules = widthRules, ChannelRules = 5, WidthMultiplier = multiplier,
            });
        foreach (var (scale, want) in new[] { (5, 5.0), (10, 10.0), (25, 25.0), (50, 50.0), (100, 100.0), (200, 200.0) })
            Check(RiverNetwork.ScaleOf(RingOf(scale)) == want, $"the scale of 1:{scale}'s ring is {RiverNetwork.ScaleOf(RingOf(scale))}");
        Check(RiverNetwork.FloorFactorFor(1, RingOf(10)) == 1 && Math.Abs(RiverNetwork.FloorFactorFor(2.5, RingOf(25)) - 1) < 1e-12
            && RiverNetwork.FloorFactorFor(5, RingOf(100)) == 0.5 && Math.Abs(RiverNetwork.FloorFactorFor(7, RingOf(200)) - 0.35) < 1e-12
            && RiverNetwork.FloorFactorFor(1, RingOf(5)) == 1 && RiverNetwork.FloorFactorFor(2, RingOf(10)) == 2,
            "the floor factor: x1 at 1:10 and x2.5 at 1:25 are 1:10's floor, x5 at 1:100 half, x7 at 1:200 0.35; 1:5 keeps 1:10's blocks");
        Check(RiverNetwork.MaskWeightFor(0.35) == 0 && RiverNetwork.MaskWeightFor(0.5) == 0 && RiverNetwork.MaskWeightFor(0.75) == 0.5
            && RiverNetwork.MaskWeightFor(1) == 1 && RiverNetwork.MaskWeightFor(2) == 1, "the mask's weight on the floor: none to a half, all from 1");
        var g100 = Ring(RingOf(100));
        double mpb = Geo.MetresPerDegree / g100.BlocksPerDegreeZ;
        var legacy = Net(g100, 0);
        var x5 = Net(g100, 5);
        // without a multiplier: width rules 1's formula, unchanged
        foreach (double q in new[] { 20.0, 50, 100, 677, 2355, 200000 })
        {
            double nav = Math.Max(0.5 * Math.Clamp(2.0 * Math.Pow(Math.Max(q, 100), 0.3), 5, 48) * 1.5, 0.5 * RiverNetwork.RealWidthMetres(Math.Max(q, 100)) / mpb);
            double want = q >= 100 ? nav : RiverNetwork.MinHalfWidth + (nav - RiverNetwork.MinHalfWidth) * Math.Pow(legacy.StreamT(q), 1.5);
            Check(legacy.HalfWidth(q) == want, $"no multiplier at 1:100: Q {q} as width rules 1 ({2 * legacy.HalfWidth(q):0.00} blocks)");
        }
        Check(legacy.FloorFactor == 1 && legacy.MaskWeight == 1 && legacy.ShipFloorFactor == 1 && legacy.WidthMultiplier == 0, "no multiplier: the whole floor, the mask's ratio, the ships' least width");
        // x5 at 1:100: half the floor in blocks; the real width where wider; the ships' least width halved
        Check(x5.FloorFactor == 0.5 && x5.MaskWeight == 0 && x5.ShipFloorFactor == 0.5, "x5 at 1:100: the floor ×0.5, the mask off the floor");
        Check(Math.Abs(2 * x5.HalfWidth(100) - 0.5 * 2 * legacy.HalfWidth(100)) < 1e-9, $"x5 at 1:100: a 100 m3/s river {2 * x5.HalfWidth(100):0.00} blocks (2.1: {2 * legacy.HalfWidth(100):0.00})");
        Check(Math.Abs(2 * x5.HalfWidth(677) - 10.6) < 0.1 && Math.Abs(2 * x5.HalfWidth(2355) - 15.4) < 0.1, $"x5 at 1:100: the Daugava {2 * x5.HalfWidth(677):0.0}, the Rhine {2 * x5.HalfWidth(2355):0.0} blocks");
        double amazonReal = 0.5 * RiverNetwork.RealWidthMetres(200000) / mpb;
        Check(Math.Abs(x5.HalfWidth(200000) - Math.Max(0.5 * legacy.HalfWidth(200000), amazonReal)) < 1e-9, "x5 at 1:100: the Amazon half its capped floor, or its real width where wider");
        // the mask's measure at x5: the floor stands (no widening, no narrowing), the real part follows within 0.5-3 times
        double typical = 0.5 * RiverNetwork.RealWidthMetres(677);
        Check(x5.MeasuredHalfWidth(677, 3 * typical) == x5.HalfWidth(677) && x5.MeasuredHalfWidth(677, 0.6 * typical) == x5.HalfWidth(677),
            "x5 at 1:100: a broad or a narrow river by the mask keeps the floor");
        Check(Math.Abs(x5.MeasuredHalfWidth(200000, 2 * 0.5 * RiverNetwork.RealWidthMetres(200000)) - 2 * amazonReal) < 1e-9,
            "x5 at 1:100: the mask widens the real width (twice the Amazon's usual)");
        // never under the smallest stream; the channel no deeper than its half width
        var thin = Net(Ring(RingOf(200)), 0.5);
        Check(thin.HalfWidth(100) >= RiverNetwork.MinHalfWidth && thin.HalfWidth(20) == RiverNetwork.MinHalfWidth && thin.MeasuredHalfWidth(100, 1) == RiverNetwork.MinHalfWidth,
            $"×0.5 at 1:200: never under the smallest stream ({2 * thin.HalfWidth(100):0.00} blocks for 100 m3/s)");
        Check(x5.Depth(100) == Math.Min(legacy.Depth(100), x5.HalfWidth(100)) && thin.Depth(100) == 1 && legacy.Depth(677) > legacy.HalfWidth(677) * 0 + 6,
            $"x5 at 1:100: a 100 m3/s channel {x5.Depth(100):0.00} deep in {2 * x5.HalfWidth(100):0.00} blocks (2.1: {legacy.Depth(100):0.00})");
        // next to x1 at 1:10 (saved as 0) the rule meets 2.1's: the mask's ratio times the drawn width
        var g10 = Ring(RingOf(10));
        var old10 = Net(g10, 0);
        var near10 = Net(g10, 0.999);
        double mpb10 = Geo.MetresPerDegree / g10.BlocksPerDegreeZ;
        foreach (var (q, half) in new[] { (91.0, 45.0), (677.0, 225.0), (1937.0, 100.0), (50.0, 40.0) })
        {
            double ratio = Math.Clamp(half / (0.5 * RiverNetwork.RealWidthMetres(q)), 0.5, 3);
            double was = ratio * old10.HalfWidth(q), now = near10.MeasuredHalfWidth(q, half);
            Check(Math.Abs(now / was - 1) < 0.005, $"×0.999 at 1:10 meets 2.1 within 0.5 %: Q {q} {2 * now:0.00} against {2 * was:0.00} blocks");
        }
        Check(Net(g100, 5, widthRules: 0).WidthMultiplier == 0, "width rules 0: no multiplier");
        // a built network: a straight reach the mask measured 450 m wide (half 45 units of 5 m), at the course's centre
        int n = 41;
        RiverReach Reach() => new RiverReach
        {
            Q = 677, Next = -1, Lat = Enumerable.Repeat(10f, n).ToArray(), Lon = Enumerable.Range(0, n).Select(k => (float)(k * 0.02)).ToArray(),
            Level = Enumerable.Range(0, n).Select(k => (short)(40 - k)).ToArray(), Fixed = new bool[n], Width = Enumerable.Repeat((byte)45, n).ToArray(),
        };
        double At(EarthGeometry geo, RiverNetwork net)
        {
            geo.FromGeo(10, 0.4, out double x, out double z);
            return net.Nearest(x, z, out var hit) ? hit.HalfWidth : -1;
        }
        double r677 = 225 / (0.5 * RiverNetwork.RealWidthMetres(677));
        double hitOld = At(g100, Net(g100, 0, reaches: new List<RiverReach> { Reach() }));
        double hit5 = At(g100, Net(g100, 5, reaches: new List<RiverReach> { Reach() }));
        double hit10 = At(g10, Net(g10, 0, reaches: new List<RiverReach> { Reach() }));
        double hit10m = At(g10, Net(g10, 1, reaches: new List<RiverReach> { Reach() }));
        Check(Math.Abs(hitOld - legacy.HalfWidth(677) * r677) < 0.01, $"a built network without a multiplier: the floor times the mask's ratio {r677:0.00} ({2 * hitOld:0.0} blocks)");
        Check(Math.Abs(hit5 - x5.HalfWidth(677)) < 0.01, $"a built x5 network at 1:100: the floor, the mask off it ({2 * hit5:0.0} blocks)");
        Check(Math.Abs(hit10m - hit10) < 1e-6, $"a built x1 network at 1:10 draws as 2.1 ({2 * hit10m:0.0} against {2 * hit10:0.0} blocks)");
    }

    /// <summary>EA-0179 (decision 0098): the coarse river rules' one-block streams. Every stream under the rivers' MinQ is the
    /// smallest (StreamT 0: MinHalfWidth, a block deep), not a narrowing; the rivers as before; nothing undefined where the
    /// streams start at the rivers' own MinQ (1:100 and 1:200: no streams, no logarithm of 1).</summary>
    static void OneBlockStreams()
    {
        EarthGeometry Ring(double ring) => new EarthGeometry { RingLength = ring, Gutter = 0, BlocksPerDegreeZ = ring / 2 / 180, EquatorZ = ring / 4 };
        RiverNetwork Net(double ring, double streamMinQ, bool oneBlock) =>
            new RiverNetwork(new List<RiverReach>(), Ring(ring), _ => 100, new RiverBuildOptions(100, 1.5)
            {
                StreamMode = "all", StreamMinQ = streamMinQ, SizeScale = ring / 204800.0, WidthRules = 1, ChannelRules = 5, WidthMultiplier = 3,
                OneBlockStreams = oneBlock,
            });
        double ring50 = 2 * 512 * Math.Round(SpawnSearch.EquatorMetres / 100.0 / 512), ring100 = 2 * 512 * Math.Round(SpawnSearch.EquatorMetres / 200.0 / 512);
        var one = Net(ring50, 40, true);
        var narrowing = Net(ring50, 40, false);
        Check(one.OneBlockStreams && one.StreamT(40) == 0 && one.StreamT(99.9) == 0 && one.StreamT(100) == 1, "one-block streams: StreamT 0 under the rivers, 1 from them");
        Check(one.HalfWidth(40) == RiverNetwork.MinHalfWidth && one.HalfWidth(90) == RiverNetwork.MinHalfWidth && one.Depth(90) == 1,
            $"one-block streams at 1:50: 40 and 90 m³/s one block wide and deep ({2 * one.HalfWidth(90):0.00} blocks, {one.Depth(90):0.00} deep)");
        Check(narrowing.HalfWidth(90) > 2 * RiverNetwork.MinHalfWidth && one.HalfWidth(100) == narrowing.HalfWidth(100) && one.HalfWidth(677) == narrowing.HalfWidth(677),
            $"without them a 90 m³/s stream narrows ({2 * narrowing.HalfWidth(90):0.0} blocks); the rivers are the same either way");
        var riversOnly = Net(ring100, 100, true);
        Check(double.IsFinite(riversOnly.HalfWidth(60)) && riversOnly.HalfWidth(60) == RiverNetwork.MinHalfWidth && double.IsFinite(riversOnly.Depth(60))
            && double.IsFinite(riversOnly.Bank(60)) && double.IsFinite(riversOnly.Valley(60)), "streams from the rivers' own 100 m³/s (1:100): a fading reach under it is still the smallest stream, nothing undefined");
        CoarseRiversRules(Ring(ring100));
    }

    /// <summary>EA-0179 (coarse river rules 2): rivers to their end and head streams, on one river at 1:100 (streams and
    /// rivers from 100 m³/s). Its course, in flow order: a 60 m³/s head (11 km) into an 80 m³/s reach (5.6 km), which with a
    /// 30 m³/s tributary makes a 150 m³/s river; below it the river loses water, to 99 and then 60 m³/s.</summary>
    static void CoarseRiversRules(EarthGeometry geo)
    {
        RiverReach R(double lon0, double lon1, float q, int next, int n = 6) => new RiverReach
        {
            Q = q, Next = next, Lat = Enumerable.Repeat(10f, n).ToArray(),
            Lon = Enumerable.Range(0, n).Select(k => (float)(lon0 + (lon1 - lon0) * k / (n - 1))).ToArray(),
            Level = Enumerable.Range(0, n).Select(k => (short)40).ToArray(), Fixed = new bool[n],
        };
        // 0.1° of longitude at 10° N is 10.95 km
        var reaches = new List<RiverReach>
        {
            R(0.00, 0.10, 60, 1, 11), // 0: the head, 11 km
            R(0.10, 0.15, 80, 3),     // 1: its biggest inflow's next piece, 5.5 km
            R(0.15, 0.18, 30, 3),     // 2: a tributary (not the biggest inflow)
            R(0.15, 0.30, 150, 4),    // 3: the river
            R(0.30, 0.40, 99, 5),     // 4: it loses water
            R(0.40, 0.50, 60, -1),    // 5: and more, to its end
        };
        RiverBuildOptions Options(bool toEnd, double headKm) => new RiverBuildOptions(100, 1.5)
        {
            StreamMode = "all", StreamMinQ = 100, SizeScale = geo.RingLength / 204800.0, WidthRules = 1, ChannelRules = 5, WidthMultiplier = 5,
            OneBlockStreams = true, ContinueDownstream = true, RiversToTheirEnd = toEnd, HeadStreamKm = headKm,
        };
        var none = CoarseRivers.Apply(reaches, Options(false, 0), 100, "all");
        Check(ReferenceEquals(none.Reaches, reaches) && none.HeadStreams == null, "coarse river rules 1: the reaches as given");
        var both = CoarseRivers.Apply(reaches, Options(true, 10), 100, "all");
        Check(both.Raised == 2 && both.Reaches[4].Q == 100 && both.Reaches[5].Q == 100 && reaches[4].Q == 99 && reaches[5].Q == 60,
            $"rules 2: the two reaches below the river are drawn at its 100 m³/s ({both.Raised} raised); the data's reaches unchanged");
        Check(both.Reaches[3].Q == 150 && both.Reaches[1].Q == 80 && both.Reaches[2].Q == 30, "rules 2 leave the river and the reaches above it alone");
        var heads = both.HeadStreams;
        Check(both.Heads == 1 && heads != null && heads[0] && heads[1] && !heads[2] && !heads[3] && !heads[4] && !heads[5],
            "head streams: the river's biggest inflow and the head above it are its head stream; the tributary is not");
        double kept0 = CoarseRivers.LengthKm(both.Reaches[0]);
        Check(Math.Abs(both.HeadKm - 10) < 0.01 && kept0 >= 10 - CoarseRivers.LengthKm(reaches[1]) - 0.01 && kept0 < CoarseRivers.LengthKm(reaches[0]) - 1
            && both.Reaches[0].Lat.Length == both.Reaches[0].Level.Length && both.Reaches[0].Lon[^1] == reaches[0].Lon[^1] && reaches[0].Lat.Length == 11,
            $"head streams: 10 km of them; the head reach cut to its last {kept0:0.0} of {CoarseRivers.LengthKm(reaches[0]):0.0} km, its end kept, the data's reach whole");
        var net = new RiverNetwork(reaches, geo, _ => 100, Options(true, 10));
        Check(net.Kept(0) && net.Kept(1) && !net.Kept(2) && net.Kept(3) && net.Kept(4) && net.Kept(5), "the network draws the head stream, the river and its lower course; not the tributary");
        Check(net.RiversToTheirEnd == 2 && net.HeadStreamCount == 1 && net.Reaches[5].Q == 100 && net.HeadStreams[1],
            "the network reports the rules' work (its reaches are the copies)");
        Check(net.HalfWidth(net.Reaches[5].Q) == net.HalfWidth(150) || net.HalfWidth(net.Reaches[5].Q) == net.HalfWidth(100),
            $"the river's lower course keeps a river's width ({2 * net.HalfWidth(net.Reaches[5].Q):0.0} blocks, not {2 * RiverNetwork.MinHalfWidth:0.0})");
        Check(net.HalfWidth(net.Reaches[1].Q) == RiverNetwork.MinHalfWidth, "the head stream is one block wide");
        var off = new RiverNetwork(reaches, geo, _ => 100, Options(false, 0));
        Check(off.Kept(5) && off.HalfWidth(off.Reaches[5].Q) == RiverNetwork.MinHalfWidth && !off.Kept(1),
            "rules 1: the lower course is a one-block stream and the river starts out of nothing (what rules 2 changes)");
        // rules 2: a one-block stream lags a streams layer's StreamMaxLag before it falls; the river keeps ChuteMaxDrop
        var steep = new RiverNetwork(reaches, geo, _ => 100, Options(true, 10) with { SteepStreams = true });
        Check(steep.LagOf(1) == RiverNetwork.StreamMaxLag && steep.LagOf(0) == RiverNetwork.StreamMaxLag && steep.LagOf(3) == RiverNetwork.ChuteMaxDrop
            && steep.LagOf(5) == RiverNetwork.ChuteMaxDrop && off.LagOf(1) == RiverNetwork.ChuteMaxDrop && steep.MaxLag == RiverNetwork.ChuteMaxDrop,
            "rules 2: the one-block streams fall at a lag of StreamMaxLag, the river (its lower course too) at ChuteMaxDrop; rules 1 all at ChuteMaxDrop");
        // a stream falling a block in 2 for 2 km, alone: rules 1 lag 4 blocks before each fall, rules 2 one (the lag only: the
        // backward bound is the world network's as before)
        int sn = 41;
        var slope = new List<RiverReach> { new RiverReach
        {
            Q = 60, Next = -1, Lat = Enumerable.Repeat(10f, sn).ToArray(), Lon = Enumerable.Range(0, sn).Select(k => (float)(1 + k * 0.0045)).ToArray(),
            Level = Enumerable.Range(0, sn).Select(k => (short)(2000 - 250 * k)).ToArray(), Fixed = new bool[sn],
        } };
        RiverBuildOptions Slope(bool steepOn) => new RiverBuildOptions(100, 1.5)
        {
            StreamMode = "all", StreamMinQ = 40, SizeScale = geo.RingLength / 204800.0, WidthRules = 1, ChannelRules = 5, WidthMultiplier = 3,
            OneBlockStreams = true, PoolLayout = RiverNetwork.PoolLayoutVersion, RiversToTheirEnd = steepOn, SteepStreams = steepOn,
        };
        int Lag(RiverNetwork net)
        {
            int worst = 0;
            if (!net.ReachLine(0, out var xs, out var zs)) return -1;
            for (int k = 1; k < xs.Length; k++)
                for (double t = 0; t < 1; t += 0.05)
                {
                    double px = xs[k - 1] + (xs[k] - xs[k - 1]) * t, pz = zs[k - 1] + (zs[k] - zs[k - 1]) * t;
                    if (!net.Nearest(px, pz, out var h)) continue;
                    double lev = slope[0].Level[k - 1] + (slope[0].Level[k] - slope[0].Level[k - 1]) * t;
                    worst = Math.Max(worst, h.WaterTop - (int)Math.Floor(lev / 100.0));
                }
            return worst;
        }
        int lag1 = Lag(new RiverNetwork(slope, geo, l => (int)Math.Floor(l / 100.0), Slope(false)));
        int lag2 = Lag(new RiverNetwork(slope, geo, l => (int)Math.Floor(l / 100.0), Slope(true)));
        Check(lag2 >= 0 && lag2 <= RiverNetwork.StreamMaxLag + 1 && lag1 > lag2,
            $"a steep one-block stream: water at most {lag2} above its level under rules 2, {lag1} under rules 1");
    }

    /// <summary>Works rules 1 worlds skip the canals (decision 0069 rule 6), rules 2 read them; worlds before rules 4 skip
    /// the areas rules 4 adds (EA-0053, EA-0057).</summary>
    static void WorksCanals()
    {
        const byte canal = Works1318Data.Canal, late = Works1318Data.Late;
        Check(Works1318Data.SkipFor(1) == (canal | late) && Works1318Data.SkipFor(2) == late && Works1318Data.SkipFor(3) == late,
            "rules 1 skip the canal areas, rules 2 read them; rules 1-3 skip works rules 4's areas");
        Check(Works1318Data.SkipFor(4) == 0 && Works1318Data.SkipFor(Works1318Data.RulesVersion) == 0, "rules 4 read every area");
        Check((canal & (Works1318Data.Sea | Works1318Data.Lake | Works1318Data.Land | Works1318Data.Fen)) == 0, "the canal flag is its own bit");
        Check((late & (canal | Works1318Data.Sea | Works1318Data.Lake | Works1318Data.Land | Works1318Data.Fen)) == 0, "the late flag is its own bit");
    }

    /// <summary>The baked file (tools/bake_nonperennial.py) on courses rules 4's list: the reported places. The Seymareh at the
    /// Seimare Dam (reaches 440162-447117), the Daugava's and the Lielupe's mouths flow all year; the Draa near 30.3 N 5.8 W
    /// (396846, 399401) is often dry.</summary>
    static void NonPerennialRealFile(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine($"(no {path}: the real file's checks skipped)"); return; }
        var d = NonPerennialData.Read(File.ReadAllBytes(path));
        Check(d.Count == 624_024 && d.CourseKey == 0x9693b65a3a265a62, $"the real file covers courses rules 4's 624,024 reaches ({d.Count})");
        Check(new[] { 440162, 441940, 443694, 445420, 447117 }.All(r => d.ClassOf(r) == NonPerennialData.Perennial), "the Seymareh at the dam flows all year");
        Check(d.ClassOf(553238) == NonPerennialData.Perennial && d.ClassOf(293749) == NonPerennialData.Perennial, "the Daugava's and the Lielupe's mouths flow all year");
        Check(d.ClassOf(396846) == NonPerennialData.OftenDry && d.ClassOf(399401) == NonPerennialData.OftenDry, "the Draa is often dry");
    }
}
