using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Game side of decision 0060 (Core.OreLayers): ores, gems and deposits in their ground layer whatever the ore choice.
    /// The ground pass records each column's layers in a per-chunk table; right after the deposit pass, the layer pass puts
    /// the column's own block back wherever a deposit block sits in a layer its deposit may not use, and removes a loose-ore
    /// indicator whose ore is all gone from its column. The ore maps ask <see cref="CodeAt"/> before they place a block.
    /// Owned by EarthGeology; exists only in ground-column worlds made with EarthParams.OreLayers.
    /// </summary>
    sealed class EarthOreLayers
    {
        public readonly OreLayers Rules;
        readonly ICoreServerAPI api;
        readonly EarthGround ground;
        /// <summary>Fills a table from the model without writing blocks (a table missing after a restart between the passes).</summary>
        readonly System.Func<IServerChunk[], int, int, Table, bool> rebuild;
        /// <summary>The regional rock block at (x, z, depth below top, top), for hand-off zones.</summary>
        readonly System.Func<double, double, int, int, int> regionalRock;

        /// <summary>Per block id, from the deposit generators: the rules of the deposits that place it, the rules of deposits
        /// whose loose-ore indicator it is, and the block it replaced (0 when not one block). Swapped whole on a rebind.</summary>
        sealed class DepositBlocks
        {
            public OreLayers.Rule[][] RulesOf = Array.Empty<OreLayers.Rule[]>(), IndicatorOf = Array.Empty<OreLayers.Rule[]>();
            public int[] HostOf = Array.Empty<int>();
            /// <summary>The layer rule of each disc generator (null: none), for prospecting.</summary>
            public Dictionary<DiscDepositGenerator, OreLayers.Rule> RuleOf = new();
        }
        volatile DepositBlocks blocks;
        readonly object bindGate = new();
        /// <summary>The block names; rocks by the layers' rule (OreBlockNames.RockName: any rock- block, halite, bauxite and
        /// dashed names included, since a deposit block's host can be any of them and the rules name halite and bauxite).</summary>
        readonly OreBlockNames names;
        public string Summary { get; private set; } = "";
        // session counts by rule: deposit blocks seen by the layer pass that stay, and that it put back
        readonly long[] kept, removed;
        /// <summary>Blocks put back by rule and the class of the layer they were in (rule index * class count + class).</summary>
        readonly long[] removedFrom;
        long indicatorsRemoved, tablesRebuilt;
        /// <summary>The first put-back block of each rule this session, for /earth orelayers.</summary>
        readonly string[] removedExample;

        // ------------------------------------------------------------------ Earth's layer deposits

        /// <summary>Earth's layer deposits (placers, salt flats, bog iron, bauxite, kaolin, gypsum): ordinary deposit assets
        /// that only ore-layer worlds keep, placed only where ground columns own the column.</summary>
        internal const string DepositFile = "earth:worldgen/deposits/earth-layers.json";
        internal static bool IsLayerDeposit(DepositVariant v) => v?.fromFile == DepositFile;

        /// <summary>
        /// Takes Earth's layer deposits out of the deposit list unless this world has ore layers, bog iron unless Geology
        /// Additions' bog iron item exists, and Earth's ore and gem deposits unless the world's deposit set has them
        /// (<see cref="EarthDepositSet"/>, <see cref="DepositSetOf"/>). Removed, not set to 0 attempts: vanilla draws a random
        /// number per variant and chunk even at 0 attempts, so the old list keeps older and non-Earth worlds' deposits
        /// exactly as they were. Idempotent; run at world start and after every deposit reload, before the ore maps bind.
        /// </summary>
        internal static int PruneDeposits(ICoreServerAPI api, GenDeposits gen, bool layered, int depositSet)
        {
            if (gen?.Deposits == null) return 0;
            bool bogIron = api.World.GetItem(new AssetLocation("game:stone-bogiron")) != null;
            var keep = gen.Deposits.Where(v => (!IsLayerDeposit(v) || layered && (bogIron || v.Code != "earth-bogiron")) && EarthDepositSet.Keeps(v?.fromFile, depositSet)).ToArray();
            int n = gen.Deposits.Length - keep.Length;
            if (n > 0) gen.Deposits = keep;
            return n;
        }
        /// <summary>The Earth deposit set of this world (<see cref="EarthDepositSet.Of"/>); 0 in a non-Earth world.</summary>
        internal static int DepositSetOf(EarthParams p)
            => p?.Enabled == true ? EarthDepositSet.Of(p.DepositSet, p.OreLayers, p.OreRules, p.WaterTableRules) : 0;

        /// <summary>The heat-map resource that scales a layer deposit under the ore maps (bauxite and gypsum are bound by
        /// code in the heat-map file).</summary>
        internal static string ResourceOf(string code) => code switch
        {
            "earth-placergold" => "Au",
            "earth-placertin" => "Sn",
            "earth-placerplatinum" => "Pt",
            "earth-saltflat" => "NaCl",
            "earth-saltflat-sylvite" => "K",
            "earth-bogiron" => "Fe",
            "earth-kaolin" => "clay",
            _ => null
        };

        public EarthOreLayers(ICoreServerAPI api, OreLayers rules, EarthGround ground, System.Func<IServerChunk[], int, int, Table, bool> rebuild, System.Func<double, double, int, int, int> regionalRock)
        {
            this.api = api; Rules = rules; this.ground = ground; this.rebuild = rebuild; this.regionalRock = regionalRock;
            kept = new long[rules.Rules.Count]; removed = new long[rules.Rules.Count];
            removedFrom = new long[rules.Rules.Count * OreLayers.ClassNames.Length];
            removedExample = new string[rules.Rules.Count];
            names = OreBlockNames.Of(api);
        }

        // ------------------------------------------------------------------ deposit blocks

        /// <summary>The deposit blocks, bound at the first chunk (the ore maps add and wrap their deposits after Earth's
        /// geology starts) and again after <see cref="Unbind"/> (/wgen regen).</summary>
        DepositBlocks Blocks()
        {
            var b = blocks;
            if (b != null) return b;
            lock (bindGate)
            {
                if (blocks == null)
                {
                    blocks = Bind(api.ModLoader.GetModSystem<GenDeposits>());
                    api.Logger.Notification("Earth " + Summary + ".");
                }
                return blocks;
            }
        }
        public void Unbind() => blocks = null;

        /// <summary>
        /// Which deposits place each block, from the deposit generators (vanilla's, other mods', the ore maps' natives) and
        /// their children. Children not named in the rules follow their parent.
        /// </summary>
        DepositBlocks Bind(GenDeposits generator)
        {
            int count = api.World.Blocks.Count;
            var rulesBy = new List<OreLayers.Rule>[count];
            var indicators = new List<OreLayers.Rule>[count];
            var host = new int[count];
            var ruleOf = new Dictionary<DiscDepositGenerator, OreLayers.Rule>();
            OreVanilla.Report(api.Logger);
            // the providers' blocks as the ore maps read them (D10), made afresh for this binding
            var provided = new ProviderBlocks(names);
            int variants = 0, unruled = 0;
            var named = new SortedSet<string>(StringComparer.Ordinal);
            void Bind(DepositVariant v, OreLayers.Rule parent)
            {
                if (v == null) return;
                var gen = v.GeneratorInst is EarthOreGenerator eog ? eog.Native : v.GeneratorInst as DiscDepositGenerator;
                var table = provided.Of(gen);
                OreLayers.Rule rule = null;
                if (table != null)
                {
                    // every host the provider names counts, also one whose ore blocks did not resolve
                    bool rockHosted = table.AllHosts.Any(names.IsRock);
                    // a named code has its rule; an unnamed child follows its parent; anything else the rock-hosted default
                    rule = Rules.Names(v.Code) ? Rules.RuleFor(v.Code, rockHosted) : parent ?? Rules.RuleFor(v.Code, rockHosted);
                    variants++;
                    ruleOf[gen] = rule;
                    if (rule == null) unruled++;
                    else
                    {
                        named.Add(v.Code + "=" + rule.Name);
                        // real blocks only, each once per host (a list per block gets this rule once in the end anyway)
                        foreach (var p in table.Hosts)
                            foreach (var b in p.Value)
                            {
                                if (b.Id >= count || b.Id == p.Key) continue;
                                (rulesBy[b.Id] ??= new List<OreLayers.Rule>()).Add(rule);
                                host[b.Id] = host[b.Id] == 0 ? p.Key : host[b.Id] == p.Key ? p.Key : -1;
                            }
                        foreach (var p in table.Indicators)
                            foreach (var b in p.Value)
                                if (b.Id < count) (indicators[b.Id] ??= new List<OreLayers.Rule>()).Add(rule);
                    }
                }
                foreach (var child in v.ChildDeposits ?? Array.Empty<DepositVariant>()) Bind(child, rule ?? parent);
            }
            foreach (var v in generator?.Deposits ?? Array.Empty<DepositVariant>()) Bind(v, null);
            for (int i = 0; i < count; i++) if (host[i] < 0) host[i] = 0;
            var result = new DepositBlocks
            {
                RulesOf = rulesBy.Select(l => l?.Distinct().ToArray()).ToArray(),
                IndicatorOf = indicators.Select(l => l?.Distinct().ToArray()).ToArray(),
                HostOf = host,
                RuleOf = ruleOf,
            };
            Summary = string.Format(CultureInfo.InvariantCulture, "ore layers v{0} (rules {1}): {2} deposit generators, {3} without a layer rule, {4} deposit blocks, {5} indicators",
                Rules.Version, Rules.Fingerprint, variants, unruled, result.RulesOf.Count(r => r != null), result.IndicatorOf.Count(r => r != null));
            return result;
        }

        /// <summary>The rock a deposit block replaced, following child deposits to their parent's host; null when not known
        /// (a block placed into several rocks, such as halite).</summary>
        string HostRock(DepositBlocks b, int id)
        {
            int h = HostBlock(b, id);
            return names.RockName(h);
        }
        /// <summary>The first block in a deposit block's host chain that is not itself a deposit block, 0 when not known.</summary>
        static int HostBlock(DepositBlocks b, int id)
        {
            for (int i = 0, cur = id; i < 4; i++)
            {
                int h = cur > 0 && cur < b.HostOf.Length ? b.HostOf[cur] : 0;
                if (h <= 0) return 0;
                if (b.RulesOf[h] == null) return h;
                cur = h;
            }
            return 0;
        }
        /// <summary>"granite" for rock-granite, null for any block that is not a rock.</summary>
        public string RockName(int id) => names.RockName(id);
        /// <summary>The rule for a deposit variant (a child the rules do not name: its parent's), for the ore maps' gate.</summary>
        public OreLayers.Rule RuleFor(DepositVariant v, bool rockHosted)
        {
            for (var cur = v; cur != null; cur = OreVanilla.ParentOf(cur))
                if (Rules.Names(cur.Code)) return Rules.RuleFor(cur.Code, rockHosted);
            return Rules.RuleFor(v?.Code, rockHosted);
        }

        /// <summary>(CR-O4) This world's latch, not the process's: EarthGeology makes the ore layers anew for each world, so each
        /// logs its own first error.</summary>
        int readingErrors;
        /// <summary>
        /// Prospecting with a generator Earth does not wrap (vanilla ores, other mods' deposits): vanilla's pick reads a
        /// simulated vanilla rock column. It gets Earth's own blocks instead, with every block in a layer this deposit may not
        /// use left out, so the reading counts only hosts the deposit can really fill. Above <paramref name="floor"/> only
        /// (the rock floor, where nothing was placed); the column keeps its length, which vanilla divides by.
        /// </summary>
        public void Reading(DiscDepositGenerator gen, BlockPos pos, int floor, ref int[] blockColumn)
        {
            try
            {
                var geo = api.ModLoader.GetModSystem<EarthGeology>();
                if (geo?.Layers != this || gen == null) return;
                Blocks().RuleOf.TryGetValue(gen, out var rule);
                int top = api.World.BlockAccessor.GetTerrainMapheightAt(pos);
                if (top <= 1) return;
                var column = (int[])blockColumn.Clone();
                for (int y = Math.Max(1, floor + 1); y < column.Length && y <= top; y++)
                {
                    int d = top - y, r = regionalRock(pos.X, pos.Z, d, top);
                    if (r > 0) column[y] = r;
                    if (rule != null && !OreLayers.Allows(rule, geo.LayerCodeAt(pos.X, pos.Z, d, top, column[y]), d, RockName(column[y]))) column[y] = 0;
                }
                blockColumn = column;
            }
            catch (Exception e)
            {
                if (Interlocked.Increment(ref readingErrors) == 1) api.Logger.Error("Earth ore layers: prospecting column failed; vanilla's reading is used. {0}", e);
            }
        }

        // ------------------------------------------------------------------ layer tables

        /// <summary>One chunk column's layers: per column its top, the depth the ground column owns, its segments' ends and
        /// codes, and below a hand-off column the regional rock's age and whether its profile is evaporitic.</summary>
        public sealed class Table
        {
            public int ChunkX, ChunkZ;
            public readonly int[] Top = new int[1024], Depth = new int[1024], Start = new int[1024], Length = new int[1024];
            public readonly byte[] RegionalAge = new byte[1024];
            public readonly bool[] RegionalEvaporite = new bool[1024];
            public int[] End = new int[8192], Code = new int[8192];
            public int Count;
            /// <summary>Rebuilt from the model (no ground-pass table was waiting).</summary>
            public bool Rebuilt;
            /// <summary>When it was kept (<see cref="Keep"/>): the oldest waiting table goes first.</summary>
            internal long Kept;
            internal void Reset(int chunkX, int chunkZ)
            {
                ChunkX = chunkX; ChunkZ = chunkZ; Count = 0; Rebuilt = false;
                Array.Fill(Top, -1); Array.Clear(Length);
            }
        }
        const int MaxTables = 768;
        readonly ConcurrentDictionary<long, Table> tables = new();
        readonly ConcurrentBag<Table> pool = new();
        long keptCount;
        static long Key(int chunkX, int chunkZ) => (long)chunkX << 32 | (uint)chunkZ;

        public Table Begin(int chunkX, int chunkZ)
        {
            if (!pool.TryTake(out var t)) t = new Table();
            t.Reset(chunkX, chunkZ);
            return t;
        }

        /// <summary>Records column k of the table from the column the ground pass just built.</summary>
        public void Record(Table t, int k, int top, GroundColumns.Column col, string era, string profile)
        {
            if (col.Segments == 0) return;
            if (t.Count + col.Segments > t.End.Length)
            {
                Array.Resize(ref t.End, Math.Max(t.End.Length * 2, t.Count + col.Segments));
                Array.Resize(ref t.Code, t.End.Length);
            }
            t.Top[k] = top;
            t.Depth[k] = col.HandOff ? col.Depth : int.MaxValue;
            t.Start[k] = t.Count; t.Length[k] = col.Segments;
            for (int i = 0; i < col.Segments; i++) { t.End[t.Count] = col.SegEnd[i]; t.Code[t.Count] = Rules.CodeOf(col, i); t.Count++; }
            t.RegionalAge[k] = (byte)Rules.AgeOfEra(era);
            t.RegionalEvaporite[k] = Rules.IsEvaporiteProfile(profile);
        }

        /// <summary>Keeps a finished table until its chunk's deposit pass; the oldest go when too many wait (they are rebuilt).
        /// Dropped tables are left to the collector, not pooled: another thread may still be reading one. Nothing is kept per
        /// chunk once its table is released, so a long pregeneration does not grow the bookkeeping.</summary>
        public void Keep(Table t)
        {
            t.Kept = Interlocked.Increment(ref keptCount);
            tables[Key(t.ChunkX, t.ChunkZ)] = t;
            // only while the deposit pass lags far behind the ground pass: a scan of the waiting tables for the oldest
            for (int tries = 0; tables.Count > MaxTables && tries < 64; tries++)
            {
                KeyValuePair<long, Table> oldest = default;
                foreach (var p in tables) if (oldest.Value == null || p.Value.Kept < oldest.Value.Kept) oldest = p;
                if (oldest.Value == null) break;
                tables.TryRemove(oldest);   // that very table: a newer one under the same key stays
            }
        }

        /// <summary>The chunk's table: kept from the ground pass, else rebuilt from the model and kept, so the rest of the deposit
        /// pass finds it.</summary>
        Table TableFor(IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            if (tables.TryGetValue(Key(chunkX, chunkZ), out var t)) return t;
            t = new Table();
            t.Reset(chunkX, chunkZ);
            if (!rebuild(chunks, chunkX, chunkZ, t)) return null;
            t.Rebuilt = true;
            Interlocked.Increment(ref tablesRebuilt);
            Keep(t);
            return t;
        }

        /// <summary>The layer code at depth d of column k (d from the column's top), with the host rock for hand-off zones.</summary>
        int CodeIn(Table t, int k, int d, string rock)
        {
            if (t.Top[k] < 0 || d < 0) return OreLayers.None;
            if (d < t.Depth[k])
            {
                int s0 = t.Start[k], s1 = s0 + t.Length[k];
                for (int i = s0; i < s1; i++) if (d < t.End[i]) return t.Code[i];
                return t.Code[s1 - 1];
            }
            return Rules.RegionalCode(rock, (OreLayers.Age)t.RegionalAge[k], t.RegionalEvaporite[k]);
        }

        /// <summary>
        /// The layer code at a block of the chunk column being generated, for a deposit about to replace
        /// <paramref name="hostBlock"/> there. <see cref="OreLayers.None"/> where ground columns do not own the column.
        /// </summary>
        public int CodeAt(IServerChunk[] chunks, int chunkX, int chunkZ, int lx, int y, int lz, int hostBlock)
            => CodeAt(TableFor(chunks, chunkX, chunkZ), lx, y, lz, hostBlock);
        /// <summary><see cref="CodeAt(IServerChunk[], int, int, int, int, int, int)"/> with the chunk's table already in hand
        /// (<see cref="TableAt"/>), for passes that ask many blocks of one chunk column.</summary>
        internal int CodeAt(Table t, int lx, int y, int lz, int hostBlock)
        {
            if (t == null) return OreLayers.None;
            int k = lz * 32 + lx;
            return CodeIn(t, k, t.Top[k] - y, names.RockName(hostBlock));
        }
        /// <summary>The layer table of the chunk column being generated (kept, or rebuilt and kept), null where ground columns
        /// do not own it. Valid until the chunk's <see cref="Release"/>.</summary>
        internal Table TableAt(IServerChunk[] chunks, int chunkX, int chunkZ) => TableFor(chunks, chunkX, chunkZ);
        /// <summary>Drops a chunk's layer table after the last pass that reads it (the layer pass, then 0065's catalogue bodies)
        /// and only then gives it back to the pool: before that the Terrain thread's next <see cref="Begin"/> could take it and
        /// refill it for another chunk while this chunk's deposit thread still reads it.</summary>
        public void Release(int chunkX, int chunkZ)
        {
            if (tables.TryRemove(Key(chunkX, chunkZ), out var t) && pool.Count < 64) pool.Add(t);
        }

        // ------------------------------------------------------------------ the layer pass

        /// <summary>After the deposit pass of one chunk column: deposit blocks in layers their deposit may not use become the
        /// column's own block again; a loose-ore indicator goes when none of its ore is left in its column. The chunk's table
        /// stays for 0065's catalogue bodies; the caller calls <see cref="Release"/> after them.</summary>
        public void Apply(IServerChunk[] chunks, int chunkX, int chunkZ)
        {
            var t = TableFor(chunks, chunkX, chunkZ);
            if (t == null) return;
            var map = chunks[0].MapChunk;
            int maxY = chunks.Length * 32 - 1;
            var b = Blocks();
            var rulesOf = b.RulesOf;
            var indicatorOf = b.IndicatorOf;
            for (int z = 0; z < 32; z++)
                for (int x = 0; x < 32; x++)
                {
                    int k = z * 32 + x, top = t.Top[k];
                    if (top < 0) continue;
                    int s0 = t.Start[k], s1 = s0 + t.Length[k], seg = s0, depth = t.Depth[k];
                    uint removedMask = 0, keptMask = 0;
                    // rules v2: the rules that kept the deposit block directly above (0: none there), for a rule's "under"
                    uint keptAbove = 0;
                    for (int y = Math.Min(top, maxY); y >= 1; y--)
                    {
                        uint above = keptAbove;
                        keptAbove = 0;
                        int index = ((y % 32) * 32 + z) * 32 + x;
                        var data = chunks[y / 32].Data;
                        int id = data.GetBlockIdUnsafe(index);
                        if (id <= 0 || id >= rulesOf.Length) continue;
                        var rules = rulesOf[id];
                        if (rules == null) continue;
                        int d = top - y, code;
                        string rock = HostRock(b, id);
                        bool inColumn = d < depth;
                        if (inColumn)
                        {
                            while (seg < s1 - 1 && d >= t.End[seg]) seg++;
                            code = t.Code[seg];
                        }
                        else
                        {
                            if (rock == null)
                            {
                                int r = regionalRock(chunkX * 32 + x, chunkZ * 32 + z, d, top);
                                rock = names.RockName(r);
                            }
                            code = Rules.RegionalCode(rock, (OreLayers.Age)t.RegionalAge[k], t.RegionalEvaporite[k]);
                        }
                        bool ok = false;
                        foreach (var r in rules)
                            if (OreLayers.Allows(r, code, d, rock) || above != 0 && OreLayers.AllowsUnder(r, above))
                            {
                                ok = true; keptMask |= 1u << r.Index; keptAbove = 1u << r.Index; Interlocked.Increment(ref kept[r.Index]); break;
                            }
                        if (ok) continue;
                        foreach (var r in rules)
                        {
                            removedMask |= 1u << r.Index; Interlocked.Increment(ref removed[r.Index]);
                            Interlocked.Increment(ref removedFrom[r.Index * OreLayers.ClassNames.Length + (int)OreLayers.ClassOf(code)]);
                            if (removedExample[r.Index] == null)
                                Interlocked.CompareExchange(ref removedExample[r.Index], string.Format(CultureInfo.InvariantCulture, "{0} at ={1} {2} ={3}, {4} blocks down in {5}{6}",
                                    api.World.Blocks[id]?.Code, chunkX * 32 + x, y, chunkZ * 32 + z, d, Rules.Describe(code), t.Rebuilt ? " (rebuilt table)" : ""), null);
                        }
                        int put = inColumn ? ground.MaterialBlock(OreLayers.MaterialOf(code)) : 0;
                        if (put <= 0) put = HostBlock(b, id);
                        if (put <= 0 && !inColumn) put = regionalRock(chunkX * 32 + x, chunkZ * 32 + z, d, top);
                        if (put > 0) data.SetBlockUnsafe(index, put);
                    }
                    if (removedMask == 0) continue;
                    int surface = map.RainHeightMap[k] + 1;
                    if (surface < 1 || surface > maxY) continue;
                    int si = ((surface % 32) * 32 + z) * 32 + x;
                    var sd = chunks[surface / 32].Data;
                    int ind = sd.GetBlockIdUnsafe(si);
                    var of = ind > 0 && ind < indicatorOf.Length ? indicatorOf[ind] : null;
                    if (of == null) continue;
                    bool orphan = true;
                    foreach (var r in of) if ((removedMask & 1u << r.Index) == 0 || (keptMask & 1u << r.Index) != 0) { orphan = false; break; }
                    if (orphan) { sd.SetBlockUnsafe(si, 0); Interlocked.Increment(ref indicatorsRemoved); }
                }
        }

        /// <summary>
        /// <c>/earth orelayers</c>: deposit blocks in the loaded columns within <paramref name="radius"/> of (x, z), counted by
        /// their deposits' layer rule and the layer class they sit in, and how many sit in a layer none of their deposits may use
        /// (0 unless something placed ore after the layer pass). Each column is read from its generated terrain top down.
        /// </summary>
        public string Census(IBlockAccessor ba, EarthGeology geo, int x0, int z0, int radius)
        {
            var b = Blocks();
            var counts = new SortedDictionary<string, SortedDictionary<string, long>>(StringComparer.Ordinal);
            var wrong = new SortedDictionary<string, long>(StringComparer.Ordinal);
            var example = new Dictionary<string, string>(StringComparer.Ordinal);
            long columns = 0, blocksSeen = 0;
            var pos = new BlockPos(0, 0, 0, 0);
            for (int x = x0 - radius; x <= x0 + radius; x++)
                for (int z = z0 - radius; z <= z0 + radius; z++)
                {
                    if ((long)(x - x0) * (x - x0) + (long)(z - z0) * (z - z0) > (long)radius * radius) continue;
                    var map = ba.GetMapChunk(x >> 5, z >> 5);
                    var heights = map?.WorldGenTerrainHeightMap;
                    if (heights == null) continue;
                    int top = heights[(z & 31) * 32 + (x & 31)];
                    if (top <= 1) continue;
                    columns++;
                    for (int y = 1; y <= top; y++)
                    {
                        pos.Set(x, y, z);
                        int id = ba.GetBlockId(pos);
                        var rules = id > 0 && id < b.RulesOf.Length ? b.RulesOf[id] : null;
                        if (rules == null) continue;
                        blocksSeen++;
                        int d = top - y, host = HostBlock(b, id);
                        // as the layer pass: a block from several host rocks stands for the rock the strata put there
                        if (RockName(host) == null && geo.RegionalRock(x, z, d, top) is int strata && RockName(strata) != null) host = strata;
                        int code = geo.LayerCodeAt(x, z, d, top, host);
                        string cls = OreLayers.ClassNames[(int)OreLayers.ClassOf(code)];
                        bool allowed = false;
                        foreach (var r in rules) if (OreLayers.Allows(r, code, d, RockName(host))) { allowed = true; break; }
                        // rules v2: directly under a block its "under" rules keep (vanilla's underclay below a coal block)
                        if (!allowed && rules.Any(r => r.Under != 0) && KeptRules(ba, geo, b, x, y + 1, z, top) is uint above && above != 0)
                            foreach (var r in rules) if (OreLayers.AllowsUnder(r, above)) { allowed = true; break; }
                        string name = rules[0].Name;
                        if (!counts.TryGetValue(name, out var byClass)) counts[name] = byClass = new SortedDictionary<string, long>(StringComparer.Ordinal);
                        byClass.TryGetValue(cls, out long n); byClass[cls] = n + 1;
                        if (!allowed)
                        {
                            wrong.TryGetValue(name, out long w); wrong[name] = w + 1;
                            if (!example.ContainsKey(name))
                                example[name] = string.Format(CultureInfo.InvariantCulture, "{0} at ={1} {2} ={3}, {4} blocks down in {5}",
                                    ba.GetBlock(pos)?.Code, x, y, z, d, Rules.Describe(code));
                        }
                    }
                }
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "Ore layers within {0} blocks ({1} loaded columns, {2} deposit blocks), by rule and layer class: ", radius, columns, blocksSeen);
            sb.Append(counts.Count == 0 ? "none" : string.Join("; ", counts.Select(p => p.Key + ": " + string.Join(", ", p.Value.Select(q => q.Key + " " + q.Value.ToString(CultureInfo.InvariantCulture))))));
            sb.Append(". In a layer they may not use: ").Append(wrong.Count == 0 ? "none" : string.Join(", ", wrong.Select(p => p.Key + " " + p.Value.ToString(CultureInfo.InvariantCulture) + " (e.g. " + example[p.Key] + ")")));
            sb.Append(". ").Append(Counts());
            return sb.ToString();
        }

        /// <summary>The census's reading of one block: the rules that keep the deposit block at (x, y, z) in its own layer (a bit
        /// per rule index), 0 when it is no deposit block or none keeps it. Not chained: an underclay's own "under" is not followed.</summary>
        uint KeptRules(IBlockAccessor ba, EarthGeology geo, DepositBlocks b, int x, int y, int z, int top)
        {
            if (y > top) return 0;
            int id = ba.GetBlockId(new BlockPos(x, y, z, 0));
            var rules = id > 0 && id < b.RulesOf.Length ? b.RulesOf[id] : null;
            if (rules == null) return 0;
            int d = top - y, host = HostBlock(b, id);
            if (RockName(host) == null && geo.RegionalRock(x, z, d, top) is int strata && RockName(strata) != null) host = strata;
            int code = geo.LayerCodeAt(x, z, d, top, host);
            uint mask = 0;
            foreach (var r in rules) if (OreLayers.Allows(r, code, d, RockName(host))) mask |= 1u << r.Index;
            return mask;
        }

        /// <summary>Session counts for <c>/earth orelayers</c>.</summary>
        public string Counts()
        {
            var sb = new StringBuilder("Layer pass this session (deposit blocks in ground columns kept / put back): ");
            bool any = false;
            foreach (var r in Rules.Rules)
            {
                long k = Interlocked.Read(ref kept[r.Index]), m = Interlocked.Read(ref removed[r.Index]);
                if (k + m == 0) continue;
                sb.Append(any ? "; " : "").AppendFormat(CultureInfo.InvariantCulture, "{0} {1}/{2} ({3:0}% kept", r.Name, k, m, 100.0 * k / (k + m));
                if (m > 0)
                {
                    var from = new List<string>();
                    for (int c = 0; c < OreLayers.ClassNames.Length; c++)
                        if (Interlocked.Read(ref removedFrom[r.Index * OreLayers.ClassNames.Length + c]) is long n && n > 0)
                            from.Add(OreLayers.ClassNames[c] + " " + n.ToString(CultureInfo.InvariantCulture));
                    sb.Append("; put back from ").Append(string.Join(", ", from));
                    if (removedExample[r.Index] is string ex) sb.Append("; e.g. ").Append(ex);
                }
                sb.Append(')');
                any = true;
            }
            if (!any) sb.Append("none yet");
            sb.AppendFormat(CultureInfo.InvariantCulture, ". Indicators removed: {0}. Tables rebuilt: {1}.", Interlocked.Read(ref indicatorsRemoved), Interlocked.Read(ref tablesRebuilt));
            return sb.ToString();
        }
    }
}
