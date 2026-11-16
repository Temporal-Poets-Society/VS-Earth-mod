using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using ProtoBuf;

namespace EarthMap
{
    /// <summary>
    /// Everything that places the Earth in one world, frozen into the savegame when the world is created and sent to
    /// clients (they need it for the live temperature and latitude).
    /// ProtoMember numbers: EarthWorldOptionsTests' PinnedParamsTable is the one list of those taken, each with its name and
    /// type (append only; S-R3 of the Fable pass: this header kept its own copy, which went stale). 54 and 94 were used on
    /// other lines and are never reused; 110-119 and 130-189 were never used. A new field takes the number after the
    /// table's last line, claimed where the other sessions see it before its branch merges. The declarations are grouped by
    /// subject, not sorted by number.
    /// </summary>
    [ProtoContract]
    public partial class EarthParams : IExtensible
    {
        /// <summary>(FP-04, the Fable pass of the 14 November review) Fields this build doesn't know, as protobuf-net read them:
        /// kept, so that saving the params again (every start stores LastSavedWith) doesn't drop a newer Earth's fields; and
        /// seen, so that a world a newer Earth saved is refused (EarthMapSystem.InitWorld) although none of the rules this build
        /// knows is above its newest. Every new rule so far came as a new field.</summary>
        IExtension unknownFields;
        IExtension IExtensible.GetExtensionObject(bool createIfMissing) => Extensible.GetExtensionObject(ref unknownFields, createIfMissing);
        /// <summary>Whether the save holds fields this build doesn't know (a newer Earth wrote them).</summary>
        public bool HasUnknownFields => unknownFields != null && unknownFields.GetLength() > 0;

        /// <summary>The layout version a world saves (Version). 1: 0.1.0. 2: beaches, coastal lake barriers, retuned rainfall. 3:
        /// stronger height curves, real seasons, summer-dry climates, forest types. 4: large rivers. 5 to 11: Earth Map up to
        /// 0.12 (not listed). It has stayed 11 since every rule got a saved version of its own; nothing reads it but the record.</summary>
        public const int CurrentVersion = 11;

        [ProtoMember(1)] public int Version;
        [ProtoMember(2)] public bool Enabled;

        // geometry (EarthGeometry)
        [ProtoMember(3)] public double RingStart;
        [ProtoMember(4)] public double RingLength;
        [ProtoMember(5)] public bool Wraps;
        [ProtoMember(6)] public double Gutter;
        [ProtoMember(7)] public double EquatorZ;
        [ProtoMember(8)] public double BlocksPerDegreeZ;
        [ProtoMember(9)] public double SeamLon0;
        /// <summary>Made with Cylinder World. Earth needs it (decision 0023): new worlds always are, and a save without it
        /// (made by an older Earth Map without Cylinder World, with Wraps false) is refused at startup.</summary>
        [ProtoMember(10)] public bool Cylinder;

        // vertical scale
        [ProtoMember(11)] public int MapHeight;
        [ProtoMember(12)] public int SeaLevel;
        [ProtoMember(13)] public double TopY;
        [ProtoMember(14)] public double LandH0;
        [ProtoMember(15)] public double MaxDepth;
        [ProtoMember(16)] public double SeaD0;

        // terrain
        [ProtoMember(17)] public int GutterSeabedY;
        [ProtoMember(18)] public long NoiseSeed;
        [ProtoMember(19)] public double DetailScale;
        [ProtoMember(20)] public List<FlatZoneData> Zones = new List<FlatZoneData>();

        // spawn and story
        [ProtoMember(21)] public double SpawnLat;
        [ProtoMember(22)] public double SpawnLon;
        [ProtoMember(23)] public int SpawnX;
        [ProtoMember(24)] public int SpawnZ;
        [ProtoMember(25)] public bool StoryPlaced;

        // runtime (from config every start; ExtendTemperature only in older saves, TemperatureRangeFrozen)
        [ProtoMember(26)] public bool ExtendTemperature;
        [ProtoMember(27)] public bool RealSeasons;
        /// <summary>A04: 1 = ExtendTemperature is this world's own, frozen at creation from earth.json's ExtendTemperatureRange:
        /// the climate hook adds its correction to world generation's temperatures too, so a setting changed later would
        /// change the new chunks of an existing world. 0: worlds made before (the config at every start, as they always had).</summary>
        [ProtoMember(108)] public int TemperatureRangeFrozen;

        // rivers (layout v4)
        [ProtoMember(28)] public bool Rivers;
        [ProtoMember(29)] public double RiverMinDischarge;
        [ProtoMember(30)] public double RiverWidthScale;
        [ProtoMember(31)] public double RiverRelief;
        /// <summary>"ends", "all" or "none"; empty in worlds made before streams (0.6 and older: none).</summary>
        [ProtoMember(32)] public string StreamMode;
        [ProtoMember(33)] public double StreamMinDischarge;
        /// <summary>0..1, see the config; worlds from before 0.7 have 0 (real courses).</summary>
        [ProtoMember(34)] public double RiverStraighten;
        [ProtoMember(35)] public double RiverSmoothWidths;

        // Frozen independently of terrain layout; missing in old saves means legacy composition.
        [ProtoMember(36)] public bool RegionalTrees;
        [ProtoMember(37)] public int TreeRulesVersion;

        // Missing fields in old saves keep their previous geology and animal spawning.
        [ProtoMember(38)] public bool RegionalGeology;
        [ProtoMember(39)] public int GeologyRulesVersion;
        [ProtoMember(40)] public string GeologyProviderSignature;
        [ProtoMember(41)] public bool RegionalFauna;
        [ProtoMember(42)] public int FaunaRulesVersion;
        // Frozen in pre.12+ worlds; older saves apply config on restart. Never removes existing entities.
        [ProtoMember(43)] public bool IncludeExtinctAnimals;

        [ProtoMember(44)] public bool RegionalOres;
        /// <summary>The regional ore catalogue's fix level (RegionalOreCatalog.Version), which every regional-ores world adopts
        /// when it loads: a record of the fixes applied, it gates nothing. What changes generation is <see cref="OreRules"/>
        /// (Core.OreBackground.CatalogueRules).</summary>
        [ProtoMember(45)] public int OreRulesVersion;
        [ProtoMember(46)] public string OreSettingsJson;
        [ProtoMember(47)] public string OreDataSignature;
        [ProtoMember(48)] public double GeologyTransitionBlocks;
        /// <summary>Companion terrain pads/expansions published during terrain planning (TerrainModel.TerrainPlanSignature).
        /// Missing in older saves; recorded at the next start. A later difference means new chunks may not line up.</summary>
        [ProtoMember(49)] public string TerrainPlanSignature;
        /// <summary>Antarctic land and ice continue across the seam gutter (TerrainModel.GutterBridges). New wrapping worlds from
        /// 0.13.2; missing in older saves (the gutter cuts a strait), which opt in with UpgradeExistingWorldGutterBridges.</summary>
        [ProtoMember(50)] public bool GutterBridges;
        /// <summary>Version of the waterfall list (RiverFalls.Version) this world's rivers drop over; 0: none (saves before
        /// 0.13.3, which opt in with UpgradeExistingWorldRiverFalls).</summary>
        [ProtoMember(51)] public int RiverFalls;
        /// <summary>Version of the river exit channels (SeaChannels.Version); 0: none (saves before 0.13.3, which opt in with
        /// UpgradeExistingWorldRiverChannels). From 5 (decision 0071) none in worlds with outline coasts.</summary>
        [ProtoMember(52)] public int RiverChannels;
        /// <summary>The sea-level canals this world has (SeaChannels.KnownCanals codes, comma-separated: "suez,panama"); empty for none.
        /// From the config at world creation; existing worlds change it with UpgradeExistingWorldCanals.</summary>
        [ProtoMember(53)] public string Canals;
        // 54 was used by 0.14 pre-releases (moved to the Earth Ruins mod's own save data); don't reuse it.
        /// <summary>Version of the island rules (IslandLayer.RulesVersion) this world's small islands come from: outlines
        /// instead of the grid's blobs, without ETOPO's false islands on reef rims. 0: none (saves before 0.14). 1: the
        /// outlines as GSHHG has them (0.14.0), 2: their raster staircases rounded off, 3: islands in the sea kept whole where
        /// a river's centre line or exit channel crosses them. Older saves keep theirs and opt in with UpgradeExistingWorldIslands.</summary>
        [ProtoMember(55)] public int Islands;
        /// <summary>Islands narrower than this (blocks) are left out: with a 4 million block ring a 40 m cay, with 512 000 one
        /// about 300 m wide.</summary>
        [ProtoMember(56)] public double MinIslandBlocks;
        /// <summary>Version of the river end rules (Core.RiverEnds.Version): rivers continue downstream to their end, end in the
        /// terminal lakes (lakes.bin: the revived Aral Sea, Lop Nur, ... and the natural salt lakes), are led on to a sea or lake
        /// they stop short of, and fade out in dry basins; their seam crossings are found by the ring's geometry
        /// (RiverNetwork.geometrySeams). 0: none (saves before 0.14.1, which opt in with UpgradeExistingWorldRiverEnds).</summary>
        [ProtoMember(57)] public int RiverEnds;
        /// <summary>Version of the world-generation temperature correction in Earth's climate hook
        /// (TerrainModel.ClimateCorrection): 1 = the real temperature less vanilla's reading with both its cuts to -20..40 °C
        /// (new worlds from 0.14.1); 0 = the older correction from the real temperature alone, which left warm highlands of
        /// worlds 512+ blocks high too cold. The two agree wherever the stored value fits (all of 256-384 high worlds). Live
        /// temperatures use version 1 in every world (they leave no seams). 2 (TerrainModel.RealLapseRules, new worlds from decision
        /// 0009's v2): the climate map stores the sea-level temperature and vanilla's altitude adjustment cools by the real lapse,
        /// 0.0065 °C per metre of the vertical scale (ClimateLapse), on both sides: warm highlands right at any world height in
        /// world generation too, and the same live temperature on client and server.</summary>
        [ProtoMember(58)] public int ClimateRules;
        /// <summary>Least water (blocks) over the sea floor away from the shore (TerrainModel.MinSeaDepth): 6 for new worlds from
        /// 0.14.1, room for a carrack over the shallow banks. 0: 5 (saves before, which opt in with UpgradeExistingWorldSeaDepth).</summary>
        [ProtoMember(59)] public int MinSeaDepth;
        /// <summary>Version of the lake shore data (ShoreData.FileVersion, shores.bin) this world's lakes are framed by: only the
        /// land touching a lake is raised to its level, further out it eases down to the real ground (TerrainModel.LakeShores).
        /// 0: the whole 3-cell ring stands at least at the lake's level, flat with a cliff where the ground is lower (saves
        /// before 0.14.3, which opt in with UpgradeExistingWorldLakeShores).</summary>
        [ProtoMember(60)] public int LakeShores;
        /// <summary>Version of the coast outlines (CoastData.FileVersion) the big land's coasts come from: land and sea near
        /// them by GSHHG instead of the 2' grid's cells. Only in worlds whose grid cells are many blocks wide (from about a
        /// 350 000 block ring) and that have the islands (they come from the same outlines). 0: none (saves before 0.14, smaller
        /// worlds; opt in with UpgradeExistingWorldCoasts).</summary>
        [ProtoMember(61)] public int Coasts;
        /// <summary>Version of the delta branches (Core.RiverDeltas.Version): where a river splits (the Mekong at Vàm Nao), its
        /// branches share its discharge instead of one branch taking all of it. Needs RiverEnds. 0: none (saves before 0.14.7,
        /// which opt in with UpgradeExistingWorldRiverDeltas).</summary>
        [ProtoMember(62)] public int RiverDeltas;
        /// <summary>Version of the river width rules (RiverNetwork.WidthRulesVersion): 1 = the standard world's gameplay widths
        /// as a floor in blocks on any bigger map, a river's real width where that is wider (new worlds from 0.14.7; worlds up
        /// to the standard 204 800 ring are unchanged by it). 0: widths grow with the world (saves before, which opt in with
        /// UpgradeExistingWorldRiverWidths).</summary>
        [ProtoMember(63)] public int RiverWidths;
        /// <summary>Version of the seam gutter's seabed (TerrainModel.GutterSeabed): 1 = the Earth's real seabed at the seam,
        /// carried across the gutter, which Cylinder World keeps as its seam band's seabed. 0: Earth's seabed fading to the flat
        /// GutterSeabedY (older saves). 2 (decision 0029): the seam meridian's land is carried across too (St. Lawrence
        /// Island), which Cylinder World keeps in its seam band. 3 (decision 0030): no gutter (Gutter 0); the real Earth runs
        /// across the jump line, its detail noise repeating around the ring (TerrainModel.RealSeam), and Cylinder World keeps
        /// its seabed and land in the band as for 1 and 2. New wrapping worlds only: it moves the sea floor and the land under
        /// existing chunks.</summary>
        [ProtoMember(64)] public int GutterSeabed;

        // Weather: creation choices in pre.12+ (0036); older saves keep decision 0017's config on every start.
        [ProtoMember(65)] public bool Weather;
        [ProtoMember(66)] public double WeatherPace;
        [ProtoMember(67)] public double RainFrequency;
        [ProtoMember(68)] public double StormFrequency;
        /// <summary>Version of the weather rules (EarthWeather.RulesVersion) the server runs; a client with other rules
        /// would see other weather, so it leaves the weather to vanilla and says so.</summary>
        [ProtoMember(69)] public int WeatherRules;
        /// <summary>Version of the climate data fixes (EarthData.ClimateFixVersion, decision 0018): 1 = UDel's missing
        /// precipitation months filled, without the one-row stripes of ocean data across the continents (climatefix.bin, new
        /// worlds from 0.15.1); 2 = the complete UDel normals, with December's precipitation over the land (climatefix2.bin,
        /// new worlds from 2026-11-07: the missing months and December's 0 mm were a cut-short download); 3 = v2 with UDel's
        /// precipitation faded into GPCP's over a 1° coast band instead of one row, without the rain stripes along the coasts
        /// (climatefix3.bin, new worlds from 2026-11-13, EA-0016); 4 = v3 with GPCP's share at CHELSA's level on desert coasts
        /// (climatefix4.bin, new worlds from 2026-11-14, EA-0060: Lima 434 → 35 mm). 0: the climate grid as before (older saves). Older saves
        /// move to the newest with UpgradeExistingWorldClimateData.</summary>
        [ProtoMember(70)] public int ClimateData;
        /// <summary>The server's weather.bin fingerprint (WeatherData.Signature): a client with other data keeps vanilla weather.
        /// Set at every start (the handshake), so it says nothing about the file the world was made with: see WeatherDataMade.</summary>
        [ProtoMember(71)] public string WeatherDataSignature;
        /// <summary>The weather.bin fingerprint (WeatherData.Signature) this world was created with, frozen then (adversarial
        /// review A6): the vegetation zones and fire rules of world generation read its monthly normals (ClimateZones,
        /// TerrainModel.Ecology), so a later start with another file says so once (new chunks would follow that file). Null
        /// in worlds made before 2.0.0-rc.3. weather.bin never changes under its name (WeatherTests pins it).</summary>
        [ProtoMember(109)] public string WeatherDataMade;
        /// <summary>Whether showers follow the local sun (longitude / 15 hours from the calendar's): only when AstraTerra's sun
        /// follows Earth's meridians on the server. Decided by the server and sent, so every side computes the same rain.</summary>
        [ProtoMember(72)] public bool WeatherLocalSun;
        /// <summary>Version of the river pool layout (RiverNetwork.PoolLayoutVersion, decision 0024): 1 = drops follow the real
        /// slope (every pool at least MinPool long, a drop as high as the valley has fallen, rapids where it is 2 blocks or
        /// more; worlds from 0.16.2). 2 = as 1, with drops of 2–4 blocks as shallow rocky chutes and curtains (5+ blocks
        /// or a listed waterfall) with a lip and a plunge pool (worlds from 0.16.4). 3 = as 2, with no rock in the chutes and
        /// the bed eased into and out of them (worlds from 0.16.5). 4 = as 3, with lake rims: a lake well above a river
        /// beside it meets it only at its drop, and no lake water stands above lower dry land (worlds from 0.16.6). 5 = as 4,
        /// with sills under 1-block steps and the drops' water blocks in the shape that stays as generated under Hydrology's
        /// rules; such worlds also turn on Hydrology's communicating vessels (worlds from 0.16.8). 6 = as 5, with rapids at 1-block
        /// steps decided step by step by Hydrology's rapids window, and spills from ramp height 2 (worlds from 0.16.10). 7 = as 6,
        /// with white water: chute steps 5 apart, and rapids runs that keep stepping every 5 blocks while the valley falls that
        /// fast (worlds from 0.16.11). 8 = as 6 (no sections), steps without sills and rapid water over whole rapids reaches;
        /// Hydrology reads the river flow query (decision 0027; new worlds from 0.16.13 when Hydrology supports it). 9 and 10:
        /// steps an eighth of a block per block, rapids' sources labelled with the flow (decision 0027). 11 = as 10, with rapids
        /// reaches only where the current is strong for the river's size, mixed with the calm reaches beside them, and calm
        /// rivers' sources labelled with the flow (decision 0046; new worlds from Earth 1.0.3 with Hydrology 0.6.2). 0: one
        /// block per step (older saves;
        /// it changes the river layout, so they keep theirs).</summary>
        [ProtoMember(73)] public int RiverPoolLayout;
        // The profile of decision 0031 (named "Experimental" in VerticalTerrainOptions; every world made from the menu has it,
        // 1.0 and 2.0 alike, ER-28). Missing fields preserve legacy worlds exactly.
        [ProtoMember(74)] public bool LinearLand;
        [ProtoMember(75)] public bool LocalAlpineEcology;
        [ProtoMember(76)] public bool PhysicalRockExposure;
        /// <summary>The vertical profile a new world was made with ("budget/curve/detail"; null for the legacy profile): a record
        /// for the log and the self-test, read by no rule.</summary>
        [ProtoMember(77)] public string TerrainProfile;
        /// <summary>Version of the vanilla land animals' natural ranges (Core.VanillaFaunaRanges, decision 0031); 0: none.</summary>
        [ProtoMember(78)] public int VanillaFaunaRanges;
        /// <summary>Version of CHELSA's rainfall on the 2' grid (rain.bin, decision 0031); 0: the 0.25° climate grid's.</summary>
        [ProtoMember(79)] public int FineRain;
        /// <summary>Summit terrain rules: 0 none, 1 the 30" residual grid (0031), 2 surveyed local summit caps (0032).</summary>
        [ProtoMember(80)] public int Summits;
        /// <summary>Version of the mountain glaciers (glaciers.bin from RGI 6, decision 0031); 0: none.</summary>
        [ProtoMember(81)] public int Glaciers;
        /// <summary>Decision 0032: physical-slope alpine cover (Core.MountainEcology.SurfaceVersion); 0 keeps pre.8 surfaces, 1 the
        /// scree-heavy alpine belt of 0032, 2 closed alpine turf to about 6 °C, thinner towards the snow line, in dry
        /// mountains and on the last ice sheets' ground, in natural patches.</summary>
        [ProtoMember(82)] public int AlpineSurfaceRules;
        [ProtoMember(83)] public string PeakCatalogSignature;
        [ProtoMember(84)] public int AridRiverRules;
        [ProtoMember(85)] public int StraitRules;
        /// <summary>Reservoir exclusions (EarthData.SetReservoirExclusions): ReservoirRulesVersion for new worlds; 0 in older saves.</summary>
        [ProtoMember(86)] public int ReservoirRules;
        public const int ReservoirRulesVersion = 1;
        /// <summary>0036: 1 freezes creation choices. Missing (0) retains older config-on-restart behaviour.</summary>
        [ProtoMember(87)] public int CreationOptionsVersion;
        [ProtoMember(88)] public string FaunaRangeProfile;
        /// <summary>0039: surveyed summit correction minimum in metres; -1 off, 0 retains all peaks in older saves.
        /// New worlds freeze the Earth tab's choice (default 3000). The 30" terrain residual is independent.</summary>
        [ProtoMember(89)] public int SummitMinElevation;
        /// <summary>0046: the Earth tab's "River rapids" set to off (earthRiverRapids): no rapids reaches in pool layout 11 and
        /// newer. Missing (false) keeps rapids reaches.</summary>
        [ProtoMember(90)] public bool NoRiverRapids;
        /// <summary>0047: placement catalogue rules (Core.PlacementCatalogue.RulesVersion): plants and animals where they
        /// lived around 1318. 0: vanilla placement by climate (older saves, or the world setting "vanilla").</summary>
        [ProtoMember(91)] public int PlacementRules;
        /// <summary>0048: soil fertility floor in vanilla's block layers (Core.SoilFertility.RulesVersion): land that isn't
        /// arid by the real aridity index has soil, not sand or gravel. 0: vanilla fertility (older saves). 2: ground columns
        /// leave arid land without topsoil, river belts excepted. 3: arid land in a river's natural flood (FloodplainGrid)
        /// keeps its topsoil too.</summary>
        [ProtoMember(92)] public int SoilRules;
        /// <summary>Ground columns (Core.GroundColumns.RulesVersion): soil, Quaternary cover and bedrock in metres from the
        /// ground tiles, mapped to blocks by the depth curve below. New worlds take the Earth tab's "Rocks and ground" (ground
        /// columns by default; earth.json's GroundColumnsPrototype only decides for a world made without that option). 0: off
        /// (every save made before it, or another rocks choice). Needs regional geology.</summary>
        [ProtoMember(190)] public int GroundColumns;
        /// <summary>Ground depth (Core.GroundColumns.DepthCurve.Choices): metres to blocks below the surface, starting at the
        /// terrain's own scale. Null (saves without it, including the first ground-column test worlds): the default, "log4000".</summary>
        [ProtoMember(191)] public string GroundDepth;
        /// <summary>Which ground sets a ground-columns world reads (Core.GroundDownloader, global-design section 9): 2 = the 30″
        /// sets of release egts-v1, fetched before the world's first chunk; 1 = the ground pack's 2′ sets only (the 30″ sets
        /// could not be had when the world was made, and are never read in it later, so its cover stays one kind). 0: worlds
        /// made before this (they read whatever 30″ sets the machine has, as before). 192: next to the other ground fields.</summary>
        [ProtoMember(192)] public int GroundData;
        /// <summary>0060: ores, gems and deposits in their ground layer whatever the ore choice (Core.OreLayers.RulesVersion):
        /// the layer pass after the deposits, the ore maps' layer gate and Earth's layer deposits, in ground-column worlds.
        /// 0: off (every world made before it keeps its generation for new chunks: no layer pass, ore-map rules with "fill any
        /// rock", vanilla bauxite strata in the cover). 1: ore-layers-v1.json; 2 (EA-0059): ore-layers-v2.json, where fire clay
        /// also stays as the underclay of a kept coal block. 193: next to the ground fields.</summary>
        [ProtoMember(193)] public int OreLayers;
        /// <summary>0059: Earth's water table rules (Core.WaterTable.RulesVersion, water-table-v1.json) for what generation
        /// writes: with Hydrate or Diedrate installed, its per-chunk aquiferData for each new chunk column, from the water
        /// table. 0: worlds made before it, where HoD keeps making its own aquifers (the query and /earth water still answer,
        /// as they save nothing). 193 is OreLayers (0060).</summary>
        [ProtoMember(194)] public int WaterTableRules;

        /// <summary>
        /// Whether Earth writes Hydrate or Diedrate's aquifer data in this world (decision 0059; the author, 15 November 2026:
        /// "i am ok for own rock and ground generation. When vanilla settings are selected the goes as vanilla and HoD
        /// defaults"): the water table (rules 1) with Earth's own rock and ground (regional rock, with or without ground
        /// columns) and Earth's biomes. With vanilla rock and ground, or biomes back to vanilla (0075), Earth writes nothing and
        /// changes nothing of HoD's, which makes its own aquifers with its own defaults; the water table still serves Earth's own
        /// rules there (seeps, the site drivers, /earth water). From the saved fields alone; every 2.0 world is new.
        /// </summary>
        public bool WritesHodAquifers() => WaterTableRules >= 1 && RegionalGeology && VanillaBiomes < 1;
        /// <summary>0065: ore rules for "Earth-like ores" (Core.OreBackground.CatalogueRules): 4 = the deposit catalogue's bodies at their
        /// real size, ore-heatmaps-v2.json (v1 when v2 is missing) for the uncatalogued backgrounds, no rules v3 ore bodies,
        /// every background at its class floor in its host rock whether or not a record is near, background sizes by the
        /// world's scale. Set for new ground-column worlds with the ore maps. 0: every world made before it (rules v3 with 0060, and
        /// ore-heatmaps-v1.json, exactly as before). 195: next free after 194 on every branch (checked 8 November 2026).</summary>
        [ProtoMember(195)] public int OreRules;
        /// <summary>The Earth tab's rock floor (earthRockFloor): this many blocks below each chunk column's lowest surface the
        /// ground is plain rock without caves, ores or lava, so tall worlds don't store deep mixed chunks. 0: none (older saves).</summary>
        [ProtoMember(93)] public int RockFloorDepth;
        // 94: RiverValleyRules (claude/terrain-walls). Don't reuse it.
        /// <summary>0051: dry land's heights from Copernicus GLO-90 tiles downloaded on demand (Core.FineElevation.Version:
        /// 1 heights only, 2 with listed summits lifted to the survey, 3 and a smooth curve between GLO-90's points without noise, 4 and the mean over a block's
        /// footprint at 75 m and more per block); 0: the built-in 2′ grid (older saves, or the Earth tab's "Built-in"). The squares that fell back to built-in heights are saved apart (EarthElevationSystem.FallbackKey).</summary>
        [ProtoMember(95)] public int FineElevation;
        /// <summary>0053: dry land's heights from GLO-90 averaged to a coarser grid that ships in Earth's height data
        /// packs (Core.CoarseElevation.Version), with a detail noise sized by GLO-90's own relief inside each cell; with
        /// FineElevation as well it is the fallback for squares that can't be downloaded. 0: none (older saves, "Built-in").</summary>
        [ProtoMember(96)] public int CoarseElevation;
        /// <summary>0053: rivers on GLO-90 (Core.RiverNetwork.Glo90CoursesVersion): courses snapped to GLO-90's valley
        /// floors and levels from GLO-90 (earthdata/rivers-glo90.bin), not straightened for the world's size, with banks and
        /// valleys sized in metres by the land heights' own grid. 2 (item 14, tall.14): with the reaches the water atlas moved
        /// into the water mask's rivers (rivers-glo90-snap.bin). 3 (item 16, tall.16): and the side arms round the islands the
        /// mask shows in them (rivers-glo90-arms.bin). 4 (0063): every reach from 20 m³/s moved onto the mask's water and
        /// smoothed, the mask's half width at each vertex (RiverReach.Width), arms rebuilt on them (rivers-glo90-snap-2.bin,
        /// rivers-glo90-arms-2.bin). 0: rivers.bin's courses and levels (older saves).</summary>
        [ProtoMember(97)] public int RiverCourses;
        /// <summary>0053: the detail noise's rules (TerrainModel.DetailRulesVersion). 1: wavelengths in metres below the
        /// land data's own cell, amplitude from the data's relief (smaller), smooth between cells, rotated octaves. 0: the
        /// first noise, fixed in blocks (older saves).</summary>
        [ProtoMember(98)] public int DetailRules;
        /// <summary>0054: the Earth tab's "Chickens everywhere" turned off (earthChickensEverywhere): chickens keep their range
        /// (1318 or the junglefowl's). Missing (false, older saves and the default) lets them spawn wherever the climate suits
        /// them until feather birds cover every region.</summary>
        [ProtoMember(99)] public bool ChickensByRange;
        /// <summary>Lake outlets (outlets.bin, OutletData.FileVersion): the lakes' tongues past their outlet down the outflow
        /// river are land and the river runs through them (Lake Erie's upper Niagara). 0 in older saves: the lakes as before.</summary>
        [ProtoMember(100)] public int LakeOutlets;
        /// <summary>0053 item 14 (tall.14): the sea refereed by GLO-90's water mask (sea-referee.bin, SeaRefereeData.FileVersion):
        /// glacier fjords GSHHG draws as sea are land, shifted outlines nearby moved (release/1.0's water atlas); 2 (item 15,
        /// tall.15) makes the coastline the mask's wherever GSHHG's differs from it. 0 in older saves: terrain.bin, islands.bin and
        /// coasts.bin as published. 1 was saved only by tall.14's test worlds; they read 2's data (logged).</summary>
        [ProtoMember(101)] public int SeaReferee;
        /// <summary>0053 item 17: the lakes of GLO-90's water mask from their outlines (Core.LakeOutlineData.FileVersion,
        /// lakes-glo90-1.bin, which comes with the GLO-90 heights): wherever this world reads GLO-90, every lake of the mask at
        /// its own outline and GLO-90 level stands in for the grid's fresh lakes (Earth's salt and terminal lakes stay the
        /// grid's). New worlds on full GLO-90 heights only. A world whose file couldn't be had when it first needed it keeps
        /// the grid's lakes for good (EarthElevationSystem.LakeOutlinesOffKey). 0: the grid's lakes (older saves).</summary>
        [ProtoMember(102)] public int LakeOutlines;
        /// <summary>0053 item 18: the rivers of GLO-90's water mask from their outlines (Core.RiverOutlineData.FileVersion,
        /// rivers-outline-glo90-1.bin, which comes with the GLO-90 heights): wherever this world reads GLO-90, dry land in them
        /// is river water at the level of the nearest river course, and a course whose centre line lies in them draws no
        /// channel of its own there (TerrainModel.ApplyOutlineRiver). New worlds on full GLO-90 heights only. A world whose
        /// file couldn't be had when it first needed it keeps the courses' channels for good
        /// (EarthElevationSystem.RiverOutlinesOffKey). 2: rivers-outline-glo90-2.bin, the rivers through the old reservoirs of
        /// item 19 (DamRules) and across the bridges the mask has as land; a world that keeps the dams for good
        /// (EarthElevationSystem.DamsOffKey) reads 1's. 0: the courses' channels (older saves).</summary>
        [ProtoMember(103)] public int RiverOutlines;
        /// <summary>Geothermal rules (Core.Geothermal.RulesVersion, decision 0057): the climate map's geologic activity is heat
        /// under the GVP's Holocene volcanoes (0 elsewhere, never enough for vanilla's random hot springs), not vanilla's
        /// noise; the hot springs are the known ones (OpenStreetMap, GeoNames), each placed where its ground allows; vanilla's
        /// lava springs are off. 0: vanilla's activity, lava springs and hot springs (older saves).</summary>
        [ProtoMember(104)] public int GeothermalRules;
        /// <summary>0053 item 19: modern dams taken out of GLO-90 (Core.DamRemovalData.RulesVersion, dams-glo90-1.bin,
        /// which comes with the GLO-90 heights): dam walls lowered, a valley floor under each left-out reservoir and the river
        /// courses falling evenly through it instead of stepping at the dam; new worlds on full GLO-90 with courses rules 3
        /// get it, with RiverOutlines 2. A world whose file couldn't be had (or didn't fit its courses) when it first needed
        /// it keeps the dams for good (EarthElevationSystem.DamsOffKey). 2 (0063 rule 3, dams-glo90-2.bin keyed to courses rules
        /// 4's list; new worlds on courses rules 4 get it, EarthWorldOptions.Freeze): the corridor keeps the river's full width
        /// across a wall and the tailrace, no level above the ground beside it. 0: GLO-90 and the courses as they are (older saves).</summary>
        [ProtoMember(105)] public int DamRules;
        /// <summary>Not saved: course stations BuildModel lowered by the dam file, -1 when it refused it
        /// (<see cref="DamCourseError"/>).</summary>
        public int DamCourseLevels;
        public string DamCourseError;
        /// <summary>Decision 0064: coasts and waters as of 1318 (Core.Works1318Data.RulesVersion, earthdata/works-1318-2.bin): where
        /// people changed them after 1318 the sea, lakes and land of 1318 come back (step 1: the Netherlands, from the RCE
        /// maps of 1250, 1500 and 2000 AD: the Zuiderzee and the IJ over the polders, the drained lakes, land over the
        /// peat-digging lakes), and natural change after 1318 stays. Only where the coasts come from their outlines (Coasts).
        /// 0: today's coasts and waters (older saves). 106 was free on every branch on 8 November 2026.</summary>
        [ProtoMember(106)] public int Works1318;
        /// <summary>Decision 0069 rule 8 (Core.NonPerennialData.RulesVersion, earthdata/rivers-nonperennial-2.bin): the arid
        /// rule dries only rivers GIRES v1.0 has as stopping 30 days a year or more (each reach's wet-source flow is none or
        /// all of its flow), not by the rainfall of its catchment. 0: the rainfall estimate (older saves). 196 was free on
        /// every branch on 12 November 2026.</summary>
        [ProtoMember(196)] public int PerennialRules;
        /// <summary>Decision 0069 rules 1-4 (Core.RiverFlowData.FileVersion, rivers-flow-glo90-1.bin in the GLO-90 folder): the
        /// water mask's river water takes its flow, still water and depth from the flow solved on it, and the mouth reaches
        /// that went to the sea across land are rerouted through their own water. Only with river outlines 4, whose water
        /// it was solved on. 0: the courses' blended flow (older saves). 197 was free on every branch on 12 November 2026.</summary>
        [ProtoMember(197)] public int FlowRules;
        /// <summary>Decision 0069 rule 7 (Core.ShoreZones.RulesVersion): no trees or shrubs on beaches, the open belts of
        /// low sea coasts, rivers' low banks and lakes' sedge shores, and no tall grass on beaches. 0: the forest to the
        /// water (older saves). 198 was free on every branch on 12 November 2026.</summary>
        [ProtoMember(198)] public int ShoreZoneRules;
        /// <summary>Decision 0070 (Core.ShoreMaterial.RulesVersion): the sea's and big lakes' shores are a beach strip of real
        /// width at any height, of the coast's own material (OSM's beaches, shores-osm-1.bin, else a rule from the ground).
        /// 0: a beach only below 3 m, all of it beach sand (older saves). 2: the beach at the water's level, the shallows of
        /// the shore's material. 3 (EA-0056): thin glacial till at the shore is bare rock where OSM maps no beach.
        /// 199 was free on every branch on 12 November 2026.</summary>
        [ProtoMember(199)] public int ShoreRules;
        /// <summary>Generation fixes from the code review of 11 November 2026 (TerrainModel.GenFixRules, GenFixRulesVersion):
        /// 1 = dry land below 0 m without a ledge on the 0 m contour, springs planned at whole blocks, crops and fruit trees in
        /// a river's green belt judged by its rain, dry river beds' gravel scattered; 2 = also the noise's dips near a water
        /// level faded in over 20-40 m above it on the grid's land (EA-0040); 3 = also a grid lake's water ending along an
        /// irregular line on low ground beside it, not a straight one between cell centres (EA-0072); 4 = also, from the code
        /// review of 14 November 2026, a crater-less volcano conduit capped at its column's top (ER-19) and the rock under a
        /// ground column read to its bottom (ER-20). 0: as before (older saves). 200 was free on every branch on 12 November
        /// 2026.</summary>
        [ProtoMember(200)] public int GenFixRules;
        /// <summary>The weather rules this world's weather follows (WeatherSettings.Rules, frozen at creation): 6 = the cold
        /// sea's low cloud decks, hail from Earth's thunderstorms, and the weather's warm and cold spells, clouds and rain in
        /// the temperature (EA-0019, EA-0023, EA-0025; new worlds from 13 November 2026); 7 = also the ground under the weather
        /// (the rain rate at the ground's temperature, the desert coasts as dry as climate fix 4) and more thunder in hot dry
        /// months (EA-0021, EA-0060, EA-0022; new worlds from 14 November 2026); 8 = also the rain in events as long as at
        /// the airports, through the whole day and skewed in warm months, and the temperature anomaly kept per 32-block cell
        /// and ten-minute step (EA-0020, EA-0073; new worlds from 14 November 2026, later). 0: worlds made before, which keep
        /// rules 5. Not <see cref="WeatherRules"/>, the server build's newest rules that a client must share. 201 was free on
        /// every branch on 13 November 2026.</summary>
        [ProtoMember(201)] public int WeatherModelRules;
        /// <summary>The Earth tab's "Biomes" (earthBiomes, EA-0061, decision 0075): 1 = vanilla biomes, chosen at creation. The
        /// biome rules are saved as 0 then (zones, fire, wetlands, ground cover, ground moisture, grass in winter, alpine
        /// surfaces), and this field also keeps what has no rules number of its own as vanilla's: the forest and shrub maps
        /// stay vanilla's noise (no scaling by Earth's forest potential) and the surface pass draws no alpine turf, fell-field
        /// or scree. 0: Earth's biomes (the default, and every world made before). 202 was free on every branch on 13 November 2026.</summary>
        [ProtoMember(202)] public int VanillaBiomes;
        public const int VanillaBiomesVersion = 1;
        /// <summary>EA-0068, decision 0077: Earth's crafting rules (<see cref="EarthCrafting"/>). 1 = Earth's extra grid recipes
        /// for its geology (grog, fire clay from clay with grog or quartz, a fireclay refractory brick without bauxite, kyanite,
        /// sillimanite, andalusite and corundum in bauxite's place), set at creation where Earth decides where bauxite lies:
        /// ground rules 2 and later, or Earth's ore maps. 0: vanilla's recipes only (every world made before, and vanilla
        /// rock with vanilla ores). 203 was free on every branch on 13 November 2026.</summary>
        [ProtoMember(203)] public int CraftingRules;
        /// <summary>The newest crafting rules (EarthCrafting.RulesVersion is this): here so that this record, which the offline
        /// tests and tools compile without the game's types, can name it in RulesAgainstBuild.</summary>
        public const int CraftingRulesVersion = 1;
        /// <summary>EA-0054 step 2, decision 0080: site rules (<see cref="SiteNiche"/>). 1 = trees and shrubs by each column's
        /// own site: the EIVE soil fit reads the column's wetness from the water table, its richness and pH from the ground
        /// column (instead of the 2' soil grid's shares), and a tree's size follows the site's productivity. 0: as before
        /// (every world made before, and worlds with vanilla biomes). 204 was free on every branch on 14 November 2026.</summary>
        [ProtoMember(204)] public int SiteRules;
        /// <summary>Record only (ER-05, code review 14 November 2026): the Earth version (modinfo) that created this world. Null in
        /// worlds made before. Read by nothing that generates; left out of the model cache's key.</summary>
        [ProtoMember(205)] public string CreatedWith;
        /// <summary>Record only (ER-05): the Earth version that last started this world (set at every start). With
        /// <see cref="RulesNewerThanBuild"/> it names the version an older build's refusal asks for. Null in worlds made before.</summary>
        [ProtoMember(206)] public string LastSavedWith;
        /// <summary>(EA-0118, the Fable pass) What the server's weather ground is (EarthData.WeatherGroundSignature), for
        /// worlds whose weather rules read it (7 and later), set at every start like <see cref="WeatherDataSignature"/>: a
        /// client with another keeps vanilla weather. Null from servers before it, which the clients then don't check. 207
        /// and 208 are taken on other branches (14 November 2026).</summary>
        [ProtoMember(209)] public string WeatherGroundSignature;
        /// <summary>(EA-0118) <see cref="WeatherGroundSignature"/> of a server whose rules read the ground and that has none.</summary>
        public const string NoWeatherGround = "none";
        /// <summary>(EA-0144, decision 0086) The live temperature's rules. 1 = the monthly temperature layer: each place's
        /// own monthly means through a mean-preserving curve, its own day-night range on a day shaped by the sun, spells sized
        /// by its own day-to-day spread, and vanilla's date-only noise taken out (<see cref="Core.SeasonCurve"/>). 0 = vanilla's
        /// season shape stretched to the place's coldest and warmest month (every world made before, and new worlds where the
        /// layer isn't there). The live temperature only: world generation never reads it. 210 was free on every branch on
        /// 16 November 2026 (207 and 208 on claude/landuse-1318, 211 kept for the data side).</summary>
        [ProtoMember(210)] public int SeasonRules;
        public const int SeasonRulesVersion = 1;
        /// <summary>(Decision 0087) The temperatures as around 1318: 1 = CHELSA's temperatures read from temperature-1318.bin
        /// (each month moved by how much colder 1300-1330 was than 1981-2010: the Last Millennium Reanalysis' level, GISTEMP's
        /// bridge, five CMIP6 past1000 models' split over the months), so every climate reading (world generation's climate
        /// map, Earth's zones, tree lines, soil and ecology, the live temperature) is as around 1318; the monthly layer's
        /// means carry the same shift. 0 = the modern normals (every world made before). 211 is kept for the data side, so
        /// 212 (free on every branch on 16 November 2026).</summary>
        [ProtoMember(212)] public int Climate1318;
        public const int Climate1318Version = 1;

        /// <summary>
        /// ER-05 (code review 14 November 2026): every saved rules number with the newest this build makes and reads, for the
        /// refusal of a world saved by a newer Earth (EarthMapSystem.InitWorld): opened by an older build, such a world made the
        /// older build's chunks for good, some fields clamped, others passed raw into tests. The newest values are the constants
        /// a new world's creation (EarthWorldOptions.Freeze, EarthMapSystem.ComputeParams) and the one-shots write, or an
        /// option's highest code. A new rules field goes in here: EarthWorldOptionsTests fails for an int field of this record
        /// that is neither here nor in its list of plain numbers.
        /// </summary>
        public IEnumerable<(string Name, int Saved, int Newest)> RulesAgainstBuild()
        {
            yield return (nameof(Version), Version, CurrentVersion);
            yield return (nameof(TemperatureRangeFrozen), TemperatureRangeFrozen, 1);
            yield return (nameof(TreeRulesVersion), TreeRulesVersion, RegionalTreeCatalog.RulesVersion);
            yield return (nameof(GeologyRulesVersion), GeologyRulesVersion, RegionalGeologyCatalog.RulesVersion);
            yield return (nameof(FaunaRulesVersion), FaunaRulesVersion, RegionalFaunaCatalog.RulesVersion);
            yield return (nameof(OreRulesVersion), OreRulesVersion, RegionalOreCatalog.Version);
            yield return (nameof(RiverFalls), RiverFalls, Core.RiverFalls.Version);
            yield return (nameof(RiverChannels), RiverChannels, SeaChannels.Version);
            yield return (nameof(Islands), Islands, IslandLayer.RulesVersion);
            yield return (nameof(RiverEnds), RiverEnds, Core.RiverEnds.Version);
            yield return (nameof(ClimateRules), ClimateRules, System.Math.Max(TerrainModel.RealRainRules, TerrainModel.RealLapseRules));
            yield return (nameof(LakeShores), LakeShores, ShoreData.FileVersion);
            yield return (nameof(Coasts), Coasts, CoastData.FileVersion);
            yield return (nameof(RiverDeltas), RiverDeltas, Core.RiverDeltas.Version);
            yield return (nameof(RiverWidths), RiverWidths, RiverNetwork.WidthRulesVersion);
            yield return (nameof(GutterSeabed), GutterSeabed, TerrainModel.RealSeamVersion);
            yield return (nameof(ClimateData), ClimateData, EarthData.ClimateFixVersion);
            yield return (nameof(RiverPoolLayout), RiverPoolLayout, RiverNetwork.PoolLayoutVersion);
            yield return (nameof(VanillaFaunaRanges), VanillaFaunaRanges, Core.VanillaFaunaRanges.Version);
            yield return (nameof(FineRain), FineRain, 1);
            yield return (nameof(Summits), Summits, KnownSummits.Version);
            yield return (nameof(Glaciers), Glaciers, 1);
            yield return (nameof(AlpineSurfaceRules), AlpineSurfaceRules, MountainEcology.SurfaceVersion);
            yield return (nameof(AridRiverRules), AridRiverRules, AridHydrology.Version);
            yield return (nameof(StraitRules), StraitRules, NavigableStraits.Version);
            yield return (nameof(ReservoirRules), ReservoirRules, ReservoirRulesVersion);
            yield return (nameof(CreationOptionsVersion), CreationOptionsVersion, 1);
            yield return (nameof(PlacementRules), PlacementRules, PlacementCatalogue.RulesVersion);
            yield return (nameof(SoilRules), SoilRules, SoilFertility.RulesVersion);
            yield return (nameof(GroundColumns), GroundColumns, Core.GroundColumns.RulesVersion);
            yield return (nameof(GroundData), GroundData, GroundDownloader.SetsVersion);
            yield return (nameof(OreLayers), OreLayers, Core.OreLayers.RulesVersion);
            yield return (nameof(WaterTableRules), WaterTableRules, WaterTable.RulesVersion);
            yield return (nameof(OreRules), OreRules, OreBackground.CurrentRules);
            yield return (nameof(FineElevation), FineElevation, Core.FineElevation.Version);
            yield return (nameof(CoarseElevation), CoarseElevation, Core.CoarseElevation.Version);
            yield return (nameof(RiverCourses), RiverCourses, System.Math.Max(RiverNetwork.Glo90WidthCourses, DamRemovalData.CourseRules2));
            yield return (nameof(DetailRules), DetailRules, TerrainModel.DetailRulesVersion);
            yield return (nameof(LakeOutlets), LakeOutlets, OutletData.FileVersion);
            yield return (nameof(SeaReferee), SeaReferee, SeaRefereeData.FileVersion);
            yield return (nameof(LakeOutlines), LakeOutlines, LakeOutlineData.FileVersion);
            yield return (nameof(RiverOutlines), RiverOutlines, RiverOutlineData.FileVersion);
            yield return (nameof(GeothermalRules), GeothermalRules, Geothermal.RulesVersion);
            yield return (nameof(DamRules), DamRules, DamRemovalData.NewestRules);
            yield return (nameof(Works1318), Works1318, Works1318Data.RulesVersion);
            yield return (nameof(PerennialRules), PerennialRules, NonPerennialData.RulesVersion);
            yield return (nameof(FlowRules), FlowRules, RiverFlowData.FileVersion);
            yield return (nameof(ShoreZoneRules), ShoreZoneRules, Core.ShoreZones.RulesVersion);
            yield return (nameof(ShoreRules), ShoreRules, Core.ShoreMaterial.RulesVersion);
            yield return (nameof(GenFixRules), GenFixRules, TerrainModel.GenFixRulesVersion);
            yield return (nameof(WeatherModelRules), WeatherModelRules, WeatherSettings.Newest);
            yield return (nameof(VanillaBiomes), VanillaBiomes, VanillaBiomesVersion);
            yield return (nameof(CraftingRules), CraftingRules, CraftingRulesVersion);
            yield return (nameof(SiteRules), SiteRules, SiteNiche.RulesVersion);
            yield return (nameof(SeasonRules), SeasonRules, SeasonRulesVersion);
            yield return (nameof(Climate1318), Climate1318, Climate1318Version);
            yield return (nameof(DepositSet), DepositSet, EarthDepositSet.Version);
            yield return (nameof(GroundCoverRules), GroundCoverRules, GroundCover.RulesVersion);
            yield return (nameof(ZoneRules), ZoneRules, ClimateZones.RulesVersion);
            yield return (nameof(MoistureRules), MoistureRules, GroundMoisture.RulesVersion);
            yield return (nameof(GrassWinterRules), GrassWinterRules, GrassWinter.RulesVersion);
            yield return (nameof(FireRules), FireRules, FireRegime.RulesVersion);
            yield return (nameof(WetlandRules), WetlandRules, WetlandGrid.RulesVersion);
            yield return (nameof(PondRules), PondRules, System.Math.Max(PondRulesWetlands, PondRulesNone));
            yield return (nameof(AridityRules), AridityRules, EarthData.PenmanAridityVersion);
            yield return (nameof(ChannelRules), ChannelRules, TerrainModel.ChannelRulesVersion);
            yield return (nameof(SpringRules), SpringRules, System.Math.Max(SpringRulesCaves, SpringRulesStreams));
        }

        /// <summary>The int fields of this record that are plain numbers, not rules (<see cref="RulesAgainstBuild"/> lists the
        /// rest): the map's size and sea, the spawn, settings, and the weather's per-start handshake.</summary>
        public static readonly string[] PlainNumbers = { nameof(MapHeight), nameof(SeaLevel), nameof(GutterSeabedY), nameof(SpawnX), nameof(SpawnZ),
            nameof(WeatherRules), nameof(SummitMinElevation), nameof(RockFloorDepth), nameof(MinSeaDepth) };

        /// <summary>The rules this world saved above this build's newest (empty for a world of this build or an older one).</summary>
        public List<string> RulesNewerThanBuild() => RulesAgainstBuild().Where(r => r.Saved > r.Newest)
            .Select(r => r.Name + " " + r.Saved + " (this build: " + r.Newest + ")").ToList();

        /// <summary>The weather rules this world's model runs: its own, at most this build's; 5 for worlds made before they
        /// were saved.</summary>
        public int WeatherModelRulesOrOld => WeatherModelRules > 0 ? System.Math.Min(WeatherModelRules, WeatherSettings.Newest) : 5;
        /// <summary>Not saved: the mouth reaches BuildModel rerouted from the flow file, -1 when its course list wasn't this
        /// world's (none rerouted).</summary>
        public int FlowReroutes;
        /// <summary>Earth's own ore and gem deposits (Core.EarthDepositSet: earth-ores.json and earth-gems.json in
        /// earth:worldgen/deposits) in the deposit list, under either ore choice. 0: left out, so worlds made before them
        /// (and non-Earth worlds) keep vanilla's per-chunk deposit stream; RC and dev worlds made since 7 November 2026 are
        /// recognised by their newer fields (EarthDepositSet.Of). 107 was reserved for it on 8 November 2026.</summary>
        [ProtoMember(107)] public int DepositSet;
        /// <summary>Ranges proposal step 5a: dry land keeps a share of vanilla's tall grass by the real aridity index
        /// (Core.GroundCover.RulesVersion, ground-cover.json). 0: vanilla's density (older saves).</summary>
        [ProtoMember(120)] public int GroundCoverRules;
        /// <summary>Habitats step 1: vegetation zones from climate (Core.ClimateZones.RulesVersion, zones.json) set the forest
        /// and shrub density. 2: trees as dense as the forest map says, without vanilla's floor of a tree every 7 to 8 chunks
        /// (Core.ClimateZones.TreeKeep). 0: the single rain curve (older saves).</summary>
        [ProtoMember(121)] public int ZoneRules;
        /// <summary>Ranges step 5c: dry land's tall grass grows after rain and withers after (Core.GroundMoisture.RulesVersion,
        /// EarthGroundMoisture). 0: grass as generated (older saves).</summary>
        [ProtoMember(122)] public int MoistureRules;
        /// <summary>Tall grass through the winter: flattened to short dead tufts when its snow melts, grown back stage by stage
        /// once the month's mean at the ground reaches 5 °C (Core.GrassWinter.RulesVersion, EarthGrassWinter). 0: vanilla's
        /// grass, the same height under and after the snow (older saves).</summary>
        [ProtoMember(123)] public int GrassWinterRules;
        /// <summary>Grassland kept open by fire: the prairie, aspen parkland, Pampas and forest-steppe lose most of the zones'
        /// forest (Core.FireRegime.RulesVersion; needs ZoneRules). 1: open land keeps a fifth of the forest; 2: the surer the
        /// grassland, the less, down to a fiftieth on the core steppe, Pampas and tallgrass prairie (Core.FireRegime.ForestCutFor).
        /// 0: the zones' forest as it is (older saves).</summary>
        [ProtoMember(124)] public int FireRules;
        /// <summary>Open wetland without trees: bog, flooded marsh and saltmarsh from GLWD v2 take their share of the zones'
        /// forest (Core.WetlandGrid.RulesVersion; needs ZoneRules). 0: the zones' forest over wetlands too (older saves).</summary>
        [ProtoMember(125)] public int WetlandRules;
        /// <summary>Vanilla's surface ponds (the Earth tab's "Surface ponds", earthSurfacePonds; ponds in caves stay in every
        /// case). PondRulesNone ("off", the tab's default; the author, 8 November 2026: "in Earth setting UI make default off"):
        /// none. PondRulesWetlands ("wetlands"; the author, 8 November: "Wetlands only"): only in the mapped wetlands (GLWD v2, by
        /// their share; Core.WetlandGrid.PondShare). 0: vanilla's ponds wherever Earth's other guards allow them (older saves,
        /// or "vanilla").</summary>
        [ProtoMember(126)] public int PondRules;
        public const int PondRulesWetlands = 1, PondRulesNone = 2;
        /// <summary>The aridity index's evapotranspiration (precipitation / potential evapotranspiration, the index the zones,
        /// fire, soil, tall grass, water table and tree soil niche read): 1 = CHELSA's Penman-Monteith (earthdata/aridity.bin,
        /// EarthData.SetPenmanAridity), whose index the zone limits and the fire rule are fitted to from version 1; 0 = the
        /// Holdridge estimate behind the rainfall, which reads 15 to 45 % wet in sunny, windy and continental climates (older
        /// saves). The rainfall vanilla and Earth's catalogues read is the same in both.</summary>
        // (128, not 127: 127 is ChannelRules, from the tall line)
        [ProtoMember(128)] public int AridityRules;

        /// <summary>0063: the river channel rules on full GLO-90 ("dredging"): 1 draws the course's channel everywhere along it
        /// at the flow's width, narrowed where the river is measured narrower than the real width usual for its flow by the
        /// same ratio (down to half, deepened to keep the flow's cross-section), joins wide outline water to it, leaves narrow
        /// outline pieces to the courses where streams go down to 20 m³/s, lets a mask lake in the channel give way, keeps
        /// mask rivers wet, draws no outline water nor the grid's lakes along the Suez and Panama canals, takes the depth
        /// and flow of the water from every course near, and smooths the courses' eight-direction legs along the way, held to
        /// the measured width (scales finer than about 1:50). 2 (0069): the channel as wide as the mask measured it. 3 (decision
        /// 0071): one bed where a river meets the sea (TerrainModel.ApplyMouth) and one smooth depth across the mask's river
        /// water (TerrainModel.MaskWaterDepth). 4 (decision 0073): mask-widened courses reach as far as their water, the depth
        /// blended over every course near, narrow side channels the flow passes through kept, small pools in a river joined
        /// to it, still or flowing water reach by reach (TerrainModel.FlowChannelRules). 5 (decision 0074): one width along a
        /// river: the measured widths averaged along each reach, and the mask's ponds along rivers a few of its pixels wide
        /// (widenings shorter than five widths) made land (RiverNetwork.PondChannelRules). 0: the outlines cut and narrow the
        /// channel (older saves).</summary>
        [ProtoMember(127)] public int ChannelRules;

        /// <summary>Vanilla's springs and mountain-side streams (GenRivulets; the Earth tab's "Mountain springs and streams",
        /// earthMountainSprings; the author, 8 November 2026: rapids "spawn in random mountain locations"). 0: vanilla's
        /// (older saves, or "vanilla"); SpringRulesCaves: springs only in cave walls below the surface, no springs in open
        /// cliffs and no mountain-side streams (on Earth's heights every such stream is rapid water); SpringRulesStreams
        /// (the author, 8 November: "Instead of 'off' - better placement"): as caves, plus springs at the heads of the real
        /// perennial streams and seeps where the water table meets a slope (EarthSprings, Core.StreamSprings). 129, not 128:
        /// release/1.0 saves AridityRules at 128 (both claimed it on 8 November; only unreleased tall.24 test worlds saved
        /// springs at 128).</summary>
        [ProtoMember(129)] public int SpringRules;
        public const int SpringRulesCaves = 1, SpringRulesStreams = 2;

        /// <summary>Channel rules 1: whether the rivers' narrow outline pieces (RiverOutlineData.FlagNarrow) are left out but
        /// for the side arms and channels between wide waters off the courses (RiverOutlineData.Layer), because the courses
        /// draw those rivers: where the world draws every stream down to 20 m³/s.</summary>
        public bool DropsNarrowRiverPieces => ChannelRules >= 1 && StreamMode == "all" && (StreamMinDischarge > 0 ? StreamMinDischarge : 20) <= 20;

    }

    [ProtoContract]
    public class FlatZoneData
    {
        [ProtoMember(1)] public string Code;
        [ProtoMember(2)] public double X;
        [ProtoMember(3)] public double Z;
        [ProtoMember(4)] public double Radius;
        [ProtoMember(5)] public double Blend;
        [ProtoMember(6)] public int TargetTop;
        /// <summary>Half the footprint the zone follows (0.14.8); 0 in older saves, whose zones are discs.</summary>
        [ProtoMember(7)] public double HalfX;
        [ProtoMember(8)] public double HalfZ;

        public FlatZone ToZone() => new FlatZone { Code = Code, X = X, Z = Z, Radius = Radius, Blend = Blend, HalfX = HalfX, HalfZ = HalfZ, TargetTop = TargetTop };
    }
}
