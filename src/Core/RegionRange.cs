using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// The range fields shared by the data files that say where something lives through Earth's region index (ranges
    /// proposal): "excludedRegions" (every WGSRPD level-3 region but these), other region lists, and "minWarmMonth" (°C:
    /// the warmest month at the ground's real elevation must reach it). Read by <see cref="VanillaFaunaRanges"/>
    /// (fauna-vanilla.json) and <see cref="PeopleRange"/> (people-1318.json). Every catalogue's region lists are
    /// <see cref="RegionSet"/>s of the one index; these two files treat an unknown code as a format error.
    /// </summary>
    public static class RegionRange
    {
        /// <summary>A region list property as a set; null when absent. Unknown codes are a format error.</summary>
        public static RegionSet Regions(JsonElement e, string property, BotanicalRegions regions, string where)
        {
            if (!e.TryGetProperty(property, out var list)) return null;
            var codes = new List<string>();
            foreach (var c in list.EnumerateArray()) codes.Add(c.GetString());
            return regions.Set(codes, code => throw new FormatException(where + " " + property + " has unknown region " + code));
        }

        /// <summary>"minWarmMonth", or NaN when absent (no limit).</summary>
        public static double MinWarmMonth(JsonElement e)
            => e.TryGetProperty("minWarmMonth", out var v) ? v.GetDouble() : double.NaN;

        public static bool Contains(RegionSet list, int region) => list != null && list.Contains(region);

        /// <summary>Whether a land point lies outside an "every region but these" range (nearest region on land).</summary>
        public static bool Excluded(RegionSet excluded, BotanicalRegions regions, double lat, double lon)
            => excluded != null && excluded.Contains(regions.FindLand(lat, lon));

        /// <summary>Whether the warmest month at the ground (°C) is below the limit; false without a limit.</summary>
        public static bool TooCold(double minWarmMonth, double warmAtGround) => !double.IsNaN(minWarmMonth) && warmAtGround < minWarmMonth;
    }

    /// <summary>
    /// Where people lived around 1318 (assets/earth/config/people-1318.json, ranges proposal step 4): every region but those
    /// nobody had settled by then, and no ice-cap climate (warmest month at the ground below "minWarmMonth"). Vanilla's
    /// worldgen structures (ruins, trader camps, structure-placed translocators) and villages are generated only there. An
    /// interim rule: the author plans to remake ruins; Earth Ruins would replace the region list, not the code.
    /// </summary>
    public sealed class PeopleRange
    {
        public const int Version = 1;
        readonly BotanicalRegions regions;
        readonly RegionSet excluded;
        public double MinWarmMonth { get; }

        public PeopleRange(string json, BotanicalRegions regions)
        {
            this.regions = regions ?? throw new ArgumentNullException(nameof(regions));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != Version) throw new FormatException("people-1318.json: unsupported version");
            excluded = RegionRange.Regions(root, "excludedRegions", regions, "people-1318.json")
                ?? throw new FormatException("people-1318.json has no excludedRegions");
            MinWarmMonth = RegionRange.MinWarmMonth(root);
        }

        /// <summary>Whether the place's region was settled by 1318.</summary>
        public bool Settled(double lat, double lon) => !RegionRange.Excluded(excluded, regions, lat, lon);

        /// <summary>Whether people lived here around 1318: a settled region and no ice-cap climate at the ground.</summary>
        public bool Allows(double lat, double lon, double warmAtGround) => Settled(lat, lon) && !RegionRange.TooCold(MinWarmMonth, warmAtGround);
    }
}
