using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace OreAudit
{
    public class AuditSite { public string Name; public string Kind; public double Lat = double.NaN, Lon = double.NaN; public double X = double.NaN, Z = double.NaN; public int Radius = 2; }
    public class AuditConfig
    {
        public bool Enabled = true;
        public int StartDelayMs = 5000;
        public bool WaitForSelfTest = false;
        public bool Shutdown = true;
        public List<AuditSite> Sites = new();
    }

    /// <summary>Test-only: counts placed deposit blocks around sites, with attempt factors and prospecting readings. Not part of Earth.</summary>
    public class OreAuditSystem : ModSystem
    {
        ICoreServerAPI api;
        AuditConfig cfg;
        string outFile;
        string[] famById;   // family per block id (null = not a deposit block)
        bool[] looseById;
        bool[] solidRock;
        static readonly HashSet<string> Grades = new() { "poor", "medium", "rich", "bountiful", "low", "high", "*" };
        static readonly int[] Bins = { 0, 5, 10, 20, 40, 80, 160, 100000 };
        object earthCore, earthOres, earthSession, geometry, catalog;
        bool selfTestDone;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 2;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            try { cfg = api.LoadModConfig<AuditConfig>("oreaudit.json"); } catch (Exception e) { Mod.Logger.Error("OREAUDIT config: {0}", e); }
            if (cfg == null || !cfg.Enabled) return;
            outFile = Path.Combine(GamePaths.DataPath, "oreaudit", "results.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), cfg.StartDelayMs));
        }

        static string Family(string path)
        {
            if (path.StartsWith("ore-"))
            {
                var p = path.Split('-');
                if (p.Length >= 3 && Grades.Contains(p[1])) return p[2];
                return p.Length >= 2 ? p[1] : path;
            }
            if (path.StartsWith("rawclay-")) { var p = path.Split('-'); return "clay-" + (p.Length > 1 ? p[1] : "?"); }
            if (path.StartsWith("peat-")) return "peat";
            if (path == "bogpeat") return "bogpeat";
            if (path.StartsWith("saltpeter-")) return "saltpeter";
            if (path == "rock-halite") return "halite";
            if (path == "rock-alum") return "alum";
            if (path == "rock-bauxite") return "bauxite";
            if (path == "rock-kimberlite") return "kimberlite";
            return null;
        }
        static string LooseFamily(string path)
        {
            if (!path.StartsWith("looseores-")) return null;
            var p = path.Split('-');
            return p.Length > 1 ? p[1] : path;
        }

        void Index()
        {
            int n = api.World.Blocks.Count;
            famById = new string[n + 1]; looseById = new bool[n + 1]; solidRock = new bool[n + 1];
            foreach (var b in api.World.Blocks)
            {
                if (b?.Code == null || b.Id <= 0 || b.Id > n) continue;
                string path = b.Code.Path;
                famById[b.Id] = Family(path);
                looseById[b.Id] = LooseFamily(path) != null;
                solidRock[b.Id] = path.StartsWith("rock-") || famById[b.Id] != null && path.StartsWith("ore-");
                if (looseById[b.Id]) famById[b.Id] = "loose:" + LooseFamily(path);
            }
        }

        void Begin()
        {
            try
            {
                Index();
                earthCore = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
                earthOres = api.ModLoader.GetModSystem("EarthMap.EarthOres");
                if (earthCore != null)
                {
                    var model = Prop(earthCore, "Model");
                    geometry = model == null ? null : Field(model, "Geometry");
                }
                if (earthOres != null)
                {
                    earthSession = earthOres.GetType().GetField("current", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(earthOres);
                    catalog = earthSession == null ? null : Field(earthSession, "Catalog");
                }
                Mod.Logger.Notification("OREAUDIT start: {0} sites, earth={1}, ores session={2}", cfg.Sites.Count, geometry != null, earthSession != null);
                WriteHeader();
            }
            catch (Exception e) { Mod.Logger.Error("OREAUDIT setup failed: {0}", e); }
            Next(0);
        }

        static object Prop(object o, string name) => o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
        static object Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
        static object Member(object o, string name) => Prop(o, name) ?? Field(o, name);

        void WriteHeader()
        {
            var gen = api.ModLoader.GetModSystem<GenDeposits>();
            var variants = new List<object>();
            void Add(DepositVariant v, string parent)
            {
                string place = null;
                try { place = v.Attributes?["placeblock"]?["code"]?.AsString(); } catch { }
                variants.Add(new
                {
                    code = v.Code, parent, generator = v.GeneratorInst?.GetType().Name, tries = v.TriesPerChunk, withOreMap = v.WithOreMap,
                    scaleWithHeight = v.ScaleWithWorldheight, placeblock = place, family = place == null ? null : Family(new AssetLocation(place).Path),
                    file = v.fromFile
                });
                foreach (var c in v.ChildDeposits ?? Array.Empty<DepositVariant>()) Add(c, v.Code);
            }
            foreach (var v in gen.Deposits) Add(v, null);
            var bindings = new List<object>();
            if (earthSession != null)
                foreach (var b in (System.Collections.IEnumerable)Field(earthSession, "Bindings"))
                {
                    var v = (DepositVariant)Prop(b, "Variant");
                    bindings.Add(new { code = v.Code, resource = Prop(b, "Resource"), cls = Prop(b, "Class")?.ToString(), child = (bool)Prop(b, "IsChild"), baseMean = (double)Prop(b, "BaseMean"), tries = v.TriesPerChunk, mapped = (bool)Prop(b, "NativelyMapped") });
                }
            Append(new { header = true, world = new { x = api.WorldManager.MapSizeX, y = api.WorldManager.MapSizeY, z = api.WorldManager.MapSizeZ, sea = api.World.SeaLevel, seed = api.World.Seed }, variants, bindings });
        }

        void Append(object o)
        {
            File.AppendAllText(outFile, JsonSerializer.Serialize(o, new JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }) + "\n");
        }

        void Next(int i)
        {
            if (i >= cfg.Sites.Count) { Done(); return; }
            var s = cfg.Sites[i];
            double x = s.X, z = s.Z;
            if (!double.IsNaN(s.Lat))
            {
                if (geometry == null) { Mod.Logger.Error("OREAUDIT {0}: lat/lon without Earth", s.Name); Next(i + 1); return; }
                var args = new object[] { s.Lat, s.Lon, 0.0, 0.0 };
                geometry.GetType().GetMethod("FromGeo").Invoke(geometry, args);
                x = (double)args[2]; z = (double)args[3];
            }
            int ccx = (int)Math.Floor(x / 32), ccz = (int)Math.Floor(z / 32), r = s.Radius;
            int total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            bool done = false;
            var t0 = DateTime.UtcNow;
            void Go()
            {
                if (done) return; done = true;
                try { Analyze(s, x, z, ccx - r, ccz - r, ccx + r, ccz + r, (DateTime.UtcNow - t0).TotalSeconds, loaded, total); }
                catch (Exception e) { Mod.Logger.Error("OREAUDIT {0} failed: {1}", s.Name, e); }
                api.Event.RegisterCallback(_ => Next(i + 1), 50);
            }
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                    api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { OnLoaded = () => { loaded++; if (loaded >= total) api.Event.EnqueueMainThreadTask(Go, "oreaudit"); } });
            api.Event.RegisterCallback(_ => { if (!done) { Mod.Logger.Warning("OREAUDIT {0}: timeout {1}/{2}", s.Name, loaded, total); Go(); } }, 600000);
        }

        sealed class Fam { public long n, land, sea, above, exposed, inFluid; public long[] depth = new long[Bins.Length - 1]; public int minY = int.MaxValue, maxY = int.MinValue; public long sumDepth; }

        void Analyze(AuditSite s, double x, double z, int cx0, int cz0, int cx1, int cz1, double secs, int loaded, int total)
        {
            int H = api.WorldManager.MapSizeY, sea = api.World.SeaLevel;
            var fams = new Dictionary<string, Fam>();
            long columns = 0, landCols = 0, seaCols = 0, sumTop = 0, rock = 0, missing = 0, sumTopLand = 0;
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
                            int top = Math.Min(H - 1, (int)mc.WorldGenTerrainHeightMap[lz * 32 + lx]);
                            columns++; sumTop += top;
                            // water over the terrain surface = sea/lake column
                            int ti = (((top + 1) % 32) * 32 + lz) * 32 + lx;
                            bool wet = top + 1 < H && chunks[(top + 1) / 32].Data.GetFluid(ti) != 0;
                            if (wet) seaCols++; else { landCols++; sumTopLand += top; }
                            for (int y = 1; y < H; y++)
                            {
                                var d = chunks[y / 32].Data;
                                int idx = ((y % 32) * 32 + lz) * 32 + lx;
                                int id = d.GetBlockIdUnsafe(idx);
                                if (id <= 0 || id >= famById.Length) continue;
                                if (y <= top && solidRock[id]) rock++;
                                string f = famById[id];
                                if (f == null) continue;
                                if (!fams.TryGetValue(f, out var a)) fams[f] = a = new Fam();
                                a.n++;
                                if (wet) a.sea++; else a.land++;
                                int depth = top - y;
                                if (y > top) a.above++;
                                if (depth >= 0) { a.sumDepth += depth; for (int k = 0; k < Bins.Length - 1; k++) if (depth >= Bins[k] && depth < Bins[k + 1]) { a.depth[k]++; break; } }
                                a.minY = Math.Min(a.minY, y); a.maxY = Math.Max(a.maxY, y);
                                if (d.GetFluid(idx) != 0) a.inFluid++;
                                if (y + 1 < H && !f.StartsWith("loose:"))
                                {
                                    int up = (((y + 1) % 32) * 32 + lz) * 32 + lx;
                                    var du = chunks[(y + 1) / 32].Data;
                                    if (du.GetBlockIdUnsafe(up) == 0 || du.GetFluid(up) != 0) a.exposed++;
                                }
                            }
                        }
                }
            // attempt factors per variant over the sampled chunks (Earth's patched factor where bound; vanilla's ore map otherwise)
            var gen = api.ModLoader.GetModSystem<GenDeposits>();
            var factors = new Dictionary<string, double[]>(); // key -> [sum tries*factor*heightscale, sum factor, chunks]
            float hs = H / 256f;
            int vi = 0;
            foreach (var v in gen.Deposits)
            {
                double sumA = 0, sumF = 0; int nch = 0;
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        double f = v.WithOreMap ? v.GetOreMapFactor(cx, cz) : 1;
                        sumF += f; sumA += v.TriesPerChunk * f * (v.ScaleWithWorldheight ? hs : 1); nch++;
                    }
                factors[vi++ + ":" + v.Code] = new[] { sumA, sumF, nch };
            }
            // Earth's own view: attempts ratio vs vanilla per bound variant (AttemptFactor / BaseMean) and evidence
            var earthRatio = new Dictionary<string, double>();
            var evidence = new Dictionary<string, object>();
            double lat = double.NaN, lon = double.NaN;
            if (earthSession != null)
            {
                var af = earthSession.GetType().GetMethod("AttemptFactor");
                int bi = 0;
                foreach (var b in (System.Collections.IEnumerable)Field(earthSession, "Bindings"))
                {
                    var v = (DepositVariant)Prop(b, "Variant");
                    double bm = (double)Prop(b, "BaseMean"), sum = 0; int nch = 0;
                    for (int cx = cx0; cx <= cx1; cx++) for (int cz = cz0; cz <= cz1; cz++) { sum += (double)af.Invoke(earthSession, new object[] { b, cx, cz }) / bm; nch++; }
                    earthRatio[bi++ + ":" + v.Code + ":" + Prop(b, "Class")] = sum / nch;
                }
                var g = geometry.GetType().GetMethod("ToGeo").Invoke(geometry, new object[] { x, z });
                lat = (double)Member(g, "Lat"); lon = (double)Member(g, "Lon");
                var at = catalog.GetType().GetMethod("At");
                foreach (string code in ((System.Collections.IDictionary)Prop(catalog, "Layers")).Keys)
                {
                    var e = at.Invoke(catalog, new object[] { code, lat, lon, false });
                    double obs = Convert.ToDouble(Member(e, "Observed")), pot = Convert.ToDouble(Member(e, "Potential")), deep = Convert.ToDouble(Member(e, "Deep"));
                    if (obs > 0 || pot > 0 || deep > 0) evidence[code] = new { obs, pot, deep, minM = Convert.ToDouble(Member(e, "MinimumMetres")) };
                }
            }
            // prospecting pick readings: site centre plus the centres of the 4 quadrants
            var propick = new List<object>();
            try
            {
                var pick = api.World.Items.FirstOrDefault(it => it is ItemProspectingPick);
                var m = typeof(ItemProspectingPick).GetMethod("GenProbeResults", BindingFlags.NonPublic | BindingFlags.Instance);
                int span = (cx1 - cx0 + 1) * 32 / 4;
                foreach (var (dx, dz) in new[] { (0, 0), (-span, -span), (span, -span), (-span, span), (span, span) })
                {
                    var pos = new BlockPos((int)x + dx, 1, (int)z + dz);
                    var reading = m.Invoke(pick, new object[] { api.World, pos }) as PropickReading;
                    if (reading == null) continue;
                    propick.Add(new { x = pos.X, z = pos.Z, readings = reading.OreReadings.ToDictionary(p => p.Key, p => new[] { p.Value.PartsPerThousand, p.Value.TotalFactor }) });
                }
            }
            catch (Exception e) { Mod.Logger.Error("OREAUDIT propick {0}: {1}", s.Name, e); }
            Append(new
            {
                site = s.Name, kind = s.Kind, lat, lon, x, z, r = s.Radius, secs, loaded, total, missing, columns, landCols, seaCols, sumTop, sumTopLand, rock,
                families = fams.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => new { p.Value.n, p.Value.land, p.Value.sea, p.Value.above, p.Value.exposed, p.Value.inFluid, p.Value.depth, minY = p.Value.n > 0 ? p.Value.minY : -1, maxY = p.Value.n > 0 ? p.Value.maxY : -1, meanDepth = p.Value.n > 0 ? p.Value.sumDepth / (double)p.Value.n : -1 }),
                factors, earthRatio, evidence, propick
            });
            Mod.Logger.Notification("OREAUDIT {0}: {1} columns ({2} land, {3} wet) in {4:0}s, {5} families", s.Name, columns, landCols, seaCols, secs, fams.Count);
        }

        void Done()
        {
            Mod.Logger.Notification("OREAUDIT done");
            if (!cfg.Shutdown) return;
            string report = Path.Combine(GamePaths.DataPath, "EarthMap", "selftest-report.txt");
            int waited = 0;
            void Check()
            {
                if (!cfg.WaitForSelfTest || File.Exists(report) || waited++ > 120) { Mod.Logger.Notification("OREAUDIT stopping the server"); api.Server.ShutDown(); }
                else api.Event.RegisterCallback(_ => Check(), 5000);
            }
            Check();
        }
    }
}
