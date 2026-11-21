using System;
using System.Collections.Concurrent;

namespace EarthMap.Core
{
    // TerrainModel, part: lake rules 1's lake beds (decision 0100, EA-0184).
    public sealed partial class TerrainModel
    {
        /// <summary>Lake rules 1: each lake's real depth (lake-depths-1.bin, made for this world's lake outlines), or null.</summary>
        public LakeDepthData LakeDepths;

        /// <summary>Lake rules 1: the lowest block a lake's bed reaches, the deepest sea floor's (VerticalScale.Trench), so Baikal's
        /// 1,642 m stay well above the world's rock floor; below sea level the vertical scale is the sea's (logarithmic).</summary>
        int LakeFloorY => Math.Max(2, (int)Math.Ceiling(Vertical.Y(-VerticalScale.Trench)) + 1);

        /// <summary>
        /// Lake rules 1: the shape of a lake's bed. Its depth at a distance d from the shore is Dmax (1 - e^(-d/L)), with L half
        /// the lake's size (its area over its shore's length: about its half width) and Dmax its floor: steep from the shore,
        /// a broad floor in the middle. LakeDepths gives the floor that makes the lake's own mean depth over its own distances
        /// to the shore; a lake it doesn't list takes a mean by its area over this, the curve's mean across a lake as wide
        /// as its size.
        /// </summary>
        public const double LakeShapeMean = 0.5677;
        /// <summary>Lake rules 1: a lake the depth file doesn't list (smaller than 1 km², or not found in HydroLAKES) takes
        /// HydroLAKES' median mean depth for its area (m; under 1 km², 1-10, 10-100, from 100 km²).</summary>
        public static readonly double[] LakeAreaDepths = { 3.0, 4.2, 7.0, 12.9 };

        /// <summary>
        /// Lake rules 1 (decision 0100): the depth (m) of an outline lake's bed at a column, from the lake's floor (LakeDepths,
        /// else by its area) and the column's distance to the shore (<see cref="ShoreDistance"/>), steep from the shore and
        /// flat in the middle. Where ETOPO 2022 holds the lake's bed (Baikal, the Great Lakes) its bathymetry on the 2′ grid
        /// (<paramref name="gridDepth"/>, the whole of it) is the bed: the curve only steepens it from the shore, as the grid's
        /// slope did before lake rules 1, and never takes it deeper than the grid.
        /// </summary>
        double LakeDepthAt(double x, double z, in LakeOutlineLayer.Hit lk, double gridDepth)
        {
            double mpb = MetresPerBlock;
            var (floor, size, bathymetry) = LakeShape(in lk);
            double l = Math.Max(size / 2, mpb);
            double distBlocks = ShoreDistance(x, z, in lk);
            double depth = floor * (1 - Math.Exp(-distBlocks * mpb / l));
            if (bathymetry && gridDepth > 0)
            {
                double room = Math.Max(1e-3, 1 - lk.Dist / LakeOutlines.Reach);
                depth = Math.Min(gridDepth, Math.Max(depth, OutlineGridSlope * lk.Dist * mpb / room));
            }
            return depth;
        }

        /// <summary>Lake rules 1: a lake's floor depth (m), its size (m: area over shore length) and whether ETOPO holds its bed:
        /// LakeDepths' where it lists the lake, else HydroLAKES' median depth for its area (over LakeShapeMean) and a compact
        /// lake's size.</summary>
        (double Floor, double Size, bool Bathymetry) LakeShape(in LakeOutlineLayer.Hit lk)
        {
            if (LakeDepths != null && lk.TileKey1 > 0 && lk.Owner == LakeOutlines && LakeDepths.TryGet(lk.TileKey1 - 1, lk.LakeIndex, out var known))
                return (known.FloorMetres, known.SizeMetres, known.Bathymetry);
            // (a compact lake's area over its shore's length is a quarter of its diameter: 0.28 √A)
            double mpb = MetresPerBlock, areaM2 = LakeArea(lk) * mpb * mpb, km2 = areaM2 / 1e6;
            double mean = km2 < 1 ? LakeAreaDepths[0] : km2 < 10 ? LakeAreaDepths[1] : km2 < 100 ? LakeAreaDepths[2] : LakeAreaDepths[3];
            return (mean / LakeShapeMean, 0.28 * Math.Sqrt(Math.Max(areaM2, 1)), false);
        }

        /// <summary>
        /// Lake rules 1: a column's distance (blocks) to its lake's shore. Within the outlines' reach their own distance; beyond
        /// it, where the outlines stop measuring, traced: on a lattice of points a reach apart, each the shortest of 16 rays
        /// stepping out by the outlines' distance (never past the shore), up to 5 times its own lake's L (the depth no longer
        /// changes past it), cached per world; the column takes its four lattice points' blend, at least the reach. Each
        /// lattice point's value depends on its own place alone, so a world generates the same bed in any order.
        /// </summary>
        double ShoreDistance(double x, double z, in LakeOutlineLayer.Hit lk)
        {
            double reach = LakeOutlines.Reach;
            if (lk.Dist < reach - 0.5) return lk.Dist;
            double s = reach;
            double fx = x / s, fz = z / s;
            long ix = (long)Math.Floor(fx), iz = (long)Math.Floor(fz);
            double tx = fx - ix, tz = fz - iz;
            double d00 = LatticeShore(ix, iz, s), d10 = LatticeShore(ix + 1, iz, s);
            double d01 = LatticeShore(ix, iz + 1, s), d11 = LatticeShore(ix + 1, iz + 1, s);
            double d = (d00 * (1 - tx) + d10 * tx) * (1 - tz) + (d01 * (1 - tx) + d11 * tx) * tz;
            return Math.Max(reach, d);
        }

        readonly ConcurrentDictionary<(long, long), float> shoreLattice = new();
        static readonly double[] RayX = Rays(true), RayZ = Rays(false);
        static double[] Rays(bool x)
        {
            var r = new double[16];
            for (int k = 0; k < 16; k++) r[k] = x ? Math.Cos(k * Math.PI / 8) : Math.Sin(k * Math.PI / 8);
            return r;
        }

        /// <summary>The distance (blocks) from lattice point (ix, iz) to its lake's shore, at most 5 times its lake's L and at
        /// least twice the reach (0 outside every lake).</summary>
        float LatticeShore(long ix, long iz, double s)
        {
            if (shoreLattice.TryGetValue((ix, iz), out float v)) return v;
            double px = ix * s, pz = iz * s;
            var g = Geometry.ToGeo(px, pz);
            float result = 0;
            if (!g.InGutter)
            {
                var h0 = LakeOutlines.At(g.Lat, g.Lon);
                if (h0.Inside)
                {
                    double mpb = MetresPerBlock;
                    double best = Math.Max(5 * Math.Max(LakeShape(in h0).Size / 2, mpb) / mpb, 2 * LakeOutlines.Reach);
                    for (int k = 0; k < 16 && best > h0.Dist; k++)
                    {
                        double t = 0;
                        for (int step = 0; step < 4096 && t < best; step++)
                        {
                            var q = Geometry.ToGeo(px + RayX[k] * t, pz + RayZ[k] * t);
                            if (q.InGutter) break;
                            var h = LakeOutlines.At(q.Lat, q.Lon);
                            if (!h.Inside || Math.Abs(h.Level - h0.Level) > 0.05) break;
                            if (h.Dist < 1) { t += h.Dist; break; }
                            t += h.Dist;
                        }
                        best = Math.Min(best, t);
                    }
                    result = (float)best;
                }
            }
            if (shoreLattice.Count > 4_000_000) shoreLattice.Clear();
            shoreLattice[(ix, iz)] = result;
            return result;
        }
    }
}
