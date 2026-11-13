using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Game side of decision 0065 (ore rules v4): the deposit catalogue's ore bodies and basin beds, placed in each chunk
    /// column after the deposit pass and its layer pass (0060), with the blocks of each deposit code's own generators (its
    /// host rocks, or the most similar host's ore block for another rock) and only in the layers that code may use. Owned
    /// by the ore session; exists only in worlds with EarthParams.OreRules 4 and up.
    /// </summary>
    sealed class EarthOreDeposits
    {
        public readonly OreDepositCatalog Catalog;
        public readonly OreDepositPlan Plan;
        readonly ICoreServerAPI api;
        readonly OreSession session;
        readonly EarthGeology geology;
        /// <summary>Per deposit code: its blocks by host and its layer rule.</summary>
        sealed class Slot
        {
            public string Code;
            public readonly Dictionary<int, Block[]> Hosts = new();
            public readonly Dictionary<int, bool> Graded = new();
            public readonly List<(string Rock, int Id)> HostRocks = new();
            /// <summary>Rock-hosted codes: per plain rock (by <see cref="rockIndex"/>) the host block id of the most similar
            /// host (0 for the code's own hosts), filled at start.</summary>
            public int[] Similar;
            public OreLayers.Rule Rule;
            public bool RockHosted;
            public long Placed;
        }
        readonly Slot[] slots;
        readonly string[] rockName;
        /// <summary>Per block id its index among the plain rocks (those with a <see cref="rockName"/>), -1 for any other block.</summary>
        readonly int[] rockIndex;
        /// <summary>Block ids of loose or solid ground (soil, sand, gravel, stone, ore): a body naming a cover layer may take
        /// them, with the ore block of the first host or rock below.</summary>
        readonly bool[] ground;
        /// <summary>Ore rules v5: per block id of a frozen ground block (permafrost sand, gravel ...) the block it froze from, so
        /// it takes that block's deposits (frozen placers: Kondyor, EA-0058); null in older rules.</summary>
        readonly int[] thawed;
        /// <summary>How far below a loose block the host rock is looked for.</summary>
        const int LooseScan = 96;
        long placed, chunkCount;
        long ticks;

        EarthOreDeposits(ICoreServerAPI api, OreSession session, EarthGeology geology, OreDepositCatalog catalog, OreDepositPlan plan, Dictionary<string, Slot> byCode)
        {
            this.api = api; this.session = session; this.geology = geology; Catalog = catalog; Plan = plan;
            int count = api.World.Blocks.Count;
            rockName = new string[count];
            ground = new bool[count];
            // a body fills plain rock with its most similar host's ore: halite and bauxite are deposits, not rock to fill
            // (OreBlockNames.PlainRock, the rule of rules v3's "fill any rock")
            var names = session.Blocks.Names;
            foreach (var b in api.World.Blocks)
            {
                if (b?.Code == null || b.Id <= 0 || b.Id >= count) continue;
                ground[b.Id] = b.BlockMaterial is EnumBlockMaterial.Soil or EnumBlockMaterial.Sand or EnumBlockMaterial.Gravel or EnumBlockMaterial.Stone or EnumBlockMaterial.Ore;
                rockName[b.Id] = names.PlainRock(b.Id);
            }
            rockIndex = new int[count];
            int rocks = 0;
            for (int id = 0; id < count; id++) rockIndex[id] = rockName[id] != null ? rocks++ : -1;
            if (session.Rules >= OreBackground.ValleySearchRules && geology?.GroundModel is { } model)
            {
                int BlockOf(int m)
                {
                    foreach (string c in model.Candidates[m])
                        if (!c.Contains('{') && api.World.GetBlock(new AssetLocation(c)) is { Id: > 0 } b && b.Id < count) return b.Id;
                    return 0;
                }
                thawed = new int[count];
                foreach (var (m, frozen) in model.FrozenPairs())
                {
                    int f = BlockOf(frozen), t = BlockOf(m);
                    if (f > 0 && t > 0 && f != t && thawed[f] == 0) thawed[f] = t;
                }
            }
            var layers = geology?.Layers;
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var list = new List<Slot>();
            IEnumerable<OreDepositCatalog.Ore> all = catalog.Deposits.SelectMany(d => d.Ores).Concat(catalog.Areas.SelectMany(a => a.Beds.Select(b => b.Ore)));
            foreach (var ore in all)
            {
                if (used.TryGetValue(ore.Code, out int i)) { ore.Slot = i; continue; }
                if (!byCode.TryGetValue(ore.Code, out var s) || s.Hosts.Count == 0) { used[ore.Code] = -1; ore.Slot = -1; continue; }
                foreach (int host in s.Hosts.Keys) if (rockName[host] != null) s.HostRocks.Add((rockName[host], host));
                s.HostRocks.Sort((a, b) => string.CompareOrdinal(a.Rock, b.Rock));
                s.RockHosted = s.HostRocks.Count > 0;
                s.Rule = layers?.Rules.RuleFor(ore.Code, s.RockHosted);
                if (s.RockHosted)
                {
                    // the most similar host per plain rock, by the hosts in rock-name order (the code's hosts come from several
                    // generators, so no one provider's order applies)
                    var similar = s.Similar = new int[rocks];
                    session.Blocks.SimilarHosts(s.HostRocks, s.Hosts.ContainsKey, (rock, host) => similar[rockIndex[rock]] = host);
                }
                ore.Slot = used[ore.Code] = list.Count;
                list.Add(s);
            }
            slots = list.ToArray();
        }

        /// <summary>Every deposit code's blocks by host, from all generators (vanilla, other mods', Earth's), children included.
        /// Where another mod defines a code Earth also ships (earth:worldgen/deposits), only the other mod's blocks count, as
        /// in EarthOres.DropDuplicateAuthoredDeposits: its deposit wins (Geology Additions' sperrylite, for example).</summary>
        static Dictionary<string, Slot> Providers(GenDeposits gen, ProviderBlocks provided)
        {
            var byCode = new Dictionary<string, Slot>(StringComparer.Ordinal);
            var others = EarthOres.OtherModsCodes(gen?.Deposits ?? Array.Empty<DepositVariant>());
            void Walk(DepositVariant v)
            {
                if (v == null) return;
                var g = v.GeneratorInst is EarthOreGenerator eog ? eog.Native : v.GeneratorInst as DiscDepositGenerator;
                if (g != null && v.Code != null && !EarthOres.YieldsTo(v, others) && provided.Of(g) is ProviderBlocks.Tables table)
                {
                    if (!byCode.TryGetValue(v.Code, out var s)) byCode[v.Code] = s = new Slot { Code = v.Code };
                    // the first generator of a code that names a host gives its blocks
                    foreach (var p in table.Hosts)
                    {
                        if (s.Hosts.ContainsKey(p.Key)) continue;
                        s.Hosts[p.Key] = p.Value;
                        s.Graded[p.Key] = table.Graded[p.Key];
                    }
                }
                foreach (var c in v.ChildDeposits ?? Array.Empty<DepositVariant>()) Walk(c);
            }
            foreach (var v in gen?.Deposits ?? Array.Empty<DepositVariant>()) Walk(v);
            return byCode;
        }

        /// <summary>Reads every catalogue file, plans it at the world's scale and binds its codes; null (logged) when there is
        /// no catalogue. Never throws for bad data: bad entries are logged and skipped.</summary>
        public static EarthOreDeposits TryCreate(ICoreServerAPI api, OreSession session, EarthMapSystem core, EarthGeology geology, GenDeposits gen, ILogger log)
        {
            var catalog = new OreDepositCatalog();
            foreach (var asset in api.Assets.GetMany(OreDepositCatalog.Folder, "earth").OrderBy(a => a.Location.Path, StringComparer.Ordinal))
            {
                if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                catalog.AddFile(asset.Location.ToString(), asset.ToText());
            }
            foreach (string p in catalog.Problems.Take(40)) log.Warning("Earth ore deposits: {0}", p);
            if (catalog.Problems.Count > 40) log.Warning("Earth ore deposits: {0} more problems not shown.", catalog.Problems.Count - 40);
            if (catalog.Files.Count == 0) { log.Warning("Earth ore deposits: no catalogue in earth:{0}; rules v4 run with the background only.", OreDepositCatalog.Folder); }
            // codes without an installed provider (e.g. Geology Additions' without that mod): left out, their share to the
            // body's other codes; a body or bed with none left is left out (0065 §4)
            var byCode = Providers(gen, session.Blocks);
            bool Installed(string code) => byCode.TryGetValue(code, out var s) && s.Hosts.Count > 0;
            var missing = catalog.Deposits.SelectMany(d => d.Ores.Select(o => (o.Code, Id: d.Id)))
                .Concat(catalog.Areas.SelectMany(a => a.Beds.Select(b => (b.Ore.Code, Id: "area " + a.Id))))
                .Where(o => !Installed(o.Code)).GroupBy(o => o.Code, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            var gone = catalog.DropOres(Installed);
            foreach (var g in missing)
                log.Notification("Earth ore deposits: no installed generator for '{0}' (another mod's ore, e.g. Geology Additions); left out of {1} bodies or beds, its share given to their other ores.",
                    g.Key, g.Select(o => o.Id).Distinct().Count());
            foreach (string line in gone.Take(40)) log.Notification("Earth ore deposits: skipped, no installable ore: {0}", line);
            if (gone.Count > 40) log.Notification("Earth ore deposits: {0} more bodies skipped for want of an installable ore.", gone.Count - 40);
            var p0 = core.Params;
            var geo = core.Model.Geometry;
            // the world's ground depth curve, as the ground columns use it (rules v4 run only with ore layers, so with them)
            var curve = geology.GroundModel.Curve;
            var scale = new OreScale(geo, curve);
            // ore rules v5 (EA-0058): a cover blanket with none of its layers in its footprint moves to the nearest that has one
            var search = session.Rules >= OreBackground.ValleySearchRules
                ? new OreDepositPlan.ValleySearch { LayerBlocks = (x, z, classes, from, to) => LayerBlocks(session, geology, x, z, classes, from, to) }
                : null;
            var plan = new OreDepositPlan(catalog, scale, p0.NoiseSeed ^ 0x0de9051, session.DepositRate, search);
            // (CR-O9) placement neither wraps nor clips at the seam, so entries that would reach across it are left out, in one
            // line (none of the shipped catalogue comes near it)
            if (plan.OffRing.Count > 0)
                log.Warning("Earth ore deposits: {0} left out, too near the world's seam ({1:0.##}° longitude) to be placed whole: {2}",
                    plan.OffRing.Count, geo.SeamLon0, string.Join(", ", plan.OffRing));
            var made = new EarthOreDeposits(api, session, geology, catalog, plan, byCode);
            log.Notification("Earth ore deposits (0065): {0} deposits and {1} basins from {2} file(s) ({3} skipped, {4} without an installable ore), h = {5:0.#} m per block north-south, depth curve {6}, deposit rate x{7:0.##}; {8} codes bound{9}.",
                catalog.Deposits.Count, catalog.Areas.Count, catalog.Files.Count, catalog.Skipped, gone.Count, scale.Hz, curve, session.DepositRate, made.slots.Length,
                missing.Count > 0 ? "; not installed: " + string.Join(", ", missing.Select(g => g.Key)) : "");
            return made;
        }

        /// <summary>Ore rules v5's valley search: how many blocks of the model's column at (x, z) between the two depths below
        /// its top lie in a layer of <paramref name="classes"/> (the same layers the layer table and the census read).</summary>
        static int LayerBlocks(OreSession session, EarthGeology geology, int x, int z, int classes, int from, int to)
        {
            int top = session.TopAt(x + .5, z + .5), n = 0;
            if (top <= 2) return 0;
            for (int d = from; d <= to && d < top; d++)
            {
                int code = geology.LayerCodeAt(x + .5, z + .5, d, top, 0);
                if (code == OreLayers.None) break;
                if ((classes & 1 << (int)OreLayers.ClassOf(code)) != 0) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>One column of the chunk column being generated.</summary>
        sealed class ChunkColumn : IOreColumn
        {
            public EarthOreDeposits Owner;
            public IServerChunk[] Chunks;
            public int ChunkX, ChunkZ, Lx, Lz, TopY, FloorY;
            /// <summary>The chunk column's layer table, fetched at the first block that asks (TableRead) and kept for the pass.</summary>
            public EarthOreLayers.Table Table;
            public bool TableRead;
            public int Top => TopY;
            public int Floor => FloorY;
            // The last answer: placement always asks Allows and then Place for the same ore and depth (and Snap's last Allows is
            // the bed's first block), so each block is resolved once. Nothing writes in between; Place and a new column clear it.
            OreDepositCatalog.Ore lastOre; int lastDepth; OreWhere lastWhere; Block[] lastBlocks; bool lastGraded, lastValid; int lastIndex;
            public void NewColumn() => lastValid = false;
            Block[] Resolve(OreDepositCatalog.Ore ore, int depth, OreWhere where, out bool graded, out int index)
            {
                if (!(lastValid && lastOre == ore && lastDepth == depth && lastWhere.Basin == where.Basin && lastWhere.Classes == where.Classes))
                {
                    lastBlocks = Owner.Resolve(this, ore, depth, where, out lastGraded, out lastIndex);
                    lastOre = ore; lastDepth = depth; lastWhere = where; lastValid = true;
                }
                graded = lastGraded; index = lastIndex;
                return lastBlocks;
            }
            public bool Allows(OreDepositCatalog.Ore ore, int depth, OreWhere where) => Resolve(ore, depth, where, out _, out _) != null;
            public bool Place(OreDepositCatalog.Ore ore, int depth, int tier, double roll, OreWhere where)
            {
                var blocks = Resolve(ore, depth, where, out bool graded, out int index);
                lastValid = false;
                if (blocks == null) return false;
                var block = graded ? blocks[Math.Clamp(tier, 0, blocks.Length - 1)] : blocks[Math.Clamp((int)(roll * blocks.Length), 0, blocks.Length - 1)];
                int y = TopY - depth;
                Chunks[y / 32].Data.SetBlockUnsafe(index, block.Id);
                Chunks[y / 32].Data.SetFluid(index, 0);
                Interlocked.Increment(ref Owner.slots[ore.Slot].Placed);
                return true;
            }
        }

        /// <summary>The ore blocks for a code at a depth of a chunk column, null where it may not go: the block there must be one of
        /// the code's hosts, or a plain rock for a rock-hosted code (the most similar host's ore block), or loose ground of a
        /// cover layer (the ore block of the first host or rock below), in a layer the body may use: its own layers, else
        /// the code's 0060 table, where in a basin coal and evaporites take any sedimentary bed (0065 §4).</summary>
        Block[] Resolve(ChunkColumn c, OreDepositCatalog.Ore ore, int depth, OreWhere where, out bool graded, out int index)
        {
            graded = false; index = -1;
            if (ore.Slot < 0) return null;
            int y = c.TopY - depth, low = Math.Max(1, c.FloorY);
            if (y <= low || y >= c.Chunks.Length * 32) return null;
            var s = slots[ore.Slot];
            index = ((y % 32) * 32 + c.Lz) * 32 + c.Lx;
            int id = c.Chunks[y / 32].Data.GetBlockIdUnsafe(index);
            if (id <= 0 || id >= ground.Length) return null;
            int host = HostFor(s, id);
            if (host <= 0 && thawed != null && thawed[id] > 0) host = HostFor(s, thawed[id]);
            bool loose = false;
            if (host <= 0)
            {
                if (!ground[id]) return null;
                for (int yy = y - 1; yy > low && yy > y - LooseScan && host <= 0; yy--)
                {
                    int below = c.Chunks[yy / 32].Data.GetBlockIdUnsafe(((yy % 32) * 32 + c.Lz) * 32 + c.Lx);
                    if (below > 0 && below < ground.Length) host = HostFor(s, below);
                }
                if (host <= 0) return null;
                loose = true;
            }
            var layers = geology?.Layers;
            if (layers != null)
            {
                if (!c.TableRead) { c.Table = layers.TableAt(c.Chunks, c.ChunkX, c.ChunkZ); c.TableRead = true; }
                int code = layers.CodeAt(c.Table, c.Lx, y, c.Lz, id);
                if (!Fits(layers.Rules, s, ore, where, code, depth, rockName[id], loose)) return null;
            }
            else if (loose) return null;
            graded = s.Graded[host];
            return s.Hosts[host];
        }

        /// <summary>The host block whose ore blocks a code takes in place of <paramref name="id"/>: itself, the most similar host
        /// rock for a plain rock and a rock-hosted code, or 0.</summary>
        int HostFor(Slot s, int id)
        {
            if (s.Hosts.ContainsKey(id)) return id;
            return s.RockHosted && rockName[id] != null ? s.Similar[rockIndex[id]] : 0;
        }

        /// <summary>The layer gate: a body's own layers (any block of a column the layers do not cover), or the code's 0060 rule
        /// with the basin rule (a sedimentary rock by the 0060 rules' list); loose ground only in a cover layer.</summary>
        static bool Fits(OreLayers rules, Slot s, OreDepositCatalog.Ore ore, OreWhere where, int code, int depth, string rock, bool loose)
        {
            var cls = OreLayers.ClassOf(code);
            if (loose && cls is OreLayers.Class.None or OreLayers.Class.Bed or OreLayers.Class.Basement) return false;
            if (where.Classes != 0) return cls == OreLayers.Class.None || (where.Classes & 1 << (int)cls) != 0;
            if (s.Rule == null || OreLayers.Allows(s.Rule, code, depth, rock)) return true;
            return where.Basin && OreDepositPlan.IsBasinCode(ore.Code) && cls == OreLayers.Class.Bed && rules.IsSedimentary(rock);
        }

        [ThreadStatic] static ChunkColumn columnCache;
        [ThreadStatic] static List<double> rowCache;

        /// <summary>Places the catalogue in one generated chunk column (after the deposit and layer passes).</summary>
        public void Place(IChunkColumnGenerateRequest request)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var chunks = request.Chunks;
            var map = chunks[0].MapChunk;
            int bx = request.ChunkX * 32, bz = request.ChunkZ * 32;
            var col = columnCache ??= new ChunkColumn();
            col.Owner = this; col.Chunks = chunks; col.ChunkX = request.ChunkX; col.ChunkZ = request.ChunkZ;
            col.Table = null; col.TableRead = false;
            // the rock floor is one height per map chunk
            int floor = Math.Max(1, EarthWorldGen.RockFloorTop(bx, bz));
            long n = 0;
            foreach (var b in Plan.BodiesNear(bx, bz, bx + 31, bz + 31))
            {
                long here = 0;
                for (int z = Math.Max(bz, b.MinZ); z <= Math.Min(bz + 31, b.MaxZ); z++)
                    for (int x = Math.Max(bx, b.MinX); x <= Math.Min(bx + 31, b.MaxX); x++)
                    {
                        if (!Set(col, map, x - bx, z - bz, floor)) continue;
                        here += (long)Plan.PlaceBody(b, x, z, col);
                    }
                if (here > 0) { Interlocked.Add(ref b.Placed, here); n += here; session.Totals.RecordBlocks(b.D.Layer, here); }
            }
            var xs = rowCache ??= new List<double>();
            foreach (var a in Plan.AreasNear(bx, bz, bx + 31, bz + 31))
            {
                long here = 0;
                for (int z = Math.Max(bz, a.MinZ); z <= Math.Min(bz + 31, a.MaxZ); z++)
                {
                    Plan.Row(a, z, xs);
                    if (xs.Count < 2) continue;
                    for (int x = bx; x < bx + 32; x++)
                    {
                        if (!OreDepositPlan.Inside(xs, x + .5) || !Set(col, map, x - bx, z - bz, floor)) continue;
                        here += (long)Plan.PlaceArea(a, x, z, col);
                    }
                }
                if (here > 0) { Interlocked.Add(ref a.Placed, here); n += here; session.Totals.RecordBlocks(a.A.Layer, here); }
            }
            Interlocked.Add(ref placed, n);
            Interlocked.Increment(ref chunkCount);
            Interlocked.Add(ref ticks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }
        static bool Set(ChunkColumn col, IMapChunk map, int lx, int lz, int floor)
        {
            int top = Math.Min(col.Chunks.Length * 32 - 1, (int)map.WorldGenTerrainHeightMap[lz * 32 + lx]);
            if (top <= 2) return false;
            col.Lx = lx; col.Lz = lz; col.TopY = top;
            col.FloorY = floor;
            col.NewColumn();
            return true;
        }

        // ------------------------------------------------------------------ prospecting and commands

        /// <summary>A column read from Earth's model (prospecting, commands): its rock by y and its layers.</summary>
        sealed class ModelColumn : IOreColumn
        {
            public EarthOreDeposits Owner; public int[] Rock; public int X, Z, TopY, FloorY;
            public int Top => TopY;
            public int Floor => FloorY;
            public bool Allows(OreDepositCatalog.Ore ore, int depth, OreWhere where)
            {
                if (ore.Slot < 0) return false;
                int y = TopY - depth;
                if (y <= Math.Max(1, FloorY) || y >= Rock.Length) return false;
                var s = Owner.slots[ore.Slot];
                int id = Rock[y];
                if (id <= 0 || id >= Owner.ground.Length) return s.RockHosted;   // Earth keeps vanilla strata here: unknown rock, counted for rock-hosted codes
                // loose ground: counted where a cover layer allows it (the host rock below is not looked up here)
                bool loose = !s.Hosts.ContainsKey(id) && !(s.RockHosted && Owner.rockName[id] != null)
                    && !(Owner.thawed != null && Owner.thawed[id] > 0 && Owner.HostFor(s, Owner.thawed[id]) > 0);
                if (loose && !Owner.ground[id]) return false;
                var geo = Owner.geology;
                if (geo?.Layers == null) return !loose;
                return Fits(geo.Layers.Rules, s, ore, where, geo.LayerCodeAt(X, Z, depth, TopY, id), depth, Owner.rockName[id], loose);
            }
            public bool Place(OreDepositCatalog.Ore ore, int depth, int tier, double roll, OreWhere where) => false;
        }

        /// <summary>Expected catalogue blocks of one deposit code in the column at (x, z), for the prospecting pick (0065 §6).</summary>
        public double ColumnBlocks(string code, int x, int z, int top, int floor, int[] rock)
        {
            if (top <= 2) return 0;
            var col = new ModelColumn { Owner = this, Rock = rock, X = x, Z = z, TopY = top, FloorY = floor };
            double sum = 0;
            foreach (var b in Plan.BodiesNear(x, z, x, z))
                if (b.D.Ores.Any(o => o.Code == code)) sum += Plan.PlaceBody(b, x, z, col, place: false, only: code);
            var xs = new List<double>();
            foreach (var a in Plan.AreasNear(x, z, x, z))
            {
                if (!a.A.Beds.Any(bd => bd.Ore.Code == code)) continue;
                Plan.Row(a, z, xs);
                if (OreDepositPlan.Inside(xs, x + .5)) sum += Plan.PlaceArea(a, x, z, col, place: false, only: code);
            }
            return sum;
        }

        /// <summary>The catalogue's deposits and basins within <paramref name="radius"/> blocks of (x, z), nearest first, for /earth ores.</summary>
        public string Near(double x, double z, int radius, int max = 8)
        {
            var found = new List<(double Dist, string Text)>();
            int x0 = (int)Math.Floor(x) - radius, x1 = (int)Math.Floor(x) + radius, z0 = (int)Math.Floor(z) - radius, z1 = (int)Math.Floor(z) + radius;
            foreach (var b in Plan.BodiesNear(x0, z0, x1, z1))
            {
                double dist = Math.Sqrt((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z));
                if (dist > radius + Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ) / 2.0) continue;
                var d = b.D;
                found.Add((dist, string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}, {2}): {3:0.###} Mt{4} of {5}, {6} {7:0}-{8:0} m down, {9:0.#}x{10:0.#} km; {11:0.#} blocks expected{12}, {13} placed this session; centre {14:0} blocks {15}{16}",
                    d.Name, d.Layer, d.Id, d.Mt, double.IsFinite(d.Grade) ? string.Format(CultureInfo.InvariantCulture, " at {0:0.###} {1}", d.Grade, d.Unit) : "",
                    string.Join("/", d.Ores.Select(o => o.Code)), d.Shape.ToString().ToLowerInvariant(), b.TopM, b.BaseM, d.LenKm, d.WidKm, b.Target,
                    b.Compact ? " (a compact cluster)" : string.Format(CultureInfo.InvariantCulture, " (fill {0:0.###})", b.Fill), Interlocked.Read(ref b.Placed),
                    dist, Direction(b.X - x, b.Z - z),
                    b.MovedM > 0 ? string.Format(CultureInfo.InvariantCulture, " (moved {0:0.#} km from its catalogue place to the nearest {1}; search {2:0} ms)", b.MovedM / 1000, b.Where, b.SearchMs)
                    : b.LayerFound ? "" : string.Format(CultureInfo.InvariantCulture, " (no {0} within the valley search's reach; search {1:0} ms)", b.Where, b.SearchMs))));
            }
            var xs = new List<double>();
            foreach (var a in Plan.AreasNear(x0, z0, x1, z1))
            {
                Plan.Row(a, (int)Math.Floor(z), xs);
                bool inside = OreDepositPlan.Inside(xs, x);
                found.Add((inside ? 0 : radius, string.Format(CultureInfo.InvariantCulture, "basin {0} ({1}, {2}){3}: {4}{5}; {6} blocks placed this session",
                    a.A.Name, a.A.Layer, a.A.Id, inside ? ", you are inside it" : ", nearby",
                    string.Join(", ", a.A.Beds.Select(bd => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#} m thick from {2:0} m ({3:0.##}% cover)", bd.Ore.Code, bd.ThickM, bd.TopM, bd.Cover * 100))),
                    double.IsFinite(a.A.Mt) ? string.Format(CultureInfo.InvariantCulture, ", {0:0.#} Mt in all", a.A.Mt) : "", Interlocked.Read(ref a.Placed))));
            }
            if (found.Count == 0) return string.Format(CultureInfo.InvariantCulture, "No catalogue deposit within {0} blocks.", radius);
            return string.Format(CultureInfo.InvariantCulture, "Catalogue deposits within {0} blocks: ", radius)
                + string.Join("; ", found.OrderBy(f => f.Dist).Take(max).Select(f => f.Text)) + (found.Count > max ? string.Format(CultureInfo.InvariantCulture, "; and {0} more", found.Count - max) : "");
        }
        static string Direction(double dx, double dz)
        {
            if (Math.Abs(dx) < 1 && Math.Abs(dz) < 1) return "here";
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            double deg = (Math.Atan2(dx, -dz) * 180 / Math.PI + 360 + 22.5) % 360;
            return names[(int)(deg / 45)];
        }

        public string Totals()
        {
            long c = Interlocked.Read(ref chunkCount);
            double ms = Interlocked.Read(ref ticks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return string.Format(CultureInfo.InvariantCulture, "catalogue: {0} blocks in {1} chunk columns ({2:0.###} ms per column); {3}",
                Interlocked.Read(ref placed), c, c > 0 ? ms / c : 0,
                string.Join(", ", slots.Where(s => Interlocked.Read(ref s.Placed) > 0).OrderBy(s => s.Code, StringComparer.Ordinal).Select(s => s.Code + " " + Interlocked.Read(ref s.Placed))));
        }
    }
}
