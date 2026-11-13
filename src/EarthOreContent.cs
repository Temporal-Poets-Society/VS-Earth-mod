using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    public sealed class EarthOreContent : ModSystem
    {
        public override void Start(ICoreAPI api) => api.RegisterBlockClass("EarthBogPeat", typeof(BlockBogPeat));
    }
    /// <summary>
    /// A finite natural ore-bearing peat block (presented like vanilla's buried, grass-free peat; Earth only places it below
    /// the grassed surface layer). It never drops itself, so breaking and re-placing cannot farm bog iron; ordinary placed
    /// peat never acquires ore.
    /// </summary>
    public sealed class BlockBogPeat : Block
    {
        ItemStack[] drops = System.Array.Empty<ItemStack>();
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var stacks = new List<ItemStack>();
            var peat = api.World.GetBlock(new AssetLocation("game:peatbrick"));
            if (peat != null) stacks.Add(new ItemStack(peat, 2));
            var iron = api.World.GetItem(new AssetLocation("game:stone-bogiron"));
            if (iron != null) stacks.Add(new ItemStack(iron, 1));
            drops = stacks.ToArray();
            // Declared drops feed the handbook ("Dropped when broken"); resolved here because the iron item is provider-owned.
            var declared = new BlockDropItemStack[drops.Length];
            for (int i = 0; i < drops.Length; i++) declared[i] = new BlockDropItemStack(drops[i]);
            Drops = declared;
        }
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var result = new ItemStack[drops.Length];
            for (int i = 0; i < drops.Length; i++) result[i] = drops[i].Clone();
            return result;
        }
    }
}
