using System;
using System.Collections.Concurrent;
using EarthMap.Core;

namespace EarthMap
{
    /// <summary>
    /// What the terrain pass (<see cref="TerrainChunkWriter"/>) leaves for the surface pass of the same chunk column, which runs
    /// after vanilla's block layers in the same pass (<see cref="RiverBlockPass"/>, <see cref="AlpineSurfacePass"/>): the river
    /// ramps' words and each column's surface flags. One per world, made by EarthWorldGen and handed to the passes that write and
    /// read it; the surface pass takes a chunk's entries out when it reads them.
    /// </summary>
    sealed class TerrainHandoff
    {
        /// <summary>River ramps per chunk column (<see cref="RampWord"/>), only for chunks that have any.</summary>
        readonly ConcurrentDictionary<long, uint[]> ramps = new ConcurrentDictionary<long, uint[]>();
        /// <summary>
        /// Per chunk column, each column's flags, <see cref="FlagBeach"/> (a beach, as settled) and <see cref="FlagDryRiver"/> (a
        /// dry river bed, arid river rules), or <see cref="NoFlags"/> when none is flagged. The surface pass runs in the same
        /// terrain pass as the writer, so a chunk without an entry was not made by Earth's terrain writer: EarthWorldGen refuses
        /// to go on (B02: the dry river beds are recorded here so the model's column is not built a second time for that flag).
        /// </summary>
        readonly ConcurrentDictionary<long, byte[]> surfaceFlags = new ConcurrentDictionary<long, byte[]>();
        public const byte FlagBeach = 1, FlagDryRiver = 2;
        public static readonly byte[] NoFlags = Array.Empty<byte>();

        public static long ChunkKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;

        /// <summary>The terrain pass's ramp words for a chunk column.</summary>
        public void PutRamps(int cx, int cz, uint[] ramp) => ramps[ChunkKey(cx, cz)] = ramp;

        /// <summary>The surface pass takes a chunk column's ramp words; false when it has none.</summary>
        public bool TakeRamps(int cx, int cz, out uint[] ramp) => ramps.TryRemove(ChunkKey(cx, cz), out ramp);

        /// <summary>The terrain pass's flags for a chunk column: always an entry, so that the surface pass tells "nothing
        /// flagged" (<see cref="NoFlags"/>) from "not made by Earth's terrain writer" (no entry).</summary>
        public void PutFlags(int cx, int cz, byte[] flags) => surfaceFlags[ChunkKey(cx, cz)] = flags ?? NoFlags;

        /// <summary>The surface pass takes a chunk column's flags; null when Earth's terrain writer did not make it.</summary>
        public byte[] TakeFlags(int cx, int cz)
        {
            surfaceFlags.TryRemove(ChunkKey(cx, cz), out byte[] flags);
            return flags;
        }

        /// <summary>(FP-13) Whether the terrain writer's flags for a chunk column are still here: past the terrain pass, its surface
        /// pass never ran (a handler before it in the pass threw, and the game skipped the rest of the pass).</summary>
        public bool Untaken(int cx, int cz) => surfaceFlags.ContainsKey(ChunkKey(cx, cz));

        /// <summary>Drops a chunk column's entries (a peek's, which no surface pass will take).</summary>
        public void Drop(int cx, int cz)
        {
            surfaceFlags.TryRemove(ChunkKey(cx, cz), out _);
            ramps.TryRemove(ChunkKey(cx, cz), out _);
        }
    }

    /// <summary>
    /// One column's river water shapes, from the terrain pass (TerrainChunkWriter) to the surface pass (RiverBlockPass), in 32
    /// bits: the ramp's height (bits 0-2: 1..6, 0 for an edge alone), the flow direction (3-5, FlowDirIndex), rapid water
    /// (6), the waterfall's height (7-14), the edge kind (15-16, TerrainModel.EdgeStep/EdgeLip; pool layout 5), the edge's
    /// depth (17-24), the spill (25-27; pool layout 5), a rapid top source (28; pool layout 8: a rapids reach, layout 11:
    /// only under a rapids reach's ramps, at its drops) and a calm river's labelled source (29; pool layout 11).
    /// </summary>
    readonly struct RampWord
    {
        public readonly uint Bits;
        public RampWord(uint bits) { Bits = bits; }
        public int Height => (int)Bits & 7;
        public int Dir => (int)(Bits >> 3) & 7;
        public bool Rapid => (Bits & 64) != 0;
        public int Fall => (int)(Bits >> 7) & 255;
        public int Edge => (int)(Bits >> 15) & 3;
        public int EdgeDepth => (int)(Bits >> 17) & 255;
        public int Spill => (int)(Bits >> 25) & 7;
        public bool RapidTop => (Bits & (1u << 28)) != 0;
        public bool CalmRiver => (Bits & (1u << 29)) != 0;

        /// <summary>
        /// Whether the surface pass draws this column's top source as rapid water (RiverBlockPass.PlaceRamps): an edge other
        /// than a plain step's (a chute's step, a curtain's lip), and where there is no edge, a rapid top source. The river query
        /// asks the same (ChunkSettler.DrawnRapidSources), so its "rapids" is what was drawn.
        /// </summary>
        public bool RapidSource => Edge != 0 ? Edge != TerrainModel.EdgeStep : RapidTop;

        /// <summary>
        /// The word the terrain pass hands over for a settled column, false when there is nothing to draw: its ramp, fall,
        /// edge and spill, and a rapids reach's rapid top source (pool layout 8; from layout 11 only under its ramps, at its
        /// drops) or a calm river's labelled source (pool layout 11, <paramref name="calmLabels"/>).
        /// </summary>
        public static bool TryOf(in TerrainModel.Column c, bool calmLabels, out uint word)
        {
            bool rapidTop = c.RapidsReach && (!calmLabels || c.Ramp > 0);
            // (decision 0069 rule 4: still river water has no current's labels)
            bool calmRiver = calmLabels && c.River && !rapidTop && !c.Still;
            word = 0;
            if (!(c.Ramp > 0 || c.Fall > 0 || c.Edge > 0 || rapidTop || calmRiver)) return false;
            word = Encode(c.Ramp > 0 || c.Fall > 0 ? Math.Max((int)c.Ramp, 1) : 0, FlowDirIndex(c.FlowX, c.FlowZ),
                c.Rapid, Math.Min((int)c.Fall, 255), c.Edge & 3, c.EdgeDepth, Math.Min((int)c.Spill, 6), rapidTop, calmRiver);
            return true;
        }

        /// <summary>The word for these values as given (the caller keeps each within its bits).</summary>
        public static uint Encode(int height, int dir, bool rapid, int fall, int edge, int edgeDepth, int spill, bool rapidTop, bool calmRiver)
            => (uint)(height | dir << 3 | (rapid ? 64 : 0) | fall << 7 | edge << 15 | edgeDepth << 17 | spill << 25
                | (rapidTop ? 1 << 28 : 0) | (calmRiver ? 1 << 29 : 0));

        /// <summary>The flow direction of a flow vector: 0 = north (-z), clockwise in 45° steps (vanilla's n, ne, e, se, s, sw,
        /// w, nw).</summary>
        public static int FlowDirIndex(float fx, float fz)
        {
            double a = Math.Atan2(fx, -fz);
            int i = (int)Math.Round(a / (Math.PI / 4));
            return ((i % 8) + 8) % 8;
        }
    }
}
