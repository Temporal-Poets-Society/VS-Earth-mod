using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Earth's water table in the game (decision 0059): a query any mod may read (<see cref="WaterTableAt"/>,
    /// <see cref="AquiferAt"/>, versioned by <see cref="WaterTableQueryVersion"/>), and, with Hydrate or Diedrate
    /// installed, HoD's per-chunk <c>aquiferData</c> written for every chunk of each new column at the end of world
    /// generation, before the column is loaded and HoD would generate its own. Without HoD nothing is written.
    /// </summary>
    public sealed class EarthWaterTable : ModSystem
    {
        ICoreServerAPI api;
        EarthMapSystem core;
        EarthGeology geology;
        WaterTable rules;
        /// <summary>Class byte (WaterTable.Class, CrystallineBit) of every block id; 0: not ground.</summary>
        byte[] classOf;
        GroundColumns.DepthCurve curve;
        volatile bool active, writeHod;
        string hodStatus = "Hydrate or Diedrate is not installed; nothing is written.";
        int errors, wetlandErrors;
        long columnsWritten;
        [ThreadStatic] static GroundColumns.Column column;

        /// <summary>The world's wetland grid (TerrainModel.Wetlands, set up by world generation) with the rules' layer weights.</summary>
        sealed class WetlandInput { public WetlandGrid Grid; public double[] Weights; }
        volatile WetlandInput wetland;

        /// <summary>The query's version in this world: 1 when Earth is active, 0 otherwise (decision 0059).</summary>
        public int WaterTableQueryVersion => active ? WaterTable.QueryVersion : 0;
        /// <summary>Whether this session writes Hydrate or Diedrate's aquifer data for new chunk columns.</summary>
        public bool WritesHodAquifers => writeHod;

        // after EarthGeology (0.35), whose WorldReady set-up this reads
        public override double ExecuteOrder() => 0.36;
        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, Init);
            api.Event.ChunkColumnGeneration(OnPreDone, EnumWorldGenPass.PreDone, "standard");
        }

        void Init()
        {
            active = false; writeHod = false;
            core = api.ModLoader.GetModSystem<EarthMapSystem>();
            geology = api.ModLoader.GetModSystem<EarthGeology>();
            var p = core?.Params;
            if (p?.Enabled != true || core.Model == null) return;
            try
            {
                string json = api.Assets.TryGet(new AssetLocation(WaterTable.RulesAsset))?.ToText();
                if (json == null) { Mod.Logger.Error("Earth water table: {0} is missing; the water table is off.", WaterTable.RulesAsset); return; }
                rules = new WaterTable(json);
                curve = geology?.GroundModel?.Curve ?? GroundColumns.DepthCurve.For(p.GroundDepth, 1 / core.Model.Vertical.Slope(0), core.Model.SeaLevel);
                classOf = BuildClassTable(out string unclassified);
                active = true;
                Mod.Logger.Notification("Earth water table v{0} (rules {1}): radius {2:0} m, depth curve {3}{4}.", WaterTable.RulesVersion, rules.Fingerprint,
                    rules.RadiusMetres, curve, unclassified.Length == 0 ? "" : "; rock blocks without a permeability class (count as no ground): " + unclassified);
            }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            {
                Mod.Logger.Error("Earth water table: the rules are invalid ({0}); the water table is off.", e.Message);
                return;
            }
            if (!api.ModLoader.IsModEnabled(HodAquiferContract.ModId)) return;
            if (p.WaterTableRules < 1)
            {
                // world generation changes for new worlds only: a save made before the water table keeps HoD's own aquifers
                hodStatus = "this world was made before Earth's water table (rules 0); Hydrate or Diedrate makes its own aquifers, as before.";
                Mod.Logger.Notification("Earth water table: {0}", hodStatus);
                return;
            }
            writeHod = CheckHod(out hodStatus);
            if (writeHod) Mod.Logger.Notification("Earth water table: {0}", hodStatus);
            else Mod.Logger.Warning("Earth water table: {0}", hodStatus);
        }

        /// <summary>The class of every block: the ground model's blocks by their material, then by name (rock-NAME, prefixes).</summary>
        byte[] BuildClassTable(out string unclassified)
        {
            var blocks = api.World.Blocks;
            var table = new byte[blocks.Count + 1];
            var model = geology?.GroundModel;
            var claimed = new bool[table.Length];
            if (model != null)
                for (int m = 0; m < model.Materials.Length; m++)
                {
                    string name = model.Materials[m];
                    var cls = rules.OfMaterial(name);
                    if (cls == WaterTable.Class.None) continue;
                    foreach (string code in model.Candidates[m])
                    {
                        if (code.Contains('{')) break;
                        Block b; try { b = api.World.GetBlock(new AssetLocation(code)); } catch (Exception) { b = null; }
                        if (b == null || b.Id <= 0 || b.Id >= table.Length) continue;
                        if (!claimed[b.Id]) { table[b.Id] = (byte)((byte)cls | (rules.IsCrystallineMaterial(name) ? WaterTable.CrystallineBit : 0)); claimed[b.Id] = true; }
                        break; // the first candidate that exists is the one the ground columns place
                    }
                }
            var missing = new System.Collections.Generic.SortedSet<string>(StringComparer.Ordinal);
            foreach (var b in blocks)
            {
                if (b?.Code == null || b.Id <= 0 || b.Id >= table.Length || claimed[b.Id]) continue;
                string path = b.Code.Path;
                table[b.Id] = rules.OfBlockPath(path);
                if (table[b.Id] == 0 && path.StartsWith("rock-", StringComparison.Ordinal)) missing.Add(path);
            }
            unclassified = string.Join(", ", missing);
            return table;
        }

        /// <summary>
        /// Writes only when the installed HoD reads Earth's bytes back unchanged with its own type and still uses data
        /// version 2 (decision 0059, section 6). Otherwise HoD makes its own aquifers, as without Earth.
        /// </summary>
        bool CheckHod(out string status)
        {
            try
            {
                var asm = api.ModLoader.GetMod(HodAquiferContract.ModId)?.Systems?.FirstOrDefault()?.GetType().Assembly;
                var manager = asm?.GetType(HodAquiferContract.ManagerType);
                var chunkType = asm?.GetType(HodAquiferContract.ChunkDataType);
                if (manager == null || chunkType == null) { status = "Hydrate or Diedrate's aquifer types were not found; HoD makes its own aquifers."; return false; }
                object v = manager.GetField(HodAquiferContract.VersionField, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (v is not int version) { status = "Hydrate or Diedrate's aquifer data version was not found; HoD makes its own aquifers."; return false; }
                if (version != HodAquiferContract.Version)
                {
                    status = string.Format(CultureInfo.InvariantCulture, "Hydrate or Diedrate's aquifer data is version {0}, Earth writes version {1}; HoD makes its own aquifers.", version, HodAquiferContract.Version);
                    return false;
                }
                byte[] bytes = HodAquiferContract.Bytes(73, true);
                object read;
                using (var ms = new MemoryStream(bytes)) read = ProtoBuf.Meta.RuntimeTypeModel.Default.Deserialize(ms, null, chunkType);
                object Prop(object o, string name) => o?.GetType().GetProperty(name)?.GetValue(o);
                object data = Prop(read, "Data");
                bool same = Prop(read, "Version") is int rv && rv == HodAquiferContract.Version
                    && Prop(data, "AquiferRatingRaw") is int raw && raw == 73
                    && Prop(data, "IsSalty") is bool salty && salty
                    && Prop(data, "AquiferRatingSmoothed") is int sm && sm == 73;
                if (!same) { status = "Hydrate or Diedrate does not read Earth's aquifer bytes back unchanged; HoD makes its own aquifers."; return false; }
                status = string.Format(CultureInfo.InvariantCulture, "Hydrate or Diedrate {0} reads aquifer data version {1}; Earth writes it for every new chunk column from its water table.",
                    api.ModLoader.GetMod(HodAquiferContract.ModId)?.Info?.Version ?? "?", version);
                return true;
            }
            catch (Exception e)
            {
                status = "checking Hydrate or Diedrate failed (" + e.Message + "); HoD makes its own aquifers.";
                return false;
            }
        }

        // ------------------------------------------------------------------ one column

        struct Probe
        {
            public bool Ok;
            public int Top, TableY, WaterTop;
            public WaterTable.Table Table;
            public WaterTable.Base Base;
            public double Elevation, TopK, PerchedTop, Aridity, Precip, Lat, Lon, Wetland;
            public bool Ground, SeaFloor, SaltFlat;
            public string Cover;
        }

        WaterTable.Node NodeAt(double x, double z)
        {
            // a lattice point (whole blocks): from the column store when its chunk is there
            var c = ColumnStore.Near(core.Model, x, z);
            if (c.Gutter) return new WaterTable.Node { Level = double.NaN };
            var v = core.Model.Vertical;
            if (c.Water == TerrainModel.WaterNone && !(c.River && !c.DryRiver)) return new WaterTable.Node { Level = c.Elev, Kind = WaterTable.Land };
            byte kind = c.Water == 1 ? WaterTable.Sea : c.Water == 2 ? WaterTable.Lake : c.Water == 3 ? WaterTable.SaltLake : WaterTable.River;
            return new WaterTable.Node { Level = c.Water == 1 ? 0 : v.Elevation(c.WaterTop), Kind = kind };
        }

        /// <summary>The mapped wetland share at a place (0 when the world has no wetland grid).</summary>
        double WetlandAt(double lat, double lon)
        {
            var grid = core.Model.Wetlands;
            if (grid == null) return 0;
            var w = wetland;
            if (w == null || !ReferenceEquals(w.Grid, grid))
            {
                double[] weights;
                try { weights = rules.WetlandWeights(grid.Layers); }
                catch (FormatException e)
                {
                    if (Interlocked.Increment(ref wetlandErrors) == 1) Mod.Logger.Error("Earth water table: {0}; mapped wetlands do not raise the table.", e.Message);
                    weights = new double[grid.Layers.Length];
                }
                wetland = w = new WetlandInput { Grid = grid, Weights = weights };
            }
            return WaterTable.WetlandShare(w.Grid, w.Weights, lat, lon);
        }

        void Metres(double lat, out double mx, out double mz)
        {
            var g = core.Model.Geometry;
            mz = Geo.MetresPerDegree / g.BlocksPerDegreeZ;
            mx = Math.Max(1e-3, Geo.MetresPerDegree * Math.Cos(lat * Math.PI / 180) / g.BlocksPerDegreeX);
        }

        /// <summary>Metres per x block at a world z (the drainage base's lattice rows: WaterTable.BaseField).</summary>
        double MetresXAt(double z)
        {
            var g = core.Model.Geometry;
            Metres(Math.Clamp((g.EquatorZ - z) / g.BlocksPerDegreeZ, -90, 90), out double mx, out _);
            return mx;
        }

        /// <summary>The table at one column with its drainage base already known; <paramref name="col"/> receives the ground column.</summary>
        Probe ProbeAt(double x, double z, in WaterTable.Base b, GroundColumns.Column col)
        {
            var r = new Probe { TopK = double.NaN, PerchedTop = double.NaN, Aridity = double.NaN };
            var g = core.Model.Geometry.ToGeo(x, z);
            if (g.InGutter || g.BeyondPole) return r;
            // probes stand at a block's centre (x + 0.5), off the block grid the column store holds: the model's own column
            var c = core.Model.GetColumn(x, z);
            r.Lat = g.Lat; r.Lon = g.Lon;
            r.Top = Math.Min(c.Top, c.IceBottom - 1);
            // under glacier ice the ground is the rock below it (Top), not the ice's surface (c.Elev): the table's depth, the
            // height above the drainage base and the salt test are the rock's
            double ground = c.IceBottom <= c.Top ? core.Model.Vertical.Elevation(r.Top) : c.Elev;
            r.Elevation = ground;
            r.WaterTop = c.WaterTop;
            r.Base = b;
            var climate = core.Model.Data.SampleClimate(g.Lat, g.Lon);
            r.Aridity = climate.Aridity;
            r.Precip = climate.Precip;
            r.Wetland = WetlandAt(g.Lat, g.Lon);
            col.Count = 0;
            if (geology != null && geology.GroundColumnAt(x, z, col, out var sample))
            {
                r.Ground = true; r.Cover = sample.Cover;
                rules.TopOf(col, geology.GroundModel.Materials, out r.TopK, out r.PerchedTop);
            }
            bool water = c.Water != TerrainModel.WaterNone || (c.River && !c.DryRiver);
            var input = new WaterTable.Column
            {
                Elevation = ground, Base = b.Level, Aridity = r.Aridity, TopK = r.TopK, PerchedTop = r.PerchedTop,
                Water = water, WaterSurface = c.Water == 1 ? 0 : core.Model.Vertical.Elevation(c.WaterTop), Wetland = r.Wetland,
            };
            r.Table = rules.At(input);
            double baseY = double.IsInfinity(b.Level) || double.IsNaN(b.Level) ? double.NaN : core.Model.Vertical.Y(b.Level) - 1;
            r.TableY = WaterTable.BlockY(r.Table, r.Top, WaterTable.BlocksAt(r.Ground ? col : null, r.Table.Depth, curve), baseY, c.WaterTop);
            r.SeaFloor = c.Water == 1;
            r.SaltFlat = c.Water == 3 || b.SaltLakeNear || (r.Cover != null && rules.SaltCovers.Contains(r.Cover));
            r.Ok = true;
            return r;
        }

        Probe ProbeAt(double x, double z, GroundColumns.Column col)
        {
            Metres(0, out _, out double mz);
            var b = new WaterTable.Base[1];
            rules.BaseField(Math.Floor(x), Math.Floor(z), 1, 1, NodeAt, MetresXAt, mz, b);
            return ProbeAt(x, z, b[0], col);
        }

        // ------------------------------------------------------------------ the query (any mod)

        /// <summary>
        /// The water table at a column (decision 0059): <paramref name="tableY"/> is the highest saturated block (the open
        /// water's top block where there is water), <paramref name="depthMetres"/> the table's real depth below the ground
        /// (negative under open water). Server side, any thread; a pure function of the world's data. False when the query
        /// is off (Earth not active) or outside the Earth.
        /// </summary>
        public bool WaterTableAt(int x, int z, out int tableY, out double depthMetres, out bool openWater)
        {
            tableY = 0; depthMetres = double.NaN; openWater = false;
            if (!active) return false;
            var col = column ??= new GroundColumns.Column();
            var p = ProbeAt(x + 0.5, z + 0.5, col);
            if (!p.Ok) return false;
            tableY = p.TableY; depthMetres = p.Table.Depth; openWater = p.Table.Water;
            return true;
        }

        /// <summary>
        /// The aquifer at a block as generated (decision 0059): whether it lies at or below the table, the hydraulic
        /// conductivity of its layer (m/day: the rules' class conductivity, halved at 1 km depth; 0 for no ground) and
        /// whether its water is salty. The layer is the ground column's or regional geology's; elsewhere the block now in
        /// the world.
        /// </summary>
        public bool AquiferAt(int x, int y, int z, out bool saturated, out double conductivity, out bool salty)
        {
            saturated = false; conductivity = 0; salty = false;
            if (!active) return false;
            var col = column ??= new GroundColumns.Column();
            var p = ProbeAt(x + 0.5, z + 0.5, col);
            if (!p.Ok) return false;
            saturated = y <= p.TableY;
            if (y > p.Top || y < 1) return true;
            var c = p.Ground ? col : null;
            // the rock's top sets the fractured zone: the first crystalline block from the top down
            double rockTop = double.NaN;
            for (int yy = p.Top; yy > y; yy--)
                if ((ClassAt(x, yy, z, p.Top) & WaterTable.CrystallineBit) != 0) { rockTop = WaterTable.MetresAt(c, p.Top - yy, curve); break; }
            rules.Evaluate(SampleOf(p), ClassAt(x, y, z, p.Top), WaterTable.MetresAt(c, p.Top - y, curve), ref rockTop, out _, out conductivity, out salty);
            return true;
        }

        byte ClassAt(int x, int y, int z, int top)
        {
            int id = geology?.Active == true ? geology.RegionalRock(x, z, top - y, top) : 0;
            if (id <= 0) id = api.World.BlockAccessor.GetBlock(new BlockPos(x, y, z, 0), BlockLayersAccess.Solid)?.Id ?? 0;
            return id > 0 && id < classOf.Length ? classOf[id] : (byte)0;
        }

        WaterTable.Sample SampleOf(in Probe p) => new()
        {
            Top = p.Top, TableY = p.TableY, Elevation = p.Elevation, TableElevation = p.Table.Elevation,
            SeaFloor = p.SeaFloor, SaltFlat = p.SaltFlat, SeaNear = p.Base.SeaNear,
        };

        // ------------------------------------------------------------------ Hydrate or Diedrate's aquifer data

        void OnPreDone(IChunkColumnGenerateRequest request)
        {
            if (!writeHod) return;
            try
            {
                var chunks = request.Chunks;
                Rate(request.ChunkX, request.ChunkZ, chunks.Length, (lx, y, lz) => chunks[y >> 5].Data.GetBlockIdUnsafe(((y & 31) * 32 + lz) * 32 + lx), out int[] rating, out bool[] salty, out _, out _);
                for (int i = 0; i < chunks.Length; i++) chunks[i].SetModdata(HodAquiferContract.ModDataKey, HodAquiferContract.Bytes(rating[i], salty[i]));
                Interlocked.Increment(ref columnsWritten);
            }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref errors) <= 3)
                    Mod.Logger.Error("Earth water table: Hydrate or Diedrate's aquifer data failed for chunk column {0},{1}; HoD makes its own there. {2}", request.ChunkX, request.ChunkZ, e);
            }
        }

        /// <summary>Ratings and salt of every chunk of a chunk column from the blocks <paramref name="blockId"/> gives (local x, y, local z).</summary>
        void Rate(int chunkX, int chunkZ, int chunkCount, System.Func<int, int, int, int> blockId, out int[] rating, out bool[] salty, out Probe[] probes, out WaterTable.ChunkSums sums)
        {
            int n = rules.Samples;
            double step = 32.0 / n;
            int cx = chunkX * 32, cz = chunkZ * 32;
            var bases = new WaterTable.Base[n * n];
            Metres(0, out _, out double mz);
            double off = Math.Floor(step / 2);
            rules.BaseField(cx + off, cz + off, n, step, NodeAt, MetresXAt, mz, bases);
            sums = new WaterTable.ChunkSums(chunkCount);
            var col = column ??= new GroundColumns.Column();
            probes = new Probe[n * n];
            double rechargeSum = 0; int rechargeN = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int lx = (int)(off + i * step), lz = (int)(off + j * step);
                    var p = ProbeAt(cx + lx + 0.5, cz + lz + 0.5, bases[j * n + i], col);
                    probes[j * n + i] = p;
                    if (!p.Ok) continue;
                    rechargeSum += rules.Recharge(p.Precip, p.Aridity); rechargeN++;
                    var c = p.Ground ? col : null;
                    rules.Accumulate(SampleOf(p), y =>
                    {
                        int id = blockId(lx, y, lz);
                        return id > 0 && id < classOf.Length ? classOf[id] : (byte)0;
                    }, d => WaterTable.MetresAt(c, d, curve), sums);
                }
            rating = new int[chunkCount]; salty = new bool[chunkCount];
            rules.Rate(sums, n * n, rechargeN > 0 ? rechargeSum / rechargeN : rules.RechargeFloor, rating, salty);
        }

        // ------------------------------------------------------------------ /earth water

        public string Describe(double x, double y, double z)
        {
            if (!active) return "Earth's water table is off in this world.";
            var col = column ??= new GroundColumns.Column();
            var p = ProbeAt(x, z, col);
            if (!p.Ok) return "Outside the Earth: no water table.";
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Water table (decision 0059): {0}. Block y={1} (ground top y={2}); drainage base {3:0.#} m{4}{5}. ",
                rules.Describe(p.Table), p.TableY, p.Top, p.Base.Level, p.Base.SeaNear ? ", the sea within reach" : "", p.Base.SaltLakeNear ? ", a salt lake within reach" : "");
            sb.AppendFormat(CultureInfo.InvariantCulture, "Aridity {0:0.00}, precipitation {1:0} mm, water surplus {2:0} mm. ", p.Aridity, p.Precip, rules.Surplus(p.Precip, p.Aridity));
            sb.Append(p.Ground
                ? string.Format(CultureInfo.InvariantCulture, "Top {0:0} m k {1:0.00}{2}; cover {3}. ", rules.TopMetres, p.TopK, double.IsNaN(p.PerchedTop) ? "" : string.Format(CultureInfo.InvariantCulture, ", tight layer from {0:0.#} m", p.PerchedTop), p.Cover)
                : "No ground column here: the top counts as medium. ");
            int cx = (int)Math.Floor(x) >> 5, cz = (int)Math.Floor(z) >> 5;
            int chunks = api.World.BlockAccessor.MapSizeY / 32;
            var ba = api.World.BlockAccessor;
            var pos = new BlockPos(0, 0, 0, 0);
            Rate(cx, cz, chunks, (lx, yy, lz) => { pos.Set(cx * 32 + lx, yy, cz * 32 + lz); return ba.GetBlock(pos, BlockLayersAccess.Solid)?.Id ?? 0; }, out int[] rating, out bool[] salty, out _, out var sums);
            int here = Math.Clamp((int)Math.Floor(y) >> 5, 0, chunks - 1);
            int samples = rules.Samples * rules.Samples;
            sb.Append("Aquifer by chunk (now, bottom up): ");
            for (int i = 0; i < chunks; i++)
            {
                if (rating[i] == 0 && i != here) continue;
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}{1}:{2}{3} ({4:0.###} m2/d, {5:0.#} m){6} ", i == here ? "[" : "", i * 32, rating[i], salty[i] ? "s" : "",
                    sums.Transmissivity[i] / samples, sums.Saturated[i] / samples, i == here ? "]" : "");
            }
            sb.Append("(y of the chunk's bottom: rating, s salty, mean transmissivity and saturated metres of the sampled columns; other chunks 0). ");
            var chunk = ba.GetChunk(cx, here, cz);
            var stored = HodAquiferContract.Read(chunk?.GetModdata(HodAquiferContract.ModDataKey));
            sb.Append(stored?.Data == null ? "Stored aquiferData here: none. "
                : string.Format(CultureInfo.InvariantCulture, "Stored aquiferData here: rating {0} (smoothed {1}), {2}, version {3}. ",
                    stored.Data.AquiferRatingRaw, stored.Data.AquiferRatingSmoothed?.ToString(CultureInfo.InvariantCulture) ?? "none", stored.Data.IsSalty ? "salty" : "fresh", stored.Version));
            sb.Append(api.ModLoader.IsModEnabled(HodAquiferContract.ModId) ? hodStatus : "Hydrate or Diedrate is not installed; nothing is written.");
            sb.AppendFormat(CultureInfo.InvariantCulture, " Columns written this session: {0}.", Interlocked.Read(ref columnsWritten));
            return sb.ToString();
        }
    }
}
