using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SlabAudit
{
    public class AuditSite { public string Name; public string Kind; public double Lat, Lon; public int Radius = 2; }
    public class AuditConfig
    {
        public bool Enabled = true;
        public int StartDelayMs = 5000;
        public bool Shutdown = true;
        public List<AuditSite> Sites = new();
    }

    /// <summary>Test-only: dumps solid and fluid block ids of fully generated chunk columns around sites. Not part of Earth.</summary>
    public class SlabAuditSystem : ModSystem
    {
        ICoreServerAPI api;
        AuditConfig cfg;
        string outDir;
        object geometry;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
        public override double ExecuteOrder() => 2;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            try { cfg = api.LoadModConfig<AuditConfig>("slabaudit.json"); } catch (Exception e) { Mod.Logger.Error("SLABAUDIT config: {0}", e); }
            if (cfg == null || !cfg.Enabled) return;
            outDir = Path.Combine(GamePaths.DataPath, "slabaudit");
            Directory.CreateDirectory(outDir);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), cfg.StartDelayMs));
        }

        void Begin()
        {
            try
            {
                var codes = new Dictionary<int, string>();
                foreach (var b in api.World.Blocks) if (b?.Code != null) codes[b.Id] = b.Code.ToString();
                File.WriteAllText(Path.Combine(outDir, "blocks.json"), JsonSerializer.Serialize(codes));
                var core = api.ModLoader.GetModSystem("EarthMap.EarthMapSystem");
                var model = core?.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(core);
                geometry = model?.GetType().GetField("Geometry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(model);
                File.WriteAllText(Path.Combine(outDir, "world.json"), JsonSerializer.Serialize(new { sea = api.World.SeaLevel, h = api.WorldManager.MapSizeY, seed = api.World.Seed, earth = geometry != null }));
                Mod.Logger.Notification("SLABAUDIT start: {0} sites, earth={1}", cfg.Sites.Count, geometry != null);
            }
            catch (Exception e) { Mod.Logger.Error("SLABAUDIT setup failed: {0}", e); }
            Next(0);
        }

        void Next(int i)
        {
            if (i >= cfg.Sites.Count) { Mod.Logger.Notification("SLABAUDIT done"); if (cfg.Shutdown) api.Server.ShutDown(); return; }
            var s = cfg.Sites[i];
            if (geometry == null) { Mod.Logger.Error("SLABAUDIT {0}: no Earth geometry", s.Name); Next(i + 1); return; }
            var args = new object[] { s.Lat, s.Lon, 0.0, 0.0 };
            geometry.GetType().GetMethod("FromGeo").Invoke(geometry, args);
            double x = (double)args[2], z = (double)args[3];
            int ccx = (int)Math.Floor(x / 32), ccz = (int)Math.Floor(z / 32), r = s.Radius;
            int total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            bool done = false;
            var t0 = DateTime.UtcNow;
            void Go()
            {
                if (done) return; done = true;
                try { Dump(s, x, z, ccx - r, ccz - r, 2 * r + 1, (DateTime.UtcNow - t0).TotalSeconds); }
                catch (Exception e) { Mod.Logger.Error("SLABAUDIT {0} failed: {1}", s.Name, e); }
                api.Event.RegisterCallback(_ => Next(i + 1), 50);
            }
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                    api.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { OnLoaded = () => { loaded++; if (loaded >= total) api.Event.EnqueueMainThreadTask(Go, "slabaudit"); } });
            api.Event.RegisterCallback(_ => { if (!done) { Mod.Logger.Warning("SLABAUDIT {0}: timeout {1}/{2}", s.Name, loaded, total); Go(); } }, 600000);
        }

        /// <summary>Layout: int32 header (cx0, cz0, n, H, blockX, blockZ), then per column (cx outer, cz inner): 1024 ushort terrain
        /// heights, 1024 ushort rain heights, then per y: 1024 int32 solid ids, 1024 int32 fluid ids (index lz*32+lx).</summary>
        void Dump(AuditSite s, double x, double z, int cx0, int cz0, int n, double secs)
        {
            int H = api.WorldManager.MapSizeY, missing = 0;
            string file = Path.Combine(outDir, s.Name + ".bin.gz");
            using (var fs = File.Create(file))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var w = new BinaryWriter(gz))
            {
                foreach (int v in new[] { cx0, cz0, n, H, (int)x, (int)z }) w.Write(v);
                for (int cx = cx0; cx < cx0 + n; cx++)
                    for (int cz = cz0; cz < cz0 + n; cz++)
                    {
                        var mc = api.WorldManager.GetMapChunk(cx, cz);
                        var chunks = new IServerChunk[H / 32];
                        bool ok = mc != null;
                        for (int cy = 0; cy < chunks.Length && ok; cy++) { chunks[cy] = api.WorldManager.GetChunk(cx, cy, cz); if (chunks[cy] == null) ok = false; else chunks[cy].Unpack(); }
                        if (!ok) missing++;
                        for (int k = 0; k < 1024; k++) w.Write(ok ? mc.WorldGenTerrainHeightMap[k] : (ushort)0);
                        for (int k = 0; k < 1024; k++) w.Write(ok ? mc.RainHeightMap[k] : (ushort)0);
                        for (int y = 0; y < H; y++)
                        {
                            var d = ok ? chunks[y / 32].Data : null;
                            int b0 = (y % 32) * 1024;
                            for (int k = 0; k < 1024; k++) w.Write(ok ? d.GetBlockIdUnsafe(b0 + k) : -1);
                            for (int k = 0; k < 1024; k++) w.Write(ok ? d.GetFluid(b0 + k) : -1);
                        }
                    }
            }
            Mod.Logger.Notification("SLABAUDIT {0}: {1}x{1} columns at {2:0},{3:0} in {4:0}s, missing {5}", s.Name, n, x, z, secs, missing);
        }
    }
}
