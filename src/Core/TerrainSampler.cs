using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The terrain grid sampled at geographic points for one set of overlays (EarthDataView): bicubic elevation, relief, the
    /// sea and lake shares, levels and nearness of the 2x2 cells around a point, the coast references, and the lake levels
    /// the GLO-90 outlines leave to the grid. Reads its own TerrainTileCache, so what it keeps (the kept lake levels) is
    /// always made under its overlays. Thread-safe.
    /// </summary>
    public sealed class TerrainSampler
    {
        readonly TerrainTileCache tiles;
        readonly TerrainOverlays overlays;
        readonly int TerrainRes, TerrainW, TerrainH;

        internal TerrainSampler(EarthDataset data, TerrainOverlays overlays, int maxCachedTiles)
        {
            this.overlays = overlays;
            tiles = new TerrainTileCache(data, overlays, maxCachedTiles);
            TerrainRes = data.TerrainRes;
            TerrainW = data.TerrainW;
            TerrainH = data.TerrainH;
        }

        public TerrainTileCache Tiles => tiles;
        public TerrainOverlays Overlays => overlays;

        /// <summary>A terrain cell. Rows are clamped at the poles, columns wrap around.</summary>
        public EarthData.Cell GetCell(int row, int col) => tiles.GetCell(row, col);
        public float GetElev(int row, int col) => tiles.GetElev(row, col);

        /// <summary>Whether a grid cell is land, or dry land's ring, only because of the outlets.bin overlay, with the overlay
        /// on: river points there are no longer held at the lake's level.</summary>
        public bool IsOutletLandCell(int row, int col)
        {
            if (!overlays.OutletOverlay || row < 0 || row >= TerrainH) return false;
            col %= TerrainW;
            if (col < 0) col += TerrainW;
            return overlays.OutletLandCells.Contains((long)row * TerrainW + col);
        }

        /// <summary>Whether a grid cell is lake only because of the lakes.bin overlay (a terminal lake), with the overlay on.</summary>
        public bool IsTerminalLakeCell(int row, int col)
        {
            if (!overlays.LakeOverlay || row < 0 || row >= TerrainH) return false;
            col %= TerrainW;
            if (col < 0) col += TerrainW;
            return overlays.LakePatchCells.Contains((long)row * TerrainW + col);
        }

        /// <summary>
        /// 0053 item 17: the highest level (m) of the grid's lakes that the GLO-90 lake outlines leave to the grid, Earth's
        /// salt lakes and the terminal lakes it fills (lake cells flagged salt, or lake only through lakes.bin), within 3
        /// cells of the 2×2 cells around a point (their rings' reach); NaN where there is none. Kept per cell.
        /// </summary>
        public double KeptLakeLevelNear(double lat, double lon)
        {
            int r0 = (int)Math.Floor((90 - lat) * TerrainRes - 0.5), c0 = (int)Math.Floor((lon + 180) * TerrainRes - 0.5);
            c0 %= TerrainW;
            if (c0 < 0) c0 += TerrainW;
            long key = (long)r0 * TerrainW + c0;
            // the overlays never change under this sampler, so a kept value always holds (a world that changes them gets a
            // new sampler with an empty cache)
            if (keptNear.TryGetValue(key, out float kept)) return kept;
            float best = float.NaN;
            for (int r = r0 - 3; r <= r0 + 4; r++)
            {
                if (r < 0 || r >= TerrainH) continue;
                for (int c = c0 - 3; c <= c0 + 4; c++)
                {
                    var cell = GetCell(r, c);
                    if ((cell.Flags & EarthData.FlagLake) == 0 || cell.Level == EarthData.NoLevel) continue;
                    if ((cell.Flags & EarthData.FlagSalt) == 0 && !IsTerminalLakeCell(r, c)) continue;
                    if (float.IsNaN(best) || cell.Level > best) best = cell.Level;
                }
            }
            // bounded: past KeptNearMax values the cache starts again (they are pure, so only time is lost)
            if (Interlocked.Increment(ref keptAdds) > KeptNearMax)
            {
                keptNear.Clear();
                Interlocked.Exchange(ref keptAdds, 0);
            }
            keptNear[key] = best;
            return best;
        }

        readonly ConcurrentDictionary<long, float> keptNear = new();
        int keptAdds;
        /// <summary>How many grid cells KeptLakeLevelNear keeps (one per 2' cell near lakes asked about; about 10 MB).</summary>
        const int KeptNearMax = 1 << 18;

        long Key(int row, int col)
        {
            if (row < 0 || row >= TerrainH) return -1;
            col %= TerrainW;
            if (col < 0) col += TerrainW;
            return (long)row * TerrainW + col;
        }

        /// <summary>0..1, bilinear between cell centres: how green the land is here for a terminal lake's shore (ShoreWeights);
        /// 0 without the lakes overlay.</summary>
        public double TerminalShoreWeight(double lat, double lon)
        {
            var lakeShoreWeight = overlays.LakeShoreWeight;
            if (!overlays.LakeOverlay || lakeShoreWeight == null || lakeShoreWeight.Count == 0) return 0;
            double fr = (90 - lat) * TerrainRes - 0.5;
            double fc = (lon + 180) * TerrainRes - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            double W(int r, int c) => lakeShoreWeight.TryGetValue(Key(r, c), out float w) ? w : 0;
            return (1 - tr) * ((1 - tc) * W(r0, c0) + tc * W(r0, c0 + 1)) + tr * ((1 - tc) * W(r0 + 1, c0) + tc * W(r0 + 1, c0 + 1));
        }

        /// <summary>The terrain at a point; <paramref name="coastRefs"/> also gives LandElev and SeaElev near the sea (CoastLayer).</summary>
        public EarthData.TerrainSample SampleTerrain(double lat, double lon, bool coastRefs)
        {
            double fr = (90 - lat) * TerrainRes - 0.5;
            double fc = (lon + 180) * TerrainRes - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;

            // 4x4 neighbourhood for bicubic elevation and relief
            var around = tiles.Around(r0, c0);
            Span<double> e = stackalloc double[16];
            around.Elev.CopyTo(e);
            var s = new EarthData.TerrainSample();
            s.Elev = Bicubic(e, tr, tc);
            // relief of each of the 4 surrounding cells (range of its 3x3 neighbourhood), interpolated so it is continuous
            double r00 = Range(e, 0, 0), r01 = Range(e, 0, 1), r10 = Range(e, 1, 0), r11 = Range(e, 1, 1);
            s.Relief = (1 - tr) * ((1 - tc) * r00 + tc * r01) + tr * ((1 - tc) * r10 + tc * r11);
            double sr = tr * tr * (3 - 2 * tr), sc = tc * tc * (3 - 2 * tc);
            s.ReliefSmooth = (1 - sr) * ((1 - sc) * r00 + sc * r01) + sr * ((1 - sc) * r10 + sc * r11);

            // 2x2 for masks, ice and level
            EarthData.Cell c00 = around.C00, c01 = around.C01, c10 = around.C10, c11 = around.C11;
            double w00 = (1 - tr) * (1 - tc), w01 = (1 - tr) * tc, w10 = tr * (1 - tc), w11 = tr * tc;
            s.Ocean = w00 * Bit(c00, EarthData.FlagOcean) + w01 * Bit(c01, EarthData.FlagOcean) + w10 * Bit(c10, EarthData.FlagOcean) + w11 * Bit(c11, EarthData.FlagOcean);
            s.Lake = w00 * Bit(c00, EarthData.FlagLake) + w01 * Bit(c01, EarthData.FlagLake) + w10 * Bit(c10, EarthData.FlagLake) + w11 * Bit(c11, EarthData.FlagLake);
            s.Ice = w00 * c00.Ice + w01 * c01.Ice + w10 * c10.Ice + w11 * c11.Ice;
            // Worlds with islands: at the sea the bicubic may not rise above the highest of the 4 cells. Between a reef flat
            // 2 m deep and a 500 m drop-off it overshot to +35 m: ridges of land along the Bahama banks' rims, in open sea.
            if (overlays.IslandOverlay && s.Ocean > 0) s.Elev = Math.Min(s.Elev, Math.Max(Math.Max(e[5], e[6]), Math.Max(e[9], e[10])));

            s.LakeLevel = double.NaN;
            s.ShoreLevel = double.NaN;
            double bestLake = -1;
            ConsiderLake(c00, w00, ref s, ref bestLake);
            ConsiderLake(c01, w01, ref s, ref bestLake);
            ConsiderLake(c10, w10, ref s, ref bestLake);
            ConsiderLake(c11, w11, ref s, ref bestLake);
            ConsiderShore(c00, ref s);
            ConsiderShore(c01, ref s);
            ConsiderShore(c10, ref s);
            ConsiderShore(c11, ref s);
            // nearness to the water whose level is held here: corners of another level (the sea's ring meeting a lake's, a
            // second lake) count as far
            s.LakeNear = (w00 * NearTo(c00, s.ShoreLevel) + w01 * NearTo(c01, s.ShoreLevel) + w10 * NearTo(c10, s.ShoreLevel)
                + w11 * NearTo(c11, s.ShoreLevel)) / 255.0;
            s.SeaNear = w00 * SeaLevelBit(c00) + w01 * SeaLevelBit(c01) + w10 * SeaLevelBit(c10) + w11 * SeaLevelBit(c11);
            s.LandElev = s.SeaElev = double.NaN;
            if (coastRefs && s.SeaNear > 0) CoastRefs(e, tr, tc, ref s);
            return s;
        }

        static double SeaLevelBit(EarthData.Cell c) => c.Level == 0 ? 1 : 0;

        /// <summary>LandElev and SeaElev for a sample taken without them (or where SeaNear was 0).</summary>
        public void AddCoastRefs(double lat, double lon, ref EarthData.TerrainSample s)
        {
            double fr = (90 - lat) * TerrainRes - 0.5;
            double fc = (lon + 180) * TerrainRes - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            Span<double> e = stackalloc double[16];
            tiles.Around(r0, c0).Elev.CopyTo(e);
            CoastRefs(e, fr - r0, fc - c0, ref s);
        }

        /// <summary>
        /// LandElev and SeaElev (see TerrainSample) from the 4x4 elevations around a point: each kind's cells keep their
        /// elevation and the others take the kind's mean, weighted by a tent two cells wide around the point (so it changes
        /// smoothly as cells come and go), then bicubic as usual. Where the outline puts land over the grid's sea (a polder,
        /// a fjord's wall the grid had as water) it rises like the land beside it instead of from the sea floor.
        /// </summary>
        static void CoastRefs(Span<double> e, double tr, double tc, ref EarthData.TerrainSample s)
        {
            double lw = 0.01, lsum = 0.01 * 2, sw = 0.01, ssum = 0.01 * -5;
            for (int j = 0; j < 4; j++)
            {
                double wr = Math.Max(0, 1 - Math.Abs(j - 1 - tr) / 2);
                for (int i = 0; i < 4; i++)
                {
                    double w = wr * Math.Max(0, 1 - Math.Abs(i - 1 - tc) / 2), v = e[j * 4 + i];
                    if (v >= 0) { lw += w; lsum += w * v; }
                    else { sw += w; ssum += w * v; }
                }
            }
            double landFill = Math.Max(2, lsum / lw), seaFill = Math.Min(-5, ssum / sw);
            Span<double> l = stackalloc double[16];
            Span<double> o = stackalloc double[16];
            for (int k = 0; k < 16; k++)
            {
                l[k] = e[k] >= 0 ? e[k] : landFill;
                o[k] = e[k] < 0 ? e[k] : seaFill;
            }
            s.LandElev = Bicubic(l, tr, tc);
            s.SeaElev = Bicubic(o, tr, tc);
        }

        static void ConsiderLake(EarthData.Cell c, double w, ref EarthData.TerrainSample s, ref double best)
        {
            if ((c.Flags & EarthData.FlagLake) == 0 || c.Level == EarthData.NoLevel || w <= best) return;
            best = w;
            s.LakeLevel = c.Level;
            s.SaltLake = (c.Flags & EarthData.FlagSalt) != 0;
        }

        static void ConsiderShore(EarthData.Cell c, ref EarthData.TerrainSample s)
        {
            if (c.Level == EarthData.NoLevel) return;
            if (double.IsNaN(s.ShoreLevel) || c.Level > s.ShoreLevel) s.ShoreLevel = c.Level;
        }

        static double NearTo(EarthData.Cell c, double level) => c.Level != EarthData.NoLevel && c.Level == level ? c.Near : 0;

        static double Bit(EarthData.Cell c, byte flag) => (c.Flags & flag) != 0 ? 1 : 0;

        static double Range(Span<double> e, int j0, int i0)
        {
            double min = double.MaxValue, max = double.MinValue;
            for (int j = j0; j < j0 + 3; j++)
            {
                for (int i = i0; i < i0 + 3; i++)
                {
                    double v = e[j * 4 + i];
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
            return max - min;
        }

        static double Bicubic(Span<double> p, double ty, double tx)
        {
            Span<double> col = stackalloc double[4];
            for (int j = 0; j < 4; j++) col[j] = CatmullRom(p[j * 4], p[j * 4 + 1], p[j * 4 + 2], p[j * 4 + 3], tx);
            return CatmullRom(col[0], col[1], col[2], col[3], ty);
        }

        static double CatmullRom(double p0, double p1, double p2, double p3, double t)
        {
            return p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
        }

        /// <summary>Elevation (m), bilinear and without the 4x4 neighbourhood (cheaper; for climate and previews).</summary>
        public double ElevBilinear(double lat, double lon)
        {
            double fr = (90 - lat) * TerrainRes - 0.5;
            double fc = (lon + 180) * TerrainRes - 0.5;
            int r0 = (int)Math.Floor(fr), c0 = (int)Math.Floor(fc);
            double tr = fr - r0, tc = fc - c0;
            return (1 - tr) * ((1 - tc) * GetElev(r0, c0) + tc * GetElev(r0, c0 + 1)) + tr * ((1 - tc) * GetElev(r0 + 1, c0) + tc * GetElev(r0 + 1, c0 + 1));
        }

        public EarthData.Cell NearestCell(double lat, double lon)
        {
            return GetCell((int)Math.Floor((90 - lat) * TerrainRes), (int)Math.Floor((lon + 180) * TerrainRes));
        }
    }
}
