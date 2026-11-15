using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// Native ranges for the ground plants of regional plant packs (Floral Zones): their block patches only generate in
    /// the botanical countries of their species (WCVP native range) or, without an identifiable species range, of the
    /// pack's floristic region. Vanilla and unrelated patches, wild crops and plants without a range keep their climate
    /// rules. Active in Earth worlds with regional trees while ModConfig RegionalPlants is on (read at every start, new
    /// chunks only). Hooks: a prefix on BlockPatch.Generate (all surface/underwater/under-tree patches; coexists with
    /// EarthWorldGen's reservation guard on the same method) and, for patches growing on tree trunks (which never call
    /// Generate), the IsPatchSuitableUnderTree call sites inside ForestFloorSystem.GenPatches (transpiler, with the tree
    /// position from a prefix). The region is looked up at the patch's own position (8-block cells). Missing data or hooks
    /// leave vanilla behaviour with a warning; nothing here throws into world generation.
    /// </summary>
    public sealed class EarthPlants : ModSystem
    {
        const string HarmonyId = "earth.regionalplants";
        const int Unset = int.MinValue;
        static volatile State state;
        static EarthPlants instance;
        static int hookErrors;

        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        RegionalPlantCatalog catalog;
        PlacementCatalogue placement;
        bool treeHooks;
        string inactiveReason = "not initialised yet";
        /// <summary>The pack's own temperature and rain limits of the patches whose limits the catalogue's climate envelopes
        /// replaced (decision 0047), put back when the gates go (<see cref="Deactivate"/>): a failure leaves those patches the
        /// pack's rules, not none. Main thread only.</summary>
        readonly Dictionary<BlockPatch, (int MinTemp, int MaxTemp, float MinRain, float MaxRain)> ownLimits = new(ReferenceEqualityComparer.Instance);

        /// <summary>Immutable once published; the gate table is keyed by patch object reference.</summary>
        sealed class State
        {
            public readonly BotanicalRegions Regions;
            public readonly EarthGeometry Geometry;
            public readonly TerrainModel Model;
            public readonly Dictionary<BlockPatch, PlantGate> Gates;
            /// <summary>Patches without limits of their own while gated (their envelopes replaced them): vetoed if a lookup fails.</summary>
            public readonly HashSet<BlockPatch> LimitsReplaced;
            public readonly int Covered, ClimateOnly, Unknown, OnTrees, Placement;
            /// <summary>This state's key in per-thread caches (a number, so an unloaded world is not kept alive by a thread).</summary>
            public readonly long CacheId = Interlocked.Increment(ref lastCacheId);
            static long lastCacheId;
            /// <summary>Patches vetoed by range and by climate envelope since start (/earth plants).</summary>
            public readonly StripedCounter Vetoes = new(), ClimateVetoes = new();
            public State(BotanicalRegions regions, TerrainModel model, Dictionary<BlockPatch, PlantGate> gates, HashSet<BlockPatch> limitsReplaced,
                int covered, int climateOnly, int unknown, int onTrees, int placement)
            {
                Regions = regions; Model = model; Geometry = model.Geometry; Gates = gates; LimitsReplaced = limitsReplaced;
                Covered = covered; ClimateOnly = climateOnly; Unknown = unknown; OnTrees = onTrees; Placement = placement;
            }
            public int Region(double x, double z, bool water)
            {
                var g = Geometry.ToGeo(x, z);
                if (g.InGutter || g.BeyondPole) return -1;
                // land patches tried over open sea never take the land fill (a ring search): they cannot grow there anyway
                var surface = water ? BotanicalRegions.Surface.Water
                    : ColumnStore.Near(Model, x, z).Water == TerrainModel.WaterSea ? BotanicalRegions.Surface.Sea : BotanicalRegions.Surface.Land;
                return Regions.At(g.Lat, g.Lon, surface);
            }
        }

        /// <summary>
        /// A world generation thread's lookups for the chunk it works on, per 8x8-block cell, filled as cells are asked: the
        /// botanical region on land and in water (a land lookup takes the terrain column, ~0.2-2 µs), Earth's climate at sea
        /// level (placement-catalogue envelopes) and whether the land is arable. Vanilla tries each patch at random positions
        /// across the chunk, so a cache of the last cell alone rarely held the next attempt's.
        /// </summary>
        sealed class ChunkCells
        {
            public const int Shift = 5 - CellShift, PerSide = 1 << Shift, Count = PerSide * PerSide;
            public long State = -1, Chunk;   // State.CacheId (a number, so an unloaded world is not kept alive by a thread)
            public readonly int[] Land = new int[Count], Water = new int[Count];
            public readonly float[] Mean = new float[Count], Cold = new float[Count], Rain = new float[Count];
            /// <summary>The rain the climate envelopes read: <see cref="Rain"/>, or with GenFixRules the river belt's (CR-E1).</summary>
            public readonly float[] EnvelopeRain = new float[Count];
            public readonly bool[] HasClimate = new bool[Count];
            /// <summary>-1 not looked up yet, 0 no, 1 yes.</summary>
            public readonly sbyte[] Arable = new sbyte[Count];

            /// <summary>This thread's cells for the chunk holding cell (cx, cz), and that cell's index.</summary>
            public static ChunkCells For(State s, int cx, int cz, out int i)
            {
                var c = cells ??= new ChunkCells();
                long chunk = ((long)(cx >> Shift) << 32) | (uint)(cz >> Shift);
                if (c.State != s.CacheId || c.Chunk != chunk)
                {
                    c.State = s.CacheId; c.Chunk = chunk;
                    Array.Fill(c.Land, Unset); Array.Fill(c.Water, Unset);
                    Array.Clear(c.HasClimate);
                    Array.Fill(c.Arable, (sbyte)-1);
                }
                i = (cx & (PerSide - 1)) + PerSide * (cz & (PerSide - 1));
                return c;
            }
        }
        [ThreadStatic] static ChunkCells cells;
        // Tree position while ForestFloorSystem places its under-tree and on-trunk patches.
        [ThreadStatic] static bool treeScope;
        [ThreadStatic] static int treeX, treeZ;

        public bool Active => state != null;

        /// <summary>Whether this start's set-up meant the gates to run (regional trees with RegionalPlants, or plants as around
        /// 1318); they then count for EarthWorldGen's check (decision 0083).</summary>
        bool wanted;

        /// <summary>(Decision 0083; FP-02) The plant gates, meant to run this start, off: for EarthWorldGen's check before the
        /// first chunk.</summary>
        internal void FeaturesOff(System.Collections.Generic.List<string> off)
        {
            if (wanted && !Active) off.Add("the regional plant gates (" + (inactiveReason ?? "off") + ")");
        }
        public RegionalPlantCatalog Catalog => catalog;
        public override double ExecuteOrder() => 0.07;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            api = sapi;
            instance = this;
            // a new server in this process says its first hook error again (CR-E4)
            Interlocked.Exchange(ref hookErrors, 0);
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () =>
            {
                try { Initialize(); }
                catch (Exception e)
                {
                    Deactivate("startup failed (" + e.Message + ")");
                    Mod.Logger.Warning("Earth regional plants disabled, plant packs keep their climate rules: {0}", e);
                }
            });
        }

        void Initialize()
        {
            wanted = false;
            var p = core?.Params;
            if (p?.Enabled != true || core.Model == null) { inactiveReason = "not an Earth world"; return; }
            // Plants as around 1318 (decision 0047): the placement catalogue gates the crops and berry bushes it maps, with
            // or without plant packs. EarthPlacement (earlier ExecuteOrder) has loaded it by now in such worlds.
            placement = api.ModLoader.GetModSystem<EarthPlacement>()?.Catalogue;
            string packsOff = !p.RegionalTrees ? "this Earth world was created without regional trees"
                : core.Config?.RegionalPlants != true ? "RegionalPlants is off in earth.json" : null;
            if (packsOff != null && placement == null) { Inactive(packsOff); return; }
            wanted = true;
            if (packsOff == null)
            {
                var rules = api.Assets.TryGet(new AssetLocation("earth:config/regionalplants.json"));
                var regions = placement == null ? api.Assets.TryGet(new AssetLocation("earth:config/treeregions.json")) : null;
                if (placement != null)
                {
                    // with the placement catalogue, a missing or unreadable plant-pack file turns the packs' ranges off and
                    // nothing else: thrown, it left out the catalogue's crop, berry and flower gates too (CR-E3)
                    try
                    {
                        if (rules == null) throw new System.IO.FileNotFoundException("regionalplants.json is missing");
                        catalog = new RegionalPlantCatalog(rules.ToText(), placement.Geography, api.ModLoader.Mods.Select(m => m.Info.ModID));
                    }
                    catch (Exception e)
                    {
                        catalog = null;
                        Mod.Logger.Error("Earth regional plants: plant-pack ranges off ({0}); plant packs keep their climate rules, catalogue crops and berries keep their 1318 ranges.", e.Message);
                    }
                }
                else
                {
                    if (rules == null || regions == null) { Warn("regionalplants.json or treeregions.json is missing"); return; }
                    catalog = new RegionalPlantCatalog(rules.ToText(), BotanicalRegions.Shared(regions.ToText()), api.ModLoader.Mods.Select(m => m.Info.ModID));
                }
            }
            else Mod.Logger.Notification("Earth regional plants: plant-pack ranges off ({0}); catalogue crops and berries keep their 1318 ranges.", packsOff);
            foreach (var provider in catalog?.Providers ?? Array.Empty<RegionalPlantCatalog.ProviderInfo>())
            {
                string installed = api.ModLoader.Mods.FirstOrDefault(m => m.Info.ModID == provider.Id)?.Info?.Version;
                if (installed != provider.Version)
                    Mod.Logger.Warning("Earth regional plants: {0} is {1}, reviewed {2}; reviewed block codes keep their native ranges, new ones keep their climate rules.", provider.Id, installed, provider.Version);
            }
            // PatchVanilla's PlantsTable: the block patch hooks, and the forest floor's (off on its own, with its own line). Its
            // line names a member that is missing or changed and what stays vanilla's.
            harmony = new Harmony(HarmonyId);
            var patches = PatchVanilla.For(api, Mod, harmony);
            patches.Apply(PatchVanilla.PlantsTable());
            if (!patches.On(PatchVanilla.Feature.RegionalPlants))
            {
                inactiveReason = "vanilla's block patch hooks changed on this game version (" + patches.StillInPlace(PatchVanilla.Feature.RegionalPlants) + ")";
                return;
            }
            treeHooks = patches.On(PatchVanilla.Feature.RegionalPlantsOnTrees);
            inactiveReason = "waiting for world generation to load the block patches";
            var vegetation = api.ModLoader.GetModSystem<GenVegetationAndPatches>();
            if (vegetation?.bpc?.Patches != null) BuildGates(vegetation.bpc);
        }

        void Inactive(string reason)
        {
            inactiveReason = reason;
            Mod.Logger.Notification("Earth regional plants inactive: {0}. Plant packs keep their climate rules.", reason);
        }

        void Warn(string reason)
        {
            inactiveReason = reason;
            Mod.Logger.Warning("Earth regional plants inactive: {0}. Plant packs keep their climate rules.", reason);
        }

        void Deactivate(string reason)
        {
            state = null;
            inactiveReason = reason;
            // the pack's own limits come back with the gates' going (the envelopes stood in for them)
            foreach (var (patch, own) in ownLimits)
            {
                patch.MinTemp = own.MinTemp; patch.MaxTemp = own.MaxTemp; patch.MinRain = own.MinRain; patch.MaxRain = own.MaxRain;
            }
            ownLimits.Clear();
        }

        internal static void InitWorldGenPostfix(GenVegetationAndPatches __instance)
        {
            var self = instance;
            if (self == null || self.catalog == null && self.placement == null) return;
            try { self.BuildGates(__instance.bpc); }
            catch (Exception e)
            {
                self.Deactivate("gate table failed (" + e.Message + ")");
                self.Mod.Logger.Warning("Earth regional plants disabled, plant packs keep their climate rules: {0}", e);
            }
        }

        void BuildGates(BlockPatchConfig bpc)
        {
            var patches = bpc?.Patches;
            if (patches == null) { Deactivate("no block patches loaded"); return; }
            var gates = new Dictionary<BlockPatch, PlantGate>(ReferenceEqualityComparer.Instance);
            var limitsReplaced = new HashSet<BlockPatch>(ReferenceEqualityComparer.Instance);
            int covered = 0, climateOnly = 0, onTrees = 0;
            var unknown = new List<string>();
            var species = new HashSet<RegionalPlantCatalog.Rule>();
            var taxa = new HashSet<string>(StringComparer.Ordinal);
            int byPlacement = 0, packByPlacement = 0, envelopes = 0;
            foreach (var patch in patches)
            {
                if (patch?.blockCodes == null) continue;
                var codes = patch.blockCodes.Where(c => c != null).Select(c => c.ToString()).ToArray();
                bool water = patch.Placement == EnumBlockPatchPlacement.NearWater || patch.Placement == EnumBlockPatchPlacement.NearSeaWater
                    || patch.Placement == EnumBlockPatchPlacement.UnderWater || patch.Placement == EnumBlockPatchPlacement.UnderSeaWater;
                if (placement != null)
                {
                    var placed = placement.CreateGate(codes, water, out _);
                    if (placed != null)
                    {
                        gates[patch] = placed;
                        byPlacement++;
                        // Decision 0047: the taxa's Earth climate envelopes replace the patch's own temperature and rain
                        // limits, which were tuned for vanilla's climate (Veto checks them). Forest, fertility and height
                        // limits stay. Patches on or under trees keep their own limits as well; the envelopes are checked on
                        // top of them (Veto, at the site's height).
                        if (patch.Placement != EnumBlockPatchPlacement.OnTrees && patch.Placement != EnumBlockPatchPlacement.UnderTrees)
                        {
                            // (the first time only: a second pass over the same patches finds the limits already replaced)
                            ownLimits.TryAdd(patch, (patch.MinTemp, patch.MaxTemp, patch.MinRain, patch.MaxRain));
                            limitsReplaced.Add(patch);
                            patch.MinTemp = -100; patch.MaxTemp = 100; patch.MinRain = 0; patch.MaxRain = 1;
                            envelopes++;
                        }
                        // A pack's wild crop (Floral Zones) counts for the pack's reviewed inventory as before.
                        if (catalog != null && codes.Any(catalog.Covers)) { covered++; packByPlacement++; }
                        foreach (string name in placed.Names) taxa.Add(name);
                        if (patch.Placement == EnumBlockPatchPlacement.OnTrees) onTrees++;
                        continue;
                    }
                }
                if (catalog == null) continue;
                var gate = catalog.CreateGate(codes, water, out var kind);
                if (kind == RegionalPlantCatalog.GateKind.Vanilla) continue;
                covered++;
                if (kind == RegionalPlantCatalog.GateKind.ClimateOnly) climateOnly++;
                else if (kind == RegionalPlantCatalog.GateKind.Unknown) unknown.Add(string.Join("+", codes.Take(2)));
                else
                {
                    gates[patch] = gate;
                    foreach (var r in gate.Rules) species.Add(r);
                    if (patch.Placement == EnumBlockPatchPlacement.OnTrees) onTrees++;
                }
            }
            var next = new State(placement?.Geography ?? catalog.Geography, core.Model, gates, limitsReplaced, covered, climateOnly, unknown.Count, onTrees, byPlacement);
            state = next;
            inactiveReason = null;
            if (placement != null)
                Mod.Logger.Notification("Earth regional plants: {0} crop and berry patches gated by their 1318 range ({1} plants of the placement catalogue), {2} of them by Earth climate envelopes instead of their own temperature and rain limits.", byPlacement, taxa.Count, envelopes);
            if (catalog == null) return;
            int expected = catalog.Inventory.Count;
            Mod.Logger.Notification("Earth regional plants: {0} of {1} regional pack block patches gated by native range ({2} species/pack-region rules), {3} climate-only (wild crops, plants without a range), {4} unmapped (climate rules kept). Providers: {5}.",
                gates.Count - byPlacement + packByPlacement, covered, species.Count, climateOnly, unknown.Count, string.Join(", ", catalog.Providers.Select(p => p.Id + " " + p.Version)));
            if (unknown.Count > 0)
                Mod.Logger.Warning("Earth regional plants: no reviewed native range for these pack patches; they keep their climate rules: {0}", string.Join(", ", unknown.Take(20)));
            if (covered != expected)
                Mod.Logger.Warning("Earth regional plants: found {0} pack patches, the reviewed inventory has {1} (a pack changed or another mod edits the patches).", covered, expected);
            if (!treeHooks && onTrees > 0)
                Mod.Logger.Warning("Earth regional plants: {0} gated patch(es) grow on tree trunks and keep their climate rules (forest-floor hook unavailable).", onTrees);
        }

        static bool Allowed(State s, PlantGate gate, int x, int z)
        {
            int cx = x >> CellShift, cz = z >> CellShift;
            var c = ChunkCells.For(s, cx, cz, out int i);
            if (c.Land[i] == Unset) c.Land[i] = s.Region(CellCentre(cx), CellCentre(cz), false);
            int region = c.Land[i];
            if (region < 0 && gate.Water)
            {
                if (c.Water[i] == Unset) c.Water[i] = s.Region(CellCentre(cx), CellCentre(cz), true);
                region = c.Water[i];
            }
            return gate.Allows(region);
        }

        /// <summary>Positions are resolved per 8x8-block cell (~0.006° at the reference scale, finer than the 0.015°
        /// boundary simplification): actual patch positions, with repeated attempts in a cell sharing one lookup.</summary>
        const int CellShift = 3;
        static double CellCentre(int cell) => (cell << CellShift) + (1 << CellShift) / 2.0;

        /// <summary>Placement-catalogue envelope at a site: Earth's yearly mean and coldest month at height y, and rain
        /// (sea-level climate per 8x8-block cell).</summary>
        static bool ClimateAllowed(State s, PlantGate gate, int x, int y, int z)
        {
            int cx = x >> CellShift, cz = z >> CellShift;
            var c = ChunkCells.For(s, cx, cz, out int i);
            if (!c.HasClimate[i])
            {
                var g = s.Geometry.ToGeo(CellCentre(cx), CellCentre(cz));
                var cs = s.Model.Data.SampleClimate(g.Lat, g.Lon);
                c.Mean[i] = (float)cs.TMean; c.Cold[i] = (float)cs.TCold; c.Rain[i] = (float)cs.Rain;
                // CR-E1: a river's green belt counts as arable (Arable) and gives trees, animals and soil its rain; with
                // GenFixRules the envelopes read that rain too, so crops along the Nile are not vetoed on Cairo's rain
                c.EnvelopeRain[i] = s.Model.Rules.RiparianEnvelopes ? (float)s.Model.RiparianRain(CellCentre(cx), CellCentre(cz), cs.Rain) : c.Rain[i];
                c.HasClimate[i] = true;
            }
            double lapse = TerrainModel.Lapse(s.Model.Vertical, y);
            return gate.ClimateAllows(c.Mean[i] - lapse, c.Cold[i] - lapse, c.EnvelopeRain[i], !gate.HasCrop || Arable(s, c, i, cx, cz));
        }

        /// <summary>Habitat for the catalogue's crops in zone worlds: arable zones hold at least half the weight (zones.json
        /// "arable"), or a river's green belt runs through dry land (the cell's climate rain, already looked up). Without
        /// zones every place counts as arable.</summary>
        static bool Arable(State s, ChunkCells c, int i, int cx, int cz)
        {
            var zones = s.Model.Zones;
            if (zones == null) return true;
            if (c.Arable[i] < 0)
            {
                double x = CellCentre(cx), z = CellCentre(cz);
                bool ok = zones.ArableShare(s.Model.ZoneDriversAt(x, z)) >= 0.5 || s.Model.RiparianWeight(x, z, c.Rain[i]) >= 0.5;
                c.Arable[i] = ok ? (sbyte)1 : (sbyte)0;
            }
            return c.Arable[i] > 0;
        }

        static bool Veto(State s, PlantGate gate, BlockPatch patch, int x, int y, int z)
        {
            try
            {
                if (!Allowed(s, gate, x, z)) { s.Vetoes.Increment(); return true; }
                if (gate.Taxa.Count == 0 || ClimateAllowed(s, gate, x, y, z)) return false;
                s.ClimateVetoes.Increment();
                return true;
            }
            catch (Exception e)
            {
                // Never break world generation: fall back to the pack's own rules. A patch whose temperature and rain limits
                // the envelopes replaced has none of its own left, so it is skipped rather than grown in any climate.
                if (Interlocked.Increment(ref hookErrors) == 1)
                    instance?.Mod.Logger.Warning("Earth regional plants lookup failed: patches keep their own climate rules, those whose limits the 1318 envelopes replace are skipped where it fails: {0}", e);
                return patch != null && s.LimitsReplaced.Contains(patch);
            }
        }

        internal static bool GeneratePrefix(BlockPatch __instance, int posX, int posY, int posZ)
        {
            // (FP-11, Fable's low 5 on ad56a74) a generation entry point inside vanilla's pass: this thread may wait for a missing tile
            using var fetch = Core.FineElevation.Generating();
            // decision 0070: land plants (ferns, flowers, bushes) stay off the shore strip; reeds and water plants keep the
            // water's edge
            if (__instance != null && __instance.Placement is EnumBlockPatchPlacement.OnSurface or EnumBlockPatchPlacement.OnSurfacePlusUnderTrees
                    or EnumBlockPatchPlacement.UnderTrees or EnumBlockPatchPlacement.ReplaceSurface
                && BlockLayerAdjustments.OnShoreStrip(WorldGenerationContext.Current, posX, posZ)) return false;
            var s = state;
            if (s == null || __instance == null || !s.Gates.TryGetValue(__instance, out var gate)) return true;
            return !Veto(s, gate, __instance, posX, posY, posZ);
        }

        public readonly struct TreeScope
        {
            internal readonly bool Set;
            internal readonly int X, Z;
            internal TreeScope(bool set, int x, int z) { Set = set; X = x; Z = z; }
        }

        internal static void ForestFloorPrefix([HarmonyArgument(1)] BlockPos pos, out TreeScope __state)
        {
            __state = new TreeScope(treeScope, treeX, treeZ);
            if (pos == null) { treeScope = false; return; }
            treeScope = true;
            treeX = pos.X;
            treeZ = pos.Z;
        }

        internal static void ForestFloorFinalizer(TreeScope __state)
        {
            treeScope = __state.Set;
            treeX = __state.X;
            treeZ = __state.Z;
        }

        static readonly MethodInfo SuitableUnderTreeTarget = AccessTools.Method(typeof(BlockPatchConfig), nameof(BlockPatchConfig.IsPatchSuitableUnderTree));
        /// <summary>The forest floor's suitability calls the transpiler rewrote (PatchVanilla turns the feature off without one).</summary>
        internal static int TreeCallSites;

        internal static IEnumerable<CodeInstruction> ForestFloorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var replacement = AccessTools.Method(typeof(EarthPlants), nameof(SuitableUnderTree));
            foreach (var ins in instructions)
            {
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo m && m == SuitableUnderTreeTarget)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = replacement;
                    TreeCallSites++;
                }
                yield return ins;
            }
        }

        /// <summary>Replaces bpc.IsPatchSuitableUnderTree(...) inside ForestFloorSystem.GenPatches (same stack shape).</summary>
        public static bool SuitableUnderTree(BlockPatchConfig config, BlockPatch patch, int mapSizeY, ClimateCondition climate, int y)
        {
            using var fetch = Core.FineElevation.Generating();
            if (!config.IsPatchSuitableUnderTree(patch, mapSizeY, climate, y)) return false;
            if (!treeScope) return true;
            var s = state;
            if (s == null || patch == null || !s.Gates.TryGetValue(patch, out var gate)) return true;
            // (patches on and under trees keep their own limits)
            return !Veto(s, gate, null, treeX, y, treeZ);
        }

        /// <summary>Short summary for /earth plants at a world position (evaluated like generation, per 8-block cell).</summary>
        public string Describe(double x, double z)
        {
            var s = state;
            if (s == null) return "Regional ground plants inactive: " + (inactiveReason ?? "unknown") + ". Plant packs keep their climate rules.";
            double px = CellCentre((int)Math.Floor(x) >> CellShift), pz = CellCentre((int)Math.Floor(z) >> CellShift);
            var g = s.Geometry.ToGeo(px, pz);
            int land = s.Region(px, pz, false);
            int water = land >= 0 ? land : s.Region(px, pz, true);
            int allowed = 0, blocked = 0;
            var natives = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var gate in s.Gates.Values)
            {
                if (gate.Allows(gate.Water ? water : land))
                {
                    allowed++;
                    foreach (string name in gate.Names) natives.Add(name);
                }
                else blocked++;
            }
            var geo = s.Regions;
            string where = land >= 0 ? geo.Name(land) + " (" + geo.Code(land) + ")"
                : water >= 0 ? "open water; nearest botanical country " + geo.Name(water) + " (" + geo.Code(water) + ") for aquatic plants"
                : "open ocean / outside the botanical countries";
            string list = natives.Count == 0 ? "No gated plant is native here."
                : "Native gated plants here (climate still applies): " + string.Join(", ", natives.Take(25)) + (natives.Count > 25 ? " and " + (natives.Count - 25) + " more" : "") + ".";
            return $"Regional ground plants at {g.Lat:F3}, {g.Lon:F3}: {where}.\n" +
                (s.Placement > 0 ? $"{s.Placement} of the gated patches are crops and berries of the placement catalogue (1318 ranges). " : "") +
                $"{s.Gates.Count} patches gated by native range: {allowed} allowed here, {blocked} blocked. {s.ClimateOnly} climate-only, {s.Unknown} unmapped (climate rules). " +
                $"Patches vetoed since start: {s.Vetoes.Value} by range, {s.ClimateVetoes.Value} by Earth climate envelope.\n" + list;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            if (instance == this) { state = null; instance = null; }
            base.Dispose();
        }
    }
}
