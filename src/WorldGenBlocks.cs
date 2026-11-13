using Vintagestory.API.Common;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMap
{
    /// <summary>
    /// The block ids Earth's passes write and the block kinds they read, looked up once per server start (EarthWorldGen's
    /// InitWorldGen, after vanilla's global config): glacier ice, snow, the river ramps' and falls' water, and per block id its
    /// kind (cover, plant), its rock's sand and gravel and whether it is plain rock. Never changed once made.
    /// </summary>
    sealed class WorldGenBlocks
    {
        /// <summary>The flow directions of vanilla's water variants, in FlowDirIndex order.</summary>
        static readonly string[] FlowDirs = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };

        public readonly int GlacierIce, Snow;
        /// <summary>
        /// Flowing water toward n, ne, e, se, s, sw, w, nw, heights 1..6 (index dir * 6 + height - 1): the ramps laid on
        /// the lower pool where a river steps down. Rapid water on rapids.
        /// </summary>
        public readonly int[] Flow, Rapid;
        /// <summary>Falling water (flowing down), for waterfall curtains.</summary>
        public readonly int Fall;
        /// <summary>Pool layout 5 (decision 0024 step 3): sources labelled with the flow toward each direction, ordinary
        /// (a step's edge) and rapid (a chute's step, a curtain's lip); falling rapid water of heights 1..6 (curtains, spills).</summary>
        public readonly int[] Edge, RapidEdge, RapidFall;
        /// <summary>Pool layout 8: the rapid source labelled still, for the surface of a rapids reach (decision 0027).</summary>
        public readonly int RapidSource;

        /// <summary>Per block id: 1 = soil, gravel or sand (surface cover), 2 = plant on top (removed).</summary>
        public readonly byte[] RockKind;
        /// <summary>Per rock block id: the sand and gravel of that rock.</summary>
        public readonly int[] SandOf, GravelOf;
        /// <summary>Per block id: a plain rock (game:rock-*).</summary>
        public readonly bool[] PlainRock;
        public readonly int DefaultSand, DefaultGravel;

        public WorldGenBlocks(IWorldAccessor world, GlobalConfig gcfg)
        {
            GlacierIce = world.GetBlock(new AssetLocation("game:glacierice"))?.Id ?? gcfg.defaultRockId;
            Snow = world.GetBlock(new AssetLocation("game:snowblock"))?.Id ?? 0;
            Fall = world.GetBlock(new AssetLocation("game:water-d-6"))?.Id ?? 0;
            Flow = new int[FlowDirs.Length * 6];
            Rapid = new int[FlowDirs.Length * 6];
            for (int i = 0; i < FlowDirs.Length; i++)
            {
                for (int h = 1; h <= 6; h++)
                {
                    Flow[i * 6 + h - 1] = world.GetBlock(new AssetLocation("game:water-" + FlowDirs[i] + "-" + h))?.Id ?? 0;
                    int rapid = world.GetBlock(new AssetLocation("game:rapidwater-" + FlowDirs[i] + "-" + h))?.Id ?? 0;
                    Rapid[i * 6 + h - 1] = rapid > 0 ? rapid : Flow[i * 6 + h - 1];
                }
            }
            Edge = new int[FlowDirs.Length];
            RapidEdge = new int[FlowDirs.Length];
            for (int i = 0; i < FlowDirs.Length; i++)
            {
                Edge[i] = world.GetBlock(new AssetLocation("game:water-" + FlowDirs[i] + "-7"))?.Id ?? 0;
                RapidEdge[i] = world.GetBlock(new AssetLocation("game:rapidwater-" + FlowDirs[i] + "-7"))?.Id ?? 0;
            }
            RapidSource = world.GetBlock(new AssetLocation("game:rapidwater-still-7"))?.Id ?? 0;
            RapidFall = new int[6];
            for (int h = 1; h <= 6; h++) RapidFall[h - 1] = world.GetBlock(new AssetLocation("game:rapidwater-d-" + h))?.Id ?? 0;

            RockKind = new byte[world.Blocks.Count];
            SandOf = new int[RockKind.Length];
            GravelOf = new int[RockKind.Length];
            PlainRock = new bool[RockKind.Length];
            foreach (var block in world.Blocks)
            {
                if (block == null || block.Id <= 0 || block.Id >= RockKind.Length) continue;
                var m = block.BlockMaterial;
                if (m == EnumBlockMaterial.Soil || m == EnumBlockMaterial.Gravel || m == EnumBlockMaterial.Sand) RockKind[block.Id] = 1;
                else if (m == EnumBlockMaterial.Plant) RockKind[block.Id] = 2;
                if (block.Code != null && block.Code.Domain == "game" && block.Code.Path.StartsWith("rock-"))
                {
                    string rock = block.Code.Path.Substring(5);
                    PlainRock[block.Id] = true;
                    SandOf[block.Id] = world.GetBlock(new AssetLocation("game:sand-" + rock))?.Id ?? 0;
                    GravelOf[block.Id] = world.GetBlock(new AssetLocation("game:gravel-" + rock))?.Id ?? 0;
                }
            }
            DefaultSand = world.GetBlock(new AssetLocation("game:sand-granite"))?.Id ?? 0;
            DefaultGravel = world.GetBlock(new AssetLocation("game:gravel-granite"))?.Id ?? 0;
        }
    }
}
