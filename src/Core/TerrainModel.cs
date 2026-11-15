using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>
    /// The terrain of one block column, from the Earth data: real elevation (bicubic from the 2' grid) plus detail
    /// noise scaled by the local relief, mapped to block heights by the vertical scale. Decides sea, lakes, dry
    /// land and ice sheets. Its rule fields and layers are set while the world's model is built (EarthParams.BuildModel,
    /// EarthElevationSystem.AttachServer, EarthWorldGen for the ecology data), the companions' pads and expansions before
    /// generation (sealed then); after that a column is a pure function of its position, and the caches it keeps (peak
    /// bases, GLO-90 summits, per-thread climate) hold only such values, so it is safe to use from any worldgen thread.
    /// The parts: TerrainModel.*.cs; RiverSettling, StoryTerrain and TerrainColumns hold its pure statics.
    /// </summary>
    public sealed partial class TerrainModel
    {
        public readonly EarthData Data;
        public readonly EarthGeometry Geometry;
        public readonly VerticalScale Vertical;
        /// <summary>This model's key in per-thread caches (a number, so an unloaded world is not kept alive by a thread).</summary>
        internal readonly long CacheId = System.Threading.Interlocked.Increment(ref lastCacheId);
        static long lastCacheId;
        public int GutterSeabedY;
        /// <summary>The world's climate rules (EarthParams.ClimateRules); from <see cref="RealLapseRules"/> the climate map stores
        /// the sea-level temperature and vanilla's altitude adjustment cools by the real lapse (decision 0009).</summary>
        public int ClimateRules { get => rules.Climate; set => rules = rules with { Climate = value }; }
        /// <summary>The world's geothermal rules (EarthParams.GeothermalRules); from 1 the climate map's geologic activity is
        /// Earth's (Core.Geothermal.Activity) instead of the vanilla noise passed in.</summary>
        public int GeothermalRules { get => rules.Geothermal; set => rules = rules with { Geothermal = value }; }
        public const int RealLapseRules = 2;
        /// <summary>From these rules (decision 0031's test profiles only) the climate map also stores the real rainfall and vanilla's
        /// altitude rain adjustment is off (ClimateLapse); the vanilla-lapse pixel for other readers keeps vanilla's rain.</summary>
        public const int RealRainRules = 3;
        /// <summary>This model is a client's (EarthMapSystem.OnParams): vanilla's client reads the climate cut to -50..40 °C
        /// (Climate.GetScaledAdjustedTemperatureFloatClient), the server to -20..40, and ClimateCorrection undoes the cut that
        /// side made.</summary>
        public bool ClientClimate;
        /// <summary>The lowest temperature vanilla's live reading gives on this side.</summary>
        public double VanillaFloor => ClientClimate ? -50 : -20;
        /// <summary>Least water (blocks) over the sea floor away from the shore, whatever the real depth (EarthParams.MinSeaDepth).</summary>
        public int MinSeaDepth = OldSeaDepth;
        /// <summary>
        /// Eased lake shores (EarthParams.LakeShores, with the grid's shores.bin overlay): dry land is held up at a lake's
        /// level only within the lake's own cells (where its water can be), not across the whole 3-cell ring the grid gives
        /// the level to: the level it is held at, and the noise's dips, fade with the lake's nearness (EarthData.TerrainSample
        /// .LakeNear). Before, ground lower than a lake stood flat at its level out to the ring's edge and ended in a
        /// cliff (south of Lake Bolsena and Lake Bracciano). The sea's shores are unchanged.
        /// </summary>
        public bool LakeShores;
        /// <summary>Eased lake shores: how far (m) below a lake's level the land is held where its nearness is 0.5 (it sinks as
        /// (1 - near) / near: 11 m at 0.9, a few hundred where the ring ends).</summary>
        public const double HoldFall = 100;
        /// <summary>6 in new worlds from 0.14.1: a carrack draws about 4.4 blocks (Magelan), so over the shallow banks (the
        /// Bahamas, Florida Bay, Torres Strait) she has room to spare, even with some water in her; 5 before (a fraction of a block).</summary>
        public const int ShipSeaDepth = 6, OldSeaDepth = 5;
        /// <summary>The plain gutter's fade from the edge's ground to GutterSeabedY (blocks, at most half the gutter).</summary>
        public const double GutterBlend = 512;
        public double DetailScale = 1.0;
        /// <summary>
        /// 0053: the detail noise's rules (EarthParams.DetailRules). 1: wavelengths in metres, all below the land
        /// data's own cell (half a cell and shorter, down to 60 m or 3 blocks), amplitude from the data's relief (the built-in
        /// grid's: about a third of the first noise's; GLO-90's included grid: its own spread inside each cell), sized
        /// smoothly between cells, octaves turned against each other (no 45-degree grain). 0: the first noise, fixed in blocks.
        /// </summary>
        public const int DetailRulesVersion = 1;
        public int DetailRules { get => rules.Detail; set => rules = rules with { Detail = value }; }
        /// <summary>Decision 0031 test profile: tree line and forest potential from the planting column's own elevation.</summary>
        public bool LocalAlpineEcology;
        /// <summary>The Earth tab's vanilla biomes (EarthParams.VanillaBiomes, decision 0075): vanilla's forest and shrub maps
        /// as its noise made them, and no alpine turf, fell-field or scree.</summary>
        public bool VanillaBiomes;
        /// <summary>Decision 0031: ridges and peaks raised to ETOPO 2022's 30" elevation (EarthData.SummitElevation).</summary>
        public bool Summits;
        public int SummitRules { get => rules.Summit; set => rules = rules with { Summit = value }; }
        /// <summary>Minimum surveyed summit elevation in metres; -1 off, 0 all peaks (older worlds).</summary>
        public int SummitMinElevation;
        public PeakCatalog Peaks;
        public int AlpineSurfaceRules;
        public int AridRiverRules { get => rules.Arid; set => rules = rules with { Arid = value }; }
        public NavigableStraits Straits;
        public WadiFans DryFans;
        /// <summary>Per reach, the part of its flow from moist catchments (AridHydrology.WetSourceFlow; arid river rules), or
        /// null. Set after the rivers: it also keeps the courses of the reaches the arid rules could dry (KeepDryReachCourses).</summary>
        public double[] WetSourceFlow
        {
            get => wetSourceFlow;
            set { wetSourceFlow = value; KeepDryReachCourses(); }
        }
        double[] wetSourceFlow;
        /// <summary>Land up to this height (m) above a sea or lake next to it is flush with the water surface: beaches.</summary>
        public const double BeachHeight = 3;
        public List<FlatZone> FlatZones = new List<FlatZone>();
        /// <summary>Large rivers carved into the terrain, or null.</summary>
        public RiverNetwork Rivers;
        /// <summary>
        /// Share of the land's vertical relief the river surface follows (1 = the real level everywhere). Below 1, rivers
        /// sit lower in their valleys and their surface steps down less often (better for boats).
        /// </summary>
        public double RiverRelief = 1.0;
        /// <summary>River exits deepened out to open sea (worlds from 0.13.3), or null.</summary>
        public SeaChannels Channels;
        /// <summary>The small islands from their outlines, as wide as this world's scale allows (EarthParams.Islands), or null.</summary>
        public IslandLayer Islands;
        /// <summary>The big land's coasts from their outlines where the grid's cells are many blocks wide (EarthParams.Coasts), or null.</summary>
        public CoastLayer Coasts;
        /// <summary>
        /// 0053 item 17: the lakes of GLO-90's water mask from their outlines (EarthParams.LakeOutlines), or null.
        /// Where this world reads GLO-90 (the square is GLO-90's, not kept on built-in heights, on a thread that reads it)
        /// and the file covers the place, they stand in for the grid's fresh lakes: water at the lake's GLO-90 level inside the
        /// outline, a bed falling from the shore, and dry land at GLO-90's height held up beside it. Earth's own salt lakes and
        /// the terminal lakes it fills on purpose (decision 0005) stay the grid's, as the file leaves them out.
        /// </summary>
        public LakeOutlineLayer LakeOutlines;
        /// <summary>
        /// 0053 item 18: the rivers of GLO-90's water mask from their outlines (EarthParams.RiverOutlines,
        /// RiverOutlineData), or null. Where this world reads GLO-90 (as LakeOutlines), dry land inside them is river water at
        /// the level of the nearest river course, and a course whose centre line lies in them draws no channel, banks or
        /// valley of its own there (ApplyOutlineRiver): the river has the mask's shores, not a channel of one width along
        /// straight pieces 500 m long. Just outside them a course's channel narrows to meet them (OutlineTaper). The file
        /// leaves out reservoirs (no modern dams in 1318), where the courses draw the river as before.
        /// </summary>
        public LakeOutlineLayer RiverOutlines;
        /// <summary>
        /// Decision 0064: the works after 1318 undone (EarthParams.Works1318, Works1318Data), with the coast's reach, or
        /// null. Where the coast's outlines decide (Coasts), SEA areas are sea over today's land, LAND areas land over today's
        /// water, and LAKE areas lakes at their level; today's lakes, river outlines and grid lakes are not drawn inside SEA
        /// and LAND areas.
        /// </summary>
        public LakeOutlineLayer Works;
        /// <summary>0064's works rules (EarthParams.Works1318): 3 measures the sea's side near a SEA area to the land of 1318
        /// (CoastDistanceAt, EA-0042); below 3 to today's outline as well.</summary>
        public int WorksRules { get => rules.Works; set => rules = rules with { Works = value }; }
        /// <summary>An outline river's bed deepens this much (blocks per block) from a block below the water at its shore, to
        /// the depth of its course's channel.</summary>
        public const double OutlineRiverSlope = 0.5;
        /// <summary>The depth (blocks) of an outline river's water with no course near it (a side arm or an oxbow the courses
        /// don't have).</summary>
        public const int OutlineRiverLoneDepth = 3;
        /// <summary>A course's channel just outside the water mask's rivers is at most this much (blocks) wider on each side
        /// than its centre line is far from them (ApplyRiver): enough to overlap their water where the course goes into them.</summary>
        public const double OutlineTaper = 2;
        /// <summary>How far (blocks) an outline river's water looks for its course beyond the course's own reach: about 2 km.</summary>
        double OutlineCourseSearch => Math.Clamp(2000 / MetresPerBlock, 32, 512);
        /// <summary>Column.RiverOutline.</summary>
        public const byte RiverInOutline = 1, RiverBesideOutline = 2;
        /// <summary>
        /// 0063 rule 4, "dredging" (EarthParams.ChannelRules): 1 draws a course's channel everywhere along it, never cut or
        /// narrowed by the water mask's rivers (ApplyChannel), at the width and depth its hit gives (the flow's, or the river's
        /// measured width where narrower down to half the flow's, deepened for it: RiverNetwork.ChannelRules); outline water at the course's level and the channel make one
        /// river, the channel deepening the bed under it; a mask lake in the channel or its banks within MaskPondRise blocks
        /// of the river's level gives way to the river; the mask's river water is never dried by the arid rules; no outline
        /// water (rivers or lakes) along the Suez and Panama canals (SeaChannels.InCanalZone); the flow direction blended
        /// along the course (RiverNetwork). 0: the outlines cut and narrow the channel (older saves).
        /// </summary>
        public int ChannelRules { get => rules.Channel; set => rules = rules with { Channel = value }; }
        /// <summary>Decision 0069: the flow solved on the water mask's river water (rivers-flow-glo90-1.bin), and its rules;
        /// null and 0 without it.</summary>
        public RiverFlowData Flow;
        public int FlowRules { get => rules.Flow; set => rules = rules with { Flow = value }; }
        /// <summary>Decision 0069 rule 7: the shore zones for plants (ShoreZones.RulesVersion); 0 without.</summary>
        public int ShoreZoneRules;
        /// <summary>EA-0054 step 2: site rules (EarthParams.SiteRules); 1 = trees and shrubs by each column's site.</summary>
        public int SiteRules;
        /// <summary>Decision 0070: the shore strip (ShoreMaterial.RulesVersion; 0 without) and OSM's beaches (null without).</summary>
        public int ShoreRules { get => rules.Shore; set => rules = rules with { Shore = value }; }
        public ShoreMaterialData ShoreData;
        /// <summary>
        /// Generation fixes from the code review of 11 November 2026 (docs/research/code-review-2026-11-11.md), new worlds
        /// only (EarthParams.GenFixRules); 0 in older saves. 1: dry land below 0 m continuous with the land at 0 m (no two-block
        /// ledge on the 0 m contour, <see cref="DryLandTop"/>, CR-T2); springs and seeps planned from the columns generation makes
        /// (whole blocks, not half a block off, CR-R1); crops and fruit trees in the river belt judged by its rain (CR-E1); dry
        /// river beds' gravel scattered by a position hash, not in diagonal stripes (CR-G5). 2 (2.0.0-rc.10, EA-0040): on the
        /// grid's land (not GLO-90's), the noise's dips near a water level fade in from 20 to 40 m above it instead of
        /// switching at 30 m, which left a crease of up to 6 blocks along that contour (Lake Geneva's Rhône plain). 3 (EA-0072):
        /// a grid lake's water ends along an irregular line inside its cells on flat ground, not along the straight line where
        /// its share reaches 0 (<see cref="GridLakeEdgeDrop"/>; the built-in heights' lakes, and the grid's kept salt and
        /// terminal lakes on GLO-90). 4 (code review of 14 November 2026): a volcano's conduit without a crater (a wet vent)
        /// has nothing above the column's own top and lava only 2 blocks or more under it (<see cref="GenFixConduitUnderTop"/>,
        /// ER-19); the rock under a ground column is read through the whole column, not its top 512 blocks
        /// (<see cref="GenFixWholeGroundColumn"/>, ER-20). 5 (Fable pass, EA-0111, EA-0112): the river flow read across its 1°
        /// tiles' edges (<see cref="GenFixFlowAcrossTiles"/>), and a lake's area as the whole lake's, not its piece in one 1°
        /// tile (<see cref="GenFixWholeLakeArea"/>): no straight seams along whole degrees in wide rivers and lake shores. Also
        /// 5 (code review of 14 November 2026): SeamNoise reads a place offset beyond the ring inside it
        /// (<see cref="GenFixSeamNoiseInRing"/>, T-L12), and the grid's kept lakes are told apart by each level near a place,
        /// not only the highest (<see cref="GenFixKeptLakeLevels"/>, T-L14).
        /// </summary>
        public int GenFixRules { get => rules.GenFix; set => rules = rules with { GenFix = value }; }

        TerrainRules rules;
        /// <summary>(ER-10) This model's terrain rules as named facts (<see cref="TerrainRules"/>); the int properties above and
        /// in LandHeights read and write it. A MemberwiseClone (World Map's snapshot) copies it with the rest.</summary>
        public ref readonly TerrainRules Rules => ref rules;
        public const int GenFixRulesVersion = 5;
        /// <summary>GenFixRules 4 (ER-19): VolcanoInterior caps a crater-less conduit at the column's top.</summary>
        public const int GenFixConduitUnderTop = 4;
        /// <summary>GenFixRules 4 (ER-20): EarthGeology.RegionalRock knows a ground column's blocks to its bottom.</summary>
        public const int GenFixWholeGroundColumn = 4;
        /// <summary>GenFixRules 5 (EA-0111, FP-21): RiverFlowData.At reads the pixels beyond a tile's edge from the next tile.</summary>
        public const int GenFixFlowAcrossTiles = 5;
        /// <summary>GenFixRules 5 (EA-0112, FP-22): the lake rules read LakeOutlineLayer.WholeArea.</summary>
        public const int GenFixWholeLakeArea = 5;
        /// <summary>GenFixRules 5 (T-L12): SeamNoise wraps a place beyond the ring (an offset noise's) into it first, so its
        /// blend has no jump where the column wraps.</summary>
        public const int GenFixSeamNoiseInRing = 5;
        /// <summary>GenFixRules 5 (T-L14): StripGridLakes keeps a grid lake or ring at any kept level near the place
        /// (TerrainSampler.KeptLakeLevelsNear), not only at the highest.</summary>
        public const int GenFixKeptLakeLevels = 5;
        /// <summary>A lake's area as the rules read it: the whole lake's from GenFixRules 5, else its piece in the tile.</summary>
        internal double LakeArea(in LakeOutlineLayer.Hit lk) => Rules.WholeLakeArea && lk.Owner != null ? lk.Owner.WholeArea(lk) : lk.Area;
        /// <summary>Rule 4: river water slower than this (m/s) is still water; rule 3: water faster than CarryMin carries the
        /// river's flow (its full depth across the width).</summary>
        public const double StillBelow = 0.05, CarryMin = 0.02;
        public const int ChannelRulesVersion = 5;
        /// <summary>
        /// Channel rules 3 (EA-0038/0039, decision 0071): where a river at the sea's level meets the sea, one bed (ApplyMouth).
        /// Before, the sea up an estuary that the coast outline has as sea and the water mask as river kept the coast profile's
        /// 1-5 blocks beside the river's 7-9 (patches along GSHHG's closing line), and the course's channel ran on through the
        /// sea as a groove of its own width and depth with walls for sides.
        /// </summary>
        public const int MouthChannelRules = 3;
        /// <summary>
        /// Channel rules 4 (EA-0048/0049/0051/0052, decision 0073): mask-widened courses reach as far as their water
        /// (RiverNetwork.MaskReachChannelRules); a narrow piece of the mask's river water that the flow file has carrying flow
        /// stays (FlowCarriesAt: a side channel round an island, the Sarkandaugava at Kundziņsala); the depth beyond the
        /// courses blended over every course near (RiverNetwork.DepthNear); still or flowing water reach by reach
        /// (FlowingShare: a lake's outflow through a wide channel, the Mīlgrāvis from Ķīšezers, is still); small pools in the
        /// river's water joined to it (JoinsRiver); no groove of a course's channel in wide water; channel sides no steeper
        /// than ChannelSide4 (TerrainModel.ChannelDepthExact).
        /// </summary>
        public const int FlowChannelRules = 4;

        /// <summary>Channel rules 4: whether the flow file has the river's flow passing here (at least CarryMin).</summary>
        public bool FlowCarriesAt(double lat, double lon) =>
            Flow != null && Flow.At(lat, lon, Rules.FlowAcrossTiles, out double e, out double n) && e * e + n * n >= CarryMin * CarryMin;
        /// <summary>Channel rules 1: a mask lake this many blocks or less above or below a river beside it gives way to it.</summary>
        public const int MaskPondRise = 3;
        /// <summary>An outline lake's bed falls this much (m per m) from its shore, to OutlineLakeDeep (m), where the grid
        /// has no deeper bed for it; the grid's bed (ETOPO's lakes, at half its depth as the grid's own lakes have it) is
        /// reached no steeper than OutlineGridSlope near the shore.</summary>
        public const double OutlineLakeSlope = 0.05, OutlineLakeDeep = 40, OutlineGridSlope = 0.2;
        /// <summary>Low land within a block of an outline lake at least this large (blocks²) is a beach flush with it; a
        /// smaller pond may show no water at all, and a ring of sand round it looked odd (Patagonia probe, 7 November).</summary>
        public const double OutlineBeachArea = 2;
        /// <summary>Elevation (m) of the land right at an outline coast, and of the sea floor right off it (on flat land; on
        /// rugged land up to 8 m higher and 4 m deeper).</summary>
        public const double CoastLand = 1.0, CoastSea = -1.5;

        readonly Simplex2 n1, n2, n3;

        public TerrainModel(EarthData data, EarthGeometry geometry, VerticalScale vertical, long seed)
        {
            Data = data;
            Geometry = geometry;
            Vertical = vertical;
            GutterSeabedY = vertical.SeaLevel - 40;
            n1 = new Simplex2(seed + 101);
            n2 = new Simplex2(seed + 202);
            n3 = new Simplex2(seed + 303);
        }

        public int SeaLevel => Vertical.SeaLevel;

        public struct Column
        {
            /// <summary>Top solid block (rock or glacier ice).</summary>
            public int Top;
            /// <summary>Top water block, -1 for none.</summary>
            public int WaterTop;
            /// <summary>0 none, 1 sea, 2 lake, 3 salt lake.</summary>
            public byte Water;
            /// <summary>Lowest glacier-ice block (up to Top), int.MaxValue for none.</summary>
            public int IceBottom;
            /// <summary>Real elevation (m) after the detail noise; the ice surface on ice sheets.</summary>
            public double Elev;
            public bool Gutter;
            /// <summary>Land at the water's edge, flush with the water surface (sand or gravel goes here).</summary>
            public bool Beach;
            /// <summary>Decision 0070: a beach of the sea's or a big lake's shore strip (at any height), and what OSM says it is
            /// made of (ShoreMaterial: Sand .. Mud, Beach untagged, Rule none); 0 elsewhere and in older worlds.</summary>
            public byte Shore;
            /// <summary>An island's land from its outline where the grid has sea (IslandLayer): from island rules v3 rivers and
            /// river exit channels go around it instead of cutting through.</summary>
            public bool IslandOverSea;
            /// <summary>River water (fresh), with the flow direction.</summary>
            public bool River;
            public float FlowX, FlowZ;
            public bool DryRiver;
            /// <summary>0, or 1..6: flowing water of that height on top of the pool (the ramp just below a step down).</summary>
            public byte Ramp;
            /// <summary>The ramp is part of rapids (rapid water).</summary>
            public bool Rapid;
            /// <summary>0, or 2+: a neighbour's water stands this much higher: a curtain of falling water up to it.</summary>
            public byte Fall;
            /// <summary>Pool layout 5: this column's water is the upper edge of a drop: 1 a 1-block step (its top source labelled
            /// with the flow), 2 a chute's step (a rapid top source), 3 a curtain's lip (rapid sources down EdgeDepth levels).
            /// Spill: above this column's ramp, falling rapid water of this height where a chute's ramp ends beside it.</summary>
            public byte Edge, EdgeDepth, Spill;
            /// <summary>Pool layout 8: river water of a rapids reach (decision 0027): its top source is rapid water.</summary>
            public bool RapidsReach;
            /// <summary>Within a river's water, bank or valley.</summary>
            public bool NearRiver;
            /// <summary>Water top of a lake within one data cell, -1 if none (rivers join it at the same level).</summary>
            public int NearLakeTop;
            /// <summary>Eased lake shores: the water top of the lake whose ring this is, and the top the land is held at here
            /// (just above the lake next to it, sinking away from it); 0 if none. Pool layout 4 keeps a lower river's banks and
            /// valley from cutting below it.</summary>
            public int LakeRingTop, LakeHoldTop;
            /// <summary>0053 item 17: dry land within LakeOutlineLayer.HoldReach of an outline lake is held at least this high
            /// (the lake's water top), and river banks and valleys keep it; 0 for none.</summary>
            public int LakeRim;
            /// <summary>Decision 0069 rule 4: river water whose solved flow is slower than StillBelow (a dead end, a
            /// backwater, the slack of a wide reach): still water, without the current's labels.</summary>
            public bool Still;
            /// <summary>0053 item 18: dry land in (RiverInOutline) or within LakeOutlineLayer.HoldReach of (RiverBesideOutline)
            /// the water mask's rivers, 0 elsewhere; the distance (blocks) to their shore, and the piece's level (m).
            /// ApplyRiver turns it into river water, or holds it at the water's top.</summary>
            public byte RiverOutline;
            public float RiverOutlineDist, RiverOutlineLevel;
            /// <summary>0053 item 17: water of an outline lake (OutlineLakeColumn); channel rules 1 let it give way to a river.</summary>
            public bool OutlineLake;
            public double Lat, Lon;
            /// <summary>0051: dry land whose height (Elev) is GLO-90's; GridElev is then its height on the built-in heights
            /// (m), which river exit channels judge low land by from channel rules 3 (SeaChannels.LowLand).</summary>
            public bool Glo90Land;
            public double GridElev;
        }

        public const byte WaterNone = 0, WaterSea = 1, WaterLake = 2, WaterSaltLake = 3;
        /// <summary>Column.Edge (pool layout 5).</summary>
        public const byte EdgeStep = 1, EdgeChute = 2, EdgeLip = 3;

        /// <summary>Top water block of a sea (level 0) or lake at the given level (m).</summary>
        public int WaterTopY(double level)
        {
            if (level >= 0) return (int)Math.Floor(Vertical.Y(level)) - 1;
            return (int)Math.Floor(Vertical.Y(level));
        }

        /// <summary>
        /// Top block of dry land at elevation h (m). Vertical.Y's curve below 0 m is the sea floor's, which starts a block below
        /// the sea's surface: dry land below 0 m outside the sea and lakes (the Caspian lowland, the Jordan valley, Death
        /// Valley, polders beyond the coast's reach) had a two-block ledge along the 0 m contour, and pits where the detail noise
        /// crosses it. With <see cref="GenFixRules"/> 1 (CR-T2) that land is a block higher, so it continues from the land at
        /// 0 m (and stands a block above a lake at its own level, as land at a lake above 0 m does).
        /// </summary>
        public int DryLandTop(double h) => (int)Math.Floor(Rules.DryLandLedge && h < 0 ? Vertical.Y(h) + 1 : Vertical.Y(h));

        public Column GetColumn(double x, double z)
        {
            Column c = GetColumnWithoutReservations(x, z);
            if (c.Gutter || c.Water != WaterNone || c.River || c.NearRiver || c.Beach || c.IceBottom <= c.Top) return c;
            foreach (var site in reservations.At(x, z))
            {
                double w = site.Weight(x, z);
                if (w <= 0) continue;
                int delta = Math.Clamp(site.TargetTop - c.Top, -site.MaxCutFill, site.MaxCutFill);
                c.Top += (int)Math.Round(delta * w);
            }
            return c;
        }

        /// <summary>
        /// What later generation steps carve into a column after <see cref="GetColumn"/> (EA-0071): the summit craters and
        /// their lava lakes, which EarthVolcanoes sets once it has indexed the volcano bodies on the server (null elsewhere,
        /// and where the world has no volcano bodies). An object of its own, made with the model, so that copies of the model
        /// (World Map samples a MemberwiseClone taken when the game starts) see a layer set after they were taken.
        /// </summary>
        public sealed class SurfaceLayers
        {
            /// <summary>The surface y a crater or lava lake leaves at a block column, or int.MaxValue where none does.
            /// Called from any thread.</summary>
            public volatile Func<int, int, int> Craters;
        }

        /// <summary>See <see cref="SurfaceLayers"/>.</summary>
        public readonly SurfaceLayers Carved = new SurfaceLayers();

        /// <summary>
        /// The column as generation leaves its surface, for maps (World Map reads it by this name, WM-0023): <see
        /// cref="GetColumn"/> with the top lowered to a crater's floor or walls, or to a lava lake's top (lava is not water:
        /// <see cref="Column.Water"/> stays as it is). Where no later step changes the surface it is GetColumn's. Never for
        /// generation itself, which carves the craters from GetColumn.
        /// </summary>
        public Column GetSurfaceColumn(double x, double z)
        {
            var c = GetColumn(x, z);
            var craters = Carved.Craters;
            if (craters == null || c.Water != WaterNone) return c;
            int y = craters((int)Math.Floor(x), (int)Math.Floor(z));
            if (y < c.Top) c.Top = y;
            return c;
        }

        /// <summary>The original Earth/story surface, without companion site pads, for deterministic planning.</summary>
        public Column GetColumnWithoutReservations(double x, double z)
        {
            return ColumnForExpansion(ExpansionAt(x, z), x, z);
        }

        /// <summary>Prospective local geography, without publishing it or applying companion height pads.</summary>
        public Column GetColumnForExpansion(TerrainExpansion expansion, double x, double z)
        {
            if (expansion == null) throw new ArgumentNullException(nameof(expansion));
            return ColumnForExpansion(expansion.Contains(x, z) ? expansion : ExpansionAt(x, z), x, z);
        }

        Column ColumnForExpansion(TerrainExpansion expansion, double x, double z)
        {
            double sourceX = x, sourceZ = z;
            expansion?.Project(x, z, out sourceX, out sourceZ);
            // the real seam: both laps of the ring (one jump apart) are the same column
            if (RealSeam) sourceX = Geometry.WrapX(sourceX);
            var g = Geometry.ToGeo(sourceX, sourceZ);
            var work = new ColumnWork();
            Column c = g.InGutter ? GutterColumn(sourceX, sourceZ, g) : NaturalColumn(sourceX, sourceZ, g.Lat, g.Lon, ref work);
            if (Islands != null && !c.Gutter) ApplyIslands(sourceX, sourceZ, ref c, ref work);
            if (Rivers != null && !c.Gutter) ApplyRiver(sourceX, sourceZ, ref c);
            if (Rivers != null && !c.Gutter && Rules.MouthBed && c.Water == WaterSea) ApplyMouth(sourceX, sourceZ, ref c, ref work);
            DryFans?.Apply(sourceX,sourceZ,ref c);
            Channels?.Apply(this, sourceX, sourceZ, ref c);
            Straits?.Apply(SeaLevel, sourceX, sourceZ, ref c);
            if (Rules.ShoreStrip && !c.Gutter) ApplyShoreStrip(sourceX, sourceZ, ref c, ref work);
            if (expansion != null)
            {
                expansion.TransformFlow(x, z, c.FlowX, c.FlowZ, out c.FlowX, out c.FlowZ);
                // Geography reported for a physical column remains the original world coordinate.
                var physical = Geometry.ToGeo(x, z); c.Lat = physical.Lat; c.Lon = physical.Lon;
            }
            if (FlatZones.Count > 0) StoryTerrain.Apply(FlatZones, Geometry, SeaLevel, x, z, ref c);
            return c;
        }
    }
}
