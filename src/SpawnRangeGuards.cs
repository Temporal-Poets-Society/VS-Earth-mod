using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>
    /// No natural spawns of a vanilla land animal outside where it lives, in worlds with regional fauna. Both rules are
    /// data in fauna-vanilla.json (ranges proposal, step 2), not code: its range (decision 0031, 1318 ranges 0054; no
    /// spotted hyenas in Japan, no polar bears in the tropics or in Antarctica, no butterflies in Antarctica), and in
    /// worlds with real-altitude ecology its "minWarmMonth" at the spawn's real elevation (goats anywhere; alpine animals
    /// up to the snow line, 0.5 °C; the rest up to the tree line, 6 °C). Animals without an entry are not limited.
    /// Two patches (bound by PatchVanilla): vanilla's worldgen creature placement and its runtime spawner.
    /// </summary>
    static class SpawnRangeGuards
    {
        static bool OutsideItsRange(EntityProperties type, BlockPos pos)
        {
            var w = WorldGenerationContext.Current;
            if (w == null || type?.Code == null || type.Code.Domain != "game") return false;
            return OutsideItsRange(w, type.Code.Path, pos);
        }

        static bool OutsideItsRange(WorldGenerationContext w, string path, BlockPos pos)
        {
            if (w.Params.CreationOptionsVersion > 0 && !w.Params.RegionalFauna) return false;
            var data = w.Rules.FaunaRanges;
            if (data == null) return false;
            bool ranges = w.Params.VanillaFaunaRanges >= 1 && data.Covers(path);
            double minWarm = w.Model.LocalAlpineEcology ? data.MinWarmMonth(path) : double.NaN;
            if (!ranges && double.IsNaN(minWarm)) return false;
            var g = w.Model.TerrainGeoAt(pos.X, pos.Z);
            if (g.InGutter || g.BeyondPole) return false;
            if (ranges && !data.Allows(path, g.Lat, g.Lon)) return true;
            if (double.IsNaN(minWarm)) return false;
            double warm = w.Model.Data.SampleClimate(g.Lat, g.Lon).TWarm - EarthWorldGen.LapsePerMetre * w.Model.Vertical.Elevation(pos.Y);
            return warm < minWarm;
        }

        /// <summary>/earth where: the vanilla animals that may spawn on this ground.</summary>
        internal static IEnumerable<string> Describe(WorldGenerationContext w, BlockPos ground)
        {
            var vanillaRanges = w.Rules.FaunaRanges;
            if (vanillaRanges == null) { yield return "Vanilla animals: no ranges loaded (not limited)."; yield break; }
            var allowed = new List<string>();
            int total = 0;
            foreach (string key in vanillaRanges.Keys) { total++; if (!OutsideItsRange(w, key, ground)) allowed.Add(key); }
            yield return "Vanilla animals that may spawn on this ground (" + allowed.Count + " of " + total + " types): "
                + (allowed.Count == 0 ? "none" : string.Join(", ", allowed)) + ".";
        }

        internal static bool WorldgenSpawnRangeGuard([HarmonyArgument(1)] EntityProperties type, [HarmonyArgument(2)] BlockPos pos, ref bool __result)
        {
            if (pos == null || !OutsideItsRange(type, pos)) return true;
            __result = false;
            return false;
        }

        internal static bool RuntimeSpawnRangeGuard([HarmonyArgument(0)] EntityProperties type, [HarmonyArgument(1)] BlockPos spawnPosition, ref Vec3d __result)
        {
            if (spawnPosition == null || !OutsideItsRange(type, spawnPosition)) return true;
            __result = null;
            return false;
        }
    }
}
