using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Earth-owned suitability data; providers continue to own blocks, recipes and valid hosts.
    /// v2: every bound variant's attempt factor is computed on the fly from Earth's evidence (vanilla's
    /// DepositVariant.GetOreMapFactor is post-patched), so cached, stale or missing region ore maps of older
    /// saves never change the rate. Save-safe: any problem disables only this subsystem for the session.
    /// </summary>
    public sealed class EarthOres : ModSystem
    {
        const string HarmonyId = "earth.regionalores";
        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        bool hooksBound, patchedFactor, patchedReload, patchedStrata;
        /// <summary>The deposit list the session bound (vanilla's GenDeposits.Deposits as Earth left it): a different list at
        /// a later set-up of the world's generators means vanilla made new generators (/wgen regen).</summary>
        DepositVariant[] boundDeposits;
        static EarthOres instance;
        /// <summary>Read by the Harmony hooks on world-generation threads; null when inactive.</summary>
        static volatile OreSession session;
        OreSession current;
        public bool Active => current != null;
        public RegionalOreCatalog Catalog => current?.Catalog;
        public OreTuning Tuning => current?.Tuning;
        internal OreSession Session => current;
        internal ILogger Log => Mod.Logger;
        public override double ExecuteOrder() => .40;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api; core = api.ModLoader.GetModSystem<EarthMapSystem>();
            instance = this;
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, InitWorld);
            // Runs after GenDeposits.initWorldGen (registered earlier, same world type): vanilla has built its map layers.
            api.Event.InitWorldGenerator(AfterNativeWorldGenInit, "standard");
        }

        void InitWorld()
        {
            // 0060: Earth's layer deposits only in ore-layer worlds (EarthGeology made its layers earlier in this phase);
            // Earth's ore and gem deposits only in worlds of their deposit set
            try
            {
                var p = core?.Params;
                int set = EarthOreLayers.DepositSetOf(p);
                if (p?.Enabled == true && p.DepositSet != set)
                {
                    // an RC or dev world made before the field: its set from its other fields, saved so it never changes
                    p.DepositSet = set;
                    core.SaveParams();
                }
                EarthOreLayers.PruneDeposits(api, api.ModLoader.GetModSystem<GenDeposits>(), api.ModLoader.GetModSystem<EarthGeology>()?.Layers != null, set);
                if (p?.Enabled == true && set < EarthDepositSet.Version)
                    Mod.Logger.Notification("Earth ores: this world was made before Earth's own ore and gem deposits; they stay out of it, so its other deposits keep their places in new chunks.");
            }
            catch (Exception e) { Mod.Logger.Warning("Earth ores: sorting out Earth's own deposits failed ({0}).", e.Message); }
            try { DropDuplicateAuthoredDeposits(api.ModLoader.GetModSystem<GenDeposits>()); }
            catch (Exception e) { Mod.Logger.Warning("Earth ores: checking Earth's own deposits against provider deposits failed ({0}).", e.Message); }
            try { Activate(); }
            catch (Exception e)
            {
                Deactivate();
                Mod.Logger.Error("Earth regional ores disabled for this session after an unexpected error; vanilla deposit generation is used. {0}", e);
            }
        }

        void Activate()
        {
            Deactivate();
            var p = core?.Params;
            if (p?.Enabled != true || !p.RegionalOres || core.Model == null) return;
            // OreRulesVersion is a fix level: EarthMapSystem moved it to RegionalOreCatalog.Version (and logged it, or warned
            // about a newer one) before this phase; p.OreRules is the generation version that changes behaviour
            foreach (string id in new[] { "advancedgeology", "interestingoregen", "rockstratavariety" })
                if (api.ModLoader.IsModEnabled(id))
                {
                    Mod.Logger.Warning("Earth regional ores: {0} also owns ore generation; Earth's ore layer is inactive this session (vanilla/{0} deposits are used).", id);
                    return;
                }
            var gen = api.ModLoader.GetModSystem<GenDeposits>();
            if (gen?.Deposits == null) { Mod.Logger.Warning("Earth regional ores: native deposit generator unavailable; Earth's ore layer is inactive this session."); return; }
            // EarthVerticalBootstrap rescaled the sea-level deposits already; repeated here (a no-op then) because the
            // generators below copy their heights (decision 0045).
            string rescaled = SealevelDeposits.Rescale(api, TerraGenConfig.seaLevel);
            if (rescaled != null) Mod.Logger.Warning(rescaled + " (late, at the ore layer)");
            // 0065: rules v4 read the rebaked heat maps when they ship, else v1; older worlds always v1
            var asset = p.OreRules >= OreBackground.CatalogueRules ? api.Assets.TryGet(new AssetLocation("earth:config/ore-heatmaps-v2.json")) : null;
            if (asset == null) asset = api.Assets.TryGet(new AssetLocation("earth:config/ore-heatmaps-v1.json"));
            if (asset == null) { Mod.Logger.Error("Earth regional ores: earth:config/ore-heatmaps-v1.json is missing; Earth's ore layer is inactive this session."); return; }
            RegionalOreCatalog catalog;
            try { catalog = new RegionalOreCatalog(asset.ToText()); }
            catch (Exception e) { Mod.Logger.Error("Earth regional ores: heat-map data is corrupt or unsupported ({0}); Earth's ore layer is inactive this session.", e.Message); return; }
            var tuning = OreTuning.Load(p.OreSettingsJson, catalog.Layers.Keys, out string problem);
            if (problem != null) Mod.Logger.Error("Earth regional ores: {0}.", problem);
            // compared after loading both, so a setting added in a later version (at its default) is no difference
            string configured = JsonSerializer.Serialize(core.Config?.Ores ?? new OreTuning());
            if (configured != JsonSerializer.Serialize(tuning)) Mod.Logger.Warning("Earth ores uses this world's saved tuning. Set UpgradeExistingWorldOreRules=true to accept current settings for new generation.");
            OreVanilla.Report(Mod.Logger);
            // without the providers' host tables nothing can be bound (OreVanilla said so)
            if (!OreVanilla.HostTables) return;
            if (!PatchHooks()) return;
            var geo = api.ModLoader.GetModSystem<EarthGeology>();
            // rules v4 place their deposit catalogue's bodies after regional geology's deposit layers, an event raised only from
            // its hook on vanilla's deposit pass: without that hook the catalogue would never place while the background stays
            // at the floor rules v4 give it beside the catalogue, so Earth's ore layer is off instead (vanilla's deposits)
            if (OreRulesNeedDepositColumns(p.OreRules) && geo?.DepositColumnsHook != true)
            {
                Mod.Logger.Warning("Earth ores rules v{0}: regional geology's hook after vanilla's deposit pass (GenDeposits.GenChunkColumn) is unavailable, " +
                    "so the deposit catalogue can't place its bodies; Earth's ore layer is off this session: no catalogue, no rules v4 background, vanilla deposits as they come.", p.OreRules);
                return;
            }
            // the same end state when the ore layers the catalogue's bodies are laid in are missing this session (regional geology
            // inactive, or its ground or layers could not be made): no catalogue, no rules v3 bodies and the background at its
            // floor would give every chunk generated now floor-only ore for good (CR-O1), so the ore layer is off here too
            if (OreRulesNeedDepositColumns(p.OreRules) && (geo?.Active != true || geo.Layers == null))
            {
                Mod.Logger.Warning("Earth ores rules v{0}: this world's ore layers are off this session (regional geology {1}), so the deposit catalogue can't place its bodies; " +
                    "Earth's ore layer is off this session: no catalogue, no rules v4 background, vanilla deposits as they come.", p.OreRules,
                    geo?.Active != true ? "is inactive" : "has no ore layers");
                return;
            }
            if (geo != null) { geo.AfterGround -= AfterGround; geo.AfterGround += AfterGround; geo.AfterDepositLayers -= AfterDepositLayers; geo.AfterDepositLayers += AfterDepositLayers; }
            var s = OreBinder.Bind(this, api, core, gen, catalog, tuning, patchedStrata);
            if (s == null) return;
            boundDeposits = gen.Deposits;
            AttachCatalogue(s, gen);
            try { UpdateFingerprint(p, s); }
            catch
            {
                s.Undo();
                throw;
            }
            current = s; session = s;
            // D16: one series of ore rules in the logs, "rules v3" for worlds before v4 (EarthParams.OreRules 0); the heat maps'
            // fix level (RegionalOreCatalog.Version, EarthParams.OreRulesVersion) is named as such
            int rules = s.RulesV4 ? s.Rules : 3;
            Mod.Logger.Notification("Earth ores rules v{0}: {1} heat layers, {2} bound deposit variants ({3}); {4}, fix level {5}. Background is a host-gated gameplay probability, not surveyed absence.",
                rules, catalog.Layers.Count, s.Bindings.Count,
                string.Join(", ", s.Bindings.GroupBy(b => b.Class).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())), catalog.DatasetId, RegionalOreCatalog.Version);
            Mod.Logger.Notification("Earth ores tuning (saved): map weights background {5} and evidence {6}; background {0}, frequency {1}, radius {2}, thickness {3}, concentration {4}; built-in classes: soil/quartz and surface starters at vanilla attempts (+evidence), Cu/Sn/Fe/coal progression floors, standard = 0.12 effective rate; rules v{7}: host search within the depth window, {8}.",
                tuning.BackgroundChance, tuning.FrequencyMultiplier, tuning.RadiusMultiplier, tuning.ThicknessMultiplier, tuning.Concentration, tuning.BackgroundWeight, tuning.EvidenceWeight,
                rules, s.Layers != null ? "no deposit fills rock that is not its host (ore layers)" : "known districts fill any rock");
            Mod.Logger.Notification("Earth ores resources with no installed generator: {0}", string.Join(", ", catalog.Layers.Keys.Except(s.Bindings.Select(b => b.Resource)).OrderBy(x => x, StringComparer.Ordinal)));
            if (s.Unsupported.Count > 0) Mod.Logger.Warning("Earth ores: unsupported provider generator classes keep native behaviour: {0}", string.Join(", ", s.Unsupported.Distinct().OrderBy(x => x, StringComparer.Ordinal)));
            if (s.BogPeatActive) Mod.Logger.Notification("Earth ores: peat-based bog iron active; provider rocky bog iron disabled.");
            if (s.RulesV4) Mod.Logger.Notification("Earth ores rules v{0} (0065): {1}; no rules v3 ore bodies; background at its class floor in its host rock everywhere (no record is not no ore), the heat raising only quartz veins, clay, peat, bog iron and salt flats; its deposits {2:0.##}x the 1:100 radius with attempts to match.",
                s.Rules, s.Deposits != null ? "deposit catalogue on" : "no deposit catalogue", OreBackground.RadiusScale(s.Rules, s.MetresPerBlock, 0));
            Mod.Logger.Notification("Earth ores: rules v3 ore bodies {0}; world deposit rate x{1:0.##}.",
                s.Bodies.Active ? string.Join(", ", s.Bodies.Beds.Select(b => string.Format(CultureInfo.InvariantCulture, "{0} x{1:0.##} (evidence {2:0.##}-{3:0.##})", b.Set.Name, b.Richness, b.Set.From, b.Set.To)))
                : s.RulesV4 ? "none (rules v4 place the catalogue's instead)" : "off", s.DepositRate);
            if (s.Layers != null) Mod.Logger.Notification("Earth ores: ore layers (0060) on: every block asks its ground layer, no deposit fills rock that is not its host, and {0}; the maps scale only how much.",
                s.RulesV4 ? "the catalogue's bodies are laid after the layer pass" : "rules v3's ore bodies are laid after the ground pass");
            if (!patchedReload) Mod.Logger.Warning("Earth ores: /wgen regen hook unavailable; after a world-generator reload Earth's ore layer is off until restart (vanilla's new deposit generators as they come).");
        }

        /// <summary>Rules v4: the deposit catalogue (0065), in its ore layers; logged and left off when it can't be.</summary>
        void AttachCatalogue(OreSession s, GenDeposits gen)
        {
            if (!s.RulesV4) return;
            if (s.Layers == null) { Mod.Logger.Warning("Earth ores: this world has ore rules v{0} but its ore layers are off this session; the catalogue's bodies are left out.", s.Rules); return; }
            try { s.Deposits = EarthOreDeposits.TryCreate(api, s, core, api.ModLoader.GetModSystem<EarthGeology>(), gen, Mod.Logger); }
            catch (Exception e) { s.Deposits = null; Mod.Logger.Error("Earth ore deposits are off this session after an unexpected error; the background is used. {0}", e); }
        }

        bool PatchHooks()
        {
            harmony ??= new Harmony(HarmonyId);
            // the ore map factor (required), the /wgen regen hook and the rock strata hook: PatchVanilla's OreTable, bound once
            // (each off on its own, and logged once, when this game version lacks or changed its method)
            if (!hooksBound)
            {
                hooksBound = true;
                var patches = PatchVanilla.For(api, Mod, harmony);
                patches.Apply(PatchVanilla.OreTable());
                patchedFactor = patches.On(PatchVanilla.Feature.OreMapFactor);
                patchedReload = patches.On(PatchVanilla.Feature.OreReload);
                patchedStrata = patches.On(PatchVanilla.Feature.OreStrata);
            }
            return patchedFactor;
        }

        /// <summary>
        /// Earth's own ore and gem deposits (assets/earth/worldgen/deposits; in worlds of their deposit set, EarthDepositSet) spawn
        /// in vanilla ore mode as ordinary deposits. When a
        /// provider mod (for example Geology Additions) defines a deposit with the same code, Earth's copy gets no attempts, so the
        /// provider's wins and nothing is generated twice. Runs before the heat-map binding, which then sees tries 0 and a sealed
        /// duplicate-free list; logged once per session.
        /// </summary>
        void DropDuplicateAuthoredDeposits(GenDeposits gen)
        {
            if (gen?.Deposits == null) return;
            var providers = OtherModsCodes(gen.Deposits);
            if (providers.Count == 0) return;
            var dropped = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var v in gen.Deposits)
                if (v.Code != null && YieldsTo(v, providers) && v.TriesPerChunk > 0)
                {
                    v.TriesPerChunk = 0;
                    dropped.Add(v.Code);
                }
            if (dropped.Count > 0 && !loggedDuplicates)
            {
                loggedDuplicates = true;
                Mod.Logger.Notification("Earth ores: another mod already defines deposit(s) {0}; Earth's own copies of them are switched off.", string.Join(", ", dropped));
            }
        }
        bool loggedDuplicates;

        /// <summary>Where Earth's own deposit assets come from (vanilla's DepositVariant.fromFile prefix).</summary>
        internal const string OwnDepositFiles = "earth:worldgen/deposits/";
        /// <summary>The deposit codes a deposit file that is not Earth's defines (vanilla's or another mod's).</summary>
        internal static HashSet<string> OtherModsCodes(IEnumerable<DepositVariant> deposits)
            => new(deposits.Where(v => v?.Code != null && v.fromFile != null && !v.fromFile.StartsWith("earth:", StringComparison.Ordinal)).Select(v => v.Code), StringComparer.Ordinal);
        /// <summary>Earth's own copy of a code another file defines (<paramref name="others"/>, from <see cref="OtherModsCodes"/>):
        /// it gives way, here (no attempts) and in the deposit catalogue (EarthOreDeposits.Providers: not its blocks).</summary>
        internal static bool YieldsTo(DepositVariant v, HashSet<string> others)
            => v.fromFile != null && v.fromFile.StartsWith(OwnDepositFiles, StringComparison.Ordinal) && others.Contains(v.Code);

        void Deactivate() { if (current != null && session == current) session = null; current = null; }

        void UpdateFingerprint(EarthParams p, OreSession s)
        {
            // Informational only: generation-relevant content, no game/mod versions, no provider attribute text.
            var lines = new List<string> { "rules=" + RegionalOreCatalog.Version, "data=" + s.Catalog.ContentHash, "tuning=" + JsonSerializer.Serialize(s.Tuning) };
            // rules v4: the ore rules and the deposit catalogue (older worlds' fingerprints are unchanged)
            if (s.RulesV4) lines.Add("orerules=" + s.Rules + "|catalogue=" + (s.Deposits?.Catalog.ContentHash ?? "none"));
            lines.AddRange(s.Bindings.Select(b => b.Variant.Code + "|" + b.Resource + "|" + b.Class + "|" + (b.IsChild ? "child" : "parent") + "|" + string.Join(",", b.Generator.HostSignature)));
            string fingerprint = Hash(string.Join("\n", lines.OrderBy(x => x, StringComparer.Ordinal)));
            if (p.OreDataSignature == fingerprint) return;
            if (!string.IsNullOrEmpty(p.OreDataSignature))
                Mod.Logger.Warning("Earth ores: heat-map data, rules or provider host mapping differ from this world's previous run. New chunks use the current mapping; existing deposits are unchanged.");
            p.OreDataSignature = fingerprint;
            core.SaveParams();
        }
        static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

        /// <summary>Whether a world's ore rules place a deposit catalogue (rules v4), whose bodies need regional geology's hook
        /// after vanilla's deposit pass.</summary>
        internal static bool OreRulesNeedDepositColumns(int oreRules) => oreRules >= OreBackground.CatalogueRules;

        /// <summary>
        /// Whether vanilla made new deposit generators since the session bound its own (<paramref name="bound"/>: the list Earth
        /// left; <paramref name="now"/>: vanilla's list now) and Earth's reload hook did not re-bind them: then the session must
        /// go (its generators are no longer vanilla's).
        /// </summary>
        internal static bool ReloadedWithoutHook(bool reloadHook, DepositVariant[] bound, DepositVariant[] now) => !reloadHook && bound != null && now != bound;

        void AfterNativeWorldGenInit()
        {
            var s = current;
            if (s == null) return;
            // /wgen regen: vanilla reloads its deposit generators (GenDeposits.reloadWorldGen) and then sets the world's
            // generators up again (this event). Without Earth's reload hook nothing re-bound the session: it goes, wholly (its
            // bauxite, bodies, catalogue and attempt factors with it), and vanilla's new generators work as they come
            if (ReloadedWithoutHook(patchedReload, boundDeposits, api.ModLoader.GetModSystem<GenDeposits>()?.Deposits))
            {
                Deactivate();
                boundDeposits = null;
                Mod.Logger.Warning("Earth ores: vanilla re-made its deposit generators (/wgen regen) and Earth's reload hook is unavailable on this game version; Earth's ore layer is off until restart (vanilla deposits as they come).");
                return;
            }
            try { OreBinder.AfterNativeInit(s); }
            catch (Exception e)
            {
                // Before any chunk is generated: give the deposits back to vanilla rather than run half-adjusted
                Deactivate();
                s.Undo();
                Mod.Logger.Error("Earth regional ores disabled for this session: the post-init adjustment failed; vanilla deposit generation is used. {0}", e);
            }
        }

        internal static void AfterReload(GenDeposits __instance)
        {
            var self = instance;
            if (self == null) return;
            try { EarthOreLayers.PruneDeposits(self.api, __instance, self.api.ModLoader.GetModSystem<EarthGeology>()?.Layers != null, EarthOreLayers.DepositSetOf(self.core?.Params)); }
            catch (Exception e) { self.Mod.Logger.Warning("Earth ores: sorting out Earth's own deposits after the reload failed ({0}).", e.Message); }
            try { if (__instance == self.api.ModLoader.GetModSystem<GenDeposits>()) self.DropDuplicateAuthoredDeposits(__instance); }
            catch (Exception e) { self.Mod.Logger.Warning("Earth ores: checking Earth's own deposits after the reload failed ({0}).", e.Message); }
            if (self.current == null) return;
            try
            {
                if (__instance != self.api.ModLoader.GetModSystem<GenDeposits>()) return;
                var old = self.current;
                self.Deactivate();
                var s = OreBinder.Bind(self, self.api, self.core, __instance, old.Catalog, old.Tuning, self.patchedStrata);
                if (s == null) return;
                // rules v4: the new binding gets its deposit catalogue too, as at the start (the reload made new generators)
                self.AttachCatalogue(s, __instance);
                try
                {
                    __instance.initWorldGen(); // rebuild vanilla map layers (prospecting keys) for the re-bound variants
                    OreBinder.AfterNativeInit(s);
                }
                catch
                {
                    s.Undo();
                    // vanilla's layers again for its own generators (the failed run may have stopped half way)
                    try { __instance.initWorldGen(); }
                    catch (Exception e) { self.Mod.Logger.Error("Earth ores: rebuilding vanilla's deposit layers after the failed re-binding failed too. {0}", e); }
                    throw;
                }
                self.current = s; session = s;
                self.boundDeposits = __instance.Deposits;
                self.Mod.Logger.Notification("Earth ores: re-bound {0} deposit variants after the world-generator reload.", s.Bindings.Count);
            }
            catch (Exception e)
            {
                self.Deactivate();
                self.Mod.Logger.Error("Earth ores: re-binding after the world-generator reload failed; vanilla deposits until restart. {0}", e);
            }
        }

        // (CR-O4) the hooks' error latches are the session's (OreSession.FactorErrors and the rest), so each world logs its first error
        internal static void FactorPostfix(DepositVariant __instance, int __0, int __1, ref float __result)
        {
            var s = session;
            if (s == null || __instance?.GeneratorInst is not EarthOreGenerator g || g.Session != s) return;
            try { __result = (float)s.AttemptFactor(g.Binding, __0, __1); }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref s.FactorErrors) == 1) instance?.Mod.Logger.Error("Earth ores attempt factor failed for {0}; vanilla value used. {1}", __instance.Code, e);
            }
        }

        /// <summary>After vanilla's rock strata: the bauxite strata replacement and, without ore layers, rules v3's ore bodies
        /// (each where the binding chose it).</summary>
        internal static void AfterStrata([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var s = session;
            if (s == null) return;
            var bauxite = s.Bauxite;
            if (bauxite != null)
            {
                try { bauxite.Replace(request); }
                catch (Exception e)
                {
                    if (Interlocked.Increment(ref s.StrataErrors) == 1) instance?.Mod.Logger.Error("Earth ores bauxite strata replacement failed; continuing. {0}", e);
                }
            }
            var bodies = s.BodiesAfterStrata;
            if (bodies != null)
            {
                try { bodies.Place(request); }
                catch (Exception e)
                {
                    if (Interlocked.Increment(ref s.SeamErrors) == 1) instance?.Mod.Logger.Error("Earth ores coal measures failed in a chunk; continuing without them there. {0}", e);
                }
            }
        }

        /// <summary>0065: the deposit catalogue's bodies after the deposit pass and its layer pass (rules v4).</summary>
        static void AfterDepositLayers(IChunkColumnGenerateRequest request)
        {
            var s = session;
            var d = s?.Deposits;
            if (d == null) return;
            try { d.Place(request); }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref s.DepositErrors) == 1) instance?.Mod.Logger.Error("Earth ore deposits failed in a chunk; continuing without them there. {0}", e);
            }
        }

        /// <summary>0060: in ore-layer worlds rules v3's beds go in after the ground pass, into hosts in layers they may use.</summary>
        static void AfterGround(IChunkColumnGenerateRequest request)
        {
            var s = session;
            var bodies = s?.BodiesAfterGround;
            if (bodies == null) return;
            try { bodies.Place(request); }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref s.SeamErrors) == 1) instance?.Mod.Logger.Error("Earth ores coal measures failed in a chunk; continuing without them there. {0}", e);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            var geology = api?.ModLoader.GetModSystem<EarthGeology>();
            if (geology != null) { geology.AfterGround -= AfterGround; geology.AfterDepositLayers -= AfterDepositLayers; }
            Deactivate();
            if (instance == this) instance = null;
            session = null;
        }

        public string Describe(double x, double z)
        {
            var s = current;
            if (s == null) return "Regional ores are inactive in this session.";
            return s.Describe(x, z);
        }
        public string GenerationTotals() => current?.GenerationTotals() ?? "inactive";
    }
}
