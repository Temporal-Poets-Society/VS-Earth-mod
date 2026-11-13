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
    static byte[] FlowFile1(ulong key = 0x1234)
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
        w.Write("EMFL"u8.ToArray()); w.Write(1); w.Write(1); w.Write(TX); w.Write(TY); w.Write(key);
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
        bad[4] = 2;
        Check(Throws(() => RiverFlowData.Read(bad)), "another version is refused");
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
