using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using ProtoBuf;

namespace EarthMap
{
    /// <summary>
    /// EarthParams, the world's terrain model built from them: geometry, vertical scale, the settings repaired where they
    /// can't make terrain, the peaks, the climate data's switches, the river courses as laid out (dams, the flow's reroutes)
    /// and the river network (EA-0089: split from EarthParams.cs, no change; the saved record stays there).
    /// </summary>
    public partial class EarthParams
    {
        /// <summary>The world's layout, from the saved settings alone: no Earth data needed.</summary>
        public EarthGeometry BuildGeometry()
        {
            return new EarthGeometry
            {
                RingStart = RingStart, RingLength = RingLength, Wraps = Wraps, Gutter = Gutter,
                EquatorZ = EquatorZ, BlocksPerDegreeZ = BlocksPerDegreeZ, SeamLon0 = SeamLon0,
            };
        }

        /// <summary>
        /// The world's frozen terrain settings made usable: one that isn't a finite number, is negative or is absurdly large
        /// (NaN detail, a negative river width) takes the default from <paramref name="defaults"/>. Zero stays: older saves
        /// use it for "not set" and the model treats it so. Returns the settings replaced (empty when all were fine).
        /// </summary>
        public string RepairSettings(EarthMapConfig defaults)
        {
            var bad = new System.Collections.Generic.List<string>();
            double Fix(string name, double value, double max, double fallback)
            {
                if (double.IsFinite(value) && value >= 0 && value <= max) return value;
                bad.Add(name + " " + value);
                return fallback;
            }
            DetailScale = Fix(nameof(DetailScale), DetailScale, 10, defaults.DetailScale);
            RiverMinDischarge = Fix(nameof(RiverMinDischarge), RiverMinDischarge, 1e6, defaults.RiverMinDischarge);
            RiverWidthScale = Fix(nameof(RiverWidthScale), RiverWidthScale, 20, defaults.RiverWidthScale);
            RiverRelief = Fix(nameof(RiverRelief), RiverRelief, 10, defaults.RiverRelief);
            StreamMinDischarge = Fix(nameof(StreamMinDischarge), StreamMinDischarge, 1e6, defaults.StreamMinDischarge);
            RiverStraighten = Fix(nameof(RiverStraighten), RiverStraighten, 1, defaults.RiverStraighten);
            RiverSmoothWidths = Fix(nameof(RiverSmoothWidths), RiverSmoothWidths, 50, defaults.RiverSmoothWidths);
            // geology's own limit (GeologyTransitions.IsValidWidth: 0..512); beyond it geology switches itself off for the session
            GeologyTransitionBlocks = Fix(nameof(GeologyTransitionBlocks), GeologyTransitionBlocks, 512, defaults.GeologyTransitionBlocks);
            if (!GeologyTransitions.IsValidWidth(GeologyTransitionBlocks)) GeologyTransitionBlocks = 96;
            MinIslandBlocks = Fix(nameof(MinIslandBlocks), MinIslandBlocks, 1e6, defaults.MinIslandBlocks);
            return string.Join(", ", bad);
        }

        public VerticalScale BuildVertical() => new VerticalScale(MapHeight, SeaLevel, TopY, LandH0, MaxDepth, SeaD0, LinearLand);

        PeakCatalog LoadPeaks(EarthData data)
        {
            if(Summits < 2 || string.IsNullOrEmpty(PeakCatalogSignature)) return null;
            var peaks=data.ReadPeaks();
            if(peaks?.Signature!=PeakCatalogSignature) throw new System.IO.InvalidDataException("this world's surveyed peak catalogue (earthdata/peaks.json, SHA-256 "
                + PeakCatalogSignature + ") is " + (peaks == null ? "missing" : "changed (this Earth's has " + peaks.Signature + ")") + ". Restore the peaks.json of the " +
                "Earth version that created this world, or set UpgradeExistingWorldMountainTerrain to true in ModConfig/earth.json once to give chunks generated " +
                "from now on this Earth's summits (they may meet the older chunks with a step at the summits that changed)");
            return peaks;
        }

        /// <summary>The climate data this world's rules read, the same on both sides (BuildModel; the client's climate-only
        /// model): the climate fix, CHELSA's temperatures and rainfall, the Penman-Monteith aridity.</summary>
        public void ConfigureClimateData(EarthData data)
        {
            data.SetClimateFix(ClimateData);
            // climate rules v2 (decision 0009): CHELSA's temperatures on the 2' grid
            data.SetFineTemperature(ClimateRules >= TerrainModel.RealLapseRules);
            // decision 0031: CHELSA's rainfall
            data.SetFineRain(FineRain >= 1);
            data.SetPenmanAridity(AridityRules >= 1);
            // decision 0087: the temperatures as around 1318
            data.SetPaleo1318(Climate1318 >= 1);
        }

        /// <param name="dams">0053 item 19: the dam file, whose course levels a world with DamRules sets before its
        /// rivers are laid out (its heights are FineElevation.Dams'); null: the courses as they are.</param>
        /// <param name="cache">This world's kept build results (the server's; null: everything is built).</param>
        public TerrainModel BuildModel(EarthData data, DamRemovalData dams = null, ModelCache cache = null, RiverFlowData flow = null)
        {
            var geo = BuildGeometry();
            ConfigureClimateData(data);
            // decision 0031: ETOPO's 30" summits, RGI's glaciers
            data.SetSummits(Summits >= 1);
            data.SetGlaciers(Glaciers >= 1);
            // first: everything below (the river exit channels too) reads the grid as this world has it; the water mask's
            // referee first of all (it is the grid, the islands and the coasts this world starts from)
            data.SetSeaReferee(SeaReferee >= 1);
            data.SetIslandOverlay(Islands > 0);
            data.SetLakeOverlay(RiverEnds > 0, RiverEnds >= Core.RiverEnds.Lakes1318 ? 2 : 0);
            data.SetShoreOverlay(LakeShores > 0);
            data.SetReservoirExclusions(ReservoirRules >= 1);
            data.SetOutletOverlay(LakeOutlets >= 1);
            var model = new TerrainModel(data, geo, BuildVertical(), NoiseSeed)
            {
                GutterSeabedY = GutterSeabedY,
                LocalAlpineEcology = LocalAlpineEcology,
                VanillaBiomes = VanillaBiomes >= 1,
                Summits = Summits >= 1 && data.HasSummits,
                SummitRules = Summits,
                SummitMinElevation = SummitMinElevation,
                Peaks = LoadPeaks(data),
                AlpineSurfaceRules = AlpineSurfaceRules,
                AridRiverRules = AridRiverRules,
                DetailScale = DetailScale,
                // a save from a newer build keeps its number, but only rules this build has run (EarthMapSystem warns)
                DetailRules = DetailRules,
                RiverRelief = RiverRelief > 0 ? RiverRelief : 1.0,
                GutterBridges = GutterBridges,
                GutterSeabed = GutterSeabed,
                MinSeaDepth = MinSeaDepth is > 0 and <= 20 ? MinSeaDepth : TerrainModel.OldSeaDepth,
                LakeShores = data.ShoreOverlay,
                ClimateRules = ClimateRules,
                GeothermalRules = GeothermalRules,
                ChannelRules = ChannelRules,
                LakeRules = LakeRules,
                ShoreZoneRules = ShoreZoneRules,
                ShoreRules = ShoreRules,
                ShoreData = ShoreMaterialFacts.Strip(ShoreRules) ? data.ShoreMaterial : null,
                // (a save from a newer build keeps its number, but only fixes this build has)
                GenFixRules = GenFixRules,
                SiteRules = SiteRules,
            };
            // 0053: the included GLO-90 heights (missing packs: the built-in heights, as before the packs)
            if (CoarseElevation > 0 && data.Coarse != null)
            {
                model.Coarse = data.Coarse;
                model.CoarseRules = CoarseElevation;
            }
            if (data.IslandOverlay) model.Islands = new IslandLayer(data.Islands, geo, MinIslandBlocks > 0 ? MinIslandBlocks : 4, Islands);
            if (Coasts > 0 && data.Coasts != null && data.IslandOverlay) model.Coasts = new CoastLayer(data.Coasts, geo, MinIslandBlocks > 0 ? MinIslandBlocks : 4);
            // decision 0064: the sea, lakes and land of 1318, before the river exits (SeaChannels) read the coast
            if (Works1318 >= 1 && model.Coasts != null && data.Works1318For(Works1318) != null)
            {
                model.Works = new LakeOutlineLayer(data.Works1318For(Works1318), geo, model.Coasts.Reach, skipFlags: Works1318Data.SkipFor(Works1318));
                model.WorksRules = Works1318;
            }
            // the straits' courses and the sea channels' ways are the slow parts: kept between starts (ModelCache)
            if (StraitRules >= 1) model.Straits = new NavigableStraits(geo, model, ModelCache.Use(cache, NavigableStraits.CachePart,
                () => NavigableStraits.FollowSea(geo, model), NavigableStraits.WriteRoutes, NavigableStraits.ReadRoutes));
            var canals = string.IsNullOrWhiteSpace(Canals) ? null : Canals.Split(',');
            var source = RiverSource(data, dams, out DamCourseLevels, out DamCourseError, FlowAsBuilt(flow), out FlowReroutes, out ulong courseKey);
            if (source != null) model.Rivers = BuildRivers(data, model, source);
            if (model.Rivers != null && AridRiverRules >= 1)
            {
                // decision 0069 rule 8: the wet-source flow from GIRES' non-perennial rivers where the world has them
                model.WetSourceFlow = PerennialRules >= 1 && data.NonPerennialFor(courseKey) != null
                    ? data.NonPerennialFor(courseKey).WetSourceFlow(model.Rivers.Reaches)
                    : AridHydrology.WetSourceFlow(data, model.Rivers.Reaches);
                model.DryFans = new WadiFans(model);
            }
            if ((RiverChannels > 0 && model.Rivers != null) || canals != null)
                model.Channels = SeaChannels.Build(model, RiverChannels > 0, canals, RiverChannels > 0 ? RiverChannels : SeaChannels.Version, cache);
            if (Zones != null)
            {
                foreach (var z in Zones)
                {
                    model.FlatZones.Add(z.ToZone());
                }
            }
            return model;
        }

        /// <summary>
        /// The river courses this world's rivers are laid out on (null without rivers), as BuildModel lays them out: its
        /// courses rules' reaches, with dam rules the dam file's levels through the old reservoirs (0053 item 19: a list read
        /// anew, so nothing else sees them), and with flow rules the flow file's rerouted mouths. The river audit
        /// (/earth checkrivers, the self-test) starts here, so it walks the network that was generated; before, it passed no
        /// flow and walked the mouths of every FlowRules 1 world unrerouted (ER-09, code review 14 November 2026).
        /// </summary>
        public List<RiverReach> RiverSourceAsBuilt(EarthData data, DamRemovalData dams, RiverFlowData flow) =>
            RiverSource(data, dams, out _, out _, FlowAsBuilt(flow), out _, out _);

        /// <summary>The flow file as this world's rivers read it: only with flow rules (decision 0069).</summary>
        RiverFlowData FlowAsBuilt(RiverFlowData flow) => FlowRules >= 1 ? flow : null;

        /// <summary>The courses with the dam file's levels (<paramref name="damLevels"/>: the stations lowered, -1 when the file
        /// was made for other courses, <paramref name="damError"/> says why), and (decision 0069 rule 2) the flow file's rerouted
        /// mouths when it was solved on this list (its key, taken before the dams lower it); <paramref name="reroutes"/> how many, -1 when
        /// the list isn't the flow file's. <paramref name="courseKey"/>: the list's key as read (DamRemovalData.KeyOf, before the
        /// dams lower its levels), for the files keyed to it (the flow, decision 0069 rule 8's classes); 0 when not needed.</summary>
        public List<RiverReach> RiverSource(EarthData data, DamRemovalData dams, out int damLevels, out string damError, RiverFlowData flow, out int reroutes,
            out ulong courseKey)
        {
            damLevels = 0;
            damError = null;
            reroutes = 0;
            var source = Rivers ? data.RiverReaches(RiverCourses) : null;
            courseKey = source != null && (flow != null || (PerennialRules >= 1 && data.HasNonPerennial)) ? DamRemovalData.KeyOf(source) : 0;
            bool flowFits = source != null && flow != null && courseKey == flow.CourseKey;
            if (source != null && DamRules >= 1 && dams != null && RiverCourses >= 1)
                damLevels = dams.LowerCourses(source, out damError);
            if (flow != null) reroutes = flowFits ? RiverFlowData.Reroute(source, flow) : -1;
            return source;
        }

        /// <summary>
        /// This world's river network from the courses <see cref="RiverSource"/> gives (with the river ends and deltas its rules
        /// have), on the grid as BuildModel set it up. Deterministic: the river audit rebuilds the same network after the
        /// start-up-only data was released (decision 0025).
        /// </summary>
        public RiverNetwork BuildRivers(EarthData data, TerrainModel model, List<RiverReach> source)
        {
            var geo = model.Geometry;
            var canals = string.IsNullOrWhiteSpace(Canals) ? null : Canals.Split(',');
            var reaches = OutletData.ReleaseRiverPoints(data, source);
            bool[] fade = null, seaEnds = null;
            if (RiverEnds > 0)
            {
                // (river ends rules 2, EA-0160: a river ends where it reaches the sea, by this world's coast)
                // (works rules 6, decision 0107: and into the lakes of 1318, as into the terminal lakes)
                var works = Works1318 >= Works1318Data.LakeRules ? model.Works : null;
                System.Func<double, double, double> lakeOf1318 = works == null ? null : (la, lo) =>
                {
                    var h = works.At(la, lo);
                    return Works1318Data.IsLake(h) ? model.RiverLevelOfLake(h.Level) : double.NaN;
                };
                var ends = Core.RiverEnds.Prepare(data, reaches, RiverEnds >= 2 && model.Coasts != null ? model.CoastSign : null, lakeOf1318);
                reaches = ends.Reaches;
                fade = ends.Fade;
                seaEnds = ends.SeaEnd;
                if (RiverDeltas > 0) reaches = Core.RiverDeltas.Apply(reaches, RiverDeltas).Reaches;
            }
            return new RiverNetwork(reaches, geo, model.RiverWaterTop, RiverOptions(data, geo) with
            {
                Falls = RiverFalls > 0 ? Core.RiverFalls.All : null,
                SeaLevelPiece = SeaChannels.CanalCrossing(SeaChannels.CanalCourses(geo, canals)),
                ContinueDownstream = RiverEnds > 0, FadeEnds = fade, SeaEnds = seaEnds,
            });
        }

        /// <summary>
        /// This world's river settings and rules as one record (BuildRivers adds the falls, the canals' crossings and the river
        /// ends' continuation, fades and sea ends, all per reach of the world's own courses); EA-0131's streams layer builds its
        /// networks with these (StreamLayer).
        /// </summary>
        public RiverBuildOptions RiverOptions(EarthData data, EarthGeometry geo)
        {
            // widths grow with the world: the reference is the standard 256k world (Cylinder World ring 204 800)
            double sizeScale = geo.RingLength > 0 ? geo.RingLength / 204800.0 : 1;
            return new RiverBuildOptions(RiverMinDischarge, RiverWidthScale > 0 ? RiverWidthScale : 1)
            {
                StreamMode = string.IsNullOrEmpty(StreamMode) ? "none" : StreamMode,
                StreamMinQ = StreamMinDischarge > 0 ? StreamMinDischarge : 20,
                SizeScale = StreamMode == null ? 1 : sizeScale,
                Straighten = RiverStraighten,
                SmoothWidths = RiverSmoothWidths,
                GeometrySeams = RiverEnds > 0,
                WidthRules = RiverWidths,
                WidthMultiplier = RiverWidthMultiplier,
                OneBlockStreams = CoarseRiverRules >= 1,
                RiversToTheirEnd = CoarseRiverRules >= 2,
                SteepStreams = CoarseRiverRules >= 2,
                HeadStreamKm = CoarseRiverRules >= 2 ? RiverNetwork.HeadStreamKmRule : 0,
                PoolLayout = RiverPoolLayout,
                Rapids = !NoRiverRapids,
                Courses = RiverCourses,
                LandCellMetres = RiverCourses < 1 ? 0 : FineElevation >= 1 ? Core.FineElevation.CellMetres
                    : CoarseElevation >= 1 && data.Coarse != null ? data.Coarse.CellMetres : 0,
                ChannelRules = ChannelRules,
            };
        }
    }
}
