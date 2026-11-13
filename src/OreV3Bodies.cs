using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Rules v3's ore bodies (decision 0006): coal measures, iron formations and the rich districts, laid by
    /// <see cref="OreRules.BedsAt"/> after the rock strata (in ore-layer worlds after the ground pass). Rules v4 replace them
    /// with the deposit catalogue (EarthOreDeposits). Part of an <see cref="OreSession"/>, set up when it is sealed.
    /// </summary>
    sealed class V3OreBodies
    {
        readonly OreSession session;
        /// <summary>One active bed set (OreRules.BedSet) with its evidence layer, the binding per bed and its richness.</summary>
        internal sealed class ActiveBeds
        {
            public OreRules.BedSet Set; public int Layer; public OreBinding[] Bindings; public double Richness;
            /// <summary>Per binding: not switched off by the saved tuning (BedEnabled, fixed for the session).</summary>
            public bool[] Enabled;
        }
        internal readonly List<ActiveBeds> Beds = new();
        /// <summary>Any ore bodies to place (none in rules v4; OreBinder clears them without the strata hook).</summary>
        internal bool Active => Beds.Count > 0;

        internal V3OreBodies(OreSession session) { this.session = session; }

        /// <summary>
        /// The active bed sets: coal measures (Ores.CoalBasins), iron formations (Ores.IronFormations) and a rich district for
        /// every other resource with a bound rock-hosted main deposit (Ores.RichDistricts; not soil, quartz, gem pockets, Earth
        /// bauxite or children such as gold in quartz). Each takes its resource's district ramp and Richness from the settings
        /// (decision 0006, "Tunable maps"); the richness is also scaled by the world's deposit rate.
        /// </summary>
        internal void Build()
        {
            Beds.Clear();
            // 0065: rules v4 replace coal measures, iron formations and rich districts with the catalogue
            if (session.RulesV4) return;
            var tuning = session.Tuning; var bindings = session.Bindings;
            void Add(OreRules.BedSet set, double family)
            {
                int layer = session.Catalog.LayerIndex(set.Layer);
                double setting = tuning.DistrictRichness(set.Layer, family);
                set = tuning.DistrictSet(set);
                if (layer < 0 || !(setting > 0)) return;
                var main = bindings.Where(b => b.Resource == set.Layer && !b.IsChild && b.Generator.RockHosted).ToList();
                var per = set.Beds.Select(spec => main.FirstOrDefault(b => b.Variant.Code == spec.Code) ?? main.FirstOrDefault(b => set.Beds.Any(s => s.Code == b.Variant.Code))).ToArray();
                if (per.All(b => b == null)) return;
                Beds.Add(new ActiveBeds { Set = set, Layer = layer, Bindings = per, Richness = setting * session.DepositRate, Enabled = per.Select(b => b != null && BedEnabled(b)).ToArray() });
            }
            Add(OreRules.CoalMeasures, tuning.CoalBasins);
            Add(OreRules.IronFormations, tuning.IronFormations);
            int refTop = session.Core.Model.SeaLevel + 30;
            foreach (var group in bindings.Where(b => !b.IsChild && b.Generator.RockHosted && b.Class is OreClass.Standard or OreClass.Progression
                         && !b.KeepsNativeDepth && !b.Generator.GemPocket && b.Resource is not ("C_coal" or "Fe")).GroupBy(b => b.Resource))
            {
                var main = group.OrderByDescending(b => b.Generator.ExpectedPerChunk).First();
                Add(OreRules.District(group.Key, main.Variant.Code, (int)Math.Round(main.Generator.TypicalDepth(refTop))), tuning.RichDistricts);
            }
        }

        /// <summary>The resource isn't switched off by the world's saved tuning: frequency or concentration 0, or a hotspot
        /// chance of 0 as set (the profile lifts the hotspot chance to the background, so the setting itself is read).</summary>
        bool BedEnabled(OreBinding b) => !session.Tuning.OreBodiesOff(b.Resource, b.Class);
        /// <summary>The saved tuning's depth limits (MinDepthBlocks, MaxDepthBlocks) allow ore at this depth below the surface.</summary>
        static bool InDepthRange(OreBinding b, int depth)
            => b.KeepsNativeDepth || depth >= b.Profile.MinDepth && (b.Profile.MaxDepth <= 0 || depth <= b.Profile.MaxDepth);
        /// <summary>One block of an ore body: within the depth limits, and filled with the resource's concentration, as in discs.</summary>
        bool BedBlock(OreBinding b, int depth, int x, int y, int z, int salt)
            => InDepthRange(b, depth) && RegionalOreCatalog.Random(session.Seed, x, y, z, salt) < b.Profile.Concentration;

        /// <summary>District strength of a bed set at a world position, 0 outside the Earth.</summary>
        double StrengthAt(ActiveBeds a, double x, double z)
        {
            var g = session.Core.Model.Geometry.ToGeo(x, z);
            if (g.InGutter || g.BeyondPole) return 0;
            // the beds lie within ~60 blocks: evidence buried down to 300 m counts
            return OreRules.BedStrength(a.Set, session.Catalog.Heat(a.Layer, g.Lat, g.Lon, 300 / session.EvidenceMetresPerBlock, session.EvidenceMetresPerBlock));
        }
        /// <summary>(CR-O8) Placement's strength inside a chunk: the corners' strengths interpolated at the block's centre
        /// (<paramref name="lx"/>, <paramref name="lz"/>: 0..31 in the chunk), so Place and prospecting share one formula.</summary>
        static double Interpolated(double b00, double b10, double b01, double b11, int lx, int lz)
        {
            double tx = (lx + .5) / 32, tz = (lz + .5) / 32;
            return (b00 + (b10 - b00) * tx) * (1 - tz) + (b01 + (b11 - b01) * tx) * tz;
        }
        /// <summary>(CR-O8) The strength Place used at block column (x, z), from its chunk's corners; 0 where Place skipped the
        /// chunk (no corner above 0). For prospecting and /earth ores, which read the exact value at the column before, so at
        /// district edges they reported seams that were not placed or missed ones that were.</summary>
        double PlacedStrength(ActiveBeds a, int x, int z)
        {
            int bx = x >> 5 << 5, bz = z >> 5 << 5;
            double b00 = StrengthAt(a, bx, bz), b10 = StrengthAt(a, bx + 32, bz), b01 = StrengthAt(a, bx, bz + 32), b11 = StrengthAt(a, bx + 32, bz + 32);
            if (b00 <= 0 && b10 <= 0 && b01 <= 0 && b11 <= 0) return 0;
            return Interpolated(b00, b10, b01, b11, x - bx, z - bz);
        }

        /// <summary>
        /// Places the ore bodies in a chunk column after the rock strata (OreRules.BedsAt): the district strength is
        /// interpolated between the chunk's corners (so it is continuous across chunks); each bed replaces rock only (not
        /// soil, air, water, ice or other ore) with the ore block of that rock or of the most similar host, graded towards rich
        /// ore with the strength in broad patches.
        /// </summary>
        internal void Place(IChunkColumnGenerateRequest request)
        {
            long seed = session.Seed;
            int bx = request.ChunkX * 32, bz = request.ChunkZ * 32;
            var chunks = request.Chunks; var map = chunks[0].MapChunk;
            int height = chunks.Length * 32;
            Span<OreRules.Bed> beds = stackalloc OreRules.Bed[OreRules.MaxBeds];
            foreach (var a in Beds)
            {
                double b00 = StrengthAt(a, bx, bz), b10 = StrengthAt(a, bx + 32, bz), b01 = StrengthAt(a, bx, bz + 32), b11 = StrengthAt(a, bx + 32, bz + 32);
                if (b00 <= 0 && b10 <= 0 && b01 <= 0 && b11 <= 0) continue;
                long placed = 0;
                for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
                {
                    // the same arithmetic as before, now shared with prospecting (CR-O8)
                    double strength = Interpolated(b00, b10, b01, b11, x, z);
                    int n = OreRules.BedsAt(a.Set, seed, bx + x, bz + z, strength, a.Richness, beds);
                    if (n == 0) continue;
                    int top = Math.Min(height - 1, (int)map.WorldGenTerrainHeightMap[z * 32 + x]);
                    for (int i = 0; i < n; i++)
                    {
                        var binding = a.Bindings[beds[i].Index];
                        var gen = binding?.Generator;
                        if (gen == null || !a.Enabled[beds[i].Index]) continue;
                        double grade = RegionalOreCatalog.GradeQuantile(OreRules.BlockNoise(seed, bx + x, bz + z, 48, a.Set.Salt + 300 + beds[i].Index), strength, binding.Profile.QualityBias);
                        for (int t = 0; t < beds[i].Thickness; t++)
                        {
                            int depth = beds[i].Depth + t, y = top - depth;
                            if (y <= 1) break;
                            if (!BedBlock(binding, depth, bx + x, y, bz + z, a.Set.Salt + 400 + beds[i].Index)) continue;
                            int index = ((y % 32) * 32 + z) * 32 + x;
                            int rock = chunks[y / 32].Data.GetBlockIdUnsafe(index);
                            if (!session.IsRock(rock)) continue;
                            var blocks = gen.BodyBlocks(chunks, request.ChunkX, request.ChunkZ, x, y, z, top, rock);
                            if (blocks == null || blocks.Length == 0) continue;
                            chunks[y / 32].Data.SetBlockUnsafe(index, blocks[RegionalOreCatalog.GradeIndex(grade, blocks.Length)].Id);
                            placed++;
                        }
                    }
                }
                if (placed > 0) session.Totals.RecordBlocks(a.Set.Layer, placed);
            }
        }

        /// <summary>Ore body blocks of one deposit code under a column, for prospecting; <paramref name="basin"/> is the
        /// strongest district strength there among the sets that have that code. Nothing at or below the rock floor's top
        /// <paramref name="floor"/> (0: none).</summary>
        /// <param name="placeable">0060: whether a binding's host in a layer it may use lies at a depth (ore-layer worlds); null: every depth counts.</param>
        internal double SeamBlocks(string code, int x, int z, int top, int floor, out double basin, System.Func<OreBinding, int, bool> placeable = null)
        {
            basin = 0;
            double blocks = 0;
            Span<OreRules.Bed> beds = stackalloc OreRules.Bed[OreRules.MaxBeds];
            foreach (var a in Beds)
            {
                if (!a.Bindings.Any(b => b?.Variant.Code == code)) continue;
                // the strength placement used here, not the exact one at the column (CR-O8)
                double strength = PlacedStrength(a, x, z);
                int n = OreRules.BedsAt(a.Set, session.Seed, x, z, strength, a.Richness, beds);
                double here = 0;
                for (int i = 0; i < n; i++)
                {
                    var binding = a.Bindings[beds[i].Index];
                    if (binding?.Variant.Code != code || !a.Enabled[beds[i].Index]) continue;
                    for (int t = 0; t < beds[i].Thickness; t++)
                    {
                        int depth = beds[i].Depth + t;
                        // as placement: nothing at or below the world's floor (y <= 1) or in the rock floor's mantle
                        if (top - depth > Math.Max(1, floor) && InDepthRange(binding, depth) && (placeable == null || placeable(binding, depth))) here += binding.Profile.Concentration;
                    }
                }
                if (here > 0) { blocks += here; basin = Math.Max(basin, strength); }
            }
            return blocks;
        }

        /// <summary>The bodies under (x, z) for /earth ores ("" when there are none): each set's strength and its beds.</summary>
        internal string Describe(double x, double z)
        {
            string seams = "";
            Span<OreRules.Bed> s = stackalloc OreRules.Bed[OreRules.MaxBeds];
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            foreach (var a in Beds)
            {
                // the strength placement used in this block column (CR-O8)
                double strength = PlacedStrength(a, ix, iz);
                if (strength <= 0) continue;
                int n = OreRules.BedsAt(a.Set, session.Seed, ix, iz, strength, a.Richness, s);
                var parts = new List<string>();
                for (int i = 0; i < n; i++) parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} blocks of {1} at {2} below the surface", s[i].Thickness, a.Set.Beds[s[i].Index].Code, s[i].Depth));
                seams += string.Format(CultureInfo.InvariantCulture, "; {0} {1:0.00}{2}", a.Set.Name, strength, n > 0 ? ": " + string.Join(", ", parts) : "");
            }
            return seams;
        }
    }
}
