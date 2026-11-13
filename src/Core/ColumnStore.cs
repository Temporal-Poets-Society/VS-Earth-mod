using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The terrain columns of recently generated chunks (audit B03): one <see cref="TerrainModel.Column"/>[1024] per chunk
    /// column, as <see cref="TerrainModel.GetColumn"/> gives them at whole block positions, so the passes after the terrain
    /// pass (geology, ores, trees, plants, the structure and pond guards) and the neighbouring chunks' river margins read a
    /// column instead of building it again. Before, a land column was built at least three times, near rivers up to about
    /// eight. Bounded: past <see cref="Capacity"/> it drops the chunks used longest ago (roughly: a thread's repeated reads
    /// of its last chunk go through a per-thread shortcut that does not refresh the chunk's age).
    ///
    /// Measured (2.0.0-rc.2, 1:10 world, one generation thread): 89 % of lookups answered from the store, half the model's
    /// column builds saved, but a column costs under 1 µs there, so chunk time does not change measurably.
    ///
    /// Why a stored column equals a fresh one: once the companions' plan is sealed (TerrainModel.SealTerrainReservations)
    /// the model's layers and rules no longer change, and a column is a pure function of its position on any thread that
    /// reads GLO-90 with tile fetching allowed. A column is a struct, handed out by value, so no caller can change a stored
    /// one. Two thread conditions make a column differ (TerrainModel.Glo90Decides, FineElevation.TryHeight): a sweep thread
    /// takes the built-in heights (<see cref="FineElevation.BuiltInOnly"/>), and a thread that may not fetch tiles
    /// (<see cref="FineElevation.FetchAllowed"/>: only chunk generation sets it) takes the built-in heights where a tile is
    /// not on this machine yet. So a sweep thread never reads or fills the store, and only a thread that may fetch fills it.
    ///
    /// The store is attached to its model after the seal (<see cref="Attach"/> checks it), so nothing is stored before it, and looked
    /// up by the model, so a second world in the same process (tests, a single-player relaunch) never reads another world's
    /// columns. Columns off the block grid (half-block probes, story placement's circles) always come from the model.
    /// Game-independent: the game side attaches it and asks it.
    /// </summary>
    public sealed class ColumnStore
    {
        /// <summary>Chunks kept: 29 MB (a column is 112 bytes, a chunk 115 kB), more than the chunks a generation front has
        /// between their terrain pass and their last pass.</summary>
        public const int DefaultCapacity = 256;
        const int Side = 32, Area = Side * Side;

        static readonly ConditionalWeakTable<TerrainModel, ColumnStore> stores = new();
        static long lastId;

        readonly TerrainModel model;
        readonly long id = Interlocked.Increment(ref lastId);
        readonly ConcurrentDictionary<long, Entry> entries = new();
        readonly object evictGate = new();
        /// <summary>Entries held, counted here: ConcurrentDictionary.Count takes every lock.</summary>
        int count, peak;
        long clock, evicted, buildTicks;
        readonly StripedCounter served = new(), direct = new(), filled = new(), failed = new();

        /// <summary>One chunk's columns, row-major (z outer), never changed once built.</summary>
        sealed class Entry
        {
            public readonly TerrainModel.Column[] Columns;
            public long Used;
            public Entry(TerrainModel.Column[] columns, long used) { Columns = columns; Used = used; }
        }

        // the chunk this thread read last: most lookups follow the one before in the same chunk (the store's number, not the
        // store, so a thread never keeps an unloaded world alive)
        [ThreadStatic] static long lastStore, lastKey;
        [ThreadStatic] static TerrainModel.Column[] lastColumns;

        /// <summary>The most chunks kept.</summary>
        public int Capacity { get; }
        public TerrainModel Model => model;

        ColumnStore(TerrainModel model, int capacity)
        {
            this.model = model;
            Capacity = Math.Max(1, capacity);
        }

        /// <summary>
        /// A new, empty store for <paramref name="model"/>, replacing any earlier one. Only once the model's companion plan is
        /// sealed (<see cref="TerrainModel.PlanSealed"/>, TerrainModel.SealTerrainReservations), when its columns can no longer
        /// change: an unsealed model is refused (InvalidOperationException), whoever calls.
        /// </summary>
        public static ColumnStore Attach(TerrainModel model, int capacity = DefaultCapacity)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (!model.PlanSealed) throw new InvalidOperationException("The column store needs the model's companion plan sealed first (TerrainModel.SealTerrainReservations): before, a column can still change.");
            var store = new ColumnStore(model, capacity);
            stores.AddOrUpdate(model, store);
            return store;
        }

        /// <summary>Drops the model's store: later lookups build their columns again.</summary>
        public static void Detach(TerrainModel model)
        {
            if (model != null) stores.Remove(model);
        }

        /// <summary>The model's store, null before <see cref="Attach"/>.</summary>
        public static ColumnStore Of(TerrainModel model) => model != null && stores.TryGetValue(model, out var s) ? s : null;

        /// <summary>
        /// The column at (x, z) for a pass that walks its own chunk's columns: from the store, its chunk built whole first
        /// when missing, on a thread that may fill it. Off the block grid, without a store or on a thread that mustn't use
        /// it: the model's column. In GLO-90 worlds only the terrain pass's thread may fetch tiles (FetchAllowed), and the
        /// game runs each pass on its own thread when it has several worldgen threads, so there the later passes' At reads
        /// a stored chunk or builds the one column, like <see cref="Near"/>.
        /// </summary>
        public static TerrainModel.Column At(TerrainModel model, double x, double z)
            => OnGrid(x, z, out int ix, out int iz) && Of(model) is { } s ? s.At(ix, iz) : model.GetColumn(x, z);

        /// <summary>
        /// The column at (x, z) for a lookup beside or away from the asking chunk (river margins, footprints, lattices,
        /// single checks, queries): from the store when its chunk is there, else the model's (and nothing is stored).
        /// </summary>
        public static TerrainModel.Column Near(TerrainModel model, double x, double z)
            => OnGrid(x, z, out int ix, out int iz) && Of(model) is { } s ? s.Near(ix, iz) : model.GetColumn(x, z);

        /// <summary>Whether (x, z) is a whole block position, the very number the int converts back to (not -0.0, NaN or out of range).</summary>
        static bool OnGrid(double x, double z, out int ix, out int iz)
        {
            ix = iz = 0;
            if (!(x >= int.MinValue && x <= int.MaxValue && z >= int.MinValue && z <= int.MaxValue)) return false;
            ix = (int)x; iz = (int)z;
            return BitConverter.DoubleToInt64Bits(ix) == BitConverter.DoubleToInt64Bits(x)
                && BitConverter.DoubleToInt64Bits(iz) == BitConverter.DoubleToInt64Bits(z);
        }

        /// <summary><see cref="At(TerrainModel, double, double)"/> at a block position.</summary>
        public TerrainModel.Column At(int x, int z) => Lookup(x, z, true);

        /// <summary><see cref="Near(TerrainModel, double, double)"/> at a block position.</summary>
        public TerrainModel.Column Near(int x, int z) => Lookup(x, z, false);

        /// <summary>Whether the chunk holding block (x, z) is in the store now (for tests).</summary>
        public bool Holds(int x, int z) => entries.ContainsKey(Key(x >> 5, z >> 5));

        TerrainModel.Column Lookup(int x, int z, bool fill)
        {
            // a sweep thread takes the built-in heights: its columns are not this world's terrain
            if (model.Fine != null && FineElevation.BuiltInOnly) return model.GetColumn(x, z);
            var columns = ColumnsOf(x >> 5, z >> 5, fill);
            if (columns == null)
            {
                direct.Increment();
                return model.GetColumn(x, z);
            }
            served.Increment();
            return columns[(z & (Side - 1)) * Side + (x & (Side - 1))];
        }

        TerrainModel.Column[] ColumnsOf(int chunkX, int chunkZ, bool fill)
        {
            long key = Key(chunkX, chunkZ);
            if (lastStore == id && lastKey == key && lastColumns != null) return lastColumns;
            if (entries.TryGetValue(key, out var e))
            {
                Volatile.Write(ref e.Used, Interlocked.Increment(ref clock));
                return Remember(key, e.Columns);
            }
            // only a thread that may fetch GLO-90 tiles builds the columns the way chunk generation does
            if (!fill || (model.Fine != null && !FineElevation.FetchAllowed)) return null;
            var columns = Build(chunkX, chunkZ);
            if (columns == null) return null;
            e = new Entry(columns, Interlocked.Increment(ref clock));
            var kept = entries.GetOrAdd(key, e);
            if (kept == e)
            {
                filled.Increment();
                if (Interlocked.Increment(ref count) > Capacity) Evict();
                // (with several threads adding at once, briefly a few more than the capacity)
                InterlockedMax(ref peak, Volatile.Read(ref count));
            }
            // (another thread built the same chunk at the same time: equal columns, its entry is kept)
            return Remember(key, kept.Columns);
        }

        TerrainModel.Column[] Remember(long key, TerrainModel.Column[] columns)
        {
            lastStore = id; lastKey = key; lastColumns = columns;
            return columns;
        }

        /// <summary>A chunk's 1024 columns, in the terrain pass's order; null if the model throws for one of them (the
        /// asking caller then builds its own column, and meets the same error there as before).</summary>
        TerrainModel.Column[] Build(int chunkX, int chunkZ)
        {
            var columns = new TerrainModel.Column[Area];
            int x0 = chunkX * Side, z0 = chunkZ * Side;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                for (int lz = 0; lz < Side; lz++)
                    for (int lx = 0; lx < Side; lx++)
                        columns[lz * Side + lx] = model.GetColumn(x0 + lx, z0 + lz);
            }
            catch (Exception)
            {
                failed.Increment();
                return null;
            }
            // what a column costs the model, for the report's estimate of the time the store saves
            Interlocked.Add(ref buildTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            return columns;
        }

        /// <summary>Drops the least recently used chunks down to the capacity (once per chunk built, a scan of a few hundred
        /// entries).</summary>
        void Evict()
        {
            lock (evictGate)
            {
                while (Volatile.Read(ref count) > Capacity)
                {
                    long oldestKey = 0, oldest = long.MaxValue;
                    bool any = false;
                    foreach (var kv in entries)
                    {
                        long used = Volatile.Read(ref kv.Value.Used);
                        if (used < oldest) { oldest = used; oldestKey = kv.Key; any = true; }
                    }
                    if (!any || !entries.TryRemove(oldestKey, out _)) break;
                    Interlocked.Decrement(ref count);
                    evicted++;
                }
            }
        }

        static void InterlockedMax(ref int target, int value)
        {
            int seen = Volatile.Read(ref target);
            while (value > seen)
            {
                int was = Interlocked.CompareExchange(ref target, value, seen);
                if (was == seen) return;
                seen = was;
            }
        }

        static long Key(int chunkX, int chunkZ) => ((long)chunkX << 32) | (uint)chunkZ;

        /// <summary>Bytes of one stored chunk's columns.</summary>
        public static long ChunkBytes => (long)Unsafe.SizeOf<TerrainModel.Column>() * Area;

        /// <summary>Lookups answered from the store, lookups the model answered (no chunk entry), chunks built whole, chunk
        /// builds the model threw in, chunks dropped, chunks held now and at most.</summary>
        public (long Served, long Direct, long Filled, long Failed, long Evicted, int Held, int Peak) Counts
            => (served.Value, direct.Value, filled.Value, failed.Value, Interlocked.Read(ref evicted), Volatile.Read(ref count), Volatile.Read(ref peak));

        /// <summary>Seconds the model took for the chunks built whole.</summary>
        public double BuildSeconds => (double)Interlocked.Read(ref buildTicks) / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>One line for the log: what the store answered, what the model built for it, and about how much time the
        /// columns not built again saved (at the whole-chunk builds' cost per column).</summary>
        public string Report()
        {
            var c = Counts;
            long asked = c.Served + c.Direct, built = c.Direct + c.Filled * Area;
            double perColumn = c.Filled > 0 ? BuildSeconds / (c.Filled * Area) : 0;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:n0} column lookups, {1:0.0}% from the store; the model built {2:n0} columns for them ({3:n0} one by one, {4:n0} chunks whole{5}) " +
                "instead of {0:n0}; {6:n0} chunks dropped, at most {7} of {8} held ({9:0.0} MB); the whole-chunk builds took {10:0.0} s, " +
                "{11:0.00} µs a column, so the columns not built again saved about {12:0.0} s",
                asked, asked > 0 ? 100.0 * c.Served / asked : 0, built, c.Direct, c.Filled,
                c.Failed > 0 ? ", " + c.Failed + " chunk builds failed" : "", c.Evicted, c.Peak, Capacity, c.Peak * ChunkBytes / 1e6,
                BuildSeconds, perColumn * 1e6, (asked - built) * perColumn);
        }
    }
}
