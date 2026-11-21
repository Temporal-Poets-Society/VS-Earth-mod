using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SoilAudit
{
    public class AuditSite { public string Name; public double Lat = double.NaN, Lon = double.NaN; public int Radius = 1; }
    public class AuditConfig
    {
        public bool Enabled = true;
        public int StartDelayMs = 5000;
        public bool WaitForSelfTest = true;
        public bool Shutdown = true;
        public List<AuditSite> Sites = new();
    }

    /// <summary>Test-only (EA-0199): the top block of every land column around sites, by kind (soil by fertility, sand, gravel,
    /// rock, ...). Not part of Earth.</summary>
    public class SoilAuditSystem : ModSystem
    {
        ICoreServerAPI api;
        AuditConfig cfg;
        string outFile;
        string[] kindById;
        object geometry;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 2;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            try { cfg = api.LoadModConfig<AuditConfig>("soilaudit.json"); } catch (Exception e) { Mod.Logger.Error("SOILAUDIT config: {0}", e); }
            if (cfg == null || !cfg.Enabled) return;
            outFile = Path.Combine(GamePaths.DataPath, "soilaudit", "results.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), cfg.StartDelayMs));
        }

        static string Kind(string path)
        {
            if (path.StartsWith("soil-")) { var p = path.Split('-'); return "soil-" + (p.Length > 1 ? p[1] : "?"); }
            if (path.StartsWith("forestfloor")) return "forestfloor";
            if (path.StartsWith("sand")) return "sand";
            if (path.StartsWith("gravel") || path.StartsWith("muddygravel")) return "gravel";
            if (path.StartsWith("rock-")) return "rock";
            if (path.StartsWith("snow") || path.StartsWith("glacierice")) return "snow/ice";
            if (path.StartsWith("peat") || path == "bogpeat") return "peat";
            if (path.StartsWith("rawclay")) return "clay";
            if (path.StartsWith("mud")) return "mud";
            return "other:" + path.Split('-')[0];
        }

        static object Prop(object o, string name) => o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
        static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        void Begin()
        {
            try
            {
                int n = api.World.Blocks.Count;
                kindById = new string[n + 1];
                foreach (var b in api.World.Blocks) if (b?.Code != null && b.Id > 0 && b.Id <= n) kindById[b.Id] = Kind(b.Code.Path);
                var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
                var model = core == null ? null : Prop(core, "Model");
                geometry = model == null ? null : Field(model, "Geometry");
                Mod.Logger.Notification("SOILAUDIT start: {0} sites, earth={1}", cfg.Sites.Count, geometry != null);
            }
            catch (Exception e) { Mod.Logger.Error("SOILAUDIT setup failed: {0}", e); }
            Next(0);
        }

        void Next(int i)
        {
            if (i >= cfg.Sites.Count) { Done(); return; }
            var s = cfg.Sites[i];
            if (geometry == null) { Mod.Logger.Error("SOILAUDIT {0}: no Earth", s.Name); Next(i + 1); return; }
            var args = new object[] { s.Lat, s.Lon, 0.0, 0.0 };
            geometry.GetType().GetMethod("FromGeo").Invoke(geometry, args);
            double x = (double)args[2], z = (double)args[3];
            int ccx = (int)Math.Floor(x / 32), ccz = (int)Math.Floor(z / 32), r = s.Radius;
            int total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            bool done = false;
            void Go()
            {
                if (done) return; done = true;
                try { Analyze(s, x, z, ccx - r, ccz - r, ccx + r, ccz + r); }
                catch (Exception e) { Mod.Logger.Error("SOILAUDIT {0} failed: {1}", s.Name, e); }
                api.Event.RegisterCallback(_ => Next(i + 1), 50);
            }
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                    api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { OnLoaded = () => { loaded++; if (loaded >= total) api.Event.EnqueueMainThreadTask(Go, "soilaudit"); } });
            api.Event.RegisterCallback(_ => { if (!done) { Mod.Logger.Warning("SOILAUDIT {0}: timeout {1}/{2}", s.Name, loaded, total); Go(); } }, 900000);
        }

        void Analyze(AuditSite s, double x, double z, int cx0, int cz0, int cx1, int cz1)
        {
            int H = api.WorldManager.MapSizeY;
            var top = new Dictionary<string, long>(); var under = new Dictionary<string, long>(); var codes = new Dictionary<string, long>();
            var underForest = new Dictionary<string, long>(); var forestDrops = new Dictionary<string, long>(); int dropChecks = 0;
            long land = 0, wet = 0, missing = 0;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var mc = api.WorldManager.GetMapChunk(cx, cz);
                    var chunks = new IServerChunk[H / 32];
                    bool ok = mc != null;
                    for (int cy = 0; cy < chunks.Length && ok; cy++) { chunks[cy] = api.WorldManager.GetChunk(cx, cy, cz); if (chunks[cy] == null) ok = false; else chunks[cy].Unpack(); }
                    if (!ok) { missing++; continue; }
                    for (int lz = 0; lz < 32; lz++)
                        for (int lx = 0; lx < 32; lx++)
                        {
                            int h = Math.Min(H - 2, (int)mc.WorldGenTerrainHeightMap[lz * 32 + lx]);
                            int above = (((h + 1) % 32) * 32 + lz) * 32 + lx;
                            if (chunks[(h + 1) / 32].Data.GetFluid(above) != 0) { wet++; continue; }
                            land++;
                            // the first solid block from a little above the terrain height down
                            int y = Math.Min(H - 1, h + 3), id = 0;
                            for (; y > 0; y--)
                            {
                                id = chunks[y / 32].Data.GetBlockIdUnsafe(((y % 32) * 32 + lz) * 32 + lx);
                                if (id > 0 && id < kindById.Length && kindById[id] != null && !kindById[id].StartsWith("other:tallgrass") && !kindById[id].StartsWith("other:flower")
                                    && !kindById[id].StartsWith("other:fern") && !kindById[id].StartsWith("other:log") && !kindById[id].StartsWith("other:leaves") && !kindById[id].StartsWith("other:snowlayer")) break;
                            }
                            string k = id > 0 && id < kindById.Length ? kindById[id] ?? "?" : "air";
                            top[k] = top.GetValueOrDefault(k) + 1;
                            if (id > 0 && id < kindById.Length) { string c = api.World.Blocks[id].Code.Path; codes[c] = codes.GetValueOrDefault(c) + 1; }
                            int id2 = y > 1 ? chunks[(y - 1) / 32].Data.GetBlockIdUnsafe((((y - 1) % 32) * 32 + lz) * 32 + lx) : 0;
                            string k2 = id2 > 0 && id2 < kindById.Length ? kindById[id2] ?? "?" : "air";
                            under[k2] = under.GetValueOrDefault(k2) + 1;
                            if (k == "forestfloor")
                            {
                                string c2 = id2 > 0 && id2 < kindById.Length ? api.World.Blocks[id2].Code.Path : "air";
                                underForest[c2] = underForest.GetValueOrDefault(c2) + 1;
                                if (dropChecks < 80 && forestDrops.Where(kv => kv.Key.EndsWith("(on " + c2 + ")")).Sum(kv => kv.Value) < 10)
                                {
                                    dropChecks++;
                                    var pos = new Vintagestory.API.MathTools.BlockPos(cx * 32 + lx, y, cz * 32 + lz);
                                    var drops = api.World.Blocks[id].GetDrops(api.World, pos, null);
                                    string d = drops == null || drops.Length == 0 ? "nothing" : string.Join("+", drops.Select(s2 => s2.Collectible.Code.Path));
                                    forestDrops[d + " (on " + c2 + ")"] = forestDrops.GetValueOrDefault(d + " (on " + c2 + ")") + 1;
                                }
                            }
                        }
                }
            var line = new { site = s.Name, lat = s.Lat, lon = s.Lon, x, z, r = s.Radius, land, wet, missing, top, under, underForest, forestDrops, codes = codes.OrderByDescending(p => p.Value).Take(12).ToDictionary(p => p.Key, p => p.Value) };
            File.AppendAllText(outFile, JsonSerializer.Serialize(line) + "\n");
            Mod.Logger.Notification("SOILAUDIT {0}: {1} land columns, {2} wet; top {3}", s.Name, land, wet, string.Join(", ", top.OrderByDescending(p => p.Value).Take(5).Select(p => p.Key + " " + p.Value)));
        }

        void Done()
        {
            Mod.Logger.Notification("SOILAUDIT done");
            if (!cfg.Shutdown) return;
            string report = Path.Combine(GamePaths.DataPath, "EarthMap", "selftest-report.txt");
            int waited = 0;
            void Check()
            {
                if (!cfg.WaitForSelfTest || File.Exists(report) || waited++ > 120) { Mod.Logger.Notification("SOILAUDIT stopping the server"); api.Server.ShutDown(); }
                else api.Event.RegisterCallback(_ => Check(), 5000);
            }
            Check();
        }
    }
}
