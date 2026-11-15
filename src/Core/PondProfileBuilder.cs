using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// Channel rules 5 (decision 0074): how far the water runs out from a reach's centre line on either side, and the
    /// river's one width there. Every PondStep blocks along the reach, a line out along the normal on each side from the
    /// channel's edge: how far the mask's river water goes before land (Run; over an island to water beyond). The water
    /// goes on (Open) where that line or one at 45° up or down the river reaches an outline lake, the sea or PondOpen half
    /// widths, or where the water half way out runs on along the river for PondShort widths (a side arm's mouth, a long
    /// widening). One is the run's running median along the reach over PondShort of the reach's widths either way, eased
    /// by a Gaussian of half a width and raised in a bell a width wide where the water goes on: it follows a widening
    /// longer than PondShort widths and not a shorter one. Only for rivers whose water is at most PondMaxMetres across (a
    /// few of the mask's 90 m pixels); null for wider ones. Built once per reach when a column first asks
    /// (TerrainModel.PondProfileOf; a pure function of the reach and the outlines, so threads that race build the same).
    /// </summary>
    internal sealed class PondProfile
    {
        public double Step;
        /// <summary>Per side (0: left of the flow, 1: right) and bin: the run straight out (blocks, at most the far
        /// limit), whether the water goes on (straight out or at 45°), and the one width.</summary>
        public float[][] Runs, One;
        public bool[][] Open;
        /// <summary>How far past a run's end water still counts as the same water (blocks): half the mask's 90 m pixel, at
        /// least 1.5 (the outlines' corners round a pixel off; a gap narrower than that is no land of the data's).</summary>
        public double Touch;

        /// <summary>At a position along the reach on a side: the run (the most of the bins at and beside it) and the one
        /// width; false where the water goes on there.</summary>
        public bool At(double along, int side, out double runs, out double one)
        {
            var r = Runs[side];
            var o = Open[side];
            int k = Math.Clamp((int)Math.Round(along / Step), 0, r.Length - 1);
            runs = Math.Max(r[k], Math.Max(r[Math.Max(0, k - 1)], r[Math.Min(r.Length - 1, k + 1)]));
            one = One[side][k];
            return !(o[k] || o[Math.Max(0, k - 1)] || o[Math.Min(r.Length - 1, k + 1)]);
        }
    }

    /// <summary>
    /// Channel rules 5 (decision 0074): builds a reach's pond profile from the river network and the water mask's river
    /// outlines, lake outlines and coast (EA-0089, the review's S-1 step 3: moved out of TerrainModel unchanged; no column
    /// state). TerrainModel.PondProfileOf keeps one per reach.
    /// </summary>
    internal static class PondProfileBuilder
    {
        /// <summary>The model's layers a profile reads.</summary>
        public readonly struct Layers
        {
            public readonly RiverNetwork Rivers;
            public readonly LakeOutlineLayer RiverOutlines, LakeOutlines;
            public readonly CoastLayer Coasts;
            public readonly EarthGeometry Geometry;

            public Layers(RiverNetwork rivers, LakeOutlineLayer riverOutlines, LakeOutlineLayer lakeOutlines, CoastLayer coasts, EarthGeometry geometry)
            {
                Rivers = rivers; RiverOutlines = riverOutlines; LakeOutlines = lakeOutlines; Coasts = coasts; Geometry = geometry;
            }
        }

        public static PondProfile Build(in Layers m, int reach)
        {
            var pts = new List<(double X, double Z, double NX, double NZ, double Along, double Hw)>();
            double flowHw = m.Rivers.ReachSamples(reach, TerrainModel.PondStep, (px, pz, nx, nz, al, hw) => pts.Add((px, pz, nx, nz, al, hw)));
            if (pts.Count == 0 || flowHw <= 0) return null;
            // the reach's own width: the median of its half widths (as HitAt has them, the mask's averaged measure)
            var hws = new double[pts.Count];
            for (int i = 0; i < hws.Length; i++) hws[i] = pts[i].Hw;
            Array.Sort(hws);
            double width = 2 * Math.Max(hws[hws.Length / 2], 1);
            double maxAlong = 0;
            foreach (var q in pts) maxAlong = Math.Max(maxAlong, q.Along);
            int nb = (int)Math.Ceiling(maxAlong / TerrainModel.PondStep) + 1;
            var runs = new[] { new float[nb], new float[nb] };
            var open = new[] { new bool[nb], new bool[nb] };
            var cap = new float[nb];
            var seen = new bool[nb];
            const double diag = 0.70710678118654752;
            foreach (var q in pts)
            {
                int k = Math.Clamp((int)Math.Round(q.Along / TerrainModel.PondStep), 0, nb - 1);
                double outTo = TerrainModel.PondOpen * Math.Max(q.Hw, 0.5 * width);
                cap[k] = (float)Math.Max(cap[k], outTo);
                for (int side = 0; side < 2; side++)
                {
                    double sx = side == 0 ? q.NX : -q.NX, sz = side == 0 ? q.NZ : -q.NZ;
                    // straight out (the run), and 45° up and down the river (water going on that way: a side arm leaving at a
                    // slant, a backwater)
                    double run = Run(m, q.X, q.Z, sx, sz, q.Hw, outTo);
                    bool goesOn = double.IsPositiveInfinity(run)
                        || double.IsPositiveInfinity(Run(m, q.X, q.Z, diag * (sx - q.NZ), diag * (sz + q.NX), q.Hw / diag, outTo))
                        || double.IsPositiveInfinity(Run(m, q.X, q.Z, diag * (sx + q.NZ), diag * (sz - q.NX), q.Hw / diag, outTo));
                    // and along the river from half way out through the water beyond the channel: water going on further
                    // than PondShort widths that way is a side arm's mouth or a long widening, not a pond
                    if (!goesOn && run > q.Hw + 2)
                    {
                        double mid = 0.5 * (q.Hw + run), mx = q.X + sx * mid, mz = q.Z + sz * mid, along = TerrainModel.PondShort * width;
                        goesOn = Along(m, mx, mz, -q.NZ, q.NX, along) || Along(m, mx, mz, q.NZ, -q.NX, along);
                    }
                    float rv = (float)Math.Min(run, outTo);
                    if (!seen[k] || rv > runs[side][k]) runs[side][k] = rv;
                    open[side][k] |= goesOn;
                }
                seen[k] = true;
            }
            // bins no sample fell in take the one before (or after, at the start)
            int first = Array.IndexOf(seen, true);
            if (first < 0) return null;
            for (int k = 0; k < nb; k++)
            {
                if (seen[k]) continue;
                int from = k < first ? first : k - 1;
                runs[0][k] = runs[0][from]; runs[1][k] = runs[1][from]; cap[k] = cap[from];
                open[0][k] = open[0][from]; open[1][k] = open[1][from];
            }
            // only where the mask draws the river a few of its 90 m pixels wide (the median water width across, both runs, at
            // most PondMaxMetres): there a pixel more or less is half the width again. Wider rivers are the mask's as they are
            var across = new float[nb];
            for (int k = 0; k < nb; k++) across[k] = runs[0][k] + runs[1][k];
            Array.Sort(across);
            if (across[nb / 2] * Geo.MetresPerDegree / m.Geometry.BlocksPerDegreeZ > TerrainModel.PondMaxMetres) return null;
            // the running median over PondShort widths either way (a widening shorter than that is gone, a longer one keeps its
            // shape), then a Gaussian of half a width (no corners)
            int half = Math.Max(1, (int)Math.Round(TerrainModel.PondShort * width / TerrainModel.PondStep));
            double sigma = 0.5 * width / TerrainModel.PondStep, inv = 1 / (2 * sigma * sigma);
            int far = (int)Math.Ceiling(3 * sigma);
            var one = new[] { new float[nb], new float[nb] };
            var win = new List<float>();
            var med = new float[nb];
            for (int side = 0; side < 2; side++)
            {
                for (int k = 0; k < nb; k++)
                {
                    win.Clear();
                    for (int j = Math.Max(0, k - half); j <= Math.Min(nb - 1, k + half); j++) win.Add(runs[side][j]);
                    win.Sort();
                    med[k] = win[win.Count / 2];
                }
                for (int k = 0; k < nb; k++)
                {
                    double sum = 0, ws = 0;
                    for (int j = Math.Max(0, k - far); j <= Math.Min(nb - 1, k + far); j++)
                    {
                        double w = Math.Exp(-(j - k) * (j - k) * inv);
                        sum += w * med[j];
                        ws += w;
                    }
                    one[side][k] = (float)(sum / ws);
                }
                // where the water goes on (a side arm's or a backwater's mouth, wide water), the one width rises to the far
                // limit in a bell a width wide, so the shore beside it eases out instead of ending straight across
                double so = width / TerrainModel.PondStep, invO = 1 / (2 * so * so);
                int farO = (int)Math.Ceiling(3 * so);
                for (int k = 0; k < nb; k++)
                {
                    if (!open[side][k]) continue;
                    for (int j = Math.Max(0, k - farO); j <= Math.Min(nb - 1, k + farO); j++)
                        one[side][j] = (float)Math.Max(one[side][j], cap[k] * Math.Exp(-(j - k) * (j - k) * invO));
                }
            }
            return new PondProfile { Step = TerrainModel.PondStep, Runs = runs, Open = open, One = one,
                Touch = Math.Max(1.5, 0.5 * TerrainModel.MaskPixelMetres * m.Geometry.BlocksPerDegreeZ / Geo.MetresPerDegree) };
        }

        /// <summary>Channel rules 5: how far (blocks) from (x, z) along (ux, uz) the mask's river water goes from beyond the
        /// channel's edge (from) before land; over land narrower than PondIsland half widths (at least PondIslandMin blocks)
        /// with the river's water beyond (an island, a strip between arms) it goes on; +inf where it goes into an outline lake,
        /// the sea, or past far.</summary>
        static double Run(in Layers m, double x, double z, double ux, double uz, double from, double far)
        {
            double gap = Math.Max(TerrainModel.PondIslandMin, TerrainModel.PondIsland * from);
            for (double d = Math.Max(1, Math.Floor(from) + 1); d <= far; d += 1)
            {
                double px = x + ux * d, pz = z + uz * d;
                if (InMaskRiver(m, px, pz)) continue;
                if (!OnLand(m, px, pz)) return double.PositiveInfinity;
                double on = 0;
                for (double j = d + 1; j <= d + gap && j <= far; j += 1)
                    if (InMaskRiver(m, x + ux * j, z + uz * j)) { on = j; break; }
                if (on == 0) return d - 1;
                d = on;
            }
            return double.PositiveInfinity;
        }

        /// <summary>Channel rules 5: whether the mask's river water goes on from (x, z) along (ux, uz) for len blocks.</summary>
        static bool Along(in Layers m, double x, double z, double ux, double uz, double len)
        {
            for (double d = 1; d <= len; d += 1)
                if (!InMaskRiver(m, x + ux * d, z + uz * d)) return false;
            return true;
        }

        /// <summary>Whether the mask's river outlines have water at (x, z).</summary>
        static bool InMaskRiver(in Layers m, double x, double z)
        {
            var g = m.Geometry.ToGeo(x, z);
            if (g.InGutter) return false;
            var rv = m.RiverOutlines.At(g.Lat, g.Lon);
            return rv.Covered && rv.Inside && !double.IsNaN(rv.Level);
        }

        /// <summary>Not in an outline lake and not at sea (by the coast outlines where they have data).</summary>
        static bool OnLand(in Layers m, double x, double z)
        {
            var g = m.Geometry.ToGeo(x, z);
            if (g.InGutter) return false;
            if (m.LakeOutlines != null)
            {
                var lk = m.LakeOutlines.At(g.Lat, g.Lon);
                if (lk.Covered && lk.Inside) return false;
            }
            double sd = m.Coasts?.SignedDistance(g.Lat, g.Lon) ?? double.NaN;
            return !(sd < 0);
        }
    }
}
