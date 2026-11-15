using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EarthMap;
using EarthMap.Core;

namespace Probe
{
    static partial class Program
    {
        static readonly FieldInfo[] ColumnFields = typeof(TerrainModel.Column).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>The first field two columns differ in (bit for bit; NaN equals NaN), or null.</summary>
        static string Differ(TerrainModel.Column a, TerrainModel.Column b)
        {
            object oa = a, ob = b;
            foreach (var f in ColumnFields)
            {
                object va = f.GetValue(oa), vb = f.GetValue(ob);
                bool same = va is double da && vb is double db ? BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db)
                    : va is float fa && vb is float fb ? BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb) : Equals(va, vb);
                if (!same) return $"{f.Name}: {va} vs {vb}";
            }
            return null;
        }

        static string Show(in TerrainModel.Column c) =>
            $"top {c.Top} water {c.Water}@{c.WaterTop} {Kind(c)} elev {c.Elev:0.00} river {c.River} near {c.NearRiver} beach {c.Beach} shore {c.Shore} outl {c.RiverOutline} lat {c.Lat:0.00000} lon {c.Lon:0.00000}";

        /// <summary>The same columns asked in another order and from several threads, on this model and on a fresh one whose
        /// lazy per-model caches are first filled by the parallel shuffled pass.</summary>
        static void Determinism(World w)
        {
            const int N = 160;
            var areas = new List<(string name, double x0, double z0)>();
            foreach (var (name, lat, lon) in Places)
            {
                var (px, pz) = w.At(lat, lon);
                areas.Add((name, px - N / 2, pz - N / 2));
            }
            // serial, row by row, on this thread
            var serial = new TerrainModel.Column[areas.Count][];
            for (int a = 0; a < areas.Count; a++)
            {
                serial[a] = new TerrainModel.Column[N * N];
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++) serial[a][j * N + i] = w.Model.GetColumn(areas[a].x0 + i, areas[a].z0 + j);
            }
            void Compare(string what, TerrainModel model, bool parallel, int seed)
            {
                int diffs = 0, total = 0;
                string first = null;
                var gate = new object();
                for (int a = 0; a < areas.Count; a++)
                {
                    var order = Enumerable.Range(0, N * N).ToArray();
                    var rng = new Random(seed + a);
                    for (int k = order.Length - 1; k > 0; k--) { int r = rng.Next(k + 1); (order[k], order[r]) = (order[r], order[k]); }
                    var got = new TerrainModel.Column[N * N];
                    int aa = a;
                    if (parallel)
                        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
                        {
                            FineElevation.FetchAllowed = false;
                            for (int k = t; k < order.Length; k += 16) { int id = order[k]; got[id] = model.GetColumn(areas[aa].x0 + id % N, areas[aa].z0 + id / N); }
                        });
                    else
                        foreach (int id in order) got[id] = model.GetColumn(areas[aa].x0 + id % N, areas[aa].z0 + id / N);
                    for (int id = 0; id < N * N; id++)
                    {
                        total++;
                        string d = Differ(serial[a][id], got[id]);
                        if (d == null) continue;
                        diffs++;
                        first ??= $"{areas[a].name} ({areas[a].x0 + id % N}, {areas[a].z0 + id / N}): {d}\n      serial: {Show(serial[a][id])}\n      other:  {Show(got[id])}";
                    }
                }
                Console.WriteLine($"  {what}: {diffs} of {total} columns differ{(first != null ? "; first: " + first : "")}");
            }
            Compare("same model, shuffled, one thread", w.Model, false, 1);
            Compare("same model, shuffled, 16 threads", w.Model, true, 2);
            // a fresh model: its per-model lazy caches (pond profiles, reach speeds, mask reaches, summits) are filled by other columns first
            var w2 = World.Build(w.Root, (int)Math.Round(40075017 / w.Geo.RingLength), Path.GetFullPath(Path.Combine(w.FineFolder ?? ".", "..")), Places.Select(p => (p.Lat, p.Lon)), w.Fine != null, false);
            Compare("fresh model, shuffled, 16 threads first", w2.Model, true, 3);
            Compare("fresh model again, reverse serial", w2.Model, false, 4);
        }

        /// <summary>The real seam: both laps the same; continuity across the jump line against the same step one chunk inside.</summary>
        static void Seam(World w)
        {
            var m = w.Model; var geo = w.Geo;
            double rs = geo.RingStart, ring = geo.RingLength;
            Console.WriteLine($"  RealSeam {m.RealSeam}, ring {ring} from {rs}");
            int laps = 0, lapDiff = 0;
            string firstLap = null;
            var bands = new (string name, double lat0, double lat1)[] { ("St Lawrence Island", 63.0, 63.8), ("Antarctica", -89.5, -78), ("open Pacific", -20, 20), ("Arctic", 70, 89.9) };
            foreach (var (name, lat0, lat1) in bands)
            {
                double zA = geo.EquatorZ - lat1 * geo.BlocksPerDegreeZ, zB = geo.EquatorZ - lat0 * geo.BlocksPerDegreeZ;
                int n = 0, land = 0, kindFlip = 0, kindFlipInside = 0;
                double sumSeam = 0, sumInside = 0; int maxSeam = 0, maxInside = 0;
                double step = Math.Max(1, (zB - zA) / 4000);
                for (double z = Math.Floor(zA); z <= zB; z += step)
                {
                    double zz = Math.Floor(z);
                    var west = m.GetColumn(rs + ring - 1, zz);   // last column of the ring (east end)
                    var east = m.GetColumn(rs, zz);              // first column
                    var in1 = m.GetColumn(rs + 40, zz); var in2 = m.GetColumn(rs + 41, zz);
                    n++;
                    if (west.Water == 0 || east.Water == 0) land++;
                    int d = Math.Abs(west.Top - east.Top), di = Math.Abs(in1.Top - in2.Top);
                    sumSeam += d; sumInside += di; maxSeam = Math.Max(maxSeam, d); maxInside = Math.Max(maxInside, di);
                    if ((west.Water == 0) != (east.Water == 0)) kindFlip++;
                    if ((in1.Water == 0) != (in2.Water == 0)) kindFlipInside++;
                    // both laps
                    foreach (int k in new[] { -2, -1, 0, 1, 2 })
                    {
                        var a = m.GetColumn(rs + k, zz); var b = m.GetColumn(rs + ring + k, zz); var c = m.GetColumn(rs - ring + k, zz);
                        laps++;
                        string df = Differ(a, b) ?? Differ(a, c);
                        if (df != null) { lapDiff++; firstLap ??= $"x {rs + k} z {zz}: {df}"; }
                    }
                }
                Console.WriteLine($"  {name,-20} {n} rows, {land} with land at the line; |dTop| across the jump line mean {sumSeam / n:0.000} max {maxSeam} (40 blocks inside: mean {sumInside / n:0.000} max {maxInside}); land/water flips {kindFlip} (inside {kindFlipInside})");
            }
            Console.WriteLine($"  laps: {lapDiff} of {laps} columns differ one ring apart{(firstLap != null ? "; first " + firstLap : "")}");
        }

        /// <summary>Columns at and beyond the poles, on the antimeridian, at odd coordinates: exceptions and broken invariants.</summary>
        static void Poles(World w)
        {
            var m = w.Model; var geo = w.Geo;
            int H = m.Vertical.MapHeight;
            int bad = 0, n = 0, threw = 0;
            string first = null, firstEx = null;
            void Check(double x, double z, string what)
            {
                n++;
                try
                {
                    var c = m.GetColumn(x, z);
                    string why = null;
                    if (c.Top < 1 || c.Top >= H) why = "top " + c.Top;
                    else if (c.Water != 0 && (c.WaterTop < 1 || c.WaterTop >= H)) why = "water top " + c.WaterTop;
                    else if (c.Water != 0 && c.WaterTop < c.Top && !(c.River && c.WaterTop == c.Top - 1)) why = $"water top {c.WaterTop} under top {c.Top}";
                    else if (c.Water == 0 && c.WaterTop != -1) why = "dry with water top " + c.WaterTop;
                    else if (!double.IsFinite(c.Elev)) why = "elev " + c.Elev;
                    else if (!double.IsFinite(c.Lat) || !double.IsFinite(c.Lon) || Math.Abs(c.Lat) > 90 || c.Lon < -180 || c.Lon >= 180) why = $"lat {c.Lat} lon {c.Lon}";
                    else if (!float.IsFinite(c.FlowX) || !float.IsFinite(c.FlowZ)) why = "flow";
                    else if (c.IceBottom != int.MaxValue && (c.IceBottom < 1 || c.IceBottom > c.Top)) why = $"ice bottom {c.IceBottom} top {c.Top}";
                    if (why != null) { bad++; first ??= $"{what} ({x}, {z}): {why}; {Show(c)}"; }
                }
                catch (Exception e) { threw++; firstEx ??= $"{what} ({x}, {z}): {e.GetType().Name}: {e.Message}\n{e.StackTrace?.Split('\n').Take(6).Aggregate((p, q) => p + "\n" + q)}"; }
            }
            double zN = geo.EquatorZ - 90 * geo.BlocksPerDegreeZ, zS = geo.EquatorZ + 90 * geo.BlocksPerDegreeZ;
            var rng = new Random(7);
            for (int k = 0; k < 4000; k++)
            {
                double x = geo.RingStart + rng.NextDouble() * geo.RingLength;
                foreach (double dz in new[] { -60.0, -48, -1, -0.5, 0, 0.5, 1, 2, 30 }) { Check(Math.Floor(x), Math.Floor(zN + dz), "north pole"); Check(Math.Floor(x), Math.Floor(zS - dz), "south pole"); }
            }
            // the antimeridian and the meridians of the tile edges, all latitudes
            foreach (double lon in new[] { 180.0, -180, 179.999999, -179.999999, 0, -169.25, 90, -90 })
                for (int k = 0; k < 6000; k++)
                {
                    double lat = -90 + 180.0 * k / 5999;
                    geo.FromGeo(lat, lon, out double x, out double z);
                    Check(Math.Floor(x), Math.Floor(z), "meridian " + lon); Check(Math.Floor(x) - 1, Math.Floor(z), "meridian " + lon); Check(x, z, "meridian exact " + lon);
                }
            // odd coordinates
            foreach (double x in new[] { -1e7, -1.5, -0.0, 0, 0.5, 1e7, 1e9, 3.0e9, -3.0e9, 1e15 })
                foreach (double z in new[] { -1e7, -1.0, 0, 0.5, 1e7, 3.0e9, -3.0e9 }) Check(x, z, "odd");
            Console.WriteLine($"  {n} columns: {threw} threw, {bad} broke an invariant");
            if (firstEx != null) Console.WriteLine("  first exception: " + firstEx);
            if (first != null) Console.WriteLine("  first broken: " + first);
        }

        /// <summary>A sweep of clustered columns over the whole Earth: exceptions and broken invariants.</summary>
        static void Fuzz(World w, int places, int perPlace)
        {
            var m = w.Model; var geo = w.Geo;
            int H = m.Vertical.MapHeight;
            long n = 0; int bad = 0, threw = 0;
            string first = null, firstEx = null;
            var kinds = new Dictionary<string, int>();
            var gate = new object();
            Parallel.For(0, places, new ParallelOptions { MaxDegreeOfParallelism = 12 }, p =>
            {
                var rng = new Random(1000 + p);
                // land-biased: retry until not open ocean (a few tries)
                double lat = 0, lon = 0;
                for (int t = 0; t < 6; t++)
                {
                    lat = Math.Asin(2 * rng.NextDouble() - 1) * 180 / Math.PI; lon = -180 + 360 * rng.NextDouble();
                    var s = w.Data.SampleTerrain(lat, lon);
                    if (!(s.Ocean >= 0.999 && s.Elev < -200)) break;
                }
                geo.FromGeo(lat, lon, out double x0, out double z0);
                x0 = Math.Floor(x0); z0 = Math.Floor(z0);
                double a = rng.NextDouble() * Math.PI * 2, dx = Math.Cos(a), dz = Math.Sin(a);
                for (int k = 0; k < perPlace; k++)
                {
                    double x = Math.Floor(x0 + dx * k), z = Math.Floor(z0 + dz * k);
                    try
                    {
                        var c = m.GetColumn(x, z);
                        string why = null;
                        if (c.Top < 1 || c.Top >= H) why = "top " + c.Top;
                        else if (c.Water != 0 && (c.WaterTop < 1 || c.WaterTop >= H)) why = "water top " + c.WaterTop;
                        else if (c.Water != 0 && c.WaterTop <= c.Top && !(c.River && c.WaterTop == c.Top - 1)) why = $"water top {c.WaterTop} not above top {c.Top}";
                        else if (c.Water == 0 && c.WaterTop != -1) why = "dry with water top " + c.WaterTop;
                        else if (!double.IsFinite(c.Elev)) why = "elev " + c.Elev;
                        else if (!float.IsFinite(c.FlowX) || !float.IsFinite(c.FlowZ)) why = "flow";
                        else if (c.River && c.Water != TerrainModel.WaterLake) why = "river without lake water";
                        else if (c.IceBottom != int.MaxValue && (c.IceBottom < 1 || c.IceBottom > c.Top)) why = $"ice bottom {c.IceBottom} top {c.Top}";
                        lock (gate)
                        {
                            n++;
                            string kd = Kind(c); kinds[kd] = kinds.GetValueOrDefault(kd) + 1;
                            if (why != null) { bad++; if (bad <= 5) Console.WriteLine($"    broken at ({x}, {z}) lat {c.Lat:0.0000} lon {c.Lon:0.0000}: {why}; {Show(c)}"); }
                        }
                    }
                    catch (Exception e)
                    {
                        lock (gate) { n++; threw++; firstEx ??= $"({x}, {z}) near {lat:0.000},{lon:0.000}: {e.GetType().Name}: {e.Message}\n{string.Join("\n", (e.StackTrace ?? "").Split('\n').Take(8))}"; }
                    }
                }
            });
            Console.WriteLine($"  {n} columns at {places} places: {threw} threw, {bad} broke an invariant; kinds {string.Join(" ", kinds.OrderByDescending(k => k.Value).Select(k => k.Key + ":" + k.Value))}");
            if (firstEx != null) Console.WriteLine("  first exception: " + firstEx);
        }

        /// <summary>The islands whose outlines touch the 180° meridian (GSHHG cuts its polygons there), and the columns across it.</summary>
        static void Islands180(World w)
        {
            var m = w.Model; var geo = w.Geo; var data = w.Data;
            var isl = data.Islands?.Islands;
            if (isl == null || m.Islands == null) { Console.WriteLine("  no islands"); return; }
            int touching = 0, shown = 0;
            foreach (var i in isl)
            {
                int onCut = 0; double lat0 = 90, lat1 = -90;
                for (int k = 0; k < i.Lon.Length; k++)
                    if (Math.Abs(Math.Abs(i.Lon[k]) - 180) < 1e-9) { onCut++; lat0 = Math.Min(lat0, i.Lat[k]); lat1 = Math.Max(lat1, i.Lat[k]); }
                if (onCut < 2) continue;
                touching++;
                double cutKm = (lat1 - lat0) * 111.32;
                // the column in the middle of the cut, and 30 % of the island's profile width to either side
                double latMid = 0.5 * (lat0 + lat1);
                bool east = i.MinLon < 0;   // this half lies east of the meridian (lon from -180)
                geo.FromGeo(latMid, 180, out double x0, out double z0);
                double xl = Math.Ceiling(x0), z = Math.Floor(z0);
                var at = m.GetColumn(xl, z); var w1 = m.GetColumn(xl - 12, z); var e1 = m.GetColumn(xl + 12, z);
                bool kept = i.WidthKm * Math.Min(geo.BlocksPerDegreeX / (111.32 * Math.Max(Math.Cos(latMid * Math.PI / 180), 0.02)), geo.BlocksPerDegreeZ / 111.32) >= m.Islands.MinWidthBlocks;
                if (shown++ < 40)
                    Console.WriteLine($"    {(east ? "E" : "W")} half lat {i.MinLat:0.000}..{i.MaxLat:0.000} lon {i.MinLon:0.000}..{i.MaxLon:0.000} area {i.AreaKm2:0.0} km2 height {i.Height:0} m width {i.WidthKm:0.00} km; cut {cutKm:0.00} km long ({onCut} points){(kept ? "" : " (too small at this scale)")}; " +
                        $"tops 12 blocks west {(w1.Water != 0 ? "~" : "")}{w1.Top}, at the meridian {(at.Water != 0 ? "~" : at.Beach ? "b" : "")}{at.Top}, 12 east {(e1.Water != 0 ? "~" : "")}{e1.Top}");
            }
            Console.WriteLine($"  {touching} island outlines of {isl.Length} have an edge on the 180° meridian");
            foreach (var i in isl.OrderByDescending(q => q.AreaKm2).Take(6)) Console.WriteLine($"    largest: {i.AreaKm2:0} km2, {i.Lon.Length} points, width {i.WidthKm:0.0} km, height {i.Height:0} m at {0.5 * (i.MinLat + i.MaxLat):0.00},{0.5 * (i.MinLon + i.MaxLon):0.00}");
            foreach (var i in isl.OrderByDescending(q => q.Lon.Length).Take(4)) Console.WriteLine($"    most points: {i.Lon.Length} points, {i.AreaKm2:0} km2, width {i.WidthKm:0.0} km at {0.5 * (i.MinLat + i.MaxLat):0.00},{0.5 * (i.MinLon + i.MaxLon):0.00}");
        }

        /// <summary>The flow file across its 1° tile edges (the same river water asked a hair west and east of a whole meridian,
        /// north and south of a whole parallel), and what the detail noise and single layer lookups cost.</summary>
        static void FlowEdge(World w)
        {
            var m = w.Model; var geo = w.Geo; var flow = w.Flow;
            if (flow != null)
            {
                foreach (var (name, meridian, at, from, to) in new[] { ("Amazon lon -60", true, -60.0, -3.6, -2.9), ("Amazon lat -3", false, -3.0, -60.9, -59.2), ("Daugava lon 25", true, 25.0, 56.5, 56.9),
                    ("Rhine lat 51", false, 51.0, 6.8, 7.1), ("Rhine lat 52", false, 52.0, 5.9, 6.3), ("Lena lon 130", true, 130.0, 61.9, 62.4), ("Volga lat 48", false, 48.0, 45.5, 46.6), ("Danube lon 19", true, 19.0, 45.2, 47.9) })
                {
                    int n = 0, wet = 0, jump = 0; double worst = 0, worstA = 0, worstB = 0, worstAt = 0;
                    const double eps = 1e-9;
                    for (double t = from; t <= to; t += 0.0002)
                    {
                        double la1 = meridian ? t : at + eps, lo1 = meridian ? at - eps : t, la2 = meridian ? t : at - eps, lo2 = meridian ? at + eps : t;
                        if (!flow.At(la1, lo1, out double e1, out double n1) || !flow.At(la2, lo2, out double e2, out double n2)) continue;
                        n++;
                        double v1 = Math.Sqrt(e1 * e1 + n1 * n1), v2 = Math.Sqrt(e2 * e2 + n2 * n2);
                        if (v1 <= 0 && v2 <= 0) continue;
                        wet++;
                        double d = Math.Abs(v1 - v2);
                        if (d > 0.02) jump++;
                        if (d > worst) { worst = d; worstA = v1; worstB = v2; worstAt = t; }
                    }
                    Console.WriteLine($"  flow at {name,-16}: {wet} wet points of {n}; speed differs by more than 0.02 m/s across the edge at {jump}; largest {worst:0.000} m/s ({worstA:0.000} vs {worstB:0.000} at {worstAt:0.0000})");
                }
            }
            // what single lookups cost (the same place asked again and again moves over a 64 x 64 patch)
            var (px, pz) = w.At(51.95, 4.1);
            double Time(string what, int n, Func<int, double> f)
            {
                double sink = 0;
                for (int i = 0; i < Math.Min(n, 20000); i++) sink += f(i);
                var t = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) sink += f(i);
                Console.WriteLine($"  {what,-58} {t.Elapsed.TotalMilliseconds * 1e6 / n,9:0} ns");
                return sink;
            }
            var n1s = new Simplex2(12345 + 101); var n3s = new Simplex2(12345 + 303);
            double mpb = m.MetresPerBlock, longest = 0.5 * 3706 / mpb, shortest = Math.Max(3, 60 / mpb);
            Time($"detail noise FbmTurned (cell {longest:0.#} to {shortest:0.#} blocks)", 400000, i => n1s.FbmTurned(px + (i & 63), pz + (i >> 6 & 63), longest, 6, shortest));
            Time("detail noise RidgedTurned", 400000, i => n3s.RidgedTurned(px + (i & 63), pz + (i >> 6 & 63), longest, 6, shortest));
            foreach (var (name, lat, lon) in new[] { ("Rotterdam", 51.95, 4.1), ("Kansas", 38.5, -98.5), ("Daugava mouth", 57.05, 24.03) })
            {
                var (qx, qz) = w.At(lat, lon);
                EarthGeometry.GeoPos G(int i) => geo.ToGeo(qx + (i & 63), qz + (i >> 6 & 63));
                Console.WriteLine($"  at {name}:");
                Time("  Geometry.ToGeo", 400000, i => G(i).Lat);
                Time("  Data.SampleTerrain (with coast refs)", 400000, i => { var g = G(i); return w.Data.SampleTerrain(g.Lat, g.Lon, true).Elev; });
                if (m.Coasts != null) Time("  Coasts.SignedDistance", 400000, i => { var g = G(i); return m.Coasts.SignedDistance(g.Lat, g.Lon); });
                if (m.Works != null) Time("  Works.At", 400000, i => { var g = G(i); return m.Works.At(g.Lat, g.Lon).Dist; });
                if (m.LakeOutlines != null) Time("  LakeOutlines.At", 400000, i => { var g = G(i); return m.LakeOutlines.At(g.Lat, g.Lon).Dist; });
                if (m.RiverOutlines != null) Time("  RiverOutlines.At", 400000, i => { var g = G(i); return m.RiverOutlines.At(g.Lat, g.Lon).Dist; });
                if (m.Islands != null) Time("  Islands.Query", 400000, i => { var g = G(i); return m.Islands.Query(g.Lat, g.Lon, out var h) ? h.Dist : 0; });
                if (m.Rivers != null) Time("  Rivers.Nearest", 400000, i => m.Rivers.Nearest(qx + (i & 63), qz + (i >> 6 & 63), out var h) ? h.Dist : 0);
                if (m.Rivers != null) Time("  Rivers.AnyBucketNear (ApplyMouth's reach)", 400000, i => m.Rivers.AnyBucketNear(qx + (i & 63), qz + (i >> 6 & 63), 2 * CoastLayer.ReachFor(geo) + 60) ? 1 : 0);
                if (m.Rivers != null) Time("  Rivers.DepthNear (ReachBlend, 2 km)", 100000, i => m.Rivers.DepthNear(qx + (i & 63), qz + (i >> 6 & 63), Math.Clamp(2000 / mpb, 32, 512), 3));
                if (m.Fine != null) Time("  Fine.TrySmoothHeight", 400000, i => { var g = G(i); return m.Fine.TrySmoothHeight(g.Lat, g.Lon, true, out double h) ? h : 0; });
                if (m.Coasts != null) Time("  CoastSideAt (works + coast distance of 1318)", 20000, i => { var g = G(i); return m.CoastSideAt(g.Lat, g.Lon, out double s) ? s : 0; });
                Time("  GetColumn", 20000, i => m.GetColumn(qx + (i & 63), qz + (i >> 6 & 63)).Top);
            }
        }

        /// <summary>A lake's area as the outlines give it on either side of a tile edge it crosses (LakeOutlineLayer.Hit.Area is the
        /// lake's area in the tile asked), against the thresholds the terrain reads it by.</summary>
        static void LakeAreas(World w)
        {
            var m = w.Model; var geo = w.Geo;
            if (m.LakeOutlines == null) { Console.WriteLine("  no lake outlines"); return; }
            double mpb = m.MetresPerBlock, px = FineElevation.CellMetres / mpb, joinArea = TerrainModel.JoinedLakePixels * px * px, stripArea = ShoreMaterial.LakeMinKm2 * 1e6 / (mpb * mpb);
            int lakesCrossing = 0, stripSplit = 0, joinSplit = 0, shoreDiffer = 0;
            var shown = new List<string>();
            foreach (var (name, parallel, at, from, to) in new[] { ("lat 62 Finland", true, 62.0, 22.0, 31.0), ("lat 63 Finland", true, 63.0, 22.0, 31.0), ("lon 27 Finland", false, 27.0, 60.5, 64.0), ("lon 28 Finland", false, 28.0, 61.0, 64.0),
                ("lat 60 Sweden", true, 60.0, 12.0, 18.5), ("lat 50 Canada", true, 50.0, -96.0, -84.0), ("lon -90 Canada", false, -90.0, 48.0, 54.0), ("lat 46 Alps", true, 46.0, 6.0, 14.0), ("lon 9 Alps", false, 9.0, 45.5, 48.0),
                ("lat 55 Russia", true, 55.0, 30.0, 40.0), ("lat 61 Karelia", true, 61.0, 29.0, 36.0) })
            {
                double step = 1.0 / (parallel ? geo.BlocksPerDegreeX : geo.BlocksPerDegreeZ), eps = 0.2 / geo.BlocksPerDegreeZ;
                double lastLevel = double.NaN; bool inLake = false;
                double aMin = 0, aMax = 0; double atStart = 0;
                for (double t = from; t <= to; t += step)
                {
                    double la1 = parallel ? at + eps : t, lo1 = parallel ? t : at - eps, la2 = parallel ? at - eps : t, lo2 = parallel ? t : at + eps;
                    var h1 = m.LakeOutlines.At(la1, lo1); var h2 = m.LakeOutlines.At(la2, lo2);
                    bool both = h1.Inside && h2.Inside && h1.Level == h2.Level;
                    if (both && !inLake) { inLake = true; lakesCrossing++; aMin = Math.Min(h1.Area, h2.Area); aMax = Math.Max(h1.Area, h2.Area); atStart = t; lastLevel = h1.Level; }
                    else if (!both && inLake)
                    {
                        inLake = false;
                        bool strip = aMin < stripArea && aMax >= stripArea, join = aMin <= joinArea && aMax > joinArea;
                        if (strip) stripSplit++;
                        if (join) joinSplit++;
                        if ((strip || join) && shown.Count < 14)
                            shown.Add($"{name} at {atStart:0.0000}..{t:0.0000} level {lastLevel:0.0} m: {aMin * mpb * mpb / 1e6:0.000} km2 on one side, {aMax * mpb * mpb / 1e6:0.000} km2 on the other{(strip ? " (shore strip on one side only)" : "")}{(join ? " (one side counts as a pool of the river's)" : "")}");
                    }
                }
            }
            Console.WriteLine($"  lakes crossing the sampled tile edges: {lakesCrossing}; with a piece under {ShoreMaterial.LakeMinKm2} km2 on one side only (no shore strip there): {stripSplit}; with a piece of at most {TerrainModel.JoinedLakePixels} mask pixels on one side only: {joinSplit}");
            foreach (var s in shown) Console.WriteLine("    " + s);
        }

        /// <summary>River water columns on either side of the flow file's tile edges (whole degrees): the bed's step, the still
        /// flag and the flow's turn across the edge, against the same a few blocks further on.</summary>
        static void RiverEdges(World w)
        {
            var m = w.Model; var geo = w.Geo;
            foreach (var (name, meridian, at, from, to) in new[] { ("Rhine lat 51", false, 51.0, 6.94, 7.0), ("Amazon lon -60", true, -60.0, -3.35, -3.05), ("Amazon lat -3", false, -3.0, -60.9, -59.2), ("Danube lon 19", true, 19.0, 45.2, 47.9),
                ("Volga lat 48", false, 48.0, 45.5, 46.6), ("Lena lon 130", true, 130.0, 61.9, 62.4), ("Daugava lon 25", true, 25.0, 56.55, 56.7), ("Elbe lat 53", false, 53.0, 11.5, 11.9), ("Vistula lon 19", true, 19.0, 52.5, 54.4), ("Nile lat 30", false, 30.0, 31.0, 31.4) })
            {
                for (double t = from; t <= to + 1e-9; t += 0.25) { if (meridian) { w.Need(t, at - 0.01, 0); w.Need(t, at + 0.01, 0); } else { w.Need(at + 0.01, t, 0); w.Need(at - 0.01, t, 0); } }
                geo.FromGeo(meridian ? from : at, meridian ? at : from, out double x0, out double z0);
                geo.FromGeo(meridian ? to : at, meridian ? at : to, out double x1, out double z1);
                double line = meridian ? Math.Ceiling(x0) : Math.Ceiling(z0);
                double a = meridian ? Math.Min(z0, z1) : Math.Min(x0, x1), b = meridian ? Math.Max(z0, z1) : Math.Max(x0, x1);
                int[] offsets = { 0, 4, 13, 31 };
                var n = new int[4]; var sum = new double[4]; var mx = new int[4]; var big = new int[4]; var still = new int[4]; var turn = new int[4];
                string worst = null;
                for (double v = Math.Floor(a); v <= b; v += 1)
                    for (int k = 0; k < offsets.Length; k++)
                    {
                        double u = line + offsets[k];
                        var ca = meridian ? m.GetColumn(u - 1, v) : m.GetColumn(v, u - 1);
                        var cb = meridian ? m.GetColumn(u, v) : m.GetColumn(v, u);
                        if (!ca.River || !cb.River || ca.WaterTop != cb.WaterTop) continue;
                        n[k]++;
                        int d = Math.Abs(ca.Top - cb.Top);
                        sum[k] += d; if (d >= 2) big[k]++;
                        if (d > mx[k]) { mx[k] = d; if (k == 0) worst = $"({(meridian ? u - 1 : v)}, {(meridian ? v : u - 1)}) bed {ca.Top} | ({(meridian ? u : v)}, {(meridian ? v : u)}) bed {cb.Top}, water {ca.WaterTop}, lat {cb.Lat:0.00000} lon {cb.Lon:0.00000}"; }
                        if (ca.Still != cb.Still) still[k]++;
                        double la = Math.Sqrt(ca.FlowX * ca.FlowX + ca.FlowZ * ca.FlowZ), lb = Math.Sqrt(cb.FlowX * cb.FlowX + cb.FlowZ * cb.FlowZ);
                        if (la > 0 && lb > 0 && (ca.FlowX * cb.FlowX + ca.FlowZ * cb.FlowZ) / (la * lb) < 0.7071) turn[k]++;
                    }
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < offsets.Length; k++)
                    sb.Append($" | {(k == 0 ? "at the edge" : offsets[k] + " on")}: {n[k]} pairs, bed step mean {(n[k] > 0 ? sum[k] / n[k] : 0):0.000} max {mx[k]}, 2+ blocks {big[k]}, still flips {still[k]}, flow turns over 45 deg {turn[k]}");
                Console.WriteLine($"  {name,-16}{sb}");
                if (worst != null && mx[0] >= 2) Console.WriteLine("      largest at the edge: " + worst);
            }
        }

        /// <summary>The shore strip (Column.Shore, Beach) of land beside one lake, north and south of a tile edge the lake crosses.</summary>
        static void LakeStrip(World w)
        {
            var m = w.Model; var geo = w.Geo;
            foreach (var (name, lat, lon, level, dLat, dLon) in new[] { ("Nasijarvi at lat 62", 62.0, 24.40, 95.0, 0.03, 0.07), ("lake at lat 62, 24.05", 62.0, 24.057, 95.0, 0.03, 0.03), ("lake at lat 62, 25.11", 62.0, 25.115, 97.0, 0.03, 0.03) })
            {
                w.Need(lat + 0.01, lon, 0); w.Need(lat - 0.01, lon, 0);
                geo.FromGeo(lat + dLat, lon - dLon, out double x0, out double z0);
                geo.FromGeo(lat - dLat, lon + dLon, out double x1, out double z1);
                geo.FromGeo(lat, lon, out _, out double zLine);
                var n = new int[2]; var strip = new int[2]; var water = new int[2]; var waterShore = new int[2]; var areaKm2 = new double[2];
                for (double z = Math.Floor(z0); z <= z1; z++)
                    for (double x = Math.Floor(x0); x <= x1; x++)
                    {
                        var c = m.GetColumn(x, z);
                        var h = m.LakeOutlines.At(c.Lat, c.Lon);
                        if (!h.Covered || double.IsNaN(h.Level) || h.Level != level) continue;
                        int side = z < zLine ? 0 : 1;   // 0 north of the line
                        if (h.Inside) { water[side]++; if (c.Shore != 0) waterShore[side]++; areaKm2[side] = h.Area * m.MetresPerBlock * m.MetresPerBlock / 1e6; continue; }
                        if (h.Dist > 1.5 || c.Water != 0) continue;
                        n[side]++;
                        if (c.Shore != 0 || c.Beach) strip[side]++;
                    }
                Console.WriteLine($"  {name}: north of the edge (the lake's piece there {areaKm2[0]:0.000} km2) {strip[0]} of {n[0]} shore columns are beach or strip, {waterShore[0]} of {water[0]} water columns carry the shore class; " +
                    $"south (piece {areaKm2[1]:0.000} km2) {strip[1]} of {n[1]}, {waterShore[1]} of {water[1]}");
            }
        }

        static readonly (string name, double lat, double lon, int size)[] DumpAreas =
        {
            ("Rotterdam", 51.95, 4.1, 512), ("Rhine-Maas delta", 51.85, 4.35, 384), ("Flevoland", 52.5, 5.45, 384), ("Wadden / Afsluitdijk", 53.0, 5.2, 384),
            ("Zeeland", 51.55, 3.9, 384), ("Daugava mouth", 57.05, 24.03, 256), ("Amazon mouth", -0.1, -50.3, 256), ("Elbe mouth", 53.87, 8.9, 256), ("Thames mouth", 51.48, 0.6, 256),
        };

        /// <summary>The columns that differ between two dumps (orig and fix), field by field.</summary>
        static void DumpDiff(World w)
        {
            int scale = (int)Math.Round(40075017 / w.Geo.RingLength);
            string dir = Path.GetFullPath(Path.Combine(w.FineFolder ?? ".", ".."));
            using var a = new BinaryReader(File.OpenRead(Path.Combine(dir, $"dump-{scale}-orig.bin")));
            using var b = new BinaryReader(File.OpenRead(Path.Combine(dir, $"dump-{scale}-fix.bin")));
            object Read(BinaryReader r, Type t) => t == typeof(int) ? r.ReadInt32() : t == typeof(double) ? r.ReadDouble() : t == typeof(float) ? r.ReadSingle() : t == typeof(bool) ? r.ReadBoolean() : (object)r.ReadByte();
            foreach (var (name, lat, lon, size) in DumpAreas)
            {
                var (px, pz) = w.At(lat, lon);
                px -= size / 2; pz -= size / 2;
                int diffs = 0; var byField = new Dictionary<string, int>();
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                    {
                        string first = null;
                        foreach (var f in ColumnFields)
                        {
                            object va = Read(a, f.FieldType), vb = Read(b, f.FieldType);
                            if (Equals(va, vb)) continue;
                            byField[f.Name] = byField.GetValueOrDefault(f.Name) + 1;
                            first ??= $"{f.Name} {va} vs {vb}";
                        }
                        if (first == null) continue;
                        if (diffs++ < 6)
                        {
                            var c = w.Model.GetColumn(px + i, pz + j);
                            Console.WriteLine($"    {name} ({px + i}, {pz + j}): {first}; this build: {Show(c)}");
                        }
                    }
                Console.WriteLine($"  {name,-22} {diffs} of {size * size} columns differ {string.Join(" ", byField.Select(k => k.Key + ":" + k.Value))}");
            }
        }

        /// <summary>Areas' columns written to a file (every field), for a byte-for-byte comparison of two builds; and the time.</summary>
        static void Dump(World w)
        {
            string tag = Environment.GetEnvironmentVariable("TAG") ?? "orig";
            int scale = (int)Math.Round(40075017 / w.Geo.RingLength);
            var areas = DumpAreas;
            string path = Path.Combine(Path.GetFullPath(Path.Combine(w.FineFolder ?? ".", "..")), $"dump-{scale}-{tag}.bin");
            using var bw = new BinaryWriter(File.Create(path));
            foreach (var (name, lat, lon, size) in areas)
            {
                w.Need(lat, lon, 1);
                var (px, pz) = w.At(lat, lon);
                px -= size / 2; pz -= size / 2;
                var cols = new TerrainModel.Column[size * size];
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                var t = Stopwatch.StartNew();
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++) cols[j * size + i] = w.Model.GetColumn(px + i, pz + j);
                double us = t.Elapsed.TotalMilliseconds * 1000 / cols.Length;
                double bytes = (GC.GetAllocatedBytesForCurrentThread() - b0) / (double)cols.Length;
                var kinds = new Dictionary<string, int>();
                foreach (var c in cols)
                {
                    string k = Kind(c); kinds[k] = kinds.GetValueOrDefault(k) + 1;
                    object o = c;
                    foreach (var f in ColumnFields)
                    {
                        object v = f.GetValue(o);
                        switch (v) { case int i4: bw.Write(i4); break; case double d8: bw.Write(d8); break; case float f4: bw.Write(f4); break; case bool b1: bw.Write(b1); break; case byte u1: bw.Write(u1); break; default: throw new InvalidOperationException(f.Name); }
                    }
                }
                Console.WriteLine($"  {name,-22} {size}x{size}: {us,8:0.00} us/column, {bytes,6:0} B/column | {string.Join(" ", kinds.OrderByDescending(k => k.Value).Select(k => k.Key + ":" + k.Value))}");
            }
            Console.WriteLine("  written " + path);
        }

        /// <summary>The coast outline and the columns across the 180° meridian, where GSHHG cuts its polygons.</summary>
        static void Floats(World w)
        {
            var m = w.Model; var geo = w.Geo;
            int[] offs = { -40, -20, -10, -5, -3, -2, -1, 0, 1, 2, 4, 9, 19, 39 };
            foreach (var (name, lats) in new[] { ("Chukotka", new[] { 66.0, 66.5, 67.0, 67.5, 68.0, 68.5 }), ("Wrangel", new[] { 71.0, 71.2 }), ("Fiji Vanua Levu / Taveuni", new[] { -16.5, -16.6, -16.7, -16.8, -16.9 }),
                ("control lon 175 Chukotka", new[] { 67.0, 67.5 }) })
            {
                Console.WriteLine("  " + name + " (offsets in blocks from the meridian: " + string.Join(" ", offs) + ")");
                foreach (double lat in lats)
                {
                    double lon0 = name.StartsWith("control") ? 175 : 180;
                    w.Need(lat, lon0 - 0.01, 0); w.Need(lat, Geo.Wrap180(lon0 + 0.01), 0);
                    geo.FromGeo(lat, lon0, out double x0, out double z0);
                    double xl = Math.Ceiling(x0), z = Math.Floor(z0);
                    var sd = new System.Text.StringBuilder(); var tops = new System.Text.StringBuilder(); var isl = new System.Text.StringBuilder();
                    foreach (int o in offs)
                    {
                        var g = geo.ToGeo(xl + o, z);
                        double d = m.Coasts != null ? m.Coasts.SignedDistance(g.Lat, g.Lon) : double.NaN;
                        var c = m.GetColumn(xl + o, z);
                        sd.Append($"{d,7:0.0}");
                        tops.Append($"{(c.Water != 0 ? "~" : c.Beach ? "b" : " ")}{c.Top,5} ");
                        bool q = m.Islands != null && m.Islands.Query(g.Lat, g.Lon, out var ih);
                    }
                    var s0 = w.Data.SampleTerrain(lat, lon0);
                    Console.WriteLine($"    lat {lat,6:0.00} grid elev {s0.Elev,6:0} ocean {s0.Ocean:0.00}  coast dist:{sd}");
                    Console.WriteLine($"                                         column top:{tops}");
                }
            }
        }

        /// <summary>The river index's 128-block cells: whether a column's position along its river (Hit.Along) and its water top
        /// jump across a cell boundary more than across a line in a cell's middle.</summary>
        static void Cells(World w)
        {
            var m = w.Model; var rv = m.Rivers;
            foreach (var (name, lat, lon) in new[] { ("Amazon Manaus", -3.13, -60.0), ("Lena", 62.0, 129.75), ("Daugava Riga", 56.95, 24.09), ("Rhine Cologne", 50.94, 6.97), ("Danube Budapest", 47.5, 19.04), ("Volga", 48.7, 44.6), ("Congo", -4.3, 15.3), ("Mississippi", 35.1, -90.1) })
            {
                w.Need(lat, lon, 0);
                var (px, pz) = w.At(lat, lon);
                int half = 1024;
                // statistics per line kind: 0 a cell boundary, 1 the middle of a cell
                var n = new long[2]; var big = new long[2]; var topDiff = new long[2]; var sum = new double[2]; var max = new double[2];
                string worst = null;
                for (int axis = 0; axis < 2; axis++)
                    for (int kind = 0; kind < 2; kind++)
                    {
                        double u0 = Math.Floor(((axis == 0 ? px : pz) - half) / 128) * 128 + (kind == 0 ? 0 : 64);
                        for (double u = u0; u < (axis == 0 ? px : pz) + half; u += 128)
                            for (double v = (axis == 0 ? pz : px) - half; v < (axis == 0 ? pz : px) + half; v += 1)
                            {
                                double xa = axis == 0 ? u - 1 : v, za = axis == 0 ? v : u - 1, xb = axis == 0 ? u : v, zb = axis == 0 ? v : u;
                                if (!rv.Nearest(xa, za, out var ha) || !rv.Nearest(xb, zb, out var hb)) continue;
                                if (ha.Dist > ha.HalfWidth || hb.Dist > hb.HalfWidth || ha.Reach != hb.Reach || ha.Mouth || hb.Mouth) continue;
                                double d = Math.Abs(ha.Along - hb.Along);
                                n[kind]++; sum[kind] += d;
                                if (d > 2.5) big[kind]++;
                                if (ha.WaterTop != hb.WaterTop) topDiff[kind]++;
                                if (d > max[kind]) { max[kind] = d; if (kind == 0) worst = $"({xa},{za}) along {ha.Along:0.0} top {ha.WaterTop} | ({xb},{zb}) along {hb.Along:0.0} top {hb.WaterTop}, reach {ha.Reach}, hw {ha.HalfWidth:0.0}"; }
                            }
                    }
                Console.WriteLine($"  {name,-16} boundary pairs {n[0]}: mean |dAlong| {(n[0] > 0 ? sum[0] / n[0] : 0):0.000} max {max[0]:0.0}, >2.5: {big[0]}, water top differs {topDiff[0]} | mid-cell pairs {n[1]}: mean {(n[1] > 0 ? sum[1] / n[1] : 0):0.000} max {max[1]:0.0}, >2.5: {big[1]}, water top differs {topDiff[1]}");
                if (worst != null && max[0] > 2.5) Console.WriteLine("      worst at a boundary: " + worst);
            }
        }

        static void Misc(World w)
        {
            var m = w.Model; var data = w.Data; var geo = w.Geo;
            // (a) terminal lakes whose kept level is not the one KeptLakeLevelNear gives (the highest within 3 cells)
            int res = data.TerrainRes, W = data.TerrainW, H = data.TerrainH;
            int lakes = 0, cells = 0, mismatch = 0, dry = 0, checkedCols = 0;
            var lost = new List<string>();
            foreach (var lake in data.TerminalLakes?.Lakes ?? Array.Empty<LakeData.Lake>())
            {
                lakes++;
                int lr = (int)Math.Floor((90 - lake.Lat) * res), lc = (int)Math.Floor((lake.Lon + 180) * res);
                int lakeCells = 0, lakeMis = 0, lakeDry = 0, lakeCols = 0;
                for (int r = lr - 3 * res; r <= lr + 3 * res; r++)
                    for (int c = lc - 4 * res; c <= lc + 4 * res; c++)
                    {
                        if (r < 0 || r >= H || !data.IsTerminalLakeCell(r, c)) continue;
                        var cell = data.GetCell(r, c);
                        if ((cell.Flags & EarthData.FlagLake) == 0 || cell.Level != (short)Math.Round(lake.Level)) continue;
                        lakeCells++;
                        double la = 90 - (r + 0.5) / res, lo = (c + 0.5) / res - 180;
                        double kept = data.KeptLakeLevelNear(la, lo);
                        bool salt = (cell.Flags & EarthData.FlagSalt) != 0;
                        if (kept != cell.Level && !salt) lakeMis++;
                        geo.FromGeo(la, lo, out double x, out double z);
                        var col = m.GetColumn(Math.Floor(x), Math.Floor(z));
                        lakeCols++;
                        if (col.Water == TerrainModel.WaterNone) lakeDry++;
                    }
                cells += lakeCells; mismatch += lakeMis; dry += lakeDry; checkedCols += lakeCols;
                if (lakeMis > 0 || (lakeCells > 0 && lakeDry * 2 > lakeCols)) lost.Add($"{lake.Lat:0.00},{lake.Lon:0.00} level {lake.Level:0}: {lakeCells} cells, {lakeMis} with another kept level near, {lakeDry} of {lakeCols} cell centres dry");
            }
            Console.WriteLine($"  terminal lakes {lakes}: {cells} cells, {mismatch} not at the kept level near them, {dry} of {checkedCols} cell centres are dry columns");
            foreach (var l in lost.Take(25)) Console.WriteLine("    " + l);

            // (b) squares with grid land but no GLO-90 tile in the source list
            if (w.Fine != null)
            {
                int squares = 0; var list = new List<string>();
                for (int latS = -90; latS < 90; latS++)
                    for (int lonW = -180; lonW < 180; lonW++)
                    {
                        if (w.Fine.SourceHas(latS, lonW)) continue;
                        int land = 0; double top = 0;
                        for (int r = (89 - latS) * res; r < (90 - latS) * res; r++)
                            for (int c = (lonW + 180) * res; c < (lonW + 181) * res; c++)
                            {
                                var cell = data.GetCell(r, c);
                                if ((cell.Flags & EarthData.FlagOcean) == 0 && cell.Elev > 0) { land++; top = Math.Max(top, cell.Elev); }
                            }
                        if (land > 0) { squares++; if (list.Count < 12) list.Add($"{latS},{lonW}: {land} land cells up to {top:0} m"); }
                    }
                Console.WriteLine($"  squares without a GLO-90 tile where the grid has dry land above 0 m: {squares}");
                foreach (var l in list) Console.WriteLine("    " + l);
            }
            // (c) the coast tiles' margin against the reach
            if (m.Coasts != null)
            {
                var cd = data.Coasts;
                Console.WriteLine($"  coasts: tile {cd.TileDeg} deg, unit {cd.Unit} per deg, margin {cd.Margin} units = {(double)cd.Margin / cd.Unit:0.0000} deg; reach {m.Coasts.Reach} blocks = {m.Coasts.Reach / geo.BlocksPerDegreeX:0.0000} deg");
            }
            Console.WriteLine($"  lake outline reach {m.LakeOutlines?.Reach}, river outline reach {m.RiverOutlines?.Reach}, works reach {m.Works?.Reach}");
        }

        /// <summary>Columns on either side of the data's tile edges (whole degrees): the step across the edge against the step
        /// five blocks further on.</summary>
        static void Edges(World w)
        {
            var m = w.Model; var geo = w.Geo;
            var lines = new (string name, bool meridian, double at, double from, double to)[]
            {
                ("lon 24 (Riga)", true, 24, 56.3, 57.7), ("lat 57 (Riga)", false, 57, 23.3, 24.7), ("lon 8 (Alps)", true, 8, 45.5, 46.5), ("lat 46 (Alps)", false, 46, 7, 8),
                ("lon 4 (Holland)", true, 4, 51.5, 52.5), ("lat 52 (Holland)", false, 52, 4, 5), ("lon -60 (Amazon)", true, -60, -3.6, -2.6), ("lat -3 (Amazon)", false, -3, -60.5, -59.5),
                ("lon 180 (Chukotka)", true, 180, 65, 69), ("lon 180 (Fiji)", true, 180, -17.2, -16.1), ("lon 180 (Wrangel)", true, 180, 70.8, 71.5), ("lon 130 (Lena)", true, 130, 61.5, 62.5),
                ("lon 6 (Geneva)", true, 6, 46, 47), ("lon 0 (Greenwich)", true, 0, 49.5, 53), ("lat 0 (Equator Congo)", false, 0, 17, 19),
            };
            foreach (var (name, meridian, at, from, to) in lines)
            {
                for (double t = from; t <= to + 1e-9; t += 0.5) { if (meridian) { w.Need(t, at - 0.01, 0); w.Need(t, at + 0.01, 0); } else { w.Need(at + 0.01, t, 0); w.Need(at - 0.01, t, 0); } }
                geo.FromGeo(meridian ? from : at, meridian ? at : from, out double x0, out double z0);
                geo.FromGeo(meridian ? to : at, meridian ? at : to, out double x1, out double z1);
                // the first block column / row at or beyond the line
                double line = meridian ? Math.Ceiling(x0) : Math.Ceiling(z0);
                double a = meridian ? Math.Min(z0, z1) : Math.Min(x0, x1), b = meridian ? Math.Max(z0, z1) : Math.Max(x0, x1);
                var n = new int[2]; var sum = new double[2]; var mx = new int[2]; var flip = new int[2]; var wt = new int[2]; var rivFlip = new int[2];
                int land = 0;
                double step = Math.Max(1, (b - a) / 6000);
                for (double v = Math.Floor(a); v <= b; v += step)
                {
                    double vv = Math.Floor(v);
                    for (int kind = 0; kind < 2; kind++)
                    {
                        double u = line + (kind == 0 ? 0 : 5);
                        var ca = meridian ? m.GetColumn(u - 1, vv) : m.GetColumn(vv, u - 1);
                        var cb = meridian ? m.GetColumn(u, vv) : m.GetColumn(vv, u);
                        n[kind]++;
                        int d = Math.Abs(ca.Top - cb.Top);
                        sum[kind] += d; mx[kind] = Math.Max(mx[kind], d);
                        if ((ca.Water == 0) != (cb.Water == 0)) flip[kind]++;
                        if (ca.Water != 0 && cb.Water != 0 && ca.WaterTop != cb.WaterTop) wt[kind]++;
                        if (ca.River != cb.River) rivFlip[kind]++;
                        if (kind == 0 && ca.Water == 0) land++;
                    }
                }
                Console.WriteLine($"  {name,-22} {n[0]} pairs ({land} land): across the edge mean |dTop| {sum[0] / n[0]:0.000} max {mx[0]}, land/water flips {flip[0]}, water tops differ {wt[0]}, river flips {rivFlip[0]} | 5 blocks on: mean {sum[1] / n[1]:0.000} max {mx[1]}, flips {flip[1]}, water tops {wt[1]}, river {rivFlip[1]}");
            }
        }
    }
}
