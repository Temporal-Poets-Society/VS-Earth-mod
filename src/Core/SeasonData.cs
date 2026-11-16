using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace EarthMap.Core
{
    /// <summary>
    /// The monthly temperature layer (EA-0144; the weather session's proposal of 16 November 2026, the author's "better to have
    /// just one data layer and have it then 2′ cells for each month + correct daily and monthly spreads"): for each cell of
    /// Earth's 2′ terrain grid and each month, the mean air temperature reduced to sea level (6.5 °C/km from the source's own
    /// height, as temperature.bin), the day-night range (mean daily maximum minus minimum) and the day-to-day spread (the
    /// standard deviation of the daily mean within the month), and a provenance byte. Baked by tools/bake_seasons.py (the
    /// weather session's) and written by tools/write_seasons.py into <see cref="FileName"/>: the TiledGrid layout (header,
    /// tile index, tiles of predicted planes, <see cref="PlaneCoding"/>). At 35-45 MB it is too big for the mod's zip: a
    /// data-release file the server fetches.
    ///
    /// Tiles come from one of three sources. On the server, the file on disk (<see cref="Open"/>): only the header and the
    /// tile index are kept in memory, and a tile's bytes are read when it is first needed. In tests, the file's bytes
    /// (<see cref="Read"/>). On a player's game, the tiles the server sends around the player (<see cref="ForTiles"/>,
    /// <see cref="AddTile"/>): a tile not here yet answers false, is reported through <see cref="Missing"/>, and the caller
    /// uses its fallback (today's season shape). A tile is decoded when first read and kept in a <see cref="TileCache{T}"/> as
    /// its raw planes (49 bytes a cell, 397 kB for a 90-cell tile). Thread-safe.
    ///
    /// Cell (row, col) is centred at latitude 90 - (row + 0.5) / 30 and longitude -180 + (col + 0.5) / 30. Rows are clamped at
    /// the poles, columns wrap. Months are 0 (January) to 11.
    /// </summary>
    public sealed class SeasonData : IDisposable
    {
        /// <summary>The data-release file. Names never change meaning: a rebake with other values goes up under a new name.</summary>
        public const string FileName = "seasons-1.bin";
        public const string Magic = "EMSN";
        /// <summary>The content version the layout below is for.</summary>
        public const int ContentVersion = 1;
        public const int Width = 10800, Height = 5400, Res = 30;

        /// <summary>The published file's size and SHA-256: seasons-1.bin for Earth 2.1.0, written by tools/write_seasons.py from
        /// the weather session's bake (seasons-layer.npz, SHA-256 7273271a...; ERA5 spread from 10 years, means as around 1318),
        /// 16 November 2026. A new asset of the data release ed-v1 (its upload waits for the author's yes); a server with the file
        /// in ModData/earth/data uses it without a download.</summary>
        public const long FileBytes = 58_168_345;
        public const string FileSha256 = "a81899dbdf8f1a71d7108bc186b58f4bc2bbfa35b10ff7ed1f0da46300e3357c";
        public static bool Published => FileBytes > 0;

        /// <summary>Where the server keeps the file: ModData/earth/data, beside the data store's files.</summary>
        public static string PathIn(string earthFolder) => Path.Combine(EarthDataStore.Folder(earthFolder), FileName);

        /// <summary>The published file, for the one download protocol (DataTransport.EnsureFilesAsync).</summary>
        public static DataTransport.FileSpec Spec(string earthFolder) => new(FileName, PathIn(earthFolder), FileBytes, FileSha256);

        /// <summary>
        /// Fetches <paramref name="spec"/> (by default the published file) from <paramref name="source"/> (earth.json's DataRepo:
        /// Earth's data release, a mirror or a local folder) into ModData/earth/data, unless it is there and checks out (size
        /// and SHA-256), as the data store's files and the ground sets are fetched. Here, Lacking (no source, or the source
        /// doesn't have it) or Failed (anything that may pass), and why.
        /// </summary>
        public static Task<(DataTransport.SetResult Result, string Why)> EnsureAsync(string earthFolder, string source, Action<string> log,
            CancellationToken token = default, DataTransport.FileSpec? spec = null)
            => DataTransport.EnsureFilesAsync(EarthDataStore.Folder(earthFolder), new[] { spec ?? Spec(earthFolder) }, source, "DataRepo",
                "data source", "monthly temperature file", log, token);

        /// <summary>Units of the stored integers, in °C.</summary>
        public const float MeanUnit = 0.1f, RangeUnit = 0.2f, SpreadUnit = 0.1f;
        /// <summary>Provenance bits: the value was measured by its source at this cell; 0 where the bake filled it.</summary>
        public const byte MeanMeasured = 1, RangeMeasured = 2, SpreadMeasured = 4;

        /// <summary>The planes of a cell, in the file's order: 12 means (int16), 12 ranges, 12 spreads (uint8), provenance.</summary>
        public static readonly PlaneType[] Types = BuildTypes();
        public const int BytesPerCell = 12 * 2 + 12 + 12 + 1;

        static PlaneType[] BuildTypes()
        {
            var t = new PlaneType[37];
            for (int m = 0; m < 12; m++) { t[m] = PlaneType.I16; t[12 + m] = PlaneType.U8; t[24 + m] = PlaneType.U8; }
            t[36] = PlaneType.U8;
            return t;
        }

        public readonly int Tile;
        readonly int tilesX, cells;
        // the server's file (on disk or in memory) and its tile index
        readonly SafeFileHandle handle;
        readonly byte[] bytes;
        readonly long[] offset;
        readonly int[] length;
        // a player's game: the tiles the server sent, coded as the file holds them
        readonly ConcurrentDictionary<int, byte[]> received;
        readonly ConcurrentDictionary<int, long> asked = new();
        readonly TileCache<byte[]> decoded;
        /// <summary>Decoded tiles kept at least (about 4.8 MB at 90 cells), and at most (102 MB).</summary>
        public const int MaxCachedTiles = 12, MaxCachedTilesBound = 256;

        /// <summary>
        /// (Fable's review of the seasons branch, F2) The decoded tiles kept. Every live temperature reading goes through the
        /// layer, and readings follow the players: one player's loaded land touches <see cref="TilesPerPlayer"/> tiles (four
        /// at the usual view distances), so the server sizes the cache to <see cref="TilesFor"/> its online players
        /// and a cache that held fewer tiles than they read would decode one again at most readings. 12 to 256.
        /// </summary>
        public int CachedTiles
        {
            get => decoded.Max;
            set => decoded.Max = Math.Clamp(value, MaxCachedTiles, MaxCachedTilesBound);
        }

        /// <summary>The decoded tiles for <paramref name="players"/> online: <paramref name="perPlayer"/> each
        /// (<see cref="TilesPerPlayer"/>), and eight for the land between them.</summary>
        public static int TilesFor(int players, int perPlayer = 4) =>
            Math.Clamp(8 + Math.Max(1, perPlayer) * Math.Max(0, players), MaxCachedTiles, MaxCachedTilesBound);

        /// <summary>
        /// (Fable's re-review of 47c9b98) The tiles one player's loaded land can touch: a square of
        /// 2 × <paramref name="chunkRadius"/> chunks of 32 blocks laid anywhere over tiles of <paramref name="tileBlocksX"/> ×
        /// <paramref name="tileBlocksZ"/> blocks touches at most ceil(span / tile) + 1 of them along each side. Four at the
        /// usual view distances (a 3° tile is about 3,600 blocks at 1:100, 1,950 at 1:200), nine at 1:200 past a radius of
        /// about 30 chunks; at least four.
        /// </summary>
        public static int TilesPerPlayer(double tileBlocksX, double tileBlocksZ, int chunkRadius)
        {
            double span = 2.0 * 32 * Math.Max(1, chunkRadius);
            static int Along(double span, double tile) =>
                tile > 0 && !double.IsInfinity(tile) ? Math.Min(16, (int)Math.Ceiling(span / tile) + 1) : 2;
            return Math.Max(4, Along(span, tileBlocksX) * Along(span, tileBlocksZ));
        }

        /// <summary>A player's game: a tile not here yet was read. Reported again after <see cref="AskAgainMs"/> while it
        /// stays missing.</summary>
        public Action<int> Missing;
        public long AskAgainMs = 5000;

        /// <summary>Tiles across, and in all.</summary>
        public int TilesAcross => tilesX;
        public int TileCount => tilesX * (Height / Tile);

        SeasonData(int tile, SafeFileHandle handle, byte[] bytes, long[] offset, int[] length)
        {
            Tile = tile;
            tilesX = Width / tile;
            cells = tile * tile;
            this.handle = handle;
            this.bytes = bytes;
            this.offset = offset;
            this.length = length;
            if (handle == null && bytes == null) received = new ConcurrentDictionary<int, byte[]>();
            decoded = new TileCache<byte[]>(DecodeTile, MaxCachedTiles);
        }

        /// <summary>The server's layer, its tiles read from <paramref name="path"/> as needed (the store has checked the file's
        /// size and SHA-256). Throws an InvalidDataException on another layout.</summary>
        public static SeasonData Open(string path)
        {
            var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            try
            {
                long size = RandomAccess.GetLength(h);
                var head = new byte[24];
                ReadExactly(h, head, 0);
                int tile = Header(head, size, out int count);
                var index = new byte[8 * count];
                ReadExactly(h, index, 24);
                Index(index, count, 24 + index.LongLength, size, out var offset, out var length);
                return new SeasonData(tile, h, null, offset, length);
            }
            catch
            {
                h.Dispose();
                throw;
            }
        }

        /// <summary>The layer from the file's bytes (tests, tools).</summary>
        public static SeasonData Read(byte[] file)
        {
            if (file == null || file.Length < 24) throw new InvalidDataException(FileName + ": bad header");
            int tile = Header(file, file.Length, out int count);
            if (24 + 8L * count > file.Length) throw new InvalidDataException(FileName + ": tile index truncated");
            Index(file.AsSpan(24, 8 * count).ToArray(), count, 24 + 8L * count, file.Length, out var offset, out var length);
            return new SeasonData(tile, null, file, offset, length);
        }

        /// <summary>A player's layer, filled by the tiles the server sends (<see cref="AddTile"/>); <paramref name="tile"/> as the
        /// server's file has it.</summary>
        public static SeasonData ForTiles(int tile)
        {
            if (tile <= 0 || Width % tile != 0 || Height % tile != 0 || tile > 360) throw new InvalidDataException(FileName + ": tile " + tile);
            return new SeasonData(tile, null, null, null, null);
        }

        static int Header(byte[] head, long size, out int count)
        {
            if (head[0] != Magic[0] || head[1] != Magic[1] || head[2] != Magic[2] || head[3] != Magic[3]) throw new InvalidDataException(FileName + ": bad header");
            int version = BitConverter.ToInt32(head, 4);
            int width = BitConverter.ToInt32(head, 8), height = BitConverter.ToInt32(head, 12), tile = BitConverter.ToInt32(head, 16);
            count = BitConverter.ToInt32(head, 20);
            if (PlaneCoding.ContentVersion(version) != ContentVersion || PlaneCoding.Coding(version) != PlaneCoding.Predicted
                || width != Width || height != Height || tile <= 0 || tile > 360 || Width % tile != 0 || Height % tile != 0
                || count != (Width / tile) * (Height / tile) || 24 + 8L * count > size)
                throw new InvalidDataException(FileName + ": layout mismatch");
            return tile;
        }

        static void Index(byte[] index, int count, long tilesAt, long size, out long[] offset, out int[] length)
        {
            offset = new long[count];
            length = new int[count];
            for (int i = 0; i < count; i++)
            {
                offset[i] = (uint)BitConverter.ToInt32(index, 8 * i);
                length[i] = BitConverter.ToInt32(index, 8 * i + 4);
                if (length[i] < 0 || length[i] > 0 && (offset[i] < tilesAt || offset[i] + length[i] > size))
                    throw new InvalidDataException(FileName + ": bad tile index");
            }
        }

        static void ReadExactly(SafeFileHandle h, byte[] buffer, long at)
        {
            int got = 0;
            while (got < buffer.Length)
            {
                int n = RandomAccess.Read(h, buffer.AsSpan(got), at + got);
                if (n <= 0) throw new InvalidDataException(FileName + ": truncated");
                got += n;
            }
        }

        /// <summary>The server: a tile's bytes as the file holds them (coded), to send to a player; empty for a tile without
        /// data.</summary>
        public byte[] TileBlob(int key)
        {
            if (received != null) throw new InvalidOperationException("a player's layer has no file");
            if ((uint)key >= (uint)length.Length) throw new ArgumentOutOfRangeException(nameof(key));
            if (length[key] == 0) return Array.Empty<byte>();
            if (bytes != null) return bytes.AsSpan((int)offset[key], length[key]).ToArray();
            var blob = new byte[length[key]];
            ReadExactly(handle, blob, offset[key]);
            return blob;
        }

        /// <summary>A player's game: a tile the server sent; decoded when first read. A tile that doesn't decode is dropped
        /// then and reported missing again.</summary>
        public void AddTile(int key, byte[] blob)
        {
            if (received == null || (uint)key >= (uint)TileCount || blob == null) return;
            received[key] = blob;
            asked.TryRemove(key, out _);
        }

        /// <summary>Whether the tile is here (always on the server).</summary>
        public bool HasTile(int key) => received == null || received.ContainsKey(key);

        /// <summary>The tile key of a cell and its index in the tile. Rows are clamped at the poles, columns wrap.</summary>
        public int KeyOf(int row, int col, out int index)
        {
            row = Math.Clamp(row, 0, Height - 1);
            col %= Width;
            if (col < 0) col += Width;
            index = (row % Tile) * Tile + col % Tile;
            return row / Tile * tilesX + col / Tile;
        }

        /// <summary>The key of the tile holding a place (degrees), for the tiles around a player.</summary>
        public int KeyAt(double lat, double lon) => KeyOf((int)Math.Floor((90 - lat) * Res), (int)Math.Floor((lon + 180) * Res), out _);

        /// <summary>Tiles decoded so far (the cost probe's counter; each is about 1 ms).</summary>
        public long Decodes => Interlocked.Read(ref decodes);
        long decodes;

        byte[] DecodeTile(int key)
        {
            Interlocked.Increment(ref decodes);
            var blob = received != null ? received[key] : TileBlob(key);
            return blob.Length == 0 ? Array.Empty<byte>() : PlaneCoding.Decode(blob, 0, blob.Length, Tile, Tile, Types, FileName + ": tile " + key);
        }

        /// <summary>The decoded planes of a tile, or null where it isn't here (a player's game) or holds no data.</summary>
        byte[] TileOf(int key)
        {
            if (!HasTile(key))
            {
                Report(key);
                return null;
            }
            byte[] t;
            try { t = decoded.Get(key); }
            catch (InvalidDataException) when (received != null)
            {
                // a damaged packet: forget it and ask again (the server's own file throws: the store checked it)
                received.TryRemove(key, out _);
                asked.TryRemove(key, out _);
                Report(key);
                return null;
            }
            return t.Length == 0 ? null : t;
        }

        void Report(int key)
        {
            var missing = Missing;
            if (missing == null) return;
            long now = Environment.TickCount64;
            if (asked.TryGetValue(key, out long at) && now - at < AskAgainMs) return;
            asked[key] = now;
            missing(key);
        }

        /// <summary>A cell's values for a month (°C; the mean at sea level). False where the tile isn't here yet (a player's
        /// game) or holds no data.</summary>
        public bool TryCell(int row, int col, int month, out float mean, out float range, out float spread)
        {
            if ((uint)month > 11) throw new ArgumentOutOfRangeException(nameof(month));
            var t = TileOf(KeyOf(row, col, out int i));
            if (t == null)
            {
                mean = range = spread = float.NaN;
                return false;
            }
            Get(t, i, month, out mean, out range, out spread);
            return true;
        }

        /// <summary>
        /// The four cells (r0, c0), (r0, c0 + 1), (r0 + 1, c0), (r0 + 1, c0 + 1) of a month at index dr * 2 + dc, as
        /// <see cref="FineTemperature.Quad"/>: one tile lookup for the four when they share a tile. False, with NaN, when any
        /// of their tiles isn't here (a player's game).
        /// </summary>
        public bool TryCells4(int r0, int c0, int month, Span<float> mean, Span<float> range, Span<float> spread)
        {
            if ((uint)month > 11) throw new ArgumentOutOfRangeException(nameof(month));
            int ra = Math.Clamp(r0, 0, Height - 1), rb = Math.Clamp(r0 + 1, 0, Height - 1);
            int ca = c0 % Width;
            if (ca < 0) ca += Width;
            int cb = ca + 1 == Width ? 0 : ca + 1;
            if (rb / Tile != ra / Tile || cb / Tile != ca / Tile)
            {
                bool all = true;
                for (int k = 0; k < 4; k++)
                    all &= TryCell(k < 2 ? ra : rb, (k & 1) == 0 ? ca : cb, month, out mean[k], out range[k], out spread[k]);
                if (!all) Fill(mean, range, spread);
                return all;
            }
            var t = TileOf(KeyOf(ra, ca, out int i00));
            if (t == null)
            {
                Fill(mean, range, spread);
                return false;
            }
            int down = (rb - ra) * Tile;
            Get(t, i00, month, out mean[0], out range[0], out spread[0]);
            Get(t, i00 + 1, month, out mean[1], out range[1], out spread[1]);
            Get(t, i00 + down, month, out mean[2], out range[2], out spread[2]);
            Get(t, i00 + down + 1, month, out mean[3], out range[3], out spread[3]);
            return true;
        }

        /// <summary>
        /// All twelve months of the four cells (r0, c0), (r0, c0 + 1), (r0 + 1, c0), (r0 + 1, c0 + 1) at index
        /// (dr * 2 + dc) * 12 + month (each span 48 long): the values of <see cref="TryCells4"/> month by month, with four tile
        /// lookups for all twelve months instead of 48 (Fable's F2). False, with NaN, when
        /// any of their tiles isn't here (a player's game).
        /// </summary>
        public bool TryMonths4(int r0, int c0, Span<float> mean, Span<float> range, Span<float> spread)
        {
            if (mean.Length < 48 || range.Length < 48 || spread.Length < 48) throw new ArgumentException("spans of 48 values");
            int ra = Math.Clamp(r0, 0, Height - 1), rb = Math.Clamp(r0 + 1, 0, Height - 1);
            int ca = c0 % Width;
            if (ca < 0) ca += Width;
            int cb = ca + 1 == Width ? 0 : ca + 1;
            for (int k = 0; k < 4; k++)
            {
                int row = k < 2 ? ra : rb, col = (k & 1) == 0 ? ca : cb;
                // the four nearly always share a tile: the cache's last-tile slot then serves the three after the first
                var t = TileOf(KeyOf(row, col, out int i));
                if (t == null)
                {
                    mean.Slice(0, 48).Fill(float.NaN);
                    range.Slice(0, 48).Fill(float.NaN);
                    spread.Slice(0, 48).Fill(float.NaN);
                    return false;
                }
                for (int month = 0; month < 12; month++) Get(t, i, month, out mean[k * 12 + month], out range[k * 12 + month], out spread[k * 12 + month]);
            }
            return true;
        }

        /// <summary>A cell's provenance bits (<see cref="MeanMeasured"/> and so on); false where the tile isn't here.</summary>
        public bool TryProvenance(int row, int col, out byte bits)
        {
            var t = TileOf(KeyOf(row, col, out int i));
            bits = t == null ? (byte)0 : t[48 * cells + i];
            return t != null;
        }

        void Get(byte[] t, int i, int month, out float mean, out float range, out float spread)
        {
            int m = 2 * (month * cells + i);
            mean = (short)(t[m] | t[m + 1] << 8) * MeanUnit;
            range = t[(24 + month) * cells + i] * RangeUnit;
            spread = t[(36 + month) * cells + i] * SpreadUnit;
        }

        static void Fill(Span<float> mean, Span<float> range, Span<float> spread)
        {
            mean.Fill(float.NaN);
            range.Fill(float.NaN);
            spread.Fill(float.NaN);
        }

        public void Dispose() => handle?.Dispose();
    }
}
