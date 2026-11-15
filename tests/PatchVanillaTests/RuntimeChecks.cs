using System.Reflection;
using System.Runtime.CompilerServices;
using EarthMap;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Feature = EarthMap.PatchVanilla.Feature;
using Spec = EarthMap.PatchVanilla.Spec;
using Bind = EarthMap.PatchVanilla.Bind;
using PatchMethod = EarthMap.PatchVanilla.PatchMethod;

// The running game's and the client's tables (rc.2 review round 3, fix package R): each binds on this game version without a
// line; a member a game update renamed or changed turns its whole feature off with one line, before anything of it is patched.
static partial class Checks
{
    /// <summary>The runtime tables, by the system that binds each.</summary>
    static readonly (string Name, Func<Spec[]> Table)[] RuntimeTables =
    {
        ("grass winter", PatchVanilla.GrassWinterTable), ("ground moisture", PatchVanilla.GroundMoistureTable),
        ("grass season", PatchVanilla.GrassSeasonTable), ("season tint", PatchVanilla.SeasonTintTable),
        ("thunder", PatchVanilla.ThunderTable), ("creative date", PatchVanilla.CreativeDateTable),
        ("snow melt light", PatchVanilla.SnowMeltLightTable),
    };

    /// <summary>A soil tick whose third parameter a game update renamed (vanilla's 1.22.7: object extra).</summary>
    static void TickWithRenamedExtra(IWorldAccessor world, BlockPos pos, object data) { }

    /// <summary>The shader loader with its first parameter renamed (vanilla's 1.22.7: ShaderProgram program).</summary>
    static void LoadShaderProgramRenamed(object shader, bool useSSBOs) { }

    static void RunRuntime()
    {
        // every runtime table binds on this game version, without a line
        foreach (var (name, table) in RuntimeTables)
        {
            var bound = Bind(table());
            Check(bound.Lines.Count == 0, "no line for the " + name + " table: " + string.Join(" | ", bound.Lines));
            foreach (var s in table()) Check(bound.Pv.On(s.Feature), s.Feature + " is on");
            bound.Harmony.UnpatchAll(bound.Harmony.Id);
        }

        // grass winter and ground moisture patch the same soil ticks, each with its own Harmony id: both go in
        var snow = AccessTools.Method(typeof(Block), nameof(Block.PerformSnowLevelUpdate));
        var should = AccessTools.Method(typeof(BlockSoil), nameof(BlockSoil.ShouldReceiveServerGameTicks));
        var tick = AccessTools.Method(typeof(BlockSoil), nameof(BlockSoil.OnServerGameTick));
        var winter = Bind(PatchVanilla.GrassWinterTable());
        var moisture = Bind(PatchVanilla.GroundMoistureTable());
        Check(Earth(snow, winter.Harmony.Id) == 1 && Earth(should, winter.Harmony.Id) == 1 && Earth(tick, winter.Harmony.Id) == 1
            && Earth(should, moisture.Harmony.Id) == 1 && Earth(tick, moisture.Harmony.Id) == 1, "grass winter's and ground moisture's soil patches go in side by side");
        winter.Harmony.UnpatchAll(winter.Harmony.Id);
        moisture.Harmony.UnpatchAll(moisture.Harmony.Id);

        // Codex 4 / A7: a game update renames OnServerGameTick's "extra". Before, EarthGrassWinter patched the snow swap and the
        // tick postfix, then threw out of its 5-second watch (every 5 s, a Fatal line and the rest of that server tick lost)
        // and never stopped trying. Now it is found before anything is patched: one line, the whole feature off.
        var renamedExtra = Bind(PatchVanilla.GrassWinterTable().Select(s => Like(s, () => new Bind[]
        {
            new PatchMethod(typeof(Block), nameof(Block.PerformSnowLevelUpdate), prefix: (typeof(EarthGrassWinter), nameof(EarthGrassWinter.SnowPrefix))),
            new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.ShouldReceiveServerGameTicks), postfix: (typeof(EarthGrassWinter), nameof(EarthGrassWinter.ShouldTickPostfix))),
            new PatchMethod(typeof(Checks), nameof(TickWithRenamedExtra), prefix: (typeof(EarthGrassWinter), nameof(EarthGrassWinter.OnTickPrefix))),
        })).ToArray());
        Console.WriteLine("  grass winter, renamed parameter: " + renamedExtra.Lines.Single());
        Check(!renamedExtra.Pv.On(Feature.GrassWinter) && renamedExtra.Lines.Single().Contains("no parameter 'extra'")
            && renamedExtra.Lines.Single().Contains("tall grass after the snow (grass winter rules) is off"), "a renamed soil tick parameter turns grass winter off, named");
        Check(Earth(snow, renamedExtra.Harmony.Id) == 0 && Earth(should, renamedExtra.Harmony.Id) == 0, "nothing of grass winter is patched");
        renamedExtra.Harmony.UnpatchAll(renamedExtra.Harmony.Id);

        // the soil tick gone (a missing member): ground moisture off, its tick postfix not patched either
        var noTick = Bind(PatchVanilla.GroundMoistureTable().Select(s => Like(s, () => new Bind[]
        {
            new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.ShouldReceiveServerGameTicks), postfix: (typeof(EarthGroundMoisture), nameof(EarthGroundMoisture.ShouldTickPostfix))),
            new PatchMethod(typeof(BlockSoil), "OnServerGameTickRenamed", prefix: (typeof(EarthGroundMoisture), nameof(EarthGroundMoisture.OnTickPrefix))),
        })).ToArray());
        Console.WriteLine("  ground moisture, missing member: " + noTick.Lines.Single());
        Check(!noTick.Pv.On(Feature.GroundMoisture) && noTick.Lines.Single().Contains("BlockSoil.OnServerGameTickRenamed not found")
            && noTick.Lines.Single().Contains("dry land keeps its grass all year"), "a missing soil tick turns ground moisture off, named");
        Check(Earth(should, noTick.Harmony.Id) == 0, "nothing of ground moisture is patched");
        noTick.Harmony.UnpatchAll(noTick.Harmony.Id);

        // a failure while patching (a later patch throwing) takes back the feature's earlier patches
        var throwing = Bind(PatchVanilla.GrassWinterTable().Select(s => Like(s, () => new Bind[]
        {
            new PatchMethod(typeof(Block), nameof(Block.PerformSnowLevelUpdate), prefix: (typeof(EarthGrassWinter), nameof(EarthGrassWinter.SnowPrefix))),
            new PatchMethod(typeof(BlockSoil), nameof(BlockSoil.OnServerGameTick), transpiler: (typeof(Checks), nameof(Throws))),
        })).ToArray());
        Check(!throwing.Pv.On(Feature.GrassWinter) && throwing.Lines.Single().Contains("could not be patched") && Earth(snow, throwing.Harmony.Id) == 0,
            "a patch throwing half-way leaves nothing of grass winter patched");
        throwing.Harmony.UnpatchAll(throwing.Harmony.Id);

        // the client's grass colour: the chunk mesh's transpiler finds vanilla's colour data on this game version
        var tesselator = AccessTools.TypeByName("Vintagestory.Client.NoObf.ChunkTesselator");
        var colorMap = AccessTools.Method(AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientWorldMap"), "getColorMapData");
        var season = Bind(PatchVanilla.GrassSeasonTable());
        Check(season.Pv.On(Feature.GrassSeason) && EarthGrassSeason.TesselatorPatched && Earth(colorMap, season.Harmony.Id) == 1, "the grass colour's mesh and accessor hooks go in");
        season.Harmony.UnpatchAll(season.Harmony.Id);
        // Codex 4: the mesh's colour data moved (the transpiler finds nothing): the whole feature is off, the accessor's postfix
        // taken back with it (before, it stayed: microblocks in Earth's colour, terrain grass in vanilla's)
        var noMesh = Bind(PatchVanilla.GrassSeasonTable().Select(s => Like(s, () => new Bind[]
        {
            new PatchMethod(tesselator, "TesselateBlock", new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) },
                transpiler: (typeof(EarthGrassSeason), nameof(EarthGrassSeason.TesselateTranspiler))),
            new PatchMethod(colorMap.DeclaringType, "getColorMapData", postfix: (typeof(EarthGrassSeason), nameof(EarthGrassSeason.ColorMapDataPostfix))),
        })).ToArray());
        Console.WriteLine("  grass season, mesh colour data not found: " + noMesh.Lines.Single());
        Check(!noMesh.Pv.On(Feature.GrassSeason) && noMesh.Lines.Single().Contains("the code Earth rewrites is not there") && noMesh.Lines.Single().Contains("grass keeps vanilla's colour")
            && Earth(colorMap, noMesh.Harmony.Id) == 0, "a mesh without its colour data turns the grass colour off, nothing of it patched");
        noMesh.Harmony.UnpatchAll(noMesh.Harmony.Id);
        // the accessor's method gone: off, the mesh's transpiler taken back (it was applied first)
        var tesselate = AccessTools.Method(tesselator, "TesselateBlock", new[] { typeof(Block), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) });
        var noAccessor = Bind(PatchVanilla.GrassSeasonTable().Select(s => Like(s, () => new Bind[]
        {
            new PatchMethod(tesselator, "TesselateBlock", new[] { typeof(Block), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) },
                transpiler: (typeof(EarthGrassSeason), nameof(EarthGrassSeason.TesselateTranspiler))),
            new PatchMethod(colorMap.DeclaringType, "getColorMapDataRenamed", postfix: (typeof(EarthGrassSeason), nameof(EarthGrassSeason.ColorMapDataPostfix))),
        })).ToArray());
        Check(!noAccessor.Pv.On(Feature.GrassSeason) && noAccessor.Lines.Single().Contains("ClientWorldMap.getColorMapDataRenamed not found") && Earth(tesselate, noAccessor.Harmony.Id) == 0,
            "a missing accessor turns the grass colour off before the mesh is patched");
        noAccessor.Harmony.UnpatchAll(noAccessor.Harmony.Id);

        // the season tint: the shader loader's parameter renamed (before, Harmony threw out of the client's mod start)
        var renamedProgram = Bind(PatchVanilla.SeasonTintTable().Select(s => Like(s, () => new Bind[]
            { new PatchMethod(typeof(Checks), nameof(LoadShaderProgramRenamed), postfix: (typeof(SeasonTintAltitude), nameof(SeasonTintAltitude.Loaded))) })).ToArray());
        Console.WriteLine("  season tint, renamed parameter: " + renamedProgram.Lines.Single());
        Check(!renamedProgram.Pv.On(Feature.SeasonTint) && renamedProgram.Lines.Single().Contains("no parameter 'program'")
            && renamedProgram.Lines.Single().Contains("the season tint keeps vanilla's altitude cooling"), "a renamed loader parameter turns the season tint off, named");
        renamedProgram.Harmony.UnpatchAll(renamedProgram.Harmony.Id);

        SeasonTintLine();
        NoClientMapRaceCopy();
        Thunder();
        CreativeDate();
        PerWorldState();
        RunWorld();
    }

    /// <summary>
    /// The creative panel's date rows (rc.3): before, EarthCreativeDate patched WorldEdit's setter, then its getter, then the
    /// dialog's constructor one by one, so a later failure left the earlier patches in. Now a renamed getter turns the whole
    /// feature off with one line, and neither the setter nor the constructor keeps an Earth patch.
    /// </summary>
    static void CreativeDate()
    {
        var handler = AccessTools.TypeByName("Vintagestory.ServerMods.WorldEdit.WorldEditClientHandler");
        var setter = AccessTools.Method(handler, "OnSetValueSettings", new[] { typeof(string), typeof(string) });
        var build = AccessTools.Constructor(typeof(GuiJsonDialog), new[] { typeof(JsonDialogSettings), typeof(ICoreClientAPI), typeof(bool) });
        Check(handler != null && setter != null && build != null, "WorldEdit's handler, its setter and the dialog's constructor are there on this game version");
        var bound = Bind(PatchVanilla.CreativeDateTable());
        Check(bound.Pv.On(Feature.CreativeDate) && EarthCreativeDate.SettingsDialogField?.FieldType == typeof(GuiJsonDialog)
            && Earth(setter, bound.Harmony.Id) == 1 && Earth(build, bound.Harmony.Id) == 1, "the date rows bind whole: setter, getter, the dialog's constructor and its field");
        bound.Harmony.UnpatchAll(bound.Harmony.Id);

        var renamedGetter = Bind(PatchVanilla.CreativeDateTable().Select(s => Like(s, () => s.Binds().Select(b => b is PatchMethod p && p.Name.EndsWith("OnGetValueSettings")
            ? new PatchMethod(handler, "OnGetValueSettingsRenamed", new[] { typeof(string) }, prefix: (typeof(EarthCreativeDate), nameof(EarthCreativeDate.OnGet)), typeName: "WorldEditClientHandler")
            : b).ToArray())).ToArray());
        Console.WriteLine("  creative date, renamed getter: " + renamedGetter.Lines.Single());
        Check(!renamedGetter.Pv.On(Feature.CreativeDate) && Earth(setter, renamedGetter.Harmony.Id) == 0 && Earth(build, renamedGetter.Harmony.Id) == 0
            && renamedGetter.Lines.Single().Contains("/earth date still works"), "a renamed getter turns the date rows off whole, named, nothing left patched");
        renamedGetter.Harmony.UnpatchAll(renamedGetter.Harmony.Id);
        EarthCreativeDate.SettingsDialogField = null;
    }

    /// <summary>A8: what one world leaves to the next in a single-player process (the game goes back to the main menu and on
    /// to another world without a new process). The ground tiles' budget is checked in GroundColumnsTests.</summary>
    static void PerWorldState()
    {
        // the volcano guards' static instance lets go of its world when the world closes (before: it kept the closed world's
        // terrain model, volcano plans and server alive until the next world started)
        var instance = typeof(EarthVolcanoes).GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static);
        var first = new EarthVolcanoes();
        instance.SetValue(null, first);
        first.Dispose();
        Check(EarthVolcanoes.Instance == null, "a closed world's volcano guards let go of it");
        var second = new EarthVolcanoes();
        instance.SetValue(null, second);
        first.Dispose();
        Check(EarthVolcanoes.Instance == second, "a world closing late leaves the next world's volcano guards in place");
        second.Dispose();

        // the weather's hook failures are each world's (before: one static set for the process, so a second world's failures
        // of a hook that had failed before were never logged)
        var firstWeather = new EarthWeather();
        var secondWeather = new EarthWeather();
        Check(firstWeather.FirstFailure("the wind lookup") && !firstWeather.FirstFailure("the wind lookup") && secondWeather.FirstFailure("the wind lookup"),
            "a hook failure is logged once per world, and again in the next world");
    }

    /// <summary>Codex 6: Earth's thunder binds whole (the region tick and every field its prefix sets), and gives vanilla back
    /// every value it changed when it lets go.</summary>
    static void Thunder()
    {
        var tick = AccessTools.Method(typeof(WeatherSimulationRegion), nameof(WeatherSimulationRegion.TickEvery25ms));
        // a field the prefix sets renamed by a game update: off before the region tick is patched (EarthThunder then stays off
        // for the session, vanilla's thunder events on, never both; before, its On stayed true after a failed binding)
        var renamed = Bind(PatchVanilla.ThunderTable().Select(s => Like(s, () => s.Binds().Select(b => b.Name == "WeatherEventState.LightningRate"
            ? new PatchVanilla.FieldByName(() => typeof(WeatherEventState), "WeatherEventState", "LightningRateRenamed", typeof(float))
            : b).ToArray())).ToArray());
        Console.WriteLine("  thunder, renamed event field: " + renamed.Lines.Single());
        Check(!renamed.Pv.On(Feature.Thunder) && renamed.Lines.Single().Contains("WeatherEventState.LightningRateRenamed not found")
            && renamed.Lines.Single().Contains("vanilla's thunder events stay") && Earth(tick, renamed.Harmony.Id) == 0, "a renamed event field turns Earth's thunder off, nothing patched");
        renamed.Harmony.UnpatchAll(renamed.Harmony.Id);

        // a region as vanilla's weather keeps it: its event's lightning and thunder, and the client's eased snapshot of them
        static (WeatherSimulationRegion Region, WeatherEvent Event) Region(float lightning, float near, float distant, float minTemp)
        {
            var region = (WeatherSimulationRegion)RuntimeHelpers.GetUninitializedObject(typeof(WeatherSimulationRegion));
            var ev = (WeatherEvent)RuntimeHelpers.GetUninitializedObject(typeof(WeatherEvent));
            ev.State = new WeatherEventState { LightningRate = lightning, NearThunderRate = near, DistantThunderRate = distant, LightningMinTemp = minTemp };
            region.CurWeatherEvent = ev;
            region.weatherData = new WeatherDataSnapshot { distantLightningRate = distant, lightningMinTemp = minTemp };
            return (region, ev);
        }
        // the client: Earth's near thunder at any temperature, vanilla's distant thunder off; given back when Earth lets go
        // (before, /weather unpatched the prefix and the event kept Earth's values until it ended)
        var (r, ev1) = Region(0.003f, 0.02f, 0.05f, 18);
        var client = new EarthThunder();
        client.TakeOver(r, ev1.State, client: true);
        client.TakeOver(r, ev1.State, client: true);   // the next tick: vanilla's values stay as first noted
        Check(ev1.State.LightningRate == 0 && ev1.State.DistantThunderRate == 0 && ev1.State.LightningMinTemp == -100
            && r.weatherData.distantLightningRate == 0 && r.weatherData.lightningMinTemp == -100, "Earth's thunder in place of the event's on the client");
        client.GiveBack(client: true);
        Check(ev1.State.LightningRate == 0.003f && ev1.State.NearThunderRate == 0.02f && ev1.State.DistantThunderRate == 0.05f && ev1.State.LightningMinTemp == 18
            && r.weatherData.distantLightningRate == 0.05f && r.weatherData.lightningMinTemp == 18, "the client's event and snapshot get vanilla's values back");
        // the server: only the strike rate is Earth's. A new event in the region (another state): the state before gets its
        // rate back at once, the new one when Earth lets go
        var (s, ev2) = Region(0.003f, 0.02f, 0.05f, 18);
        var server = new EarthThunder();
        server.TakeOver(s, ev2.State, client: false);
        Check(ev2.State.LightningRate == 0 && ev2.State.LightningMinTemp == 18 && ev2.State.NearThunderRate == 0.02f, "the server changes only the strike rate");
        var ev3 = (WeatherEvent)RuntimeHelpers.GetUninitializedObject(typeof(WeatherEvent));
        ev3.State = new WeatherEventState { LightningRate = 0.007f, LightningMinTemp = 20 };
        s.CurWeatherEvent = ev3;
        server.TakeOver(s, ev3.State, client: false);
        Check(ev2.State.LightningRate == 0.003f && ev3.State.LightningRate == 0, "a replaced event state gets vanilla's strike rate back");
        server.GiveBack(client: false);
        Check(ev3.State.LightningRate == 0.007f && ev3.State.LightningMinTemp == 20, "the server's events get vanilla's strike rate back");
    }

    /// <summary>Cylinder World review: Earth no longer carries the client map race fix (Cylinder World, required from 1.0.4 on,
    /// locks the same map dictionaries in every version Earth accepts): no Earth mod system or patch for it is left. Map
    /// regions are a ConcurrentDictionary on this game version, which is why Cylinder World 1.1 locks only the map chunks
    /// there (it locks the regions too where they are a plain dictionary).</summary>
    static void NoClientMapRaceCopy()
    {
        Check(typeof(EarthMapSystem).Assembly.GetType("EarthMap.ClientMapRaceFix") == null, "Earth has no ClientMapRaceFix mod system");
        Check(!Enum.GetNames<Feature>().Contains("ClientMapRace"), "PatchVanilla has no client map race feature");
        var regions = AccessTools.Field(AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientWorldMap"), "MapRegions");
        Check(regions != null && regions.FieldType.IsGenericType && regions.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>),
            "the client's map regions are a ConcurrentDictionary on this game version");
    }

    /// <summary>Codex 9: a chunk shader whose altitude line a game update changed, in a world with climate rules 3, is said once
    /// a session (before: silently left as vanilla's); other shaders and other worlds say nothing.</summary>
    static void SeasonTintLine()
    {
        const string vanillaLine = "x += max(0.0, seaLevelDist * 1.5);";
        static (IShaderProgram Program, FakeShaderPart Shader) Program(string pass, string code)
        {
            var shader = DispatchProxy.Create<IShader, FakeShaderPart>();
            var part = (FakeShaderPart)(object)shader;
            part.Props["Code"] = code;
            var program = DispatchProxy.Create<IShaderProgram, FakeShaderPart>();
            ((FakeShaderPart)(object)program).Props["PassName"] = pass;
            ((FakeShaderPart)(object)program).Props["VertexShader"] = shader;
            return (program, part);
        }
        var lines = new List<string>();
        SeasonTintAltitude.Begin(lines.Add);
        // a world without rules 3 keeps vanilla's line: nothing to say where it is missing
        SeasonTintAltitude.Loaded(Program("chunkopaque", "void main() { }").Program);
        Check(lines.Count == 0, "no line where the world keeps vanilla's tint");
        var owner = new object();
        ClimateLapse.Use(owner, new VerticalScale(1024, 110, 1022, 1000, 47, 20, true), realRain: true);
        try
        {
            var found = Program("chunkopaque", "float x = 0.0; " + vanillaLine);
            SeasonTintAltitude.Loaded(found.Program);
            Check(lines.Count == 0 && ((string)found.Shader.Props["Code"]).Contains("Earth Map: the real lapse") && !((string)found.Shader.Props["Code"]).Contains(vanillaLine),
                "the line is rewritten in a rules 3 world");
            SeasonTintAltitude.Loaded(Program("gui", "void main() { }").Program);
            Check(lines.Count == 0, "a shader without the plant tint says nothing");
            SeasonTintAltitude.Loaded(Program("chunkopaque", "x += max(0.0, seaLevelDist * 1.25);").Program);
            SeasonTintAltitude.Loaded(Program("chunktopsoil", "x += max(0.0, seaLevelDist * 1.25);").Program);
            Console.WriteLine("  season tint, changed shader line: " + lines.SingleOrDefault());
            Check(lines.Count == 1 && lines[0].Contains("the chunkopaque shader no longer has the line") && lines[0].Contains("keeps vanilla's altitude cooling"),
                "a chunk shader without the line is said once a session, naming it");
            SeasonTintAltitude.Begin(lines.Add);
            SeasonTintAltitude.Loaded(Program("chunkliquid", "void main() { }").Program);
            Check(lines.Count == 2, "and again in the next session");
        }
        finally { ClimateLapse.Use(owner, null); }
    }
}

/// <summary>A stand-in for the client's shader program and shader (interfaces of the game's API): properties only.</summary>
public class FakeShaderPart : DispatchProxy
{
    public readonly Dictionary<string, object> Props = new();

    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name.StartsWith("get_", StringComparison.Ordinal)) return Props.TryGetValue(method.Name.Substring(4), out var v) ? v : Default(method.ReturnType);
        if (method.Name.StartsWith("set_", StringComparison.Ordinal)) { Props[method.Name.Substring(4)] = args[0]; return null; }
        return Default(method.ReturnType);
    }

    static object Default(Type t) => t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
}
