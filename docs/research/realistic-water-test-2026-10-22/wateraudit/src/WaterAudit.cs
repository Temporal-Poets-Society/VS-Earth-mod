using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace WaterAudit
{
    public class Scenario
    {
        /// <summary>river_trench, sea_trench, mill, dam, ramp, wheel</summary>
        public string Kind;
        public string Name;
        public int Width = 2;
        public int Length = 40;
        /// <summary>Trenches: "in" digs from the far end and opens to the water last (all at once); "out" digs away from the water, one row per second.</summary>
        public string Order = "in";
        /// <summary>Mill: where the wheel goes: tunnel (8 blocks down the tunnel), side (beside the fall), under (in the fall).</summary>
        public string WheelAt = "tunnel";
        /// <summary>Candidate sites as [lat, lon]; the first one where the feature is found is used.</summary>
        public List<double[]> Sites = new();
    }

    public class AuditConfig
    {
        public bool Enabled = true;
        public int StartDelayMs = 5000;
        public bool Shutdown = true;
        public int Radius = 3;
        public int Half = 56;
        public int[] DumpAtSec = { 15, 30, 60, 120, 300, 600 };
        public List<Scenario> Scenarios = new();
    }

    /// <summary>
    /// Test-only: finds river/coast features in an Earth world, does what a player would (dig trenches, build a dam, poke a
    /// river step, build a mill pit, place water wheels) and dumps solid and fluid ids around each before and after.
    /// Not part of Earth.
    /// </summary>
    public class WaterAuditSystem : ModSystem
    {
        ICoreServerAPI api;
        AuditConfig cfg;
        string outDir;
        object geometry;
        IBlockAccessor ba => api.World.BlockAccessor;
        int H;
        int unloads;
        int cobble;
        readonly Stopwatch clock = new();
        readonly List<double> tickDts = new();
        readonly List<Dictionary<string, object>> plans = new();
        readonly List<(int x, int z)> used = new();
        readonly List<(string name, BlockPos pos)> wheels = new();
        readonly List<object> wheelLog = new();
        readonly List<object> lagLog = new();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 2;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            try { cfg = api.LoadModConfig<AuditConfig>("wateraudit.json"); } catch (Exception e) { Mod.Logger.Error("WATERAUDIT config: {0}", e); }
            if (cfg == null || !cfg.Enabled) return;
            outDir = Path.Combine(GamePaths.DataPath, "wateraudit");
            Directory.CreateDirectory(outDir);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), cfg.StartDelayMs));
            api.Event.ChunkColumnUnloaded += pos => { if (unloads++ < 50) Log("chunk column unloaded {0},{1} at {2:0}s", pos.X, pos.Z, clock.Elapsed.TotalSeconds); };
        }

        void Log(string fmt, params object[] args) => Mod.Logger.Notification("WATERAUDIT " + fmt, args);

        void Begin()
        {
            H = api.WorldManager.MapSizeY;
            cobble = api.World.GetBlock(new AssetLocation("game:cobblestone-granite")).Id;
            var codes = new Dictionary<int, string>();
            foreach (var b in api.World.Blocks) if (b?.Code != null) codes[b.Id] = b.Code.ToString();
            File.WriteAllText(Path.Combine(outDir, "blocks.json"), JsonSerializer.Serialize(codes));
            var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
            var model = core?.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core);
            geometry = model?.GetType().GetField("Geometry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(model);
            bool rw = api.ModLoader.IsModEnabled("realisticwater"), rr = api.ModLoader.IsModEnabled("realisticrapids");
            File.WriteAllText(Path.Combine(outDir, "world.json"), JsonSerializer.Serialize(new { sea = api.World.SeaLevel, h = H, seed = api.World.Seed, earth = geometry != null, realisticwater = rw, realisticrapids = rr }));
            Log("start: earth={0} realisticwater={1} realisticrapids={2}", geometry != null, rw, rr);
            if (geometry == null) { Finish(); return; }
            var sites = cfg.Scenarios.SelectMany(s => s.Sites).Select(a => (a[0], a[1])).Distinct().ToList();
            LoadSites(sites, 0, () => api.Event.EnqueueMainThreadTask(Locate, "wateraudit"));
        }

        (int x, int z) ToBlock(double lat, double lon)
        {
            var args = new object[] { lat, lon, 0.0, 0.0 };
            geometry.GetType().GetMethod("FromGeo").Invoke(geometry, args);
            return ((int)(double)args[2], (int)(double)args[3]);
        }

        void LoadSites(List<(double lat, double lon)> sites, int i, Action done)
        {
            if (i >= sites.Count) { done(); return; }
            var (x, z) = ToBlock(sites[i].lat, sites[i].lon);
            int ccx = x / 32, ccz = z / 32, r = cfg.Radius, total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            var t0 = DateTime.UtcNow;
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                    api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions
                    {
                        KeepLoaded = true,
                        OnLoaded = () =>
                        {
                            if (++loaded == total)
                            {
                                Log("site {0:0.000},{1:0.000} at {2},{3} loaded in {4:0}s", sites[i].lat, sites[i].lon, x, z, (DateTime.UtcNow - t0).TotalSeconds);
                                api.Event.RegisterCallback(_ => LoadSites(sites, i + 1, done), 50);
                            }
                        }
                    });
        }

        // ---------- block helpers ----------

        Block Fluid(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z), BlockLayersAccess.Fluid);
        Block Solid(int x, int y, int z) => ba.GetBlock(new BlockPos(x, y, z), BlockLayersAccess.SolidBlocks);
        int Terrain(int x, int z) => ba.GetTerrainMapheightAt(new BlockPos(x, 0, z));
        bool Open(int x, int y, int z) { var b = Solid(x, y, z); return b.Id == 0 || b.Replaceable >= 6000; }
        bool SolidTop(int x, int y, int z) => Solid(x, y, z).SideSolid[BlockFacing.UP.Index];

        /// <summary>Highest fluid cell of the column above the terrain, -1 if none.</summary>
        int TopFluid(int x, int z, out Block b)
        {
            int t = Terrain(x, z), y = t + 1;
            b = null;
            while (y < H - 1 && Fluid(x, y, z).IsLiquid()) y++;
            if (y - 1 <= t) return -1;
            b = Fluid(x, y - 1, z);
            return y - 1;
        }

        bool StillAt(int x, int z, int wt, string liquid)
        {
            int y = TopFluid(x, z, out var b);
            return y == wt && b.LiquidLevel == 7 && b.LiquidCode == liquid && Open(x, wt + 1, z);
        }

        /// <summary>Dry column whose top is at least at the water level (Earth's river banks are flush with the water).</summary>
        bool Land(int x, int z, int wt) => Terrain(x, z) >= wt && TopFluid(x, z, out _) < 0;

        IEnumerable<(int x, int z)> Around(int cx, int cz, int r)
        {
            var list = new List<(int, int, int)>();
            for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++) list.Add((dx * dx + dz * dz, cx + dx, cz + dz));
            return list.OrderBy(t => t.Item1).Select(t => (t.Item2, t.Item3));
        }

        bool Free(int x, int z) => used.All(u => Math.Abs(u.x - x) + Math.Abs(u.z - z) >= 48);

        static readonly (int dx, int dz)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        // ---------- locating ----------

        void Locate()
        {
            foreach (var s in cfg.Scenarios.Where(s => s.Kind == "probe"))
                foreach (var site in s.Sites) Probe(site[0], site[1]);
            foreach (var s in cfg.Scenarios.Where(s => s.Kind != "probe"))
            {
                Dictionary<string, object> plan = null;
                foreach (var site in s.Sites)
                {
                    var (cx, cz) = ToBlock(site[0], site[1]);
                    try
                    {
                        plan = s.Kind switch
                        {
                            "river_trench" => FindTrench(cx, cz, "water", s.Length, s.Width),
                            "sea_trench" => FindTrench(cx, cz, "saltwater", s.Length, s.Width),
                            "mill" => FindTrench(cx, cz, "water", 46, 2),
                            "dam" => FindDam(cx, cz),
                            "ramp" => FindRamp(cx, cz),
                            "wheel" => FindRapid(cx, cz),
                            _ => null
                        };
                    }
                    catch (Exception e) { Mod.Logger.Error("WATERAUDIT {0} at {1},{2}: {3}", s.Name, site[0], site[1], e); }
                    if (plan != null) { plan["lat"] = site[0]; plan["lon"] = site[1]; break; }
                    Log("{0}: nothing at {1},{2}", s.Name, site[0], site[1]);
                }
                if (plan == null) { Log("{0}: NOT FOUND", s.Name); continue; }
                plan["name"] = s.Name; plan["kind"] = s.Kind; plan["width"] = s.Width; plan["length"] = s.Kind == "mill" ? 46 : s.Length; plan["order"] = s.Order; plan["wheelAt"] = s.WheelAt;
                used.Add(((int)plan["x"], (int)plan["z"]));
                plans.Add(plan);
                Log("{0}: {1}", s.Name, JsonSerializer.Serialize(plan));
            }
            File.WriteAllText(Path.Combine(outDir, "plan.json"), JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true }));
            if (plans.Count == 0) { Finish(); return; }
            foreach (var p in plans) Dump(p, "t0");
            api.Event.RegisterGameTickListener(dt => tickDts.Add(dt), 20);
            clock.Start();
            foreach (var p in plans)
            {
                try { Apply(p); } catch (Exception e) { Mod.Logger.Error("WATERAUDIT apply {0}: {1}", p["name"], e); }
            }
            Log("ops applied in {0} ms", clock.ElapsedMilliseconds);
            SampleLag("ops");
            foreach (int sec in cfg.DumpAtSec)
            {
                int t = sec;
                api.Event.RegisterCallback(_ =>
                {
                    SampleLag("t" + t);
                    foreach (var p in plans) Dump(p, "t" + t);
                    if (t == cfg.DumpAtSec.Max()) Finish();
                }, t * 1000);
            }
            for (int t = 2; t <= cfg.DumpAtSec.Max(); t += 4) { int tt = t; api.Event.RegisterCallback(_ => SampleWheels(tt), tt * 1000); }
        }

        Dictionary<string, object> FindTrench(int cx, int cz, string liquid, int len, int width)
        {
            foreach (var (x, z) in Around(cx, cz, 48))
            {
                if (!Free(x, z)) continue;
                int wt = TopFluid(x, z, out var b);
                if (wt < 0 || b.LiquidLevel != 7 || b.LiquidCode != liquid || !Open(x, wt + 1, z)) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    int px = -dz, pz = dx;
                    if (Math.Abs(x + dx * len - cx) > 90 || Math.Abs(z + dz * len - cz) > 90) continue;
                    bool ok = true;
                    for (int w = 0; w < width && ok; w++)
                        for (int k = -3; k <= 0 && ok; k++)
                            ok = StillAt(x + dx * k + px * w, z + dz * k + pz * w, wt, liquid);
                    for (int k = 1; k <= len && ok; k++)
                        for (int w = -1; w <= width && ok; w++)
                            ok = Land(x + dx * k + px * w, z + dz * k + pz * w, wt);
                    if (ok) return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["wt"] = wt, ["dx"] = dx, ["dz"] = dz, ["px"] = px, ["pz"] = pz, ["liquid"] = liquid };
                }
            }
            return null;
        }

        Dictionary<string, object> FindDam(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 48))
            {
                if (!Free(x, z)) continue;
                if (!(TopFluid(x, z, out var b) is int wt) || wt < 0 || b.LiquidLevel != 7 || b.LiquidCode != "water") continue;
                foreach (var (ax, az) in new[] { (1, 0), (0, 1) })
                {
                    int lo = 0, hi = 0;
                    while (lo > -60 && StillAt(x + ax * (lo - 1), z + az * (lo - 1), wt, "water")) lo--;
                    while (hi < 60 && StillAt(x + ax * (hi + 1), z + az * (hi + 1), wt, "water")) hi++;
                    if (hi - lo + 1 > 48 || !Land(x + ax * (lo - 1), z + az * (lo - 1), wt) || !Land(x + ax * (hi + 1), z + az * (hi + 1), wt)) continue;
                    // a pool: still water 8 blocks up- and downstream all across
                    int fx = -az, fz = ax;
                    bool ok = true;
                    for (int f = -8; f <= 9 && ok; f++)
                        for (int a = lo; a <= hi && ok; a++)
                        {
                            int qx = x + ax * a + fx * f, qz = z + az * a + fz * f;
                            int y = TopFluid(qx, qz, out var q);
                            ok = y < 0 ? Land(qx, qz, wt) : (y == wt && q.LiquidLevel == 7);
                        }
                    if (ok) return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["wt"] = wt, ["ax"] = ax, ["az"] = az, ["lo"] = lo, ["hi"] = hi, ["fx"] = fx, ["fz"] = fz };
                }
            }
            return null;
        }

        Dictionary<string, object> FindRamp(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || b.LiquidCode != "water" || b.LiquidLevel >= 7 || !b.Code.Path.StartsWith("water-")) continue;
                string flow = b.Variant["flow"];
                if (flow == "still" || flow == "d") continue;
                if (Fluid(x, y - 1, z).LiquidLevel != 7) continue;
                return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y - 1, ["block"] = b.Code.ToString() };
            }
            return null;
        }

        Dictionary<string, object> FindRapid(int cx, int cz)
        {
            foreach (var (x, z) in Around(cx, cz, 80))
            {
                if (!Free(x, z)) continue;
                int y = TopFluid(x, z, out var b);
                if (y < 0 || !b.Code.Path.StartsWith("rapidwater-") || b.LiquidLevel >= 7) continue;
                string flow = b.Variant["flow"];
                int ax, az;
                string side;
                if (flow == "e" || flow == "w") { ax = 1; az = 0; side = "north"; }
                else if (flow == "n" || flow == "s") { ax = 0; az = 1; side = "east"; }
                else continue;
                bool ok = true;
                for (int a = -1; a <= 1 && ok; a++)
                    for (int dy = 0; dy <= 2 && ok; dy++)
                        ok = Open(x + ax * a, y + dy, z + az * a);
                if (!ok) continue;
                return new Dictionary<string, object> { ["x"] = x, ["z"] = z, ["y"] = y, ["wt"] = y, ["block"] = b.Code.ToString(), ["side"] = side, ["wheelY"] = y + 1 };
            }
            return null;
        }

        /// <summary>Per column of ±96 around a site: terrain height, top fluid y and id (int32 each), header (x0, z0, n).</summary>
        void Probe(double lat, double lon)
        {
            var (cx, cz) = ToBlock(lat, lon);
            int r = 96, n = 2 * r + 1;
            using var fs = File.Create(Path.Combine(outDir, $"probe_{lat:0.0000}_{lon:0.0000}.bin.gz"));
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            using var w = new BinaryWriter(gz);
            foreach (int v in new[] { cx - r, cz - r, n }) w.Write(v);
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    int y = TopFluid(x, z, out var b);
                    w.Write(Terrain(x, z)); w.Write(y); w.Write(b?.Id ?? 0);
                }
            Log("probe {0},{1} written", lat, lon);
        }

        // ---------- player actions ----------

        int broken;
        void Break(int x, int y, int z)
        {
            var pos = new BlockPos(x, y, z);
            if (ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id == 0) return;
            ba.BreakBlock(pos, null, 0);
            broken++;
        }

        void Place(int x, int y, int z, int id)
        {
            var pos = new BlockPos(x, y, z);
            ba.SetBlock(id, pos);
            ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
            ba.TriggerNeighbourBlockUpdate(pos);
        }

        void EnsureFloor(int x, int y, int z) { if (!SolidTop(x, y, z)) ba.SetBlock(cobble, new BlockPos(x, y, z)); }

        int I(Dictionary<string, object> p, string k) => (int)p[k];

        void Apply(Dictionary<string, object> p)
        {
            broken = 0;
            int x = I(p, "x"), z = I(p, "z"), wt = I(p, "wt");
            switch ((string)p["kind"])
            {
                case "river_trench":
                case "sea_trench":
                    DigLevel(p, I(p, "length"), I(p, "width"), (string)p["order"] == "out");
                    break;
                case "mill":
                    {
                        int dx = I(p, "dx"), dz = I(p, "dz"), px = I(p, "px"), pz = I(p, "pz");
                        // tunnel 4 high at the bottom of the pit, with a cobblestone ceiling so gravel cannot fall in
                        for (int k = 46; k >= 23; k--)
                            for (int w = 0; w < 2; w++)
                            {
                                int qx = x + dx * k + px * w, qz = z + dz * k + pz * w;
                                EnsureFloor(qx, wt - 15, qz);
                                ba.SetBlock(cobble, new BlockPos(qx, wt - 10, qz));
                                for (int y = wt - 11; y >= wt - 14; y--) Break(qx, y, qz);
                            }
                        for (int k = 22; k >= 21; k--)
                            for (int w = 0; w < 2; w++)
                            {
                                int qx = x + dx * k + px * w, qz = z + dz * k + pz * w;
                                EnsureFloor(qx, wt - 15, qz);
                                for (int y = Terrain(qx, qz) + 3; y >= wt - 14; y--) Break(qx, y, qz);
                            }
                        // tunnel: 8 blocks down the tunnel; side: beside the fall, its rim reaching into the pit; under: in the fall itself
                        string at = (string)p["wheelAt"];
                        int wk = at == "side" ? 23 : at == "under" ? 22 : 31, wy = at == "side" ? wt - 12 : wt - 13;
                        int wx = x + dx * wk, wz = z + dz * wk;
                        PlaceWheel((string)p["name"], wx, wy, wz, dx != 0 ? "north" : "east");
                        p["wheelX"] = wx; p["wheelY"] = wy; p["wheelZ"] = wz;
                        DigLevel(p, 20, 2, false);
                        break;
                    }
                case "dam":
                    {
                        int ax = I(p, "ax"), az = I(p, "az"), fx = I(p, "fx"), fz = I(p, "fz"), lo = I(p, "lo"), hi = I(p, "hi");
                        int placed = 0;
                        for (int f = 0; f <= 1; f++)
                            for (int a = lo - 1; a <= hi + 1; a++)
                            {
                                int qx = x + ax * a + fx * f, qz = z + az * a + fz * f;
                                for (int y = Terrain(qx, qz) + 1; y <= wt + 2; y++) { Place(qx, y, qz, cobble); placed++; }
                            }
                        p["placed"] = placed;
                        break;
                    }
                case "ramp":
                    {
                        int y = I(p, "y");
                        Place(x, y, z, cobble);
                        api.Event.RegisterCallback(_ => { Break(x, y, z); Log("{0}: block removed again", p["name"]); }, 3000);
                        break;
                    }
                case "wheel":
                    PlaceWheel((string)p["name"], x, I(p, "wheelY"), z, (string)p["side"]);
                    break;
            }
            p["broken"] = broken;
            Log("{0}: applied, {1} blocks broken", p["name"], broken);
        }

        /// <summary>Level trench from the far end towards the water, so the last block dug opens it.</summary>
        void DigLevel(Dictionary<string, object> p, int len, int width, bool outward)
        {
            int x = I(p, "x"), z = I(p, "z"), wt = I(p, "wt"), dx = I(p, "dx"), dz = I(p, "dz"), px = I(p, "px"), pz = I(p, "pz");
            void Row(int k)
            {
                for (int w = 0; w < width; w++)
                {
                    int qx = x + dx * k + px * w, qz = z + dz * k + pz * w;
                    EnsureFloor(qx, wt - 1, qz);
                    for (int y = Terrain(qx, qz) + 3; y >= wt; y--) Break(qx, y, qz);
                }
            }
            if (!outward) { for (int k = len; k >= 1; k--) Row(k); return; }
            for (int k = 1; k <= len; k++) { int kk = k; api.Event.RegisterCallback(_ => Row(kk), (k - 1) * 1000); }
        }

        static readonly FieldInfo RccField = typeof(BEBehaviorRightClickConstructable).GetField("rcc", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo FlowRate = typeof(BEBehaviorMPWaterWheel).GetField("flowRate", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo SourceCount = typeof(BEBehaviorMPWaterWheel).GetField("suitablePowerSourceBlockCount", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo Blocked = typeof(BEBehaviorMPWaterWheel).GetField("blocked", BindingFlags.NonPublic | BindingFlags.Instance);

        void PlaceWheel(string name, int x, int y, int z, string side)
        {
            var pos = new BlockPos(x, y, z);
            var block = api.World.GetBlock(new AssetLocation("game:waterwheel-3m-" + side));
            ba.SetBlock(block.Id, pos);
            ba.TriggerNeighbourBlockUpdate(pos);
            var be = ba.GetBlockEntity(pos);
            var rc = be?.GetBehavior<BEBehaviorRightClickConstructable>();
            var rcc = rc == null ? null : (RightClickConstruction)RccField.GetValue(rc);
            if (rcc == null) { Mod.Logger.Error("WATERAUDIT {0}: wheel has no construction behaviour", name); return; }
            rcc.CurrentCompletedStage = rcc.Stages.Length - 1;
            be.MarkDirty(true);
            wheels.Add((name, pos));
            Log("{0}: wheel {1} at {2},{3},{4} complete={5}", name, block.Code, x, y, z, rc.IsComplete);
        }

        void SampleWheels(int t)
        {
            foreach (var (name, pos) in wheels)
            {
                var wb = ba.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPWaterWheel>();
                if (wb == null) { wheelLog.Add(new { t, name, missing = true }); continue; }
                var below = Fluid(pos.X, pos.Y - 1, pos.Z);
                wheelLog.Add(new { t, name, flowRate = (float)FlowRate.GetValue(wb), sources = (int)SourceCount.GetValue(wb), blocked = (bool)Blocked.GetValue(wb), below = below.Code?.ToString() });
            }
        }

        void SampleLag(string tag)
        {
            if (tickDts.Count == 0) { lagLog.Add(new { tag, n = 0 }); return; }
            var d = tickDts.OrderBy(v => v).ToList();
            lagLog.Add(new { tag, n = d.Count, mean = d.Average(), p99 = d[(int)(d.Count * 0.99)], max = d[^1], over100ms = d.Count(v => v > 0.1) });
            tickDts.Clear();
        }

        /// <summary>Layout: int32 header (x0, y0, z0, sx, sy, sz), then per x, z, y: int32 solid id, int32 fluid id.</summary>
        void Dump(Dictionary<string, object> p, string tag)
        {
            int cx = I(p, "x"), cz = I(p, "z"), wt = I(p, "wt"), h = cfg.Half;
            int x0 = cx - h, z0 = cz - h, y0 = Math.Max(1, wt - 20), sx = 2 * h + 1, sz = 2 * h + 1, sy = 34;
            var pos = new BlockPos(0, 0, 0);
            using var fs = File.Create(Path.Combine(outDir, p["name"] + "_" + tag + ".bin.gz"));
            using var gz = new GZipStream(fs, CompressionLevel.Fastest);
            using var w = new BinaryWriter(gz);
            foreach (int v in new[] { x0, y0, z0, sx, sy, sz }) w.Write(v);
            for (int x = x0; x < x0 + sx; x++)
                for (int z = z0; z < z0 + sz; z++)
                    for (int y = y0; y < y0 + sy; y++)
                    {
                        pos.Set(x, y, z);
                        w.Write(ba.GetBlock(pos, BlockLayersAccess.SolidBlocks).Id);
                        w.Write(ba.GetBlock(pos, BlockLayersAccess.Fluid).Id);
                    }
        }

        void Finish()
        {
            File.WriteAllText(Path.Combine(outDir, "plan.json"), JsonSerializer.Serialize(plans, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outDir, "wheels.json"), JsonSerializer.Serialize(wheelLog, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outDir, "lag.json"), JsonSerializer.Serialize(lagLog, new JsonSerializerOptions { WriteIndented = true }));
            Log("done");
            if (cfg.Shutdown) api.Server.ShutDown();
        }
    }
}
