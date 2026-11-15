using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using static EarthMap.Core.RiverNetwork;

namespace EarthMap.Core
{
    /// <summary>
    /// The last stage of a river network's build: the pieces' cells and the index the queries find them by. Each piece is
    /// in every 128-block cell its influence (water, bank, valley: Reach) reaches, and the index groups whole cells into
    /// buckets of indexSpan × indexSpan (SizeScale rounded up: one bucket per legacy cell would replicate every widened piece
    /// into O(scale²) buckets at large scales), each bucket's pieces in piece order (the order fixes the queries' ties and
    /// blending sums).
    /// </summary>
    internal static class RiverSpatialIndexBuilder
    {
        /// <summary>Sets each piece's cell range (Seg.CellX0..CellZ1) and fills grid with the buckets' piece ids.</summary>
        public static void Build(RiverNetwork net, Seg[] segs, Dictionary<long, int[]> grid)
        {
            // each piece's cells, on all cores (a piece's cells are its own; its reach is a pure function of its discharge,
            // and the pieces come reach by reach, so a run of one discharge computes it once, as the queries' cache does)
            // (ER-25: in runs of 65,536 pieces on ParallelWork's few workers, not a raw uncapped Parallel.ForEach)
            const int Run = 1 << 16;
            if (segs.Length > 0) ParallelWork.For((segs.Length + Run - 1) / Run, k =>
            {
                int lastQ = 0;
                bool haveQ = false;
                double reach = 0, halfWidth = 0;
                for (int i = k * Run, end = Math.Min(segs.Length, i + Run); i < end; i++)
                {
                    ref var s = ref segs[i];
                    int qBits = BitConverter.SingleToInt32Bits(s.Q);
                    if (!haveQ || qBits != lastQ)
                    {
                        reach = net.Reach(s.Q, out halfWidth);
                        lastQ = qBits;
                        haveQ = true;
                    }
                    // (channel rules 4: as far as the mask widens it, RiverNetwork.SegReach)
                    double r = net.SegReach(s.M1, s.M2, s.Q, reach, halfWidth);
                    s.CellX0 = (int)Math.Floor((Math.Min(s.X1, s.X2) - r) / Cell);
                    s.CellX1 = (int)Math.Floor((Math.Max(s.X1, s.X2) + r) / Cell);
                    s.CellZ0 = (int)Math.Floor((Math.Min(s.Z1, s.Z2) - r) / Cell);
                    s.CellZ1 = (int)Math.Floor((Math.Max(s.Z1, s.Z2) + r) / Cell);
                }
            });
            // then the buckets, each with its pieces in piece order, on several cores (CellIndex; the same arrays as the
            // serial build that was here until 2.0.0-rc.3)
            CellIndex.Build(segs.Length, (int i, out int x0, out int x1, out int z0, out int z1) =>
            {
                ref readonly var s = ref segs[i];
                x0 = net.Bucket(s.CellX0); x1 = net.Bucket(s.CellX1);
                z0 = net.Bucket(s.CellZ0); z1 = net.Bucket(s.CellZ1);
            }, grid);
        }
    }
}
