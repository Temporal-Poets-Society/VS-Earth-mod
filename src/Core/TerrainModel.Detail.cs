using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: the detail noise (rules 0 and 1, the sub-cell noise) and the summit excess.
    public sealed partial class TerrainModel
    {
        /// <summary>
        /// A detail noise at x (wrapped into the ring when RealSeam) that repeats around the ring: within SeamNoiseBlend of
        /// the jump line it blends into the same noise one ring further (or back), half and half at the line, so the two
        /// ends of the ring meet without a step. Elsewhere, and in worlds without RealSeam, it is the plain noise.
        /// </summary>
        double SeamNoise(Simplex2 n, double x, double z, double wavelength, int octaves, bool ridged = false)
        {
            double Plain(double at) => ridged ? n.Ridged(at, z, wavelength, octaves) : n.Fbm(at, z, wavelength, octaves);
            if (!RealSeam) return Plain(x);
            double ring = Geometry.RingLength, p = x - Geometry.RingStart, edge = Math.Min(p, ring - p);
            if (edge >= SeamNoiseBlend) return Plain(x);
            double own = 0.5 + 0.5 * Geo.SmoothStep(Math.Max(0, edge) / SeamNoiseBlend);
            return own * Plain(x) + (1 - own) * Plain(p < ring / 2 ? x + ring : x - ring);
        }

        /// <summary>Detail noise (m) for a column: gentle undulation everywhere, ridges and valleys where the land is rugged.</summary>
        double Detail(double x, double z, in EarthData.TerrainSample s)
        {
            if (DetailRules >= 1) return BuiltInDetail(x, z, s);
            double relief = Math.Min(s.Relief, 2500);
            double mountain = Geo.SmoothStep((relief - 150) / 1200);
            double baseAmp = 1.5 + 0.03 * Math.Max(0, s.Elev);
            double d = baseAmp * SeamNoise(n2, x, z, 56, 3);
            d += relief * 0.22 * SeamNoise(n1, x, z, 150, 4);
            if (mountain > 0) d += mountain * relief * 0.4 * (SeamNoise(n3, x, z, 260, 4, true) - 0.45);
            return d * DetailScale;
        }

        double SummitExcess(double lat, double lon, in EarthData.TerrainSample s)
            => Summits && s.Elev > 0 && s.Ocean <= 0 && (double.IsNaN(s.LakeLevel) || s.Lake <= 0)
                ? (DetailRules >= 1 ? Data.SummitExcessCubic(lat, lon) * Geo.SmoothStep((s.ReliefSmooth - 60) / 300)
                    : Data.SummitExcess(lat, lon) * Geo.SmoothStep((s.Relief - 60) / 300)) : 0;

        /// <summary>The built-in grid's cell north-south (2', m): the detail noise of rules 1 stays below it.</summary>
        public const double BuiltInCellMetres = 3706;

        /// <summary>Metres per block (north-south; across too, as Cylinder World keeps the Earth's proportions).</summary>
        public double MetresPerBlock => Geometry != null && Geometry.BlocksPerDegreeZ > 0 ? Geo.MetresPerDegree / Geometry.BlocksPerDegreeZ : 100;

        /// <summary>
        /// Detail rules 1 on the built-in heights: a gentle undulation everywhere and, where the 2' cell is rugged, relief
        /// below the cell's size (ridges more than knolls in mountains). About half the first noise's spread (at the 2,500 m
        /// relief cap 76 m against the 144 m measured round Everest), so it doesn't put new mountains on the data's.
        /// </summary>
        double BuiltInDetail(double x, double z, in EarthData.TerrainSample s)
        {
            double relief = Math.Min(s.ReliefSmooth, 2500);
            double mountain = Geo.SmoothStep((relief - 150) / 1200);
            double sigma = 1.0 + 0.002 * Math.Max(0, s.Elev) + 0.03 * relief;
            return SubCellNoise(x, z, BuiltInCellMetres, sigma, mountain) * DetailScale;
        }

        /// <summary>
        /// 0053: noise (m) for relief finer than a data grid's cell: spread <paramref name="sigma"/> (standard deviation),
        /// wavelengths from half the cell down to 60 m or 3 blocks (whichever is longer), a share of ridges where
        /// <paramref name="ridges"/> (0..1). Turned octaves (Simplex2.FbmTurned); blended across the ring's jump line like
        /// SeamNoise.
        /// </summary>
        double SubCellNoise(double x, double z, double cellMetres, double sigma, double ridges)
        {
            if (sigma <= 0) return 0;
            double mpb = MetresPerBlock;
            double longest = 0.5 * cellMetres / mpb, shortest = Math.Max(3, 60 / mpb);
            if (longest < shortest) return 0;
            double a = 1 - 0.6 * ridges, b = 0.6 * ridges, norm = Math.Sqrt(a * a + b * b);
            double Plain(double at) => (a * n1.FbmTurned(at, z, longest, 6, shortest)
                + (b > 0 ? b * n3.RidgedTurned(at, z, longest, 6, shortest) : 0)) / norm;
            double v;
            if (!RealSeam) v = Plain(x);
            else
            {
                double ring = Geometry.RingLength, p = x - Geometry.RingStart, edge = Math.Min(p, ring - p);
                if (edge >= SeamNoiseBlend) v = Plain(x);
                else
                {
                    double own = 0.5 + 0.5 * Geo.SmoothStep(Math.Max(0, edge) / SeamNoiseBlend);
                    v = own * Plain(x) + (1 - own) * Plain(p < ring / 2 ? x + ring : x - ring);
                }
            }
            return sigma * v;
        }
    }
}
