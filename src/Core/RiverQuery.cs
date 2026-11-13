using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// What the river flow query (EarthMapSystem.RiverAt, decision 0027) answers beyond the model's own column: from pool
    /// layout 11, whether the generator drew a river column's top source as rapid water. A chunk's drawn rapid sources
    /// (EarthWorldGen.DrawnRapidSources: the terrain pass's settling and ramp words and the surface pass's rule for the
    /// source's kind, through ChunkSettler) cost its 72 x 72 model columns and one settling, so a chunk is settled only for
    /// a column that can have been drawn rapid at all (<see cref="MayBeDrawnRapid"/>; every other river column is calm
    /// without it), once however many threads ask at once (one Lazy per chunk, as TileCache does), then a lookup. Where a
    /// chunk's answer is not the generator's yet there is no answer: <see cref="RiverQueryNotReadyException"/>, never a
    /// guess a caller would keep. A chunk whose answer failed or was not ready is not asked again for <see cref="RetryMs"/>
    /// (a water update per tick would otherwise settle it again each time). Cleared when large. Game-independent; any thread.
    /// </summary>
    public sealed class RiverQuery
    {
        /// <summary>Chunks whose drawn rapid sources (and failures) are kept.</summary>
        public const int ChunkLimit = 256;
        /// <summary>How long a chunk whose drawn sources failed or were not ready is not asked again (milliseconds).</summary>
        public const long RetryMs = 10_000;

        readonly Func<int, int, bool[]> drawnSources;
        readonly Func<long> clock;
        readonly Action<int, int, Exception> failed;
        readonly ConcurrentDictionary<long, Lazy<bool[]>> drawnChunks = new();
        readonly ConcurrentDictionary<long, Failure> failures = new();

        /// <summary>A chunk's drawn sources that could not be had: until when they are not asked again, and the error (null:
        /// not ready).</summary>
        sealed class Failure
        {
            public readonly long Until;
            public readonly Exception Error;
            public Failure(long until, Exception error) { Until = until; Error = error; }
        }

        /// <param name="drawnSources">Chunk (cx, cz)'s 32 x 32 drawn rapid top sources, row by row (z outer); null while the
        /// answer is not the generator's yet. It may throw.</param>
        /// <param name="clockMs">Milliseconds, for <see cref="RetryMs"/> (default: Environment.TickCount64).</param>
        /// <param name="failed">Told each time a chunk's drawn sources throw (not again while that chunk waits).</param>
        public RiverQuery(Func<int, int, bool[]> drawnSources, Func<long> clockMs = null, Action<int, int, Exception> failed = null)
        {
            this.drawnSources = drawnSources ?? throw new ArgumentNullException(nameof(drawnSources));
            clock = clockMs ?? (() => Environment.TickCount64);
            this.failed = failed;
        }

        /// <summary>
        /// Pool layout 11: whether the generator drew block column (<paramref name="x"/>, <paramref name="z"/>)'s top source
        /// as rapid water (<paramref name="c"/> is its model column, a river's). <paramref name="column"/>: the model's
        /// column at a block position. Throws <see cref="RiverQueryNotReadyException"/> where its chunk's drawn sources are
        /// not the generator's yet (before 2.0.0-rc.3 the model's rough guess was answered there, and Hydrology kept it for
        /// good), or failed a moment ago; the drawn sources' own exception the first time they fail.
        /// </summary>
        public bool DrawnRapid(int x, int z, in TerrainModel.Column c, Func<int, int, TerrainModel.Column> column)
        {
            if (!MayBeDrawnRapid(x, z, c, column)) return false;
            if (TryDrawn(x, z, out bool rapid)) return rapid;
            throw new RiverQueryNotReadyException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Earth's river query has no answer for column ({0}, {1}) yet: its chunk's drawn rapid sources are not the generator's yet " +
                "(a GLO-90 tile still to come, or the world's generation not set up); ask again later", x, z));
        }

        /// <summary>
        /// The river's own water in a model column (EarthMapSystem.RiverSpanAt): its bed (the top of the ground under the
        /// water) and its water surface. False where the column is no river or holds no water: a rock breaking a chute's
        /// surface (about one chute column in 8, TerrainModel.Rivers) keeps River and WaterTop but has its bed a block
        /// above the surface, and the generator draws no water there (the terrain pass fills from the bed up to the
        /// surface, the surface pass's ramps and labels need water under them), so there is no span to give.
        /// </summary>
        public static bool Span(in TerrainModel.Column c, out int bed, out int waterTop)
        {
            bed = waterTop = -1;
            if (!c.River || c.WaterTop <= c.Top) return false;
            bed = c.Top;
            waterTop = c.WaterTop;
            return true;
        }

        /// <summary>
        /// Whether the generator can have drawn this river column's top source as rapid water at all (pool layout 11), told
        /// from the model's own columns without settling: the surface pass draws a top source rapid only on fresh water
        /// above the ground (ChunkSettler.DrawnRapidSources), and only as a rapids reach's rapid top source or at an upper
        /// edge that is not a plain step's (RampWord.RapidSource). Settling (RiverSettling.Settle) marks an edge only on a
        /// column with a 4-neighbour of the same water standing lower, and from pool layout 8 on (no sills) never changes a
        /// water column's water, surface or bed, nor whether its reach is a rapids reach. So any other river column is calm
        /// as drawn, whatever the settling: a calm reach's columns away from its steps, and the rocks breaking a chute's
        /// surface. A necessary condition only: a column it lets through is settled with its chunk for the exact answer.
        /// </summary>
        public static bool MayBeDrawnRapid(int x, int z, in TerrainModel.Column c, Func<int, int, TerrainModel.Column> column)
        {
            if (c.Water != TerrainModel.WaterLake || c.WaterTop <= c.Top) return false;
            if (c.RapidsReach) return true;
            for (int i = 0; i < 4; i++)
            {
                var n = column(x + (i == 0 ? -1 : i == 1 ? 1 : 0), z + (i == 2 ? -1 : i == 3 ? 1 : 0));
                if (n.Water == c.Water && n.WaterTop >= 0 && n.WaterTop < c.WaterTop) return true;
            }
            return false;
        }

        bool TryDrawn(int x, int z, out bool rapid)
        {
            rapid = false;
            int cx = x >> 5, cz = z >> 5; // (floor division by the chunk size, 32)
            long key = ((long)cx << 32) | (uint)cz;
            // a chunk that failed or was not ready a moment ago: not settled again until RetryMs has passed
            if (failures.TryGetValue(key, out var failure))
            {
                if (clock() < failure.Until)
                {
                    // (a new exception each time: the remembered one may be thrown on several threads at once)
                    if (failure.Error != null) throw new RiverQueryNotReadyException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Earth's river query has no answer for column ({0}, {1}) yet: its chunk's drawn rapid sources failed a moment ago and are " +
                        "asked again from {2} ms after that ({3})", x, z, RetryMs, failure.Error.Message), failure.Error);
                    return false;
                }
                failures.TryRemove(new KeyValuePair<long, Failure>(key, failure));
            }
            if (!drawnChunks.TryGetValue(key, out var lazy))
            {
                if (drawnChunks.Count >= ChunkLimit) drawnChunks.Clear();
                lazy = drawnChunks.GetOrAdd(key, _ => new Lazy<bool[]>(() => drawnSources(cx, cz), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
            }
            bool[] drawn;
            try { drawn = lazy.Value; }
            catch (Exception e)
            {
                if (drawnChunks.TryRemove(new KeyValuePair<long, Lazy<bool[]>>(key, lazy)))
                {
                    Remember(key, e);
                    failed?.Invoke(cx, cz, e);
                }
                throw;
            }
            if (drawn == null)
            {
                if (drawnChunks.TryRemove(new KeyValuePair<long, Lazy<bool[]>>(key, lazy))) Remember(key, null);
                return false;
            }
            rapid = drawn[(z & 31) * 32 + (x & 31)];
            return true;
        }

        void Remember(long key, Exception error)
        {
            if (failures.Count >= ChunkLimit) failures.Clear();
            failures[key] = new Failure(clock() + RetryMs, error);
        }
    }

    /// <summary>
    /// Earth's river query has no answer for this column yet (EarthMapSystem.RiverAt, RiverQuery.DrawnRapid): the answer
    /// would not be the generator's, so none is given rather than a guess. Nothing about the column follows from a call
    /// that threw; ask again later. Hydrology catches every exception from the query, keeps nothing from that call and
    /// asks again at the column's next water update.
    /// </summary>
    public sealed class RiverQueryNotReadyException : InvalidOperationException
    {
        public RiverQueryNotReadyException(string message, Exception inner = null) : base(message, inner) { }
    }
}
