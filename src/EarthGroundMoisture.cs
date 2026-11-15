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
    /// Ranges step 5c, server: dry land greens after rain and goes barren after. On land where step 5a keeps only a share of
    /// vanilla's tall grass, the columns 5a keeps hold their grass all year; on the others a tuft grows while the ground is
    /// wet (<see cref="GroundMoisture"/>, the decayed rain of the past days from Earth's weather) and withers when it has
    /// dried. Driven by vanilla's random soil ticks (BlockSoil.ShouldReceiveServerGameTicks, decided off the main thread;
    /// the block is set in OnServerGameTick), so only loaded land changes, like vanilla's own grass growth. New worlds only
    /// (EarthParams.MoistureRules). In worlds with the 1318 catalogue, the same wet spells bring the catalogue's dry-land
    /// annuals (taxa flagged ephemeral, such as the California poppy) into bloom where their range and climate envelope
    /// allow them, and they die back with the grass.
    /// </summary>
    public class EarthGroundMoisture : ModSystem
    {
        const string HarmonyId = "earth.groundmoisture";
        /// <summary>Ground moisture from which a tuft grows, and below which one withers (GroundMoisture.At).</summary>
        public const double Wet = 0.10, Dry = 0.03;
        /// <summary>Share of vanilla's idle soil ticks that look at the moisture.</summary>
        const double TickChance = 0.3;
        /// <summary>Below this month's mean temperature (°C) grass is dormant: neither flush nor die-back.</summary>
        const double DormantBelow = 5;
        const int CellSize = 64;
        const double BucketHours = 6;

        sealed class MoistureTick { public int BlockId; }
        sealed class Bloom { public PlacementCatalogue.Taxon Taxon; public int BlockId; }

        static volatile EarthGroundMoisture active;
        /// <summary>The patches are in (they stay for the process; they do nothing while no world has the rules on).</summary>
        static bool patched;
        /// <summary>The hooks could not go in this session (said once): off until the next world.</summary>
        bool failed;
        readonly BoundedCache<long, float> cache = new(50_000);
        ICoreServerAPI sapi;
        EarthMapSystem earth;
        int grassVeryShort, grassShort;
        Bloom[] blooms = Array.Empty<Bloom>();
        System.Collections.Generic.HashSet<int> bloomIds = new();
        PlacementCatalogue catalogue;
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

        void Watch()
        {
            var p = earth?.Params;
            bool on = p != null && p.Enabled && p.MoistureRules >= 1 && p.GroundCoverRules >= 1;
            if (on && active != this && !failed)
            {
                if (!Begin())
                {
                    failed = true;
                    return;
                }
                active = this;
                Mod.Logger.Notification("Earth ground moisture rules v{0}: dry land greens after rain (moisture >= {1}) and goes barren after (< {2}); {3} annuals bloom after rain.",
                    p.MoistureRules, Wet, Dry, blooms.Length);
            }
            else if (!on && active == this) active = null;
        }

        /// <summary>
        /// The patches (once per process: PatchVanilla's GroundMoistureTable, both soil ticks checked first, both or neither,
        /// one line naming the member a game update changed), the grass blocks and the annuals that bloom. False, said once,
        /// when they can't be had: the rules are then off for this world. Nothing escapes: the watch runs every 5 s, and an
        /// exception out of a tick listener would abort the rest of that server tick every time.
        /// </summary>
        bool Begin()
        {
            try
            {
                if (!patched)
                {
                    var pv = PatchVanilla.For(sapi, Mod, new Harmony(HarmonyId));
                    pv.Apply(PatchVanilla.GroundMoistureTable());
                    if (!pv.On(PatchVanilla.Feature.GroundMoisture)) return false;
                    patched = true;
                }
                grassVeryShort = sapi.World.GetBlock(new AssetLocation("game:tallgrass-veryshort-free"))?.BlockId ?? 0;
                grassShort = sapi.World.GetBlock(new AssetLocation("game:tallgrass-short-free"))?.BlockId ?? 0;
                FindBlooms();
                return true;
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth Map: dry land greening after rain could not start; dry land keeps its grass all year in this world (ranges step 5c): {0}", e);
                return false;
            }
        }

        /// <summary>The catalogue's ephemeral taxa and the block each blooms as (the first "-free" block under its mapped prefix).</summary>
        void FindBlooms()
        {
            var placement = sapi.ModLoader.GetModSystem<EarthPlacement>();
            catalogue = placement?.Active == true ? placement.Catalogue : null;
            var list = new System.Collections.Generic.List<Bloom>();
            var ids = new System.Collections.Generic.HashSet<int>();
            if (catalogue != null)
                foreach (var m in catalogue.Mappings)
                {
                    if (m.Path != "blockPatch" || m.Prefix == null) continue;
                    var t = System.Linq.Enumerable.FirstOrDefault(catalogue.Taxa, x => x.Id == m.Taxon);
                    if (t?.Ephemeral != true) continue;
                    foreach (var b in sapi.World.Blocks)
                    {
                        if (b?.Code == null || b.Code.Domain != m.Provider || !b.Code.Path.StartsWith(m.Prefix, StringComparison.Ordinal)) continue;
                        ids.Add(b.BlockId);
                        if (b.Code.Path.EndsWith("-free", StringComparison.Ordinal) && !list.Exists(x => x.Taxon == t)) list.Add(new Bloom { Taxon = t, BlockId = b.BlockId });
                    }
                }
            blooms = list.ToArray();
            bloomIds = ids;
        }

        /// <summary>An ephemeral annual whose range and climate envelope admit this column, or 0.</summary>
        int BloomAt(EarthWorldGen gen, BlockPos pos, Random rnd)
        {
            var list = blooms;
            if (list.Length == 0 || catalogue == null) return 0;
            var model = gen.Model;
            var g = model.TerrainGeoAt(pos.X, pos.Z);
            if (g.InGutter || g.BeyondPole) return 0;
            int region = catalogue.RegionAt(g.Lat, g.Lon);
            var cs = model.Data.SampleClimate(g.Lat, g.Lon);
            double lapse = TerrainModel.Lapse(model.Vertical, pos.Y);
            int start = rnd.Next(list.Length);
            for (int k = 0; k < list.Length; k++)
            {
                var b = list[(start + k) % list.Length];
                if (catalogue.Allows(b.Taxon, region, cs.TMean - lapse, cs.TCold - lapse, cs.Rain)) return b.BlockId;
            }
            return 0;
        }

        public override void Dispose()
        {
            if (sapi != null && tickId != 0) sapi.Event.UnregisterGameTickListener(tickId);
            if (active == this) active = null;
            base.Dispose();
        }

        /// <summary>The ground moisture here now, or NaN where there is no weather; cached per 64-block cell and 6 game hours.
        /// <paramref name="temperature"/>: this month's mean at the ground at height <paramref name="y"/> (the weather's normals
        /// are at sea level; cooled by the real lapse rate, so mountain grass goes dormant in its own winter).</summary>
        internal double MoistureAt(int x, int y, int z, double hours, out double temperature)
        {
            temperature = double.NaN;
            // (CR-W5) only while vanilla's rain is Earth's (its patch can be refused on its own): dry land would otherwise green
            // after rain the game never showed
            var weather = EarthWeather.RainPatched ? EarthWeather.For(EnumAppSide.Server)?.Model : null;
            if (weather == null) return double.NaN;
            int cx = (int)Math.Floor(x / (double)CellSize), cz = (int)Math.Floor(z / (double)CellSize);
            long bucket = (long)Math.Floor(hours / BucketHours);
            long key = ((long)(cx & 0xFFFFF) << 44) | ((long)(cz & 0xFFFFF) << 24) | (bucket & 0xFFFFFF);
            double wx = (cx + 0.5) * CellSize, wz = (cz + 0.5) * CellSize;
            temperature = weather.NormalTemp(wx, wz, hours);
            var model = EarthWorldGen.Instance?.Model;
            if (model != null) temperature -= TerrainModel.Lapse(model.Vertical, y);
            if (cache.TryGetValue(key, out float m)) return m;
            // (X-L7, the Fable pass) at the bucket's start, not the first asker's hour: the cached value is the same whoever
            // asked first
            m = (float)GroundMoisture.At(weather, wx, wz, bucket * BucketHours);
            cache.Set(key, m);
            return m;
        }

        internal static void ShouldTickPostfix(BlockSoil __instance, IWorldAccessor world, BlockPos pos, Random offThreadRandom, ref object extra, ref bool __result)
        {
            var self = active;
            if (__result || self == null || offThreadRandom.NextDouble() > TickChance) return;
            if (__instance.Variant["grasscoverage"] is not string cover || cover == "none") return;
            var gen = EarthWorldGen.Instance;
            if (gen?.Active != true) return;
            var ba = world.BlockAccessor;
            var abovePos = pos.UpCopy();
            var above = ba.GetBlock(abovePos);
            bool air = above.BlockId == 0;
            bool tuft = !air && above.Code?.Domain == "game" && above.Code.Path.StartsWith("tallgrass-", StringComparison.Ordinal) && above.Code.Path.EndsWith("-free", StringComparison.Ordinal);
            bool bloom = !air && self.bloomIds.Contains(above.BlockId);
            if (!air && !tuft && !bloom) return;
            if (ba.GetRainMapHeightAt(pos) > pos.Y + 1) return;
            double share = gen.TallGrassShareAt(pos.X, pos.Z);
            if (double.IsNaN(share) || share >= 0.999 || GroundCover.Keeps(pos.X, pos.Z, share)) return;
            // (the rain's own hour, as EarthWeather.HoursNow: TotalDays with any timelapse)
            double m = self.MoistureAt(pos.X, pos.Y + 1, pos.Z, world.Calendar.TotalDays * world.Calendar.HoursPerDay, out double temperature);
            if (double.IsNaN(m) || !(temperature >= DormantBelow)) return;
            if (air && m >= Wet)
            {
                // a wet spell: mostly grass, and the place's annuals in bloom where it has any
                int id = offThreadRandom.NextDouble() < 0.25 ? self.BloomAt(gen, pos, offThreadRandom) : 0;
                if (id == 0) id = offThreadRandom.NextDouble() < 0.6 ? self.grassVeryShort : self.grassShort;
                if (id == 0) return;
                extra = new MoistureTick { BlockId = id };
                __result = true;
            }
            else if ((tuft || bloom) && m < Dry)
            {
                extra = new MoistureTick { BlockId = 0 };
                __result = true;
            }
        }

        internal static bool OnTickPrefix(IWorldAccessor world, BlockPos pos, object extra)
        {
            if (extra is not MoistureTick t) return true;
            var abovePos = pos.UpCopy();
            var above = world.BlockAccessor.GetBlock(abovePos);
            // the world may have changed since the decision off the main thread
            if (t.BlockId != 0 && above.BlockId == 0) world.BlockAccessor.SetBlock(t.BlockId, abovePos);
            else if (t.BlockId == 0 && (above.Code?.Path.StartsWith("tallgrass-", StringComparison.Ordinal) == true || active?.bloomIds.Contains(above.BlockId) == true))
                world.BlockAccessor.SetBlock(0, abovePos);
            return false;
        }
    }
}
