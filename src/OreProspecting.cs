using System;
using System.Threading;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>
    /// What the prospecting pick reads for Earth's ore maps: per deposit code the expected ore in a column, from the bound
    /// variants' attempts and placement rules, rules v3's ore bodies and rules v4's catalogue, over Earth's own rock column
    /// (cached per thread). Part of an <see cref="OreSession"/>; each EarthOreGenerator answers the pick through it.
    /// </summary>
    sealed class OreProspecting
    {
        readonly OreSession session;
        internal OreProspecting(OreSession session) { this.session = session; }

        /// <summary>
        /// Expectation for the whole deposit code (the pick asks one variant per code): sum over natively mapped bound variants.
        /// Hosts come from Earth's strata (vanilla's simulated column where Earth keeps vanilla strata); factors from Earth.
        /// Formerly unmapped variants (clay, quartz, surface starters…) stay unlisted, as in vanilla.
        /// </summary>
        internal void Reading(EarthOreGenerator asked, BlockPos pos, int[] blockColumn, out double ppt, out double totalFactor)
        {
            ppt = 0; totalFactor = 0;
            int top = asked.TopAt(pos);
            if (top <= 1) return;
            // nothing was placed in the rock floor (decision 0050)
            int floor = EarthWorldGen.RockFloorTop(pos.X, pos.Z);
            var column = EarthColumn(pos.X, pos.Z, top, floor, blockColumn);
            int cx = pos.X >> 5, cz = pos.Z >> 5;
            var ctx = session.Context(cx, cz);
            // vanilla multiplies every variant's attempts by the world's deposit rate (globalDepositSpawnRate)
            double rate = session.DepositRate;
            foreach (var b in session.SameCode(asked.Binding))
            {
                if (!b.NativelyMapped) continue;
                if (b.IsChild)
                {
                    var pb = b.Parent;
                    if (pb == null) continue;
                    // the child is placed only where its own depth rules allow, at the parent's depths (GenerateChild)
                    var child = b.Generator;
                    double parentShare = pb.Generator.PlacedShare(pos.X, pos.Z, column, top, ctx, d => child.ChildDepthFactor(ctx, d));
                    double parentPpt = session.AttemptFactor(pb, cx, cz) * rate * parentShare * pb.Generator.ExpectedPerChunk / (top * 1024.0) * 1000;
                    double childFactor = session.AttemptFactor(b, cx, cz);
                    ppt += parentPpt * b.Variant.TriesPerChunk / 1024 * childFactor * b.Generator.ChildBlocks(pb.Generator.MeanThickness);
                    totalFactor = Math.Max(totalFactor, Math.Min(1, childFactor) * Math.Min(1, parentPpt / 1000));
                    continue;
                }
                double f = session.AttemptFactor(b, cx, cz) * rate;
                double share = b.Generator.PlacedShare(pos.X, pos.Z, column, top, ctx);
                totalFactor = Math.Max(totalFactor, Math.Min(1, f) * share);
                ppt += f * share * b.Generator.ExpectedPerChunk / (top * 1024.0) * 1000;
            }
            // coal measures (rules v3) under this column
            string code = asked.Binding.Variant.Code;
            double seam = session.Bodies.SeamBlocks(code, pos.X, pos.Z, top, floor, out double basin,
                asked.Layers == null ? null : (b, d) => b.Generator.CanPlace(column, top, d, pos.X, pos.Z));
            if (seam > 0)
            {
                ppt += seam / top * 1000;
                totalFactor = Math.Max(totalFactor, basin);
            }
            // 0065 section 6: the deposit catalogue's bodies and beds in this column, as they are placed
            var deposits = session.Deposits;
            if (deposits != null)
            {
                double blocks = deposits.ColumnBlocks(code, pos.X, pos.Z, top, floor, column);
                if (blocks > 0)
                {
                    ppt += blocks / top * 1000;
                    totalFactor = Math.Max(totalFactor, Math.Min(1, .2 + blocks / 10));
                }
            }
        }

        sealed class ColumnCache { public long Owner; public int X, Z, Top, Floor; public int[] Rock = Array.Empty<int>(); }
        [ThreadStatic] static ColumnCache columnCache;
        /// <summary>Rock per y (index 1..top) of Earth's strata at a column; falls back to vanilla's simulated column where Earth
        /// keeps vanilla strata. Empty at and below the rock floor's top <paramref name="floor"/> (0: none), where no deposit was placed.</summary>
        internal int[] EarthColumn(int x, int z, int top, int floor, int[] vanillaColumn)
        {
            var c = columnCache ??= new ColumnCache();
            if (c.Owner == session.CacheId && c.X == x && c.Z == z && c.Top == top && c.Floor == floor) return c.Rock;
            c.Owner = 0;   // incomplete while filling
            if (c.Rock.Length < top + 1) c.Rock = new int[top + 1];
            var geo = session.Geology;
            for (int y = 0; y <= top; y++)
            {
                int r = 0;
                if (y <= floor) { c.Rock[y] = 0; continue; }
                if (geo != null && y > 0)
                {
                    try { r = geo.RegionalRock(x, z, top - y, top); }
                    catch (Exception e)
                    {
                        // the rest of this column reads vanilla's simulated rock; logged once per session (CR-O4)
                        geo = null; r = 0;
                        if (Interlocked.Increment(ref session.ColumnErrors) == 1) session.Log.Error("Earth ores: reading Earth's rock for prospecting failed; vanilla's simulated column is used there (further errors suppressed). {0}", e);
                    }
                }
                c.Rock[y] = r > 0 ? r : vanillaColumn != null && y < vanillaColumn.Length ? vanillaColumn[y] : 0;
            }
            c.Owner = session.CacheId; c.X = x; c.Z = z; c.Top = top; c.Floor = floor;
            return c.Rock;
        }
    }
}
