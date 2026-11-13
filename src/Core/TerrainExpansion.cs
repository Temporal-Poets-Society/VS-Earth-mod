using System;

namespace EarthMap.Core
{
    /// <summary>Immutable local magnification of canonical Earth terrain around a fixed physical anchor.
    /// Outside OuterRadius the map and its derivative are exactly the identity. No Earth river data is edited.</summary>
    public sealed class TerrainExpansion
    {
        public string Id { get; }
        public double CenterX { get; }
        public double CenterZ { get; }
        public double InnerRadius { get; }
        public double OuterRadius { get; }
        public double Magnification { get; }

        public TerrainExpansion(string id, double centerX, double centerZ, double innerRadius, double outerRadius, double magnification)
        {
            if (string.IsNullOrWhiteSpace(id) || !double.IsFinite(centerX) || !double.IsFinite(centerZ)
                || !double.IsFinite(innerRadius) || !double.IsFinite(outerRadius) || !double.IsFinite(magnification)
                || innerRadius <= 0 || outerRadius <= innerRadius || outerRadius > 8192
                || magnification < 1 || magnification > 64)
                throw new ArgumentException("Invalid local terrain expansion.");
            Id = id; CenterX = centerX; CenterZ = centerZ; InnerRadius = innerRadius;
            OuterRadius = outerRadius; Magnification = magnification;
        }

        public bool Contains(double x, double z)
        {
            double dx = x - CenterX, dz = z - CenterZ;
            return Magnification != 1 && dx * dx + dz * dz < OuterRadius * OuterRadius;
        }

        // f(r) = r * alpha(r), alpha = 1 - (1 - 1/m) * w. The quintic w falls
        // monotonically from one to zero; f' = alpha + r*alpha' is always positive.
        // Both first and second derivatives meet the constant inner/outer maps.
        void Factors(double radius, out double tangential, out double radial)
        {
            if (Magnification == 1 || radius >= OuterRadius) { tangential = radial = 1; return; }
            if (radius <= InnerRadius) { tangential = radial = 1 / Magnification; return; }
            double t = (radius - InnerRadius) / (OuterRadius - InnerRadius);
            double smooth = t * t * t * (t * (6 * t - 15) + 10);
            double derivative = 30 * t * t * (t - 1) * (t - 1) / (OuterRadius - InnerRadius);
            double k = 1 - 1 / Magnification;
            tangential = 1 - k * (1 - smooth);
            radial = tangential + radius * k * derivative;
        }

        /// <summary>Physical world coordinates to the unchanged canonical Earth sampling coordinates.</summary>
        public void Project(double x, double z, out double canonicalX, out double canonicalZ)
        {
            canonicalX = x; canonicalZ = z;
            if (!Contains(x, z)) return;
            double dx = x - CenterX, dz = z - CenterZ, r = Math.Sqrt(dx * dx + dz * dz);
            Factors(r, out double alpha, out _);
            canonicalX = CenterX + dx * alpha; canonicalZ = CenterZ + dz * alpha;
        }

        /// <summary>Inverse of Project; useful for displaying canonical feature positions in physical space.</summary>
        public void Unproject(double canonicalX, double canonicalZ, out double x, out double z)
        {
            x = canonicalX; z = canonicalZ;
            if (!Contains(canonicalX, canonicalZ)) return;
            double dx = canonicalX - CenterX, dz = canonicalZ - CenterZ, sourceRadius = Math.Sqrt(dx * dx + dz * dz);
            if (sourceRadius == 0) return;
            double radius;
            if (sourceRadius <= InnerRadius / Magnification) radius = sourceRadius * Magnification;
            else
            {
                double low = InnerRadius, high = OuterRadius;
                for (int i = 0; i < 64; i++)
                {
                    double middle = (low + high) / 2;
                    Factors(middle, out double alpha, out _);
                    if (middle * alpha < sourceRadius) low = middle; else high = middle;
                }
                radius = (low + high) / 2;
            }
            x = CenterX + dx * radius / sourceRadius; z = CenterZ + dz * radius / sourceRadius;
        }

        /// <summary>Derivative of physical-to-canonical mapping; all eigenvalues are strictly positive.</summary>
        public void Jacobian(double x, double z, out double xx, out double xz, out double zz)
        {
            xx = zz = 1; xz = 0;
            if (!Contains(x, z)) return;
            double dx = x - CenterX, dz = z - CenterZ, rr = dx * dx + dz * dz;
            Factors(Math.Sqrt(rr), out double tangent, out double radial);
            if (rr == 0) { xx = zz = tangent; return; }
            double delta = (radial - tangent) / rr;
            xx = tangent + delta * dx * dx; xz = delta * dx * dz; zz = tangent + delta * dz * dz;
        }

        /// <summary>Map canonical flow tangents back to physical space, preserving their direction through bends.</summary>
        public void TransformFlow(double x, double z, float sourceX, float sourceZ, out float flowX, out float flowZ)
        {
            flowX = sourceX; flowZ = sourceZ;
            if (!Contains(x, z) || (sourceX == 0 && sourceZ == 0)) return;
            Jacobian(x, z, out double xx, out double xz, out double zz);
            double determinant = xx * zz - xz * xz;
            double vx = (zz * sourceX - xz * sourceZ) / determinant;
            double vz = (xx * sourceZ - xz * sourceX) / determinant;
            double length = Math.Sqrt(vx * vx + vz * vz);
            flowX = (float)(vx / length); flowZ = (float)(vz / length);
        }
    }
}
