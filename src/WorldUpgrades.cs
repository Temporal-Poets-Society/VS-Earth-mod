using System;
using System.Globalization;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// What WorldUpgrades needs of the game and of the world being started (EA-0088, D-R5 of the Fable pass): the game's is
    /// <see cref="GameUpgradeHost"/>, tests/EarthWorldOptionsTests has one of its own, so the one-shots run without the game.
    /// </summary>
    interface IUpgradeHost
    {
        /// <summary>The config this start read (EarthConfigStore.Load).</summary>
        EarthMapConfig Config { get; }
        /// <summary>The params a one-shot changed, written into the save's data.</summary>
        void Store(EarthParams p);
        /// <summary>The world's params written into the save's data (the rule fixes adopted without a flag).</summary>
        void SaveParams();
        /// <summary>The world's canals by its options (earthSuezCanal, earthPanamaCanal), as EarthWorldOptions.CanalCodes.</summary>
        string CanalCodes();
        /// <summary>The newest climate fix the loaded Earth data has (0: none).</summary>
        int ClimateFixAvailable { get; }
        /// <summary>The loaded summit catalogue's signature, null without one.</summary>
        string PeakCatalogSignature();
        /// <summary>The coast outlines a world with this geometry gets by the config (EarthMapSystem.CoastsFor).</summary>
        int CoastsFor(EarthGeometry geo, bool islands);
        /// <summary>Once the game runs (RunGame), after which a save event writes the world.</summary>
        void OnRunGame(Action handler);
        void AddSaveHandler(Action handler);
        void RemoveSaveHandler(Action handler);
        /// <summary>One-shot flags reset in earth.json as it is now (EarthConfigStore.ResetFlags).</summary>
        void ResetFlags(EarthMapConfig session, Action<EarthMapConfig> reset, string names, ILogger log);
    }

    /// <summary>
    /// The one-shot opt-ins of earth.json (UpgradeExistingWorld*): each applies to the Earth world being started, for the chunks
    /// generated from then on, and is reset in the config file once the world holding it is saved, so it happens once. Older
    /// chunks keep what they got, so a seam is possible where they meet; each one's log line says so. They run in three groups
    /// at fixed points of the world's start (EarthMapSystem.InitWorld), in the order of their tables, after the Earth data is
    /// loaded: the terrain layout's first, mountains and water before the model is built (both change what the model reads),
    /// the regional systems' after it, with the rule fixes that reach existing saves without a flag.
    ///
    /// Each one-shot is idempotent (it sets the world's rules to this build's, or to the config's choice), so one applied in a
    /// start whose save never reached the disk (the server stopped before the game ran, or crashed before its first save)
    /// simply runs again at the next start, its flag still set (Codex round 3 item 2, review A2).
    ///
    /// A world made in this start takes none of them (ER-07, code review 14 November 2026): its params were just frozen from
    /// the Earth tab, and a flag left set in earth.json for an older world rewrote them (ores "vanilla" on the tab became
    /// Earth's ore maps). The flags it skips are reset in earth.json once it is saved, as applied ones are (FP-09, the Fable
    /// pass): left set, its own second start applied them, or the first start after an update. A flag belongs to no world, so
    /// it is set again for the one it is meant for, while that one is being started.
    /// </summary>
    sealed class WorldUpgrades
    {
        delegate ref bool Flag(EarthMapConfig config);

        /// <summary>One one-shot: its config flag; what it does to the world's params, true when it changed them (it logs what it
        /// did); and, optionally, what is said about it when it isn't asked for.</summary>
        readonly record struct OneShot(Flag Asked, System.Func<EarthParams, bool> Apply, System.Action<EarthParams> NotAsked = null);

        readonly IUpgradeHost host;
        readonly ILogger log;
        /// <summary>The flags this start applied, or skipped for a world made in it, reset in earth.json at the world's first save
        /// once the game runs.</summary>
        readonly System.Collections.Generic.List<(string Name, Flag Asked)> applied = new();
        bool running, hooked;
        /// <summary>The world was made in this start (EarthMapSystem.InitWorld's fresh): no one-shot applies.</summary>
        readonly bool fresh;

        public WorldUpgrades(IUpgradeHost host, ILogger log, bool fresh = false)
        {
            this.host = host;
            this.log = log;
            this.fresh = fresh;
        }

        EarthMapConfig Config => host.Config;

        /// <summary>Whether earth.json asks for any one-shot (an UpgradeExistingWorld* flag set): it moves the world to this
        /// build's rules, which may read data its saved rules don't (EarthMapSystem.StoreFilesRead).</summary>
        public static bool AnyAsked(EarthMapConfig config)
            => config != null && typeof(EarthMapConfig).GetFields().Any(f => f.FieldType == typeof(bool)
                && f.Name.StartsWith("UpgradeExistingWorld", StringComparison.Ordinal) && (bool)f.GetValue(config));

        /// <summary>Each one-shot asked for, in order: applied and the params stored (in the save, in memory) when it changed
        /// them. Its flag goes off for this session at once and in earth.json once that save is written (<see cref="Saved"/>).
        /// A one-shot that throws keeps its flag, so it runs again at the next start once the cause is fixed.</summary>
        void Run(EarthParams p, params OneShot[] table)
        {
            var left = new System.Collections.Generic.List<string>();
            foreach (var u in table)
            {
                ref bool asked = ref u.Asked(Config);
                if (!asked) { if (!fresh) u.NotAsked?.Invoke(p); continue; }
                string name = u.Apply.Method.Name.Replace("Apply", "UpgradeExistingWorld");
                if (fresh) left.Add(name);
                else if (u.Apply(p)) Save(p);
                asked = false;
                applied.Add((name, u.Asked));
                Hook();
            }
            if (left.Count > 0)
                log.Warning("Earth: earth.json asks for {0}; a one-shot is for a world made before this start, so this world, made now with the Earth " +
                    "tab's choices, keeps them. The flags are reset in earth.json once this world is saved, so that none of its later starts applies " +
                    "them: set one again while the world it is meant for is the one being started.", string.Join(", ", left));
        }

        /// <summary>
        /// The game writes the save only once it runs: ServerSystemLoadAndSaveGame subscribes its writer at RunGame (1.22.7), and
        /// a server that stops before (WorldReady, the spawn chunks) saves nothing. So the flags are reset at the first save
        /// event after RunGame (an autosave, or the save on shutdown): the game raises it for the mods just before it writes
        /// the world from the save data that holds the upgrade.
        /// </summary>
        void Hook()
        {
            if (hooked) return;
            hooked = true;
            host.OnRunGame(() => running = true);
            host.AddSaveHandler(Saved);
        }

        void Saved()
        {
            if (!running || applied.Count == 0) return;
            var done = applied.ToArray();
            applied.Clear();
            host.RemoveSaveHandler(Saved);
            host.ResetFlags(Config, c => { foreach (var (_, asked) in done) asked(c) = false; },
                string.Join(", ", System.Linq.Enumerable.Select(done, d => d.Name)), log);
        }

        void Save(EarthParams p) => host.Store(p);

        // ------------------------------------------------------------------------------------------ the terrain layout

        /// <summary>Before anything reads the layout: the falls, channels and widths are part of the river layout, the coasts
        /// and the bridge of what the straits and sea channels read.</summary>
        public void Layout(EarthParams p) => Run(p,
            new OneShot(c => ref c.UpgradeExistingWorldRiverFalls, ApplyRiverFalls),
            new OneShot(c => ref c.UpgradeExistingWorldCanals, ApplyCanals),
            new OneShot(c => ref c.UpgradeExistingWorldRiverChannels, ApplyRiverChannels),
            new OneShot(c => ref c.UpgradeExistingWorldRiverEnds, ApplyRiverEnds),
            new OneShot(c => ref c.UpgradeExistingWorldRiverDeltas, ApplyRiverDeltas),
            new OneShot(c => ref c.UpgradeExistingWorldRiverWidths, ApplyRiverWidths),
            new OneShot(c => ref c.UpgradeExistingWorldSeaDepth, ApplySeaDepth),
            new OneShot(c => ref c.UpgradeExistingWorldClimateData, ApplyClimateData),
            new OneShot(c => ref c.UpgradeExistingWorldLakeShores, ApplyLakeShores),
            new OneShot(c => ref c.UpgradeExistingWorldIslands, ApplyIslands, KeptIslands),
            new OneShot(c => ref c.UpgradeExistingWorldCoasts, ApplyCoasts),
            new OneShot(c => ref c.UpgradeExistingWorldGutterBridges, ApplyGutterBridges));

        bool ApplyRiverFalls(EarthParams p)
        {
            if (p.Rivers && p.RiverFalls < RiverFalls.Version)
            {
                p.RiverFalls = RiverFalls.Version;
                log.Warning("UpgradeExistingWorldRiverFalls: the rivers drop over their waterfalls (Victoria, Niagara, Iguazu, ...) in chunks " +
                    "generated from now on. Chunks already generated near a fall keep the old river and may meet the new one with a step. The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldRiverFalls: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                p.Rivers ? "it already has the waterfalls" : "it has no rivers");
            return false;
        }

        bool ApplyCanals(EarthParams p)
        {
            string before = p.Canals ?? "";
            p.Canals = host.CanalCodes();
            log.Warning("UpgradeExistingWorldCanals: canals in this world now '{0}' (were '{1}', from the world's earthSuezCanal and earthPanamaCanal) for chunks generated from now on. " +
                "Chunks already generated on a canal's course keep their land. The flag is reset once the world is saved.", p.Canals, before);
            return true;
        }

        bool ApplyRiverChannels(EarthParams p)
        {
            if (p.Rivers && p.RiverChannels < SeaChannels.Version)
            {
                p.RiverChannels = SeaChannels.Version;
                log.Warning("UpgradeExistingWorldRiverChannels: rivers' ways out to the sea are deepened for ships (rules {0}) in chunks generated from now on. " +
                    "Chunks already generated keep their shallower water and may meet a channel with a step on the sea bed. The flag is reset once the world is saved.", p.RiverChannels);
                return true;
            }
            log.Notification("UpgradeExistingWorldRiverChannels: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                p.Rivers ? "it already has the current channels" : "it has no rivers");
            return false;
        }

        bool ApplyRiverEnds(EarthParams p)
        {
            if (p.Rivers && p.RiverEnds < Core.RiverEnds.Version)
            {
                p.RiverEnds = Core.RiverEnds.Version;
                log.Warning("UpgradeExistingWorldRiverEnds: rivers continue to their real end, into the terminal lakes (the Aral Sea, Lop Nur, ...) " +
                    "or the sea, or fade out in dry basins, in chunks generated from now on. Chunks already generated keep their land and rivers and may " +
                    "meet the new lakes and courses with a step. The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldRiverEnds: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                p.Rivers ? "it already has the river ends" : "it has no rivers");
            return false;
        }

        bool ApplyRiverDeltas(EarthParams p)
        {
            if (p.Rivers && p.RiverEnds > 0 && p.RiverDeltas < Core.RiverDeltas.Version)
            {
                p.RiverDeltas = Core.RiverDeltas.Version;
                log.Warning("UpgradeExistingWorldRiverDeltas: delta branches share their river's water (the Mekong's Tiền and Bassac) in chunks " +
                    "generated from now on. Chunks already generated keep their rivers and may meet the new widths with a step. The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldRiverDeltas: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                !p.Rivers ? "it has no rivers" : p.RiverEnds == 0 ? "it needs the river ends first: UpgradeExistingWorldRiverEnds" : "it already has the delta branches");
            return false;
        }

        bool ApplyRiverWidths(EarthParams p)
        {
            if (p.Rivers && p.RiverWidths < RiverNetwork.WidthRulesVersion)
            {
                p.RiverWidths = RiverNetwork.WidthRulesVersion;
                log.Warning("UpgradeExistingWorldRiverWidths: river widths of 0.14.7 (the standard world's widths in blocks, real widths where wider) " +
                    "in chunks generated from now on. In worlds bigger than the standard 204 800 ring rivers become narrower and chunks already generated " +
                    "may meet them with a step. The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldRiverWidths: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                p.Rivers ? "it already has the current width rules" : "it has no rivers");
            return false;
        }

        bool ApplySeaDepth(EarthParams p)
        {
            if (p.MinSeaDepth < TerrainModel.ShipSeaDepth)
            {
                p.MinSeaDepth = TerrainModel.ShipSeaDepth;
                log.Warning("UpgradeExistingWorldSeaDepth: at least {0} blocks of water away from the shore in chunks generated from now on. " +
                    "Chunks already generated keep their sea floor and may meet the new one with a one-block step. The flag is reset once the world is saved.", p.MinSeaDepth);
                return true;
            }
            log.Notification("UpgradeExistingWorldSeaDepth: nothing to do in this world (it has {0} blocks already). The flag is reset once the world is saved.", p.MinSeaDepth);
            return false;
        }

        bool ApplyClimateData(EarthParams p)
        {
            int available = host.ClimateFixAvailable;
            if (p.ClimateData < available)
            {
                int before = p.ClimateData;
                p.ClimateData = available;
                log.Warning("UpgradeExistingWorldClimateData: climate fix v{0} (was v{1}; decision 0018: the complete UDel normals, without the one-row " +
                    "stripes across the land or along the coasts and with December's rain and snow over the land) for chunks generated from now on. Chunks already generated keep their climate; " +
                    "forests, soil and grass may change with a straight edge where new chunks meet them. The flag is reset once the world is saved.", p.ClimateData, before);
                return true;
            }
            log.Notification("UpgradeExistingWorldClimateData: nothing to do in this world ({0}). The flag is reset once the world is saved.",
                available > 0 ? "it has climate fix v" + p.ClimateData + " already" : "earthdata/climatefix.bin to climatefix4.bin are missing");
            return false;
        }

        bool ApplyLakeShores(EarthParams p)
        {
            if (p.LakeShores < ShoreData.FileVersion)
            {
                p.LakeShores = ShoreData.FileVersion;
                log.Warning("UpgradeExistingWorldLakeShores: the land around lakes slopes down to the real ground instead of standing flat at the " +
                    "lake's level, in chunks generated from now on. Chunks already generated keep their land and may meet the new slopes with a step " +
                    "(up to about 10 blocks next to the old plateaus). The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldLakeShores: nothing to do in this world (it has the eased lake shores already). The flag is reset once the world is saved.");
            return false;
        }

        bool ApplyIslands(EarthParams p)
        {
            int before = p.Islands;
            double beforeMin = p.MinIslandBlocks;
            p.Islands = Config.Islands ? IslandLayer.RulesVersion : 0;
            p.MinIslandBlocks = Config.MinIslandBlocks;
            log.Warning("UpgradeExistingWorldIslands: islands from outlines {0} (were {1}), at least {2} blocks wide, for chunks generated from now on. " +
                "Chunks already generated keep their coasts and may meet the new ones with a step near small islands and reefs. The flag is reset once the world is saved.",
                p.Islands > 0 ? "on, rules v" + p.Islands : "off", before > 0 ? "on, rules v" + before + ", " + beforeMin + " blocks" : "off", p.MinIslandBlocks);
            return true;
        }

        void KeptIslands(EarthParams p)
        {
            if (p.Islands > 0 && p.Islands < IslandLayer.RulesVersion)
                log.Notification("Earth islands: this world keeps its saved island rules v{0} (outlines with GSHHG's raster staircases), so new chunks " +
                    "match the existing ones (current rules are v{1}, corners rounded off; UpgradeExistingWorldIslands opts in for new chunks only).", p.Islands, IslandLayer.RulesVersion);
        }

        bool ApplyCoasts(EarthParams p)
        {
            int before = p.Coasts;
            p.Coasts = host.CoastsFor(p.BuildGeometry(), p.Islands > 0);
            log.Warning("UpgradeExistingWorldCoasts: coasts from outlines {0} (were {1}) for chunks generated from now on{2}. " +
                "Chunks already generated keep their coasts and may meet the new ones with a step where they differ. The flag is reset once the world is saved.",
                p.Coasts > 0 ? "on" : "off", before > 0 ? "on" : "off",
                Config.Coasts && p.Coasts == 0 ? (p.Islands > 0 ? " (the grid cells are under CoastMinCellBlocks wide here)" : " (needs the islands: UpgradeExistingWorldIslands)") : "");
            return true;
        }

        bool ApplyGutterBridges(EarthParams p)
        {
            if (!p.GutterBridges)
            {
                p.GutterBridges = true;
                log.Warning("UpgradeExistingWorldGutterBridges: Antarctica now continues across the seam gutter in chunks generated from now on. " +
                    "Chunks already generated there keep the strait and may meet the bridge with a step. The flag is reset once the world is saved.");
                return true;
            }
            log.Notification("UpgradeExistingWorldGutterBridges: nothing to do in this world (it already has the bridge). The flag is reset once the world is saved.");
            return false;
        }

        // ------------------------------------------------------------------------------------------ mountains and water

        /// <summary>Terrain rules the model reads: before it is built, with the Earth data loaded.</summary>
        public void Terrain(EarthParams p) => Run(p,
            new OneShot(c => ref c.UpgradeExistingWorldMountainTerrain, ApplyMountainTerrain),
            new OneShot(c => ref c.UpgradeExistingWorldWaterTerrain, ApplyWaterTerrain));

        bool ApplyMountainTerrain(EarthParams p)
        {
            // (decision 0075: a world made with vanilla biomes keeps alpine surface rules 0)
            if (p.LocalAlpineEcology && p.VanillaBiomes < 1 && p.AlpineSurfaceRules < MountainEcology.SurfaceVersion)
                p.AlpineSurfaceRules = MountainEcology.SurfaceVersion;
            if (p.Summits >= 1 && p.Summits < KnownSummits.Version) p.Summits = KnownSummits.Version;
            if (p.Summits >= 2)
            {
                string peaks = host.PeakCatalogSignature();
                if (peaks != null) p.PeakCatalogSignature = peaks;
            }
            log.Warning("UpgradeExistingWorldMountainTerrain: summit heights and alpine surfaces updated for future chunks only. Existing chunks stay unchanged and may meet new terrain with a seam. The flag is reset once the world is saved.");
            return true;
        }

        bool ApplyWaterTerrain(EarthParams p)
        {
            if(p.AridRiverRules<AridHydrology.Version)p.AridRiverRules=AridHydrology.Version;
            if(p.StraitRules<NavigableStraits.Version)p.StraitRules=NavigableStraits.Version;
            if(p.ReservoirRules<EarthParams.ReservoirRulesVersion)p.ReservoirRules=EarthParams.ReservoirRulesVersion;
            log.Warning("UpgradeExistingWorldWaterTerrain: future chunks adopt dry wadis, reservoir exclusions and shipping straits. Existing chunks stay unchanged and seams are possible. The flag is reset once the world is saved.");
            return true;
        }

        // ------------------------------------------------------------------------------------------ the regional systems

        /// <summary>
        /// The regional systems are frozen per save, and their one-shots apply the config (on or off) to the world. Fixed tree,
        /// fauna, ore and placement rules are adopted automatically: they only change what new chunks and future spawns get,
        /// without visible seams. Rock layers keep the save's geology rules so new chunks line up with old ones (eligibility
        /// fixes still apply). After the model is built.
        /// </summary>
        public void Regional(EarthParams p)
        {
            Run(p,
                new OneShot(c => ref c.UpgradeExistingWorldOreRules, ApplyOreRules),
                new OneShot(c => ref c.UpgradeExistingWorldGeologyRules, ApplyGeologyRules),
                new OneShot(c => ref c.UpgradeExistingWorldFaunaRules, ApplyFaunaRules),
                new OneShot(c => ref c.UpgradeExistingWorldTreeRules, ApplyTreeRules));

            // Rule fixes that don't produce seams reach existing saves without any flag.
            bool saveChanged = false;
            // (a saved number above this build's is refused before this, InitWorld, ER-05; S-R1 of the Fable pass)
            void Adopt(bool enabled, string name, ref int saved, int current)
            {
                if (!enabled || saved >= current) return;
                log.Notification("Earth {0} rules v{1} → v{2}: fixes apply to new generation in this world; what already exists is unchanged.", name, saved, current);
                saved = current;
                saveChanged = true;
            }
            Adopt(p.RegionalTrees, "tree", ref p.TreeRulesVersion, RegionalTreeCatalog.RulesVersion);
            Adopt(p.RegionalFauna, "fauna", ref p.FaunaRulesVersion, RegionalFaunaCatalog.RulesVersion);
            Adopt(p.RegionalOres, "ore", ref p.OreRulesVersion, RegionalOreCatalog.Version);
            Adopt(p.PlacementRules >= 1, "placement", ref p.PlacementRules, PlacementCatalogue.RulesVersion);
            if (p.RegionalGeology && p.GeologyRulesVersion < RegionalGeologyCatalog.RulesVersion)
                log.Notification("This world keeps the rock layers of geology rules v{0} (now v{1}) so new chunks line up with old ones; clay, peat and salt " +
                    "eligibility fixes apply. Set UpgradeExistingWorldGeologyRules=true once to switch its new chunks to the current rock layers.",
                    p.GeologyRulesVersion, RegionalGeologyCatalog.RulesVersion);

            if (p.CreationOptionsVersion == 0 && p.RegionalFauna && p.IncludeExtinctAnimals != Config.IncludeExtinctAnimals)
            {
                p.IncludeExtinctAnimals = Config.IncludeExtinctAnimals;
                saveChanged = true;
                log.Notification("Extinct animals are {0} for future natural spawns; existing animals remain.", p.IncludeExtinctAnimals ? "enabled" : "disabled");
            }
            if (saveChanged) host.SaveParams();
        }

        bool ApplyOreRules(EarthParams p)
        {
            p.RegionalOres = Config.RegionalOres;
            if (p.RegionalOres)
            {
                p.OreRulesVersion = RegionalOreCatalog.Version;
                // a ground-column world takes the real-size deposits (ore rules v4) as a new world would, not the rules-v3
                // ore bodies with heat maps v1
                if (OreLayerFacts.Layers(p.OreLayers)) p.OreRules = OreBackground.CurrentRules;
                p.OreSettingsJson = System.Text.Json.JsonSerializer.Serialize(Config.Ores ?? new OreTuning());
                p.OreDataSignature = null;
            }
            // decision 0077 (ER-07): the crafting rules follow where Earth decides bauxite, as EarthWorldOptions.Freeze sets them;
            // recipes only, nothing generated changes
            int crafting = EarthCrafting.RulesFor(p.GroundColumns, p.RegionalOres);
            if (crafting != p.CraftingRules)
            {
                log.Notification("UpgradeExistingWorldOreRules: crafting rules {0} → {1} from the next start, when the recipes are set (decision 0077: refractory bricks without bauxite {2}).",
                    p.CraftingRules, crafting, crafting > 0 ? "on" : "off");
                p.CraftingRules = crafting;
            }
            log.Warning("UpgradeExistingWorldOreRules: regional ores are {0} for new generation in this world{1}. Existing deposits remain. The flag is reset once the world is saved.",
                p.RegionalOres ? "on" : "off", p.RegionalOres ? ", with the current Ores tuning" : "");
            return true;
        }

        bool ApplyGeologyRules(EarthParams p)
        {
            p.RegionalGeology = Config.RegionalGeology;
            if (p.RegionalGeology)
            {
                p.GeologyRulesVersion = RegionalGeologyCatalog.RulesVersion;
                p.GeologyProviderSignature = null;
                p.GeologyTransitionBlocks = Config.GeologyTransitionBlocks;
            }
            log.Warning("UpgradeExistingWorldGeologyRules: regional geology is {0} for new chunks in this world{1}. Rock layers in new chunks may not line up " +
                "with chunks already generated. With Hydrate or Diedrate installed, its aquifer data for new chunks now comes from " +
                (p.WritesHodAquifers() ? "Earth's water table" : "Hydrate or Diedrate itself") +
                " (decision 0059). The flag is reset once the world is saved.", p.RegionalGeology ? "on" : "off",
                p.RegionalGeology ? string.Format(CultureInfo.InvariantCulture, " (rules v{0}, contact zone {1} blocks)", p.GeologyRulesVersion, p.GeologyTransitionBlocks) : "");
            return true;
        }

        bool ApplyFaunaRules(EarthParams p)
        {
            p.RegionalFauna = Config.RegionalFauna;
            if (p.RegionalFauna) p.FaunaRulesVersion = RegionalFaunaCatalog.RulesVersion;
            log.Notification("UpgradeExistingWorldFaunaRules: regional fauna is {0} for future natural spawns in this world; existing animals remain. The flag is reset once the world is saved.",
                p.RegionalFauna ? "on" : "off");
            return true;
        }

        bool ApplyTreeRules(EarthParams p)
        {
            p.RegionalTrees = Config.RegionalTrees;
            if (p.RegionalTrees) p.TreeRulesVersion = RegionalTreeCatalog.RulesVersion;
            log.Notification("UpgradeExistingWorldTreeRules: regional trees are {0} for new chunks in this world. The flag is reset once the world is saved.",
                p.RegionalTrees ? "on" : "off");
            return true;
        }
    }
}
