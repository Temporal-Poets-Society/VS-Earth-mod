using System.Diagnostics;
using System.Globalization;
using System.Text;
using EarthMap.Core;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string root = Environment.GetEnvironmentVariable("EARTHROOT") ?? @"C:\VS mods\Earth\.claude\worktrees\release-1.0";
string outDir = Environment.GetEnvironmentVariable("OUT") ?? Path.Combine(AppContext.BaseDirectory, "out");
Directory.CreateDirectory(outDir);
int height = int.Parse(Environment.GetEnvironmentVariable("HEIGHT") ?? "512");
int pools = int.Parse(Environment.GetEnvironmentVariable("POOLS") ?? "10");
bool rivers = Environment.GetEnvironmentVariable("NORIVERS") != "1";

var sw = Stopwatch.StartNew();
var data = EarthData.LoadFromFolder(root, true);
// EarthParams.BuildModel for a new world from the Earth tab defaults (EarthMapSystem.ComputeParams)
data.SetClimateFix(true); data.SetFineTemperature(true); data.SetFineRain(true); data.SetSummits(true); data.SetGlaciers(true);
data.SetIslandOverlay(true); data.SetLakeOverlay(true); data.SetShoreOverlay(true); data.SetReservoirExclusions(true);
var geo = new EarthGeometry { RingStart = 51200, RingLength = 409600, Wraps = true, Gutter = 0, EquatorZ = 102400, BlocksPerDegreeZ = 204800.0 / 180, SeamLon0 = -169.25 };
var vs = new VerticalTerrainOptions().Build(height, Math.Min(110, height / 2), 0.0625, 200, 20, 0.43);
var model = new TerrainModel(data, geo, vs, long.Parse(Environment.GetEnvironmentVariable("SEED") ?? "386217"))
{
    GutterSeabedY = 60, LocalAlpineEcology = true, Summits = data.HasSummits, SummitRules = KnownSummits.Version, SummitMinElevation = 3000,
    Peaks = data.ReadPeaks(), AlpineSurfaceRules = MountainEcology.SurfaceVersion, AridRiverRules = AridHydrology.Version, DetailScale = 1, RiverRelief = 1,
    GutterBridges = true, GutterSeabed = TerrainModel.RealSeamVersion, MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = data.ShoreOverlay, ClimateRules = 3,
};
model.Islands = new IslandLayer(data.Islands, geo, 4, IslandLayer.RulesVersion);
model.Coasts = new CoastLayer(data.Coasts, geo, 4);
model.Straits = new NavigableStraits(geo, model);
if (rivers)
{
    var ends = RiverEnds.Prepare(data, data.RiverReaches());
    var reaches = RiverDeltas.Apply(ends.Reaches).Reaches;
    model.Rivers = new RiverNetwork(reaches, geo, 100, 1.5, model.RiverWaterTop, "ends", 20, geo.RingLength / 204800.0, 0.5, 2,
        falls: RiverFalls.All, seaLevelPiece: SeaChannels.CanalCrossing(SeaChannels.CanalCourses(geo, null)),
        continueDownstream: true, fadeEnds: ends.Fade, geometrySeams: true, widthRules: RiverNetwork.WidthRulesVersion, poolLayout: pools, rapids: true);
    model.WetSourceFlow = AridHydrology.WetSourceFlow(data, model.Rivers.Reaches);
    model.DryFans = new WadiFans(model);
    var cb = Stopwatch.StartNew();
    model.Channels = SeaChannels.Build(model, true, null, int.Parse(Environment.GetEnvironmentVariable("CHANNELS") ?? SeaChannels.Version.ToString()));
    Console.WriteLine($"channels {Environment.GetEnvironmentVariable("CHANNELS") ?? SeaChannels.Version.ToString()} built in {cb.ElapsedMilliseconds} ms");
}
#if WALLS
model.RiverValleyRules = int.Parse(Environment.GetEnvironmentVariable("VALLEY") ?? TerrainModel.RiverValleyVersion.ToString());
Console.WriteLine($"river valley rules {model.RiverValleyRules}");
#endif
#if FINE
// GLO90=<folder of converted tiles>: dry land's heights from Copernicus GLO-90 (decision 0051), attached after the model is
// built as EarthElevationSystem.AttachServer does; FINERULES (default the code's current version) sets the world's rules.
// With every tile in the folder no thread needs to fetch (FineElevation.FetchAllowed stays false).
string glo90 = Environment.GetEnvironmentVariable("GLO90");
if (!string.IsNullOrEmpty(glo90))
{
    model.Fine = new FineElevation(glo90, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
    model.FineRules = int.Parse(Environment.GetEnvironmentVariable("FINERULES") ?? FineElevation.Version.ToString());
    Console.WriteLine($"GLO-90 land heights from {glo90}, rules {model.FineRules}");
}
#endif
Console.WriteLine($"model in {sw.ElapsedMilliseconds} ms, height {height}, sea {model.SeaLevel}, pools {pools}");
foreach (string a in (Environment.GetEnvironmentVariable("ABLATE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
{
    if (a == "channels") model.Channels = null;
    else if (a == "straits") model.Straits = null;
    else if (a == "rivers") model.Rivers = null;
    else if (a == "coasts") model.Coasts = null;
    else if (a == "islands") model.Islands = null;
    else if (a == "fans") model.DryFans = null;
    Console.WriteLine("ablated " + a);
}

// Column.Shelf (the shore profile channel rules 3 shelve to) exists from decision 0052 on: not in release/1.0
#if WALLS
string ShelfText(TerrainModel.Column c) => c.Shelf.ToString("0.00");
#else
string ShelfText(TerrainModel.Column c) => "-";
#endif
string mode = args.Length > 0 ? args[0] : "sites";
var sites = new List<(string name, double lat, double lon)>();
for (int i = 1; i + 2 < args.Length + 1 && i + 2 <= args.Length; i += 3)
    sites.Add((args[i], double.Parse(args[i + 1]), double.Parse(args[i + 2])));
if (sites.Count == 0) sites.AddRange(new[] { ("sion", 46.2562, 7.3295), ("zeeland", 51.4183, 4.0143) });

if (mode == "spills")
{
    // river or lake water beside dry land whose top is below the water's top (the water would run out), within RADIUS; and
    // the time to make the columns
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "400");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Floor(fx), cz = (int)Math.Floor(fz), n = 2 * R + 1;
        var cols = new TerrainModel.Column[n * n];
        var swc = Stopwatch.StartNew();
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(cx - R + i, cz - R + j);
        double us = swc.Elapsed.TotalMilliseconds * 1000.0 / (n * n);
        int spills = 0, worst = 0;
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
        {
            var a = cols[j * n + i];
            if (a.Water == TerrainModel.WaterNone || a.Water == TerrainModel.WaterSea) continue;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int i2 = i + di, j2 = j + dj; if (i2 < 0 || j2 < 0 || i2 >= n || j2 >= n) continue;
                var b = cols[j2 * n + i2];
                if (b.Water != TerrainModel.WaterNone) continue;
                if (b.Top < a.WaterTop) { spills++; worst = Math.Max(worst, a.WaterTop - b.Top); }
            }
        }
        Console.WriteLine($"== {site.name}: water beside lower dry land {spills} (worst {worst}); {us:0.00} us per column");
    }
    return;
}
if (mode == "hash")
{
    // a fingerprint of every column (all fields old and new rules share) within RADIUS of each site
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "600");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Floor(fx), cz = (int)Math.Floor(fz), n = 2 * R + 1;
        var h = new ulong[n];
        Parallel.For(0, n, j =>
        {
            ulong acc = 1469598103934665603UL;
            for (int i = 0; i < n; i++)
            {
                var c = model.GetColumn(cx - R + i, cz - R + j);
                foreach (long v in new long[] { c.Top, c.WaterTop, c.Water, c.Beach ? 1 : 0, c.NearRiver ? 1 : 0, c.River ? 1 : 0, c.Ramp, BitConverter.DoubleToInt64Bits(c.Elev), c.IceBottom })
                    { acc ^= (ulong)v; acc *= 1099511628211UL; }
            }
            h[j] = acc;
        });
        ulong all = 0; foreach (var x in h) all = all * 31 + x;
        Console.WriteLine($"== {site.name} {all:x16}");
    }
    return;
}
if (mode == "seabed")
{
    // steps on the sea bed and from the shore down to it within RADIUS: neighbouring columns where at least one is sea and the
    // tops differ by MINSTEP or more (the shore's own top counts as sea - 1 at most)
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "600"), minStep = int.Parse(Environment.GetEnvironmentVariable("MINSTEP") ?? "3");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Floor(fx), cz = (int)Math.Floor(fz), n = 2 * R + 1, sea = model.SeaLevel;
        var cols = new TerrainModel.Column[n * n];
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        var hist = new SortedDictionary<int, int>();
        int shoreDrops = 0, bedSteps = 0, worst = 0, seaCols = 0, deep6 = 0;
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
        {
            var a = cols[j * n + i];
            if (a.Water == TerrainModel.WaterSea) { seaCols++; if (a.Top <= sea - 1 - 6) deep6++; }
            foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
            {
                int i2 = i + di, j2 = j + dj; if (i2 >= n || j2 >= n) continue;
                var b = cols[j2 * n + i2];
                bool sa = a.Water == TerrainModel.WaterSea, sb = b.Water == TerrainModel.WaterSea;
                if (!sa && !sb) continue;
                int ta = sa ? a.Top : Math.Min(a.Top, sea - 1), tb = sb ? b.Top : Math.Min(b.Top, sea - 1);
                int st = Math.Abs(ta - tb);
                if (st < minStep) continue;
                if (sa && sb) bedSteps++; else shoreDrops++;
                if (st >= int.Parse(Environment.GetEnvironmentVariable("SHOWFROM") ?? "99") && (j * 7 + i * 13) % int.Parse(Environment.GetEnvironmentVariable("EVERY") ?? "40") == 0)
                {
                    var l = sa ? b : a; var w = sa ? a : b;
                    var gl = geo.ToGeo(cx - R + (sa ? i2 : i), cz - R + (sa ? j2 : j));
                    bool dec = model.CoastLandAt(gl.Lat, gl.Lon, out bool cl);
                    Console.WriteLine($"   ({i - R},{j - R}) {gl.Lat:0.0000} {gl.Lon:0.0000} land top {l.Top} water{l.Water} near {(l.NearRiver ? 1 : 0)} beach {(l.Beach ? 1 : 0)} elev {l.Elev:0.0} outline {(dec ? (cl ? "land" : "sea") : "-")} | sea top {w.Top} shelf {ShelfText(w)} elev {w.Elev:0.0}");
                }
                hist[st] = hist.GetValueOrDefault(st) + 1; worst = Math.Max(worst, st);
            }
        }
        Console.WriteLine($"== {site.name}: sea columns {seaCols}, 6+ deep {deep6}; steps >= {minStep}: shore to bed {shoreDrops}, on the bed {bedSteps}, worst {worst}; by height: {string.Join(", ", hist.Select(h => h.Key + ":" + h.Value))}");
    }
    return;
}
if (mode == "crossings")
{
    // every river exit channel: its course's points (after the river's end) the coast outlines have as land
    int chans = 0, withLand = 0, points = 0, landPoints = 0, noWay = model.Channels.NoWay;
    var worst = new List<(int n, string where)>();
    foreach (var ch in model.Channels.Channels)
    {
        if (ch.Canal) continue;
        chans++;
        int n = 0;
        for (int k = 1; k < ch.X.Length; k++)
        {
            points++;
            var g = geo.ToGeo(ch.X[k], ch.Z[k]);
            if (model.CoastLandAt(g.Lat, g.Lon, out bool land) && land) n++;
        }
        landPoints += n;
        if (n > 0) { withLand++; var g0 = geo.ToGeo(ch.X[0], ch.Z[0]); worst.Add((n, $"{g0.Lat:0.000} {g0.Lon:0.000} q {ch.Q:0}")); }
    }
    Console.WriteLine($"channels {chans}, no way {noWay}, with outline land on the course {withLand}, points {points}, on outline land {landPoints}");
    foreach (var w in worst.OrderByDescending(w => w.n).Take(12)) Console.WriteLine($"   {w.n} land points: {w.where}");
    foreach (int r in model.Channels.NoWayReaches)
    {
        if (model.Rivers.ReachLine(r, out var lx, out var lz)) { var g = geo.ToGeo(lx[^1], lz[^1]); Console.WriteLine($"   no way: reach {r} ends {g.Lat:0.000} {g.Lon:0.000} q {model.Rivers.Reaches[r].Q:0}"); }
    }
    return;
}
if (mode == "channels")
{
    // the exit channels passing within RADIUS blocks of each site: river, discharge, depth, widths, course
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "400");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        Console.WriteLine($"== {site.name}");
        foreach (var ch in model.Channels.Channels)
        {
            if (ch.Canal) continue;
            bool close = false;
            for (int k = 0; k < ch.X.Length; k++) if (Math.Abs(ch.X[k] - fx) < R && Math.Abs(ch.Z[k] - fz) < R) close = true;
            if (!close) continue;
            
            Console.WriteLine($"channel of reach {ch.Reach} q {ch.Q:0} depth {ch.Depth} hw {ch.HalfWidth:0.0} max hw {ch.MaxHalfWidth:0.0} length {ch.Length:0}");
            for (int k = 0; k < ch.X.Length; k++)
            {
                var g = geo.ToGeo(ch.X[k], ch.Z[k]);
                var s = data.SampleTerrain(g.Lat, g.Lon, true);
                bool decided = model.CoastLandAt(g.Lat, g.Lon, out bool cland);
                bool island = model.Islands.Query(g.Lat, g.Lon, out var ih) && ih.Inside;
                var col = model.GetColumn(ch.X[k], ch.Z[k]);
                Console.WriteLine($"   {g.Lat:0.0000} {g.Lon:0.0000}  ({ch.X[k] - fx:0},{ch.Z[k] - fz:0})  elev {s.Elev:0.0} ocean {s.Ocean:0.00} outline {(decided ? (cland ? "land" : "sea") : "-")} island {island} column top {col.Top} water {col.Water}");
            }
        }
    }
    return;
}

#if FINE
if (mode == "finediff")
{
    // columns within RADIUS whose water (sea, lake, river or none) differs between the built-in heights and GLO-90 (GLO90 must
    // be set): GLO-90 should change dry land's height only
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "600");
    var fine = model.Fine;
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Round(fx), cz = (int)Math.Round(fz), n = 2 * R + 1;
        var a = new TerrainModel.Column[n * n];
        var b = new TerrainModel.Column[n * n];
        model.Fine = null;
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) a[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        model.Fine = fine;
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) b[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        var kinds = new SortedDictionary<string, int>();
        var shown = 0;
        for (int k = 0; k < n * n; k++)
        {
            if (a[k].Water == b[k].Water) continue;
            string kind = $"water {a[k].Water} -> {b[k].Water}";
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            if (shown++ < int.Parse(Environment.GetEnvironmentVariable("EXAMPLES") ?? "12"))
            {
                int i = k % n, j = k / n; var g = geo.ToGeo(cx - R + i, cz - R + j);
                Console.WriteLine($"   ({i - R},{j - R}) {g.Lat:0.0000} {g.Lon:0.0000}: built-in top {a[k].Top} water {a[k].Water} elev {a[k].Elev:0.0} near {(a[k].NearRiver ? 1 : 0)} | GLO-90 top {b[k].Top} water {b[k].Water} elev {b[k].Elev:0.0} near {(b[k].NearRiver ? 1 : 0)}");
            }
        }
        Console.WriteLine($"== {site.name}: columns whose water differs {kinds.Values.Sum()}: {string.Join(", ", kinds.Select(kv => kv.Key + ": " + kv.Value))}");
    }
    return;
}
#endif
#if FINE
if (mode == "lanes")
{
    // every river exit channel's lane worldwide (its course, the river's half width plus the channel's depth each side for the
    // sloping sides): columns that are sea on the built-in heights and not on GLO-90, or the other way round (GLO90 must be set)
    var fine = model.Fine;
    var chans = model.Channels.Channels.Where(c => !c.Canal).ToList();
    var lanes = new List<(int ch, int x, int z)>();
    // the columns under each course's centre line
    var centre = new HashSet<(int, int, int)>();
    for (int c = 0; c < chans.Count; c++)
    {
        var ch = chans[c];
        var seen = new HashSet<(int, int)>();
        double side = ch.HalfWidth + ch.Depth + 1;
        for (int k = 0; k + 1 < ch.X.Length; k++)
        {
            double dx = ch.X[k + 1] - ch.X[k], dz = ch.Z[k + 1] - ch.Z[k], len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) continue;
            double nx = -dz / len, nz = dx / len;
            for (double t = 0; t <= len; t += 0.5)
            {
                for (double o = -side; o <= side; o += 0.5)
                    seen.Add(((int)Math.Floor(ch.X[k] + dx * t / len + nx * o), (int)Math.Floor(ch.Z[k] + dz * t / len + nz * o)));
                centre.Add((c, (int)Math.Floor(ch.X[k] + dx * t / len), (int)Math.Floor(ch.Z[k] + dz * t / len)));
            }
        }
        foreach (var (x, z) in seen) lanes.Add((c, x, z));
    }
    var a = new TerrainModel.Column[lanes.Count];
    var b = new TerrainModel.Column[lanes.Count];
    model.Fine = null;
    Parallel.For(0, lanes.Count, i => a[i] = model.GetColumn(lanes[i].x, lanes[i].z));
    model.Fine = fine;
    Parallel.For(0, lanes.Count, i => b[i] = model.GetColumn(lanes[i].x, lanes[i].z));
    var dry = new int[chans.Count]; var wet = new int[chans.Count]; var cut = new int[chans.Count]; var first = new string[chans.Count];
    int dryLand = 0;
    for (int i = 0; i < lanes.Count; i++)
    {
        bool sa = a[i].Water == TerrainModel.WaterSea, sb = b[i].Water == TerrainModel.WaterSea;
        int c = lanes[i].ch;
        if (sa && !sb)
        {
            dry[c]++;
            if (b[i].Water == TerrainModel.WaterNone) dryLand++;
            if (centre.Contains((c, lanes[i].x, lanes[i].z))) cut[c]++;
            if (first[c] == null) { var g = geo.ToGeo(lanes[i].x, lanes[i].z); first[c] = $"{g.Lat:0.0000} {g.Lon:0.0000} (GLO-90 {b[i].Elev:0} m, built-in {a[i].Elev:0} m)"; }
        }
        else if (!sa && sb) wet[c]++;
    }
    Console.WriteLine($"channels {chans.Count}, lane columns {lanes.Count}; channels with lane sea that is not sea on GLO-90 {dry.Count(v => v > 0)} ({dry.Sum()} columns, " +
        $"{dryLand} of them dry land), of which on the course's centre line {cut.Count(v => v > 0)} ({cut.Sum()} columns); with lane land that becomes sea {wet.Count(v => v > 0)} ({wet.Sum()} columns)");
    foreach (int c in Enumerable.Range(0, chans.Count).Where(c => dry[c] > 0 || wet[c] > 0).OrderByDescending(c => dry[c]).Take(int.Parse(Environment.GetEnvironmentVariable("EXAMPLES") ?? "15")))
    {
        var g0 = geo.ToGeo(chans[c].X[0], chans[c].Z[0]);
        Console.WriteLine($"   {dry[c]} not sea ({cut[c]} on the centre line), {wet[c]} new sea: river end {g0.Lat:0.000} {g0.Lon:0.000} q {chans[c].Q:0}; first at {first[c] ?? "-"}");
    }
    return;
}
#endif
if (mode == "levels")
{
    // dry columns in a river's valley within RADIUS: the natural ground (no rivers, channels or fans) against the nearest
    // river's water. Ground below the water is what the valley rules raise (0: up to the water out to a quarter of the valley,
    // then a step; 1 and 2: eased); ground above the water is what they cut.
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "400");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Round(fx), cz = (int)Math.Round(fz), n = 2 * R + 1;
        var cols = new TerrainModel.Column[n * n];
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        var rivers0 = model.Rivers; var channels0 = model.Channels; var fans0 = model.DryFans;
        model.Rivers = null; model.Channels = null; model.DryFans = null;
        var nat = new TerrainModel.Column[n * n];
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) nat[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        model.Rivers = rivers0; model.Channels = channels0; model.DryFans = fans0;
        int valley = 0, below = 0, below4 = 0, below10 = 0, deepest = 0, above = 0;
        var d = new List<int>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var a = cols[j * n + i];
                if (a.Water != TerrainModel.WaterNone || !a.NearRiver || nat[j * n + i].Water != TerrainModel.WaterNone) continue;
                if (!rivers0.Nearest(cx - R + i, cz - R + j, out var hit)) continue;
                int diff = nat[j * n + i].Top - hit.WaterTop;
                valley++; d.Add(diff);
                if (diff < 0) { below++; deepest = Math.Max(deepest, -diff); if (diff <= -4) below4++; if (diff <= -10) below10++; }
                else if (diff > 1) above++;
            }
        d.Sort();
        string pct(int k) => valley == 0 ? "-" : (100.0 * k / valley).ToString("0.0") + "%";
        Console.WriteLine($"== {site.name}: valley columns {valley}; natural ground below the river's water {below} ({pct(below)}), 4+ below {below4}, 10+ below {below10}, deepest {deepest}; " +
            $"2+ above the water {above} ({pct(above)}); natural minus water: 10% {(valley > 0 ? d[valley / 10] : 0)}, median {(valley > 0 ? d[valley / 2] : 0)}, 90% {(valley > 0 ? d[valley * 9 / 10] : 0)}");
    }
    return;
}

if (mode == "walls")
{
    // land-to-land steps of at least MINSTEP blocks within RADIUS of each site, at river banks/valleys
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "400"), minStep = int.Parse(Environment.GetEnvironmentVariable("MINSTEP") ?? "4");
    foreach (var site in sites)
    {
        geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
        int cx = (int)Math.Round(fx), cz = (int)Math.Round(fz);
        int n = 2 * R + 1;
        var cols = new TerrainModel.Column[n * n];
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        var rivers0 = model.Rivers; var channels0 = model.Channels; var fans0 = model.DryFans;
        // the same area without rivers, channels and fans: the natural ground the walls are measured against
        model.Rivers = null; model.Channels = null; model.DryFans = null;
        var nat = new TerrainModel.Column[n * n];
        Parallel.For(0, n, j => { for (int i = 0; i < n; i++) nat[j * n + i] = model.GetColumn(cx - R + i, cz - R + j); });
        model.Rivers = rivers0; model.Channels = channels0; model.DryFans = fans0;
        int made = int.Parse(Environment.GetEnvironmentVariable("MADE") ?? "3");
        int count = 0, worst = 0, riverSide = 0, natural = 0, natWorst = 0; var examples = new List<string>();
        var maxByStep = new SortedDictionary<int, int>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var a = cols[j * n + i];
                if (a.Water != TerrainModel.WaterNone) continue;
                foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
                {
                    int i2 = i + di, j2 = j + dj;
                    if (i2 >= n || j2 >= n) continue;
                    var b = cols[j2 * n + i2];
                    if (b.Water != TerrainModel.WaterNone) continue;
                    int step = Math.Abs(a.Top - b.Top);
                    if (!(a.NearRiver || b.NearRiver)) continue;
                    // the natural ground's own steps at the same pairs (how rough the ground is before the rivers shape it)
                    int natStep = Math.Abs(nat[j * n + i].Top - nat[j2 * n + i2].Top);
                    if (natStep >= minStep) { natural++; natWorst = Math.Max(natWorst, natStep); }
                    if (step < minStep) continue;
                    // only steps the river shaping made: at least MADE blocks more than the natural ground's step there
                    if (step - Math.Abs(nat[j * n + i].Top - nat[j2 * n + i2].Top) < made) continue;
                    count++; worst = Math.Max(worst, step);
                    maxByStep[step] = maxByStep.GetValueOrDefault(step) + 1;
                    if (step >= int.Parse(Environment.GetEnvironmentVariable("SHOWFROM") ?? "0") && examples.Count < int.Parse(Environment.GetEnvironmentVariable("EXAMPLES") ?? "12"))
                    {
                        var hi = a.Top > b.Top ? (i, j, a) : (i2, j2, b);
                        var lo = a.Top > b.Top ? (i2, j2, b) : (i, j, a);
                        model.Rivers = null; model.Channels = null; model.DryFans = null;
                        var nhi = model.GetColumn(cx - R + hi.Item1, cz - R + hi.Item2); var nlo = model.GetColumn(cx - R + lo.Item1, cz - R + lo.Item2);
                        model.Rivers = rivers0; model.Channels = channels0; model.DryFans = fans0;
                        rivers0.Nearest(cx - R + lo.Item1, cz - R + lo.Item2, out var hit);
                        double hw = rivers0.HalfWidth(hit.Q), bank = rivers0.Bank(hit.Q), valley = rivers0.Valley(hit.Q);
                        examples.Add($"  at ({hi.Item1 - R},{hi.Item2 - R}) top {hi.Item3.Top} (natural {nhi.Top}) next to ({lo.Item1 - R},{lo.Item2 - R}) top {lo.Item3.Top} (natural {nlo.Top}); " +
                            $"river wt {hit.WaterTop} q {hit.Q:0} dist {hit.Dist:0.0} hw {hw:0.0} bank {bank:0.0} valley {valley:0.0} beyond bank {(hit.Dist - hw - bank) / valley:0.00} valleys" +
                            (rivers0.Nearest(cx - R + hi.Item1, cz - R + hi.Item2, out var hh) ? $" | high side: river wt {hh.WaterTop} q {hh.Q:0} dist {hh.Dist:0.0} reach {rivers0.HalfWidth(hh.Q) + rivers0.Bank(hh.Q) + rivers0.Valley(hh.Q):0.0}" : " | high side: no river"));
                    }
                }
            }
        Console.WriteLine($"== {site.name}: {count} land steps >= {minStep} at rivers within {R} (worst {worst}); by height: {string.Join(", ", maxByStep.Select(kv => kv.Key + ":" + kv.Value))}; natural steps >= {minStep} at the same river pairs {natural} (worst {natWorst})");
        foreach (var e in examples) Console.WriteLine(e);
    }
    return;
}

foreach (var site in sites)
{
    geo.FromGeo(site.lat, site.lon, out double fx, out double fz);
    int cx = (int)Math.Round(fx), cz = (int)Math.Round(fz);
    Console.WriteLine($"== {site.name} {site.lat},{site.lon} -> x {cx} z {cz}");
    int R = int.Parse(Environment.GetEnvironmentVariable("RADIUS") ?? "200");
    var rgb = MapColors.Render(model, cx - R, cz - R, 2 * R + 1, 2 * R + 1, 1, (i, j) => i == R && j == R ? ((byte)255, (byte)0, (byte)0) : null);
    Png.WriteRgb(Path.Combine(outDir, site.name + ".png"), 2 * R + 1, 2 * R + 1, rgb);
    int G = int.Parse(Environment.GetEnvironmentVariable("GRID") ?? "24");
    int step = int.Parse(Environment.GetEnvironmentVariable("STEP") ?? "1");
    var sb = new StringBuilder();
    for (int j = -G; j <= G; j++)
    {
        sb.Append($"{j * step,5}|");
        for (int i = -G; i <= G; i++)
        {
            var c = model.GetColumn(cx + i * step, cz + j * step);
            char k = c.Water == TerrainModel.WaterSea ? '~' : c.Water != TerrainModel.WaterNone ? (c.River ? 'r' : 'l') : c.Beach ? 'b' : c.NearRiver ? 'n' : ' ';
            sb.Append($"{c.Top,4}{k}");
        }
        sb.AppendLine();
    }
    File.WriteAllText(Path.Combine(outDir, site.name + "-grid.txt"), sb.ToString());
    // north-south and east-west transects with the data behind each column
    foreach (bool ns in new[] { true, false })
    {
        Console.WriteLine(ns ? "-- north to south" : "-- west to east");
        int T = int.Parse(Environment.GetEnvironmentVariable("TRANSECT") ?? "40");
        for (int t = -T; t <= T; t++)
        {
            double x = ns ? cx : cx + t, z = ns ? cz + t : cz;
            var c = model.GetColumn(x, z);
            var g = geo.ToGeo(x, z);
            var s = data.SampleTerrain(g.Lat, g.Lon, true);
            double ex = data.SummitExcess(g.Lat, g.Lon);
            Console.WriteLine($"{t,4} lat {g.Lat:0.00000} lon {g.Lon:0.00000} top {c.Top,4} wtop {c.WaterTop,4} w{c.Water} riv {(c.River ? 1 : 0)} near {(c.NearRiver ? 1 : 0)} beach {(c.Beach ? 1 : 0)} " +
                $"h {c.Elev,8:0.0} elev {s.Elev,8:0.0} relief {s.Relief,7:0.0} ocean {s.Ocean:0.00} lake {s.Lake:0.00} shore {s.ShoreLevel,6:0.0} excess {ex,7:0.0}");
        }
    }
}
