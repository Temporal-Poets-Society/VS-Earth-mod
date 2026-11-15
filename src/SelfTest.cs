using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Automated check on a dedicated server (config SelfTest.Enabled): generates the chunks around a list of places
    /// with the full worldgen pipeline, renders the result (top blocks) to VintagestoryData/EarthMap/selftest-*.png,
    /// compares the generated terrain height with the terrain model, logs a report and stops the server.
    /// </summary>
    public class SelfTest : ModSystem
    {
        ICoreServerAPI sapi;
        EarthMapSystem core;
        readonly StringBuilder report = new StringBuilder();

        public override double ExecuteOrder() => 1.5;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                if (core.Config?.SelfTest?.Enabled == true) api.Event.RegisterCallback(_ => Start(), 3000);
            });
            // The digest hashes each chunk column as generation leaves it, before the running game changes anything (water
            // flowing and Hydrology filling, seasonal ice, plants growing), so it measures generation alone. It hashes at the
            // end of each pass in DigestPasses (this system starts after the generators, so its handler runs last in the
            // pass): the earlier passes hold Earth's terrain, rock and ore output before the vegetation pass, whose
            // vanilla parts are not repeatable from run to run.
            if (core.Config?.SelfTest?.Enabled == true && core.Config.SelfTest.Digest)
                foreach (var pass in DigestPasses())
                    api.Event.ChunkColumnGeneration(request => HashGenerated(request, pass), pass, "standard");
        }

        IEnumerable<EnumWorldGenPass> DigestPasses() =>
            (core.Config.SelfTest.DigestPasses ?? new string[0])
                .Select(s => Enum.TryParse<EnumWorldGenPass>(s, true, out var p) ? p : EnumWorldGenPass.None)
                .Where(p => p >= EnumWorldGenPass.Terrain && p <= EnumWorldGenPass.PreDone)
                .Distinct().OrderBy(p => p);

        readonly System.Collections.Concurrent.ConcurrentDictionary<(EnumWorldGenPass Pass, long Column), (byte[] Column, string Chunks)> generated = new();

        int[] hashAs;

        /// <summary>
        /// The block id the digest hashes for each block id: itself, except that every loot vessel counts as the first one.
        /// Vanilla's buried ruins pick a vessel's kind (forage, farming, ore, seed...) differently in every run of one build
        /// (8 Nov, raw dumps), so the vessel's place is checked but not its kind.
        /// </summary>
        int[] HashAs()
        {
            if (hashAs != null) return hashAs;
            var blocks = sapi.World.Blocks;
            var map = new int[blocks.Count];
            int vessel = -1;
            for (int id = 0; id < map.Length; id++)
            {
                map[id] = id;
                if (blocks[id]?.Code?.Path.StartsWith("lootvessel-", StringComparison.Ordinal) == true)
                    map[id] = vessel < 0 ? (vessel = id) : vessel;
            }
            return hashAs = map;
        }

        void HashGenerated(IChunkColumnGenerateRequest request, EnumWorldGenPass pass)
        {
            var chunks = request.Chunks;
            // (EA-0088) the format is Core.ColumnDigest's, pinned in tests/ServerToolsTests
            using var column = new ColumnDigest(HashAs());
            foreach (var chunk in chunks)
            {
                chunk.Unpack_ReadOnly();
                var data = chunk.Data;
                column.AddChunk(i => data[i], data.GetFluid);
            }
            var map = chunks.Length > 0 ? chunks[0].MapChunk : null;
            if (map != null)
            {
                column.AddHeights(map.WorldGenTerrainHeightMap);
                column.AddHeights(map.RainHeightMap);
            }
            long key = ((long)request.ChunkX << 32) | (uint)request.ChunkZ;
            generated[(pass, key)] = (column.Finish(), column.Chunks);
            generatedMeta[(pass, key)] = MetaHash(chunks, map);
            if (core.Config.SelfTest.DigestDumpColumns?.Contains(request.ChunkX + "," + request.ChunkZ) == true)
                DumpGenerated(request, pass);
        }

        // Earth's generation output beside the blocks, hashed apart ("DIGEST-<pass>Meta") so the block digests stay
        // comparable with runs made before it: the map chunk's top rock per column (Earth's geology and bauxite strata set
        // it) and the data Earth stores on map chunks (hot springs; the rock floor, EarthWorldGen.RockFloorModdata) and on
        // chunks (Hydrate or Diedrate's aquifers, with HoD installed).
        static readonly string[] MetaMapKeys = { "hotspringlocations", "earthRockFloor" };
        static readonly string[] MetaChunkKeys = { HodAquiferContract.ModDataKey };
        readonly System.Collections.Concurrent.ConcurrentDictionary<(EnumWorldGenPass Pass, long Column), byte[]> generatedMeta = new();

        static byte[] MetaHash(IServerChunk[] chunks, IMapChunk map)
            => ColumnDigest.Meta(map?.TopRockIdMap, MetaMapKeys.Select(k => map?.GetModdata(k))
                .Concat(chunks.SelectMany(chunk => MetaChunkKeys.Select(k => chunk.GetModdata(k)))));

        /// <summary>A raw copy of a column as a pass left it (DigestDumpColumns), to see which blocks two runs disagree on.</summary>
        void DumpGenerated(IChunkColumnGenerateRequest request, EnumWorldGenPass pass)
        {
            string dir = Path.Combine(GamePaths.DataPath, "EarthMap", "gen", pass.ToString());
            Directory.CreateDirectory(dir);
            lock (generated)
                if (!File.Exists(Path.Combine(dir, "blockcodes.txt")))
                    File.WriteAllLines(Path.Combine(dir, "blockcodes.txt"), sapi.World.Blocks.Where(b => b != null).Select(b => b.Id + " " + b.Code));
            var buffer = new byte[32 * 32 * 32 * 8];
            using var raw = new FileStream(Path.Combine(dir, "dump-" + request.ChunkX + "-" + request.ChunkZ + ".bin"), FileMode.Create);
            foreach (var chunk in request.Chunks)
            {
                var data = chunk.Data;
                for (int i = 0; i < 32 * 32 * 32; i++)
                {
                    BitConverter.TryWriteBytes(buffer.AsSpan(i * 8), data[i]);
                    BitConverter.TryWriteBytes(buffer.AsSpan(i * 8 + 4), data.GetFluid(i));
                }
                raw.Write(buffer);
            }
        }

        void Log(string msg, params object[] args)
        {
            string s = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, msg, args) : msg;
            report.AppendLine(s);
            Mod.Logger.Notification("SELFTEST " + s);
        }

        void Start()
        {
            var cfg = core.Config.SelfTest;
            if (core.Params?.Enabled != true || core.Model == null)
            {
                Log("Earth Map is not active in this world, nothing to test. World sea={0}, generation sea={1}, climate sea={2}.",
                    sapi.World.SeaLevel, TerraGenConfig.seaLevel, Climate.Sealevel);
                Finish();
                return;
            }
            Log("world {0} x {1} x {2}, sea level {3}", sapi.WorldManager.MapSizeX, sapi.WorldManager.MapSizeZ, sapi.WorldManager.MapSizeY, sapi.World.SeaLevel);
            Log("terrain profile: {0}; curve={1}; detail={2}; ecology={3}; rock={4}; climate={5}; API sea={6}; helper sea={7}",
                core.Params.TerrainProfile ?? "legacy", core.Params.LinearLand ? "linear" : core.Params.LandH0.ToString(),
                core.Params.DetailScale, core.Params.LocalAlpineEcology, core.Params.PhysicalRockExposure, core.Params.ClimateRules,
                Vintagestory.ServerMods.TerraGenConfig.seaLevel, Climate.Sealevel);
            Log("decision 0031 data: vanilla fauna ranges={0}, CHELSA rain={1} ({2}), summits={3} ({4}), glaciers={5} ({6})",
                core.Params.VanillaFaunaRanges, core.Params.FineRain, core.Data.FineRain ? "on" : core.Data.FineRainError ?? "off",
                core.Params.Summits, core.Model.Summits ? "on" : core.Data.SummitsError ?? "off",
                core.Params.Glaciers, core.Data.HasGlaciers ? "on" : core.Data.GlaciersError ?? "off");
            VerifyVerticalProfile();
            Log("regional rules: trees={0}/v{1}, geology={2}/v{3}, fauna={4}/v{5}, extinct={6}, fauna ranges={7}, plants={8}",
                core.Params.RegionalTrees, core.Params.TreeRulesVersion, core.Params.RegionalGeology, core.Params.GeologyRulesVersion,
                // the world's own range profile (saved at creation, decision 0054); earth.json's only for worlds that saved none
                core.Params.RegionalFauna, core.Params.FaunaRulesVersion, core.Params.IncludeExtinctAnimals,
                core.Params.CreationOptionsVersion > 0 && core.Params.FaunaRangeProfile != null ? core.Params.FaunaRangeProfile : core.Config.FaunaRangeProfile + " (earth.json)",
                sapi.ModLoader.GetModSystem<EarthPlants>()?.Active == true ? "active" : "inactive");
            CheckCrafting();
            var fauna = sapi.ModLoader.GetModSystem<EarthFaunaSystem>();
            if (fauna?.Active == true)
            {
                try { Log("{0}", fauna.VerifyRuntimeClimateAdapter()); }
                catch (Exception e) { Log("runtime fauna climate analysis failed: {0}", e); }
            }
            Log("climate data: fix v{0} (decision 0018), applied to the grid: v{1} ({2})", core.Params.ClimateData, core.Data?.ClimateFixApplied,
                core.Params.ClimateData == (core.Data?.ClimateFixApplied ?? -1) ? "PASS" : "FAIL");
            if (core.Params.ClimateRules >= TerrainModel.RealLapseRules)
            {
                Log("climate rules v{0}: CHELSA temperatures (earthdata/temperature.bin) in use: {1} ({2})", core.Params.ClimateRules,
                    core.Data?.FineTemperatures, core.Data?.FineTemperatures == true ? "PASS" : "FAIL");
                // 20 °C stored at sea level, read 100 blocks up through vanilla's own method
                int dist = 100;
                double expected = Math.Clamp(Climate.DescaleTemperature(20) / Climate.TemperatureScaleConversion - 20 - core.Model.LapseAt(core.Model.SeaLevel + dist), -20, 40);
                float got = Climate.GetScaledAdjustedTemperatureFloat(Climate.DescaleTemperature(20), dist);
                Log("climate rules v{0} (decision 0009): real lapse in vanilla's altitude adjustment {1}; 20 °C at sea level reads {2:0.00} °C {3} blocks up, expected {4:0.00} ({5})",
                    core.Params.ClimateRules, ClimateLapse.Active ? "on" : "off", got, dist, expected, ClimateLapse.Active && Math.Abs(got - expected) < 0.01 ? "PASS" : "FAIL");
                // vanilla's own world generation: BlockPatchConfig.IsPatchSuitableAt reads the climate through
                // Climate.GetScaledAdjustedTemperature from vanilla's assembly, as its block layers, snow, trees and patches do; a
                // call there compiled with vanilla's method inlined would keep vanilla's lapse
                try
                {
                    int b = Climate.DescaleTemperature(20), climate = (b << 16) | (128 << 8);
                    int real = Climate.GetScaledAdjustedTemperature(b, dist);
                    int old = Math.Clamp((int)((b - dist / 1.5f) / Climate.TemperatureScaleConversion) - 20, -20, 40);
                    var config = new Vintagestory.ServerMods.NoObf.BlockPatchConfig();
                    bool Fits(int t) => config.IsPatchSuitableAt(new Vintagestory.ServerMods.NoObf.BlockPatch { MinTemp = t, MaxTemp = t }, sapi.World.Blocks[0], sapi.WorldManager.MapSizeY,
                        climate, core.Model.SeaLevel + dist, 0.5f, 0.5f);
                    Log("climate rules v{0}: vanilla's patch placement {1} blocks up takes {2} °C with the real lapse (vanilla's lapse: {3} °C) ({4})",
                        core.Params.ClimateRules, dist, Fits(real) ? real.ToString() : "not " + real, old, Fits(real) && (real == old || !Fits(old)) ? "PASS" : "FAIL");
                }
                catch (Exception e)
                {
                    Log("climate rules v{0}: vanilla's patch placement could not be checked: {1}", core.Params.ClimateRules, e.Message);
                }
            }
            Log(StoryPlacer.Describe(sapi, core));
            Log("ore rules: enabled={0}/v{1}, contact half-width={2}, active geology rock-layer rules=v{3}", core.Params.RegionalOres, core.Params.OreRulesVersion,
                core.Params.GeologyTransitionBlocks, sapi.ModLoader.GetModSystem<EarthGeology>()?.ActiveRulesVersion ?? 0);
            var earthOres = sapi.ModLoader.GetModSystem<EarthOres>();
            if (earthOres?.Active == true)
            {
                var bog = sapi.World.GetBlock(new AssetLocation("earth:bogpeat"));
                var drops = bog.GetDrops(sapi.World, new BlockPos(0), null);
                bool iron = sapi.World.GetItem(new AssetLocation("game:stone-bogiron")) != null;
                bool correct = drops.Sum(d => d.Collectible.Code.ToString() == "game:peatbrick" ? d.StackSize : 0) == 2
                    && drops.Sum(d => d.Collectible.Code.ToString() == "game:stone-bogiron" ? d.StackSize : 0) == (iron ? 1 : 0)
                    && drops.All(d => d.Collectible.Code.ToString() != "earth:bogpeat");
                // The provider's rocky bog iron is only switched off while Earth's peat-based bog iron replaces it.
                bool rockyBogDisabled = earthOres.Session?.BogPeatActive != true
                    || sapi.ModLoader.GetModSystem<GenDeposits>().Deposits.Where(v => v.Code is "bogironlarge" or "bogironsmall").All(v => v.TriesPerChunk == 0);
                Log("bog peat adapter: {0}; iron provider={1}, rocky bog disabled={2}, drops={3}", correct && rockyBogDisabled ? "PASS" : "FAIL", iron, rockyBogDisabled,
                    string.Join(",", drops.Select(d => d.Collectible.Code + "=" + d.StackSize)));
            }
            var places = new List<(string name, double x, double z)>();
            foreach (string place in cfg.Places)
            {
                if (TryResolve(place, out double x, out double z)) places.Add((place, x, z));
                else Log("could not resolve place '{0}'", place);
            }
            try
            {
                if (cfg.RenderWorldMap)
                {
                    string map = core.Commands.RenderWorldMap(256, out string msg);
                    Log(msg);
                }
            }
            catch (Exception e)
            {
                Log("world map failed: {0}", e);
            }
            RunNext(places, 0);
        }

        void VerifyVerticalProfile()
        {
            var p = core.Params;
            var cw = CylinderCompat.TryGet(sapi);
            bool seaOk = sapi.World.SeaLevel == p.SeaLevel && Climate.Sealevel == p.SeaLevel
                && Vintagestory.ServerMods.TerraGenConfig.seaLevel == p.SeaLevel && cw?.Get("SeaLevel") == p.SeaLevel;
            Log("vertical sea agreement: {0}; Earth={1}, Cylinder={2}", seaOk ? "PASS" : "FAIL", p.SeaLevel, cw?.Get("SeaLevel"));
            // decision 0045: vanilla's sea-level deposits are scaled for the sea level in use, not the one GenTerra set first
            var (deposits, mismatched) = SealevelDeposits.Check(sapi);
            Log("deposit sea agreement: {0}; {1} sea-level deposit generator(s), {2} not scaled for sea level {3}; Init hook {4}",
                mismatched == 0 && SealevelDeposits.Hooked ? "PASS" : "FAIL", deposits, mismatched, Vintagestory.ServerMods.TerraGenConfig.seaLevel,
                SealevelDeposits.Hooked ? "on" : "off");
            // rules 2 has its own check above; the test profiles' rules 3 also read the real rain back unadjusted
            if (p.ClimateRules < TerrainModel.RealRainRules) return;
            bool climateOk = true;
            foreach (var point in new[] { (35.36, 138.73), (27.99, 86.93), (9.03, 38.74), (56.95, 24.10) })
            {
                core.Model.Geometry.FromGeo(point.Item1, point.Item2, out double x, out double z);
                int y = (int)core.Model.Vertical.Y(Math.Max(0, core.Data.SampleTerrain(point.Item1, point.Item2).Elev));
                int pixel = core.Model.ClimatePixel(x, z, 0, 1, 1);
                float actual = Climate.GetScaledAdjustedTemperatureFloat((pixel >> 16) & 255, y - p.SeaLevel);
                double expected = Math.Clamp(core.Model.TrueTemperature(x, z, y), -20, 40);
                float client = Climate.GetScaledAdjustedTemperatureFloatClient((pixel >> 16) & 255, y - p.SeaLevel);
                double expectedClient = Math.Clamp(core.Model.TrueTemperature(x, z, y), -50, 40);
                int rainByte = (pixel >> 8) & 255;
                bool ok = Math.Abs(actual - expected) < .4 && Math.Abs(client - expectedClient) < .4
                    && Climate.GetRainFall(rainByte, y) == rainByte;
                climateOk &= ok;
                Log("vertical climate {0},{1}: helper={2:0.00}, Earth={3:0.00}, {4}", point.Item1, point.Item2, actual, expected, ok ? "PASS" : "FAIL");
            }
            Log("vertical climate helpers: {0}", climateOk ? "PASS" : "FAIL");
        }

        bool TryResolve(string place, out double x, out double z)
        {
            x = z = 0;
            if (place == "spawn")
            {
                x = core.Params.SpawnX; z = core.Params.SpawnZ;
                return true;
            }
            if (place == "jumpline")
            {
                // right on the (west) jump line in the Bering Strait: Cylinder World's seam band on top of our gutter
                var geo = core.Model.Geometry;
                geo.FromGeo(65.8, -168.5, out _, out z);
                x = geo.RingStart + 8;
                return true;
            }
            var parts = place.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            {
                core.Model.Geometry.FromGeo(lat, lon, out x, out z);
                return true;
            }
            var story = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.GenStoryStructures>();
            var loc = story?.Structures?.Get(place);
            if (loc == null) return false;
            x = loc.CenterPos.X; z = loc.CenterPos.Z;
            return true;
        }

        void RunNext(List<(string name, double x, double z)> places, int index)
        {
            if (index >= places.Count)
            {
                Finish();
                return;
            }
            var (name, x, z) = places[index];
            int r = core.Config.SelfTest.RadiusChunks;
            int ccx = (int)Math.Floor(x / 32), ccz = (int)Math.Floor(z / 32);
            int cx0 = ccx - r, cz0 = ccz - r, cx1 = ccx + r, cz1 = ccz + r;
            int total = (2 * r + 1) * (2 * r + 1), loaded = 0;
            bool done = false;
            var started = DateTime.UtcNow;
            void Analyze()
            {
                if (done) return;
                done = true;
                try
                {
                    AnalyzeArea(name, cx0, cz0, cx1, cz1, (DateTime.UtcNow - started).TotalSeconds);
                }
                catch (Exception e)
                {
                    Log("{0}: analysis failed: {1}", name, e);
                }
                try
                {
                    GroundCensus(name, cx0, cz0, cx1, cz1);
                }
                catch (Exception e)
                {
                    Log("{0}: ground census failed: {1}", name, e);
                }
                try
                {
                    CheckSurfaceColumns(name, cx0, cz0, cx1, cz1);
                }
                catch (Exception e)
                {
                    Log("{0}: surface column check failed: {1}", name, e);
                }
                try
                {
                    if (core.Config.SelfTest.MeasureStorage) MeasureStorage(name, cx0, cz0, cx1, cz1);
                }
                catch (Exception e)
                {
                    Log("{0}: storage measurement failed: {1}", name, e);
                }
                try
                {
                    if (core.Config.SelfTest.Digest) WriteDigest(name, cx0, cz0, cx1, cz1);
                }
                catch (Exception e)
                {
                    Log("{0}: digest failed: {1}", name, e);
                }
                sapi.Event.RegisterCallback(_ => RunNext(places, index + 1), 100);
            }
            if (core.Config.SelfTest.Digest)
            {
                // The equivalence check needs the same world every run. Asked for all at once, the columns are generated
                // back to back on the chunk thread, but a finished column only counts as loaded once the main thread has
                // taken it in, so the next column's neighbour check sometimes finds it missing and sometimes not, and
                // trees and plants along the border come out differently (8 Nov: 11 of 20 places differed between two
                // runs of one build). So: one column at a time, the next asked for from the previous one's OnLoaded (run
                // on the main thread after the game took the column in), and kept loaded so the idle unloader never
                // drops a column a later neighbour reads. Equivalence runs also use one generation thread
                // (--reducedthreads).
                var order = new List<(int cx, int cz)>();
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cz = cz0; cz <= cz1; cz++)
                        order.Add((cx, cz));
                void Request(int k)
                {
                    if (k >= order.Count) { Analyze(); return; }
                    sapi.WorldManager.LoadChunkColumnPriority(order[k].cx, order[k].cz, new ChunkLoadOptions
                    {
                        KeepLoaded = true,
                        OnLoaded = () =>
                        {
                            loaded++;
                            sapi.Event.EnqueueMainThreadTask(() => Request(k + 1), "earth-selftest");
                        }
                    });
                }
                Request(0);
            }
            else
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions
                        {
                            OnLoaded = () =>
                            {
                                loaded++;
                                if (loaded >= total) sapi.Event.EnqueueMainThreadTask(Analyze, "earth-selftest");
                            }
                        });
                    }
                }
            }
            // don't hang forever if a chunk never reports back
            sapi.Event.RegisterCallback(_ => { if (!done) { Log("{0}: timed out with {1}/{2} chunk columns", name, loaded, total); Analyze(); } }, 240000);
        }

        /// <summary>A highest height for the report: "none" where no dry land was sampled (a place at sea).</summary>
        static string Highest(double metres) => metres == double.MinValue ? "none" : metres.ToString("F0", CultureInfo.InvariantCulture);

        /// <summary>0053: the included GLO-90 heights over an area: dry-land columns on them and how far the columns stand from
        /// the grid (the sub-cell noise and restored peaks), and the rivers' water against the grid.</summary>
        void ReportCoarse(string name, int cx0, int cz0, int cx1, int cz1)
        {
            var model = core.Model;
            var coarse = model.Coarse;
            if (coarse == null) return;
            int land = 0, used = 0, river = 0;
            double sum = 0, sq = 0, maxCol = double.MinValue, maxGrid = double.MinValue, riverBelow = 0;
            for (int x = cx0 * 32; x < (cx1 + 1) * 32; x += 2)
            {
                for (int z = cz0 * 32; z < (cz1 + 1) * 32; z += 2)
                {
                    var c = model.GetColumn(x, z);
                    if (c.Gutter) continue;
                    if (c.River && coarse.TryHeight(c.Lat, c.Lon, out double rg)) { river++; riverBelow += rg - model.Vertical.Elevation(c.WaterTop + 1); }
                    if (c.Water != TerrainModel.WaterNone || c.River || c.NearRiver) continue;
                    land++;
                    if (!coarse.TryHeight(c.Lat, c.Lon, out double g)) continue;
                    used++;
                    double d = c.Elev - g;
                    sum += d; sq += d * d;
                    maxCol = Math.Max(maxCol, c.Elev); maxGrid = Math.Max(maxGrid, g);
                }
            }
            Log("{0}: included GLO-90 heights ({1:F0} m cells) on {2} of {3} dry-land columns sampled; column - grid mean {4:F1} m, spread {5:F1} m; " +
                "highest column {6} m, grid {7} m; {8} river columns, their water on average {9:F0} m below the grid there",
                name, coarse.CellMetres, used, land, used > 0 ? sum / used : 0, used > 0 ? Math.Sqrt(Math.Max(0, sq / used - (sum / used) * (sum / used))) : 0,
                Highest(maxCol), Highest(maxGrid), river, river > 0 ? riverBelow / river : 0);
        }

        /// <summary>Decision 0051: how much of the area's dry land took GLO-90's heights, against the built-in ones, and a
        /// west-east profile of the column tops through the middle.</summary>
        void ReportFine(string name, int cx0, int cz0, int cx1, int cz1)
        {
            var model = core.Model;
            var fine = model.Fine;
            if (fine == null) return;
            int land = 0, used = 0;
            double maxFine = double.MinValue, maxBuilt = double.MinValue, diff = 0, offset = 0;
            for (int x = cx0 * 32; x < (cx1 + 1) * 32; x += 2)
            {
                for (int z = cz0 * 32; z < (cz1 + 1) * 32; z += 2)
                {
                    var c = model.GetColumn(x, z);
                    if (c.Water != TerrainModel.WaterNone || c.River || c.Gutter) continue;
                    land++;
                    var s = core.Data.SampleTerrain(c.Lat, c.Lon);
                    maxBuilt = Math.Max(maxBuilt, s.Elev);
                    if (!fine.TryHeight(c.Lat, c.Lon, out double f)) continue;
                    used++;
                    maxFine = Math.Max(maxFine, f);
                    diff += Math.Abs(f - s.Elev);
                    offset += Math.Abs(c.Elev - f);
                }
            }
            Log("{0}: GLO-90 heights on {1} of {2} dry-land columns sampled; highest GLO-90 {3} m, built-in {4} m; mean |GLO-90 - built-in| {5:F0} m; " +
                "mean |column - GLO-90| {6:F1} m; lookups {7}, tiles read {8}, looks for absent tiles {12}, tiles fetched {9}, failed {10}, squares on built-in heights {11}",
                name, used, land, Highest(maxFine), Highest(maxBuilt), used > 0 ? diff / used : 0, used > 0 ? offset / used : 0, fine.Lookups, fine.Loads,
                sapi.ModLoader.GetModSystem<EarthElevationSystem>()?.Downloader?.TilesStored ?? 0,
                sapi.ModLoader.GetModSystem<EarthElevationSystem>()?.Downloader?.TilesFailed ?? 0, fine.FallbackKeys.Length, fine.Absent);
            // rules 2: the listed peaks looked at in this area, lifted or not
            foreach (var p in model.FineSummits)
            {
                if (p.X < cx0 * 32 || p.X >= (cx1 + 1) * 32 || p.Z < cz0 * 32 || p.Z >= (cz1 + 1) * 32) continue;
                var c = model.GetColumn(p.X, p.Z);
                Log("{0}: summit {1} ({2:F0} m surveyed): GLO-90 top {3:F0} m, {4:F0} m from the catalogue's point, at x={5} z={6}; {7}; " +
                    "column there {8:F0} m, top y {9}",
                    name, p.Name, p.Surveyed, p.Top, p.Distance, p.X, p.Z, p.Lift > 0 ? $"lifted {p.Lift:F0} m" : "not lifted: " + p.Skipped, c.Elev, c.Top);
            }
            int zMid = (cz0 + cz1 + 1) * 16;
            var tops = new List<string>();
            for (int x = cx0 * 32; x < (cx1 + 1) * 32; x += 16)
            {
                var c = model.GetColumn(x, zMid);
                tops.Add(c.Water != TerrainModel.WaterNone ? "~" + c.WaterTop : c.Top.ToString(CultureInfo.InvariantCulture));
            }
            Log("{0}: column tops west to east every 16 blocks at z {1} (~ water top): {2}", name, zMid, string.Join(" ", tops));
        }

        /// <summary>
        /// Equivalence check (SelfTest.Digest): a SHA-256 of each chunk column of the place, every block id and fluid id of
        /// every chunk from the bottom up plus the map chunk's two height maps, written one line per column to
        /// EarthMap/digest-place.txt, and one hash over the place in the report. Two builds generate the same world when
        /// every hash is the same (with the same mods, world settings and seed, so block ids match).
        /// </summary>
        void WriteDigest(string name, int cx0, int cz0, int cx1, int cz1)
        {
            int chunksHigh = sapi.WorldManager.MapSizeY / 32;
            var lines = new StringBuilder();
            var chunkLines = new StringBuilder();
            bool dump = core.Config.SelfTest.DigestDumpPlaces?.Contains(name) == true;
            string dumpDir = Path.Combine(GamePaths.DataPath, "EarthMap");
            if (dump && !File.Exists(Path.Combine(dumpDir, "blockcodes.txt")))
            {
                Directory.CreateDirectory(dumpDir);
                File.WriteAllLines(Path.Combine(dumpDir, "blockcodes.txt"), sapi.World.Blocks.Where(b => b != null).Select(b => b.Id + " " + b.Code));
            }
            using var place = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[32 * 32 * 32 * 8];
            var canon = HashAs();
            bool snapshots = DigestPasses().Contains(EnumWorldGenPass.PreDone);
            int columns = 0, missing = 0, live = 0;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var map = sapi.WorldManager.GetMapChunk(cx, cz);
                    // raw dumps (DigestDumpPlaces) are of the live world and never change what the digest hashes
                    if (dump && map != null)
                    {
                        using var raw = new FileStream(Path.Combine(dumpDir, "dump-" + cx + "-" + cz + ".bin"), FileMode.Create);
                        for (int cy = 0; cy < chunksHigh; cy++)
                        {
                            var chunk = sapi.WorldManager.GetChunk(cx, cy, cz);
                            if (chunk == null) continue;
                            chunk.Unpack_ReadOnly();
                            var data = chunk.Data;
                            for (int i = 0; i < 32 * 32 * 32; i++)
                            {
                                BitConverter.TryWriteBytes(buffer.AsSpan(i * 8), data[i]);
                                BitConverter.TryWriteBytes(buffer.AsSpan(i * 8 + 4), data.GetFluid(i));
                            }
                            raw.Write(buffer);
                        }
                    }
                    // a column generated in this session: its hash as generation left it (HashGenerated)
                    if (generated.TryGetValue((EnumWorldGenPass.PreDone, ((long)cx << 32) | (uint)cz), out var made))
                    {
                        place.AppendData(made.Column);
                        lines.Append(cx).Append(' ').Append(cz).Append(' ').AppendLine(Convert.ToHexString(made.Column));
                        chunkLines.Append(cx).Append(' ').Append(cz).AppendLine(made.Chunks);
                        columns++;
                        continue;
                    }
                    // no snapshot (PreDone not in DigestPasses, or the column was not generated in this session): the live
                    // world, counted in the report so a comparison of generation is not taken at face value
                    using var column = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                    if (map == null) { missing++; continue; }
                    chunkLines.Append(cx).Append(' ').Append(cz);
                    for (int cy = 0; cy < chunksHigh; cy++)
                    {
                        var chunk = sapi.WorldManager.GetChunk(cx, cy, cz);
                        if (chunk == null) { column.AppendData(BitConverter.GetBytes(-1)); chunkLines.Append(" -"); continue; }
                        chunk.Unpack_ReadOnly();
                        var data = chunk.Data;
                        for (int i = 0; i < 32 * 32 * 32; i++)
                        {
                            int id = data[i];
                            BitConverter.TryWriteBytes(buffer.AsSpan(i * 8), (uint)id < (uint)canon.Length ? canon[id] : id);
                            BitConverter.TryWriteBytes(buffer.AsSpan(i * 8 + 4), data.GetFluid(i));
                        }
                        column.AppendData(buffer);
                        chunkLines.Append(' ').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer))[..8]);
                    }
                    chunkLines.AppendLine();
                    foreach (var h in new[] { map.WorldGenTerrainHeightMap, map.RainHeightMap })
                        if (h != null) foreach (ushort v in h) column.AppendData(BitConverter.GetBytes(v));
                    byte[] hash = column.GetHashAndReset();
                    place.AppendData(hash);
                    lines.Append(cx).Append(' ').Append(cz).Append(' ').AppendLine(Convert.ToHexString(hash));
                    columns++;
                    live++;
                }
            }
            string safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' ? ch : '_').ToArray());
            string file = Path.Combine(GamePaths.DataPath, "EarthMap", "digest-" + safe + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, lines.ToString());
            File.WriteAllText(Path.ChangeExtension(file, ".chunks.txt"), chunkLines.ToString());
            Log("DIGEST {0} {1} ({2} chunk columns{3}{4})", name, Convert.ToHexString(place.GetHashAndReset())[..16], columns,
                missing > 0 ? ", " + missing + " missing" : "",
                live > 0 ? ", " + live + (snapshots ? " hashed live: not generated in this session (INCOMPLETE as a generation check)" : " hashed live") : "");

            // the earlier passes: only what was hashed during generation (the live world has moved on since)
            foreach (var pass in DigestPasses().Where(p => p != EnumWorldGenPass.PreDone))
            {
                var passLines = new StringBuilder();
                var passChunks = new StringBuilder();
                int have = 0, absent = 0;
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        if (!generated.TryGetValue((pass, ((long)cx << 32) | (uint)cz), out var made)) { absent++; continue; }
                        place.AppendData(made.Column);
                        passLines.Append(cx).Append(' ').Append(cz).Append(' ').AppendLine(Convert.ToHexString(made.Column));
                        passChunks.Append(cx).Append(' ').Append(cz).AppendLine(made.Chunks);
                        have++;
                    }
                string passFile = Path.Combine(GamePaths.DataPath, "EarthMap", "digest-" + safe + "." + pass + ".txt");
                File.WriteAllText(passFile, passLines.ToString());
                File.WriteAllText(Path.ChangeExtension(passFile, ".chunks.txt"), passChunks.ToString());
                Log("DIGEST-{0} {1} {2} ({3} chunk columns{4})", pass, name, Convert.ToHexString(place.GetHashAndReset())[..16], have, absent > 0 ? ", " + absent + " not generated in this session: INCOMPLETE" : "");
            }

            // every pass's other output (MetaHash), one line per column in digest-<place>.<pass>Meta.txt
            foreach (var pass in DigestPasses())
            {
                var metaLines = new StringBuilder();
                int have = 0, absent = 0;
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        if (!generatedMeta.TryGetValue((pass, ((long)cx << 32) | (uint)cz), out var meta)) { absent++; continue; }
                        place.AppendData(meta);
                        metaLines.Append(cx).Append(' ').Append(cz).Append(' ').AppendLine(Convert.ToHexString(meta));
                        have++;
                    }
                File.WriteAllText(Path.Combine(GamePaths.DataPath, "EarthMap", "digest-" + safe + "." + pass + "Meta.txt"), metaLines.ToString());
                Log("DIGEST-{0}Meta {1} {2} ({3} chunk columns{4})", pass, name, Convert.ToHexString(place.GetHashAndReset())[..16], have, absent > 0 ? ", " + absent + " not generated in this session: INCOMPLETE" : "");
            }
        }

        /// <summary>
        /// Tall-world test: how a place's chunks are stored. Empty chunks have no block data, a one-block chunk 1 bit per
        /// block (the palette always holds air), a mixed one 2-15 bits. Unpacked is what a chunk in use takes in memory; the
        /// engine compresses idle server chunks, and compressed is also roughly what is saved and sent to clients.
        /// </summary>
        /// <summary>
        /// Ground surface census of the area (EA-0062, EA-0064): of the land columns, how many carry terra rossa or bauxite in
        /// their top 8 blocks, and the loose flint and chert on top. Columns from the terrain height map down.
        /// </summary>
        void GroundCensus(string name, int cx0, int cz0, int cx1, int cz1)
        {
            int ys = sapi.WorldManager.MapSizeY;
            long columns = 0, rossa = 0, bauxite = 0, flint = 0, chert = 0, rendzina = 0, bauxiteIce = 0, bauxiteDeep = 0;
            // decision 0077: Earth's kyanite, sillimanite and andalusite in the top 48 blocks, and their loose markers
            var alu = new SortedDictionary<string, long>(StringComparer.Ordinal);
            string aluExample = null;
            string bauxiteExample = null;
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var map = sapi.WorldManager.GetMapChunk(cx, cz);
                    if (map == null) continue;
                    for (int k = 0; k < 1024; k++)
                    {
                        int top = map.WorldGenTerrainHeightMap[k];
                        if (top < 2 || top + 1 >= ys) continue;
                        int x = cx * 32 + (k & 31), z = cz * 32 + (k >> 5);
                        columns++;
                        var above = sapi.World.BlockAccessor.GetBlock(new BlockPos(x, top + 1, z, 0));
                        string a = above?.Code?.Path ?? "";
                        if (above?.Code?.Domain == "earth" && a.StartsWith("looseores-", StringComparison.Ordinal)) alu[a.Substring(0, a.IndexOf('-', 10))] = alu.GetValueOrDefault(a.Substring(0, a.IndexOf('-', 10))) + 1;
                        for (int y = top; y > top - 48 && y > 0; y--)
                        {
                            var ob = sapi.World.BlockAccessor.GetBlock(new BlockPos(x, y, z, 0));
                            if (ob?.Code?.Domain != "earth") continue;
                            string oc = ob.Code.Path;
                            if (!(oc.StartsWith("ore-kyanite-", StringComparison.Ordinal) || oc.StartsWith("ore-sillimanite-", StringComparison.Ordinal) || oc.StartsWith("ore-andalusite-", StringComparison.Ordinal))) continue;
                            alu[oc] = alu.GetValueOrDefault(oc) + 1;
                            aluExample ??= string.Format(CultureInfo.InvariantCulture, "{0} at ={1} {2} ={3}", oc, x, y, z);
                        }
                        if (a.StartsWith("looseflints-", StringComparison.Ordinal)) flint++;
                        else if (a.StartsWith("loosestones-chert", StringComparison.Ordinal)) chert++;
                        bool r = false, b = false, g = false;
                        for (int d = 0; d < 8 && top - d > 0; d++)
                        {
                            var bl = sapi.World.BlockAccessor.GetBlock(new BlockPos(x, top - d, z, 0));
                            string c = bl?.Code?.Path ?? "";
                            if (d == 1 || d == 2) { string key = bl?.Code?.ToShortString() ?? "air"; counts[key] = counts.GetValueOrDefault(key) + 1; }
                            if (c == "terrarossa") r = true;
                            else if (c == "rock-bauxite") b = true;
                            else if (c == "gravel-limestone" && d <= 2) g = true;
                        }
                        if (r) rossa++;
                        if (b)
                        {
                            bauxite++;
                            var tc = ColumnStore.At(core.Model, x, z);
                            if (tc.IceBottom <= tc.Top) bauxiteIce++;
                            else bauxiteExample ??= string.Format(CultureInfo.InvariantCulture, "={0} {1} ={2}", x, top, z);
                        }
                        for (int y = top - 8; y > top - 60 && y > 0; y--)
                            if (sapi.World.BlockAccessor.GetBlock(new BlockPos(x, y, z, 0))?.Code?.Path == "rock-bauxite") { bauxiteDeep++; break; }
                        if (g && !r) rendzina++;
                    }
                }
            var common = string.Join(", ", counts.OrderByDescending(p => p.Value).Take(6).Select(p => p.Key + " " + p.Value.ToString(CultureInfo.InvariantCulture)));
            Log("{0}: ground census of {1} columns: terra rossa {2}, bauxite {3} ({8} under glacier ice, e.g. elsewhere {9}; {10} with bauxite 8-60 blocks down), limestone rubble under the topsoil without terra rossa {4}; loose flint {5}, loose chert {6}; blocks 1-2 down: {7}",
                name, columns, rossa, bauxite, rendzina, flint, chert, common, bauxiteIce, bauxiteExample ?? "none", bauxiteDeep);
            Log("{0}: aluminosilicates in the top 48 blocks: {1}; e.g. {2}", name,
                alu.Count == 0 ? "none" : string.Join(", ", alu.Select(p => p.Key + " " + p.Value.ToString(CultureInfo.InvariantCulture))), aluExample ?? "none");
        }

        /// <summary>Decision 0077: Earth's crafting recipes are in the world (and resolve) exactly when its crafting rules are on,
        /// and its aluminosilicate deposits exactly when its ore layers are v4 or later.</summary>
        void CheckCrafting()
        {
            var p = core.Params;
            var (count, resolved) = EarthCrafting.Census(sapi);
            bool on = p.CraftingRules > 0;
            Log("crafting rules v{0} (ground rules {1}, ore maps {2}): {3} of Earth's grid recipes in the world, {4} resolved ({5})",
                p.CraftingRules, p.GroundColumns, p.RegionalOres, count, resolved,
                on ? (count > 0 && resolved == count ? "PASS" : "FAIL") : (count == 0 ? "PASS" : "FAIL"));
            int findable = EarthCrafting.Findable(sapi);
            Log("crafting rules: {0} of Earth's grid recipes findable by the crafting grid's ingredient index ({1})", findable,
                on ? (findable > 0 ? "PASS" : "FAIL") : (findable == 0 ? "PASS" : "FAIL"));
            if (on)
            {
                // real stacks laid in a grid: a recipe of Earth's must take them, one stack per ingredient, and make the output
                foreach (var (what, output, inputs) in new (string, string, string[])[]
                {
                    ("hammer + kyanite", "earth:crushed-kyanite", new[] { "item:game:hammer-copper", "item:earth:ore-kyanite" }),
                    ("hammer + andalusite", "earth:crushed-andalusite", new[] { "item:game:hammer-copper", "item:earth:ore-andalusite" }),
                    ("hammer + fired brick", "earth:grog", new[] { "item:game:hammer-copper", "item:game:burnedbrick-red" }),
                    ("8 blue clay + grog", "game:clay-fire", new[] { "item:game:clay-blue*8", "item:earth:grog" }),
                    ("fireclay brick", "game:refractorybrick-raw-tier1", new[] { "item:game:clay-fire*3", "item:game:crushed-quartz", "item:earth:grog" }),
                    ("tier 2 with kyanite", "game:refractorybrick-raw-tier2", new[] { "item:game:clay-fire*2", "item:game:crushed-quartz", "item:earth:crushed-kyanite", "item:game:crushed-olivine" }),
                    ("tier 3 with corundum", "game:refractorybrick-raw-tier3", new[] { "item:game:clay-fire*2", "item:game:crushed-quartz", "item:game:ore-corundum", "item:game:crushed-olivine", "item:game:crushed-ilmenite" }),
                })
                {
                    string got = GridMatch(output, inputs);
                    Log("crafting rules: {0} -> {1}: {2} ({3})", what, output, got ?? "no recipe of Earth's takes it", got != null ? "PASS" : "FAIL");
                }
            }
            int tephra = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.RecipeRegistrySystem>()?.BarrelRecipes
                .Count(r => r.Name?.Domain == "earth" && r.Name.Path.Contains("mortar-tephra")) ?? -1;
            Log("volcanic-ash mortar barrel recipes: {0} ({1})", tephra, tephra > 0 ? "PASS" : "FAIL");
            var gen = sapi.ModLoader.GetModSystem<Vintagestory.ServerMods.GenDeposits>();
            var codes = (gen?.Deposits ?? Array.Empty<Vintagestory.ServerMods.DepositVariant>()).Where(v => v.fromFile == EarthOreLayers.AluminosilicateFile).Select(v => v.Code).ToList();
            bool want = OreLayerFacts.Aluminosilicates(p.OreLayers) && sapi.ModLoader.GetModSystem<EarthGeology>()?.Layers != null;
            Log("aluminosilicate deposits (ore layers v{0}): {1} ({2})", p.OreLayers, codes.Count == 0 ? "none" : string.Join(", ", codes),
                want ? (codes.Count == 3 ? "PASS" : "FAIL") : (codes.Count == 0 ? "PASS" : "FAIL"));
        }

        /// <summary>Earth's crafting recipe whose output is <paramref name="output"/> and whose ingredients take exactly these
        /// stacks ("item:domain:code" or "...*N" for N single stacks), one each; null when none does.</summary>
        string GridMatch(string output, string[] inputs)
        {
            var stacks = new List<ItemStack>();
            foreach (string spec in inputs)
            {
                string code = spec.Substring(5);
                int n = 1, star = code.IndexOf('*');
                if (star > 0) { n = int.Parse(code.Substring(star + 1), CultureInfo.InvariantCulture); code = code.Substring(0, star); }
                var item = sapi.World.GetItem(new AssetLocation(code));
                if (item == null) return null;
                for (int i = 0; i < n; i++) stacks.Add(new ItemStack(item));
            }
            foreach (var r in sapi.World.GridRecipes.Where(EarthCrafting.IsEarthCrafting))
            {
                if (r.Output?.ResolvedItemStack?.Collectible?.Code?.ToString() != output || r.ResolvedIngredients == null) continue;
                var ings = r.ResolvedIngredients.Where(i => i != null).ToList();
                if (ings.Count != stacks.Count) continue;
                var used = new bool[stacks.Count];
                bool all = true;
                foreach (var ing in ings)
                {
                    int k = Enumerable.Range(0, stacks.Count).FirstOrDefault(j => !used[j] && ing.SatisfiesAsIngredient(stacks[j], false), -1);
                    if (k < 0) { all = false; break; }
                    used[k] = true;
                }
                if (all) return r.Name + " (" + r.Output.ResolvedItemStack.StackSize + " made)";
            }
            return null;
        }

        void MeasureStorage(string name, int cx0, int cz0, int cx1, int cz1)
        {
            int ys = sapi.WorldManager.MapSizeY / 32;
            int empty = 0, single = 0, mixed = 0, missing = 0;
            long unpacked = 0, blocksZ = 0, lightZ = 0, fluidsZ = 0;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    for (int cy = 0; cy < ys; cy++)
                    {
                        var chunk = sapi.WorldManager.GetChunk(cx, cy, cz);
                        if (chunk == null) { missing++; continue; }
                        chunk.Unpack();
                        object data = chunk.Data;
                        int palette = PaletteCount(data, "blocksLayer");
                        if (palette <= 1) empty++;
                        else if (palette == 2) single++;
                        else mixed++;
                        unpacked += LayerBytes(data, "blocksLayer") + LayerBytes(data, "fluidsLayer") + LayerBytes(data, "lightLayer");
                        var args = new object[] { null, null, null, null, 2 };
                        compressInto ??= data.GetType().GetMethod("CompressInto", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        compressInto.Invoke(data, args);
                        blocksZ += ((byte[])args[0])?.Length ?? 0;
                        lightZ += (((byte[])args[1])?.Length ?? 0) + (((byte[])args[2])?.Length ?? 0);
                        fluidsZ += ((byte[])args[3])?.Length ?? 0;
                    }
                }
            }
            int columns = (cx1 - cx0 + 1) * (cz1 - cz0 + 1);
            const double MB = 1024 * 1024;
            long managed = GC.GetTotalMemory(true);
            long workingSet = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            Log("storage {0}: {1} columns x {2} chunks: {3} empty, {4} one block, {5} mixed{6}; unpacked {7:0.0} MB ({8:0.0} KB per column); compressed blocks {9:0.00} MB, light {10:0.00} MB, fluids {11:0.00} MB ({12:0.0} KB per column); server managed {13:0} MB, working set {14:0} MB, {15} chunks loaded",
                name, columns, ys, empty, single, mixed, missing > 0 ? ", " + missing + " missing" : "", unpacked / MB, unpacked / 1024.0 / columns,
                blocksZ / MB, lightZ / MB, fluidsZ / MB, (blocksZ + lightZ + fluidsZ) / 1024.0 / columns, managed / MB, workingSet / MB, sapi.WorldManager.AllLoadedChunks.Count);
        }

        MethodInfo compressInto;

        static object LayerOf(object data, string layer) =>
            data.GetType().GetField(layer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(data);

        static int PaletteCount(object data, string layer)
        {
            object l = LayerOf(data, layer);
            if (l == null) return 0;
            if (l.GetType().GetField("palette", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(l) == null) return 0;
            return (int)l.GetType().GetField("paletteCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(l);
        }

        /// <summary>Bit planes of 32·32·32 bits (4 KB each).</summary>
        static long LayerBytes(object data, string layer)
        {
            object l = LayerOf(data, layer);
            if (l == null) return 0;
            var f = l.GetType().GetField("bitsize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (int)f.GetValue(l) * 4096L;
        }

        /// <summary>
        /// EA-0071: where a later step carved the surface (a crater, a lava lake), the map's surface column
        /// (<see cref="TerrainModel.GetSurfaceColumn"/>) must be what the generated chunk shows: its rain height, give or take a
        /// block of snow or plants. Logged only where the area has such columns.
        /// </summary>
        void CheckSurfaceColumns(string name, int cx0, int cz0, int cx1, int cz1)
        {
            var model = core.Model;
            if (model?.Carved.Craters == null) return;
            int carved = 0, exact = 0, within1 = 0, worst = 0;
            string first = null;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var mc = sapi.WorldManager.GetMapChunk(cx, cz);
                    if (mc?.RainHeightMap == null) continue;
                    for (int i = 0; i < 1024; i++)
                    {
                        double x = cx * 32 + (i & 31) + 0.5, z = cz * 32 + (i >> 5) + 0.5;
                        int surface = model.GetSurfaceColumn(x, z).Top;
                        if (surface >= model.GetColumn(x, z).Top) continue;
                        carved++;
                        int diff = Math.Abs(mc.RainHeightMap[i] - surface);
                        if (diff == 0) exact++;
                        if (diff <= 1) within1++;
                        if (diff > worst) { worst = diff; first = $"{(int)x},{(int)z}: surface column {surface}, rain height {mc.RainHeightMap[i]}"; }
                    }
                }
            }
            if (carved == 0) return;
            Log("{0}: surface columns: {1} carved columns (craters, lava lakes), {2} exactly at the generated rain height, {3} within a block{4} ({5})",
                name, carved, exact, within1, worst > 1 ? "; worst " + first : "", within1 == carved ? "PASS" : "FAIL");
        }

        void AnalyzeArea(string name, int cx0, int cz0, int cx1, int cz1, double seconds)
        {
            try { ReportFine(name, cx0, cz0, cx1, cz1); }
            catch (Exception e) { Log("{0}: GLO-90 report failed: {1}", name, e.Message); }
            try { ReportCoarse(name, cx0, cz0, cx1, cz1); }
            catch (Exception e) { Log("{0}: included GLO-90 report failed: {1}", name, e.Message); }
            int w = (cx1 - cx0 + 1) * 32, h = (cz1 - cz0 + 1) * 32;
            var rgb = new byte[w * h * 3];
            var tops = new int[w * h];
            // water surface per column (-1 = none) and kind (1 fresh, 2 salt), to find water seams
            var wtop = new int[w * h];
            var wkind = new byte[w * h];
            // flowing (ramp) water on top
            var wflow = new bool[w * h];
            int rampsOk = 0, rampsBad = 0, inRiver = 0, dryInRiver = 0;
            string firstInRiver = null;
            var inRiverFamilies = new Dictionary<string, int>();
            string firstBadRamp = null;
            int columns = 0, exact = 0, within2 = 0, maxDiff = 0, water = 0, missing = 0, overflow = 0;
            int minTop = int.MaxValue, maxTop = int.MinValue;
            string firstOverflow = null;
            var heights = new List<int>();
            int mapHeight = sapi.WorldManager.MapSizeY;
            var families = new Dictionary<string, int>();
            var woods = new Dictionary<string, int>();
            var ba = sapi.World.BlockAccessor;
            var pos = new BlockPos(0);
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    var mc = sapi.WorldManager.GetMapChunk(cx, cz);
                    for (int lz = 0; lz < 32; lz++)
                    {
                        for (int lx = 0; lx < 32; lx++)
                        {
                            int px = (cx - cx0) * 32 + lx, pz = (cz - cz0) * 32 + lz;
                            int k = pz * w + px;
                            if (mc == null) { missing++; continue; }
                            int x = cx * 32 + lx, z = cz * 32 + lz;
                            int i2 = lz * 32 + lx;
                            int rain = mc.RainHeightMap[i2];
                            int wg = mc.WorldGenTerrainHeightMap[i2];
                            var c = core.Model.GetColumn(x, z);
                            columns++;
                            heights.Add(wg);
                            if (rain >= mapHeight)
                            {
                                overflow++;
                                if (firstOverflow == null)
                                {
                                    pos.Set(x, Math.Min(wg, mapHeight - 1), z);
                                    firstOverflow = string.Format(CultureInfo.InvariantCulture, "x={0} z={1} rainmap={2} terrainmap={3} model top={4} water top={5} block at terrain height {6}",
                                        x, z, rain, wg, c.Top, c.WaterTop, ba.GetBlock(pos, BlockLayersAccess.FluidOrSolid)?.Code);
                                }
                                rain = wg;
                            }
                            int diff = Math.Abs(wg - c.Top);
                            if (diff == 0) exact++;
                            if (diff <= 2) within2++;
                            maxDiff = Math.Max(maxDiff, diff);
                            minTop = Math.Min(minTop, rain);
                            maxTop = Math.Max(maxTop, rain);
                            pos.Set(x, rain, z);
                            var block = ba.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
                            string fam = Family(block);
                            bool isWater = fam == "water" || fam == "rapidwater" || fam == "saltwater" || fam == "lakeice";
                            if (isWater) water++;
                            wtop[k] = isWater ? rain : -1;
                            wkind[k] = (byte)(fam == "saltwater" ? 2 : isWater ? 1 : 0);
                            wflow[k] = isWater && block.Variant?["flow"] is string flow && flow != "still";
                            // anything solid standing in river water (a structure, a tree), apart from plants on the bed
                            if (c.River)
                            {
                                bool counted = false;
                                for (int y = c.Top + 1; y <= c.WaterTop; y++)
                                {
                                    pos.Set(x, y, z);
                                    var sb = ba.GetBlock(pos, BlockLayersAccess.Solid);
                                    if (sb == null || sb.Id == 0)
                                    {
                                        // a dry pocket below the river surface: something (a structure) placed air in the channel
                                        if (y < c.WaterTop && (ba.GetBlock(pos, BlockLayersAccess.Fluid)?.Id ?? 0) == 0) dryInRiver++;
                                        continue;
                                    }
                                    if (sb.BlockMaterial == EnumBlockMaterial.Plant || counted) continue;
                                    counted = true;
                                    inRiver++;
                                    string f = Family(sb);
                                    inRiverFamilies[f] = inRiverFamilies.GetValueOrDefault(f) + 1;
                                    firstInRiver ??= string.Format(CultureInfo.InvariantCulture, "x={0} y={1} z={2}: {3}", x, y, z, sb.Code);
                                }
                            }
                            // (pool layout 5 lays the ramps out when it settles a chunk, one column per height from the step, not
                            // as the model's own ramp heights: not checked here)
                            if (c.River && c.Ramp > 0 && core.Model.Rivers?.PoolLayout < 5)
                            {
                                // the ramp block itself (a tree may overhang it)
                                pos.Set(x, c.WaterTop + 1, z);
                                var rampBlock = ba.GetBlock(pos, BlockLayersAccess.Fluid);
                                if (rampBlock?.Variant?["flow"] is string rf && rf != "still") rampsOk++;
                                else
                                {
                                    rampsBad++;
                                    firstBadRamp ??= string.Format(CultureInfo.InvariantCulture, "x={0} z={1}: {2} at y {3}, model pool top {4} ramp {5}", x, z, rampBlock?.Code, c.WaterTop + 1, c.WaterTop, c.Ramp);
                                }
                            }
                            families[fam] = families.TryGetValue(fam, out int n) ? n + 1 : 1;
                            if (fam.StartsWith("leaves") || fam == "log")
                            {
                                string wood = block.Variant?["wood"] ?? block.Code.Path.Substring(block.Code.Path.LastIndexOf('-') + 1);
                                woods[wood] = woods.TryGetValue(wood, out int wn) ? wn + 1 : 1;
                            }
                            var (r, g, b) = Color(fam, block);
                            tops[k] = rain;
                            rgb[3 * k] = r; rgb[3 * k + 1] = g; rgb[3 * k + 2] = b;
                        }
                    }
                }
            }
            // water seams: neighbouring water columns at different heights or of different kinds (fresh vs salt); a
            // step whose upper side is flowing or falling water (a river ramp, a waterfall) is fine
            int seams = 0, rampSteps = 0;
            string firstSeam = null;
            for (int pz = 0; pz < h; pz++)
            {
                for (int px = 0; px < w; px++)
                {
                    int k = pz * w + px;
                    if (wtop[k] < 0) continue;
                    foreach (int n in new[] { px + 1 < w ? k + 1 : -1, pz + 1 < h ? k + w : -1 })
                    {
                        if (n < 0 || wtop[n] < 0) continue;
                        if (wkind[n] == wkind[k] && wtop[n] != wtop[k] && (wtop[n] > wtop[k] ? wflow[n] : wflow[k]))
                        {
                            rampSteps++;
                            continue;
                        }
                        if (wtop[n] != wtop[k] || wkind[n] != wkind[k])
                        {
                            seams++;
                            if (firstSeam == null)
                            {
                                // what lies on top of the lower of the two columns
                                int lo = wtop[n] < wtop[k] ? n : k;
                                int lx0 = cx0 * 32 + lo % w, lz0 = cz0 * 32 + lo / w;
                                var sb = new System.Text.StringBuilder();
                                for (int y = wtop[lo] + 2; y >= wtop[lo] - 1; y--)
                                {
                                    pos.Set(lx0, y, lz0);
                                    sb.Append($" y{y}:{ba.GetBlock(pos, BlockLayersAccess.Solid)?.Code?.Path}/{ba.GetBlock(pos, BlockLayersAccess.Fluid)?.Code?.Path}");
                                }
                                firstSeam = string.Format(CultureInfo.InvariantCulture, "x={0} z={1}: y {2} {3} next to y {4} {5};{6}",
                                    cx0 * 32 + px, cz0 * 32 + pz, wtop[k], wkind[k] == 2 ? "salt" : "fresh", wtop[n], wkind[n] == 2 ? "salt" : "fresh", sb);
                            }
                        }
                    }
                }
            }

            // hill shading
            for (int pz = 1; pz < h; pz++)
            {
                for (int px = 1; px < w; px++)
                {
                    int k = pz * w + px;
                    double shade = Math.Clamp(1 + ((tops[k] - tops[k - 1]) - (tops[k] - tops[k - w])) * 0.08, 0.6, 1.35);
                    for (int ch = 0; ch < 3; ch++) rgb[3 * k + ch] = (byte)Math.Clamp(rgb[3 * k + ch] * shade, 0, 255);
                }
            }
            string safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' ? ch : '_').ToArray());
            string file = Path.Combine(GamePaths.DataPath, "EarthMap", "selftest-" + safe + ".png");
            Png.WriteRgb(file, w, h, rgb);

            var g0 = core.Model.Geometry.ToGeo((cx0 + cx1 + 1) * 16, (cz0 + cz1 + 1) * 16);
            string fams = string.Join(", ", families.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + (100.0 * kv.Value / Math.Max(1, columns)).ToString("0", CultureInfo.InvariantCulture) + "%"));
            heights.Sort();
            string pct = heights.Count == 0 ? "-" : string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}/{3}/{4}", heights[0], heights[heights.Count / 10], heights[heights.Count / 2], heights[heights.Count * 9 / 10], heights[heights.Count - 1]);
            Log("{0} at {1}: {2} columns in {3:0.0}s{4}; terrain height = model in {5:0.0}% (within 2: {6:0.0}%, max diff {7}); terrain y min/10%/50%/90%/max {8}; surface y {9}..{10}; water {11:0}%; top blocks: {12}; image {13}",
                name, EarthMapSystem.FormatLatLon(g0.Lat, g0.Lon), columns, seconds, missing > 0 ? " (" + missing + " missing)" : "",
                100.0 * exact / Math.Max(1, columns), 100.0 * within2 / Math.Max(1, columns), maxDiff, pct, minTop, maxTop, 100.0 * water / Math.Max(1, columns), fams, file);
            ProbeClimate(name, (cx0 + cx1 + 1) * 16, (cz0 + cz1 + 1) * 16);
            ProbeRegionalSystems(name, (cx0 + cx1 + 1) * 16, (cz0 + cz1 + 1) * 16);
            Log("{0}: water seams {1}{2}; river ramps placed {3}, missing {4}{5}; ramp edges {6}", name, seams, firstSeam != null ? " (first: " + firstSeam + ")" : "",
                rampsOk, rampsBad, firstBadRamp != null ? " (first: " + firstBadRamp + ")" : "", rampSteps);
            Log("{0}: river columns with something solid in the water: {1}{2}{3}; dry blocks below river surfaces: {4}", name, inRiver,
                firstInRiver != null ? " (first: " + firstInRiver + ")" : "",
                inRiverFamilies.Count > 0 ? "; by kind: " + string.Join(", ", inRiverFamilies.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + "=" + kv.Value)) : "",
                dryInRiver);
            int treeCols = woods.Values.Sum();
            if (treeCols > 0)
            {
                Log("{0}: tree canopy by wood: {1}", name, string.Join(", ", woods.OrderByDescending(kv => kv.Value).Take(8)
                    .Select(kv => kv.Key + " " + (100.0 * kv.Value / treeCols).ToString("0", CultureInfo.InvariantCulture) + "%")));
            }
            if (overflow > 0) Log("{0}: {1} columns have a rain height map value above the world height, first: {2}", name, overflow, firstOverflow);
        }

        void ProbeRegionalSystems(string name, int x, int z)
        {
            var ores = sapi.ModLoader.GetModSystem<EarthOres>();
            if (ores?.Active == true)
            {
                Log("{0} regional ores: {1}", name, ores.Describe(x,z));
                Log("{0} ore generation placement totals (cumulative, not independent deposits): {1}", name, ores.GenerationTotals());
                var column = new int[sapi.WorldManager.MapSizeY];
                for (int y = 0; y < column.Length; y++) column[y] = sapi.World.BlockAccessor.GetBlockId(new BlockPos(x, y, z));
                var gen = sapi.ModLoader.GetModSystem<GenDeposits>();
                int checkedReadings = 0;
                foreach (var v in gen.Deposits.Where(v => v.GeneratorInst is EarthOreGenerator))
                {
                    v.GeneratorInst.GetPropickReading(new BlockPos(x, 1, z), 128, column, out double ppt, out double factor);
                    if (!double.IsFinite(ppt) || !double.IsFinite(factor) || ppt < 0 || factor < 0) throw new InvalidOperationException("Invalid prospecting expectation for " + v.Code);
                    checkedReadings++;
                }
                Log("{0} ore prospecting adapters: PASS ({1} finite nonnegative readings)", name, checkedReadings);
            }
            var geology = sapi.ModLoader.GetModSystem<EarthGeology>();
            if (geology?.Active == true)
            {
                Log("{0} geology: {1}", name, geology.Describe(x, z));
                var region = sapi.WorldManager.GetMapRegion(x / sapi.WorldManager.RegionSize, z / sapi.WorldManager.RegionSize);
                if (region?.OreMaps != null && region.OreMaps.TryGetValue("halite", out var salt) && salt?.Data != null)
                {
                    Log("{0} halite prospecting map: {1}/{2} positive cells, maximum native value {3}", name,
                        salt.Data.Count(value => value > 0), salt.Data.Length, salt.Data.Max());
                }
                // Sample actual generated strata, not just the catalogue's proposed profile.
                var mc = sapi.WorldManager.GetMapChunk(x / 32, z / 32);
                if (mc != null)
                {
                    int index = (z % 32) * 32 + x % 32;
                    int top = Math.Min(mc.WorldGenTerrainHeightMap[index], sapi.WorldManager.MapSizeY - 1);
                    int mapped = mc.TopRockIdMap[index];
                    var runs = new List<string>();
                    string previous = null;
                    int runTop = top, runBottom = top;
                    var pos = new BlockPos(0);
                    void Flush()
                    {
                        if (previous != null) runs.Add(previous + " y=" + runBottom + ".." + runTop);
                    }
                    for (int y = top; y > 0; y--)
                    {
                        pos.Set(x, y, z);
                        string code = sapi.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid)?.Code?.ToString() ?? "missing";
                        if (code != previous)
                        {
                            Flush(); previous = code; runTop = y;
                        }
                        runBottom = y;
                    }
                    Flush();
                    Log("{0} generated column: TopRockIdMap={1}; {2}", name,
                        sapi.World.GetBlock(mapped)?.Code?.ToString() ?? "invalid", string.Join("; ", runs.Take(24)));
                }
            }
            var plants = sapi.ModLoader.GetModSystem<EarthPlants>();
            if (plants?.Active == true) Log("{0} plants: {1}", name, plants.Describe(x, z));
            var placement = sapi.ModLoader.GetModSystem<EarthPlacement>();
            if (placement?.Active == true)
            {
                var surface = new BlockPos((int)x, 0, (int)z, 0);
                surface.Y = sapi.World.BlockAccessor.GetTerrainMapheightAt(surface);
                Log("{0} placement: {1}", name, placement.Describe(x, surface.Y, z).Replace('\n', ' '));
                Log("{0} census: {1}", name, placement.Census(surface, (core.Config.SelfTest.RadiusChunks + 1) * 32).Replace('\n', ' '));
            }
            var fauna = sapi.ModLoader.GetModSystem<EarthFaunaSystem>();
            if (fauna?.Active == true)
            {
                Log("{0} fauna: {1}", name, fauna.Describe(x, z));
                int radius = (core.Config.SelfTest.RadiusChunks + 1) * 32;
                var actual = sapi.World.LoadedEntities.Values.Where(e => e.Code != null
                    && Math.Abs(e.Pos.X - x) < radius && Math.Abs(e.Pos.Z - z) < radius).ToArray();
                var controlled = actual.Where(e => fauna.Catalog.Controls(e.Code.ToString())).ToArray();
                int violations = controlled.Count(e => !fauna.AllowsAt(e.Code, e.Pos.X, e.Pos.Z));
                // Existing entities may legitimately walk across a range boundary after spawning.
                Log("{0} loaded animals/entities: {1}; controlled fauna={2}, outside current range={3}; {4}", name,
                    actual.Length, controlled.Length, violations,
                    string.Join(", ", controlled.GroupBy(e => e.Code.ToString()).OrderByDescending(g => g.Count()).Take(12)
                        .Select(g => g.Key + "=" + g.Count())));
            }
        }

        /// <summary>Climate as the game reports it at the surface (worldgen values and live values), next to the real data.</summary>
        void ProbeClimate(string name, int x, int z)
        {
            var mc = sapi.WorldManager.GetMapChunk(x / 32, z / 32);
            if (mc == null) return;
            int y = mc.RainHeightMap[(z % 32) * 32 + x % 32];
            if (y >= sapi.WorldManager.MapSizeY) y = mc.WorldGenTerrainHeightMap[(z % 32) * 32 + x % 32];
            var pos = new BlockPos(x, y + 1, z);
            var wg = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            var now = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
            var g = core.Model.Geometry.ToGeo(x, z);
            core.Model.ClimateAt(g.Lat, g.Lon, out double tReal, out double rainReal, out _);
            double tTrue = core.Model.TrueTemperature(x, z, y + 1);
            Log("{0} climate at y={1}: game worldgen {2:0.0} °C, now {3:0.0} °C (month {4}), rain {5:0.00}, fertility {6:0.00}, forest {7:0.00}; real yearly mean {8:0.0} °C at the surface, {9:0.0} °C at this height, VS rain {10:0}",
                name, y + 1, wg?.WorldGenTemperature ?? float.NaN, now?.Temperature ?? float.NaN, sapi.World.Calendar.MonthName, wg?.WorldgenRainfall ?? float.NaN,
                wg?.Fertility ?? float.NaN, wg?.ForestDensity ?? float.NaN, tReal, tTrue, rainReal);
            // climate rules v2 (decision 0009): vanilla's reading with the real lapse, plus Earth's cuts undone, is the real
            // temperature at this height; a vanilla path that skipped the patched adjustment would be off by the lapse difference
            if (core.Params.ClimateRules >= TerrainModel.RealLapseRules && core.Params.ExtendTemperature && wg != null)
            {
                // vanilla's story structures force their own climate around them (storystructures.json forceTemperature: the
                // village 10 °C): there the climate map holds that value, not Earth's, so the expectation is vanilla's forced
                // temperature with the real lapse; in the blend ring around it (out to twice the radius) nothing is compared
                int forced = ForcedClimate(x, z, out bool blended);
                if (blended)
                    Log("{0} climate rules v2: game worldgen {1:0.0} °C, real {2:0.0} °C at this height; in the ring where vanilla blends a story " +
                        "structure's forced climate into Earth's: not compared", name, wg.WorldGenTemperature, tTrue);
                else if (forced >= 0)
                {
                    double expect = Math.Clamp(forced / Climate.TemperatureScaleConversion - 20 - core.Model.LapseAt(y + 1), -20, 40) + core.Model.ClimateCorrection(x, z, y + 1);
                    Log("{0} climate rules v2: game worldgen {1:0.0} °C, vanilla's forced story climate ({2:0.0} °C at sea level) with the real lapse {3:0.0} °C " +
                        "(Earth's real {4:0.0} °C does not apply here) ({5})", name, wg.WorldGenTemperature, forced / Climate.TemperatureScaleConversion - 20,
                        expect, tTrue, Math.Abs(wg.WorldGenTemperature - expect) < 1 ? "PASS" : "FAIL");
                }
                else
                    Log("{0} climate rules v2: game worldgen {1:0.0} °C, real {2:0.0} °C at this height ({3})",
                        name, wg.WorldGenTemperature, tTrue, Math.Abs(wg.WorldGenTemperature - tTrue) < 1 ? "PASS" : "FAIL");
            }
            // seasons: mid-January and mid-July at 10:00 (about the day's mean in vanilla's daily curve)
            var cal = sapi.World.Calendar;
            double dpy = cal.DaysPerYear, year0 = Math.Floor(cal.TotalDays / dpy) * dpy;
            var jan = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, year0 + dpy * 0.5 / 12 + 10 / 24.0);
            var jul = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, year0 + dpy * 6.5 / 12 + 10 / 24.0);
            var cs = core.Data.SampleClimate(g.Lat, g.Lon);
            double lapse = tReal - cs.TMean;
            Log("{0} seasons: game January {1:0.0} °C, July {2:0.0} °C; real coldest month {3:0.0} °C, warmest {4:0.0} °C; summer drought {5:0.00}",
                name, jan?.Temperature ?? float.NaN, jul?.Temperature ?? float.NaN, cs.TCold + lapse, cs.TWarm + lapse, cs.DrySummer);
        }

        /// <summary>
        /// The temperature byte a vanilla story structure forces at a column (GenMaps.ForceClimateAt, from storystructures.json
        /// forceTemperature), -1 where none does; <paramref name="blended"/> in the ring beyond its radius (out to twice it),
        /// where vanilla blends the forced value into the climate map's own.
        /// </summary>
        int ForcedClimate(int x, int z, out bool blended)
        {
            blended = false;
            var maps = sapi.ModLoader.GetModSystem<GenMaps>();
            var list = typeof(GenMaps).GetField("forceClimate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(maps)
                as List<ForceClimate>;
            if (list == null) return -1;
            foreach (var f in list)
            {
                if (f?.CenterPos == null) continue;
                double d = Math.Sqrt((double)(x - f.CenterPos.X) * (x - f.CenterPos.X) + (double)(z - f.CenterPos.Z) * (z - f.CenterPos.Z));
                if (d <= f.Radius) return (f.Climate >> 16) & 0xFF;
                if (d < 2 * f.Radius) blended = true;
            }
            return -1;
        }

        static string Family(Block block)
        {
            if (block == null || block.Code == null) return "air";
            string path = block.Code.Path;
            int dash = path.IndexOf('-');
            return dash > 0 ? path.Substring(0, dash) : path;
        }

        static (byte, byte, byte) Color(string fam, Block block)
        {
            switch (fam)
            {
                case "water":
                case "rapidwater": return (60, 110, 210);
                case "saltwater": return (30, 70, 160);
                case "lakeice": return (190, 220, 245);
                case "glacierice": case "packedglacierice": return (225, 240, 255);
                case "snowblock": case "snowlayer": return (245, 248, 252);
                case "soil": return block.Code.Path.Contains("high") ? ((byte)70, (byte)130, (byte)50) : block.Code.Path.Contains("low") || block.Code.Path.Contains("verylow") ? ((byte)120, (byte)140, (byte)70) : ((byte)95, (byte)140, (byte)60);
                case "forestfloor": return (60, 100, 45);
                case "sand": case "sandwavy": return (220, 205, 150);
                case "gravel": return (150, 145, 140);
                case "muddygravel": return (110, 105, 90);
                case "rock": case "crackedrock": return (125, 120, 118);
                case "log": case "logsection": case "leaves": case "leavesbranchy": case "leavesnarrow": return (35, 85, 35);
                case "tallgrass": return (120, 170, 70);
                case "wallice": return (170, 230, 250);
                case "glacier": return (215, 235, 250);
                case "mantle": return (40, 20, 20);
                default: return (170, 150, 120);
            }
        }

        /// <summary>
        /// Earth's weather in the running game (decision 0017): the patches are in, vanilla's precipitation, live rainfall,
        /// wind lookups and region winds are Earth's, cloud patterns follow Earth's cloud cover (desert vs. a wet coast), and
        /// over the year the monsoon reaches Mumbai through the game's own precipitation calls.
        /// </summary>
        void CheckWeather()
        {
            var weather = EarthWeather.For(EnumAppSide.Server);
            if (core.Params?.Weather != true) { Log("weather: vanilla weather in this world (its Earth tab, or earth.json for worlds older than the tab)"); return; }
            var m = weather?.Model;
            Log("weather: model {0}; patches: {1}; wind {2}; Sea: {3}; rules v{4}, data {5}, local sun {6}", m != null ? "built" : "MISSING", string.Join(", ", EarthWeather.Active),
                weather?.ControlsWind == true ? "Earth's" : "an older Sea's", weather?.SeaLink ?? "-", core.Params.WeatherRules, core.Params.WeatherDataSignature ?? "-",
                core.Params.WeatherLocalSun);
            if (m == null) { Log("weather: FAIL no model"); return; }
            var ws = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.WeatherSystemServer>();
            var geo = core.Model.Geometry;
            double hours = sapi.World.Calendar.TotalHours, hpd = sapi.World.Calendar.HoursPerDay;
            int sea = sapi.World.SeaLevel;
            int precipOk = 0, precipN = 0, climOk = 0, climN = 0, windOk = 0, regionOk = 0, regionN = 0;
            foreach (var (lat, lon) in new[] { (51.5, -0.1), (19.1, 72.9), (1.35, 103.8), (60.4, 5.3), (23.0, 5.5), (-33.9, 151.2), (40.4, -3.7), (35.7, 139.7) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                double want = Math.Max(0, m.Cloudness(x, z, hours) - 0.5);
                double got = ws.GetPrecipitation(x, sea + 5, z, hours / hpd);
                precipN++;
                if (Math.Abs(got - want) < 1e-4) precipOk++;
                var clim = sapi.World.BlockAccessor.GetClimateAt(new BlockPos((int)x, sea + 5, (int)z), EnumGetClimateMode.ForSuppliedDate_TemperatureRainfallOnly, hours / hpd);
                double wantRain = Math.Clamp(m.Cloudness((int)x + 0.5, (int)z + 0.5, hours) - 0.5, 0, 1);
                // (no climate where the map region isn't loaded)
                if (clim != null) climN++;
                if (clim != null && Math.Abs(clim.Rainfall - wantRain) < 1e-3) climOk++;
                // a vector (open sea contracts section 5): X east, Z south, its length Earth's strength at that height
                var wNow = m.WindAt(x, z, sapi.World.Calendar.TotalHours, true);
                var wantWind = WeatherContract.VanillaVector(wNow, EarthWeather.AltitudeScale(wNow.Strength, sea + 5, sea));
                var gotWind = sapi.World.BlockAccessor.GetWindSpeedAt(new Vec3d(x, sea + 5, z));
                if (weather.ControlsWind && Math.Abs(gotWind.X - wantWind.X) < 1e-3 && Math.Abs(gotWind.Z - wantWind.Z) < 1e-3) windOk++;
                int rs = sapi.World.BlockAccessor.RegionSize;
                var region = ws.getOrCreateWeatherSimForRegion((int)(x / rs), (int)(z / rs));
                if (region != null)
                {
                    regionN++;
                    double want2 = EarthWeather.AltitudeScale(m.WindAt(((int)(x / rs) + 0.5) * rs, ((int)(z / rs) + 0.5) * rs, sapi.World.Calendar.TotalHours, false).Strength, sea, sea);
                    if (Math.Abs(region.GetWindSpeed(sea) - want2) < 1e-3) regionOk++;
                }
            }
            // the live rainfall is only there where a map region is loaded: none before the first place (a headless run), which
            // is nothing to compare, not a failure
            Log("weather: {0} vanilla precipitation readings of {1} are Earth's rain ({2}); live rainfall {3} of {4} places with a loaded map region ({5}); " +
                "wind lookups {6} of {1}{7}; region winds {8} of {9}",
                precipOk, precipN, precipOk == precipN ? "PASS" : "FAIL", climOk, climN, climN == 0 ? "none loaded, not compared" : climOk == climN ? "PASS" : "FAIL", windOk,
                weather.ControlsWind ? "" : " (an older Sea has the wind)", regionOk, regionN);

            // through the game's own calls: the monsoon reaches Mumbai in July, January stays dry
            geo.FromGeo(19.1, 72.9, out double mx, out double mz);
            double year = sapi.World.Calendar.DaysPerYear;
            int janRain = 0, julRain = 0, n = 600;
            var rnd = new Random(5);
            for (int i = 0; i < n; i++)
            {
                double y = 3 + rnd.Next(200);
                if (ws.GetPrecipitation(mx, sea + 5, mz, (y + (0.2 + 0.6 * rnd.NextDouble()) / 12) * year) > 0) janRain++;
                if (ws.GetPrecipitation(mx, sea + 5, mz, (y + (6.2 + 0.6 * rnd.NextDouble()) / 12) * year) > 0) julRain++;
            }
            // the shares are the real climate's at the Earth tab's rain frequency 1; with rain off none may fall, with another
            // frequency they are only reported
            double rainFrequency = core.Params.RainFrequency;
            Log("weather: Mumbai rains {0:0.0}% of January and {1:0.0}% of July through vanilla's precipitation ({2})", janRain * 100.0 / n, julRain * 100.0 / n,
                rainFrequency == 0 ? (julRain == 0 && janRain == 0 ? "PASS: rain off" : "FAIL: rain off")
                : rainFrequency != 1 ? "rain x" + rainFrequency.ToString(CultureInfo.InvariantCulture) + ", not compared"
                : julRain > n / 4 && janRain < n / 30 ? "PASS" : "FAIL");

            // cloud patterns (rules 7): a weather region in Egypt's Western Desert picks fair-weather patterns more often than one
            // in western Norway. A region's pick weighs vanilla's chances by Earth's cloud cover at its centre then
            // (EarthWeather.RandomWeatherPatternPrefix, CoverWeight); this is the share of fair patterns those weights give, at 24
            // moments through the coming year. Both points are among the self test's places (Places), so their regions are
            // loaded, and both on land. (Before 2.0.0 it drew patterns at the one moment the test ran, its desert point in the
            // Persian Gulf: a clear spell on the coast that May made it fail.)
            double daysNow = sapi.World.Calendar.TotalDays, daysPerYear = sapi.World.Calendar.DaysPerYear, modelHoursPerDay = m.Settings.HoursPerDay;
            const int fairMoments = 24;
            double Fair(double lat, double lon)
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                int rs = sapi.World.BlockAccessor.RegionSize;
                var region = ws.getOrCreateWeatherSimForRegion((int)(x / rs), (int)(z / rs));
                var patterns = region?.WeatherPatterns;
                if (patterns == null || patterns.Length == 0) return double.NaN;
                var cond = region.weatherData?.climateCond;
                double share = 0;
                for (int k = 0; k < fairMoments; k++)
                {
                    double cover = m.CloudCover((region.regionX + 0.5) * rs, (region.regionZ + 0.5) * rs, (daysNow + k * daysPerYear / fairMoments) * modelHoursPerDay);
                    double fair = 0, all = 0;
                    foreach (var pattern in patterns)
                    {
                        pattern.updateHereChance(cond?.Rainfall ?? 0, cond?.Temperature ?? 15);
                        double w = Math.Max(0, pattern.hereChance) * EarthWeather.CoverWeight(pattern.config?.Code, cover);
                        all += w;
                        if (EarthWeather.CoverWeight(pattern.config?.Code, 0) > 1.5) fair += w;
                    }
                    if (all > 0) share += fair / all;
                }
                return share / fairMoments;
            }
            double desert = Fair(29.8, 27.0), wet = Fair(61.1, 6.5);
            Log("weather: fair-weather cloud patterns {0:0}% of the picks in the Western Desert, {1:0}% in western Norway, over {2} moments of a year ({3})", desert * 100, wet * 100,
                fairMoments, double.IsNaN(desert) || double.IsNaN(wet) ? "skipped, regions not loaded" : desert > wet ? "PASS" : "FAIL");
            // with Sea 0.4+ as the wind's consumer: Sea's own WindAt (what ships and waves use) is Earth's wind
            if (weather.SeaLink == "provider")
            {
                var seaSys = sapi.ModLoader.GetModSystem("Sea.SeaSystem");
                var windAt = seaSys?.GetType().GetMethod("WindAt", new[] { typeof(double), typeof(double) });
                int same = 0, total = 0;
                foreach (var (lat, lon) in new[] { (50.0, -20.0), (15.0, 70.0), (-45.0, 60.0), (20.0, -150.0) })
                {
                    geo.FromGeo(lat, lon, out double x, out double z);
                    object w = windAt?.Invoke(seaSys, new object[] { x, z });
                    double sx = w == null ? double.NaN : Convert.ToDouble(w.GetType().GetField("X").GetValue(w));
                    double sz = w == null ? double.NaN : Convert.ToDouble(w.GetType().GetField("Z").GetValue(w));
                    var e = m.WindAt(x, z, sapi.World.Calendar.TotalHours, true);
                    total++;
                    if (Math.Abs(sx - e.X) < 1e-6 && Math.Abs(sz - e.Z) < 1e-6) same++;
                }
                Log("weather: Sea's wind is Earth's at {0} of {1} places ({2})", same, total, same == total ? "PASS" : "FAIL");
            }
            CheckWeatherRole(weather, m, geo, hours);
            var s0 = m.Sample(core.Params.SpawnX, core.Params.SpawnZ, hours);
            Log("weather at spawn now: cloudness {0:0.00}, cover {1:0.00}, wind {2}, {3:0} hPa, month {4}, normal {5:0.0} mm/day; {6} storms on the Earth",
                s0.Cloudness, s0.CloudCover, s0.Wind, s0.Pressure, (int)(s0.YearRel * 12) + 1, s0.NormalPrecip, m.Storms.AliveIn(hours * m.Settings.Pace).Count(st => st.AliveAt(hours * m.Settings.Pace)));
        }

        /// <summary>The open sea contracts' weather role on the server (decision 0017): Earth's entry is in api.ObjectCache, and
        /// another mod reading only base types gets Earth's weather from it in m/s and vanilla's 0-1 rain.</summary>
        void CheckWeatherRole(EarthWeather weather, WeatherModel m, EarthGeometry geo, double hours)
        {
            Dictionary<string, object> role;
            lock (sapi.ObjectCache) role = sapi.ObjectCache.TryGetValue(WeatherContract.Key, out var o) ? o as Dictionary<string, object> : null;
            if (role == null) { Log("weather: the weather role {0} is missing (FAIL)", WeatherContract.Key); return; }
            var wind = role.TryGetValue("wind", out var a) ? a as System.Func<double, double, double, bool, (double X, double Z)> : null;
            var rain = role.TryGetValue("rain", out var b) ? b as System.Func<double, double, double, (double Cloudness, double Precipitation)> : null;
            var storm = role.TryGetValue("storm", out var c) ? c as System.Func<double, double, double, (double Presence, double WindMps, string Kind)> : null;
            var pressure = role.TryGetValue("pressure", out var d) ? d as System.Func<double, double, double, double> : null;
            int same = 0, total = 0;
            foreach (var (lat, lon) in new[] { (50.0, -20.0), (15.0, 70.0), (-45.0, 60.0), (20.0, -150.0), (19.1, 72.9) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                total++;
                if (wind == null || rain == null || storm == null || pressure == null) continue;
                var w = wind(x, z, hours, false);
                var e = m.WindAt(x, z, hours, false);
                var r = rain(x, z, hours);
                double cl = m.Cloudness(x, z, hours);
                var s = storm(x, z, hours);
                double hpa = pressure(x, z, hours);
                if (hpa == m.Sample(x, z, hours).Pressure && Math.Abs(w.X - e.X * 25) < 1e-9 && Math.Abs(w.Z - e.Z * 25) < 1e-9
                    && Math.Abs(r.Cloudness - Math.Clamp(cl, 0, 1)) < 1e-9 && Math.Abs(r.Precipitation - Math.Clamp(cl - 0.5, 0, 1)) < 1e-9
                    && s.Presence >= 0 && s.Presence <= 1 && s.WindMps >= 0 && (s.Kind == null || s.Kind == "gale" || s.Kind == "hurricane")) same++;
            }
            Log("weather: the weather role is {0} (owner {1}, revision {2}); {3} of {4} places answer Earth's weather in m/s, vanilla's rain and the sea-level pressure ({5})",
                weather.RoleStatus, role.TryGetValue("owner", out var ow) ? ow : "-", weather.Revision, same, total,
                weather.HoldsRole && same == total ? "PASS" : "FAIL");
        }

        void Finish()
        {
            try { CheckWeather(); }
            catch (Exception e) { Log("weather check failed: {0}", e); }
            // every river of the world, as generated with this world's story sites
            try
            {
                if (core.Config.SelfTest.CheckRivers && core.Model?.Rivers != null) Log("{0}", core.Commands.CheckRivers(out _));
            }
            catch (Exception e)
            {
                Mod.Logger.Error("SELFTEST river check failed: {0}", e);
            }
            try
            {
                if (core.RegionalTrees != null) Log("{0}", EarthTrees.SelectionReport());
                Log("structure water guards: {0}", EarthWorldGen.StructureGuardReport());
                Log("pond rules: {0}", EarthWorldGen.PondGuardReport());
                var waterTable = sapi.ModLoader.GetModSystem<EarthWaterTable>();
                if (waterTable != null) Log("Hydrate or Diedrate aquifers: {0}", waterTable.HodReport());
                var layers = sapi.ModLoader.GetModSystem<EarthGeology>()?.Layers;
                if (layers != null) Log("{0}", layers.Counts());
                string file = Path.Combine(GamePaths.DataPath, "EarthMap", "selftest-report.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, report.ToString());
                Mod.Logger.Notification("SELFTEST report written to {0}", file);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("SELFTEST could not write the report: {0}", e.Message);
            }
            if (core.Config.SelfTest.Shutdown)
            {
                Mod.Logger.Notification("SELFTEST done, stopping the server");
                sapi.Server.ShutDown();
            }
        }
    }
}
