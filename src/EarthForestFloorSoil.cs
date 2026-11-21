using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>
    /// (Decision 0105, EA-0199; the author, 20 November 2026: "Yes, soil beneath") Vanilla's forest floor replaces the top soil
    /// block under trees and always drops low soil (`soil-low-none`), whatever lay there. In a world with Earth soil
    /// (EarthParams.Topsoil) it drops the soil it lies on, bare, in that soil's own grade: Earth's top-block pass gives the
    /// block under the top one the column's grade (BlockLayerAdjustments.TopsoilChunkPostfix), so a forest on black earth gives
    /// black earth and one on podzol barren soil. Where no soil lies beneath, the closest sensible block (the coordinator for
    /// the author, 20 November 2026): clay gives low soil; peat gives the grade the world's tier set gives peat soils
    /// (Histosols: low in vanilla tiers, barren in realistic tiers; breaking peat gives peat bricks, so no peat block comes
    /// from here); rock, sand or gravel give barren soil (a thin, poor humus). Past a block that decides nothing (a loose stone,
    /// another forest floor) it looks further down, up to three blocks; air or water beneath gives vanilla's drop. Climate-only worlds and worlds made before
    /// keep vanilla's drop; the forest floor's own fertility for plants is untouched. The behaviour is added to vanilla's
    /// block by assets/earth/patches/forestfloor-soil.json.
    /// </summary>
    public sealed class EarthForestFloorSoilSystem : ModSystem
    {
        public const string BehaviorName = "EarthForestFloorSoil";
        public override void Start(ICoreAPI api) => api.RegisterBlockBehaviorClass(BehaviorName, typeof(BlockBehaviorEarthForestFloorSoil));
    }

    public sealed class BlockBehaviorEarthForestFloorSoil : BlockBehavior
    {
        public BlockBehaviorEarthForestFloorSoil(Block block) : base(block) { }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropChanceMultiplier, ref EnumHandling handling)
        {
            if (world?.Api?.Side != EnumAppSide.Server || pos == null) return null;
            var p = world.Api.ModLoader.GetModSystem<EarthMapSystem>()?.Params;
            if (p == null || !p.Enabled || !Core.Topsoil.On(p.Topsoil)) return null;
            Block soil = null;
            for (int d = 1; d <= 3 && soil == null; d++)
            {
                var below = world.BlockAccessor.GetBlock(pos.DownCopy(d));
                string grade = GradeBeneath(below?.Code?.Domain == "game" ? below.Code.Path : null, below == null || below.Id == 0 || below.IsLiquid(), p.Topsoil);
                if (grade == null) return null;          // air or water beneath: vanilla's drop
                if (grade.Length == 0) continue;         // not decided by this block (e.g. a loose stone): look deeper
                soil = world.GetBlock(new AssetLocation("game", "soil-" + grade + "-none"));
            }
            if (soil == null) return null;
            handling = EnumHandling.PreventDefault;
            return new[] { new ItemStack(soil) };
        }

        /// <summary>
        /// The soil grade (Core.Topsoil.BlockCodes) a forest floor gives for the block beneath it: a soil block's own grade;
        /// low for clay; for peat the tier set's grade of peat soils (low in vanilla tiers, very low in realistic tiers, as
        /// soil-rules-v4.json's Histosols); very low (barren) for rock, sand and gravel; "" for a block that decides nothing
        /// (look deeper); null for air or water (vanilla's drop). <paramref name="path"/>: the block's code path in the game's
        /// domain, null for another domain's block (treated as rock: barren). <paramref name="tiers"/>: EarthParams.Topsoil.
        /// </summary>
        internal static string GradeBeneath(string path, bool airOrWater, int tiers)
        {
            if (airOrWater) return null;
            if (path == null) return "verylow";
            if (path.StartsWith("soil-"))
            {
                var parts = path.Split('-');
                return parts.Length == 3 && System.Array.IndexOf(Core.Topsoil.BlockCodes, parts[1]) >= 0 ? parts[1] : "low";
            }
            if (path.StartsWith("forestfloor")) return "";
            if (path.StartsWith("rawclay") || path.StartsWith("clay")) return "low";
            if (path.StartsWith("peat") || path == "bogpeat") return tiers == Core.Topsoil.VanillaTiers ? "low" : "verylow";
            if (path.StartsWith("loose") || path.StartsWith("tallgrass") || path.StartsWith("flower")) return "";
            return "verylow";
        }
    }
}
