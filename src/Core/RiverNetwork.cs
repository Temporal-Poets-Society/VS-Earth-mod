using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>One river reach from rivers.bin: discharge, downstream reach, and a centre line in flow direction.</summary>
    public sealed class RiverReach
    {
        public float Q;
        /// <summary>Index of the downstream reach, -1 at a mouth.</summary>
        public int Next = -1;
        public float[] Lon, Lat;
        /// <summary>Water level (m): the valley floor, de-dammed, smoothed, never rising downstream.</summary>
        public short[] Level;
        /// <summary>Vertex in a lake or the sea: the river must be exactly at that water's level there.</summary>
        public bool[] Fixed;
        /// <summary>0063 (courses rules 4): the water mask's half width of the river at each vertex, in WidthUnit metres
        /// (0: the mask has no river water near; 255: that much or more); null where the data has none (record version 3).</summary>
        public byte[] Width;
        public const double WidthUnit = 5;
    }

    public static class RiverData
    {
        public static List<RiverReach> Read(Stream s) => Read(EarthBinary.ReadAll(s));

        /// <summary>
        /// The reaches of a rivers file already in memory (rivers-glo90.bin, read once for its hash too); <paramref name="name"/>
        /// names it in errors. The reaches must be in flow order, each flowing into a later one or nowhere: the network lays
        /// them out in that order (RiverPoolPlanner's forward pass takes an inflow's water level as laid out), so a reach
        /// that flows into an earlier one would sink to level 0 without an error. A file out of order fails here, naming
        /// the reach.
        /// </summary>
        public static List<RiverReach> Read(byte[] file, string name = "rivers.bin")
        {
            EarthBinary.Expect(file, "EMRV", 12, name);
            int version = BitConverter.ToInt32(file, 4);
            int count = BitConverter.ToInt32(file, 8);
            // decoded without the copy to its exact size (about 50 MB for rivers-glo90.bin, read on every start and river
            // audit): a record that runs past the decoded bytes fails instead
            byte[] b = EarthBinary.UnbrotliAll(file, 12, file.Length - 12, name, out int decoded);
            var list = new List<RiverReach>(count);
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                var re = ReadReach(b, ref o, version);
                if (o > decoded) throw new InvalidDataException(name + ": reach " + i + " runs past the end of the data");
                if (re.Next != -1 && (re.Next <= i || re.Next >= count))
                    throw new InvalidDataException(name + ": reach " + i + " flows into reach " + re.Next + ", not a later one (the reaches must be in flow order)");
                list.Add(re);
            }
            return list;
        }

        /// <summary>One reach record (also rivers-glo90-snap.bin's, RiverSnapData).</summary>
        internal static RiverReach ReadReach(byte[] b, ref int o, int version)
        {
            var r = new RiverReach { Q = BitConverter.ToSingle(b, o) };
            o += 4;
            if (version >= 2) { r.Next = BitConverter.ToInt32(b, o); o += 4; }
            int n = BitConverter.ToInt32(b, o);
            o += 4;
            r.Lon = new float[n]; r.Lat = new float[n]; r.Level = new short[n]; r.Fixed = new bool[n];
            if (version >= 3) r.Width = new byte[n];
            for (int k = 0; k < n; k++)
            {
                r.Lon[k] = BitConverter.ToSingle(b, o);
                r.Lat[k] = BitConverter.ToSingle(b, o + 4);
                r.Level[k] = BitConverter.ToInt16(b, o + 8);
                o += 10;
                if (version >= 2) { r.Fixed[k] = b[o] != 0; o += 1; }
                if (version >= 3) { r.Width[k] = b[o]; o += 1; }
            }
            return r;
        }
    }

    /// <summary>
    /// The large rivers in world coordinates, laid out as navigable water:
    ///  - the water surface is a staircase of flat pools (whole blocks). Pool layout 0 (older worlds): where the real river
    ///    falls gently a pool steps down by one block once the valley floor is a block lower, but not before MinPool blocks
    ///    of flat water; where it falls fast (rapids, falls: the natural barriers) the steps follow each other every
    ///    RampLength blocks. Pool layout 1 (decision 0024): every pool is at least MinPool long, and its drop is as high as
    ///    the valley floor has fallen since it began: 1 block where the river falls gently, more where it falls fast
    ///    (rapids), with no cap. Pool layout 2 (decision 0024 1b): a drop of 2–4 blocks is a chute of 1-block steps
    ///    every 2 blocks (rapids, shallow and rocky in the terrain), one of 5 or more and every listed waterfall a curtain
    ///    with a shallow lip above it and a plunge pool below. Before a lake, the sea or a confluence the river starts
    ///    stepping down early enough to arrive at that level.
    ///  - after each step a ramp of flowing water (heights 6..1, VS's own spreading pattern) lies on the lower pool, so
    ///    the surface slopes down over RampLength blocks instead of dropping.
    ///  - the steps are placed by distance along the river; a column's position along the river is blended over the
    ///    nearby pieces of the centre line, so a step runs straight across the river even in a bend.
    /// Widths are exaggerated with a gentle power of the discharge (Daugava ~14 blocks, Rhine ~20, Danube ~28).
    /// Built in stages from the world's river settings (RiverBuildOptions): the courses (RiverCoursePreparer), the pools
    /// (RiverPoolPlanner), the pieces (RiverSegmentBuilder) and their index (RiverSpatialIndexBuilder); this class keeps what
    /// the queries read. Read-only after construction (thread-safe).
    /// </summary>
    public sealed partial class RiverNetwork
    {
        /// <summary>The reaches this network was built from (EarthData.Rivers, or as RiverEnds prepared them; same indices); null
        /// after <see cref="ReleaseStartupData"/>.</summary>
        public List<RiverReach> Reaches { get; private set; }
        public readonly double WidthScale;
        /// <summary>Smallest navigable river (m³/s): full width and depth from here.</summary>
        public readonly double MinQ;
        /// <summary>Streams: "ends" (each river continued upstream along its main branch), "all" or "none".</summary>
        public readonly string StreamMode;
        /// <summary>Smallest stream (m³/s), one block wide.</summary>
        public readonly double StreamMinQ;
        public readonly int MinPool;
        /// <summary>World size relative to the standard 256k world (Cylinder World ring 204 800): banks, valleys, pools and
        /// (before width rules 1) the widths scale with it.</summary>
        public readonly double SizeScale;
        /// <summary>Version of the width rules (EarthParams.RiverWidths). 0: the water's width grows with the world (SizeScale),
        /// the same in km on any map. 1: the gameplay width of the standard world is a floor in blocks (worlds up to the
        /// standard size are unchanged), and a river is as wide as it really is where that is wider (big worlds:
        /// RealWidthMetres / MetresPerBlock).</summary>
        public const int WidthRulesVersion = 1;
        /// <summary>Real width (m) of a river: hydraulic geometry, about 8·√Q (Rhine at Lobith ~2300 m³/s: ~380 m; Mississippi
        /// ~17 000: ~1 km; Amazon ~200 000: ~3.6 km). Used from width rules 1.</summary>
        public static double RealWidthMetres(double q) => 8 * Math.Sqrt(Math.Max(q, 0));
        /// <summary>Channel rules 1: the Gaussian's sigma (m) the courses are smoothed by along the way, and the least sigma
        /// (blocks) worth smoothing.</summary>
        public const double CourseSmoothMetres = 200, CourseSmoothMinBlocks = 4;
        /// <summary>Reaches smoothed along the way in this network (channel rules 1).</summary>
        public int SmoothedReaches { get; private set; }
        public readonly int WidthRules;
        /// <summary>Metres per block north-south (from width rules 1, else 0).</summary>
        public readonly double MetresPerBlock;
        /// <summary>What the gameplay widths (and the ship channels' least width) are scaled by: SizeScale before width rules 1,
        /// from then on at most 1 (smaller worlds keep their narrower rivers, bigger ones the standard world's in blocks).</summary>
        public readonly double WidthFloorScale;
        /// <summary>0..1: how much the courses are straightened for the widths (loop cut-offs, smoothing).</summary>
        public readonly double Straighten;
        bool[] kept;
        double[][] lineX, lineZ;
        /// <summary>A reach's course in world coordinates as laid out (straightened), null if not kept.</summary>
        public bool ReachLine(int reach, out double[] x, out double[] z)
        {
            x = lineX != null && reach >= 0 && reach < lineX.Length ? lineX[reach] : null;
            z = x != null ? lineZ[reach] : null;
            return x != null;
        }
        /// <summary>
        /// Drops what only start-up (SeaChannels.Build) and the diagnostics read once the world is built (decision 0025): the
        /// reaches, the laid-out courses (ReachLine) and the pool layout's debug bounds. Columns are unchanged; the river audit
        /// then works on a network built again (EarthParams.BuildRivers).
        /// </summary>
        public void ReleaseStartupData()
        {
            Reaches = null;
            lineX = lineZ = null;
            dbgCap = null;
            dbgBound0 = null;
        }
        /// <summary>Whether a reach is part of this world's rivers (navigable, or a stream).</summary>
        public bool Kept(int reach) => kept != null && reach >= 0 && reach < kept.Length && kept[reach];
        bool[] fades;
        /// <summary>Whether a reach lies on the faded last stretch of a river ending in a dry basin (its pieces' flow falls
        /// below the reach's own there).</summary>
        public bool Fades(int reach) => fades != null && reach >= 0 && reach < fades.Length && fades[reach];
        public const int RampLength = 6;
        /// <summary>Version of the pool layout (EarthParams.RiverPoolLayout, decision 0024): 1 = drops follow the real slope (each pool
        /// at least MinPool long, a drop as high as the valley has fallen, rapids where a drop is 2 blocks or more). 0: one
        /// block per step, steps every RampLength blocks where the river falls fast, rapids where steps are close together.
        /// 2 = as 1, but a drop of 2–4 blocks (not a listed waterfall) is a chute of 1-block steps ChuteSpacing apart, and a
        /// curtain (5+ blocks or a listed waterfall) has a lip and a plunge pool (decision 0024 1b). 3 = as 2, with no rock in
        /// the chutes and the bed easing into and out of chutes, lips and plunge pools over EaseLength blocks. 4 = as 3, with
        /// lake rims: a lake more than 3 blocks above a river beside it gives way to the river's channel and banks (LakeRim
        /// blocks beyond them), and the river's banks and valley keep the land the lake's eased shore holds up, so the lake
        /// meets the lower river only at its drop (TerrainModel.ApplyRiver) and no lake water stands above lower dry land
        /// (TerrainModel.SettleSteps). 5 = as 4, with the drops drawn in the shape that stays as generated under Hydrology's
        /// rules (decision 0024 step 3, from the 2026-10-23 rule study): a sill under each 1-block step, rapid sources at a
        /// curtain's lip and at a chute's step edges, rapid falling water in curtains and a spill where a chute's ramp ends
        /// beside the next step (TerrainModel.SettleSteps, EarthWorldGen.PlaceRamps). 6 = as 5, with which 1-block steps and
        /// ramps are rapid decided step by step by Hydrology's rapids window, and spills from ramp height 2 (the rule study's
        /// RapidRule). 7 = as 6, with white water (the rule study's whitewater: 1-block steps 2-5 apart stay as generated, all
        /// rapid): a chute's steps WhiteWaterSpacing apart; after a chute the river keeps stepping down every WhiteWaterSpacing
        /// blocks while its valley falls that fast (a rapids run); and where a chute begins and the valley ahead (along the
        /// river, across reach ends) falls GorgeMinSteps blocks or more within GorgeMinSteps * GorgeSpan pools (a steep stretch
        /// such as the Congo's Livingstone rapids), the river takes up to GorgeMaxSteps of them early as one white-water section of 1-block steps
        /// WhiteWaterSpacing apart, cutting into a gorge, and runs calm until the valley has caught up.</summary>
        /// <remarks>10 = as 9, with the steps led by the valley ahead (StepLead) and no spills (TerrainModel.SettleSteps).
        /// 11 = as 10, with rapids reaches only where the current is strong for the river's size (RapidsPower), none at all
        /// where the world turned rapids off; rapid water only at a rapids reach's drops (its step edges and the ramps below
        /// them), and every other river source ordinary water labelled with the river's flow (decision 0046).</remarks>
        public const int PoolLayoutVersion = 11;
        /// <summary>Pool layout 8 (decision 0027): steps where the valley falls as in layout 6 (no sections), with no sills, and
        /// rapid water on the whole surface of a rapids reach: one whose real valley falls RapidsSlope m/km or more, averaged
        /// over RapidsWindow blocks (times SizeScale) each way along the river. Hydrology reads it through Earth's river flow
        /// query and needs no sills then. New worlds get it only when the installed Hydrology supports the query; else 7.</summary>
        public const double RapidsSlope = 0.5, RapidsWindow = 100;
        /// <summary>Pool layout 9 (the user's play-test, 2026-10-25: "rapids have breaks ... I would expect no jumps in water
        /// height"): the river steps down one block at a time as soon as the valley is a block lower, never closer than
        /// StepSpacing blocks, so each ramp (6..1) runs out where the next step's edge begins and the surface falls an eighth
        /// of a block per block, with no vertical water face. Layouts 2-8 waited MinPool blocks, then took the valley's whole
        /// fall as a chute of 1-block steps ChuteSpacing apart, whose ramps the next step cut off at height 4 (faces of about
        /// 3/4 block, and long still pools between chutes). Where the valley falls faster, the water lags behind it and the
        /// steps come closer (StepGap); a lag of more than ChuteMaxDrop goes down at once (a curtain, as before). Also: the
        /// river beside a lake takes the lake's level by the river's position (its centre line), not by each column
        /// (TerrainModel.ApplyRiver), and the sources of a rapids reach carry the river's flow (EarthWorldGen.PlaceRamps).
        /// New worlds get it only when the installed Hydrology keeps those flow labels (its EarthRiverQueryVersion 2).</summary>
        public const int StepSpacing = RampLength + 1;
        /// <summary>Pool layout 9: the least distance from the last step for the next one, by how far the water is above the
        /// valley floor (1: StepSpacing; 2: 5; 3: 4; more: ChuteSpacing). Steps 5 or 4 apart carry a spill of falling rapid
        /// water (SettleSteps), a small lump in the white water. A floor of 6 instead had none, but let the water lag more
        /// often by more than ChuteMaxDrop, which makes a curtain: 340 drops of 2+ blocks in a 512k world against 121 (7, 6,
        /// 5, 4, 3 by lag: 216; layout 8: 4919).</summary>
        public static int StepGap(int lag) => lag <= 1 ? StepSpacing : lag == 2 ? 5 : lag == 3 ? 4 : (int)ChuteSpacing;
        /// <summary>Pool layout 10 (the play-test of layout 9, 2026-10-26: "there is a gap with still water", a step with a
        /// vertical water face at the Congo): the river looks ahead along the valley and steps down early, StepSpacing apart,
        /// where the valley ahead falls faster than that, instead of lagging behind it and then taking the fall in steps 5,
        /// 4 or 3 apart, whose spills (falling rapid water a block above the ramp) showed as a wall of water. It leads the
        /// valley by at most StepLead blocks; listed falls are not anticipated (their hold and drop stay as before).</summary>
        public const int StepLead = 3;
        /// <summary>Pool layout 11 (decision 0046, a player's report on the Olt and the Po, 2026-11-02: "it's going so fast boats
        /// get dragged away ... there's almost no level difference on that river"): 0.5 m/km is an ordinary lowland gradient,
        /// so most rivers in hilly country were rapids. A rapids reach now also needs a strong current for its size: the
        /// valley's fall (m/m) times the square root of the discharge (m³/s) at least RapidsPower, which is proportional to
        /// the stream power per metre of channel width (width grows about as √Q). The Congo's Livingstone rapids fall under
        /// 1 m/km but carry 40 000 m³/s; a lowland river of 100-500 m³/s needs about 1-2.5 m/km.</summary>
        public const double RapidsPower = 0.025;
        /// <summary>Pool layout 11: the least valley fall (m/km) of a rapids reach, however large the river (the Danube's Iron
        /// Gates fall about 0.4 m/km in Earth's levels; big lowland rivers 0.1 or less).</summary>
        public const double RapidsMinSlope = 0.3;
        /// <summary>Pool layout 11: whether a reach with this valley fall (m/km) and discharge (m³/s) is a rapids reach.</summary>
        public static bool RapidsByPower(double metresPerKm, double q) =>
            metresPerKm >= RapidsMinSlope && metresPerKm / 1000 * Math.Sqrt(Math.Max(0, q)) >= RapidsPower;
        /// <summary>Whether the world has rapids reaches at all (the world option earthRiverRapids, pool layout 11); false: every
        /// reach is calm, and only the drops' own edges foam (layout 6's rule, and Hydrology's).</summary>
        public readonly bool RapidsReaches = true;
        bool[] reachRapids;
        float[] reachSlope, reachValley, reachQ;
        /// <summary>Pool layout 8: whether a reach is a rapids reach, and its water's fall per block (both averaged over
        /// RapidsWindow each way; constant along the reach).</summary>
        public bool ReachRapids(int r) => reachRapids != null && r >= 0 && r < reachRapids.Length && reachRapids[r];
        public double ReachSlope(int r) => reachSlope != null && r >= 0 && r < reachSlope.Length ? reachSlope[r] : 0;
        /// <summary>Pool layout 7: gorge sections - the fewest and most steps in one, and how long a stretch per step (in
        /// MinPool) still counts as steep.</summary>
        public const int GorgeMinSteps = 4, GorgeMaxSteps = 6;
        public const double GorgeSpan = 2;
        /// <summary>Pool layout 7: how many gorge sections the layout made, and how many steps they took (diagnostics).</summary>
        public int GorgeSections, GorgeSteps;
        /// <summary>Basins whose pool layout had not settled when its rounds ran out (RiverPoolPlanner.UnconvergedBasins, CR-R7):
        /// a tributary may stand above its main river at a confluence there.</summary>
        public int UnconvergedBasins;
        /// <summary>Pool layout 7: the distance between a rapids run's 1-block steps (blocks): each ramp (6, 5, 4, 3, 2) reaches
        /// the next step, so the run is white water from end to end.</summary>
        public const double WhiteWaterSpacing = 5;
        /// <summary>Pool layout 7: how deep a white-water section's pools are, across the full width (blocks of water). With
        /// 1-deep pools a broken sill made a hole as deep as the pool, and Hydrology's communicating vessels raised the pool to
        /// the next step (the rule study, 2026-10-24); 3 is safe even from a 2-deep hole. The user's choice (2026-10-24): 3,
        /// though a caravel (3 deep) can then pass the rapids.</summary>
        public const int WhiteWaterDepth = 3;
        /// <summary>The distance between a chute's steps in this world's pool layout.</summary>
        public double ChuteGap => Sections ? WhiteWaterSpacing : ChuteSpacing;
        /// <summary>Pool layout 7 only: white water in gorge sections (chutes 5 apart, sections, runs, 3-deep pools).</summary>
        public bool Sections => PoolLayout == 7;
        /// <summary>This network's pool layout's rules by name.</summary>
        internal RiverPoolRules PoolRules => new RiverPoolRules(PoolLayout);
        /// <summary>Pool layout 4: how far beyond a lower river's banks a lake above it gives way to land (blocks).</summary>
        public const double LakeRim = 2;
        /// <summary>Pool layout 2: the highest drop laid out as a chute, the distance between its steps (blocks), how far above a
        /// curtain its shallow lip reaches, and the plunge pool's greatest extra depth.</summary>
        public const int ChuteMaxDrop = 4, LipLength = 8, MaxPlunge = 8;
        /// <summary>
        /// How far (blocks) this network's water may lag above its valley before it goes down at once, and the highest drop
        /// laid out as a chute: ChuteMaxDrop for a world's rivers; StreamMaxLag for a streams layer's (EA-0131,
        /// RiverBuildOptions.KeepAll). Steps come no closer than ChuteSpacing, so on a valley falling faster than a block in 3 a
        /// river lags up to ChuteMaxDrop and then falls; a one-block stream that did so stood on banks built up to 5 blocks
        /// above the slope (the first bench, the Dolomites). A stream falls as soon as it lags 2: a short curtain with its lip
        /// and plunge pool, the cascades of a mountain brook.
        /// </summary>
        public readonly int MaxLag;
        readonly bool steepStreams;
        /// <summary>
        /// The lag a reach's pools may have above its valley before it falls (RiverPoolPlanner): MaxLag, but StreamMaxLag for a
        /// one-block stream of a network with coarse river rules 2 (RiverBuildOptions.SteepStreams, EA-0179). A coarse world's
        /// streams were laid out with a river's ChuteMaxDrop: in a valley falling faster than a block in 3 such a stream stood
        /// on banks built up to 5 blocks above the slope, or, its pools held under its levels to reach the ones downstream, in
        /// a trench (the Dudh Kosi, high Andean streams, the Passer at Meran with a smaller Smallest stream). Only the lag: the
        /// backward bound still holds their pools under their own levels where it must (a streams layer's network, its levels
        /// fitted to the land first, lets them stand at their levels; a coarse world's levels are not fitted, and its water
        /// rose with them above the land drawn at 1:50 in Greenland and the Himalaya, EA-0179).
        /// </summary>
        internal int LagOf(int reach) => steepStreams && reach >= 0 && reach < Reaches.Count && Reaches[reach].Q < MinQ ? StreamMaxLag : MaxLag;
        public const int StreamMaxLag = 1;
        public const double ChuteSpacing = 3;
        /// <summary>Pool layout 3: how far the bed eases from the river's depth to a chute's or lip's shallow depth, and out of a
        /// plunge pool (blocks).</summary>
        public const double EaseLength = 8;
        public readonly int PoolLayout;

        internal struct Seg
        {
            /// <remarks>(T-L8, code review 14 November 2026) Floats: a block position from 2^21 (2,097,152) blocks on is
            /// kept to 0.25 block, from 2^22 to 0.5 block. With the ring's x, that is east of about 17.6° E at 1:10 and 18.4° E
            /// at 1:5. A course's line moves by at most that; a column's water, width and bed round to whole blocks with
            /// noise of a block, so the snap is not seen. Kept as floats: the segments' memory and every saved world's
            /// rivers.</remarks>
            public float X1, Z1, X2, Z2, Q, Len;
            /// <summary>Channel rules 1 (0063): the river's measured half width (blocks; RiverReach.Width) at the segment's start
            /// and end, 0 where it has none (the flow's width there).</summary>
            public float M1, M2;
            // Membership in the original 128-block grid is part of query semantics:
            // Nearest's second pass blends only the segments in that exact cell.
            public int CellX0, CellX1, CellZ0, CellZ1;
            /// <summary>Reach index, and the distance along the reach at the segment start.</summary>
            public int Reach;
            public float S0;
            /// <summary>Prolongs a river mouth out to open water (the river data can stop short of the coastline).</summary>
            public bool Mouth;
        }

        /// <summary>
        /// Per reach: pool top at its start, distance from the last step upstream to its start, its steps; its length, the
        /// reach downstream and the inflow it continues (the river's own upstream reach, -1 at a source).
        /// </summary>
        internal sealed class Pools
        {
            public int P0;
            public float D0;
            public float[] Drops;
            public bool[] Rapid;
            /// <summary>Pool layout 2, per step: Step (a lone 1-block step), Chute, Curtain (5+ blocks or a listed waterfall: lip and
            /// plunge pool), ShortCurtain (2+ blocks at one place without room for a chute: into a lake, at a reach's end);
            /// and for a curtain, its height.</summary>
            public byte[] Shape, Height;
            /// <summary>Pool layout 1: the last drop before this reach's start (along its main inflow) was rapids.</summary>
            public bool RapidIn;
            public double Len;
            public int Next = -1, MainIn = -1;
        }

        internal const byte ShapeStep = 0, ShapeChute = 1, ShapeCurtain = 2, ShapeShortCurtain = 3;

        Seg[] segs;
        Pools[] pools;
        readonly Dictionary<long, int[]> grid = new Dictionary<long, int[]>();
        internal const double Cell = 128;
        readonly int indexSpan;
        readonly EarthGeometry geo;
        /// <summary>
        /// Seam crossings found by the ring's geometry (EarthGeometry.AcrossSeam; worlds with river end rules). Older worlds keep
        /// the fixed test "a piece longer than 4000 blocks crosses the seam", which with rings from about 1 million blocks also
        /// dropped real pieces of the data (gaps in the river, steps in its pools); below that it gives the same result.
        /// </summary>
        readonly bool geometrySeams;
        internal bool AcrossSeam(double x1, double x2, double len) => geometrySeams ? geo.AcrossSeam(x1, x2) : len > 4000;
        /// <summary>Identifies this network in the per-thread query caches (never reused within a process).</summary>
        readonly long cacheOwner = System.Threading.Interlocked.Increment(ref lastCacheOwner);
        static long lastCacheOwner;
        public int SegmentCount => segs?.Length ?? 0;
        /// <summary>The waterfalls placed on this network (RiverFalls), none unless the world has them.</summary>
        public readonly IReadOnlyList<PlacedFall> Falls;

        /// <summary>The network of these courses with the settings given one by one (as <see cref="RiverBuildOptions"/>'s
        /// defaults).</summary>
        public RiverNetwork(List<RiverReach> reaches, EarthGeometry geo, double minQ, double widthScale, Func<double, int> waterTop,
            string streamMode = "none", double streamMinQ = 20, double sizeScale = 1, double straighten = 0, double smoothWidths = 0, int minPool = 32,
            IReadOnlyList<RiverFall> falls = null, Func<double, double, double, double, bool> seaLevelPiece = null,
            bool continueDownstream = false, bool[] fadeEnds = null, bool geometrySeams = false, int widthRules = 0, int poolLayout = 0, bool rapids = true,
            int courses = 0, double landCellMetres = 0, int channelRules = 0, bool[] seaEnds = null)
            : this(reaches, geo, waterTop, new RiverBuildOptions(minQ, widthScale)
            {
                StreamMode = streamMode, StreamMinQ = streamMinQ, SizeScale = sizeScale, Straighten = straighten, SmoothWidths = smoothWidths,
                MinPool = minPool, Falls = falls, SeaLevelPiece = seaLevelPiece, ContinueDownstream = continueDownstream, FadeEnds = fadeEnds,
                GeometrySeams = geometrySeams, WidthRules = widthRules, PoolLayout = poolLayout, Rapids = rapids, Courses = courses,
                LandCellMetres = landCellMetres, ChannelRules = channelRules, SeaEnds = seaEnds,
            })
        {
        }

        /// <summary>
        /// The network of these courses (the reaches' indices stay this network's reach numbers) on this ring, with the water
        /// top the terrain gives a level (TerrainModel.RiverWaterTop). Built in stages, each reading only what the stages
        /// before it made: the courses (RiverCoursePreparer), the pools (RiverPoolPlanner), the pieces
        /// (RiverSegmentBuilder) and their index (RiverSpatialIndexBuilder). The network keeps what the queries read.
        /// </summary>
        public RiverNetwork(List<RiverReach> reaches, EarthGeometry geo, Func<double, int> waterTop, RiverBuildOptions options)
        {
            // the network's settings: what the widths and the stages below read
            this.geo = geo;
            Courses = options.Courses;
            ChannelRules = options.ChannelRules;
            // 0053: banks and valleys in metres, from the land heights' own cell (GLO-90 rivers on GLO-90 land only)
            double blockMetres = geo != null && geo.BlocksPerDegreeZ > 0 ? Geo.MetresPerDegree / geo.BlocksPerDegreeZ : 0;
            if (options.Courses >= 1 && options.LandCellMetres > 0 && blockMetres > 0)
            {
                bankUnit = Math.Max(1, BankMetres / blockMetres);
                landCellBlocks = options.LandCellMetres / blockMetres;
            }
            PoolLayout = options.PoolLayout;
            RapidsReaches = options.Rapids;
            geometrySeams = options.GeometrySeams;
            Reaches = reaches;
            MinQ = options.MinQ;
            WidthScale = options.WidthScale;
            SizeScale = options.SizeScale > 0 ? options.SizeScale : 1;
            WidthRules = options.WidthRules;
            WidthFloorScale = WidthRules >= 1 ? Math.Min(SizeScale, 1) : SizeScale;
            MetresPerBlock = WidthRules >= 1 && geo != null && geo.BlocksPerDegreeZ > 0 ? Geo.MetresPerDegree / geo.BlocksPerDegreeZ : 0;
            // (EA-0159) the river width multiplier measures in metres per block: with width rules 1 only
            WidthMultiplier = MetresPerBlock > 0 && options.WidthMultiplier > 0 && double.IsFinite(options.WidthMultiplier) ? options.WidthMultiplier : 0;
            FloorFactor = WidthMultiplier > 0 ? FloorFactorFor(WidthMultiplier, geo.RingLength) : 1;
            MaskWeight = WidthMultiplier > 0 ? MaskWeightFor(FloorFactor) : 1;
            // Group complete legacy cells rather than changing their boundaries.
            // Large worlds otherwise replicate every widened segment into O(scale²)
            // tiny grid buckets, consuming gigabytes before world generation starts.
            indexSpan = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(SizeScale)));
            Straighten = Math.Clamp(options.Straighten, 0, 1);
            StreamMode = options.StreamMode ?? "none";
            StreamMinQ = Math.Min(options.StreamMinQ, options.MinQ);
            MinPool = Math.Max(RampLength, (int)Math.Round(options.MinPool * SizeScale));
            // (EA-0131: a streams layer's one-block streams fall as soon as they lag 2 blocks: MaxLag)
            MaxLag = options.KeepAll ? StreamMaxLag : ChuteMaxDrop;
            steepStreams = options.SteepStreams && options.OneBlockStreams && !options.KeepAll;
            // (EA-0179) coarse river rules: rivers and one-block streams, nothing between
            OneBlockStreams = options.OneBlockStreams;
            // (EA-0179) coarse river rules 2: rivers to their end, head streams (copies of the reaches they change)
            var coarse = CoarseRivers.Apply(reaches, options, StreamMinQ, StreamMode);
            reaches = coarse.Reaches;
            Reaches = reaches;
            HeadStreams = coarse.HeadStreams;
            RiversToTheirEnd = coarse.Raised;
            HeadStreamCount = coarse.Heads;
            HeadStreamKm = coarse.HeadKm;

            // the courses as laid out: the reaches this world shows, in world coordinates, straightened, measured
            var prepared = RiverCoursePreparer.Prepare(this, geo, reaches, options, blockMetres);
            kept = prepared.Keep;
            lineX = prepared.X;
            lineZ = prepared.Z;
            SmoothedReaches = prepared.Smoothed;
            // the pools: the falls placed, each reach's staircase of pools, rapids
            var planner = new RiverPoolPlanner(this, geo, prepared, options.Falls, waterTop);
            planner.Plan();
            pools = planner.ReachPools;
            Falls = planner.PlacedFalls;
            dbgCap = planner.JunctionCaps;
            dbgBound0 = planner.FirstBounds();
            GorgeSections = planner.GorgeSections;
            GorgeSteps = planner.GorgeSteps;
            UnconvergedBasins = planner.UnconvergedBasins;
            reachRapids = planner.ReachRapids;
            reachSlope = planner.ReachSlope;
            reachValley = planner.ReachValley;
            reachQ = planner.ReachQ;
            // the pieces the queries look up, and their index
            var pieces = new RiverSegmentBuilder(this, geo, prepared, pools, options.FadeEnds, geometrySeams, ChannelRules, options.SeaEnds);
            segs = pieces.Segs;
            fades = pieces.Fades;
            segFirst = pieces.SegFirst;
            segLast = pieces.SegLast;
            RiverSpatialIndexBuilder.Build(this, segs, grid);
        }

        internal static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
        // C# integer division truncates towards zero, so negative cells need floor division.
        internal int Bucket(int cell) => cell / indexSpan - (cell < 0 && cell % indexSpan != 0 ? 1 : 0);
        static bool InCell(in Seg s, int cx, int cz) => cx >= s.CellX0 && cx <= s.CellX1 && cz >= s.CellZ0 && cz <= s.CellZ1;

        /// <summary>Half the water width (blocks): the gameplay floor (times FloorFactor in a world with a river width
        /// multiplier), or the real width where wider; streams narrow towards the smallest, which a multiplied world never goes
        /// under.</summary>
        public double HalfWidth(double q)
        {
            double nav = 0.5 * Math.Clamp(2.0 * Math.Pow(Math.Max(q, MinQ), 0.3), 5, 48) * WidthScale * WidthFloorScale;
            if (WidthMultiplier > 0) nav *= FloorFactor;
            if (MetresPerBlock > 0) nav = Math.Max(nav, 0.5 * RealWidthMetres(Math.Max(q, MinQ)) / MetresPerBlock);
            double hw = StreamHalfWidth(q, nav);
            return WidthMultiplier > 0 ? Math.Max(MinHalfWidth, hw) : hw;
        }

        /// <summary>
        /// EA-0159 (decision 0095): the world's river width multiplier (EarthParams.RiverWidthMultiplier, the Earth tab's "River
        /// width"; ×1 = the widths of 1:10, where width rules 1 were tuned). The gameplay floor, a width in blocks the same at every
        /// scale, is drawn that many times as wide in metres as at 1:10: times FloorFactor, multiplier × ReferenceScale / N in
        /// blocks at 1:N (never more than the multiplier: finer than 1:10 keeps 1:10's blocks). The real width (8·√Q, or the
        /// water mask's measure) is never multiplied. The mask's ratio to the usual width for the flow (0.5-3) moves the floor
        /// only as far as MaskWeight says (MeasuredHalfWidth): fully where the floor is 1:10's in blocks or more, not at all where
        /// it is half that or less (1:100's ×5), fading between. Its narrowing and widening of the real width always count. A
        /// river is never narrower than the smallest stream (MinHalfWidth), and its channel no deeper than its half width
        /// (Depth). The ships' least width of exit channels shrinks with the floor (ShipFloorFactor).
        /// 0 (every world made before, and a new one whose multiplier gives exactly 1:10's floor in blocks, ×1 at 1:10 or ×2.5 at
        /// 1:25): 2.1's widths, the code paths as they were. Side arms are reaches of their own and take the same rule; so do a
        /// streams layer's networks (StreamLayer takes the world's RiverBuildOptions).
        /// </summary>
        public readonly double WidthMultiplier;
        /// <summary>The scale whose widths are ×1: 1:10, where width rules 1's floor is about the real width.</summary>
        public const double ReferenceScale = 10;
        /// <summary>What the gameplay floor in blocks is multiplied by (FloorFactorFor); 1 without a multiplier.</summary>
        public readonly double FloorFactor;
        /// <summary>How far the water mask's ratio moves the floor (MaskWeightFor); 1 without a multiplier (channel rules 2).</summary>
        public readonly double MaskWeight;
        /// <summary>The floor factor at and under which the mask's ratio leaves the floor alone (MaskWeightFor).</summary>
        public const double MaskFadeFrom = 0.5;
        /// <summary>The share of the ship channels' least width (SeaChannels) a world keeps: FloorFactor, 1 without a multiplier.</summary>
        public double ShipFloorFactor => FloorFactor;

        /// <summary>The scale 1:N of a world with this Cylinder World ring: the Equator over the ring, to the nearest whole
        /// number (1:25's ring gives 25.007), at least 1; ReferenceScale without a ring.</summary>
        public static double ScaleOf(double ringLength) =>
            ringLength > 0 ? Math.Max(1, Math.Round(SpawnSearch.EquatorMetres / ringLength)) : ReferenceScale;

        /// <summary>The floor factor of a river width multiplier on a ring: multiplier × ReferenceScale / N, at most the
        /// multiplier (×1 at 1:10 gives 1; ×2.5 at 1:25 gives 1; ×5 at 1:100 gives 0.5).</summary>
        public static double FloorFactorFor(double multiplier, double ringLength) => multiplier * Math.Min(1, ReferenceScale / ScaleOf(ringLength));

        /// <summary>How far the mask's ratio moves the floor at a floor factor: 0 up to MaskFadeFrom, 1 from 1, linear between.</summary>
        public static double MaskWeightFor(double floorFactor) => Math.Clamp((floorFactor - MaskFadeFrom) / (1 - MaskFadeFrom), 0, 1);

        /// <summary>The gameplay floor alone, times FloorFactor, narrowed for streams as HalfWidth narrows them.</summary>
        double FloorHalfWidth(double q) =>
            StreamHalfWidth(q, 0.5 * Math.Clamp(2.0 * Math.Pow(Math.Max(q, MinQ), 0.3), 5, 48) * WidthScale * WidthFloorScale * FloorFactor);

        /// <summary>The real width alone (8·√Q, from the smallest navigable river's for streams), narrowed for streams as
        /// HalfWidth narrows them: the part of HalfWidth no multiplier changes.</summary>
        double RealPartHalfWidth(double q) => StreamHalfWidth(q, 0.5 * RealWidthMetres(Math.Max(q, MinQ)) / MetresPerBlock);

        /// <summary>
        /// A multiplied world (EA-0159): the half width (blocks) at which a river of discharge q whose water mask measured
        /// measuredMetres (the mask's half width) is drawn. The ratio of the measure to the usual half width for the flow
        /// (RealWidthMetres), bounded to MinWidthShare-MaxWiden as channel rules 2 bound it, scales the real part fully and the
        /// floor to the power MaskWeight; the wider of the two, at least the smallest stream's. With the weight 1 this is
        /// channel rules 2's rule (the ratio times HalfWidth); with 0 the floor stands as it is and only the real width follows
        /// the mask, so a broad lowland river can't triple an exaggerated floor (the Daugava at Riga was 45 blocks at 1:100).
        /// </summary>
        internal double MeasuredHalfWidth(double q, double measuredMetres)
        {
            double ratio = Math.Clamp(measuredMetres / Math.Max(1, 0.5 * RealWidthMetres(q)), MinWidthShare, MaxWiden);
            double floor = FloorHalfWidth(q) * (MaskWeight >= 1 ? ratio : MaskWeight <= 0 ? 1 : Math.Pow(ratio, MaskWeight));
            return Math.Max(MinHalfWidth, Math.Max(floor, RealPartHalfWidth(q) * ratio));
        }
        /// <summary>The half width the courses are straightened and smoothed for: before width rules 1 the water's, from then on
        /// still that of rules 0 (it grows with the world), so the centre lines are the same with either rules.</summary>
        internal double CourseHalfWidth(double q) => WidthRules >= 1
            ? StreamHalfWidth(q, 0.5 * Math.Clamp(2.0 * Math.Pow(Math.Max(q, MinQ), 0.3), 5, 48) * WidthScale * SizeScale)
            : HalfWidth(q);

        double StreamHalfWidth(double q, double nav)
        {
            if (q >= MinQ) return nav;
            // streams narrow towards the smallest, one block wide (0.8: still continuous along a diagonal)
            return MinHalfWidth + (nav - MinHalfWidth) * Math.Pow(StreamT(q), 1.5);
        }

        /// <summary>Channel rules 1: the water's half width at t along a piece whose flow gives flowHalfWidth and whose measured
        /// half width is m1, m2 at its ends (0: none there, the flow's): at each end the smaller of the two, but never under
        /// MinWidthShare of the flow's (CappedHalfWidth), along the piece. Channel rules 2 (decision 0069): the measured one
        /// wider than the flow's too, up to MaxWiden times it (MaskWidthHalfWidth).</summary>
        double MaskedHalfWidth(double flowHalfWidth, float m1, float m2, double t)
        {
            // (EA-0159) a multiplied world: the measured half widths are already drawn widths (MeasuredHalfWidth)
            bool widen = ChannelRules >= MaskWidthChannelRules, drawn = WidthMultiplier > 0;
            double a = m1 > 0 ? (drawn ? m1 : widen ? MaskWidthHalfWidth(flowHalfWidth, m1) : CappedHalfWidth(flowHalfWidth, m1)) : flowHalfWidth;
            double b = m2 > 0 ? (drawn ? m2 : widen ? MaskWidthHalfWidth(flowHalfWidth, m2) : CappedHalfWidth(flowHalfWidth, m2)) : flowHalfWidth;
            return a + (b - a) * t;
        }

        /// <summary>
        /// Channel rules 2 (decision 0069): the river as wide as the water mask measured it, wider than its flow's usual width
        /// too (up to MaxWiden times it), and never under MinWidthShare of it. Before, only a narrower measure counted: where
        /// the mask's river was two pixels across (the Lielupe, about 160 m) the course drew its flow's 90 m, and where it was
        /// three or more the mask's own water was drawn (about 270 m), so the river went thin and wide by turns, ponds along
        /// it. The depth stays the flow's (DeepenFor deepens only a narrower channel).
        /// </summary>
        public static double MaskWidthHalfWidth(double flowHalfWidth, double measured) =>
            Math.Clamp(measured, MinWidthShare * flowHalfWidth, MaxWiden * flowHalfWidth);

        /// <summary>Channel rules 1: the half width of a river whose flow gives flowHalfWidth where it was measured at measured
        /// (blocks, > 0): the measured one where narrower, but at least MinWidthShare of the flow's (the author: "where river
        /// narrows irl and goes deeper we should try similar, but need to keep also some width").</summary>
        public static double CappedHalfWidth(double flowHalfWidth, double measured) =>
            Math.Max(Math.Min(flowHalfWidth, measured), MinWidthShare * flowHalfWidth);

        /// <summary>Channel rules 1: how much deeper than Depth(q) a channel this much narrower than the flow's is: the flow's
        /// width over its own, so the cross-section stays the flow's (at most MaxDeepen, the half width never being under
        /// MinWidthShare of the flow's); 1 where it is as wide.</summary>
        public static double DeepenFor(double flowHalfWidth, double halfWidth) =>
            halfWidth < flowHalfWidth && halfWidth > 0 ? Math.Min(MaxDeepen, flowHalfWidth / halfWidth) : 1;
        /// <summary>Channel depth at the centre (blocks): a block more for wider rivers (bigger ships); streams shallower.</summary>
        public double Depth(double q)
        {
            // (EA-0159) a multiplied world: a channel no deeper than its half width (the gameplay depth made a 4-block-wide
            // river a slot 7 deep)
            if (WidthMultiplier > 0) return Math.Min(GameplayDepth(q), Math.Max(1, HalfWidth(q)));
            return GameplayDepth(q);
        }
        double GameplayDepth(double q)
        {
            double nav = Math.Clamp(2 + 0.6 * Math.Log(Math.Max(q, MinQ)), 4, 9) + Math.Max(0, WidthScale - 1) * 2;
            if (q >= MinQ) return nav;
            return 1 + (nav - 1) * Math.Pow(StreamT(q), 1.5);
        }
        /// <summary>Width of the flat bank strip beyond the water (blocks).</summary>
        public double Bank(double q) => BankOf(StreamT(q), HalfWidth(q));
        /// <summary>How far beyond the bank the valley eases back into the natural terrain (blocks); streams cut small valleys.</summary>
        public double Valley(double q) => ValleyOf(StreamT(q), HalfWidth(q));
        // Bank and Valley from StreamT and HalfWidth (pure functions of q): Reach evaluates each of those once.
        double BankOf(double streamT, double halfWidth) => landCellBlocks > 0
            ? (1 + streamT) * bankUnit + 0.25 * halfWidth
            : (1 + streamT) * SizeScale + 0.25 * halfWidth;
        // 0053: on GLO-90 land the valley is in the heights; the river shapes only what the land grid can't show: about a
        // third of a cell (more for big rivers) beyond the bank, at least two blocks
        double ValleyOf(double streamT, double halfWidth) => landCellBlocks > 0
            ? Math.Max(2, (0.3 + 0.4 * streamT) * landCellBlocks) + 0.5 * halfWidth
            : (4 + 12 * streamT) * SizeScale + 1.5 * halfWidth;

        /// <summary>0053: the version of the rivers on GLO-90 (EarthParams.RiverCourses): courses and levels from
        /// rivers-glo90.bin, loops cut only at the water's own width, banks and valleys in metres from the land grid. 2 (item
        /// 14, tall.14): the same with the reaches the water atlas moved into the water mask's rivers (rivers-glo90-snap.bin,
        /// Glo90SnapCourses). 3 (item 16, tall.16): and the side arms round the islands the water mask shows in the rivers
        /// (rivers-glo90-arms.bin, Glo90ArmsCourses). 4 (0063): every reach from 20 m³/s moved onto the mask's water,
        /// smoothed, with the mask's half width at each vertex, and the arms built on those (Glo90WidthCourses). 5 (decision
        /// 0092, EA-0148): rules 4's on GLO-90's pixel-is-point grid (rivers-glo90-snap-3.bin, rivers-glo90-arms-3.bin,
        /// Glo90PointCourses): rules 4 put the mask's cells half a cell south-east of the heights. A world gets less when
        /// the installed data can't give it the newest (EarthData.Glo90RiverCourses).</summary>
        public const int Glo90CoursesVersion = 5;
        /// <summary>The first courses rules with rivers-glo90-snap.bin applied.</summary>
        public const int Glo90SnapCourses = 2;
        /// <summary>The first courses rules with rivers-glo90-arms.bin applied (after the snap).</summary>
        public const int Glo90ArmsCourses = 3;
        /// <summary>0063: the courses rules with every reach from 20 m³/s moved onto the mask's river (rivers-glo90-snap-2.bin,
        /// rivers-glo90-arms-2.bin) and the mask's half width at each vertex (RiverReach.Width).</summary>
        public const int Glo90WidthCourses = 4;
        /// <summary>Decision 0092 (EA-0148): courses rules 4's on GLO-90's pixel-is-point grid (rivers-glo90-snap-3.bin,
        /// rivers-glo90-arms-3.bin).</summary>
        public const int Glo90PointCourses = 5;
        /// <summary>The bank's width per unit (m) on GLO-90 rivers: a block at 1:15 and smaller scales, more at larger ones.</summary>
        public const double BankMetres = 15;
        /// <summary>This network's courses rules (0: rivers.bin; Glo90CoursesVersion: the rivers on GLO-90).</summary>
        public readonly int Courses;
        /// <summary>
        /// 0063: the world's channel rules (EarthParams.ChannelRules, TerrainModel.ChannelRulesVersion). From 1 a course's water is
        /// as wide as the smaller of the flow's (HalfWidth) and the river's measured half width at its vertices (RiverReach.Width:
        /// the water mask's, or a gorge's valley floor; along each piece), but at least MinWidthShare of the flow's, and as much
        /// deeper as it is narrower (the flow's cross-section); banks, valleys and the width a course claims columns with
        /// (Nearest) follow it, so what a course claims it fills. A hit's direction is blended across the course's vertices.
        /// 0: the flow's width everywhere, each piece's own direction.
        /// </summary>
        public readonly int ChannelRules;
        /// <summary>Channel rules 1: a channel is never narrower than this share of the flow's width, however narrow it was
        /// measured (the lower Tigris loses water to the marshes and is 3.4 times narrower in the mask than its flow)...</summary>
        public const double MinWidthShare = 0.5;
        /// <summary>...so it is at most this many times deeper than the flow's depth (1 / MinWidthShare).</summary>
        public const double MaxDeepen = 2;
        /// <summary>Channel rules 2: the channel follows the mask's measured width wider than the flow's too
        /// (MaskWidthHalfWidth), up to this many times the flow's.</summary>
        public const int MaskWidthChannelRules = 2;
        public const double MaxWiden = 3;
        /// <summary>Channel rules 4 (decision 0073): a piece the mask widens reaches as far as its widened water, bank and
        /// valley in the index and the queries (SegReach). Before, its flow's reach clipped it: the blended depth of the mask's
        /// water and the course's banks ended along straight lines (an axis-aligned box and a line along the course; the
        /// Daugava at Ķīšezers, a 7-block bed against a 3-block one).</summary>
        public const int MaskReachChannelRules = 4;
        /// <summary>Channel rules 5 (decision 0074, EA-0055): one width along a river. The measured half widths are averaged
        /// along each reach (RiverCoursePreparer.OneWidth, over a Gaussian of OneWidthSigma of the flow's widths), and the mask's
        /// river water further out than the river's one width, where it ends soon beyond, is land (TerrainModel.OutlineRiver, PondProfileOf).</summary>
        public const int PondChannelRules = 5;
        public const double OneWidthSigma = 2;

        /// <summary>How many reaches the network was built from (per-reach tables: TerrainModel.ReachSpeed); 0 without the
        /// pieces' reach ranges (channel rules 0).</summary>
        public int ReachCount => segFirst?.Length ?? 0;

        /// <summary>A piece's reach for the index and the queries: its flow's (Reach(Q)), or from channel rules 4 the farthest
        /// its mask-widened water, bank and valley reach (MaskedHalfWidth at either end).</summary>
        internal double SegReach(float m1, float m2, float q, double reach, double halfWidth)
        {
            if (ChannelRules < MaskReachChannelRules || !(m1 > 0 || m2 > 0)) return reach;
            double hw = Math.Max(MaskedHalfWidth(halfWidth, m1, m2, 0), MaskedHalfWidth(halfWidth, m1, m2, 1)), st = StreamT(q);
            return Math.Max(reach, hw + BankOf(st, hw) + ValleyOf(st, hw));
        }
        /// <summary>The smallest stream's half width (blocks): still continuous along a diagonal.</summary>
        internal const double MinHalfWidth = 0.8;
        // channel rules 1: each reach's first and last piece in segs (-1: none)
        int[] segFirst, segLast;
        // the land heights' cell (blocks) and the bank unit (blocks) of GLO-90 rivers; 0 for the size-scaled rules
        readonly double landCellBlocks, bankUnit;
        internal double LandCellBlocks => landCellBlocks;
        /// <summary>0 at the smallest stream, 1 from the navigable size on (by the logarithm of the discharge).</summary>
        public double StreamT(double q) => q >= MinQ ? 1 : OneBlockStreams ? 0 : Math.Clamp(Math.Log(Math.Max(q, 1e-3) / StreamMinQ) / Math.Log(MinQ / StreamMinQ), 0, 1);
        /// <summary>
        /// EA-0179 (decision 0098): the coarse river rules of a world at 1:50 and coarser (EarthParams.CoarseRiverRules). A reach
        /// from MinQ is a river (the River width rule); one from StreamMinQ to MinQ a stream as the smallest is (StreamT 0: one block
        /// wide, MinHalfWidth, a block deep, the smallest bank and valley), the way the streams layer draws its one-block streams;
        /// under StreamMinQ nothing (SelectReaches). Before (and at 1:25 and finer) a stream narrowed from MinQ's width down to
        /// the smallest at StreamMinQ.
        /// </summary>
        public readonly bool OneBlockStreams;
        /// <summary>(EA-0179, coarse river rules 2) The reaches kept as head streams above a river (CoarseRivers), null without;
        /// RiverCoursePreparer keeps them whatever their discharge.</summary>
        internal readonly bool[] HeadStreams;
        /// <summary>(EA-0179) What coarse river rules 2 did: reaches below a river drawn as a river, rivers given a head stream
        /// and its km (the world's log line).</summary>
        public readonly int RiversToTheirEnd, HeadStreamCount;
        public readonly double HeadStreamKm;
        /// <summary>The coarse river rules this build makes (EarthParams.CoarseRiverRules): 1 rivers, one-block streams, nothing
        /// smaller; 2 also rivers to their end (a reach below a river is a river), head streams (a river whose biggest inflow is
        /// not drawn starts from that inflow's last HeadStreamKmRule as a one-block stream; CoarseRivers) and the steep
        /// streams' lag (LagOf).</summary>
        public const int CoarseRulesVersion = 2;
        /// <summary>Coarse river rules 2: a head stream's length (km of the data's course above the river it feeds).</summary>
        public const double HeadStreamKmRule = 10;

        /// <summary>How long (blocks) a river ending in a dry basin takes to fade to a stream.</summary>
        internal double FadeLength(double q) => 64 * SizeScale + 16 * HalfWidth(q);

        /// <summary>Discharge along a fading end, t 0..1: from the river's own down to the smallest stream (log scale).</summary>
        internal double FadedQ(double q, double t) => Math.Exp(Math.Log(q) + t * (Math.Log(StreamMinQ) - Math.Log(q)));
        /// <summary>How far a river's influence (water, bank, valley) reaches from its centre line (blocks).</summary>
        public double Reach(double q) => Reach(q, out _);
        /// <summary>Reach (= HalfWidth + Bank + Valley, summed in that order) and the half width, each term evaluated once.</summary>
        internal double Reach(double q, out double halfWidth)
        {
            double hw = HalfWidth(q), st = StreamT(q);
            halfWidth = hw;
            return hw + BankOf(st, hw) + ValleyOf(st, hw);
        }

        public struct Hit
        {
            public double Dist, Q, DirX, DirZ;
            /// <summary>Top block of the still water here.</summary>
            public int WaterTop;
            /// <summary>0, or 1..6: height of the flowing layer on top of the pool (just after a step down).</summary>
            public int Ramp;
            /// <summary>The ramp is part of rapids (pool layout 1: below a drop of 2 blocks or more; 0: steps close together).</summary>
            public bool Rapid;
            /// <summary>On the prolongation of a river mouth (only low ground is cut).</summary>
            public bool Mouth;
            /// <summary>On a mouth's prolongation: how far (blocks) past the river's end the nearest point of it lies; 0 elsewhere.</summary>
            public double Beyond;
            /// <summary>Pool layout 2: in a chute (rapids: a shallow, rocky bed); on the lip above a curtain (a shallow bed); the
            /// extra depth of the plunge pool below a curtain (0 elsewhere); rock breaking the chute's surface.</summary>
            public bool Chute, Lip, Rocks;
            public int Plunge;
            /// <summary>Pool layout 3: how far the bed is brought up to a chute's or lip's shallow depth (0..1, eased in and out),
            /// and the extra depth of a plunge pool (eased out below it).</summary>
            public double Shoal, PlungeDepth;
            /// <summary>Pool layout 7: the part of Shoal that comes from a chute or gorge section (not a curtain's lip): its bed
            /// eases to WhiteWaterDepth instead of a lip's 2.</summary>
            public double ChuteShoal;
            /// <summary>For tests: the reach this position was counted on, and the distance along it.</summary>
            public int Reach;
            public double Along;
            /// <summary>The nearest point of the river's centre line, and the water top of the reach's first and last pools
            /// (pool layout 9: the river's level beside a lake is decided there, TerrainModel.ApplyRiver).</summary>
            public double CentreX, CentreZ;
            public int StartTop, EndTop;
            /// <summary>The water's half width here, its channel's depth at the centre (before the shapes of rapids, lips and
            /// plunge pools), the bank's width and the valley's (blocks): the flow's (HalfWidth(Q), Depth(Q), Bank(Q), Valley(Q)),
            /// or from channel rules 1 as narrowed to the measured width (and deepened for it).</summary>
            public double HalfWidth, Depth, BankWidth, ValleyWidth;
            /// <summary>Channel rules 1: the measured half width here (blocks, along the piece), 0 where the piece has none;
            /// Masked: the piece has it at either end.</summary>
            public double MaskHalfWidth;
            public bool Masked;
        }

        /// <summary>
        /// One candidate segment of a 128-block cell, prepared for the per-column queries: the projection terms exactly as
        /// <see cref="Project(in Seg, double, double, out double)"/> computes them, and the segment's influence reach and half width (pure functions of its
        /// discharge, so caching them changes no bit of any result).
        /// </summary>
        struct Candidate
        {
            public double X1, Z1, DX, DZ, Len2, Reach, HalfWidth;
            /// <summary>Box of the projected points (X1 + t * DX for t in 0..1: rounding is monotonic, so it stays inside).</summary>
            public double MinX, MaxX, MinZ, MaxZ;
            public float S0, Len;
            public int RiverReach, Seg;
            /// <summary>Channel rules 1: the mask's half widths at the ends (Seg.M1, M2) and the discharge's StreamT, when either
            /// is set (Masked).</summary>
            public float M1, M2;
            public double StreamT;
            public bool Masked;
        }

        /// <summary>A candidate's half width at t along it, and the reach of its influence there (as Reach: water, bank, valley).</summary>
        double CandidateWidth(in Candidate s, double t, out double reach)
        {
            if (!s.Masked) { reach = s.Reach; return s.HalfWidth; }
            double hw = MaskedHalfWidth(s.HalfWidth, s.M1, s.M2, t);
            reach = hw + BankOf(s.StreamT, hw) + ValleyOf(s.StreamT, hw);
            return hw;
        }

        /// <summary>
        /// Slack for the box test that skips a candidate without projecting: far beyond the rounding of a projection at
        /// any world coordinate, so a skipped candidate is one the exact distance test would have rejected anyway.
        /// </summary>
        const double BoxSlack = 1.0 / 64;

        /// <summary>A cell's candidates in index order (the legacy order: it fixes ties and the blend's summation order).</summary>
        sealed class CellCandidates
        {
            public long Owner;
            public int Cx, Cz, Count;
            public Candidate[] Items = new Candidate[32];
        }

        // Terrain columns are generated chunk by chunk (32 x 32, always inside one 128-block cell) on several worldgen
        // threads, so each thread keeps its last few cells. The cache copies what it needs and holds no reference to the
        // network: the index itself stays immutable and shared, and an unloaded world is not kept alive by a thread.
        const int CacheWays = 4;
        [ThreadStatic] static CellCandidates[] threadCells;
        [ThreadStatic] static int threadCellNext;

        CellCandidates Candidates(int cx, int cz)
        {
            var ways = threadCells;
            if (ways == null)
            {
                ways = threadCells = new CellCandidates[CacheWays];
                for (int i = 0; i < CacheWays; i++) ways[i] = new CellCandidates { Owner = 0 };
            }
            for (int i = 0; i < CacheWays; i++)
            {
                var w = ways[i];
                if (w.Cx == cx && w.Cz == cz && w.Owner == cacheOwner) return w;
            }
            int slot = threadCellNext;
            threadCellNext = (slot + 1) % CacheWays;
            var c = ways[slot];
            // not yet valid while it is being filled (an exception must not leave a half-built entry behind)
            c.Owner = 0;
            c.Count = 0;
            c.Cx = cx;
            c.Cz = cz;
            if (grid.TryGetValue(Key(Bucket(cx), Bucket(cz)), out var ids))
            {
                var items = c.Items;
                int n = 0, lastQ = 0;
                bool haveQ = false;
                double reach = 0, halfWidth = 0;
                foreach (int i in ids)
                {
                    ref readonly var s = ref segs[i];
                    if (!InCell(in s, cx, cz)) continue;
                    // segments come reach by reach: compute the width terms once per run of equal discharge (by bits)
                    int qBits = BitConverter.SingleToInt32Bits(s.Q);
                    if (!haveQ || qBits != lastQ)
                    {
                        reach = Reach(s.Q, out halfWidth);
                        lastQ = qBits;
                        haveQ = true;
                    }
                    if (n == items.Length) Array.Resize(ref items, items.Length * 2);
                    FillCandidate(ref items[n++], in s, i, reach, halfWidth);
                }
                c.Items = items;
                c.Count = n;
            }
            c.Owner = cacheOwner;
            return c;
        }

        void FillCandidate(ref Candidate it, in Seg s, int i, double reach, double halfWidth)
        {
            it.M1 = s.M1;
            it.M2 = s.M2;
            it.Masked = s.M1 > 0 || s.M2 > 0;
            it.StreamT = it.Masked ? StreamT(s.Q) : 0;
            // the same expressions as Project (float differences, then double)
            double dx = s.X2 - s.X1, dz = s.Z2 - s.Z1;
            it.X1 = s.X1;
            it.Z1 = s.Z1;
            it.DX = dx;
            it.DZ = dz;
            it.Len2 = dx * dx + dz * dz;
            double x2 = s.X1 + dx, z2 = s.Z1 + dz;
            it.MinX = Math.Min(it.X1, x2);
            it.MaxX = Math.Max(it.X1, x2);
            it.MinZ = Math.Min(it.Z1, z2);
            it.MaxZ = Math.Max(it.Z1, z2);
            it.Reach = SegReach(s.M1, s.M2, s.Q, reach, halfWidth);
            it.HalfWidth = halfWidth;
            it.S0 = s.S0;
            it.Len = s.Len;
            it.RiverReach = s.Reach;
            it.Seg = i;
        }

        /// <summary>The river closest to (x, z) whose influence (water, bank, valley) reaches it.</summary>
        public bool Nearest(double x, double z, out Hit hit)
        {
            hit = default;
            if (segs == null || segs.Length == 0) return false;
            x = geo.WrapX(x);
            int cx = (int)Math.Floor(x / Cell), cz = (int)Math.Floor(z / Cell);
            var cell = Candidates(cx, cz);
            int count = cell.Count;
            if (count == 0) return false;
            var items = cell.Items;
            double best = double.MaxValue, bestD = 0, bestS = 0, bestT = 0;
            int bk = -1;
            for (int k = 0; k < count; k++)
            {
                ref readonly var s = ref items[k];
                // clearly out of reach (NaN compares false and falls through to the exact test)
                double e = s.Reach + BoxSlack;
                if (x < s.MinX - e || x > s.MaxX + e || z < s.MinZ - e || z > s.MaxZ + e) continue;
                double d = Project(in s, x, z, out double t);
                if (d > s.Reach) continue;
                // prefer the bigger river where two influence zones overlap (confluences); channel rules 1: by the width the
                // course has here (narrowed by the water mask), so that a course claims only what it fills
                double hw = s.HalfWidth;
                if (s.Masked)
                {
                    hw = CandidateWidth(in s, t, out double reachHere);
                    if (d > reachHere) continue;
                }
                double score = d - hw;
                if (score >= best) continue;
                best = score;
                bk = k;
                bestD = d;
                bestS = s.S0 + t * s.Len;
                bestT = t;
            }
            if (bk < 0) return false;
            return HitAt(items, count, bk, x, z, bestD, bestS, bestT, out hit);
        }

        /// <summary>
        /// Channel rules 1 (TerrainModel): the flow at (x, z) from every course whose influence reaches it, each segment's
        /// downstream direction weighted by its half width, falling off with the square of the distance beyond it and
        /// easing out at its reach (OutlineFade): one direction turning smoothly through a confluence and round a bend,
        /// where the nearest course's own made straight-edged wedges of water labelled another way (Rīgas HES). fx, fz in:
        /// the direction to keep where no course reaches; out: the blend, a unit vector.
        /// </summary>
        public void FlowNear(double x, double z, ref double fx, ref double fz) => BlendNear(x, z, 0, ref fx, ref fz);

        /// <summary>
        /// Channel rules 1 (TerrainModel.OutlineRiver): how deep the water mask's river water at (x, z) may be (blocks), from
        /// every course whose influence reaches it rather than the nearest alone (the nearest one's depth made straight-edged
        /// patches a block shallower wherever a tributary's or an arm's end was nearer than the main river: Rīgas HES). Each
        /// course pulls towards its centre depth (Depth, deepened where the mask narrows it) within its water, easing back to
        /// far across its bank and valley (OutlineFade); the deepest course governs, and a shallower one shallows the water
        /// only where no deeper course reaches a block below far. far where no course reaches. Continuous in (x, z). And
        /// FlowNear's blended direction, in the same pass over the candidates.
        /// </summary>
        public double BlendNear(double x, double z, double far, ref double fx, ref double fz)
        {
            if (segs == null || segs.Length == 0) return far;
            x = geo.WrapX(x);
            var cell = Candidates((int)Math.Floor(x / Cell), (int)Math.Floor(z / Cell));
            var items = cell.Items;
            double up = 0, down = 0, sx = 0, sz = 0;
            for (int k = 0; k < cell.Count; k++)
            {
                ref readonly var s = ref items[k];
                double e = s.Reach + BoxSlack;
                if (x < s.MinX - e || x > s.MaxX + e || z < s.MinZ - e || z > s.MaxZ + e) continue;
                double d = Project(in s, x, z, out double t);
                if (d > s.Reach) continue;
                double hw = CandidateWidth(in s, t, out double reach);
                if (d > reach) continue;
                double fade = OutlineFade(d, hw, reach);
                double depth = Depth(segs[s.Seg].Q);
                if (s.Masked) depth *= DeepenFor(s.HalfWidth, hw);
                double v = (depth - far) * fade;
                if (v > up) up = v;
                else if (v < down) down = v;
                double len = Math.Sqrt(s.Len2);
                if (len > 0)
                {
                    double beyond = 1 + Math.Max(0, d - hw), w = fade * hw / (beyond * beyond * len);
                    sx += w * s.DX;
                    sz += w * s.DZ;
                }
            }
            double norm = Math.Sqrt(sx * sx + sz * sz);
            if (norm > 1e-9)
            {
                fx = sx / norm;
                fz = sz / norm;
            }
            return far + up + down * Math.Max(0, 1 - up);
        }

        /// <summary>BlendNear's depth: a course's share at d blocks from it, its water within hw and its influence out to reach
        /// (1 in its water, easing to 0 at reach).</summary>
        public static double OutlineFade(double d, double hw, double reach) =>
            d <= hw ? 1 : 1 - Geo.SmoothStep((d - hw) / Math.Max(1, reach - hw));

        /// <summary>
        /// 0053 item 18: the river course nearest to (x, z) within the given distance (blocks), its influence's
        /// reach or not, chosen as Nearest chooses (the bigger river where two are about as near): the level of the water the
        /// water mask's rivers have there (TerrainModel.ApplyOutlineRiver), which reaches further from the course than its
        /// channel does (the Daugava at Riga is 600-700 m wide, its channel 200 m). Columns within reach take Nearest.
        /// </summary>
        public bool NearestCourse(double x, double z, double within, out Hit hit)
        {
            hit = default;
            if (segs == null || segs.Length == 0) return false;
            x = geo.WrapX(x);
            int cx0 = (int)Math.Floor((x - within) / Cell), cx1 = (int)Math.Floor((x + within) / Cell);
            int cz0 = (int)Math.Floor((z - within) / Cell), cz1 = (int)Math.Floor((z + within) / Cell);
            int bx0 = Bucket(cx0), bx1 = Bucket(cx1), bz0 = Bucket(cz0), bz1 = Bucket(cz1);
            var items = wideScratch ??= new Candidate[64];
            int n = 0, lastQ = 0;
            bool haveQ = false;
            double reach = 0, halfWidth = 0;
            for (int bz = bz0; bz <= bz1; bz++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    if (!grid.TryGetValue(Key(bx, bz), out var ids)) continue;
                    foreach (int i in ids)
                    {
                        ref readonly var s = ref segs[i];
                        if (s.CellX1 < cx0 || s.CellX0 > cx1 || s.CellZ1 < cz0 || s.CellZ0 > cz1) continue;
                        // a segment is in every bucket its reach covers: take it in the first one this search scans
                        if (Math.Max(Bucket(s.CellX0), bx0) != bx || Math.Max(Bucket(s.CellZ0), bz0) != bz) continue;
                        int qBits = BitConverter.SingleToInt32Bits(s.Q);
                        if (!haveQ || qBits != lastQ)
                        {
                            reach = Reach(s.Q, out halfWidth);
                            lastQ = qBits;
                            haveQ = true;
                        }
                        if (n == items.Length) wideScratch = items = ResizeCandidates(items);
                        FillCandidate(ref items[n++], in s, i, reach, halfWidth);
                    }
                }
            }
            double best = double.MaxValue, bestD = 0, bestS = 0, bestT = 0;
            int bk = -1;
            for (int k = 0; k < n; k++)
            {
                ref readonly var s = ref items[k];
                double e = within + BoxSlack;
                if (x < s.MinX - e || x > s.MaxX + e || z < s.MinZ - e || z > s.MaxZ + e) continue;
                double d = Project(in s, x, z, out double t);
                if (d > within) continue;
                double score = d - (s.Masked ? CandidateWidth(in s, t, out _) : s.HalfWidth);
                if (score >= best) continue;
                best = score;
                bk = k;
                bestD = d;
                bestS = s.S0 + t * s.Len;
                bestT = t;
            }
            if (bk < 0) return false;
            return HitAt(items, n, bk, x, z, bestD, bestS, bestT, out hit);
        }

        /// <summary>
        /// Channel rules 3 (TerrainModel.ApplyMouth): every course within <paramref name="within"/> blocks of (x, z), at the
        /// nearest piece of each of its reaches, as Nearest would describe it there; the largest value <paramref name="f"/>
        /// gives one of them, or <paramref name="none"/>. Where two branches of a delta meet, the nearest alone made a step
        /// along the line between them.
        /// </summary>
        /// <summary>Whether any bucket <see cref="MaxNear"/> would read for (x, z, within) holds a piece: false means MaxNear finds
        /// no hit and returns its <c>none</c> (the same bucket range, without a candidate; ER-11, code review 14 November 2026).</summary>
        public bool AnyBucketNear(double x, double z, double within)
        {
            if (segs == null || segs.Length == 0) return false;
            x = geo.WrapX(x);
            int cx0 = (int)Math.Floor((x - within) / Cell), cx1 = (int)Math.Floor((x + within) / Cell);
            int cz0 = (int)Math.Floor((z - within) / Cell), cz1 = (int)Math.Floor((z + within) / Cell);
            int bx0 = Bucket(cx0), bx1 = Bucket(cx1), bz0 = Bucket(cz0), bz1 = Bucket(cz1);
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                    if (grid.TryGetValue(Key(bx, bz), out var ids) && ids.Length > 0) return true;
            return false;
        }

        public double MaxNear(double x, double z, double within, Func<Hit, double> f, double none)
        {
            if (segs == null || segs.Length == 0) return none;
            x = geo.WrapX(x);
            int cx0 = (int)Math.Floor((x - within) / Cell), cx1 = (int)Math.Floor((x + within) / Cell);
            int cz0 = (int)Math.Floor((z - within) / Cell), cz1 = (int)Math.Floor((z + within) / Cell);
            int bx0 = Bucket(cx0), bx1 = Bucket(cx1), bz0 = Bucket(cz0), bz1 = Bucket(cz1);
            var items = maxScratch ??= new Candidate[64];
            int n = 0, lastQ = 0;
            bool haveQ = false;
            double reach = 0, halfWidth = 0;
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    if (!grid.TryGetValue(Key(bx, bz), out var ids)) continue;
                    foreach (int i in ids)
                    {
                        ref readonly var s = ref segs[i];
                        if (s.CellX1 < cx0 || s.CellX0 > cx1 || s.CellZ1 < cz0 || s.CellZ0 > cz1) continue;
                        if (Math.Max(Bucket(s.CellX0), bx0) != bx || Math.Max(Bucket(s.CellZ0), bz0) != bz) continue;
                        int qBits = BitConverter.SingleToInt32Bits(s.Q);
                        if (!haveQ || qBits != lastQ)
                        {
                            reach = Reach(s.Q, out halfWidth);
                            lastQ = qBits;
                            haveQ = true;
                        }
                        if (n == items.Length) maxScratch = items = ResizeCandidates(items);
                        FillCandidate(ref items[n++], in s, i, reach, halfWidth);
                    }
                }
            // the nearest piece of each reach
            var reaches = maxReaches ??= new (int Reach, int K, double D, double S, double T)[16];
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                ref readonly var s = ref items[k];
                double e = within + BoxSlack;
                if (x < s.MinX - e || x > s.MaxX + e || z < s.MinZ - e || z > s.MaxZ + e) continue;
                double d = Project(in s, x, z, out double t);
                if (d > within) continue;
                int j = 0;
                while (j < m && reaches[j].Reach != s.RiverReach) j++;
                if (j == m)
                {
                    if (m == reaches.Length) Array.Resize(ref maxReaches, m * 2);
                    reaches = maxReaches;
                    reaches[m++] = (s.RiverReach, k, d, s.S0 + t * s.Len, t);
                }
                else if (d < reaches[j].D) reaches[j] = (s.RiverReach, k, d, s.S0 + t * s.Len, t);
            }
            double best = none;
            for (int j = 0; j < m; j++)
            {
                var (_, k, d, sAlong, t) = reaches[j];
                if (!HitAt(items, n, k, x, z, d, sAlong, t, out var hit)) continue;
                double v = f(hit);
                if (v > best) best = v;
            }
            return best;
        }

        /// <summary>
        /// Channel rules 4 (TerrainModel.MaskWaterDepth): the flow's depth (Depth(Q)) of the courses near (x, z), blended as
        /// ReachBlend does. Before, the nearest course's alone: in wide water between a big river and a small one the depth
        /// changed by blocks along the straight line halfway between them (the Daugava, 729 m³/s and 7 blocks, against
        /// Ķīšezers' outflow, 22 m³/s and 1 block).
        /// </summary>
        public double DepthNear(double x, double z, double within, double none) => ReachBlend(x, z, within, depthOf ??= (r, q) => Depth(q), none);

        /// <summary>EA-0113 (the Fable pass's FP-24): DepthNear's value, made once instead of a closure per column.</summary>
        Func<int, float, double> depthOf;

        /// <summary>
        /// Channel rules 4: a value of each reach within <paramref name="within"/> blocks of (x, z) (value(reach, its
        /// discharge)), blended by Q / (1 + d)², d the distance to the nearest piece of the reach, easing out towards the
        /// search radius, with <paramref name="none"/> as a course of 1 m³/s half the radius away (far from them all the
        /// blend eases to it): one value that changes smoothly between courses, never along the line halfway between them.
        /// </summary>
        public double ReachBlend(double x, double z, double within, Func<int, float, double> value, double none)
        {
            if (segs == null || segs.Length == 0) return none;
            x = geo.WrapX(x);
            int cx0 = (int)Math.Floor((x - within) / Cell), cx1 = (int)Math.Floor((x + within) / Cell);
            int cz0 = (int)Math.Floor((z - within) / Cell), cz1 = (int)Math.Floor((z + within) / Cell);
            int bx0 = Bucket(cx0), bx1 = Bucket(cx1), bz0 = Bucket(cz0), bz1 = Bucket(cz1);
            var reaches = depthReaches ??= new (int Reach, double D, float Q)[16];
            int m = 0;
            for (int bz = bz0; bz <= bz1; bz++)
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    if (!grid.TryGetValue(Key(bx, bz), out var ids)) continue;
                    foreach (int i in ids)
                    {
                        ref readonly var s = ref segs[i];
                        if (s.CellX1 < cx0 || s.CellX0 > cx1 || s.CellZ1 < cz0 || s.CellZ0 > cz1) continue;
                        if (Math.Max(Bucket(s.CellX0), bx0) != bx || Math.Max(Bucket(s.CellZ0), bz0) != bz) continue;
                        if (s.Mouth) continue;
                        // (EA-0113, FP-24) a piece whose box is clearly beyond the search is not projected: it would be dropped
                        // below (NaN compares false and falls through to the exact test)
                        double e = within + BoxSlack;
                        if (x < Math.Min(s.X1, s.X2) - e || x > Math.Max(s.X1, s.X2) + e || z < Math.Min(s.Z1, s.Z2) - e || z > Math.Max(s.Z1, s.Z2) + e) continue;
                        double d = Project(in s, x, z, out _);
                        if (d > within) continue;
                        int j = 0;
                        while (j < m && reaches[j].Reach != s.Reach) j++;
                        if (j == m)
                        {
                            if (m == reaches.Length) { Array.Resize(ref depthReaches, m * 2); reaches = depthReaches; }
                            reaches[m++] = (s.Reach, d, s.Q);
                        }
                        else if (d < reaches[j].D) reaches[j].D = d;
                    }
                }
            if (m == 0) return none;
            double w0 = 1 / ((1 + 0.5 * within) * (1 + 0.5 * within)), sw = w0, sv = w0 * none;
            for (int j = 0; j < m; j++)
            {
                var (r, d, q) = reaches[j];
                double w = q / ((1 + d) * (1 + d)) * (1 - Geo.SmoothStep(d / within));
                sw += w;
                sv += w * value(r, q);
            }
            return sv / sw;
        }

        /// <summary>Channel rules 4: points along a reach's own pieces (their starts and middles, the last one's end; no mouth
        /// prolongation), in world coordinates; none without them.</summary>
        public void ReachPoints(int reach, Action<double, double> point)
        {
            if (segFirst == null || reach < 0 || reach >= segFirst.Length || segFirst[reach] < 0) return;
            for (int i = segFirst[reach]; i <= segLast[reach]; i++)
            {
                ref readonly var s = ref segs[i];
                if (s.Mouth || s.Reach != reach) continue;
                point(s.X1, s.Z1);
                point(0.5 * (s.X1 + s.X2), 0.5 * (s.Z1 + s.Z2));
                if (i == segLast[reach]) point(s.X2, s.Z2);
            }
        }

        /// <summary>Channel rules 5: points about step blocks apart along a reach's own pieces (no mouth prolongation): the
        /// point, the unit normal to the left of the flow (x, z), the position along the reach (Hit.Along's) and the water's half
        /// width there (the masked one, as HitAt has it); the flow's half width of the reach is returned (0: no pieces).</summary>
        public double ReachSamples(int reach, double step, Action<double, double, double, double, double, double> sample)
        {
            if (segFirst == null || reach < 0 || reach >= segFirst.Length || segFirst[reach] < 0) return 0;
            double flowHw = 0;
            for (int i = segFirst[reach]; i <= segLast[reach]; i++)
            {
                ref readonly var s = ref segs[i];
                if (s.Mouth || s.Reach != reach || !(s.Len > 0)) continue;
                double hw = HalfWidth(s.Q);
                flowHw = Math.Max(flowHw, hw);
                double dx = (s.X2 - s.X1) / s.Len, dz = (s.Z2 - s.Z1) / s.Len;
                int n = Math.Max(1, (int)Math.Ceiling(s.Len / step));
                for (int j = 0; j < n; j++)
                {
                    double t = (j + 0.5) / n;
                    sample(s.X1 + (s.X2 - s.X1) * t, s.Z1 + (s.Z2 - s.Z1) * t, dz, -dx, s.S0 + t * s.Len,
                        s.M1 > 0 || s.M2 > 0 ? MaskedHalfWidth(hw, s.M1, s.M2, t) : hw);
                }
            }
            return flowHw;
        }

        [ThreadStatic] static (int Reach, double D, float Q)[] depthReaches;

        [ThreadStatic] static Candidate[] maxScratch;
        [ThreadStatic] static (int Reach, int K, double D, double S, double T)[] maxReaches;

        [ThreadStatic] static Candidate[] wideScratch;

        static Candidate[] ResizeCandidates(Candidate[] items)
        {
            Array.Resize(ref items, items.Length * 2);
            return items;
        }

        /// <summary>The hit of the candidate bk (index in items, nearest at bestD, bestS along its reach, bestT along the
        /// segment): Nearest's and NearestCourse's.</summary>
        bool HitAt(Candidate[] items, int count, int bk, double x, double z, double bestD, double bestS, double bestT, out Hit hit)
        {
            var b = segs[items[bk].Seg];
            double hw = items[bk].HalfWidth;
            // position along the reach, blended over the pieces of its centre line nearly as close as the nearest one (in a
            // bend two pieces are about equally close), also those of the river's reaches just upstream and downstream,
            // leaving out other loops of the same river (meanders)
            var bp = pools[b.Reach];
            int bReach = b.Reach, bNext = bp.Next, bMainIn = bp.MainIn;
            double W = hw + 6, sum = 0, ws = 0;
            double e2 = bestD + W + BoxSlack;
            for (int k = 0; k < count; k++)
            {
                ref readonly var s = ref items[k];
                double offset;
                int sr = s.RiverReach;
                if (sr == bReach) offset = 0;
                else if (sr == bNext) offset = bp.Len;
                else if (sr == bMainIn) offset = -pools[sr].Len;
                else continue;
                // clearly beyond bestD + W
                if (x < s.MinX - e2 || x > s.MaxX + e2 || z < s.MinZ - e2 || z > s.MaxZ + e2) continue;
                double d = Project(in s, x, z, out double t);
                if (d >= bestD + W) continue;
                double si = offset + s.S0 + t * s.Len;
                if (Math.Abs(si - bestS) > 4 * hw + 24) continue;
                double w = 1 - (d - bestD) / W;
                w *= w;
                sum += w * si;
                ws += w;
            }
            double along = ws > 0 ? sum / ws : bestS;
            // past either end of the reach: the pools of the reach there
            int reach = b.Reach;
            for (int guard = 0; guard < 8; guard++)
            {
                var p = pools[reach];
                if (along > p.Len && p.Next >= 0) { along -= p.Len; reach = p.Next; }
                else if (along < 0 && p.MainIn >= 0) { reach = p.MainIn; along += pools[reach].Len; }
                else break;
            }
            var pl = pools[reach];
            var drops = pl.Drops;
            // steps at or before this position
            int lo = 0, hi = drops.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (drops[mid] <= along) lo = mid + 1; else hi = mid;
            }
            int nDrops = lo;
            double since = nDrops == 0 ? pl.D0 + along : along - drops[nDrops - 1];
            double len = b.Len;
            double dirX = len > 0 ? (b.X2 - b.X1) / len : 0, dirZ = len > 0 ? (b.Z2 - b.Z1) / len : 0;
            if (segFirst != null && len > 0 && !b.Mouth) BlendDirection(items[bk].Seg, in b, bestT, ref dirX, ref dirZ);
            // the water's width, depth, bank and valley here: the flow's, or (channel rules 1) the measured width's where narrower
            ref readonly var bc = ref items[bk];
            double hwHere = bc.HalfWidth, maskHere = 0, depthHere = Depth(b.Q), stHere = StreamT(b.Q);
            if (bc.Masked)
            {
                hwHere = MaskedHalfWidth(bc.HalfWidth, bc.M1, bc.M2, bestT);
                maskHere = RiverStraighten.WidthBetween(bc.M1, bc.M2, bestT);
                depthHere *= DeepenFor(bc.HalfWidth, hwHere);
            }
            hit = new Hit
            {
                Dist = bestD, Q = b.Q,
                DirX = dirX, DirZ = dirZ,
                HalfWidth = hwHere, Depth = depthHere, BankWidth = BankOf(stHere, hwHere), ValleyWidth = ValleyOf(stHere, hwHere),
                MaskHalfWidth = maskHere, Masked = bc.Masked,
                WaterTop = pl.P0 - nDrops,
                Ramp = since >= 0 && since < RampLength ? RampLength - (int)Math.Floor(since) : 0,
                Rapid = nDrops > 0 ? pl.Rapid[nDrops - 1] : pl.RapidIn,
                Mouth = b.Mouth,
                Beyond = b.Mouth ? bestT * len : 0,
                Reach = reach,
                Along = along,
                CentreX = items[bk].X1 + bestT * items[bk].DX,
                CentreZ = items[bk].Z1 + bestT * items[bk].DZ,
                StartTop = pl.P0,
                EndTop = pl.P0 - drops.Length,
            };
            if (PoolRules.EasedBeds && pl.Shape != null)
            {
                // the same places as pool layout 2, each eased over EaseLength: towards a chute's first step and away from its last,
                // towards a curtain's lip, and out of its plunge pool
                static double Ease(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
                // the step before this position and the one after it, also across reach ends (a chute or a curtain just
                // upstream or downstream, short reaches between), as far as anything here depends on them
                Pools pp = pl, np_ = pl;
                int pi = nDrops - 1, ni = nDrops;
                double after = pi >= 0 ? along - drops[pi] : double.PositiveInfinity;
                double before = ni < drops.Length ? drops[ni] - along : double.PositiveInfinity;
                double reachOut = LipLength + EaseLength + MaxPlunge * 2;
                double back = along;
                for (int guard = 0, up = pl.MainIn; pi < 0 && guard < 8 && up >= 0 && pools[up] != null && back <= reachOut; guard++)
                {
                    var q = pools[up];
                    if (q.Shape != null && q.Drops.Length > 0) { pp = q; pi = q.Drops.Length - 1; after = back + q.Len - q.Drops[pi]; break; }
                    back += q.Len;
                    up = q.MainIn;
                }
                double ahead = pl.Len - along;
                for (int guard = 0, dn = pl.Next; ni >= drops.Length && guard < 8 && dn >= 0 && pools[dn] != null && ahead <= reachOut; guard++)
                {
                    var q = pools[dn];
                    if (q.Shape != null && q.Drops.Length > 0) { np_ = q; ni = 0; before = ahead + q.Drops[0]; break; }
                    ahead += q.Len;
                    dn = q.Next;
                }
                if (pi >= 0 && pi < pp.Shape.Length)
                {
                    if (pp.Shape[pi] == ShapeChute)
                    {
                        hit.Chute = after <= ChuteGap + 1;
                        hit.Shoal = Math.Max(hit.Shoal, Ease(1 - (after - ChuteGap - 1) / EaseLength));
                        hit.ChuteShoal = Math.Max(hit.ChuteShoal, Ease(1 - (after - ChuteGap - 1) / EaseLength));
                    }
                    if (pp.Shape[pi] == ShapeCurtain)
                        hit.PlungeDepth = Math.Min(pp.Height[pi] / 2, MaxPlunge) * Ease(1 - (after - pp.Height[pi]) / EaseLength);
                }
                if (ni < np_.Shape.Length && before < double.PositiveInfinity)
                {
                    if (np_.Shape[ni] == ShapeChute)
                    {
                        if (before <= 1) hit.Chute = true;
                        hit.Shoal = Math.Max(hit.Shoal, Ease(1 - (before - 1) / EaseLength));
                        hit.ChuteShoal = Math.Max(hit.ChuteShoal, Ease(1 - (before - 1) / EaseLength));
                    }
                    if (np_.Shape[ni] == ShapeCurtain) hit.Shoal = Math.Max(hit.Shoal, Ease(1 - (before - LipLength) / EaseLength));
                }
            }
            else if (PoolRules.Chutes && pl.Shape != null)
            {
                // the steps around this position: a chute's from just above its first step to past its last ramp's start, a
                // curtain's lip above it and its plunge pool below
                if (nDrops > 0)
                {
                    int i = nDrops - 1;
                    double after = along - drops[i];
                    if (pl.Shape[i] == ShapeChute && after <= ChuteSpacing + 1) hit.Chute = true;
                    if (pl.Shape[i] == ShapeCurtain && after <= pl.Height[i]) hit.Plunge = Math.Min(pl.Height[i] / 2, MaxPlunge);
                }
                if (nDrops < drops.Length)
                {
                    double before = drops[nDrops] - along;
                    if (pl.Shape[nDrops] == ShapeChute && before <= 1) hit.Chute = true;
                    if (pl.Shape[nDrops] == ShapeCurtain && before <= LipLength) hit.Lip = true;
                }
                hit.Rocks = hit.Chute;
            }
            return true;
        }

        /// <summary>
        /// Channel rules 1 (0063): the direction of piece i (b) at t along it, blended with the next piece's near its end and
        /// with the one before near its start (along the reach, or across a reach's end into the reach downstream or the main
        /// inflow), each over half the shorter piece: half and half at the vertex, so the flow turns with the course instead
        /// of jumping at each vertex (43-48 % of the water at Rome, the Gauja and the Tigris pointed more than 45° off).
        /// </summary>
        void BlendDirection(int i, in Seg b, double t, ref double dirX, ref double dirZ)
        {
            double len = b.Len, s = t * len;
            bool atStart = s < 0.5 * len;
            int j = atStart ? PieceBefore(i, in b) : PieceAfter(i, in b);
            if (j < 0) return;
            ref readonly var o = ref segs[j];
            double ol = o.Len;
            if (ol <= 0) return;
            double span = 0.5 * Math.Min(len, ol), e = atStart ? s : len - s;
            if (e >= span) return;
            double w = 0.5 * (1 - e / span);
            double nx = (1 - w) * dirX + w * (o.X2 - o.X1) / ol, nz = (1 - w) * dirZ + w * (o.Z2 - o.Z1) / ol;
            double n = Math.Sqrt(nx * nx + nz * nz);
            if (n < 1e-6) return;
            dirX = nx / n;
            dirZ = nz / n;
        }

        int PieceBefore(int i, in Seg b)
        {
            if (i > 0 && segs[i - 1].Reach == b.Reach && !segs[i - 1].Mouth && Joined(in segs[i - 1], in b)) return i - 1;
            if (segFirst[b.Reach] != i) return -1;
            int up = pools[b.Reach].MainIn;
            int j = up >= 0 ? segLast[up] : -1;
            return j >= 0 && Joined(in segs[j], in b) ? j : -1;
        }

        int PieceAfter(int i, in Seg b)
        {
            if (i + 1 < segs.Length && segs[i + 1].Reach == b.Reach && !segs[i + 1].Mouth && Joined(in b, in segs[i + 1])) return i + 1;
            if (segLast[b.Reach] != i) return -1;
            int dn = pools[b.Reach].Next;
            int j = dn >= 0 ? segFirst[dn] : -1;
            return j >= 0 && Joined(in b, in segs[j]) ? j : -1;
        }

        /// <summary>Piece b starts where piece a ends (within a block).</summary>
        static bool Joined(in Seg a, in Seg b) => Math.Abs(a.X2 - b.X1) + Math.Abs(a.Z2 - b.Z1) < 1;

        /// <summary>
        /// Distance from (x, z) to the nearest river water (its edge), looking only within the given distance; infinity
        /// if there is none that close.
        /// </summary>
        public double DistanceToWater(double x, double z, double within)
        {
            if (segs == null || segs.Length == 0) return double.PositiveInfinity;
            x = geo.WrapX(x);
            double best = double.PositiveInfinity;
            int cx0 = (int)Math.Floor((x - within) / Cell), cx1 = (int)Math.Floor((x + within) / Cell);
            int cz0 = (int)Math.Floor((z - within) / Cell), cz1 = (int)Math.Floor((z + within) / Cell);
            // Intersect original fine-cell rectangles: a wider bucket must not add
            // candidates that the legacy distance window would never have visited.
            if (cx0 > cx1 || cz0 > cz1) return best;
            int lastQ = 0;
            bool haveQ = false;
            double halfWidth = 0;
            for (int cz = Bucket(cz0); cz <= Bucket(cz1); cz++)
            {
                for (int cx = Bucket(cx0); cx <= Bucket(cx1); cx++)
                {
                    if (!grid.TryGetValue(Key(cx, cz), out var ids)) continue;
                    foreach (int i in ids)
                    {
                        ref readonly var s = ref segs[i];
                        if (s.CellX1 < cx0 || s.CellX0 > cx1 || s.CellZ1 < cz0 || s.CellZ0 > cz1) continue;
                        int qBits = BitConverter.SingleToInt32Bits(s.Q);
                        if (!haveQ || qBits != lastQ)
                        {
                            halfWidth = HalfWidth(s.Q);
                            lastQ = qBits;
                            haveQ = true;
                        }
                        double d = Project(in s, x, z, out double t);
                        d -= s.M1 > 0 || s.M2 > 0 ? MaskedHalfWidth(halfWidth, s.M1, s.M2, t) : halfWidth;
                        if (d < best) best = d;
                    }
                }
            }
            return best <= within ? Math.Max(0, best) : double.PositiveInfinity;
        }

        static double Project(in Seg s, double x, double z, out double t)
        {
            double dx = s.X2 - s.X1, dz = s.Z2 - s.Z1;
            double len2 = dx * dx + dz * dz;
            t = len2 > 0 ? Math.Clamp(((x - s.X1) * dx + (z - s.Z1) * dz) / len2, 0, 1) : 0;
            double px = s.X1 + t * dx, pz = s.Z1 + t * dz;
            return Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }

        /// <summary><see cref="Project(in Seg, double, double, out double)"/> on a prepared candidate: the same operations in the same order.</summary>
        static double Project(in Candidate s, double x, double z, out double t)
        {
            double dx = s.DX, dz = s.DZ;
            double len2 = s.Len2;
            t = len2 > 0 ? Math.Clamp(((x - s.X1) * dx + (z - s.Z1) * dz) / len2, 0, 1) : 0;
            double px = s.X1 + t * dx, pz = s.Z1 + t * dz;
            return Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
        }
    }
}
