using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Server-only geographic lithology adapter. EarthOres owns the optional ore-generation layer.
    /// Generation follows the world's saved geology rules version: v1 (0.12 saves) is reproduced block for block,
    /// v2 (new worlds) adds absolute-height strata, coastal fill, chalk/chert/tuff, preserved kimberlite and
    /// discontinuous gravel. Surface clay/peat/salt eligibility is shared by all versions.
    /// </summary>
    public sealed class EarthGeology : ModSystem
    {
        const string HarmonyId="earth.regionalgeology";
        const int ClaySalt=7411, PeatSalt=7643;
        static EarthGeology instance;
        static int epoch;
        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        bool strataHook, soilHook, depositHook, depositColumnHook;
        // Immutable after InitWorld; shared by the worldgen threads.
        bool[] replaceRock, isRock, surfaceCover, clayOrPeat, gravelBlock;
        int[] sandOf, gravelOf, materialBlock;
        int glacierIceId, rules, mapTop;
        /// <summary>0048: the world's soil rules (EarthParams.SoilRules); 2 and newer: ground columns leave real deserts without topsoil.</summary>
        int soilRules;
        /// <summary>The world's ground rules (EarthParams.GroundColumns): 2 tells the ground the place's temperature (EA-0062).</summary>
        int groundRules;
        /// <summary>Ground rules 2 (EA-0062): vanilla's bauxite strata are ordinary rock to the strata pass, so regional geology
        /// replaces them (Earth's own bauxite comes later, from its layer deposits in terra rossa and laterite). 0: kept.</summary>
        int strataBauxite;
        long seed;
        double width, cellBlocks;
        const double GravelCoreShare=.4;
        GeologyReference reference;
        /// <summary>Ground columns prototype (EarthParams.GroundColumns), null when off.</summary>
        EarthGround ground;
        /// <summary>0060: ores in their ground layer (EarthParams.OreLayers), null when off.</summary>
        EarthOreLayers layers;
        internal EarthOreLayers Layers=>layers;
        /// <summary>Whether the hook after vanilla's deposit pass is bound (PatchVanilla's GeologyDepositColumns): the only place
        /// <see cref="AfterDepositLayers"/> is raised, so rules v4's deposit catalogue needs it.</summary>
        internal bool DepositColumnsHook=>depositColumnHook;
        readonly Dictionary<string,int> rocks=new(StringComparer.Ordinal);
        string mappingDescription;
        int generationErrors, pickFilterErrors;
        public bool Active { get; private set; }
        public RegionalGeologyCatalog Catalog { get; private set; }
        /// <summary>Rules version this world generates with (0 when inactive).</summary>
        public int ActiveRulesVersion => Active ? rules : 0;
        public override double ExecuteOrder()=>0.35;
        public override bool ShouldLoad(EnumAppSide side)=>side==EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api=api;
            core=api.ModLoader.GetModSystem<EarthMapSystem>();
            instance=this;
            harmony=new Harmony(HarmonyId);
            // the hooks on vanilla's strata, soil and deposit passes, and (optional) the GA child-grade clamp again after
            // /wgen regen: PatchVanilla's GeologyTable, each part off on its own when this game version lacks its method
            var patches=PatchVanilla.For(api,Mod,harmony);
            patches.Apply(PatchVanilla.GeologyTable());
            strataHook=patches.On(PatchVanilla.Feature.GeologyStrata);
            soilHook=patches.On(PatchVanilla.Feature.GeologySoil);
            depositHook=patches.On(PatchVanilla.Feature.GeologyDeposits);
            depositColumnHook=patches.On(PatchVanilla.Feature.GeologyDepositColumns);
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady,InitWorld);
            api.Event.MapRegionGeneration(MaskSaltMap,"standard");
        }

        void InitWorld()
        {
            Active=false;
            Interlocked.Increment(ref epoch);
            var p=core?.Params;
            if(p?.Enabled!=true || !p.RegionalGeology || core.Model==null)return;
            // Only change deposit generators in regional Earth worlds.
            try{NormalizeGeologyAdditionsChildGrades();}
            catch(Exception e){Mod.Logger.Warning("Earth GA child-grade compatibility adapter was not applied: {0}",e.Message);}
            try{Active=TryInit(p);}
            catch(Exception e)
            {
                Active=false;
                Mod.Logger.Error("Earth regional geology is off for this session after an unexpected error; new chunks use vanilla rock strata. {0}",e);
            }
        }

        bool TryInit(EarthParams p)
        {
            int version=p.GeologyRulesVersion;
            if(version>RegionalGeologyCatalog.RulesVersion)
            {
                // A world from a newer Earth Map: the nearest rules this build has give smaller contacts than vanilla strata
                // would. The saved version stays, so the newer build continues with its own rules (decision 0002).
                Mod.Logger.Warning("Earth geology: this world was saved with geology rules v{0}, newer than this Earth Map (v{1}). New chunks use v{1} this session; their rock layers may not line up with chunks made by the newer version. Use that version to keep its geology exactly.",
                    version,RegionalGeologyCatalog.RulesVersion);
                version=RegionalGeologyCatalog.RulesVersion;
            }
            if(!RegionalGeologyCatalog.IsSupportedRulesVersion(version))
            {
                Mod.Logger.Error("Earth geology: this world was saved with geology rules v{0}; this Earth Map supports v{1}-v{2}. Regional geology is off for this session and new chunks use vanilla rock strata. Use the Earth Map version that created the world to keep its geology.",
                    version,RegionalGeologyCatalog.OldestRulesVersion,RegionalGeologyCatalog.RulesVersion);
                return false;
            }
            if(version!=RegionalGeologyCatalog.RulesVersion)
                Mod.Logger.Notification("Earth geology: this world keeps its saved rules v{0}, so new chunks match the existing ones (current rules are v{1}; UpgradeExistingWorldGeologyRules opts in for new chunks only, with visible contacts).",version,RegionalGeologyCatalog.RulesVersion);
            if(!strataHook)
            {
                Mod.Logger.Warning("Earth geology: the rock-strata hook is unavailable in this game version, so regional geology is off for this session (vanilla strata in new chunks).");
                return false;
            }
            foreach(string competitor in new[]{"advancedgeology","rockstratavariety"})
                if(api.ModLoader.IsModEnabled(competitor))
                {
                    Mod.Logger.Error("Earth geology: {0} also owns rock/deposit generation; Earth regional geology is off for this session. Disable one of them.",competitor);
                    return false;
                }
            if(!GeologyTransitions.IsValidWidth(p.GeologyTransitionBlocks))
            {
                Mod.Logger.Error("Earth geology: the saved contact width {0} is invalid; regional geology is off for this session.",p.GeologyTransitionBlocks);
                return false;
            }
            string grid=api.Assets.TryGet(new AssetLocation(RegionalGeologyCatalog.GridAsset))?.ToText();
            string rulesText=api.Assets.TryGet(new AssetLocation(RegionalGeologyCatalog.RulesAsset(version)))?.ToText();
            if(grid==null || rulesText==null)
            {
                Mod.Logger.Error("Earth geology: shipped data {0} is missing; regional geology is off for this session.",grid==null?RegionalGeologyCatalog.GridAsset:RegionalGeologyCatalog.RulesAsset(version));
                return false;
            }
            RegionalGeologyCatalog catalog;
            try{catalog=new RegionalGeologyCatalog(grid,rulesText);}
            catch(Exception e) when (e is ArgumentException or FormatException or OverflowException or KeyNotFoundException or InvalidOperationException)
            {
                Mod.Logger.Error("Earth geology: shipped geology data is invalid ({0}); regional geology is off for this session.",e.Message);
                return false;
            }
            if(catalog.Version!=version)
            {
                Mod.Logger.Error("Earth geology: {0} declares rules v{1}, expected v{2}; regional geology is off for this session.",RegionalGeologyCatalog.RulesAsset(version),catalog.Version,version);
                return false;
            }
            if(!ResolveMaterials(p,catalog,grid,rulesText))return false;
            Catalog=catalog;
            BuildBlockTables(catalog);
            ground=null;
            if(GroundColumnFacts.Columns(p.GroundColumns))
            {
                if(p.GroundColumns!=GroundColumns.RulesVersion)
                    Mod.Logger.Warning("Earth ground columns: this world was saved with ground rules v{0}; this build has v{1}. Rules marked for a later version stay off in this world.",p.GroundColumns,GroundColumns.RulesVersion);
                // depth scale: the terrain's metres per block at sea level, then the world's Ground depth choice
                var curve=GroundColumns.DepthCurve.For(p.GroundDepth,1/core.Model.Vertical.Slope(0),core.Model.SeaLevel);
                ground=EarthGround.TryCreate(api,curve,p.NoiseSeed,id=>replaceRock[id],GroundSets(p),out string groundError);
                if(ground==null)Mod.Logger.Error("Earth ground columns are off for this session ({0}); new chunks use regional geology only.",groundError);
                else Mod.Logger.Notification("Earth {0}.",ground.Summary);
            }
            layers=ground!=null && OreLayerFacts.Layers(p.OreLayers)?CreateLayers(p):null;
            soilRules=p.SoilRules;
            groundRules=p.GroundColumns;
            strataBauxite=ground!=null && GroundColumnFacts.NoBauxiteStrata(groundRules)?api.World.GetBlock(new AssetLocation("game:rock-bauxite"))?.Id??0:0;
            if(ground!=null && soilRules>=SoilFertility.GroundRules)
                Mod.Logger.Notification("Earth ground columns: soil rules v{0}, arid land (aridity index below {1}, {2} excepted) has no topsoil.",soilRules,SoilFertility.DryLandAridity,
                    soilRules<SoilFertility.FloodplainRules?"river belts":"river belts and floodplains");
            rules=version;
            seed=p.NoiseSeed;
            width=p.GeologyTransitionBlocks;
            var geometry=core.Model.Geometry;
            cellBlocks=geometry.BlocksPerDegreeZ*.5;
            mapTop=api.WorldManager.MapSizeY-1;
            if(GeologyFacts.SmoothedBeds(version))
            {
                var g=catalog.Generation;
                reference=new GeologyReference(core.Model,seed,cellBlocks,g.DatumRadiusCells,g.StrataThicknessPerSeaLevel,g.DatumUndulation,g.ThicknessVariation);
            }
            else reference=null;
            if(!soilHook)Mod.Logger.Warning("Earth geology: the soil-layer hook is unavailable; surface sand/gravel cover and top-rock repair are skipped.");
            if(!depositHook)Mod.Logger.Warning("Earth geology: the deposit hook is unavailable; clay, peat and salt use vanilla placement rules.");
            if(GeologyFacts.GravelLenses(version) && !depositColumnHook)Mod.Logger.Warning("Earth geology: the deposit-column hook is unavailable; gravel lenses may remain under clay/peat patches.");
            Mod.Logger.Notification("Earth geology v{0}: GLiM + GUM 0.5-degree categories, {1} reviewed regional envelope(s){2}. Subsurface thicknesses are synthesized; unknown bedrock preserves vanilla. Proxies: {3}",
                version,catalog.Provinces.Count,GeologyFacts.SmoothedBeds(version)?string.Format(CultureInfo.InvariantCulture,", bed boundaries below a smoothed reference surface (thickness scale {0:0} blocks), preserved {1}",reference.ThicknessScale,string.Join("/",catalog.Preserve)):"",mappingDescription);
            Mod.Logger.Notification("Earth geology: ore geography is {0}. GA mudstone is not selected because its crushing recipe yields bauxite. Contact half-width {1} blocks.",p.RegionalOres ? "handled by regional ore rules" : "legacy native",p.GeologyTransitionBlocks);
            Mod.Logger.Notification("Earth geology: clay/peat use frequency multipliers (relief in metres, wetness index); missing salt evidence is unknown, positive surface-salt evidence is favoured.");
            return true;
        }

        /// <summary>0060: the ore-layer rules for this world's ground columns, null (logged) when they cannot be read.</summary>
        EarthOreLayers CreateLayers(EarthParams p)
        {
            // decision 0002: each world reads the ore-layer rules it was made with (v1 worlds keep v1, EA-0059's v2 is for new ones)
            int version=OreLayers.Supported(p.OreLayers);
            if(version!=p.OreLayers)
                Mod.Logger.Warning("Earth ore layers: this world was saved with ore-layer rules v{0}; this build reads v1 to v{1} and uses v{2} for new chunks.",p.OreLayers,OreLayers.RulesVersion,version);
            string asset=OreLayers.RulesAsset(version);
            string text=api.Assets.TryGet(new AssetLocation(asset))?.ToText();
            try
            {
                if(text==null)throw new System.IO.FileNotFoundException(asset+" is missing");
                var rules=new OreLayers(text,ground.Model);
                if(rules.Version!=version)throw new FormatException(asset+" declares rules v"+rules.Version);
                var made=new EarthOreLayers(api,rules,ground,RebuildLayerTable,(x,z,d,top)=>RegionalRock(x,z,d,top));
                ground.RewritePreservedStrata(api);
                Mod.Logger.Notification("Earth ore layers v{0}: deposits keep to their ground layers under either ore choice; the layer pass runs after the deposits{1}.",version,OreLayerFacts.Under(version)?" (fire clay also as the underclay of a kept coal block)":"");
                return made;
            }
            catch(Exception e) when (e is FormatException or KeyNotFoundException or InvalidOperationException or System.Text.Json.JsonException or System.IO.FileNotFoundException)
            {
                Mod.Logger.Error("Earth ore layers are off for this session ({0}); new chunks keep deposits wherever their hosts are.",e.Message);
                return null;
            }
        }

        /// <summary>
        /// Whether this ground-columns world reads the 30″ ground sets (EarthParams.GroundData; global-design section 9). A new
        /// world decides once, before its first chunk: missing sets are fetched from GroundRepo (the server waits; 172 MB from
        /// Earth's data release), and a world whose source lacks them (none set, or not there) keeps the ground pack's 2′ sets
        /// for good, so its cover never mixes the two; a fetch that fails otherwise stops the server (review A3). A 30″ world
        /// whose sets went missing fetches them again, and stops if it can't. Worlds made before this read whatever sets the
        /// machine has, as they did. "New" is the world Earth created in this start (EarthMapSystem.CreatedThisStart), not
        /// SaveGame.IsNew: a new world whose first start stopped (its data could not be had) is created on its second start,
        /// when the game no longer calls it new (adversarial review A1).
        /// </summary>
        bool GroundSets(EarthParams p)
        {
            string folder=EarthGround.DataFolder,repo=core.Config?.GroundRepo;
            if(p.GroundData==0 && !core.CreatedThisStart)
            {
                // (EA-0116 D-L9) checked by their SHA-256 too, as a 30" world's are: a set that is here but damaged failed at its
                // first column read (the chunk noted, the server stopped: late); it is fetched again before the game runs, and
                // the server stops if it can't be. A set that isn't here stays away, as these worlds always had it.
                var damaged=GroundDownloader.Missing(folder,true).Where(s=>System.IO.File.Exists(GroundDownloader.PathOf(folder,s))).ToList();
                if(damaged.Count>0)
                {
                    string names=string.Join(", ",damaged.Select(s=>s.Name));
                    GroundDownloader.Result again;
                    try{again=GroundDownloader.Get(folder,repo,s=>Mod.Logger.Notification("Earth ground sets: "+s),core?.Stopping??default,damaged);}
                    catch(Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or AggregateException)
                    {
                        Mod.Logger.Warning("Earth ground sets: {0}",e.Message);
                        again=GroundDownloader.Result.Failed;
                    }
                    if(again!=GroundDownloader.Result.Here)
                    {
                        Mod.Logger.Error("Earth ground columns: the 30\" ground set(s) {0} in {1} are damaged and could not be fetched again. The server stops before the game runs (nothing is saved); put the files from {2} there, or let Earth fetch them (GroundRepo in earth.json), and start again.",
                            names,System.IO.Path.Combine(folder,GroundDownloader.Subfolder),GroundDownloader.Release);
                        api.Server.ShutDown();
                        return false;
                    }
                    Mod.Logger.Notification("Earth ground columns: the damaged 30\" ground set(s) {0} were fetched again.",names);
                }
                bool here=GroundDownloader.Missing(folder,false).Count==0;
                Mod.Logger.Notification("Earth ground columns: this world was made before Earth recorded its ground sets; it reads the 30\" sets {0}.",here?"on this machine":"that are here (not all of them are)");
                return true;
            }
            if(p.GroundData==GroundDownloader.TwoMinuteOnly)
            {
                Mod.Logger.Notification("Earth ground columns: this world was made with the ground pack's 2' sets and keeps them; the 30\" sets are not read.");
                return false;
            }
            GroundDownloader.Result got;
            string error=null;
            // checked against their SHA-256, also for a saved 30″ world: a damaged file of the same size would change its ground
            try{got=GroundDownloader.Missing(folder,true).Count==0?GroundDownloader.Result.Here:GroundDownloader.Get(folder,repo,s=>{Mod.Logger.Notification("Earth ground sets: "+s);Mod.Logger.StoryEvent("Earth ground sets: "+s);},core?.Stopping??default);}
            catch(Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or AggregateException)
            {
                if(e is OperationCanceledException && core?.Stopping.IsCancellationRequested==true)
                {
                    Mod.Logger.Notification("Earth ground sets: the download stopped with the server; it resumes at the next start (nothing is saved).");
                    return false;
                }
                error=e.Message;
                Mod.Logger.Warning("Earth ground sets: {0}",e.Message);
                got=GroundDownloader.Result.Failed;
            }
            string sets=System.IO.Path.Combine(folder,GroundDownloader.Subfolder);
            // review A3: only the source's answer that it can't have them (none set, or it doesn't have a set) decides a new
            // world's ground for good; a failure that may pass stops the server before the game runs (nothing is saved, the
            // world is created again at the next start), and a 30" world never goes on with the 2' sets
            if(p.GroundData==0 && got!=GroundDownloader.Result.Failed)
            {
                bool ok=got==GroundDownloader.Result.Here;
                p.GroundData=ok?GroundDownloader.SetsVersion:GroundDownloader.TwoMinuteOnly;
                core.SaveParams();
                if(ok)Mod.Logger.Notification("Earth ground columns: this new world reads the 30\" ground sets (release egts-v1) in {0}.",sets);
                else Mod.Logger.Warning("Earth ground columns: the 30\" ground sets can't be had ({0}); this new world uses the ground pack's 2' sets for good, so its cover stays one kind. New worlds try again.",
                    string.IsNullOrWhiteSpace(repo)?"GroundRepo in earth.json is empty":"the source doesn't have them, see above");
                return ok;
            }
            if(got==GroundDownloader.Result.Here)return true;
            Mod.Logger.Error("{0}",p.GroundData==0
                ?"Earth ground columns: the 30\" ground sets could not be fetched into "+sets+" ("+(error??"see above")+"). The server stops, so that a failure that may pass doesn't leave this new world with the 2' sets for good; nothing is saved, start again once the server reaches "+(string.IsNullOrWhiteSpace(repo)?GroundDownloader.Release:repo)+", or put the three files from "+GroundDownloader.Release+" there."
                :"Earth ground columns: this world was made with the 30\" ground sets, but they are missing from "+sets+" and could not be had ("+(error??"see above")+"). The server stops, so that no chunk of this world gets the 2' sets; put the three files from "+GroundDownloader.Release+" there, or let Earth fetch them (GroundRepo in earth.json), and start again.");
            api.Server.ShutDown();
            return false;
        }

        /// <summary>
        /// Chooses each material's block. A saved world keeps the blocks it was generated with whenever they still
        /// exist (adding or updating Geology Additions never changes an existing world); new worlds prefer provider
        /// assets that exist, then Earth's own copy of the rock (gneiss, schist, dolostone), then the vanilla stand-in.
        /// The fingerprint is informational: a change is logged and the new value stored.
        /// </summary>
        /// <summary>(Decision 0083) The rocks this world was generated with whose block is gone this session (a provider mod
        /// removed).</summary>
        readonly List<string> lostBlocks=new();

        /// <summary>(Decision 0083; FP-02) The parts of regional geology this world's saved rules use that are off this session,
        /// for EarthWorldGen's check before the first chunk; each logged its own line when it went off.</summary>
        internal void FeaturesOff(EarthParams p,List<string> off)
        {
            if(!p.RegionalGeology || core?.Model==null)return;
            if(!Active){off.Add("regional geology (its line is above)");return;}
            if(lostBlocks.Count>0)off.Add("regional geology's rocks as generated ("+string.Join(", ",lostBlocks)+": their block no longer exists)");
            if(GroundColumnFacts.Columns(p.GroundColumns) && ground==null)off.Add("ground columns (their line is above)");
            if(OreLayerFacts.Layers(p.OreLayers) && ground!=null && layers==null)off.Add("ore layers (their line is above)");
            if(!soilHook)off.Add("regional geology's surface cover (vanilla's soil-layer hook is missing on this game version)");
            if(!depositHook)off.Add("regional geology's clay, peat and salt placement (vanilla's deposit hook is missing on this game version)");
            if(GeologyFacts.GravelLenses(rules) && !depositColumnHook)off.Add("regional geology's deposit layers (vanilla's deposit-column hook is missing on this game version)");
        }

        bool ResolveMaterials(EarthParams p,RegionalGeologyCatalog catalog,string grid,string rulesText)
        {
            lostBlocks.Clear();
            var saved=ParseSignature(p.GeologyProviderSignature);
            var legacy=new List<string>{"rules="+p.GeologyRulesVersion,"data="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(grid+rulesText)))};
            var current=new List<string>{"rules="+p.GeologyRulesVersion,"data="+catalog.Fingerprint};
            var proxies=new List<string>();
            rocks.Clear();
            var blocks=new int[catalog.Materials.Count];
            for(int i=0;i<catalog.Materials.Count;i++)
            {
                var m=catalog.Materials[i];
                bool providerOn=m.Provider!=null && api.ModLoader.IsModEnabled(m.Provider);
                // What 0.12 computed, only to recognise an unchanged 0.12 fingerprint without rewriting it.
                string legacySelected=m.Vanilla;
                if(providerOn)
                {
                    legacy.Add(m.Provider+"="+api.ModLoader.Mods.FirstOrDefault(mod=>mod.Info?.ModID==m.Provider)?.Info?.Version);
                    if(m.Asset!=null && BlockId(m.Asset)>0)legacySelected=m.Asset;
                }
                legacy.Add(m.Id+"="+legacySelected);
                string chosen=null;
                if(saved.TryGetValue(m.Id,out string savedCode))
                {
                    if(BlockId(savedCode)>0)chosen=savedCode;
                    else
                {
                    Mod.Logger.Warning("Earth geology: '{0}' was generated as {1} in this world, but that block no longer exists (provider removed?). New chunks fall back; expect a visible change where they meet existing chunks.",m.Id,savedCode);
                    lostBlocks.Add(m.Id+" was "+savedCode);
                }
                }
                if(chosen==null && providerOn && m.Asset!=null)
                {
                    if(BlockId(m.Asset)>0)chosen=m.Asset;
                    else Mod.Logger.Warning("Earth geology: {0} is enabled but provides no {1}; '{2}' uses vanilla {3}.",m.Provider,m.Asset,m.Id,m.Vanilla);
                }
                // Earth's own copy of the rock (patches ground-*-rock.json, with ore blocks and deposit hosts copied from the
                // stand-in), for new worlds only: a saved world keeps the block it recorded above.
                if(chosen==null && !providerOn && m.Asset!=null && BlockId(m.Asset)>0)chosen=m.Asset;
                chosen??=m.Vanilla;
                int id=BlockId(chosen);
                if(id<=0)
                {
                    Mod.Logger.Error("Earth geology: material {0} for '{1}' does not exist in this game version; regional geology is off for this session.",chosen,m.Id);
                    return false;
                }
                blocks[i]=id;
                rocks[m.Id]=id;
                current.Add(m.Id+"="+chosen);
                if(m.Asset!=null && chosen==m.Vanilla)proxies.Add(m.Id+"→"+m.Vanilla);
            }
            materialBlock=blocks;
            mappingDescription=proxies.Count==0?"provider-specific rocks":string.Join(", ",proxies);
            string legacySignature=string.Join(";",legacy.Distinct().OrderBy(s=>s,StringComparer.Ordinal));
            string signature=string.Join(";",current.Distinct().OrderBy(s=>s,StringComparer.Ordinal));
            string stored=p.GeologyProviderSignature;
            if(string.IsNullOrEmpty(stored)){p.GeologyProviderSignature=signature;core.SaveParams();}
            else if(stored!=signature && stored!=legacySignature)
            {
                Mod.Logger.Warning("Earth geology: the geology data/material fingerprint differs from the one saved with this world. Generation continues with the world's saved rules v{0} and saved materials; the new fingerprint is stored. Already generated chunks are unchanged. Saved: {1}; now: {2}",
                    p.GeologyRulesVersion,Brief(stored),Brief(signature));
                p.GeologyProviderSignature=signature;
                core.SaveParams();
            }
            return true;
        }
        static string Brief(string signature)=>string.Join(";",(signature??"").Split(';').Where(s=>s.StartsWith("data=",StringComparison.Ordinal)||s.StartsWith("rules=",StringComparison.Ordinal)).Select(s=>s.Length>20?s.Substring(0,20)+"…":s));
        static Dictionary<string,string> ParseSignature(string signature)
        {
            var map=new Dictionary<string,string>(StringComparer.Ordinal);
            if(string.IsNullOrEmpty(signature))return map;
            foreach(string part in signature.Split(';'))
            {
                int eq=part.IndexOf('=');
                if(eq<=0)continue;
                string key=part.Substring(0,eq),value=part.Substring(eq+1);
                if(value.Contains(':'))map[key]=value; // material entries carry a domain-qualified block code
            }
            return map;
        }
        int BlockId(string code)
        {
            if(string.IsNullOrWhiteSpace(code))return 0;
            try{return api.World.GetBlock(new AssetLocation(code))?.Id??0;}
            catch(Exception){return 0;}
        }

        void BuildBlockTables(RegionalGeologyCatalog catalog)
        {
            int count=api.World.Blocks.Count;
            var replace=new bool[count];var rock=new bool[count];var cover=new bool[count];var clayPeat=new bool[count];var gravel=new bool[count];
            var sand=new int[count];var grav=new int[count];
            var preserve=new HashSet<string>(catalog.Preserve,StringComparer.Ordinal);
            foreach(var block in api.World.Blocks)
            {
                if(block?.Code==null || block.Id<=0 || block.Id>=count)continue;
                string path=block.Code.Path;
                // EarthOres replaces unrestricted bauxite strata separately when regional ores is enabled.
                rock[block.Id]=path.StartsWith("rock-",StringComparison.Ordinal);
                replace[block.Id]=rock[block.Id] && !preserve.Contains(path);
                cover[block.Id]=path.StartsWith("soil-",StringComparison.Ordinal) || path.StartsWith("sand-",StringComparison.Ordinal) || path.StartsWith("gravel-",StringComparison.Ordinal);
                clayPeat[block.Id]=path.StartsWith("rawclay-",StringComparison.Ordinal) || path.StartsWith("peat-",StringComparison.Ordinal);
                if(rock[block.Id])
                {
                    string suffix=path.Substring(5);
                    sand[block.Id]=api.World.GetBlock(new AssetLocation(block.Code.Domain,"sand-"+suffix))?.Id??0;
                    grav[block.Id]=api.World.GetBlock(new AssetLocation(block.Code.Domain,"gravel-"+suffix))?.Id??0;
                    if(grav[block.Id]>0 && grav[block.Id]<count)gravel[grav[block.Id]]=true;
                }
            }
            replaceRock=replace;isRock=rock;surfaceCover=cover;clayOrPeat=clayPeat;gravelBlock=gravel;sandOf=sand;gravelOf=grav;
            glacierIceId=BlockId("game:glacierice");
        }

        void MaskSaltMap(IMapRegion region,int regionX,int regionZ,ITreeAttribute chunkGenParams=null)
        {
            if(!Active || region.OreMaps==null || !region.OreMaps.TryGetValue("halite",out var map) || map?.Data==null || map.InnerSize<=0)return;
            var geometry=core.Model.Geometry;
            double step=(double)api.WorldManager.RegionSize/map.InnerSize;
            // These are native generation potentials, not a geological evidence map.
            // Missing positive records must not zero an unknown region or change its native value.
            for(int z=0;z<map.Size;z++)for(int x=0;x<map.Size;x++)
            {
                double wx=regionX*api.WorldManager.RegionSize+(x-map.TopLeftPadding+.5)*step;
                double wz=regionZ*api.WorldManager.RegionSize+(z-map.TopLeftPadding+.5)*step;
                var geo=geometry.ToGeo(wx,wz);
                int index=z*map.Size+x;
                map.Data[index]=RegionalGeologyCatalog.SaltMapValue(map.Data[index],geo.InGutter||geo.BeyondPole);
            }
        }

        /// <summary>
        /// Pads Geology Additions child-deposit grade lists like the native parent generator clamps them, and drops
        /// unresolved hosts: it prevents a native crash. Runs in Earth worlds with regional geology whenever GA is installed
        /// (at world start and after every deposit reload); other worlds keep GA's deposit generators as they are.
        /// </summary>
        internal static void AfterDepositReload(GenDeposits __instance)
        {
            var self=instance;
            // 0060: the reload brings Earth's layer deposits back; only ore-layer worlds keep them (and Earth's ore and gem
            // deposits only worlds of their deposit set)
            if(self!=null)try{EarthOreLayers.PruneDeposits(self.api,__instance,self.layers?.Rules.Version??0,EarthOreLayers.DepositSetOf(self.core?.Params));}catch(Exception e){self.Mod.Logger.Warning("Earth geology: sorting out Earth's own deposits after a deposit reload failed: {0}",e.Message);}
            if(self?.core?.Params?.Enabled!=true || !self.core.Params.RegionalGeology)return;
            self.layers?.Unbind(); // 0060: the deposit blocks are bound again at the next chunk
            try{self.NormalizeGeologyAdditionsChildGrades(__instance);}
            catch(Exception e){self.Mod.Logger.Warning("Earth GA child-grade compatibility adapter was not re-applied after a deposit reload: {0}",e.Message);}
        }
        void NormalizeGeologyAdditionsChildGrades(GenDeposits generator=null)
        {
            var provider=api.ModLoader.Mods.FirstOrDefault(m=>m.Info?.ModID=="geoaddons");
            if(provider==null)return;
            generator??=api.ModLoader.GetModSystem<GenDeposits>();
            // vanilla's host and indicator tables through the ore area's adapter (OreVanilla), null when changed
            if(generator?.Deposits==null || !OreVanilla.HostTables || !OreVanilla.IndicatorTables)
            {
                Mod.Logger.Warning("Earth GA child-grade adapter is unavailable on this game version (deposits resolved={0}); Geology Additions {1} runs unmodified.",generator?.Deposits!=null,provider.Info.Version);
                return;
            }
            if(provider.Info.Version!="1.4.8")
                Mod.Logger.Notification("Earth GA child-grade adapter: Geology Additions {0} (reviewed: 1.4.8); applying the same crash-only clamp.",provider.Info.Version);
            int padded=0,omitted=0;
            foreach(var parent in generator.Deposits)
            {
                if(parent?.ChildDeposits==null)continue;
                foreach(var child in parent.ChildDeposits)
                {
                    if(child?.GeneratorInst is not ChildDepositGenerator childGen)continue;
                    if(OreVanilla.HostTable(childGen) is not Dictionary<int,ResolvedDepositBlock> byHost)continue;
                    var bySurface=OreVanilla.IndicatorTable(childGen);
                    int grades=Math.Max(1,childGen.PlaceBlock?.AllowedVariants?.Length??1);
                    foreach(var entry in byHost.ToArray())
                    {
                        var blocks=entry.Value?.Blocks;
                        string host=entry.Key>=0 && entry.Key<api.World.Blocks.Count ? api.World.Blocks[entry.Key]?.Code?.ToString() : entry.Key.ToString(CultureInfo.InvariantCulture);
                        bool missingSurface=childGen.SurfaceBlock!=null && (bySurface==null || !bySurface.TryGetValue(entry.Key,out var indicator)||indicator?.Blocks?.Length is not >0);
                        if(blocks?.Length is not >0 || missingSurface)
                        {
                            byHost.Remove(entry.Key);
                            omitted++;
                            Mod.Logger.Warning("Earth GA child compatibility: omitted unsupported host {0}, child={1}, parent={2}, source={3}; placement variants={4}, missing indicator={5}.",host,child.Code,parent.Code,parent.fromFile,blocks?.Length??0,missingSurface);
                        }
                        else if(blocks.Length<grades)
                        {
                            entry.Value.Blocks=RegionalGeologyCompatibility.PadChildGrades(blocks,grades);
                            padded++;
                            Mod.Logger.Warning("Earth GA child compatibility: clamped {0} global grades to {1} supported grades for host={2}, child={3}, parent={4}, source={5}.",grades,blocks.Length,host,child.Code,parent.Code,parent.fromFile);
                        }
                    }
                }
            }
            Mod.Logger.Notification("Earth GA {0} child compatibility: {1} host grade arrays normalized; {2} unresolved host entries omitted. Native deposit frequency and supported grades retained.",provider.Info.Version,padded,omitted);
        }

        // ------------------------------------------------------------------ per-chunk column cache

        sealed class ColumnCache
        {
            public int Epoch,ChunkX,ChunkZ;
            public bool Valid;
            public readonly RegionalGeologyCatalog.Sample[] Samples=new RegionalGeologyCatalog.Sample[1024];
            public readonly double[] Lat=new double[1024],Lon=new double[1024];
        }
        /// <summary>The strata and soil passes of a chunk column read the same 1024 samples: computed once per thread and chunk.
        /// VS 1.22.7 runs each world-gen pass on its own thread (ServerSystemSupplyChunks), so a pass on another thread (the
        /// deposit pass, a rebuilt layer table) computes its own.</summary>
        [ThreadStatic] static ColumnCache columnCache;
        ColumnCache Columns(int chunkX,int chunkZ)
        {
            var c=columnCache??=new ColumnCache();
            int e=Volatile.Read(ref epoch);
            if(c.Valid && c.Epoch==e && c.ChunkX==chunkX && c.ChunkZ==chunkZ)return c;
            c.Valid=false;
            int cx=chunkX*32,cz=chunkZ*32;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                int k=z*32+x;
                c.Samples[k]=SampleAt(cx+x,cz+z,out var geo);
                c.Lat[k]=geo.Lat;c.Lon[k]=geo.Lon;
            }
            c.Epoch=e;c.ChunkX=chunkX;c.ChunkZ=chunkZ;c.Valid=true;
            return c;
        }

        /// <summary>ER-08: a pass that threw for a chunk column refuses the world as the terrain does (EarthWorldGen.FailedPass:
        /// the chunk is made again at the next start, the server stops); throws (but for a second failure of a chunk made anew,
        /// FP-14, which goes on).</summary>
        void ReportGenerationError(string pass,IServerChunk[] chunks,int chunkX,int chunkZ,Exception e)
        {
            Interlocked.Increment(ref generationErrors);
            EarthWorldGen.FailedPass("geology ("+pass+")",chunks,chunkX,chunkZ,e);
        }

        // ------------------------------------------------------------------ strata

        internal static void AfterStrata([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var self=instance;
            if(self?.Active!=true)return;
            // (FP-13) a chunk in flight after a refusal is noted and stops here
            EarthWorldGen.ThrowIfRefusedChunk(request.Chunks,request.ChunkX,request.ChunkZ);
            // (FP-11/FP-12) generation: this thread may wait for GLO-90 tiles
            using var fetch=FineElevation.Generating();
            try{self.ApplyStrata(request);}
            catch(Exception e){self.ReportGenerationError("rock strata",request.Chunks,request.ChunkX,request.ChunkZ,e);}
        }
        void ApplyStrata(IChunkColumnGenerateRequest request)
        {
            var st=strata??=new StrataColumns();
            // (Fable's overview low 11, 14 November 2026) the per-thread state keeps no geology profile past the pass (ER-22
            // removed the same pattern elsewhere): only numbers stay between chunks
            try{ApplyStrata(request,st);}
            finally{Array.Clear(st.Profile);}
        }

        void ApplyStrata(IChunkColumnGenerateRequest request,StrataColumns st)
        {
            var chunks=request.Chunks;
            var map=chunks[0].MapChunk;
            var cols=Columns(request.ChunkX,request.ChunkZ);
            int cx=request.ChunkX*32,cz=request.ChunkZ*32,maxY=chunks.Length*32-1;
            bool v1=!GeologyFacts.SmoothedBeds(rules);
            // each column's top, beds and bed boundaries first (reads only: StrataTop looks at the column's own ice)
            bool everyColumn=true;
            int floor=int.MaxValue;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                int k=z*32+x;
                st.Top[k]=0;
                var sample=cols.Samples[k];
                if(!sample.HasRegionalBedrock){everyColumn=false;continue;}
                int top=StrataTop(chunks,map.WorldGenTerrainHeightMap[k],x,z,cx+x,cz+z,maxY);
                if(top<1){everyColumn=false;continue;}
                var profile=sample.Profile;
                var bounds=st.Bounds.AsSpan(k*RegionalGeologyCatalog.MaxBeds,RegionalGeologyCatalog.MaxBeds);
                if(v1)st.Beds[k]=RegionalGeologyCatalog.ColumnV1(profile,top,cols.Lat[k],cols.Lon[k],seed,bounds);
                else
                {
                    reference.At(cx+x,cz+z,out double datum,out double thickness);
                    st.Beds[k]=RegionalGeologyCatalog.ColumnV2(profile,datum,thickness,bounds);
                }
                st.Top[k]=top;st.Profile[k]=profile;st.Bed[k]=0;st.TopFound[k]=false;
                floor=Math.Min(floor,top);
            }
            // (EA-0113, the Fable pass's FP-30) where every column has regional bedrock, the layers below the lowest top go a
            // layer at a time: read, and where all 1024 blocks are rock to replace and their bed is one material, written
            // with one SetBlockBulk instead of 1024 writes. Above them, and in a chunk where a column has none, block by block
            // as before. The blocks written are the same: each column's beds, its top rock and the skips are worked out as the
            // column-by-column loop did (a bed's search runs forward from the last bed; its answer doesn't depend on where it
            // starts as long as that isn't past it), only in another order.
            int deep=everyColumn?floor:0;
            for(int k=0;k<1024;k++)
                if(st.Top[k]>=1)for(int y=st.Top[k];y>deep;y--)StrataBlock(chunks,map,st,v1,k,y);
            for(int y=deep;y>=1;y--)StrataLayer(chunks,map,st,v1,y);
        }

        /// <summary>(FP-30) The strata pass's state for the columns of one chunk, kept per thread; its profiles are cleared at
        /// the end of each pass (low 11).</summary>
        sealed class StrataColumns
        {
            public readonly int[] Top=new int[1024],Beds=new int[1024],Bed=new int[1024],Rock=new int[1024];
            public readonly bool[] TopFound=new bool[1024];
            public readonly RegionalGeologyCatalog.Profile[] Profile=new RegionalGeologyCatalog.Profile[1024];
            public readonly int[] Bounds=new int[1024*RegionalGeologyCatalog.MaxBeds];
        }
        [ThreadStatic] static StrataColumns strata;

        bool Replaceable(int id)=>id>0 && id<replaceRock.Length && (replaceRock[id] || id==strataBauxite);

        /// <summary>One block of column k at height y, as the strata pass always did.</summary>
        void StrataBlock(IServerChunk[] chunks,IMapChunk map,StrataColumns st,bool v1,int k,int y)
        {
            int index=(y%32)*1024+k;
            var data=chunks[y/32].Data;
            int existing=data.GetBlockIdUnsafe(index);
            if(existing<=0 || existing>=replaceRock.Length)return;
            if(!replaceRock[existing] && existing!=strataBauxite)
            {
                if(!st.TopFound[k] && isRock[existing]){map.TopRockIdMap[k]=existing;st.TopFound[k]=true;}
                return;
            }
            int rock=StrataRock(st,v1,k,y);
            data.SetBlockUnsafe(index,rock);
            if(!st.TopFound[k]){map.TopRockIdMap[k]=rock;st.TopFound[k]=true;}
        }

        /// <summary>Column k's rock at height y; moves its bed on.</summary>
        int StrataRock(StrataColumns st,bool v1,int k,int y)
        {
            var bounds=st.Bounds.AsSpan(k*RegionalGeologyCatalog.MaxBeds,RegionalGeologyCatalog.MaxBeds);
            st.Bed[k]=v1?RegionalGeologyCatalog.BedV1(st.Top[k]-y,bounds,st.Beds[k],st.Bed[k]):RegionalGeologyCatalog.BedV2(y,bounds,st.Beds[k],st.Bed[k]);
            return materialBlock[RegionalGeologyCatalog.MaterialOfBed(st.Profile[k],st.Bed[k])];
        }

        /// <summary>(FP-30) One layer of all 1024 columns below every column's top.</summary>
        void StrataLayer(IServerChunk[] chunks,IMapChunk map,StrataColumns st,bool v1,int y)
        {
            var data=chunks[y/32].Data;
            int at=(y%32)*1024;
            for(int k=0;k<1024;k++)
                if(!Replaceable(data.GetBlockIdUnsafe(at+k)))
                {
                    for(int c=0;c<1024;c++)StrataBlock(chunks,map,st,v1,c,y);
                    return;
                }
            int one=st.Rock[0]=StrataRock(st,v1,0,y);
            bool same=true;
            for(int k=1;k<1024;k++)
                if((st.Rock[k]=StrataRock(st,v1,k,y))!=one)same=false;
            if(same)data.SetBlockBulk(at,32,32,one);
            else for(int k=0;k<1024;k++)data.SetBlockUnsafe(at+k,st.Rock[k]);
            for(int k=0;k<1024;k++)
                if(!st.TopFound[k]){map.TopRockIdMap[k]=st.Rock[k];st.TopFound[k]=true;}
        }

        /// <summary>
        /// Highest block the strata pass rewrites: the terrain top, or just below glacier ice. Earth's terrain pass fills
        /// ice contiguously from the model's IceBottom to the top, so scanning the ice blocks equals IceBottom-1
        /// without recomputing the terrain column.
        /// </summary>
        int StrataTop(IServerChunk[] chunks,int heightmap,int x,int z,double wx,double wz,int maxY)
        {
            int top=Math.Min(heightmap,maxY);
            if(glacierIceId>0)
            {
                while(top>=1 && chunks[top/32].Data.GetBlockIdUnsafe(((top%32)*32+z)*32+x)==glacierIceId)top--;
                return top;
            }
            return Math.Min(top,ColumnStore.At(core.Model,wx,wz).IceBottom-1);
        }

        // ------------------------------------------------------------------ surface cover

        internal static void AfterSoil([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var self=instance;
            if(self?.Active!=true)return;
            EarthWorldGen.ThrowIfRefusedChunk(request.Chunks,request.ChunkX,request.ChunkZ);
            using var fetch=FineElevation.Generating();
            try
            {
                var cols=self.Columns(request.ChunkX,request.ChunkZ);
                self.ApplyCover(request,cols);
                if(self.ground!=null)self.ApplyGround(request,cols);
                self.RestoreTopRock(request);
            }
            catch(Exception e){self.ReportGenerationError("surface cover and ground",request.Chunks,request.ChunkX,request.ChunkZ,e);}
            self.AfterGround?.Invoke(request);
        }
        /// <summary>After the ground pass of each chunk column (0060: the ore maps lay their beds here in ore-layer worlds).
        /// Subscribers catch their own errors.</summary>
        internal event Action<IChunkColumnGenerateRequest> AfterGround;
        void RestoreTopRock(IChunkColumnGenerateRequest request)
        {
            var chunks=request.Chunks;var map=chunks[0].MapChunk;
            int maxY=chunks.Length*32-1;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                int k=z*32+x;
                // Only glacier ice (never rock) lies between Earth's IceBottom and the top, so the first rock below the top is the same.
                for(int y=Math.Min(map.WorldGenTerrainHeightMap[k],maxY);y>0;y--)
                {
                    int id=chunks[y/32].Data.GetBlockIdUnsafe(((y%32)*32+z)*32+x);
                    if(id>0 && id<isRock.Length && isRock[id]){map.TopRockIdMap[k]=id;break;}
                }
            }
        }
        void ApplyCover(IChunkColumnGenerateRequest request,ColumnCache cols)
        {
            var chunks=request.Chunks;
            var map=chunks[0].MapChunk;
            int cx=request.ChunkX*32,cz=request.ChunkZ*32,maxY=chunks.Length*32-1;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                int k=z*32+x;
                var s=cols.Samples[k];
                if(s.OutsideEarth || (ground!=null && ground.Covers(cols.Lat[k],cols.Lon[k])))continue;
                // Unambiguous sediment textures only. A map labelled alluvium does not establish gravel vs clay.
                bool dune=s.Sediment==11;
                bool coarse=s.Sediment is 2 or 15 or 19;
                if(!dune && !coarse)continue;
                int top=map.WorldGenTerrainHeightMap[k],rock=map.TopRockIdMap[k];
                if(top<4 || rock<=0 || rock>=sandOf.Length)continue;
                int id=dune?sandOf[rock]:gravelOf[rock];
                if(id<=0)continue;
                int thickness=2;
                if(GeologyFacts.GravelLenses(rules) && coarse)
                {
                    // v2: discontinuous lenses, mostly one block, two in their cores
                    double coverage=Catalog.GravelCoverage(s.Sediment);
                    if(coverage<=0)continue;
                    double mask=GeologyTransitions.GravelMask(cx+x,cz+z,seed);
                    if(mask<GeologyTransitions.GravelThreshold(coverage))continue;
                    thickness=mask>=GeologyTransitions.GravelThreshold(coverage*GravelCoreShare)?2:1;
                }
                // Water columns carry Earth's water directly above the terrain top (exact, and cheaper than the terrain model).
                if(top+1<=maxY && chunks[(top+1)/32].Data.GetFluid((((top+1)%32)*32+z)*32+x)!=0)continue;
                var c=ColumnStore.At(core.Model,cx+x,cz+z);
                if(c.IceBottom<=c.Top || c.Water!=TerrainModel.WaterNone || c.Beach)continue;
                // Vegetated old dunes retain their topsoil. Till retains soil; its coarse fraction is a proxy, not pure gravel.
                int cap=dune && core.Model.Data.SampleClimate(cols.Lat[k],cols.Lon[k]).Precip<600?0:2;
                for(int depth=cap;depth<cap+thickness;depth++)
                {
                    int y=top-depth,index=((y%32)*32+z)*32+x;
                    var data=chunks[y/32].Data;
                    int old=data.GetBlockIdUnsafe(index);
                    if(old>0 && old<surfaceCover.Length && (surfaceCover[old] || replaceRock[old]))data.SetBlockUnsafe(index,id);
                }
            }
        }

        /// <summary>Ground columns prototype: rewrites the natural ground of every column inside the ground grid.</summary>
        void ApplyGround(IChunkColumnGenerateRequest request,ColumnCache cols)
        {
            var table=layers?.Begin(request.ChunkX,request.ChunkZ);
            GroundPass(request.Chunks,request.ChunkX,request.ChunkZ,cols,true,table);
            if(table!=null)layers.Keep(table);
        }
        /// <summary>0060: the layer table of a chunk column whose ground pass ran in an earlier session.</summary>
        bool RebuildLayerTable(IServerChunk[] chunks,int chunkX,int chunkZ,EarthOreLayers.Table table)
        {
            if(!Active || ground==null)return false;
            GroundPass(chunks,chunkX,chunkZ,Columns(chunkX,chunkZ),false,table);
            return true;
        }
        /// <summary>Builds every ground column of a chunk column; <paramref name="write"/>: rewrites its blocks (the ground
        /// pass). Records the columns' layers in <paramref name="table"/> when given (0060).</summary>
        void GroundPass(IServerChunk[] chunks,int chunkX,int chunkZ,ColumnCache cols,bool write,EarthOreLayers.Table table)
        {
            var map=chunks[0].MapChunk;
            int cx=chunkX*32,cz=chunkZ*32,maxY=chunks.Length*32-1;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                int k=z*32+x;
                double lat=cols.Lat[k],lon=cols.Lon[k];
                if(cols.Samples[k].OutsideEarth || !ground.Covers(lat,lon))continue;
                var c=ColumnStore.At(core.Model,cx+x,cz+z);
                // (W-L3, code review 14 November 2026) a table rebuilt in a later session reads the height map after the
                // volcano craters lowered it; the ground pass saw the terrain's own top there, which craters only ever lower.
                // Elsewhere the height map is what the ground pass saw: vanilla's caves (Terrain pass, before it) lower it
                // at their mouths, and the rebuild must read that too (Fable's review of ae8e89d).
                int hm=map.WorldGenTerrainHeightMap[k];
                int top=Math.Min(!write && EarthVolcanoes.Instance?.CraterCarves(cx+x,cz+z)==true?Math.Max(hm,c.Top):hm,maxY);
                if(top<2)continue;
                if(c.IceBottom<=c.Top)continue; // glacier ice keeps the strata below it
                bool water=top+1<=maxY && chunks[(top+1)/32].Data.GetFluid((((top+1)%32)*32+z)*32+x)!=0;
                var surface=water?(c.River?GroundColumns.Surface.RiverBed:GroundColumns.Surface.Water):c.Beach?GroundColumns.Surface.Beach:GroundColumns.Surface.Land;
                var sample=GroundAt(lat,lon,cx+x,cz+z,c,surface,cols.Samples[k].HasRegionalBedrock);
                var col=write?ground.Write(chunks,x,z,top,surface,sample):ground.Build(sample,surface);
                if(table!=null)layers.Record(table,k,top,col,sample.Era,cols.Samples[k].HasRegionalBedrock?cols.Samples[k].Profile?.Id:null);
            }
        }
        /// <summary>The ground sample with what the terrain knows about the place (elevation, lake or sea, river valley) and
        /// whether regional geology maps rock here.</summary>
        GroundColumns.Sample GroundAt(double lat,double lon,double wx,double wz,in TerrainModel.Column c,GroundColumns.Surface surface,bool regionalRock)
        {
            var place=new GroundColumns.Place{Elevation=c.Elev,Surface=surface,Lake=c.Water is 2 or 3,NearRiver=c.NearRiver||c.River,RegionalRock=regionalRock,Shore=c.Shore,ShoreRiseBlocks=c.Beach&&c.Shore!=0?Math.Clamp(c.Top-(c.NearLakeTop>=0?c.NearLakeTop:core.Model.SeaLevel-1),0,8):0,ShoreRules=core.Model.ShoreRules};
            // 0048 soil rules 2: real deserts carry no topsoil; a river's green belt is counted in as the climate map has it
            place.GroundRules=groundRules;
            EarthData.ClimateSample cs=default;bool sampled=false;
            // ground rules 2 (EA-0062): the yearly mean at the place's own height, for rules such as warm karst's terra rossa
            if(GroundColumnFacts.WarmKarst(groundRules) && surface!=GroundColumns.Surface.Water)
            {
                cs=core.Model.Data.SampleClimate(lat,lon);sampled=true;
                place.HasTemperature=!double.IsNaN(cs.TMean);
                place.TMean=cs.TMean-EarthWorldGen.LapsePerMetre*Math.Max(0,c.Elev);
            }
            if(soilRules>=SoilFertility.GroundRules && surface==GroundColumns.Surface.Land)
            {
                if(!sampled)cs=core.Model.Data.SampleClimate(lat,lon);
                place.Arid=SoilFertility.AridGround(cs.Rain,cs.DrySummer,cs.PenmanFactor) && SoilFertility.AridGround(core.Model.RiparianRain(wx,wz,cs.Rain),cs.DrySummer,cs.PenmanFactor);
                // soil rules 3: the natural flood of a river with dry-season water keeps its soil (FloodplainGrid), wind-blown
                // sand excepted (GroundColumns.WindSand, once the cover is known)
                place.Floodplain=place.Arid && soilRules>=SoilFertility.FloodplainRules && core.Model.Floodplains?.Floods(lat,lon,c.Elev)==true;
            }
            if(ground.Model.ReliefRadiusMetres>0)
            {
                int bx=(int)Math.Floor(wx),bz=(int)Math.Floor(wz);
                var r=ReliefFor(Math.DivRem(bx,32,out int rx)-(rx<0?1:0),Math.DivRem(bz,32,out int rz)-(rz<0?1:0));
                int k=((bz%32+32)%32)*32+(bx%32+32)%32;
                place.HasRelief=!double.IsNaN(r.Mean[k]);
                place.Relief=c.Elev-r.Mean[k];place.Slope=r.Slope[k];place.ReliefNorm=r.Norm[k];
            }
            return ground.Model.At(lat,lon,place);
        }
        /// <summary>Mean ground around each column of a chunk and its slope (GroundRelief), once per chunk and thread. Metres per
        /// block from the chunk's corner, whichever column asks first, so the ground pass and later readings agree.</summary>
        sealed class ReliefCache{public int Epoch,ChunkX,ChunkZ;public bool Valid;public readonly double[] Mean=new double[1024],Slope=new double[1024],Norm=new double[1024];}
        [ThreadStatic] static ReliefCache reliefCache;
        ReliefCache ReliefFor(int chunkX,int chunkZ)
        {
            var r=reliefCache??=new ReliefCache();
            int e=Volatile.Read(ref epoch);
            if(r.Valid && r.Epoch==e && r.ChunkX==chunkX && r.ChunkZ==chunkZ)return r;
            var geometry=core.Model.Geometry;
            double lat=geometry.ToGeo(chunkX*32,chunkZ*32).Lat;
            double mz=core.Model.MetresPerBlock,mx=Math.Max(1e-3,111320*Math.Cos(lat*Math.PI/180)/geometry.BlocksPerDegreeX);
            GroundRelief.Field(chunkX*32,chunkZ*32,32,(x,z)=>ColumnStore.Near(core.Model,x,z).Elev,mx,mz,ground.Model.ReliefRadiusMetres,r.Mean,r.Slope,ground.Model.ReliefWeight,r.Norm);
            r.Epoch=e;r.ChunkX=chunkX;r.ChunkZ=chunkZ;r.Valid=true;
            return r;
        }
        static GroundColumns.Surface SurfaceOf(in TerrainModel.Column c)=>c.River?GroundColumns.Surface.RiverBed:c.Water!=TerrainModel.WaterNone?GroundColumns.Surface.Water:c.Beach?GroundColumns.Surface.Beach:GroundColumns.Surface.Land;

        /// <summary>v2: after the deposit pass, gravel directly below a clay/peat patch becomes part of that patch.</summary>
        internal static void AfterDeposits([HarmonyArgument(0)] IChunkColumnGenerateRequest request)
        {
            var self=instance;
            if(self?.Active!=true)return;
            EarthWorldGen.ThrowIfRefusedChunk(request.Chunks,request.ChunkX,request.ChunkZ);
            // (FP-12) the deposit catalogue's bodies (AfterDepositLayers) run their valley search here
            using var fetch=FineElevation.Generating();
            if(GeologyFacts.GravelLenses(self.rules))
                try{self.ClearGravelUnderPatches(request);}
                catch(Exception e){self.ReportGenerationError("clay/peat cleanup",request.Chunks,request.ChunkX,request.ChunkZ,e);}
            // 0060: deposit blocks back out of layers their deposit may not use
            // 0065: the layer table stays for the catalogue's bodies (same layers as the pass) and goes after them
            if(self.layers!=null)
                try{self.layers.Apply(request.Chunks,request.ChunkX,request.ChunkZ);}
                catch(Exception e){self.ReportGenerationError("ore layer pass",request.Chunks,request.ChunkX,request.ChunkZ,e);}
            // 0065: the deposit catalogue's bodies, already in their layers, after the layer pass
            try{self.AfterDepositLayers?.Invoke(request);}
            finally{self.layers?.Release(request.ChunkX,request.ChunkZ);}
        }
        /// <summary>After the deposit pass and its layer pass of each chunk column (0065: the catalogue's ore bodies). Subscribers
        /// catch their own errors.</summary>
        internal event Action<IChunkColumnGenerateRequest> AfterDepositLayers;
        void ClearGravelUnderPatches(IChunkColumnGenerateRequest request)
        {
            var chunks=request.Chunks;var map=chunks[0].MapChunk;
            int maxY=chunks.Length*32-1,cx=request.ChunkX*32,cz=request.ChunkZ*32;
            // the column's own latitude and longitude, as the ground pass reads them (this pass runs on another thread, where
            // the strata pass's column samples are not at hand, and needs nothing else of them)
            var geometry=ground!=null?core.Model.Geometry:null;
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)
            {
                if(geometry!=null && geometry.ToGeo(cx+x,cz+z) is var g && ground.Covers(g.Lat,g.Lon))continue;
                int top=Math.Min(map.WorldGenTerrainHeightMap[z*32+x]+1,maxY),patch=0;
                for(int y=top;y>0 && y>=top-8;y--)
                {
                    int index=((y%32)*32+z)*32+x;
                    var data=chunks[y/32].Data;
                    int id=data.GetBlockIdUnsafe(index);
                    if(id<=0 || id>=clayOrPeat.Length){if(id!=0)patch=0;continue;}
                    if(clayOrPeat[id])patch=id;
                    else if(patch!=0 && gravelBlock[id])data.SetBlockUnsafe(index,patch);
                    else patch=0;
                }
            }
        }

        // ------------------------------------------------------------------ public queries

        public RegionalGeologyCatalog.Sample SampleColumn(double x,double z)=>SampleAt(x,z,out _);
        /// <summary>The ground-columns model, null when ground columns are off in this world.</summary>
        internal GroundColumns GroundModel=>Active?ground?.Model:null;
        /// <summary>
        /// Water table (decision 0059): the ground column at a position built into <paramref name="col"/>, with its sample.
        /// False where ground columns do not own the ground (off, outside Earth, under glacier ice).
        /// </summary>
        internal bool GroundColumnAt(double x,double z,GroundColumns.Column col,out GroundColumns.Sample sample)
        {
            sample=default;
            if(!Active || ground==null)return false;
            var s=SampleAt(x,z,out var geo);
            if(s.OutsideEarth || !ground.Covers(geo.Lat,geo.Lon))return false;
            var tc=ColumnStore.Near(core.Model,x,z);
            if(tc.IceBottom<=tc.Top)return false;
            var surface=SurfaceOf(tc);
            sample=GroundAt(geo.Lat,geo.Lon,x,z,tc,surface,s.HasRegionalBedrock);
            ground.Model.Build(sample,surface,col);
            return true;
        }
        /// <summary>Whether the ground-columns prototype owns this column's ground (and its beach sand).</summary>
        public bool GroundCovers(double x,double z)
        {
            if(!Active || ground==null)return false;
            var g=core.Model.Geometry.ToGeo(x,z);
            return !g.InGutter && !g.BeyondPole && ground.Covers(g.Lat,g.Lon);
        }
        RegionalGeologyCatalog.Sample SampleAt(double x,double z,out EarthGeometry.GeoPos original)
            =>RegionalGeologyWorld.Sample(Catalog,core.Model.Geometry,x,z,seed,width,cellBlocks,out original);

        sealed class RockColumn
        {
            public int Epoch=-1,Top=int.MinValue,StrataTop,Beds;
            public double X=double.NaN,Z=double.NaN;
            public RegionalGeologyCatalog.Profile Profile;
            public readonly int[] Bounds=new int[RegionalGeologyCatalog.MaxBeds];
            /// <summary>Ground columns: the column's blocks by depth below the top (0 = top), when it lies in the ground grid.</summary>
            public bool Ground;
            /// <summary>Blocks from the top the ground column owns; below that, regional rock (hand-off) when Profile is set.</summary>
            public int GroundDepth;
            /// <summary>512 long (the top 512 blocks) before GenFixRules 4; from it as deep as the column (ER-20).</summary>
            public int[] GroundIds=new int[512];
            /// <summary>0060: the column's layer segments (ends, codes), the depth it owns (int.MaxValue unless it hands off),
            /// and the regional rock's age and evaporitic profile below a hand-off.</summary>
            public int Segs,Owned;
            public readonly int[] SegEnd=new int[GroundColumns.Column.Max],SegCode=new int[GroundColumns.Column.Max];
            public OreLayers.Age Age;
            public bool Evaporitic;
        }
        [ThreadStatic] static RockColumn rockColumn;

        /// <summary>
        /// 0060: the layer code at <paramref name="depth"/> below the terrain top <paramref name="top"/> (the heightmap) for a
        /// deposit about to replace <paramref name="hostBlock"/> there; <see cref="OreLayers.None"/> where ground columns do
        /// not own the column or ore layers are off. Same answer as the layer table, from the model (any column, any time).
        /// </summary>
        public int LayerCodeAt(double x,double z,int depth,int top,int hostBlock)
        {
            if(!Active || layers==null || depth<0)return OreLayers.None;
            var c=RockColumnAt(x,z,top);
            if(!c.Ground || c.Segs==0)return OreLayers.None;
            if(depth<c.Owned)
            {
                for(int i=0;i<c.Segs;i++)if(depth<c.SegEnd[i])return c.SegCode[i];
                return c.SegCode[c.Segs-1];
            }
            return layers.Rules.RegionalCode(layers.RockName(hostBlock),c.Age,c.Evaporitic);
        }

        /// <summary>
        /// The regional rock block the strata pass would place at depth <paramref name="depth"/> below the terrain top
        /// <paramref name="top"/> (the heightmap), or 0 where Earth leaves vanilla strata (no regional profile, above the
        /// rock under glacier ice, outside Earth). Matches the strata pass, which starts below glacier ice.
        /// </summary>
        public int RegionalRock(double x,double z,int depth,int top)
        {
            if(!Active)return 0;
            var c=RockColumnAt(x,z,top);
            int y=top-depth;
            if(c.Ground && depth<c.GroundDepth)return depth>=0 && y>=1 && depth<c.GroundIds.Length?c.GroundIds[depth]:0;
            if(c.Profile==null || y<1 || y>c.StrataTop)return 0;
            int bed=!GeologyFacts.SmoothedBeds(rules)?RegionalGeologyCatalog.BedV1(c.StrataTop-y,c.Bounds,c.Beds,0):RegionalGeologyCatalog.BedV2(y,c.Bounds,c.Beds,0);
            return materialBlock[RegionalGeologyCatalog.MaterialOfBed(c.Profile,bed)];
        }

        /// <summary>Per-thread cache of the last column's profile and bed boundaries (callers walk down one column at a time).</summary>
        RockColumn RockColumnAt(double x,double z,int top)
        {
            var c=rockColumn??=new RockColumn();
            int e=Volatile.Read(ref epoch);
            if(c.Epoch==e && c.X==x && c.Z==z && c.Top==top)return c;
            c.Epoch=-1;
            var s=SampleAt(x,z,out var geo);
            c.Profile=null;
            c.Ground=false;c.GroundDepth=0;c.Segs=0;
            // the terrain column, read once for both parts below (from the column store when its chunk is there; building
            // it is the expensive call)
            TerrainModel.Column tc=default;
            bool haveColumn=false;
            if(ground!=null && !s.OutsideEarth && ground.Covers(geo.Lat,geo.Lon))
            {
                tc=ColumnStore.Near(core.Model,x,z);haveColumn=true;
                if(tc.IceBottom>tc.Top)
                {
                    // ER-20 (GenFixRules 4): the whole ground column, not its top 512 blocks (a column whose top is above y 512
                    // owns everything down to y 1, as EarthGround.Write places it)
                    bool whole=core.Model.Rules.WholeGroundColumn;
                    if(whole && c.GroundIds.Length<top+1)c.GroundIds=new int[top+1];
                    Array.Clear(c.GroundIds);
                    var surface=SurfaceOf(tc);
                    var gs=GroundAt(geo.Lat,geo.Lon,x,z,tc,surface,s.HasRegionalBedrock);
                    c.GroundDepth=ground.Column(gs,surface,c.GroundIds.AsSpan(0,whole?c.GroundIds.Length:512),Math.Max(0,top),out var built);
                    c.Ground=true;
                    if(layers!=null)
                    {
                        c.Segs=built.Segments;
                        for(int i=0;i<c.Segs;i++){c.SegEnd[i]=built.SegEnd[i];c.SegCode[i]=layers.Rules.CodeOf(built,i);}
                        c.Owned=built.HandOff?built.Depth:int.MaxValue;
                        c.Age=layers.Rules.AgeOfEra(gs.Era);
                        c.Evaporitic=s.HasRegionalBedrock && layers.Rules.IsEvaporiteProfile(s.Profile?.Id);
                    }
                }
            }
            if((!c.Ground || c.GroundDepth<Math.Max(0,top)) && s.HasRegionalBedrock)
            {
                if(!haveColumn)tc=ColumnStore.Near(core.Model,x,z);
                int strataTop=Math.Min(Math.Min(top,tc.IceBottom-1),mapTop);
                if(strataTop>=1)
                {
                    c.Profile=s.Profile;c.StrataTop=strataTop;
                    if(!GeologyFacts.SmoothedBeds(rules))c.Beds=RegionalGeologyCatalog.ColumnV1(s.Profile,strataTop,geo.Lat,geo.Lon,seed,c.Bounds);
                    else
                    {
                        reference.At(x,z,out double datum,out double thickness);
                        c.Beds=RegionalGeologyCatalog.ColumnV2(s.Profile,datum,thickness,c.Bounds);
                    }
                }
            }
            c.X=x;c.Z=z;c.Top=top;c.Epoch=e;
            return c;
        }

        RegionalGeologyCatalog.Site Habitat(double x,double z,out TerrainModel.Column center)=>RegionalGeologyWorld.SiteAt(core.Model,x,z,out center);

        internal static bool BeforeDeposit(GenDeposits __instance,IServerChunk[] chunks,int chunkX,int chunkZ,BlockPos depoCenterPos,DepositVariant variant,
            IBlockAccessor ___blockAccessor,ref Dictionary<BlockPos,DepositVariant> ___subDepositsToPlace)
        {
            var self=instance;
            if(self?.Active!=true || variant==null || depoCenterPos==null)return true;
            string code=variant.Code;
            if(code is not ("peat" or "clay" or "redclay" or "halite") && !(self.layers!=null && code is "gravel" or "soil" or "bogironlarge" or "bogironsmall") && !EarthOreLayers.IsLayerDeposit(variant))return true;
            bool world=__instance==self.api.ModLoader.GetModSystem<GenDeposits>();
            try
            {
                // (FP-12) the world's deposit pass may wait for GLO-90 tiles; the prospecting pick's never does (main thread)
                if(!world)return self.FilterDeposit(chunks,chunkX,chunkZ,depoCenterPos,variant,___blockAccessor,ref ___subDepositsToPlace);
                using var fetch=FineElevation.Generating();
                return self.FilterDeposit(chunks,chunkX,chunkZ,depoCenterPos,variant,___blockAccessor,ref ___subDepositsToPlace);
            }
            catch(Exception e)
            {
                // (ER-08, Fable's re-review) only the world's own deposit pass refuses a chunk: the prospecting pick builds its own
                // GenDeposits and runs GeneratePartial on the main thread for a column it simulates and throws away, and a refusal
                // there would note the real chunk under the player for deletion
                if(world)self.ReportGenerationError("deposit filter",chunks,chunkX,chunkZ,e);
                else if(Interlocked.Increment(ref self.pickFilterErrors)==1)
                    self.Mod.Logger.Warning("Earth geology: the deposit filter failed in a prospecting reading (said once a session; the world is unchanged): {0}",e);
                return true;
            }
        }
        bool FilterDeposit(IServerChunk[] chunks,int chunkX,int chunkZ,BlockPos pos,DepositVariant variant,IBlockAccessor blockAccessor,ref Dictionary<BlockPos,DepositVariant> subDeposits)
        {
            string code=variant.Code;
            var sample=SampleAt(pos.X,pos.Z,out var geo);
            // Ground columns lay clay and peat as mapped layers; vanilla's random patches would double them.
            bool columns=ground!=null && !sample.OutsideEarth && ground.Covers(geo.Lat,geo.Lon);
            if(columns && code is "clay" or "redclay" or "peat")return false;
            // 0060: Earth's layer deposits start only where ground columns own the column
            if(EarthOreLayers.IsLayerDeposit(variant))return columns && layers!=null;
            // 0060: the columns also lay soil and alluvial gravel; Geology Additions' rocky bog iron gives way to Earth's bog
            // peat; vanilla's surface salt is off (salt flats come with the playa)
            if(columns && layers!=null && (code is "gravel" or "soil" or "bogironlarge" or "bogironsmall" || code=="halite" && variant.Generator=="disc-followsurfacebelow"))return false;
            if(code is "gravel" or "soil" or "bogironlarge" or "bogironsmall")return true;
            if(code=="halite")
            {
                // The physical setting of the centre is enough; relief is not needed.
                var c=ColumnStore.Near(core.Model,pos.X,pos.Z);
                var site=RegionalGeologyWorld.SiteAtCentre(c);
                bool surfaceBed=variant.Generator=="disc-followsurfacebelow";
                // Evidence matters only for exposed beds; buried attempts keep native behaviour either way.
                bool evidence=surfaceBed && (sample.MappedSalt || Catalog.SaltOccurrence(geo.Lat,geo.Lon)!=null);
                switch(RegionalGeologyCatalog.SaltDecision(sample,site,evidence,surfaceBed))
                {
                    case RegionalGeologyCatalog.SaltPermission.Deny:return false;
                    case RegionalGeologyCatalog.SaltPermission.Native:return true;
                }
                // Favoured: a mapped salt flat may be cold (Altiplano); skip only the native warm-desert climate gate.
                if(variant.GeneratorInst==null)return true;
                pos.Y=c.Top;
                variant.GeneratorInst.GenDeposit(blockAccessor,chunks,chunkX,chunkZ,pos,ref subDeposits);
                return false;
            }
            var habitat=Habitat(pos.X,pos.Z,out var column);
            if(code is "clay" or "redclay")
            {
                // A frequency multiplier: deterministic per centre, so every chunk sharing this deposit agrees.
                double clay=RegionalGeologyCatalog.ClayFactor(sample,habitat);
                return clay>0 && GeologyTransitions.Hash(seed,pos.X,0,pos.Z,ClaySalt)<clay;
            }
            double peat=RegionalGeologyCatalog.PeatFactor(sample,habitat);
            if(peat<=0 || GeologyTransitions.Hash(seed,pos.X,0,pos.Z,PeatSalt)>=peat)return false;
            // The vanilla 18 C maximum excludes real tropical peat. Earth has already checked wetness, temperature and
            // relief; retain the normal generator's size, substrate and roughness checks.
            if(variant.GeneratorInst==null)return false;
            pos.Y=column.Top;
            variant.GeneratorInst.GenDeposit(blockAccessor,chunks,chunkX,chunkZ,pos,ref subDeposits);
            return false;
        }

        public string Describe(double x,double z)
        {
            if(!Active)return "Regional geology is disabled in this save.";
            var s=SampleAt(x,z,out var geo);
            var site=Habitat(x,z,out var column);
            if(ground!=null && !s.OutsideEarth && ground.Covers(geo.Lat,geo.Lon))
            {
                var surface=SurfaceOf(column);
                var gs=GroundAt(geo.Lat,geo.Lon,x,z,column,surface,s.HasRegionalBedrock);
                string text=ground.Describe(gs,surface)+" (surface y="+column.Top+"; prototype, sources in docs/research/geology-columns-2026-11-06)";
                if(layers!=null)text+=". Ore layers (0060): "+layers.Rules.DescribeColumn(ground.Build(gs,surface),gs.Era);
                return text;
            }
            string strata;
            if(GeologyFacts.SmoothedBeds(rules))
            {
                reference.At(x,z,out double datum,out double thickness);
                strata=string.Format(CultureInfo.InvariantCulture,"bed boundaries hang below a smoothed reference surface at y={0:0} (thickness scale {1:0} blocks); surface y={2}",datum,thickness,column.Top);
            }
            else strata="bed depths follow each column's own surface (rules v1)";
            int top=column.Top;
            string surfaceRock=s.HasRegionalBedrock?(Catalog.Materials.FirstOrDefault(m=>rocks.TryGetValue(m.Id,out int id)&&id==RegionalRock(x,z,0,top))?.Id??"vanilla"):"vanilla strata";
            return string.Format(CultureInfo.InvariantCulture,
                "Geology v{0}: {1}; GLiM={2}, GUM={3}{12}; province={4}. Source: {5}. Top regional rock={13}; {14}. Peat factor={6:0.00}; clay: {7}; slope={15:0} m/km, wetness={16:0.00}; mapped surface salt={8}; buried salt occurrence kernel={10}; contact half-width={11} blocks. Missing salt evidence means unknown. Depths are synthesized, not borehole data. Ore behavior follows the saved RegionalOres setting. Rock proxies: {9}",
                rules,s.Profile.Label,s.Lithology,s.Sediment,s.Province??"global coarse categories",s.Source,
                RegionalGeologyCatalog.PeatFactor(s,site),RegionalGeologyCatalog.ClayInterpretation(s,site),s.MappedSalt,mappingDescription,Catalog.SaltOccurrence(geo.Lat,geo.Lon)?.Id??"none",core.Params.GeologyTransitionBlocks,
                s.Filled?" (borrowed from the nearest mapped cell)":"",surfaceRock,strata,site.Slope,site.Wetness);
        }
        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            if(instance==this)instance=null;
            base.Dispose();
        }
    }
}
