using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthMap.Core
{
    // RiverNetwork, part: what only the tests and diagnostics read (EA-0092, the review's ER-27 / terrain-water section 5:
    // moved out of RiverNetwork.cs unchanged). The pool planner's junction caps and first bounds (dbgCap, dbgBound0) are
    // set where the pools are planned and read by DebugReach only.
    public sealed partial class RiverNetwork
    {
        /// <summary>Pool layout 8: the real valley's fall along a reach (m/km, averaged as for ReachRapids).</summary>
        public double ReachValleySlope(int r) => reachValley != null && r >= 0 && r < reachValley.Length ? reachValley[r] : 0;
        double[] dbgCap;
        /// <summary>For DebugReach: each reach's backward bound at its start (NaN where not kept).</summary>
        double[] dbgBound0;
        /// <summary>Total length (blocks) of the indexed river pieces, mouth prolongations left out, and the widest x span of
        /// one piece (tests: nothing real dropped, nothing across the seam).</summary>
        internal (double Length, double MaxDx) IndexedExtent()
        {
            double len = 0, maxDx = 0;
            foreach (var g in segs ?? Array.Empty<Seg>())
            {
                if (g.Mouth) continue;
                len += g.Len;
                maxDx = Math.Max(maxDx, Math.Abs(g.X2 - g.X1));
            }
            return (len, maxDx);
        }
        /// <summary>For tests: the river length (blocks) in reaches of each kind: rapids by layouts 8-10's rule, by layout 11's,
        /// and in all.</summary>
        public (double Old, double New, double All) RapidsLengths()
        {
            double o = 0, n = 0, a = 0;
            if (reachValley == null) return (0, 0, 0);
            for (int r = 0; r < reachValley.Length; r++)
            {
                if (pools[r] == null) continue;
                double len = pools[r].Len;
                a += len;
                if (reachValley[r] >= RapidsSlope) o += len;
                if (RapidsByPower(reachValley[r], reachQ[r])) n += len;
            }
            return (o, n, a);
        }

        /// <summary>For tests: a reach's pools (start level, steps, neighbours).</summary>
        public string DebugReach(int r)
        {
            var p = pools != null && r >= 0 && r < pools.Length ? pools[r] : null;
            if (p == null) return "no pools";
            return $"P0 {p.P0} D0 {p.D0:0} len {p.Len:0} next {p.Next} mainIn {p.MainIn} cap {dbgCap?[r]:0.#} bound0 {(dbgBound0 != null && !double.IsNaN(dbgBound0[r]) ? dbgBound0[r] : (double?)null):0.#} drops [{string.Join(", ", p.Drops.Select(d => d.ToString("0")))}]";
        }

        /// <summary>For tests: a reach's drops with their shapes (c step, s chute, C curtain, S short curtain).</summary>
        public string DebugShapes(int r)
        {
            var p = pools != null && r >= 0 && r < pools.Length ? pools[r] : null;
            if (p?.Shape == null) return "";
            return string.Join(", ", p.Drops.Select((d, i) => d.ToString("0") + (i < p.Shape.Length ? "csCS"[p.Shape[i]].ToString() : "")));
        }

        /// <summary>For tests: every step (world x, z of the centre line, pool top below it).</summary>
        public IEnumerable<(double X, double Z, int Top)> Drops()
        {
            if (segs == null) yield break;
            foreach (var s in segs)
            {
                var pl = pools[s.Reach];
                for (int i = 0; i < pl.Drops.Length; i++)
                {
                    double a = pl.Drops[i] - s.S0;
                    if (a < 0 || a >= s.Len) continue;
                    double f = a / Math.Max(1e-6, s.Len);
                    yield return (s.X1 + f * (s.X2 - s.X1), s.Z1 + f * (s.Z2 - s.Z1), pl.P0 - i - 1);
                }
            }
        }
        /// <summary>Drops this thread's cached cells (for tests that alter an index after querying it).</summary>
        internal static void ResetThreadCache()
        {
            threadCells = null;
            threadCellNext = 0;
        }

        /// <summary>For tests: the segment ids a query in this fine cell examines, in order.</summary>
        internal int[] CellCandidateIds(int cx, int cz)
        {
            var c = Candidates(cx, cz);
            var ids = new int[c.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = c.Items[i].Seg;
            return ids;
        }
    }
}
