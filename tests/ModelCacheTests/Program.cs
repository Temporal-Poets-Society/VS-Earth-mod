using EarthMap.Core;

// 2.0.0-rc.4's start-up speed-ups: the parallel cell index (CellIndex) must give the arrays a serial build gives, with
// any number of workers; the model cache (ModelCache) must give back exactly what was kept, and only for the same inputs.
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

    static int Main()
    {
        CellIndexMatchesSerial();
        ParallelWorkCoversEveryItemOnce();
        CacheRoundTrip();
        Console.WriteLine(failures == 0 ? $"ModelCacheTests: {checks} checks passed" : $"ModelCacheTests: {failures} of {checks} checks failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Random rectangles (runs of the same cell, overlaps, negative cells, wide items) against a serial build.</summary>
    static void CellIndexMatchesSerial()
    {
        var rng = new Random(386217);
        foreach (int n in new[] { 0, 1, 7, 5000, 60000 })
        {
            var rects = new (int X0, int X1, int Z0, int Z1)[n];
            int x = 0, z = 0;
            for (int i = 0; i < n; i++)
            {
                // a walk: most items stay in the cell of the one before, some jump, some are wide
                if (rng.NextDouble() < 0.2) { x += rng.Next(-3, 4); z += rng.Next(-3, 4); }
                if (rng.NextDouble() < 0.01) { x = rng.Next(-100000, 100000); z = rng.Next(-100000, 100000); }
                int wx = rng.NextDouble() < 0.05 ? rng.Next(0, 30) : rng.Next(0, 2), wz = rng.NextDouble() < 0.05 ? rng.Next(0, 30) : rng.Next(0, 2);
                rects[i] = (x - wx, x + wx, z - wz, z + wz);
            }
            var serial = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
                for (int cz = rects[i].Z0; cz <= rects[i].Z1; cz++)
                    for (int cx = rects[i].X0; cx <= rects[i].X1; cx++)
                    {
                        long key = CellIndex.Key(cx, cz);
                        if (!serial.TryGetValue(key, out var l)) serial[key] = l = new List<int>();
                        l.Add(i);
                    }
            foreach (int workers in new[] { 1, 2, 3, 8, 13 })
            {
                var grid = new Dictionary<long, int[]>();
                CellIndex.Build(n, (int i, out int x0, out int x1, out int z0, out int z1) => { var r = rects[i]; x0 = r.X0; x1 = r.X1; z0 = r.Z0; z1 = r.Z1; }, grid, workers);
                bool same = grid.Count == serial.Count && serial.All(kv => grid.TryGetValue(kv.Key, out var a) && a.SequenceEqual(kv.Value));
                Check(same, $"CellIndex with {workers} workers equals the serial build for {n} items");
            }
        }
    }

    /// <summary>ParallelWork: every item exactly once, each worker's state made once and put away once (also when an item
    /// throws), never more workers than asked.</summary>
    static void ParallelWorkCoversEveryItemOnce()
    {
        foreach (int count in new[] { 0, 1, 63, 64, 65, 10007 })
            foreach (int chunk in new[] { 1, 64 })
                foreach (int workers in new[] { 1, 3, 8 })
                {
                    var seen = new int[count];
                    int made = 0, putAway = 0;
                    ParallelWork.For(count, () => { Interlocked.Increment(ref made); return new object(); }, (i, _) => Interlocked.Increment(ref seen[i]),
                        _ => Interlocked.Increment(ref putAway), chunk, workers);
                    Check(seen.All(n => n == 1), $"ParallelWork covers {count} items once (chunk {chunk}, {workers} workers)");
                    Check(made == putAway && made <= workers && (count == 0 ? made == 0 : made >= 1), $"ParallelWork's states: {made} made, {putAway} put away ({count}, {chunk}, {workers})");
                }
        int made2 = 0, putAway2 = 0;
        bool threw = false;
        try
        {
            ParallelWork.For(1000, () => { Interlocked.Increment(ref made2); return 0; }, (i, _) => { if (i == 500) throw new IOException("item 500"); },
                _ => Interlocked.Increment(ref putAway2), 1, 4);
        }
        catch (Exception e) { threw = e is IOException || e is AggregateException a && a.Flatten().InnerExceptions.Any(x => x is IOException); }
        Check(threw && made2 == putAway2, $"an item's exception reaches the caller and every state is put away ({made2}/{putAway2})");
    }

    static void CacheRoundTrip()
    {
        string folder = Path.Combine(Path.GetTempPath(), "earth-modelcache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] inputs = { 1, 2, 3, 4 };
            double[] data = { 0.1, -0.0, double.Epsilon, 1e300, Math.PI };
            int built = 0;
            double[] Build() { built++; return (double[])data.Clone(); }
            void Write(BinaryWriter w, double[] v) { w.Write(v.Length); foreach (double d in v) w.Write(d); }
            double[] Read(BinaryReader r) { var v = new double[r.ReadInt32()]; for (int i = 0; i < v.Length; i++) v[i] = r.ReadDouble(); return v; }
            static bool Bits(double[] a, double[] b) => a.Length == b.Length && a.Zip(b).All(p => BitConverter.DoubleToInt64Bits(p.First) == BitConverter.DoubleToInt64Bits(p.Second));

            // a new world: built and saved
            var c1 = ModelCache.Open(folder, inputs);
            var v1 = ModelCache.Use(c1, "part", Build, Write, Read);
            Check(built == 1 && c1.Built.Count == 1 && c1.Reused.Count == 0 && c1.Problem == null, "first start builds the part");
            Check(c1.Save() == null && File.Exists(ModelCache.PathOf(folder, c1.Key)), "the file is written");
            Check(Bits(v1, data), "the built value is the built value");

            // the next start: read back bit for bit, not built
            var c2 = ModelCache.Open(folder, inputs);
            var v2 = ModelCache.Use(c2, "part", Build, Write, Read);
            Check(built == 1 && c2.Reused.Count == 1 && c2.Built.Count == 0, "second start reuses the part");
            Check(Bits(v2, data), "the reused value equals the built one bit for bit (-0.0, denormals)");
            Check(c2.Save() == null, "saving with nothing built only marks the file used");

            // a part the file doesn't have, or one the reader turns down, is built (and saved with the rest)
            var c3 = ModelCache.Open(folder, inputs);
            ModelCache.Use(c3, "other", Build, Write, Read);
            ModelCache.Use(c3, "part", Build, Write, r => null);
            Check(built == 3 && c3.Built.Count == 2, "a missing part and a turned-down part are built");
            Check(c3.Save() == null, "saved again");
            var c3b = ModelCache.Open(folder, inputs);
            ModelCache.Use(c3b, "other", Build, Write, Read);
            ModelCache.Use(c3b, "part", Build, Write, Read);
            Check(built == 3 && c3b.Reused.Count == 2, "both parts are kept after that");

            // a reader that stops short of the part's end: built again
            var c3c = ModelCache.Open(folder, inputs);
            ModelCache.Use(c3c, "part", Build, Write, r => new double[] { r.ReadInt32() });
            Check(built == 4 && c3c.Built.Count == 1, "a part not read to its end is built");

            // other inputs: another key, nothing reused
            var c4 = ModelCache.Open(folder, new byte[] { 1, 2, 3, 5 });
            Check(c4.Key != c1.Key, "other inputs give another key");
            ModelCache.Use(c4, "part", Build, Write, Read);
            Check(c4.Built.Count == 1 && c4.Problem == null, "other inputs build");
            Check(ModelCache.InputsOf(folder, c1.Key).SequenceEqual(inputs), "a file's inputs can be read back");

            // a damaged file: said, ignored, rebuilt, replaced
            string path = ModelCache.PathOf(folder, c1.Key);
            var bytes = File.ReadAllBytes(path);
            bytes[bytes.Length / 2] ^= 0x40;
            File.WriteAllBytes(path, bytes);
            var c5 = ModelCache.Open(folder, inputs);
            ModelCache.Use(c5, "part", Build, Write, Read);
            Check(c5.Problem != null && c5.Built.Count == 1, "a damaged file is reported and rebuilt: " + c5.Problem);
            Check(c5.Save() == null && ModelCache.Use(ModelCache.Open(folder, inputs), "part", Build, Write, Read) is var v5 && Bits(v5, data), "the replaced file reads back");
            File.WriteAllBytes(path, new byte[10]);
            var c6 = ModelCache.Open(folder, inputs);
            Check(c6.Problem != null, "a truncated file is reported");
            Check(ModelCache.InputsOf(folder, c1.Key) == null, "a truncated file's inputs are not read back");

            // a file whose hash is right but whose header lies (Codex review of rc.4: a negative inputs length made the
            // explanation throw): refused by Open, nothing from InputsOf, no exception
            byte[] liar = new byte[] { 9, 9, 9 };
            string liarKey = ModelCache.KeyOf(liar);
            foreach (int badLength in new[] { -5, int.MaxValue })
            {
                using var body = new MemoryStream();
                using (var w = new BinaryWriter(body, System.Text.Encoding.UTF8, true))
                {
                    w.Write(0x434D4145u); w.Write(ModelCache.FormatVersion); w.Write(liarKey); w.Write(badLength);
                }
                byte[] b = body.ToArray();
                File.WriteAllBytes(ModelCache.PathOf(folder, liarKey), b.Concat(System.Security.Cryptography.SHA256.HashData(b)).ToArray());
                bool threw = false;
                byte[] read = null;
                ModelCache opened = null;
                try { read = ModelCache.InputsOf(folder, liarKey); opened = ModelCache.Open(folder, liar); } catch (Exception) { threw = true; }
                Check(!threw && read == null && opened?.Problem != null, $"a header with inputs length {badLength} is refused without an exception");
            }

            // no cache: always built
            int before = built;
            ModelCache.Use(null, "part", Build, Write, Read);
            Check(built == before + 1, "without a cache the part is built");

            // pruning: only the newest KeepFiles stay, and the one just saved always does
            for (int i = 0; i < ModelCache.KeepFiles + 4; i++)
            {
                var c = ModelCache.Open(folder, BitConverter.GetBytes(1000 + i));
                ModelCache.Use(c, "part", Build, Write, Read);
                Check(c.Save() == null, "save " + i);
                File.SetLastWriteTimeUtc(ModelCache.PathOf(folder, c.Key), DateTime.UtcNow.AddMinutes(i - 100));
            }
            var last = ModelCache.Open(folder, BitConverter.GetBytes(999));
            ModelCache.Use(last, "part", Build, Write, Read);
            Check(last.Save() == null, "last save");
            int files = Directory.GetFiles(folder, "model-*.bin").Length;
            Check(files == ModelCache.KeepFiles, $"the folder keeps {ModelCache.KeepFiles} files ({files})");
            Check(File.Exists(ModelCache.PathOf(folder, last.Key)), "the file just saved is kept");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
