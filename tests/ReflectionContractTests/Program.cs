using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

// (EA-0132) The members World Map 0.5.3, Hydrology 0.6.2 and Cylinder World 1.1.0 read from Earth by reflection, each looked
// up the way that mod looks it up (by name, by parameter types, as a field or a property, public or not), so a rename, a
// changed type, a new overload or an optional parameter fails here with the consumer's name instead of in their worlds.
// The list, with where each consumer binds it: docs/REFLECTION-SURFACE.md. Keep the two in step.
// Arguments: the repository root (EarthMap.dll from bin/Release/mod, the grass maps from assets), optionally the DLL's path.

static class Program
{
    static int checks, failures;
    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const string Wmap = "World Map 0.5.3", Hydro = "Hydrology 0.6.2", Cw = "Cylinder World 1.1.0";

    static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        string dll = args.Length > 1 ? args[1] : Path.Combine(root, "bin", "Release", "mod", "EarthMap.dll");
        if (!File.Exists(dll)) { Console.Error.WriteLine("No " + dll + ": build EarthMap.csproj -c Release first"); return 2; }
        Assembly earth;
        try { earth = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll)); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        try { Run(earth, root); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        Console.WriteLine(failures == 0 ? $"PASS {checks} reflection contract checks" : $"FAIL {failures} of {checks} reflection contract checks");
        return failures == 0 ? 0 : 1;
    }

    static void Check(bool ok, string consumer, string what)
    {
        checks++;
        if (ok) return;
        failures++;
        Console.Error.WriteLine($"FAIL {consumer} binds {what}");
    }

    static string Name(Type t) => t?.Name ?? "nothing";

    /// <summary>World Map's WorldSource.Member: GetField(name), else GetProperty(name) (public, instance or static); the member's type.</summary>
    static Type Member(Type t, string name) => t == null ? null : t.GetField(name)?.FieldType ?? t.GetProperty(name)?.PropertyType;

    /// <summary>A number Convert.ToDouble reads (World Map's Need, Cylinder World's numbers).</summary>
    static bool Numeric(Type t) => t != null && (t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t == typeof(short) || t == typeof(byte));

    /// <summary>GetMethod(name, types): public, instance or static, as World Map and Cylinder World ask.</summary>
    static MethodInfo Typed(Type t, string name, params Type[] ps) => t?.GetMethod(name, ps);

    /// <summary>An instance method with this return type (CreateDelegate on the instance, or Invoke).</summary>
    static bool Returns(MethodInfo m, Type ret) => m != null && !m.IsStatic && m.ReturnType == ret;

    static Type ModSystem(Assembly earth, string fullName, string consumer)
    {
        var t = earth.GetType(fullName);
        Check(t != null && typeof(ModSystem).IsAssignableFrom(t), consumer, $"the mod system {fullName} (found among the mod systems by its full name)");
        return t;
    }

    static void Run(Assembly earth, string root)
    {
        const string All = "World Map, Hydrology and Cylinder World";
        var system = ModSystem(earth, "EarthMap.EarthMapSystem", All);
        if (system == null) return;

        // Execute order: Cylinder World's EarthPolarSystem (0.09) binds Earth's model after Earth's own start; World Map and
        // Hydrology bind later still
        double order = RuntimeHelpers.GetUninitializedObject(system) is ModSystem ms ? ms.ExecuteOrder() : double.NaN;
        Check(order == 0.05, All, $"EarthMapSystem.ExecuteOrder() 0.05 (Cylinder World's EarthPolarSystem runs at 0.09, after it; found {order})");

        // ---------------------------------------------------------------- EarthMapSystem: Params and Model
        // World Map: a field or property (Member); Cylinder World: Harmony's AccessTools.Property, so a property
        var paramsType = Member(system, "Params");
        var modelType = Member(system, "Model");
        Check(paramsType != null, Wmap, "EarthMapSystem.Params (a public field or property)");
        Check(modelType != null, Wmap, "EarthMapSystem.Model (a public field or property)");
        Check(AccessTools.Property(system, "Params")?.GetMethod != null, Cw, "EarthMapSystem.Params, a property (AccessTools.Property)");
        Check(AccessTools.Property(system, "Model")?.GetMethod != null, Cw, "EarthMapSystem.Model, a property (AccessTools.Property)");
        Check(Returns(system.GetMethod("LongitudeAt", PublicInstance, null, new[] { typeof(double) }, null), typeof(double?)), Wmap,
            "EarthMapSystem.LongitudeAt(double) returning double? (the HUD clock)");

        // Hydrology's river query (EarthRiverQuery.Bind)
        var version = system.GetProperty("RiverQueryVersion", PublicInstance);
        Check(version?.PropertyType == typeof(int) && version.GetMethod != null, Hydro, "EarthMapSystem.RiverQueryVersion, a public int property");
        Type dblRef = typeof(double).MakeByRefType(), intRef = typeof(int).MakeByRefType();
        Check(Returns(system.GetMethod("RiverAt", PublicInstance, null, new[] { typeof(int), typeof(int), typeof(bool).MakeByRefType(), dblRef, dblRef }, null), typeof(bool)), Hydro,
            "EarthMapSystem.RiverAt(int, int, out bool, out double, out double) returning bool");
        Check(Returns(system.GetMethod("RiverFlowAt", PublicInstance, null, new[] { typeof(int), typeof(int) }, null), typeof(int)), Hydro,
            "EarthMapSystem.RiverFlowAt(int, int) returning int");
        Check(Returns(system.GetMethod("RiverSpanAt", PublicInstance, null, new[] { typeof(int), typeof(int), intRef, intRef }, null), typeof(bool)), Hydro,
            "EarthMapSystem.RiverSpanAt(int, int, out int, out int) returning bool");
        var notReady = earth.GetTypes().Where(t => t.Name == "RiverQueryNotReadyException").ToList();
        Check(notReady.Count == 1 && typeof(Exception).IsAssignableFrom(notReady[0]), Hydro,
            "an exception type named RiverQueryNotReadyException (matched by its name, on the exception or an inner one)");

        // Save keys: Earth's mark while it creates a world (Cylinder World reads it), and the key of Cylinder World's record Earth reads
        Check(Constant(system, "CreatingKey") == "earth-creating", Cw, "the save key \"earth-creating\" (EarthMapSystem.CreatingKey; Cylinder World's EarthCreatingKey)");
        Check(Constant(system, "CylinderRecordKey") == "cylinderworld-seam", Cw, "Cylinder World's save key \"cylinderworld-seam\" (EarthMapSystem.CylinderRecordKey)");
        // World-config attributes Earth sets and Hydrology reads (not by reflection: its H2Filling's two constants)
        Check(Constant(system, "HydrologyVesselsKey") == "hydrologyFillVessels", Hydro,
            "the world-config attribute \"hydrologyFillVessels\" (EarthMapSystem.HydrologyVesselsKey; Hydrology's H2Filling.WorldVesselsKey)");
        Check(Constant(system, "HydrologyVesselsWithQueryKey") == "hydrologyFillVesselsWithRiverQuery", Hydro,
            "the world-config attribute \"hydrologyFillVesselsWithRiverQuery\" (EarthMapSystem.HydrologyVesselsWithQueryKey; Hydrology's H2Filling.WorldVesselsWithQueryKey)");

        // ---------------------------------------------------------------- EarthParams
        if (paramsType != null)
        {
            Check(Member(paramsType, "Enabled") == typeof(bool), Wmap, "EarthParams.Enabled (bool)");
            var enabled = AccessTools.Field(paramsType, "Enabled");
            Check(enabled != null && typeof(bool).IsAssignableFrom(enabled.FieldType), Cw, "EarthParams.Enabled, a bool field (AccessTools.Field)");
        }

        if (modelType != null) Model(earth, modelType, root);
        Heights(earth);
        Weather(earth);
        Ores(earth);
    }

    static string Constant(Type t, string name)
    {
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return f != null && f.IsLiteral ? f.GetRawConstantValue() as string : null;
    }

    // -------------------------------------------------------------------- TerrainModel

    static void Model(Assembly earth, Type model, string root)
    {
        // Geometry and Data: World Map reads them as a field or property, Cylinder World as public fields
        var geometry = Member(model, "Geometry");
        var data = Member(model, "Data");
        Check(geometry != null && model.GetField("Geometry") != null, $"{Wmap} and {Cw}", "TerrainModel.Geometry, a public field");
        Check(data != null && model.GetField("Data") != null, $"{Wmap} and {Cw}", "TerrainModel.Data, a public field");

        // FlatZones: World Map clones the model (MemberwiseClone) and puts a copy of this list into the clone
        var zones = model.GetField("FlatZones");
        Check(zones != null && typeof(IList).IsAssignableFrom(zones.FieldType) && !zones.FieldType.IsAbstract && zones.FieldType.GetConstructor(Type.EmptyTypes) != null,
            Wmap, "TerrainModel.FlatZones, a public field holding an IList it can make anew (Activator.CreateInstance) and set on its snapshot");
        Check(!model.IsValueType && !model.IsAbstract, Wmap, "TerrainModel, a class it can snapshot with MemberwiseClone");

        if (geometry != null)
        {
            foreach (string n in new[] { "RingStart", "RingLength", "EquatorZ", "BlocksPerDegreeZ" })
                Check(Numeric(Member(geometry, n)), Wmap, $"{geometry.Name}.{n} (a number)");
            Check(Member(geometry, "Wraps") == typeof(bool), Wmap, $"{geometry.Name}.Wraps (bool)");
            foreach (string n in new[] { "EquatorZ", "BlocksPerDegreeZ" })
                Check(geometry.GetField(n)?.FieldType == typeof(double), Cw, $"{geometry.Name}.{n}, a public double field");
            // World Map's Geography: ToGeo's result into its GeoPosition(double, double, bool, bool)
            var toGeo = Typed(geometry, "ToGeo", typeof(double), typeof(double));
            Check(toGeo != null && !toGeo.IsStatic, Wmap, $"{geometry.Name}.ToGeo(double, double)");
            GeoFields(toGeo?.ReturnType, Wmap, "ToGeo");
        }

        // The column: World Map's SurfaceColumnMethod (GetSurfaceColumn where it returns GetColumn's type) and Cylinder World's GetColumn
        var column = Typed(model, "GetColumn", typeof(double), typeof(double));
        var surface = Typed(model, "GetSurfaceColumn", typeof(double), typeof(double));
        Check(column != null && !column.IsStatic && column.ReturnType.IsValueType, $"{Wmap} and {Cw}", "TerrainModel.GetColumn(double, double) returning a struct");
        Check(surface != null && !surface.IsStatic && surface.ReturnType == column?.ReturnType, Wmap,
            "TerrainModel.GetSurfaceColumn(double, double) returning GetColumn's type (else World Map samples GetColumn, without the craters)");
        if (column != null)
        {
            var c = column.ReturnType;
            // Expression.Field into ColumnSample(int, int, byte, int, bool) and EarthSurface.Convert(..., bool beach, ...)
            foreach (var (n, t) in new[] { ("Top", typeof(int)), ("WaterTop", typeof(int)), ("Water", typeof(byte)), ("IceBottom", typeof(int)), ("River", typeof(bool)), ("Beach", typeof(bool)) })
                Check(c.GetField(n, PublicInstance)?.FieldType == t, Wmap, $"{c.Name}.{n}, a {t.Name} field");
            // Cylinder World's EarthPolarModel (GetField, exact types; WaterTop optional, read in the seam band's water)
            foreach (var (n, t) in new[] { ("Top", typeof(int)), ("IceBottom", typeof(int)), ("Water", typeof(byte)), ("Lat", typeof(double)), ("Lon", typeof(double)), ("WaterTop", typeof(int)) })
                Check(c.GetField(n)?.FieldType == t, Cw, $"{c.Name}.{n}, a {t.Name} field");
        }

        var geoAt = Typed(model, "TerrainGeoAt", typeof(double), typeof(double));
        Check(geoAt != null && !geoAt.IsStatic, Wmap, "TerrainModel.TerrainGeoAt(double, double)");
        GeoFields(geoAt?.ReturnType, Wmap, "TerrainGeoAt");

        // ClimateAt: bound by its bare name (GetMethod(name), which throws once it has an overload), then CreateDelegate
        // to void (double lat, double lon, out double temperature, out double rain, out double height)
        try
        {
            var climate = model.GetMethod("ClimateAt");
            var ps = climate?.GetParameters().Select(p => p.ParameterType).ToArray();
            var dblRef = typeof(double).MakeByRefType();
            Check(climate != null && !climate.IsStatic && climate.ReturnType == typeof(void)
                && ps.SequenceEqual(new[] { typeof(double), typeof(double), dblRef, dblRef, dblRef }), Wmap,
                "TerrainModel.ClimateAt as void(double, double, out double, out double, out double)" + (climate == null ? "" : ", found " + climate));
        }
        catch (AmbiguousMatchException)
        {
            Check(false, Wmap, "TerrainModel.ClimateAt by its name alone, but it now has overloads (GetMethod throws AmbiguousMatchException)");
        }

        if (data != null)
        {
            Check(Returns(Typed(data, "GlacierCoverage", typeof(double), typeof(double)), typeof(double)), Wmap, $"{data.Name}.GlacierCoverage(double, double) returning double");
            Check(Member(data, "HasGlaciers") == typeof(bool), Wmap, $"{data.Name}.HasGlaciers (bool)");
            var hasTerrain = data.GetProperty("HasTerrain");
            Check(hasTerrain?.PropertyType == typeof(bool) && hasTerrain.GetMethod != null, Cw, $"{data.Name}.HasTerrain, a bool property");
            var sample = Typed(data, "SampleClimate", typeof(double), typeof(double));
            Check(sample != null && !sample.IsStatic && sample.ReturnType.IsValueType, Cw, $"{data.Name}.SampleClimate(double, double) returning a struct");
            if (sample != null)
                foreach (string n in new[] { "TMean", "TCold", "TWarm" })
                    Check(sample.ReturnType.GetField(n)?.FieldType == typeof(double), Cw, $"{sample.ReturnType.Name}.{n}, a double field");
        }

        var vertical = Member(model, "Vertical");
        Check(vertical != null && Numeric(Member(vertical, "SeaLevel")) && Numeric(Member(vertical, "MapHeight")), Wmap, "TerrainModel.Vertical's SeaLevel and MapHeight (numbers)");

        // Far terrain's biome tiles
        Check(Returns(Typed(model, "ForestPotential", typeof(double), typeof(double)), typeof(double)), Wmap, "TerrainModel.ForestPotential(double, double) returning double");
        Check(Returns(Typed(model, "ClimatePixelForVanillaLapse", typeof(double), typeof(double), typeof(int), typeof(float), typeof(float)), typeof(int)), Wmap,
            "TerrainModel.ClimatePixelForVanillaLapse(double, double, int, float, float) returning int");

        // Grass seasons: the map index into GrassPhenology.Codes, a 24-step curve, and the maps as earth's assets
        Check(Returns(Typed(model, "GrassSeasonAt", typeof(double), typeof(double), typeof(float[])), typeof(int)), Wmap, "TerrainModel.GrassSeasonAt(double, double, float[]) returning int");
        var steps = earth.GetType("EarthMap.Core.GrassSeason")?.GetField("Steps", AnyStatic);
        Check(steps?.IsLiteral == true && (int)steps.GetRawConstantValue() == 24, Wmap, "GrassSeason.Steps 24 (the curve's buffer it hands GrassSeasonAt)");
        string[] maps = { "evergreen", "season80", "season66", "season52", "season38", "summerdry", "steppe" };
        // Earth's colour map codes are "earthGrass" and the map's name (earthGrassSummerDry: the asset earthgrass-summerdry.png)
        var codes = earth.GetType("EarthMap.Core.GrassPhenology")?.GetField("Codes", AnyStatic)?.GetValue(null) as string[];
        Check(codes != null && codes.Select(c => c.StartsWith("earthGrass", StringComparison.Ordinal) ? c.Substring(10).ToLowerInvariant() : c).SequenceEqual(maps), Wmap,
            "GrassPhenology.Codes in the order evergreen, season80, season66, season52, season38, summerdry, steppe (its share table and map names, by index)" +
            (codes == null ? "" : "; found " + string.Join(", ", codes)));
        var missing = maps.Where(m => !File.Exists(Path.Combine(root, "assets", "earth", "textures", "environment", "seasons", "earthgrass-" + m + ".png"))).ToList();
        Check(missing.Count == 0, Wmap, "the assets earth:textures/environment/seasons/earthgrass-<map>.png" + (missing.Count == 0 ? "" : "; missing " + string.Join(", ", missing)));

        // Rivers: the network's private segments, read field by field into its river lines
        var rivers = Member(model, "Rivers");
        Check(rivers != null, Wmap, "TerrainModel.Rivers (a field or property)");
        if (rivers != null)
        {
            var segs = rivers.GetField("segs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Check(segs != null && segs.FieldType.IsArray, Wmap, $"{rivers.Name}.segs, an instance field holding an array");
            var seg = segs?.FieldType.GetElementType();
            if (seg != null)
            {
                Check(seg.IsValueType, Wmap, $"{seg.Name}, a struct (Expression.Unbox)");
                foreach (string n in new[] { "X1", "Z1", "X2", "Z2", "Q" })
                    Check(Numeric(seg.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType), Wmap, $"{seg.Name}.{n} (a number field)");
            }
            var halfWidth = Typed(rivers, "HalfWidth", typeof(double));
            Check(halfWidth != null && Numeric(halfWidth.ReturnType), Wmap, $"{rivers.Name}.HalfWidth(double) returning a number");
        }

        // Cylinder World's seam gutter: GetField / GetProperty, optional (older Earth versions had neither)
        Check(model.GetField("GutterSeabed")?.FieldType == typeof(int), Cw, "TerrainModel.GutterSeabed, a public int field");
        Check(model.GetProperty("RealSeam")?.PropertyType == typeof(bool), Cw, "TerrainModel.RealSeam, a public bool property");
    }

    /// <summary>The result World Map builds its GeoPosition(double, double, bool, bool) from (Expression.Field).</summary>
    static void GeoFields(Type t, string consumer, string method)
    {
        if (t == null) return;
        foreach (var (n, type) in new[] { ("Lat", typeof(double)), ("Lon", typeof(double)), ("InGutter", typeof(bool)), ("BeyondPole", typeof(bool)) })
            Check(t.GetField(n, PublicInstance)?.FieldType == type, consumer, $"{method}'s {t.Name}.{n}, a {type.Name} field");
    }

    // -------------------------------------------------------------------- FineElevation.BuiltInOnly

    static void Heights(Assembly earth)
    {
        // World Map: GetProperty("BuiltInOnly", Public | Static), bool, public getter and setter
        var p = earth.GetType("EarthMap.Core.FineElevation")?.GetProperty("BuiltInOnly", BindingFlags.Public | BindingFlags.Static);
        Check(p?.PropertyType == typeof(bool) && p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true, Wmap,
            "EarthMap.Core.FineElevation.BuiltInOnly, a public static bool property with a public setter");
        // Cylinder World: AccessTools.TypeByName and AccessTools.Property, a static public setter
        var t = AccessTools.TypeByName("EarthMap.Core.FineElevation");
        var q = t == null ? null : AccessTools.Property(t, "BuiltInOnly");
        Check(q?.PropertyType == typeof(bool) && q.GetSetMethod()?.IsStatic == true, Cw, "FineElevation.BuiltInOnly, a static settable bool (its /cyl map)");
    }

    // -------------------------------------------------------------------- EarthWeather: the barometer

    static void Weather(Assembly earth)
    {
        var weather = ModSystem(earth, "EarthMap.EarthWeather", Wmap);
        var model = weather?.GetProperty("Model", PublicInstance);
        Check(model != null && model.GetMethod != null, Wmap, "EarthWeather.Model, a public instance property");
        var sample = model?.PropertyType.GetMethod("Sample", PublicInstance, null, new[] { typeof(double), typeof(double), typeof(double) }, null);
        Check(sample != null, Wmap, $"{Name(model?.PropertyType)}.Sample(double, double, double)");
        Check(sample?.ReturnType.GetField("Pressure", PublicInstance)?.FieldType == typeof(double), Wmap, $"{Name(sample?.ReturnType)}.Pressure, a double field (hPa)");
    }

    // -------------------------------------------------------------------- the ore heat maps

    static void Ores(Assembly earth)
    {
        var geology = ModSystem(earth, "EarthMap.EarthGeology", Wmap);
        Check(Returns(geology?.GetMethod("RegionalRock", PublicInstance, null, new[] { typeof(double), typeof(double), typeof(int), typeof(int) }, null), typeof(int)), Wmap,
            "EarthGeology.RegionalRock(double, double, int, int) returning int");
        // RockFloorTop by its exact parameter types (an optional third parameter isn't found: EA-0132)
        var floor = earth.GetType("EarthMap.EarthWorldGen")?.GetMethod("RockFloorTop", AnyStatic, null, new[] { typeof(int), typeof(int) }, null);
        Check(floor != null && floor.ReturnType == typeof(int), Wmap, "EarthWorldGen.RockFloorTop(int, int), static, returning int");
        var reading = earth.GetType("EarthMap.RockFloorPass")?.GetMethod("RockFloorReading", AnyStatic, null,
            new[] { typeof(DiscDepositGenerator), typeof(BlockPos), typeof(int[]).MakeByRefType() }, null);
        Check(reading != null && reading.ReturnType == typeof(void), Wmap, "RockFloorPass.RockFloorReading(DiscDepositGenerator, BlockPos, ref int[]), static, returning void");
        Check(earth.GetType("EarthMap.EarthOreGenerator") != null, Wmap, "the deposit generator type EarthMap.EarthOreGenerator (matched by its full name)");
    }
}
