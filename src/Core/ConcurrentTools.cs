using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// A concurrent cache of values that are worked out again the same when dropped (the grass colour's curves, the ground's
    /// moisture, the tall grass's height): emptied when it has taken in more than its capacity. It counts what it adds
    /// itself, because ConcurrentDictionary.Count takes every lock (as WeatherStorms' slot cache says), and these caches are
    /// asked on the client's mesh threads and the server's off-thread soil ticks.
    /// </summary>
    public sealed class BoundedCache<TKey, TValue>
    {
        readonly ConcurrentDictionary<TKey, TValue> map = new();
        readonly int capacity;
        int count;

        public BoundedCache(int capacity) { this.capacity = capacity; }

        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value);

        /// <summary>Stores a value, first emptying the cache when it holds more than its capacity.</summary>
        public void Set(TKey key, TValue value)
        {
            if (Volatile.Read(ref count) > capacity) Clear();
            if (map.TryAdd(key, value)) Interlocked.Increment(ref count);
            else map[key] = value;
        }

        public void Clear()
        {
            map.Clear();
            Volatile.Write(ref count, 0);
        }
    }

    /// <summary>
    /// A counter that many world generation threads add to on a hot path (vetoed patches, rejected spawns, fruit-tree
    /// attempts): each thread adds to its own slot, a cache line away from the others, instead of every thread fighting over
    /// one Interlocked field; the slots are summed when it is read (/earth).
    /// </summary>
    public sealed class StripedCounter
    {
        const int Stripes = 16, Spacing = 16;   // 16 longs = 128 bytes between slots
        readonly long[] slots = new long[Stripes * Spacing];

        public void Increment() => Interlocked.Increment(ref slots[(Environment.CurrentManagedThreadId & (Stripes - 1)) * Spacing]);

        public long Value
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < Stripes; i++) sum += Volatile.Read(ref slots[i * Spacing]);
                return sum;
            }
        }
    }
}
