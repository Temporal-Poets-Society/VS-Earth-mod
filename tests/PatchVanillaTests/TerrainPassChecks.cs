using System.Reflection;
using EarthMap;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Kind = EarthMap.TerrainPassReport.Kind;

// EA-0162: a chunk that reaches Earth's surface pass without Earth's terrain. The terrain writer's handoff belongs to the chunk
// column being made, not to its coordinates; the refusal names the mod whose handler or generation keeps Earth's terrain out.
static partial class Checks
{
    static void RunTerrainPass()
    {
        Handoff();
        Report();
    }

    /// <summary>The handoff, keyed by the column's own chunk object (any object stands for it here).</summary>
    static void Handoff()
    {
        var h = new TerrainHandoff();
        object game = new object(), preview = new object();
        byte[] gameFlags = { 1 }, previewFlags = { 2 };
        uint[] ramps = { 7 };
        // the game's column and another mod's chunks at the same coordinates, their passes interleaved: before, the second
        // writer overwrote the first's entry, the preview's surface pass took it, and the game's chunk found none (a refusal)
        h.Put(game, 5, 6, gameFlags, ramps);
        h.Put(preview, 5, 6, previewFlags, null);
        Check(ReferenceEquals(h.TakeFlags(preview, 5, 6), previewFlags), "another generation at the same coordinates takes its own flags");
        Check(h.Untaken(game, 5, 6) && !h.Untaken(preview, 5, 6), "the game's entry is still there");
        Check(ReferenceEquals(h.TakeFlags(game, 5, 6), gameFlags), "the game's column takes its own flags");
        Check(h.TakeFlags(game, 5, 6) == null, "flags are taken once");
        Check(h.TakeRamps(game, 5, 6, out var r) && ReferenceEquals(r, ramps) && !h.TakeRamps(game, 5, 6, out _), "ramps are taken once, after the flags");
        Check(!h.TakeRamps(preview, 5, 6, out _), "no ramps where none were put");
        // "nothing flagged" is an entry, "not made by the writer" is none
        h.Put(game, 1, 1, null, null);
        Check(h.TakeFlags(game, 1, 1)?.Length == 0, "a column with nothing flagged hands over NoFlags");
        Check(h.TakeFlags(new object(), 1, 1) == null, "a column the writer did not make has no entry");
        // a chunk object reused at other coordinates (a mod's pool) does not hand on an old entry
        h.Put(preview, 3, 3, previewFlags, null);
        Check(h.TakeFlags(preview, 4, 3) == null && h.TakeFlags(preview, 3, 3) == null, "an entry from other coordinates is dropped, not handed on");
        // a cut-short terrain pass (FP-13): the flags stay untaken until the next pass drops them
        h.Put(game, 8, 8, gameFlags, ramps);
        Check(h.Untaken(game, 8, 8), "a cut-short pass leaves its entry untaken");
        h.Drop(game);
        Check(!h.Untaken(game, 8, 8) && h.TakeFlags(game, 8, 8) == null, "dropped");
        // without chunks: nothing kept, nothing found
        h.Put((object)null, 0, 0, gameFlags, null);
        Check(h.TakeFlags((IServerChunk[])null, 0, 0) == null && h.TakeFlags(System.Array.Empty<IServerChunk>(), 0, 0) == null, "no chunks, no entry");
    }

    static void OtherModTerrain(IChunkColumnGenerateRequest request) { }

    /// <summary>Another mod's wrapper around a handler (it calls it).</summary>
    static ChunkColumnGenerationDelegate WrapHandler(ChunkColumnGenerationDelegate inner) => request => inner(request);

    /// <summary>The refusal's names and causes.</summary>
    static void Report()
    {
        string ModOf(Assembly a) => a == typeof(Checks).Assembly ? "Test Mod (testmod 1.0.0)" : "assembly " + a.GetName().Name;
        var genTerra = Handler(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GenTerra)), nameof(FakeTerra));
        // vanilla's block layers as the game registers them (never called here)
        var layers = (ChunkColumnGenerationDelegate)Delegate.CreateDelegate(typeof(ChunkColumnGenerationDelegate),
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GenBlockLayers)),
            typeof(GenBlockLayers).GetMethod("OnChunkColumnGeneration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(IChunkColumnGenerateRequest) }, null));
        ChunkColumnGenerationDelegate other = OtherModTerrain;
        int calls = 0;
        ChunkColumnGenerationDelegate lambda = _ => calls++;
        ChunkColumnGenerationDelegate earthTerrain = EarthWorldGen.InPlaceOfGenTerra(genTerra);

        var read = TerrainPassReport.Read(new[] { earthTerrain, layers, other, lambda }, earthTerrain, ModOf);
        Console.WriteLine("  terrain pass: " + TerrainPassReport.Listing(read, false, false));
        Check(read[0].Kind == Kind.EarthTerrain && read[1].Kind == Kind.Vanilla && read[2].Kind == Kind.Other && read[3].Kind == Kind.Other,
            "Earth's terrain, vanilla's handler and another mod's are told apart");
        Check(read[1].Name == "Vintagestory.ServerMods.GenBlockLayers.OnChunkColumnGeneration" && read[2].Name == "Checks.OtherModTerrain" && read[2].Mod == "Test Mod (testmod 1.0.0)",
            "a handler is named by its type, method and mod (" + read[2] + ")");
        Check(read[3].Name == "Checks.Report (a lambda)", "a lambda is named by the method it was written in (" + read[3].Name + ")");
        Check(read[0].Name == "EarthMap.EarthWorldGen.InPlaceOfGenTerra (a lambda) around Checks.FakeTerra", "Earth's terrain handler is named with what it wraps (" + read[0].Name + ")");
        // another mod's wrapper around Earth's terrain handler names both
        var wrapped = TerrainPassReport.Read(new[] { WrapHandler(earthTerrain) }, earthTerrain, ModOf)[0];
        Check(wrapped.Kind == Kind.Other && wrapped.Name == "Checks.WrapHandler (a lambda) around EarthMap.EarthWorldGen.InPlaceOfGenTerra (a lambda) around Checks.FakeTerra",
            "a wrapper is named with the handler it wraps (" + wrapped.Name + ")");
        Check(TerrainPassReport.Read(new[] { earthTerrain }, null, ModOf)[0].Kind == Kind.Earth, "Earth's code that isn't its terrain handler is Earth's");

        // the player's case (EA-0162): vanilla's handler replaced before Earth started; Earth's terrain runs inside vanilla's
        var before = TerrainPassReport.Read(new[] { other, layers }, null, ModOf);
        var (cause, advice) = TerrainPassReport.Cause(6765, 3125, before, viaGenTerra: true, genTerraInPass: false, null, null);
        Console.WriteLine("  replaced before Earth: " + cause + ". " + advice);
        Check(cause.StartsWith("the terrain pass made chunk 6765,3125 without Earth's terrain: vanilla's terrain generator was replaced before Earth started")
            && cause.Contains("Test Mod (testmod 1.0.0)'s Checks.OtherModTerrain") && advice.Contains("remove the mod named here"), "the mod that replaced vanilla's terrain is named");
        // Earth's handler replaced after Earth started
        (cause, _) = TerrainPassReport.Cause(1, 2, TerrainPassReport.Read(new[] { other, layers }, earthTerrain, ModOf), viaGenTerra: false, genTerraInPass: false, null, null);
        Check(cause.Contains("Earth's terrain handler was replaced after Earth started") && cause.Contains("Checks.OtherModTerrain"), "the mod that replaced Earth's handler is named");
        // Earth's handler in place but not run, with another mod's handler beside it and a patch
        (cause, _) = TerrainPassReport.Cause(1, 2, TerrainPassReport.Read(new[] { earthTerrain, other, layers }, earthTerrain, ModOf), false, false, null,
            new[] { "some.mod on EarthWorldGen.MakeTerrain" });
        Check(cause.Contains("is in the terrain pass but did not run") && cause.Contains("also runs Test Mod") && cause.Contains("Harmony patches there: some.mod on EarthWorldGen.MakeTerrain"),
            "a handler beside Earth's and a patch on Earth's code are named");
        // nothing of another mod in the pass: taken out (no name to give), or a game update
        (cause, advice) = TerrainPassReport.Cause(1, 2, TerrainPassReport.Read(new[] { layers }, earthTerrain, ModOf), false, false, null, null);
        Check(cause.Contains("Earth's terrain handler was taken out of the terrain pass") && advice.Contains("game update"), "a handler taken out with nothing in its place");
        (cause, _) = TerrainPassReport.Cause(1, 2, TerrainPassReport.Read(new[] { layers }, null, ModOf), true, false, null, null);
        Check(cause.Contains("vanilla's terrain generator, inside which Earth's terrain runs, was taken out"), "vanilla's handler taken out (Earth inside it)");
        // another mod's own generation
        (cause, _) = TerrainPassReport.Cause(1, 2, TerrainPassReport.Read(new[] { earthTerrain, layers }, earthTerrain, ModOf), false, false,
            "Test Mod (testmod 1.0.0)'s own generation (request type X)", null);
        Check(cause.Contains("the chunk came from Test Mod (testmod 1.0.0)'s own generation"), "a chunk of another mod's generation names it");
        Check(calls == 0, "reading the handlers calls none of them");
    }
}
