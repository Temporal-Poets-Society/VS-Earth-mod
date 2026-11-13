using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// The state arrays and queue of the start-up grid searches (SeaChannels.Route, RiverEnds.Search), kept from one search to
    /// the next instead of allocated per river end (decision 0025: about 24 MB per river mouth, ~20 GB at start-up). Each search
    /// starts from what fresh arrays held: only the states the previous search reached are reset. Not thread-safe: one per
    /// thread that searches (each of SeaChannels.Build's parallel workers has its own; RiverEnds.Prepare searches on one).
    /// </summary>
    internal sealed class SearchBuffers
    {
        public double[] Dist = Array.Empty<double>();
        public int[] Prev = Array.Empty<int>();
        public byte[] Land = Array.Empty<byte>();
        public readonly PriorityQueue<int, double> Queue = new PriorityQueue<int, double>();
        int[] touched = new int[1024];
        public sealed class CellCache { public EarthData.Cell[] Cell = Array.Empty<EarthData.Cell>(); public int[] Depth = Array.Empty<int>(), Stamp = Array.Empty<int>(); public int Search; }
        readonly CellCache cellCache = new CellCache();
        /// <summary>A per-search cache of n grid cells: Stamp[i] == Search marks an entry read in this search.</summary>
        public CellCache Cells(int n)
        {
            var c = cellCache;
            if (c.Stamp.Length < n) { c.Cell = new EarthData.Cell[n]; c.Depth = new int[n]; c.Stamp = new int[n]; c.Search = 0; }
            c.Search++;
            return c;
        }
        int touchedCount;

        /// <summary>Ready for a search over n states: every distance infinite, every land count 0, the queue empty.</summary>
        public void Begin(int n)
        {
            if (Dist.Length < n)
            {
                Dist = new double[n];
                Array.Fill(Dist, double.PositiveInfinity);
                Prev = new int[n];
                Land = new byte[n];
            }
            else
            {
                for (int i = 0; i < touchedCount; i++)
                {
                    int k = touched[i];
                    Dist[k] = double.PositiveInfinity;
                    Land[k] = 0;
                }
            }
            touchedCount = 0;
            Queue.Clear();
        }

        /// <summary>Sets a state's distance (and remembers the state for the next Begin).</summary>
        public void SetDist(int k, double d)
        {
            if (double.IsPositiveInfinity(Dist[k]))
            {
                if (touchedCount == touched.Length) Array.Resize(ref touched, touched.Length * 2);
                touched[touchedCount++] = k;
            }
            Dist[k] = d;
        }
    }
}
