using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Delta branches (worlds with EarthParams.RiverDeltas): HydroRIVERS is a tree, so where a river splits into branches the
    /// whole river goes down one of them and the others are small local streams, or missing. The Mekong's main stem crosses
    /// from the Tiền to the Bassac at Vàm Nao and takes all of its 15 000 m³/s down the Bassac (54 blocks wide in a 204 800
    /// ring, a straight canal across the delta), while the Tiền below Vàm Nao is a 20-90 m³/s stream.
    /// Each delta in <see cref="All"/> names the point on the main stem where it splits and its branches, each found by its
    /// mouth (the river ending nearest to it): every reach of the branch's main chain gets its share of the discharge at the
    /// split, its head is led to the split point (through optional points on the way) at the split's level, and the main
    /// stem below the split keeps what is left. The reaches keep their indices.
    /// </summary>
    public static class RiverDeltas
    {
        /// <summary>Version of these rules, stored per world (EarthParams.RiverDeltas; 0: none).</summary>
        public const int Version = 1;
        /// <summary>How far (degrees) the split point and a branch's mouth may be from the river data.</summary>
        const double SplitReach = 0.05, MouthReach = 0.08;

        public sealed class Branch
        {
            public string Name;
            public double MouthLat, MouthLon;
            /// <summary>Share of the discharge at the split.</summary>
            public double Share;
            /// <summary>Points (lat, lon) from the split to the head of the branch's chain in the river data, or none.</summary>
            public (double Lat, double Lon)[] Via = Array.Empty<(double, double)>();
        }

        public sealed class Delta
        {
            public string Name;
            public double SplitLat, SplitLon;
            public Branch[] Branches;
        }

        /// <summary>The deltas, applied in this order (a later split can be on a branch of an earlier one).</summary>
        public static readonly Delta[] All =
        {
            // Vàm Nao: below it the Tiền and the Hậu (Bassac) carry about half each (the Tiền ~85% above it, the Vàm Nao
            // channel moves a third of it across)
            new Delta
            {
                Name = "Mekong", SplitLat = 10.573, SplitLon = 105.365,
                Branches = new[] { new Branch { Name = "Tiền (Cổ Chiên)", MouthLat = 9.852, MouthLon = 106.496, Share = 0.5 } },
            },
        };

        public sealed class Result
        {
            public List<RiverReach> Reaches;
            public readonly List<string> Report = new List<string>();
        }

        public static Result Apply(List<RiverReach> reaches, Delta[] deltas = null)
        {
            var res = new Result { Reaches = new List<RiverReach>(reaches) };
            var list = res.Reaches;
            int nr = list.Count;
            RiverReach Own(int r)
            {
                var re = list[r];
                if (!ReferenceEquals(re, reaches[r])) return re;
                var c = new RiverReach { Q = re.Q, Next = re.Next, Lon = re.Lon, Lat = re.Lat, Level = re.Level, Fixed = re.Fixed, Width = re.Width };
                list[r] = c;
                return c;
            }
            // reaches flowing into each reach (built once: the deltas change no Next)
            var into = new List<int>[nr];
            for (int r = 0; r < nr; r++)
            {
                int n = list[r].Next;
                if (n >= 0 && n < nr) (into[n] ??= new List<int>()).Add(r);
            }

            foreach (var d in deltas ?? All)
            {
                // the main stem at the split: the biggest river there
                int r0 = -1, k0 = -1;
                double bestQ = 0, bestD = double.MaxValue;
                double cosS = Math.Cos(d.SplitLat * Math.PI / 180);
                for (int r = 0; r < nr; r++)
                {
                    var re = list[r];
                    if (re.Q < bestQ) continue;
                    for (int k = 0; k < re.Lat.Length; k++)
                    {
                        double dd = Dist(re.Lat[k], re.Lon[k], d.SplitLat, d.SplitLon, cosS);
                        if (dd > SplitReach) continue;
                        if (re.Q > bestQ || dd < bestD) { bestQ = re.Q; bestD = dd; r0 = r; k0 = k; }
                    }
                }
                if (r0 < 0)
                {
                    res.Report.Add($"{d.Name}: no river at the split {d.SplitLat:0.###},{d.SplitLon:0.###}");
                    continue;
                }
                var main = new HashSet<int>();
                for (int r = r0, guard = 0; r >= 0 && guard < nr && main.Add(r); r = list[r].Next, guard++) { }
                double qs = list[r0].Q;
                short levelS = list[r0].Level[k0];
                float splitLat = list[r0].Lat[k0], splitLon = list[r0].Lon[k0];

                double taken = 0;
                foreach (var b in d.Branches)
                {
                    // the branch: the river ending nearest to its mouth (not the main stem)
                    int m = -1;
                    double md = MouthReach;
                    double cosM = Math.Cos(b.MouthLat * Math.PI / 180);
                    for (int r = 0; r < nr; r++)
                    {
                        var re = list[r];
                        if (re.Next >= 0 || main.Contains(r)) continue;
                        int n = re.Lat.Length;
                        double dd = Dist(re.Lat[n - 1], re.Lon[n - 1], b.MouthLat, b.MouthLon, cosM);
                        if (dd < md) { md = dd; m = r; }
                    }
                    if (m < 0)
                    {
                        res.Report.Add($"{d.Name}: {b.Name}: no river ends near {b.MouthLat:0.###},{b.MouthLon:0.###}; its share stays on the main stem");
                        continue;
                    }
                    // its main chain, mouth to head (the biggest reach flowing in each time)
                    var chain = new List<int> { m };
                    var seen = new HashSet<int> { m };
                    for (int cur = m; into[cur] != null;)
                    {
                        int up = -1;
                        foreach (int i in into[cur]) if (!main.Contains(i) && !seen.Contains(i) && (up < 0 || list[i].Q > list[up].Q)) up = i;
                        if (up < 0) break;
                        chain.Add(up);
                        seen.Add(up);
                        cur = up;
                    }
                    float add = (float)(b.Share * qs);
                    foreach (int r in chain) Own(r).Q += add;
                    // the head led to the split (through the points on the way), at the split's level
                    var head = Own(chain[chain.Count - 1]);
                    int extra = 1 + b.Via.Length;
                    var lat = new float[head.Lat.Length + extra];
                    var lon = new float[lat.Length];
                    var lev = new short[lat.Length];
                    var fix = new bool[lat.Length];
                    lat[0] = splitLat; lon[0] = splitLon;
                    for (int i = 0; i < b.Via.Length; i++) { lat[1 + i] = (float)b.Via[i].Lat; lon[1 + i] = (float)b.Via[i].Lon; }
                    for (int i = 0; i < extra; i++) lev[i] = levelS;
                    Array.Copy(head.Lat, 0, lat, extra, head.Lat.Length);
                    Array.Copy(head.Lon, 0, lon, extra, head.Lon.Length);
                    Array.Copy(head.Level, 0, lev, extra, head.Level.Length);
                    Array.Copy(head.Fixed, 0, fix, extra, head.Fixed.Length);
                    // (0063: the points led to the split have no measured width)
                    if (head.Width != null)
                    {
                        var wid = new byte[lat.Length];
                        Array.Copy(head.Width, 0, wid, extra, head.Width.Length);
                        head.Width = wid;
                    }
                    head.Lat = lat; head.Lon = lon; head.Level = lev; head.Fixed = fix;
                    // the branch's water never above the split's (a local stream may start higher)
                    short top = levelS;
                    for (int c = chain.Count - 1; c >= 0; c--)
                    {
                        var re = Own(chain[c]);
                        if (!ReferenceEquals(re.Level, lev)) re.Level = (short[])re.Level.Clone();
                        for (int i = 0; i < re.Level.Length; i++)
                        {
                            if (re.Level[i] > top) re.Level[i] = top;
                            top = re.Level[i];
                        }
                    }
                    taken += b.Share;
                    res.Report.Add($"{d.Name}: {b.Name}: {chain.Count} reaches from {head.Lat[extra]:0.###},{head.Lon[extra]:0.###} to " +
                        $"{list[m].Lat[list[m].Lat.Length - 1]:0.###},{list[m].Lon[list[m].Lon.Length - 1]:0.###}, +{add:0} m³/s (now {list[m].Q:0} at the mouth)");
                }
                if (taken <= 0) continue;
                float keep = (float)Math.Max(0, 1 - taken);
                foreach (int r in main) Own(r).Q *= keep;
                res.Report.Add($"{d.Name}: main stem below the split {qs:0} -> {qs * keep:0} m³/s ({main.Count} reaches)");
            }
            return res;
        }

        static double Dist(double lat, double lon, double lat0, double lon0, double cos)
        {
            double dx = Geo.Wrap180(lon - lon0) * cos, dz = lat - lat0;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
