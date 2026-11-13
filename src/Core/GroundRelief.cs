using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Where a column sits in the land around it, for the ground columns: the mean elevation of the ground within a radius
    /// (real metres) and the slope. A column above that mean is a crest or knoll, one below it a hollow or valley floor;
    /// <see cref="GroundColumns"/> thins the cover on crests and steep slopes and thickens it in hollows (rules: "relief").
    ///
    /// Elevations are read on a lattice of about a third of the radius, aligned to the world, and the disc mean and the slope
    /// are interpolated between lattice points. Chunks side by side east-west share the lattice; north-south neighbours may
    /// not: the caller's metres per unit across comes from each chunk's own latitude, so where round(radius / 3) or a disc's
    /// rim point flips between two such chunks they read different lattices (code review 2026-11-11, CR-T9; a fix changes the
    /// ground and needs a ground rules version). Coordinates are
    /// the caller's (blocks in the game, any grid in tests) with metres per unit along each axis.
    /// </summary>
    public static class GroundRelief
    {
        /// <summary>
        /// Mean elevation within <paramref name="radiusMetres"/> and slope (rise over run) for an n x n block of positions
        /// starting at (<paramref name="x0"/>, <paramref name="z0"/>), one unit apart; row-major (z outer) into
        /// <paramref name="mean"/> and <paramref name="slope"/>.
        /// </summary>
        /// <param name="weight">Optional: the cover factor a place gets for its relief (height above the mean); then
        /// <paramref name="norm"/> receives that factor's mean over the disc, so dividing by it keeps the neighbourhood's
        /// total cover (sediment moves from crests to hollows, none is made).</param>
        public static void Field(double x0, double z0, int n, Func<double, double, double> elevation, double metresX, double metresZ,
                                 double radiusMetres, double[] mean, double[] slope, Func<double, double> weight = null, double[] norm = null)
        {
            double rx = Math.Max(1, radiusMetres / metresX), rz = Math.Max(1, radiusMetres / metresZ);
            double sx = Math.Max(1, Math.Round(rx / 3)), sz = Math.Max(1, Math.Round(rz / 3));
            int kx = (int)Math.Ceiling(rx / sx), kz = (int)Math.Ceiling(rz / sz);
            // lattice nodes covering the block, one more on each side for interpolation, plus the disc around each
            int ix0 = (int)Math.Floor(x0 / sx) - 1, ix1 = (int)Math.Floor((x0 + n - 1) / sx) + 2;
            int iz0 = (int)Math.Floor(z0 / sz) - 1, iz1 = (int)Math.Floor((z0 + n - 1) / sz) + 2;
            int ex0 = ix0 - kx, ez0 = iz0 - kz, ew = ix1 - ix0 + 1 + 2 * kx, eh = iz1 - iz0 + 1 + 2 * kz;
            var e = new double[ew * eh];
            for (int j = 0; j < eh; j++)
                for (int i = 0; i < ew; i++)
                    e[j * ew + i] = elevation((ex0 + i) * sx, (ez0 + j) * sz);
            int mw = ix1 - ix0 + 1, mh = iz1 - iz0 + 1;
            var m = new double[mw * mh];
            var w = weight != null ? new double[mw * mh] : null;
            for (int j = 0; j < mh; j++)
                for (int i = 0; i < mw; i++)
                {
                    double sum = 0; int count = 0;
                    for (int dj = -kz; dj <= kz; dj++)
                        for (int di = -kx; di <= kx; di++)
                        {
                            double u = di * sx / rx, v = dj * sz / rz;
                            if (u * u + v * v > 1.0001) continue;
                            double h = e[(j + kz + dj) * ew + (i + kx + di)];
                            if (double.IsNaN(h)) continue;
                            sum += h; count++;
                        }
                    double mm = m[j * mw + i] = count > 0 ? sum / count : double.NaN;
                    if (w == null) continue;
                    double ws = 0; int wc = 0;
                    for (int dj = -kz; dj <= kz; dj++)
                        for (int di = -kx; di <= kx; di++)
                        {
                            double u = di * sx / rx, v = dj * sz / rz;
                            if (u * u + v * v > 1.0001) continue;
                            double h = e[(j + kz + dj) * ew + (i + kx + di)];
                            if (double.IsNaN(h) || double.IsNaN(mm)) continue;
                            ws += weight(h - mm); wc++;
                        }
                    w[j * mw + i] = wc > 0 ? ws / wc : 1;
                }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    double fx = (x0 + x) / sx - ix0, fz = (z0 + z) / sz - iz0;
                    int i = (int)Math.Floor(fx), j = (int)Math.Floor(fz);
                    double tx = fx - i, tz = fz - j;
                    mean[z * n + x] = Lerp2(m, mw, i, j, tx, tz);
                    if (w != null) norm[z * n + x] = Lerp2(w, mw, i, j, tx, tz);
                    // slope of the bilinear surface through the lattice elevations
                    int ei = i + kx, ej = j + kz;
                    double e00 = e[ej * ew + ei], e10 = e[ej * ew + ei + 1], e01 = e[(ej + 1) * ew + ei], e11 = e[(ej + 1) * ew + ei + 1];
                    double dx = ((e10 - e00) * (1 - tz) + (e11 - e01) * tz) / (sx * metresX);
                    double dz = ((e01 - e00) * (1 - tx) + (e11 - e10) * tx) / (sz * metresZ);
                    slope[z * n + x] = Math.Sqrt(dx * dx + dz * dz);
                }
        }

        static double Lerp2(double[] a, int w, int i, int j, double tx, double tz)
        {
            double v00 = a[j * w + i], v10 = a[j * w + i + 1], v01 = a[(j + 1) * w + i], v11 = a[(j + 1) * w + i + 1];
            return (v00 * (1 - tx) + v10 * tx) * (1 - tz) + (v01 * (1 - tx) + v11 * tx) * tz;
        }
    }
}
