using System.Reflection;
using System.Reflection.Emit;
using EarthMap;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Feature = EarthMap.PatchVanilla.Feature;
using Spec = EarthMap.PatchVanilla.Spec;
using Bind = EarthMap.PatchVanilla.Bind;
using PatchMethod = EarthMap.PatchVanilla.PatchMethod;

// PatchVanilla against the installed game's own assemblies. Every feature of the table binds on this game version (and every
// transpiler finds the code it rewrites); a target a game update renamed or changed logs one line naming the game version,
// the member and the feature, and turns only that feature off, with nothing of it left applied.
string vs = typeof(Checks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    string file = new AssemblyName(e.Name).Name + ".dll";
    foreach (string dir in new[] { vs, Path.Combine(vs, "Lib"), Path.Combine(vs, "Mods") })
        if (File.Exists(Path.Combine(dir, file))) return Assembly.LoadFrom(Path.Combine(dir, file));
    return null;
};
// the runtime spawner is in the server's own assembly, which the game has loaded when Earth binds
Assembly.LoadFrom(Path.Combine(vs, "VintagestoryLib.dll"));
// WorldEdit's client handler (the creative panel's date rows) is in the creative mod's assembly, loaded when Earth binds
Assembly.LoadFrom(Path.Combine(vs, "Mods", "VSCreativeMod.dll"));
Checks.Run();

static partial class Checks
{
    const string Game = "1.22.7-test";
    static int checks, run;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("FAILED: " + message); }
    /// <summary>World generation's nine tables and the runtime ones.</summary>
    static int Tables => 9 + RuntimeTables.Length;

    static (PatchVanilla Pv, Harmony Harmony, List<string> Lines) Bind(IEnumerable<Spec> table)
    {
        var harmony = new Harmony("earth.patchvanillatest." + (++run));
        var lines = new List<string>();
        var pv = new PatchVanilla(harmony, "test", Game, lines.Add, lines.Add);
        pv.Apply(table);
        return (pv, harmony, lines);
    }

    // a terrain pass handler list as vanilla's mod systems leave it: GenTerra's handler, another, the block layers'
    static FakeHandlers handlers;
    static int terraCalls, layersCalls;
    static (int X, int Z, bool Known) seenInLayers;
    static void FakeTerra(GenTerra self, IChunkColumnGenerateRequest request) => terraCalls++;
    static void FakeOther(object self, IChunkColumnGenerateRequest request) { }
    static void FakeLayers(GenBlockLayers self, IChunkColumnGenerateRequest request)
    {
        layersCalls++;
        seenInLayers = BlockLayerAdjustments.LayersChunk;
        if (request.ChunkX < 0) throw new InvalidOperationException("vanilla throws");
    }
    static ChunkColumnGenerationDelegate Handler(object target, string method) => (ChunkColumnGenerationDelegate)Delegate.CreateDelegate(typeof(ChunkColumnGenerationDelegate), target,
        typeof(Checks).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static));
    static FakeHandlers NewHandlers(bool withTerra = true, bool withLayers = true)
    {
        var h = new FakeHandlers();
        var terrain = h.OnChunkColumnGen[(int)EnumWorldGenPass.Terrain];
        if (withTerra) terrain.Add(Handler(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GenTerra)), nameof(FakeTerra)));
        terrain.Add(Handler(new object(), nameof(FakeOther)));
        if (withLayers) terrain.Add(Handler(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GenBlockLayers)), nameof(FakeLayers)));
        return h;
    }

    static Spec[] With(Feature feature, System.Func<Spec, Spec> change) => PatchVanilla.Table(handlers = NewHandlers()).Select(s => s.Feature == feature ? change(s) : s).ToArray();

    static Spec Like(Spec s, System.Func<Bind[]> binds) => new Spec(s.Feature, s.What, s.Without, binds)
        { Needs = s.Needs, Before = s.Before, Rewrote = s.Rewrote, Bound = s.Bound, Unbound = s.Unbound, Critical = s.Critical, Instead = s.Instead };

    static int Earth(MethodBase original, string owner)
    {
        var info = Harmony.GetPatchInfo(original);
        if (info == null) return 0;
        return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Count(p => p.owner == owner);
    }

    /// <summary>The main table's features (the geology and ore tables are bound by their own systems).</summary>
    static readonly Feature[] Main = PatchVanilla.Table(null).Select(s => s.Feature).ToArray();

    static void OthersOn(PatchVanilla pv, params Feature[] off)
    {
        foreach (Feature f in Main)
            Check(pv.On(f) != off.Contains(f), f + (off.Contains(f) ? " is off" : " stays on"));
    }

    static readonly MethodInfo Pond = AccessTools.Method(typeof(GenPonds), "TryPlacePondAt");

    static void StrataWithRenamedParameter(IChunkColumnGenerateRequest chunkRequest) { }
    internal static void NamedStrataPostfix(IChunkColumnGenerateRequest request) { }

    internal static IEnumerable<CodeInstruction> Throws(IEnumerable<CodeInstruction> instructions) => throw new InvalidOperationException("a transpiler that fails");

    public static void Run()
    {
        // every feature binds on this game version, without a line
        handlers = NewHandlers();
        var terrainList = handlers.OnChunkColumnGen[(int)EnumWorldGenPass.Terrain];
        var vanilla = terrainList.ToArray();
        var all = Bind(PatchVanilla.Table(handlers));
        Check(all.Lines.Count == 0, "no line on this game version: " + string.Join(" | ", all.Lines));
        OthersOn(all.Pv);
        Check(BlockLayerAdjustments.AridFillPatched && BlockLayerAdjustments.SoilFertilityPatched, "both block-layer transpilers found their code");
        Check(EarthTrees.Patched && EarthTrees.PatchError == null, "the tree pick is bound");
        Check(SpringsAndPonds.RivuletRandom != null && StructurePlacementGuards.SchematicsRef != null && EarthTrees.positionRef != null, "the non-public fields are bound");
        Check(Earth(Pond, all.Harmony.Id) == 1, "the pond guard is patched");
        // vanilla's terrain and block layer handlers replaced in place (the other one and the order untouched); while Earth is
        // inactive the terrain wrapper calls vanilla's; the block layers run inside their chunk's tall grass scope, which is
        // put back after them, also when they throw
        Check(terrainList.Count == 3 && terrainList[0] != vanilla[0] && terrainList[1] == vanilla[1] && terrainList[2] != vanilla[2], "two handlers replaced in place");
        Check(EarthWorldGen.Instance == null, "no Earth world in this process");
        terrainList[0](new Request(3, 4));
        Check(terraCalls == 1, "vanilla's terrain runs where Earth is inactive");
        terrainList[2](new Request(5, 7));
        Check(layersCalls == 1 && seenInLayers == (5, 7, true) && BlockLayerAdjustments.LayersChunk == (0, 0, false), "the block layers run in their chunk's scope, put back after");
        try { terrainList[2](new Request(-5, 7)); Check(false, "vanilla's exception passes"); } catch (InvalidOperationException) { }
        Check(seenInLayers == (-5, 7, true) && BlockLayerAdjustments.LayersChunk == (0, 0, false), "the scope is put back when vanilla throws");
        all.Pv.Release();
        Check(terrainList.SequenceEqual(vanilla), "vanilla's handlers are put back");
        all.Harmony.UnpatchAll(all.Harmony.Id);

        // regional geology's and the ore layer's tables bind on this game version too (private field injection, argument
        // positions, Harmony's "after" for the ore strata hook)
        var geology = Bind(PatchVanilla.GeologyTable());
        var ores = Bind(PatchVanilla.OreTable());
        Check(geology.Lines.Count == 0 && ores.Lines.Count == 0, "no line for geology and ores: " + string.Join(" | ", geology.Lines.Concat(ores.Lines)));
        foreach (var s in PatchVanilla.GeologyTable()) Check(geology.Pv.On(s.Feature), s.Feature + " is on");
        foreach (var s in PatchVanilla.OreTable()) Check(ores.Pv.On(s.Feature), s.Feature + " is on");
        var strata = AccessTools.Method(typeof(GenRockStrataNew), "GenChunkColumn");
        Check(Harmony.GetPatchInfo(strata).Postfixes.Single(p => p.owner == ores.Harmony.Id).after.Contains("earth.regionalgeology"), "the ore strata hook runs after regional geology's");
        Check(OreVanilla.HostTables && OreVanilla.IndicatorTables, "the deposit generators' host and indicator tables can be read");
        geology.Harmony.UnpatchAll(geology.Harmony.Id);
        ores.Harmony.UnpatchAll(ores.Harmony.Id);
        // the ore layer's dependent parts: without the ore map factor neither the reload nor the strata hook is bound (one line);
        // without the reload hook a re-made deposit list turns the session off; rules v4 need geology's deposit-column hook
        var noFactor = Bind(PatchVanilla.OreTable().Select(s => s.Feature != Feature.OreMapFactor ? s : Like(s, () => new Bind[]
            { new PatchMethod(typeof(DepositVariant), "GetOreMapFactorRenamed", new[] { typeof(int), typeof(int) }, postfix: (typeof(EarthOres), nameof(EarthOres.FactorPostfix))) })).ToArray());
        Check(noFactor.Lines.Count == 1 && !noFactor.Pv.On(Feature.OreMapFactor) && !noFactor.Pv.On(Feature.OreReload) && !noFactor.Pv.On(Feature.OreStrata),
            "no ore map factor: the ore layer's hooks are all off, one line");
        noFactor.Harmony.UnpatchAll(noFactor.Harmony.Id);
        var listA = new DepositVariant[0]; var listB = new DepositVariant[0];
        Check(EarthOres.ReloadedWithoutHook(false, listA, listB) && !EarthOres.ReloadedWithoutHook(true, listA, listB)
            && !EarthOres.ReloadedWithoutHook(false, listA, listA) && !EarthOres.ReloadedWithoutHook(false, null, listB),
            "a re-made deposit list without the reload hook ends the session; with the hook, or the same list, it stays");
        Check(EarthOres.OreRulesNeedDepositColumns(4) && !EarthOres.OreRulesNeedDepositColumns(3) && !EarthOres.OreRulesNeedDepositColumns(0),
            "rules v4 (the deposit catalogue) need the deposit-column hook, earlier rules don't");
        var noColumns = Bind(PatchVanilla.GeologyTable().Select(s => s.Feature != Feature.GeologyDepositColumns ? s : Like(s, () => new Bind[]
            { new PatchMethod(typeof(GenDeposits), "GenChunkColumnRenamed", declared: true, postfix: (typeof(EarthGeology), nameof(EarthGeology.AfterDeposits))) })).ToArray());
        Check(!noColumns.Pv.On(Feature.GeologyDepositColumns) && noColumns.Lines.Single().Contains("GenDeposits.GenChunkColumnRenamed not found")
            && noColumns.Pv.On(Feature.GeologyDeposits) && noColumns.Pv.On(Feature.GeologyStrata), "a missing deposit-column hook is named, the other geology hooks stay");
        noColumns.Harmony.UnpatchAll(noColumns.Harmony.Id);

        // a private field a patch injects is gone: named
        var injected = Bind(new[] { Like(PatchVanilla.GeologyTable().Single(s => s.Feature == Feature.GeologyDeposits), () => new Bind[]
            { new PatchMethod(typeof(GenPonds), "TryPlacePondAt", prefix: (typeof(EarthGeology), nameof(EarthGeology.BeforeDeposit))) }) });
        Console.WriteLine("  injected field: " + injected.Lines.Single());
        Check(!injected.Pv.On(Feature.GeologyDeposits) && injected.Lines.Single().Contains("GenPonds.TryPlacePondAt changed"), "a missing injected field or parameter is found before patching");
        injected.Harmony.UnpatchAll(injected.Harmony.Id);

        // a parameter a game update renamed: a patch reading it by position (as every patch on a non-public target does) still
        // binds; one reading it by its old name is found before patching, and only its feature is off
        var positional = Bind(new[] { new Spec(Feature.OreStrata, "the test strata hook", "none", () => new Bind[]
            { new PatchMethod(typeof(Checks), nameof(StrataWithRenamedParameter), postfix: (typeof(EarthOres), nameof(EarthOres.AfterStrata))) }) });
        Check(positional.Lines.Count == 0 && positional.Pv.On(Feature.OreStrata), "a patch reading by position binds to a renamed parameter");
        positional.Harmony.UnpatchAll(positional.Harmony.Id);
        var named = Bind(new[] { new Spec(Feature.OreStrata, "the test strata hook", "none", () => new Bind[]
            { new PatchMethod(typeof(Checks), nameof(StrataWithRenamedParameter), postfix: (typeof(Checks), nameof(NamedStrataPostfix))) }) });
        Console.WriteLine("  renamed parameter: " + named.Lines.Single());
        Check(!named.Pv.On(Feature.OreStrata) && named.Lines.Single().Contains("no parameter 'request'"), "a patch reading a renamed parameter by name is off, found before patching");
        named.Harmony.UnpatchAll(named.Harmony.Id);

        // Earth's terrain must stay in place until the world generates (EarthWorldGen refuses an Earth world otherwise):
        // bound through the handler list it is; vanilla's own handler put back afterwards is caught
        handlers = NewHandlers();
        var held = Bind(PatchVanilla.Table(handlers));
        Check(held.Pv.StillInPlace(Feature.Terrain) == null && held.Pv.Unverified(Feature.Terrain) == null, "Earth's terrain is in place after binding");
        var heldList = handlers.OnChunkColumnGen[(int)EnumWorldGenPass.Terrain];
        var earthEntry = heldList[0];
        var genTerra = Handler(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GenTerra)), nameof(FakeTerra));
        heldList[0] = genTerra;
        string replacedCause = held.Pv.StillInPlace(Feature.Terrain);
        Console.WriteLine("  vanilla's terrain put back later: " + replacedCause);
        Check(replacedCause?.Contains("vanilla's own GenTerra's Terrain pass handler is back") == true, "vanilla's terrain handler put back after Earth is refused");
        // Codex round 3 item 3: a mod wrapping Earth's handler after Earth (request => earthHandler(request)) still runs
        // Earth's writer: not refused here, only noted, the per-chunk check decides
        heldList[0] = request => earthEntry(request);
        terraCalls = 0;
        heldList[0](new Request(1, 1));
        Check(terraCalls == 1, "the wrapper reaches Earth's handler (which, with no Earth world here, calls vanilla's)");
        Check(held.Pv.StillInPlace(Feature.Terrain) == null && held.Pv.Unverified(Feature.Terrain)?.Contains("wrapped or replaced it after Earth") == true,
            "a later wrapper around Earth's terrain handler is not refused, only noted (" + held.Pv.Unverified(Feature.Terrain) + ")");
        // a mod replacing Earth's entry with a handler of its own: the same note (it may call Earth's; the chunks tell)
        heldList[0] = Handler(new object(), nameof(FakeOther));
        Check(held.Pv.StillInPlace(Feature.Terrain) == null && held.Pv.Unverified(Feature.Terrain) != null, "a later replacement of Earth's terrain handler is left to the per-chunk check");
        heldList[0] = earthEntry;
        Check(held.Pv.StillInPlace(Feature.Terrain) == null && held.Pv.Unverified(Feature.Terrain) == null, "Earth's handler back in place: nothing to note");
        held.Pv.Release();
        held.Harmony.UnpatchAll(held.Harmony.Id);

        // another mod wrapped GenTerra's handler before Earth: Earth's terrain runs as a prefix on GenTerra's own method (a
        // warning naming the second way), still in place until that prefix is removed
        handlers = NewHandlers(withTerra: false);
        var noTerra = Bind(PatchVanilla.Table(handlers));
        Console.WriteLine("  no terrain handler: " + noTerra.Lines.Single());
        Check(noTerra.Pv.On(Feature.Terrain) && noTerra.Lines.Single().Contains("GenTerra's Terrain pass handler not found")
            && noTerra.Lines.Single().Contains("it runs as a Harmony prefix on GenTerra's own method instead"), "the terrain's second way binds, and says so");
        Check(noTerra.Pv.StillInPlace(Feature.Terrain) == null, "the second way is in place");
        OthersOn(noTerra.Pv);
        noTerra.Harmony.Unpatch(AccessTools.Method(typeof(GenTerra), "OnChunkColumnGen"), HarmonyPatchType.Prefix, noTerra.Harmony.Id);
        Check(noTerra.Pv.StillInPlace(Feature.Terrain)?.Contains("was removed") == true, "a removed terrain prefix is caught");
        noTerra.Pv.Release();
        noTerra.Harmony.UnpatchAll(noTerra.Harmony.Id);

        // another mod wrapped GenBlockLayers' handler before Earth: the tall grass scope is a prefix and finalizer on its method
        var noLayers = Bind(PatchVanilla.Table(NewHandlers(withLayers: false)));
        Console.WriteLine("  no block layer handler: " + noLayers.Lines.Single());
        Check(noLayers.Pv.On(Feature.TallGrass) && noLayers.Pv.On(Feature.TallGrassAroundStructures) && noLayers.Lines.Single().Contains("Harmony prefix on GenBlockLayers' own method instead"),
            "the tall grass scope's second way binds");
        var layersMethod = AccessTools.Method(typeof(GenBlockLayers), "OnChunkColumnGeneration");
        var layersInfo = Harmony.GetPatchInfo(layersMethod);
        Check(layersInfo.Prefixes.Any(x => x.owner == noLayers.Harmony.Id && x.PatchMethod.Name == nameof(BlockLayerAdjustments.BlockLayersScopePrefix))
            && layersInfo.Finalizers.Any(x => x.owner == noLayers.Harmony.Id), "the scope prefix and finalizer are on vanilla's block layers");
        noLayers.Pv.Release();
        noLayers.Harmony.UnpatchAll(noLayers.Harmony.Id);

        // neither way: Earth's terrain is off with an error line, and the world would be refused
        handlers = NewHandlers(withTerra: false);
        var neither = Bind(PatchVanilla.Table(handlers).Select(s => { if (s.Feature != Feature.Terrain) return s; var c = Like(s, s.Binds); c.Instead = Like(s.Instead, () => new Bind[]
            { new PatchMethod(typeof(GenTerra), "OnChunkColumnGenRenamed", prefix: (typeof(EarthWorldGen), nameof(EarthWorldGen.GenTerraPrefix))) }); return c; }).ToArray());
        Console.WriteLine("  no terrain at all: " + neither.Lines.Single());
        Check(!neither.Pv.On(Feature.Terrain) && neither.Lines.Single().Contains("this server refuses Earth worlds") && neither.Pv.StillInPlace(Feature.Terrain) != null,
            "without either way Earth's terrain is off, and the reason is kept for the refusal");
        OthersOn(neither.Pv, Feature.Terrain);
        neither.Pv.Release();
        neither.Harmony.UnpatchAll(neither.Harmony.Id);
        Check(Earth(Pond, all.Harmony.Id) == 0, "unpatched again");

        // a renamed target: one line, that feature off, every other one on
        var renamed = Bind(With(Feature.Ponds, s => Like(s, () => new Bind[]
            { new PatchMethod(typeof(GenPonds), "TryPlacePondAtRenamed", prefix: (typeof(SpringsAndPonds), nameof(SpringsAndPonds.PondGuard))) })));
        Check(renamed.Lines.Count == 1, "one line for a renamed target");
        string line = renamed.Lines[0];
        Console.WriteLine("  renamed target: " + line);
        Check(line.Contains("Vintage Story " + Game) && line.Contains("GenPonds.TryPlacePondAtRenamed not found") && line.Contains("the pond guard is off"),
            "the line names the game version, the member and the feature");
        OthersOn(renamed.Pv, Feature.Ponds);
        Check(Earth(Pond, renamed.Harmony.Id) == 0, "nothing of the feature applied");
        renamed.Harmony.UnpatchAll(renamed.Harmony.Id);

        // a changed signature (a parameter the patch reads is gone): found before patching
        var changed = Bind(With(Feature.Springs, s => Like(s, () => new Bind[]
        {
            new PatchMethod(typeof(GenRivulets), "tryGenRivulet", prefix: (typeof(SpringsAndPonds), nameof(SpringsAndPonds.SpringGuard))),
            new PatchMethod(typeof(GenRivulets), "tryGenMountainSideRivers", prefix: (typeof(SpringsAndPonds), nameof(SpringsAndPonds.SpringGuard))),
        })));
        Console.WriteLine("  changed signature: " + changed.Lines.Single());
        Check(changed.Lines.Single().Contains("GenRivulets.tryGenMountainSideRivers changed (argument 3 is a Int32, not a Single)"), "a changed signature is named (the spring guard reads its arguments by position: the activity line, a float, at 3)");
        OthersOn(changed.Pv, Feature.Springs);
        Check(Earth(AccessTools.Method(typeof(GenRivulets), "tryGenRivulet"), changed.Harmony.Id) == 0, "the springs' first patch was not applied either");
        changed.Harmony.UnpatchAll(changed.Harmony.Id);

        // a failure while patching takes back what the feature had applied
        var failing = Bind(With(Feature.Ponds, s => Like(s, () => new Bind[]
        {
            new PatchMethod(typeof(GenPonds), "TryPlacePondAt", prefix: (typeof(SpringsAndPonds), nameof(SpringsAndPonds.PondGuard))),
            new PatchMethod(typeof(GenRivulets), "tryGenMountainSideRivers", transpiler: (typeof(Checks), nameof(Throws))),
        })));
        Console.WriteLine("  failing patch: " + failing.Lines.Single());
        Check(failing.Lines.Single().Contains("could not be patched") && !failing.Pv.On(Feature.Ponds), "a failed patch turns its feature off");
        Check(Earth(Pond, failing.Harmony.Id) == 0, "the feature's earlier patch was taken back");
        OthersOn(failing.Pv, Feature.Ponds);
        failing.Harmony.UnpatchAll(failing.Harmony.Id);

        // a transpiler that does not find its code: off, and taken back
        var nothing = Bind(With(Feature.AridBasins, s => Like(s, () => new Bind[]
            { new PatchMethod(typeof(GenPonds), "TryPlacePondAt", transpiler: (typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.AridFillTranspiler))) })));
        Console.WriteLine("  transpiler without its code: " + nothing.Lines.Single());
        Check(nothing.Lines.Single().Contains("the code Earth rewrites is not there") && !nothing.Pv.On(Feature.AridBasins), "a transpiler finding nothing turns its feature off");
        Check(Earth(Pond, nothing.Harmony.Id) == 1, "only the pond guard's own patch is on that method");
        OthersOn(nothing.Pv, Feature.AridBasins);
        nothing.Harmony.UnpatchAll(nothing.Harmony.Id);

        // a game type that is gone: only the feature naming it is off
        var gone = Bind(With(Feature.CoralSalinity, s => Like(s, () => throw new TypeLoadException("Vintagestory.GameContent.BlockCoral"))));
        Console.WriteLine("  gone type: " + gone.Lines.Single());
        Check(gone.Lines.Single().Contains("a game type it names is gone") && gone.Lines.Single().Contains("the coral salinity guard is off"), "a gone type is named");
        OthersOn(gone.Pv, Feature.CoralSalinity);
        gone.Harmony.UnpatchAll(gone.Harmony.Id);

        // a feature that needs another: off with it, one line only
        var needs = Bind(With(Feature.TallGrass, s => { var c = Like(s, () => new Bind[]
            { new PatchMethod(typeof(GenBlockLayers), "PlaceTallGrassRenamed", prefix: (typeof(BlockLayerAdjustments), nameof(BlockLayerAdjustments.TallGrassCoverPrefix))) }); c.Instead = null; return c; }));
        Check(needs.Lines.Count == 1, "one line for a feature and the one that needs it");
        OthersOn(needs.Pv, Feature.TallGrass, Feature.TallGrassAroundStructures);
        needs.Harmony.UnpatchAll(needs.Harmony.Id);

        // the deferred underground guard runs up front when WouldOverlapAt changed
        var underground = AccessTools.Method(typeof(WorldGenStructure), "TryGenerateUnderground");
        var instead = Bind(With(Feature.UndergroundStructures, s =>
        {
            var c = Like(s, () => new Bind[] { new PatchMethod(typeof(WorldGenStructure), "WouldOverlapAtRenamed",
                prefix: (typeof(StructurePlacementGuards), nameof(StructurePlacementGuards.DeferredUndergroundGuard))) });
            c.Instead = s.Instead;
            return c;
        }));
        Console.WriteLine("  second way: " + instead.Lines.Single());
        Check(instead.Pv.On(Feature.UndergroundStructures) && instead.Lines.Single().Contains("it runs before vanilla's own checks instead"), "the up-front guard stands in");
        Check(Harmony.GetPatchInfo(underground).Prefixes.Single(p => p.owner == instead.Harmony.Id).PatchMethod.Name == nameof(StructurePlacementGuards.UndergroundStructureGuard),
            "the up-front guard is the one patched");
        OthersOn(instead.Pv);
        instead.Harmony.UnpatchAll(instead.Harmony.Id);

        // the rest of world generation's tables bind on this game version too, without a line
        StoryPlacer.SchematicRef = null;
        var story = Bind(PatchVanilla.Table(NewHandlers()));
        Check(story.Pv.On(Feature.StorySchematics) && StoryPlacer.SchematicRef != null, "the story locations' schematic field is bound");
        story.Pv.Release();
        story.Harmony.UnpatchAll(story.Harmony.Id);
        var tables = new (string Name, System.Func<Spec[]> Table)[] { ("plants", PatchVanilla.PlantsTable), ("placement", PatchVanilla.PlacementTable),
            ("sea-level deposits", PatchVanilla.SealevelTable), ("lapse", PatchVanilla.ClimateLapseTable), ("fauna", PatchVanilla.FaunaTable), ("dungeons", PatchVanilla.DungeonTable) };
        foreach (var (name, table) in tables)
        {
            var bound = Bind(table());
            Check(bound.Lines.Count == 0, "no line for the " + name + " table: " + string.Join(" | ", bound.Lines));
            foreach (var s in table()) Check(bound.Pv.On(s.Feature), s.Feature + " is on");
            bound.Harmony.UnpatchAll(bound.Harmony.Id);
        }
        Check(EarthPlants.TreeCallSites > 0, "the forest floor's suitability calls are found");
        Check(SealevelDeposits.Hooked, "the sea-level deposits' levels are recorded");
        Check(EarthFaunaSystem.RuntimeClimateMethod?.Name == "GetSuitableClimateTemperatureRainfall" && EarthFaunaSystem.RuntimeTmpPos?.FieldType == typeof(Vintagestory.API.MathTools.BlockPos),
            "the fauna probe gets the runtime climate helper and its position field");
        Check(EarthMap.DungeonMapSize.GeneratorRef != null && EarthMap.DungeonMapSize.SizeRef != null, "the tiled dungeons' generator and map size are bound");

        // the story locations' schematic field renamed: one line, only the story locations' sites and heights off (StoryPlacer
        // keeps vanilla's sites and heights without it, and says so per location)
        StoryPlacer.SchematicRef = null;
        var noStory = Bind(With(Feature.StorySchematics, s => Like(s, () => new Bind[]
            { new PatchVanilla.ReadField<WorldGenStoryStructure, BlockSchematicPartial>("schematicDataRenamed", r => StoryPlacer.SchematicRef = r) })));
        Console.WriteLine("  story schematic: " + noStory.Lines.Single());
        Check(noStory.Lines.Single().Contains("WorldGenStoryStructure.schematicDataRenamed not found") && noStory.Lines.Single().Contains("the story locations' placement is off")
            && StoryPlacer.SchematicRef == null, "a missing story schematic field is named, its reader left without it");
        OthersOn(noStory.Pv, Feature.StorySchematics);
        noStory.Pv.Release();
        noStory.Harmony.UnpatchAll(noStory.Harmony.Id);

        // the forest floor's private method changed: only the plants under and on trees are off, the block patch gates stay
        var noFloor = Bind(PatchVanilla.PlantsTable().Select(s => s.Feature != Feature.RegionalPlantsOnTrees ? s : Like(s, () => new Bind[]
            { new PatchMethod(typeof(ForestFloorSystem), "GenPatchesRenamed", prefix: (typeof(EarthPlants), nameof(EarthPlants.ForestFloorPrefix))) })).ToArray());
        Check(noFloor.Pv.On(Feature.RegionalPlants) && !noFloor.Pv.On(Feature.RegionalPlantsOnTrees) && noFloor.Lines.Single().Contains("ForestFloorSystem.GenPatchesRenamed not found"),
            "a changed forest floor turns off only the plants on trees");
        noFloor.Harmony.UnpatchAll(noFloor.Harmony.Id);

        // the runtime spawner's position field gone: regional fauna off as a whole, nothing of it patched
        var canSpawn = AccessTools.Method(typeof(GenCreatures), "CanSpawnAtPosition");
        var noTmp = Bind(PatchVanilla.FaunaTable().Select(s => Like(s, () => s.Binds().Select(b => b is PatchVanilla.FieldByName
            ? new PatchVanilla.FieldByName(() => AccessTools.TypeByName("Vintagestory.Server.ServerSystemEntitySpawner"), "ServerSystemEntitySpawner", "tmpPosRenamed", typeof(Vintagestory.API.MathTools.BlockPos))
            : b).ToArray())).ToArray());
        Check(!noTmp.Pv.On(Feature.RegionalFauna) && noTmp.Lines.Single().Contains("ServerSystemEntitySpawner.tmpPosRenamed not found") && Earth(canSpawn, noTmp.Harmony.Id) == 0,
            "a missing spawner field turns regional fauna off before anything is patched");
        noTmp.Harmony.UnpatchAll(noTmp.Harmony.Id);

        // a fruit tree climate list of another type: 1318 placement off, as an error
        var noConds = Bind(PatchVanilla.PlacementTable().Select(s => Like(s, () => s.Binds().Select(b => b is PatchVanilla.FieldByName
            ? new PatchVanilla.FieldByName(() => typeof(Vintagestory.GameContent.BlockFruitTreeBranch), "BlockFruitTreeBranch", "WorldGenConds", typeof(object[]))
            : b).ToArray())).ToArray());
        Check(!noConds.Pv.On(Feature.Placement1318) && noConds.Lines.Single().Contains("BlockFruitTreeBranch.WorldGenConds changed"), "a changed fruit tree climate list turns 1318 placement off");
        noConds.Harmony.UnpatchAll(noConds.Harmony.Id);

        // the running game's and the client's tables (RuntimeChecks.cs)
        RunRuntime();

        Console.WriteLine($"PatchVanilla checks: {checks} passed ({Enum.GetValues<Feature>().Length} features in {Tables} tables).");
    }
}

sealed class FakeHandlers : IWorldGenHandler
{
    public List<MapRegionGeneratorDelegate> OnMapRegionGen { get; } = new();
    public List<MapChunkGeneratorDelegate> OnMapChunkGen { get; } = new();
    public List<ChunkColumnGenerationDelegate>[] OnChunkColumnGen { get; } = Enumerable.Range(0, 6).Select(_ => new List<ChunkColumnGenerationDelegate>()).ToArray();
    public void WipeAllHandlers() { }
}

sealed class Request : IChunkColumnGenerateRequest
{
    public Request(int x, int z) { ChunkX = x; ChunkZ = z; }
    public IServerChunk[] Chunks => null;
    public int ChunkX { get; }
    public int ChunkZ { get; }
    public ITreeAttribute ChunkGenParams => null;
    public ushort[][] NeighbourTerrainHeight => null;
    public bool RequiresChunkBorderSmoothing => false;
}
