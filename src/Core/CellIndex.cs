using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// A "which items reach this cell" index, built on several cores: each item covers a rectangle of cells, and each cell's
    /// array lists the items reaching it in ascending order. The order is part of what the queries answer (their ties and
    /// blending sums go in item order), so it must not depend on the threads: every cell belongs to one worker (by its key's
    /// hash), and every worker walks all the items in order, keeping only the cells it owns. The arrays come out the same
    /// with any number of workers, and as a serial build makes them. Used by the river network's index
    /// (RiverSpatialIndexBuilder) and the sea channels' (SeaChannels.Build).
    /// </summary>
    internal static class CellIndex
    {
        /// <summary>An item's cells: x0..x1 by z0..z1 (inclusive). Called once per item per worker, from several threads.</summary>
        public delegate void Rect(int item, out int x0, out int x1, out int z0, out int z1);

        /// <summary>The key of cell (x, z), as RiverNetwork.Key and the sea channels' index have it.</summary>
        public static long Key(int x, int z) => ((long)x << 32) | (uint)z;

        /// <summary>Adds every cell any of the <paramref name="count"/> items reaches to <paramref name="into"/> (which must not
        /// have them yet), with its items in ascending order. Workers: <see cref="ParallelWork.DefaultWorkers"/> (each walks
        /// every item, so more would mostly repeat that walk).</summary>
        public static void Build(int count, Rect rect, Dictionary<long, int[]> into, int workers = 0)
        {
            if (count <= 0) return;
            int w = workers > 0 ? workers : ParallelWork.DefaultWorkers;
            var parts = new Dictionary<long, int[]>[w];
            if (w == 1) parts[0] = BuildPart(count, rect, 0, 1);
            else Parallel.For(0, w, new ParallelOptions { MaxDegreeOfParallelism = w }, t => parts[t] = BuildPart(count, rect, t, w));
            int total = into.Count;
            foreach (var p in parts) total += p.Count;
            into.EnsureCapacity(total);
            foreach (var p in parts)
                foreach (var kv in p) into.Add(kv.Key, kv.Value);
        }

        /// <summary>The worker owning a cell: its key mixed (so that neighbouring cells spread over the workers), modulo the
        /// workers.</summary>
        static int Owner(long key, int workers) => workers == 1 ? 0 : (int)(((ulong)key * 0x9E3779B97F4A7C15UL >> 33) % (ulong)workers);

        sealed class Slot
        {
            public int[] Ids;
            public int At;
        }

        /// <summary>One worker's cells: counted first, then filled in the items' order. An item's cells mostly continue the
        /// item before's (a course's pieces follow each other), so the counting goes by runs of one cell and looks it up once
        /// per run.</summary>
        static Dictionary<long, int[]> BuildPart(int count, Rect rect, int t, int w)
        {
            var counts = new Dictionary<long, int>();
            long runKey = 0;
            int run = 0;
            for (int i = 0; i < count; i++)
            {
                rect(i, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long key = Key(x, z);
                        if (Owner(key, w) != t) continue;
                        if (run > 0 && key == runKey) { run++; continue; }
                        if (run > 0) CollectionsMarshal.GetValueRefOrAddDefault(counts, runKey, out _) += run;
                        runKey = key;
                        run = 1;
                    }
            }
            if (run > 0) CollectionsMarshal.GetValueRefOrAddDefault(counts, runKey, out _) += run;
            var slots = new Dictionary<long, Slot>(counts.Count);
            foreach (var kv in counts) slots[kv.Key] = new Slot { Ids = new int[kv.Value] };
            counts = null;
            Slot slot = null;
            long slotKey = 0;
            for (int i = 0; i < count; i++)
            {
                rect(i, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        long key = Key(x, z);
                        if (Owner(key, w) != t) continue;
                        if (slot == null || key != slotKey) { slot = slots[key]; slotKey = key; }
                        slot.Ids[slot.At++] = i;
                    }
            }
            var cells = new Dictionary<long, int[]>(slots.Count);
            foreach (var kv in slots) cells.Add(kv.Key, kv.Value.Ids);
            return cells;
        }
    }
}
