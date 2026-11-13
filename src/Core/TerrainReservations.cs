using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthMap.Core
{
    /// <summary>A fixed rectangular site pad, published before worldgen. Coordinates are inclusive at the
    /// minimum and exclusive at the maximum. The terrain outside the pad eases back over Blend blocks.
    /// Water, river valleys, ice and existing story pads always retain their original columns.</summary>
    public sealed class TerrainReservation
    {
        public string Id { get; }
        public int MinX { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxZ { get; }
        public int TargetTop { get; }
        public int Blend { get; }
        public int MaxCutFill { get; }

        public TerrainReservation(string id, int minX, int minZ, int maxX, int maxZ, int targetTop, int blend, int maxCutFill)
        {
            if (string.IsNullOrWhiteSpace(id) || maxX <= minX || maxZ <= minZ || maxX - minX > 4096 || maxZ - minZ > 4096
                || blend < 0 || blend > 256 || maxCutFill < 0 || maxCutFill > 96 || targetTop < 1)
                throw new ArgumentException("Invalid Earth terrain reservation.");
            Id = id; MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
            TargetTop = targetTop; Blend = blend; MaxCutFill = maxCutFill;
        }

        public bool Intersects(double minX, double minZ, double maxX, double maxZ, bool includeBlend = true)
        {
            int b = includeBlend ? Blend : 0;
            return minX < MaxX + b && maxX >= MinX - b && minZ < MaxZ + b && maxZ >= MinZ - b;
        }

        internal double Weight(double x, double z)
        {
            double dx = Math.Max(Math.Max(MinX - x, 0), x - (MaxX - 1));
            double dz = Math.Max(Math.Max(MinZ - z, 0), z - (MaxZ - 1));
            double d = Math.Sqrt(dx * dx + dz * dz);
            return d == 0 ? 1 : Blend == 0 || d >= Blend ? 0 : 1 - Geo.SmoothStep(d / Blend);
        }
    }

    internal sealed class TerrainReservationIndex
    {
        const int CellSize = 256;
        readonly Dictionary<long, TerrainReservation[]> cells = new Dictionary<long, TerrainReservation[]>();
        public static readonly TerrainReservationIndex Empty = new TerrainReservationIndex(Array.Empty<TerrainReservation>());
        public readonly TerrainReservation[] All;
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static int Cell(double v) => (int)Math.Floor(v / CellSize);

        public TerrainReservationIndex(IEnumerable<TerrainReservation> reservations)
        {
            All = reservations.ToArray();
            var build = new Dictionary<long, List<TerrainReservation>>();
            foreach (var r in All)
                for (int z = Cell(r.MinZ - r.Blend); z <= Cell(r.MaxZ + r.Blend); z++)
                    for (int x = Cell(r.MinX - r.Blend); x <= Cell(r.MaxX + r.Blend); x++)
                    {
                        long k = Key(x, z);
                        if (!build.TryGetValue(k, out var list)) build[k] = list = new List<TerrainReservation>();
                        list.Add(r);
                    }
            foreach (var kv in build) cells[kv.Key] = kv.Value.ToArray();
        }

        public TerrainReservation[] At(double x, double z) => cells.TryGetValue(Key(Cell(x), Cell(z)), out var values) ? values : Array.Empty<TerrainReservation>();

        public bool Intersects(double minX, double minZ, double maxX, double maxZ)
        {
            for (int z = Cell(minZ); z <= Cell(maxZ); z++)
                for (int x = Cell(minX); x <= Cell(maxX); x++)
                    if (cells.TryGetValue(Key(x, z), out var rs))
                        foreach (var r in rs) if (r.Intersects(minX, minZ, maxX, maxZ)) return true;
            return false;
        }
    }
}
