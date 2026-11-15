using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    // TerrainModel, part: dry land heights from GLO-90 (full and included), the surveyed peaks restored and the GLO-90 summit lift.
    public sealed partial class TerrainModel
    {
        // Summit rules 3: a surveyed peak's own column before any summit lift (the ground, noise and 30" excess), per peak
        // column; pure, so every thread computes the same value.
        readonly System.Collections.Concurrent.ConcurrentDictionary<(double, double), double> peakBases = new();
        double PeakBase(double x, double z) => peakBases.GetOrAdd((x, z), static (k, m) =>
        {
            var g = m.Geometry.ToGeo(k.Item1, k.Item2);
            var s = m.Data.SampleTerrain(g.Lat, g.Lon, m.Coasts != null);
            double detail = OpenOcean(s) ? 0 : m.Detail(k.Item1, k.Item2, s);
            return s.Elev + detail + m.SummitExcess(g.Lat, g.Lon, s);
        }, this);

        /// <summary>
        /// Decision 0051: dry land's heights from Copernicus GLO-90 tiles (null: the built-in grid's). Only the
        /// height of columns that are dry land changes; which columns are land, sea or lake, the sea floor, lake beds and the
        /// rivers stay the built-in data's.
        /// </summary>
        public FineElevation Fine;
        /// <summary>
        /// 0053: GLO-90 averaged to a coarser grid from Earth's height data packs (null: not used). Dry land takes
        /// it where full GLO-90 (Fine) has no tile here or isn't used, with a detail noise sized by the grid's own spread
        /// inside each cell and the surveyed peaks restored as on the built-in heights.
        /// </summary>
        public CoarseElevation Coarse;
        /// <summary>The world's included-GLO-90 rules (EarthParams.CoarseElevation, CoarseElevation.Version).</summary>
        public int CoarseRules { get => rules.Coarse; set => rules = rules with { Coarse = value }; }
        /// <summary>Undulation (m) left on GLO-90's land, so slopes aren't planes between its 90 m points (rules 1 and 2;
        /// rules 3 joins the points by a smooth curve instead and adds none).</summary>
        public const double FineUndulation = 1.0;

        /// <summary>The world's GLO-90 rules (EarthParams.FineElevation, FineElevation.Version); 2 adds the summit lift.</summary>
        public int FineRules { get => rules.Fine; set => rules = rules with { Fine = value }; }

        /// <summary>GLO-90's land height at a column (rules 1 and 2: bilinear with a metre of undulation; rules 3: a smooth
        /// curve, nothing added), NaN where the built-in heights apply.</summary>
        double FineLand(double x, double z, double lat, double lon, in EarthData.TerrainSample s)
        {
            if (OpenOcean(s)) return double.NaN;
            if (Fine != null && FineGround(x, z, lat, lon, out double f))
                return f + FineSummitLift(x, z, lat, lon);
            return CoarseLand(x, z, lat, lon, s);
        }

        /// <summary>GLO-90's ground at a column before any summit lift, by the world's GLO-90 rules.</summary>
        bool FineGround(double x, double z, double lat, double lon, out double f)
        {
            if (Rules.FineFootprintMean && FineFootprint > 1 && FineMean(x, z, FineFootprint, out f)) return true;
            if (Rules.FineSmooth) return Fine.TrySmoothHeight(lat, lon, Rules.FineBounded, out f);
            if (!Fine.TryHeight(lat, lon, out f)) return false;
            f += FineUndulation * SeamNoise(n2, x, z, 56, 3) * DetailScale;
            return true;
        }

        /// <summary>
        /// GLO-90 rules 4: smooth points averaged over a block's footprint, n × n per block (1, 2 from 75 m per block, 3 from
        /// 150 m; GLO-90's points are about 90 m apart). One point per block at 1:200 and 1:400 is one of 5 to 20 GLO-90 points
        /// under the block, so neighbouring blocks differed by chance: in eight test areas (scalecheck, 5 November 2026) 7–22 m
        /// from the block's mean at 1:400 and flat land 1.4–1.8 times as rough as its means; 3 × 3 points: 2–3 m and 1.01–1.06.
        /// </summary>
        public int FineFootprint => Geometry == null ? 1 : MetresPerBlock >= 150 ? 3 : MetresPerBlock >= 75 ? 2 : 1;

        /// <summary>The mean of n × n smooth GLO-90 points spread evenly over the block centred on (x, z); false when any of
        /// them has no GLO-90 height (the caller then takes the point itself).</summary>
        bool FineMean(double x, double z, int n, out double mean)
        {
            double sum = 0;
            mean = double.NaN;
            for (int a = 0; a < n; a++)
            {
                for (int b = 0; b < n; b++)
                {
                    double px = x + (a + 0.5) / n - 0.5, pz = z + (b + 0.5) / n - 0.5;
                    var g = Geometry.ToGeo(RealSeam ? Geometry.WrapX(px) : px, pz);
                    if (g.InGutter || !Fine.TrySmoothHeight(g.Lat, g.Lon, Rules.FineBounded, out double h)) return false;
                    sum += h;
                }
            }
            mean = sum / (n * n);
            return true;
        }

        /// <summary>0053: the included GLO-90 grid's land height with its sub-cell noise and the surveyed peaks, NaN where it
        /// has no land.</summary>
        double CoarseLand(double x, double z, double lat, double lon, in EarthData.TerrainSample s)
        {
            if (Coarse == null || !Rules.CoarseHeights) return double.NaN;
            double h = CoarseGround(x, z, lat, lon, s);
            if (double.IsNaN(h)) return h;
            if (Summits && Rules.SummitRestore && SummitMinElevation >= 0 && h > 0)
                h = KnownSummits.Restore(Geometry, x, z, h, SummitMinElevation, Peaks, SummitRules, CoarseBaseOf, peakSupport);
            return h;
        }

        double CoarseGround(double x, double z, double lat, double lon, in EarthData.TerrainSample s)
        {
            if (!Coarse.TryHeight(lat, lon, out double c)) return double.NaN;
            // the noise's spread: GLO-90's own spread inside the cell where the packs have it, else a share of the 2' relief
            double sigma = Coarse.TryRoughness(lat, lon, out double rough) ? rough : 0.05 * Math.Min(s.ReliefSmooth, 2500);
            double ridges = Geo.SmoothStep((sigma - 40) / 200);
            // (detail rules 2, a gullies prototype no build gave a world, was removed for 2.0.0: docs/research/detail-gullies-2026-11-05.md)
            return c + SubCellNoise(x, z, Coarse.CellMetres, sigma, ridges) * DetailScale;
        }

        // the ground at a peak's column before its restore, per column (pure: every thread computes the same value)
        readonly System.Collections.Concurrent.ConcurrentDictionary<(double, double), double> coarseBases = new();
        double CoarseBase(double x, double z) => coarseBases.GetOrAdd((x, z), static (k, m) =>
        {
            var g = m.Geometry.ToGeo(k.Item1, k.Item2);
            var s = m.Data.SampleTerrain(g.Lat, g.Lon, m.Coasts != null);
            double h = m.CoarseGround(k.Item1, k.Item2, g.Lat, g.Lon, s);
            return double.IsNaN(h) ? s.Elev : h;
        }, this);

        // PeakBase and CoarseBase as delegates for KnownSummits.Restore, made once rather than per column
        Func<double, double, double> peakBaseOf, coarseBaseOf;
        Func<double, double, double> PeakBaseOf => peakBaseOf ??= PeakBase;
        Func<double, double, double> CoarseBaseOf => coarseBaseOf ??= CoarseBase;
        // per peak: its playable top column and its support radius (KnownSummits.Restore; pure in the peak, the catalogue and
        // the summit cutoff)
        readonly KnownSummits.SupportCache peakSupport = new();

        /// <summary>
        /// A listed peak on GLO-90 (0051 rules 2): the data's own top within 1 km of the catalogue's position, the column
        /// there, and how far that column is below the survey. Lift 0 (with the reason) where nothing is lifted.
        /// </summary>
        public sealed class FineSummit
        {
            public string Name, Skipped;
            public double Surveyed, Top, Distance, X, Z, Lift;
        }

        // per peak; computed from the tiles alone, so every thread gets the same answer. A peak whose tiles aren't
        // available isn't kept (a later thread that may fetch them computes it).
        readonly System.Collections.Concurrent.ConcurrentDictionary<KnownSummits.Peak, FineSummit> fineSummits = new();

        /// <summary>The listed peaks looked at so far on GLO-90 (for the self-test).</summary>
        public ICollection<FineSummit> FineSummits => fineSummits.Values;

        FineSummit FineSummitOf(KnownSummits.Peak p)
        {
            if (fineSummits.TryGetValue(p, out var known)) return known;
            if (!Fine.TryHighest(p.Lat, p.Lon, FineElevation.SummitSearch, out double topLat, out double topLon, out double top, out double dist))
                return null;
            var s = new FineSummit { Name = p.Name, Surveyed = p.Height, Top = top, Distance = dist };
            Geometry.FromGeo(topLat, topLon, out double px, out double pz);
            // the top on a playable column, as the summit correction does, so that column reaches the survey exactly
            s.X = Math.Round(px); s.Z = Math.Round(pz);
            var g = Geometry.ToGeo(s.X, s.Z);
            if (!FineGround(s.X, s.Z, g.Lat, g.Lon, out double f)) return null;
            double lift = p.Height - f;
            if (dist > FineElevation.SummitSearch - FineElevation.SummitEdge) s.Skipped = "top at the search's edge";
            else if (lift <= 0) s.Skipped = "GLO-90 reaches the survey";
            else if (lift > FineElevation.SummitMaxLift) s.Skipped = "gap over " + FineElevation.SummitMaxLift + " m";
            else s.Lift = lift;
            return fineSummits.GetOrAdd(p, s);
        }

        /// <summary>
        /// Rules 2: listed peaks at or above the Earth tab's summit cutoff come to their surveyed height on GLO-90. Only
        /// the data's own top is raised, by what GLO-90 lacks there, fading to nothing 300 m away; nothing is lowered.
        /// Where peaks overlap the larger lift holds (two catalogue entries for one top don't add up).
        /// </summary>
        double FineSummitLift(double x, double z, double lat, double lon)
        {
            if (!Rules.FineSummitLift || !Rules.SummitRestore || SummitMinElevation < 0) return 0;
            double reach = (FineElevation.SummitSearch + FineElevation.SummitLiftRadius) / Geo.MetresPerDegree;
            double cosLat = Math.Max(0.01, Math.Cos(lat * Math.PI / 180));
            double best = 0;
            if (Peaks != null) foreach (var p in Peaks.Near(lat, lon)) best = Math.Max(best, FineSummitLift(p, x, z, lat, lon, reach, cosLat));
            else foreach (var p in KnownSummits.Peaks) best = Math.Max(best, FineSummitLift(p, x, z, lat, lon, reach, cosLat));
            return best;
        }

        double FineSummitLift(KnownSummits.Peak p, double x, double z, double lat, double lon, double reach, double cosLat)
        {
            if (p.Height < SummitMinElevation || Math.Abs(lat - p.Lat) > reach || Math.Abs(Geo.Wrap180(lon - p.Lon)) * cosLat > reach) return 0;
            var s = FineSummitOf(p);
            if (s == null || s.Lift <= 0) return 0;
            double dx = (x - s.X) * Geo.MetresPerDegree * cosLat / Geometry.BlocksPerDegreeX, dz = (z - s.Z) * Geo.MetresPerDegree / Geometry.BlocksPerDegreeZ;
            double r = Math.Sqrt(dx * dx + dz * dz) / FineElevation.SummitLiftRadius;
            return r >= 1 ? 0 : s.Lift * (1 - r) * (1 - r);
        }
    }
}
