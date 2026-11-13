using System;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>
    /// Climate rules v2 (decision 0009): in Earth worlds that have them, vanilla's altitude adjustment of the climate map's
    /// temperature cools by the real lapse rate, 0.0065 °C per real metre of Earth's vertical scale
    /// (TerrainModel.Lapse), instead of 1 °C per 6.375 blocks, and the climate map stores the sea-level temperature
    /// (TerrainModel.ClimatePixel). Every reader of the map goes through these four methods of vanilla's Climate class: world
    /// generation (block layers, snow, ponds, trees, patches, structures, deposits, creatures), the live climate on both sides
    /// and the client's plant and water tints, also in other mods. The lapse per block distance is a table built once per world;
    /// without one (other worlds, Earth worlds with older rules) the prefixes let vanilla run. Climate rules 3 (decision 0031's
    /// test profiles) also store the real rainfall, and vanilla's GetRainFall then leaves it as it is (no altitude rain).
    /// </summary>
    static class ClimateLapse
    {
        const string HarmonyId = "earth.climatelapse";
        static readonly object gate = new object();
        static bool patched;

        /// <summary>The lapse (°C) per distance to sea level, from -Offset; null when vanilla's own adjustment applies.</summary>
        sealed class Table
        {
            public float[] Celsius;
            public int Offset;
            public VerticalScale Vertical;
            /// <summary>Climate rules 3: the map's rainfall is the real one, read without vanilla's altitude adjustment.</summary>
            public bool RealRain;
        }
        static volatile Table table;
        /// <summary>Who uses the real lapse (each side's EarthMapSystem): in single player server and client share this process and
        /// the table, and either may shut down first while the other still generates or renders.</summary>
        static readonly System.Collections.Generic.Dictionary<object, (VerticalScale Vertical, bool RealRain)> users =
            new System.Collections.Generic.Dictionary<object, (VerticalScale, bool)>();

        /// <summary>Installs the prefixes (once per process; they do nothing until <see cref="Use"/>). Early, at mod start, so
        /// that nothing has compiled vanilla's small methods into its own code yet.</summary>
        public static void Patch(ICoreAPI api, Mod mod)
        {
            lock (gate)
            {
                if (patched) return;
                patched = true;
                // PatchVanilla's ClimateLapseTable: the four temperature adjustments together (a world reads one lapse or
                // vanilla's everywhere, never a mix), the rain adjustment on its own
                PatchVanilla.For(api, mod, new Harmony(HarmonyId)).Apply(PatchVanilla.ClimateLapseTable());
            }
        }

        /// <summary>One side's claim: the real lapse for its world (climate rules 2+), or none (null), and whether its map stores
        /// the real rainfall (rules 3). The table stays while any side still claims it, so one side shutting down doesn't switch
        /// it off under the other.</summary>
        public static void Use(object user, VerticalScale vertical, bool realRain = false)
        {
            lock (gate)
            {
                if (vertical == null) users.Remove(user);
                else users[user] = (vertical, realRain);
                (VerticalScale Vertical, bool RealRain) use = default;
                foreach (var v in users.Values) use = v;   // one world per process: every claim has the same scale and rules
                if (use.Vertical == null) { table = null; return; }
                if (table?.Vertical == use.Vertical && table.RealRain == use.RealRain) return;
                int sea = use.Vertical.SeaLevel, offset = sea + 64, top = use.Vertical.MapHeight + 64;
                var c = new float[offset + top - sea + 1];
                for (int i = 0; i < c.Length; i++) c[i] = (float)TerrainModel.Lapse(use.Vertical, sea + i - offset);
                table = new Table { Celsius = c, Offset = offset, Vertical = use.Vertical, RealRain = use.RealRain };
            }
        }

        /// <summary>The vertical scale of a world with climate rules 3 on this side, else null (SeasonTintAltitude).</summary>
        public static VerticalScale RealRainVertical => table is { RealRain: true } t ? t.Vertical : null;

        /// <summary>Whether the real lapse is in use (an Earth world with climate rules 2+ on this side).</summary>
        public static bool Active => table != null;

        /// <summary>The adjustment in the climate byte's units (vanilla: distToSealevel / 1.5).</summary>
        static float LapseBytes(Table t, int distToSealevel)
        {
            int i = distToSealevel + t.Offset;
            float celsius = (uint)i < (uint)t.Celsius.Length ? t.Celsius[i] : (float)TerrainModel.Lapse(t.Vertical, t.Vertical.SeaLevel + distToSealevel);
            return celsius * Climate.TemperatureScaleConversion;
        }

        // the same formulas as vanilla's, with distToSealevel / 1.5f replaced by the real lapse in byte units

        internal static bool ScaledFloat(int unscaledTemp, int distToSealevel, ref float __result)
        {
            var t = table;
            if (t == null) return true;
            __result = Math.Clamp((unscaledTemp - LapseBytes(t, distToSealevel)) / Climate.TemperatureScaleConversion - 20f, -20f, 40f);
            return false;
        }

        internal static bool ScaledFloatClient(int unscaledTemp, int distToSealevel, ref float __result)
        {
            var t = table;
            if (t == null) return true;
            __result = Math.Clamp((unscaledTemp - LapseBytes(t, distToSealevel)) / Climate.TemperatureScaleConversion - 20f, -50f, 40f);
            return false;
        }

        internal static bool Scaled(int unscaledTemp, int distToSealevel, ref int __result)
        {
            var t = table;
            if (t == null) return true;
            __result = Math.Clamp((int)((unscaledTemp - LapseBytes(t, distToSealevel)) / Climate.TemperatureScaleConversion) - 20, -20, 40);
            return false;
        }

        internal static bool Adjusted(int unscaledTemp, int distToSealevel, ref int __result)
        {
            var t = table;
            if (t == null) return true;
            __result = (int)Math.Clamp(unscaledTemp - LapseBytes(t, distToSealevel), 0f, 255f);
            return false;
        }

        internal static bool Rain(int rainfall, ref int __result)
        {
            var t = table;
            if (t == null || !t.RealRain) return true;
            __result = Math.Clamp(rainfall, 0, 255);
            return false;
        }
    }
}
