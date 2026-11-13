using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// A bounded cache of decoded tiles keyed by number, for the readers that decode a tile the first time something comes
    /// into it: CoastLayer (coasts.bin), LakeOutlineLayer (the lake and river outlines, the works of 1318), TiledGrid (rain,
    /// aridity, summits, glaciers), TerrainTileCache (terrain.bin's tiles under a world's overlays) and FineTemperature
    /// (temperature.bin's decoded temperatures). A hit takes no lock: each thread first looks at the last tile this cache
    /// gave it (a chunk's columns mostly read one tile of each), then at the last few tiles it used of any cache of this
    /// kind, then at a concurrent map; a tile in none of them is decoded once (others asking for it meanwhile wait for that
    /// decode), and past Max the least recently used quarter is dropped. Tiles are pure functions of their key, so what is
    /// cached never changes a result; a failed decode is not kept. Before, each reader took one global lock and moved the
    /// tile in a linked list on every lookup, from every worldgen thread.
    /// </summary>
    public sealed class TileCache<T> where T : class
    {
        readonly Func<int, T> decode;
        /// <summary>Tiles kept; past it the least recently used are dropped (read at each new tile).</summary>
        public int Max;
        readonly ConcurrentDictionary<int, Lazy<Entry>> map = new();
        readonly object trimGate = new();
        long clock;
        int count;

        sealed class Entry
        {
            public T Value;
            public long Used;
        }

        // Per thread, keyed by the cache's number and the tile's (a number, not the cache, so a thread doesn't keep an
        // unloaded world's cache alive; it keeps at most these few tiles): the last tile of each cache in a slot of the
        // cache's own (TileCacheThread; rain, aridity, summits and glaciers are read in turn for every column, so one slot
        // for all of them would be pushed out at every switch), then the last few tiles of any cache of this kind (a column
        // near a tile's edge reads its neighbours too).
        /// <summary>The cache's number in the high half of a key, and its slot among each thread's last tiles.</summary>
        readonly long tag;
        readonly int slot;
        const int RecentTiles = 8;
        sealed class Recent
        {
            public readonly long[] Keys = { long.MinValue, long.MinValue, long.MinValue, long.MinValue, long.MinValue, long.MinValue, long.MinValue, long.MinValue };
            public readonly T[] Values = new T[RecentTiles];
            public int Next;
        }
        [ThreadStatic] static Recent recent;

        public TileCache(Func<int, T> decode, int max)
        {
            this.decode = decode;
            Max = max;
            long id = TileCacheThread.NewId();
            tag = id << 32;
            slot = (int)id & (TileCacheThread.Count - 1);
        }

        /// <summary>The tile of a key, decoded when first asked for.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int key)
        {
            // (small, so it is inlined into the readers' lookups: as fast as the last tile TerrainTileCache, FineTemperature
            // and TiledGrid each kept per thread in front of this cache before)
            long k = tag | (uint)key;
            var slots = TileCacheThread.Slots;
            if (slots != null)
            {
                ref var last = ref slots[slot];
                // (the slot holds a T: only this cache writes under its own number)
                if (last.Key == k) return Unsafe.As<T>(last.Value);
            }
            return Miss(key, k);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        T Miss(int key, long k)
        {
            var r = recent ??= new Recent();
            int at = Array.IndexOf(r.Keys, k);
            T value;
            if (at >= 0) value = r.Values[at];
            else
            {
                value = Shared(key);
                r.Keys[r.Next] = k;
                r.Values[r.Next] = value;
                r.Next = (r.Next + 1) % RecentTiles;
            }
            ref var last = ref (TileCacheThread.Slots ??= TileCacheThread.NewSlots())[slot];
            last.Key = k;
            last.Value = value;
            return value;
        }

        T Shared(int key)
        {
            if (!map.TryGetValue(key, out var lazy))
            {
                var made = new Lazy<Entry>(() => new Entry { Value = decode(key) }, LazyThreadSafetyMode.ExecutionAndPublication);
                lazy = map.GetOrAdd(key, made);
                if (ReferenceEquals(lazy, made) && Interlocked.Increment(ref count) > Max) Trim();
            }
            Entry e;
            try { e = lazy.Value; }
            catch
            {
                // not kept: the next ask decodes again, as before
                if (map.TryRemove(new KeyValuePair<int, Lazy<Entry>>(key, lazy))) Interlocked.Decrement(ref count);
                throw;
            }
            e.Used = Interlocked.Increment(ref clock);
            return e.Value;
        }

        void Trim()
        {
            if (!Monitor.TryEnter(trimGate)) return;
            try
            {
                int keep = Math.Max(1, Max * 3 / 4);
                if (Volatile.Read(ref count) <= Max) return;
                var held = new List<(int Key, long Used, Lazy<Entry> Lazy)>();
                foreach (var kv in map)
                    if (kv.Value.IsValueCreated) held.Add((kv.Key, kv.Value.Value.Used, kv.Value));
                held.Sort((a, b) => a.Used.CompareTo(b.Used));
                foreach (var h in held)
                {
                    if (Volatile.Read(ref count) <= keep) break;
                    if (map.TryRemove(new KeyValuePair<int, Lazy<Entry>>(h.Key, h.Lazy))) Interlocked.Decrement(ref count);
                }
            }
            finally { Monitor.Exit(trimGate); }
        }

        /// <summary>Drops every tile (tests).</summary>
        public void Clear()
        {
            lock (trimGate)
            {
                foreach (var k in map.Keys)
                    if (map.TryRemove(k, out _)) Interlocked.Decrement(ref count);
            }
        }
    }

    /// <summary>
    /// Each thread's last tile of every TileCache, one slot per cache by its number. Not generic, so a lookup reads it as
    /// cheaply as a reader's own thread-static field: a thread-static of a generic class is found through the type on
    /// every read, and the same slots there made world sampling a few percent slower.
    /// </summary>
    static class TileCacheThread
    {
        /// <summary>Slots per thread: caches made one after the other (a world's dozen or so) get slots of their own.</summary>
        public const int Count = 16;
        public struct Slot
        {
            public long Key;
            public object Value;
        }
        [ThreadStatic] public static Slot[] Slots;
        static long instances;

        public static long NewId() => Interlocked.Increment(ref instances);

        public static Slot[] NewSlots()
        {
            var s = new Slot[Count];
            for (int i = 0; i < Count; i++) s[i].Key = long.MinValue;
            return s;
        }
    }
}
