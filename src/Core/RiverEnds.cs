using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Where rivers end (worlds with EarthParams.RiverEnds, with the lakes.bin overlay on): the reaches as the river network
    /// gets them, before it is built.
    ///  - into a terminal lake (the revived Aral Sea, Lop Nur, ... and the natural salt lakes, LakeData): the vertices in the
    ///    lake are at its level (Fixed), the river above never below it;
    ///  - a river stopping short of water (HydroRIVERS ends at the head of a fjord or an estuary the 2' grid has as land, or
    ///    a few cells before a lake) is led on over low ground to the nearest sea or lake within reach, coming down to it;
    ///  - a river with no water within reach ends in a dry basin (a wadi in the sand, the Chu and the Talas in the Moyynkum):
    ///    it fades out (RiverNetwork, fadeEnds) instead of ending at full width.
    /// With it the network also shows every reach below a shown one (RiverNetwork, continueDownstream).
    /// </summary>
    public static class RiverEnds
    {
        /// <summary>Version of these rules, stored per world (EarthParams.RiverEnds; 0: none). Worlds with them also find the
        /// rivers' seam crossings by the ring's geometry (RiverNetwork.geometrySeams).</summary>
        public const int Version = 1;
        /// <summary>How far (2' grid cells, ~3.7 km north-south) a river end is led on to water.</summary>
        const int SearchCells = 16;
        /// <summary>How much higher than the river's end (m) the ground on the way may be (a valley's floor between its walls);
        /// to the sea over a few cells more: a fjord or a narrow estuary averages with its walls into its 2' cells.</summary>
        const double ClimbMetres = 40, FjordClimbMetres = 150;
        const int FjordCells = 8;
        /// <summary>Rivers smaller than this (m³/s) are left as they are: no world shows them.</summary>
        const double MinQ = 5;

        public enum End { Water, IntoLake, Led, Faded, Antimeridian }

        public sealed class Result
        {
            public List<RiverReach> Reaches;
            /// <summary>Per reach: ends in a dry basin and fades out.</summary>
            public bool[] Fade;
            /// <summary>Reaches whose course was changed or classified, and how they end.</summary>
            public readonly Dictionary<int, End> Ends = new Dictionary<int, End>();
            public int LakeVertices;
        }

        public static Result Prepare(EarthData data, List<RiverReach> reaches)
        {
            var res = new Result { Reaches = new List<RiverReach>(reaches), Fade = new bool[reaches.Count] };
            var list = res.Reaches;
            int nr = list.Count, cpd = data.TerrainRes;
            RiverReach Own(int r)
            {
                var re = list[r];
                if (!ReferenceEquals(re, reaches[r])) return re;
                var c = new RiverReach { Q = re.Q, Next = re.Next, Lon = (float[])re.Lon.Clone(), Lat = (float[])re.Lat.Clone(),
                    Level = (short[])re.Level.Clone(), Fixed = (bool[])re.Fixed.Clone(), Width = (byte[])re.Width?.Clone() };
                list[r] = c;
                return c;
            }
            (int row, int col) Cell(double lat, double lon) => ((int)Math.Floor((90 - lat) * cpd), (int)Math.Floor((lon + 180) * cpd));

            // 1. into the terminal lakes: the vertices in a lake at its level, the river above it never lower
            var minEnd = new double[nr];
            var search = new SearchBuffers();
            Array.Fill(minEnd, double.NegativeInfinity);
            if (data.LakeOverlay)
            {
                var boxes = new List<(int r0, int r1, int c0, int c1)>();
                foreach (var lake in data.TerminalLakes.Lakes)
                {
                    var (lr, lc) = Cell(lake.Lat, lake.Lon);
                    boxes.Add((lr - 3 * cpd, lr + 3 * cpd, lc - 4 * cpd, lc + 4 * cpd));
                }
                for (int r = 0; r < nr; r++)
                {
                    var re = list[r];
                    if (re.Q < MinQ) continue;
                    int first = -1;
                    for (int k = 0; k < re.Lon.Length; k++)
                    {
                        var (row, col) = Cell(re.Lat[k], re.Lon[k]);
                        bool inBox = false;
                        foreach (var b in boxes) if (row >= b.r0 && row <= b.r1 && col >= b.c0 && col <= b.c1) { inBox = true; break; }
                        if (!inBox || !data.IsTerminalLakeCell(row, col)) continue;
                        short level = data.GetCell(row, col).Level;
                        var own = Own(r);
                        if (first < 0)
                        {
                            first = k;
                            for (int j = 0; j < k; j++) own.Level[j] = Math.Max(own.Level[j], level);
                        }
                        own.Level[k] = level;
                        own.Fixed[k] = true;
                        res.LakeVertices++;
                    }
                    if (first < 0) continue;
                    // below the lake (a river leaving it) never above its level
                    var o = list[r];
                    short lv = o.Level[first];
                    for (int j = first + 1; j < o.Level.Length; j++) if (!o.Fixed[j]) o.Level[j] = Math.Min(o.Level[j], lv);
                    res.Ends[r] = End.IntoLake;
                }
                // the reaches above: never below where the reach they run into starts (downstream first)
                for (int r = nr - 1; r >= 0; r--)
                {
                    int nx = list[r].Next;
                    if (nx < 0 || nx >= nr || ReferenceEquals(list[nx], reaches[nx])) continue;
                    short start = list[nx].Level[0];
                    var re = list[r];
                    if (re.Level[re.Level.Length - 1] >= start) continue;
                    var own = Own(r);
                    for (int j = 0; j < own.Level.Length; j++) own.Level[j] = Math.Max(own.Level[j], start);
                }
            }

            // 2. rivers that end short of water: led on to it, or faded
            for (int r = 0; r < nr; r++)
            {
                var re = list[r];
                if (re.Next >= 0 || re.Q < MinQ || re.Lon.Length < 2) continue;
                int n = re.Lon.Length;
                if (re.Fixed[n - 1]) continue;
                var (row, col) = Cell(re.Lat[n - 1], re.Lon[n - 1]);
                if (re.Level[n - 1] <= 5 && WaterNear(data, row, col, 2)) continue;
                if (Math.Abs(re.Lon[n - 1]) >= 179.98f) { res.Ends[r] = End.Antimeridian; continue; }
                var path = Route(data, search, row, col, re.Level[n - 1]);
                if (path == null)
                {
                    res.Fade[r] = true;
                    res.Ends[r] = End.Faded;
                    continue;
                }
                var own = Own(r);
                double endLevel = own.Level[n - 1];
                var goal = data.GetCell(path[path.Count - 1].r, path[path.Count - 1].c);
                double goalLevel = (goal.Flags & EarthData.FlagOcean) != 0 ? 0 : goal.Level;
                var lon = new List<float>(own.Lon);
                var lat = new List<float>(own.Lat);
                var lev = new List<short>(own.Level);
                var fix = new List<bool>(own.Fixed);
                for (int i = 1; i < path.Count; i++)
                {
                    double t = (double)i / (path.Count - 1);
                    lat.Add((float)(90 - (path[i].r + 0.5) / cpd));
                    lon.Add((float)((path[i].c + 0.5) / cpd - 180));
                    lev.Add((short)Math.Round(endLevel + (goalLevel - endLevel) * t));
                    fix.Add(i == path.Count - 1);
                }
                // (0063: the points it is led on through have no measured width)
                if (own.Width != null && own.Width.Length == n) Array.Resize(ref own.Width, lat.Count);
                own.Lon = lon.ToArray();
                own.Lat = lat.ToArray();
                own.Level = lev.ToArray();
                own.Fixed = fix.ToArray();
                res.Ends[r] = End.Led;
            }
            return res;
        }

        static bool IsWater(in EarthData.Cell c) => (c.Flags & (EarthData.FlagOcean | EarthData.FlagLake)) != 0;

        static bool WaterNear(EarthData data, int row, int col, int radius)
        {
            for (int dr = -radius; dr <= radius; dr++)
                for (int dc = -radius; dc <= radius; dc++)
                    if (IsWater(data.GetCell(row + dr, col + dc))) return true;
            return false;
        }

        /// <summary>
        /// Way on the grid from a river's end cell to the nearest sea, or lake no higher than the river, within SearchCells,
        /// over land (not ice) keeping low: at most ClimbMetres above the river's end, or more (up to FjordClimbMetres) only
        /// down a fjord or an estuary to the sea within FjordCells. Null if there is none.
        /// The cheapest way over ground up to FjordClimbMetres comes first, as before. Where that one isn't allowed (it
        /// climbs too high for where it ends), a longer way that is allowed may still exist: searched for separately, over
        /// low ground only, then (for the sea) over higher ground within reach of a fjord.
        /// </summary>
        static List<(int r, int c)> Route(EarthData data, SearchBuffers search, int row0, int col0, double endLevel)
            => Allowed(data, Search(data, search, row0, col0, endLevel, FjordClimbMetres, SearchCells, false), endLevel)
            ?? Search(data, search, row0, col0, endLevel, ClimbMetres, SearchCells, false)
            ?? Allowed(data, Search(data, search, row0, col0, endLevel, FjordClimbMetres, FjordCells, true, FjordCells), endLevel);

        /// <summary>The path if it is allowed (see Route), else null.</summary>
        static List<(int r, int c)> Allowed(EarthData data, List<(int r, int c)> path, double endLevel)
        {
            if (path == null) return null;
            double climb = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var c = data.GetCell(path[i].r, path[i].c);
                if (!IsWater(c)) climb = Math.Max(climb, c.Elev - endLevel);
            }
            bool sea = (data.GetCell(path[^1].r, path[^1].c).Flags & EarthData.FlagOcean) != 0;
            return climb > ClimbMetres && !(sea && path.Count - 1 <= FjordCells) ? null : path;
        }

        /// <summary>Cheapest way (Dijkstra, cost rising with the climb) to water within radius cells, over land at most
        /// climbLimit metres above the river's end; seaOnly: lakes are not a goal.</summary>
        /// <param name="maxSteps">Above 0: only ways of at most this many steps (the fjord fallback); the state is then
        /// (cell, steps), so a cheaper but longer way to a cell can't hide a short one.</param>
        static List<(int r, int c)> Search(EarthData data, SearchBuffers search, int row0, int col0, double endLevel, double climbLimit, int radius, bool seaOnly, int maxSteps = 0)
        {
            int R = radius, side = 2 * R + 1, W = data.TerrainW;
            int L = maxSteps > 0 ? maxSteps + 1 : 1;
            // (reused buffers, starting as fresh arrays would)
            search.Begin(side * side * L);
            var dist = search.Dist;
            var prev = search.Prev;
            int s0 = (R * side + R) * L;
            search.SetDist(s0, 0);
            prev[s0] = -1;
            var pq = search.Queue;
            pq.Enqueue(s0, 0);
            int goal = -1;
            while (pq.TryDequeue(out int k, out double dk))
            {
                if (dk > dist[k]) continue;
                int cell = k / L, steps = k % L;
                if (maxSteps > 0 && steps >= maxSteps && k != s0)
                {
                    var last = data.GetCell(row0 + cell / side - R, col0 + cell % side - R);
                    if ((last.Flags & EarthData.FlagOcean) != 0 || !seaOnly && IsWater(last)) { goal = k; break; }
                    continue;
                }
                int dr = cell / side - R, dc = cell % side - R;
                if (k != s0)
                {
                    var here = data.GetCell(row0 + dr, col0 + dc);
                    if ((here.Flags & EarthData.FlagOcean) != 0 || !seaOnly && IsWater(here)) { goal = k; break; }
                }
                for (int nr = -1; nr <= 1; nr++)
                {
                    for (int nc = -1; nc <= 1; nc++)
                    {
                        if (nr == 0 && nc == 0) continue;
                        int ar = dr + nr, ac = dc + nc;
                        if (Math.Abs(ar) > R || Math.Abs(ac) > R) continue;
                        int row = row0 + ar;
                        if (row < 0 || row >= data.TerrainH) continue;
                        var c = data.GetCell(row, ((col0 + ac) % W + W) % W);
                        double cost;
                        double step = nr != 0 && nc != 0 ? Math.Sqrt(2) : 1;
                        if ((c.Flags & EarthData.FlagOcean) != 0) cost = step;
                        else if ((c.Flags & EarthData.FlagLake) != 0)
                        {
                            if (c.Level > endLevel + 2) continue;
                            cost = step;
                        }
                        else
                        {
                            if ((c.Flags & EarthData.FlagIce) != 0 || c.Elev > endLevel + climbLimit) continue;
                            cost = step * (1 + Math.Max(0, c.Elev - endLevel) / 5);
                        }
                        int m = ((ar + R) * side + ac + R) * L + (maxSteps > 0 ? steps + 1 : 0);
                        double nd = dk + cost;
                        if (nd >= dist[m]) continue;
                        search.SetDist(m, nd);
                        prev[m] = k;
                        pq.Enqueue(m, nd);
                    }
                }
            }
            if (goal < 0) return null;
            var path = new List<(int r, int c)>();
            for (int k = goal; k >= 0; k = prev[k]) path.Add((row0 + k / L / side - R, ((col0 + k / L % side - R) % W + W) % W));
            path.Reverse();
            return path;
        }
    }
}
