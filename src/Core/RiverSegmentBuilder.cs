using System;
using static EarthMap.Core.RiverNetwork;

namespace EarthMap.Core
{
    /// <summary>
    /// The third stage of a river network's build: the pieces the queries look up, in reach order: each kept reach's course
    /// piece by piece (the last stretch of a river fading out in a dry basin in short pieces of falling discharge), then each
    /// river mouth's prolongation out to open water; and (channel rules 1) each reach's first and last piece. The pieces are
    /// counted first and written once into an array of that size (a list grown to them and copied out held both at once:
    /// about 0.6 GB more at 1:10).
    /// </summary>
    internal sealed class RiverSegmentBuilder
    {
        readonly RiverNetwork net;
        readonly EarthGeometry geo;
        readonly RiverCourses courses;
        readonly Pools[] pools;
        readonly bool[] fadeEnds;
        readonly bool geometrySeams;
        /// <summary>Per reach on the last stretch of a river ending in a dry basin: the fade's length and how far below this
        /// reach's end the river ends (null: no such river).</summary>
        (double Len, double Below)[] fadeAhead;

        /// <summary>The pieces, in reach order, the mouths' after all the courses'.</summary>
        public Seg[] Segs { get; }
        /// <summary>Per reach, whether it lies on a fading end (null where no river fades).</summary>
        public bool[] Fades { get; private set; }
        /// <summary>Channel rules 1, per reach: its first and last piece (mouths left out; -1: none); null before.</summary>
        public int[] SegFirst { get; }
        public int[] SegLast { get; }

        public RiverSegmentBuilder(RiverNetwork net, EarthGeometry geo, RiverCourses courses, Pools[] pools, bool[] fadeEnds, bool geometrySeams, int channelRules)
        {
            this.net = net;
            this.geo = geo;
            this.courses = courses;
            this.pools = pools;
            this.fadeEnds = fadeEnds;
            this.geometrySeams = geometrySeams;
            int nr = courses.Count;
            var keep = courses.Keep;
            if (fadeEnds != null) FadingEnds();
            int count = 0;
            for (int r = 0; r < nr; r++) if (keep[r]) count = CoursePieces(r, null, count);
            for (int r = 0; r < nr; r++) if (keep[r] && Mouth(r, out _)) count++;
            var segs = new Seg[count];
            int at = 0;
            for (int r = 0; r < nr; r++) if (keep[r]) at = CoursePieces(r, segs, at);
            for (int r = 0; r < nr; r++) if (keep[r] && Mouth(r, out var mouth)) segs[at++] = mouth;
            if (at != count) throw new InvalidOperationException($"river pieces: {at} written, {count} counted");
            Segs = segs;
            // channel rules 1: each reach's first and last piece (mouths left out), for the flow's direction across vertices
            if (channelRules >= 1)
            {
                SegFirst = new int[nr];
                SegLast = new int[nr];
                Array.Fill(SegFirst, -1);
                Array.Fill(SegLast, -1);
                for (int i = 0; i < segs.Length; i++)
                {
                    if (segs[i].Mouth) continue;
                    int r = segs[i].Reach;
                    if (SegFirst[r] < 0) SegFirst[r] = i;
                    SegLast[r] = i;
                }
            }
        }

        /// <summary>A river ending in a dry basin (no lake, no sea: a wadi in the sand) narrows and shallows to a stream over its
        /// last stretch (FadeLength, up its main course) instead of ending at full width: the reaches on that stretch.</summary>
        void FadingEnds()
        {
            var reaches = courses.Reaches;
            int nr = courses.Count;
            for (int r = 0; r < nr; r++)
            {
                if (!courses.Keep[r] || r >= fadeEnds.Length || !fadeEnds[r] || pools[r].Next >= 0 || reaches[r].Q <= net.StreamMinQ) continue;
                fadeAhead ??= new (double, double)[nr];
                double len = net.FadeLength(reaches[r].Q), below = 0;
                for (int cur = r, guard = 0; cur >= 0 && below < len && guard < 10000; guard++)
                {
                    fadeAhead[cur] = (len, below);
                    below += pools[cur].Len;
                    cur = pools[cur].MainIn;
                }
            }
            if (fadeAhead != null)
            {
                Fades = new bool[nr];
                for (int r = 0; r < nr; r++) Fades[r] = fadeAhead[r].Len > 0;
            }
        }

        /// <summary>A kept reach's course as pieces, written from at on (into null: only counted); the next index. A piece
        /// across the seam is left out; in a fading stretch the pieces are at most a few blocks long, each with its own
        /// (smaller) discharge.</summary>
        int CoursePieces(int r, Seg[] into, int at)
        {
            var re = courses.Reaches[r];
            var xs = courses.X[r];
            var zs = courses.Z[r];
            var lens = courses.Lengths[r];
            double fadeFrom = double.MaxValue, fadeLen = 0;
            if (fadeAhead != null && fadeAhead[r].Len > 0)
            {
                fadeLen = fadeAhead[r].Len;
                fadeFrom = pools[r].Len + fadeAhead[r].Below - fadeLen;
            }
            double s0 = 0;
            var m = courses.MeasuredHalfWidth?[r];
            for (int k = 1; k < xs.Length; k++)
            {
                double x1 = xs[k - 1], z1 = zs[k - 1], x2 = xs[k], z2 = zs[k];
                double len = lens[k - 1];
                if (geometrySeams ? !geo.AcrossSeam(x1, x2) : Math.Abs(x2 - x1) <= 4000 && Math.Abs(z2 - z1) <= 4000)
                {
                    int pieces = s0 + len > fadeFrom && fadeLen > 0 ? Math.Max(1, (int)Math.Ceiling(len / (4 * net.SizeScale))) : 1;
                    if (into == null) at += pieces;
                    else for (int i = 0; i < pieces; i++)
                    {
                        double t0 = (double)i / pieces, t1 = (double)(i + 1) / pieces;
                        double q = re.Q;
                        if (pieces > 1 || s0 + len > fadeFrom)
                        {
                            double mid = s0 + 0.5 * (t0 + t1) * len;
                            if (mid > fadeFrom) q = net.FadedQ(re.Q, Math.Clamp((mid - fadeFrom) / fadeLen, 0, 1));
                        }
                        into[at++] = new Seg
                        {
                            X1 = (float)(x1 + (x2 - x1) * t0), Z1 = (float)(z1 + (z2 - z1) * t0),
                            X2 = (float)(x1 + (x2 - x1) * t1), Z2 = (float)(z1 + (z2 - z1) * t1),
                            Q = (float)q, Len = (float)(len / pieces), Reach = r, S0 = (float)(s0 + t0 * len),
                            M1 = m != null ? (float)RiverStraighten.WidthBetween(m[k - 1], m[k], t0) : 0,
                            M2 = m != null ? (float)RiverStraighten.WidthBetween(m[k - 1], m[k], t1) : 0,
                        };
                    }
                }
                s0 += len;
            }
            return at;
        }

        /// <summary>
        /// River mouths: HydroRIVERS lines can end a few km short of the coast in ETOPO (the Daugava stops ~15 blocks before
        /// the Gulf of Riga), so every river ending in the sea or a lake gets prolonged along its last direction (over its last
        /// 24 blocks or more); the terrain only lets it cut through low ground. False for a kept reach with no mouth: one
        /// flowing on, fading out in a dry basin, ending above sea level outside a lake, or too short or across the seam.
        /// </summary>
        bool Mouth(int r, out Seg mouth)
        {
            mouth = default;
            if (pools[r].Next >= 0) return false;
            if (fadeEnds != null && r < fadeEnds.Length && fadeEnds[r]) return false;
            var re = courses.Reaches[r];
            var xs = courses.X[r];
            var zs = courses.Z[r];
            var lens = courses.Lengths[r];
            int n = xs.Length;
            if (n < 2 || !(courses.Fixed[r][n - 1] || courses.Level[r][n - 1] <= 5)) return false; // into a lake, or at sea level
            int k = n - 2;
            double back = lens[k];
            while (k > 0 && back < 24) back += lens[--k];
            double ex = xs[n - 1], ez = zs[n - 1];
            double dx = ex - xs[k], dz = ez - zs[k], dl = Math.Sqrt(dx * dx + dz * dz);
            if (dl < 1 || (geometrySeams ? geo.AcrossSeam(xs[k], ex) : dl > 4000)) return false;
            double ext = 2 * net.HalfWidth(re.Q) + 40;
            var mw = courses.MeasuredHalfWidth;
            float mEnd = mw?[r] != null ? (float)mw[r][n - 1] : 0;
            mouth = new Seg
            {
                X1 = (float)ex, Z1 = (float)ez, X2 = (float)(ex + dx / dl * ext), Z2 = (float)(ez + dz / dl * ext), Q = re.Q, Len = (float)ext,
                Reach = r, S0 = (float)pools[r].Len, Mouth = true, M1 = mEnd, M2 = mEnd,
            };
            return true;
        }
    }
}
