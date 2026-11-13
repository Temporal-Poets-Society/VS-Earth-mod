using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace EarthMap
{
    /// <summary>
    /// Moves vanilla's story locations onto suitable real land. Vanilla picks each location at a random offset from
    /// its parent (spawn, or an earlier location). We keep those distances, anchor the chain at the Earth spawn, try the
    /// four mirror images of each offset and search around each for the best site, at least 75% of vanilla's distance
    /// from spawn and from the parent (since 0.15.5: free mirrors once folded the village back to spawn):
    ///  - surface buildings (village, Tobias' cave, lazaret, treasure hunter): dry, fairly even, ice-free lowland. Since
    ///    0.14.8 only the building's own footprint is levelled, at the ground's own height there, with a short blend
    ///    (vanilla forces a "veryflat" landform over 200 blocks, which our terrain ignores). Tobias' cave brings its own
    ///    hill, so it looks for hilly surroundings where that hill fits in.
    ///  - the devastation area (its blight spreads far beyond its building, over whatever terrain is there) stays at
    ///    vanilla's own spot when that is dry land, else moves to the nearest dry land (with none near, it searches
    ///    around the mirror images of its offset like the others); only its tower's footprint is
    ///    levelled, at the local height. (Before 0.14.8 it got a disc over its 800-block landform radius at sea level + 1.)
    ///  - the resonance archive (underground): dry land at least a few blocks above sea level, no flattening.
    /// Runs once per world, right after vanilla (and Cylinder World) placed them and before any chunk exists;
    /// <see cref="ApplyHeights"/> then sets their heights at every start. The searches read Earth's included heights, not
    /// full GLO-90 (0051); only the chosen site's ground is measured on the world's own heights.
    /// </summary>
    public static class StoryPlacer
    {
        /// <summary>StorySearchRadius bounds (blocks): 0 used to search forever, a huge one scans for minutes at startup.</summary>
        const double MinSearchRadius = 64, MaxSearchRadius = 50_000, DefaultSearchRadius = 3000;
        enum Kind { Underground, FlatLocal, Hill, Dry }
        /// <summary>Levelled pad around a building's footprint: flat this far beyond it, then a blend.</summary>
        const double PadMargin = 8, MinPadBlend = 32, MaxPadBlend = 96;
        /// <summary>A building whose blocks stand this high above its edges brings its own hill (Tobias' cave).</summary>
        const int HillRelief = 30;
        /// <summary>A site keeps at least this share of vanilla's distance from spawn and from its parent.</summary>
        const double MinDistanceShare = 0.75;
        /// <summary>A story structure's building (WorldGenStoryStructure.schematicData, internal), bound by PatchVanilla
        /// (StorySchematics); null, with PatchVanilla's line at start, when a game update renamed or changed it: surface
        /// buildings then stay at vanilla's sites and no story location gets Earth's height.</summary>
        internal static HarmonyLib.AccessTools.FieldRef<WorldGenStoryStructure, BlockSchematicPartial> SchematicRef;

        /// <summary>A planned move of one story location, applied only after every location has been planned.</summary>
        sealed class Move
        {
            public StoryStructureLocation Loc;
            public string Code;
            public int Dx, Dz;
            public Kind Kind;
            public FlatZoneData Zone;
            public double MeanTop, FromParent, VanillaFromSpawn;
        }

        /// <summary>
        /// Moves the story locations to suitable land. All in two steps: every location is planned first (searches only, nothing
        /// changed), then the moves are applied. A failure while planning therefore leaves vanilla's positions untouched.
        /// </summary>
        public static void Place(ICoreServerAPI sapi, EarthMapSystem core, ILogger log)
        {
            var p = core.Params;
            var model = core.Model;
            var story = sapi.ModLoader.GetModSystem<GenStoryStructures>();
            if (story?.Structures == null)
            {
                log.Notification("No story locations in this world.");
                return;
            }
            var scfg = ((IStoryStructureSystem)story).scfg;
            if (scfg?.Structures == null) return;

            // vanilla anchored the chain at the map centre; Cylinder World moved it to its spawn (Earth's own since 0.15.5,
            // Cylinder World 0.11.11)
            double ax = sapi.WorldManager.MapSizeX / 2.0, az = sapi.WorldManager.MapSizeZ / 2.0;
            var cw = CylinderCompat.TryGet(sapi);
            if (cw != null && cw.SpawnZ > 0) { ax = cw.SpawnX; az = cw.SpawnZ; }

            var oldPos = new Dictionary<string, (double x, double z)>();
            foreach (var (code, loc) in story.Structures) oldPos[code] = (loc.CenterPos.X, loc.CenterPos.Z);
            var newPos = new Dictionary<string, (double x, double z)>();
            var taken = new List<(double x, double z, double r)>();
            var moves = new List<Move>();
            double mapX = sapi.WorldManager.MapSizeX, mapZ = sapi.WorldManager.MapSizeZ;
            double radius = core.Config.StorySearchRadius;
            if (!(radius >= MinSearchRadius && radius <= MaxSearchRadius))
            {
                double used = double.IsFinite(radius) && radius > 0 ? Math.Clamp(radius, MinSearchRadius, MaxSearchRadius) : DefaultSearchRadius;
                log.Warning("StorySearchRadius {0} is outside {1}..{2} blocks; using {3}.", radius, MinSearchRadius, MaxSearchRadius, used);
                radius = used;
            }

            // 5 November 2026: on a small Earth with the whole GLO-90 store on the machine the searches (thousands of sites
            // over up to ±12,000 blocks around each mirror image: hundreds of 1° squares at 1:400) decoded tiles again and
            // again through the 24-tile cache, and a new 1:400 world didn't open within minutes. They now read the included
            // heights (this thread only; FineElevation.BuiltInOnly), as the random start's catalogue does.
            bool ownHeights = FineElevation.BuiltInOnly;
            FineElevation.BuiltInOnly = true;
            try
            {
            foreach (var s in scfg.Structures)
            {
                var loc = story.Structures.Get(s.Code);
                if (loc == null || !oldPos.TryGetValue(s.Code, out var old)) continue;

                (double x, double z) parentOld = (ax, az), parentNew = (p.SpawnX, p.SpawnZ);
                if (!string.IsNullOrEmpty(s.DependsOnStructure) && s.DependsOnStructure != "spawn"
                    && oldPos.TryGetValue(s.DependsOnStructure, out var po) && newPos.TryGetValue(s.DependsOnStructure, out var pn))
                {
                    parentOld = po;
                    parentNew = pn;
                }
                double vx = old.x - parentOld.x, vz = old.z - parentOld.z;
                // each site stays at least most of vanilla's distance from spawn and from its parent: free mirrors and
                // the search around them could otherwise fold the chain back (a Lisbon spawn got the village 615 blocks
                // away, vanilla's was 14 000)
                double spawnMin = MinDistanceShare * Math.Sqrt(Math.Pow(old.x - ax, 2) + Math.Pow(old.z - az, 2));
                var away = new List<(double x, double z, double min)>
                {
                    (p.SpawnX, p.SpawnZ, spawnMin),
                    (parentNew.x, parentNew.z, MinDistanceShare * Math.Sqrt(vx * vx + vz * vz)),
                };

                var schematic = SchematicRef?.Invoke(s);
                Kind kind;
                int r, relief = 0;
                double halfX = 0, halfZ = 0;
                if (s.Placement == EnumStructurePlacement.Underground)
                {
                    kind = Kind.Underground;
                    r = Math.Max(48, Math.Max(loc.LandformRadius, 32));
                }
                else if (schematic == null)
                {
                    log.Warning("Story location {0}: its building's size is unknown; it stays at vanilla's x={1} z={2}.", s.Code, loc.CenterPos.X, loc.CenterPos.Z);
                    newPos[s.Code] = old;
                    continue;
                }
                else
                {
                    halfX = schematic.SizeX / 2.0;
                    halfZ = schematic.SizeZ / 2.0;
                    r = Math.Max(24, (int)Math.Ceiling(Math.Sqrt(halfX * halfX + halfZ * halfZ) + PadMargin));
                    relief = Relief(schematic);
                    // the devastation area's blight reaches far beyond its building: it is best on whatever land vanilla chose
                    kind = s.GenerationRadius > 0 ? Kind.Dry : relief >= HillRelief ? Kind.Hill : Kind.FlatLocal;
                }

                Site best = null;
                if (kind == Kind.Dry)
                {
                    // vanilla's own spot when it is dry land, else the nearest dry land around it
                    best = Search(model, old.x, old.z, r, kind, relief, 0, taken, away, mapX, mapZ);
                    for (double reach = 256; best == null && reach <= 4 * radius; reach *= 2)
                        best = Search(model, old.x, old.z, r, kind, relief, reach, taken, away, mapX, mapZ);
                }
                // the mirror images that keep the distance floor from spawn (when none does, the farthest one)
                var targets = new List<(double x, double z)>();
                foreach (int sx in new[] { 1, -1 })
                    foreach (int sz in new[] { 1, -1 })
                        targets.Add((parentNew.x + sx * vx, parentNew.z + sz * vz));
                double SpawnDist((double x, double z) t) => Math.Sqrt(Math.Pow(t.x - p.SpawnX, 2) + Math.Pow(t.z - p.SpawnZ, 2));
                double farthest = targets.Max(SpawnDist);
                targets = targets.Where(t => SpawnDist(t) >= Math.Min(spawnMin, farthest)).ToList();
                // three widening passes: radius, 2x, 4x; a hill that finds no hilly site settles for an even one; the
                // devastation area searches here only when its vanilla spot has no dry land near it (open sea)
                foreach (var tryKind in kind == Kind.Hill ? new[] { Kind.Hill, Kind.FlatLocal } : new[] { kind })
                {
                    double reach = radius;
                    for (int pass = 0; pass < 3 && best == null; pass++, reach *= 2)
                    {
                        foreach (var t in targets)
                        {
                            var site = Search(model, t.x, t.z, r, tryKind, relief, reach, taken, away, mapX, mapZ);
                            if (site != null && (best == null || site.Score < best.Score)) best = site;
                        }
                    }
                    if (best != null) { kind = tryKind; break; }
                }
                if (best == null)
                {
                    log.Warning("No suitable land found for story location {0}; it stays at x={1} z={2}. Use /wgen story setpos {0} to move it.", s.Code, loc.CenterPos.X, loc.CenterPos.Z);
                    newPos[s.Code] = old;
                    continue;
                }

                // the pad's height from the world's own ground (full GLO-90 where the world has it), not the search's
                best.MeanTop = GroundMean(model, best.X, best.Z, r, ownHeights);
                int dx = (int)Math.Round(best.X) - loc.CenterPos.X, dz = (int)Math.Round(best.Z) - loc.CenterPos.Z;
                int nx = loc.CenterPos.X + dx, nz = loc.CenterPos.Z + dz;
                newPos[s.Code] = (nx, nz);
                taken.Add((nx, nz, r));
                FlatZoneData zone = null;
                if (kind != Kind.Underground)
                {
                    int target = Math.Max(model.SeaLevel + 1, (int)Math.Round(best.MeanTop));
                    // the pad follows the building's box, which vanilla rebuilds from the centre at every start:
                    // X1 = centre - SizeX/2 (integer), X2 = X1 + SizeX
                    double bx = nx - schematic.SizeX / 2 + halfX, bz = nz - schematic.SizeZ / 2 + halfZ;
                    zone = new FlatZoneData { Code = s.Code, X = bx, Z = bz, HalfX = halfX, HalfZ = halfZ,
                        Radius = PadMargin, Blend = PadBlend(halfX, halfZ), TargetTop = target };
                }
                moves.Add(new Move { Loc = loc, Code = s.Code, Dx = dx, Dz = dz, Kind = kind, Zone = zone, MeanTop = best.MeanTop,
                    FromParent = Math.Sqrt(Math.Pow(nx - parentNew.x, 2) + Math.Pow(nz - parentNew.z, 2)), VanillaFromSpawn = spawnMin / MinDistanceShare });
            }
            }
            finally
            {
                FineElevation.BuiltInOnly = ownHeights;
            }

            // every location planned: apply the moves
            foreach (var m in moves)
            {
                var loc = m.Loc;
                // CenterPos is shared with vanilla's forced landform/climate entries, so they move along
                loc.CenterPos.X += m.Dx;
                loc.CenterPos.Z += m.Dz;
                var c = loc.Location;
                if (c != null) loc.Location = new Cuboidi(c.X1 + m.Dx, c.Y1, c.Z1 + m.Dz, c.X2 + m.Dx, c.Y2, c.Z2 + m.Dz);
                if (m.Zone != null)
                {
                    var zone = m.Zone;
                    p.Zones.Add(zone);
                    model.FlatZones.Add(zone.ToZone());
                }
                var g = model.Geometry.ToGeo(loc.CenterPos.X, loc.CenterPos.Z);
                log.Notification("Earth Map: story location {0} at {1} (x={2} z={3}), ground y={4:0} ({5}), {6:0} blocks from its parent, {7:0} from spawn (vanilla {8:0})",
                    m.Code, EarthMapSystem.FormatLatLon(g.Lat, g.Lon), loc.CenterPos.X, loc.CenterPos.Z, m.MeanTop, m.Kind, m.FromParent,
                    Math.Sqrt(Math.Pow(loc.CenterPos.X - p.SpawnX, 2) + Math.Pow(loc.CenterPos.Z - p.SpawnZ, 2)), m.VanillaFromSpawn);
            }
            if (moves.Count > 0) story.StoryStructureInstancesDirty = true;
        }

        static double PadBlend(double halfX, double halfZ) => Math.Clamp(0.6 * Math.Max(halfX, halfZ), MinPadBlend, MaxPadBlend);

        /// <summary>How high a schematic's blocks stand above its edges (90th percentile of the column tops minus the
        /// edges' median): about 47 for Tobias' cave, which is a hill, 17 or less for the other buildings.</summary>
        static int Relief(BlockSchematic sc)
        {
            var top = new Dictionary<int, int>();
            int n = Math.Min(sc.Indices.Count, sc.BlockIds.Count);
            for (int i = 0; i < n; i++)
            {
                if (!sc.BlockCodes.TryGetValue(sc.BlockIds[i], out var code) || code == null) continue;
                if (code.Path == "air" || code.Path.StartsWith("meta-")) continue;
                uint idx = sc.Indices[i];
                int x = (int)(idx & 0x3ff), z = (int)((idx >> 10) & 0x3ff), y = (int)((idx >> 20) & 0x3ff);
                int key = z * 1024 + x;
                if (!top.TryGetValue(key, out int t) || y > t) top[key] = y;
            }
            if (top.Count == 0) return 0;
            var all = top.Values.OrderBy(v => v).ToList();
            var edges = top.Where(kv => kv.Key % 1024 == 0 || kv.Key / 1024 == 0 || kv.Key % 1024 == sc.SizeX - 1 || kv.Key / 1024 == sc.SizeZ - 1)
                .Select(kv => kv.Value).OrderBy(v => v).ToList();
            int edge = edges.Count > 0 ? edges[edges.Count / 2] : 0;
            return all[9 * all.Count / 10] - edge;
        }

        /// <summary>
        /// Every start, after vanilla reset them: each story building with an Earth site stands on that site's ground.
        /// Vanilla puts "surface" buildings at sea level (on its forced flat landform) and otherwise reads the height in
        /// whichever chunk generates first, which can be off the levelled pad. Surface buildings keep vanilla's relation
        /// to the ground (one block below the pad's top, as on the old sea-level sites), so older worlds keep the
        /// heights they had.
        /// </summary>
        public static void ApplyHeights(ICoreServerAPI sapi, EarthMapSystem core)
        {
            var zones = core.Params?.Zones;
            if (zones == null || zones.Count == 0) return;
            var story = sapi.ModLoader.GetModSystem<GenStoryStructures>();
            var scfg = story?.Structures == null ? null : ((IStoryStructureSystem)story).scfg;
            if (scfg?.Structures == null) return;
            foreach (var s in scfg.Structures)
            {
                var zone = zones.FirstOrDefault(z => z.Code == s.Code);
                var loc = story.Structures.Get(s.Code);
                if (zone == null || loc?.Location == null || !(SchematicRef?.Invoke(s) is BlockSchematicStructure sc)) continue;
                bool ruin = s.Placement == EnumStructurePlacement.SurfaceRuin && !s.UseWorldgenHeight;
                bool surface = s.Placement == EnumStructurePlacement.Surface && !s.UseWorldgenHeight;
                if (!ruin && !surface && !s.UseWorldgenHeight) continue;
                int h = zone.TargetTop;
                if (surface)
                {
                    h -= 1;
                    s.UseWorldgenHeight = true;
                }
                loc.WorldgenHeight = h;
                var c = loc.Location;
                c.Y1 = ruin ? h - sc.SizeY + sc.OffsetY : h + sc.OffsetY;
                c.Y2 = c.Y1 + sc.SizeY;
                story.StoryStructureInstancesDirty = true;
            }
        }

        sealed class Site
        {
            public double X, Z, Score, MeanTop;
        }

        static Site Search(TerrainModel model, double tx, double tz, int r, Kind kind, int relief, double reach, List<(double x, double z, double r)> taken,
            List<(double x, double z, double min)> away, double mapX, double mapZ)
        {
            double step = Math.Max(96, r / 3.0);
            // the candidates in grid order (rows north to south, each west to east), without those too close to a placed
            // site or a kept-away place, or whose site would leave the map (a map shorter than the Earth crops it)
            var candidates = new List<(double X, double Z, double D)>();
            for (double dz = -reach; dz <= reach; dz += step)
            {
                for (double dx = -reach; dx <= reach; dx += step)
                {
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    if (d > reach) continue;
                    double x = tx + dx, z = tz + dz;
                    bool clash = false;
                    foreach (var t in taken)
                    {
                        if (Math.Sqrt((x - t.x) * (x - t.x) + (z - t.z) * (z - t.z)) < r + t.r + 300) { clash = true; break; }
                    }
                    if (clash) continue;
                    foreach (var a in away)
                    {
                        if (Math.Sqrt((x - a.x) * (x - a.x) + (z - a.z) * (z - a.z)) < a.min) { clash = true; break; }
                    }
                    if (clash) continue;
                    double edge = r + MaxPadBlend + 64;
                    if (x < edge || z < edge || x > mapX - edge || z > mapZ - edge) continue;
                    candidates.Add((x, z, d));
                }
            }
            // each candidate's ground on several cores (ParallelWork, from 2.0.0-rc.4; up to a few thousand candidates of 33 to
            // 81 columns a search): every worker reads the heights this thread reads (FineElevation.BuiltInOnly is per thread:
            // the caller's switch is set on each worker and the worker's own put back), then the best is picked in grid order,
            // the first of equal scores winning, as the single loop did
            var found = new (bool Ok, double Score, double Mean)[candidates.Count];
            bool builtInOnly = FineElevation.BuiltInOnly;
            ParallelWork.For(candidates.Count,
                () => { bool own = FineElevation.BuiltInOnly; FineElevation.BuiltInOnly = builtInOnly; return own; },
                (i, own) =>
                {
                    var (x, z, _) = candidates[i];
                    bool ok = Evaluate(model, x, z, r, kind, relief, out double score, out double mean);
                    found[i] = (ok, score, mean);
                },
                own => FineElevation.BuiltInOnly = own);
            Site best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!found[i].Ok) continue;
                double score = found[i].Score;
                if (reach > 0) score += 25 * candidates[i].D / reach;
                if (best == null || score < best.Score) best = new Site { X = candidates[i].X, Z = candidates[i].Z, Score = score, MeanTop = found[i].Mean };
            }
            return best;
        }

        static readonly (double f, int n)[] Rings = { (0, 1), (0.5, 8), (1.0, 12), (1.0, 12) };

        /// <summary>The mean column top over <see cref="Evaluate"/>'s rings, read with <paramref name="builtInOnly"/> as this
        /// thread's switch (the world's own heights when false: this thread may then wait for a GLO-90 tile as chunk generation
        /// does, so that the pad's height doesn't depend on which tiles happen to be here or on what the thread did before, CR-D1).</summary>
        static double GroundMean(TerrainModel model, double x, double z, int r, bool builtInOnly)
        {
            bool was = FineElevation.BuiltInOnly, fetch = FineElevation.FetchAllowed;
            FineElevation.BuiltInOnly = builtInOnly;
            if (!builtInOnly) FineElevation.FetchAllowed = true;
            try
            {
                double sum = 0;
                int count = 0;
                for (int k = 0; k < Rings.Length; k++)
                {
                    double rr = Rings[k].f * r + (k == 3 ? 64 : 0);
                    int n = Rings[k].n;
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2 * Math.PI * (i + 0.5 * k) / n;
                        sum += model.GetColumn(x + rr * Math.Cos(a), z + rr * Math.Sin(a)).Top;
                        count++;
                    }
                }
                return sum / count;
            }
            finally
            {
                FineElevation.BuiltInOnly = was;
                FineElevation.FetchAllowed = fetch;
            }
        }

        static bool Evaluate(TerrainModel model, double x, double z, int r, Kind kind, int relief, out double score, out double mean)
        {
            score = 0;
            mean = 0;
            var g = model.Geometry.ToGeo(x, z);
            if (g.InGutter || Math.Abs(g.Lat) > 70 || model.Geometry.DistToRingEnd(x) < 3000 + r) return false;
            int sea = model.SeaLevel;
            int min = int.MaxValue, max = int.MinValue, count = 0;
            double sum = 0;
            for (int k = 0; k < Rings.Length; k++)
            {
                double rr = Rings[k].f * r + (k == 3 ? 64 : 0);
                int n = Rings[k].n;
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * (i + 0.5 * k) / n;
                    var c = model.GetColumn(x + rr * Math.Cos(a), z + rr * Math.Sin(a));
                    if (c.Water != TerrainModel.WaterNone || c.Gutter || c.IceBottom <= c.Top) return false;
                    min = Math.Min(min, c.Top);
                    max = Math.Max(max, c.Top);
                    sum += c.Top;
                    count++;
                }
            }
            mean = sum / count;
            // a flattened site must not touch a river anywhere in its blend zone (it would dam the river with a cliff:
            // the treasure hunter's site cut the Rhine at Düsseldorf)
            if (kind != Kind.Underground && model.Rivers != null)
            {
                double zone = r + MaxPadBlend + 12;
                if (model.Rivers.DistanceToWater(x, z, zone) < zone) return false;
            }
            int range = max - min;
            switch (kind)
            {
                case Kind.Underground:
                    if (min < sea + 6) return false;
                    score = range * 0.3 + Math.Abs(mean - (sea + 25)) * 0.2;
                    return true;
                case Kind.Dry:
                    // any dry land will do (the pad levels the building's footprint); evener is a little better
                    score = range * 0.5;
                    return true;
                case Kind.Hill:
                    if (max > sea + 45 || min < sea) return false;
                    // hills around it about as high as the building's own, so it looks like one more of them
                    double hills = Hills(model, x, z, r, mean);
                    if (hills < 0.4 * relief) return false;
                    score = range * 1.5 + Math.Abs(hills - 0.7 * relief);
                    return true;
                default:
                    if (max > sea + 45 || min < sea) return false;
                    score = range * 1.5 + (mean - sea) * 0.3;
                    return true;
            }
        }

        /// <summary>How high the surroundings rise above a site: upper quartile of two rings at 1.6 and 2.4 site radii
        /// (water counts as no rise).</summary>
        static double Hills(TerrainModel model, double x, double z, int r, double mean)
        {
            var rises = new List<double>();
            foreach (double f in new[] { 1.6, 2.4 })
            {
                for (int i = 0; i < 24; i++)
                {
                    double a = 2 * Math.PI * (i + (f > 2 ? 0.5 : 0)) / 24;
                    var c = model.GetColumn(x + f * r * Math.Cos(a), z + f * r * Math.Sin(a));
                    rises.Add(c.Water != TerrainModel.WaterNone ? 0 : c.Top - mean);
                }
            }
            rises.Sort();
            return rises[3 * rises.Count / 4];
        }

        /// <summary>Spawn and story locations for map markers: (x, z, isSpawn).</summary>
        public static List<(double x, double z, bool spawn)> Markers(ICoreServerAPI sapi, EarthMapSystem core)
        {
            var list = new List<(double, double, bool)> { (core.Params.SpawnX, core.Params.SpawnZ, true) };
            var story = sapi.ModLoader.GetModSystem<GenStoryStructures>();
            if (story?.Structures != null)
            {
                foreach (var (_, loc) in story.Structures) list.Add((loc.CenterPos.X, loc.CenterPos.Z, false));
            }
            return list;
        }

        public static string Describe(ICoreServerAPI sapi, EarthMapSystem core)
        {
            var sb = new StringBuilder();
            var model = core.Model;
            sb.AppendFormat(CultureInfo.InvariantCulture, "Spawn: {0} (x={1} z={2})", EarthMapSystem.FormatLatLon(core.Params.SpawnLat, core.Params.SpawnLon), core.Params.SpawnX, core.Params.SpawnZ);
            var story = sapi.ModLoader.GetModSystem<GenStoryStructures>();
            if (story?.Structures == null) return sb.ToString();
            foreach (var (code, loc) in story.Structures)
            {
                var g = model.Geometry.ToGeo(loc.CenterPos.X, loc.CenterPos.Z);
                var zone = core.Params.Zones?.FirstOrDefault(zz => zz.Code == code);
                sb.AppendFormat(CultureInfo.InvariantCulture, "\n{0}: {1} (x={2} z={3}){4}", code, EarthMapSystem.FormatLatLon(g.Lat, g.Lon), loc.CenterPos.X, loc.CenterPos.Z,
                    zone == null ? "" : zone.HalfX > 0
                        ? string.Format(CultureInfo.InvariantCulture, ", levelled to y={0} over {1:0}x{2:0} blocks", zone.TargetTop, 2 * (zone.HalfX + zone.Radius), 2 * (zone.HalfZ + zone.Radius))
                        : string.Format(CultureInfo.InvariantCulture, ", flattened to y={0} within {1:0} blocks", zone.TargetTop, zone.Radius));
            }
            return sb.ToString();
        }
    }
}
