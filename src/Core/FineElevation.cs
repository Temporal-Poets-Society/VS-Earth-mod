using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0099 (the Fable pass's FP-03): a GLO-90 square a chunk needs could not be fetched while the game runs (the network,
    /// the release down, no room on the disk). The chunk is not made now: Earth's terrain pass notes it, it is saved empty
    /// and made again at the next start, instead of taking the built-in heights there for good.
    /// </summary>
    public sealed class TileUnavailableException : Exception
    {
        public readonly int LatS, LonW;
        public TileUnavailableException(int latS, int lonW, string message) : base(message) { LatS = latS; LonW = lonW; }
    }

    /// <summary>
    /// Land heights from Copernicus GLO-90 tiles kept on this machine (decision 0051), bilinear between the 3″ points. Which
    /// squares the source has comes from the tile list shipped with the mod; a square without a tile is open sea (0 m). A
    /// square this world keeps on the built-in heights (for good: a tile no source has; for the session: one that couldn't
    /// be fetched now) and a tile not on this machine give no height: the caller uses the built-in one. When a tile is
    /// missing and <see cref="Fetch"/> is set, a thread that has <see cref="FetchAllowed"/> on waits for it (chunk
    /// generation); others don't. Thread-safe.
    /// </summary>
    public sealed class FineElevation
    {
        /// <summary>
        /// Rules the world records (EarthParams.FineElevation): 1 = GLO-90 on dry land, bilinear, whole metres; 2 = and a
        /// listed summit's own top in GLO-90 lifted to the survey (narrow, never lowered); 3 = a smooth curve through the 3″
        /// points (Catmull-Rom, <see cref="TrySmoothHeight"/>) instead of straight lines, and no undulation noise; 4 = at
        /// 75 m and more per block, a block's height is the mean of 2 × 2 such points over its footprint (3 × 3 from 150 m),
        /// so that small Earth sizes get each block's area, not one point of it (TerrainModel.FineFootprint); 5 = the smooth
        /// curve never swings past the two points it runs between (<see cref="TrySmoothHeight(double, double, bool, out double)"/>),
        /// and land below 0 m is a beach only within the outline coast's reach (TerrainModel.CoastColumn): GLO-90's whole
        /// metres made fields and fish ponds a metre below the sea, and the swings round them, beach sand in squares and
        /// crosses far inland (Suez, the Dutch polders; 8 November 2026).
        /// </summary>
        public const int Version = 5;

        /// <summary>Rules 2: how far (m) from a listed peak its top is looked for in GLO-90.</summary>
        public const double SummitSearch = 1000;
        /// <summary>Rules 2: a top this near (m) the search's edge may belong to a higher point outside it, so it isn't lifted.</summary>
        public const double SummitEdge = 185;
        /// <summary>Rules 2: the lift fades to nothing this far (m) from the top, about three GLO-90 points.</summary>
        public const double SummitLiftRadius = 300;
        /// <summary>Rules 2: a larger gap (m) between survey and GLO-90 is taken as a catalogue error and not lifted.</summary>
        public const double SummitMaxLift = 300;
        public const string FileExtension = ".egl";
        /// <summary>GLO-90's cell north-south (3", m).</summary>
        public const double CellMetres = 92.6;

        public string Folder { get; }
        readonly byte[] sourceHas;
        readonly ConcurrentDictionary<int, byte> fallback = new();
        readonly object gate = new();
        readonly Dictionary<int, (DemTiles.Tile tile, long used)> cache = new();
        readonly HashSet<int> notHere = new();
        long clock;
        /// <summary>Decoded tiles kept in memory to begin with (about 2–3 MB each at full width); the cache grows past it
        /// while it thrashes (<see cref="CapacityNow"/>).</summary>
        public int Capacity = 24;

        /// <summary>
        /// EA-0193: the most tiles the cache grows to. A tile read again soon after the cache dropped it (within as many loads
        /// as the cache may grow to) makes room for one more, so the cache grows to the squares in use at once and no further: a
        /// server with players in 30 squares read every chunk's tile anew from disk with 24 (16 ms and 6 MB a chunk, the
        /// places taken in turn as their requests are; tileprobe, 19 November 2026), and now holds 32; players in 12 squares
        /// or fewer leave it at 24. 128 tiles are at most about 380 MB.
        /// </summary>
        public int MaxCapacity = 128;
        int grown;
        /// <summary>When each recently dropped tile was dropped (by load count), to tell a thrash from a tile not used for long.</summary>
        readonly Dictionary<int, long> dropped = new();

        /// <summary>The tiles the cache holds at most now: <see cref="Capacity"/> and what thrashing added.</summary>
        public int CapacityNow { get { lock (gate) return Limit; } }
        int Limit => Math.Max(4, Math.Max(Capacity, Math.Min(Capacity + grown, MaxCapacity)));

        /// <summary>Tiles read again soon after the cache dropped them (each made the cache one tile larger, up to the most).</summary>
        public long Reloads;

        /// <summary>One line for the log: tiles read, how many again soon after being dropped, the cache's size.</summary>
        public string Report()
        {
            lock (gate)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:n0} tiles read from disk, {1:n0} of them again soon after the cache dropped them; the cache holds {2} of at most {3} tiles (began at {4}, grows to {5})",
                    Loads, Reloads, cache.Count, Limit, Math.Max(4, Capacity), Math.Max(Capacity, MaxCapacity));
        }

        /// <summary>
        /// Downloads and converts a square's tile (latS, lonW) if it can, blocking; true when the tile is on this machine
        /// afterwards. Set by the server; null in tests and on clients.
        /// </summary>
        public Func<int, int, bool> Fetch;
        /// <summary>Called when a square falls back to the built-in heights for good (to save it with the world).</summary>
        public Action<int, int> FellBack;
        /// <summary>
        /// Review A3: whether a square whose fetch just failed is one the sources don't have (their answer, not a failure that
        /// may pass), so it falls back for good; otherwise for this session only (<see cref="FellBackForSession"/>), and
        /// it is fetched again at the next start. Null (tests, tools): for good, as before.
        /// </summary>
        public Func<int, int, bool> Lacks;
        /// <summary>Called when a square falls back to the built-in heights for this session only (not saved).</summary>
        public Action<int, int> FellBackForSession;
        /// <summary>
        /// EA-0099 (the Fable pass's FP-03): called, where it is set, for a square whose fetch failed and that the sources
        /// don't lack (a failure that may pass), before any fallback is marked. Inside one of Earth's chunk passes
        /// (<see cref="InChunkPass"/>) it may throw <see cref="TileUnavailableException"/>, so that the chunk is not made
        /// without the square (it is made again later, rather than on the built-in heights, which would meet the GLO-90 chunks
        /// of later sessions with a step). True: the square is not given up; this read takes the built-in heights and the next
        /// one tries again. False: the square falls back for this session as before. Null: as before.
        /// </summary>
        public Func<int, int, bool> Unavailable;
        /// <summary>
        /// EA-0106 (the Fable pass's FP-15): asked at the first read of a square's file in a session whether it is the release's
        /// tile (DemDownloader.KeptMatches, given the file and the tile read from it); null (nothing to compare with yet) asks
        /// again at a later read. False (Fable's re-review of 90692fe): the tile here stays in use for this session, so the
        /// square always has heights, and <see cref="Replace"/> is asked to fetch the release's beside it; it takes the old
        /// one's place at the next start (<see cref="ApplyReplacements"/>). Null: not asked.
        /// </summary>
        public Func<int, int, byte[], DemTiles.Tile, bool?> Verify;
        /// <summary>See <see cref="Verify"/>: fetch the release's tile for a square into <see cref="ReplacementExtension"/>
        /// beside the one in use.</summary>
        public Action<int, int> Replace;
        /// <summary>The release's tile for a square whose tile here is not the release's, waiting for the next start.</summary>
        public const string ReplacementExtension = ".release";
        /// <summary>(EA-0116 T-L10) A tile file's bytes; a read refused for a moment (a sharing violation: another server
        /// replacing the file) is tried twice more, 20 and 60 ms later. Gone: FileNotFoundException at once.</summary>
        static byte[] ReadTileFile(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return File.ReadAllBytes(path); }
                catch (IOException) when (attempt < 2 && File.Exists(path)) { System.Threading.Thread.Sleep(attempt == 0 ? 20 : 60); }
            }
        }

        /// <summary>
        /// (EA-0116 D-R3) Leftovers of a process that was killed while it wrote into the store: tiles' temporary files (the
        /// downloader's unique <c>.xxxxxxxxxxxx.part</c> names, written whole at once, so an hour old is never in use) and
        /// tiles set aside as damaged (<c>.bad</c>) more than 30 days ago. A release file's own <c>name.part</c> is a download
        /// cut short, kept to be resumed; tiles set aside as <see cref="DiffersExtension"/> are kept for whoever looks. Never
        /// throws; the count removed.
        /// </summary>
        public int SweepLeftovers()
        {
            int removed = 0;
            try
            {
                if (!Directory.Exists(Folder)) return 0;
                var now = DateTime.UtcNow;
                foreach (var f in Directory.EnumerateFiles(Folder))
                {
                    string name = Path.GetFileName(f);
                    bool part = name.EndsWith(".part", StringComparison.Ordinal) && UniquePart.IsMatch(name);
                    bool bad = name.EndsWith(FileExtension + ".bad", StringComparison.Ordinal);
                    if (!part && !bad) continue;
                    try
                    {
                        if (now - File.GetLastWriteTimeUtc(f) < (part ? TimeSpan.FromHours(1) : TimeSpan.FromDays(30))) continue;
                        File.Delete(f);
                        removed++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return removed;
        }

        /// <summary>
        /// (Fable's re-review of 90692fe) At start, before any tile is read: each release tile fetched beside one that was not
        /// the release's (<see cref="ReplacementExtension"/>) takes its place. Equal heights (another encoding of the same
        /// tile): the old file simply goes. Other heights: the old file is kept as <see cref="DiffersExtension"/> and the
        /// square named in the log, since chunks made before keep the old heights. A replacement that is not a tile of its
        /// square with GLO-90's dimensions is deleted. Never throws; (replaced with the same heights, replaced with others).
        /// </summary>
        public (int Same, int Other) ApplyReplacements(Action<string> log)
        {
            int same = 0, other = 0;
            try
            {
                if (!Directory.Exists(Folder)) return (0, 0);
                foreach (var release in Directory.GetFiles(Folder, "*" + FileExtension + ReplacementExtension))
                {
                    string target = release.Substring(0, release.Length - ReplacementExtension.Length);
                    string square = Path.GetFileNameWithoutExtension(target);
                    try
                    {
                        var next = DemTiles.Decode(File.ReadAllBytes(release));
                        if (DemTiles.ShortName(next.LatS, next.LonW) != square || next.Rows != DemTiles.Glo90Rows || next.Cols != DemTiles.Glo90Cols(next.LatS))
                            throw new InvalidDataException("not this square's GLO-90 tile");
                        bool equal = false;
                        if (File.Exists(target))
                        {
                            try { equal = DemTiles.Decode(File.ReadAllBytes(target)).Heights.AsSpan().SequenceEqual(next.Heights); }
                            catch (InvalidDataException) { }
                            if (!equal) File.Move(target, target + DiffersExtension, true);
                        }
                        File.Move(release, target, true);
                        if (equal) same++;
                        else
                        {
                            other++;
                            log?.Invoke(square + ": the tile here had other heights than the release's; the release's takes its place from now on. " +
                                "Chunks made before keep the old heights (the old tile is kept as " + square + FileExtension + DiffersExtension + ").");
                        }
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        log?.Invoke(square + ": the release's tile fetched beside the one here could not take its place (" + e.Message + ")");
                        if (e is InvalidDataException) try { File.Delete(release); } catch (Exception) { }
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return (same, other);
        }

        static readonly System.Text.RegularExpressions.Regex UniquePart = new(@"\.[0-9a-f]{12}\.part$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>A tile set aside because it is not the release's: kept beside the store's tiles under this extension.</summary>
        public const string DiffersExtension = ".differs";
        // the squares whose file Verify found to be the release's in this session
        readonly HashSet<int> verified = new();
        /// <summary>0053 item 19: modern dams taken out (EarthParams.DamRules): each tile's heights as
        /// <see cref="DamRemovalData"/> has them, applied as it is read. Set before the first height is read; null: GLO-90
        /// as it is.</summary>
        public DamRemovalData Dams;

        // 0: not decided for this thread yet; 1: reads GLO-90; 2: takes the built-in heights
        [ThreadStatic] static int builtInOnly;
        /// <summary>
        /// Threads that sample the whole world for a picture, recognised by name: Wmap's world chart samples every 500 blocks
        /// across the globe off the generation thread, and reading GLO-90 there decoded tiles everywhere (0051
        /// Verification: the chart didn't finish in 2 minutes 46 seconds against 55 seconds, 189,000 tiles read).
        /// </summary>
        public static readonly string[] SweepThreadNames = { "Wmap world chart" };

        /// <summary>
        /// This thread takes the built-in heights and never reads GLO-90: for a map's prediction of the whole world (as on
        /// clients, which have no tiles). Never for anything that places blocks or entities, which must match the terrain.
        /// On by itself for <see cref="SweepThreadNames"/>. A reader that sweeps the whole world on shared pool threads sets
        /// it around each sample and puts the old value back, because the same threads may next draw nearby terrain, which
        /// must read GLO-90. Wmap 0.5.1's climate raster doesn't yet (0051 Verification).
        /// </summary>
        public static bool BuiltInOnly
        {
            get
            {
                int v = builtInOnly;
                if (v == 0)
                {
                    string name = Thread.CurrentThread.Name;
                    builtInOnly = v = name != null && Array.IndexOf(SweepThreadNames, name) >= 0 ? 2 : 1;
                }
                return v == 2;
            }
            set => builtInOnly = value ? 2 : 1;
        }

        [ThreadStatic] static bool fetchAllowed;
        /// <summary>Whether this thread may wait for a missing tile: chunk generation sets it (EarthElevationSystem.PrepareChunk,
        /// on the generation threads and, for the spawn chunks, the main thread, whose flag is cleared when the game starts
        /// running), every other generation entry point for its own length (<see cref="Generating"/>), and the story pad's
        /// ground mean around its own reads.</summary>
        public static bool FetchAllowed { get => fetchAllowed; set => fetchAllowed = value; }

        /// <summary>
        /// (FP-11, FP-12, R1 of the Fable pass of the 14 November 2026 code review) This thread may wait for missing tiles until
        /// the returned scope ends, then is as it was: for each of world generation's entry points other than the terrain pass
        /// (a map region's forest, shrub, climate and ocean maps, Earth's later passes, the deposit pass). Before, only a
        /// thread that had made a terrain pass could fetch, so the map regions the game makes before a chunk's first terrain
        /// pass (the spawn regions of a new world, the first new regions of each session, every region on a server with more
        /// than one generation thread) took the built-in heights where a tile wasn't on the machine, and were saved so.
        /// </summary>
        public static FetchScope Generating()
        {
            bool was = fetchAllowed;
            fetchAllowed = true;
            return new FetchScope(was);
        }

        /// <summary>See <see cref="Generating"/>.</summary>
        public readonly struct FetchScope : IDisposable
        {
            readonly bool was;
            internal FetchScope(bool was) { this.was = was; }
            public void Dispose() => fetchAllowed = was;
        }

        [ThreadStatic] static int chunkPass;
        /// <summary>
        /// EA-0099: whether this thread is in one of Earth's chunk column passes (<see cref="ChunkPass"/>), where a square that
        /// can't be had may leave the chunk unmade (<see cref="Unavailable"/> throws; EarthWorldGen notes the chunk and makes
        /// it again at the next start). Elsewhere (a map region, the map layers, the story pad: handlers the game doesn't
        /// guard, where a throw would end the server) such a read takes the built-in heights and the square is not given up.
        /// </summary>
        public static bool InChunkPass => chunkPass > 0;

        /// <summary>See <see cref="InChunkPass"/>: until the returned scope ends.</summary>
        public static ChunkPassScope ChunkPass()
        {
            chunkPass++;
            return new ChunkPassScope(true);
        }

        /// <summary>See <see cref="ChunkPass"/>.</summary>
        public readonly struct ChunkPassScope : IDisposable
        {
            readonly bool entered;
            internal ChunkPassScope(bool entered) { this.entered = entered; }
            public void Dispose() { if (entered) chunkPass--; }
        }

        [ThreadStatic] static int missedReads;
        /// <summary>
        /// Reads on this thread that took the built-in heights where chunk generation reads GLO-90: a tile not on this machine
        /// that this thread could not fetch (not a square kept on built-in heights for good, which generation takes too), or
        /// a sweep thread's <see cref="BuiltInOnly"/>. A running count: a reader compares it before and after a piece of work
        /// (the river query keeps a chunk's answer only when it rebuilt the chunk without such a read).
        /// </summary>
        public static int MissedReads => missedReads;

        // the last tile this thread used: most lookups are in the same square as the one before. Keyed by the reader's number,
        // not the reader, so that a pool thread doesn't keep a closed world's reader alive (its ~70 MB tile cache and the dam
        // data; CR-D10, as CoarseElevation does); the one tile it keeps is ~3 MB
        static int owners;
        readonly int ownerId = Interlocked.Increment(ref owners);
        [ThreadStatic] static int lastOwner;
        [ThreadStatic] static int lastKey;
        [ThreadStatic] static DemTiles.Tile lastTile;

        /// <summary>Height lookups (the threads' own counts summed: one shared counter was a contended cache line on every
        /// lookup of every worldgen thread; for the self-test); tiles read from disk; looks for a tile that wasn't on this machine.</summary>
        public long Lookups { get { long n = 0; foreach (var c in lookups.Values) n += c.Value; return n; } }
        public long Loads, Absent;
        readonly ThreadLocal<System.Runtime.CompilerServices.StrongBox<long>> lookups = new(() => new System.Runtime.CompilerServices.StrongBox<long>(), true);
        long arrivals;
        /// <summary>Tiles written to the folder this session (<see cref="TileArrived"/>): a reader that went without one can tell
        /// whether trying again may now read it (StreamLayer).</summary>
        public long Arrivals { get { lock (gate) return arrivals; } }

        /// <param name="tileList">The 360 × 180 bitmap of squares the source has (DemTiles.SquareIndex, bit 7 first).</param>
        public FineElevation(string folder, byte[] tileList)
        {
            if (tileList == null || tileList.Length * 8 < 360 * 180) throw new ArgumentException("tile list too short");
            Folder = folder;
            sourceHas = tileList;
        }

        public bool SourceHas(int latS, int lonW)
        {
            if (latS < -90 || latS > 89) return false;
            int i = DemTiles.SquareIndex(latS, Wrap(lonW));
            return (sourceHas[i >> 3] & (0x80 >> (i & 7))) != 0;
        }

        /// <summary>All squares the source has.</summary>
        public IEnumerable<(int latS, int lonW)> SourceSquares()
        {
            for (int latS = 89; latS >= -90; latS--)
                for (int lonW = -180; lonW < 180; lonW++)
                    if (SourceHas(latS, lonW)) yield return (latS, lonW);
        }

        public string PathOf(int latS, int lonW) => Path.Combine(Folder, DemTiles.ShortName(latS, lonW) + FileExtension);

        public bool IsHere(int latS, int lonW) => File.Exists(PathOf(latS, lonW));

        public bool IsFallback(int latS, int lonW) => fallback.ContainsKey(Key(latS, Wrap(lonW)));

        /// <summary>The square keeps the built-in heights for good (from the save, or the source lacks its tile); not a square
        /// that fell back for this session only, whose tile is fetched again at the next start.</summary>
        public bool IsFallbackForGood(int latS, int lonW) => fallback.TryGetValue(Key(latS, Wrap(lonW)), out byte kind) && kind == ForGood;

        // the fallback squares' kinds: kept on the built-in heights for good (saved with the world), or for this session
        const byte ForGood = 0, ForSession = 1;

        /// <summary>The squares this world keeps on built-in heights for good (FineElevation.Key), sorted: what its save holds.
        /// Not those that fell back for this session only.</summary>
        public int[] FallbackKeys
        {
            get { var k = new List<int>(); foreach (var kv in fallback) if (kv.Value == ForGood) k.Add(kv.Key); k.Sort(); return k.ToArray(); }
        }

        /// <summary>Squares this world keeps on the built-in heights (from its save).</summary>
        public void SetFallback(IEnumerable<int> keys)
        {
            foreach (int k in keys) fallback[k] = ForGood;
        }

        /// <summary>The square keeps the built-in heights for good (<see cref="FellBack"/> is told, to save it).</summary>
        public void MarkFallback(int latS, int lonW) => MarkFallback(latS, lonW, true);

        /// <summary>The square keeps the built-in heights: for good (told to <see cref="FellBack"/>, saved), or for this session
        /// only (told to <see cref="FellBackForSession"/>, fetched again at the next start).</summary>
        public void MarkFallback(int latS, int lonW, bool forGood)
        {
            lonW = Wrap(lonW);
            int key = Key(latS, lonW);
            if (forGood)
            {
                bool was = fallback.TryGetValue(key, out byte kind) && kind == ForGood;
                fallback[key] = ForGood;
                if (!was) FellBack?.Invoke(latS, lonW);
            }
            else if (fallback.TryAdd(key, ForSession)) FellBackForSession?.Invoke(latS, lonW);
        }

        /// <summary>A tile was just written to the folder: forget that it was missing.</summary>
        public void TileArrived(int latS, int lonW)
        {
            lock (gate) { notHere.Remove(Key(latS, Wrap(lonW))); arrivals++; }
        }

        public static int Key(int latS, int lonW) => (latS + 90) * 360 + (lonW + 180);
        public static void FromKey(int key, out int latS, out int lonW) { latS = key / 360 - 90; lonW = key % 360 - 180; }
        static int Wrap(int lonW) => ((lonW + 180) % 360 + 360) % 360 - 180;

        /// <summary>
        /// The land height (m) at a place, or false where it must come from the built-in data: a square kept on built-in
        /// heights, or a tile that isn't on this machine (and can't be fetched now).
        /// </summary>
        public bool TryHeight(double lat, double lon, out double height)
        {
            height = 0;
            if (BuiltInOnly) { missedReads++; return false; }
            lookups.Value.Value++;
            lat = Math.Clamp(lat, -90, 90);
            lon = Geo.Wrap180(lon);
            // the square whose rows reach this latitude: lat in (latS, latS + 1]
            int latS = Math.Clamp((int)Math.Ceiling(lat) - 1, -90, 89);
            double fr = (latS + 1 - lat) * DemTiles.Glo90Rows;
            int r0 = Math.Min((int)Math.Floor(fr), DemTiles.Glo90Rows - 1);
            double tr = fr - r0;
            if (!RowValue(latS, r0, lon, out double a)) return false;
            if (tr <= 0) { height = a; return true; }
            // the next row south: this tile's, or the first row of the square below
            bool below = r0 + 1 >= DemTiles.Glo90Rows;
            if (below && latS == -90) { height = a; return true; }
            if (!RowValue(below ? latS - 1 : latS, below ? 0 : r0 + 1, lon, out double b)) return false;
            height = a + (b - a) * tr;
            return true;
        }

        /// <summary>
        /// GLO-90's highest point within <paramref name="radiusM"/> metres of a place (rules 2's summit lift): the grid
        /// point's position, its height and its distance from the place. False where a tile there isn't available: a
        /// square kept on built-in heights, or a tile not on this machine that this thread may not fetch. Open sea reads
        /// 0 m. Ties keep the first point found (north first, then west), so the answer doesn't depend on the thread.
        /// </summary>
        public bool TryHighest(double lat, double lon, double radiusM, out double topLat, out double topLon, out double top, out double distM)
        {
            topLat = lat; topLon = lon; top = double.MinValue; distM = 0;
            if (BuiltInOnly) { missedReads++; return false; }
            const double metresPerDegree = Geo.MetresPerDegree;
            double mLon = Math.Max(1, metresPerDegree * Math.Cos(lat * Math.PI / 180));
            double dLat = radiusM / metresPerDegree;
            int rows = DemTiles.Glo90Rows;
            int k0 = (int)Math.Ceiling((lat - dLat) * rows), k1 = (int)Math.Floor((lat + dLat) * rows);
            for (int k = k1; k >= k0; k--)
            {
                if (k <= -90 * rows || k > 90 * rows) continue;
                double pLat = (double)k / rows;
                // the square whose rows reach pLat (lat in (latS, latS + 1]) and the row in it
                int latS = FloorDiv(k + rows - 1, rows) - 1;
                int row = (latS + 1) * rows - k;
                double dy = (pLat - lat) * metresPerDegree;
                double rest = radiusM * radiusM - dy * dy;
                if (rest < 0) continue;
                double half = Math.Sqrt(rest) / mLon, lo = lon - half, hi = lon + half;
                int cols = DemTiles.Glo90Cols(latS);
                for (int lonW = (int)Math.Floor(lo); lonW <= (int)Math.Floor(hi); lonW++)
                {
                    int c0 = Math.Max(0, (int)Math.Ceiling((lo - lonW) * cols)), c1 = Math.Min(cols - 1, (int)Math.Floor((hi - lonW) * cols));
                    if (c0 > c1) continue;
                    if (!TileAt(latS, Wrap(lonW), out var t)) return false;
                    for (int c = c0; c <= c1; c++)
                    {
                        double v = t == null ? 0 : t.Heights[row * cols + c];
                        if (v > top) { top = v; topLat = pLat; topLon = lonW + (double)c / cols; }
                    }
                }
            }
            if (top == double.MinValue) return false;
            double ey = (topLat - lat) * metresPerDegree, ex = Geo.Wrap180(topLon - lon) * mLon;
            distM = Math.Sqrt(ey * ey + ex * ex);
            topLon = Geo.Wrap180(topLon);
            return true;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>
        /// Rules 3: the land height (m) at a place on a smooth surface through GLO-90's 3″ points: Catmull-Rom along each of
        /// the four nearest rows (each in its own spacing, so across the bands where GLO-90 thins its columns too), then
        /// across them. It passes through every point like the bilinear <see cref="TryHeight"/>, without the creases along
        /// the grid lines that straight lines leave every 90 m. False where a tile it needs isn't available.
        /// </summary>
        public bool TrySmoothHeight(double lat, double lon, out double height) => TrySmoothHeight(lat, lon, false, out height);

        /// <summary>
        /// <see cref="TrySmoothHeight(double, double, out double)"/>; <paramref name="bounded"/> (rules 5): each piece of the
        /// curve, along the rows and across them, stays between the two points it runs between, so the surface never dips
        /// below or rises above the four points around it. Catmull-Rom alone swings past them by up to 7 % of a neighbour's
        /// step, and GLO-90's whole metres put such swings exactly at block edges: round every point a metre lower than flat
        /// land at 0 m its four diagonal neighbours dipped to about -0.005 m, a block lower (beach sand in an X, near Suez).
        /// </summary>
        public bool TrySmoothHeight(double lat, double lon, bool bounded, out double height)
        {
            height = 0;
            if (BuiltInOnly) { missedReads++; return false; }
            lookups.Value.Value++;
            lat = Math.Clamp(lat, -90, 90);
            lon = Geo.Wrap180(lon);
            const int last = 180 * DemTiles.Glo90Rows - 1;
            // global row from the North Pole: row r of square latS is (89 - latS) * rows + r
            double g = (90 - lat) * DemTiles.Glo90Rows;
            int g0 = Math.Min((int)Math.Floor(g), last);
            double t = g - g0;
            double r0 = 0, r1 = 0, r2 = 0, r3 = 0;
            for (int i = -1; i <= 2; i++)
            {
                int gi = Math.Clamp(g0 + i, 0, last);
                if (!SmoothRowValue(89 - gi / DemTiles.Glo90Rows, gi % DemTiles.Glo90Rows, lon, bounded, out double v)) return false;
                if (i == -1) r0 = v; else if (i == 0) r1 = v; else if (i == 1) r2 = v; else r3 = v;
            }
            height = bounded ? CatmullRomBounded(r0, r1, r2, r3, t) : CatmullRom(r0, r1, r2, r3, t);
            return true;
        }

        /// <summary><see cref="CatmullRom"/> held between <paramref name="b"/> and <paramref name="c"/> (rules 5).</summary>
        public static double CatmullRomBounded(double a, double b, double c, double d, double t)
            => Math.Clamp(CatmullRom(a, b, c, d, t), Math.Min(b, c), Math.Max(b, c));

        /// <summary>Catmull-Rom between <paramref name="b"/> (t 0) and <paramref name="c"/> (t 1).</summary>
        public static double CatmullRom(double a, double b, double c, double d, double t)
            => b + 0.5 * t * (c - a + t * (2 * a - 5 * b + 4 * c - d + t * (3 * (b - c) + d - a)));

        /// <summary>A row's height at a longitude, Catmull-Rom between the row's points (its own spacing), reaching into the
        /// squares to the west and east at a tile's edge.</summary>
        bool SmoothRowValue(int latS, int row, double lon, bool bounded, out double v)
        {
            v = 0;
            int lonW = Math.Clamp((int)Math.Floor(lon), -180, 179);
            if (!TileAt(latS, lonW, out var t)) return false;
            int cols = t?.Cols ?? DemTiles.Glo90Cols(latS);
            double fc = (lon - lonW) * cols;
            int c0 = Math.Min((int)Math.Floor(fc), cols - 1);
            double tc = fc - c0;
            DemTiles.Tile west = null, east = null;
            bool haveWest = false, haveEast = false;
            double P(int k, out bool ok)
            {
                ok = true;
                if (k >= 0 && k < cols) return t == null ? 0 : t.Heights[row * cols + k];
                if (k < 0)
                {
                    if (!haveWest) { haveWest = true; if (!TileAt(latS, Wrap(lonW - 1), out west)) { ok = false; haveWest = false; return 0; } }
                    return west == null ? 0 : west.Heights[row * west.Cols + west.Cols + k];
                }
                if (!haveEast) { haveEast = true; if (!TileAt(latS, Wrap(lonW + 1), out east)) { ok = false; haveEast = false; return 0; } }
                return east == null ? 0 : east.Heights[row * east.Cols + k - cols];
            }
            double p0 = P(c0 - 1, out bool o0), p1 = P(c0, out bool o1), p2 = P(c0 + 1, out bool o2), p3 = P(c0 + 2, out bool o3);
            if (!(o0 && o1 && o2 && o3)) return false;
            v = bounded ? CatmullRomBounded(p0, p1, p2, p3, tc) : CatmullRom(p0, p1, p2, p3, tc);
            return true;
        }

        /// <summary>A row's height at a longitude, linear between the row's points (the row's own spacing).</summary>
        bool RowValue(int latS, int row, double lon, out double v)
        {
            v = 0;
            int lonW = Math.Clamp((int)Math.Floor(lon), -180, 179);
            if (!TileAt(latS, lonW, out var t)) return false;
            int cols = t?.Cols ?? DemTiles.Glo90Cols(latS);
            double fc = (lon - lonW) * cols;
            int c0 = Math.Min((int)Math.Floor(fc), cols - 1);
            double tc = fc - c0;
            double p = t == null ? 0 : t.Heights[row * cols + c0];
            if (tc <= 0) { v = p; return true; }
            double q;
            if (c0 + 1 < cols) q = t == null ? 0 : t.Heights[row * cols + c0 + 1];
            else
            {
                // the first point of the square to the east (same latitude band, same spacing)
                if (!TileAt(latS, Wrap(lonW + 1), out var e)) return false;
                q = e == null ? 0 : e.Heights[row * e.Cols];
            }
            v = p + (q - p) * tc;
            return true;
        }

        /// <summary>The tile of a square: null with true for open sea (no tile in the source), false where the heights must
        /// come from the built-in data.</summary>
        bool TileAt(int latS, int lonW, out DemTiles.Tile tile)
        {
            tile = null;
            int key = Key(latS, lonW);
            if (lastOwner == ownerId && lastKey == key && lastTile != null) { tile = lastTile; return true; }
            if (!SourceHas(latS, lonW)) return true;
            if (fallback.ContainsKey(key)) return false;
            tile = Load(key, latS, lonW);
            if (tile == null && FetchAllowed && Fetch != null)
            {
                // Fetch is true at once for a tile another thread wrote; forget it was missing before reading it
                if (Fetch(latS, lonW)) { TileArrived(latS, lonW); tile = Load(key, latS, lonW); }
                if (tile == null)
                {
                    bool lacks = Lacks?.Invoke(latS, lonW) ?? true;
                    if (lacks || Unavailable?.Invoke(latS, lonW) != true) MarkFallback(latS, lonW, lacks);
                }
            }
            if (tile == null)
            {
                // still to come (generation would wait for it), not kept on built-in heights for good
                if (!fallback.ContainsKey(key)) missedReads++;
                return false;
            }
            lastOwner = ownerId; lastKey = key; lastTile = tile;
            return true;
        }

        DemTiles.Tile Load(int key, int latS, int lonW)
        {
            long seen;
            lock (gate)
            {
                if (cache.TryGetValue(key, out var e)) { cache[key] = (e.tile, ++clock); return e.tile; }
                if (notHere.Contains(key)) return null;
                seen = arrivals;
            }
            DemTiles.Tile t = null;
            string path = PathOf(latS, lonW);
            bool damaged = true;
            try
            {
                if (File.Exists(path))
                {
                    byte[] data;
                    try { data = ReadTileFile(path); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { damaged = false; throw; }
                    t = DemTiles.Decode(data);
                    if (t.LatS != latS || t.LonW != lonW) throw new InvalidDataException("holds " + DemTiles.ShortName(t.LatS, t.LonW));
                    // the readers index rows and columns by GLO-90's grid: a tile of other dimensions that passed its checksum
                    // (a mirror's, an odd converter's) would throw out of range on a generation thread (CR-D9)
                    if (t.Rows != DemTiles.Glo90Rows || t.Cols != DemTiles.Glo90Cols(t.LatS))
                        throw new InvalidDataException(t.Cols + " x " + t.Rows + " cells, not GLO-90's " + DemTiles.Glo90Cols(t.LatS) + " x " + DemTiles.Glo90Rows);
                    // EA-0106: the release's tile? Once a session, before the dams change the heights
                    bool known;
                    lock (gate) known = verified.Contains(key);
                    if (Verify != null && !known)
                    {
                        bool? same = Verify(latS, lonW, data, t);
                        // (Fable's re-review of 90692fe) not the release's: still read this session, the release's fetched beside it
                        if (same == false) Replace?.Invoke(latS, lonW);
                        if (same != null) lock (gate) verified.Add(key);
                    }
                    Dams?.ApplyTo(t);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // a damaged file is set aside, so the next fetch replaces it; (EA-0116 T-L10) one that could not be read now
                // (another process writing it, no access) is left as it is and read again later
                LastError = DemTiles.ShortName(latS, lonW) + ": " + e.Message;
                if (damaged) try { File.Move(path, path + ".bad", true); } catch (Exception) { }
                t = null;
            }
            lock (gate)
            {
                if (t == null)
                {
                    // not if a tile arrived meanwhile: it may be this one, written after File.Exists looked
                    Absent++;
                    if (seen == arrivals) notHere.Add(key);
                    return null;
                }
                Loads++;
                // (EA-0193) dropped no more loads ago than the cache may grow to: the squares in use don't fit, so one more
                // may stay (a window of only the current size missed a cycle of more than twice it: 60 squares at 24)
                if (dropped.Remove(key, out long at) && Loads - at <= Math.Max(Limit, MaxCapacity))
                {
                    Reloads++;
                    if (Capacity + grown < MaxCapacity) grown++;
                }
                cache[key] = (t, ++clock);
                while (cache.Count > Limit)
                {
                    int oldest = -1; long min = long.MaxValue;
                    foreach (var kv in cache) if (kv.Value.used < min) { min = kv.Value.used; oldest = kv.Key; }
                    cache.Remove(oldest);
                    dropped[oldest] = Loads;
                }
                // the drop record only needs the window's drops: older ones are not thrashing
                if (dropped.Count > 4 * Math.Max(Limit, MaxCapacity))
                    foreach (var kv in new List<KeyValuePair<int, long>>(dropped))
                        if (Loads - kv.Value > Math.Max(Limit, MaxCapacity)) dropped.Remove(kv.Key);
                return t;
            }
        }

        /// <summary>The last tile that could not be read, with the reason.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// The squares whose points a box of places reaches, with one point to spare on each side (bilinear looks one point
        /// south and east): what must be on this machine before chunks in that box are generated.
        /// </summary>
        public static IEnumerable<(int latS, int lonW)> SquaresFor(double latMin, double latMax, double lonMin, double lonMax)
        {
            const double spare = 1.0 / DemTiles.Glo90Rows;
            int s0 = Math.Max(-90, (int)Math.Ceiling(latMin - spare) - 1), s1 = Math.Min(89, (int)Math.Ceiling(latMax) - 1);
            int w0 = (int)Math.Floor(lonMin), w1 = (int)Math.Floor(lonMax + spare);
            if (w1 - w0 >= 359) { w0 = -180; w1 = 179; }
            for (int a = s0; a <= s1; a++)
                for (int o = w0; o <= w1; o++)
                    yield return (a, Wrap(o));
        }
    }
}
