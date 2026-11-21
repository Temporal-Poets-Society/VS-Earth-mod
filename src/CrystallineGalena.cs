using System;
using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Galena, with its silver, in crystalline rock (decision 0102, EA-0174). Vanilla's galena deposit names only sedimentary
    /// hosts (claystone, sandstone, shale, chalk, limestone, chert, conglomerate), so on Earth's regional rock the lead and
    /// silver districts in gneiss, schist, slate and granite (Freiberg, Kongsberg, Broken Hill, the Harz, Cornwall) had none.
    /// <para>
    /// The blocks and items (patches/galena-crystalline-ores.json, written by tools/make_rock_ore_patches.py) exist in every
    /// world, so block lists stay the same whatever a world chose. The hosts are added here, while the game runs, to vanilla's
    /// own galena deposit: the five rocks with their ore blocks, their loose-ore indicator and the silver child's blocks, as
    /// vanilla's DiscDepositGenerator.Init would resolve them had galena.json named the rocks. Only in worlds with regional
    /// rock, vanilla ores and geology rules that include them (<see cref="For"/>), at WorldReady and after every deposit
    /// reload; every other world, and every world made before, keeps vanilla's hosts. The prospecting pick's own copy of the
    /// deposits gets them too, so its silver reading counts the galena the silver nests in.
    /// </para>
    /// Amounts stay vanilla's: the same attempts, ore map and depths, which now also find a host in these rocks. The grades
    /// are the blocks that exist for the rock, poor first, as vanilla orders a resolved host: galena takes the grades vanilla
    /// gives sphalerite, its companion ore, in the same rock (granite poor and medium; slate, phyllite, gneiss and schist all
    /// four). Vanilla's handbook lists the hosts it resolved itself, so it does not name these rocks.
    /// </summary>
    static class CrystallineGalena
    {
        /// <summary>The host rocks, in the order of tools/make_rock_ore_patches.py's GALENA_ROCKS.</summary>
        internal static readonly string[] Rocks = { "granite", "slate", "phyllite", "gneiss", "schist" };
        /// <summary>Vanilla's galena deposit, as galena.json names it.</summary>
        internal const string DepositCode = "galena";
        static readonly string[] Grades = { "poor", "medium", "rich", "bountiful" };

        /// <summary>The prospecting pick's own deposit generator (ProPickWorkSpace.depositGen, private, 1.22.7), built on a
        /// worker thread when the pick loads; null when a game update changed it (the pick's silver reading then misses the
        /// galena in these rocks, and <see cref="Start"/> says so).</summary>
        static readonly AccessTools.FieldRef<ProPickWorkSpace, GenDeposits> pickDeposits =
            PatchVanilla.ReadField<ProPickWorkSpace, GenDeposits>.Resolved("depositGen", out pickProblem);
        static readonly string pickProblem;
        internal static bool PickReadable => pickDeposits != null;

        /// <summary>Whether a world gets the hosts: an Earth world with regional rock and vanilla ores (Earth's own ore maps
        /// choose their hosts themselves) whose geology rules, as this session generates with them, include them.</summary>
        internal static bool For(EarthParams p, int activeGeologyRules)
            => p != null && p.Enabled && p.RegionalGeology && !p.RegionalOres && GeologyFacts.CrystallineGalena(activeGeologyRules);

        /// <summary>At WorldReady in a world <see cref="For"/> allows: the world's deposits now, the pick's as soon as they exist.</summary>
        internal static void Start(ICoreServerAPI api, ILogger log)
        {
            int added = AddTo(api, api.ModLoader.GetModSystem<GenDeposits>(), log);
            if (added > 0)
                log.Notification("Earth geology: galena and its silver also form in {0} in this world (decision 0102): {1} host rocks added to vanilla's galena deposit.",
                    string.Join(", ", Rocks), added);
            if (pickDeposits == null)
            {
                log.Warning("Earth geology: ProPickWorkSpace.depositGen {0} in {1}; the prospecting pick's silver reading misses the galena in these rocks.", pickProblem, PatchVanilla.Versions);
                return;
            }
            Pick(api, log, 60);
        }

        /// <summary>The pick builds its deposits on a worker thread from its OnLoaded, which may end after WorldReady: tried
        /// again every half second (on the main thread, where the pick also reads them) for up to <paramref name="tries"/> times.</summary>
        static void Pick(ICoreServerAPI api, ILogger log, int tries)
        {
            var ws = api.ObjectCache.TryGetValue("propickworkspace", out object o) ? o as ProPickWorkSpace : null;
            var deposits = ws == null ? null : pickDeposits(ws);
            if (deposits != null)
            {
                int added = AddTo(api, deposits, log);
                if (added > 0) log.Notification("Earth geology: the prospecting pick reads galena and its silver in those rocks too ({0} host rocks).", added);
                return;
            }
            if (tries > 0) api.Event.RegisterCallback(_ => Pick(api, log, tries - 1), 500);
            else log.Warning("Earth geology: the prospecting pick's deposits did not appear within 30 s; its silver reading misses the galena in {0}.", string.Join(", ", Rocks));
        }

        /// <summary>
        /// Adds the rocks to <paramref name="deposits"/>' galena deposit as hosts; returns how many it added (0 when they are
        /// there already, so it can run again after a reload). A rock without its rock, ore or loose-ore block in this game
        /// (another mod's rocks replaced Earth's) is left out; a galena deposit of another shape than vanilla's is left alone.
        /// </summary>
        internal static int AddTo(ICoreServerAPI api, GenDeposits deposits, ILogger log)
        {
            var variant = deposits?.Deposits?.FirstOrDefault(v => v?.Code == DepositCode);
            if (variant?.GeneratorInst is not DiscDepositGenerator gen || variant.GeneratorInst is ChildDepositGenerator) return 0;
            string key = gen.InBlock?.Name;
            if (key == null || gen.PlaceBlock?.Code?.Path != "ore-*-galena-{" + key + "}" || gen.InBlock.Code?.Path != "rock-*")
            {
                log.Warning("Earth geology: vanilla's galena deposit is not as decision 0102 read it (in {0}, places {1}); it keeps its own hosts.", gen.InBlock?.Code, gen.PlaceBlock?.Code);
                return 0;
            }
            var hosts = OreVanilla.HostTable(gen);
            var indicators = OreVanilla.IndicatorTable(gen);
            // vanilla puts a loose ore over a host by its indicator table, unguarded: a host without one would throw
            if (hosts == null || (gen.SurfaceBlock != null && indicators == null)) return 0;
            int added = 0;
            var missing = new List<string>();
            foreach (string rock in Rocks)
            {
                var host = api.World.GetBlock(new AssetLocation("game", "rock-" + rock));
                if (host == null || host.BlockId <= 0) { missing.Add("rock-" + rock); continue; }
                if (hosts.ContainsKey(host.BlockId)) continue;
                var ores = Grades.Select(g => api.World.GetBlock(new AssetLocation("game", "ore-" + g + "-galena-" + rock))).Where(b => b != null && b.BlockId > 0).ToArray();
                if (ores.Length == 0) { missing.Add("ore-*-galena-" + rock); continue; }
                Block indicator = null;
                if (gen.SurfaceBlock != null)
                {
                    var code = gen.SurfaceBlock.Code.Clone();
                    code.Path = code.Path.Replace("{" + key + "}", rock);
                    indicator = api.World.GetBlock(code);
                    if (indicator == null || indicator.BlockId <= 0) { missing.Add(code.ToShortString()); continue; }
                }
                // as Init: the children nest in each of the host's ore blocks, then the indicator, then the host itself
                foreach (var child in variant.ChildDeposits ?? Array.Empty<DepositVariant>())
                    if (child?.GeneratorInst is ChildDepositGenerator childGen)
                        foreach (var ore in ores)
                        {
                            try { childGen.ResolveAdd(ore, key, rock); }
                            catch (Exception e) { log.Warning("Earth geology: child deposit {0} of galena does not resolve in {1}: {2}", child.Code, rock, e.Message); }
                        }
                if (indicator != null) indicators[host.BlockId] = new ResolvedDepositBlock { Blocks = new[] { indicator } };
                hosts[host.BlockId] = new ResolvedDepositBlock { Blocks = ores };
                added++;
            }
            if (missing.Count > 0)
                log.Warning("Earth geology: galena stays out of a rock whose blocks this game lacks (another mod's rocks?): {0}.", string.Join(", ", missing));
            return added;
        }
    }
}
