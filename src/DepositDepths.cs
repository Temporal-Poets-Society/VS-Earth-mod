using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Decision 0097 (EA-0170), deposit depth rules 1: vanilla's deposits at the depth they have in vanilla's standard world,
    /// below the local ground, and their attempts without the world-height factor. In worlds that save the rules
    /// (<see cref="EarthParams.DepositDepthRules"/>: new Earth worlds with vanilla ores, any rock choice).
    ///
    /// Vanilla places a deposit by height rules made for its own terrain, where land stands a little above sea level: a
    /// sea-level deposit (all iron, bituminous coal, anthracite, sulfur, borax, saltpeter, chromite...) at a share of the sea
    /// level, a surface-following one (deep native copper, galena, cassiterite, lignite...) at a share of the column's ground
    /// height, an "anywhere" one (quartz with its gold and silver, olivine) at a share of the world height. Earth keeps the sea
    /// level at 110 at every world height (decision 0045) and has real plateaus, so on ground above about 1,450 m the sea-level
    /// deposits lay 140-640 blocks down, below the rock floor at 1:10 (none left in the Alps, Ethiopia, Tibet or the Atacama;
    /// tall-world-ores-2026-11-03), and the surface-following ones went deeper the higher the ground. With these rules each
    /// lies as deep below its column's ground as it would below a column standing at sea level in vanilla's standard world
    /// (256 high at sea level 110; for another sea level the vanilla world with that sea level): a plateau has the ore of a
    /// lowland within the same reach. Earth's own deposits follow the surface already ("surface below") and are not touched.
    ///
    /// Attempts: vanilla multiplies every height-scaled variant's attempts by world height / 256 to keep ore per block of
    /// rock, because its depth bands grow with the world. These bands no longer do (they are the standard world's), so the
    /// factor is taken out: such a variant gets its standard world's attempts, times sea level / 110 for a world whose sea
    /// level is not 110 (its bands are that much thicker). Before this, lowland ore thickened with world height (1.3-1.6
    /// times per column from 512 to 1024 high, the Dead Sea's salt 8.6 times). Variants vanilla does not height-scale (the
    /// surface starters, Earth's own) keep their attempts per area.
    ///
    /// The prospecting pick reads the same depths: the generators' height ranges (GetYMinMax, which the pick's host count
    /// and child deposits use) are moved the same way, and a reading's amount follows the attempts.
    ///
    /// Not covered: the column's top is the terrain height map's, so under glacier ice the depth counts from the ice's top;
    /// the pick's own simulated deposit pass (ProPickWorkSpace's private GenDeposits) keeps vanilla's attempts, which only
    /// changes how many of its simulated column's blocks are ore rather than host rock.
    /// </summary>
    public sealed class EarthDepositDepths : ModSystem
    {
        internal const string HarmonyId = "earth.depositdepths";
        ICoreServerAPI api;
        EarthMapSystem core;
        Harmony harmony;
        PatchVanilla patches;
        int rules;

        // after EarthVerticalBootstrap (the sea level, at WorldReady) and EarthOres (which sorts the deposit list first)
        public override double ExecuteOrder() => .41;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, InitWorld);
            // again after vanilla's world-generator init (the same variants; a no-op when nothing changed)
            api.Event.InitWorldGenerator(() => { if (DepositDepth.Active) Attempts(api.ModLoader.GetModSystem<GenDeposits>()); }, "standard");
        }

        void InitWorld()
        {
            DepositDepth.Active = false;
            var p = core?.Params;
            if (p?.Enabled != true || p.DepositDepthRules < 1) return;
            rules = p.DepositDepthRules;
            try
            {
                harmony ??= new Harmony(HarmonyId);
                patches ??= PatchVanilla.For(api, Mod, harmony);
                patches.Apply(PatchVanilla.DepositDepthsTable());
                if (!patches.On(PatchVanilla.Feature.DepositDepths)) return;
                DepositDepth.Active = true;
                Attempts(api.ModLoader.GetModSystem<GenDeposits>());
            }
            catch (Exception e)
            {
                DepositDepth.Active = false;
                Mod.Logger.Error("Earth deposit depths are off this session after an unexpected error; vanilla's deposit heights are used. {0}", e);
            }
        }

        void Attempts(GenDeposits gen)
        {
            int n = DepositDepth.TakeOutHeightFactor(gen, TerraGenConfig.seaLevel);
            if (n > 0) Mod.Logger.Notification("Earth deposit depths (rules {0}): {1} deposit(s) at their depth below the local ground, attempts without the world-height factor (sea level {2}).",
                rules, n, TerraGenConfig.seaLevel);
        }

        /// <summary>(Decision 0083) Off where this world's saved rules use it.</summary>
        internal void FeaturesOff(EarthParams p, System.Collections.Generic.List<string> off)
        {
            if (p.DepositDepthRules < 1) return;
            if (!DepositDepth.Active) off.Add("vanilla deposits at their depth below the local ground (deposit depth rules " + p.DepositDepthRules + "; its line is above)");
            else if (patches?.StillInPlace(PatchVanilla.Feature.DepositDepths) is string why) off.Add("vanilla deposits at their depth below the local ground (" + why + ")");
        }

        /// <summary>After /wgen regen: vanilla made new variants with the factor back in.</summary>
        internal static void AfterReload(GenDeposits __instance)
        {
            if (!DepositDepth.Active) return;
            try { DepositDepth.TakeOutHeightFactor(__instance, TerraGenConfig.seaLevel); }
            catch (Exception) { /* the world keeps vanilla's attempts for this reload; generation goes on */ }
        }

        public override void Dispose()
        {
            DepositDepth.Active = false;
            patches?.Release();
            harmony?.UnpatchAll(HarmonyId);
            base.Dispose();
        }
    }

    /// <summary>
    /// The depth rules' arithmetic and the patches on vanilla's generators (<see cref="EarthDepositDepths"/>). Every height a
    /// generator gives is moved by how far the column's ground stands above the sea level; a surface-following one's share is
    /// taken of the standard column (the sea level) instead of the local one; an "anywhere" one's of the standard world's height.
    /// </summary>
    internal static class DepositDepth
    {
        /// <summary>Vanilla's standard world: 256 high at sea level 110.</summary>
        internal const int VanillaSea = 110, VanillaHeight = 256;
        /// <summary>Set for a world with the rules, read by the generation threads; false in every other world of the process.</summary>
        internal static volatile bool Active;

        /// <summary>The height of the vanilla world with this sea level.</summary>
        internal static double StandardHeight(int sea) => sea * (double)VanillaHeight / VanillaSea;

        /// <summary>The lowest height vanilla writes a deposit block at (its disc generator skips y 1 and below).</summary>
        internal const int Bottom = 2;

        /// <summary>A height <paramref name="standardY"/> in the standard world, in a column whose ground stands at
        /// <paramref name="top"/>: lifted by how far that ground stands above the sea level. Below the standard world's bottom
        /// it stays there, so vanilla skips it here as it would there (saltpeter's band reaches below y 0).</summary>
        internal static int Lift(int standardY, int top, int sea) => standardY < Bottom ? standardY : standardY + (top - sea);

        /// <summary>A sea-level deposit's height in a column: vanilla's (its share of the sea level, with its distortion, already
        /// the standard world's), lifted.</summary>
        internal static int Sealevel(int vanillaY, int top, int sea) => Lift(vanillaY, top, sea);

        /// <summary>An "anywhere" deposit's height: its share of the standard world's height instead of this world's
        /// (<paramref name="posyi"/>, vanilla's start height, without distortion), with vanilla's distortion, lifted.</summary>
        internal static int Anywhere(int vanillaY, int posyi, int top, int sea, int mapHeight)
            => Lift((int)(posyi * StandardHeight(sea) / mapHeight) + (vanillaY - posyi), top, sea);

        /// <summary>A surface-following deposit's height: vanilla puts it at its share <paramref name="fraction"/> of the column's
        /// height, then distorts it; here at that share of a standard column (the sea level), with the distortion, lifted: as far
        /// below the ground as the share leaves below a column at sea level.</summary>
        internal static int Surface(int vanillaY, float fraction, int top, int sea)
        {
            int distort = (int)(fraction * (float)top) - vanillaY; // vanilla's own arithmetic, so the distortion comes back exactly
            return Lift((int)(fraction * (float)sea) - distort, top, sea);
        }

        /// <summary>A height range of the standard world, lifted for the pick; a range wholly below the bottom stays as it is.</summary>
        static void LiftRange(ref double miny, ref double maxy, int top, int sea)
        {
            if (maxy < Bottom) return;
            miny = System.Math.Max(miny, Bottom) + (top - sea);
            maxy += top - sea;
        }

        /// <summary>Takes vanilla's world-height factor out of every height-scaled deposit of <paramref name="gen"/> (its
        /// attempts become the standard world's, times sea level / 110), once: a variant done is no longer height-scaled.
        /// Children are attempted per parent block and keep theirs. Returns how many variants changed.</summary>
        internal static int TakeOutHeightFactor(GenDeposits gen, int sea)
        {
            if (gen?.Deposits == null) return 0;
            double f = sea / (double)VanillaSea;
            int n = 0;
            foreach (var v in gen.Deposits)
            {
                if (v == null || !v.ScaleWithWorldheight) continue;
                v.ScaleWithWorldheight = false;
                if (f != 1)
                {
                    v.TriesPerChunk = (float)(v.TriesPerChunk * f);
                    // what the pick reads: vanilla computed it from the attempts at Init
                    if (v.GeneratorInst is DiscDepositGenerator disc) disc.absAvgQuantity *= f;
                }
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ the patches (PatchVanilla.DepositDepthsTable)

        static int Top(IMapChunk chunk, int lx, int lz) => chunk.WorldGenTerrainHeightMap[lz * 32 + lx];

        internal static void SealevelColumn(IMapChunk heremapchunk, int lx, int lz, BlockPos pos)
        {
            if (!Active) return;
            pos.Y = Sealevel(pos.Y, Top(heremapchunk, lx, lz), TerraGenConfig.seaLevel);
        }

        internal static void AnywhereColumn(DepositGeneratorBase __instance, IMapChunk heremapchunk, int lx, int lz, BlockPos targetPos, int ___posyi)
        {
            if (!Active) return;
            targetPos.Y = Anywhere(targetPos.Y, ___posyi, Top(heremapchunk, lx, lz), TerraGenConfig.seaLevel, __instance.Api.WorldManager.MapSizeY);
        }

        internal static void SurfaceColumn(IMapChunk heremapchunk, int lx, int lz, BlockPos pos, float ___ypos)
        {
            if (!Active) return;
            pos.Y = Surface(pos.Y, ___ypos, Top(heremapchunk, lx, lz), TerraGenConfig.seaLevel);
        }

        // The pick's height ranges, at the probed column's ground (the terrain map height its reading divides by too).
        static int Top(DepositGeneratorBase g, BlockPos pos) => g.Api.World.BlockAccessor.GetTerrainMapheightAt(pos);

        internal static void SealevelRange(DepositGeneratorBase __instance, BlockPos pos, ref double miny, ref double maxy)
        {
            if (!Active) return;
            LiftRange(ref miny, ref maxy, Top(__instance, pos), TerraGenConfig.seaLevel);
        }

        internal static void AnywhereRange(DepositGeneratorBase __instance, BlockPos pos, ref double miny, ref double maxy)
        {
            if (!Active) return;
            int sea = TerraGenConfig.seaLevel;
            double scale = StandardHeight(sea) / __instance.Api.WorldManager.MapSizeY;
            miny *= scale; maxy *= scale;
            LiftRange(ref miny, ref maxy, Top(__instance, pos), sea);
        }

        internal static void SurfaceRange(DepositGeneratorBase __instance, BlockPos pos, ref double miny, ref double maxy)
        {
            if (!Active) return;
            // vanilla: from the smallest share of pos.Y up to pos.Y; here the same shares of a standard column
            int sea = TerraGenConfig.seaLevel;
            double share = pos.Y > 0 ? miny / pos.Y : 0;
            miny = share * sea; maxy = sea;
            LiftRange(ref miny, ref maxy, Top(__instance, pos), sea);
        }
    }
}
