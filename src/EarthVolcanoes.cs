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
        int lavaId, basaltId, scoriaId, tephraId, mapHeight, rules;
        /// <summary>Geothermal rules 3 (decision 0078): the rock of each part, by magma family (index Geothermal.Magma - 1).</summary>
        Materials[] materials;
        /// <summary>Per block id: 1 a plain rock (game:rock-*), 2 an ore in a rock; and per target rock (index in
        /// <see cref="targets"/>) the same ore in that rock (0: none).</summary>
        byte[] rockKind;
        int[][] oreIn;
        int[] targets;

        sealed class Materials
        {
            public int Flow, Ash, Margin, Cumulate, Sleeve, Plug, Dyke;
        }
        /// <summary>Per block id: ground that fresh ash buries (soil, gravel, sand, rock), and plants on it (removed).</summary>
        bool[] ashable, plant;
        IReadOnlyList<Geothermal.Volcano> bodies;
        /// <summary>Body indices by 512-block cell, each listed in every cell its body may reach.</summary>
        Dictionary<long, List<int>> cells;
        const int CellSize = 512;
        ConcurrentDictionary<int, Lazy<VolcanoPlan>> plans;
        /// <summary>Each body's GVP place in blocks (for /earth volcano).</summary>
        double[] bodyX, bodyZ;
        /// <summary>Each body's farthest reach from its GVP place (blocks), as the cell index has it: its landform's
        /// VolcanoPlan.MaxReach plus the vent search (a plan's Reach from its vent never passes it: VolcanoTests).</summary>
        double[] bodyReach;

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
            // (Fable's re-review of ad56a74) asked by the guards inside vanilla's pond and rivulet passes, which have no net: a
            // body that can't be planned is skipped there, as before; the volcano passes (Guarded) fail on it
            foreach (var plan in PlansNear(bx, bz, failIfUnplanned: false))
                if (plan.InCrater(bx, bz)) return true;
            return false;
        }

        /// <summary>
        /// Whether a crater's carve reaches a column (Crater's footprint: a volcano with a crater, within its reach and two
        /// blocks): there the Terrain pass lowered the height map after the ground pass had run (EarthGeology's rebuild, W-L3).
        /// </summary>
        internal bool CraterCarves(int x, int z)
        {
            if (!active) return false;
            // a body that can't be planned is skipped (the rebuild reads the height map there, as before)
            foreach (var plan in PlansNear(x, z, failIfUnplanned: false))
                if (plan.Crater && plan.Dist(x, z) <= plan.CraterReach + 2) return true;
            return false;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            Instance = this;
            api.Event.InitWorldGenerator(Init, "standard");
            api.Event.GetWorldgenBlockAccessor(provider => accessor = provider.GetBlockAccessor(false));
            // (FP-13) through EarthWorldGen's net: a throw is a failed pass, a chunk in flight after a refusal is noted
            api.Event.ChunkColumnGeneration(EarthWorldGen.Guard(api, "volcano craters and ash", Craters), EnumWorldGenPass.Terrain, "standard");
            api.Event.ChunkColumnGeneration(EarthWorldGen.Guard(api, "volcano interiors", Depths), EnumWorldGenPass.Vegetation, "standard");
            // EarthMapSystem (ExecuteOrder 0.05) has made /earth by now
            api.ChatCommands.GetOrCreate("earth")
                .BeginSubCommand("volcano")
                    .WithDescription("The nearest volcano's body (geothermal rules 2 and 3): its vent, crater, lava lake, chamber and conduit, and the blocks down its vent")
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
            // the map hook holds this instance: let the closed world's model go with it
            if (model != null && model.Carved.Craters != null && model.Carved.Craters.Target == this) model.Carved.Craters = null;
            base.Dispose();
        }

        /// <summary>When a map may plan a volcano again after its planning read a built-in height (Environment.TickCount64).</summary>
        readonly ConcurrentDictionary<int, long> mapRetryAt = new();

        /// <summary>
        /// The surface a crater or lava lake leaves at a block column, for maps (<see cref="TerrainModel.GetSurfaceColumn"/>,
        /// EA-0071, WM-0023); int.MaxValue elsewhere. Any thread. A volcano's plan is the one generation made or makes; a map's
        /// thread plans it itself only from the full heights, as /earth volcano does (CR-D1): never on a whole-world sweep's
        /// thread (FineElevation.BuiltInOnly), and a plan whose planning read a built-in height is not used or kept. Such a
        /// plan (its cone sampled out to tens of kilometres in rules 3) would be wrong, and kept it would make generation carve
        /// a wrong crater and cone.
        /// </summary>
        int MapSurface(int x, int z)
        {
            var index = cells;
            if (!active || index == null || !index.TryGetValue(Key((int)Math.Floor((double)x / CellSize), (int)Math.Floor((double)z / CellSize)), out var list))
                return int.MaxValue;
            int best = int.MaxValue;
            foreach (int i in list)
            {
                if (!bodies[i].HasCrater) continue;
                var plan = MapPlan(i);
                if (plan == null) continue;
                int y = plan.SeenSurface(x, z);
                if (y < best) best = y;
            }
            return best;
        }

        VolcanoPlan MapPlan(int i)
        {
            var made = plans;
            if (made.TryGetValue(i, out var lazy) && lazy.IsValueCreated) return lazy.Value;
            if (FineElevation.BuiltInOnly) return null;
            if (mapRetryAt.TryGetValue(i, out long at) && Environment.TickCount64 < at) return null;
            int missed = FineElevation.MissedReads;
            var plan = MakePlan(i);
            if (plan == null) { mapRetryAt[i] = long.MaxValue; return null; }
            if (FineElevation.MissedReads != missed) { mapRetryAt[i] = Environment.TickCount64 + 60000; return null; }
            return made.GetOrAdd(i, new Lazy<VolcanoPlan>(plan)).Value;
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
            var it = plan.Interior;
            if (it != null)
            {
                sb.AppendFormat(inv, " Interior (rules 3, {0} magma): cone {1}; chamber y {2:0} ({3}-{4}), {5:0} wide; conduit radius {6:0.0} (cupola {7:0.0}, funnel {8:0.0}), {9} to y {10}, top y {11}; {12} dykes{13}{14}. Ash radius {15:0.0}{16}.",
                    it.Magma, it.Edifice ? $"base y {it.BaseY}, radius {it.EdgeR:0}" : "none", it.ChamberY, it.ChamberBottom, it.ChamberTop, 2 * it.A, it.R0, it.RCupola, it.RFunnel,
                    it.Active ? "lava" : "plug", it.Active ? it.ConduitLavaTop : it.PlugTop, it.ConduitTop, it.Dykes.Count,
                    it.Sill ? $", a sill at y {it.SillY} (radius {it.SillR:0})" : "", it.Ring ? $", a ring dyke (radius {it.RingR:0})" : "", plan.AshR, tephraId > 0 ? "" : " (no tephra block)");
            }
            else sb.AppendFormat(inv, " Chamber y {0}, lava {1:0.0} x {2:0.0} (half-width x half-height), body {3:0.0} x {4:0.0}. Conduit radius {5:0.0}, lava to y {6}, rock to y {7}. Ash radius {8:0.0}{9}.",
                plan.ChamberY, plan.LavaA, plan.LavaB, plan.BodyA, plan.BodyB, plan.ConduitR, plan.ConduitLavaTop, plan.ConduitTop, plan.AshR, tephraId > 0 ? "" : " (no tephra block)");
            var ba = sapi.World.BlockAccessor;
            var p = new BlockPos(plan.VentX, Math.Max(1, plan.VentTop), plan.VentZ);
            if (ba.GetChunkAtBlockPos(p) == null)
            {
                sb.AppendFormat(inv, "\nThe vent isn't loaded: /tp ={0} {1} ={2} first.", plan.VentX, plan.VentTop + 8, plan.VentZ);
                return TextCommandResult.Success(sb.ToString());
            }
            int y0 = Math.Max(1, it != null ? it.ChamberBottom - 2 : (int)Math.Floor(plan.ChamberY - plan.BodyB) - 2);
            int y1 = Math.Min(mapHeight - 1, Math.Max(plan.VentTop, plan.Lake ? plan.LakeY : 0) + 3);
            sb.Append(it != null ? "\nDown the conduit (y: blocks):" : "\nDown the vent (y: blocks):");
            string last = null;
            int from = y0;
            for (int y = y0; y <= y1 + 1; y++)
            {
                string code = null;
                if (y <= y1)
                {
                    p.Y = y;
                    // rules 3: down the conduit's own axis, which bows a little
                    if (it != null)
                    {
                        it.Axis(y, out double ox, out double oz);
                        p.X = plan.VentX + (int)Math.Round(ox);
                        p.Z = plan.VentZ + (int)Math.Round(oz);
                    }
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

        /// <summary>Whether <see cref="Init"/> ran to its end in a world with volcano bodies (decision 0083).</summary>
        bool ready;

        /// <summary>(Decision 0083; FP-02) The volcano bodies where this world's rules use them (geothermal rules 2 and later),
        /// not set up this session, for EarthWorldGen's check before the first chunk.</summary>
        internal void FeaturesOff(EarthParams p, List<string> off)
        {
            if (GeothermalFacts.VolcanoBodies(p.GeothermalRules) && !ready) off.Add("the volcano bodies (geothermal rules " + p.GeothermalRules + "; their set-up did not finish, its error is above)");
        }

        void Init()
        {
            ready = false;
            var gen = EarthWorldGen.Instance;
            model = gen != null && gen.Active ? gen.Model : null;
            active = model != null && GeothermalFacts.VolcanoBodies(model.GeothermalRules);
            // (C-L1, code review 14 November 2026) the tables are built in locals and published at the end: on /wgen regen
            // World Map's threads may read them (MapSurface) while this runs again
            var newCells = new Dictionary<long, List<int>>();
            if (!active) { cells = newCells; plans = new ConcurrentDictionary<int, Lazy<VolcanoPlan>>(); return; }
            LoadGlobalConfig(sapi);
            lavaId = gcfg.lavaBlockId;
            basaltId = gcfg.basaltBlockId;
            scoriaId = sapi.World.GetBlock(new AssetLocation("game:rock-scoria"))?.BlockId ?? basaltId;
            // Earth's tephra (the rocks and ground work, 0056); without it no fresh ash
            tephraId = sapi.World.GetBlock(new AssetLocation("earth:tephra"))?.BlockId ?? 0;
            var blocks = sapi.World.Blocks;
            ashable = new bool[blocks.Count];
            plant = new bool[blocks.Count];
            hasEntity = new bool[blocks.Count];
            foreach (var block in blocks)
            {
                if (block == null || block.BlockId <= 0 || block.BlockId >= ashable.Length) continue;
                var mat = block.BlockMaterial;
                ashable[block.BlockId] = block.ForFluidsLayer == false && mat is EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel or EnumBlockMaterial.Sand or EnumBlockMaterial.Stone;
                plant[block.BlockId] = mat == EnumBlockMaterial.Plant;
                hasEntity[block.BlockId] = block.EntityClass != null;
            }
            mapHeight = sapi.WorldManager.MapSizeY;
            var newBodies = Geothermal.Bodies;
            double mpb = model.MetresPerBlock;
            double search = VolcanoPlan.SearchM;
            foreach (var l in Geothermal.LavaLakes) search = Math.Max(search, l.SearchM);
            rules = GeothermalFacts.VolcanoInteriors(model.GeothermalRules) ? 3 : 2;
            if (rules >= 3) InitMaterials();
            int activeCount = 0, lakes = 0;
            var newX = new double[newBodies.Count];
            var newZ = new double[newBodies.Count];
            var newReach = new double[newBodies.Count];
            for (int i = 0; i < newBodies.Count; i++)
            {
                var v = newBodies[i];
                if (v.Active) activeCount++;
                if (v.Lake != null) lakes++;
                double reach = VolcanoPlan.MaxReach(mpb, rules, v.Form) + search / mpb + 2;
                model.Geometry.FromGeo(v.VentLat, v.VentLon, out double x, out double z);
                newX[i] = x;
                newZ[i] = z;
                newReach[i] = reach;
                int ax = (int)Math.Floor((x - reach) / CellSize), bx = (int)Math.Floor((x + reach) / CellSize);
                int az = (int)Math.Floor((z - reach) / CellSize), bz = (int)Math.Floor((z + reach) / CellSize);
                for (int cx = ax; cx <= bx; cx++)
                {
                    for (int cz = az; cz <= bz; cz++)
                    {
                        if (!newCells.TryGetValue(Key(cx, cz), out var list)) newCells[Key(cx, cz)] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
            // publish: the per-body arrays first, then the cells that index them, then fresh plans (plans by body index)
            bodies = newBodies;
            bodyX = newX; bodyZ = newZ; bodyReach = newReach;
            cells = newCells;
            plans = new ConcurrentDictionary<int, Lazy<VolcanoPlan>>();
            // maps see the craters too (TerrainModel.GetSurfaceColumn, EA-0071): World Map drew uncarved cones (WM-0023)
            model.Carved.Craters = MapSurface;
            Mod.Logger.Notification("Earth geothermal rules v{0}: {1} volcanoes with a magma chamber and conduit, {2} of them active in the game's time " +
                "(open lava under a crust, craters in their cones, fresh ash{4}), {3} with a lava lake{5}.", model.GeothermalRules, newBodies.Count, activeCount, lakes,
                tephraId > 0 ? "" : " left out: Earth's tephra block is missing",
                rules >= 3 ? "; interiors of rules 3 (layered cones, lens chambers, conduits with cupola and funnel, dykes and sills, no caves beside them)" : "");
            ready = true;
        }

        /// <remarks>(W-P4) Most chunks and map columns lie in no volcano's cell: the shared empty array, no iterator, there.</remarks>
        IEnumerable<VolcanoPlan> PlansNear(int x0, int z0, bool failIfUnplanned = true) =>
            cells.TryGetValue(Key((int)Math.Floor((double)x0 / CellSize), (int)Math.Floor((double)z0 / CellSize)), out var list)
                ? PlansIn(list, x0, z0, failIfUnplanned) : Array.Empty<VolcanoPlan>();

        System.Func<int, Lazy<VolcanoPlan>> newPlan;

        IEnumerable<VolcanoPlan> PlansIn(List<int> list, int x0, int z0, bool failIfUnplanned)
        {
            var make = newPlan ??= k => new Lazy<VolcanoPlan>(() => MakePlan(k));
            foreach (int i in list)
            {
                // ER-06 (code review 14 Nov 2026): the cell lists a body up to a cell beyond its reach; a chunk out of that reach
                // doesn't force the plan, which reads up to 7,728 model columns 20-40 km out (the same plan whichever chunk
                // makes it, so no chunk changes)
                double reach = bodyReach[i];
                if (bodyX[i] + reach < x0 || bodyX[i] - reach > x0 + 32 || bodyZ[i] + reach < z0 || bodyZ[i] - reach > z0 + 32) continue;
                var lazy = plans.GetOrAdd(i, make);
                VolcanoPlan plan;
                // (a stop during the planning, Fable's re-review of ad56a74: Lazy keeps a thrown exception, so the entry goes)
                try { plan = lazy.Value; }
                catch
                {
                    plans.TryRemove(new KeyValuePair<int, Lazy<VolcanoPlan>>(i, lazy));
                    throw;
                }
                if (plan == null)
                {
                    // (decision 0083, FP-02) a failed pass of this chunk (EarthWorldGen.Guard: noted, the server stops), not a
                    // volcano left out of this session's chunks that the next session carves the rest of; not kept, so a later
                    // chunk plans it again
                    plans.TryRemove(new KeyValuePair<int, Lazy<VolcanoPlan>>(i, lazy));
                    if (!failIfUnplanned) continue;
                    throw new InvalidOperationException("volcano " + bodies[i].Name + " could not be planned (its error is above)");
                }
                if (plan.VentX + plan.Reach < x0 || plan.VentX - plan.Reach > x0 + 32 || plan.VentZ + plan.Reach < z0 || plan.VentZ - plan.Reach > z0 + 32) continue;
                yield return plan;
            }
        }

        VolcanoPlan MakePlan(int i)
        {
            var v = bodies[i];
            try
            {
                var plan = new VolcanoPlan(v, model, rules);
                Mod.Logger.VerboseDebug("Earth volcano {0}: vent at {1},{2} (summit y {3}, vent top {4}), chamber y {5}{6}{7}.", v.Name, plan.VentX, plan.VentZ,
                    plan.SummitY, plan.VentTop, plan.ChamberY, plan.Crater ? $", crater rim y {plan.RimY}" : "", plan.Lake ? $", lava lake y {plan.LakeY}" : "");
                return plan;
            }
            // (Fable's re-review of ad56a74) the server stopping during the planning is no failure of the volcano: it goes on to
            // the pass, which notes the chunk without a refusal (EarthWorldGen.Failed); (Fable's re-review of f55cba9, EA-0099)
            // nor is a GLO-90 square the plan reads that can't be had now: the chunk is left for the next start
            catch (Exception e) when (e is not ServerStoppingException and not TileUnavailableException)
            {
                Mod.Logger.Error("Earth volcano {0} ({1}) could not be planned and is left out: {2}", v.Name, v.Number, e);
                return null;
            }
        }

        /// <summary>(W-L2, code review 14 November 2026) Which blocks carry a block entity (a ruin's chest, a fruiting bush):
        /// a write over one removes the entity first, as vanilla's GenRivulets does, so no orphan entity stays in the chunk.
        /// The blocks written are the same.</summary>
        bool[] hasEntity;
        /// <summary>The chunk column being written (its world x and z at local 0), set by the pass on its own thread.</summary>
        [ThreadStatic] static int writeX0, writeZ0;

        void DropEntity(IServerChunk[] chunks, int lx, int y, int lz, int old)
        {
            var e = hasEntity;
            if (old > 0 && e != null && old < e.Length && e[old]) chunks[y / 32].RemoveBlockEntity(new BlockPos(writeX0 + lx, y, writeZ0 + lz, 0));
        }

        void SetSolid(IServerChunk[] chunks, int lx, int y, int lz, int id)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            DropEntity(chunks, lx, y, lz, data[i]);
            data[i] = id;
            if (data.GetFluid(i) != 0) data.SetFluid(i, 0);
        }

        void SetLava(IServerChunk[] chunks, int lx, int y, int lz)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            DropEntity(chunks, lx, y, lz, data[i]);
            data[i] = 0;
            data.SetFluid(i, lavaId);
        }

        void Clear(IServerChunk[] chunks, int lx, int y, int lz)
        {
            if (y < 1 || y >= mapHeight - 1) return;
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            DropEntity(chunks, lx, y, lz, data[i]);
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
            writeX0 = x0; writeZ0 = z0;
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
            writeX0 = x0; writeZ0 = z0;
            foreach (var plan in PlansNear(x0, z0))
            {
                if (plan.Interior != null) Interior(request.Chunks, plan, x0, z0);
                else Body(request.Chunks, plan, x0, z0);
            }
        }

        int Rock(string code) => sapi.World.GetBlock(new AssetLocation("game:rock-" + code))?.BlockId ?? 0;

        /// <summary>Rules 3: each magma family's rocks (decision 0078), and which blocks are rock or ore in rock.</summary>
        void InitMaterials()
        {
            int Or(int id, int fallback) => id > 0 ? id : fallback;
            int andesite = Or(Rock("andesite"), basaltId), granite = Or(Rock("granite"), andesite), peridotite = Or(Rock("peridotite"), basaltId);
            int tuff = Or(Rock("tuff"), scoriaId), obsidian = Or(Rock("obsidian"), andesite);
            materials = new[]
            {
                new Materials { Flow = basaltId, Ash = scoriaId, Margin = basaltId, Cumulate = peridotite, Sleeve = basaltId, Plug = basaltId, Dyke = basaltId },
                new Materials { Flow = andesite, Ash = tuff, Margin = andesite, Cumulate = andesite, Sleeve = andesite, Plug = andesite, Dyke = andesite },
                new Materials { Flow = andesite, Ash = tuff, Margin = granite, Cumulate = granite, Sleeve = obsidian, Plug = andesite, Dyke = andesite },
            };
            targets = new[] { basaltId, scoriaId, andesite, tuff, granite, peridotite, obsidian };
            var blocks = sapi.World.Blocks;
            rockKind = new byte[blocks.Count];
            oreIn = new int[targets.Length][];
            for (int t = 0; t < targets.Length; t++) oreIn[t] = new int[blocks.Count];
            var targetRocks = new string[targets.Length];
            for (int t = 0; t < targets.Length; t++)
            {
                string path = sapi.World.GetBlock(targets[t])?.Code?.Path;
                targetRocks[t] = path != null && path.StartsWith("rock-", StringComparison.Ordinal) ? path.Substring("rock-".Length) : null;
            }
            foreach (var block in blocks)
            {
                if (block?.Code == null || block.BlockId <= 0 || block.BlockId >= rockKind.Length || block.Code.Domain != "game") continue;
                string path = block.Code.Path;
                if (path.StartsWith("rock-", StringComparison.Ordinal)) { rockKind[block.BlockId] = 1; continue; }
                if (!path.StartsWith("ore-", StringComparison.Ordinal)) continue;
                string rock = block.Variant?["rock"];
                if (string.IsNullOrEmpty(rock) || !path.EndsWith("-" + rock, StringComparison.Ordinal)) continue;
                rockKind[block.BlockId] = 2;
                string stem = path.Substring(0, path.Length - rock.Length);
                for (int t = 0; t < targets.Length; t++)
                {
                    if (targetRocks[t] == null) continue;
                    oreIn[t][block.BlockId] = sapi.World.GetBlock(new AssetLocation("game", stem + targetRocks[t]))?.BlockId ?? 0;
                }
            }
        }

        /// <summary>A rock or ore block made of another rock: plain rock becomes it; an ore keeps its ore in it (or stays as it
        /// was where the game has no such ore in that rock).</summary>
        int InRock(int id, int rock)
        {
            if (rockKind[id] == 1) return rock;
            int t = Array.IndexOf(targets, rock);
            int ore = t >= 0 ? oreIn[t][id] : 0;
            return ore > 0 ? ore : id;
        }

        /// <summary>
        /// Geothermal rules 3 (decision 0078): the cone's own layered rock over its base, then the interior. A cave or water
        /// in the hot zone becomes the rock its column holds next to it, so caves end in their own rock and no lone block of
        /// the interior's rock is left in one.
        /// </summary>
        void Interior(IServerChunk[] chunks, VolcanoPlan plan, int x0, int z0)
        {
            var it = plan.Interior;
            var mat = materials[Math.Clamp((int)it.Magma - 1, 0, 2)];
            var mc = chunks[0].MapChunk;
            ushort[] terrain = mc.WorldGenTerrainHeightMap;
            for (int lz = 0; lz < 32; lz++)
            {
                for (int lx = 0; lx < 32; lx++)
                {
                    int x = x0 + lx, z = z0 + lz;
                    if (plan.Dist(x, z) > it.Reach) continue;
                    var c = it.Prepare(x, z);
                    int top = Math.Min((int)terrain[lz * 32 + lx], mapHeight - 2);
                    if (!double.IsNaN(c.Profile))
                    {
                        bool crater = plan.InCrater(x, z);
                        for (int y = Math.Max(1, it.BaseY + 1); y <= top; y++)
                        {
                            if (crater && y >= top - 1) break;
                            var data = chunks[y / 32].Data;
                            int idx = ((y % 32) * 32 + lz) * 32 + lx;
                            int id = data[idx];
                            if (id <= 0 || id >= rockKind.Length || rockKind[id] == 0) continue;
                            int layer = it.Layer(c, y);
                            if (layer < 0) continue;
                            int to = InRock(id, layer == 0 ? mat.Flow : mat.Ash);
                            if (to != id) data[idx] = to;
                        }
                    }
                    if (!c.Any) continue;
                    int lo = Math.Max(1, it.Bottom), hi = Math.Min(mapHeight - 2, Math.Max(top, it.ConduitTop));
                    for (int y = lo; y <= hi; y++)
                    {
                        var part = it.At(c, y, top);
                        switch (part)
                        {
                            case VolcanoInterior.Part.None: break;
                            case VolcanoInterior.Part.Lava: SetLava(chunks, lx, y, lz); break;
                            case VolcanoInterior.Part.Hot:
                                if (y < top)
                                {
                                    int layer = it.Layer(c, y);
                                    Fill(chunks, lx, y, lz, top, layer == 1 ? mat.Ash : layer == 0 ? mat.Flow : mat.Margin);
                                }
                                break;
                            default:
                                int rock = part switch
                                {
                                    VolcanoInterior.Part.Margin => mat.Margin,
                                    VolcanoInterior.Part.Cumulate => mat.Cumulate,
                                    VolcanoInterior.Part.Sleeve or VolcanoInterior.Part.Crust => mat.Sleeve,
                                    VolcanoInterior.Part.Plug => mat.Plug,
                                    _ => mat.Dyke,
                                };
                                SetSolid(chunks, lx, y, lz, rock);
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>An open block (air, or water or another fluid) under the surface in the hot zone becomes the nearest plain
        /// rock above or below it in its column (within 24 blocks), else <paramref name="fallback"/>. A solid block with a fluid
        /// in it only loses the fluid.</summary>
        void Fill(IServerChunk[] chunks, int lx, int y, int lz, int top, int fallback)
        {
            var data = chunks[y / 32].Data;
            int i = ((y % 32) * 32 + lz) * 32 + lx;
            if (data[i] != 0)
            {
                var b = sapi.World.Blocks[data[i]];
                if (b != null && b.SideSolid.All)
                {
                    if (data.GetFluid(i) != 0) data.SetFluid(i, 0);
                    return;
                }
            }
            int host = fallback;
            for (int k = 1; k <= 24 && host == fallback; k++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    int yy = y + s * k;
                    if (yy < 1 || yy > top || yy >= mapHeight - 1) continue;
                    int id = chunks[yy / 32].Data[((yy % 32) * 32 + lz) * 32 + lx];
                    if (id > 0 && id < rockKind.Length && rockKind[id] == 1) { host = id; break; }
                }
            }
            SetSolid(chunks, lx, y, lz, host);
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
