using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// What Earth's ore code calls each block, per block id, from the world's block list (fixed once the assets are loaded, so
    /// built once per world and shared by the ore maps and the ore layers). Rock names follow two rules, and each reader says
    /// which it uses:
    /// <list type="bullet">
    /// <item><see cref="RockName"/>: "granite" for any rock- block, halite, bauxite and dashed names included. The ore layers'
    /// rule: a deposit block's host can be any of them, and the layer rules name halite and bauxite.</item>
    /// <item><see cref="PlainRock"/>: only a plain rock (rock-x, no dash, not halite or bauxite). The rule for rock a deposit may
    /// fill with its most similar host's ore (rules v3's "fill any rock", the catalogue's bodies): halite and bauxite are
    /// deposits themselves, not rocks to fill.</item>
    /// </list>
    /// </summary>
    sealed class OreBlockNames
    {
        readonly string[] rockName, plainRock;
        readonly sbyte[] grade;
        /// <summary>The ids with a <see cref="PlainRock"/> name, ascending.</summary>
        internal readonly int[] PlainRockIds;
        /// <summary>The block list's length when these were made: every table here has this length.</summary>
        internal readonly int Count;

        OreBlockNames(IList<Block> blocks)
        {
            Count = blocks.Count;
            rockName = new string[Count]; plainRock = new string[Count]; grade = new sbyte[Count];
            Array.Fill(grade, (sbyte)-1);
            foreach (var b in blocks)
                if (b?.Code != null && b.Id > 0 && b.Id < Count)
                {
                    string path = b.Code.Path;
                    if (path.StartsWith("rock-", StringComparison.Ordinal)) rockName[b.Id] = path.Substring(5);
                    plainRock[b.Id] = PlainRockOf(b);
                    grade[b.Id] = (sbyte)GradeOf(b);
                }
            PlainRockIds = Enumerable.Range(0, Count).Where(id => plainRock[id] != null).ToArray();
        }
        static readonly ConditionalWeakTable<IWorldAccessor, OreBlockNames> perWorld = new();
        /// <summary>The names of this world's blocks, made at the first call (at WorldReady, when the block list is complete) and
        /// kept with the world, never beyond it: a closed single-player world's blocks are not held.</summary>
        internal static OreBlockNames Of(ICoreServerAPI api) => perWorld.GetValue(api.World, w => new OreBlockNames(w.Blocks));

        /// <summary>"granite" for rock-granite and the part after "rock-" of any rock block; null for any block that is not a rock.</summary>
        internal string RockName(int id) => id > 0 && id < Count ? rockName[id] : null;
        internal bool IsRock(int id) => RockName(id) != null;
        /// <summary>"granite" for a plain rock block (rock-granite), null for anything else.</summary>
        internal string PlainRock(int id) => id > 0 && id < Count ? plainRock[id] : null;
        /// <summary>A block's ore grade (0 poor .. 3 bountiful), -1 when it has none.</summary>
        internal int Grade(int id) => id > 0 && id < Count ? grade[id] : -1;

        /// <summary>The <see cref="PlainRock"/> rule on a block.</summary>
        internal static string PlainRockOf(Block b)
        {
            string p = b?.Code?.Path;
            if (p == null || !p.StartsWith("rock-", StringComparison.Ordinal)) return null;
            string rock = p.Substring(5);
            return rock.Contains('-') || rock is "halite" or "bauxite" ? null : rock;
        }
        /// <summary>A block's ore grade from its code (-poor-, -medium-, -rich-, -bountiful-), -1 when it has none.</summary>
        internal static int GradeOf(Block b)
        {
            string p = b.Code.Path;
            if (p.Contains("-poor-")) return 0;
            if (p.Contains("-medium-")) return 1;
            if (p.Contains("-rich-")) return 2;
            if (p.Contains("-bountiful-")) return 3;
            return -1;
        }
    }

    /// <summary>
    /// D10: the provider deposit generators' blocks as Earth places them, read from vanilla's tables (OreVanilla) once per
    /// binding: by OreBinder for the ore maps and the deposit catalogue, by EarthOreLayers.Bind for the layer gate. Each
    /// generator's tables are made at the first request and kept for the binding.
    /// </summary>
    sealed class ProviderBlocks
    {
        public readonly OreBlockNames Names;
        readonly Dictionary<DiscDepositGenerator, Tables> tables = new(ReferenceEqualityComparer.Instance);

        internal ProviderBlocks(OreBlockNames names) { Names = names; }

        /// <summary>One generator's blocks.</summary>
        internal sealed class Tables
        {
            /// <summary>Per host with real ore blocks, in the provider's host order: its distinct real blocks, from poor to
            /// bountiful when every one is graded (<see cref="Graded"/>), else in the provider's order.</summary>
            public readonly Dictionary<int, Block[]> Hosts = new();
            public readonly Dictionary<int, bool> Graded = new();
            /// <summary>Every host the provider names, also those whose ore blocks did not resolve.</summary>
            public int[] AllHosts;
            /// <summary>Loose-ore indicators per host: the real blocks of every host that has one.</summary>
            public Dictionary<int, Block[]> Indicators;
        }

        /// <summary>A generator's tables; null when vanilla's host table cannot be read.</summary>
        internal Tables Of(DiscDepositGenerator g)
        {
            if (g == null) return null;
            if (tables.TryGetValue(g, out var t)) return t;
            var source = OreVanilla.HostTable(g);
            if (source == null) return null;
            t = new Tables { AllHosts = source.Keys.ToArray() };
            foreach (var p in source)
            {
                var blocks = OreBlocks(p.Value, out bool graded);
                if (blocks.Length == 0) continue;
                t.Hosts[p.Key] = blocks;
                t.Graded[p.Key] = graded;
            }
            var hints = OreVanilla.IndicatorTable(g);
            t.Indicators = hints?.Where(p => p.Value?.Blocks?.Any(b => b?.Id > 0) == true).ToDictionary(p => p.Key, p => p.Value.Blocks.Where(b => b?.Id > 0).ToArray()) ?? new Dictionary<int, Block[]>();
            tables[g] = t;
            return t;
        }

        static Block[] OreBlocks(ResolvedDepositBlock host, out bool graded)
        {
            var blocks = host?.Blocks?.Where(b => b?.Id > 0).Distinct().ToArray() ?? Array.Empty<Block>();
            graded = blocks.Length > 0 && blocks.All(b => OreBlockNames.GradeOf(b) >= 0);
            return graded ? blocks.OrderBy(OreBlockNames.GradeOf).ToArray() : blocks;
        }

        /// <summary>
        /// For every plain rock (<see cref="OreBlockNames.PlainRock"/>) that is not a host: the host whose ore blocks it takes,
        /// the most similar of <paramref name="hostRocks"/> by <see cref="OreRules.SimilarHost"/> over their names in the order
        /// given, and of that name the first host in that order. The order is each caller's: it decides ties and the fallback.
        /// </summary>
        internal void SimilarHosts(IReadOnlyList<(string Rock, int Id)> hostRocks, System.Func<int, bool> isHost, System.Action<int, int> take)
        {
            var names = hostRocks.Select(h => h.Rock).ToList();
            var firstHost = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var h in hostRocks) firstHost.TryAdd(h.Rock, h.Id);
            // the answer depends on the rock's name only
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int id in Names.PlainRockIds)
            {
                if (isHost(id)) continue;
                string rock = Names.PlainRock(id);
                if (!byName.TryGetValue(rock, out int host)) byName[rock] = host = firstHost[OreRules.SimilarHost(rock, names)];
                take(id, host);
            }
        }
    }
}
