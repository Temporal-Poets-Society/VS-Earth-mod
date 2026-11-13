using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Decision 0045. Vanilla's disc-followsealevel deposits (saltpeter, most iron, coal seams, sulfur, gems…) turn their
    /// relative heights into block heights once, in Init (GenDeposits.AssetsFinalize), from the sea level GenTerra has just
    /// set from the world height. Earth lowers the sea level only at WorldReady (EarthVerticalBootstrap; earlier would reseed
    /// vanilla's block-layer noise, which reads the sea level in StartServerSide), so those deposits stayed scaled for
    /// vanilla's level: up to 0.8 x 220 blocks at world height 512 against Earth's 110, in hills and, for saltpeter (placed
    /// in air), coating the ground. A postfix on Init records the level each generator was scaled with; Rescale brings every
    /// recorded or listed generator to the current level. Idempotent, so it may run more than once.
    /// </summary>
    static class SealevelDeposits
    {
        const string HarmonyId = "earth.sealeveldeposits";
        static readonly ConditionalWeakTable<FollowSealevelDiscGenerator, StrongBox<int>> scaledWith = new();
        static Harmony harmony;
        /// <summary>The Init postfix is in place: recorded levels are exact, not assumed.</summary>
        public static bool Hooked { get; internal set; }

        /// <summary>The Init postfix, through PatchVanilla's SealevelTable (a missing or changed Init is its line, and the levels
        /// are then assumed).</summary>
        public static void Patch(ICoreAPI api, Mod mod)
        {
            if (Hooked) return;
            harmony ??= new Harmony(HarmonyId);
            PatchVanilla.For(api, mod, harmony).Apply(PatchVanilla.SealevelTable());
        }

        public static void Unpatch()
        {
            harmony?.UnpatchAll(HarmonyId);
            Hooked = false;
            scaledWith.Clear();
        }

        internal static void InitPostfix(FollowSealevelDiscGenerator __instance) => scaledWith.AddOrUpdate(__instance, new StrongBox<int>(TerraGenConfig.seaLevel));

        /// <summary>The world's sea-level deposit generators: GenDeposits' variants and their children, and any recorded elsewhere.</summary>
        static IEnumerable<FollowSealevelDiscGenerator> Generators(ICoreServerAPI api)
        {
            var seen = new HashSet<FollowSealevelDiscGenerator>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<DepositVariant>(api.ModLoader.GetModSystem<GenDeposits>()?.Deposits ?? Array.Empty<DepositVariant>());
            while (stack.Count > 0)
            {
                var v = stack.Pop();
                if (v == null) continue;
                if (v.GeneratorInst is FollowSealevelDiscGenerator g && seen.Add(g)) yield return g;
                foreach (var c in v.ChildDeposits ?? Array.Empty<DepositVariant>()) stack.Push(c);
            }
            foreach (var pair in scaledWith)
                if (seen.Add(pair.Key)) yield return pair.Key;
        }

        /// <summary>
        /// Scales every sea-level deposit to the current sea level. <paramref name="assumed"/> is the level an unrecorded
        /// generator is taken to be scaled with (the one before Earth changed it). Returns a log line, null when nothing moved.
        /// </summary>
        public static string Rescale(ICoreServerAPI api, int assumed)
        {
            int sea = TerraGenConfig.seaLevel, moved = 0, unrecorded = 0;
            var from = new SortedSet<int>();
            var done = new HashSet<NatFloat>(ReferenceEqualityComparer.Instance);
            foreach (var g in Generators(api))
            {
                int was;
                if (scaledWith.TryGetValue(g, out var box)) was = box.Value;
                else { was = assumed; unrecorded++; }
                if (was == sea || was <= 0) continue;
                // a NatFloat shared by two generators is scaled once
                if (g.YPosRel != null && done.Add(g.YPosRel))
                {
                    float f = sea / (float)was;
                    g.YPosRel.avg *= f;
                    g.YPosRel.var *= f;
                }
                scaledWith.AddOrUpdate(g, new StrongBox<int>(sea));
                from.Add(was);
                moved++;
            }
            if (moved == 0) return null;
            return string.Format("Earth Map: {0} sea-level deposit generator(s) rescaled from sea level {1} to {2}{3}.", moved, string.Join("/", from), sea,
                unrecorded > 0 ? $" ({unrecorded} not recorded at Init, assumed {assumed})" : "");
        }

        /// <summary>For the self-test: generators found, and those not scaled for the current sea level (or unrecorded).</summary>
        public static (int Count, int Mismatched) Check(ICoreServerAPI api)
        {
            int n = 0, bad = 0, sea = TerraGenConfig.seaLevel;
            foreach (var g in Generators(api))
            {
                n++;
                if (!scaledWith.TryGetValue(g, out var box) || box.Value != sea) bad++;
            }
            return (n, bad);
        }
    }
}
