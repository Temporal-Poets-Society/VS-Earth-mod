using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Hashing, smooth value noise and the normal distribution for the weather. Pure and deterministic, so the server
    /// and every client get the same numbers from the same key. The lattice can repeat exactly along x (Cylinder World's
    /// ring). The hash and value noise are Sea's (Sea 0.3, src/Wind/Noise.cs), which moved here with the weather.
    /// <para>Earth has other hashes and value noises (BotanicalRegions' border noise, GrassWinter's, WeatherModel's two-octave
    /// field) that look alike but are not interchangeable: saved worlds and the weather's tests fix their numbers, so they
    /// stay as they are. Only the content fingerprints share <see cref="Fnv1a(ReadOnlySpan{byte})"/>.</para>
    /// </summary>
    public static class WeatherNoise
    {
        const ulong FnvOffset = 14695981039346656037UL, FnvPrime = 1099511628211UL;

        /// <summary>64-bit FNV-1a of bytes (weather.bin's signature).</summary>
        public static ulong Fnv1a(ReadOnlySpan<byte> data)
        {
            unchecked
            {
                ulong h = FnvOffset;
                for (int i = 0; i < data.Length; i++) h = (h ^ data[i]) * FnvPrime;
                return h;
            }
        }

        /// <summary>64-bit FNV-1a of a string's UTF-16 code units (the botanical regions file's content key).</summary>
        public static ulong Fnv1a(string s)
        {
            unchecked
            {
                ulong h = FnvOffset;
                foreach (char c in s) { h ^= c; h *= FnvPrime; }
                return h;
            }
        }

        /// <summary>splitmix64 finalizer.</summary>
        public static ulong Mix(ulong a)
        {
            a += 0x9E3779B97F4A7C15UL;
            a = (a ^ (a >> 30)) * 0xBF58476D1CE4E5B9UL;
            a = (a ^ (a >> 27)) * 0x94D049BB133111EBUL;
            return a ^ (a >> 31);
        }

        public static ulong Hash(long key, long a, long b, long c) => Mix((ulong)key ^ Mix((ulong)a ^ Mix((ulong)b ^ Mix((ulong)c))));

        /// <summary>0 ≤ u &lt; 1 from a hash.</summary>
        public static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));

        /// <summary>The n-th uniform number (0..1) of a hashed stream.</summary>
        public static double Unit(ulong h, int n) => Unit(Mix(h + (ulong)n * 0x632BE59BD9B4E019UL));

        public static long Wrap(long i, long period) => period > 0 ? ((i % period) + period) % period : i;

        static double Corner(long key, long ix, long iz, long it) => Unit(Hash(key, ix, iz, it)) * 2 - 1;

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        /// <summary>Value noise (-1..1) at lattice coordinates. With periodX &gt; 0 the lattice repeats every periodX cells along x.</summary>
        public static double Value(long key, double x, double z, double t, long periodX)
        {
            double fx0 = Math.Floor(x), fz0 = Math.Floor(z), ft0 = Math.Floor(t);
            long x0 = (long)fx0, z0 = (long)fz0, t0 = (long)ft0;
            double u = Fade(x - fx0), v = Fade(z - fz0), w = Fade(t - ft0);
            long xa = Wrap(x0, periodX), xb = Wrap(x0 + 1, periodX);

            double c000 = Corner(key, xa, z0, t0), c100 = Corner(key, xb, z0, t0);
            double c010 = Corner(key, xa, z0 + 1, t0), c110 = Corner(key, xb, z0 + 1, t0);
            double c001 = Corner(key, xa, z0, t0 + 1), c101 = Corner(key, xb, z0, t0 + 1);
            double c011 = Corner(key, xa, z0 + 1, t0 + 1), c111 = Corner(key, xb, z0 + 1, t0 + 1);

            double a0 = Lerp(Lerp(c000, c100, u), Lerp(c010, c110, u), v);
            double a1 = Lerp(Lerp(c001, c101, u), Lerp(c011, c111, u), v);
            return Lerp(a0, a1, w);
        }

        /// <summary>Two octaves of <see cref="Value"/>, normalised back to about -1..1.</summary>
        public static double Fbm(long key, double x, double z, double t, long periodX)
        {
            double a = Value(key, x, z, t, periodX);
            double b = Value(key + 7919, x * 2 + 0.37, z * 2 + 0.61, t * 2 + 0.13, periodX * 2);
            return (a + 0.5 * b) / 1.5;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double SmoothStep(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>0 below a, rising to 1 at b, 1 until c, falling to 0 at d.</summary>
        public static double Band(double x, double a, double b, double c, double d) => SmoothStep(a, b, x) * (1 - SmoothStep(c, d, x));

        // ------------------------------------------------------------------------------------------ normal distribution

        /// <summary>Standard normal density.</summary>
        public static double Pdf(double x) => PortableMath.Exp(-0.5 * x * x) * 0.3989422804014327;

        /// <summary>Standard normal distribution function (Abramowitz-Stegun 7.1.26 on erf; error under 1.5e-7).</summary>
        public static double Cdf(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2);
            double t = 1 / (1 + 0.3275911 * z);
            double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * PortableMath.Exp(-z * z);
            return x >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
        }

        /// <summary>Inverse of <see cref="Cdf"/> (Acklam's rational approximation, relative error under 1.2e-9).</summary>
        public static double InverseCdf(double p)
        {
            p = Math.Clamp(p, 1e-12, 1 - 1e-12);
            const double plow = 0.02425, phigh = 1 - plow;
            if (p < plow)
            {
                double q = Math.Sqrt(-2 * PortableMath.Log(p));
                return (((((-7.784894002430293e-03 * q - 3.223964580411365e-01) * q - 2.400758277161838e+00) * q - 2.549732539343734e+00) * q + 4.374664141464968e+00) * q + 2.938163982698783e+00)
                    / ((((7.784695709041462e-03 * q + 3.224671290700398e-01) * q + 2.445134137142996e+00) * q + 3.754408661907416e+00) * q + 1);
            }
            if (p > phigh)
            {
                double q = Math.Sqrt(-2 * PortableMath.Log(1 - p));
                return -(((((-7.784894002430293e-03 * q - 3.223964580411365e-01) * q - 2.400758277161838e+00) * q - 2.549732539343734e+00) * q + 4.374664141464968e+00) * q + 2.938163982698783e+00)
                    / ((((7.784695709041462e-03 * q + 3.224671290700398e-01) * q + 2.445134137142996e+00) * q + 3.754408661907416e+00) * q + 1);
            }
            {
                double q = p - 0.5, r = q * q;
                return (((((-3.969683028665376e+01 * r + 2.209460984245205e+02) * r - 2.759285104469687e+02) * r + 1.383577518672690e+02) * r - 3.066479806614716e+01) * r + 2.506628277459239e+00) * q
                    / (((((-5.447609879822406e+01 * r + 1.615858368580409e+02) * r - 1.556989798598866e+02) * r + 6.680131188771972e+01) * r - 1.328068155288572e+01) * r + 1);
            }
        }

        /// <summary>
        /// A lookup from a noise field's values to a standard normal variable with the same rank (its empirical
        /// distribution, measured once from a fixed set of samples, so every side builds the same table).
        /// </summary>
        public sealed class Gaussianizer
        {
            const int Bins = 2048;
            readonly double[] table = new double[Bins + 1];

            /// <param name="field">The field (key, x, z, t) whose values to map, about -1..1.</param>
            public Gaussianizer(Func<long, double, double, double, double> field)
            {
                const int n = 60000;
                var v = new double[n];
                for (int i = 0; i < n; i++)
                {
                    ulong h = Hash(0x6A55, i, 17, 3);
                    v[i] = field(0x51F1 + (i & 7), Unit(h, 0) * 1000, Unit(h, 1) * 1000, Unit(h, 2) * 1000);
                }
                Array.Sort(v);
                int k = 0;
                for (int b = 0; b <= Bins; b++)
                {
                    double x = -1 + 2.0 * b / Bins;
                    while (k < n && v[k] <= x) k++;
                    table[b] = InverseCdf((k + 0.5) / (n + 1.0));
                }
            }

            public double Map(double fbm)
            {
                double f = (Math.Clamp(fbm, -1, 1) + 1) * 0.5 * Bins;
                int i = Math.Min((int)f, Bins - 1);
                double t = f - i;
                return table[i] + (table[i + 1] - table[i]) * t;
            }
        }
    }
}
