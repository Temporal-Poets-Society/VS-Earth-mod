using System;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace EarthMap
{
    /// <summary>
    /// Server: tall grass lies flat after the snow and grows back in spring (<see cref="GrassWinter"/>). Vanilla swaps a tuft
    /// to its snow-covered variant and back (Block.PerformSnowLevelUpdate with the block from GetSnowCoveredVariant); here
    /// the tuft that comes out of the snow is at most "short". Regrowth rides on vanilla's random soil ticks, like ranges step
    /// 5c (BlockSoil.ShouldReceiveServerGameTicks decides off the main thread, OnServerGameTick sets the block), so only loaded
    /// land changes. The same ticks sprout grass again on bare grassy soil where vanilla's world generation put it
    /// (<see cref="GrassWinter.Covers"/>), so burnt, eaten or cut grass comes back. New worlds only (EarthParams.GrassWinterRules).
    /// </summary>
    public class EarthGrassWinter : ModSystem
    {
        const string HarmonyId = "earth.grasswinter";

        sealed class GrowTick { public int BlockId; public bool Sprout; }

        static volatile EarthGrassWinter active;
        /// <summary>The patches are in (they stay for the process; they do nothing while no world has the rules on).</summary>
        static bool patched;
        /// <summary>The hooks or the tall grass blocks are missing this session (said once): off until the next world.</summary>
        bool failed;
        /// <summary>By block id: the tuft a snow-covered tall grass becomes when its snow melts (0: not tall grass).</summary>
        int[] afterSnow = Array.Empty<int>();
        /// <summary>By block id: the height index of a "-free" tall grass tuft (-1: not one).</summary>
        sbyte[] freeHeight = Array.Empty<sbyte>();
        /// <summary>By height index: the "-free" tuft block.</summary>
        readonly int[] freeTuft = new int[GrassWinter.Heights.Length];
        readonly BoundedCache<long, sbyte> targets = new(200_000);
        ICoreServerAPI sapi;
        EarthMapSystem earth;
        long tickId;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            earth = api.ModLoader.GetModSystem<EarthMapSystem>();
            // ER-18: at once when the params are set, not at the first five-second watch
            if (earth != null) earth.ParamsReady += _ => Watch();
            api.Event.SaveGameLoaded += () => tickId = api.Event.RegisterGameTickListener(_ => Watch(), 5000, 1000);
        }

        public override void Dispose()
        {
            if (sapi != null && tickId != 0) sapi.Event.UnregisterGameTickListener(tickId);
            if (active == this) active = null;
            base.Dispose();
        }

        void Watch()
        {
            var p = earth?.Params;
            bool on = p != null && p.Enabled && p.GrassWinterRules >= 1;
            if (on && active != this && !failed)
            {
                if (!Begin())
                {
                    failed = true;
                    return;
                }
                active = this;
                Mod.Logger.Notification("Earth grass winter rules v{0}: tall grass lies flat after the snow (at most {1}) and grows back from {2} °C.",
                    p.GrassWinterRules, GrassWinter.Heights[GrassWinter.Flattened], GrassWinter.GrowingBase);
            }
            else if (!on && active == this) active = null;
        }

        /// <summary>
        /// The patches (once per process: PatchVanilla's GrassWinterTable, the snow swap and both soil ticks checked first, all
        /// or none, one line naming the member a game update changed) and the tall grass blocks. False, said once, when either
        /// can't be had: the rules are then off for this world. Nothing escapes: the watch runs every 5 s, and an exception out
        /// of a tick listener would abort the rest of that server tick every time.
        /// </summary>
        bool Begin()
        {
            try
            {
                if (!patched)
                {
                    var pv = PatchVanilla.For(sapi, Mod, new Harmony(HarmonyId));
                    pv.Apply(PatchVanilla.GrassWinterTable());
                    if (!pv.On(PatchVanilla.Feature.GrassWinter)) return false;
                    patched = true;
                }
                if (FindTufts()) return true;
                Mod.Logger.Warning("Earth Map: vanilla's tall grass blocks not found; tall grass stays as vanilla keeps it under snow.");
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth Map: tall grass after the snow could not start; tall grass stays as vanilla keeps it under snow in this world: {0}", e);
            }
            return false;
        }

        bool FindTufts()
        {
            var world = sapi.World;
            int n = world.Blocks.Count;
            var after = new int[n];
            var height = new sbyte[n];
            Array.Fill(height, (sbyte)-1);
            for (int i = 0; i < GrassWinter.Heights.Length; i++)
            {
                freeTuft[i] = world.GetBlock(new AssetLocation("game:tallgrass-" + GrassWinter.Heights[i] + "-free"))?.BlockId ?? 0;
                if (freeTuft[i] == 0) return false;
                height[freeTuft[i]] = (sbyte)i;
            }
            foreach (var b in world.Blocks)
            {
                if (b?.Code == null || b.Code.Domain != "game" || !b.Code.Path.StartsWith("tallgrass-", StringComparison.Ordinal)) continue;
                string cover = b.Variant["cover"];
                if (cover == null || !cover.StartsWith("snow", StringComparison.Ordinal)) continue;
                int h = GrassWinter.AfterSnow(GrassWinter.HeightIndex(b.Variant["tallgrass"] ?? ""));
                if (b.BlockId < n) after[b.BlockId] = freeTuft[h];
            }
            afterSnow = after;
            freeHeight = height;
            targets.Clear();
            return true;
        }

        /// <summary>A snow-covered tuft whose snow melts comes back flattened (vanilla would give the same height back).</summary>
        internal static void SnowPrefix(Block __instance, ref Block newBlock)
        {
            var self = active;
            if (self == null || newBlock == null) return;
            int id = __instance.BlockId;
            var after = self.afterSnow;
            if ((uint)id >= (uint)after.Length || after[id] == 0) return;
            // melting: the new block is the free tuft vanilla links to this one ("notSnowCovered")
            if (newBlock.Variant?["cover"] != "free" || newBlock.FirstCodePart() != "tallgrass") return;
            if (newBlock.BlockId == after[id]) return;
            var flat = self.sapi.World.GetBlock(after[id]);
            if (flat != null) newBlock = flat;
        }

        /// <summary>The height vanilla's block layers give tall grass here, -1 none: from the climate, rain and forest maps the
        /// world was made with (GetClimateAt WorldGenValues, as vanilla's own soil growth reads them off the main thread),
        /// cached per 4-block cell and 8-block height band.</summary>
        int TargetAt(IBlockAccessor ba, BlockPos pos)
        {
            long key = ((long)(pos.X >> 2) & 0xFFFFFF) << 40 | ((long)(pos.Z >> 2) & 0xFFFFFF) << 16 | (long)(pos.Y >> 3) & 0xFFFF;
            if (targets.TryGetValue(key, out sbyte t)) return t;
            var c = ba.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            int result = c == null ? -1 : GrassWinter.TargetHeight(c.Temperature, c.WorldgenRainfall, c.ForestDensity);
            targets.Set(key, (sbyte)result);
            return result;
        }

        internal static void ShouldTickPostfix(BlockSoil __instance, IWorldAccessor world, BlockPos pos, Random offThreadRandom, ref object extra, ref bool __result)
        {
            var self = active;
            if (__result || self == null) return;
            var gen = EarthWorldGen.Instance;
            if (gen?.Active != true) return;
            var ba = world.BlockAccessor;
            var abovePos = pos.UpCopy();
            int aboveId = ba.GetBlockId(abovePos);
            var heights = self.freeHeight;
            bool bare = aboveId == 0;
            if (!bare && ((uint)aboveId >= (uint)heights.Length || heights[aboveId] < 0)) return;
            int days = world.Calendar.DaysPerMonth;
            // the dice first, against the most any month gives, and the soil's own state: the weather is looked up only for
            // the ticks that may still grow something (the same draw decides below, against this month's chance)
            double roll = offThreadRandom.NextDouble();
            if (roll > (bare ? GrassWinter.MaxGrowChance(days) / 3 : GrassWinter.MaxGrowChance(days))) return;
            if (bare && (__instance.Variant["grasscoverage"] != "normal" || ba.GetRainMapHeightAt(pos) > pos.Y + 1)) return;
            double temperature = WeatherTemperature(pos);
            double p = GrassWinter.GrowChance(temperature, days);
            if (bare)
            {
                // grass comes back where it was burnt, eaten or cut: about once a month at 10 °C, on open grassy soil where
                // vanilla's world generation put grass (dry land's tufts are ranges step 5c's, after rain)
                if (p <= 0 || roll > p / 3) return;
                double share = gen.TallGrassShareAt(pos.X, pos.Z);
                if (!double.IsNaN(share) && share < 0.999 && !GroundCover.Keeps(pos.X, pos.Z, share)) return;
                var c = ba.GetClimateAt(abovePos, EnumGetClimateMode.WorldGenValues);
                if (c == null || GrassWinter.TargetHeight(c.Temperature, c.WorldgenRainfall, c.ForestDensity) < 0
                    || !GrassWinter.Covers(pos.X, pos.Z, c.ForestDensity, c.WorldgenRainfall, c.Temperature, __instance.Fertility)) return;
                extra = new GrowTick { BlockId = self.freeTuft[0], Sprout = true };
                __result = true;
                return;
            }
            int h = heights[aboveId];
            if (p <= 0 || roll > p) return;
            int target = self.TargetAt(ba, abovePos);
            if (h >= target) return;
            extra = new GrowTick { BlockId = self.freeTuft[h + 1] };
            __result = true;
        }

        /// <summary>This month's mean temperature at the ground (the weather's normals are at sea level; the real lapse
        /// rate cools them to this height); NaN without weather.</summary>
        static double WeatherTemperature(BlockPos pos)
        {
            var weather = EarthWeather.For(EnumAppSide.Server)?.Model;
            var model = EarthWorldGen.Instance?.Model;
            var cal = active?.sapi?.World?.Calendar;
            if (weather == null || model == null || cal == null) return double.NaN;
            return weather.NormalTemp(pos.X, pos.Z, cal.TotalHours) - TerrainModel.Lapse(model.Vertical, pos.Y + 1);
        }

        internal static bool OnTickPrefix(IWorldAccessor world, BlockPos pos, object extra)
        {
            if (extra is not GrowTick t) return true;
            var self = active;
            var abovePos = pos.UpCopy();
            int aboveId = world.BlockAccessor.GetBlockId(abovePos);
            if (t.Sprout)
            {
                if (aboveId == 0) world.BlockAccessor.SetBlock(t.BlockId, abovePos);
                return false;
            }
            // the world may have changed since the decision off the main thread: grow only a free tuft one stage shorter
            if (self != null && (uint)aboveId < (uint)self.freeHeight.Length && self.freeHeight[aboveId] >= 0
                && self.freeHeight[aboveId] + 1 < self.freeTuft.Length && self.freeTuft[self.freeHeight[aboveId] + 1] == t.BlockId)
                world.BlockAccessor.ExchangeBlock(t.BlockId, abovePos);
            return false;
        }
    }
}
