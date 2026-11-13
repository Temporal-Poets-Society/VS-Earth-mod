using System.Collections.Concurrent;
using EarthMap.Core;

namespace RiverSnap
{
    /// <summary>A decoded GLO-90 tile (DemTiles.Tile's heights), or a marker for open sea (0 m) and a missing tile.</summary>
    sealed class GridTile
    {
        public int Cols;
        public short[] H;
        public long Used;
        public static readonly GridTile Sea = new GridTile { Cols = 0 };
        public static readonly GridTile Missing = new GridTile { Cols = -1 };
    }

    /// <summary>
    /// The converted GLO-90 store (FineElevation's folder and tile list), decoded with DemTiles, shared by all workers with a
    /// bounded least-recently-used cache. Lookups go through each worker's own <see cref="Sampler"/>, which keeps its last
    /// few tiles, so the shared dictionary is touched only when a worker moves to another square.
    /// </summary>
    sealed class TileStore
    {
        readonly FineElevation fine;
        readonly ConcurrentDictionary<int, Lazy<GridTile>> map = new();
        readonly object evictGate = new();
        long clock;
        public readonly int Capacity;
        public long Loads, Evictions, MissingTiles, BytesRead;

        public TileStore(FineElevation fine, int capacity)
        {
            this.fine = fine;
            Capacity = Math.Max(16, capacity);
        }

        public int Cached => map.Count;

        public GridTile Get(int latS, int lonW)
        {
            if (!fine.SourceHas(latS, lonW)) return GridTile.Sea;
            int key = FineElevation.Key(latS, lonW);
            var lazy = map.GetOrAdd(key, k => new Lazy<GridTile>(() => Load(k), LazyThreadSafetyMode.ExecutionAndPublication));
            var t = lazy.Value;
            t.Used = Interlocked.Increment(ref clock);
            if (map.Count > Capacity) Evict();
            return t;
        }

        GridTile Load(int key)
        {
            FineElevation.FromKey(key, out int latS, out int lonW);
            string path = fine.PathOf(latS, lonW);
            if (!File.Exists(path))
            {
                Interlocked.Increment(ref MissingTiles);
                return GridTile.Missing;
            }
            var bytes = File.ReadAllBytes(path);
            var t = DemTiles.Decode(bytes);
            if (t.LatS != latS || t.LonW != lonW) throw new InvalidDataException(path + " holds " + DemTiles.ShortName(t.LatS, t.LonW));
            Interlocked.Increment(ref Loads);
            Interlocked.Add(ref BytesRead, bytes.Length);
            return new GridTile { Cols = t.Cols, H = t.Heights };
        }

        void Evict()
        {
            if (!Monitor.TryEnter(evictGate)) return;
            try
            {
                int over = map.Count - Capacity;
                if (over <= 0) return;
                var loaded = new List<(int Key, long Used)>();
                foreach (var kv in map) if (kv.Value.IsValueCreated) loaded.Add((kv.Key, kv.Value.Value.Used));
                loaded.Sort((a, b) => a.Used.CompareTo(b.Used));
                int drop = Math.Min(loaded.Count, over + Capacity / 8);
                for (int i = 0; i < drop; i++) map.TryRemove(loaded[i].Key, out _);
                Interlocked.Add(ref Evictions, drop);
            }
            finally { Monitor.Exit(evictGate); }
        }
    }

    /// <summary>
    /// GLO-90 heights for one worker thread: the same points and the same bilinear arithmetic as FineElevation.TryHeight
    /// (rows linear along their own spacing, then between rows; a square without a tile is 0 m), NaN where a tile the
    /// source has is not in the store.
    /// </summary>
    sealed class Sampler
    {
        readonly TileStore store;
        readonly int[] keys = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        readonly GridTile[] tiles = new GridTile[4];
        int next;

        public Sampler(TileStore store) { this.store = store; }

        GridTile Tile(int latS, int lonW)
        {
            int key = (latS + 90) * 360 + (lonW + 180);
            if (keys[0] == key) return tiles[0];
            if (keys[1] == key) return tiles[1];
            if (keys[2] == key) return tiles[2];
            if (keys[3] == key) return tiles[3];
            var t = store.Get(latS, lonW);
            keys[next] = key;
            tiles[next] = t;
            next = (next + 1) & 3;
            return t;
        }

        static int Wrap(int lonW) => ((lonW + 180) % 360 + 360) % 360 - 180;

        public double Height(double lat, double lon)
        {
            lat = Math.Clamp(lat, -90, 90);
            lon = Geo.Wrap180(lon);
            int latS = Math.Clamp((int)Math.Ceiling(lat) - 1, -90, 89);
            double fr = (latS + 1 - lat) * DemTiles.Glo90Rows;
            int r0 = Math.Min((int)Math.Floor(fr), DemTiles.Glo90Rows - 1);
            double tr = fr - r0;
            double a = RowValue(latS, r0, lon);
            if (tr <= 0) return a;
            bool below = r0 + 1 >= DemTiles.Glo90Rows;
            if (below && latS == -90) return a;
            double b = RowValue(below ? latS - 1 : latS, below ? 0 : r0 + 1, lon);
            return a + (b - a) * tr;
        }

        double RowValue(int latS, int row, double lon)
        {
            int lonW = Math.Clamp((int)Math.Floor(lon), -180, 179);
            var t = Tile(latS, lonW);
            if (t.Cols < 0) return double.NaN;
            int cols = t.H != null ? t.Cols : DemTiles.Glo90Cols(latS);
            double fc = (lon - lonW) * cols;
            int c0 = Math.Min((int)Math.Floor(fc), cols - 1);
            double tc = fc - c0;
            double p = t.H == null ? 0 : t.H[row * cols + c0];
            if (tc <= 0) return p;
            double q;
            if (c0 + 1 < cols) q = t.H == null ? 0 : t.H[row * cols + c0 + 1];
            else
            {
                var e = Tile(latS, Wrap(lonW + 1));
                if (e.Cols < 0) return double.NaN;
                q = e.H == null ? 0 : e.H[row * e.Cols];
            }
            return p + (q - p) * tc;
        }
    }
}
