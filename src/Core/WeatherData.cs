using System;
using System.IO;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The monthly normals the weather is made from (earthdata/weather.bin, tools/bake_earth.py --weather-only), on a 0.5°
    /// grid: precipitation (mm/day), sea-level-equivalent temperature (°C), the land fraction and (v2) the reanalysis' 10 m
    /// winds and total cloud cover. Loaded on both sides (the client has no terrain data, so the weather may only use what
    /// is here and in climate.bin). Kept as the file's bytes and decoded on reading (about 15 MB instead of 60 as floats).
    ///
    /// Months are interpolated: month m's value holds at the middle of the month (yearRel (m + 0.5) / 12, the game
    /// calendar's months), and changes linearly to the next one's, wrapping December to January.
    ///
    /// Also worked out at load: the latitude of the tropical rain band (the ITCZ, where the trade winds meet) for every
    /// month and degree of longitude. Without the reanalysis' winds the prevailing winds are laid out around it (which is
    /// how the monsoons come about), and it steers the weather systems either way.
    /// </summary>
    public sealed class WeatherData
    {
        public const int Months = 12;
        public int Res { get; private set; }
        public int W { get; private set; }
        public int H { get; private set; }

        // the decompressed file: 12 precipitation planes (u8), 12 temperature planes (u8), land (u8), and in v2 12 planes
        // each of wind east (i8), wind north (i8) and cloud cover (u8)
        byte[] raw;
        int n;
        bool v2;
        static readonly float[] PrecipLut = BuildPrecipLut();

        static float[] BuildPrecipLut()
        {
            var l = new float[256];
            for (int q = 0; q < 256; q++) l[q] = (float)(PortableMath.Exp(q / 32.0) - 1);
            return l;
        }

        /// <summary>Whether the file has the reanalysis' monthly winds and cloud cover (weather.bin v2).</summary>
        public bool HasWindsAndClouds => v2;

        /// <summary>A fingerprint of the file (FNV-1a of its bytes): the server sends it so a client with other data keeps
        /// vanilla weather instead of computing other weather.</summary>
        public string Signature { get; private set; }

        /// <summary>[month][degree of longitude 0..359, from -180]: ITCZ latitude (°).</summary>
        float[][] itcz;

        const int MaxRes = 8;

        public static WeatherData Read(Stream s)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMWX", 20, "weather.bin");
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12), months = BitConverter.ToInt32(file, 16);
            if (version < 1 || version > 2 || months != Months || w <= 0 || w % 360 != 0 || w / 360 > MaxRes || h != w / 2)
                throw new InvalidDataException("weather.bin: unsupported layout");
            var d = new WeatherData { W = w, H = h, Res = w / 360, n = w * h, v2 = version >= 2 };
            int planes = 2 * Months + 1 + (d.v2 ? 3 * Months : 0);
            d.raw = EarthBinary.Unbrotli(file, 20, file.Length - 20, checked(d.n * planes), "weather.bin");
            d.Signature = WeatherNoise.Fnv1a(file).ToString("x16");
            d.FindItcz();
            return d;
        }

        // plane offsets in raw
        int PrecipAt(int m) => m * n;
        int TempAt(int m) => (Months + m) * n;
        int LandOffset => 2 * Months * n;
        int WindEAt(int m) => (2 * Months + 1 + m) * n;
        int WindNAt(int m) => (3 * Months + 1 + m) * n;
        int CloudAt(int m) => (4 * Months + 1 + m) * n;

        float P(int o, int i) => PrecipLut[raw[o + i]];
        float T(int o, int i) => raw[o + i] / 2f - 50;
        float U8(int o, int i) => raw[o + i];
        float I8(int o, int i) => (sbyte)raw[o + i];

        // ------------------------------------------------------------------------------------------ sampling

        /// <summary>The two months around a point in the year and the weight of the second.</summary>
        public static void MonthPair(double yearRel, out int m0, out int m1, out double t)
        {
            double f = (yearRel - Math.Floor(yearRel)) * Months - 0.5;
            double fl = Math.Floor(f);
            t = f - fl;
            m0 = (((int)fl % Months) + Months) % Months;
            m1 = (m0 + 1) % Months;
        }

        public struct Normals
        {
            /// <summary>Precipitation, mm/day.</summary>
            public double Precip;
            /// <summary>Sea-level-equivalent temperature, °C.</summary>
            public double Temp;
            /// <summary>0..1.</summary>
            public double Land;
            /// <summary>The month's mean 10 m wind, m/s east and north (0 without the reanalysis data).</summary>
            public double WindE, WindN;
            /// <summary>The month's mean total cloud cover, 0..1 (NaN without the reanalysis data).</summary>
            public double Cloud;
        }

        /// <summary>The normals at a place and time of year (0..1), bilinear in space, linear between the months.</summary>
        public Normals Sample(double lat, double lon, double yearRel) => Sample(lat, lon, yearRel, true);

        /// <summary>A daily amount over an average month (365.25 / 12 days): precipitation mm/day to mm per month, degrees
        /// above a base to degree days.</summary>
        public static double PerMonth(double perDay) => perDay * 365.25 / 12;

        /// <summary>This file in per-thread caches (a number, so a thread does not keep an unloaded world's data alive).</summary>
        readonly long cacheId = System.Threading.Interlocked.Increment(ref lastCacheId);
        static long lastCacheId;

        sealed class MonthCache
        {
            public long Owner;
            public double Lat = double.NaN, Lon = double.NaN;
            public readonly double[] Temp = new double[Months], Precip = new double[Months];
        }
        [ThreadStatic] static MonthCache monthCache;

        /// <summary>
        /// The middle of every month at a place (January first), each exactly <see cref="Sample"/> at yearRel (m + 0.5) / 12:
        /// sea-level temperature (°C) and precipitation in mm per month (<see cref="PerMonth"/>). The zones' dry months, the fire
        /// regime's summer and the grass's season maps read the year this way, and the first two ask for the same place one
        /// after the other, so the last place is kept per thread.
        /// </summary>
        public void MonthlyNormals(double lat, double lon, Span<double> temp, Span<double> precipMm)
        {
            var c = monthCache ??= new MonthCache();
            if (c.Owner != cacheId || !c.Lat.Equals(lat) || !c.Lon.Equals(lon))
            {
                for (int m = 0; m < Months; m++)
                {
                    // the middle of month m, bilinear in space
                    var n = Sample(lat, lon, (m + 0.5) / 12, false);
                    c.Temp[m] = n.Temp;
                    c.Precip[m] = PerMonth(n.Precip);
                }
                c.Owner = cacheId; c.Lat = lat; c.Lon = lon;
            }
            c.Temp.AsSpan().CopyTo(temp);
            c.Precip.AsSpan().CopyTo(precipMm);
        }

        /// <summary><see cref="Sample"/>; <paramref name="winds"/> false leaves out the reanalysis' winds and clouds.</summary>
        Normals Sample(double lat, double lon, double yearRel, bool winds)
        {
            MonthPair(yearRel, out int m0, out int m1, out double mt);
            double fr = (90 - lat) * Res - 0.5, fc = (lon + 180) * Res - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            int ra = Math.Clamp(r0, 0, H - 1), rb = Math.Clamp(r0 + 1, 0, H - 1);
            int ca = ((c0 % W) + W) % W, cb = (ca + 1) % W;
            int i00 = ra * W + ca, i01 = ra * W + cb, i10 = rb * W + ca, i11 = rb * W + cb;
            double w00 = (1 - tr) * (1 - tc), w01 = (1 - tr) * tc, w10 = tr * (1 - tc), w11 = tr * tc;
            var b = raw;
            var lut = PrecipLut;
            // (local functions, not delegates: nothing is allocated per call)
            double BiP(int o) => w00 * lut[b[o + i00]] + w01 * lut[b[o + i01]] + w10 * lut[b[o + i10]] + w11 * lut[b[o + i11]];
            double BiU(int o) => w00 * b[o + i00] + w01 * b[o + i01] + w10 * b[o + i10] + w11 * b[o + i11];
            double BiI(int o) => w00 * (sbyte)b[o + i00] + w01 * (sbyte)b[o + i01] + w10 * (sbyte)b[o + i10] + w11 * (sbyte)b[o + i11];
            var r = new Normals
            {
                Precip = BiP(PrecipAt(m0)) * (1 - mt) + BiP(PrecipAt(m1)) * mt,
                Temp = (BiU(TempAt(m0)) * (1 - mt) + BiU(TempAt(m1)) * mt) / 2 - 50,
                Land = BiU(LandOffset) / 255,
                Cloud = double.NaN,
            };
            if (v2 && winds)
            {
                r.WindE = (BiI(WindEAt(m0)) * (1 - mt) + BiI(WindEAt(m1)) * mt) / 8;
                r.WindN = (BiI(WindNAt(m0)) * (1 - mt) + BiI(WindNAt(m1)) * mt) / 8;
                r.Cloud = (BiU(CloudAt(m0)) * (1 - mt) + BiU(CloudAt(m1)) * mt) / 250;
            }
            return r;
        }

        float[] oceanRowTemp, annualRange;
        readonly object oceanRowLock = new object();

        /// <summary>(Weather rules 6, EA-0025) The month normals' yearly range here (warmest minus coldest month, °C), between
        /// the cells as <see cref="Sample(double, double, double)"/>: how continental the climate is.</summary>
        public double AnnualRange(double lat, double lon)
        {
            var g = Volatile.Read(ref annualRange);
            if (g == null)
            {
                lock (oceanRowLock)
                {
                    g = annualRange;
                    if (g == null)
                    {
                        g = new float[W * H];
                        for (int i = 0; i < W * H; i++)
                        {
                            double lo = double.MaxValue, hi = double.MinValue;
                            for (int m = 0; m < Months; m++)
                            {
                                double t = raw[TempAt(m) + i] / 2.0 - 50;
                                lo = Math.Min(lo, t);
                                hi = Math.Max(hi, t);
                            }
                            g[i] = (float)(hi - lo);
                        }
                        Volatile.Write(ref annualRange, g);
                    }
                }
            }
            double fr = (90 - lat) * Res - 0.5, fc = (lon + 180) * Res - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            int ra = Math.Clamp(r0, 0, H - 1), rb = Math.Clamp(r0 + 1, 0, H - 1);
            int ca = ((c0 % W) + W) % W, cb = (ca + 1) % W;
            return (1 - tr) * ((1 - tc) * g[ra * W + ca] + tc * g[ra * W + cb]) + tr * ((1 - tc) * g[rb * W + ca] + tc * g[rb * W + cb]);
        }

        /// <summary>
        /// (Weather rules 6, EA-0023) How much colder the sea surface air is here than the open ocean's mean at this latitude
        /// and time of year (°C, positive where colder; NaN over land): the cold upwelling coasts (Peru, Namibia, California,
        /// the Canaries, Western Australia), where a low cloud deck sits on a cold sea under warm, dry air. The row means are
        /// over the cells with no land, computed once from this file.
        /// </summary>
        public double ColdWater(double lat, double lon, double yearRel, double temp, double land)
        {
            if (land > 0.5 || double.IsNaN(temp)) return double.NaN;
            var rows = OceanRowTemps();
            MonthPair(yearRel, out int m0, out int m1, out double mt);
            double fr = Math.Clamp((90 - lat) * Res - 0.5, 0, H - 1);
            int r0 = (int)Math.Floor(fr), r1 = Math.Min(H - 1, r0 + 1);
            double tr = fr - r0;
            double Row(int m) => rows[m * H + r0] * (1 - tr) + rows[m * H + r1] * tr;
            double mean = Row(m0) * (1 - mt) + Row(m1) * mt;
            return double.IsNaN(mean) ? double.NaN : mean - temp;
        }

        float[] OceanRowTemps()
        {
            var r = Volatile.Read(ref oceanRowTemp);
            if (r != null) return r;
            lock (oceanRowLock)
            {
                if (oceanRowTemp != null) return oceanRowTemp;
                var t = new float[Months * H];
                for (int m = 0; m < Months; m++)
                {
                    int to = TempAt(m);
                    for (int row = 0; row < H; row++)
                    {
                        double sum = 0;
                        int count = 0;
                        for (int c = 0; c < W; c++)
                        {
                            int i = row * W + c;
                            if (raw[LandOffset + i] > 25) continue;   // under a tenth land
                            sum += raw[to + i] / 2.0 - 50;
                            count++;
                        }
                        t[m * H + row] = count >= W / 8 ? (float)(sum / count) : float.NaN;
                    }
                }
                // rows with too little open ocean (the Arctic, Antarctica's coast) take the nearest row's mean
                for (int m = 0; m < Months; m++)
                    for (int row = 0; row < H; row++)
                    {
                        if (!float.IsNaN(t[m * H + row])) continue;
                        for (int d = 1; d < H; d++)
                        {
                            if (row - d >= 0 && !float.IsNaN(t[m * H + row - d])) { t[m * H + row] = t[m * H + row - d]; break; }
                            if (row + d < H && !float.IsNaN(t[m * H + row + d])) { t[m * H + row] = t[m * H + row + d]; break; }
                        }
                    }
                Volatile.Write(ref oceanRowTemp, t);
                return t;
            }
        }

        /// <summary>Monthly precipitation (mm/day) at one cell, for tests and maps.</summary>
        public double MonthPrecip(int month, double lat, double lon) => P(PrecipAt(month), Cell(lat, lon));
        public double MonthTemp(int month, double lat, double lon) => T(TempAt(month), Cell(lat, lon));
        public double LandAt(double lat, double lon) => U8(LandOffset, Cell(lat, lon)) / 255;
        public double MonthCloud(int month, double lat, double lon) => v2 ? U8(CloudAt(month), Cell(lat, lon)) / 250 : double.NaN;

        int Cell(double lat, double lon)
        {
            int r = Math.Clamp((int)Math.Floor((90 - lat) * Res), 0, H - 1);
            int c = (((int)Math.Floor((lon + 180) * Res) % W) + W) % W;
            return r * W + c;
        }

        /// <summary>The ITCZ's latitude (°) at a longitude and time of year, linear between degrees and months.</summary>
        public double ItczLat(double lon, double yearRel)
        {
            MonthPair(yearRel, out int m0, out int m1, out double mt);
            double f = Geo.Wrap180(lon) + 180 - 0.5;
            int a = (((int)Math.Floor(f)) % 360 + 360) % 360, b = (a + 1) % 360;
            double t = f - Math.Floor(f);
            double v0 = itcz[m0][a] * (1 - t) + itcz[m0][b] * t, v1 = itcz[m1][a] * (1 - t) + itcz[m1][b] * t;
            return v0 * (1 - mt) + v1 * mt;
        }

        /// <summary>
        /// Per month and degree of longitude: where the trade winds meet. Over the sea that is the rain band (the wettest
        /// latitude between 25°S and 30°N, the rain smoothed over ±3° of latitude and ±10° of longitude); over land in summer
        /// it is the heat low (the warmest latitude, sea-level equivalent): the monsoon trough over northern India, the
        /// Sahara's southern edge, northern Australia. The two are blended half and half, smoothed over 25° of longitude and
        /// kept within 20°S..28°N. The box means come from running sums (a few ms instead of a brute-force fifth of a second).
        /// </summary>
        void FindItcz()
        {
            itcz = new float[Months][];
            int rN = (int)Math.Floor((90 - 30.0) * Res), rS = (int)Math.Floor((90 + 25.0) * Res);
            int rows = rS - rN + 1;
            int dl = 3 * Res, dd = 10;
            var p = new double[360, rows];
            var t = new double[360, rows];
            // running sums over longitude (wrapping) then latitude
            var sp = new double[360 + 1, rows];
            var st = new double[360 + 1, rows];
            for (int m = 0; m < Months; m++)
            {
                int po = PrecipAt(m), to = TempAt(m);
                for (int deg = 0; deg < 360; deg++)
                    for (int r = 0; r < rows; r++)
                    {
                        double a = 0, b = 0;
                        for (int c = deg * Res; c < (deg + 1) * Res; c++)
                        {
                            int i = (rN + r) * W + c;
                            a += P(po, i);
                            b += T(to, i);
                        }
                        p[deg, r] = a / Res;
                        t[deg, r] = b / Res;
                    }
                for (int r = 0; r < rows; r++)
                {
                    sp[0, r] = st[0, r] = 0;
                    for (int deg = 0; deg < 360; deg++)
                    {
                        sp[deg + 1, r] = sp[deg, r] + p[deg, r];
                        st[deg + 1, r] = st[deg, r] + t[deg, r];
                    }
                }
                double LonSum(double[,] s, int deg, int r)
                {
                    // degrees deg-dd .. deg+dd, wrapping
                    int a = deg - dd, b = deg + dd;
                    double sum = 0;
                    if (a < 0) { sum += s[360, r] - s[360 + a, r]; a = 0; }
                    if (b > 359) { sum += s[b - 359, r]; b = 359; }
                    return sum + s[b + 1, r] - s[a, r];
                }
                var raw = new double[360];
                var colP = new double[rows];
                var colT = new double[rows];
                for (int deg = 0; deg < 360; deg++)
                {
                    for (int r = 0; r < rows; r++) { colP[r] = LonSum(sp, deg, r); colT[r] = LonSum(st, deg, r); }
                    double bestP = double.MinValue, latP = 5, tMax = double.MinValue;
                    var tRow = new double[rows];
                    double cp = 0, ct = 0;
                    // a sliding window of ±dl rows over the column sums
                    int lo = 0, hi = -1;
                    for (int r = 0; r < rows; r++)
                    {
                        int wantLo = Math.Max(0, r - dl), wantHi = Math.Min(rows - 1, r + dl);
                        while (hi < wantHi) { hi++; cp += colP[hi]; ct += colT[hi]; }
                        while (lo < wantLo) { cp -= colP[lo]; ct -= colT[lo]; lo++; }
                        int count = (hi - lo + 1) * (2 * dd + 1);
                        double mp = cp / count;
                        tRow[r] = ct / count;
                        if (mp > bestP) { bestP = mp; latP = 90 - (rN + r + 0.5) / Res; }
                        tMax = Math.Max(tMax, tRow[r]);
                    }
                    double sw = 0, slat = 0;
                    for (int r = 0; r < rows; r++)
                    {
                        double wgt = PortableMath.Exp((tRow[r] - tMax) / 1.5);
                        sw += wgt;
                        slat += wgt * (90 - (rN + r + 0.5) / Res);
                    }
                    raw[deg] = 0.5 * latP + 0.5 * slat / sw;
                }
                var smooth = new float[360];
                const int half = 12;
                for (int deg = 0; deg < 360; deg++)
                {
                    double s = 0, ws = 0;
                    for (int k = -half; k <= half; k++)
                    {
                        double wk = 1 - Math.Abs(k) / (half + 1.0);
                        s += wk * raw[((deg + k) % 360 + 360) % 360];
                        ws += wk;
                    }
                    smooth[deg] = (float)Math.Clamp(s / ws, -20, 28);
                }
                itcz[m] = smooth;
            }
        }
    }
}
