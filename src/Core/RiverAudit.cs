using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// Walks the centre line of every generated river, block by block, and reports where the water is broken:
    ///  - blocked: dry land on the centre line (a dam, a cliff, a flattened story site, a mouth that stops short);
    ///  - uphill: the water surface rises going downstream (a lake or pool higher than the river running into it);
    ///  - fall: the surface drops by 2 or more blocks between neighbouring blocks (out of a lake, or a bug);
    ///  - rapids: such a drop where the river's pool layout puts one (pool layout 1: rapids, as intended);
    ///  - waterfall: such a drop at one of the listed waterfalls (RiverFalls), as intended.
    /// Uses the terrain model, so it sees what the world generator will build (including a world's flattened story
    /// sites), but not what vanilla adds later (structures, trees). Thread-safe; takes a few tens of seconds.
    /// </summary>
    public sealed class RiverAudit
    {
        public sealed class Problem
        {
            public string Kind;
            public double X, Z, Lat, Lon, Q;
            /// <summary>Blocked: blocks of dry land in a row; uphill/fall: blocks of height.</summary>
            public int Size;
            public int Reach;
            public string Note;
        }

        public int Reaches, Samples;
        public long WaterSamples;
        public readonly List<Problem> Problems = new List<Problem>();
        readonly Dictionary<(string, int, int), Problem> byCell = new Dictionary<(string, int, int), Problem>();
        public TimeSpan Elapsed;

        /// <param name="rivers">The model's network as built (with its reaches and courses), when the model's own has released
        /// them (RiverNetwork.ReleaseStartupData); null for the model's.</param>
        public static RiverAudit Run(TerrainModel model, Action<string> progress = null, RiverNetwork rivers = null)
        {
            var audit = new RiverAudit();
            rivers ??= model.Rivers;
            var reaches = rivers?.Reaches ?? model.Data.Rivers;
            if (rivers == null || reaches == null) return audit;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var geo = model.Geometry;
            // spatial order, so the terrain tile cache is reused
            var order = Enumerable.Range(0, reaches.Count).Where(r => rivers.Kept(r))
                .OrderBy(r => (int)Math.Floor(reaches[r].Lat[0] / 3)).ThenBy(r => (int)Math.Floor(reaches[r].Lon[0] / 3)).ToList();
            int done = 0;
            foreach (int r in order)
            {
                var re = reaches[r];
                // the course as laid out in this world (straightened)
                if (!rivers.ReachLine(r, out var lx, out var lz)) continue;
                int n = lx.Length;
                if (n < 2) continue;
                audit.Reaches++;
                bool havePrev = false;
                TerrainModel.Column prev = default;
                int blockedRun = 0;
                double bx = 0, bz = 0;
                double x0 = lx[0], z0 = lz[0];
                for (int k = 1; k < n; k++)
                {
                    double x1 = lx[k], z1 = lz[k];
                    double dx = x1 - x0, dz = z1 - z0, len = Math.Sqrt(dx * dx + dz * dz);
                    if (rivers.AcrossSeam(x0, x1, len))
                    {
                        x0 = x1; z0 = z1; havePrev = false;
                        continue;
                    }
                    int steps = Math.Max(1, (int)Math.Ceiling(len));
                    for (int u = k == 1 ? 0 : 1; u <= steps; u++)
                    {
                        double x = x0 + dx * u / steps, z = z0 + dz * u / steps;
                        // block (floor x, floor z) as generation makes it: GetColumn at the block's corner (ColumnStore), not
                        // half a block off at its centre (code review 2026-11-11, CR-R1's same pattern; the audit only reads)
                        var c = model.GetColumn(Math.Floor(x), Math.Floor(z));
                        audit.Samples++;
                        if (c.Gutter) { havePrev = false; continue; }
                        if (c.Water == TerrainModel.WaterNone)
                        {
                            if (blockedRun == 0) { bx = x; bz = z; }
                            blockedRun++;
                            continue;
                        }
                        audit.WaterSamples++;
                        if (blockedRun > 0)
                        {
                            audit.Add(model, "blocked", bx, bz, re.Q, blockedRun, BlockNote(model, bx, bz), r);
                            blockedRun = 0;
                        }
                        if (havePrev)
                        {
                            int diff = prev.WaterTop - c.WaterTop;
                            if (diff < 0) audit.Add(model, "uphill", x, z, re.Q, -diff, Describe(prev) + " -> " + Describe(c), r);
                            else if (diff >= 2)
                            {
                                string fall = ListedFall(rivers, x, z);
                                if (fall != null) audit.Add(model, "waterfall", x, z, re.Q, diff, fall + ": " + Describe(prev) + " -> " + Describe(c), r);
                                else if (rivers.PoolLayout >= 1 && prev.River && c.River && c.Rapid) audit.Add(model, "rapids", x, z, re.Q, diff, Describe(prev) + " -> " + Describe(c), r);
                                else audit.Add(model, "fall", x, z, re.Q, diff, Describe(prev) + " -> " + Describe(c), r);
                            }
                        }
                        prev = c;
                        havePrev = true;
                    }
                    x0 = x1; z0 = z1;
                }
                if (blockedRun > 0) audit.Add(model, "blocked", bx, bz, re.Q, blockedRun, BlockNote(model, bx, bz) + ", at the river's end", r);
                if (++done % 20000 == 0) progress?.Invoke($"{done} of {order.Count} reaches checked");
            }
            audit.Elapsed = sw.Elapsed;
            return audit;
        }

        /// <summary>The listed waterfall (RiverFalls) at this spot of a river, or null.</summary>
        static string ListedFall(RiverNetwork rivers, double x, double z)
        {
            foreach (var f in rivers.Falls)
            {
                double reach = rivers.HalfWidth(f.Q) + 16;
                if ((f.X - x) * (f.X - x) + (f.Z - z) * (f.Z - z) <= reach * reach) return f.Name;
            }
            return null;
        }

        static string Describe(in TerrainModel.Column c) =>
            (c.River ? "river" : c.Water == TerrainModel.WaterSea ? "sea" : "lake") + " y" + c.WaterTop;

        static string BlockNote(TerrainModel model, double x, double z)
        {
            var c = model.GetColumn(x, z);
            string note = string.Format(CultureInfo.InvariantCulture, "land y{0}, {1:0} m", c.Top, c.Elev);
            foreach (var zone in model.FlatZones)
            {
                if (zone.Distance(x - zone.X, z - zone.Z) < zone.Radius + zone.Blend) note += ", in the flattened site of " + zone.Code;
            }
            return note;
        }

        void Add(TerrainModel model, string kind, double x, double z, double q, int size, string note, int reach)
        {
            // one problem per spot: merge with one of the same kind nearby
            int gx = (int)Math.Floor(x / 24), gz = (int)Math.Floor(z / 24);
            for (int oz = -1; oz <= 1; oz++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (byCell.TryGetValue((kind, gx + ox, gz + oz), out var p))
                    {
                        p.Size = Math.Max(p.Size, size);
                        return;
                    }
                }
            }
            var g = model.Geometry.ToGeo(x, z);
            var np = new Problem { Kind = kind, X = x, Z = z, Lat = g.Lat, Lon = g.Lon, Q = q, Size = size, Note = note, Reach = reach };
            Problems.Add(np);
            byCell[(kind, gx, gz)] = np;
        }

        public int Count(string kind) => Problems.Count(p => p.Kind == kind);

        public string Summary() => string.Format(CultureInfo.InvariantCulture,
            "{0} river reaches, {1:0.0} million blocks of centre line ({2:0.00}% water) checked in {3:0}s: {4} blocked, {5} running uphill, {6} falls of 2+ blocks (and {7} spots at listed waterfalls, {8} rapids)",
            Reaches, Samples / 1e6, 100.0 * WaterSamples / Math.Max(1, Samples), Elapsed.TotalSeconds, Count("blocked"), Count("uphill"), Count("fall"), Count("waterfall"), Count("rapids"));

        /// <summary>The report: the summary, then the problems, biggest rivers first. spawnX/Z give map coordinates.</summary>
        public string Report(double spawnX, double spawnZ, int max = 400)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary());
            sb.AppendLine("Map coordinates are relative to spawn (as the in-game map shows them); x/z are absolute.");
            foreach (var kind in new[] { "blocked", "uphill", "fall", "waterfall", "rapids" })
            {
                var list = Problems.Where(p => p.Kind == kind).OrderByDescending(p => p.Q).ToList();
                if (list.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"{kind} ({list.Count}):");
                foreach (var p in list.Take(max))
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  Q {0,6:0} m3/s  {1,7:0.000},{2,8:0.000}  x={3:0} z={4:0}  map {5:0}, {6:0}  size {7}  {8}  (reach {9})",
                        p.Q, p.Lat, p.Lon, p.X, p.Z, p.X - spawnX, p.Z - spawnZ, p.Size, p.Note, p.Reach));
                }
                if (list.Count > max) sb.AppendLine($"  ... and {list.Count - max} more");
            }
            return sb.ToString();
        }
    }
}
