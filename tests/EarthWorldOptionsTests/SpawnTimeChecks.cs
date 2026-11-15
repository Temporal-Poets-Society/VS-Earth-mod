using System;
using EarthMap.Core;

/// <summary>
/// (2.0.1, EA-0141; 2.0.2, EA-0142) A new world's first hour at its spawn where AstraTerra's sun follows Earth's meridians:
/// Core.SpawnTime moves the clock forward only, less than a day, so the spawn's local solar hour is the game's start hour.
/// </summary>
static class SpawnTimeChecks
{
    public static void Run(Action<bool, string> check)
    {
        const double start = 8, day = 24;
        foreach (var (name, lon) in new[] { ("Riga", 24.105), ("Tokyo", 139.650), ("Santiago", -70.669), ("Wellington", 174.776),
                                            ("Honolulu", -157.858), ("London", -0.128), ("0°", 0.0), ("180°", 180.0), ("-180°", -180.0) })
        {
            double shift = SpawnTime.MorningShift(lon, day);
            double local = SpawnTime.LocalHour(start + shift, lon, day);
            check(Math.Abs(local - start) < 1e-9, $"spawn time: {name} ({lon}°) starts at {start}:00 local (clock moved {shift:+0.00} h, local {local:0.00})");
            // (EA-0142, Fable's F1 on 0fa502a) the game keeps the moment the world started apart from its clock: a move back
            // makes its elapsed time negative, and vanilla's rift weather then fails a restarted first start
            check(shift >= 0 && shift < day, $"spawn time: {name} moves the clock forward only, less than a day ({shift:0.00} h)");
        }
        check(Math.Abs(SpawnTime.MorningShift(24.105, day) - (24 - 24.105 / 15)) < 1e-9, "spawn time: Riga's clock goes forward 22.4 h (its sun is 1.6 h ahead of the calendar)");
        check(Math.Abs(SpawnTime.MorningShift(139.65, day) - (24 - 139.65 / 15)) < 1e-9, "spawn time: Tokyo's clock goes forward 14.7 h, never back 9.3 h");
        check(Math.Abs(SpawnTime.MorningShift(-70.669, day) - 70.669 / 15) < 1e-9, "spawn time: Santiago's clock goes forward 4.7 h");
        check(SpawnTime.MorningShift(0, day) == 0 && SpawnTime.MorningShift(360, day) == 0, "spawn time: on the calendar's own meridian, no move");
        check(Math.Abs(SpawnTime.MorningShift(90, 48) - 36) < 1e-9, "spawn time: a 48-hour day moves by the longitude's share of it");
        check(SpawnTime.MorningShift(double.NaN, day) == 0 && SpawnTime.MorningShift(10, 0) == 0, "spawn time: no number, no move");
        // every longitude: the spawn's local hour is the start hour, and the clock only goes forward
        bool all = true, forward = true;
        for (double lon = -180; lon <= 180; lon += 0.37)
        {
            double shift = SpawnTime.MorningShift(lon, day);
            all &= Math.Abs(SpawnTime.LocalHour(start + shift, lon, day) - start) < 1e-9;
            forward &= shift >= 0 && shift < day;
        }
        check(all, "spawn time: at every longitude the spawn's local hour is the start hour");
        check(forward, "spawn time: at every longitude the clock goes forward only (the world's elapsed time never negative)");
    }
}
