using System;

namespace EarthMap.Core
{
    /// <summary>2D simplex noise (Gustavson's public-domain algorithm) with a seeded permutation. Stateless after construction.</summary>
    public sealed class Simplex2
    {
        readonly short[] perm = new short[512];
        static readonly double[] GradX = { 1, -1, 1, -1, 1, -1, 0, 0, 0.7071, -0.7071, 0.7071, -0.7071 };
        static readonly double[] GradY = { 1, 1, -1, -1, 0, 0, 1, -1, 0.7071, 0.7071, -0.7071, -0.7071 };
        const double F2 = 0.36602540378443865, G2 = 0.21132486540518713;

        public Simplex2(long seed)
        {
            var p = new short[256];
            for (short i = 0; i < 256; i++) p[i] = i;
            ulong s = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
            for (int i = 255; i > 0; i--)
            {
                s = s * 6364136223846793005UL + 1442695040888963407UL;
                int j = (int)((s >> 33) % (ulong)(i + 1));
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        /// <summary>About -1..1.</summary>
        public double Noise(double xin, double yin)
        {
            double s = (xin + yin) * F2;
            int i = (int)Math.Floor(xin + s), j = (int)Math.Floor(yin + s);
            double t = (i + j) * G2;
            double x0 = xin - (i - t), y0 = yin - (j - t);
            int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
            double x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
            double x2 = x0 - 1 + 2 * G2, y2 = y0 - 1 + 2 * G2;
            int ii = i & 255, jj = j & 255;
            double n = 0;
            double t0 = 0.5 - x0 * x0 - y0 * y0;
            if (t0 > 0) { int g = perm[ii + perm[jj]] % 12; t0 *= t0; n += t0 * t0 * (GradX[g] * x0 + GradY[g] * y0); }
            double t1 = 0.5 - x1 * x1 - y1 * y1;
            if (t1 > 0) { int g = perm[ii + i1 + perm[jj + j1]] % 12; t1 *= t1; n += t1 * t1 * (GradX[g] * x1 + GradY[g] * y1); }
            double t2 = 0.5 - x2 * x2 - y2 * y2;
            if (t2 > 0) { int g = perm[ii + 1 + perm[jj + 1]] % 12; t2 *= t2; n += t2 * t2 * (GradX[g] * x2 + GradY[g] * y2); }
            return 70 * n;
        }

        /// <summary>Fractal sum, normalised to about -1..1.</summary>
        public double Fbm(double x, double y, double wavelength, int octaves, double gain = 0.5, double lacunarity = 2)
        {
            double sum = 0, amp = 1, norm = 0, f = 1 / wavelength;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(x * f + o * 17.31, y * f - o * 9.17);
                norm += amp;
                amp *= gain;
                f *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal: sharp crests, 0..1.</summary>
        public double Ridged(double x, double y, double wavelength, int octaves)
        {
            double sum = 0, amp = 1, norm = 0, f = 1 / wavelength, weight = 1;
            for (int o = 0; o < octaves; o++)
            {
                double n = 1 - Math.Abs(Noise(x * f + o * 31.7, y * f + o * 11.3));
                n *= n * weight;
                weight = Math.Clamp(n * 1.5, 0, 1);
                sum += amp * n;
                norm += amp;
                amp *= 0.5;
                f *= 2.03;
            }
            return sum / norm;
        }

        // 0053: each octave turned by the golden angle from the one before (and the first off the axes), so no lattice
        // direction is shared between octaves; simplex's skewed lattice and diagonal gradients otherwise line up in all of
        // them and show as 45-degree grain on hillshades and maps.
        const int MaxTurned = 8;
        static readonly double[] TurnCos = new double[MaxTurned], TurnSin = new double[MaxTurned];
        static Simplex2()
        {
            for (int o = 0; o < MaxTurned; o++)
            {
                double a = 0.61 + o * 2.399963229728653;
                TurnCos[o] = Math.Cos(a);
                TurnSin[o] = Math.Sin(a);
            }
        }

        /// <summary>Standard deviation of <see cref="Noise"/> over the plane (measured on 4 million points, 0053).</summary>
        public const double NoiseStd = 0.44058;
        /// <summary>Mean and standard deviation of one ridge octave, (1 - |noise|)^2 (measured likewise).</summary>
        public const double RidgeMean = 0.44060, RidgeStd = 0.27735;

        double Turned(int o, double x, double y, double f, double dx, double dy)
            => Noise((TurnCos[o] * x - TurnSin[o] * y) * f + dx, (TurnSin[o] * x + TurnCos[o] * y) * f + dy);

        /// <summary>
        /// 0053: a fractal sum with turned octaves and unit standard deviation (about -3..3). Wavelengths halve from
        /// <paramref name="wavelength"/>; octaves shorter than <paramref name="minWavelength"/> are left out (0 without any).
        /// </summary>
        public double FbmTurned(double x, double y, double wavelength, int octaves, double minWavelength, double gain = 0.5)
        {
            double sum = 0, amp = 1, norm2 = 0, w = wavelength;
            for (int o = 0; o < octaves && o < MaxTurned && w >= minWavelength; o++)
            {
                sum += amp * Turned(o, x, y, 1 / w, o * 17.31, -o * 9.17);
                norm2 += amp * amp;
                amp *= gain;
                w *= 0.5;
            }
            return norm2 > 0 ? sum / (Math.Sqrt(norm2) * NoiseStd) : 0;
        }

        /// <summary>
        /// 0053: ridges (connected crests, not round knolls) with turned octaves, about zero mean and unit standard
        /// deviation. Octaves shorter than <paramref name="minWavelength"/> are left out.
        /// </summary>
        public double RidgedTurned(double x, double y, double wavelength, int octaves, double minWavelength)
        {
            double sum = 0, amp = 1, norm2 = 0, w = wavelength, weight = 1;
            for (int o = 0; o < octaves && o < MaxTurned && w >= minWavelength; o++)
            {
                double n = 1 - Math.Abs(Turned(o, x, y, 1 / w, o * 31.7, o * 11.3));
                n *= n;
                // each octave's crests are stronger where the octave above has its crests (as Ridged does)
                double v = (n - RidgeMean) / RidgeStd * weight;
                weight = Math.Clamp(0.5 + n, 0.5, 1.5);
                sum += amp * v;
                norm2 += amp * amp;
                amp *= 0.5;
                w *= 0.5;
            }
            return norm2 > 0 ? sum / Math.Sqrt(norm2) : 0;
        }
    }
}
