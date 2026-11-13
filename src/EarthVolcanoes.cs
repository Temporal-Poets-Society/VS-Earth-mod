using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Volcano bodies in Earth worlds of geothermal rules 2 (decision 0057 part 2; Core.VolcanoPlan has the shapes):
    /// - Terrain pass, after Earth's terrain, vanilla's caves and soil (ExecuteOrder 0.71, after EarthWorldGen's 0.7):
    ///   craters of active cones, lined with scoria, and the lava lakes in them, sealed with basalt below the lava's top.
    /// - Vegetation pass, after the rock floor is filled (EarthWorldGen.FillRockFloor, which would otherwise turn a chamber
    ///   below it into plain rock): magma chambers and conduits.
    /// Lava is vanilla's still lava in the fluid layer, as vanilla's caves place it; only a lava lake's top is lit (the
    /// chambers and conduits are sealed, as vanilla's cave lava under its basalt is).
    /// </summary>
    public class EarthVolcanoes : ModStdWorldGen
    {
        ICoreServerAPI sapi;
        IWorldGenBlockAccessor accessor;
        TerrainModel model;
        bool active;
        int lavaId, basaltId, scoriaId, tephraId, mapHeight;
        /// <summary>Per block id: ground that fresh ash buries (soil, gravel, sand, rock), and plants on it (removed).</summary>
        bool[] ashable, plant;
        IReadOnlyList<Geothermal.Volcano> bodies;
        /// <summary>Body indices by 512-block cell, each listed in every cell its body may reach.</summary>
        Dictionary<long, List<int>> cells;
        const int CellSize = 512;
        ConcurrentDictionary<int, Lazy<VolcanoPlan>> plans;
        /// <summary>Each body's GVP place in blocks (for /earth volcano).</summary>
        double[] bodyX, bodyZ;

        public override double ExecuteOrder() => 0.71;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        /// <summary>The server's instance, for EarthWorldGen's guards on vanilla's ponds and springs; null once its world has
        /// closed (<see cref="Dispose"/>).</summary>
        internal static EarthVolcanoes Instance { get; private set; }

        /// <summary>
        /// Whether a column is in a crater (geothermal rules 2) or on its rim. The ground there is hot over open lava: no
        /// vanilla pond fills the crater, and no spring runs down its walls (into a lava lake). Vanilla's ponds had filled
        /// Etna's and Vesuvius's craters, and springs ran down Nyiragongo's walls towards its lake.
        /// </summary>
        internal bool InCrater(double x, double z)
        {
            if (!active) return false;
            int bx = (int)Math.Floor(x), bz = (int)Math.Floor(z);
            foreach (var plan in PlansNear(bx, bz))
                if (plan.InCrater(bx, bz)) return true;
            return false;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Instance = this;
            api.Event.InitWorldGenerator(Init, "standard");
            api.Event.GetWorldgenBlockAccessor(provider => accessor = provider.GetBlockAccessor(false));
            api.Event.ChunkColumnGeneration(Craters, EnumWorldGenPass.Terrain, "standard");
            api.Event.ChunkColumnGeneration(Depths, EnumWorldGenPass.Vegetation, "standard");
            // EarthMapSystem (ExecuteOrder 0.05) has made /earth by now
            api.ChatCommands.GetOrCreate("earth")
                .BeginSubCommand("volcano")
                    .WithDescription("The nearest volcano's body (geothermal rules 2): its vent, crater, lava lake and chamber, and the blocks down its vent")
                    .RequiresPlayer()
                    .HandleWith(CmdVolcano)
                .EndSubCommand();
        }

        /// <summary>The world closes: the static instance lets go of it. A single-player game goes back to the main menu and on
        /// to the next world in the same process; held, this instance kept the closed world's terrain model, volcano plans and
        /// server alive until the next world started.</summary>
        public override void Dispose()
        {
            if (Instance == this) Instance = null;
            base.Dispose();
        }

        TextCommandResult CmdVolcano(TextCommandCallingArgs args)
        {
            if (!active)
                return TextCommandResult.Success("No volcano bodies in this world: its geothermal rules are " + (model?.GeothermalRules ?? 0) + " (bodies come with rules 2, in new worlds).");
            var pos = args.Caller.Pos;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                double dx = bodyX[i] - pos.X, dz = bodyZ[i] - pos.Z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            var v = bodies[best];
            VolcanoPlan plan;
            if (plans.TryGetValue(best, out var made)) plan = made.Value;
            else
            {
                // made here, on the command's thread, which may not wait for a GLO-90 tile: a plan that read a built-in height
                // where generation reads a tile still to come is shown but not kept, so that chunks carve it from generation's
                // own heights (CR-D1)
                int missed = FineElevation.MissedReads;
                plan = MakePlan(best);
                if (plan != null && FineElevation.MissedReads == missed) plan = plans.GetOrAdd(best, new Lazy<VolcanoPlan>(plan)).Value;
            }
            if (plan == null) return TextCommandResult.Error(v.Name + " could not be planned; see the server log.");
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.AppendFormat(inv, "{0} (GVP {1}, {2}, {3}{4}), {5:0} blocks away. Vent ={6} ={7}: summit y {8}, vent top y {9}.", v.Name, v.Number, v.Form,
                v.Active ? "active in the game's time" : "dormant", v.Lake != null ? ", lava lake" : "",
                Math.Sqrt((plan.VentX - pos.X) * (plan.VentX - pos.X) + (plan.VentZ - pos.Z) * (plan.VentZ - pos.Z)), plan.VentX, plan.VentZ, plan.SummitY, plan.VentTop);
            if (plan.Crater) sb.AppendFormat(inv, " Crater radius {0:0.0}, rim y {1}, floor y {2}.", plan.CraterR, plan.RimY, plan.FloorY);
            if (plan.Lake) sb.AppendFormat(inv, " Lava lake radius {0:0.0}, lava y {1}, floor y {2}.", plan.LakeR, plan.LakeY, plan.LakeFloorY);
            sb.AppendFormat(inv, " Chamber y {0}, lava {1:0.0} x {2:0.0} (half-width x half-height), body {3:0.0} x {4:0.0}. Conduit radius {5:0.0}, lava to y {6}, rock to y {7}. Ash radius {8:0.0}{9}.",
                plan.ChamberY, plan.LavaA, plan.LavaB, plan.BodyA, plan.BodyB, plan.ConduitR, plan.ConduitLavaTop, plan.ConduitTop, plan.AshR, tephraId > 0 ? "" : " (no tephra block)");
            var ba = sapi.World.BlockAccessor;
            var p = new BlockPos(plan.VentX, Math.Max(1, plan.VentTop), plan.VentZ);
            if (ba.GetChunkAtBlockPos(p) == null)
            {
                sb.AppendFormat(inv, "\nThe vent isn't loaded: /tp ={0} {1} ={2} first.", plan.VentX, plan.VentTop + 8, plan.VentZ);
                return TextCommandResult.Success(sb.ToString());
            }
            int y0 = Math.Max(1, (int)Math.Floor(plan.ChamberY - plan.BodyB) - 2);
            int y1 = Math.Min(mapHeight - 1, Math.Max(plan.VentTop, plan.Lake ? plan.LakeY : 0) + 3);
            sb.Append("\nDown the vent (y: blocks):");
            string last = null;
            int from = y0;
            for (int y = y0; y <= y1 + 1; y++)
            {
                string code = null;
                if (y <= y1)
                {
                    p.Y = y;
                    var fluid = ba.GetBlock(p, BlockLayersAccess.Fluid);
                    var solid = ba.GetBlock(p, BlockLayersAccess.SolidBlocks);
                    code = fluid != null && fluid.BlockId != 0 ? fluid.Code?.Path : solid?.Code?.Path ?? "?";
                }
                if (code == last) continue;
                if (last != null) sb.AppendFormat(inv, " {0}-{1} {2};", from, y - 1, last);
                last = code;
                from = y;
            }
            return TextCommandResult.Success(sb.ToString());
        }

        static long Key(int cx, int cz) => (long)cx << 32 | (uint)cz;

        void Init()
        {
            var gen = EarthWorldGen.Instance;
            model = gen != null && gen.Active ? gen.Model : null;
            active = model != null && model.GeothermalRules >= 2;
            cells = new Dictionary<long, List<int>>();
            plans = new ConcurrentDictionary<int, Lazy<VolcanoPlan>>();
            if (!active) return;
            LoadGlobalConfig(sapi);
            lavaId = gcfg.lavaBlockId;
            basaltId = gcfg.basaltBlockId;
            scoriaId = sapi.World.GetBlock(new AssetLocation("game:rock-scoria"))?.BlockId ?? basaltId;
            // Earth's tephra (the rocks and ground work, 0056); without it no fresh ash
            tephraId = sapi.World.GetBlock(new AssetLocation("earth:tephra"))?.BlockId ?? 0;
            var blocks = sapi.World.Blocks;
            ashable = new bool[blocks.Count];
            plant = new bool[blocks.Count];
            foreach (var block in blocks)
            {
                if (block == null || block.BlockId <= 0 || block.BlockId >= ashable.Length) continue;
                var mat = block.BlockMaterial;
                ashable[block.BlockId] = block.ForFluidsLayer == false && mat is EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or EnumBlockMaterial.Stone;
                plant[block.BlockId] = mat == EnumBlockMaterial.Plant;
            }
            mapHeight = sapi.WorldManager.MapSizeY;
            bodies = Geothermal.Bodies;
            double mpb = model.MetresPerBlock;
            double search = VolcanoPlan.SearchM;
            foreach (var l in Geothermal.LavaLakes) search = Math.Max(search, l.SearchM);
            double reach = VolcanoPlan.MaxReach(mpb) + search / mpb + 2;
            int activeCount = 0, lakes = 0;
            bodyX = new double[bodies.Count];
            bodyZ = new double[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                var v = bodies[i];
                if (v.Active) activeCount++;
                if (v.Lake != null) lakes++;
                model.Geometry.FromGeo(v.VentLat, v.VentLon, out double x, out double z);
                bodyX[i] = x;
                bodyZ[i] = z;
                int ax = (int)Math.Floor((x - reach) / CellSize), bx = (int)Math.Floor((x + reach) / CellSize);
                int az = (int)Math.Floor((z - reach) / CellSize), bz = (int)Math.Floor((z + reach) / CellSize);
                for (int cx = ax; cx <= bx; cx++)
                {
                    for (int cz = az; cz <= bz; cz++)
                    {
                        if (!cells.TryGetValue(Key(cx, cz), out var list)) cells[Key(cx, cz)] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
            Mod.Logger.Notification("Earth geothermal rules v{0}: {1} volcanoes with a magma chamber and conduit, {2} of them active in the game's time " +
                "(open lava under a crust, craters in their cones, fresh ash{4}), {3} with a lava lake.", model.GeothermalRules, bodies.Count, activeCount, lakes,
                tephraId > 0 ? "" : " left out: Earth's tephra block is missing");
        }

        IEnumerable<VolcanoPlan> PlansNear(int x0, int z0)
        {
            if (!cells.TryGetValue(Key((int)Math.Floor((double)x0 / CellSize), (int)Math.Floor((double)z0 / CellSize)), out var list)) yield break;
            foreach (int i in list)
            {
                var plan = plans.GetOrAdd(i, k => new Lazy<VolcanoPlan>(() => MakePlan(k))).Value;
                if (plan == null) continue;
                if (plan.VentX + plan.Reach < x0 || plan.VentX - plan.Reach > x0 + 32 || plan.VentZ + plan.Reach < z0 || plan.VentZ - plan.Reach > z0 + 32) continue;
                yield return plan;
            }
        }

        VolcanoPlan MakePlan(int i)
        {
            var v = bodies[i];
            try
            {
                var plan = new VolcanoPlan(v, model);
                Mod.Logger.VerboseDebug("Earth volcano {0}: vent at {1},{2} (summit y {3}, vent top {4}), chamber y {5}{6}{7}.", v.Name, plan.VentX, plan.VentZ,
                    plan.SummitY, plan.VentTop, plan.ChamberY, plan.Crater ? $", crater rim y {plan.RimY}" : "", plan.Lake ? $", lava lake y {plan.LakeY}" : "");
                return plan;
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Earth volcano {0} ({1}) could not be planned and is left out: {2}", v.Name, v.Number, e);
                return null;
            }
        }

        void SetSolid(IServerChunk[] chunks, int lx, int y, int lz, int id)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            data[i] = id;
            if (data.GetFluid(i) != 0) data.SetFluid(i, 0);
        }

        void SetLava(IServerChunk[] chunks, int lx, int y, int lz)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            data[i] = 0;
            data.SetFluid(i, lavaId);
        }

        void Clear(IServerChunk[] chunks, int lx, int y, int lz)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            data[i] = 0;
            if (data.GetFluid(i) != 0) data.SetFluid(i, 0);
        }

        /// <summary>Basalt where a block is open (air or a fluid), so lava next to it can't flow out.</summary>
        void Seal(IServerChunk[] chunks, int lx, int y, int lz)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            if (data[i] == 0 || data.GetFluid(i) != 0) SetSolid(chunks, lx, y, lz, basaltId);
        }

        void Craters(IChunkColumnGenerateRequest request)
        {
            if (!active) return;
            int x0 = request.ChunkX * 32, z0 = request.ChunkZ * 32;
            foreach (var plan in PlansNear(x0, z0))
            {
                if (plan.AshR > 0 && tephraId > 0) Ash(request.Chunks, plan, x0, z0);
                if (plan.Crater) Crater(request.Chunks, plan, x0, z0);
            }
        }

        /// <summary>Fresh ash (Earth's tephra) over the ground around an active vent, under any water-free surface of soil,
        /// gravel, sand or rock; the grass on it goes. Ice, snow and water stay.</summary>
        void Ash(IServerChunk[] chunks, VolcanoPlan plan, int x0, int z0)
        {
            var mc = chunks[0].MapChunk;
            ushort[] terrain = mc.WorldGenTerrainHeightMap, rain = mc.RainHeightMap;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    int x = x0 + lx, z = z0 + lz;
                    double d = plan.Dist(x, z);
                    int depth = plan.AshDepth(x, z, d);
                    if (depth <= 0) continue;
                    int i = lz * 32 + lx, top = terrain[i];
                    if (rain[i] > top || top < 2 || top >= mapHeight - 2) continue;
                    var data = chunks[top / 32].Data;
                    int idx = ((top % 32) * 32 + lz) * 32 + lx;
                    int id = data[idx];
                    if (id <= 0 || id >= ashable.Length || !ashable[id] || data.GetFluid(idx) != 0) continue;
                    int above = chunks[(top + 1) / 32].Data[(((top + 1) % 32) * 32 + lz) * 32 + lx];
                    if (above > 0 && above < plant.Length && plant[above]) Clear(chunks, lx, top + 1, lz);
                    for (int k = 0; k < depth; k++)
                    {
                        int y = top - k;
                        int b = chunks[y / 32].Data[((y % 32) * 32 + lz) * 32 + lx];
                        if (b <= 0 || b >= ashable.Length || !ashable[b]) break;
                        SetSolid(chunks, lx, y, lz, tephraId);
                    }
                }
            }
        }

        void Crater(IServerChunk[] chunks, VolcanoPlan plan, int x0, int z0)
        {
            var mc = chunks[0].MapChunk;
            ushort[] terrain = mc.WorldGenTerrainHeightMap, rain = mc.RainHeightMap;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    int x = x0 + lx, z = z0 + lz;
                    double d = plan.Dist(x, z);
                    if (d > plan.CraterReach + 2) continue;
                    int i = lz * 32 + lx;
                    int top = terrain[i], above = Math.Max(top, rain[i]);
                    if (plan.InLake(d))
                    {
                        int floor = plan.LakeFloorY;
                        for (int y = floor - 2; y <= floor; y++) SetSolid(chunks, lx, y, lz, basaltId);
                        for (int y = floor + 1; y <= plan.LakeY; y++) SetLava(chunks, lx, y, lz);
                        for (int y = plan.LakeY + 1; y <= above; y++) Clear(chunks, lx, y, lz);
                        terrain[i] = (ushort)floor;
                        rain[i] = (ushort)plan.LakeY;
                        accessor?.ScheduleBlockLightUpdate(new BlockPos(x, plan.LakeY, z), 0, lavaId);
                        continue;
                    }
                    int surface = plan.CraterSurface(x, z);
                    if (surface < top)
                    {
                        for (int y = surface + 1; y <= above; y++) Clear(chunks, lx, y, lz);
                        for (int y = surface - 1; y <= surface; y++) SetSolid(chunks, lx, y, lz, scoriaId);
                        terrain[i] = (ushort)surface;
                        rain[i] = (ushort)surface;
                    }
                    if (plan.LakeRim(d))
                    {
                        for (int y = plan.LakeFloorY - 2; y <= plan.LakeY; y++) Seal(chunks, lx, y, lz);
                    }
                }
            }
        }

        void Depths(IChunkColumnGenerateRequest request)
        {
            if (!active) return;
            int x0 = request.ChunkX * 32, z0 = request.ChunkZ * 32;
            foreach (var plan in PlansNear(x0, z0)) Body(request.Chunks, plan, x0, z0);
        }

        void Body(IServerChunk[] chunks, VolcanoPlan plan, int x0, int z0)
        {
            bool lavaConduit = plan.ConduitLavaTop >= plan.ChamberY;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    double d = plan.Dist(x0 + lx, z0 + lz);
                    if (d > plan.Reach) continue;
                    double lava = plan.LavaHalf(d), body = plan.BodyHalf(d);
                    if (body >= 0)
                    {
                        for (int y = (int)Math.Ceiling(plan.ChamberY - body); y <= (int)Math.Floor(plan.ChamberY + body); y++)
                        {
                            if (lava >= 0 && Math.Abs(y - plan.ChamberY) <= lava) SetLava(chunks, lx, y, lz);
                            else SetSolid(chunks, lx, y, lz, basaltId);
                        }
                    }
                    if (plan.InConduit(d))
                    {
                        for (int y = plan.ChamberY; y <= plan.ConduitLavaTop; y++) SetLava(chunks, lx, y, lz);
                        for (int y = Math.Max(plan.ChamberY, plan.ConduitLavaTop + 1); y <= plan.ConduitTop; y++) SetSolid(chunks, lx, y, lz, basaltId);
                    }
                    else if (lavaConduit && plan.ConduitSleeve(d))
                    {
                        for (int y = plan.ChamberY; y <= plan.ConduitLavaTop; y++)
                        {
                            if (lava >= 0 && Math.Abs(y - plan.ChamberY) <= lava) continue;
                            SetSolid(chunks, lx, y, lz, basaltId);
                        }
                    }
                }
            }
        }
    }
}
