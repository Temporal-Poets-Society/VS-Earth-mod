using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Func4 = System.Func<double, double, double, bool, System.ValueTuple<double, double>>;
using Func3 = System.Func<double, double, double, System.ValueTuple<double, double>>;

namespace EarthMap
{
    /// <summary>
    /// The vanilla readers that take X of the wind as its strength, handed the vector's horizontal length instead (open sea
    /// contracts section 5), on a client while Earth writes vanilla's wind as a vector. Read in 1.22.7's code first:
    ///  - DefaultShaderUniforms.Update: four reads of GlobalConstants.CurrentSurfaceWindSpeedClient.X (the foliage wave
    ///    counters, WindSpeed, the vanilla water's wave intensity);
    ///  - EntityBoat.OnRenderFrame: CurrentWindSpeedClient.X for how much a boat rocks. Its weather vane's Atan2(X, Z) is a
    ///    direction and keeps the vector, so the vane now turns with the wind.
    /// Exactly one mod may apply this, or the second takes the length of a length: the first sets
    /// api.ObjectCache["seacontracts:vanillawind-vector"] to its mod ID, and Earth leaves it when the key is there (Rough Seas
    /// patches the same two). Other vanilla X readers (fruit-tree leaf fall, the calm-air gate of ambient particles) are not
    /// in the contract and stay as they are; the rest of vanilla already takes the wind as a vector or its length.
    /// Where a reader is left alone (a game update changed it), Earth writes this client's wind in vanilla's own form, the
    /// strength in X, rather than a vector the reader would take for a strength (<see cref="ReadersTakeVector"/>, CR-W5).
    /// </summary>
    static class VanillaWindVector
    {
        public const string Key = "seacontracts:vanillawind-vector";
        const string HarmonyId = "earth.windvector";
        static Harmony harmony;
        /// <summary>Readers patched now (empty when another mod holds the key or nothing went in).</summary>
        public static readonly List<string> Active = new List<string>();
        /// <summary>Whether every reader takes the vector's length on this client: Earth patched them all, or the mod that holds
        /// the key does it. False before, and after one was refused: EarthWeather.OnGetWindSpeed then writes the strength in
        /// X (CR-W5).</summary>
        public static volatile bool ReadersTakeVector;

        static readonly FieldInfo SurfaceWind = AccessTools.Field(typeof(GlobalConstants), nameof(GlobalConstants.CurrentSurfaceWindSpeedClient));
        static readonly FieldInfo Wind = AccessTools.Field(typeof(GlobalConstants), nameof(GlobalConstants.CurrentWindSpeedClient));
        static readonly FieldInfo VecX = AccessTools.Field(typeof(Vec3f), nameof(Vec3f.X));

        public static void Apply(ICoreClientAPI capi, ILogger log)
        {
            if (capi == null || harmony != null) return;
            lock (capi.ObjectCache)
            {
                if (capi.ObjectCache.TryGetValue(Key, out var who))
                {
                    log.Notification("Earth weather: {0} already hands vanilla's wind readers the wind vector's length; Earth leaves that to it.", who as string ?? "another mod");
                    ReadersTakeVector = true;
                    return;
                }
                harmony = new Harmony(HarmonyId);
                // (CR-W12) each reader is looked up inside Patch's try: an ambiguous match after a game update would otherwise
                // escape the one-second watch that calls this. Both are tried, also when the first is refused (&, not &&).
                ReadersTakeVector = Patch(log, "shader wind", () => AccessTools.Method(typeof(DefaultShaderUniforms), nameof(DefaultShaderUniforms.Update)), SurfaceWind, nameof(ShaderTranspiler))
                    & Patch(log, "boat rocking", () => AccessTools.Method(typeof(EntityBoat), nameof(EntityBoat.OnRenderFrame)), Wind, nameof(BoatTranspiler));
                if (Active.Count > 0) capi.ObjectCache[Key] = WeatherContract.Owner;
                else harmony = null;
            }
        }

        /// <summary>Client only: the patch is the process's, so a single player's server side must never take it out.</summary>
        public static void Remove(ICoreClientAPI capi)
        {
            if (capi == null) return;
            ReadersTakeVector = false;
            if (harmony == null) return;
            harmony.UnpatchAll(HarmonyId);
            harmony = null;
            Active.Clear();
            lock (capi.ObjectCache)
                if (capi.ObjectCache.TryGetValue(Key, out var who) && who as string == WeatherContract.Owner) capi.ObjectCache.Remove(Key);
        }

        /// <summary>Patches one reader after checking its code still reads X of the wind as a strength (vanilla's code is
        /// looked at first, so a changed method is left alone rather than half patched). False when it is left alone.</summary>
        static bool Patch(ILogger log, string what, Func<MethodInfo> find, FieldInfo field, string transpiler)
        {
            MethodInfo method = null;
            try
            {
                method = find();
                if (method == null || field == null || VecX == null) throw new Exception("method or field not found");
                StrengthForX(PatchProcessor.GetOriginalInstructions(method), field, out int reads);
                if (reads == 0) throw new Exception("it no longer reads the wind's X as its strength");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(VanillaWindVector), transpiler));
                Active.Add(what);
                return true;
            }
            catch (Exception e)
            {
                log.Warning("Earth weather: could not hand {0} the wind's strength ({1}): {2}; this client gets the wind as vanilla writes it, the strength alone (rain slants east, as in vanilla)",
                    method?.DeclaringType?.Name + "." + method?.Name, what, e.Message);
                return false;
            }
        }

        /// <summary>The vanilla strength a reader gets instead of X: the vector's horizontal length.</summary>
        public static float Strength(Vec3f v) => v == null ? 0f : WeatherContract.VanillaStrength(v.X, v.Z);

        /// <summary>Each read of <paramref name="field"/>.X becomes <see cref="Strength"/> of it, except where the next few
        /// instructions read the same field again (a direction: Atan2(X, Z)).</summary>
        static List<CodeInstruction> StrengthForX(IEnumerable<CodeInstruction> instructions, FieldInfo field, out int replaced)
        {
            var codes = new List<CodeInstruction>(instructions);
            var strength = AccessTools.Method(typeof(VanillaWindVector), nameof(Strength));
            replaced = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (!codes[i].LoadsField(field) || !codes[i + 1].LoadsField(VecX)) continue;
                bool direction = false;
                for (int k = i + 2; k < Math.Min(codes.Count, i + 5) && !direction; k++) direction = codes[k].LoadsField(field);
                if (direction) continue;
                var old = codes[i + 1];
                codes[i + 1] = new CodeInstruction(OpCodes.Call, strength).WithLabels(old.labels).WithBlocks(old.blocks);
                replaced++;
            }
            return codes;
        }

        static IEnumerable<CodeInstruction> ShaderTranspiler(IEnumerable<CodeInstruction> instructions) => StrengthForX(instructions, SurfaceWind, out _);

        static IEnumerable<CodeInstruction> BoatTranspiler(IEnumerable<CodeInstruction> instructions) => StrengthForX(instructions, Wind, out _);
    }
}
