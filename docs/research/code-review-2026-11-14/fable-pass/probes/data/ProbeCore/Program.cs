using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using EarthMap.Core;

// Throwaway probes for the review (concern: untrusted and damaged input, commands, start-up cost).
// dotnet run -c Release --project ProbeCore -- <mode> <args>
static class Probe
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        switch (mode)
        {
            case "readers": Readers(args[1]); break;
            case "egl": Egl(); break;
            case "tiff": Tiff(args[1]); break;
            case "manifest": Manifest(args[1], args[2]); break;
            case "http": Http(args[1], args[2]).GetAwaiter().GetResult(); break;
            case "badsource": BadSource(args[1]); break;
            case "diskfull": DiskFull(args[1], args[2]); break;
            case "weathercmd": WeatherCmd(args[1]); break;
            case "load": Load(args[1]); break;
            case "ground": Ground(args[1]); break;
            default: Console.WriteLine("modes: readers egl tiff manifest http badsource diskfull weathercmd load ground"); return 2;
        }
        Console.Out.Flush();
        Environment.Exit(0);
        return 0;
    }

    // ------------------------------------------------------------------------------------------ mutation harness

    sealed record Mutation(string Name, Func<byte[], byte[]> Make);

    static IEnumerable<Mutation> Mutations(int headerInts = 7)
    {
        yield return new("zero-length", b => Array.Empty<byte>());
        yield return new("3 bytes", b => b.Take(3).ToArray());
        yield return new("header only (16 B)", b => b.Take(16).ToArray());
        yield return new("half", b => b.Take(b.Length / 2).ToArray());
        yield return new("minus 1 byte", b => b.Take(b.Length - 1).ToArray());
        for (int i = 1; i <= headerInts; i++)
        {
            int at = 4 * i;
            foreach (int v in new[] { int.MaxValue, -1, 0, 0x01000000 })
                yield return new($"int32@{at}={(v == int.MaxValue ? "MAX" : v == 0x01000000 ? "16M" : v.ToString())}", b =>
                {
                    var c = (byte[])b.Clone();
                    if (at + 4 <= c.Length) BitConverter.TryWriteBytes(c.AsSpan(at, 4), v);
                    return c;
                });
        }
        for (int seed = 1; seed <= 4; seed++)
        {
            int s = seed;
            yield return new($"8 body bytes flipped (seed {s})", b =>
            {
                var c = (byte[])b.Clone();
                var rnd = new Random(s);
                if (c.Length > 64) for (int k = 0; k < 8; k++) c[rnd.Next(32, c.Length)] ^= (byte)(1 << rnd.Next(8));
                return c;
            });
        }
        yield return new("garbage (random bytes, same length, magic kept)", b =>
        {
            var c = new byte[b.Length];
            new Random(7).NextBytes(c);
            Array.Copy(b, c, Math.Min(4, b.Length));
            return c;
        });
    }

    static readonly Dictionary<string, int> summary = new();

    /// <summary>Runs one reader over every mutation; prints each outcome that is not a clean InvalidDataException at load.</summary>
    static void Fuzz(string what, byte[] original, Action<byte[]> load, Action exercise = null, int headerInts = 7, string[] cleanTypes = null)
    {
        cleanTypes ??= new[] { "InvalidDataException", "EndOfStreamException" };
        var outcomes = new Dictionary<string, List<string>>();
        // the unchanged file first: must load
        string baseline = Run(() => { load(original); exercise?.Invoke(); }, out long baseAlloc, out long baseMs);
        Console.WriteLine($"== {what} ({original.Length:N0} B): unchanged -> {baseline} ({baseAlloc / 1e6:F0} MB allocated, {baseMs} ms)");
        foreach (var m in Mutations(headerInts))
        {
            byte[] bytes = m.Make(original);
            if (Environment.GetEnvironmentVariable("PROBE_VERBOSE") == "1") { Console.WriteLine("      .. " + m.Name); Console.Out.Flush(); }
            string phase = "load";
            string result = Run(() =>
            {
                load(bytes);
                phase = "use";
                exercise?.Invoke();
            }, out long alloc, out long ms);
            string key = result == "ok" ? "accepted (no error)" : phase + ": " + result;
            if (alloc > Math.Max(400e6, 6.0 * baseAlloc)) key += " [ALLOC " + (alloc / 1e6).ToString("F0") + " MB]";
            if (ms > 20000) key += " [SLOW " + ms + " ms]";
            if (!outcomes.TryGetValue(key, out var list)) outcomes[key] = list = new List<string>();
            list.Add(m.Name);
        }
        foreach (var kv in outcomes.OrderBy(k => k.Key))
        {
            bool clean = kv.Key.StartsWith("load: ") && cleanTypes.Any(t => kv.Key == "load: " + t);
            Console.WriteLine($"   {(clean ? "  " : kv.Key.StartsWith("accepted") ? " ~" : "!!")} {kv.Key}  x{kv.Value.Count}" + (clean ? "" : "   <- " + string.Join("; ", kv.Value.Take(6)) + (kv.Value.Count > 6 ? "; ..." : "")));
            string sk = clean ? "clean InvalidData at load" : kv.Key.StartsWith("accepted") ? "accepted" : kv.Key.StartsWith("use:") ? "throws at first use (lazy)" : "other exception type at load";
            summary[sk] = summary.GetValueOrDefault(sk) + kv.Value.Count;
        }
    }

    static string Run(Action a, out long alloc, out long ms)
    {
        string result = null;
        long before = GC.GetTotalAllocatedBytes(false);
        var sw = Stopwatch.StartNew();
        var t = new Thread(() =>
        {
            try { a(); result = "ok"; }
            catch (Exception e) { result = e.GetType().Name; }
        }, 16 << 20);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(60))) result = "HANG (> 60 s)";
        ms = sw.ElapsedMilliseconds;
        alloc = GC.GetTotalAllocatedBytes(false) - before;
        return result;
    }

    static void Readers(string root)
    {
        string ed = Path.Combine(root, "earthdata");
        byte[] F(string name) => File.ReadAllBytes(Path.Combine(ed, name));
        Stream S(byte[] b) => new MemoryStream(b, false);
        var clean = EarthData.LoadFromFolder(root, false);
        int W = 10800;

        // the client's files, through the whole load (as a client or server start reads them)
        string[] clientFiles = { "earth.json", "climate.bin", "climatefix.bin", "climatefix2.bin", "climatefix3.bin", "climatefix4.bin", "weather.bin", "weatherground.bin", "temperature.bin", "rain.bin", "aridity.bin" };
        var cache = clientFiles.ToDictionary(n => n, F);
        foreach (string victim in Environment.GetEnvironmentVariable("PROBE_SKIP_CLIENT") == "1" ? new string[0] : clientFiles)
        {
            EarthData d = null;
            Fuzz("client load with " + victim, cache[victim], b =>
            {
                d = EarthData.Load(name => name == victim ? S(b) : cache.TryGetValue(name, out var o) ? S(o) : null, false);
            }, () =>
            {
                d.SetClimateFix(EarthData.ClimateFixVersion); d.SetFineTemperature(true); d.SetFineRain(true); d.SetPenmanAridity(true);
                var wg = d.WeatherGround;
                double sink = 0;
                for (double la = -85; la <= 85; la += 9.7) for (double lo = -179; lo <= 179; lo += 13.3) sink += d.SampleClimate(la, lo).Rain;
                if (d.Weather != null) for (double la = -85; la <= 85; la += 19.7) for (double lo = -179; lo <= 179; lo += 23.3) sink += d.Weather.Sample(la, lo, 0.3).Temp;
                // optional files report their error instead of throwing: count it as a clean refusal
                string err = d.WeatherError ?? d.FineTemperatureError ?? d.FineRainError ?? d.PenmanError ?? d.WeatherGroundError;
                if (err != null) throw new InvalidDataException("(reported as an error string) " + err);
            }, victim == "earth.json" ? 0 : 7, new[] { "InvalidDataException", "EndOfStreamException", "JsonReaderException", "JsonException" });
        }

        // terrain.bin through the server's load, tiles decoded by sampling
        if (Environment.GetEnvironmentVariable("PROBE_SKIP_CLIENT") != "1")
        {
            byte[] ej = F("earth.json"), cl = F("climate.bin");
            EarthData d = null;
            Fuzz("server load with terrain.bin (other optional files left out)", F("terrain.bin"), b =>
            {
                d = EarthData.Load(name => name == "terrain.bin" ? S(b) : name == "earth.json" ? S(ej) : name == "climate.bin" ? S(cl) : null, true);
            }, () =>
            {
                double sink = 0;
                for (double la = -88; la <= 88; la += 5.9) for (double lo = -179; lo <= 179; lo += 6.1) sink += d.SampleTerrain(la, lo).Elev;
            });
        }

        Fuzz("rivers.bin (RiverData.Read)", F("rivers.bin"), b => RiverData.Read(b));
        Fuzz("islands.bin", F("islands.bin"), b => IslandData.Read(S(b), W));
        Fuzz("lakes.bin", F("lakes.bin"), b => LakeData.Read(S(b), W));
        Fuzz("shores.bin", F("shores.bin"), b => ShoreData.Read(S(b), W));
        Fuzz("outlets.bin", F("outlets.bin"), b => OutletData.Read(S(b), W));
        Fuzz("coasts.bin", F("coasts.bin"), b => CoastData.Read(S(b)));
        Fuzz("sea-referee.bin", F("sea-referee.bin"), b => SeaRefereeData.Read(S(b), W));
        Fuzz("rivers-glo90-snap-2.bin", F("rivers-glo90-snap-2.bin"), b => RiverSnapData.Read(S(b)));
        Fuzz("rivers-glo90-arms-2.bin", F("rivers-glo90-arms-2.bin"), b => RiverArmsData.Read(S(b)));
        Fuzz("works-1318-2.bin", F("works-1318-2.bin"), b => Works1318Data.Read(b));
        Fuzz("rivers-nonperennial-2.bin", F("rivers-nonperennial-2.bin"), b => NonPerennialData.Read(b));
        Fuzz("shores-osm-1.bin", F("shores-osm-1.bin"), b => ShoreMaterialData.Read(b));
        Fuzz("stream-heads-1.bin", F("stream-heads-1.bin"), b => StreamHeads.Read(S(b)));
        Fuzz("summits.bin (TiledGrid)", F("summits.bin"), b => { var g = new TiledGrid(b, "EMSU", 1); double s = 0; for (double la = -60; la <= 70; la += 3.1) for (double lo = -179; lo <= 179; lo += 3.3) s += g.Bilinear(la, lo); });
        Fuzz("glaciers.bin (TiledGrid)", F("glaciers.bin"), b => { var g = new TiledGrid(b, "EMGL", 1); double s = 0; for (double la = -60; la <= 80; la += 3.1) for (double lo = -179; lo <= 179; lo += 3.3) s += g.Bilinear(la, lo); });
        Fuzz("peaks.json", F("peaks.json"), b => new PeakCatalog(b), null, 0, new[] { "InvalidDataException", "JsonReaderException", "JsonException" });

        Console.WriteLine();
        Console.WriteLine("SUMMARY of mutated inputs by outcome:");
        foreach (var kv in summary.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
    }

    /// <summary>The 2' ground sets (assets or the data store) and what a damaged one does at load and at first use.</summary>
    static void Ground(string root)
    {
        string dir = Path.Combine(root, "assets", "earth", "config", "ground");
        foreach (string name in new[] { "global-crust1.egt", "global-glaciation.egts", "global-permafrost.egts" })
        {
            byte[] file = File.ReadAllBytes(Path.Combine(dir, name));
            List<GroundTile> tiles = null;
            Fuzz(name + " (GroundTile.ReadAll, then every tile unpacked as a column read would)", file, b => tiles = GroundTile.ReadAll(b), () =>
            {
                foreach (var t in tiles)
                    foreach (var f in t.Fields)
                        t.ValueAtCell(f, 0, 0);
            }, 7, new[] { "FormatException", "InvalidDataException", "JsonReaderException", "JsonException", "KeyNotFoundException", "ArgumentException", "ArgumentOutOfRangeException", "InvalidOperationException" });
        }
        // the lazy file path (the 30" sets): a set truncated after the server read its headers
        string tmp = Path.Combine(Path.GetTempPath(), "probe-egts-" + Guid.NewGuid().ToString("N") + ".egts");
        byte[] set = File.ReadAllBytes(Path.Combine(dir, "global-permafrost.egts"));
        File.WriteAllBytes(tmp, set);
        var lazy = GroundTile.ReadAllFromFile(tmp);
        Console.WriteLine($"lazy set: {lazy.Count} tiles' headers read from {set.Length:N0} bytes");
        using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) fs.SetLength(set.Length / 3);
        int threw = 0, ok = 0; string first = null;
        foreach (var t in lazy)
            try { t.ValueAtCell(t.Fields[0], 0, 0); ok++; }
            catch (Exception e) { threw++; first ??= e.GetType().Name + ": " + e.Message; }
        Console.WriteLine($"after the file was cut to a third while 'the server ran': {ok} tiles still read, {threw} throw at first column read; first: {first}");
        // a deflate bomb in a tile body: 64 MB of zeros declared as the header's bodyBytes
        File.Delete(tmp);
    }

    // ------------------------------------------------------------------------------------------ GLO-90 tiles

    static DemTiles.Tile Synthetic(int latS, int lonW)
    {
        int cols = DemTiles.Glo90Cols(latS), rows = DemTiles.Glo90Rows;
        var t = new DemTiles.Tile { LatS = latS, LonW = lonW, Cols = cols, Rows = rows, Heights = new short[cols * rows] };
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) t.Heights[r * cols + c] = (short)(200 + 80 * Math.Sin(r / 37.0) + 60 * Math.Cos(c / 23.0) + latS);
        t.Min = t.Heights.Min(); t.Max = t.Heights.Max();
        return t;
    }

    static void Egl()
    {
        byte[] file = DemTiles.Encode(Synthetic(56, 24));
        Fuzz(".egl tile (DemTiles.Decode); FineElevation.Load's filter takes IOException and InvalidDataException", file, b => DemTiles.Decode(b), null, 8);
        // every single header byte set to 0xFF, one at a time: any exception type the reader's filter would let through?
        var types = new Dictionary<string, int>();
        for (int at = 0; at < 48; at++)
            foreach (byte v in new byte[] { 0xFF, 0x00, 0x7F, 0x80 })
            {
                var c = (byte[])file.Clone(); c[at] = v;
                string r;
                try { DemTiles.Decode(c); r = "ok"; } catch (Exception e) { r = e.GetType().Name; }
                types[r] = types.GetValueOrDefault(r) + 1;
            }
        Console.WriteLine("each of the first 48 bytes set to FF/00/7F/80: " + string.Join(", ", types.Select(k => k.Key + " x" + k.Value)));
        // through FineElevation: a damaged file is set aside as .bad and reads as missing
        string dir = Path.Combine(Path.GetTempPath(), "probe-egl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var list = new byte[360 * 180 / 8]; Array.Fill(list, (byte)0xFF);
        var fine = new FineElevation(dir, list);
        var bad = (byte[])file.Clone(); bad[bad.Length / 2] ^= 0x10;
        File.WriteAllBytes(fine.PathOf(56, 24), bad);
        bool got = fine.TryHeight(56.5, 24.5, out double h);
        Console.WriteLine($"FineElevation with a bit-flipped N56E024.egl: TryHeight -> {got}; LastError = {fine.LastError}; files now: {string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName))}");
        File.WriteAllBytes(fine.PathOf(57, 24), Array.Empty<byte>());
        got = fine.TryHeight(57.5, 24.5, out h);
        Console.WriteLine($"FineElevation with a zero-length N57E024.egl: TryHeight -> {got}; LastError = {fine.LastError}");
        // a tile with a valid checksum but of another square's dimensions (a mirror's): refused, not out of range
        var wrong = Synthetic(56, 24); wrong.Cols = 100; wrong.Rows = 100; wrong.Heights = new short[10000];
        File.WriteAllBytes(fine.PathOf(58, 24), DemTiles.Encode(new DemTiles.Tile { LatS = 58, LonW = 24, Cols = 100, Rows = 100, Heights = new short[10000] }));
        got = fine.TryHeight(58.5, 24.5, out h);
        Console.WriteLine($"FineElevation with a 100 x 100 tile of valid checksum: TryHeight -> {got}; LastError = {fine.LastError}");
        Directory.Delete(dir, true);
    }

    static void Tiff(string path)
    {
        byte[] tif = File.ReadAllBytes(path);
        Fuzz("Copernicus GeoTIFF " + Path.GetFileName(path) + " (DemTiles.ReadGeoTiff; not hash-checked, the bucket's bytes)", tif, b => DemTiles.ReadGeoTiff(b), null, 1);
        // the directory's entries: each tag's count and value set to hostile numbers
        long ifd = BitConverter.ToUInt32(tif, 4);
        int n = BitConverter.ToUInt16(tif, (int)ifd);
        var results = new Dictionary<string, List<string>>();
        void Try(string what, Action<byte[]> change)
        {
            var c = (byte[])tif.Clone();
            change(c);
            string r = Run(() => DemTiles.ReadGeoTiff(c), out long alloc, out long ms);
            string key = r + (alloc > 1.5e9 ? $" [ALLOC {alloc / 1e9:F1} GB]" : alloc > 400e6 ? " [ALLOC >400 MB]" : "") + (ms > 5000 ? $" [{ms} ms]" : "");
            if (!results.TryGetValue(key, out var l)) results[key] = l = new List<string>();
            l.Add(what);
        }
        Try("ifd offset = 0xFFFFFFF0", c => BitConverter.TryWriteBytes(c.AsSpan(4, 4), 0xFFFFFFF0u));
        Try("ifd offset = file length - 1", c => BitConverter.TryWriteBytes(c.AsSpan(4, 4), (uint)(c.Length - 1)));
        Try("entry count = 65535", c => BitConverter.TryWriteBytes(c.AsSpan((int)ifd, 2), (ushort)65535));
        for (int i = 0; i < n; i++)
        {
            int e = (int)ifd + 2 + 12 * i;
            int tag = BitConverter.ToUInt16(tif, e);
            foreach (uint v in new uint[] { 0, 1, 0x7FFFFFFF, 0xFFFFFFFF, 60000, 500_000_000 })
            {
                Try($"tag {tag} value={v}", c => BitConverter.TryWriteBytes(c.AsSpan(e + 8, 4), v));
                Try($"tag {tag} count={v}", c => BitConverter.TryWriteBytes(c.AsSpan(e + 4, 4), v));
            }
            Try($"tag {tag} type=99", c => BitConverter.TryWriteBytes(c.AsSpan(e + 2, 2), (ushort)99));
        }
        Console.WriteLine("GeoTIFF directory mutations by outcome:");
        foreach (var kv in results.OrderByDescending(k => k.Value.Count))
            Console.WriteLine($"  {kv.Value.Count,4}  {kv.Key}" + (kv.Key is "InvalidDataException" ? "" : "   e.g. " + string.Join("; ", kv.Value.Take(5))));
    }

    // ------------------------------------------------------------------------------------------ a mirror's manifest

    static byte[] TileList(string root) => File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin"));

    static void Manifest(string root, string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        string repo = Path.Combine(work, "repo");
        Directory.CreateDirectory(repo);
        // a little release: two squares in one part
        byte[] a = DemTiles.Encode(Synthetic(56, 24)), b = DemTiles.Encode(Synthetic(56, 25));
        File.WriteAllBytes(Path.Combine(repo, "part1.zip"), a.Concat(b).ToArray());
        string Sha(byte[] x) => Convert.ToHexString(SHA256.HashData(x)).ToLowerInvariant();
        string Good(string tiles = null, string parts = null) => "{\"format\":\"egl\",\"formatVersion\":1,\"parts\":[" + (parts ?? $"{{\"name\":\"part1.zip\",\"bytes\":{a.Length + b.Length}}}") + "],\"tiles\":{" +
            (tiles ?? $"\"N56E024\":[1,0,{a.Length},\"{Sha(a)}\"],\"N56E025\":[1,{a.Length},{b.Length},\"{Sha(b)}\"]") + "}}";
        var cases = new (string Name, string Json)[]
        {
            ("good", Good()),
            ("length -5", Good($"\"N56E024\":[1,0,-5,\"{Sha(a)}\"]")),
            ("length 2147483647 with part bytes to match (a mirror's manifest is not checked)", Good($"\"N56E024\":[1,0,2147483647,\"{Sha(a)}\"]", "{\"name\":\"part1.zip\",\"bytes\":9999999999}")),
            ("length 1500000000, three squares at once", Good($"\"N56E024\":[1,0,1500000000,\"{Sha(a)}\"],\"N56E025\":[1,0,1500000000,\"{Sha(a)}\"],\"N57E024\":[1,0,1500000000,\"{Sha(a)}\"]", "{\"name\":\"part1.zip\",\"bytes\":9999999999}")),
            ("offset 9223372036854775800 (offset + length overflows)", Good($"\"N56E024\":[1,9223372036854775800,{a.Length},\"{Sha(a)}\"]")),
            ("part index 7", Good($"\"N56E024\":[7,0,{a.Length},\"{Sha(a)}\"]")),
            ("part index 0", Good($"\"N56E024\":[0,0,{a.Length},\"{Sha(a)}\"]")),
            ("part name ..\\\\..\\\\secret", Good(null, "{\"name\":\"..\\\\..\\\\secret\",\"bytes\":100000}")),
            ("part name C:secret (drive-relative, no separator)", Good(null, $"{{\"name\":\"C:secret\",\"bytes\":{a.Length + b.Length}}}")),
            ("part name with ? and # (a URL's query)", Good(null, $"{{\"name\":\"part1.zip?x=1#y\",\"bytes\":{a.Length + b.Length}}}")),
            ("sha of the wrong length", Good($"\"N56E024\":[1,0,{a.Length},\"abcd\"]")),
            ("tiles not an object", "{\"format\":\"egl\",\"formatVersion\":1,\"parts\":[{\"name\":\"p\",\"bytes\":1}],\"tiles\":[1,2]}"),
            ("truncated JSON", Good().Substring(0, 60)),
            ("zero-length", ""),
            ("square name N99E999", Good($"\"N99E999\":[1,0,{a.Length},\"{Sha(a)}\"]")),
            ("wrong tile under the right hash (the other square's bytes)", Good($"\"N56E024\":[1,{a.Length},{b.Length},\"{Sha(b)}\"]")),
        };
        foreach (var (name, json) in cases)
        {
            File.WriteAllText(Path.Combine(repo, "manifest.json"), json);
            string store = Path.Combine(work, "store-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var fine = new FineElevation(store, TileList(root));
            var log = new List<string>();
            using var dl = new DemDownloader(fine, null, 3, repo) { Log = s => { lock (log) log.Add(s); }, Retries = 1 };
            long before = GC.GetTotalAllocatedBytes(false);
            var sw = Stopwatch.StartNew();
            string outcome;
            try
            {
                var tasks = new[] { dl.FetchAsync(56, 24), dl.FetchAsync(56, 25), dl.FetchAsync(57, 24) };
                bool done = Task.WaitAll(tasks, 60000);
                outcome = done ? string.Join("/", tasks.Select(t => t.Result ? "kept" : "no")) : "HANG";
            }
            catch (Exception e) { outcome = "THROWS " + (e.InnerException ?? e).GetType().Name; }
            long alloc = GC.GetTotalAllocatedBytes(false) - before;
            string files = Directory.Exists(store) ? string.Join(",", Directory.GetFiles(store).Select(Path.GetFileName)) : "(no folder)";
            string outside = File.Exists(Path.Combine(work, "secret")) ? " ESCAPED" : "";
            Console.WriteLine($"manifest [{name}]: squares {outcome}; {alloc / 1e6:F0} MB allocated, {sw.ElapsedMilliseconds} ms; store: {files}{outside}");
            lock (log) foreach (string l in log.Take(3)) Console.WriteLine("      log: " + (l.Length > 170 ? l.Substring(0, 170) + "..." : l));
        }
    }

    // ------------------------------------------------------------------------------------------ localhost HTTP

    sealed class Srv : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        public Func<string, string, NetworkStream, Task> Handle;   // (path, headers, stream)
        public readonly List<string> Paths = new();
        public Srv() { listener.Start(); _ = Task.Run(Accept); }
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public string Url => $"http://127.0.0.1:{Port}/";
        async Task Accept()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(stop.Token); } catch { return; }
                _ = Task.Run(async () =>
                {
                    using (c)
                    {
                        try
                        {
                            var s = c.GetStream();
                            var head = new StringBuilder(); var one = new byte[1];
                            while (!head.ToString().EndsWith("\r\n\r\n") && await s.ReadAsync(one) == 1) head.Append((char)one[0]);
                            string path = head.ToString().Split("\r\n")[0].Split(' ')[1];
                            lock (Paths) Paths.Add(path);
                            await Handle(path, head.ToString(), s);
                            await s.FlushAsync();
                        }
                        catch { }
                    }
                });
            }
        }
        public static Task Send(NetworkStream s, string text) => s.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }

    static async Task Http(string root, string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        using var inner = new Srv();
        inner.Handle = async (p, h, s) => await Srv.Send(s, "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\nsecret");
        long sent = 0;
        using var srv = new Srv();
        srv.Handle = async (p, h, s) =>
        {
            if (p.StartsWith("/tofile/")) await Srv.Send(s, "HTTP/1.1 302 Found\r\nLocation: file:///C:/Windows/win.ini\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            else if (p.StartsWith("/toinner/")) await Srv.Send(s, $"HTTP/1.1 302 Found\r\nLocation: {inner.Url}internal\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            else if (p.StartsWith("/loop/")) await Srv.Send(s, $"HTTP/1.1 302 Found\r\nLocation: {p}x\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            else if (p.StartsWith("/endless/"))
            {
                // no Content-Length: a body that never ends (stopped here at 1.5 GB so the probe ends)
                await Srv.Send(s, "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n");
                var block = new byte[1 << 20];
                for (int i = 0; i < 1500; i++) { await s.WriteAsync(block); Interlocked.Add(ref sent, block.Length); }
            }
            else if (p.StartsWith("/gzipbomb/"))
            {
                await Srv.Send(s, "HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: 20\r\nConnection: close\r\n\r\n01234567890123456789");
            }
            else if (p.StartsWith("/wrongrange/"))
                await Srv.Send(s, "HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 0-9/10\r\nContent-Length: 10\r\nConnection: close\r\n\r\n0123456789");
            else await Srv.Send(s, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        };
        using var transport = new DataTransport(TimeSpan.FromSeconds(20)) { ReadTimeout = TimeSpan.FromSeconds(10) };
        async Task Case(string what, Func<Task<string>> run)
        {
            long before = GC.GetTotalAllocatedBytes(false);
            var sw = Stopwatch.StartNew();
            string r;
            try { r = await run(); } catch (Exception e) { r = e.GetType().Name + ": " + e.Message; }
            Console.WriteLine($"http [{what}]: {r}  ({(GC.GetTotalAllocatedBytes(false) - before) / 1e6:F0} MB allocated, {sw.ElapsedMilliseconds} ms)");
        }
        await Case("manifest redirected to file://", async () => { var (found, data, _) = await transport.GetAsync(srv.Url + "tofile/manifest.json"); return $"found {found}, {data?.Length} bytes"; });
        await Case("manifest redirected to another local port (an internal address)", async () => { var (found, data, _) = await transport.GetAsync(srv.Url + "toinner/manifest.json"); return $"found {found}, {data?.Length} bytes: '{Encoding.ASCII.GetString(data ?? Array.Empty<byte>())}', inner server saw {inner.Paths.Count} request(s)"; });
        await Case("redirect loop", async () => { var (found, data, _) = await transport.GetAsync(srv.Url + "loop/manifest.json"); return $"found {found}"; });
        await Case("file:// as a source (IsUrl is false: read as a folder path)", async () => { var (found, data, _) = await transport.GetAsync(DataTransport.Locate("file:///C:/Windows/", "win.ini")); return $"found {found}, {data?.Length} bytes"; });
        await Case("ftp:// as a source for a whole file", async () => { var r = await transport.FileAsync("ftp://127.0.0.1/x/", "a.bin", Path.Combine(work, "a.bin"), 10, new string('0', 64), 1); return $"ok {r.Ok}, notThere {r.NotThere}, error {r.Error}"; });
        await Case("range answered with other bytes of a 206 (start 0 asked 5)", async () => { var d = await transport.RangeAsync(srv.Url + "wrongrange/", "p.zip", 5, 10); return "got " + Encoding.ASCII.GetString(d) + " (the caller checks the hash)"; });
        // the release's manifest from a mirror: no size limit on the body
        {
            var store = Path.Combine(work, "store");
            var fine = new FineElevation(store, TileList(root));
            var log = new List<string>();
            using var dl = new DemDownloader(fine, null, 1, srv.Url + "endless/") { Log = s => { lock (log) log.Add(s); }, Retries = 1 };
            var proc = Process.GetCurrentProcess();
            long peak = 0; bool run = true;
            var watch = Task.Run(async () => { while (run) { proc.Refresh(); peak = Math.Max(peak, proc.WorkingSet64); await Task.Delay(200); } });
            var sw = Stopwatch.StartNew();
            var t = dl.FetchAsync(56, 24);
            bool done = t.Wait(120000);
            run = false; await watch;
            Console.WriteLine($"http [mirror's manifest.json is an endless body]: fetch {(done ? "ended " + t.Result : "still running after 120 s")} after {sw.ElapsedMilliseconds} ms; server sent {Interlocked.Read(ref sent) / 1e6:F0} MB; process working set peaked at {peak / 1e6:F0} MB");
            lock (log) foreach (string l in log.Take(3)) Console.WriteLine("      log: " + (l.Length > 200 ? l.Substring(0, 200) : l));
        }
    }

    static void BadSource(string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        DataTransport.TurnWait = TimeSpan.FromSeconds(3);
        foreach (string source in new[] { "http://exa mple.invalid/", "http://", "https://[::1", "ftp://127.0.0.1/data/", "C:\\no\\such\\folder", "\\\\?\\bad<>|path", "relative/folder", "http://127.0.0.1:1/", "   ", "con:" })
        {
            string r;
            var sw = Stopwatch.StartNew();
            try { var got = GroundDownloader.Get(Path.Combine(work, "g"), source, null, default, new[] { new GroundDownloader.Set("x.egts", 10, new string('0', 64)) }); r = got.ToString(); }
            catch (Exception e) { r = "THROWS " + e.GetType().Name + ": " + e.Message; }
            Console.WriteLine($"GroundDownloader.Get(source \"{source}\"): {r}  ({sw.ElapsedMilliseconds} ms)");
            sw.Restart();
            try { bool ok = EarthDataStore.Ensure(Path.Combine(work, "e"), null, source, null, out string why, default, new[] { new EarthDataStore.File("x.bin", 10, new string('0', 64)) }); r = ok + " (" + why + ")"; }
            catch (Exception e) { r = "THROWS " + e.GetType().Name + ": " + e.Message; }
            Console.WriteLine($"EarthDataStore.Ensure(source \"{source}\"): {r}  ({sw.ElapsedMilliseconds} ms)");
        }
        // a store folder that can't be written (a file stands where the folder should be)
        File.WriteAllText(Path.Combine(work, "blocked"), "x");
        try { bool ok = EarthDataStore.Ensure(Path.Combine(work, "blocked"), null, work, null, out string why, default, new[] { new EarthDataStore.File("x.bin", 10, new string('0', 64)) }); Console.WriteLine("Ensure into an unwritable store: " + ok + " (" + why + ")"); }
        catch (Exception e) { Console.WriteLine("Ensure into an unwritable store THROWS " + e.GetType().Name + ": " + e.Message); }
        try { var got = GroundDownloader.Get(Path.Combine(work, "blocked"), work, null, default, new[] { new GroundDownloader.Set("x.egts", 10, new string('0', 64)) }); Console.WriteLine("GroundDownloader.Get into an unwritable folder: " + got); }
        catch (Exception e) { Console.WriteLine("GroundDownloader.Get into an unwritable folder THROWS " + e.GetType().Name + ": " + e.Message); }
        // a cancelled token (the server stopping)
        var cts = new CancellationTokenSource(); cts.Cancel();
        try { bool ok = EarthDataStore.Ensure(Path.Combine(work, "e2"), null, work, null, out string why, cts.Token, new[] { new EarthDataStore.File("x.bin", 10, new string('0', 64)) }); Console.WriteLine("Ensure with a cancelled token: " + ok); }
        catch (Exception e) { Console.WriteLine("Ensure with a cancelled token THROWS " + e.GetType().Name); }
    }

    /// <summary>"Download all" into a store that can't be written (as a full disk: every write throws IOException).</summary>
    static void DiskFull(string root, string work)
    {
        if (Directory.Exists(work)) Directory.Delete(work, true);
        string repo = Path.Combine(work, "repo"), cop = Path.Combine(work, "copernicus");
        Directory.CreateDirectory(repo); Directory.CreateDirectory(cop);
        // a release of 40 squares in one part
        var list = new byte[360 * 180 / 8];
        var squares = new List<(int, int)>();
        for (int i = 0; i < 40; i++) squares.Add((10 + i / 8, 20 + i % 8));
        foreach (var (la, lo) in squares) { int ix = DemTiles.SquareIndex(la, lo); list[ix >> 3] |= (byte)(0x80 >> (ix & 7)); }
        using (var part = new FileStream(Path.Combine(repo, "part1.zip"), FileMode.Create))
        {
            var sb = new StringBuilder();
            byte[] tile = null;
            foreach (var (la, lo) in squares)
            {
                tile = DemTiles.Encode(Synthetic(la, lo));
                if (sb.Length > 0) sb.Append(',');
                sb.Append($"\"{DemTiles.ShortName(la, lo)}\":[1,{part.Position},{tile.Length},\"{Convert.ToHexString(SHA256.HashData(tile)).ToLowerInvariant()}\"]");
                part.Write(tile);
            }
            File.WriteAllText(Path.Combine(repo, "manifest.json"), "{\"format\":\"egl\",\"formatVersion\":1,\"parts\":[{\"name\":\"part1.zip\",\"bytes\":" + part.Position + "}],\"tiles\":{" + sb + "}}");
        }
        long partBytes = new FileInfo(Path.Combine(repo, "part1.zip")).Length;
        // the store: a path under a FILE, so creating the folder and every write throws IOException (what a full disk does)
        // (a directory stands where each tile file should go: the write's rename throws, as a write on a full disk does; the
        // manifest's copy can still be kept, as when the disk filled after the download began)
        string store = Path.Combine(work, "glo90");
        var fine = new FineElevation(store, list);
        foreach (var (la, lo) in squares) Directory.CreateDirectory(fine.PathOf(la, lo));
        var log = new List<string>();
        using var dl = new DemDownloader(fine, cop, 3, repo) { Log = s => { lock (log) log.Add(s); } };
        var sw = Stopwatch.StartNew();
        dl.StartAll(3, DemDownloader.Origin.Repo);
        while (dl.All.Running && sw.Elapsed.TotalSeconds < 600) Thread.Sleep(200);
        var b = dl.All;
        Console.WriteLine($"download all of {squares.Count} squares ({partBytes / 1e6:F1} MB in the release) into a store where every write fails:");
        Console.WriteLine($"  ended {(b.Running ? "NO (still running after 600 s)" : "yes")} after {sw.Elapsed.TotalSeconds:F0} s; done {b.Done}, failed {b.Failed}, error '{b.Error}'");
        Console.WriteLine($"  bytes read from the sources: {dl.BytesDownloaded / 1e6:F1} MB = {dl.BytesDownloaded / (double)partBytes:F1} times the release; log lines: {log.Count}");
        lock (log)
        {
            foreach (var g in log.GroupBy(l => System.Text.RegularExpressions.Regex.Replace(l, @"[NS]\d\d[EW]\d\d\d|\d+", "#")).OrderByDescending(g => g.Count()).Take(6))
                Console.WriteLine($"      x{g.Count()}: {(g.First().Length > 190 ? g.First().Substring(0, 190) + "..." : g.First())}");
        }
    }

    // ------------------------------------------------------------------------------------------ command costs

    static void WeatherCmd(string root)
    {
        var data = EarthData.LoadFromFolder(root, false);
        const int W = 1024000; int L = W * 2 / 5;
        var inp = new LayoutInput { WorldWidth = W, CwRingStart = W / 10, CwRingLength = W * 4 / 5, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwBandInner = 608, Gutter = false };
        var geo = EarthLayout.Build(inp, data, out _);
        foreach (var (days, label) in new[] { (9 * 12, "9-day months (the game's default)"), (30 * 12, "30-day months") })
        {
            var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = days, HoursPerDay = 24 }, 12345);
            Console.WriteLine($"weather rules {m.Settings.Rules}, {label}:");
            foreach (var (name, la, lo) in new[] { ("Riga", 56.95, 24.1), ("Kinshasa", -4.3, 15.3), ("Kansas", 38.5, -98.0), ("Sahara", 23.0, 10.0) })
            {
                geo.FromGeo(la, lo, out double x, out double z);
                double hours = 5000.3, sink = 0;
                // warm up
                for (int k = 0; k < 200; k++) sink += m.HailAt(x, z, hours + k, 50) + m.ThunderAt(x, z, hours + k) + m.FogAt(x, z, hours + k, 50).Ground + m.Sample(x, z, hours + k).Cloudness;
                var sw = Stopwatch.StartNew();
                // /ew hail: a year of quarter-hours until 6 found, then 624 ring points
                int year = (int)Math.Ceiling(days * 24.0 * 4), found = 0, calls = 0; bool was = false;
                for (int k = 1; k <= year && found < 6; k++) { double h = m.HailAt(x, z, hours + k / 4.0, 50); calls++; bool on = h >= 0.3; if (on && !was) found++; was = on; }
                for (int ring = 1; ring <= 12; ring++) for (int k = 0; k < ring * 8; k++) { double a = k * 2 * Math.PI / (ring * 8); sink += m.HailAt(x + ring * 250 * Math.Cos(a), z + ring * 250 * Math.Sin(a), hours, 50); calls++; }
                double hailMs = sw.Elapsed.TotalMilliseconds; int hailCalls = calls;
                sw.Restart(); calls = 0; found = 0; was = false;
                for (int k = 1; k <= 30 * 24 && found < 8; k++) { var f = m.FogAt(x, z, hours + k, 50); calls++; bool on = f.Ground >= 0.5; if (on && !was) found++; was = on; }
                double fogMs = sw.Elapsed.TotalMilliseconds; int fogCalls = calls;
                sw.Restart(); calls = 0; double next = -1;
                for (int ring = 1; ring <= 12; ring++) for (int k = 0; k < ring * 8; k++) { double a = k * 2 * Math.PI / (ring * 8); sink += m.ThunderAt(x + ring * 250 * Math.Cos(a), z + ring * 250 * Math.Sin(a), hours); calls++; }
                for (int k = 0; k <= 4 * 24 * 4 && next < 0; k++) { calls++; if (m.ThunderAt(x, z, hours + k / 4.0) > 0) next = k / 4.0; }
                double thMs = sw.Elapsed.TotalMilliseconds; int thCalls = calls;
                sw.Restart(); calls = 0;
                for (int r = -8; r <= 8; r++) for (int c = -12; c <= 12; c++) { sink += m.Sample(x + c * 128, z + r * 128, hours).Cloudness; calls++; }
                double mapMs = sw.Elapsed.TotalMilliseconds;
                Console.WriteLine($"  {name,-9} /ew hail {hailMs,8:F1} ms ({hailCalls} HailAt) | /ew fog {fogMs,7:F1} ms ({fogCalls} FogAt) | /ew thunder {thMs,7:F1} ms ({thCalls} ThunderAt) | /ew map {mapMs,6:F1} ms ({calls} Sample)");
            }
        }
    }

    // ------------------------------------------------------------------------------------------ start-up cost

    static void Load(string root)
    {
        var proc = Process.GetCurrentProcess();
        void Line(string what, Stopwatch sw)
        {
            long before = GC.GetTotalMemory(false);
            GC.Collect(2, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); GC.Collect();
            var info = GC.GetGCMemoryInfo();
            proc.Refresh();
            Console.WriteLine($"{what,-58} {sw.ElapsedMilliseconds,6} ms | live managed {GC.GetTotalMemory(false) / 1e6,6:F0} MB (large objects {info.GenerationInfo[3].SizeAfterBytes / 1e6,5:F0} MB) | allocated so far {GC.GetTotalAllocatedBytes(false) / 1e6,6:F0} MB | peak working set {proc.PeakWorkingSet64 / 1e6,6:F0} MB");
        }
        var sw = Stopwatch.StartNew();
        var client = EarthData.LoadFromFolder(root, false);
        Line("client: EarthData.Load (climate, fixes, weather)", sw);
        sw.Restart();
        client.SetClimateFix(EarthData.ClimateFixVersion); client.SetFineTemperature(true); client.SetFineRain(true); client.SetPenmanAridity(true);
        var wg = client.WeatherGround;
        double sink = 0;
        for (double la = -85; la <= 85; la += 1.7) for (double lo = -179; lo <= 179; lo += 2.3) sink += client.SampleClimate(la, lo).Rain;
        Line("client: + fix 4, CHELSA temp/rain/aridity, world sweep", sw);
        GC.KeepAlive(client); GC.KeepAlive(wg);
        sw.Restart();
        var data = EarthData.LoadFromFolder(root, true);
        Line("server: EarthData.Load with terrain (tracked earthdata)", sw);
        sw.Restart();
        data.SetClimateFix(EarthData.ClimateFixVersion); data.SetFineTemperature(true); data.SetFineRain(true); data.SetPenmanAridity(true);
        data.SetSummits(true); data.SetGlaciers(true); data.SetSeaReferee(true); data.SetIslandOverlay(true); data.SetLakeOverlay(true); data.SetShoreOverlay(true); data.SetOutletOverlay(true);
        Line("server: + the world's choices (overlays, referee)", sw);
        sw.Restart();
        var peaks = data.ReadPeaks();
        Line("server: + peaks.json (" + (peaks?.Count ?? 0) + " peaks)", sw);
        sw.Restart();
        var reaches = data.RiverReaches();
        Line("server: rivers.bin reaches held (" + (reaches?.Count ?? 0) + ")", sw);
        sw.Restart();
        // the store's files hashed, as every server start does (here: the tracked files that are in the list)
        var have = EarthDataStore.Files.Where(f => File.Exists(Path.Combine(root, "earthdata", f.Name))).ToArray();
        string fake = Path.Combine(Path.GetTempPath(), "probe-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fake, "data"));
        // (hash them where they are: EarthDataStore.Missing takes the folder above "data")
        long bytes = 0; var hsw = Stopwatch.StartNew();
        Parallel.ForEach(have, f => { DataTransport.FileMatches(Path.Combine(root, "earthdata", f.Name), f.Bytes, f.Sha256); Interlocked.Add(ref bytes, f.Bytes); });
        Console.WriteLine($"SHA-256 of {have.Length} store files here ({bytes / 1e6:F0} MB of the store's {EarthDataStore.TotalBytes() / 1e6:F0} MB), in parallel: {hsw.ElapsedMilliseconds} ms");
        Directory.Delete(fake, true);
        GC.KeepAlive(data); GC.KeepAlive(reaches); GC.KeepAlive(peaks);
        Console.WriteLine($"store list: {EarthDataStore.Files.Length} files, {EarthDataStore.TotalBytes() / 1e6:F1} MB; ground sets {GroundDownloader.TotalBytes() / 1e6:F1} MB; GLO-90 release {DemDownloader.Glo90RepoBytes / 1e9:F2} GB");
    }
}
