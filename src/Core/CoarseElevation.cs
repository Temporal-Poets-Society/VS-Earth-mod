using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// Built-in land heights from Copernicus GLO-90 averaged to a coarse grid (tools/Glo90Coarse), shipped in the height data
    /// packs. Each grid cell holds the mean of the GLO-90 land points inside it (sea points, exactly 0 m in GLO-90, are left
    /// out, so coasts aren't pulled down); a cell without land holds none. An optional second layer holds the spread of the
    /// GLO-90 heights around this grid's own interpolated surface (root mean square, m) on its own grid.
    ///
    /// Grids: rows are evenly spaced in latitude (RowsPerDegree per degree). Each 1° row of tiles has its own number of
    /// columns per degree (fewer towards the poles, so cells stay about square in metres). A cell covers the GLO-90 points
    /// of one tile split evenly: GLO-90 point (i, j) of tile (latS, lonW) at latitude latS + 1 - i / 1200 and longitude
    /// lonW + j / nativeCols stands for the area half a point around it, so a tile's points cover
    /// latS + 1/2400 .. latS + 1 + 1/2400 and lonW - 1/(2 nativeCols) .. lonW + 1 - 1/(2 nativeCols), and the grid is
    /// shifted by that half point. Interpolation is bicubic (Catmull-Rom) along each row at that row's own spacing, then
    /// across four rows, so it is smooth across tile and band edges. Longitude wraps at ±180°.
    ///
    /// Files: an index (<see cref="IndexFile"/>: grids, height steps, the roughness table and where each tile is) and the
    /// payload parts it names, each read once into memory. Tiles are decoded when first used and kept in a bounded cache.
    /// Thread-safe; results don't depend on threads or cache state. No Vintage Story types.
    /// </summary>
    public sealed partial class CoarseElevation
    {
        /// <summary>Rules and format: 1 = land-only means, bicubic, the index and parts below.</summary>
        public const int Version = 1;
        public const string Prefix = "glo90c";
        public const string IndexFile = Prefix + "-index.bin";
        public const int NativeRowsPerDegree = 1200;
        /// <summary>A decoded height code for a cell without land.</summary>
        public const ushort NoLand = 0xFFFF;
        /// <summary>Metres per degree of latitude on a sphere of the mean Earth radius (6,371 km).</summary>
        public const double MetresPerDegree = 6371000 * Math.PI / 180;

        static readonly byte[] IndexMagic = { (byte)'E', (byte)'G', (byte)'C', (byte)'I' };
        static readonly byte[] PartMagic = { (byte)'E', (byte)'G', (byte)'C', (byte)'P' };

        // ------------------------------------------------------------------------------------------ grids and steps

        /// <summary>A grid: rows per degree of latitude and, per 1° row of tiles (index latS + 90), columns per degree.</summary>
        public sealed partial class Grid
        {
            public int RowsPerDegree;
            /// <summary>Columns per degree for the tiles of each 1° row (index latS + 90).</summary>
            public readonly ushort[] Cols = new ushort[180];
            /// <summary>GLO-90's columns per degree in that row (the half-point shift of the columns).</summary>
            public readonly ushort[] NativeCols = new ushort[180];

            public int Rows => 180 * RowsPerDegree;
            /// <summary>The 1° row (latS + 90) of grid row g (0 = northernmost).</summary>
            public int Band(int g) => 179 - g / RowsPerDegree;
            /// <summary>Fractional grid row of a latitude (row centres at whole numbers).</summary>
            public double RowY(double lat) => (90 + 0.5 / NativeRowsPerDegree - lat) * RowsPerDegree - 0.5;
            public double ColLon(int band, int j) => -180 - 0.5 / NativeCols[band] + (j + 0.5) / Cols[band];
            /// <summary>Fractional column of a longitude in a band, counted from 180° W (column centres at whole numbers).</summary>
            public double ColX(int band, double lon) => (lon + 180 + 0.5 / NativeCols[band]) * Cols[band] - 0.5;
            public int CellsPerTile(int band) => RowsPerDegree * Cols[band];

            internal static Grid Read(BinaryReader r)
            {
                var g = new Grid { RowsPerDegree = r.ReadUInt16() };
                for (int b = 0; b < 180; b++) { g.NativeCols[b] = r.ReadUInt16(); g.Cols[b] = r.ReadUInt16(); }
                if (g.RowsPerDegree <= 0) throw new InvalidDataException("coarse heights: empty grid");
                for (int b = 0; b < 180; b++)
                    if (g.Cols[b] == 0 || g.NativeCols[b] == 0) throw new InvalidDataException("coarse heights: empty grid row");
                return g;
            }
        }

        /// <summary>
        /// Height steps: code 0 is <see cref="Base"/> metres; segment s runs from From[s] with steps of Step[s] metres to the
        /// next segment's start. Codes are whole numbers below <see cref="Codes"/>.
        /// </summary>
        public sealed partial class Steps
        {
            public float Base;
            public float[] From, Step;
            public int Codes;
            double[] codeAt;

            Steps() { }

            void Prepare()
            {
                codeAt = new double[From.Length];
                for (int s = 1; s < From.Length; s++)
                {
                    if (!(From[s] > From[s - 1]) || !(Step[s - 1] > 0)) throw new InvalidDataException("coarse heights: bad steps");
                    codeAt[s] = codeAt[s - 1] + (From[s] - From[s - 1]) / Step[s - 1];
                    if (Math.Abs(codeAt[s] - Math.Round(codeAt[s])) > 1e-6) throw new InvalidDataException("coarse heights: steps don't meet on a code");
                }
                if (!(Step[^1] > 0)) throw new InvalidDataException("coarse heights: bad steps");
            }

            double Position(double metres)
            {
                int s = From.Length - 1;
                while (s > 0 && metres < From[s]) s--;
                return codeAt[s] + (metres - From[s]) / Step[s];
            }

            /// <summary>The nearest code to a height (clamped to the codes there are).</summary>
            public int Code(double metres) => (int)Math.Clamp(Math.Round(Position(metres), MidpointRounding.AwayFromZero), 0, Codes - 1);

            public double Metres(int code)
            {
                int s = From.Length - 1;
                while (s > 0 && code < codeAt[s] - 1e-9) s--;
                return From[s] + (code - codeAt[s]) * Step[s];
            }

            /// <summary>The step (m) at a height.</summary>
            public double StepAt(double metres)
            {
                int s = From.Length - 1;
                while (s > 0 && metres < From[s]) s--;
                return Step[s];
            }

            internal static Steps Read(BinaryReader r)
            {
                int n = r.ReadByte();
                if (n == 0) throw new InvalidDataException("coarse heights: no steps");
                var q = new Steps { From = new float[n], Step = new float[n] };
                for (int s = 0; s < n; s++) { q.From[s] = r.ReadSingle(); q.Step[s] = r.ReadSingle(); }
                q.Base = q.From[0];
                q.Codes = r.ReadUInt16();
                q.Prepare();
                return q;
            }
        }

        // ------------------------------------------------------------------------------------------ tile codec

        /// <summary>LOCO-I's median edge predictor from the west, north and north-west values (0 outside the tile).</summary>
        static int Predict(int[] v, int cols, int r, int c)
        {
            int w = c > 0 ? v[r * cols + c - 1] : 0;
            int n = r > 0 ? v[(r - 1) * cols + c] : 0;
            int nw = r > 0 && c > 0 ? v[(r - 1) * cols + c - 1] : 0;
            int lo = Math.Min(w, n), hi = Math.Max(w, n);
            return nw >= hi ? lo : nw <= lo ? hi : w + n - nw;
        }
        // ------------------------------------------------------------------------------------------ arithmetic coder

        public ref struct RangeDecoder
        {
            readonly ReadOnlySpan<byte> data;
            int pos;
            uint range, code;
            public int Shift;

            public RangeDecoder(ReadOnlySpan<byte> d, int shift)
            {
                data = d; pos = 0; range = 0xFFFFFFFF; code = 0; Shift = shift;
                for (int i = 0; i < 5; i++) code = (code << 8) | Next();
            }

            uint Next() => pos < data.Length ? data[pos++] : 0u;

            public int Bit(ref ushort p)
            {
                uint bound = (range >> 12) * p;
                int bit;
                if (code < bound) { range = bound; p += (ushort)((4096 - p) >> Shift); bit = 0; }
                else { code -= bound; range -= bound; p -= (ushort)(p >> Shift); bit = 1; }
                while (range < (1u << 24)) { range <<= 8; code = (code << 8) | Next(); }
                return bit;
            }

            /// <summary>Whether the decoder read past the data (a damaged payload).</summary>
            public bool Overrun => pos > data.Length + 4;
        }

        /// <summary>
        /// The models for one tile's residuals: per activity context, a zero flag, a sign, the magnitude's bit count in unary
        /// and its bits below the leading one.
        /// </summary>
        sealed partial class ResidualModel
        {
            public const int Contexts = 20, MaxBits = 20;
            public readonly ushort[] Zero = new ushort[Contexts], Sign = new ushort[Contexts];
            public readonly ushort[] Len = new ushort[Contexts * MaxBits];
            public readonly ushort[] Mant = new ushort[Contexts * MaxBits * 2];
            public readonly ushort[] Low = new ushort[MaxBits * MaxBits];
            public readonly ushort[] Mask = new ushort[16];

            public ResidualModel()
            {
                Array.Fill(Zero, (ushort)2048); Array.Fill(Sign, (ushort)2048); Array.Fill(Len, (ushort)2048);
                Array.Fill(Mant, (ushort)2048); Array.Fill(Low, (ushort)2048); Array.Fill(Mask, (ushort)2048);
            }

            /// <summary>The context of an activity value (sum of nearby residual magnitudes and gradients), in log steps.</summary>
            public static int Context(int a)
            {
                if (a < 2) return a;                       // 0, 1
                int b = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)a); // octave: 1 for 2..3, 2 for 4..7 ...
                int half = (a >> (b - 1)) & 1;             // its lower or upper half
                return Math.Min(Contexts - 1, 2 * b + half);
            }

            public int Decode(ref RangeDecoder dec, int ctx)
            {
                if (dec.Bit(ref Zero[ctx]) == 0) return 0;
                bool neg = dec.Bit(ref Sign[ctx]) == 1;
                int nb = 0;
                while (nb < MaxBits - 1 && dec.Bit(ref Len[ctx * MaxBits + nb]) == 1) nb++;
                uint m = 1;
                for (int i = nb - 1; i >= 0; i--)
                {
                    int bit = i == nb - 1 ? dec.Bit(ref Mant[(ctx * MaxBits + nb) * 2])
                            : i == nb - 2 ? dec.Bit(ref Mant[(ctx * MaxBits + nb) * 2 + 1])
                            : dec.Bit(ref Low[nb * MaxBits + i]);
                    m = (m << 1) | (uint)bit;
                }
                return neg ? -(int)m : (int)m;
            }
        }

        /// <summary>How tiles are coded; recorded in the index.</summary>
        public sealed partial class Scheme
        {
            /// <summary>0: median edge (LOCO-I); 1: plane W + N - NW; 2: W plus <see cref="Weights"/> (/64) of N - W, NW - W,
            /// NE - W, WW - W and NN - W.</summary>
            public int Predictor;
            /// <summary>Activity for the contexts: 0, nearby residuals; 1, and nearby gradients.</summary>
            public int Context;
            /// <summary>Probability adaptation (shift).</summary>
            public int Shift;
            public int[] Weights = { 0, 0, 0, 0, 0 };

            /// <summary>Version 1's scheme: least-squares weights fitted on 1,324 tiles' 24-arc-second codes (tools/Glo90Coarse measure2).</summary>
            public static Scheme Default => new() { Predictor = 2, Context = 1, Shift = 5, Weights = new[] { 36, -15, 14, -9, -12 } };

            internal static Scheme Read(BinaryReader r)
            {
                var s = new Scheme { Predictor = r.ReadByte(), Context = r.ReadByte(), Shift = r.ReadByte() };
                s.Weights = new int[r.ReadByte()];
                for (int i = 0; i < s.Weights.Length; i++) s.Weights[i] = r.ReadInt16();
                if (s.Predictor > 2 || s.Context > 1 || s.Shift < 1 || s.Shift > 8 || s.Weights.Length != 5)
                    throw new InvalidDataException("coarse heights: unknown coding");
                return s;
            }
        }

        static int PredictWith(Scheme s, int[] v, int cols, int r, int c)
        {
            if (s.Predictor == 0 || r < 2 || c < 2 || c >= cols - 1) return Predict(v, cols, r, c);
            int w = v[r * cols + c - 1], n = v[(r - 1) * cols + c], nw = v[(r - 1) * cols + c - 1];
            if (s.Predictor == 1) return w + n - nw;
            int ne = v[(r - 1) * cols + c + 1], ww = v[r * cols + c - 2], nn = v[(r - 2) * cols + c];
            var k = s.Weights;
            return w + ((k[0] * (n - w) + k[1] * (nw - w) + k[2] * (ne - w) + k[3] * (ww - w) + k[4] * (nn - w) + 32) >> 6);
        }

        static int Activity(Scheme s, int[] v, int[] err, int cols, int r, int c)
        {
            int eW = c > 0 ? err[r * cols + c - 1] : 0, eN = r > 0 ? err[(r - 1) * cols + c] : 0;
            int eNW = r > 0 && c > 0 ? err[(r - 1) * cols + c - 1] : 0, eNE = r > 0 && c + 1 < cols ? err[(r - 1) * cols + c + 1] : 0;
            int a = 2 * (eW + eN) + eNW + eNE;
            if (s.Context == 1 && r > 0 && c > 0 && c + 1 < cols)
            {
                int w = v[r * cols + c - 1], n = v[(r - 1) * cols + c], nw = v[(r - 1) * cols + c - 1], ne = v[(r - 1) * cols + c + 1];
                a += Math.Abs(w - nw) + Math.Abs(n - nw) + Math.Abs(ne - n);
            }
            return a >> 1;
        }

        public static ushort[] DecodeHeights(ReadOnlySpan<byte> payload, int cols, int rows, Scheme s)
        {
            if (payload.Length < 1) throw new InvalidDataException("coarse heights: tile damaged");
            int n = cols * rows;
            bool allLand = payload[0] == 1;
            int[] v = ArrayPool<int>.Shared.Rent(n), err = ArrayPool<int>.Shared.Rent(n);
            var codes = new ushort[n];
            try
            {
                var m = new ResidualModel();
                var dec = new RangeDecoder(payload.Slice(1), s.Shift);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        bool isLand = true;
                        if (!allLand)
                        {
                            int mc = (c > 0 && codes[i - 1] != NoLand ? 1 : 0) | (r > 0 && codes[i - cols] != NoLand ? 2 : 0)
                                   | (r > 0 && c > 0 && codes[i - cols - 1] != NoLand ? 4 : 0) | (r > 0 && c + 1 < cols && codes[i - cols + 1] != NoLand ? 8 : 0);
                            isLand = dec.Bit(ref m.Mask[mc]) == 1;
                        }
                        if (!isLand) { v[i] = Predict(v, cols, r, c); err[i] = 0; codes[i] = NoLand; continue; }
                        int p = PredictWith(s, v, cols, r, c);
                        int ctx = ResidualModel.Context(Activity(s, v, err, cols, r, c));
                        int e = m.Decode(ref dec, ctx);
                        v[i] = p + e;
                        err[i] = Math.Abs(e);
                        if ((uint)v[i] >= NoLand) throw new InvalidDataException("coarse heights: tile damaged");
                        codes[i] = (ushort)v[i];
                    }
                if (dec.Overrun) throw new InvalidDataException("coarse heights: tile damaged");
                return codes;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(v);
                ArrayPool<int>.Shared.Return(err);
            }
        }

        public static ushort[] DecodeRoughness(ReadOnlySpan<byte> payload, int cols, int rows, Scheme s)
        {
            int n = cols * rows;
            int[] v = ArrayPool<int>.Shared.Rent(n), err = ArrayPool<int>.Shared.Rent(n);
            var codes = new ushort[n];
            try
            {
                var m = new ResidualModel();
                var dec = new RangeDecoder(payload, s.Shift);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c, p = Predict(v, cols, r, c);
                        int ctx = ResidualModel.Context(Activity(s, v, err, cols, r, c));
                        int e = m.Decode(ref dec, ctx);
                        v[i] = p + e;
                        err[i] = Math.Abs(e);
                        if ((uint)v[i] > 0xFF) throw new InvalidDataException("coarse roughness: tile damaged");
                        codes[i] = (ushort)v[i];
                    }
                if (dec.Overrun) throw new InvalidDataException("coarse roughness: tile damaged");
                return codes;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(v);
                ArrayPool<int>.Shared.Return(err);
            }
        }

        // ------------------------------------------------------------------------------------------ files

        /// <summary>One tile's place in the parts: which part, where, and its two payloads' lengths (roughness 0 if none).</summary>
        public struct TileEntry
        {
            public short LatS, LonW;
            public byte Part;
            public int Offset, HeightBytes, RoughBytes;
        }

        /// <summary>Name of payload part n (1-based).</summary>
        public static string PartFile(int n) => Prefix + "-" + n + ".bin";
        public const int PartHeaderBytes = 8;

        // ------------------------------------------------------------------------------------------ the reader

        struct TileRef { public byte[] Data; public int Offset, HeightBytes, RoughBytes; }

        readonly Grid hGrid, rGrid;
        readonly Steps steps;
        readonly Scheme scheme;
        readonly float[] hTable, rTable;
        readonly TileRef[] tiles = new TileRef[180 * 360];

        /// <summary>Latitude spacing of the height grid (arc-seconds).</summary>
        public double SpacingArcSeconds => 3600.0 / hGrid.RowsPerDegree;
        /// <summary>The height grid's north-south cell size in metres.</summary>
        public double CellMetres => MetresPerDegree / hGrid.RowsPerDegree;
        /// <summary>Whether the roughness layer is included.</summary>
        public bool HasRoughness => rGrid != null;
        /// <summary>Latitude spacing of the roughness grid (arc-seconds), 0 without it.</summary>
        public double RoughnessSpacingArcSeconds => rGrid == null ? 0 : 3600.0 / rGrid.RowsPerDegree;
        /// <summary>The converter's note (source, grid, steps).</summary>
        public string Note { get; }
        /// <summary>Tiles held, and the payload bytes kept in memory.</summary>
        public int TileCount { get; }
        public long PayloadBytes { get; }
        /// <summary>Height steps (for tools and tests).</summary>
        public Steps HeightSteps => steps;
        public Grid HeightGrid => hGrid;
        public Grid RoughnessGrid => rGrid;
        /// <summary>The largest step (m) at a height.</summary>
        public double StepAt(double metres) => steps.StepAt(metres);

        /// <summary>Decoded tiles kept (bytes, approximately); 96 MB by default. Older tiles are dropped past it.</summary>
        public long CacheBytes = 96L << 20;
        /// <summary>Tile decodes so far, and the bytes decoded tiles hold now.</summary>
        public long Decodes => Interlocked.Read(ref decodes);
        public long CachedBytes => Interlocked.Read(ref cachedBytes);
        long decodes, cachedBytes, clock;

        sealed class Slot
        {
            public ushort[] Data;
            public long Used;
        }
        readonly ConcurrentDictionary<int, Lazy<Slot>> cache = new();
        readonly object trimGate = new();

        // a few tiles per thread: most lookups fall in the tiles of the lookup before. Keyed by the reader's number, not the
        // reader, so a pool thread doesn't keep an old world's reader (and its payload) alive.
        static int readers;
        readonly int id = Interlocked.Increment(ref readers);
        sealed class Local
        {
            public int Owner;
            public readonly int[] Keys = { -1, -1, -1, -1, -1, -1, -1, -1 };
            public readonly ushort[][] Data = new ushort[8][];
            public int Next;
        }
        [ThreadStatic] static Local local;

        CoarseElevation(byte[] index, Func<int, byte[]> part)
        {
            if (index.Length < 5 || index[0] != IndexMagic[0] || index[1] != IndexMagic[1] || index[2] != IndexMagic[2] || index[3] != IndexMagic[3])
                throw new InvalidDataException("not an Earth coarse height index");
            if (index[4] != Version) throw new InvalidDataException("coarse height index version " + index[4]);
            byte[] body = EarthBinary.UnbrotliAll(index, 5, "coarse height index");
            using var r = new BinaryReader(new MemoryStream(body));
            int parts = r.ReadByte();
            Note = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
            hGrid = Grid.Read(r);
            steps = Steps.Read(r);
            scheme = Scheme.Read(r);
            hTable = new float[steps.Codes];
            for (int k = 0; k < steps.Codes; k++) hTable[k] = (float)steps.Metres(k);
            long at = r.BaseStream.Position;
            if (r.ReadUInt16() != 0)
            {
                r.BaseStream.Position = at;
                rGrid = Grid.Read(r);
                rTable = new float[r.ReadByte()];
                for (int k = 0; k < rTable.Length; k++) rTable[k] = r.ReadSingle();
                if (rTable.Length < 2) throw new InvalidDataException("coarse roughness: no table");
            }
            var data = new byte[parts + 1][];
            for (int p = 1; p <= parts; p++)
            {
                data[p] = part(p);
                if (data[p] == null) throw new FileNotFoundException("Earth coarse heights: missing " + PartFile(p));
                var d = data[p];
                if (d.Length < PartHeaderBytes || d[0] != PartMagic[0] || d[1] != PartMagic[1] || d[2] != PartMagic[2] || d[3] != PartMagic[3]
                    || d[4] != Version || d[5] != p)
                    throw new InvalidDataException("Earth coarse heights: " + PartFile(p) + " isn't part " + p + " of this version");
                PayloadBytes += d.Length;
            }
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                short latS = r.ReadInt16(), lonW = r.ReadInt16();
                byte p = r.ReadByte();
                int off = r.ReadInt32(), hb = r.ReadInt32(), rb = r.ReadInt32();
                if (latS < -90 || latS > 89 || lonW < -180 || lonW > 179 || p < 1 || p > parts || off < PartHeaderBytes || hb <= 0 || rb < 0
                    || (long)off + hb + rb > data[p].Length || (rb > 0) != (rGrid != null))
                    throw new InvalidDataException("coarse heights: index entry damaged");
                tiles[Key(latS, lonW)] = new TileRef { Data = data[p], Offset = off, HeightBytes = hb, RoughBytes = rb };
            }
            TileCount = count;
        }

        /// <summary>From the index and parts as bytes (tools and tests).</summary>
        public static CoarseElevation FromBytes(byte[] index, IReadOnlyList<byte[]> parts) =>
            new CoarseElevation(index, p => p - 1 < parts.Count ? parts[p - 1] : null);

        /// <summary>
        /// The coarse heights from the data files, or null when they aren't there: the index missing, or a part missing or not
        /// installed (open returns null or throws FileNotFoundException). Each file is opened once and read into memory; no
        /// stream is kept. Damaged or foreign files throw InvalidDataException.
        /// </summary>
        public static CoarseElevation TryOpen(Func<string, Stream> open) => TryOpen(open, out _);

        /// <summary>TryOpen, with why it gave null where the index is there but a part isn't (<paramref name="missing"/>; null
        /// when it opened, or when there is no index at all: no packs).</summary>
        public static CoarseElevation TryOpen(Func<string, Stream> open, out string missing)
        {
            missing = null;
            byte[] index = ReadAll(open, IndexFile);
            if (index == null) return null;
            try
            {
                return new CoarseElevation(index, p => ReadAll(open, PartFile(p)) ?? throw new FileNotFoundException(PartFile(p)));
            }
            catch (FileNotFoundException e)
            {
                missing = "the included GLO-90 heights have their index but not all parts (" + e.Message + ")";
                return null;
            }
        }

        static byte[] ReadAll(Func<string, Stream> open, string name)
        {
            Stream s;
            try { s = open(name); }
            catch (FileNotFoundException) { return null; }
            if (s == null) return null;
            using (s) return EarthBinary.ReadAll(s);
        }

        static int Key(int latS, int lonW) => (latS + 90) * 360 + (lonW + 180);

        /// <summary>Whether the packs hold a tile for the square (latS, lonW); squares without one are open sea.</summary>
        public bool HasTile(int latS, int lonW) => latS >= -90 && latS <= 89 && tiles[Key(latS, WrapIndex(lonW + 180, 360) - 180)].Data != null;

        static int WrapIndex(int j, int n) { j %= n; return j < 0 ? j + n : j; }

        static double Wrap180(double lon)
        {
            lon %= 360;
            if (lon >= 180) lon -= 360;
            if (lon < -180) lon += 360;
            return lon;
        }

        Local ThreadLocal()
        {
            var l = local;
            if (l == null || l.Owner != id) local = l = new Local { Owner = id };
            return l;
        }

        ushort[] Tile(int layer, int key) => Tile(layer, key, ThreadLocal());

        ushort[] Tile(int layer, int key, Local l)
        {
            ref readonly TileRef t = ref tiles[key];
            if (t.Data == null) return null;
            int k = layer << 16 | key;
            var keys = l.Keys;
            for (int i = 0; i < keys.Length; i++)
                if (keys[i] == k) return l.Data[i];
            var data = Shared(layer, key);
            keys[l.Next] = k; l.Data[l.Next] = data; l.Next = (l.Next + 1) & 7;
            return data;
        }

        ushort[] Shared(int layer, int key)
        {
            int k = layer << 16 | key;
            var lazy = cache.GetOrAdd(k, _ => new Lazy<Slot>(() => Decode(layer, key), LazyThreadSafetyMode.ExecutionAndPublication));
            var slot = lazy.Value;
            slot.Used = Interlocked.Increment(ref clock);
            if (Interlocked.Read(ref cachedBytes) > CacheBytes) Trim();
            return slot.Data;
        }

        Slot Decode(int layer, int key)
        {
            ref readonly TileRef t = ref tiles[key];
            int band = key / 360;
            Grid g = layer == 0 ? hGrid : rGrid;
            int cols = g.Cols[band], rows = g.RowsPerDegree;
            ushort[] data = layer == 0
                ? DecodeHeights(new ReadOnlySpan<byte>(t.Data, t.Offset, t.HeightBytes), cols, rows, scheme)
                : DecodeRoughness(new ReadOnlySpan<byte>(t.Data, t.Offset + t.HeightBytes, t.RoughBytes), cols, rows, scheme);
            if (layer == 0)
            {
                foreach (ushort c in data)
                    if (c != NoLand && c >= hTable.Length) throw new InvalidDataException("coarse heights: code out of range");
            }
            else
            {
                foreach (ushort c in data)
                    if (c >= rTable.Length) throw new InvalidDataException("coarse roughness: code out of range");
            }
            Interlocked.Increment(ref decodes);
            Interlocked.Add(ref cachedBytes, 2L * data.Length + 64);
            return new Slot { Data = data };
        }

        void Trim()
        {
            if (!Monitor.TryEnter(trimGate)) return;
            try
            {
                if (Interlocked.Read(ref cachedBytes) <= CacheBytes) return;
                var held = new List<(int key, long used, long bytes)>();
                foreach (var kv in cache)
                    if (kv.Value.IsValueCreated) held.Add((kv.Key, kv.Value.Value.Used, 2L * kv.Value.Value.Data.Length + 64));
                held.Sort((a, b) => a.used.CompareTo(b.used));
                long target = CacheBytes * 3 / 4;
                foreach (var h in held)
                {
                    if (Interlocked.Read(ref cachedBytes) <= target) break;
                    if (cache.TryRemove(h.key, out _)) Interlocked.Add(ref cachedBytes, -h.bytes);
                }
            }
            finally { Monitor.Exit(trimGate); }
        }

        /// <summary>Drops every decoded tile (tests).</summary>
        public void ClearCache()
        {
            lock (trimGate)
            {
                foreach (var k in cache.Keys)
                    if (cache.TryRemove(k, out var v) && v.IsValueCreated) Interlocked.Add(ref cachedBytes, -(2L * v.Value.Data.Length + 64));
            }
            local = null;
        }

        /// <summary>A cell's code: grid row g (0 = northernmost), column j from 180° W (wraps); NoLand outside the grid or the
        /// tiles held.</summary>
        ushort Cell(int layer, Grid grid, int g, int j)
        {
            if (g < 0 || g >= grid.Rows) return NoLand;
            int band = grid.Band(g);
            int cols = grid.Cols[band];
            j = WrapIndex(j, 360 * cols);
            int lonIdx = j / cols;
            ushort[] t = Tile(layer, band * 360 + lonIdx);
            if (t == null) return NoLand;
            return t[(g % grid.RowsPerDegree) * cols + (j - lonIdx * cols)];
        }

        /// <summary>
        /// The codes of four cells in a row (columns j .. j + 3, wrapping), NoLand where none; from one tile when they share
        /// it, as they mostly do.
        /// </summary>
        void RowCodes(int layer, Grid grid, int g, int j, Local l, Span<ushort> codes)
        {
            int band = grid.Band(g);
            int cols = grid.Cols[band];
            int jw = WrapIndex(j, 360 * cols);
            int lonIdx = jw / cols, c = jw - lonIdx * cols;
            if (c + 3 < cols)
            {
                ushort[] t = Tile(layer, band * 360 + lonIdx, l);
                if (t == null) { codes.Fill(NoLand); return; }
                int at = (g % grid.RowsPerDegree) * cols + c;
                codes[0] = t[at]; codes[1] = t[at + 1]; codes[2] = t[at + 2]; codes[3] = t[at + 3];
                return;
            }
            for (int m = 0; m < 4; m++) codes[m] = Cell(layer, grid, g, j + m);
        }

        double HeightAt(int g, int j)
        {
            ushort c = Cell(0, hGrid, g, j);
            return c == NoLand ? double.NaN : hTable[c];
        }

        /// <summary>
        /// A value for a cell without land, the same whichever lookup asks: the mean of the land cells beside it (west and
        /// east in its row; the three nearest by longitude in the rows north and south); NaN if none.
        /// </summary>
        double Fill(int g, int j)
        {
            double sum = 0; int n = 0;
            double a = HeightAt(g, j - 1); if (!double.IsNaN(a)) { sum += a; n++; }
            a = HeightAt(g, j + 1); if (!double.IsNaN(a)) { sum += a; n++; }
            double lon = hGrid.ColLon(hGrid.Band(g), j);
            for (int dg = -1; dg <= 1; dg += 2)
            {
                int g2 = g + dg;
                if (g2 < 0 || g2 >= hGrid.Rows) continue;
                int jc = (int)Math.Floor(hGrid.ColX(hGrid.Band(g2), lon) + 0.5);
                for (int dj = -1; dj <= 1; dj++)
                {
                    a = HeightAt(g2, jc + dj);
                    if (!double.IsNaN(a)) { sum += a; n++; }
                }
            }
            return n > 0 ? sum / n : double.NaN;
        }

        static void CatmullRom(double t, Span<double> w)
        {
            double t2 = t * t, t3 = t2 * t;
            w[0] = 0.5 * (-t3 + 2 * t2 - t);
            w[1] = 0.5 * (3 * t3 - 5 * t2 + 2);
            w[2] = 0.5 * (-3 * t3 + 4 * t2 + t);
            w[3] = 0.5 * (t3 - t2);
        }

        /// <summary>The land height (m) at a place, bicubic; false where none of the four nearest cells has land.</summary>
        public bool TryHeight(double lat, double lon, out double metres) => TryHeight(lat, lon, out metres, out _);

        /// <summary>
        /// The land height (m) at a place, bicubic; false where none of the four nearest cells has land (open sea, or a tile
        /// the packs don't hold). Cells without land around a coast take the mean of their land neighbours, so the coast's
        /// heights aren't pulled towards the sea. <paramref name="landShare"/> is the bilinear share of land cells among the
        /// four nearest (0..1, continuous), for a caller that blends towards other data where it falls to 0.
        /// </summary>
        public bool TryHeight(double lat, double lon, out double metres, out double landShare)
        {
            metres = 0; landShare = 0;
            if (!(lat >= -90 && lat <= 90) || !double.IsFinite(lon)) return false;
            lon = Wrap180(lon);
            Grid G = hGrid;
            double y = G.RowY(lat);
            int g0 = (int)Math.Floor(y) - 1;
            double u = y - Math.Floor(y);
            Span<double> v = stackalloc double[16];
            Span<double> t = stackalloc double[4];
            Span<int> j0 = stackalloc int[4];
            Span<ushort> codes = stackalloc ushort[4];
            var l = ThreadLocal();
            for (int k = 0; k < 4; k++)
            {
                int g = g0 + k;
                if (g < 0 || g >= G.Rows) { t[k] = 0; j0[k] = 0; for (int m = 0; m < 4; m++) v[k * 4 + m] = double.NaN; continue; }
                double x = G.ColX(G.Band(g), lon);
                int jf = (int)Math.Floor(x);
                t[k] = x - jf; j0[k] = jf - 1;
                RowCodes(0, G, g, jf - 1, l, codes);
                for (int m = 0; m < 4; m++) v[k * 4 + m] = codes[m] == NoLand ? double.NaN : hTable[codes[m]];
            }
            double l5 = double.IsNaN(v[5]) ? 0 : 1, l6 = double.IsNaN(v[6]) ? 0 : 1, l9 = double.IsNaN(v[9]) ? 0 : 1, l10 = double.IsNaN(v[10]) ? 0 : 1;
            landShare = (1 - u) * ((1 - t[1]) * l5 + t[1] * l6) + u * ((1 - t[2]) * l9 + t[2] * l10);
            if (landShare <= 0) { landShare = 0; return false; }

            bool complete = true;
            for (int i = 0; i < 16; i++)
            {
                if (!double.IsNaN(v[i])) continue;
                int k = i >> 2, g = g0 + k;
                if (g >= 0 && g < G.Rows) v[i] = Fill(g, j0[k] + (i & 3));
                if (double.IsNaN(v[i])) complete = false;
            }
            if (!complete)
            {
                // inner cells still without a value (rare, where rows of different spacing meet): the mean of the inner ones
                double s = 0; int n = 0;
                foreach (int i in Inner) if (!double.IsNaN(v[i])) { s += v[i]; n++; }
                if (n == 0) { landShare = 0; return false; }
                foreach (int i in Inner) if (double.IsNaN(v[i])) v[i] = s / n;
                // outer cells of the inner rows: the inner cell beside them; outer rows: the inner row beside them
                for (int k = 1; k <= 2; k++)
                {
                    if (double.IsNaN(v[k * 4])) v[k * 4] = v[k * 4 + 1];
                    if (double.IsNaN(v[k * 4 + 3])) v[k * 4 + 3] = v[k * 4 + 2];
                }
                for (int m = 0; m < 4; m++)
                {
                    if (double.IsNaN(v[m])) v[m] = v[4 + m];
                    if (double.IsNaN(v[12 + m])) v[12 + m] = v[8 + m];
                }
            }
            metres = Bicubic(v, t, u);
            return true;
        }

        static readonly int[] Inner = { 5, 6, 9, 10 };

        static double Bicubic(Span<double> v, Span<double> t, double u)
        {
            Span<double> w = stackalloc double[4];
            Span<double> row = stackalloc double[4];
            for (int k = 0; k < 4; k++)
            {
                CatmullRom(t[k], w);
                row[k] = w[0] * v[k * 4] + w[1] * v[k * 4 + 1] + w[2] * v[k * 4 + 2] + w[3] * v[k * 4 + 3];
            }
            CatmullRom(u, w);
            return w[0] * row[0] + w[1] * row[1] + w[2] * row[2] + w[3] * row[3];
        }

        /// <summary>
        /// The spread (root mean square, m) of GLO-90's heights around this grid's interpolated surface near a place, bicubic
        /// between the roughness cells and never below 0. False without the layer, or where none of the four nearest cells
        /// lies in a tile the packs hold. Cells without land read 0 m.
        /// </summary>
        public bool TryRoughness(double lat, double lon, out double metres)
        {
            metres = 0;
            if (rGrid == null || !(lat >= -90 && lat <= 90) || !double.IsFinite(lon)) return false;
            lon = Wrap180(lon);
            Grid G = rGrid;
            double y = G.RowY(lat);
            int g0 = (int)Math.Floor(y) - 1;
            double u = y - Math.Floor(y);
            Span<double> v = stackalloc double[16];
            Span<double> t = stackalloc double[4];
            int held = 0;
            Span<ushort> codes = stackalloc ushort[4];
            var l = ThreadLocal();
            for (int k = 0; k < 4; k++)
            {
                int g = g0 + k;
                if (g < 0 || g >= G.Rows) { t[k] = 0; for (int m = 0; m < 4; m++) v[k * 4 + m] = 0; continue; }
                double x = G.ColX(G.Band(g), lon);
                int jf = (int)Math.Floor(x);
                t[k] = x - jf;
                RowCodes(1, G, g, jf - 1, l, codes);
                for (int m = 0; m < 4; m++)
                {
                    ushort c = codes[m];
                    v[k * 4 + m] = c == NoLand ? 0 : rTable[c];
                    if (c != NoLand && (k == 1 || k == 2) && (m == 1 || m == 2)) held++;
                }
            }
            if (held == 0) return false;
            metres = Math.Max(0, Bicubic(v, t, u));
            return true;
        }
    }
}
