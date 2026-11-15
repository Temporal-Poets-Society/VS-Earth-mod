using System;
using System.Collections.Concurrent;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Server, every Earth world (EA-0063): light where snow melts away. Vanilla's snow accumulation
    /// (WeatherSimulationSnowAccum) swaps melting snow through a bulk block accessor made without relighting
    /// (GetBlockAccessorBulkMinimalUpdate); a snow block (snowblock, lightAbsorption left at the default, opaque) that melts
    /// to air or a thin snow layer leaves its cell with the sunlight it had as a solid block, none, and the rock around it
    /// lies pitch-black in daylight. Earth's alpine pass lays snow blocks one or two deep in the nival zone, so where the
    /// game's summer is warmer than the zone the melting shows along every snow line (the Matterhorn's ridges).
    /// A postfix on Block.PerformSnowLevelUpdate (no event says a block melted) notes each snow cell whose light absorption
    /// drops; a tick on the main thread, after vanilla's bulk commit, hands it to the game's own relight
    /// (IBlockAccessor.MarkAbsorptionChanged: sunlight spreads down and out from the cell, and the chunks go to the clients).
    /// Patches already dark from before: /earth relight.
    /// </summary>
    public class SnowMeltLight : ModSystem
    {
        const string HarmonyId = "earth.snowmeltlight";

        /// <summary>A melted cell: where, the light absorption it had, and how often it was tried while its chunk was not loaded.</summary>
        readonly record struct Melted(BlockPos Pos, int OldAbsorption, int Tries);

        static readonly ConcurrentQueue<Melted> queue = new();
        static volatile bool on;
        /// <summary>/earth snowlight off: the relight paused for a comparison in game (on again with /earth snowlight on).</summary>
        internal static volatile bool Paused;
        /// <summary>The patch is in (it stays for the process; it does nothing while no Earth world runs).</summary>
        static bool patched;
        /// <summary>At most this many cells wait (a whole snowfield melting at once is relit in turns, the rest dropped).</summary>
        const int MaxQueued = 200_000;
        /// <summary>How often a cell whose chunk is not loaded yet (melted as its chunk loaded) is tried again.</summary>
        const int MaxTries = 20;

        ICoreServerAPI sapi;
        EarthMapSystem earth;
        long tickId;
        bool failed;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            // a pause from /earth snowlight off belongs to the world it was given in (ER-22)
            Paused = false;
            earth = api.ModLoader.GetModSystem<EarthMapSystem>();
            // ER-18: on at once when the params are set (the half-second tick stays: it relights the queue)
            if (earth != null) earth.ParamsReady += _ => Tick();
            api.Event.SaveGameLoaded += () => tickId = api.Event.RegisterGameTickListener(_ => Tick(), 500, 1000);
        }

        public override void Dispose()
        {
            if (sapi != null && tickId != 0) sapi.Event.UnregisterGameTickListener(tickId);
            on = false;
            queue.Clear();
            base.Dispose();
        }

        void Tick()
        {
            try
            {
                bool earthWorld = earth?.Params is { Enabled: true };
                if (earthWorld && !on && !failed)
                {
                    if (!patched)
                    {
                        var pv = PatchVanilla.For(sapi, Mod, new Harmony(HarmonyId));
                        pv.Apply(PatchVanilla.SnowMeltLightTable());
                        if (!pv.On(PatchVanilla.Feature.SnowMeltLight)) { failed = true; return; }
                        patched = true;
                    }
                    on = true;
                }
                else if (!earthWorld) on = false;
                if (on) Relight();
            }
            catch (Exception e)
            {
                failed = true;
                on = false;
                Mod.Logger.Error("Earth Map: relighting melted snow stopped for this session: {0}", e);
            }
        }

        /// <summary>The cells noted since the last tick, through the game's relight (main thread, after vanilla's commit).</summary>
        void Relight()
        {
            var ba = sapi.World.BlockAccessor;
            for (int n = queue.Count; n > 0 && queue.TryDequeue(out var m); n--)
            {
                if (sapi.WorldManager.GetMapChunk(m.Pos.X / GlobalConstants.ChunkSize, m.Pos.Z / GlobalConstants.ChunkSize) == null)
                {
                    if (m.Tries < MaxTries) queue.Enqueue(m with { Tries = m.Tries + 1 });
                    continue;
                }
                int now = ba.GetBlock(m.Pos).GetLightAbsorption(ba, m.Pos);
                if (now < m.OldAbsorption) ba.MarkAbsorptionChanged(m.OldAbsorption, now, m.Pos);
            }
        }

        /// <summary>Postfix on Block.PerformSnowLevelUpdate (the main thread, or a chunk thread as a column loads): a snow block
        /// giving way to a block that lets more light through is noted.</summary>
        internal static void SnowPostfix(Block __instance, BlockPos pos, Block newBlock)
        {
            if (!on || Paused || newBlock == null || pos == null || __instance.BlockMaterial != EnumBlockMaterial.Snow || newBlock.Id == __instance.Id) return;
            if (newBlock.LightAbsorption >= __instance.LightAbsorption || queue.Count >= MaxQueued) return;
            queue.Enqueue(new Melted(pos.Copy(), __instance.LightAbsorption, 0));
        }

        /// <summary>/earth relight [radius]: the game's full relight of the loaded chunks around a place (patches dark from
        /// snow that melted before this fix, or in another session). Returns the box relit.</summary>
        internal static string FullRelight(ICoreServerAPI api, BlockPos at, int radius)
        {
            radius = Math.Clamp(radius, 1, 128);
            int maxY = api.WorldManager.MapSizeY - 1;
            var min = new BlockPos(at.X - radius, Math.Max(0, at.Y - radius), at.Z - radius);
            var max = new BlockPos(at.X + radius, Math.Min(maxY, at.Y + radius), at.Z + radius);
            api.WorldManager.FullRelight(min, max, true);
            return $"{min.X},{min.Y},{min.Z} to {max.X},{max.Y},{max.Z}";
        }
    }
}
