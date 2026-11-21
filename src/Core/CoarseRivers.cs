using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Coarse river rules 2 (EA-0179, decision 0098; EarthParams.CoarseRiverRules, worlds at 1:50 and coarser), applied to
    /// a network's reaches before it lays them out. The reaches given are not changed: a reach this changes is a copy in the
    /// list returned (as RiverDeltas does), at the same index.
    /// <list type="bullet">
    /// <item>Rivers to their end: a reach below a river whose flow has dropped under MinQ (a losing river: the lower
    /// Darling below Wilcannia at 99.9 m³/s, the Shebelle, the Chobe) is drawn as a river. Its flow for the network is MinQ,
    /// so it keeps the river rule's width, depth and banks instead of narrowing at once to a one-block stream (60 places
    /// worldwide). Its reported discharge (RiverNetwork.Hit.Q) is MinQ there.</item>
    /// <item>Head streams (the author: "Yes, taper them"): above each river whose biggest inflow the world doesn't draw (at
    /// 1:100 and 1:200, where no stream is drawn, every river's start), that inflow's own upper main branch is drawn as a
    /// one-block stream for HeadStreamKm of the data's length, the last reach cut there. A river then starts from a stream,
    /// as at 1:50, not out of nothing at its full width. Tributaries stay left out.</item>
    /// </list>
    /// </summary>
    internal static class CoarseRivers
    {
        public sealed class Result
        {
            public List<RiverReach> Reaches;
            /// <summary>The reaches kept as head streams (null without any).</summary>
            public bool[] HeadStreams;
            public int Raised, Heads;
            public double HeadKm;
        }

        /// <param name="streamMinQ">The network's smallest stream: a reach of at least that much is drawn on its own ("all").</param>
        public static Result Apply(List<RiverReach> reaches, RiverBuildOptions options, double streamMinQ, string streamMode)
        {
            var res = new Result { Reaches = reaches };
            if (options.KeepAll || (!options.RiversToTheirEnd && !(options.HeadStreamKm > 0))) return res;
            int nr = reaches.Count;
            var list = new List<RiverReach>(reaches);
            res.Reaches = list;
            double minQ = options.MinQ;
            RiverReach Own(int r)
            {
                var re = list[r];
                if (!ReferenceEquals(re, reaches[r])) return re;
                var c = new RiverReach { Q = re.Q, Next = re.Next, Lon = re.Lon, Lat = re.Lat, Level = re.Level, Fixed = re.Fixed, Width = re.Width };
                list[r] = c;
                return c;
            }
            if (options.RiversToTheirEnd)
            {
                // reaches are in flow order (each flows into a later one): one pass carries "a river above" down every course
                var riverAbove = new bool[nr];
                for (int r = 0; r < nr; r++)
                {
                    int nx = list[r].Next;
                    if (nx < 0 || nx >= nr) continue;
                    if (list[r].Q >= minQ || riverAbove[r]) riverAbove[nx] = true;
                }
                for (int r = 0; r < nr; r++)
                {
                    if (!riverAbove[r] || list[r].Q >= minQ) continue;
                    Own(r).Q = (float)minQ;
                    res.Raised++;
                }
            }
            if (options.HeadStreamKm > 0)
            {
                // what the network keeps on its own ("all": every reach of the smallest stream or more; "ends" and "none" keep
                // no stream that matters here)
                bool Kept(int r) => list[r].Q >= minQ || (streamMode == "all" && list[r].Q >= streamMinQ);
                var bigIn = new int[nr];
                Array.Fill(bigIn, -1);
                for (int r = 0; r < nr; r++)
                {
                    int nx = list[r].Next;
                    if (nx < 0 || nx >= nr) continue;
                    if (bigIn[nx] < 0 || list[r].Q > list[bigIn[nx]].Q) bigIn[nx] = r;
                }
                var heads = new bool[nr];
                for (int r = 0; r < nr; r++)
                {
                    if (list[r].Q < minQ) continue;
                    int u = bigIn[r];
                    if (u < 0 || Kept(u)) continue;
                    res.Heads++;
                    double left = options.HeadStreamKm;
                    for (int guard = 0; u >= 0 && left > 0 && !Kept(u) && !heads[u] && guard < nr; guard++)
                    {
                        double len = LengthKm(list[u]);
                        heads[u] = true;
                        if (len > left)
                        {
                            Cut(Own(u), left);
                            res.HeadKm += left;
                            break;
                        }
                        res.HeadKm += len;
                        left -= len;
                        u = bigIn[u];
                    }
                }
                res.HeadStreams = heads;
            }
            return res;
        }

        /// <summary>A reach's length along its vertices (km).</summary>
        internal static double LengthKm(RiverReach re)
        {
            double km = 0;
            for (int k = 1; k < re.Lat.Length; k++) km += StepKm(re, k);
            return km;
        }

        static double StepKm(RiverReach re, int k)
        {
            double dla = (re.Lat[k] - re.Lat[k - 1]) * Geo.MetresPerDegree / 1000;
            double dlo = Geo.Wrap180(re.Lon[k] - re.Lon[k - 1]) * Geo.MetresPerDegree / 1000 * Math.Cos(re.Lat[k] * Math.PI / 180);
            return Math.Sqrt(dla * dla + dlo * dlo);
        }

        /// <summary>Keeps the reach's last <paramref name="km"/> (from the first vertex at least that far up from its end; at least
        /// two vertices).</summary>
        static void Cut(RiverReach re, double km)
        {
            int n = re.Lat.Length, k0 = n - 1;
            double got = 0;
            while (k0 > 0 && got < km) { got += StepKm(re, k0); k0--; }
            k0 = Math.Min(k0, n - 2);
            if (k0 <= 0) return;
            re.Lat = re.Lat[k0..]; re.Lon = re.Lon[k0..]; re.Level = re.Level[k0..]; re.Fixed = re.Fixed[k0..];
            if (re.Width != null && re.Width.Length == n) re.Width = re.Width[k0..];
        }
    }
}
