using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace FishProbe
{
    /// <summary>Settings, from ModConfig/fishprobe.json; without the file the probe does nothing.</summary>
    public class ProbeConfig
    {
        public List<Place> Places = new();
        /// <summary>Chunks loaded on each side of a place (4: a 9 x 9 chunk square).</summary>
        public int Radius = 4;
        /// <summary>Random columns per chunk for the replays of vanilla's spawn tests.</summary>
        public int Samples = 48;
        public bool Shutdown = true;
        public int LoadTimeoutSeconds = 1800;
    }

    /// <summary>A place by latitude and longitude (Earth worlds), or by block X and Z (any world).</summary>
    public class Place
    {
        public string Name;
        public double? Lat, Lon, X, Z;
        public int? Radius;
    }

    /// <summary>
    /// EA-0178: what fish a world has and why. After the world is up it generates the chunks around each place, then
    /// (1) counts the fish entities there by group (freshwater, saltwater, reef) and by the water they are in,
    /// (2) classifies every column's water at vanilla's spawn height (the terrain heightmap + 1, as tryOnlySurface does),
    /// (3) replays vanilla's worldgen test (GenCreatures) and runtime test (ServerSystemEntitySpawner) for every fish type on
    /// random columns stage by stage, the runtime one checked against the spawner's own CanSpawnAt_offthread (with every
    /// mod's patches on it), and writes FishProbe/report.txt and report.json in the data folder.
    /// </summary>
    public class FishProbeSystem : ModSystem
    {
        ICoreServerAPI sapi;
        ProbeConfig cfg;
        readonly List<Area> areas = new();
        int pending;
        long waitListener;
        double waitStarted;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            try { cfg = api.LoadModConfig<ProbeConfig>("fishprobe.json"); }
            catch (Exception e) { api.Logger.Error("[fishprobe] bad config: {0}", e.Message); }
            if (cfg == null) return;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => api.Event.RegisterCallback(_ => Begin(), 5000));
        }

        sealed class Area
        {
            public string Name;
            public double Lat = double.NaN, Lon = double.NaN;
            public int X, Z, Cx0, Cz0, Cx1, Cz1;
            public int Chunks => (Cx1 - Cx0 + 1) * (Cz1 - Cz0 + 1);
        }

        void Begin()
        {
            try
            {
                object earth = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "EarthMap.EarthMapSystem");
                object geometry = null;
                MethodInfo fromGeo = null;
                if (earth != null)
                {
                    object model = earth.GetType().GetProperty("Model")?.GetValue(earth);
                    geometry = model?.GetType().GetField("Geometry")?.GetValue(model);
                    fromGeo = geometry?.GetType().GetMethod("FromGeo");
                }
                foreach (var p in cfg.Places)
                {
                    var a = new Area { Name = p.Name };
                    double x, z;
                    if (p.Lat.HasValue && p.Lon.HasValue)
                    {
                        if (fromGeo == null) { sapi.Logger.Warning("[fishprobe] {0}: no Earth geometry, skipped", p.Name); continue; }
                        object[] args = { p.Lat.Value, p.Lon.Value, 0.0, 0.0 };
                        fromGeo.Invoke(geometry, args);
                        x = (double)args[2]; z = (double)args[3];
                        a.Lat = p.Lat.Value; a.Lon = p.Lon.Value;
                    }
                    else
                    {
                        x = p.X ?? sapi.WorldManager.MapSizeX / 2.0;
                        z = p.Z ?? sapi.WorldManager.MapSizeZ / 2.0;
                    }
                    a.X = (int)x; a.Z = (int)z;
                    int r = p.Radius ?? cfg.Radius;
                    a.Cx0 = a.X / 32 - r; a.Cx1 = a.X / 32 + r; a.Cz0 = a.Z / 32 - r; a.Cz1 = a.Z / 32 + r;
                    areas.Add(a);
                }
                sapi.Logger.Notification("[fishprobe] {0} places", areas.Count);
                waitStarted = sapi.World.ElapsedMilliseconds;
                waitListener = sapi.Event.RegisterGameTickListener(_ => Wait(), 1000);
            }
            catch (Exception e)
            {
                sapi.Logger.Error("[fishprobe] start failed: {0}", e);
                Finish();
            }
        }

        // One place at a time (a few hundred column requests at once overflow the server's request queue, which then drops
        // some); a column not there after a minute is asked for again.
        int loading = -1;
        double requested;

        bool Missing(Area a, int cx, int cz) => sapi.WorldManager.GetChunk(cx, 0, cz) == null || sapi.WorldManager.GetMapChunk(cx, cz) == null;

        void Request(Area a)
        {
            pending = 0;
            for (int cx = a.Cx0; cx <= a.Cx1; cx++)
                for (int cz = a.Cz0; cz <= a.Cz1; cz++)
                    if (Missing(a, cx, cz))
                    {
                        pending++;
                        sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
                    }
            requested = sapi.World.ElapsedMilliseconds;
        }

        void Wait()
        {
            bool timeout = sapi.World.ElapsedMilliseconds - waitStarted > cfg.LoadTimeoutSeconds * 1000.0;
            if (loading < 0) { loading = 0; if (areas.Count > 0) Request(areas[0]); return; }
            if (loading < areas.Count && !timeout)
            {
                var a = areas[loading];
                int missing = 0;
                for (int cx = a.Cx0; cx <= a.Cx1; cx++)
                    for (int cz = a.Cz0; cz <= a.Cz1; cz++)
                        if (Missing(a, cx, cz)) missing++;
                if (missing > 0)
                {
                    if (sapi.World.ElapsedMilliseconds - requested > 60000) Request(a);
                    return;
                }
                sapi.Logger.Notification("[fishprobe] {0} loaded ({1}/{2})", a.Name, loading + 1, areas.Count);
                if (++loading < areas.Count) { Request(areas[loading]); return; }
            }
            sapi.Event.UnregisterGameTickListener(waitListener);
            if (timeout) sapi.Logger.Warning("[fishprobe] places still loading after {0} s; measuring what is there", cfg.LoadTimeoutSeconds);
            // let the generated chunks' entities join the world
            sapi.Event.RegisterCallback(_ => Measure(), 8000);
        }

        void Finish()
        {
            if (cfg.Shutdown) sapi.Event.RegisterCallback(_ => sapi.Server.ShutDown(), 2000);
        }

        // ------------------------------------------------------------------------------------------ measuring

        static readonly string[] Groups = { "freshwater", "saltwater", "reef" };
        static string GroupOf(string path) =>
            path.StartsWith("fish-freshwater") ? "freshwater" : path.StartsWith("fish-saltwater") ? "saltwater" : path.StartsWith("fish-reef") ? "reef" : null;

        /// <summary>One column at vanilla's surface spawn height.</summary>
        struct Col
        {
            public string Kind;      // land, salt, fresh-still, fresh-flow, fresh-low, rapid, other
            public int Hm, Depth, Light, Surface;
            public double RelY;
            public bool Frozen, Coral, Plant, Wide;
        }

        sealed class KindStats
        {
            public int Columns, Frozen, Coral, Plant, Wide, RelYIn8to10, RelYAbove1, Light3, Light8;
            public List<int> Depths = new();
            public List<double> RelYs = new();
            public List<int> Lights = new();
            public List<int> Surfaces = new();
        }

        sealed class StageStats
        {
            public long Tests;
            public SortedDictionary<string, long> FirstFail = new();
            public long Pass, VanillaPass, Disagree;
            public SortedDictionary<string, int> ColumnsWithAPass = new();
            public SortedDictionary<string, int> ColumnsTested = new();
        }

        sealed class AreaReport
        {
            public string Name;
            public double Lat, Lon;
            public int X, Z, Chunks, ChunksLoaded;
            public SortedDictionary<string, KindStats> Kinds = new();
            public SortedDictionary<string, SortedDictionary<string, int>> FishByGroupAndKind = new();
            public SortedDictionary<string, int> FishByType = new();
            public SortedDictionary<string, int> FishDark = new();
            public SortedDictionary<string, StageStats> Worldgen = new(), Runtime = new();
        }

        object spawner;
        MethodInfo canSpawnAt, suitableClimate, canSpawnAtPosition;
        FieldInfo tmpPosField;
        object worldMap;
        GenCreatures genCreatures;
        int mapY, sea;
        readonly Random rand = new(1318);

        void Measure()
        {
            var reports = new List<AreaReport>();
            var text = new StringBuilder();
            try
            {
                mapY = sapi.WorldManager.MapSizeY;
                sea = sapi.World.SeaLevel;
                BindVanilla(text);
                DescribeTypes(text);
                foreach (var a in areas) reports.Add(MeasureArea(a));
                foreach (var r in reports) Describe(r, text);
            }
            catch (Exception e)
            {
                text.AppendLine("FAILED: " + e);
                sapi.Logger.Error("[fishprobe] {0}", e);
            }
            string dir = sapi.GetOrCreateDataPath("FishProbe");
            File.WriteAllText(Path.Combine(dir, "report.txt"), text.ToString());
            File.WriteAllText(Path.Combine(dir, "report.json"), JsonConvert.SerializeObject(new
            {
                World = new { MapSizeY = mapY, SeaLevel = sea, TerraGenSeaLevel = TerraGenConfig.seaLevel, Seed = sapi.WorldManager.Seed,
                    Mods = sapi.ModLoader.Mods.Select(m => m.Info.ModID + "@" + m.Info.Version).ToArray() },
                Areas = reports
            }, Formatting.Indented));
            sapi.Logger.Notification("[fishprobe] report written to {0}", dir);
            Finish();
        }

        void BindVanilla(StringBuilder text)
        {
            object server = sapi.World;
            var systems = server.GetType().GetField("Systems", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(server) as Array;
            spawner = systems?.Cast<object>().FirstOrDefault(s => s.GetType().Name == "ServerSystemEntitySpawner");
            var t = spawner?.GetType();
            canSpawnAt = t?.GetMethod("CanSpawnAt_offthread", BindingFlags.Instance | BindingFlags.Public);
            suitableClimate = t?.GetMethod("GetSuitableClimateTemperatureRainfall", BindingFlags.Instance | BindingFlags.NonPublic);
            tmpPosField = t?.GetField("tmpPos", BindingFlags.Instance | BindingFlags.NonPublic);
            worldMap = server.GetType().GetField("WorldMap")?.GetValue(server);
            genCreatures = sapi.ModLoader.GetModSystem<GenCreatures>();
            canSpawnAtPosition = typeof(GenCreatures).GetMethod("CanSpawnAtPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            text.AppendLine($"World: height {mapY}, sea level {sea} (TerraGenConfig {TerraGenConfig.seaLevel}), seed {sapi.WorldManager.Seed}");
            text.AppendLine("Mods: " + string.Join(", ", sapi.ModLoader.Mods.Select(m => m.Info.ModID + " " + m.Info.Version)));
            text.AppendLine($"Vanilla bound: spawner {spawner != null}, CanSpawnAt_offthread {canSpawnAt != null}, climate {suitableClimate != null}, tmpPos {tmpPosField != null}, world map {worldMap != null}, GenCreatures {genCreatures != null}");
            text.AppendLine();
        }

        IEnumerable<EntityProperties> FishTypes(string group) =>
            sapi.World.EntityTypes.Where(t => t.Code?.Domain == "game" && GroupOf(t.Code.Path) == group);

        void DescribeTypes(StringBuilder text)
        {
            text.AppendLine("Fish spawn conditions as loaded (after every mod's patches):");
            foreach (string g in Groups)
            {
                var types = FishTypes(g).ToList();
                text.AppendLine($"  {g}: {types.Count} types");
                var first = types.FirstOrDefault();
                if (first == null) continue;
                var sc = first.Server.SpawnConditions;
                text.AppendLine($"    e.g. {first.Code}: habitat {first.Habitat}");
                text.AppendLine($"    worldgen inside [{string.Join(", ", sc.Worldgen?.InsideBlockCodes?.Select(c => c.ToString()) ?? Array.Empty<string>())}], tries {sc.Worldgen?.TriesPerChunk?.avg}, surface {sc.Worldgen?.TryOnlySurface}, minLight {sc.Worldgen?.MinLightLevel}");
                text.AppendLine($"    runtime inside [{string.Join(", ", sc.Runtime?.InsideBlockCodes?.Select(c => c.ToString()) ?? Array.Empty<string>())}], chance {sc.Runtime?.Chance}, group {sc.Runtime?.Group}, maxByGroup {sc.Runtime?.MaxQuantityByGroup?.Code}={sc.Runtime?.MaxQuantityByGroup?.MaxQuantity}, habitatSize {sc.Runtime?.HabitatSize}");
                foreach (var t in types)
                {
                    var r = t.Server.SpawnConditions.Runtime;
                    text.AppendLine($"      {t.Code.Path,-44} temp {r.MinTemp,5:0}..{r.MaxTemp,-4:0} rain {r.MinRain:0.##}..{r.MaxRain:0.##} Y {r.MinY:0.##}..{r.MaxY:0.##} map {t.Server.SpawnConditions.Worldgen?.MapCode}");
                }
            }
            text.AppendLine();
        }

        double RelY(int y) => y > sea ? 1.0 + (double)(y - sea) / (mapY - sea) : (double)y / sea;
        double RelYWorldgen(int y) => y > TerraGenConfig.seaLevel
            ? 1.0 + ((double)y - TerraGenConfig.seaLevel) / (sapi.World.BlockAccessor.MapSizeY - TerraGenConfig.seaLevel)
            : (double)y / TerraGenConfig.seaLevel;

        Col Classify(IBlockAccessor ba, int x, int z, ushort[] heights)
        {
            var c = new Col { Hm = heights[(z % 32) * 32 + x % 32] };
            int y0 = c.Hm + 1;
            var pos = new BlockPos(x, y0, z);
            c.RelY = RelY(y0);
            c.Light = ba.GetLightLevel(pos, EnumLightLevelType.MaxLight);
            Block fluid = ba.GetBlock(pos, BlockLayersAccess.Fluid);
            Block solid = ba.GetBlock(pos, BlockLayersAccess.Solid);
            c.Coral = solid.Code?.Path.StartsWith("coral") == true;
            c.Plant = solid.Id != 0 && !c.Coral;
            string fp = fluid.Code?.Path ?? "air";
            if (fluid.Id == 0 || !fluid.IsLiquid())
            {
                c.Kind = "land";
                return c;
            }
            c.Kind = fp.StartsWith("saltwater") ? "salt"
                : fp == "water-still-7" ? "fresh-still"
                : fp.StartsWith("water-") && fp.EndsWith("-7") ? "fresh-flow"
                : fp.StartsWith("water-") ? "fresh-low"
                : fp.StartsWith("rapidwater") ? "rapid" : "other:" + fp;
            int top = y0;
            var p = pos.Copy();
            while (top + 1 < mapY)
            {
                p.Y = top + 1;
                Block f = ba.GetBlock(p, BlockLayersAccess.Fluid);
                if (f.Id == 0 || !f.IsLiquid()) break;
                top++;
            }
            c.Depth = top - c.Hm;
            c.Surface = top + 1;
            p.Y = top; string atTop = ba.GetBlock(p, BlockLayersAccess.Solid).Code?.Path ?? "";
            p.Y = top + 1; string above = ba.GetBlock(p, BlockLayersAccess.Solid).Code?.Path ?? "";
            c.Frozen = atTop.Contains("ice") || above.Contains("ice");
            string family = fp.Split('-')[0];
            c.Wide = true;
            foreach (var (dx, dz) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2) })
            {
                Block n = ba.GetBlock(new BlockPos(x + dx, y0, z + dz), BlockLayersAccess.Fluid);
                if (n.Code == null || n.Code.Path.Split('-')[0] != family) { c.Wide = false; break; }
            }
            return c;
        }

        AreaReport MeasureArea(Area a)
        {
            var ba = sapi.World.BlockAccessor;
            var rep = new AreaReport { Name = a.Name, Lat = a.Lat, Lon = a.Lon, X = a.X, Z = a.Z, Chunks = a.Chunks };
            var cols = new Dictionary<(int, int), Col>();
            for (int cx = a.Cx0; cx <= a.Cx1; cx++)
                for (int cz = a.Cz0; cz <= a.Cz1; cz++)
                {
                    var mc = sapi.WorldManager.GetMapChunk(cx, cz);
                    if (mc == null || sapi.WorldManager.GetChunk(cx, 0, cz) == null) continue;
                    rep.ChunksLoaded++;
                    var heights = mc.WorldGenTerrainHeightMap;
                    for (int lx = 0; lx < 32; lx++)
                        for (int lz = 0; lz < 32; lz++)
                        {
                            int x = cx * 32 + lx, z = cz * 32 + lz;
                            var c = Classify(ba, x, z, heights);
                            cols[(x, z)] = c;
                            if (!rep.Kinds.TryGetValue(c.Kind, out var k)) rep.Kinds[c.Kind] = k = new KindStats();
                            k.Columns++;
                            if (c.Kind == "land") continue;
                            if (c.Frozen) k.Frozen++;
                            if (c.Coral) k.Coral++;
                            if (c.Plant) k.Plant++;
                            if (c.Wide) k.Wide++;
                            if (c.RelY >= 0.8 && c.RelY <= 1.0) k.RelYIn8to10++;
                            if (c.RelY > 1.0) k.RelYAbove1++;
                            if (c.Light >= 3) k.Light3++;
                            if (c.Light >= 8) k.Light8++;
                            k.Depths.Add(c.Depth); k.RelYs.Add(Math.Round(c.RelY, 3)); k.Lights.Add(c.Light); k.Surfaces.Add(c.Surface);
                        }
                }

            // fish entities in the area, by group and by the water at their column
            foreach (var e in sapi.World.LoadedEntities.Values)
            {
                string g = e.Code == null ? null : GroupOf(e.Code.Path);
                if (g == null || e.Pos.Dimension != 0) continue;
                int x = (int)e.Pos.X, z = (int)e.Pos.Z;
                if (x / 32 < a.Cx0 || x / 32 > a.Cx1 || z / 32 < a.Cz0 || z / 32 > a.Cz1) continue;
                string kind = cols.TryGetValue((x, z), out var c) ? c.Kind : "unloaded";
                if (!rep.FishByGroupAndKind.TryGetValue(g, out var byKind)) rep.FishByGroupAndKind[g] = byKind = new SortedDictionary<string, int>();
                byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
                rep.FishByType[e.Code.Path] = rep.FishByType.GetValueOrDefault(e.Code.Path) + 1;
                int light = ba.GetLightLevel(new BlockPos((int)e.Pos.X, (int)e.Pos.Y, (int)e.Pos.Z), EnumLightLevelType.MaxLight);
                if (light < 8) rep.FishDark[g] = rep.FishDark.GetValueOrDefault(g) + 1;
            }

            // replays of vanilla's tests on random columns
            foreach (string g in Groups)
            {
                var types = FishTypes(g).ToList();
                var wg = rep.Worldgen[g] = new StageStats();
                var rt = rep.Runtime[g] = new StageStats();
                for (int cx = a.Cx0; cx <= a.Cx1; cx++)
                    for (int cz = a.Cz0; cz <= a.Cz1; cz++)
                    {
                        var mc = sapi.WorldManager.GetMapChunk(cx, cz);
                        if (mc == null) continue;
                        IWorldChunk[] column = ChunkColumn(cx, cz);
                        if (column == null) continue;
                        try
                        {
                            for (int s = 0; s < cfg.Samples; s++)
                            {
                                int x = cx * 32 + rand.Next(32), z = cz * 32 + rand.Next(32);
                                if (!cols.TryGetValue((x, z), out var c)) continue;
                                bool anyW = false, anyR = false;
                                foreach (var t in types)
                                {
                                    anyW |= ReplayWorldgen(wg, t, x, z, c);
                                    anyR |= ReplayRuntime(rt, t, x, z, c, column);
                                }
                                wg.ColumnsTested[c.Kind] = wg.ColumnsTested.GetValueOrDefault(c.Kind) + 1;
                                rt.ColumnsTested[c.Kind] = rt.ColumnsTested.GetValueOrDefault(c.Kind) + 1;
                                if (anyW) wg.ColumnsWithAPass[c.Kind] = wg.ColumnsWithAPass.GetValueOrDefault(c.Kind) + 1;
                                if (anyR) rt.ColumnsWithAPass[c.Kind] = rt.ColumnsWithAPass.GetValueOrDefault(c.Kind) + 1;
                            }
                        }
                        finally
                        {
                            foreach (var ch in column) ch?.ReleaseBlockReadLock();
                        }
                    }
            }
            return rep;
        }

        IWorldChunk[] ChunkColumn(int cx, int cz)
        {
            var column = new IWorldChunk[mapY / 32];
            for (int cy = 0; cy < column.Length; cy++)
            {
                var ch = sapi.WorldManager.GetChunk(cx, cy, cz);
                if (ch == null)
                {
                    for (int k = 0; k < cy; k++) column[k]?.ReleaseBlockReadLock();
                    return null;
                }
                ch.Unpack_ReadOnly();
                ch.AcquireBlockReadLock();
                column[cy] = ch;
            }
            return column;
        }

        static void Fail(StageStats s, string stage) => s.FirstFail[stage] = s.FirstFail.GetValueOrDefault(stage) + 1;

        /// <summary>GenCreatures.TrySpawnGroupAt for one position: CanSpawnAtPosition, then CanSpawnAtConditions, then the map.</summary>
        bool ReplayWorldgen(StageStats s, EntityProperties t, int x, int z, Col c)
        {
            var sc = t.Server.SpawnConditions.Worldgen;
            if (sc == null) return false;
            s.Tests++;
            var ba = sapi.World.BlockAccessor;
            var pos = new BlockPos(x, c.Hm + 1, z);
            double rel = RelYWorldgen(pos.Y);
            if (!sc.CanSpawnInside(ba.GetBlock(pos))) { Fail(s, "1 inside block"); return false; }
            var below = pos.DownCopy();
            if (!ba.GetBlock(below).CanCreatureSpawnOn(ba, below, t, sc)) { Fail(s, "2 ground"); return false; }
            int light = ba.GetLightLevel(pos, sc.LightLevelType);
            if (sc.MinLightLevel > light || sc.MaxLightLevel < light) { Fail(s, "3 light"); return false; }
            var mr = ba.GetMapRegion(x / sapi.WorldManager.RegionSize, z / sapi.WorldManager.RegionSize);
            var cm = mr?.ClimateMap;
            if (cm == null) { Fail(s, "4 no climate map"); return false; }
            int rs = sapi.WorldManager.RegionSize;
            float fx = (float)(x % rs) / rs * cm.InnerSize, fz = (float)(z % rs) / rs * cm.InnerSize;
            int climate = cm.GetUnpaddedColorLerped(fx, fz);
            float temp = Climate.GetScaledAdjustedTemperatureFloat((climate >> 16) & 0xff, (int)(TerraGenConfig.seaLevel * 0.09));
            float rain = ((climate >> 8) & 0xff) / 255f;
            if (sc.MinTemp > temp || sc.MaxTemp < temp) { Fail(s, "4 temperature"); return false; }
            if (sc.MinRain > rain || sc.MaxRain < rain) { Fail(s, "5 rain"); return false; }
            if (sc.MinY > rel || sc.MaxY < rel) { Fail(s, "6 height (minY/maxY)"); return false; }
            string code = t.Server.SpawnConditions.Climate?.MapCode ?? sc.MapCode;
            if (code != null && genCreatures != null && genCreatures.GetAnimalMapDensity(code, x, z, mr) < 128f) { Fail(s, "7 species map (" + code + ")"); return false; }
            // vanilla's own position test, with every mod's patches on it (Earth's range guards and water depth)
            if (canSpawnAtPosition != null && genCreatures != null && !(bool)canSpawnAtPosition.Invoke(genCreatures, new object[] { ba, t, pos.Copy(), sc })) { Fail(s, "8 refused by a mod's patch"); return false; }
            s.Pass++;
            return true;
        }

        /// <summary>
        /// ServerSystemEntitySpawner.TrySpawnSomethingAt for one surface position: its height test, then CanSpawnAt_offthread
        /// in its own order (climate, inside block, ground, collision, nearby player, habitat), then the light test the
        /// nearby player makes (temporal stability's CanSpawnNearby). The spawner's own verdict is taken too.
        /// </summary>
        bool ReplayRuntime(StageStats s, EntityProperties t, int x, int z, Col c, IWorldChunk[] column)
        {
            var sc = t.Server.SpawnConditions.Runtime;
            if (sc == null) return false;
            s.Tests++;
            var ba = sapi.World.BlockAccessor;
            var pos = new BlockPos(x, c.Hm + 1, z);
            double rel = RelY(pos.Y);
            if (pos.Y < 1 || pos.Y >= mapY) { Fail(s, "0 outside world"); return false; }
            if (sc.MinY > rel || sc.MaxY < rel) { Fail(s, "1 height (minY/maxY)"); return false; }
            bool vanilla = false;
            if (canSpawnAt != null)
            {
                try { vanilla = canSpawnAt.Invoke(spawner, new object[] { t, pos.Copy(), sc, column }) != null; }
                catch (Exception e) { Fail(s, "vanilla threw " + (e.InnerException ?? e).GetType().Name); }
            }
            bool mine = RuntimeStages(s, t, sc, pos, column, ba);
            if (vanilla) s.VanillaPass++;
            if (mine != vanilla) s.Disagree++;
            if (!mine) return false;
            int light = ba.GetLightLevel(pos, sc.LightLevelType);
            if (sc.MinLightLevel > light || sc.MaxLightLevel < light) { Fail(s, "7 light (player's test)"); return false; }
            if (canSpawnAt != null && !vanilla) { Fail(s, "8 refused by a mod's patch"); return false; }
            s.Pass++;
            return true;
        }

        bool RuntimeStages(StageStats s, EntityProperties t, RuntimeSpawnConditions sc, BlockPos pos, IWorldChunk[] column, IBlockAccessor ba)
        {
            if (suitableClimate != null && tmpPosField != null && worldMap != null)
            {
                var tmp = (BlockPos)tmpPosField.GetValue(spawner);
                tmp.Set(pos);
                object climate = suitableClimate.Invoke(spawner, new[] { worldMap, sc });
                if (climate == null) { Fail(s, "2 climate"); return false; }
            }
            Block inside = column[pos.Y / 32].GetLocalBlockAtBlockPos_LockFree(sapi.World, pos);
            if (!sc.CanSpawnInside(inside)) { Fail(s, "3 inside block"); return false; }
            var below = pos.DownCopy();
            if (!column[below.Y / 32].GetLocalBlockAtBlockPos_LockFree(sapi.World, below, 1).CanCreatureSpawnOn(ba, below, t, sc)) { Fail(s, "4 ground"); return false; }
            if (sc.HabitatSize > 0 && (t.Habitat == EnumHabitat.Underwater || t.Habitat == EnumHabitat.Sea))
            {
                foreach (var f in new[] { BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.WEST, BlockFacing.SOUTH })
                {
                    var n = pos.AddCopy(f, sc.HabitatSize);
                    if (!sc.CanSpawnInside(column[n.Y / 32].GetLocalBlockAtBlockPos_LockFree(sapi.World, n))) { Fail(s, "6 habitat (water " + sc.HabitatSize + " blocks around)"); return false; }
                }
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------ text report

        static string Q(List<int> v)
        {
            if (v.Count == 0) return "-";
            var o = v.OrderBy(d => d).ToList();
            return $"{o[0]}/{o[o.Count / 2]}/{o[^1]}";
        }
        static string Q(List<double> v)
        {
            if (v.Count == 0) return "-";
            var o = v.OrderBy(d => d).ToList();
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}/{1:0.00}/{2:0.00}", o[0], o[o.Count / 2], o[^1]);
        }

        void Describe(AreaReport r, StringBuilder text)
        {
            text.AppendLine($"=== {r.Name}" + (double.IsNaN(r.Lat) ? "" : string.Format(CultureInfo.InvariantCulture, " ({0:0.###}, {1:0.###})", r.Lat, r.Lon)) + $" at {r.X},{r.Z}: {r.ChunksLoaded}/{r.Chunks} chunks");
            text.AppendLine("  water at spawn height (min/median/max): columns, depth, relY, light; counts frozen, coral, plant, 5 wide, relY 0.8-1, relY>1, light>=3, light>=8");
            foreach (var (kind, k) in r.Kinds)
            {
                if (kind == "land") { text.AppendLine($"    {kind,-12} {k.Columns,7}"); continue; }
                text.AppendLine($"    {kind,-12} {k.Columns,7}  surface {Q(k.Surfaces),-12} depth {Q(k.Depths),-10} relY {Q(k.RelYs),-16} light {Q(k.Lights),-9} | ice {k.Frozen} coral {k.Coral} plant {k.Plant} wide {k.Wide} rel.8-1 {k.RelYIn8to10} rel>1 {k.RelYAbove1} L3 {k.Light3} L8 {k.Light8}");
            }
            int total = r.FishByType.Values.Sum();
            text.AppendLine($"  fish entities: {total}");
            foreach (var (g, byKind) in r.FishByGroupAndKind)
                text.AppendLine($"    {g,-10} {byKind.Values.Sum(),5} (in: {string.Join(", ", byKind.Select(p => p.Key + " " + p.Value))}; dark <8: {r.FishDark.GetValueOrDefault(g)})");
            if (r.FishByType.Count > 0) text.AppendLine("    types: " + string.Join(", ", r.FishByType.Select(p => p.Key.Replace("fish-", "").Replace("-adult", "") + " " + p.Value)));
            foreach (string g in Groups)
            {
                Stage(text, "worldgen", g, r.Worldgen.GetValueOrDefault(g));
                Stage(text, "runtime ", g, r.Runtime.GetValueOrDefault(g));
            }
            text.AppendLine();
        }

        static void Stage(StringBuilder text, string which, string g, StageStats s)
        {
            if (s == null) return;
            string extra = which.StartsWith("runtime") ? $", spawner says yes {s.VanillaPass}, disagrees {s.Disagree}" : "";
            text.AppendLine($"  {which} {g,-10} tests {s.Tests}, pass {s.Pass}{extra}; first failure: {string.Join(", ", s.FirstFail.Select(p => p.Key + " " + p.Value))}");
            text.AppendLine($"           columns with a passing type: {string.Join(", ", s.ColumnsTested.Where(p => p.Key != "land").Select(p => p.Key + " " + s.ColumnsWithAPass.GetValueOrDefault(p.Key) + "/" + p.Value))}");
        }
    }
}
