using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EarthMap.Core
{
    /// <summary>
    /// The soil a place offers plants, on a 0.1 degree grid from SoilGrids' 2' soil groups and topsoil pH (the ground pack's
    /// global-soilgrids tiles), each cell over the 0.2 degree square around its centre. Earth ships it
    /// (assets/earth/config/soil-site.bin) so the fire rules and the trees' soil fit work without the optional ground pack;
    /// <see cref="Bake"/> rebuilds it from the tiles (ZoneTests checks it does). Layers, 0..250 per cell (255: no soil mapped):
    /// <list type="bullet">
    /// <item><see cref="Grass"/>: share of grassland soils (<see cref="GrassSoils"/>), built under grass by fire and drought;</item>
    /// <item><see cref="Wet"/>: share of waterlogged and floodplain soils (<see cref="WetSoils"/>);</item>
    /// <item><see cref="Poor"/>: share of leached, sandy and peat soils poor in nutrients (<see cref="PoorSoils"/>);</item>
    /// <item><see cref="Ph"/>: mean topsoil pH times 20.</item>
    /// </list>
    /// File: "ESS1", west, north, step (doubles), columns, rows, layers (ints), each layer's name (length byte, ASCII), then
    /// the layers' cells one after another, rows from the north, deflated.
    /// </summary>
    public sealed class SoilSiteGrid
    {
        public const string Asset = "earth:config/soil-site.bin";
        const string Magic = "ESS1";
        public const byte NoSoil = 255;
        public const int Grass = 0, Wet = 1, Poor = 2, Ph = 3;
        public static readonly string[] LayerNames = { "grass", "wet", "poor", "ph" };
        public static readonly string[] GrassSoils = { "Chernozems", "Phaeozems", "Kastanozems" };
        public static readonly string[] WetSoils = { "Gleysols", "Histosols", "Stagnosols", "Planosols", "Fluvisols" };
        // leached sands, peat and the old tropical soils; not the loamy, acid-topped Albeluvisols (Retisols) of the Baltic and
        // Russian forests, nor Alisols and Umbrisols, which hold more nutrients than their acidity suggests
        public static readonly string[] PoorSoils = { "Podzols", "Arenosols", "Histosols", "Ferralsols", "Acrisols", "Plinthosols" };
        public const string SoilField = "soil_group", PhField = "ph";

        public double West { get; private set; }
        public double North { get; private set; }
        public double Step { get; private set; }
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        byte[][] layers;

        SoilSiteGrid() { }

        public static SoilSiteGrid Read(byte[] file)
        {
            if (file == null || file.Length < 40 || Encoding.ASCII.GetString(file, 0, 4) != Magic) throw new FormatException("not a soil site grid (ESS1)");
            var g = new SoilSiteGrid
            {
                West = BitConverter.ToDouble(file, 4), North = BitConverter.ToDouble(file, 12), Step = BitConverter.ToDouble(file, 20),
                Cols = BitConverter.ToInt32(file, 28), Rows = BitConverter.ToInt32(file, 32),
            };
            int count = BitConverter.ToInt32(file, 36);
            if (g.Cols <= 0 || g.Rows <= 0 || !(g.Step > 0) || (long)g.Cols * g.Rows > 50_000_000 || count < LayerNames.Length || count > 32)
                throw new FormatException("soil site grid has a bad header");
            int o = 40;
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (o >= file.Length || o + 1 + file[o] > file.Length) throw new FormatException("soil site grid is truncated");
                names[i] = Encoding.ASCII.GetString(file, o + 1, file[o]);
                o += 1 + file[o];
            }
            for (int i = 0; i < LayerNames.Length; i++)
                if (names[i] != LayerNames[i]) throw new FormatException("soil site grid layer " + i + " is " + names[i] + ", not " + LayerNames[i]);
            g.layers = new byte[count][];
            using var z = new DeflateStream(new MemoryStream(file, o, file.Length - o), CompressionMode.Decompress);
            for (int i = 0; i < count; i++)
            {
                var cells = g.layers[i] = new byte[g.Cols * g.Rows];
                int read = 0, n;
                while (read < cells.Length && (n = z.Read(cells, read, cells.Length - read)) > 0) read += n;
                if (read != cells.Length) throw new FormatException("soil site grid is truncated");
            }
            return g;
        }

        public byte[] Write()
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(Magic));
            ms.Write(BitConverter.GetBytes(West)); ms.Write(BitConverter.GetBytes(North)); ms.Write(BitConverter.GetBytes(Step));
            ms.Write(BitConverter.GetBytes(Cols)); ms.Write(BitConverter.GetBytes(Rows)); ms.Write(BitConverter.GetBytes(layers.Length));
            foreach (var name in LayerNames) { ms.WriteByte((byte)name.Length); ms.Write(Encoding.ASCII.GetBytes(name)); }
            using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, true)) foreach (var l in layers) z.Write(l);
            return ms.ToArray();
        }

        /// <summary>The grid from SoilGrids' tiles (an EGTS set with "soil_group" categories and "ph" values), read at the
        /// tiles' own cell centres (no warp), so the same tiles always give the same bytes.</summary>
        public static SoilSiteGrid Bake(byte[] soilTiles, double step = 0.1, double south = -60, double north = 84)
        {
            var tiles = GroundTile.ReadAll(soilTiles);
            var g = new SoilSiteGrid { West = -180, North = north, Step = step, Cols = (int)Math.Round(360 / step), Rows = (int)Math.Round((north - south) / step) };
            double fine = double.NaN;
            foreach (var t in tiles) if (double.IsNaN(fine) || t.Step < fine) fine = t.Step;
            int fcols = (int)Math.Round(360 / fine), frows = (int)Math.Round((north - south) / fine);
            // each fine cell: soil flags (bit 0 mapped, 1 grass, 2 wet, 3 poor) and pH (0: none)
            var flags = new byte[(long)fcols * frows];
            var ph = new float[(long)fcols * frows];
            var grass = new HashSet<string>(GrassSoils, StringComparer.Ordinal);
            var wet = new HashSet<string>(WetSoils, StringComparer.Ordinal);
            var poor = new HashSet<string>(PoorSoils, StringComparer.Ordinal);
            foreach (var t in tiles)
            {
                GroundTile.Field soil = null, acidity = null;
                foreach (var x in t.Fields)
                {
                    if (x.Name == SoilField && x.Category) soil = x;
                    if (x.Name == PhField && !x.Category) acidity = x;
                }
                if (soil == null && acidity == null) continue;
                for (int j = 0; j < t.Ny; j++)
                {
                    int r = (int)Math.Floor((north - (t.North - (j + 0.5) * t.Step)) / fine);
                    if (r < 0 || r >= frows) continue;
                    for (int i = 0; i < t.Nx; i++)
                    {
                        int c = (int)Math.Floor((t.West + (i + 0.5) * t.Step + 180) / fine);
                        if (c < 0 || c >= fcols) continue;
                        long k = (long)r * fcols + c;
                        string name = soil == null ? null : t.CategoryAtCell(soil, i, j);
                        if (name != null)
                            flags[k] = (byte)(1 | (grass.Contains(name) ? 2 : 0) | (wet.Contains(name) ? 4 : 0) | (poor.Contains(name) ? 8 : 0));
                        double v = acidity == null ? double.NaN : t.ValueAtCell(acidity, i, j);
                        if (v > 2 && v < 11) ph[k] = (float)v;
                    }
                }
            }
            int cols = g.Cols, rows = g.Rows, half = (int)Math.Round(step / fine);
            g.layers = new byte[LayerNames.Length][];
            for (int l = 0; l < LayerNames.Length; l++) g.layers[l] = new byte[cols * rows];
            for (int r = 0; r < rows; r++)
            {
                int fr0 = (int)Math.Round((r + 0.5) * step / fine) - half;
                for (int c = 0; c < cols; c++)
                {
                    int fc0 = (int)Math.Round((c + 0.5) * step / fine) - half;
                    int n = 0, ng = 0, nw = 0, np = 0, nph = 0;
                    double sph = 0;
                    for (int y = fr0; y < fr0 + 2 * half; y++)
                    {
                        if (y < 0 || y >= frows) continue;
                        for (int x = fc0; x < fc0 + 2 * half; x++)
                        {
                            long k = (long)y * fcols + (x % fcols + fcols) % fcols;
                            byte f = flags[k];
                            if ((f & 1) != 0) { n++; if ((f & 2) != 0) ng++; if ((f & 4) != 0) nw++; if ((f & 8) != 0) np++; }
                            if (ph[k] > 0) { nph++; sph += ph[k]; }
                        }
                    }
                    int at = r * cols + c;
                    g.layers[Grass][at] = n == 0 ? NoSoil : (byte)Math.Round(250.0 * ng / n);
                    g.layers[Wet][at] = n == 0 ? NoSoil : (byte)Math.Round(250.0 * nw / n);
                    g.layers[Poor][at] = n == 0 ? NoSoil : (byte)Math.Round(250.0 * np / n);
                    g.layers[Ph][at] = nph == 0 ? NoSoil : (byte)Math.Clamp(Math.Round(20 * sph / nph), 0, 250);
                }
            }
            return g;
        }

        /// <summary>A layer's value here, bilinear between cell centres (shares 0..1, pH in pH units); NaN where no soil is
        /// mapped around (sea, ice).</summary>
        public double Value(int layer, double lat, double lon)
        {
            var cells = layers[layer];
            double fx = (lon - West) / Step - 0.5, fy = (North - lat) / Step - 0.5;
            if (fy < -0.5 || fy > Rows - 0.5) return double.NaN;
            fy = Math.Clamp(fy, 0, Rows - 1);
            int x0 = (int)Math.Floor(fx), y0 = Math.Min((int)fy, Rows - 1), y1 = Math.Min(y0 + 1, Rows - 1);
            double tx = fx - x0, ty = fy - y0, sum = 0, weight = 0;
            void Add(int x, int y, double w)
            {
                if (w <= 0) return;
                byte v = cells[y * Cols + ((x % Cols) + Cols) % Cols];
                if (v == NoSoil) return;
                sum += v * w; weight += w;
            }
            Add(x0, y0, (1 - tx) * (1 - ty)); Add(x0 + 1, y0, tx * (1 - ty)); Add(x0, y1, (1 - tx) * ty); Add(x0 + 1, y1, tx * ty);
            if (weight < 1e-9) return double.NaN;
            return sum / weight / (layer == Ph ? 20.0 : 250.0);
        }

        /// <summary>0..1: the grassland-soil share here; 0 where no soil is mapped.</summary>
        public double GrassShare(double lat, double lon)
        {
            double v = Value(Grass, lat, lon);
            return double.IsNaN(v) ? 0 : v;
        }

        /// <summary>The stored byte of a cell (tests).</summary>
        public byte Cell(int layer, int row, int col) => layers[layer][row * Cols + col];
    }
}
