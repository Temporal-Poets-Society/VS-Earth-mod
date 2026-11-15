using System;
using System.Collections.Generic;
using EarthMap.Core;

namespace EarthMap
{
    /// <summary>
    /// (ER-03, ER-12; EA-0078, EA-0086) What the game asks of Earth's weather many times over for the same answer, kept a
    /// moment per thread: each weather region's fog (four regions every frame), each cloud tile's cover (hundreds a pass on
    /// the cloud thread), each region's wind at one game hour (every region every tick, four per windmill or butterfly
    /// lookup), the hail at the player (four regions a client tick). The fog and the cloud tiles are only drawn, so they may
    /// be up to <see cref="DrawHours"/> old (about a second of play at the default speed); the region wind and the hail
    /// are kept only for the exact same hour and place, so they are the model's answer bit for bit. A new model (a world
    /// change, a rebuild) misses everything; the maps keep the model's number, not the model, and are emptied when another
    /// model asks, so a world that is left isn't kept alive (CR-D10).
    /// </summary>
    static class WeatherDrawCache
    {
        /// <summary>How old drawn fog and cloud cover may be (game hours).</summary>
        public const double DrawHours = 0.02;
        /// <summary>How far the ground under the player may move before the land fog is worked out again (m).</summary>
        const double FogMetres = 2;

        struct Fog { public long Model; public double Hours, Metres; public WeatherFog Value; }
        struct Timed { public long Model; public double Hours, Value; }
        struct Hail { public long Model; public double Hours, X, Z, Metres, Value; }

        [ThreadStatic] static Dictionary<long, Fog> fog;
        [ThreadStatic] static Dictionary<long, Timed> tiles, winds;
        [ThreadStatic] static Hail hail;

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        [ThreadStatic] static long forModel;

        /// <summary>Empties this thread's maps when a different model asks than the one they were filled for.</summary>
        static void For(WeatherModel m)
        {
            if (forModel == m.Id) return;
            forModel = m.Id;
            fog?.Clear(); tiles?.Clear(); winds?.Clear();
            hail = default;
        }

        /// <summary>The fog of one weather region (centre x, z) for drawing.</summary>
        public static WeatherFog FogAt(WeatherModel m, int regionX, int regionZ, double x, double z, double hours, double metres)
        {
            For(m);
            var map = fog ??= new Dictionary<long, Fog>();
            long k = Key(regionX, regionZ);
            if (map.TryGetValue(k, out var e) && e.Model == m.Id && Math.Abs(hours - e.Hours) <= DrawHours && Math.Abs(metres - e.Metres) <= FogMetres)
                return e.Value;
            if (map.Count > 256) map.Clear();
            var v = m.FogAt(x, z, hours, metres);
            map[k] = new Fog { Model = m.Id, Hours = hours, Metres = metres, Value = v };
            return v;
        }

        /// <summary>The cloud cover of one cloud tile (centre x, z) for drawing.</summary>
        public static double CloudCoverAt(WeatherModel m, int tileX, int tileZ, double x, double z, double hours)
        {
            For(m);
            var map = tiles ??= new Dictionary<long, Timed>();
            long k = Key(tileX, tileZ);
            if (map.TryGetValue(k, out var e) && e.Model == m.Id && Math.Abs(hours - e.Hours) <= DrawHours) return e.Value;
            if (map.Count > 65536) map.Clear();
            double v = m.CloudCover(x, z, hours);
            map[k] = new Timed { Model = m.Id, Hours = hours, Value = v };
            return v;
        }

        /// <summary>A weather region's wind strength at its centre (x, z), kept for the exact same game hour only.</summary>
        public static double RegionWindAt(WeatherModel m, int regionX, int regionZ, double x, double z, double hours)
        {
            For(m);
            var map = winds ??= new Dictionary<long, Timed>();
            long k = Key(regionX, regionZ);
            if (map.TryGetValue(k, out var e) && e.Model == m.Id && e.Hours == hours) return e.Value;
            if (map.Count > 4096) map.Clear();
            double v = m.WindAt(x, z, hours, false).Strength;
            map[k] = new Timed { Model = m.Id, Hours = hours, Value = v };
            return v;
        }

        /// <summary>The hail at one place and hour (the player), kept for the exact same arguments only.</summary>
        public static double HailAt(WeatherModel m, double x, double z, double hours, double metres)
        {
            For(m);
            ref var e = ref hail;
            if (e.Model == m.Id && e.Hours == hours && e.X == x && e.Z == z && e.Metres == metres) return e.Value;
            double v = m.HailAt(x, z, hours, metres);
            e = new Hail { Model = m.Id, Hours = hours, X = x, Z = z, Metres = metres, Value = v };
            return v;
        }
    }
}
