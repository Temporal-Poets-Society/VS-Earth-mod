using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// rivers-flow-glo90-1.bin (tools/bake_river_flow.py; decision 0069 rules 1, 2 and 4): the flow of the water mask's
    /// river water, solved once per 1° tile from the river courses (courses rules 4) and their discharges: per 90 m pixel
    /// a speed (cm/s; 0 where the water carries no flow: side arms, backwaters, canals) and a direction; and the mouth
    /// reaches the bake rerouted through their own water to their own mouth (a course that went on to the sea across land
    /// while its water reached the sea elsewhere: the Lielupe at 23.85° E, the Daugava at Vecāķi). Solved on one course
    /// list (CourseKey); a world on another list reads no reroutes from it. Travels with the GLO-90 heights, on the data
    /// release beside the outlines.
    /// </summary>
    public sealed class RiverFlowData
    {
        public const int FileVersion = 1;
        public const string FileName = "rivers-flow-glo90-1.bin";
        public const string Magic = "EMFL";
        /// <summary>The file this build reads (from the global bake): any other is refused (EarthElevationSystem fetches it
        /// again).</summary>
        public const long FileBytes = 93_803_882;
        public const string FileSha256 = "d6cb96dd8d7c60e11b11ea9c97dbdd666fbef25c5db583f94f11a32f0f4f1954";
        public const byte NoData = 255, NoFlow = 0, HasFlow = 1;

        /// <summary>The course list (DamRemovalData.KeyOf) the flow was solved on.</summary>
        public ulong CourseKey { get; private set; }
        readonly byte[] file;
        readonly byte[] states;
        readonly int[] offset, length;
        readonly TileCache<Tile> tiles;
        /// <summary>The rerouted mouth reaches: reach -> (the vertex after which the new end starts, its latitudes and
        /// longitudes).</summary>
        public readonly Dictionary<int, (int After, float[] Lat, float[] Lon)> Reroutes = new();

        sealed class Tile { public int H, W; public byte[] Speed, Dir; }

        public static bool IsExpected(byte[] bytes, out string why) => LakeOutlineData.Matches(bytes, FileBytes, FileSha256, out why);

        RiverFlowData(byte[] file)
        {
            this.file = file;
            EarthBinary.Expect(file, Magic, 36, FileName);
            if (BitConverter.ToInt32(file, 4) != FileVersion) throw new InvalidDataException(FileName + ": version " + BitConverter.ToInt32(file, 4));
            int deg = BitConverter.ToInt32(file, 8), tx = BitConverter.ToInt32(file, 12), ty = BitConverter.ToInt32(file, 16);
            if (deg != 1 || tx != 360 || ty != 180) throw new InvalidDataException(FileName + ": layout");
            CourseKey = BitConverter.ToUInt64(file, 20);
            int trailer = BitConverter.ToInt32(file, 28);
            int o = 32;
            if (file.Length < o + tx * ty) throw new InvalidDataException(FileName + ": cut short");
            states = new byte[tx * ty];
            Buffer.BlockCopy(file, o, states, 0, states.Length);
            o += states.Length;
            offset = new int[states.Length];
            length = new int[states.Length];
            for (int t = 0; t < states.Length; t++)
            {
                if (states[t] != HasFlow) continue;
                if (o + 8 > file.Length) throw new InvalidDataException(FileName + ": index cut short");
                offset[t] = BitConverter.ToInt32(file, o);
                length[t] = BitConverter.ToInt32(file, o + 4);
                if (offset[t] < 0 || length[t] < 0 || (long)offset[t] + length[t] > file.Length) throw new InvalidDataException(FileName + ": bad tile index");
                o += 8;
            }
            if (trailer < o || trailer + 4 > file.Length) throw new InvalidDataException(FileName + ": bad reroute offset");
            int n = BitConverter.ToInt32(file, trailer), p = trailer + 4;
            for (int k = 0; k < n; k++)
            {
                if (p + 12 > file.Length) throw new InvalidDataException(FileName + ": reroutes cut short");
                int reach = BitConverter.ToInt32(file, p), after = BitConverter.ToInt32(file, p + 4), m = BitConverter.ToInt32(file, p + 8);
                p += 12;
                if (m < 1 || p + 8L * m > file.Length) throw new InvalidDataException(FileName + ": bad reroute");
                var la = new float[m];
                var lo = new float[m];
                for (int i = 0; i < m; i++, p += 8) { la[i] = BitConverter.ToSingle(file, p); lo[i] = BitConverter.ToSingle(file, p + 4); }
                Reroutes[reach] = (after, la, lo);
            }
            tiles = new TileCache<Tile>(Decode, 24);
        }

        public static RiverFlowData Read(byte[] file) => new(file);

        /// <summary>
        /// Rule 2: each rerouted mouth reach keeps its vertices up to the one its new end starts after, then follows the new
        /// end through its water to its own mouth (levels easing from that vertex's to its old mouth's, the last one fixed
        /// as the old mouth was, the water mask's half width of that vertex kept). Returns how many reaches it rerouted.
        /// </summary>
        public static int Reroute(List<RiverReach> list, RiverFlowData flow)
        {
            int n = 0;
            foreach (var (reach, (after, lat, lon)) in flow.Reroutes)
            {
                if (reach < 0 || reach >= list.Count) continue;
                var re = list[reach];
                if (re.Lat == null || after < 0 || after >= re.Lat.Length - 1) continue;
                int keep = after + 1, m = lat.Length, len = keep + m;
                var la = new float[len]; var lo = new float[len]; var lv = new short[len]; var fx = new bool[len];
                Array.Copy(re.Lat, la, keep); Array.Copy(re.Lon, lo, keep);
                Array.Copy(re.Level, lv, keep); Array.Copy(re.Fixed, fx, keep);
                short l0 = re.Level[after], l1 = re.Level[^1];
                for (int i = 0; i < m; i++)
                {
                    la[keep + i] = lat[i]; lo[keep + i] = lon[i];
                    lv[keep + i] = (short)Math.Round(l0 + (l1 - l0) * (i + 1.0) / m);
                }
                fx[len - 1] = re.Fixed[^1];
                byte[] w = null;
                if (re.Width != null)
                {
                    w = new byte[len];
                    Array.Copy(re.Width, w, keep);
                    for (int i = keep; i < len; i++) w[i] = re.Width[after];
                }
                re.Lat = la; re.Lon = lo; re.Level = lv; re.Fixed = fx; re.Width = w;
                n++;
            }
            return n;
        }

        Tile Decode(int t)
        {
            byte[] raw = EarthBinary.UnbrotliAll(file, offset[t], length[t], FileName);
            int h = BitConverter.ToInt32(raw, 0), w = BitConverter.ToInt32(raw, 4);
            if (h <= 0 || w <= 0 || raw.Length != 8 + 2L * h * w) throw new InvalidDataException(FileName + ": bad tile");
            var tile = new Tile { H = h, W = w, Speed = new byte[h * w], Dir = new byte[h * w] };
            Buffer.BlockCopy(raw, 8, tile.Speed, 0, h * w);
            Buffer.BlockCopy(raw, 8 + h * w, tile.Dir, 0, h * w);
            return tile;
        }

        /// <summary>Whether the file has this place's tile (with flow or none: NoFlow is still water everywhere in it).</summary>
        public bool Covers(double lat, double lon) => State(lat, lon, out _, out _, out _) != NoData;

        byte State(double lat, double lon, out int t, out int lat0, out int lon0)
        {
            lat0 = (int)Math.Floor(lat);
            lon0 = (int)Math.Floor(lon);
            if (lon0 >= 180) lon0 -= 360; else if (lon0 < -180) lon0 += 360;
            t = -1;
            if (lat0 < -90 || lat0 >= 90) return NoData;
            t = (lat0 + 90) * 360 + lon0 + 180;
            return states[t];
        }

        /// <summary>
        /// The flow at a place (m/s east and north), interpolated between the four nearest pixels' centres (a pixel without
        /// flow counts as none, so the flow slows towards the banks): false where the file has no tile there.
        /// </summary>
        public bool At(double lat, double lon, out double east, out double north) => At(lat, lon, false, out east, out north);

        /// <summary>
        /// The same; with <paramref name="acrossTiles"/> (GenFixRules 5, EA-0111) a corner pixel beyond this tile's edge is
        /// read from the tile that holds it, as one grid: before, the four pixels were clamped to the place's own tile, so
        /// the flow stepped at every whole degree (a straight seam across wide rivers at 1:10: the Amazon on 3° S, the Lena
        /// on 130° E).
        /// </summary>
        public bool At(double lat, double lon, bool acrossTiles, out double east, out double north)
        {
            east = north = 0;
            byte st = State(lat, lon, out int t, out int lat0, out int lon0);
            if (st == NoData) return false;
            if (st == NoFlow) return true;
            var tile = tiles.Get(t);
            double fi = (lat0 + 1 - lat) * tile.H - 0.5, fj = (lon - lon0) * tile.W - 0.5;
            int i0 = (int)Math.Floor(fi), j0 = (int)Math.Floor(fj);
            double a = fi - i0, b = fj - j0;
            for (int di = 0; di <= 1; di++)
                for (int dj = 0; dj <= 1; dj++)
                {
                    int i = i0 + di, j = j0 + dj;
                    double v, ang;
                    byte next = HasFlow;
                    if (acrossTiles && (i < 0 || i >= tile.H || j < 0 || j >= tile.W))
                        // the pixel's centre in degrees, from the neighbouring tile: still water there fades the flow out;
                        // where the file has no tile (or beyond a pole) the edge pixel stands in, as before
                        next = PixelAt(lat0 + 1 - (i + 0.5) / tile.H, lon0 + (j + 0.5) / tile.W, out v, out ang);
                    else v = ang = 0;
                    if (next == NoFlow) continue;
                    if (!acrossTiles || next == NoData || i >= 0 && i < tile.H && j >= 0 && j < tile.W)
                    {
                        i = Math.Clamp(i, 0, tile.H - 1); j = Math.Clamp(j, 0, tile.W - 1);
                        int k = i * tile.W + j;
                        v = tile.Speed[k] / 100.0;
                        ang = tile.Dir[k] * (Math.PI / 128);
                    }
                    if (v <= 0) continue;
                    double wgt = (di == 0 ? 1 - a : a) * (dj == 0 ? 1 - b : b);
                    east += wgt * v * Math.Cos(ang);
                    north += wgt * v * Math.Sin(ang);
                }
            return true;
        }

        /// <summary>The speed (m/s) and direction (radians) of the pixel holding a place, and its tile's state: HasFlow,
        /// NoFlow (still water) or NoData (no tile, or beyond a pole).</summary>
        byte PixelAt(double lat, double lon, out double speed, out double angle)
        {
            speed = angle = 0;
            if (lat >= 90 || lat < -90) return NoData;
            lon = ((lon + 180) % 360 + 360) % 360 - 180;
            byte st = State(lat, lon, out int t, out int lat0, out int lon0);
            if (st != HasFlow) return st;
            var tile = tiles.Get(t);
            int i = Math.Clamp((int)Math.Floor((lat0 + 1 - lat) * tile.H), 0, tile.H - 1), j = Math.Clamp((int)Math.Floor((lon - lon0) * tile.W), 0, tile.W - 1);
            int k = i * tile.W + j;
            speed = tile.Speed[k] / 100.0;
            angle = tile.Dir[k] * (Math.PI / 128);
            return HasFlow;
        }
    }
}
