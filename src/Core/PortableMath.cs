using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Exp, Log, Pow, Sin, Cos and Atan2 that give the same bits on every operating system (EA-0029). .NET's
    /// <see cref="Math"/> functions call the C library of the system they run on (Windows' UCRT, glibc on Linux, Apple's
    /// libm), and those round differently in the last bit: measured 13 November 2026, Earth's weather at 40,000 places and
    /// hours had other bits on Ubuntu than on Windows with the same .NET 8. The weather is computed separately by the server
    /// and every client (decision 0017), so a Linux server with Windows clients could put a rain edge or a thunderstorm a
    /// hair apart. These use only +, −, ×, ÷, <see cref="Math.Sqrt"/>, <see cref="Math.Floor"/> and
    /// <see cref="Math.ScaleB"/>, which IEEE 754 defines exactly and .NET does not fuse, so they are the same everywhere.
    ///
    /// Algorithms and constants from fdlibm 5.3 (Sun Microsystems, 1993; "Permission to use, copy, modify, and distribute
    /// this software is freely granted, provided that this notice is preserved"), as in FreeBSD's msun: e_exp.c, e_log.c,
    /// k_sin.c, k_cos.c, s_atan.c. Exp and Log are within an ulp of the true value; Pow is exp(y·log x), within a few ulps
    /// for the weather's arguments; Sin and Cos reduce by a three-part π/2, exact enough for |x| below 2^19.
    /// </summary>
    public static class PortableMath
    {
        // ------------------------------------------------------------------------------------------ exp (e_exp.c)

        const double ExpOverflow = 7.09782712893383973096e+02, ExpUnderflow = -7.45133219101941108420e+02;
        const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10, InvLn2 = 1.44269504088896338700e+00;
        const double P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03, P3 = 6.61375632143793436117e-05,
            P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08;

        public static double Exp(double x)
        {
            if (double.IsNaN(x)) return x;
            if (x > ExpOverflow) return double.PositiveInfinity;
            if (x < ExpUnderflow) return 0;
            double ax = Math.Abs(x), hi = 0, lo = 0;
            int k = 0;
            if (ax > 0.5 * 6.93147180559945286227e-01)
            {
                if (ax < 1.5 * 6.93147180559945286227e-01)
                {
                    if (x >= 0) { hi = x - Ln2Hi; lo = Ln2Lo; k = 1; }
                    else { hi = x + Ln2Hi; lo = -Ln2Lo; k = -1; }
                }
                else
                {
                    k = (int)(InvLn2 * x + (x >= 0 ? 0.5 : -0.5));
                    double t = k;
                    hi = x - t * Ln2Hi;
                    lo = t * Ln2Lo;
                }
                x = hi - lo;
            }
            else if (ax < 3.725290298461914e-09) return 1 + x;   // 2^-28
            double z = x * x;
            double c = x - z * (P1 + z * (P2 + z * (P3 + z * (P4 + z * P5))));
            if (k == 0) return 1 - ((x * c) / (c - 2.0) - x);
            double y = 1 - ((lo - (x * c) / (2.0 - c)) - hi);
            return Math.ScaleB(y, k);
        }

        // ------------------------------------------------------------------------------------------ log (e_log.c)

        const double Lg1 = 6.666666666666735130e-01, Lg2 = 3.999999999940941908e-01, Lg3 = 2.857142874366239149e-01,
            Lg4 = 2.222219843214978396e-01, Lg5 = 1.818357216161805012e-01, Lg6 = 1.531383769920937332e-01, Lg7 = 1.479819860511658591e-01;

        public static double Log(double x)
        {
            if (double.IsNaN(x) || x < 0) return double.NaN;
            if (x == 0) return double.NegativeInfinity;
            if (double.IsPositiveInfinity(x)) return x;
            int k = 0;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int hx = (int)(bits >> 32);
            if (hx < 0x00100000)
            {
                // subnormal: scale up by 2^54
                k -= 54;
                x *= 1.80143985094819840000e+16;
                bits = BitConverter.DoubleToInt64Bits(x);
                hx = (int)(bits >> 32);
            }
            k += (hx >> 20) - 1023;
            hx &= 0x000fffff;
            int i = (hx + 0x95f64) & 0x100000;
            // the mantissa as m in [sqrt(2)/2, sqrt(2)), and k so that x = m·2^k
            long mbits = ((long)(hx | (i ^ 0x3ff00000)) << 32) | (bits & 0xffffffffL);
            k += i >> 20;
            double f = BitConverter.Int64BitsToDouble(mbits) - 1.0;
            double s = f / (2.0 + f), dk = k, z = s * s, w = z * z;
            double t1 = w * (Lg2 + w * (Lg4 + w * Lg6)), t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
            double r = t2 + t1, hfsq = 0.5 * f * f;
            if (k == 0) return f - (hfsq - s * (hfsq + r));
            return dk * Ln2Hi - ((hfsq - (s * (hfsq + r) + dk * Ln2Lo)) - f);
        }

        /// <summary>x^y as exp(y·log x); for x below 0 only whole y (the sign by y's parity), else NaN.</summary>
        public static double Pow(double x, double y)
        {
            if (y == 0) return 1;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            if (x == 1) return 1;
            if (x == 0) return y > 0 ? 0 : double.PositiveInfinity;
            if (Math.Floor(y) == y && Math.Abs(y) <= 64)
            {
                // whole powers by repeated squaring: exact where the result fits, as the C libraries' pow is
                long e = (long)Math.Abs(y);
                double b = x, acc = 1;
                while (e > 0) { if ((e & 1) != 0) acc *= b; b *= b; e >>= 1; }
                return y > 0 ? acc : 1 / acc;
            }
            if (x < 0)
            {
                if (Math.Floor(y) != y) return double.NaN;
                double r = Exp(y * Log(-x));
                return Math.Abs(y % 2) == 1 ? -r : r;
            }
            return Exp(y * Log(x));
        }

        // ------------------------------------------------------------------------------------------ sin, cos (k_sin.c, k_cos.c)

        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
            S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
            C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
        // π/2 in three 33-bit parts (e_rem_pio2.c), so n·part is exact for |n| below 2^20
        const double InvPio2 = 6.36619772367581382433e-01, Pio2_1 = 1.57079632673412561417e+00,
            Pio2_2 = 6.07710050630396597660e-11, Pio2_3 = 2.02226624871116645580e-21, Pio2_3t = 8.47842766036889956997e-32;

        static double KernelSin(double x)
        {
            double z = x * x, v = z * x;
            double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            return x + v * (S1 + z * r);
        }

        static double KernelCos(double x)
        {
            double z = x * x;
            double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            double hz = 0.5 * z, w = 1.0 - hz;
            return w + (((1.0 - w) - hz) + z * r);
        }

        /// <summary>x − n·π/2 with |result| at most about π/4, and n mod 4.</summary>
        static double Reduce(double x, out int quadrant)
        {
            double fn = Math.Floor(x * InvPio2 + 0.5);
            double r = ((x - fn * Pio2_1) - fn * Pio2_2) - (fn * Pio2_3 + fn * Pio2_3t);
            quadrant = (int)((long)fn & 3);
            return r;
        }

        public static double Sin(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (Math.Abs(x) <= 0.7853981633974483) return KernelSin(x);
            double r = Reduce(x, out int q);
            return q switch { 0 => KernelSin(r), 1 => KernelCos(r), 2 => -KernelSin(r), _ => -KernelCos(r) };
        }

        public static double Cos(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return double.NaN;
            if (Math.Abs(x) <= 0.7853981633974483) return KernelCos(x);
            double r = Reduce(x, out int q);
            return q switch { 0 => KernelCos(r), 1 => -KernelSin(r), 2 => -KernelCos(r), _ => KernelSin(r) };
        }

        // ------------------------------------------------------------------------------------------ atan, atan2 (s_atan.c)

        static readonly double[] AtanHi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
        static readonly double[] AtanLo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
        static readonly double[] AT =
        {
            3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01, -1.11111104054623557880e-01,
            9.09088713343650656196e-02, -7.69187620504482999495e-02, 6.66107313738753120669e-02, -5.83357013379057348645e-02,
            4.97687799461593236017e-02, -3.65315727442169155270e-02, 1.62858201153657823623e-02,
        };
        const double Pi = 3.1415926535897931160e+00, PiLo = 1.2246467991473531772e-16, HalfPi = 1.57079632679489655800e+00;

        public static double Atan(double x)
        {
            if (double.IsNaN(x)) return x;
            bool negative = x < 0;
            double ax = Math.Abs(x);
            if (ax >= 7.378697629483821e19) return x > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];   // 2^66
            int id;
            if (ax < 0.4375)
            {
                if (ax < 7.450580596923828e-09) return x;   // 2^-27
                id = -1;
            }
            else
            {
                x = ax;
                if (ax < 1.1875)
                {
                    if (ax < 0.6875) { id = 0; x = (2.0 * x - 1.0) / (2.0 + x); }
                    else { id = 1; x = (x - 1.0) / (x + 1.0); }
                }
                else if (ax < 2.4375) { id = 2; x = (x - 1.5) / (1.0 + 1.5 * x); }
                else { id = 3; x = -1.0 / x; }
            }
            double z = x * x, w = z * z;
            double s1 = z * (AT[0] + w * (AT[2] + w * (AT[4] + w * (AT[6] + w * (AT[8] + w * AT[10])))));
            double s2 = w * (AT[1] + w * (AT[3] + w * (AT[5] + w * (AT[7] + w * AT[9]))));
            if (id < 0) return x - x * (s1 + s2);
            double r = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
            return negative ? -r : r;
        }

        /// <summary>The angle of (x, y) from the +x axis, −π..π, as Math.Atan2.</summary>
        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            if (y == 0)
            {
                if (x > 0 || (x == 0 && !double.IsNegative(x))) return y;            // ±0
                return double.IsNegative(y) ? -Pi : Pi;
            }
            if (x == 0) return y > 0 ? HalfPi : -HalfPi;
            if (double.IsInfinity(x))
            {
                if (double.IsInfinity(y)) return (x > 0 ? Pi / 4 : 3 * Pi / 4) * (y > 0 ? 1 : -1);
                return x > 0 ? (y > 0 ? 0.0 : -0.0) : (y > 0 ? Pi : -Pi);
            }
            if (double.IsInfinity(y)) return y > 0 ? HalfPi : -HalfPi;
            double z = Atan(Math.Abs(y / x));
            if (x > 0) return y > 0 ? z : -z;
            return y > 0 ? Pi - (z - PiLo) : (z - PiLo) - Pi;
        }
    }
}
