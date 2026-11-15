using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// (Weather rules 7) What the weather knows of the ground beyond weather.bin, both from the mod's own files so the server
    /// and every client get the same answers:
    ///  - the height each 0.5° cell's sea-level temperatures were reduced from (earthdata/weatherground.bin, tools/bake_earth.py
    ///    --weather-ground-only; 0 at sea and where the reanalysis' surface air stands in), so the rain rate reads the ground's
    ///    temperature (EA-0021);
    ///  - how much drier climate fix 4 made the precipitation than fix 3 (climatefix4.bin: GPCP's share at CHELSA's level on
    ///    the desert coasts, EA-0060), so the weather doesn't rain on Lima's or Walvis Bay's desert coast either. 1 elsewhere.
    /// Immutable and thread-safe; no Vintage Story types.
    /// </summary>
    public sealed class WeatherGround
    {
        public const string FileName = "weatherground.bin";

        readonly int gw, gh, gres;
        readonly float[] metres;
        readonly int cw, ch, cres;
        readonly Dictionary<int, float> precipRatio;
        readonly int[] rowFirst, rowLast;

        WeatherGround(int gw, int gh, float[] metres, int cw, int ch, Dictionary<int, float> precipRatio)
        {
            this.gw = gw; this.gh = gh; gres = gw / 360; this.metres = metres;
            this.cw = cw; this.ch = ch; cres = cw / 360; this.precipRatio = precipRatio;
            // (ER-12, W-10) each row's span of changed cells, so that a place away from the desert coasts looks up nothing
            rowFirst = new int[ch]; rowLast = new int[ch];
            Array.Fill(rowFirst, int.MaxValue); Array.Fill(rowLast, int.MinValue);
            foreach (int i in precipRatio.Keys)
            {
                int r = i / cw, c = i % cw;
                if (r < 0 || r >= ch) continue;
                rowFirst[r] = Math.Min(rowFirst[r], c); rowLast[r] = Math.Max(rowLast[r], c);
            }
        }

        /// <summary>Cells where the precipitation changes (tests).</summary>
        public int ChangedCells => precipRatio.Count;

        /// <summary>
        /// The ground from weatherground.bin (header EMWG, version 1, width, height, metres per unit, then brotli of the u16
        /// heights row by row from the north-west, each as the step from the cell before) and the climate grid's precipitation
        /// with fix 4 and fix 3 at the cells fix 4 changes (EarthDataset.PrecipChange). Throws on a damaged file.
        /// </summary>
        public static WeatherGround Read(Stream s, int climateW, int climateH, int[] changed, float[] newPrecip, float[] oldPrecip)
        {
            byte[] file = EarthBinary.ReadAll(s);
            EarthBinary.Expect(file, "EMWG", 20, FileName);
            int version = BitConverter.ToInt32(file, 4), w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12), step = BitConverter.ToInt32(file, 16);
            if (version != 1 || w <= 0 || w % 360 != 0 || h != w / 2 || step <= 0 || step > 1000)
                throw new InvalidDataException(FileName + ": unsupported layout");
            byte[] raw = EarthBinary.Unbrotli(file, 20, file.Length - 20, checked(2 * w * h), FileName);
            var m = new float[w * h];
            for (int r = 0; r < h; r++)
            {
                int q = 0;
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    q += BitConverter.ToInt16(raw, 2 * i);
                    if (q < 0) throw new InvalidDataException(FileName + ": negative height");
                    m[i] = q * step;
                }
            }
            var ratio = new Dictionary<int, float>();
            if (changed != null)
                for (int k = 0; k < changed.Length; k++)
                {
                    float a = newPrecip[k], b = oldPrecip[k];
                    float f = b > 0 ? Math.Clamp(a / b, 0f, 1f) : 1f;
                    if (f != 1f) ratio[changed[k]] = f;
                }
            return new WeatherGround(w, h, m, climateW, climateH, ratio);
        }

        /// <summary>The height (m) the weather's sea-level temperatures were reduced from here, bilinear between weather.bin's
        /// cell centres as its normals are.</summary>
        public double MetresAt(double lat, double lon)
        {
            double fr = (90 - lat) * gres - 0.5, fc = (lon + 180) * gres - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            int ra = Math.Clamp(r0, 0, gh - 1), rb = Math.Clamp(r0 + 1, 0, gh - 1);
            int ca = ((c0 % gw) + gw) % gw, cb = (ca + 1) % gw;
            return (1 - tr) * ((1 - tc) * metres[ra * gw + ca] + tc * metres[ra * gw + cb]) + tr * ((1 - tc) * metres[rb * gw + ca] + tc * metres[rb * gw + cb]);
        }

        /// <summary>0..1: the precipitation with climate fix 4 over that with fix 3 here, bilinear between the 0.25° climate
        /// grid's cell centres (1 where fix 4 changes nothing).</summary>
        public double PrecipScale(double lat, double lon)
        {
            if (precipRatio.Count == 0) return 1;
            double fr = (90 - lat) * cres - 0.5, fc = (lon + 180) * cres - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            int ra = Math.Clamp(r0, 0, ch - 1), rb = Math.Clamp(r0 + 1, 0, ch - 1);
            int ca = ((c0 % cw) + cw) % cw, cb = (ca + 1) % cw;
            // (the same arithmetic with 1 for a cell outside its row's span: the dictionary would answer 1 there too)
            double R(int r, int c) => c < rowFirst[r] || c > rowLast[r] ? 1 : precipRatio.TryGetValue(r * cw + c, out float f) ? f : 1;
            return (1 - tr) * ((1 - tc) * R(ra, ca) + tc * R(ra, cb)) + tr * ((1 - tc) * R(rb, ca) + tc * R(rb, cb));
        }
    }
}
