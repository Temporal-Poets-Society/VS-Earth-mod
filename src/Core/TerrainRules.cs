namespace EarthMap.Core
{
    /// <summary>
    /// (ER-10, code review 14 November 2026) What a world's terrain rules numbers turn on, as named facts: the one place that
    /// says which version of which family changed what in <see cref="TerrainModel"/>. The numbers are frozen per world
    /// (EarthParams, decision 0002) and set on the model by EarthParams.BuildModel and the server's attach steps; the model's
    /// int properties (ChannelRules, GenFixRules, ...) read and write them here. Every rules test in TerrainModel's own files
    /// goes through a fact; tests/TerrainRulesTests pins each fact's first version, written from the comparisons before they
    /// moved, and the 26-place column hashes of EA-0085 stayed the same across the move. Older worlds keep their rules bit for
    /// bit. Not here yet: RiverNetwork's pool layouts (PoolRules, RiverSettling's SettleRules), the passes' own tests.
    /// </summary>
    public readonly struct TerrainRules
    {
        /// <summary>Channel rules (EarthParams.ChannelRules).</summary>
        public int Channel { get; init; }
        /// <summary>Generation fixes from the code reviews (EarthParams.GenFixRules).</summary>
        public int GenFix { get; init; }
        /// <summary>0064's works rules (EarthParams.Works1318).</summary>
        public int Works { get; init; }
        /// <summary>Shore rules (EarthParams.ShoreRules).</summary>
        public int Shore { get; init; }
        /// <summary>GLO-90 rules (EarthParams.FineElevation).</summary>
        public int Fine { get; init; }
        /// <summary>Surveyed summit rules (EarthParams.Summits).</summary>
        public int Summit { get; init; }
        /// <summary>Detail noise rules (EarthParams.DetailRules).</summary>
        public int Detail { get; init; }
        /// <summary>Included GLO-90 rules (EarthParams.CoarseElevation).</summary>
        public int Coarse { get; init; }
        /// <summary>Climate rules (EarthParams.ClimateRules).</summary>
        public int Climate { get; init; }
        /// <summary>Arid river rules (EarthParams.AridRiverRules).</summary>
        public int Arid { get; init; }
        /// <summary>River flow rules (EarthParams.FlowRules).</summary>
        public int Flow { get; init; }
        /// <summary>Geothermal rules (EarthParams.GeothermalRules).</summary>
        public int Geothermal { get; init; }

        // ---------------------------------------------------------------- channel rules (0063, 0071, 0073, 0074)

        /// <summary>Channel rules 1 (0063): the course's channel dredged everywhere at the flow's width; the mask's river water
        /// kept wet against the arid rules; no outline water along the Suez and Panama canals; the channel depth's noise; the
        /// flow blended along the course. 0: the outlines cut and narrow the channel.</summary>
        public bool Dredged => Channel >= 1;
        /// <summary>Channel rules 3 (0071): one bed where river and sea meet (TerrainModel.ApplyMouth, MouthShelf), the solved
        /// flow's depth at a mouth.</summary>
        public bool MouthBed => Channel >= TerrainModel.MouthChannelRules;
        /// <summary>Channel rules 4 (0073): river water as one bed, still or flowing reach by reach, the mask's narrow pieces the
        /// flow passes through kept, the blended depth near courses.</summary>
        public bool OneBed => Channel >= TerrainModel.FlowChannelRules;
        /// <summary>Channel rules 5 (0074): the mask's ponds along a river trimmed to its one width (TerrainModel.PondProfileOf).</summary>
        public bool OneWidth => Channel >= RiverNetwork.PondChannelRules;

        // ---------------------------------------------------------------- generation fixes

        /// <summary>GenFix 1 (review of 11 November): dry land below 0 m continuous with the land at 0 m (DryLandTop), and the
        /// other fixes of that review the passes test themselves.</summary>
        public bool DryLandLedge => GenFix >= 1;
        /// <summary>GenFix 1 (CR-E1): the plant and crop envelopes read a river's green belt's rain, as trees and soil do.</summary>
        public bool RiparianEnvelopes => GenFix >= 1;
        /// <summary>GenFix 1: the springs read a block's column where the terrain pass does (its corner), not its centre.</summary>
        public bool CornerColumns => GenFix >= 1;
        /// <summary>GenFix 1 (CR-G5): the gravel on a quarter of a dry river bed scattered by a position hash, not on diagonals.</summary>
        public bool ScatteredBedGravel => GenFix >= 1;
        /// <summary>GenFix 2 (EA-0040): on the grid's land the noise's dips near a water level fade in from 20 to 40 m.</summary>
        public bool EasedDips => GenFix >= 2;
        /// <summary>GenFix 3 (EA-0072): a grid lake's water ends along an irregular line on low ground (GridLakeEdgeAt).</summary>
        public bool GridLakeEdge => GenFix >= 3;
        /// <summary>GenFix 4 (ER-19): a crater-less vent's conduit capped at its column's top (VolcanoInterior).</summary>
        public bool ConduitUnderTop => GenFix >= TerrainModel.GenFixConduitUnderTop;
        /// <summary>GenFix 4 (ER-20): RegionalRock reads a ground column to its bottom (EarthGeology).</summary>
        public bool WholeGroundColumn => GenFix >= TerrainModel.GenFixWholeGroundColumn;
        /// <summary>GenFix 5 (EA-0111): the river flow read across its 1° tiles' edges (RiverFlowData.At).</summary>
        public bool FlowAcrossTiles => GenFix >= TerrainModel.GenFixFlowAcrossTiles;
        /// <summary>GenFix 5 (EA-0112): a lake's whole area, not its piece in one 1° tile (LakeOutlineLayer.Hit.WholeArea).</summary>
        public bool WholeLakeArea => GenFix >= TerrainModel.GenFixWholeLakeArea;
        /// <summary>GenFix 5 (T-L12): SeamNoise reads an offset place beyond the ring inside it.</summary>
        public bool SeamNoiseInRing => GenFix >= TerrainModel.GenFixSeamNoiseInRing;
        /// <summary>GenFix 5 (T-L14): every kept grid lake level near a place counts, not only the highest.</summary>
        public bool KeptLakeLevels => GenFix >= TerrainModel.GenFixKeptLakeLevels;

        // ---------------------------------------------------------------- works of 1318 (0064)

        /// <summary>Works rules 3 (EA-0042): the sea of 1318 between SEA areas (EnclosedBySea), and the sea's side near a SEA
        /// area measured to the land of 1318 (CoastDistanceAt). Below 3 to today's outline as well.</summary>
        public bool SeaOf1318 => Works >= 3;
        /// <summary>Works rules 4 (EA-0053): the islands' outlines inside a SEA or LAND area of 1318 as the works decide.</summary>
        public bool IslandsOf1318 => Works >= 4;

        // ---------------------------------------------------------------- shores (0070)

        /// <summary>Shore rules 1: the shore strip of the place's material (TerrainModel.ApplyShoreStrip).</summary>
        public bool ShoreStrip => ShoreMaterialFacts.Strip(Shore);
        /// <summary>Shore rules 2: the beach at the water's level, the foredune, the shallows of the shore's material.</summary>
        public bool ShoreAtWater => ShoreMaterialFacts.AtWater(Shore);

        // ---------------------------------------------------------------- GLO-90, summits, detail (0051, 0053, 0031)

        /// <summary>GLO-90 rules 2: the summit lift on GLO-90's land (FineSummitLift).</summary>
        public bool FineSummitLift => Fine >= 2;
        /// <summary>GLO-90 rules 3: GLO-90's points joined by a smooth curve (FineElevation.TrySmoothHeight).</summary>
        public bool FineSmooth => Fine >= 3;
        /// <summary>GLO-90 rules 4: the mean over the column's footprint where a block is wider than GLO-90's spacing.</summary>
        public bool FineFootprintMean => Fine >= 4;
        /// <summary>GLO-90 rules 5: the smooth curve bounded by its points; beaches end a coast's reach inland.</summary>
        public bool FineBounded => Fine >= 5;
        /// <summary>Summit rules 2: surveyed peaks restored where the data's tops lack them.</summary>
        public bool SummitRestore => Summit >= 2;
        /// <summary>Detail rules 1: the detail noise sized by the data's own cell and relief (TerrainModel.BuiltInDetail).</summary>
        public bool CellDetail => Detail >= 1;
        /// <summary>Included-GLO-90 rules 1: dry land from the included grid where full GLO-90 isn't used.</summary>
        public bool CoarseHeights => Coarse >= 1;

        // ---------------------------------------------------------------- climate, rivers' water, geothermal

        /// <summary>Climate rules 2 (decision 0009): the climate map stores the sea-level temperature, vanilla cools by the real lapse.</summary>
        public bool RealLapse => Climate >= TerrainModel.RealLapseRules;
        /// <summary>Climate rules 3: the climate map stores the real rainfall.</summary>
        public bool RealRain => Climate >= TerrainModel.RealRainRules;
        /// <summary>Arid river rules 1: rivers dry where their climate and flow say so (AridHydrology).</summary>
        public bool AridRivers => Arid >= 1;
        /// <summary>Flow rules 1 (0069): the flow solved on the mask's river water (RiverFlowData).</summary>
        public bool SolvedFlow => Flow >= 1;
        /// <summary>Geothermal rules 1: the climate map's geologic activity is Earth's (Geothermal.Activity).</summary>
        public bool GeothermalActivity => GeothermalFacts.Activity(Geothermal);

        public override string ToString() => $"terrain rules: channel {Channel}, genfix {GenFix}, works {Works}, shore {Shore}, fine {Fine}, summit {Summit}, detail {Detail}, coarse {Coarse}, climate {Climate}, arid {Arid}, flow {Flow}, geothermal {Geothermal}";
    }
}
