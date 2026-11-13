using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Optional geographic veto on natural spawn eligibility for mapped fauna providers; never subscribes to generic entity creation.
    /// Fails safe: any missing data, provider, hook or API change leaves natural spawning exactly as vanilla/the providers define it.
    /// </summary>
    public sealed class EarthFaunaSystem : ModSystem
    {
        const string HarmonyId="earth.regionalfauna";
        static volatile EarthFaunaSystem instance;
        static int epochCounter;
        // Runtime climate context: the entity whose CanSpawnAt_offthread call is in progress on this thread (null = not mapped).
        [ThreadStatic] static EntityProperties runtimeClimateType;
        [ThreadStatic] static bool runtimeClimateDiagnostic;
        // Per-thread site cache: candidates of several species, and the runtime postfix, query the same column.
        [ThreadStatic] static int cacheEpoch, cacheX, cacheZ;
        [ThreadStatic] static bool cacheCoast;
        [ThreadStatic] static RegionalFaunaCatalog.Site cacheSite;

        /// <summary>Spawn gate of one loaded entity code; immutable after initialisation except for its counters.</summary>
        sealed class Mapping
        {
            public string Code;
            public RegionalFaunaCatalog.Rule Rule;
            /// <summary>Generic carcass: its group's spawnable species (held/extinct-off members already removed).</summary>
            public RegionalFaunaCatalog.Rule[] Group;
            public bool Blocked, NeedsCoast, Unknown;
            public long WorldgenAccepted, RuntimeAccepted;
            public bool SamplesClimate => !Blocked && !Unknown;
        }
        /// <summary>Everything the hot paths read, published once; null = inactive (vanilla behaviour).</summary>
        sealed class State
        {
            public RegionalFaunaCatalog Catalog;
            public TerrainModel Model;
            public Dictionary<AssetLocation,Mapping> ByCode;
            public HashSet<string> Controlled;
            public bool IncludeExtinct, ExtendTemperature;
            public int Epoch, Unknown, Groups;
        }

        ICoreServerAPI api;
        EarthMapSystem earth;
        Harmony harmony;
        MethodInfo runtimeClimateMethod;
        volatile State state;
        string status="not started";
        // (per spawn candidate on every world generation thread: striped, not one contended field)
        readonly StripedCounter rejected=new(), worldgenClimateSamples=new();
        long runtimeClimateSamples, runtimeClimateProbeSamples;

        /// <summary>
        /// "natural" (default; PHYLACINE present-natural = range without human impact), "current" (PHYLACINE current ranges, when
        /// the catalogue carries them) or "1318" (natural minus what each taxon had lost by about 1318, decision 0054; worlds placed
        /// as around 1318 use it unless they chose "current"). Set before WorldReady, e.g. from EarthMapSystem after loading its
        /// config.
        /// </summary>
        public string RangeProfile { get; set; } = RegionalFaunaCatalog.NaturalProfile;
        public RegionalFaunaCatalog Catalog => state?.Catalog;
        /// <summary>Provider mod ids whose natural spawning this session controls (empty when inactive).</summary>
        public IReadOnlyCollection<string> ControlledProviders => (IReadOnlyCollection<string>)state?.Controlled ?? Array.Empty<string>();
        public bool Active => state!=null;
        /// <summary>Why the integration is (in)active, for /earth fauna.</summary>
        public string Status => state!=null ? "active" : status;

        public override double ExecuteOrder() => .06;
        public override bool ShouldLoad(EnumAppSide forSide) => forSide==EnumAppSide.Server;

        public override void AssetsLoaded(ICoreAPI coreApi)
        {
            try { RepairFelinae(coreApi); }
            catch(Exception e) { coreApi.Logger.Warning("Earth fauna compatibility: Felinae repair skipped: {0}",e.Message); }
        }
        static void RepairFelinae(ICoreAPI coreApi)
        {
            // Exact reviewed provider defect: lynxes have no idle sound, but the legacy range field indexes it.
            // Preserve their intentional silence and retain caracal ranges; do not change AI, animations or models.
            var provider=coreApi.ModLoader.Mods.FirstOrDefault(m=>m.Info.ModID=="felinae");
            if(provider?.Info.Version!="0.2.14") return;
            // Only where Earth's regional fauna will use the provider; other worlds keep the provider exactly as shipped.
            if(coreApi is not ICoreServerAPI sapi || !RegionalFaunaWorld(sapi)) return;
            var asset=coreApi.Assets.TryGet(new AssetLocation("felinae:entities/land/felinae.json"));
            if(asset==null) return;
            var json=JObject.Parse(asset.ToText());
            var ranges=json["idleSoundRangeByType"] as JObject;
            var sounds=json["soundsByType"] as JObject;
            if(ranges==null || sounds==null || (float?)ranges["*-adult-*"]!=36 || (float?)ranges["*-baby-*"]!=12) return;
            // Shape guard also protects repackaged files that kept the old version number.
            if(sounds.Properties().Where(p=>p.Name.Contains("lynx")).Any(p=>p.Value["idle"]!=null)) return;
            json["idleSoundRangeByType"]=new JObject { ["*-adult-*-caracalcaracal"]=36,["*-baby-*-caracalcaracal"]=12 };
            asset.Data=Encoding.UTF8.GetBytes(json.ToString());
            coreApi.Logger.Notification("Earth fauna compatibility: repaired Felinae 0.2.14 idleSoundRange for silent lynx variants; caracal sound ranges preserved.");
        }
        /// <summary>
        /// Earth world with regional fauna, decided at AssetsLoaded (the save is open, EarthMapSystem has not read it yet):
        /// saved parameters when present, otherwise what EarthMapSystem will decide for a brand-new standard world.
        /// </summary>
        static bool RegionalFaunaWorld(ICoreServerAPI sapi)
        {
            var save=sapi.WorldManager?.SaveGame;
            if(save==null) return false;
            if(RequiredMods.Problem(sapi)!=null) return false;
            byte[] stored=save.GetData(EarthMapSystem.SaveKey);
            if(stored!=null)
            {
                var p=SerializerUtil.Deserialize<EarthParams>(stored);
                return p?.Enabled==true && p.RegionalFauna;
            }
            if(!EarthMapSystem.IsNewWorld(save) || save.WorldType!="standard" || !EarthWorldOptions.IsEarthStyle(save.PlayStyle)) return false;
            EarthMapConfig cfg=null;
            try { cfg=sapi.LoadModConfig<EarthMapConfig>("earth.json"); }
            catch(Exception e) { sapi.Logger.Warning("Earth fauna compatibility: earth.json can't be read ({0}); deciding with its defaults.",e.Message); }
            cfg??=new EarthMapConfig();
            // the same choice EarthWorldOptions.Freeze saves for the new world (earthNature, else the older earthAnimals)
            return cfg.Enabled && EarthWorldOptions.Nature1318(save.WorldConfiguration, cfg.RegionalFauna)
                && CylinderCompat.HasEarthConfiguration(sapi);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            api=sapi; instance=this;
            earth=api.ModLoader.GetModSystem<EarthMapSystem>();
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady,Initialize);
        }
        void Initialize()
        {
            try { InitializeOrDisable(); }
            catch(Exception e)
            {
                Deactivate();
                status="off for this session: initialisation failed (see server log)";
                Mod.Logger.Error("Earth fauna: initialisation failed; regional fauna is off for this session and natural spawning stays vanilla/provider-controlled. {0}",e);
            }
        }
        void InitializeOrDisable()
        {
            var p=earth?.Params;
            if(p?.Enabled!=true || earth.Model==null) { status="not an Earth world"; return; }
            if(!p.RegionalFauna) { status="regional fauna is off in this save"; return; }
            if(p.CreationOptionsVersion > 0) RangeProfile=p.FaunaRangeProfile ?? RegionalFaunaCatalog.NaturalProfile;
            // Decision 0054: animals as around 1318 in worlds placed as around 1318, including those created before the profile
            // existed (they saved the old default "natural"); a deliberate "current" stays.
            if(p.PlacementRules>=1 && RangeProfile!=RegionalFaunaCatalog.CurrentProfile) RangeProfile=RegionalFaunaCatalog.Profile1318;
            var rulesAsset=api.Assets.TryGet(new AssetLocation("earth:config/fauna-species.json"));
            if(rulesAsset==null) { Disable("the shipped fauna-species.json is missing",true); return; }
            string rulesJson=rulesAsset.ToText();
            string[] supported;
            try { supported=RegionalFaunaCatalog.ProviderIds(rulesJson); }
            catch(Exception e) { Disable("the shipped fauna-species.json is unreadable: "+e.Message,true); return; }
            var installed=api.ModLoader.Mods.Where(m=>supported.Contains(m.Info.ModID)).ToArray();
            if(installed.Length==0)
            {
                // The common case: nothing to control, so no catalogue, no mappings and no Harmony patches.
                status="no supported fauna provider installed";
                Mod.Logger.Notification("Earth fauna: no supported fauna provider (e.g. Fauna of the Stone Age) is installed; natural spawning is unchanged.");
                return;
            }
            if(p.FaunaRulesVersion!=RegionalFaunaCatalog.RulesVersion)
                Mod.Logger.Warning("Earth fauna: this world saved fauna rules v{0}; using the current v{1} for future natural spawns only (existing animals are unaffected).",
                    p.FaunaRulesVersion,RegionalFaunaCatalog.RulesVersion);
            var regionsAsset=api.Assets.TryGet(new AssetLocation("earth:config/treeregions.json"));
            if(regionsAsset==null) { Disable("the shipped treeregions.json is missing",true); return; }
            RegionalFaunaCatalog catalog;
            try { catalog=new RegionalFaunaCatalog(rulesJson,regionsAsset.ToText(),installed.Select(m=>m.Info.ModID),RangeProfile); }
            catch(Exception e) { Disable("the shipped fauna data is corrupt or from another Earth version: "+e.Message,true); return; }
            foreach(var problem in catalog.Problems) Mod.Logger.Warning("Earth fauna: {0}",problem);
            int alive=0;
            if(p.PlacementRules>=1)
            {
                // Animals as around 1318 (decision 0047): aurochs, moa, Steller's sea cow and the like still lived then and
                // spawn in their ranges; only animals extinct before 1318 wait for the extinct-animals option.
                var placement=api.Assets.TryGet(new AssetLocation("earth:config/placement.json"));
                try
                {
                    var names=placement==null ? null : PlacementCatalogue.ReadAliveIn1318(placement.ToText());
                    if(names==null) Mod.Logger.Error("Earth fauna: placement.json is missing; every extinct animal waits for the extinct-animals option.");
                    else foreach(var r in catalog.Rules) if(r.Extinct && names.Contains(r.Taxon)) { r.AliveIn1318=true; alive++; }
                }
                catch(Exception e) { Mod.Logger.Error("Earth fauna: placement.json can't be read ({0}); every extinct animal waits for the extinct-animals option.",e.Message); }
            }

            var controlled=new HashSet<string>(StringComparer.Ordinal);
            foreach(var mod in installed)
            {
                var provider=catalog.SupportedProviders.First(s=>s.Id==mod.Info.ModID);
                if(mod.Info.Version!=provider.Version) Mod.Logger.Warning("Earth fauna: provider {0} is {1}, reviewed version {2}; new species fail closed and known asset behaviour needs revalidation.",provider.Id,mod.Info.Version,provider.Version);
                controlled.Add(provider.Id);
            }
            bool includeExtinct=p.IncludeExtinctAnimals;
            var types=api.World.EntityTypes.Where(t=>t?.Code!=null && controlled.Contains(t.Code.Domain)).ToArray();
            var codes=types.Select(t=>t.Code.ToString()).ToArray();
            var byCode=new Dictionary<AssetLocation,Mapping>();
            int unknown=0, groups=0;
            for(int i=0;i<types.Length;i++)
            {
                var m=new Mapping { Code=codes[i] };
                m.Rule=catalog.Find(codes[i],out bool ambiguous);
                if(ambiguous) Mod.Logger.Warning("Earth fauna: {0} matches more than one taxon; treated as unknown (no natural spawning) pending a mapping fix.",codes[i]);
                if(m.Rule!=null)
                {
                    m.Blocked=RegionalFaunaCatalog.Blocked(m.Rule,includeExtinct);
                    m.NeedsCoast=m.Rule.NeedsCoast;
                }
                else if(!ambiguous && catalog.GroupRules(codes[i],codes) is { } group)
                {
                    m.Group=group.Where(r=>!RegionalFaunaCatalog.Blocked(r,includeExtinct)).ToArray();
                    m.Blocked=m.Group.Length==0;
                    m.NeedsCoast=m.Group.Any(r=>r.NeedsCoast);
                    groups++;
                }
                else
                {
                    m.Unknown=true;
                    if(types[i].Server?.SpawnConditions?.Worldgen!=null || types[i].Server?.SpawnConditions?.Runtime!=null)
                    {
                        unknown++;
                        Mod.Logger.Warning("Earth fauna: unsupported entity {0} from a mapped provider is excluded from natural spawning pending a mapping. Existing/player-bred entities are unaffected.",codes[i]);
                    }
                }
                byCode[types[i].Code]=m;
            }
            foreach(var r in catalog.Rules.Where(r=>r.Quarantined))
                Mod.Logger.Notification("Earth fauna: {0}/{1} held from new natural spawning: {2}",r.Provider,r.Taxon,r.Note??"Range or taxonomy needs review.");

            state=new State { Catalog=catalog, Model=earth.Model, ByCode=byCode, Controlled=controlled, IncludeExtinct=includeExtinct,
                ExtendTemperature=p.ExtendTemperature, Epoch=Interlocked.Increment(ref epochCounter), Unknown=unknown, Groups=groups };
            // PatchVanilla's FaunaTable: every hook checked (names, argument positions, types, the injected tmpPos) before anything
            // is patched, all or none; its line names a mismatch, and the game is left untouched
            harmony=new Harmony(HarmonyId);
            var patches=PatchVanilla.For(api,Mod,harmony);
            patches.Apply(PatchVanilla.FaunaTable());
            if(!patches.On(PatchVanilla.Feature.RegionalFauna))
            {
                Deactivate();
                status="off for this session: this game version's natural-spawn methods differ from the reviewed VS 1.22 hooks ("+patches.StillInPlace(PatchVanilla.Feature.RegionalFauna)+")";
                return;
            }
            runtimeClimateMethod=RuntimeClimateMethod;
            if(api.ModLoader.IsModEnabled("biomes")) Mod.Logger.Warning("Earth fauna: Biomes is also installed; its independent geographic veto can suppress Earth's valid species. Disable Biomes for the reviewed Earth profile.");
            Mod.Logger.Notification("Earth fauna: {0} supported providers installed, {1} taxon mappings ({2} held), {3} generic carcass groups, {4} unmapped natural-spawn entities; extinct {5}{7}; ranges: {6}. Vanilla and unrelated mods keep their own spawning.",
                controlled.Count,catalog.Rules.Count,catalog.Rules.Count(r=>r.Quarantined),groups,unknown,includeExtinct?"included":"excluded",ProfileText(catalog),
                alive>0 ? " (except "+alive+" still alive in 1318)" : "");
        }
        void Disable(string why,bool dataError)
        {
            status="off for this session: "+why;
            string message="Earth fauna: "+why+". Regional fauna is off for this session; natural spawning stays vanilla/provider-controlled.";
            if(dataError) Mod.Logger.Error(message); else Mod.Logger.Warning(message);
        }
        void Deactivate()
        {
            state=null;
            try { harmony?.UnpatchAll(HarmonyId); } catch(Exception e) { Mod.Logger.Warning("Earth fauna: unpatching failed: {0}",e.Message); }
            harmony=null; runtimeClimateMethod=null;
        }
        /// <summary>The server's runtime climate helper and its position field, as PatchVanilla found them (for the probe).</summary>
        internal static MethodInfo RuntimeClimateMethod;
        internal static FieldInfo RuntimeTmpPos;
        static string ProfileText(RegionalFaunaCatalog catalog) => catalog.RangeProfile==RegionalFaunaCatalog.CurrentProfile
            ? $"current ranges (PHYLACINE current) for {catalog.CurrentRangeRules} mapped mammals"
            : catalog.RangeProfile==RegionalFaunaCatalog.Profile1318
            ? $"ranges around 1318 (natural range without what {catalog.Rules.Count(r=>r.Lost1318Regions!=null || r.Lost1318Envelopes!=null)} taxa had lost by then)"
            : "natural range without human impact (PHYLACINE present-natural) for mapped living mammals";

        // ------------------------------------------------------------------------------------------ eligibility

        static RegionalFaunaCatalog.Site CachedSite(State st,double x,double z,bool coast)
        {
            int bx=(int)Math.Floor(x), bz=(int)Math.Floor(z);
            // the same lookup only (a coast site resolves the region on the sea's finer grid): a land candidate reusing a
            // coast candidate's site made the answer depend on which was asked first (CR-E5)
            if(cacheEpoch==st.Epoch && cacheX==bx && cacheZ==bz && cacheCoast==coast) return cacheSite;
            var s=st.Catalog.SiteAt(st.Model,bx,bz,coast);
            cacheEpoch=st.Epoch; cacheX=bx; cacheZ=bz; cacheCoast=coast; cacheSite=s;
            return s;
        }
        bool Allows(State st,Mapping m,double x,double z)
        {
            // Held/extinct/unknown are decided before computing the site.
            bool ok=!m.Unknown && !m.Blocked && (m.Rule!=null
                ? RegionalFaunaCatalog.Allows(m.Rule,CachedSite(st,x,z,m.NeedsCoast),st.IncludeExtinct)
                : RegionalFaunaCatalog.AllowsAny(m.Group,CachedSite(st,x,z,m.NeedsCoast),st.IncludeExtinct));
            if(!ok) rejected.Increment();
            return ok;
        }
        static Mapping MappingOf(State st,EntityProperties type) => type?.Code!=null && st.ByCode.TryGetValue(type.Code,out var m) ? m : null;

        internal static bool WorldgenPrefix([HarmonyArgument(1)] EntityProperties type,[HarmonyArgument(2)] BlockPos pos,ref bool __result)
        {
            var self=instance; var st=self?.state; var m=st==null ? null : MappingOf(st,type);
            if(m==null || self.Allows(st,m,pos.X,pos.Z)) return true;
            __result=false; return false;
        }
        internal static void WorldgenClimatePrefix([HarmonyArgument(1)] EntityProperties type,[HarmonyArgument(2)] BlockPos pos,[HarmonyArgument(5)] ref float rain,[HarmonyArgument(6)] ref float temp)
        {
            var self=instance; var st=self?.state;
            if(st==null || MappingOf(st,type)?.SamplesClimate!=true) return;
            var sample=RegionalFaunaClimate.At(st.Model,pos.X,pos.Z,st.ExtendTemperature);
            temp=(float)sample.Temperature; rain=(float)sample.Rainfall;
            self.worldgenClimateSamples.Increment();
        }
        internal static void WorldgenPostfix([HarmonyArgument(1)] EntityProperties type,bool __result)
        {
            if(!__result) return;
            var st=instance?.state; var m=st==null ? null : MappingOf(st,type);
            if(m!=null) Interlocked.Increment(ref m.WorldgenAccepted);
        }
        internal static bool RuntimePrefix([HarmonyArgument(0)] EntityProperties type,[HarmonyArgument(1)] BlockPos spawnPosition,ref Vec3d __result,out EntityProperties __state)
        {
            __state=runtimeClimateType;
            var self=instance; var st=self?.state; var m=st==null ? null : MappingOf(st,type);
            // Clear an outer context for unrelated/nested calls, and restore it in the finalizer even if another patch throws.
            runtimeClimateType=m?.SamplesClimate==true ? type : null;
            if(m==null || self.Allows(st,m,spawnPosition.X,spawnPosition.Z)) return true;
            __result=null; return false;
        }
        internal static void RuntimeFinalizer(EntityProperties __state) => runtimeClimateType=__state;
        internal static void RuntimePostfix([HarmonyArgument(0)] EntityProperties type,ref Vec3d __result)
        {
            if(__result==null) return;
            var self=instance; var st=self?.state; var m=st==null ? null : MappingOf(st,type);
            if(m==null) return;
            // Vanilla only moves the candidate vertically; this re-check hits the per-thread site cache unless another mod moved X/Z.
            if(!self.Allows(st,m,__result.X,__result.Z)) { __result=null; return; }
            Interlocked.Increment(ref m.RuntimeAccepted);
        }
        internal static bool RuntimeClimatePrefix([HarmonyArgument(1)] RuntimeSpawnConditions sc,BlockPos ___tmpPos,ref ClimateCondition __result)
        {
            var self=instance; var st=self?.state;
            if(st==null || runtimeClimateType==null || sc==null || ___tmpPos==null) return true;
            var sample=RegionalFaunaClimate.At(st.Model,___tmpPos.X,___tmpPos.Z,st.ExtendTemperature);
            float temperature=(float)sample.Temperature, rain=(float)sample.Rainfall;
            var climate=new ClimateCondition { Temperature=temperature,WorldGenTemperature=temperature,Rainfall=rain,WorldgenRainfall=rain };
            if(sc.ClimateValueMode!=EnumGetClimateMode.WorldGenValues)
            {
                // Retain provider-requested seasonal/precipitation callbacks at the actual candidate position.
                // Earth extends clipped game temperatures in that callback; avoid applying that extension twice.
                climate.Temperature=climate.WorldGenTemperature=Math.Clamp(temperature,-20,40);
                EarthMapSystem.SuppliedEarthTemperature=true;
                try{climate=self.api.World.BlockAccessor.GetClimateAt(___tmpPos,climate,sc.ClimateValueMode,self.api.World.Calendar.TotalDays);}
                finally{EarthMapSystem.SuppliedEarthTemperature=false;}
                if(climate!=null) climate.WorldGenTemperature=temperature;
            }
            __result=climate!=null && sc.MatchesClimate(climate)?climate:null;
            if(runtimeClimateDiagnostic) Interlocked.Increment(ref self.runtimeClimateProbeSamples);
            else Interlocked.Increment(ref self.runtimeClimateSamples);
            return false;
        }

        // ------------------------------------------------------------------------------------------ diagnostics

        /// <summary>Habitat of a world position as the spawn gate sees it (null when inactive).</summary>
        public RegionalFaunaCatalog.Site SiteAt(double x,double z,bool checkCoast=true)
        {
            var st=state;
            return st==null ? default : st.Catalog.SiteAt(st.Model,x,z,checkCoast);
        }
        /// <summary>Would a natural spawn of this loaded entity code be allowed here now (true for uncontrolled codes)?</summary>
        public bool AllowsAt(AssetLocation code,double x,double z)
        {
            var st=state;
            if(st==null || code==null || !st.ByCode.TryGetValue(code,out var m)) return true;
            if(m.Unknown || m.Blocked) return false;
            var s=st.Catalog.SiteAt(st.Model,x,z,m.NeedsCoast);
            return m.Rule!=null ? RegionalFaunaCatalog.Allows(m.Rule,s,st.IncludeExtinct) : RegionalFaunaCatalog.AllowsAny(m.Group,s,st.IncludeExtinct);
        }
        public string EligibilityReport()
        {
            var st=state;
            var top=st==null ? Array.Empty<string>() : st.ByCode.Values
                .SelectMany(m=>new[]{("worldgen:"+m.Code,Interlocked.Read(ref m.WorldgenAccepted)),("runtime:"+m.Code,Interlocked.Read(ref m.RuntimeAccepted))})
                .Where(p=>p.Item2>0).OrderByDescending(p=>p.Item2).Take(20).Select(p=>p.Item1+"="+p.Item2).ToArray();
            return "Accepted natural-spawn eligibility checks (not confirmed entity spawns): "+(top.Length==0?"none":string.Join(", ",top));
        }
        public string VerifyRuntimeClimateAdapter()
        {
            var st=state;
            if(st==null || runtimeClimateMethod==null) return "Runtime climate probe skipped: regional fauna inactive ("+status+").";
            var reports=new List<string>();
            // A detached shell exercises the actual Harmony-patched helper. It owns no chunks, thread or server state.
            object detached=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(runtimeClimateMethod.DeclaringType);
            var tmpField=RuntimeTmpPos;
            var previous=runtimeClimateType; bool previousDiagnostic=runtimeClimateDiagnostic;
            try
            {
                runtimeClimateDiagnostic=true;
                foreach(var fixture in new[]{("caninae","simensis",9d,39d),("dinornithidae","xenicusgilviventris",-44d,171d),("capreolinae","capreoluscapreolus",57d,25d)})
                {
                    var mapping=st.Catalog.Rules.FirstOrDefault(r=>r.Provider==fixture.Item1 && r.Tokens.Contains(fixture.Item2));
                    var type=mapping==null?null:api.World.EntityTypes.FirstOrDefault(t=>t?.Code!=null && st.ByCode.TryGetValue(t.Code,out var m) && m.Rule==mapping && t.Server?.SpawnConditions?.Runtime!=null);
                    if(type==null) { reports.Add(fixture.Item2+": provider/runtime variant absent");continue; }
                    st.Model.Geometry.FromGeo(fixture.Item3,fixture.Item4,out double x,out double z);
                    var ground=RegionalFaunaClimate.At(st.Model,(int)x,(int)z,st.ExtendTemperature);
                    var pos=new BlockPos((int)x,Math.Max(1,(int)Math.Round(ground.SurfaceY)),(int)z);
                    tmpField.SetValue(detached,pos);
                    runtimeClimateType=type;
                    foreach(var mode in new[]{EnumGetClimateMode.WorldGenValues,EnumGetClimateMode.NowValues})
                    {
                        var sc=type.Server.SpawnConditions.Runtime.Clone();
                        sc.ClimateValueMode=mode;
                        // Read the helper result with diagnostic-only bounds; never mutate a provider's live conditions.
                        sc.MinTemp=-200;sc.MaxTemp=200;sc.MinRain=0;sc.MaxRain=1;
                        var result=(ClimateCondition)runtimeClimateMethod.Invoke(detached,new object[]{null,sc});
                        if(result==null || Math.Abs(result.WorldGenTemperature-ground.Temperature)>.001 || Math.Abs(result.WorldgenRainfall-ground.Rainfall)>.001)
                            throw new InvalidOperationException("Runtime climate adapter probe failed: "+fixture.Item2+"/"+mode);
                        reports.Add($"{fixture.Item2}/{mode}: {result.WorldGenTemperature:F2}C, rain {result.Rainfall:F3}, ground baseline retained");
                    }
                    var reject=type.Server.SpawnConditions.Runtime.Clone();
                    reject.ClimateValueMode=EnumGetClimateMode.WorldGenValues;reject.MinTemp=(float)ground.Temperature+1;reject.MaxTemp=200;
                    if(runtimeClimateMethod.Invoke(detached,new object[]{null,reject})!=null)
                        throw new InvalidOperationException("Runtime climate adapter bypassed temperature limits: "+fixture.Item2);
                    reports.Add(fixture.Item2+": restrictive temperature limit still rejected");
                }
            }
            finally { runtimeClimateType=previous;runtimeClimateDiagnostic=previousDiagnostic; }
            return "PASS runtime patched-helper probes (detached, no spawning; absent providers explicitly skipped): "+string.Join("; ",reports);
        }
        public string ClimateReport(double x,double z)
        {
            var st=state;
            if(st==null) return "Regional fauna climate adapter inactive.";
            var sample=RegionalFaunaClimate.At(st.Model,x,z,st.ExtendTemperature);
            int pixel=st.Model.ClimatePixel(x,z,0,1,1);
            // as vanilla's GenCreatures reads it (its fixed reference height; the real lapse with climate rules v2)
            double legacy=Climate.GetScaledAdjustedTemperatureFloat((pixel>>16)&255,(int)(st.Model.SeaLevel*.09));
            return $"Natural-fauna ground climate {sample.Temperature:F2} C / rain {sample.Rainfall:F3}; legacy fixed-reference temperature {legacy:F2} C. Ground samples worldgen {worldgenClimateSamples.Value}, runtime {Interlocked.Read(ref runtimeClimateSamples)}, detached runtime probes {Interlocked.Read(ref runtimeClimateProbeSamples)}.";
        }
        public string Describe(double x,double z)
        {
            var st=state;
            if(st==null) return "Regional fauna inactive: "+status+". Natural spawning is unchanged (vanilla and other mods).";
            var catalog=st.Catalog;
            var s=catalog.SiteAt(st.Model,x,z);
            var candidates=catalog.Rules.Where(r=>RegionalFaunaCatalog.Allows(r,s,st.IncludeExtinct))
                .Select(r=>r.Extinct ? r.Taxon+" (extinct)" : r.Taxon).Distinct().OrderBy(n=>n).ToArray();
            return $"Fauna at {s.Lat:F4}, {s.Lon:F4}: {catalog.Geography.Name(s.Region)}; {s.Habitat}{(s.Coastal?", coastal":"")}.\n"+
                $"Ranges: {ProfileText(catalog)}; documented regions or boxes for birds, reptiles, sirenians, taxonomic splits and extinct taxa (extinct: last natural range before human-driven extinction).\n"+
                $"Providers {st.Controlled.Count}; mapped taxa {catalog.Rules.Count}; held {catalog.Rules.Count(r=>r.Quarantined)}; generic carcass groups {st.Groups}; unmapped entities {st.Unknown}; geography vetoes {rejected.Value}.\n"+
                (candidates.Length==0?"No mapped species' range and habitat include this position.":"Mapped species whose range and habitat include this position: "+string.Join(", ",candidates))+"\n"+
                "Provider climate limits, altitude, forest, collision, population and depletion conditions still apply. Vanilla background fauna is not geographically controlled; candidate counts are not observed populations.\n"+ClimateReport(x,z)+"\n"+EligibilityReport();
        }
        public override void Dispose()
        {
            Deactivate();
            if(instance==this) instance=null;
            base.Dispose();
        }
    }
}
