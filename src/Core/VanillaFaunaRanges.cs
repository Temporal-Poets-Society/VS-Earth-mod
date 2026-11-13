using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Natural ranges of vanilla's land animals (assets/earth/config/fauna-vanilla.json, tools/build-vanilla-fauna.py,
    /// decision 0031): each animal type (entity code family and type, e.g. bear-polar, hyena-spotted) has the PHYLACINE
    /// present-natural range of the species it depicts, chickens a box over the red junglefowl's, butterflies every region
    /// but Antarctica ("excludedRegions", read through the region index). Types it doesn't list (other mods' animals) are
    /// not limited, nor are ranges given as regions when no region index is given.
    /// <para>Each entry may also carry "minWarmMonth" (°C): the animal lives only where the warmest month at the spawn's real
    /// elevation is at least this (real-altitude worlds, decision 0031): 6 °C, the tree line, for forest and lowland
    /// animals; 0.5 °C, the snow line, for those of alpine meadows and fell-fields; none for goats. Ranges and these limits
    /// replace the old "no land animals south of 60°S" guard (ranges proposal, step 2).</para>
    /// <para>Given the region index, worlds placed as around 1318 (decision 0054) use each entry's 1318 range instead: the
    /// natural range without the regions the species had lost by then, and for domestic animals, which went feral when people
    /// disappeared, the regions where people kept them ("range1318Regions", replacing the natural range) or kept them beyond it
    /// ("added1318Regions"). The losses never remove an entry's current range ("currentMask", PHYLACINE current).</para>
    /// <para>Decision 0054's "Chickens everywhere" replaces the chicken's range (natural and 1318) with every region but its
    /// "everywhereExcludedRegions": everywhere people could keep them, not Antarctica.</para>
    /// </summary>
    public sealed class VanillaFaunaRanges
    {
        /// <summary>2: 1318 ranges (decision 0054); the tur ("goat-turdag") takes the East Caucasian tur's range. 3: altitude
        /// limits as data ("minWarmMonth") and a butterfly range ("excludedRegions"); no latitude guard.</summary>
        public const int Version = 3;

        sealed class Entry
        {
            public byte[] Mask, Current;
            public GeoBox[] Boxes;
            /// <summary>Natural range: every region but these.</summary>
            public RegionSet Excluded;
            /// <summary>Not limited by range (a range given as regions without a region index); the altitude limit still applies.</summary>
            public bool Unlimited;
            public double MinWarmMonth = double.NaN;
            public RegionSet Range1318, Added1318, Lost1318;
            public GeoBox[] LostBoxes1318;
            public bool Has1318 => Range1318 != null || Added1318 != null || Lost1318 != null || LostBoxes1318 != null;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly BotanicalRegions regions;
        readonly bool use1318;
        /// <summary>The listed animal types ("bear-polar", "chicken", ...), for /earth where.</summary>
        public IEnumerable<string> Keys => entries.Keys;
        /// <summary>Number of animal types limited by a range.</summary>
        public int Count { get; }
        /// <summary>Whether the 1318 ranges are in effect (a region index was given, and the world is placed as around 1318).</summary>
        public bool Uses1318 => regions != null && use1318;

        /// <param name="regions">The WGSRPD level-3 index (treeregions.json, shared); needed for region-based ranges and 1318.</param>
        /// <param name="chickensEverywhere">Decision 0054's world option: chickens are not limited by range (climate only), so
        /// every region has feathers for arrows.</param>
        /// <param name="use1318">With regions: the 1318 ranges (decision 0054) instead of the natural ones.</param>
        public VanillaFaunaRanges(string json, BotanicalRegions regions = null, bool chickensEverywhere = false, bool use1318 = true)
        {
            this.regions = regions;
            this.use1318 = use1318;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != Version) throw new FormatException("fauna-vanilla.json: unsupported version");
            foreach (var e in root.GetProperty("entries").EnumerateArray())
            {
                string key = e.GetProperty("family").GetString();
                if (e.TryGetProperty("type", out var t)) key += "-" + t.GetString();
                var entry = new Entry();
                entry.MinWarmMonth = RegionRange.MinWarmMonth(e);
                bool everywhere = chickensEverywhere && key == "chicken";
                if (everywhere) ExcludedRange(e, "everywhereExcludedRegions", entry, key);
                else if (e.TryGetProperty("mask", out var m))
                {
                    entry.Mask = Convert.FromBase64String(m.GetString());
                    if (entry.Mask.Length != RegionalFaunaCatalog.MaskBytes) throw new FormatException("fauna-vanilla.json: " + key + " has a bad range raster");
                }
                else if (e.TryGetProperty("envelopes", out var env)) entry.Boxes = Boxes(env, key);
                else if (e.TryGetProperty("excludedRegions", out _)) ExcludedRange(e, "excludedRegions", entry, key);
                else throw new FormatException("fauna-vanilla.json: " + key + " has no range");
                if (Uses1318 && !everywhere)
                {
                    entry.Range1318 = Regions(e, "range1318Regions", key);
                    entry.Added1318 = Regions(e, "added1318Regions", key);
                    entry.Lost1318 = Regions(e, "lost1318Regions", key);
                    if (e.TryGetProperty("lost1318Envelopes", out var lost)) entry.LostBoxes1318 = Boxes(lost, key);
                    if (e.TryGetProperty("currentMask", out var cm))
                    {
                        entry.Current = Convert.FromBase64String(cm.GetString());
                        if (entry.Current.Length != RegionalFaunaCatalog.MaskBytes) throw new FormatException("fauna-vanilla.json: " + key + " has a bad current raster");
                    }
                }
                entries[key] = entry;
                if (!entry.Unlimited) Count++;
            }
        }

        void ExcludedRange(JsonElement e, string property, Entry entry, string key)
        {
            if (!e.TryGetProperty(property, out _)) throw new FormatException("fauna-vanilla.json: " + key + " has no " + property);
            if (regions != null) entry.Excluded = Regions(e, property, key);
            else entry.Unlimited = true;
        }

        static GeoBox[] Boxes(JsonElement list, string key)
        {
            var boxes = new List<GeoBox>();
            foreach (var b in list.EnumerateArray())
            {
                var values = new List<double>();
                foreach (var v in b.EnumerateArray()) values.Add(v.GetDouble());
                if (!GeoBox.TryFrom(values.ToArray(), out var box)) throw new FormatException("fauna-vanilla.json: " + key + " has a bad box");
                boxes.Add(box);
            }
            return boxes.ToArray();
        }

        RegionSet Regions(JsonElement e, string property, string key) => RegionRange.Regions(e, property, regions, "fauna-vanilla.json: " + key);

        // Entity code paths are few and long-lived; caching their entry keeps the spawn checks free of substring allocations.
        readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> byPath = new System.Collections.Concurrent.ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        Entry Find(string path) => byPath.TryGetValue(path, out var e) ? e : byPath.GetOrAdd(path, Resolve(path));

        Entry Resolve(string path)
        {
            int a = path.IndexOf('-');
            string family = a < 0 ? path : path.Substring(0, a);
            if (entries.TryGetValue(family, out var e)) return e;
            if (a < 0) return null;
            int b = path.IndexOf('-', a + 1);
            return entries.TryGetValue(b < 0 ? path : path.Substring(0, b), out e) ? e : null;
        }

        /// <summary>Whether a vanilla animal (entity code path such as "bear-polar-adult-male") may live at a place; true for
        /// animals without a range here.</summary>
        public bool Allows(string path, double lat, double lon)
        {
            var e = Find(path);
            if (e == null || e.Unlimited) return true;
            if (!Uses1318 || !e.Has1318) return Natural(e, lat, lon);
            int region = regions.FindLand(lat, lon);   // land animals: every land point has a region
            bool inRange = e.Range1318 != null ? Contains(e.Range1318, region)
                : Natural(e, lat, lon) || e.Added1318 != null && Contains(e.Added1318, region);
            if (!inRange) return false;
            bool lost = e.Lost1318 != null && Contains(e.Lost1318, region) || e.LostBoxes1318 != null && GeoBox.Any(e.LostBoxes1318, lat, lon);
            return !lost || e.Current != null && RegionalFaunaCatalog.InMask(e.Current, lat, lon);
        }

        /// <summary>Whether a range limits this animal (entity code path); for the placement inventory (decision 0047).</summary>
        public bool Covers(string path) => Find(path) is { Unlimited: false };

        /// <summary>Lowest warmest-month mean (°C, at the spawn's real elevation) the animal lives at; NaN: no limit or not listed.</summary>
        public double MinWarmMonth(string path) => Find(path)?.MinWarmMonth ?? double.NaN;

        bool Natural(Entry e, double lat, double lon)
            => e.Mask != null ? RegionalFaunaCatalog.InMask(e.Mask, lat, lon)
            : e.Boxes != null ? GeoBox.Any(e.Boxes, lat, lon)
            : !RegionRange.Excluded(e.Excluded, regions, lat, lon);

        static bool Contains(RegionSet list, int region) => RegionRange.Contains(list, region);
    }
}
