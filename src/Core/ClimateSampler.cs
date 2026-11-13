using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The climate one world reads (EarthDataView), sampled at geographic points: the 0.25° climate grid with the world's
    /// climate fix, bilinear, then where the world chose them CHELSA's temperatures and rainfall on the 2' grid
    /// (temperature.bin, rain.bin) and the Penman-Monteith aridity factor (aridity.bin). Never changes. Thread-safe.
    /// </summary>
    public sealed class ClimateSampler
    {
        readonly int ClimateRes, ClimateW, ClimateH;
        readonly float[] tMean, tCold, tWarm, precip, rain, drySummer;
        // null where the world doesn't read them
        readonly FineTemperature fine;
        readonly TiledGrid fineRain, penman;

        /// <param name="planes">The climate grid's planes with the world's fix (EarthDataset.ClimatePlanes).</param>
        internal ClimateSampler(EarthDataset data, float[][] planes, FineTemperature fine, TiledGrid fineRain, TiledGrid penman)
        {
            ClimateRes = data.ClimateRes;
            ClimateW = data.ClimateW;
            ClimateH = data.ClimateH;
            tMean = planes[0]; tCold = planes[1]; tWarm = planes[2]; precip = planes[3]; rain = planes[4]; drySummer = planes[5];
            this.fine = fine;
            this.fineRain = fineRain;
            this.penman = penman;
        }

        public EarthData.ClimateSample SampleClimate(double lat, double lon)
        {
            double fr = (90 - lat) * ClimateRes - 0.5;
            double fc = (lon + 180) * ClimateRes - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            int ra = Math.Clamp(r0, 0, ClimateH - 1), rb = Math.Clamp(r0 + 1, 0, ClimateH - 1);
            int ca = ((c0 % ClimateW) + ClimateW) % ClimateW, cb = (ca + 1) % ClimateW;
            int i00 = ra * ClimateW + ca, i01 = ra * ClimateW + cb, i10 = rb * ClimateW + ca, i11 = rb * ClimateW + cb;
            double w00 = (1 - tr) * (1 - tc), w01 = (1 - tr) * tc, w10 = tr * (1 - tc), w11 = tr * tc;
            var s = new EarthData.ClimateSample
            {
                TMean = w00 * tMean[i00] + w01 * tMean[i01] + w10 * tMean[i10] + w11 * tMean[i11],
                TCold = w00 * tCold[i00] + w01 * tCold[i01] + w10 * tCold[i10] + w11 * tCold[i11],
                TWarm = w00 * tWarm[i00] + w01 * tWarm[i01] + w10 * tWarm[i10] + w11 * tWarm[i11],
                Precip = w00 * precip[i00] + w01 * precip[i01] + w10 * precip[i10] + w11 * precip[i11],
                Rain = w00 * rain[i00] + w01 * rain[i01] + w10 * rain[i10] + w11 * rain[i11],
                DrySummer = w00 * drySummer[i00] + w01 * drySummer[i01] + w10 * drySummer[i10] + w11 * drySummer[i11],
            };
            if (fine != null) FineTemperatureAt(lat, lon, ref s);
            if (fineRain != null) FineRainAt(lat, lon, ref s);
            if (penman != null) s.PenmanFactor = PenmanFactorAt(lat, lon);
            return s;
        }

        /// <summary>The temperatures from CHELSA's 2' cells, bilinear between their centres; a corner without CHELSA takes the
        /// 0.25° grid's value here. CHELSA ends at 84°N, where its Arctic is up to a few degrees off the climate grid's: it fades
        /// into the climate grid from 83°N, so the two meet without a step.</summary>
        void FineTemperatureAt(double lat, double lon, ref EarthData.ClimateSample s)
        {
            double share = Geo.SmoothStep((FineEdgeLat - lat) / FineFadeDegrees);
            if (share <= 0) return;
            var f = fine;
            double fr = (90 - lat) * f.Res - 0.5, fc = (lon + 180) * f.Res - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            Span<float> ms = stackalloc float[4], cs = stackalloc float[4], ws = stackalloc float[4];
            f.Quad(r0, c0, ms, cs, ws);
            double mean = 0, cold = 0, warm = 0;
            for (int k = 0; k < 4; k++)
            {
                int dr = k >> 1, dc = k & 1;
                double w = (dr == 0 ? 1 - tr : tr) * (dc == 0 ? 1 - tc : tc);
                if (!float.IsNaN(ms[k]))
                {
                    mean += w * ms[k]; cold += w * cs[k]; warm += w * ws[k];
                }
                else
                {
                    mean += w * s.TMean; cold += w * s.TCold; warm += w * s.TWarm;
                }
            }
            s.TMean += share * (mean - s.TMean);
            s.TCold += share * (cold - s.TCold);
            s.TWarm += share * (warm - s.TWarm);
        }

        /// <summary>Where CHELSA's temperatures end (a little short of its last row, 84°N) and how far south they fade in.</summary>
        const double FineEdgeLat = 83.95, FineFadeDegrees = 0.95;

        /// <summary>CHELSA's rainfall and summer dryness from the 2' cells, bilinear; a corner without data keeps the coarse grid's.</summary>
        void FineRainAt(double lat, double lon, ref EarthData.ClimateSample s)
        {
            var g = fineRain;
            g.Position(lat, lon, out double fr, out double fc);
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0, rain = 0, dry = 0;
            int n = g.CellsPerTile;
            for (int k = 0; k < 4; k++)
            {
                int dr = k >> 1, dc = k & 1;
                double w = (dr == 0 ? 1 - tr : tr) * (dc == 0 ? 1 - tc : tc);
                var t = g.TileOf(r0 + dr, c0 + dc, out int i);
                if (t != null && t[2 * n + i] != 0) { rain += w * t[i]; dry += w * t[n + i] / 255.0; }
                else { rain += w * s.Rain; dry += w * s.DrySummer; }
            }
            s.Rain = rain;
            s.DrySummer = dry;
        }

        /// <summary>aridity.bin's factor (Holdridge / Penman-Monteith evapotranspiration), bilinear in log between the 2' cell
        /// centres that have one; 1 where none of the four has (north of 84°N).</summary>
        double PenmanFactorAt(double lat, double lon)
        {
            var g = penman;
            g.Position(lat, lon, out double fr, out double fc);
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0, sum = 0, weight = 0;
            for (int k = 0; k < 4; k++)
            {
                int dr = k >> 1, dc = k & 1;
                double w = (dr == 0 ? 1 - tr : tr) * (dc == 0 ? 1 - tc : tc);
                var t = g.TileOf(r0 + dr, c0 + dc, out int i);
                if (t == null || t[i] == 0) continue;
                sum += w * (t[i] - 128);
                weight += w;
            }
            return weight > 1e-9 ? Math.Pow(2, sum / weight / 64) : 1;
        }
    }
}
