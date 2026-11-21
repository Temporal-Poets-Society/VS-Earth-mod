namespace EarthMap.Core
{
    // EA-0084 (ER-10, code review 14 November 2026): the saved rules numbers that more than one version of reads, as named
    // facts per family, each fact's first version written once. TerrainRules holds the twelve families TerrainModel reads
    // itself (with their record); these are the others, asked by number where they are read. A rule with a single version
    // reads plainly as on (>= 1) and gets facts with its second version. tests/TerrainRulesTests pins every fact here.

    /// <summary>River pool layouts (EarthParams.RiverPoolLayout, RiverNetwork.PoolLayout; decision 0024 and RiverNetwork's
    /// version notes).</summary>
    public static class PoolLayoutFacts
    {
        /// <summary>1: drops follow the real slope (each pool at least MinPool long).</summary>
        public static bool SlopePools(int layout) => layout >= 1;
        /// <summary>4: a lake above a river beside it gives way to the river's channel and banks (lake rims).</summary>
        public static bool LakeRims(int layout) => layout >= 4;
        /// <summary>5: a sill under each 1-block step, so Hydrology's communicating vessels may fill the deep channels.</summary>
        public static bool StepSills(int layout) => layout >= 5;
        /// <summary>8 (decision 0027): rapids reaches, no sills; Hydrology reads the river flow query (version 1).</summary>
        public static bool RapidsReaches(int layout) => layout >= 8;
        /// <summary>9: the river steps down one block at a time, the sources of a rapids reach carry its flow (query 2).</summary>
        public static bool BlockSteps(int layout) => layout >= 9;
        /// <summary>11 (decision 0046): rapid water only at a rapids reach's drops, every other source labelled (query 3).</summary>
        public static bool DrawnRapids(int layout) => layout >= 11;
        /// <summary>The river query version a world of this layout answers (EarthMapSystem.RiverQueryVersion): 0 below 8.</summary>
        public static int RiverQueryVersion(int layout) => DrawnRapids(layout) ? 3 : BlockSteps(layout) ? 2 : RapidsReaches(layout) ? 1 : 0;
    }

    /// <summary>Alpine surface rules (EarthParams.AlpineSurfaceRules, MountainEcology.SurfaceVersion; decision 0032).</summary>
    public static class AlpineSurfaceFacts
    {
        /// <summary>1: closed turf from the tree line up, scree towards the snow line and in dry mountains.</summary>
        public static bool Turf(int rules) => rules >= 1;
        /// <summary>2: on the last ice sheets' ground (Scandinavia, Scotland, the Canadian Rockies) turf closes only near the
        /// tree line (global-glaciation.egts).</summary>
        public static bool IceSheetGround(int rules) => rules >= 2;
    }

    /// <summary>Shore zone rules (EarthParams.ShoreZoneRules, Core.ShoreZones.RulesVersion; decision 0069).</summary>
    public static class ShoreZoneFacts
    {
        /// <summary>1: the shore zones: no tall grass on a beach's sand, the tree pick by the shore's zone.</summary>
        public static bool Zones(int rules) => rules >= 1;
        /// <summary>2: the sea coast's zone at any height, not only on low coasts.</summary>
        public static bool AnyHeight(int rules) => rules >= 2;
    }

    /// <summary>Vegetation zone rules (EarthParams.ZoneRules, Core.ClimateZones.RulesVersion; habitats step 1).</summary>
    public static class ZoneFacts
    {
        /// <summary>1: vegetation zones from climate set the forest and shrub density.</summary>
        public static bool Zones(int rules) => rules >= 1;
        /// <summary>2: trees as dense as the forest map says, without vanilla's floor of a tree every 7 to 8 chunks.</summary>
        public static bool NoTreeFloor(int rules) => rules >= 2;
    }

    /// <summary>Geothermal rules (EarthParams.GeothermalRules, Geothermal.RulesVersion; decisions 0057, 0078).
    /// TerrainRules.GeothermalActivity is 1.</summary>
    public static class GeothermalFacts
    {
        /// <summary>1: the known hot springs and the volcanoes' heat in the climate map.</summary>
        public static bool Activity(int rules) => rules >= 1;
        /// <summary>2: volcano bodies: a magma chamber and conduit, craters and lava in the active ones.</summary>
        public static bool VolcanoBodies(int rules) => rules >= 2;
        /// <summary>3 (decision 0078): the volcanoes' interiors in real proportions.</summary>
        public static bool VolcanoInteriors(int rules) => rules >= 3;
    }

    /// <summary>Shore material rules (EarthParams.ShoreRules, Core.ShoreMaterial.RulesVersion; decision 0070). TerrainRules'
    /// ShoreStrip and ShoreAtWater are 1 and 2.</summary>
    public static class ShoreMaterialFacts
    {
        /// <summary>1: the shore strip of the place's material (TerrainModel.ApplyShoreStrip, shore-material.bin).</summary>
        public static bool Strip(int rules) => rules >= 1;
        /// <summary>2: the beach at the water's level, the foredune, the shallows of the shore's material.</summary>
        public static bool AtWater(int rules) => rules >= 2;
        /// <summary>3 (EA-0056): a coast of thin till over glacially scoured rock is bare rock to the water where OSM maps no
        /// beach (GroundColumns).</summary>
        public static bool ScouredTill(int rules) => rules >= 3;
    }

    /// <summary>Regional geology rules (EarthParams.GeologyRulesVersion, RegionalGeologyCatalog.RulesVersion): 1 is 0.12's,
    /// reproduced block for block, 2 new worlds'. A world generates with 1 or 2 only (EarthGeology turns regional geology off
    /// for any other), so where these facts are read, "not from 2" is 1.</summary>
    public static class GeologyFacts
    {
        /// <summary>2: the rules file's generation block: the coastal fill of the grid's no-data cells, the cells' jitter and
        /// warp, the reference surface's scale, the gravel lenses' coverage.</summary>
        public static bool GenerationBlock(int rules) => rules >= 2;
        /// <summary>2: a point's category comes from the nearest jittered seed of the 0.5° cells, so contacts run between
        /// cells, not along the grid.</summary>
        public static bool CellContacts(int rules) => rules >= 2;
        /// <summary>2: bed boundaries at heights below a smoothed regional reference surface (ColumnV2, BedV2), not counted
        /// down from each column's top (ColumnV1, BedV1).</summary>
        public static bool SmoothedBeds(int rules) => rules >= 2;
        /// <summary>2: discontinuous gravel lenses on coarse sediment, cleared again under vanilla's clay and peat patches
        /// (the deposit-column hook).</summary>
        public static bool GravelLenses(int rules) => rules >= 2;
        /// <summary>3 (decision 0101): unconsolidated cover (GLiM class 1) takes the nearest mapped bedrock within the rules'
        /// radius, else a sedimentary basin profile, instead of keeping vanilla's strata.</summary>
        public static bool CoveredFill(int rules) => rules >= 3;
        /// <summary>4 (EA-0198, decision 0106): the rock comes from the 30″ rock set (full-resolution GLiM, with Macrostrat's
        /// national maps where they exist) through the rules' litho table, and contacts bend at the scale of its cells.</summary>
        public static bool FineRock(int rules) => rules >= 4;
        /// <summary>3 (decision 0101): clay and peat read a deposit's relief over neighbours 1.2 km away at any scale
        /// (<see cref="RegionalGeologyWorld.FootprintMetres"/>), not 12 blocks.</summary>
        public static bool ReliefInMetres(int rules) => rules >= 3;
        /// <summary>3 (decision 0102): with vanilla ores, vanilla's galena and its silver also form in granite, slate, phyllite,
        /// gneiss and schist (CrystallineGalena adds them to its hosts while the game runs).</summary>
        public static bool CrystallineGalena(int rules) => rules >= 3;
    }

    /// <summary>Ground column rules (EarthParams.GroundColumns, GroundColumns.RulesVersion; decision 0076). One rules file
    /// serves every version: a rule added later names the versions it holds for.</summary>
    public static class GroundColumnFacts
    {
        /// <summary>1: ground columns: the ground built from the data tiles' units down to the rock.</summary>
        public static bool Columns(int rules) => rules >= 1;
        /// <summary>2 (EA-0062): the yearly mean at the place's own height is read, so karst carries terra rossa only where it
        /// is warm (rendzina on limestone rubble where it is cool).</summary>
        public static bool WarmKarst(int rules) => rules >= 2;
        /// <summary>2: vanilla's bauxite strata are replaced like other rock under the ground (Earth's bauxite is the ore
        /// layers').</summary>
        public static bool NoBauxiteStrata(int rules) => rules >= 2;
    }

    /// <summary>Ore layer rules (EarthParams.OreLayers, OreLayers.RulesVersion; each version its own rules file).</summary>
    public static class OreLayerFacts
    {
        /// <summary>1 (decision 0060): deposits keep to their ground layers.</summary>
        public static bool Layers(int rules) => rules >= 1;
        /// <summary>2 (EA-0059): a deposit block may sit directly under a block its rule's <c>under</c> keeps (the fire clay
        /// under a coal seam).</summary>
        public static bool Under(int rules) => rules >= 2;
        /// <summary>3 (EA-0064, decision 0076): <c>surface</c> rules lay loose flint and chert on the ground of their cover.</summary>
        public static bool Surface(int rules) => rules >= 3;
        /// <summary>4 (EA-0068, decision 0077): Earth's kyanite, sillimanite and andalusite deposits are kept
        /// (OreLayers.AluminosilicateVersion).</summary>
        public static bool Aluminosilicates(int rules) => rules >= 4;
    }
}
