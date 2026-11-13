using EarthMap;
using Vintagestory.API.Common;

// Earth's runtime hooks outside PatchVanilla that read the game's own types (rc.3: the Cylinder World review's items).
static partial class Checks
{
    static void RunWorld()
    {
        ClimateShiftOnce();
        CreatingMarker();
    }

    /// <summary>
    /// Cylinder World review (the "creating" marker left behind): Earth counted a world as new whenever its earth-creating
    /// marker was set. A marker left in a world that has run (Earth removed after a failed first start, the world played
    /// with Cylinder World, Earth added back) made Earth create an Earth world over the played chunks, while Cylinder World
    /// 1.1.0-rc.2 kept it an existing world. Now the marker counts only while the world has no Cylinder World record, as
    /// Cylinder World counts it.
    /// </summary>
    static void CreatingMarker()
    {
        static Vintagestory.API.Server.ISaveGame Save(bool isNew, byte[] creating, byte[] cylinder)
        {
            var save = System.Reflection.DispatchProxy.Create<Vintagestory.API.Server.ISaveGame, FakeSave>();
            var fake = (FakeSave)(object)save;
            fake.IsNew = isNew;
            if (creating != null) fake.Data[EarthMapSystem.CreatingKey] = creating;
            if (cylinder != null) fake.Data[EarthMapSystem.CylinderRecordKey] = cylinder;
            return save;
        }
        byte[] marked = { 1 }, record = { 8, 1 };
        Check(EarthMapSystem.IsNewWorld(Save(true, null, null)), "a new save is a new world");
        Check(EarthMapSystem.IsNewWorld(Save(false, marked, null)), "a world whose first start stopped (marker set, no Cylinder World record) is resumed as new");
        Check(!EarthMapSystem.IsNewWorld(Save(false, marked, record)), "a world that ran with Cylinder World keeps its played chunks though the marker was left behind");
        Check(!EarthMapSystem.IsNewWorld(Save(false, System.Array.Empty<byte>(), null)) && !EarthMapSystem.IsNewWorld(Save(false, null, null)),
            "a world whose marker is cleared, or that never had one, is no new world");
        Check(EarthMapSystem.CylinderRecordKey == "cylinderworld-seam", "Cylinder World's record is its seam params (CylinderWorldCore's SaveKey)");
    }

    /// <summary>
    /// Cylinder World review (EarthClimateHooks): vanilla's reuse overload GetClimateAt(pos, baseClimate, mode, totalDays) sets
    /// a reading's live temperature back to its base and raises OnGetClimate on the same object again (catch-up loops of up
    /// to hundreds of passes). Earth's range extension added its shift to the base on every pass, so a reading in Antarctica
    /// got colder by it each time. Now a reading is shifted once: its base keeps one shift, its live temperature one.
    /// </summary>
    static void ClimateShiftOnce()
    {
        // a reading as vanilla's map gives it: base and live temperature alike (-20 °C, the map's floor)
        var c = new ClimateCondition { Temperature = -20f, WorldGenTemperature = -20f, Rainfall = 0.3f, WorldgenRainfall = 0.3f };
        EarthClimateHooks.Shift(c, -18.5, -18.5);
        Check(c.WorldGenTemperature == -38.5f && c.Temperature == -38.5f, "a fresh reading gets the range extension's shift");
        // the reuse overload as ServerWorldMap and ClientWorldMap do it, 40 passes, vanilla's seasons adding 3 °C from the base
        for (int i = 0; i < 40; i++)
        {
            c.Temperature = c.WorldGenTemperature;
            c.Rainfall = c.WorldgenRainfall;
            c.Temperature += 3;
            EarthClimateHooks.Shift(c, -18.5, -18.5);
        }
        Check(c.WorldGenTemperature == -38.5f && c.Temperature == -35.5f,
            $"a reused reading keeps one shift: base -38.5, live -35.5 (got {c.WorldGenTemperature}, {c.Temperature})");
        // a world from before Earth 0.14.1 (climate rules 0): its base and live shifts differ, each kept once
        var old = new ClimateCondition { Temperature = -20f, WorldGenTemperature = -20f };
        EarthClimateHooks.Shift(old, -10, -18.5);
        for (int i = 0; i < 5; i++)
        {
            old.Temperature = old.WorldGenTemperature;
            EarthClimateHooks.Shift(old, -10, -18.5);
        }
        Check(old.WorldGenTemperature == -30f && old.Temperature == -38.5f, $"an older world's reading keeps its base and live shifts (got {old.WorldGenTemperature}, {old.Temperature})");
        // a new reading each time (world generation's, every fresh GetClimateAt) is shifted each time, as before
        for (int i = 0; i < 3; i++)
        {
            var fresh = new ClimateCondition { Temperature = -20f, WorldGenTemperature = -20f };
            EarthClimateHooks.Shift(fresh, -18.5, -18.5);
            Check(fresh.WorldGenTemperature == -38.5f && fresh.Temperature == -38.5f, "every new reading is shifted");
        }
        // a reading whose base gets no shift (within 0.05 °C) is left as it is however often it is reused
        var mild = new ClimateCondition { Temperature = 12f, WorldGenTemperature = 12f };
        for (int i = 0; i < 3; i++)
        {
            mild.Temperature = mild.WorldGenTemperature;
            EarthClimateHooks.Shift(mild, 0.01, 0.01);
        }
        Check(mild.WorldGenTemperature == 12f && mild.Temperature == 12f, "a reading inside vanilla's range is left alone");
    }
}

/// <summary>A stand-in for the game's save (an interface of its API): IsNew and the mods' stored data.</summary>
public class FakeSave : System.Reflection.DispatchProxy
{
    public bool IsNew;
    public readonly Dictionary<string, byte[]> Data = new();

    protected override object Invoke(System.Reflection.MethodInfo method, object[] args) => method.Name switch
    {
        "get_IsNew" => IsNew,
        "GetData" when args.Length == 1 && args[0] is string key => Data.TryGetValue(key, out var d) ? d : null,
        _ => throw new NotSupportedException(method.Name),
    };
}
