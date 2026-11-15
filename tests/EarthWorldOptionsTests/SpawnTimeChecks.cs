using System;
using EarthMap.Core;

/// <summary>
/// (2.0.1, EA-0141) A new world's first hour at its spawn where AstraTerra's sun follows Earth's meridians: Core.SpawnTime moves
/// the clock so the spawn's local solar hour is the game's start hour, at most half a day either way, never before the
/// calendar's zero.
/// </summary>
static class SpawnTimeChecks
{
    public static void Run(Action<bool, string> check)
    {
        const double start = 8, day = 24, total = 4 * 9 * 24 + start;   // 1 May, 08:00, nine-day months
        foreach (var (name, lon) in new[] { ("Riga", 24.105), ("Tokyo", 139.650), ("Santiago", -70.669), ("Wellington", 174.776),
                                            ("Honolulu", -157.858), ("London", -0.128), ("0°", 0.0), ("180°", 180.0), ("-180°", -180.0) })
        {
            double shift = SpawnTime.MorningShift(lon, day, total);
            double local = SpawnTime.LocalHour(start + shift, lon, day);
            check(Math.Abs(local - start) < 1e-9 || Math.Abs(local - start - day) < 1e-9 || Math.Abs(local - start + day) < 1e-9,
                $"spawn time: {name} ({lon}°) starts at {start}:00 local (clock moved {shift:+0.00;-0.00} h, local {local:0.00})");
            check(shift >= -day / 2 && shift < day / 2, $"spawn time: {name} moves the clock at most half a day ({shift:0.00} h)");
        }
        check(Math.Abs(SpawnTime.MorningShift(24.105, day, total) - (-24.105 / 15)) < 1e-9, "spawn time: Riga's clock goes back 1.6 h (its sun is 1.6 h ahead of the calendar)");
        check(Math.Abs(SpawnTime.MorningShift(-70.669, day, total) - 70.669 / 15) < 1e-9, "spawn time: Santiago's clock goes forward 4.7 h");
        check(Math.Abs(SpawnTime.MorningShift(139.65, day, total) - (-139.65 / 15)) < 1e-9, "spawn time: Tokyo's clock goes back 9.3 h, to the evening before");
        check(Math.Abs(SpawnTime.MorningShift(139.65, day, 5) - (-139.65 / 15 + 24)) < 1e-9, "spawn time: never before the calendar's zero (a day later instead)");
        check(Math.Abs(SpawnTime.MorningShift(90, 48, total) - (-12)) < 1e-9, "spawn time: a 48-hour day moves by the longitude's share of it");
        check(SpawnTime.MorningShift(double.NaN, day, total) == 0 && SpawnTime.MorningShift(10, 0, total) == 0, "spawn time: no number, no move");
        // every longitude: the spawn's local hour is the start hour
        bool all = true;
        for (double lon = -180; lon <= 180; lon += 0.37)
        {
            double local = SpawnTime.LocalHour(start + SpawnTime.MorningShift(lon, day, total), lon, day);
            all &= Math.Abs(local - start) < 1e-9;
        }
        check(all, "spawn time: at every longitude the spawn's local hour is the start hour");
    }
}
