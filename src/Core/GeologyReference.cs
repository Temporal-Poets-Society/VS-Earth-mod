using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Rules v2 stratigraphic reference surface: a heavily low-passed terrain elevation (block height of the bedrock
    /// surface averaged over ~0.6 source cells) with a gentle regional undulation. Bed boundaries hang at fixed depths
    /// below it, so valleys, canyons and cliffs cut into lower beds. Values live on a 64-block lattice (deterministic
    /// per node, bilinear in between), so chunk borders match. Immutable and thread-safe.
    /// </summary>
    public sealed class GeologyReference
    {
        public const int Lattice = 64;
        readonly TerrainModel model;
        readonly long seed;
        readonly double radius, thickness, undulation, variation, wavelength;
        static readonly (double X, double Z, double W)[] Stencil = BuildStencil();

        public GeologyReference(TerrainModel model, long seed, double cellBlocks, double radiusCells, double thicknessPerSeaLevel, double undulation, double variation)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.seed = seed;
            radius = Math.Max(Lattice, radiusCells * cellBlocks);
            thickness = Math.Max(16, thicknessPerSeaLevel * model.SeaLevel);
            this.undulation = undulation;
            this.variation = variation;
            wavelength = Math.Max(256, 1.6 * cellBlocks);
        }
        public double ThicknessScale => thickness;

        static (double, double, double)[] BuildStencil()
        {
            var s = new (double, double, double)[1 + 6 + 12];
            s[0] = (0, 0, 3);
            for (int i = 0; i < 6; i++) { double a = i * Math.PI / 3; s[1 + i] = (.5 * Math.Cos(a), .5 * Math.Sin(a), 2); }
            for (int i = 0; i < 12; i++) { double a = (i + .5) * Math.PI / 6; s[7 + i] = (Math.Cos(a), Math.Sin(a), 1); }
            return s;
        }

        /// <summary>Reference height (block y) and stratigraphic thickness scale (blocks) at a lattice node.</summary>
        public void Node(long ix, long iz, out double datum, out double thick)
        {
            double x = ix * (double)Lattice, z = iz * (double)Lattice;
            double sum = 0, wsum = 0;
            foreach (var p in Stencil)
            {
                var g = model.Geometry.ToGeo(x + p.X * radius, z + p.Z * radius);
                if (g.BeyondPole) continue;
                var t = model.Data.SampleTerrain(g.Lat, g.Lon);
                // bedrock surface: below ice sheets; deep sea floor clamped so coasts are not dragged far down
                double h = t.Elev - (t.Ice > 15 ? t.Ice : 0);
                sum += p.W * model.Vertical.Y(Math.Max(h, -150));
                wsum += p.W;
            }
            datum = wsum > 0 ? sum / wsum : model.SeaLevel;
            datum += undulation * thickness * (GeologyTransitions.Noise(x / wavelength, z / wavelength, seed, 3371) * 2 - 1);
            thick = thickness * (1 + variation * (GeologyTransitions.Noise(x / (wavelength * 1.7), z / (wavelength * 1.7), seed, 3907) * 2 - 1));
        }

        [ThreadStatic] static long cacheKeyX, cacheKeyZ;
        [ThreadStatic] static GeologyReference cacheOwner;
        [ThreadStatic] static double[] cacheNodes;

        /// <summary>Reference height and thickness at a column (bilinear between the 4 surrounding lattice nodes; per-thread node cache).</summary>
        public void At(double x, double z, out double datum, out double thick)
        {
            long ix = (long)Math.Floor(x / Lattice), iz = (long)Math.Floor(z / Lattice);
            var n = cacheNodes;
            if (n == null || cacheOwner != this || cacheKeyX != ix || cacheKeyZ != iz)
            {
                n ??= cacheNodes = new double[8];
                cacheOwner = null; // invalid until all four nodes are written
                Node(ix, iz, out n[0], out n[1]);
                Node(ix + 1, iz, out n[2], out n[3]);
                Node(ix, iz + 1, out n[4], out n[5]);
                Node(ix + 1, iz + 1, out n[6], out n[7]);
                cacheOwner = this; cacheKeyX = ix; cacheKeyZ = iz;
            }
            double tx = x / Lattice - ix, tz = z / Lattice - iz;
            datum = (n[0] * (1 - tx) + n[2] * tx) * (1 - tz) + (n[4] * (1 - tx) + n[6] * tx) * tz;
            thick = (n[1] * (1 - tx) + n[3] * tx) * (1 - tz) + (n[5] * (1 - tx) + n[7] * tx) * tz;
        }
    }
}
