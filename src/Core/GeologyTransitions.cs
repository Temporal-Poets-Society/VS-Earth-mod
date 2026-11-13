using System;

namespace EarthMap.Core
{
    /// <summary>
    /// The position hashes that saved worlds depend on, bit for bit: each is part of generation in worlds already made, so
    /// none of them may ever change. New code takes one of these instead of writing another. (In this file because every
    /// project that compiles a user of them compiles this file too.)
    /// </summary>
    public static class StableHash
    {
        /// <summary>[0,1) from a seed, a block and a salt (53 bits): the 0.12.2 ore hash, a splitmix64 finaliser. Ore placement
        /// (RegionalOreCatalog.Random) and geology (GeologyTransitions.Hash, rules-v1 saves) both read it.</summary>
        public static double Unit(long seed, int x, int y, int z, int salt)
        {
            unchecked
            {
                ulong v = (ulong)seed ^ (uint)x * 0x9E3779B185EBCA87UL ^ (uint)y * 0xC2B2AE3D27D4EB4FUL ^ (uint)z * 0x165667B19E3779F9UL ^ (uint)salt;
                v ^= v >> 30; v *= 0xbf58476d1ce4e5b9UL; v ^= v >> 27; v *= 0x94d049bb133111ebUL; v ^= v >> 31;
                return (v >> 11) * (1.0 / 9007199254740992.0);
            }
        }
        /// <summary>[0,1) from a column alone (53 bits, no seed): tall grass kept by the ground cover rules (GroundCover.Keeps).</summary>
        public static double Column(int x, int z)
        {
            unchecked
            {
                ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)z * 0xC2B2AE3D27D4EB4FUL ^ 0x5A17D9A2E3C1B7F1UL;
                h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 27; h *= 0x94D049BB133111EBUL; h ^= h >> 31;
                return (h >> 11) * (1.0 / (1UL << 53));
            }
        }
        /// <summary>[0,1) from two ints (24 bits, no seed): volcano ash edges and crater phases (geothermal rules 2, VolcanoPlan).</summary>
        public static double Unit24(int x, int z)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)z * 19349663u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216.0;
            }
        }
    }

    /// <summary>Coherent world-coordinate contact zones. Entire stratigraphic columns share a profile.</summary>
    public static class GeologyTransitions
    {
        /// <summary>Position hash in [0,1): <see cref="StableHash.Unit"/>, the 0.12.2 ore hash that rules-v1 saves depend on.</summary>
        public static double Hash(long seed, int x, int y, int z, int salt) => StableHash.Unit(seed, x, y, z, salt);
        static double Smooth(double t) => t * t * (3 - 2 * t);
        /// <summary>Smooth value noise in [0,1] on a unit lattice.</summary>
        public static double Noise(double x, double z, long seed, int salt)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            double tx = Smooth(x - ix), tz = Smooth(z - iz);
            double a = Hash(seed, ix, 0, iz, salt), b = Hash(seed, ix + 1, 0, iz, salt);
            double c = Hash(seed, ix, 0, iz + 1, salt), d = Hash(seed, ix + 1, 0, iz + 1, salt);
            return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
        }
        public static bool IsValidWidth(double width) => double.IsFinite(width) && width >= 0 && width <= 512;

        /// <summary>Rules v1 contact warp (frozen: v1 saves keep these exact positions).</summary>
        public static (double X, double Z) Warp(double x, double z, long seed, double width)
        {
            if (!IsValidWidth(width)) throw new ArgumentOutOfRangeException(nameof(width));
            if (width == 0) return (x, z);
            double scale = Math.Max(32, width * .6);
            double dx = (.65 * Noise(x / scale, z / scale, seed, 781) + .35 * Noise(x / (scale * 2.7), z / (scale * 2.7), seed, 913)) * 2 - 1;
            double dz = (.65 * Noise(x / scale, z / scale, seed, 1193) + .35 * Noise(x / (scale * 2.7), z / (scale * 2.7), seed, 1583)) * 2 - 1;
            return (x + dx * width, z + dz * width);
        }

        /// <summary>
        /// Rules v2 warp: the v1 contact wiggle plus a low-frequency octave at the scale of the 0.5-degree source cells,
        /// which bends whole contacts so the source grid does not show. Continuous, deterministic, world-coordinate based.
        /// </summary>
        public static (double X, double Z) WarpV2(double x, double z, long seed, double width, double cellBlocks, double lowWavelengthCells, double lowAmplitudeCells)
        {
            var w = width > 0 ? Warp(x, z, seed, width) : (x, z);
            if (!(cellBlocks > 0) || !(lowAmplitudeCells > 0)) return w;
            double scale = Math.Max(64, lowWavelengthCells * cellBlocks), amplitude = lowAmplitudeCells * cellBlocks;
            double dx = Noise(x / scale, z / scale, seed, 2141) * 2 - 1;
            double dz = Noise(x / scale, z / scale, seed, 2683) * 2 - 1;
            // a second, offset lattice removes the residual lattice-aligned anisotropy of value noise
            dx = .7 * dx + .3 * (Noise(x / (scale * .57) + .37, z / (scale * .57) + .71, seed, 2909) * 2 - 1);
            dz = .7 * dz + .3 * (Noise(x / (scale * .57) + .53, z / (scale * .57) + .19, seed, 3191) * 2 - 1);
            return (w.Item1 + dx * amplitude, w.Item2 + dz * amplitude);
        }

        /// <summary>v2: discontinuous gravel-lens mask, ~[0,1], two octaves of ~40 and ~14 block patches.</summary>
        public static double GravelMask(double x, double z, long seed)
            => .7 * Noise(x / 40, z / 40, seed, 4421) + .3 * Noise(x / 14, z / 14, seed, 4637);

        static readonly Lazy<double[]> gravelQuantiles = new Lazy<double[]>(() =>
        {
            // The mask's distribution does not depend on the seed: tabulate it once from a fixed sample.
            var v = new double[16384];
            for (int i = 0; i < v.Length; i++)
                v[i] = GravelMask(Hash(1, i, 0, 0, 17) * 1e6, Hash(1, i, 0, 0, 29) * 1e6, 977);
            Array.Sort(v);
            return v;
        });
        /// <summary>Mask threshold above which the given share of ground lies.</summary>
        public static double GravelThreshold(double coverage)
        {
            if (!(coverage > 0)) return double.MaxValue;
            if (coverage >= 1) return double.MinValue;
            var q = gravelQuantiles.Value;
            return q[(int)Math.Round((1 - coverage) * (q.Length - 1))];
        }
    }
}
