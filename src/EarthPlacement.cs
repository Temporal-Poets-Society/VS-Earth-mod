using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// Earth's placement catalogue in the game (decisions 0049, 0047): in worlds saved with placement rules (the default
    /// "1318" world setting), plants follow where they grew around 1318. This system loads the catalogue and owns the fruit
    /// trees: a prefix on BlockFruitTreeBranch.TryPlaceBlockForWorldGen replaces vanilla's climate test of each mapped tree
    /// type with the taxon's 1318 range (botanical country) and Earth climate envelope (real yearly mean and coldest month
    /// at the site's elevation, rain); unmapped types keep vanilla's test. The rare-fruit-tree patch drops its forest
    /// minimum, since olive, pomegranate and fig grow in open country. EarthPlants gates the mapped crop and berry patches
    /// with the same catalogue. It also writes the placement inventory (which patches, fruit trees and animals the
    /// catalogue, another Earth system or nobody controls) and serves /earth placement and /earth census.
    /// Without the setting, or when data or hooks are missing, placement stays vanilla's; nothing throws into world generation.
    /// </summary>
    public sealed class EarthPlacement : ModSystem
    {
        const string HarmonyId = "earth.placement";
        static volatile State state;
        static EarthPlacement instance;
        static int hookErrors;

        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        PlacementCatalogue catalogue;
        string status = "not started";
        string inventorySummary;

        sealed class State
        {
            public PlacementCatalogue Catalogue;
            public TerrainModel Model;
            /// <summary>Fruit-tree attempts since start (/earth placement), counted on every world generation thread.</summary>
            public readonly StripedCounter Attempts = new(), Empty = new(), Infertile = new();
            public readonly ConcurrentDictionary<string, long> Placed = new(StringComparer.Ordinal);
        }

        /// <summary>The loaded catalogue in a 1318 world, else null (EarthPlants gates crops and berries with it).</summary>
        public PlacementCatalogue Catalogue => catalogue;
        public bool Active => state != null;
        public override double ExecuteOrder() => 0.069;
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
                    Deactivate();
                    status = "off for this session: startup failed (" + e.Message + ")";
                    Mod.Logger.Error("Earth placement: startup failed; plants keep vanilla placement by climate. {0}", e);
                }
            });
        }

        void Initialize()
        {
            var p = core?.Params;
            if (p?.Enabled != true || core.Model == null) { status = "not an Earth world"; return; }
            if (p.PlacementRules < 1) { status = "this world uses vanilla placement by climate (world setting)"; Mod.Logger.Notification("Earth placement: {0}.", status); return; }
            var json = api.Assets.TryGet(new AssetLocation("earth:config/placement.json"));
            var regions = api.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"));
            if (json == null || regions == null) { Fail("placement.json or treeregions.json is missing"); return; }
            catalogue = new PlacementCatalogue(json.ToText(), regions.ToText());
            if (p.PlacementRules != PlacementCatalogue.RulesVersion)
                Mod.Logger.Warning("Earth placement: this world saved placement rules v{0}; using the current v{1} for new chunks only.", p.PlacementRules, PlacementCatalogue.RulesVersion);
            // PatchVanilla's PlacementTable: the fruit-tree placement, the block patch config and the fruit trees' climate list, all
            // or none (its error line names the member and that plants keep vanilla placement by climate this session)
            harmony = new Harmony(HarmonyId);
            var patches = PatchVanilla.For(api, Mod, harmony);
            patches.Apply(PatchVanilla.PlacementTable());
            if (!patches.On(PatchVanilla.Feature.Placement1318))
            {
                Deactivate();
                status = "off for this session: this game version's fruit-tree placement differs from the reviewed VS 1.22 hook (" + patches.StillInPlace(PatchVanilla.Feature.Placement1318) + ")";
                return;
            }
            state = new State { Catalogue = catalogue, Model = core.Model };
            status = "active";
            Mod.Logger.Notification("Earth placement: plants as around {0} ({1} taxa, {2} provider mappings).", catalogue.Profile, catalogue.Taxa.Count, catalogue.Mappings.Count);
            var vegetation = api.ModLoader.GetModSystem<GenVegetationAndPatches>();
            if (vegetation?.bpc?.Patches != null) PatchesLoaded(vegetation.bpc);
        }

        void Fail(string why)
        {
            Deactivate();
            status = "off for this session: " + why;
            Mod.Logger.Error("Earth placement: {0}. Plants keep vanilla placement by climate for this session.", why);
        }

        void Deactivate()
        {
            state = null;
            catalogue = null;
            try { harmony?.UnpatchAll(HarmonyId); } catch (Exception e) { Mod.Logger.Warning("Earth placement: unpatching failed: {0}", e.Message); }
            harmony = null;
        }

        internal static void InitWorldGenPostfix(GenVegetationAndPatches __instance)
        {
            var self = instance;
            if (self == null || state == null) return;
            try { self.PatchesLoaded(__instance.bpc); }
            catch (Exception e) { self.Mod.Logger.Warning("Earth placement: patch inventory failed: {0}", e); }
        }

        void PatchesLoaded(BlockPatchConfig bpc)
        {
            if (bpc?.Patches == null) return;
            foreach (var patch in bpc.Patches)
                if (patch?.blockCodes != null && patch.blockCodes.Any(c => c?.Domain == "game" && c.Path == "fruittree-branch") && patch.MinForest > 0)
                {
                    Mod.Logger.Notification("Earth placement: rare fruit tree patch forest minimum {0} -> 0 (olive, fig and pomegranate grow in open country).", patch.MinForest);
                    patch.MinForest = 0;
                }
            WriteInventory(bpc);
        }

        // ---------------------------------------------------------------- fruit trees

        internal static bool FruitTreePrefix(BlockFruitTreeBranch __instance, IBlockAccessor blockAccessor, BlockPos pos, IRandom worldgenRandom, ref bool __result)
        {
            var s = state;
            var conds = __instance?.WorldGenConds;
            if (s == null || conds == null || conds.Length == 0 || blockAccessor == null || pos == null) return true;
            try
            {
                var model = s.Model;
                var g = model.TerrainGeoAt(pos.X, pos.Z);
                if (g.InGutter || g.BeyondPole) return true;
                s.Attempts.Increment();
                if (blockAccessor.GetBlockBelow(pos, 1, 0).Fertility <= 20) { s.Infertile.Increment(); __result = false; return false; }
                var cat = s.Catalogue;
                int region = cat.RegionAt(g.Lat, g.Lon);
                var cs = model.Data.SampleClimate(g.Lat, g.Lon);
                double lapse = TerrainModel.Lapse(model.Vertical, pos.Y);
                double temp = cs.TMean - lapse, cold = cs.TCold - lapse, rain = EnvelopeRain(model, pos.X, pos.Z, cs.Rain);
                ClimateCondition vanilla = null;
                int start = worldgenRandom.NextInt(conds.Length);
                for (int i = 0; i < conds.Length; i++)
                {
                    var c = conds[(i + start) % conds.Length];
                    var taxon = cat.FruitTree(c.Type);
                    bool fits;
                    if (taxon != null) fits = cat.Allows(taxon, region, temp, cold, rain);
                    else
                    {
                        vanilla ??= blockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
                        fits = c.MinTemp <= vanilla.Temperature && c.MaxTemp >= vanilla.Temperature && c.MinRain <= vanilla.Rainfall && c.MaxRain >= vanilla.Rainfall;
                    }
                    if (!fits || worldgenRandom.NextFloat() > c.Chance) continue;
                    // As vanilla places it.
                    blockAccessor.SetBlock(__instance.BlockId, pos);
                    blockAccessor.SpawnBlockEntity(__instance.EntityClass, pos);
                    if (blockAccessor.GetBlockEntity(pos) is BlockEntityFruitTreeBranch be)
                    {
                        be.TreeType = c.Type;
                        be.FastForwardGrowth = 0.2f + 0.8f * worldgenRandom.NextFloat();
                    }
                    s.Placed.AddOrUpdate(c.Type, 1, (_, n) => n + 1);
                    __result = true;
                    return false;
                }
                s.Empty.Increment();
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref hookErrors) == 1) instance?.Mod.Logger.Warning("Earth placement: fruit-tree check failed, vanilla's is used (further failures are not logged): {0}", e);
                return true;
            }
        }

        // ---------------------------------------------------------------- inventory

        /// <summary>
        /// Who controls each natural placement path in this session (decision 0047's uncontrolled-path report): block patches
        /// (placement catalogue, plant-pack ranges, other Earth systems, deliberately exempt, or nobody), fruit-tree types and
        /// naturally spawning animals. Written to VintagestoryData/EarthMap/placement-inventory-&lt;seed&gt;.txt.
        /// </summary>
        void WriteInventory(BlockPatchConfig bpc)
        {
            var cat = catalogue;
            if (cat == null) return;
            var packs = api.ModLoader.GetModSystem<EarthPlants>()?.Catalog;
            var sb = new StringBuilder();
            var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            void Add(string group, string line) { if (!groups.TryGetValue(group, out var l)) groups[group] = l = new List<string>(); l.Add(line); }
            foreach (var patch in bpc.Patches)
            {
                if (patch?.blockCodes == null) continue;
                var codes = patch.blockCodes.Where(c => c != null).Select(c => c.ToString()).ToArray();
                if (codes.Length == 0) continue;
                string label = string.Join(" ", codes.Distinct().Take(3)) + (codes.Distinct().Count() > 3 ? " …" : "") + " [" + patch.Placement + "]";
                var kind = cat.Classify(codes, out var taxa);
                switch (kind)
                {
                    case PlacementCatalogue.PatchKind.Mapped: Add("1 placement catalogue (1318 range)", label + " -> " + string.Join(", ", taxa.Select(t => t.Name))); continue;
                    case PlacementCatalogue.PatchKind.Partial: Add("5 partly mapped (climate rules kept)", label); continue;
                    case PlacementCatalogue.PatchKind.NotOrganism: Add("4 not organisms", label); continue;
                    case PlacementCatalogue.PatchKind.ControlledElsewhere: Add("3 other Earth systems", label); continue;
                }
                if (codes.Any(c => c.EndsWith(":fruittree-branch", StringComparison.Ordinal))) Add("1 placement catalogue (1318 range)", label + " -> fruit trees by type");
                else if (packs != null && codes.Any(packs.Covers)) Add("2 plant-pack native ranges (EarthPlants)", label);
                else Add("6 uncontrolled (climate only)", label);
            }
            sb.AppendLine("Earth placement inventory (decision 0047), profile " + cat.Profile + ", seed " + api.WorldManager.Seed);
            sb.AppendLine();
            sb.AppendLine("Block patches:");
            foreach (var pair in groups) sb.AppendLine($"  {pair.Key.Substring(2)}: {pair.Value.Count}");
            var fruit = api.World.Blocks.OfType<BlockFruitTreeBranch>().FirstOrDefault(b => b.WorldGenConds != null)?.WorldGenConds ?? Array.Empty<FruitTreeWorldGenConds>();
            var unmappedFruit = fruit.Where(c => cat.FruitTree(c.Type) == null).Select(c => c.Type).ToArray();
            sb.AppendLine($"Fruit-tree types: {fruit.Length - unmappedFruit.Length} by 1318 range, {unmappedFruit.Length} vanilla climate" + (unmappedFruit.Length > 0 ? " (" + string.Join(", ", unmappedFruit) + ")" : ""));
            var fauna = api.ModLoader.GetModSystem<EarthFaunaSystem>();
            var providers = new HashSet<string>(fauna?.ControlledProviders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            VanillaFaunaRanges vanilla = null;
            if (core.Params.VanillaFaunaRanges >= 1)
            {
                var asset = api.Assets.TryGet(new AssetLocation("earth:config/fauna-vanilla.json"));
                var regions = api.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"));   // ranges given as regions (butterflies)
                try { vanilla = asset == null ? null : new VanillaFaunaRanges(asset.ToText(), regions == null ? null : BotanicalRegions.Shared(regions.ToText()), !core.Params.ChickensByRange, use1318: false); }
                catch (Exception e)
                {
                    vanilla = null;
                    Mod.Logger.Warning("Earth placement: fauna-vanilla.json can't be read ({0}); the inventory lists vanilla's animals as uncontrolled.", e.Message);
                }
            }
            var animals = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var t in api.World.EntityTypes)
            {
                if (t?.Code == null || t.Server?.SpawnConditions?.Worldgen == null && t.Server?.SpawnConditions?.Runtime == null) continue;
                string group = providers.Contains(t.Code.Domain) ? "regional fauna catalogue (EarthFauna)"
                    : t.Code.Domain == "game" && vanilla != null && vanilla.Covers(t.Code.Path) ? "vanilla ranges (decisions 0031, 0054)"
                    : "uncontrolled (climate only)";
                if (!animals.TryGetValue(group, out var l)) animals[group] = l = new List<string>();
                l.Add(t.Code.ToString());
            }
            sb.AppendLine("Naturally spawning animals:");
            foreach (var pair in animals) sb.AppendLine($"  {pair.Key}: {pair.Value.Count}");
            sb.AppendLine();
            foreach (var pair in groups)
            {
                sb.AppendLine("== Patches: " + pair.Key.Substring(2));
                foreach (string line in pair.Value) sb.AppendLine("  " + line);
            }
            foreach (var pair in animals)
            {
                sb.AppendLine("== Animals: " + pair.Key);
                foreach (string code in pair.Value) sb.AppendLine("  " + code);
            }
            string dir = Path.Combine(GamePaths.DataPath, "EarthMap");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "placement-inventory-" + api.WorldManager.Seed + ".txt");
            File.WriteAllText(file, sb.ToString());
            int uncontrolled = groups.TryGetValue("6 uncontrolled (climate only)", out var u) ? u.Count : 0;
            int mapped = groups.TryGetValue("1 placement catalogue (1318 range)", out var m) ? m.Count : 0;
            int wild = animals.TryGetValue("uncontrolled (climate only)", out var a) ? a.Count : 0;
            inventorySummary = $"{mapped} block patches by 1318 range, {uncontrolled} uncontrolled; {wild} naturally spawning animal types without a range. Inventory: {file}";
            Mod.Logger.Notification("Earth placement: {0}", inventorySummary);
        }

        // ---------------------------------------------------------------- commands

        /// <summary>The rain a fruit tree's climate envelope reads at (x, z): the climate's, or with GenFixRules the river belt's
        /// (TerrainModel.RiparianRain, as trees, animals, soil and the plant packs read it; CR-E1: peaches and pomegranates
        /// along the Nile were vetoed on the desert's rain).</summary>
        static double EnvelopeRain(TerrainModel model, double x, double z, double rain)
            => model.GenFixRules >= 1 ? model.RiparianRain(x, z, rain) : rain;

        /// <summary>/earth placement: the site, which catalogue taxa may grow here and why not, and this session's fruit trees.</summary>
        public string Describe(double x, double y, double z)
        {
            var s = state;
            if (s == null) return "Earth placement: " + status + ". Plants keep vanilla placement by climate.";
            var cat = s.Catalogue;
            var g = s.Model.TerrainGeoAt(x, z);
            if (g.InGutter || g.BeyondPole) return "Outside the Earth.";
            int region = cat.RegionAt(g.Lat, g.Lon);
            var cs = s.Model.Data.SampleClimate(g.Lat, g.Lon);
            double lapse = TerrainModel.Lapse(s.Model.Vertical, y);
            double temp = cs.TMean - lapse, cold = cs.TCold - lapse, rain = EnvelopeRain(s.Model, x, z, cs.Rain);
            var here = new List<string>();
            var climateOut = new List<string>();
            foreach (var t in cat.Taxa)
            {
                if (!cat.InRange(t, region)) continue;
                if (PlacementCatalogue.ClimateAllows(t, temp, cold, rain)) here.Add(t.Name);
                else climateOut.Add(t.Name);
            }
            string where = region >= 0 ? cat.Geography.Name(region) + " (" + cat.Geography.Code(region) + ")" : "outside the botanical countries";
            var placed = s.Placed.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value);
            return $"Placement as around {cat.Profile} at {g.Lat:F3}, {g.Lon:F3}: {where}; {temp:F1} °C yearly mean, coldest month {cold:F1} °C, rain {rain:F0}.\n" +
                $"In range and climate here: {(here.Count == 0 ? "none" : string.Join(", ", here))}.\n" +
                $"In range, climate outside: {(climateOut.Count == 0 ? "none" : string.Join(", ", climateOut))}.\n" +
                $"Fruit trees placed this session: {(s.Placed.IsEmpty ? "none" : string.Join(", ", placed))} ({s.Attempts.Value} attempts: {s.Infertile.Value} on ground of fertility 20 or less, {s.Empty.Value} found no type)." +
                (inventorySummary != null ? "\n" + inventorySummary : "");
        }

        /// <summary>/earth census: what actually generated within the radius of loaded chunks (surface plants, fruit trees, animals).</summary>
        public string Census(BlockPos centre, int radius)
        {
            var cat = catalogue;
            var world = api.World;
            var accessor = world.BlockAccessor;
            radius = Math.Clamp(radius, 8, 256);
            var plants = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var unmapped = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var ground = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var taxonByBlock = new Dictionary<int, PlacementCatalogue.Taxon>();
            var pos = new BlockPos(0, 0, 0, centre.dimension);
            for (int x = centre.X - radius; x <= centre.X + radius; x++)
            for (int z = centre.Z - radius; z <= centre.Z + radius; z++)
            {
                pos.Set(x, 0, z);
                int top = accessor.GetTerrainMapheightAt(pos);
                if (top <= 0) continue;
                pos.Y = top;
                var surface = accessor.GetBlock(pos);
                if (surface?.Code != null && surface.Id != 0)
                {
                    string family = surface.Code.Path;
                    int cut = family.IndexOf('-', family.IndexOf('-') + 1);
                    family = (cut > 0 ? family.Substring(0, cut) : family) + " (fertility " + surface.Fertility + ")";
                    ground[family] = ground.TryGetValue(family, out int g) ? g + 1 : 1;
                }
                for (int dy = 1; dy <= 2; dy++)
                {
                    pos.Y = top + dy;
                    var block = accessor.GetBlock(pos);
                    if (block?.Code == null || block.Id == 0) continue;
                    string path = block.Code.Path;
                    // any block the catalogue maps (crops, berries and wild plants such as the cacti); other mods' crops and bushes apart
                    if (!taxonByBlock.TryGetValue(block.Id, out var taxon)) taxonByBlock[block.Id] = taxon = cat?.ForBlock(block.Code.ToString());
                    if (taxon == null && !path.StartsWith("crop-", StringComparison.Ordinal) && !path.StartsWith("fruitingbush-", StringComparison.Ordinal)) continue;
                    var into = taxon != null ? plants : unmapped;
                    string key = taxon?.Name ?? block.Code.Domain + ":" + path.Substring(0, path.LastIndexOf('-') is int k and > 0 ? k : path.Length);
                    into[key] = into.TryGetValue(key, out int n) ? n + 1 : 1;
                    break;
                }
            }
            var trees = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int cy0 = 0, cy1 = world.BlockAccessor.MapSizeY / GlobalConstants.ChunkSize;
            for (int cx = (centre.X - radius) / GlobalConstants.ChunkSize; cx <= (centre.X + radius) / GlobalConstants.ChunkSize; cx++)
            for (int cz = (centre.Z - radius) / GlobalConstants.ChunkSize; cz <= (centre.Z + radius) / GlobalConstants.ChunkSize; cz++)
            for (int cy = cy0; cy < cy1; cy++)
            {
                var chunk = accessor.GetChunk(cx, cy, cz);
                if (chunk?.BlockEntities == null) continue;
                foreach (var be in chunk.BlockEntities.Values)
                {
                    if (be is not BlockEntityFruitTreeBranch branch || branch.RootOff is { } off && (off.X != 0 || off.Y != 0 || off.Z != 0)) continue;
                    if (Math.Abs(be.Pos.X - centre.X) > radius || Math.Abs(be.Pos.Z - centre.Z) > radius) continue;
                    string type = branch.TreeType ?? "?";
                    trees[type] = trees.TryGetValue(type, out int n) ? n + 1 : 1;
                }
            }
            var animals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in world.GetEntitiesAround(centre.ToVec3d(), radius, radius, e => e is not EntityPlayer && e.Alive && e.Properties?.Code != null))
            {
                string code = e.Code.Domain + ":" + e.Code.Path;
                animals[code] = animals.TryGetValue(code, out int n) ? n + 1 : 1;
            }
            static string List(SortedDictionary<string, int> d) => d.Count == 0 ? "none" : string.Join(", ", d.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value));
            string report = $"Census within {radius} blocks (loaded chunks only):\n" +
                $"Catalogue plants: {List(plants)}.\n" +
                $"Other crops and berry bushes: {List(unmapped)}.\n" +
                $"Fruit trees: {List(trees)}.\n" +
                $"Surface: {string.Join(", ", ground.OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " " + p.Value))}.\n" +
                $"Animals: {List(animals)}.";
            Mod.Logger.Notification("Earth placement census at {0},{1}: {2}", centre.X, centre.Z, report.Replace('\n', ' '));
            return report;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            if (instance == this) { state = null; instance = null; }
            base.Dispose();
        }
    }
}
