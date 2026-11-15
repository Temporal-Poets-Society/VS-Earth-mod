using System;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// CHELSA v2.1 temperatures on the 2' terrain grid (temperature.bin, tools/bake_chelsa.py): the yearly mean, coldest and
    /// warmest month of 1981-2010, reduced to sea level with 6.5 °C/km from CHELSA's own height for each cell. Unlike the 0.25°
    /// climate grid, they follow the mountains: Hawaii's summits above the trade-wind inversion, the Andes' valleys
    /// (decision 0009, climate rules v2). The file is a TiledGrid of 7 bytes a cell (three planes of 16-bit values, then a
    /// byte that is 0 where CHELSA has none): plain, the values as planar-predicted residuals; predicted (PlaneCoding, from
    /// rc.14), the values themselves after the grid's decoding. Its tiles are decoded on demand into the three temperatures
    /// and kept in a TileCache. Thread-safe.
    /// </summary>
    public sealed class FineTemperature
    {
        public int Width => grid.Width;
        public int Height => grid.Height;
        public int Res => grid.Res;
        readonly TiledGrid grid;
        readonly int tile;
        readonly TileCache<Tile> tiles;
        const int MaxCachedTiles = 48;

        sealed class Tile
        {
            /// <summary>Sea-level yearly mean, coldest and warmest month (°C × 10), short.MinValue where CHELSA has none.</summary>
            public short[] Mean, Cold, Warm;
        }

        FineTemperature(byte[] file)
        {
            // the decoded temperatures are cached here, not the grid's raw bytes
            grid = new TiledGrid(file, "EMTP", 7, 0, "temperature.bin", new[] { PlaneType.I16, PlaneType.I16, PlaneType.I16, PlaneType.U8 });
            if (grid.Version != 1) throw new InvalidDataException("temperature.bin: layout mismatch");
            tile = grid.Tile;
            tiles = new TileCache<Tile>(Decode, MaxCachedTiles);
        }

        public static FineTemperature Read(Stream s) => new FineTemperature(EarthBinary.ReadAll(s));

        /// <summary>The sea-level temperatures (°C) of a grid cell; false where CHELSA has none (north of 84°N). Rows are clamped
        /// at the poles, columns wrap.</summary>
        public bool Cell(int row, int col, out float mean, out float cold, out float warm)
        {
            var t = tiles.Get(grid.KeyOf(row, col, out int i));
            short m = t.Mean[i];
            if (m == short.MinValue)
            {
                mean = cold = warm = float.NaN;
                return false;
            }
            mean = m / 10f;
            cold = t.Cold[i] / 10f;
            warm = t.Warm[i] / 10f;
            return true;
        }

        /// <summary>
        /// The four cells (r0, c0), (r0, c0 + 1), (r0 + 1, c0), (r0 + 1, c0 + 1) at once, at index dr * 2 + dc (NaN where CHELSA has
        /// none): the same values as <see cref="Cell"/>, with one tile lookup and its divisions for the four (they nearly always
        /// share a tile) instead of four.
        /// </summary>
        public void Quad(int r0, int c0, Span<float> mean, Span<float> cold, Span<float> warm)
        {
            int ra = Math.Clamp(r0, 0, Height - 1), rb = Math.Clamp(r0 + 1, 0, Height - 1);
            int ca = c0 % Width;
            if (ca < 0) ca += Width;
            int cb = ca + 1 == Width ? 0 : ca + 1;
            int tr = ra / tile, tc = ca / tile;
            if (rb / tile != tr || cb / tile != tc)
            {
                for (int k = 0; k < 4; k++)
                {
                    Cell(k < 2 ? ra : rb, (k & 1) == 0 ? ca : cb, out mean[k], out cold[k], out warm[k]);
                }
                return;
            }
            var t = tiles.Get(grid.KeyOf(ra, ca, out int i00));
            int down = (rb - ra) * tile;
            Put(t, i00, 0, mean, cold, warm);
            Put(t, i00 + 1, 1, mean, cold, warm);
            Put(t, i00 + down, 2, mean, cold, warm);
            Put(t, i00 + down + 1, 3, mean, cold, warm);
        }

        static void Put(Tile t, int i, int k, Span<float> mean, Span<float> cold, Span<float> warm)
        {
            short m = t.Mean[i];
            if (m == short.MinValue)
            {
                mean[k] = cold[k] = warm[k] = float.NaN;
                return;
            }
            mean[k] = m / 10f;
            cold[k] = t.Cold[i] / 10f;
            warm[k] = t.Warm[i] / 10f;
        }

        Tile Decode(int key)
        {
            int n = tile * tile;
            var raw = grid.Decode(key);
            // a tile without data (the format allows it; temperature.bin has none) is a tile without CHELSA
            if (raw.Length == 0)
            {
                var none = new short[n];
                Array.Fill(none, short.MinValue);
                return new Tile { Mean = none, Cold = none, Warm = none };
            }
            var t = grid.Coding == PlaneCoding.Predicted
                ? new Tile { Mean = Values(raw, 0), Cold = Values(raw, 2 * n), Warm = Values(raw, 4 * n) }
                : new Tile { Mean = UnPaeth(raw, 0), Cold = UnPaeth(raw, 2 * n), Warm = UnPaeth(raw, 4 * n) };
            for (int i = 0; i < n; i++)
                if (raw[6 * n + i] == 0) t.Mean[i] = short.MinValue;
            return t;
        }

        /// <summary>A plane of 16-bit values as the predicted grid gives them.</summary>
        short[] Values(byte[] raw, int at)
        {
            var q = new short[tile * tile];
            Buffer.BlockCopy(raw, at, q, 0, 2 * q.Length);
            return q;
        }

        /// <summary>Undoes the planar prediction (left + up - upleft) of the plain file's bake.</summary>
        short[] UnPaeth(byte[] raw, int at)
        {
            var q = new short[tile * tile];
            for (int r = 0; r < tile; r++)
            {
                for (int c = 0; c < tile; c++)
                {
                    int i = r * tile + c;
                    int a = c > 0 ? q[i - 1] : 0, b = r > 0 ? q[i - tile] : 0, ab = c > 0 && r > 0 ? q[i - tile - 1] : 0;
                    q[i] = (short)(BitConverter.ToInt16(raw, at + 2 * i) + a + b - ab);
                }
            }
            return q;
        }
    }
}
