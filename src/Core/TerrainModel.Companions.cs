using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: companion terrain planning: site pads (reservations), terrain expansions, the plan signature and story terrain checks.
    public sealed partial class TerrainModel
    {
        readonly object reservationLock = new object();
        readonly HashSet<string> reservationOwners = new HashSet<string>(StringComparer.Ordinal);
        volatile TerrainReservationIndex reservations = TerrainReservationIndex.Empty;
        volatile TerrainExpansion[] expansions = Array.Empty<TerrainExpansion>();
        readonly HashSet<string> expansionOwners = new HashSet<string>(StringComparer.Ordinal);
        bool reservationsSealed;

        /// <summary>Publish immutable companion site pads during EarthWorldGen.TerrainPlanning. No updates
        /// are permitted once planning closes. A default Earth world has no reservations and is unchanged.</summary>
        public void RegisterTerrainReservations(string owner, IEnumerable<TerrainReservation> sites)
        {
            if (string.IsNullOrWhiteSpace(owner) || sites == null) throw new ArgumentException("Owner and sites are required.");
            lock (reservationLock)
            {
                if (reservationsSealed) throw new InvalidOperationException("Earth terrain planning has closed.");
                if (reservationOwners.Contains(owner)) throw new InvalidOperationException("Owner already published Earth reservations: " + owner);
                var all = new List<TerrainReservation>(reservations.All);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var site in sites)
                {
                    if (site == null || !ids.Add(site.Id) || site.TargetTop >= Vertical.MapHeight - 1)
                        throw new ArgumentException("Invalid or duplicate Earth reservation.");
                    // the real seam: both laps of the ring are the same column, but a pad applies at its own x only, so it is kept
                    // out of Cylinder World's identical band as expansions are (IsTerrainExpansionAllowed)
                    if (RealSeam && (site.MinX - site.Blend < Geometry.RingStart + RealSeamKeepOut
                        || site.MaxX + site.Blend > Geometry.RingStart + Geometry.RingLength - RealSeamKeepOut))
                        throw new ArgumentException("Earth reservation lies within " + RealSeamKeepOut + " blocks of the real seam: " + site.Id);
                    if (IntersectsStoryTerrain(site.MinX - site.Blend, site.MinZ - site.Blend, site.MaxX + site.Blend, site.MaxZ + site.Blend))
                        throw new ArgumentException("Earth reservation intersects protected story terrain: " + site.Id);
                    foreach (var old in all)
                        if (old.Intersects(site.MinX - site.Blend, site.MinZ - site.Blend, site.MaxX + site.Blend, site.MaxZ + site.Blend))
                            throw new ArgumentException("Earth reservations overlap: " + site.Id + " / " + old.Id);
                    all.Add(site);
                }
                reservations = new TerrainReservationIndex(all);
                reservationOwners.Add(owner);
            }
        }

        public void SealTerrainReservations() { lock (reservationLock) reservationsSealed = true; }
        /// <summary>Whether the companions' plan is sealed (<see cref="SealTerrainReservations"/>): from then on no pad or
        /// expansion can change, and a column is a pure function of its position (what the column store relies on).</summary>
        public bool PlanSealed { get { lock (reservationLock) return reservationsSealed; } }
        public bool HasTerrainExpansions => expansions.Length != 0;

        /// <summary>Check the entire bounded domain, not just the monument, before prospective sampling.
        /// Optional physical map dimensions make the bounds explicit for nonstandard Cylinder World layouts.</summary>
        public bool IsTerrainExpansionAllowed(TerrainExpansion expansion, out string reason, int mapWidth = 0, int mapLength = 0)
        {
            reason = null;
            if (expansion == null) { reason = "Missing terrain expansion."; return false; }
            double r = expansion.OuterRadius, x = expansion.CenterX, z = expansion.CenterZ;
            double keepOut = Math.Max(Geometry.Gutter / 2, RealSeam ? RealSeamKeepOut : 0);
            double west = Math.Max(0, Geometry.RingStart + keepOut) + 8;
            double east = Geometry.RingStart + Geometry.RingLength - keepOut - 8;
            if (mapWidth > 0) east = Math.Min(east, mapWidth - 8);
            double north = Math.Max(0, Geometry.EquatorZ - 90 * Geometry.BlocksPerDegreeZ) + 8;
            double south = Math.Min(mapLength > 0 ? mapLength : 2 * Geometry.EquatorZ,
                Geometry.EquatorZ + 90 * Geometry.BlocksPerDegreeZ) - 8;
            if (x - r < west || x + r > east || z - r < north || z + r > south)
            { reason = "Terrain expansion intersects a world edge, pole or seam gutter."; return false; }
            if (IntersectsStoryTerrain(x - r, z - r, x + r, z + r))
            { reason = "Terrain expansion intersects protected story terrain."; return false; }
            foreach (var old in expansions)
            {
                double dx = old.CenterX - x, dz = old.CenterZ - z, reach = old.OuterRadius + r;
                if (dx * dx + dz * dz < reach * reach || old.Id == expansion.Id)
                { reason = "Terrain expansion overlaps an existing expansion."; return false; }
            }
            return true;
        }

        /// <summary>Publish immutable local geography before chunk generation. Existing world data is never rebuilt.</summary>
        public void RegisterTerrainExpansions(string owner, IEnumerable<TerrainExpansion> sites, int mapWidth = 0, int mapLength = 0)
        {
            if (string.IsNullOrWhiteSpace(owner) || sites == null) throw new ArgumentException("Owner and expansions are required.");
            lock (reservationLock)
            {
                if (reservationsSealed) throw new InvalidOperationException("Earth terrain planning has closed.");
                if (expansionOwners.Contains(owner)) throw new InvalidOperationException("Owner already published Earth expansions: " + owner);
                var all = new List<TerrainExpansion>(expansions);
                foreach (var site in sites)
                {
                    if (!IsTerrainExpansionAllowed(site, out string why, mapWidth, mapLength)) throw new ArgumentException(why);
                    foreach (var old in all)
                    {
                        double dx = old.CenterX - site.CenterX, dz = old.CenterZ - site.CenterZ, r = old.OuterRadius + site.OuterRadius;
                        if (old.Id == site.Id || dx * dx + dz * dz < r * r) throw new ArgumentException("Terrain expansion domains overlap or have duplicate ids.");
                    }
                    all.Add(site);
                }
                expansions = all.ToArray();
                expansionOwners.Add(owner);
            }
        }

        TerrainExpansion ExpansionAt(double x, double z)
        {
            foreach (var expansion in expansions) if (expansion.Contains(x, z)) return expansion;
            return null;
        }

        /// <summary>Coordinate used to sample natural terrain and rivers. Raw RiverNetwork interfaces remain canonical.</summary>
        public void ProjectTerrain(double x, double z, out double canonicalX, out double canonicalZ)
        {
            var expansion = ExpansionAt(x, z);
            canonicalX = x; canonicalZ = z;
            expansion?.Project(x, z, out canonicalX, out canonicalZ);
        }

        /// <summary>Source geography of the locally expanded landscape. Geometry.ToGeo retains physical attribution.</summary>
        public EarthGeometry.GeoPos TerrainGeoAt(double x, double z)
        {
            ProjectTerrain(x, z, out double sourceX, out double sourceZ);
            return Geometry.ToGeo(sourceX, sourceZ);
        }
        /// <summary>Canonical description of the published companion pads and expansions ("" when there are none), so a
        /// save can notice when companions were added, removed or changed between starts.</summary>
        public string TerrainPlanSignature()
        {
            var parts = new List<string>();
            foreach (var r in reservations.All)
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "pad:{0}:{1},{2},{3},{4}:{5}:{6}:{7}",
                    r.Id, r.MinX, r.MinZ, r.MaxX, r.MaxZ, r.TargetTop, r.Blend, r.MaxCutFill));
            foreach (var e in expansions)
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "expansion:{0}:{1:R},{2:R}:{3:R}:{4:R}:{5:R}",
                    e.Id, e.CenterX, e.CenterZ, e.InnerRadius, e.OuterRadius, e.Magnification));
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts);
        }
        public bool HasTerrainReservations => reservations.All.Length > 0;
        public bool IntersectsReservedTerrain(double minX, double minZ, double maxX, double maxZ)
            => reservations.Intersects(minX, minZ, maxX, maxZ);

        public bool IntersectsStoryTerrain(double minX, double minZ, double maxX, double maxZ)
            => StoryTerrain.Intersects(FlatZones, Geometry, minX, minZ, maxX, maxZ);
    }
}
