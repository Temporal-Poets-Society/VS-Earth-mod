using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Makes an <see cref="OreSession"/> once per world (and again after a world-generator reload): appends Earth's authored
    /// adapters, wraps every supported provider variant in an <see cref="EarthOreGenerator"/>, seals the session (codes, bog
    /// peat, rules v3's ore bodies) and swaps the generators in, keeping the rollback. After the seal nothing in the session's
    /// bindings changes but the one-time base-mean refinement (<see cref="AfterNativeInit"/>).
    /// </summary>
    sealed class OreBinder
    {
        readonly EarthOres owner;
        readonly ICoreServerAPI api;
        readonly ILogger log;
        readonly OreSession s;

        OreBinder(EarthOres owner, ICoreServerAPI api, OreSession s) { this.owner = owner; this.api = api; log = owner.Log; this.s = s; }

        /// <summary>
        /// Wraps supported deposit variants. Mutates the generator only after every binding succeeded; rolls back on failure. The
        /// returned session keeps the rollback (OreSession.Undo) for a failure of the steps after binding (vanilla's
        /// initWorldGen, AfterNativeInit, saving the fingerprint): the native generators, ore-map flags, bog-iron tries and the
        /// deposit list come back as they were, so a failed activation really leaves vanilla deposits. <paramref name="strataHook"/>:
        /// the strata hook is in place (bauxite strata and rules v3's ore bodies need it). Null (logged) when nothing could be bound.
        /// </summary>
        internal static OreSession Bind(EarthOres owner, ICoreServerAPI api, EarthMapSystem core, GenDeposits gen, RegionalOreCatalog catalog, OreTuning tuning, bool strataHook)
        {
            var geology = api.ModLoader.GetModSystem<EarthGeology>();
            var s = new OreSession(owner, api, core, catalog, tuning, geology);
            var binder = new OreBinder(owner, api, s);
            var undo = new List<Action>();
            var originalDeposits = gen.Deposits;
            try
            {
                var variants = gen.Deposits.ToList();
                // the world's globalDepositSpawnRate, which vanilla multiplies into every variant's attempts (worldgen/deposits.json)
                if (OreVanilla.DepositRate(api) is float rate && float.IsFinite(rate) && rate >= 0) s.DepositRate = rate;
                // vanilla's bauxite strata go where Earth's bauxite is a host-gated deposit instead (needs the strata hook)
                bool replaceBauxite = strataHook && s.BauxiteId > 0;
                // 0060: ore-layer worlds have bauxite in laterite and gypsum in evaporite beds as deposits under both choices
                bool layered = s.Layers != null;
                foreach (var a in Adapters)
                    if ((a.LayerWorlds || !layered) && (!a.BauxiteStrata || replaceBauxite))
                        binder.AddMissing(variants, a.Code, a.Place, a.Hosts, a.Tries, a.Radius, a.Thickness, a.MinDepth, a.MaxDepth);
                foreach (var v in variants) binder.BindTree(v, null);
                if (!layered) replaceBauxite &= s.Bindings.Any(b => b.Variant.Code == "earth-bauxite");
                binder.Seal();
                // where each rules generation's part runs, chosen here once for the session
                if (replaceBauxite) s.Bauxite = new BauxiteStrata(s);
                if (!strataHook) s.Bodies.Beds.Clear();
                if (s.Bodies.Active) { if (layered) s.BodiesAfterGround = s.Bodies; else s.BodiesAfterStrata = s.Bodies; }
                foreach (var b in s.Bindings)
                {
                    var v = b.Variant; var gi = v.GeneratorInst; bool map = v.WithOreMap;
                    undo.Add(() => { v.GeneratorInst = gi; v.WithOreMap = map; });
                    v.GeneratorInst = b.Generator;
                    v.WithOreMap = true; // vanilla then asks GetOreMapFactor, which Earth answers on the fly
                }
                // Provider rocky bog iron is replaced only when Earth's finite bog peat is really generated.
                if (s.BogPeatActive)
                    foreach (var v in variants.Where(v => v.Code is "bogironlarge" or "bogironsmall"))
                    {
                        float tries = v.TriesPerChunk; undo.Add(() => v.TriesPerChunk = tries);
                        v.TriesPerChunk = 0;
                    }
                gen.Deposits = variants.ToArray();
                if (s.Bindings.Count == 0) return Fail("no deposit variant could be bound");
                s.Undo = Restore;
                return s;
            }
            catch
            {
                Restore();
                throw;
            }
            void Restore()
            {
                for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
                undo.Clear();
                gen.Deposits = originalDeposits;
            }
            OreSession Fail(string why)
            {
                Restore();
                owner.Log.Warning("Earth regional ores: {0}; Earth's ore layer is inactive this session.", why);
                return null;
            }
        }

        /// <summary>
        /// Earth's authored adapters for resources vanilla lacks (decision 0006, 0.12's tries; in this order). They get a
        /// default vanilla ore map, so the standard rule (mean 0.0705 x 3.6 compensation) reproduces their 0.12 effective
        /// attempt rate. Bauxite lenses (only where vanilla's bauxite strata are replaced) and gypsum are for worlds without ore
        /// layers; 0060 worlds have them as layer deposits (earth-layers.json, other sizes). Made here and appended after the
        /// deposit list, never shipped as worldgen/deposits assets: vanilla sorts those by key, earth: before game:, so they
        /// would draw first from the per-chunk random stream and move every vanilla deposit (Core.EarthDepositSet).
        /// Wolframite is Earth's own deposit since 0065 (earth-ores.json): vanilla 1.22.7 has no wolframite block to adapt.
        /// Radius, thickness and depths below the surface in blocks.
        /// </summary>
        static readonly (string Code, string Place, string[] Hosts, float Tries, float Radius, int Thickness, float MinDepth, float MaxDepth, bool LayerWorlds, bool BauxiteStrata)[] Adapters =
        {
            ("earth-bauxite", "rock-bauxite", new[] { "claystone", "granite", "basalt", "andesite", "limestone" }, .3f, 12, 3, 2, 15, false, true),
            ("uranium", "ore-*-uranium-{rock}", new[] { "granite", "sandstone", "claystone", "shale" }, 1, 8, 2, 45, 100, true, false),
            ("corundum", "ore-corundum-{rock}", new[] { "basalt", "peridotite", "slate", "phyllite" }, .4f, 5, 2, 15, 70, true, false),
            ("earth-gypsum", "rock-gypsum", new[] { "limestone", "dolostone", "chalk", "claystone", "shale", "sandstone" }, .4f, 10, 2, 5, 60, false, false),
        };

        void AddMissing(List<DepositVariant> variants, string code, string place, string[] hosts, float tries, float radius, int thickness, float minDepth, float maxDepth)
        {
            if (variants.Any(v => v.Code == code)) return;
            try
            {
                var valid = hosts.Where(h => api.World.GetBlock(new AssetLocation("game:rock-" + h))?.Id > 0 && api.World.SearchBlocks(new AssetLocation("game:" + place.Replace("{rock}", h))).Any(b => b.Id > 0)).ToArray();
                if (valid.Length == 0) return;
                var body = new
                {
                    inblock = new { code = "game:rock-*", name = "rock", allowedVariants = valid },
                    placeblock = place.Contains('*') ? (object)new { code = "game:" + place, name = "grade", allowedVariants = new[] { "poor", "medium", "rich", "bountiful" } } : new { code = "game:" + place },
                    radius = new { dist = "uniform", avg = radius, var = radius * .25 },
                    thickness = new { dist = "uniform", avg = thickness, var = 1 },
                    depth = new { dist = "uniform", avg = (minDepth + maxDepth) / 2, var = (maxDepth - minDepth) / 2 }
                };
                var v = new DepositVariant
                {
                    Code = code, Generator = "disc-followsurfacebelow", TriesPerChunk = tries, ScaleWithWorldheight = false,
                    WithOreMap = true, fromFile = AdapterFile,
                    Attributes = new Vintagestory.API.Datastructures.JsonObject(Newtonsoft.Json.Linq.JToken.Parse(JsonSerializer.Serialize(body)))
                };
                v.Init(api, new LCGRandom(api.WorldManager.Seed + 34613L), NormalizedSimplexNoise.FromDefaultOctaves(3, .1, .9, 1));
                if (v.GeneratorInst == null) return;
                variants.Add(v);
            }
            catch (Exception e) { log.Warning("Earth ores: authored {0} deposit unavailable ({1}).", code, e.Message); }
        }
        /// <summary>The fromFile of Earth's authored adapters.</summary>
        internal const string AdapterFile = "earth:authored-ore-adapters";

        static bool Supported(DepositGeneratorBase g, bool child)
        {
            if (g == null) return false;
            var t = g.GetType();
            return child ? t == typeof(ChildDepositGenerator)
                : t == typeof(FollowSurfaceDiscGenerator) || t == typeof(FollowSurfaceBelowDiscGenerator) || t == typeof(FollowSealevelDiscGenerator) || t == typeof(AnywhereDiscGenerator);
        }

        void BindTree(DepositVariant v, DepositVariant parentVariant, OreBinding parent = null, bool isChild = false)
        {
            OreBinding b = null;
            string res = s.Catalog.ResourceFor(v.Code);
            // 0060: Earth's layer deposits are scaled by their resource's map like any other deposit
            if (res == null && EarthOreLayers.IsLayerDeposit(v) && EarthOreLayers.ResourceOf(v.Code) is string alias && s.Catalog.Layers.ContainsKey(alias)) res = alias;
            if (res != null)
            {
                if (!Supported(v.GeneratorInst, isChild)) s.Unsupported.Add(v.Code + " (" + (v.GeneratorInst?.GetType().Name ?? "no generator") + ")");
                else
                {
                    try { b = CreateBinding(v, parentVariant, res, parent, isChild); }
                    catch (Exception e) { log.Warning("Earth ores: {0} keeps native behaviour ({1}).", v.Code, e.Message); b = null; }
                }
            }
            var childLayers = new List<int>();
            foreach (var child in v.ChildDeposits ?? Array.Empty<DepositVariant>())
            {
                int before = s.Bindings.Count;
                BindTree(child, v, b, true);
                if (b != null && s.Bindings.Count > before && s.Bindings[^1].Variant == child) childLayers.Add(s.Bindings[^1].Layer);
            }
            if (b != null) b.HeatLayers = new[] { b.Layer }.Concat(childLayers).Distinct().ToArray();
        }

        OreBinding CreateBinding(DepositVariant v, DepositVariant parentVariant, string res, OreBinding parent, bool isChild)
        {
            // 0060: layer deposits keep their native attempts, depth and size; the map adds up to the soil boost where the
            // resource's evidence is (they have no vanilla ore map to compensate, and no ore bodies)
            var cls = EarthOreLayers.IsLayerDeposit(v) ? OreClass.Soil : OreRules.Classify(v.Code, res);
            var b = new OreBinding
            {
                Id = s.Bindings.Count, Variant = v, Resource = res, Layer = s.Catalog.LayerIndex(res), Class = cls,
                Parent = parent, ChildOfUnbound = isChild && parent == null,
                Profile = s.Tuning.For(res, cls, isChild),
                NativelyMapped = v.WithOreMap,
                EarthAuthored = v.fromFile == AdapterFile,
                KeepsNativeDepth = OreRules.KeepsNativeDepth(v.Code, cls),
                TextureSalt = RegionalOreCatalog.StableCode(v.Code),
                MapScale = (parentVariant ?? v).OreMapScale, MapContrast = (parentVariant ?? v).OreMapContrast, MapSub = (parentVariant ?? v).OreMapSub,
                Totals = s.Totals.Of(res),
            };
            b.HeatLayers = new[] { b.Layer };
            b.Clustered = b.NativelyMapped && (cls is OreClass.Standard or OreClass.Progression);
            if (!b.NativelyMapped) b.BaseMean = 1;
            else
            {
                double m = OreRules.VanillaOreMapMean(b.MapScale, b.MapContrast, b.MapSub);
                b.BaseMean = double.IsNaN(m) ? .0705 : m; // refined from the live layer in AfterNativeInit
            }
            b.Generator = new EarthOreGenerator(owner, s, api, v, (DiscDepositGenerator)v.GeneratorInst, b);
            b.HostSearch = !isChild && (cls is OreClass.Standard or OreClass.Progression or OreClass.Veins) && b.Generator.RockHosted && !b.KeepsNativeDepth;
            // rules v3 without ore layers only: in ore-layer worlds no deposit fills rock that is not its host
            b.Lens = s.Layers != null || isChild || !b.Generator.RockHosted ? 0 : cls == OreClass.Surface ? 2 : cls is OreClass.Standard or OreClass.Progression && !b.KeepsNativeDepth ? 1 : 0;
            s.Bindings.Add(b);
            if (res == "peat" && !isChild) s.Peat = b;
            return b;
        }

        /// <summary>The bindings by code, bog peat, and rules v3's ore bodies; the session's shared data is fixed from here on.</summary>
        void Seal()
        {
            s.ByCode = s.Bindings.GroupBy(b => b.Variant.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            s.BogPeatActive = s.BogPeatId > 0 && s.Peat != null;
            s.Bodies.Build();
        }

        /// <summary>One-time, before world generation starts (InitWorldGenerator, after vanilla built its layers).</summary>
        internal static void AfterNativeInit(OreSession s)
        {
            foreach (var b in s.Bindings)
            {
                // Formerly unmapped variants: Earth supplies the whole factor; vanilla must not build region maps for them.
                if (!b.NativelyMapped) { b.Variant.OreMapLayer = null; continue; }
                if (b.EarthAuthored || !double.IsNaN(OreRules.VanillaOreMapMean(b.MapScale, b.MapContrast, b.MapSub)) || b.Variant.OreMapLayer == null) continue;
                // Provider with unusual map settings: measure its vanilla mean once (deterministic: fixed sample windows).
                double sum = 0; long n = 0;
                for (int i = 0; i < 12; i++)
                {
                    int[] data = b.Variant.OreMapLayer.GenLayer(i * 977 - 5000, i * 613 + 3000, 33, 33);
                    foreach (int d in data) { sum += (d & 255) / 255.0; n++; }
                }
                if (n > 0 && sum > 0) b.BaseMean = sum / n;
            }
        }
    }
}
