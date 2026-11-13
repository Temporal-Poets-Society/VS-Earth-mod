using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Offline checks of the per-chunk column store (src/Core/ColumnStore.cs, audit B03): a stored column is the model's
    /// own, field for field; nothing is stored before Attach or off the block grid; lookups beside a chunk never fill it;
    /// a second world never reads the first's columns; the least recently used chunks go past the capacity; many threads
    /// filling and reading at once get the model's columns; a GLO-90 world's sweep threads and threads that may not fetch
    /// tiles never fill it.
    ///   dotnet run -c Release -- [--data &lt;earthdata folder&gt;]
    /// </summary>
    static class Program
    {
        static int failures, checks;
        static readonly FieldInfo[] fields = typeof(TerrainModel.Column).GetFields();

        static void Check(bool ok, string what)
        {
            checks++;
            if (ok) return;
            failures++;
            Console.WriteLine("  FAIL " + what);
        }

        static bool Bits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        /// <summary>Whether two columns are equal in every field (doubles and floats bit for bit).</summary>
        static bool Same(TerrainModel.Column a, TerrainModel.Column b, out string field)
        {
            object x = a, y = b;
            foreach (var f in fields)
            {
                object u = f.GetValue(x), v = f.GetValue(y);
                bool equal = u is double d ? Bits(d, (double)v) : u is float s ? BitConverter.SingleToInt32Bits(s) == BitConverter.SingleToInt32Bits((float)v) : Equals(u, v);
                if (!equal) { field = f.Name + " " + u + "/" + v; return false; }
            }
            field = null;
            return true;
        }

        static int Main(string[] args)
        {
            int at = Array.IndexOf(args, "--data");
            string dataPath = Path.GetFullPath(at >= 0 && at + 1 < args.Length ? args[at + 1] : FindEarthData());
            var clock = Stopwatch.StartNew();
            var data = EarthData.LoadFromFolder(dataPath, true);
            var geo = EarthLayout.Build(new LayoutInput { WorldWidth = 256000, CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = 51200, CwPoleDistance = 51152, CwBandInner = 608, GutterMargin = 512 }, data, out _);
            var vertical = VerticalScale.ForWorld(384, 165);
            Console.WriteLine("Earth data {0} loaded in {1:0.0} s", dataPath, clock.Elapsed.TotalSeconds);
            Console.WriteLine("column {0} bytes, chunk {1:n0} bytes, {2} chunks {3:0.0} MB", Unsafe.SizeOf<TerrainModel.Column>(), ColumnStore.ChunkBytes,
                ColumnStore.DefaultCapacity, ColumnStore.DefaultCapacity * ColumnStore.ChunkBytes / 1e6);

            // chunks over a delta, the Alps, the Nile, the Amazon, the Greenland ice and a fjord coast
            var chunks = new List<(int X, int Z)>();
            foreach (var (lat, lon) in new[] { (52.37, 4.9), (45.83, 6.86), (29.8, 31.2), (-3.1, -60.0), (72.5, -38.5), (61.1, 6.5) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                chunks.Add(((int)Math.Floor(x) >> 5, (int)Math.Floor(z) >> 5));
            }

            Unsealed(new TerrainModel(data, geo, vertical, 290142474));
            Before(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks[0]);
            Stored(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks);
            NearAndOffGrid(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks[1]);
            SecondWorld(Sealed(new TerrainModel(data, geo, vertical, 290142474)), Sealed(new TerrainModel(data, geo, vertical, 12345)), chunks[1]);
            Eviction(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks[2]);
            Threads(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks[0]);
            Glo90Threads(Sealed(new TerrainModel(data, geo, vertical, 290142474)), chunks[1]);
            MissedReads();

            Console.WriteLine("{0} checks, {1}; {2:0.0} s", checks, failures == 0 ? "all passed" : failures + " failed", clock.Elapsed.TotalSeconds);
            return failures == 0 ? 0 : 1;
        }

        static string FindEarthData()
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "earthdata", "terrain.bin"))) return Path.Combine(dir.FullName, "earthdata");
            throw new FileNotFoundException("No earthdata/terrain.bin above the working directory; pass --data <earthdata folder>");
        }

        /// <summary>The model's own columns of a chunk, row-major.</summary>
        static TerrainModel.Column[] Fresh(TerrainModel m, (int X, int Z) chunk)
        {
            var a = new TerrainModel.Column[1024];
            for (int lz = 0; lz < 32; lz++)
                for (int lx = 0; lx < 32; lx++)
                    a[lz * 32 + lx] = m.GetColumn(chunk.X * 32 + lx, chunk.Z * 32 + lz);
            return a;
        }

        static bool SameChunk(TerrainModel.Column[] expected, Func<int, int, TerrainModel.Column> get, (int X, int Z) chunk, out string first)
        {
            for (int lz = 0; lz < 32; lz++)
                for (int lx = 0; lx < 32; lx++)
                    if (!Same(expected[lz * 32 + lx], get(chunk.X * 32 + lx, chunk.Z * 32 + lz), out string f))
                    {
                        first = $"{chunk.X * 32 + lx},{chunk.Z * 32 + lz}: {f}";
                        return false;
                    }
            first = null;
            return true;
        }

        /// <summary>FineElevation.MissedReads: a read that takes the built-in heights where generation would read a tile (one not
        /// here that this thread may not fetch, or a sweep thread's) counts; open sea and a square kept on built-in heights
        /// for good don't.</summary>
        static void MissedReads()
        {
            Console.WriteLine("missed GLO-90 reads");
            string folder = Path.Combine(Path.GetTempPath(), "earth-missedreads-" + Guid.NewGuid().ToString("N"));
            var list = new byte[360 * 180 / 8];
            foreach (var (latS, lonW) in new[] { (45, 6), (46, 6) })
            {
                int i = DemTiles.SquareIndex(latS, lonW);
                list[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
            var fine = new FineElevation(folder, list);
            fine.MarkFallback(46, 6);
            void On(Action body) { var t = new Thread(() => body()); t.Start(); t.Join(); }
            On(() =>
            {
                FineElevation.FetchAllowed = false;
                int m = FineElevation.MissedReads;
                Check(!fine.TryHeight(45.5, 6.5, out _) && FineElevation.MissedReads == m + 1, "a tile still to come counts");
                Check(fine.TryHeight(45.5, -30.5, out _) && FineElevation.MissedReads == m + 1, "open sea does not count");
                Check(!fine.TryHeight(46.5, 6.5, out _) && FineElevation.MissedReads == m + 1, "a square kept on built-in heights for good does not count");
                FineElevation.BuiltInOnly = true;
                Check(!fine.TryHeight(45.5, -30.5, out _) && FineElevation.MissedReads == m + 2, "a sweep thread's read counts");
            });
        }

        /// <summary>The companions' plan sealed, as EarthWorldGen does before it attaches the store.</summary>
        static TerrainModel Sealed(TerrainModel m)
        {
            m.SealTerrainReservations();
            return m;
        }

        /// <summary>The store refuses a model whose plan is not sealed (its columns could still change), and takes it once sealed.</summary>
        static void Unsealed(TerrainModel m)
        {
            Console.WriteLine("unsealed plan");
            Check(!m.PlanSealed, "a new model's plan is open");
            bool refused = false;
            try { ColumnStore.Attach(m); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && ColumnStore.Of(m) == null, "Attach refuses an unsealed model");
            m.SealTerrainReservations();
            Check(m.PlanSealed && ColumnStore.Attach(m) == ColumnStore.Of(m), "Attach takes it once sealed");
            ColumnStore.Detach(m);
        }

        /// <summary>Before Attach there is no store: lookups are the model's.</summary>
        static void Before(TerrainModel m, (int X, int Z) chunk)
        {
            Console.WriteLine("before attach");
            Check(ColumnStore.Of(m) == null, "no store before Attach");
            var fresh = Fresh(m, chunk);
            Check(SameChunk(fresh, (x, z) => ColumnStore.At(m, x, z), chunk, out string f), "At without a store is the model's column " + f);
            Check(ColumnStore.Of(m) == null, "At without a store attaches nothing");
            var s = ColumnStore.Attach(m);
            Check(ColumnStore.Of(m) == s && s.Model == m, "Attach gives the model's store");
            ColumnStore.Detach(m);
            Check(ColumnStore.Of(m) == null, "Detach removes it");
        }

        /// <summary>Every column read from the store equals the model's, filled by At, read again by At and Near.</summary>
        static void Stored(TerrainModel m, List<(int X, int Z)> chunks)
        {
            Console.WriteLine("stored = fresh");
            var s = ColumnStore.Attach(m);
            foreach (var chunk in chunks)
            {
                var fresh = Fresh(m, chunk);
                Check(!s.Holds(chunk.X * 32, chunk.Z * 32), $"chunk {chunk} not held before");
                Check(SameChunk(fresh, (x, z) => ColumnStore.At(m, x, z), chunk, out string f), $"chunk {chunk}: At = model {f}");
                Check(s.Holds(chunk.X * 32 + 31, chunk.Z * 32 + 31), $"chunk {chunk} held after At");
                Check(SameChunk(fresh, (x, z) => ColumnStore.Near(m, x, z), chunk, out f), $"chunk {chunk}: Near = model {f}");
                // a caller's copy is its own: changing it leaves the store's column as it was
                var copy = s.At(chunk.X * 32 + 5, chunk.Z * 32 + 7);
                copy.Top += 1000; copy.River = !copy.River;
                Check(Same(fresh[7 * 32 + 5], s.At(chunk.X * 32 + 5, chunk.Z * 32 + 7), out f), "a changed copy leaves the store's column " + f);
            }
            var c = s.Counts;
            Check(c.Filled == chunks.Count && c.Direct == 0 && c.Held == chunks.Count, $"one fill per chunk, nothing built one by one ({c})");
            Console.WriteLine("  " + s.Report());
            ColumnStore.Detach(m);
        }

        /// <summary>Near never fills; off the block grid (half blocks, -0.0) the model answers and nothing is stored.</summary>
        static void NearAndOffGrid(TerrainModel m, (int X, int Z) chunk)
        {
            Console.WriteLine("near and off the grid");
            var s = ColumnStore.Attach(m);
            int x = chunk.X * 32 + 3, z = chunk.Z * 32 + 9;
            Check(Same(m.GetColumn(x, z), ColumnStore.Near(m, x, z), out string f), "Near on a missing chunk is the model's " + f);
            Check(!s.Holds(x, z) && s.Counts.Direct == 1 && s.Counts.Filled == 0, "Near on a missing chunk stores nothing");
            Check(Same(m.GetColumn(x + 0.5, z + 0.5), ColumnStore.At(m, x + 0.5, z + 0.5), out f), "a half-block lookup is the model's " + f);
            Check(!s.Holds(x, z), "a half-block lookup stores nothing");
            ColumnStore.At(m, x, z);
            Check(s.Holds(x, z), "At fills");
            long served = s.Counts.Served;
            Check(Same(m.GetColumn(x + 0.25, z), ColumnStore.Near(m, x + 0.25, z), out f) && s.Counts.Served == served, "an off-grid lookup beside a held column is the model's " + f);
            Check(Same(m.GetColumn(-0.0, z), ColumnStore.Near(m, -0.0, z), out f) && s.Counts.Served == served, "-0.0 is not a block position " + f);
            ColumnStore.At(m, double.NaN, z);
            Check(Same(m.GetColumn(1e12, z), ColumnStore.At(m, 1e12, z), out f), "a position beyond int goes to the model " + f);
            Check(s.Counts.Served == served && s.Counts.Filled == 1, "NaN and positions beyond int never reach the store");
            ColumnStore.Detach(m);
        }

        /// <summary>Two models (another seed: other columns) with their own stores; one thread switching between them.</summary>
        static void SecondWorld(TerrainModel a, TerrainModel b, (int X, int Z) chunk)
        {
            Console.WriteLine("second world");
            var fa = Fresh(a, chunk);
            var fb = Fresh(b, chunk);
            int differ = 0;
            for (int i = 0; i < 1024; i++) if (!Same(fa[i], fb[i], out _)) differ++;
            Check(differ > 0, "the two worlds' columns differ here (" + differ + ")");
            ColumnStore.Attach(a);
            Check(ColumnStore.Of(b) == null, "the second world has no store yet");
            Check(SameChunk(fb, (x, z) => ColumnStore.At(b, x, z), chunk, out string f), "the second world without a store reads its own model " + f);
            ColumnStore.Attach(b);
            for (int round = 0; round < 3; round++)
            {
                Check(SameChunk(fa, (x, z) => ColumnStore.At(a, x, z), chunk, out f), "world a reads its own columns " + f);
                Check(SameChunk(fb, (x, z) => ColumnStore.At(b, x, z), chunk, out f), "world b reads its own columns " + f);
                // alternating column by column: the thread's last chunk belongs to one store only
                bool ok = true;
                for (int i = 0; i < 1024 && ok; i++)
                {
                    int x = chunk.X * 32 + (i & 31), z = chunk.Z * 32 + (i >> 5);
                    ok = Same(fa[i], ColumnStore.Near(a, x, z), out f) && Same(fb[i], ColumnStore.Near(b, x, z), out f);
                }
                Check(ok, "alternating worlds column by column " + f);
            }
            // a new store for the same model (a second start) starts empty and reads right
            var again = ColumnStore.Attach(a);
            Check(!again.Holds(chunk.X * 32, chunk.Z * 32), "a new store starts empty");
            Check(SameChunk(fa, (x, z) => ColumnStore.At(a, x, z), chunk, out f), "a new store for the same world " + f);
            ColumnStore.Detach(a); ColumnStore.Detach(b);
        }

        /// <summary>Past the capacity the least recently used chunk goes; a chunk read again stays.</summary>
        static void Eviction(TerrainModel m, (int X, int Z) chunk)
        {
            Console.WriteLine("eviction");
            var s = ColumnStore.Attach(m, 3);
            (int X, int Z) C(int i) => (chunk.X + i, chunk.Z);
            for (int i = 0; i < 3; i++) s.At(C(i).X * 32, C(i).Z * 32);
            Check(s.Counts.Held == 3 && s.Counts.Evicted == 0, "three held");
            s.At(C(0).X * 32 + 1, C(0).Z * 32);   // chunk 0 used again: chunk 1 is now the oldest
            s.At(C(3).X * 32, C(3).Z * 32);
            Check(s.Counts.Held == 3 && s.Counts.Evicted == 1, "one dropped past the capacity");
            Check(s.Holds(C(0).X * 32, C(0).Z * 32) &&!s.Holds(C(1).X * 32, C(1).Z * 32) && s.Holds(C(2).X * 32, C(2).Z * 32) && s.Holds(C(3).X * 32, C(3).Z * 32),
                "the least recently used chunk went");
            var fresh = Fresh(m, C(1));
            Check(SameChunk(fresh, (x, z) => s.At(x, z), C(1), out string f), "a dropped chunk is built again the same " + f);
            Check(s.Counts.Peak == 3 && s.Counts.Held == 3, "never more than the capacity held (peak " + s.Counts.Peak + ")");
            ColumnStore.Detach(m);
        }

        /// <summary>Eight threads read and fill 16 chunks at random through a store of 6: every column the model's.</summary>
        static void Threads(TerrainModel m, (int X, int Z) chunk)
        {
            Console.WriteLine("threads");
            var keys = new List<(int X, int Z)>();
            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) keys.Add((chunk.X - 1 + i, chunk.Z - 1 + j));
            var fresh = new Dictionary<(int, int), TerrainModel.Column[]>();
            foreach (var k in keys) fresh[k] = Fresh(m, k);
            var s = ColumnStore.Attach(m, 6);
            int wrong = 0;
            long lookups = 0;
            string firstWrong = null;
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
            {
                var rnd = new Random(1000 + t);
                for (int n = 0; n < 4000; n++)
                {
                    var k = keys[rnd.Next(keys.Count)];
                    // a run of lookups in one chunk, as the passes do, then another chunk
                    for (int r = rnd.Next(1, 64); r > 0; r--)
                    {
                        int lx = rnd.Next(32), lz = rnd.Next(32);
                        var c = rnd.Next(3) == 0 ? ColumnStore.Near(m, k.X * 32 + lx, k.Z * 32 + lz) : ColumnStore.At(m, k.X * 32 + lx, k.Z * 32 + lz);
                        Interlocked.Increment(ref lookups);
                        if (!Same(fresh[k][lz * 32 + lx], c, out string f) && Interlocked.Increment(ref wrong) == 1) firstWrong = $"{k} {lx},{lz}: {f}";
                    }
                }
            });
            var counts = s.Counts;
            Check(wrong == 0, $"{wrong} wrong columns of {lookups} ({firstWrong})");
            Check(counts.Served + counts.Direct == lookups, $"every lookup counted ({counts.Served} + {counts.Direct} of {lookups})");
            Check(counts.Held <= 6 && counts.Evicted >= counts.Filled - 6, $"at most the capacity held after the threads ({counts})");
            Console.WriteLine("  " + s.Report());
            ColumnStore.Detach(m);
        }

        /// <summary>A GLO-90 world (a tile list with no squares: no tile is ever read): a thread that may not fetch tiles reads
        /// but never fills; a thread that may fills; a sweep thread (built-in heights only) never reads the store.</summary>
        static void Glo90Threads(TerrainModel m, (int X, int Z) chunk)
        {
            Console.WriteLine("GLO-90 thread rules");
            string folder = Path.Combine(Path.GetTempPath(), "earth-columnstore-" + Guid.NewGuid().ToString("N"));
            m.Fine = new FineElevation(folder, new byte[360 * 180 / 8]);
            m.FineRules = 5;
            var s = ColumnStore.Attach(m);
            int x = chunk.X * 32 + 4, z = chunk.Z * 32 + 6;
            var fresh = m.GetColumn(x, z);
            void On(Action body) { var t = new Thread(() => body()); t.Start(); t.Join(); }
            On(() =>
            {
                FineElevation.FetchAllowed = false;
                Check(Same(fresh, ColumnStore.At(m, x, z), out string f), "no-fetch thread: the model's column " + f);
                Check(!s.Holds(x, z) && s.Counts.Filled == 0, "no-fetch thread: nothing filled");
            });
            On(() =>
            {
                FineElevation.FetchAllowed = true;
                Check(Same(fresh, ColumnStore.At(m, x, z), out string f), "fetching thread: the model's column " + f);
                Check(s.Holds(x, z) && s.Counts.Filled == 1, "fetching thread: the chunk filled");
            });
            On(() =>
            {
                FineElevation.FetchAllowed = false;
                long served = s.Counts.Served;
                Check(Same(fresh, ColumnStore.Near(m, x, z), out string f) && s.Counts.Served == served + 1, "no-fetch thread reads a held chunk " + f);
            });
            On(() =>
            {
                FineElevation.BuiltInOnly = true;
                FineElevation.FetchAllowed = true;
                var c = s.Counts;
                var own = m.GetColumn(x, z);
                Check(Same(own, ColumnStore.At(m, x, z), out string f), "sweep thread: its own built-in column " + f);
                ColumnStore.At(m, x + 32, z);
                var after = s.Counts;
                Check(after.Served == c.Served && after.Direct == c.Direct && after.Filled == c.Filled, "sweep thread: the store neither read nor filled");
            });
            ColumnStore.Detach(m);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
