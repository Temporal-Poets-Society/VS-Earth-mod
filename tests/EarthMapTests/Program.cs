using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Offline checks of the terrain model, with the same geometry the mod uses for a Cylinder World
    /// 256000 x 102400 world (ring 204800) at world height 256. Writes renders to tests/out/.
    ///   dotnet run -- [dataFolder] [mapHeight]
    /// </summary>
    static class Program
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        /// <summary>
        /// Antarctica across the seam gutter (TerrainModel.GutterBridges): land and ice all the way across south of 60°S
        /// where both edges are land, the same one ring apart (both sides of Cylinder World's jump line), no cliff at
        /// the edges or the seam, open sea everywhere else and in old saves.
        /// </summary>
        static void CheckGutterBridges(TerrainModel model, EarthGeometry geo)
        {
            Console.WriteLine("seam gutter bridge");
            double ring = geo.RingLength, start = geo.RingStart, half = geo.Gutter / 2;
            double Z(double lat) => geo.EquatorZ - lat * geo.BlocksPerDegreeZ;
            bool before = model.GutterBridges;
            try
            {
                model.GutterBridges = false;
                Check(model.GetColumn(start + 10, Z(-85)).Water == TerrainModel.WaterSea, "old saves: the Antarctic gutter stays sea");
                model.GutterBridges = true;

                int land = 0, total = 0, mismatches = 0, maxStep = 0, northLand = 0;
                foreach (double lat in new[] { -88.0, -85, -82, -80, -79 })
                {
                    double z = Z(lat);
                    // east edge → east jump line → (one ring back) west jump line → west edge, one block at a time
                    TerrainModel.Column? prev = null;
                    for (double x = start + ring - half - 8; x <= start + ring + half + 8; x += 1)
                    {
                        var c = model.GetColumn(x, z);
                        if (geo.ToGeo(x, z).InGutter)
                        {
                            // (natural terrain outside the gutter never reaches the jump line, and isn't periodic)
                            var mirror = model.GetColumn(x - ring, z);
                            if (c.Top != mirror.Top || c.IceBottom != mirror.IceBottom || c.Water != mirror.Water) mismatches++;
                            total++;
                            if (c.Water == TerrainModel.WaterNone) land++;
                        }
                        if (prev is TerrainModel.Column p && p.Water == TerrainModel.WaterNone && c.Water == TerrainModel.WaterNone)
                            maxStep = Math.Max(maxStep, Math.Abs(c.Top - p.Top));
                        prev = c;
                    }
                }
                foreach (double lat in new[] { -55.0, -30, 0, 40, 66, 80 })
                {
                    for (double x = start - half; x < start + half; x += 37)
                        if (model.GetColumn(x, Z(lat)).Water == TerrainModel.WaterNone) northLand++;
                }
                Console.WriteLine($"  79-88°S: {land} of {total} gutter columns are land, largest step {maxStep} blocks, {mismatches} mismatches one ring apart");
                Check(total > 0 && land > total * 0.9, "the Antarctic gutter carries the land across");
                Check(mismatches == 0, "the bridge is identical one ring apart (both sides of the jump line)");
                Check(maxStep <= 4, "no cliff at the gutter edges or the seam");
                var plateau = model.GetColumn(start + 5, Z(-87));
                Check(plateau.IceBottom <= plateau.Top && plateau.Top > model.SeaLevel + 40, $"the polar plateau stays ice up high at the seam (top {plateau.Top})");
                Check(northLand == 0, "north of 60°S the gutter stays open sea");
            }
            finally { model.GutterBridges = before; }
        }

        /// <summary>
        /// St. Lawrence Island across the seam gutter (TerrainModel.GutterSeabed 2, decision 0029): land at the jump line where
        /// the seam meridian crosses the island, the same one ring apart, no cliff across the gutter; sea there with
        /// GutterSeabed 1; the open-sea gutter elsewhere exactly as with GutterSeabed 1.
        /// </summary>
        static void CheckSeamLand(TerrainModel model, EarthGeometry geo)
        {
            Console.WriteLine("seam gutter land (St. Lawrence Island)");
            double ring = geo.RingLength, start = geo.RingStart, half = geo.Gutter / 2;
            double Z(double lat) => geo.EquatorZ - lat * geo.BlocksPerDegreeZ;
            int before = model.GutterSeabed;
            try
            {
                int rows = 0, landAtLine = 0, oldLandAtLine = 0, total = 0, mismatches = 0, maxStep = 0;
                for (double z = Z(63.6); z <= Z(62.9); z += 2)
                {
                    model.GutterSeabed = 1;
                    if (model.GetColumn(start + 0.5, z).Water == TerrainModel.WaterNone) oldLandAtLine++;
                    model.GutterSeabed = 2;
                    if (model.GetColumn(start + 0.5, z).Water != TerrainModel.WaterNone) continue;
                    landAtLine++;
                    rows++;
                    TerrainModel.Column? prev = null;
                    for (double x = start + ring - half - 8; x <= start + ring + half + 8; x += 1)
                    {
                        var c = model.GetColumn(x, z);
                        if (geo.ToGeo(x, z).InGutter)
                        {
                            var mirror = model.GetColumn(x - ring, z);
                            if (c.Top != mirror.Top || c.Water != mirror.Water) mismatches++;
                            total++;
                        }
                        if (prev is TerrainModel.Column p && p.Water == TerrainModel.WaterNone && c.Water == TerrainModel.WaterNone)
                            maxStep = Math.Max(maxStep, Math.Abs(c.Top - p.Top));
                        prev = c;
                    }
                }
                Console.WriteLine($"  63.6-62.9°N: {landAtLine} rows (every 2 blocks) of land at the jump line, {oldLandAtLine} with GutterSeabed 1; " +
                    $"{mismatches} of {total} gutter columns differ one ring apart, largest step {maxStep} blocks");
                Check(landAtLine > 0, "the island continues across the jump line");
                Check(oldLandAtLine == 0, "GutterSeabed 1 saves keep the sea channel");
                Check(mismatches == 0, "the carried island is identical one ring apart");
                Check(maxStep <= 4, "no cliff across the gutter");

                int differ = 0, open = 0;
                foreach (double lat in new[] { -55.0, -30, 0, 40, 56, 66, 80 })
                    for (double x = start - half; x < start + half; x += 37)
                    {
                        model.GutterSeabed = 1;
                        var a = model.GetColumn(x, Z(lat));
                        model.GutterSeabed = 2;
                        var b = model.GetColumn(x, Z(lat));
                        if (a.Top != b.Top || a.Water != b.Water) differ++;
                        open++;
                    }
                Check(differ == 0, $"the open-sea gutter is unchanged ({differ} of {open} columns differ)");
            }
            finally { model.GutterSeabed = before; }
        }

        /// <summary>
        /// The real seam (decision 0030, GutterSeabed 3 in a layout without a gutter): the Earth runs across the jump line, so
        /// St. Lawrence Island keeps its real east-west length; every column near the jump line is the same one ring apart
        /// (Cylinder World's identical band), along the whole seam meridian; and the jump line is no step: across it the
        /// ground changes no more than between its neighbours (the detail noise blends into the other lap's). Without the
        /// blend (GutterSeabed 2 in the same layout) the jump line is a step.
        /// </summary>
        static void CheckRealSeam(EarthData data, LayoutInput layout, VerticalScale vs, int sea, bool riverEnds)
        {
            Console.WriteLine("real seam (no gutter)");
            var inp = new LayoutInput
            {
                WorldWidth = layout.WorldWidth, CwRingStart = layout.CwRingStart, CwRingLength = layout.CwRingLength,
                CwEquatorZ = layout.CwEquatorZ, CwPoleDistance = layout.CwPoleDistance, CwBandInner = layout.CwBandInner, Gutter = false,
            };
            var geo = EarthLayout.Build(inp, data, out _);
            var model = BuildTestModel(data, geo, vs, sea, riverEnds, out _);
            model.GutterBridges = true;
            Check(geo.Gutter == 0 && !geo.ToGeo(geo.RingStart, geo.EquatorZ).InGutter, "new worlds have no gutter");
            double ring = geo.RingLength, x0 = geo.RingStart;
            double Z(double lat) => geo.EquatorZ - lat * geo.BlocksPerDegreeZ;

            foreach (int version in new[] { TerrainModel.RealSeamVersion, 2 })
            {
                model.GutterSeabed = version;
                int rows = 0, mismatches = 0, total = 0, jumpSteps = 0;
                string firstStep = null;
                for (double z = Z(89); z < Z(-89); z += 16)
                {
                    rows++;
                    // the same one ring apart, 700 blocks each side of the jump line (Cylinder World's identical band)
                    for (double x = x0 - 700; x <= x0 + 700; x += 50)
                    {
                        var a = model.GetColumn(x, z);
                        var b = model.GetColumn(x + ring, z);
                        if (a.Top != b.Top || a.Water != b.Water) mismatches++;
                        total++;
                    }
                    // the step across the jump line (from the ring's last column to its first, where a jump lands) against the
                    // largest of its eight neighbours' steps
                    int Top(double x) { var c = model.GetColumn(x, z); return c.Water == TerrainModel.WaterSea ? Math.Min(c.Top, sea - 1) : c.Top; }
                    int across = Math.Abs(Top(x0) - Top(x0 + ring - 1)), local = 0;
                    for (int k = 1; k <= 4; k++)
                        local = Math.Max(local, Math.Max(Math.Abs(Top(x0 + k) - Top(x0 + k - 1)), Math.Abs(Top(x0 + ring - k) - Top(x0 + ring - k - 1))));
                    if (across > Math.Max(2, 2 * local))
                    {
                        jumpSteps++;
                        firstStep ??= $"{(geo.EquatorZ - z) / geo.BlocksPerDegreeZ:0.00}°: {across} blocks (neighbours {local})";
                    }
                }
                Console.WriteLine($"  GutterSeabed {version}: {mismatches} of {total} columns near the jump line differ one ring apart; " +
                    $"{jumpSteps} of {rows} rows step at the jump line{(firstStep != null ? " (first " + firstStep + ")" : "")}");
                if (version >= TerrainModel.RealSeamVersion)
                {
                    Check(mismatches == 0, "the real seam is the same one ring apart");
                    Check(jumpSteps == 0, "the jump line is no step in the real seam");
                }
                else Check(jumpSteps > 0, "without the noise blend the jump line is a step (the check sees it)");
            }

            // St. Lawrence Island (171.85-168.7°W, 62.9-63.8°N): its land's east-west extent, through the jump line
            model.GutterSeabed = TerrainModel.RealSeamVersion;
            double west = double.NaN, east = double.NaN, zi = Z(63.25);
            for (double z = Z(63.85); z <= Z(62.85); z += 8)
                for (double x = x0 + ring - 3.5 * geo.BlocksPerDegreeX; x <= x0 + ring + 1.5 * geo.BlocksPerDegreeX; x += 8)
                    if (model.GetColumn(x, z).Water == TerrainModel.WaterNone) { west = double.IsNaN(west) ? x : Math.Min(west, x); east = double.IsNaN(east) ? x : Math.Max(east, x); }
            double span = (east - west) / geo.BlocksPerDegreeX;
            bool acrossLine = model.GetColumn(x0 + ring - 2, zi).Water == TerrainModel.WaterNone && model.GetColumn(x0 + 2, zi).Water == TerrainModel.WaterNone;
            Console.WriteLine($"  St. Lawrence Island: land {span:0.00}° east-west ({geo.LonAt((west - x0) / ring):0.00}..{geo.LonAt((east - x0) / ring - 1):0.00}°), across the jump line {acrossLine}");
            Check(acrossLine && span > 2.9 && span < 3.4, "St. Lawrence Island keeps its real length (about 3.2°) across the jump line");
        }

        /// <summary>
        /// Ground frozen all year (TerrainModel.FrozenAllYear: no vanilla springs or rapids there): Antarctica's mountains
        /// and the high summits are; valleys, the Arctic coasts with a thaw and the Antarctic Peninsula's shore are not.
        /// </summary>
        /// <summary>
        /// Code review 2026-11-11, CR-T2 (GenFixRules 1, new worlds): dry land below 0 m continues from the land at 0 m (a
        /// one-block step across the 0 m contour, as across any other), where older worlds keep their two-block ledge; the sea
        /// floor's curve is unchanged.
        /// </summary>
        static void CheckDryLandBelowSea(TerrainModel model)
        {
            Console.WriteLine("dry land below 0 m (GenFixRules)");
            int was = model.GenFixRules;
            try
            {
                model.GenFixRules = 0;
                int oldStep = model.DryLandTop(0) - model.DryLandTop(-0.01);
                model.GenFixRules = TerrainModel.GenFixRulesVersion;
                int newStep = model.DryLandTop(0) - model.DryLandTop(-0.01);
                Check(oldStep == 2, $"older worlds keep the two-block ledge on the 0 m contour ({oldStep})");
                Check(newStep == 1, $"GenFixRules 1: one block across the 0 m contour ({newStep})");
                Check(model.DryLandTop(5) == (int)Math.Floor(model.Vertical.Y(5)), "GenFixRules 1: land above 0 m unchanged");
                bool monotone = true;
                for (double h = -500; h < 10; h += 0.25)
                    if (model.DryLandTop(h + 0.25) < model.DryLandTop(h)) monotone = false;
                Check(monotone, "GenFixRules 1: dry land rises with its elevation from -500 m to 10 m");
                Check(model.DryLandTop(-28) == (int)Math.Floor(model.Vertical.Y(-28)) + 1, "GenFixRules 1: the Caspian lowland a block above the sea floor's curve");
            }
            finally { model.GenFixRules = was; }
        }

        static void CheckFrozenGround(TerrainModel model, EarthGeometry geo)
        {
            Console.WriteLine("frozen ground (no springs or rapids)");
            foreach (var (name, lat, lon, frozen) in new[] {
                ("Graham Land peaks", -65.5, -63.0, true), ("Vinson Massif", -78.53, -85.62, true), ("Transantarctic Mts", -85.0, 175.0, true),
                ("Ross Island", -77.6, 167.0, true), ("Everest", 27.988, 86.925, true),
                ("Chamonix", 45.92, 6.87, false), ("Longyearbyen", 78.22, 15.65, false), ("Reykjavik", 64.14, -21.9, false), ("Tromso", 69.65, 18.96, false) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                var c = model.GetColumn(x, z);
                var g = model.TerrainGeoAt(x, z);
                double warm = model.Data.SampleClimate(g.Lat, g.Lon).TWarm - 0.0065 * model.Vertical.Elevation(c.Top);
                Check(model.FrozenAllYear(x, z, c.Top) == frozen, $"{name}: {(frozen ? "frozen" : "thaws")} (top {c.Top}, elev {c.Elev:0} m, warmest month {warm:0.0} °C, ice {c.IceBottom <= c.Top})");
            }
        }

        /// <summary>
        /// The Earth tab (decision 0023): world sizes keep Earth's proportions, start coordinates parse, every starting
        /// climate finds a random place in its band for several seeds (the same place for the same seed), and the capitals
        /// have dry land near them.
        /// </summary>
        static void CheckSpawnSearch(TerrainModel model, EarthGeometry geo, int W, int L)
        {
            Console.WriteLine("world size and spawn (Earth tab)");
            // 0068 (2.0.0-rc.5): the size is a scale; the width is the ring (2 x length) and 16 384 blocks of seam ocean at each end
            const int margin = SpawnSearch.SeamMargin;
            SpawnSearch.SizeFor("banana", out int w, out int l, out bool bad);
            Check(bad && l == 200192 && w == 2 * 200192 + 2 * margin, "an unusable size takes the default (1:100)");
            foreach (string tooFine in new[] { "4", "1:4", "0", "-10", "63999", "9000000", "1:8192000", "1:64000", "1:1001" })
            {
                SpawnSearch.SizeFor(tooFine, out _, out _, out bad);
                Check(bad, $"size {tooFine} is refused (1:5 is the finest scale; widths from before rc.5 64 000 to 8 192 000)");
            }
            // the menu's scales: the Equator's 40,075 km over the ring (2 x length) within 0.2 %, the lengths those of the
            // widths before rc.5, both sides whole map regions, under the width cap
            foreach (var (size, scale, before) in new[] { ("200", 200.0, "250880"), ("100", 100.0, "500736"), ("50", 50.0, "1002496"), ("25", 25.0, "2002944"), ("10", 10.0, "5009920"), ("1:10", 10.0, "5009920"), ("5", 5.0, null) })
            {
                SpawnSearch.SizeFor(size, out w, out l, out bad);
                double s = 40075017.0 / (2.0 * l);
                Check(!bad && w == 2 * l + 2 * margin && w % 512 == 0 && l % 512 == 0 && w <= SpawnSearch.MaxWidth && Math.Abs(s / scale - 1) < 0.002,
                    $"Earth size {size} -> {w} x {l}: 1:{s:0.00}");
                if (before == null) continue;
                SpawnSearch.SizeFor(before, out int w0, out int l0, out bool bad0);
                Check(!bad0 && l0 == l && w0 == w, $"a width from before rc.5 ({before}) keeps its length {l0} and gets the new width {w0}");
            }
            SpawnSearch.SizeFor("10", out w, out l, out _);
            Check(w == 4040704 && l == 2003968, $"1:10 is 4 040 704 x 2 003 968 (Cylinder World's numbers): {w} x {l}");
            SpawnSearch.SizeFor("5", out w, out l, out _);
            Check(w == 8047616 && l == 4007424, $"1:5 is 8 047 616 x 4 007 424: {w} x {l}");
            SpawnSearch.SizeFor("1024000", out w, out l, out _);
            Check(w % 512 == 0 && l % 512 == 0 && 2 * l == 819200, $"a width from before rc.5, 1024000 -> {w} x {l}, whole map regions");
            Check(SpawnSearch.TryParseLatLon("56.95, 24.11", out double la, out double lo) && la == 56.95 && lo == 24.11
                && SpawnSearch.TryParseLatLon("-33.9 151.2", out _, out _) && !SpawnSearch.TryParseLatLon("95,10", out _, out _)
                && !SpawnSearch.TryParseLatLon("Riga", out _, out _), "start coordinates parse ('lat, lon', 'lat lon'; not past the poles, not words)");
            foreach (var band in SpawnSearch.ClimateBands)
            {
                foreach (int seed in new[] { 12345, 777, 2026 })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool found = SpawnSearch.FindClimatePlace(model, W, L, seed, band.climate, out double lat, out double lon, out double t);
                    long ms = sw.ElapsedMilliseconds;
                    Check(found && t >= band.min && t <= band.max && lat > -60,
                        $"{band.climate}, seed {seed}: {(found ? $"{lat:0.00},{lon:0.00} at {t:0.0} °C" : "nothing found")} ({ms} ms)");
                    if (seed == 12345 && found)
                    {
                        SpawnSearch.FindClimatePlace(model, W, L, seed, band.climate, out double lat2, out double lon2, out _);
                        Check(lat2 == lat && lon2 == lon, $"{band.climate}: the same seed gives the same place");
                    }
                }
            }
            int far = 0;
            foreach (var (code, lat, lon, name) in new[] { ("riga", 56.949, 24.105, "Riga"), ("mexicocity", 19.433, -99.133, "Mexico City"),
                ("addisababa", 9.030, 38.740, "Addis Ababa"), ("wellington", -41.286, 174.776, "Wellington"), ("tokyo", 35.676, 139.650, "Tokyo"),
                ("london", 51.507, -0.128, "London"), ("lima", -12.046, -77.043, "Lima") })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                bool ok = SpawnSearch.FindDryLand(model, W, L, x, z, 3000, 10000, out int sx, out int sz);
                double d = Math.Sqrt((sx - x) * (sx - x) + (sz - z) * (sz - z));
                if (d > 100) far++;
                Check(ok && d < 600, $"{name}: dry land {d:0} blocks from the capital");
            }
            Check(far <= 3, $"most capitals have dry land within 100 blocks ({far} farther)");
        }

        /// <summary>
        /// 0053 item 17: the lake outlines' layer on a synthetic file: a lake cut by a tile edge (the cut is no shore), an
        /// island in it with a pond on the island (in, out, in again), a lake against a tile with no lakes (that side is a
        /// shore), distances across tile edges, and a tile without data.
        /// </summary>
        static void CheckLakeOutlines()
        {
            Console.WriteLine("lake outlines (synthetic)");
            static void Var(List<byte> b, ulong v) { while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; } b.Add((byte)v); }
            static ulong Zig(long v) => (ulong)((v << 1) ^ (v >> 63));
            // lakes: (level m, flags, rings of (x, y) in 1/2400° from the tile's south-west corner)
            static byte[] Tile(params (double level, int flags, int[][] rings)[] lakes)
            {
                var b = new List<byte>();
                Var(b, (ulong)lakes.Length);
                foreach (var (level, flags, rings) in lakes)
                {
                    Var(b, Zig((long)Math.Round(level * 10)));
                    Var(b, (ulong)flags);
                    Var(b, (ulong)rings.Length);
                    long px = 0, py = 0;
                    foreach (var r in rings)
                    {
                        Var(b, (ulong)(r.Length / 2));
                        for (int i = 0; i < r.Length; i += 2) { Var(b, Zig(r[i] - px)); Var(b, Zig(r[i + 1] - py)); px = r[i]; py = r[i + 1]; }
                    }
                }
                using var ms = new MemoryStream();
                using (var br = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) br.Write(b.ToArray());
                return ms.ToArray();
            }
            static int Key(int lat0, int lon0) => (lat0 + 90) * 360 + lon0 + 180;
            var tiles = new SortedDictionary<int, byte[]>
            {
                // A (50°N 10°E): lake L from 10.5°E on across the east edge, with an island and a salt pond on it; lake M against
                // the north edge
                [Key(50, 10)] = Tile(
                    (100, 0, new[] { new[] { 1200, 600, 2400, 600, 2400, 1800, 1200, 1800 }, new[] { 1500, 900, 1500, 1500, 1800, 1500, 1800, 900 } }),
                    (120, LakeOutlineData.FlagSalt, new[] { new[] { 1600, 1100, 1700, 1100, 1700, 1300, 1600, 1300 } }),
                    (80, 0, new[] { new[] { 100, 2200, 600, 2200, 600, 2400, 100, 2400 } })),
                // B (50°N 11°E): the rest of L, to 11.5°E
                [Key(50, 11)] = Tile((100, 0, new[] { new[] { 0, 600, 1200, 600, 1200, 1800, 0, 1800 } })),
            };
            var states = new byte[360 * 180];
            Array.Fill(states, LakeOutlineData.NoData);
            foreach (var k in tiles.Keys) states[k] = LakeOutlineData.HasLakes;
            states[Key(51, 10)] = LakeOutlineData.NoLakes;
            var file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes("EMLO"));
            foreach (int v in new[] { LakeOutlineData.FileVersion, 2400, 1, 360, 180 }) file.AddRange(BitConverter.GetBytes(v));
            file.AddRange(states);
            int o = file.Count + 8 * tiles.Count;
            var body = new List<byte>();
            foreach (var t in tiles.Values) { file.AddRange(BitConverter.GetBytes(o + body.Count)); file.AddRange(BitConverter.GetBytes(t.Length)); body.AddRange(t); }
            file.AddRange(body);
            var layer = new LakeOutlineLayer(LakeOutlineData.Read(file.ToArray()), new EarthGeometry { RingLength = 36000, Gutter = 0, BlocksPerDegreeZ = 100 });
            Check(layer.Reach == 6, $"reach at 1113 m per block: {layer.Reach} blocks (6)");
            bool Near(double a, double b) => Math.Abs(a - b) < 0.05;
            var h = layer.At(50.5, 10.55);
            Check(h.Covered && h.Inside && h.Level == 100 && Near(h.Dist, 5), $"in L by its west shore: inside {h.Inside}, level {h.Level}, {h.Dist:0.00} blocks (5)");
            h = layer.At(50.5, 10.98);
            Check(h.Inside && h.Level == 100 && Near(h.Dist, 6), $"in L by the tile's cut, west of it: {h.Dist:0.00} blocks (Reach: the cut is no shore)");
            h = layer.At(50.5, 11.02);
            Check(h.Inside && h.Level == 100 && Near(h.Dist, 6), $"in L by the tile's cut, east of it: inside {h.Inside}, {h.Dist:0.00} blocks (Reach)");
            h = layer.At(50.5, 11.47);
            Check(h.Inside && Near(h.Dist, 3), $"in L by its east shore in the next tile: {h.Dist:0.00} blocks (3)");
            h = layer.At(50.5, 10.66);
            Check(h.Covered && !h.Inside && h.Level == 120 && Near(h.Dist, 2.0 / 3) && h.HoldLevel == 120, $"on the island beside the pond: inside {h.Inside}, level {h.Level}, {h.Dist:0.00} blocks (0.67), hold {h.HoldLevel}");
            h = layer.At(50.5, 10.69);
            Check(h.Inside && h.Level == 120 && (h.Flags & LakeOutlineData.FlagSalt) != 0, $"in the salt pond on the island: inside {h.Inside}, level {h.Level}, flags {h.Flags}");
            h = layer.At(50.8, 10.9);
            Check(!h.Inside && h.Level == 100 && Near(h.Dist, 5) && double.IsNaN(h.HoldLevel), $"north of L: level {h.Level}, {h.Dist:0.00} blocks (5), no hold");
            h = layer.At(50.78, 11.01);
            Check(!h.Inside && h.Level == 100 && Near(h.Dist, 3), $"north of L across the tile edge: level {h.Level}, {h.Dist:0.00} blocks (3)");
            h = layer.At(50.99, 10.15);
            Check(h.Inside && h.Level == 80 && Near(h.Dist, 1), $"in M against a tile without lakes: {h.Dist:0.00} blocks (1: that side is a shore)");
            h = layer.At(51.02, 10.15);
            Check(h.Covered && !h.Inside && h.Level == 80 && Near(h.Dist, 2), $"north of M in the tile without lakes: level {h.Level}, {h.Dist:0.00} blocks (2)");
            h = layer.At(0.5, 0.5);
            Check(!h.Covered && double.IsNaN(h.Level), "a tile without data: the grid decides");
            // GenFixRules 5 (EA-0112, Fable re-review of c4cfa1f): a hit's whole area comes from the layer that made it. A
            // second layer numbers its lakes on its own (the works of 1318 beside the water mask): its lake 0 in tile A is a
            // small pond where the first layer's lake 0 is L
            var tiles2 = new SortedDictionary<int, byte[]> { [Key(50, 10)] = Tile((50, 0, new[] { new[] { 200, 200, 400, 200, 400, 400, 200, 400 } })) };
            var states2 = new byte[360 * 180];
            Array.Fill(states2, LakeOutlineData.NoData);
            states2[Key(50, 10)] = LakeOutlineData.HasLakes;
            var file2 = new List<byte>();
            file2.AddRange(Encoding.ASCII.GetBytes("EMLO"));
            foreach (int v in new[] { LakeOutlineData.FileVersion, 2400, 1, 360, 180 }) file2.AddRange(BitConverter.GetBytes(v));
            file2.AddRange(states2);
            int o2 = file2.Count + 8;
            file2.AddRange(BitConverter.GetBytes(o2)); file2.AddRange(BitConverter.GetBytes(tiles2[Key(50, 10)].Length)); file2.AddRange(tiles2[Key(50, 10)]);
            var other = new LakeOutlineLayer(LakeOutlineData.Read(file2.ToArray()), new EarthGeometry { RingLength = 36000, Gutter = 0, BlocksPerDegreeZ = 100 });
            var pond = other.At(50.125, 10.125);
            var lakeL = layer.At(50.5, 10.55);
            Check(pond.Inside && pond.Level == 50 && lakeL.Inside && lakeL.Level == 100, "the second layer's pond and the first layer's L, both lake 0 of tile A");
            Check(layer.WholeArea(pond) == other.WholeArea(pond) && other.WholeArea(pond) < 0.5 * layer.WholeArea(lakeL) && layer.WholeArea(lakeL) > lakeL.Area,
                $"a hit's whole area is its own layer's, whichever layer is asked (pond {other.WholeArea(pond):0}, asked of the other layer {layer.WholeArea(pond):0}; L with its piece next door {layer.WholeArea(lakeL):0} > {lakeL.Area:0})");
            // a fresh layer, so the first ask computes and the second reads the stored float
            var fresh = new LakeOutlineLayer(LakeOutlineData.Read(file.ToArray()), new EarthGeometry { RingLength = 36000, Gutter = 0, BlocksPerDegreeZ = 100 });
            var lakeF = fresh.At(50.5, 10.55);
            double firstAsk = fresh.WholeArea(lakeF), secondAsk = fresh.WholeArea(fresh.At(50.5, 10.6));
            Check(firstAsk == secondAsk && firstAsk == (float)firstAsk, $"the same answer on the first and a repeated ask, a float both times ({firstAsk:R})");
        }

        /// <summary>
        /// Decision 0064: the works after 1318 as the coast sees them (Works1318Data.Combine: the sea of 1318 over today's land,
        /// the land of 1318 over today's water), and the shipped file at known Dutch places: the Zuiderzee polders sea, the
        /// drained lakes of North Holland lakes, the Loosdrecht peat-digging lakes land; the Middelzee (diked by 1500) and the
        /// Biesbosch (drowned in 1421) as today.
        /// </summary>
        static void CheckWorks1318()
        {
            Console.WriteLine("works after 1318");
            const byte sea = Works1318Data.Sea, land = Works1318Data.Land;
            var inSea = new LakeOutlineLayer.Hit { Covered = true, Inside = true, Level = 0, Flags = sea, Dist = 4 };
            var nearSea = new LakeOutlineLayer.Hit { Covered = true, Inside = false, Level = 0, Flags = sea, Dist = 3 };
            var inLand = new LakeOutlineLayer.Hit { Covered = true, Inside = true, Level = 0, Flags = land, Dist = 5 };
            var none = new LakeOutlineLayer.Hit { Covered = true, Level = double.NaN, Dist = 16 };
            Check(Works1318Data.Combine(10, inSea, 16) == -4 && Works1318Data.Combine(double.NaN, inSea, 16) == -4,
                "inside the sea of 1318: sea, as far from its shore as the area's edge, with or without an outline there");
            Check(Works1318Data.Combine(10, nearSea, 16) == 3 && Works1318Data.Combine(2, nearSea, 16) == 2,
                "beside it: land no farther from the sea than the nearer of the two shores");
            Check(Works1318Data.Combine(-8, inLand, 16) == 5, "inside the land of 1318 over today's water: land");
            Check(Works1318Data.Combine(-8, none, 16) == -8 && Works1318Data.Combine(-8, default(LakeOutlineLayer.Hit), 16) == -8, "no area near: the outline alone");
            string path = Path.Combine(FindRoot(), "earthdata", Works1318Data.FileName);
            if (!File.Exists(path)) { Check(false, path + " is missing"); return; }
            var file = Works1318Data.Read(File.ReadAllBytes(path));
            var geo = new EarthGeometry { RingLength = 360000, BlocksPerDegreeZ = 1000 };
            var layer = new LakeOutlineLayer(file, geo, 16, skipFlags: Works1318Data.SkipFor(Works1318Data.RulesVersion));
            var rules3 = new LakeOutlineLayer(file, geo, 16, skipFlags: Works1318Data.SkipFor(3));
            // works rules 4 (EA-0053, EA-0057): Maasvlakte 2 (2008-2013) is sea, and the canals OSM names otherwise are land;
            // a world of rules 3 has them as today
            foreach (var (name, lat, lon, kind) in new (string, double, double, byte)[]
            {
                ("Maasvlakte 2", 51.96083, 3.98, sea), ("the Corinth Canal", 37.93125, 22.9887, land),
                ("the White Sea-Baltic Canal at Belomorsk", 64.49417, 34.77667, land), ("the Ismailia Canal", 30.41875, 31.57792, land),
            })
            {
                var h = layer.At(lat, lon);
                byte got = h.Inside ? (byte)(h.Flags & (sea | land)) : (byte)0;
                Check(got == kind, $"{name}: {(got == sea ? "sea" : got == land ? "land" : "as today")} in 1318 (works rules 4)");
                var o = rules3.At(lat, lon);
                Check(!o.Inside, $"{name}: as today in a world of works rules 3");
            }
            foreach (var (name, lat, lon, kind) in new (string, double, double, byte)[]
            {
                ("Flevoland", 52.45, 5.5, sea), ("the Noordoostpolder", 52.7, 5.75, sea), ("the Wieringermeer", 52.85, 5.0, sea),
                ("the Lauwerszee", 53.37, 6.2, sea), ("the Beemster", 52.55, 4.91, Works1318Data.Lake),
                ("the Haarlemmermeer", 52.31, 4.68, Works1318Data.Lake), ("Loosdrecht's lakes", 52.2, 5.06, land),
                ("Utrecht", 52.09, 5.12, 0), ("the Middelzee", 53.13, 5.75, 0), ("the Biesbosch", 51.76, 4.8, 0), ("Paris", 48.86, 2.35, 0),
            })
            {
                var h = layer.At(lat, lon);
                byte got = h.Inside ? h.Flags : (byte)0;
                Check(got == kind, $"{name}: {(got == sea ? "sea" : got == land ? "land" : got == Works1318Data.Lake ? "lake" : "as today")} in 1318");
            }
            CheckLakes1318(geo);
        }

        /// <summary>
        /// Decision 0107 (works rules 6, river ends rules 3): works-1318-4.bin answers as works-1318-3.bin wherever rules 5 had an
        /// area, and adds the lakes of 1318 at their levels (salt where they were) and the Salton Sea as land; lakes-2.bin is
        /// lakes.bin with the Aral at 29 m, every other lake's patches and record the same.
        /// </summary>
        static void CheckLakes1318(EarthGeometry geo)
        {
            string root = FindRoot();
            string p3 = Path.Combine(root, "earthdata", Works1318Data.FileName3), p4 = Path.Combine(root, "earthdata", Works1318Data.FileName4);
            if (!File.Exists(p3) || !File.Exists(p4)) { Check(false, p4 + " or " + p3 + " is missing"); return; }
            var l5 = new LakeOutlineLayer(Works1318Data.Read(File.ReadAllBytes(p3)), geo, 16, skipFlags: Works1318Data.SkipFor(Works1318Data.PointRules));
            var l6 = new LakeOutlineLayer(Works1318Data.Read(File.ReadAllBytes(p4)), geo, 16, skipFlags: Works1318Data.SkipFor(Works1318Data.LakeRules));
            bool same = true;
            int n = 0;
            for (double lat = 51.0; lat <= 53.6; lat += 0.01)
                for (double lon = 3.4; lon <= 7.2; lon += 0.01)
                {
                    var a = l5.At(lat, lon);
                    if (!a.Inside) continue;
                    var b = l6.At(lat, lon);
                    same &= b.Inside && b.Flags == a.Flags && b.Level.Equals(a.Level);
                    n++;
                }
            Check(same && n > 1000, $"works rules 6's file answers as rules 5's inside every area of rules 5 ({n} places in the Netherlands)");
            const byte lake = Works1318Data.Lake, salt = LakeOutlineData.FlagSalt;
            foreach (var (name, lat, lon, flags, level) in new (string, double, double, byte, double)[]
            {
                ("Lake Texcoco", 19.45, -99.05, lake | salt, 2237), ("Tenochtitlan's island (founded 1325)", 19.4326, -99.1332, lake | salt, 2237),
                ("Lake Chalco", 19.24, -98.95, lake, 2240), ("Lake Fucino", 42.0, 13.55, lake, 660), ("Lake Copais", 38.46, 23.1, lake, 97),
                ("the Aral's western basin", 44.9, 58.4, lake | salt, 29), ("Whittlesey Mere", 52.495, -0.2, lake, 1),
                ("Hachirogata", 39.98, 140.0, lake | salt, 0), ("the Salton Sea", 33.3, -115.8, Works1318Data.Land, 0),
                ("the Penon de los Banos (a natural island)", 19.4414, -99.0857, 0, 0), ("Muynak, a port of the 1960 Aral", 43.77, 59.03, 0, 0),
                ("Rome", 41.9, 12.5, 0, 0),
            })
            {
                var h = l6.At(lat, lon);
                byte got = h.Inside ? h.Flags : (byte)0;
                Check(got == flags && (flags == 0 || (flags & lake) == 0 || h.Level == level),
                    $"{name}: {(got == 0 ? "as today" : (got & lake) != 0 ? $"a lake of 1318 at {h.Level} m{((got & salt) != 0 ? ", salt" : "")}" : "land")} in a world of works rules 6");
                Check(!l5.At(lat, lon).Inside, $"{name}: as today in a world of works rules 5");
            }
            string k1 = Path.Combine(root, "earthdata", "lakes.bin"), k2 = Path.Combine(root, "earthdata", LakeData.FileName2);
            if (!File.Exists(k1) || !File.Exists(k2)) { Check(false, k2 + " or " + k1 + " is missing"); return; }
            const int w = 360 * 30;
            LakeData a1, a2;
            using (var s1 = File.OpenRead(k1)) a1 = LakeData.Read(s1, w);
            using (var s2 = File.OpenRead(k2)) a2 = LakeData.Read(s2, w);
            static bool InAral(IslandData.Patch p) => p.Row >= (90 - 47.6) * 30 && p.Row <= (90 - 43.0) * 30 && p.Col >= (180 + 57.8) * 30 && p.Col <= (180 + 62.5) * 30;
            var rest1 = a1.Patches.Where(p => !InAral(p)).Select(p => (p.Row, p.Col, p.ElevCode, p.Level, p.Flags)).ToList();
            var rest2 = a2.Patches.Where(p => !InAral(p)).Select(p => (p.Row, p.Col, p.ElevCode, p.Level, p.Flags)).ToList();
            var aral2 = a2.Lakes.Single(l => l.Name == "Aral Sea");
            var aralCells = a2.Patches.Count(p => InAral(p) && (p.Flags & EarthData.FlagLake) != 0 && p.Level == 29);
            Check(rest1.SequenceEqual(rest2), $"lakes-2.bin: the patches away from the Aral as lakes.bin's ({rest2.Count})");
            Check(a1.Lakes.Length == a2.Lakes.Length && a1.Lakes.Zip(a2.Lakes).All(t => t.First.Name == "Aral Sea" || (t.First.Name == t.Second.Name && t.First.Level == t.Second.Level)),
                "lakes-2.bin: the same lakes, every one but the Aral at its level");
            Check(aral2.Level == 29 && aralCells > 300 && aralCells < 600, $"lakes-2.bin: the Aral at {aral2.Level} m, {aralCells} cells of the 2' grid (the western basin)");
        }

        /// <summary>
        /// 0053 item 18: the river courses' search for the water mask's rivers (RiverNetwork.NearestCourse) on one straight
        /// reach: within the course's reach Nearest's own hit; beyond it, where Nearest has none, the course up to the given
        /// distance and not beyond. And the river outlines' file is told from the lakes' by its magic.
        /// </summary>
        static void CheckOutlineCourse()
        {
            Console.WriteLine("river outlines: course search and file (synthetic)");
            var geo = new EarthGeometry { RingLength = 36000, Gutter = 0, BlocksPerDegreeZ = 100, EquatorZ = 10000 };
            int n = 21;
            var re = new RiverReach { Q = 500, Next = -1, Lat = new float[n], Lon = new float[n], Level = new short[n], Fixed = new bool[n] };
            for (int k = 0; k < n; k++) { re.Lat[k] = 10; re.Lon[k] = (float)(k * 0.05); re.Level[k] = (short)(40 - 2 * k); }
            var rv = new RiverNetwork(new List<RiverReach> { re }, geo, 100, 1.5, l => 100 + (int)Math.Floor(l / 8), "none", 20, 1, 0, 0);
            geo.FromGeo(10, 0.5, out double x, out double z);
            double reach = rv.Reach(500);
            bool same = true;
            foreach (double off in new[] { 0.0, 3, -reach * 0.5, reach - 1 })
            {
                bool a = rv.Nearest(x, z + off, out var h1), b = rv.NearestCourse(x, z + off, reach + 40, out var h2);
                same &= a && b && h1.WaterTop == h2.WaterTop && h1.Reach == h2.Reach && Math.Abs(h1.Dist - h2.Dist) < 1e-9
                    && Math.Abs(h1.Along - h2.Along) < 1e-6 && h1.Ramp == h2.Ramp;
            }
            Check(same, $"within the course's reach ({reach:0.0} blocks) NearestCourse gives Nearest's hit");
            double far = reach + 20;
            bool beyond = !rv.Nearest(x, z + far, out _) && rv.NearestCourse(x, z + far, reach + 40, out var h3) && Math.Abs(h3.Dist - far) < 1e-6
                && rv.Nearest(x, z, out var h0) && h3.WaterTop == h0.WaterTop;
            Check(beyond, $"beyond its reach: Nearest has none, NearestCourse the course {far:0.0} blocks off, at its level there");
            Check(!rv.NearestCourse(x, z + reach + 50, reach + 40, out _), "nothing beyond the search distance");
            var file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes("EMRO"));
            foreach (int v in new[] { RiverOutlineData.FileVersion, 2400, 1, 360, 180 }) file.AddRange(BitConverter.GetBytes(v));
            file.AddRange(Enumerable.Repeat(LakeOutlineData.NoData, 360 * 180));
            var rivers = RiverOutlineData.Read(file.ToArray());
            bool lakeRefused = false;
            try { LakeOutlineData.Read(file.ToArray()); } catch (InvalidDataException) { lakeRefused = true; }
            var layer = RiverOutlineData.Layer(rivers, new EarthGeometry { RingLength = 3_600_000, Gutter = 0, BlocksPerDegreeZ = 11132 });
            Check(rivers.TilesX == 360 && lakeRefused && layer.Reach == 16, $"the rivers' file reads as rivers, not as lakes; reach at 1:10 {layer.Reach} blocks (16)");
        }

        /// <summary>A tile of an outline file (LakeOutlineData's layout): pieces (level m, flags, rings of x, y units from
        /// the tile's south-west corner).</summary>
        static byte[] OutlineTile(params (double level, int flags, int[][] rings)[] pieces)
        {
            static void Var(List<byte> b, ulong v) { while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; } b.Add((byte)v); }
            static ulong Zig(long v) => (ulong)((v << 1) ^ (v >> 63));
            var b = new List<byte>();
            Var(b, (ulong)pieces.Length);
            foreach (var (level, flags, rings) in pieces)
            {
                Var(b, Zig((long)Math.Round(level * 10)));
                Var(b, (ulong)flags);
                Var(b, (ulong)rings.Length);
                long px = 0, py = 0;
                foreach (var r in rings)
                {
                    Var(b, (ulong)(r.Length / 2));
                    for (int i = 0; i < r.Length; i += 2) { Var(b, Zig(r[i] - px)); Var(b, Zig(r[i + 1] - py)); px = r[i]; py = r[i + 1]; }
                }
            }
            using var ms = new MemoryStream();
            using (var br = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) br.Write(b.ToArray());
            return ms.ToArray();
        }

        /// <summary>A whole outline file of 1° tiles (the others without data).</summary>
        static byte[] OutlineFile(string magic, int version, int unit, SortedDictionary<int, byte[]> tiles)
        {
            var states = new byte[360 * 180];
            Array.Fill(states, LakeOutlineData.NoData);
            foreach (var k in tiles.Keys) states[k] = LakeOutlineData.HasLakes;
            var file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes(magic));
            foreach (int v in new[] { version, unit, 1, 360, 180 }) file.AddRange(BitConverter.GetBytes(v));
            file.AddRange(states);
            int o = file.Count + 8 * tiles.Count;
            var body = new List<byte>();
            foreach (var t in tiles.Values) { file.AddRange(BitConverter.GetBytes(o + body.Count)); file.AddRange(BitConverter.GetBytes(t.Length)); body.AddRange(t); }
            file.AddRange(body);
            return file.ToArray();
        }

        /// <summary>
        /// River outlines at 1:100 (synthetic, 24,000 units a degree, a mask pixel 20). Riga: the Daugava crosses 57°N
        /// (24.0875..24.0959°E) from N56E024 into N57E024; just south of the edge it is N56E024's water, and the tile's edge
        /// is no shore. A narrow piece's end reaching 0.3 pixels into the wide water (0063 rule 2): version 3 counts the
        /// two as one water, measuring to the tributary's sides across the mouth, where version 2 has a shore along the
        /// narrow piece's end and the wide water's edge (the old one-block-deep line), and land beside it the same in
        /// both. And a cell whose centre lies in both: its wide water stays water in version 3.
        /// </summary>
        static void CheckRiverOutlineUnion()
        {
            Console.WriteLine("river outlines: Riga at 57°N, narrow pieces in wide water (synthetic)");
            static int Key(int lat0, int lon0) => (lat0 + 90) * 360 + lon0 + 180;
            byte[] Make(int version) => OutlineFile("EMRO", version, 24000, new SortedDictionary<int, byte[]>
            {
                [Key(56, 24)] = OutlineTile(
                    (0, 0, new[] { new[] { 2100, 22800, 2302, 22800, 2302, 24000, 2100, 24000 } }),
                    (0, RiverOutlineData.FlagNarrow, new[] { new[] { 2296, 23000, 2700, 23000, 2700, 23040, 2296, 23040 } }),
                    // the centre of index cell (10, 10) at 1:100 (336, 336 blocks: 7243.98 units) in both
                    (30, 0, new[] { new[] { 7000, 7100, 7250, 7100, 7250, 7400, 7000, 7400 } }),
                    (30, RiverOutlineData.FlagNarrow, new[] { new[] { 7240, 7230, 7600, 7230, 7600, 7260, 7240, 7260 } })),
                [Key(57, 24)] = OutlineTile((0, 0, new[] { new[] { 2100, 0, 2302, 0, 2302, 1200, 2100, 1200 } })),
            });
            var geo = new EarthGeometry { RingLength = 400_752, Gutter = 0, BlocksPerDegreeZ = 1113.2 };
            var v2 = RiverOutlineData.Layer(RiverOutlineData.Read(Make(2)), geo, false);
            var v3 = RiverOutlineData.Layer(RiverOutlineData.Read(Make(3)), geo, false);
            static double Lat(int y) => 56 + y / 24000.0;
            static double Lon(int x) => 24 + x / 24000.0;
            static double B(double units) => units / 24000 * 1113.2;
            static bool Near(double a, double b) => Math.Abs(a - b) < 0.02;
            static bool Same(double a, double b) => a == b || double.IsNaN(a) && double.IsNaN(b);
            Check(!v2.UnionShores && v3.UnionShores && v3.Reach == 4, $"version 3 counts overlapping pieces as one water, version 2 not; reach {v3.Reach} blocks (4)");
            foreach (var (layer, name) in new[] { (v2, "version 2"), (v3, "version 3") })
            {
                var s = layer.At(56.99264, 24.09122);
                var n = layer.At(57.0005, 24.0912);
                var e = layer.At(56.995, 24.0985);
                Check(s.Inside && s.Level == 0 && Near(s.Dist, 4) && n.Inside && Near(n.Dist, 4) && !e.Inside && Near(e.Dist, B(62)),
                    $"Riga, {name}: the Daugava just south of 57°N inside {s.Inside} ({s.Dist:0.00} blocks: the tile's edge is no shore), " +
                    $"north of it {n.Inside} ({n.Dist:0.00}), land east of it {e.Inside} ({e.Dist:0.00}, {B(62):0.00})");
            }
            foreach (var (x, what, old, now) in new[]
            {
                (2299, "in both pieces", B(3), B(Math.Sqrt(3 * 3 + 20 * 20))),
                (2280, "in the wide water off the mouth", B(16), B(Math.Sqrt(22 * 22 + 20 * 20))),
                (2310, "in the narrow piece by the wide water", B(8), B(20)),
            })
            {
                var a = v2.At(Lat(23020), Lon(x));
                var b = v3.At(Lat(23020), Lon(x));
                Check(a.Inside && b.Inside && Near(a.Dist, old) && Near(b.Dist, now),
                    $"junction, {what}: version 2 {a.Dist:0.00} blocks ({old:0.00}), version 3 {b.Dist:0.00} ({now:0.00})");
            }
            var l2 = v2.At(Lat(22990), Lon(2320));
            var l3 = v3.At(Lat(22990), Lon(2320));
            Check(!l3.Inside && Near(l3.Dist, B(10)) && l2.Inside == l3.Inside && l2.Dist == l3.Dist && Same(l2.Level, l3.Level) && Same(l2.HoldLevel, l3.HoldLevel),
                $"junction, land beside the mouth: {l3.Dist:0.00} blocks ({B(10):0.00}), hold {l3.HoldLevel}, the same in both");
            var c2 = v2.At(Lat(7300), Lon(7100));
            var c3 = v3.At(Lat(7300), Lon(7100));
            Check(c3.Inside && c3.Level == 30, $"a cell whose centre is in a wide and a narrow piece, in its wide water: version 3 inside {c3.Inside} (version 2 {c2.Inside})");

            // channel rules 1: two wide waters, a narrow channel between them off the courses (stays), a narrow canal
            // leaving one of them (goes) and a narrow river joining both along a course (goes: the course draws it)
            Console.WriteLine("river outlines: which narrow pieces channel rules 1 leave out (synthetic)");
            byte[] joins = OutlineFile("EMRO", 3, 24000, new SortedDictionary<int, byte[]>
            {
                [Key(50, 10)] = OutlineTile(
                    (0, 0, new[] { new[] { 0, 0, 2000, 0, 2000, 2000, 0, 2000 } }),
                    (0, 0, new[] { new[] { 4000, 0, 6000, 0, 6000, 2000, 4000, 2000 } }),
                    (0, RiverOutlineData.FlagNarrow, new[] { new[] { 1990, 1000, 4010, 1000, 4010, 1040, 1990, 1040 } }),
                    (0, RiverOutlineData.FlagNarrow, new[] { new[] { 1990, 1500, 3000, 1500, 3000, 1540, 1990, 1540 } }),
                    (0, RiverOutlineData.FlagNarrow, new[] { new[] { 1990, 500, 4010, 500, 4010, 540, 1990, 540 } })),
            });
            static bool Course(double lat, double lon) => lat > 50.0200 && lat < 50.0235;
            var keepAll = RiverOutlineData.Layer(RiverOutlineData.Read(joins), geo, false);
            var dropAll = RiverOutlineData.Layer(RiverOutlineData.Read(joins), geo, true);
            var rules1 = RiverOutlineData.Layer(RiverOutlineData.Read(joins), geo, true, Course);
            foreach (var (y, x, what, kept) in new[] { (1020, 3000, "the channel between them", true), (1520, 2500, "the canal", false), (520, 3000, "the river along a course", false) })
            {
                bool all = keepAll.At(50 + y / 24000.0, 10 + x / 24000.0).Inside, none = dropAll.At(50 + y / 24000.0, 10 + x / 24000.0).Inside;
                bool r1 = rules1.At(50 + y / 24000.0, 10 + x / 24000.0).Inside;
                Check(all && !none && r1 == kept, $"{what}: water with every piece {all}, with no narrow piece {none}, with channel rules 1 {r1} ({kept})");
            }
            // EA-0179 (decision 0098): coarse river rules (1:50 and coarser) keep a piece under 2 blocks wide only along a drawn
            // course (the river along it stays; the 1.8-block channel between the wide waters and the canal go, whatever their
            // flags); wider water stays wherever it is
            var only = RiverOutlineData.Layer(RiverOutlineData.Read(joins), geo, false, Course, null, true);
            Check(only.CourseOnlyAt != null && keepAll.CourseOnlyAt == null, "coarse river rules: the course-only rule; none without them");
            foreach (var (y, x, what, kept) in new[] { (1020, 3000, "the channel between them", false), (1520, 2500, "the canal", false), (520, 3000, "the river along a course", true), (1000, 1000, "the wide water", true) })
            {
                bool o = only.At(50 + y / 24000.0, 10 + x / 24000.0).Inside;
                Check(o == kept, $"coarse river rules: {what} {o} ({kept})");
            }
            static bool NoCourse(double lat, double lon) => false;
            var noCourse = RiverOutlineData.Layer(RiverOutlineData.Read(joins), geo, false, NoCourse, null, true);
            Check(noCourse.At(50 + 1000 / 24000.0, 10 + 1000 / 24000.0).Inside && !noCourse.At(50 + 520 / 24000.0, 10 + 3000 / 24000.0).Inside,
                "coarse river rules: with no drawn course, the wide water stays and the narrow river goes");
        }

        /// <summary>
        /// Geothermal rules 1 (decision 0057; the author: "Hot springs should be only in places with hot springs"). Geologic
        /// activity is heat under the GVP's Holocene volcanoes and never over 128, so vanilla makes no hot springs of its own
        /// (over 128 it would also flood every deep cave floor with lava); the hot springs are the known ones. Nothing at
        /// Riga, on the Russian plain or at long-dead Hohentwiel.
        /// </summary>
        static void CheckGeothermal()
        {
            static double Km(double lat1, double lon1, double lat2, double lon2)
                => Math.Sqrt(Math.Pow((lat1 - lat2) * 111.32, 2) + Math.Pow((lon1 - lon2) * 111.32 * Math.Cos(lat1 * Math.PI / 180), 2));
            static double NearestSpring(double lat, double lon)
                => Geothermal.SpringsBetween(lat - 0.1, lat + 0.1).Select(s => Km(lat, lon, s.Lat, s.Lon)).DefaultIfEmpty(999).Min();
            int riga = Geothermal.Activity(56.95, 24.1), plain = Geothermal.Activity(55.0, 45.0), hohentwiel = Geothermal.Activity(47.76667, 8.81667);
            int etna = Geothermal.Activity(37.748, 14.999), etna10 = Geothermal.Activity(37.748 + 10 / 111.32, 14.999);
            int kilauea = Geothermal.Activity(19.421, -155.287), eifel = Geothermal.Activity(50.17, 6.85);
            var volcanoes = Geothermal.VolcanoesBetween(-90, 90).ToList();
            var springs = Geothermal.SpringsBetween(-90, 90).ToList();
            int hottest = volcanoes.Concat(springs).Max(s => Geothermal.Activity(s.Lat, s.Lon));
            double strokkur = NearestSpring(64.3127, -20.3008), oldFaithful = NearestSpring(44.4605, -110.8281), hanmer = NearestSpring(-42.52276, 172.82872);
            Console.WriteLine($"geothermal: {Geothermal.VolcanoCount} volcanoes, {Geothermal.SpringCount} hot springs and geysers; Riga {riga}, Russian plain {plain}, " +
                $"Hohentwiel {hohentwiel}, Etna {etna} (10 km off {etna10}), Kilauea {kilauea}, West Eifel {eifel}, hottest site {hottest}; " +
                $"nearest spring to Strokkur {strokkur:0.00} km, Old Faithful {oldFaithful:0.00} km, Hanmer Springs {hanmer:0.00} km");
            Check(riga == 0 && plain == 0 && hohentwiel == 0, "geothermal: no activity at Riga, on the Russian plain or at Hohentwiel (no deep lava)");
            Check(etna == 128 && kilauea == 128 && eifel == 128 && etna10 > 0 && etna10 < etna, "geothermal: 128 under Holocene volcanoes (Etna, Kilauea, West Eifel), less away from them");
            Check(hottest <= 128 && Geothermal.Heat(Geothermal.SiteKind.HotSpring, 0) == 0,
                "geothermal: never over 128 anywhere, springs add none (no random hot springs, no lava under spa towns)");
            Check(volcanoes.Count > 1200 && volcanoes.Any(v => v.Name == "Kilauea") && springs.Count > 5000,
                "geothermal: the GVP's Holocene volcanoes and over 5000 known hot springs and geysers");
            Check(strokkur < 0.2 && oldFaithful < 0.2 && hanmer < 1, "geothermal: Strokkur, Old Faithful and Hanmer Springs are known springs");
        }

        /// <summary>
        /// Geothermal rules 2 (0057 part 2; the author: "proper volcanoes, with magma chamber etc."): the GVP's volcanic
        /// mountains get bodies, not its fields and minor vents; active in the game's time (some 100-200 years after 1318) is
        /// an eruption 1300-1700 or a busy volcano since; the eight lava lakes.
        /// </summary>
        static void CheckVolcanoes()
        {
            var bodies = Geothermal.Bodies;
            int active = bodies.Count(v => v.Active), lakes = bodies.Count(v => v.Lake != null);
            var etna = Geothermal.FindVolcano("Etna");
            var vesuvius = Geothermal.FindVolcano("Vesuvius");
            var shasta = Geothermal.FindVolcano("Shasta");
            var eifel = Geothermal.FindVolcano("West Eifel Volcanic Field");
            var kilauea = Geothermal.FindVolcano("Kilauea");
            var masaya = Geothermal.FindVolcano("Masaya");
            Console.WriteLine($"volcanoes: {bodies.Count} bodies, {active} active in the game's time, {lakes} lava lakes; Etna {etna.Form} active {etna.Active}, " +
                $"Vesuvius active {vesuvius.Active}, Shasta active {shasta.Active}, West Eifel body {eifel.HasBody}");
            Check(bodies.Count > 700 && bodies.Count < 1000 && lakes == 8 && Geothermal.LavaLakes.Length == 8,
                "volcanoes: the GVP's volcanic mountains get bodies (not fields or minor vents), all eight lava lakes among them");
            Check(etna.Active && etna.HasCrater && vesuvius.Active && !shasta.Active && shasta.HasBody && !shasta.HasCrater && !eifel.HasBody,
                "volcanoes: Etna and Vesuvius (1631) active in the game's time, Shasta (last 1250) dormant, West Eifel (a field) has no body");
            Check(kilauea.Lake != null && Math.Abs(kilauea.VentLat - 19.4069) < 1e-6 && masaya.Lake != null && masaya.VentLat == masaya.Lat,
                "volcanoes: Kilauea's lava lake at Halemaumau, Masaya's at the GVP's place");
        }

        /// <summary>Volcano bodies in the suite's world: every volcano planned, its chamber under rock and above the bottom,
        /// lava lakes below their rims and below every column next to them, no lava at a dormant vent; the same plan twice.</summary>
        static void CheckVolcanoPlans(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            var m = new TerrainModel(data, geo, vs, 12345) { GeothermalRules = Geothermal.RulesVersion };
            var sw = Stopwatch.StartNew();
            int bad = 0, craters = 0, lakes = 0, submarine = 0;
            string firstBad = null;
            foreach (var v in Geothermal.Bodies)
            {
                VolcanoPlan p;
                try { p = new VolcanoPlan(v, m); }
                catch (Exception e) { bad++; firstBad ??= v.Name + ": " + e.Message; continue; }
                string why = VolcanoPlanProblem(p, m);
                if (why != null) { bad++; firstBad ??= v.Name + ": " + why; }
                if (p.Crater) craters++;
                if (p.Lake) lakes++;
                if (p.Submarine) submarine++;
            }
            Console.WriteLine($"volcano plans: {Geothermal.Bodies.Count} in {sw.ElapsedMilliseconds} ms, {craters} craters, {lakes} lava lakes, {submarine} under the sea; " +
                $"{bad} broken{(firstBad != null ? " (first: " + firstBad + ")" : "")}");
            foreach (string name in new[] { "Etna", "Vesuvius", "Shasta", "Kilauea", "Nyiragongo", "Erebus", "Villarrica" })
            {
                var p = new VolcanoPlan(Geothermal.FindVolcano(name), m);
                Console.WriteLine($"  {name}: vent {p.VentX},{p.VentZ}, summit y {p.SummitY}, vent top {p.VentTop}, chamber y {p.ChamberY} (lava {p.LavaA:0} x {p.LavaB:0}), " +
                    $"conduit lava to {p.ConduitLavaTop}, rock to {p.ConduitTop}{(p.Crater ? $", crater rim {p.RimY} floor {p.FloorY} r {p.CraterR:0.0}" : "")}" +
                    $"{(p.Lake ? $", lava lake y {p.LakeY} floor {p.LakeFloorY} r {p.LakeR:0.0}" : "")}");
            }
            Check(bad == 0, "volcano plans: every chamber under rock, every lava lake below its rim and every column next to it, no lava in a dormant conduit");
            var a = new VolcanoPlan(Geothermal.FindVolcano("Nyiragongo"), m);
            var b = new VolcanoPlan(Geothermal.FindVolcano("Nyiragongo"), m);
            Check(a.VentX == b.VentX && a.VentZ == b.VentZ && a.LakeY == b.LakeY && a.ChamberY == b.ChamberY, "volcano plans: the same plan every time (every chunk agrees)");
            // the crater's edge, walked out from the vent at 72 angles: not a circle, and within its reach; hot ground (no
            // pond or spring) over the crater and its rim, not beyond
            var etna = new VolcanoPlan(Geothermal.FindVolcano("Etna"), m);
            double near = double.MaxValue, far = 0;
            for (int k = 0; k < 72; k++)
            {
                double ang = 2 * Math.PI * k / 72, r = 0;
                while (r < etna.CraterReach + 3 && etna.CraterSurface(etna.VentX + (int)Math.Round(r * Math.Cos(ang)), etna.VentZ + (int)Math.Round(r * Math.Sin(ang))) != int.MaxValue) r += 0.25;
                near = Math.Min(near, r);
                far = Math.Max(far, r);
            }
            bool hot = etna.InCrater(etna.VentX, etna.VentZ) && !etna.InCrater(etna.VentX + (int)Math.Ceiling(etna.CraterReach) + 4, etna.VentZ)
                && !a.InCrater(a.VentX, a.VentZ + (int)Math.Ceiling(a.CraterReach) + 4) && a.InCrater(a.VentX + 1, a.VentZ);
            Console.WriteLine($"  Etna's crater edge {near:0.0}-{far:0.0} blocks from the vent (radius {etna.CraterR:0.0}, reach {etna.CraterReach:0.0}); hot ground in and around the craters {hot}");
            Check(etna.Crater && far > near * 1.1 && far <= etna.CraterReach + 1 && hot, "volcano plans: crater edges stray from a circle; no ponds or springs in craters, only there");
            // EA-0071: maps read the surface as generation leaves it. Without a layer it is GetColumn's; with one, the crater's
            // floor in the crater (the rain height Crater writes there), the lava's top in a lake, GetColumn's beyond.
            var plainVent = m.GetSurfaceColumn(etna.VentX + 0.5, etna.VentZ + 0.5);
            bool plain = plainVent.Top == m.GetColumn(etna.VentX + 0.5, etna.VentZ + 0.5).Top;
            m.Carved.Craters = (x, z) => Math.Min(etna.SeenSurface(x, z), a.SeenSurface(x, z));
            int etnaFloor = m.GetSurfaceColumn(etna.VentX + 0.5, etna.VentZ + 0.5).Top, etnaTop = m.GetColumn(etna.VentX + 0.5, etna.VentZ + 0.5).Top;
            int lakeTop = m.GetSurfaceColumn(a.VentX + 0.5, a.VentZ + 0.5).Top;
            int ox = etna.VentX + (int)Math.Ceiling(etna.CraterReach) + 6;
            bool beyond = m.GetSurfaceColumn(ox + 0.5, etna.VentZ + 0.5).Top == m.GetColumn(ox + 0.5, etna.VentZ + 0.5).Top;
            m.Carved.Craters = null;
            Console.WriteLine($"  surface columns: Etna's vent {etnaTop} -> {etnaFloor} (floor {etna.FloorY}), Nyiragongo's lake top {lakeTop} (lava {a.LakeY}); beyond the crater unchanged {beyond}");
            Check(plain && etnaFloor == Math.Min(etnaTop, etna.FloorY) && lakeTop == Math.Min(m.GetColumn(a.VentX + 0.5, a.VentZ + 0.5).Top, a.LakeY) && beyond,
                "surface columns: GetSurfaceColumn carries the crater floor and the lava lake's top, and is GetColumn elsewhere (EA-0071)");
        }

        /// <summary>EA-0071 (decision 0079): the grass season a map reads at a column, as the client tints grass there.</summary>
        static void CheckGrassSeasonAt(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            var m = new TerrainModel(data, geo, vs, 12345);
            if (data.Weather == null) { Console.WriteLine("  grass season: no weather data loaded (skipped)"); return; }
            var curve = new float[GrassSeason.Steps];
            string Name(int k) => k >= 0 && k < GrassPhenology.Codes.Length ? GrassPhenology.Codes[k] : "none";
            (int Map, double Swing, double WetAt) At(double lat, double lon)
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                int k = m.GrassSeasonAt(x, z, curve);
                int wettest = 0;
                for (int i = 1; i < curve.Length; i++) if (curve[i] > curve[wettest]) wettest = i;
                return (k, curve.Max() - curve.Min(), (wettest + 0.5) / GrassSeason.Steps);
            }
            var latvia = At(57.10, 25.20);
            var amazon = At(-3.1, -60.0);
            var spain = At(39.5, -4.0);
            var kazakh = At(50.0, 70.0);
            var mumbai = At(19.1, 72.9);
            var sahara = At(23.0, 12.0);
            Console.WriteLine($"  grass season: Latvia {Name(latvia.Map)}, Amazonas {Name(amazon.Map)}, La Mancha {Name(spain.Map)}, Kazakh steppe {Name(kazakh.Map)}, " +
                $"Mumbai {Name(mumbai.Map)} (wetness swing {mumbai.Swing:0.00}, wettest at year {mumbai.WetAt:0.00}), Latvia's swing {latvia.Swing:0.00}");
            bool temperate = latvia.Map >= 1 && latvia.Map <= 4;
            bool dry = spain.Map == GrassPhenology.SummerDry || spain.Map == GrassPhenology.Steppe;
            bool monsoon = mumbai.Swing > 1.5 && mumbai.WetAt > 0.5 && mumbai.WetAt < 0.85;
            Check(temperate && amazon.Map == GrassPhenology.Evergreen && dry && kazakh.Map >= 1 && monsoon && latvia.Swing < mumbai.Swing && sahara.Map >= -1,
                "grass season: GrassSeasonAt gives each place's grass season map and its wet and dry seasons (EA-0071)");
        }

        static string VolcanoPlanProblem(VolcanoPlan p, TerrainModel m)
        {
            if (p.ChamberY + p.BodyB > p.VentTop - 6) return $"chamber top {p.ChamberY + p.BodyB:0} near the vent top {p.VentTop}";
            if (p.ChamberY - p.BodyB < 1) return $"chamber bottom {p.ChamberY - p.BodyB:0} below the world";
            if (!p.Lake && p.ConduitLavaTop >= p.VentTop) return "conduit lava at the surface";
            if (!p.Volcano.Active && p.ConduitLavaTop >= p.ChamberY) return "lava in a dormant conduit";
            if (p.Crater && p.FloorY > p.RimY) return $"crater floor {p.FloorY} over its rim {p.RimY}";
            if (!p.Lake) return null;
            if (p.LakeFloorY >= p.LakeY || p.LakeY >= p.RimY) return $"lava lake y {p.LakeY}, floor {p.LakeFloorY}, rim {p.RimY}";
            int box = (int)Math.Ceiling(p.LakeR + 2);
            for (int dz = -box; dz <= box; dz++)
            {
                for (int dx = -box; dx <= box; dx++)
                {
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    if (d <= p.LakeR || d > p.LakeR + 1.01) continue;
                    int top = Math.Min(m.GetColumn(p.VentX + dx, p.VentZ + dz).Top, p.CraterSurface(p.VentX + dx, p.VentZ + dz));
                    if (top <= p.LakeY) return $"lava spills at {dx},{dz} (top {top}, lava {p.LakeY})";
                }
            }
            return null;
        }

        /// <summary>The climate map's geologic byte: vanilla's in older worlds, Earth's from geothermal rules 1.</summary>
        static void CheckGeothermalPixel(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            var old = new TerrainModel(data, geo, vs, 12345);
            var now = new TerrainModel(data, geo, vs, 12345) { GeothermalRules = Geothermal.RulesVersion };
            geo.FromGeo(56.95, 24.1, out double rx, out double rz);
            geo.FromGeo(37.748, 14.999, out double ex, out double ez);
            int oldRiga = old.ClimatePixel(rx, rz, 200, 1, 1) & 255, nowRiga = now.ClimatePixel(rx, rz, 200, 1, 1) & 255;
            int nowEtna = now.ClimatePixel(ex, ez, 0, 1, 1) & 255;
            bool restSame = (old.ClimatePixel(rx, rz, 200, 1, 1) >> 8) == (now.ClimatePixel(rx, rz, 200, 1, 1) >> 8);
            Console.WriteLine($"geothermal pixel: Riga vanilla 200 -> {oldRiga} (rules 0), {nowRiga} (rules 1); Etna {nowEtna}");
            Check(oldRiga == 200 && nowRiga == 0 && nowEtna == 128 && restSame,
                "geothermal pixel: older worlds keep vanilla's byte; rules 1 write Earth's, temperature and rain unchanged");
        }

        // EA-0108 (the Fable pass's FP-18): rivers.bin is read only when asked for (and after a release that came first, read
        // anew); the islands and coasts as read, dropped once the sea referee's are built, come back the same for a view
        // without the referee, and its terrain samples are the same as before
        static void CheckStartMemory(string root)
        {
            var d = EarthData.LoadFromFolder(root, true);
            d.SetIslandOverlay(true);
            d.SetSeaReferee(false);
            var before = d.Islands;
            var beforeCoasts = d.Coasts;
            var places = new List<(double Lat, double Lon)>();
            for (double lat = -60; lat <= 75; lat += 0.37) for (double lon = -180; lon < 180; lon += 1.13) places.Add((lat, lon));
            var s0 = places.Select(q => d.SampleTerrain(q.Lat, q.Lon, true)).ToList();
            d.SetSeaReferee(true);
            bool refereeOn = d.SeaRefereeOverlay;
            var refereeIslands = d.Islands;
            d.SetSeaReferee(false);
            var again = d.Islands;
            var s1 = places.Select(q => d.SampleTerrain(q.Lat, q.Lon, true)).ToList();
            bool sameIslands = again.Islands.Length == before.Islands.Length && again.Patches.SequenceEqual(before.Patches);
            for (int i = 0; sameIslands && i < again.Islands.Length; i++)
                sameIslands = again.Islands[i].Lon.AsSpan().SequenceEqual(before.Islands[i].Lon) && again.Islands[i].Lat.AsSpan().SequenceEqual(before.Islands[i].Lat)
                    && again.Islands[i].AreaKm2 == before.Islands[i].AreaKm2 && again.Islands[i].Height == before.Islands[i].Height;
            bool sameSamples = true;
            for (int i = 0; sameSamples && i < s0.Count; i++)
                sameSamples = s0[i].Elev.Equals(s1[i].Elev) && s0[i].Ocean.Equals(s1[i].Ocean) && s0[i].Lake.Equals(s1[i].Lake);
            Check(refereeOn && !ReferenceEquals(refereeIslands, before) && !ReferenceEquals(again, before) && !ReferenceEquals(d.Coasts, beforeCoasts)
                && sameIslands && sameSamples,
                $"EA-0108: the islands and coasts as read, dropped for the referee's, are read again the same for a view without it " +
                $"({again.Islands.Length} islands, {again.Patches.Length} patches; {places.Count} terrain samples the same)");

            // rivers.bin: read at the first ask, released, read anew; and released before any ask, read anew too
            var reaches = d.Rivers;
            int count = reaches?.Count ?? -1;
            d.ReleaseRivers();
            bool released = d.Rivers == null;
            var anew = d.RiverReaches();
            var d2 = EarthData.LoadFromFolder(root, true);
            d2.ReleaseRivers();
            var neverAsked = d2.RiverReaches();
            Check(count > 600_000 && released && anew != null && anew.Count == count && !ReferenceEquals(anew, reaches) && neverAsked?.Count == count && d2.Rivers == null,
                $"EA-0108: rivers.bin read when asked for ({count} reaches), released, read anew ({anew?.Count}); released before any ask, read anew ({neverAsked?.Count})");
        }

        static int Main(string[] args)
        {
            CheckLakeOutlines();
            CheckWorks1318();
            CheckOutlineCourse();
            CheckRiverOutlineUnion();
            CheckGeothermal();
            CheckVolcanoes();
            if (args.Length > 0 && args[0] == "lakes") return failures;
            string root = args.Length > 0 ? args[0] : FindRoot();
            int mapHeight = args.Length > 1 ? int.Parse(args[1]) : 256;
            string outDir = Path.Combine(root, "tests", "out");
            Directory.CreateDirectory(outDir);

            var sw = Stopwatch.StartNew();
            CheckStartMemory(root);
            sw.Restart();
            var data = EarthData.LoadFromFolder(root, true);
            Console.WriteLine($"data loaded in {sw.ElapsedMilliseconds} ms");
            // the water mask's referee of the sea as a new world has it (0053 item 14); SEAREFEREE=0: the published terrain,
            // islands and coasts, as worlds from before tall.14 have them
            data.SetSeaReferee(Environment.GetEnvironmentVariable("SEAREFEREE") != "0");
            Console.WriteLine($"sea referee {(data.SeaRefereeOverlay ? "on" : "off")} (sea-referee.bin {(data.SeaReferee != null ? "fits " + (data.SeaRefereeAvailable > 0) : "missing")})");

            // Cylinder World 256000 x 102400: jump inset 25600, ring 204800, walls 48 deep (WORLD=1024000: the Earth tab's other
            // sizes, length 0.4 and ring 0.8 of the width)
            int W = int.TryParse(Environment.GetEnvironmentVariable("WORLD"), out int ww) ? ww : 256000, L = W * 2 / 5;
            var inp = new LayoutInput
            {
                WorldWidth = W,
                CwRingStart = W / 10, CwRingLength = W * 4 / 5, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48,
                CwBandInner = int.Parse(Environment.GetEnvironmentVariable("BANDINNER") ?? "608"), // Cylinder World's seam half width minus its jump inset
                // NOGUTTER=1: the layout of new worlds (no seam gutter, decision 0030); with GUTTER=3 the real seam
                Gutter = Environment.GetEnvironmentVariable("NOGUTTER") != "1",
            };
            var geo = EarthLayout.Build(inp, data, out string report);
            Console.WriteLine(report);
            int sea = (int)(0.4313725490196078 * mapHeight);
            var vs = VerticalScale.ForWorld(mapHeight, sea);
            // river ends as in a new world (terminal lakes, rivers continued, led on or faded) unless NOENDS=1
            bool riverEnds = Environment.GetEnvironmentVariable("NOENDS") != "1";
            sw.Restart();
            var model = BuildTestModel(data, geo, vs, sea, riverEnds, out var ends);
            if (model.Rivers != null) Console.WriteLine($"rivers: {model.Rivers.SegmentCount} segments indexed in {sw.ElapsedMilliseconds} ms");

            // rapids mode: each site's reach, its valley fall, discharge and current strength, and the rapids decisions of pool
            // layouts 8-10 and 11 (decision 0046)
            if (args.Length > 2 && args[2] == "rapids")
            {
                RapidsSites(model, geo);
                return 0;
            }

            // realseam mode: the real seam checks alone (decision 0030)
            if (args.Length > 2 && args[2] == "realseam")
            {
                CheckRealSeam(data, inp, vs, sea, riverEnds);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }

            // lakes mode: the terminal lakes and river ends, with renders before/after
            if (args.Length > 2 && args[2] == "lakes")
            {
                CheckRiverEnds(model, ends);
                CheckSeaChannels(model);
                CheckSeaDepth(model);
                RenderEnds(data, geo, vs, sea, outDir);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }

            if (args.Length > 4 && args[2] == "shoredebug")
            {
                double la = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), lo = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                geo.FromGeo(la, lo, out double cx, out double cz);
                foreach (bool eased in new[] { false, true })
                {
                    data.SetShoreOverlay(eased);
                    var m = new TerrainModel(data, geo, vs, 12345) { LakeShores = data.ShoreOverlay };
                    Console.WriteLine(eased ? "eased" : "old");
                    for (int j = -12; j <= 12; j++)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int i = -16; i <= 16; i++) { var c = m.GetColumn(cx + i, cz + j); var g2 = geo.ToGeo(cx + i, cz + j); var st = data.SampleTerrain(g2.Lat, g2.Lon); sb.Append(c.Water != TerrainModel.WaterNone ? "    ~" : $" {c.Top,3}{(st.Lake > 0 ? "L" : st.ShoreLevel > 0 ? "s" : ".")}{(int)Math.Round(st.LakeNear * 9)}"); }
                        Console.WriteLine(sb);
                    }
                    var s0 = data.SampleTerrain(la, lo); Console.WriteLine($"elev {s0.Elev:0} shore {s0.ShoreLevel} lake {s0.Lake:0.00} ocean {s0.Ocean:0.00} near {s0.LakeNear:0.00} overlay {data.ShoreOverlay} nearCells {data.Shores?.NearCells.Length} first {(data.Shores?.NearCells.Length > 0 ? data.Shores.NearCells[0] + ":" + data.Shores.Near[0] : "")}");
                }
                return 0;
            }
            // cost: what the climate hook's lookups and the terrain column cost per call (EA-0070), at a 1:100 world's places
            if (args.Length > 2 && args[2] == "cost")
            {
                model.ClimateRules = TerrainModel.RealRainRules;
                // (ER-11, P-7: also the open sea, a river mouth and Rotterdam's sea of 1318, whose columns take other steps)
                var places = new[] { ("Zermatt", 45.98, 7.65), ("Riga", 56.95, 24.1), ("Amazon", -3.1, -60.0), ("Sahara", 23.0, 10.0), ("Siberia", 62.0, 129.7),
                    ("Open sea", 0.02, -30.02), ("Daugava mouth", 57.05, 24.03), ("Rotterdam", 51.95, 4.1) };
                double Time(string what, int n, Func<int, double> f)
                {
                    double sink = 0;
                    for (int i = 0; i < Math.Min(n, 20000); i++) sink += f(i);
                    long bytes = GC.GetAllocatedBytesForCurrentThread();
                    var t = Stopwatch.StartNew();
                    for (int i = 0; i < n; i++) sink += f(i);
                    double us = t.Elapsed.TotalMilliseconds * 1000 / n;
                    double alloc = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)n;
                    Console.WriteLine($"  {what,-52} {us,8:0.000} us/call {alloc,8:0.0} B/call");
                    return sink;
                }
                var wm = data.Weather == null ? null : new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = 12, HoursPerDay = 24, Rules = 6 }, 12345);
                double total = 0;
                foreach (var (name, la, lo) in places)
                {
                    geo.FromGeo(la, lo, out double px, out double pz);
                    Console.WriteLine($"{name}:");
                    // the game asks the climate for nearby blocks in turn (block ticks, snow, spawning): a 64 x 64 patch, cycled
                    double X(int i) => px + (i & 63);
                    double Z(int i) => pz + ((i >> 6) & 63);
                    total += Time("ClimateCorrection (rules 3, as the hook)", 400000, i => model.ClimateCorrection(X(i), Z(i), 120));
                    total += Time("SampleClimate at ToGeo (season correction)", 400000, i => { var g = geo.ToGeo(X(i), Z(i)); return data.SampleClimate(g.Lat, g.Lon).TMean; });
                    total += Time("TrueTemperature", 400000, i => model.TrueTemperature(X(i), Z(i), 120));
                    total += Time("StoredTemperature (rules 1 worlds' path)", 400000, i => model.StoredTemperature(X(i), Z(i), out _));
                    total += Time("ClimatePixel (world generation, per pixel)", 40000, i => model.ClimatePixel(X(i), Z(i), 0, 1, 1));
                    total += Time("GetColumn (terrain, per block column)", 40000, i => model.GetColumn(X(i * 7), Z(i * 7)).Top);
                    if (wm != null)
                    {
                        total += Time("Weather TemperatureAnomaly (rules 6, as the hook)", 100000, i => wm.TemperatureAnomaly(X(i), Z(i), 5000 + i * 1e-4, 1600));
                        total += Time("  same, one block and hour (memo upper bound)", 100000, i => wm.TemperatureAnomaly(px, pz, 5000, 1600));
                    }
                }
                // (ER-14, EA-0078, EA-0086) the weather as the game asks it, rules 6 to the newest: one call each, and what one
                // frame, one GetWindSpeedAt and one snow catch-up cost on the model (the client caches and the skipped wind
                // handler are outside the model: the composites show what they avoid). "weather" alone runs only this part.
                if (data.Weather != null)
                {
                    int regionSize = 128;
                    foreach (int rules in new[] { 6, 7, WeatherSettings.Newest })
                    {
                        var w = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = 12, HoursPerDay = 24, Rules = rules }, 12345) { Ground = data.WeatherGround };
                        Console.WriteLine($"weather rules {rules}:");
                        foreach (var (name, la, lo) in new[] { ("Riga", 56.95, 24.1), ("Po plain", 45.1, 10.0), ("Kansas", 39.1, -94.6) })
                        {
                            geo.FromGeo(la, lo, out double px, out double pz);
                            double X(int i) => px + (i & 63);
                            double Z(int i) => pz + ((i >> 6) & 63);
                            double H(int i) => 5000 + i * (1 / 3600.0);   // the clock a game second a call
                            Console.WriteLine($" {name}:");
                            total += Time("Cloudness (the rain postfix, block centre)", 100000, i => w.Cloudness(X(i) + 0.5, Z(i) + 0.5, H(i)));
                            total += Time("TemperatureAnomaly (the climate hook)", 100000, i => w.TemperatureAnomaly(X(i), Z(i), H(i), 300));
                            total += Time("climate reading: Cloudness + anomaly", 100000, i => w.Cloudness(X(i) + 0.5, Z(i) + 0.5, H(i)) + w.TemperatureAnomaly(X(i), Z(i), H(i), 300));
                            total += Time("WindAt with gusts (Earth's wind handler)", 100000, i => w.WindAt(X(i), Z(i), H(i), true).X);
                            total += Time("Storms.Influence", 100000, i => w.Storms.Influence(la, lo, H(i)).Presence);
                            total += Time("CloudCover (one cloud tile)", 40000, i => w.CloudCover(X(i * 37), Z(i * 37), H(i)));
                            total += Time("FogAt (one weather region)", 40000, i => w.FogAt(X(i), Z(i), H(i), 300).Ground);
                            total += Time("HailAt", 100000, i => w.HailAt(X(i), Z(i), H(i), 300));
                            // a frame on the client: four regions' fog (rules 7+), four hail lookups at the player (rules 6+)
                            total += Time("frame: 4 x FogAt + 4 x HailAt at the player", 10000, i =>
                            {
                                double s = 0, h = H(i);
                                for (int k = 0; k < 4; k++) s += w.FogAt(px + (k & 1) * regionSize, pz + (k >> 1) * regionSize, h, 300).Ground + w.HailAt(px, pz, h, 300);
                                return s;
                            });
                            // GetWindSpeedAt as vanilla chains it: its handler (4 region winds, a climate reading) then Earth's
                            total += Time("GetWindSpeedAt: vanilla's handler + Earth's", 20000, i =>
                            {
                                double s = 0, h = H(i);
                                for (int k = 0; k < 4; k++) s += w.WindAt(px + (k & 1) * regionSize, pz + (k >> 1) * regionSize, h, false).X;
                                s += w.Cloudness(X(i) + 0.5, Z(i) + 0.5, h) + w.TemperatureAnomaly(X(i), Z(i), h, 300);
                                return s + w.WindAt(X(i), Z(i), h, true).X;
                            });
                            // the snow catch-up: 8 corners of a region, 480 hours each (corners outside, hours inside)
                            total += Time("snow catch-up: 8 corners x 480 hours (per region)", 3, i =>
                            {
                                double s = 0;
                                for (int c = 0; c < 8; c++)
                                    for (int k = 0; k < 480; k++)
                                    {
                                        double cx = px + (c & 1) * regionSize + i * 4 * regionSize, cz = pz + ((c >> 1) & 3) * 42, h = 4000 + k;
                                        s += w.Cloudness(cx + 0.5, cz + 0.5, h) + w.TemperatureAnomaly(cx, cz, h, 300);
                                    }
                                return s;
                            });
                        }
                    }
                }
                Console.WriteLine(total == 0 ? "" : "done");
                return 0;
            }
            // mouthedge lat lon [half]: the test world's model with its rivers, as a new world has the lake shores (GenFixRules
            // newest): ~ lake, r river water, b beach (flush), digits the dry top's last digit (EA-0072's mouth)
            if (args.Length > 4 && args[2] == "mouthedge")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double la = double.Parse(args[3], ci), lo = double.Parse(args[4], ci);
                int half = args.Length > 5 ? int.Parse(args[5]) : 40;
                geo.FromGeo(la, lo, out double cx, out double cz);
                foreach (int gen in new[] { 2, TerrainModel.GenFixRulesVersion })
                {
                    model.GenFixRules = gen;
                    Console.WriteLine($"GenFixRules {gen} (rivers)");
                    for (int j = -half; j <= half; j++)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int i = -half; i <= half; i++)
                        {
                            var c = model.GetColumn(cx + i, cz + j);
                            sb.Append(c.Water != TerrainModel.WaterNone ? (c.River ? 'r' : '~') : c.Beach ? 'b' : (char)('0' + ((c.Top % 10) + 10) % 10));
                        }
                        Console.WriteLine(sb);
                    }
                }
                return 0;
            }
            // lakeedge lat lon [half]: the grid lakes' water edge around a place on the built-in heights, eased shores as a new
            // world has them, GenFixRules 2 and the newest side by side (EA-0072): ~ water, digits the dry top's last digit
            if (args.Length > 4 && args[2] == "lakeedge")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double la = double.Parse(args[3], ci), lo = double.Parse(args[4], ci);
                int half = args.Length > 5 ? int.Parse(args[5]) : 40;
                geo.FromGeo(la, lo, out double cx, out double cz);
                data.SetShoreOverlay(true);
                foreach (int gen in new[] { 2, TerrainModel.GenFixRulesVersion })
                {
                    var m = new TerrainModel(data, geo, vs, 12345) { LakeShores = data.ShoreOverlay, GenFixRules = gen };
                    Console.WriteLine($"GenFixRules {gen}");
                    for (int j = -half; j <= half; j += 1)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int i = -half; i <= half; i++)
                        {
                            var c = m.GetColumn(cx + i, cz + j);
                            sb.Append(c.Water != TerrainModel.WaterNone ? '~' : c.Beach ? 'b' : (char)('0' + ((c.Top % 10) + 10) % 10));
                        }
                        Console.WriteLine(sb);
                    }
                }
                if (Environment.GetEnvironmentVariable("EDGEROW") is string dbr && dbr.Length > 0)
                {
                    int dj = int.Parse(dbr);
                    var m3 = new TerrainModel(data, geo, vs, 12345) { LakeShores = data.ShoreOverlay, GenFixRules = TerrainModel.GenFixRulesVersion };
                    for (int i = -half; i <= half; i++)
                    {
                        var g = geo.ToGeo(cx + i, cz + dj);
                        var st = data.SampleTerrain(g.Lat, g.Lon, true);
                        var c3 = m3.GetColumn(cx + i, cz + dj);
                        Console.WriteLine($"i {i,4} elev {st.Elev:0.0} lake {st.Lake:0.00} level {st.LakeLevel} relief {st.Relief:0} col {(c3.Water != 0 ? "water" : "dry")} top {c3.Top} elevh {c3.Elev:0.0}");
                    }
                }
                if (Environment.GetEnvironmentVariable("EDGEDEBUG") is string dbg && dbg.Length > 0)
                {
                    int di = int.Parse(dbg);
                    for (int j = -half; j <= half; j++)
                    {
                        var g = geo.ToGeo(cx + di, cz + j);
                        var st = data.SampleTerrain(g.Lat, g.Lon, true);
                        Console.WriteLine($"j {j,4} lat {g.Lat:0.0000} elev {st.Elev:0.0} lake {st.Lake:0.00} level {st.LakeLevel} ocean {st.Ocean:0.00} shore {st.ShoreLevel} near {st.LakeNear:0.00}");
                    }
                }
                return 0;
            }
            // shores mode: the lake shore checks only
            if (args.Length > 2 && args[2] == "shores")
            {
                CheckLakeShores(data, geo, vs);
            CheckGridLakeEdge(data, geo, vs);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }

            // islands mode: the island checks with before/after renders of the Bahamas at three world sizes
            // widths mode: river widths by discharge and ring size, old and new width rules
            if (args.Length > 2 && args[2] == "widths")
            {
                CheckRiverWidths(data);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }
            // ringrender lat lon ring width height step [name]: a new world's terrain at any ring size (WIDTHS=0: old width rules)
            if (args.Length > 8 && args[2] == "ringrender")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double ring = double.Parse(args[5], ci);
                var rm = RingModel(data, vs, ring, Environment.GetEnvironmentVariable("WIDTHS") != "0" ? RiverNetwork.WidthRulesVersion : 0, out var rgeo);
                RenderAt(rm, rgeo, outDir, args.Length > 9 ? args[9] : $"ring{ring / 1000:0}k-{args[3]}_{args[4]}", double.Parse(args[3], ci), double.Parse(args[4], ci),
                    int.Parse(args[6]), int.Parse(args[7]), double.Parse(args[8], ci));
                return 0;
            }
            // ringcanals ring: the canal checks in a new world at any ring size (with RINGCANALS, both canals by default)
            if (args.Length > 3 && args[2] == "ringcanals")
            {
                Environment.SetEnvironmentVariable("RINGCANALS", Environment.GetEnvironmentVariable("RINGCANALS") ?? "suez,panama");
                var rm = RingModel(data, vs, double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), RiverNetwork.WidthRulesVersion, out _);
                CheckCanals(rm);
                // PROBE=lat,lon;lat,lon: columns to print
                foreach (var spot in (Environment.GetEnvironmentVariable("PROBE") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var ll = spot.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    rm.Geometry.FromGeo(ll[0], ll[1], out double px, out double pz);
                    var c = rm.GetColumn(px, pz);
                    Console.WriteLine($"  probe {spot}: water {c.Water} wtop {c.WaterTop - rm.SeaLevel + 1:+0;-0} top {c.Top - rm.SeaLevel + 1:+0;-0} river {c.River} elev {c.Elev:0.0}");
                }
                // water beside water with another surface, around each canal: where and what
                foreach (var ch in rm.Channels.Channels.Where(c => c.Canal && !c.Lake))
                {
                    double minX = ch.X.Min() - 400, maxX = ch.X.Max() + 400, minZ = ch.Z.Min() - 400, maxZ = ch.Z.Max() + 400;
                    var kinds = new Dictionary<string, int>();
                    var where = new Dictionary<string, string>();
                    for (double z = minZ; z <= maxZ; z += 1)
                        for (double x = minX; x <= maxX; x += 1)
                        {
                            var c = rm.GetColumn(x, z);
                            if (c.Water == TerrainModel.WaterNone) continue;
                            foreach (var (nx, nz) in new[] { (1, 0), (0, 1) })
                            {
                                var d = rm.GetColumn(x + nx, z + nz);
                                if (d.Water == TerrainModel.WaterNone || d.WaterTop == c.WaterTop) continue;
                                var (hi, lo) = c.WaterTop > d.WaterTop ? (c, d) : (d, c);
                                if (hi.River && lo.River && hi.WaterTop - lo.WaterTop == 1) continue;
                                string Kind(TerrainModel.Column k) => k.River ? (k.Ramp > 0 ? "ramp" : "river") : k.Water == TerrainModel.WaterSea ? "sea" : "lake";
                                string key = $"{Kind(hi)} {hi.WaterTop - rm.SeaLevel + 1:+0;-0} over {Kind(lo)} {lo.WaterTop - rm.SeaLevel + 1:+0;-0}";
                                kinds[key] = kinds.GetValueOrDefault(key) + 1;
                                if (!where.ContainsKey(key)) { var g = rm.Geometry.ToGeo(x, z); where[key] = $"{g.Lat:0.0000},{g.Lon:0.0000}"; }
                            }
                        }
                    Console.WriteLine($"  {ch.Name}: water beside water at another level (surface vs sea level):");
                    foreach (var kv in kinds.OrderByDescending(k => k.Value)) Console.WriteLine($"    {kv.Value,6}  {kv.Key}  e.g. {where[kv.Key]}");
                }
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }
            // estuary mode: islands in an estuary stay whole (island rules v3) and the delta branches (RiverDeltas)
            if (args.Length > 2 && args[2] == "estuary")
            {
                CheckEstuaryAndDeltas(data, geo, vs, sea);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }
            if (args.Length > 2 && args[2] == "islands")
            {
                CheckIslands(data, vs, outDir, true);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }

            // coasts mode: the outline coast checks with before/after renders of a fjord, a delta and a rocky coast at 512k and 4M
            if (args.Length > 2 && args[2] == "coasts")
            {
                CheckCoasts(data, vs, outDir, true);
                Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
                return failures == 0 ? 0 : 1;
            }
            // coastprobe lat lon ring: the outline's signed distance just around a place (tile edges)
            if (args.Length > 5 && args[2] == "coastprobe")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double pla = double.Parse(args[3], ci), plo = double.Parse(args[4], ci), pring = double.Parse(args[5], ci);
                var pinp = new LayoutInput
                {
                    WorldWidth = (int)(pring * 1.25),
                    CwRingStart = pring / 8, CwRingLength = pring, CwEquatorZ = pring / 4, CwPoleDistance = pring / 4 - 48, CwBandInner = 608,
                };
                var pgeo = EarthLayout.Build(pinp, data, out _);
                var layer = new CoastLayer(data.Coasts, pgeo, 4);
                for (int j = -4; j <= 4; j++)
                {
                    var line = new System.Text.StringBuilder($"{pla + j * 0.01,8:0.000}:");
                    for (int i = -6; i <= 6; i++) line.Append($" {layer.SignedDistance(pla + j * 0.01, plo + i * (args.Length > 6 ? double.Parse(args[6], ci) : 0.002)),6:0.0}");
                    Console.WriteLine(line);
                }
                data.SetIslandOverlay(true);
                var pm = CoastModel(data, pgeo, vs, null, true);
                pgeo.FromGeo(pla, plo, out double px0, out double pz0);
                for (int i = -4; i <= 4; i++)
                {
                    var c = pm.GetColumn(px0 + i * 0.5, pz0);
                    var g = pgeo.ToGeo(px0 + i * 0.5, pz0);
                    var st = data.SampleTerrain(g.Lat, g.Lon, true);
                    Console.WriteLine($"  x {px0 + i * 0.5:0.0} lon {g.Lon:0.000000}: water {c.Water} top {c.Top} river {c.River} elev {c.Elev:0.0} d {layer.SignedDistance(g.Lat, g.Lon):0.0} " +
                        $"ocean {st.Ocean:0.00} seaNear {st.SeaNear:0.00} land {st.LandElev:0.0} sea {st.SeaElev:0.0} elev {st.Elev:0.0}");
                }
                return 0;
            }
            // coastrender lat lon ring width height step: before/after renders of any place (tests/out/coastrender-*.png)
            if (args.Length > 8 && args[2] == "coastrender")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                RenderCoastPlace(data, vs, outDir, double.Parse(args[3], ci), double.Parse(args[4], ci), double.Parse(args[5], ci),
                    int.Parse(args[6]), int.Parse(args[7]), double.Parse(args[8], ci));
                return 0;
            }

            // render lat lon width height step [name]: the new-world test model around a place (tests/out/render-*.png)
            if (args.Length > 7 && args[2] == "render")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string rname = args.Length > 8 ? args[8] : $"render-{args[3]}_{args[4]}";
                if (Environment.GetEnvironmentVariable("ISLANDS") != "0")
                {
                    // as a new world: the island overlay first (the river exit channels read the grid with it), then the outlines
                    data.SetIslandOverlay(true);
                    model = BuildTestModel(data, geo, vs, sea, riverEnds, out _);
                    model.Islands = new IslandLayer(data.Islands, geo, 4, int.TryParse(Environment.GetEnvironmentVariable("ISLANDRULES"), out int irv) ? irv : IslandLayer.RulesVersion);
                }
                // GUTTER=n: the seam as a world saving GutterSeabed n has it (with the Antarctic bridge); COASTS=1: outline coasts
                if (int.TryParse(Environment.GetEnvironmentVariable("GUTTER"), out int gutterSeabed)) { model.GutterSeabed = gutterSeabed; model.GutterBridges = true; }
                if (Environment.GetEnvironmentVariable("COASTS") == "1") model.Coasts = new CoastLayer(data.Coasts, geo, 4);
                double rlat = double.Parse(args[3], ci), rlon = double.Parse(args[4], ci), rstep = double.Parse(args[7], ci);
                int rw = int.Parse(args[5]), rh = int.Parse(args[6]);
                RenderAt(model, geo, outDir, rname, rlat, rlon, rw, rh, rstep);
                if (Environment.GetEnvironmentVariable("DUMP") == "1")
                {
                    double hlon = rw * rstep / 2 / geo.BlocksPerDegreeX, hlat = rh * rstep / 2 / geo.BlocksPerDegreeZ;
                    foreach (var isl in data.Islands.Islands)
                        if (isl.MaxLon >= rlon - hlon && isl.MinLon <= rlon + hlon && isl.MaxLat >= rlat - hlat && isl.MinLat <= rlat + hlat)
                        {
                            Console.WriteLine($"  island {isl.AreaKm2:0.00} km² width {isl.WidthKm:0.00} km, {isl.Lon.Length} points, lon {isl.MinLon:0.0000}..{isl.MaxLon:0.0000} lat {isl.MinLat:0.0000}..{isl.MaxLat:0.0000}");
                            if (Environment.GetEnvironmentVariable("POINTS") == "1") Console.WriteLine("    " + string.Join(" ", isl.Lon.Select((v, k) => $"{v:0.0000},{isl.Lat[k]:0.0000}")));
                        }
                    // a character map: ~ sea, r river, l lake, b beach, I island land, . land
                    geo.FromGeo(rlat, rlon, out double mx, out double mz);
                    double cs = Math.Max(1, rw * rstep / 120);
                    for (double zz = mz - rh * rstep / 2; zz < mz + rh * rstep / 2; zz += cs * 2)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (double xx = mx - rw * rstep / 2; xx < mx + rw * rstep / 2; xx += cs)
                        {
                            var c = model.GetColumn(xx, zz);
                            bool il = model.Islands != null && model.Islands.IsLand(c.Lat, c.Lon);
                            sb.Append(c.Water == TerrainModel.WaterSea ? '~' : c.River ? 'r' : c.Water != TerrainModel.WaterNone ? 'l' : il ? 'I' : c.Beach ? 'b' : '.');
                        }
                        Console.WriteLine("  " + sb);
                    }
                    if (Environment.GetEnvironmentVariable("ACROSS") is string across)
                    {
                        double alat = double.Parse(across, ci);
                        for (double alon = rlon - 0.012; alon <= rlon + 0.012; alon += 0.001)
                        {
                            geo.FromGeo(alat, alon, out double qx, out double qz);
                            var c = model.GetColumn(qx, qz);
                            bool q = model.Islands.Query(c.Lat, c.Lon, out var ih);
                            var nat = model.Islands; model.Islands = null; var c0 = model.GetColumn(qx, qz); model.Islands = nat;
                            var ts = data.SampleTerrain(c.Lat, c.Lon, true);
                            Console.WriteLine($"  lon {alon:0.000} x {qx:0.0}: water {c.Water} river {c.River} top {c.Top} wtop {c.WaterTop} | island q {q} in {ih.Inside} d {ih.Dist:0.0} | no-isl water {c0.Water} river {c0.River} top {c0.Top} | ocean {ts.Ocean:0.00} elev {ts.Elev:0.0} | c.Elev {c.Elev:0.0}" +
                                (model.Rivers.Nearest(qx, qz, out var rhit) ? $" | river d {rhit.Dist:0.0} hw {model.Rivers.HalfWidth(rhit.Q):0.0} bank {model.Rivers.Bank(rhit.Q):0.0} Q {rhit.Q:0} mouth {rhit.Mouth} wt {rhit.WaterTop}" : " | no river"));
                        }
                    }
                    // the grid's ocean share (0-9) and lake (L) at the same spots
                    Console.WriteLine("  ocean share:");
                    for (double zz = mz - rh * rstep / 2; zz < mz + rh * rstep / 2; zz += cs * 2)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (double xx = mx - rw * rstep / 2; xx < mx + rw * rstep / 2; xx += cs)
                        {
                            var g = geo.ToGeo(xx, zz);
                            var ts = data.SampleTerrain(g.Lat, g.Lon, true);
                            sb.Append(ts.Lake > 0 && !double.IsNaN(ts.LakeLevel) ? 'L' : (char)('0' + Math.Clamp((int)(ts.Ocean * 9.999), 0, 9)));
                        }
                        Console.WriteLine("  " + sb);
                    }
                }
                return 0;
            }

            // mouths lat0 lat1 lon0 lon1 [minQ]: rivers ending in a box, with their main chain upstream (delta branches)
            if (args.Length > 6 && args[2] == "mouths2")
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                double la0 = double.Parse(args[3], ci), la1 = double.Parse(args[4], ci), lo0 = double.Parse(args[5], ci), lo1 = double.Parse(args[6], ci);
                double minQ = args.Length > 7 ? double.Parse(args[7], ci) : 20;
                var rv = data.Rivers;
                var into = new List<int>[rv.Count];
                for (int r = 0; r < rv.Count; r++) if (rv[r].Next >= 0) (into[rv[r].Next] ??= new List<int>()).Add(r);
                for (int r = 0; r < rv.Count; r++)
                {
                    var re = rv[r];
                    int nv = re.Lon.Length;
                    if (re.Next >= 0 || re.Q < minQ || re.Lat[nv - 1] < la0 || re.Lat[nv - 1] > la1 || re.Lon[nv - 1] < lo0 || re.Lon[nv - 1] > lo1) continue;
                    Console.WriteLine($"mouth reach {r} Q {re.Q:0} at {re.Lat[nv - 1]:0.000},{re.Lon[nv - 1]:0.000}");
                    int cur = r, steps = 0;
                    while (steps++ < 400)
                    {
                        var c = rv[cur];
                        var ins = into[cur];
                        string side = ins == null ? "" : string.Join(" ", ins.Select(i => $"{i}:{rv[i].Q:0}"));
                        if (steps < 400 || ins == null) Console.WriteLine($"   {cur} Q {c.Q:0} {c.Lat[0]:0.000},{c.Lon[0]:0.000} -> {c.Lat[c.Lat.Length - 1]:0.000},{c.Lon[c.Lon.Length - 1]:0.000} in [{side}]");
                        if (ins == null) break;
                        cur = ins.OrderByDescending(i => rv[i].Q).First();
                    }
                }
                return 0;
            }

            // audit mode: walk the centre line of every river (see RiverAudit)
            if (args.Length > 2 && args[2] == "audit")
            {
                // 1-block steps closer together than StepSpacing within a reach (pool layout 9+: where spills form)
                int close = 0, steps = 0;
                for (int r = 0; r < model.Rivers.Reaches.Count; r++)
                {
                    if (!model.Rivers.Kept(r)) continue;
                    string dbg = model.Rivers.DebugReach(r);
                    int i0 = dbg.IndexOf("drops [");
                    if (i0 < 0) continue;
                    var ds = dbg.Substring(i0 + 7, dbg.IndexOf(']', i0) - i0 - 7).Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
                    steps += ds.Length;
                    for (int i = 1; i < ds.Length; i++) if (ds[i] - ds[i - 1] < RiverNetwork.StepSpacing) close++;
                }
                Console.WriteLine($"steps (distinct places) {steps}, closer than {RiverNetwork.StepSpacing} to the one before in the reach: {close}");
                var audit = RiverAudit.Run(model, m => Console.WriteLine("  " + m));
                geo.FromGeo(48.60, 7.86, out double spx, out double spz);
                string auditReport = audit.Report(spx, spz);
                File.WriteAllText(Path.Combine(outDir, "river-audit.txt"), auditReport);
                Console.WriteLine(auditReport.Length > 12000 ? auditReport.Substring(0, 12000) + " ..." : auditReport);
                return 0;
            }

            // lakerims mode: where pool layout 4's lake rims act - beside every river's centre line, just past its banks, lake
            // water more than 3 blocks above the river (run with POOLS=3 to see the places before)
            if (args.Length > 2 && args[2] == "lakerims")
            {
                // RIMSPOTS="lat,lon;...": the river check (lake walls, curtains) in 320 blocks around each instead
                if (Environment.GetEnvironmentVariable("RIMSPOTS") is string rimSpots)
                {
                    foreach (var spot in rimSpots.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var ll = spot.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        RiverCheck(model, geo, $"{ll[0]:0.00},{ll[1]:0.00}", ll[0], ll[1], 160);
                    }
                    return 0;
                }
                var rv = model.Rivers;
                var spots = new Dictionary<(int, int), (double lat, double lon, int up, double q, int n)>();
                for (int r = 0; r < rv.Reaches.Count; r++)
                {
                    if (!rv.Kept(r) || !rv.ReachLine(r, out var lx, out var lz)) continue;
                    double q = rv.Reaches[r].Q, off = rv.HalfWidth(q) + rv.Bank(q) + 1;
                    for (int k = 1; k < lx.Length; k++)
                    {
                        double dx = lx[k] - lx[k - 1], dz = lz[k] - lz[k - 1], len = Math.Sqrt(dx * dx + dz * dz);
                        if (len < 1e-6 || len > 1000) continue;
                        for (double u = 0; u < len; u += 4)
                        {
                            double x = lx[k - 1] + dx * u / len, z = lz[k - 1] + dz * u / len;
                            if (!rv.Nearest(x, z, out var h)) continue;
                            foreach (int side in new[] { -1, 1 })
                            {
                                var c = model.GetColumn(x - dz / len * off * side, z + dx / len * off * side);
                                if (c.River || (c.Water != TerrainModel.WaterLake && c.Water != TerrainModel.WaterSaltLake) || c.WaterTop <= h.WaterTop + 3) continue;
                                var key = ((int)Math.Floor(x / 256), (int)Math.Floor(z / 256));
                                var g = geo.ToGeo(x, z);
                                spots[key] = spots.TryGetValue(key, out var s) ? (s.lat, s.lon, Math.Max(s.up, c.WaterTop - h.WaterTop), Math.Max(s.q, q), s.n + 1)
                                    : (g.Lat, g.Lon, c.WaterTop - h.WaterTop, q, 1);
                            }
                        }
                    }
                }
                Console.WriteLine($"lake water more than 3 blocks above a river beside it: {spots.Values.Sum(s => s.n)} samples in {spots.Count} cells of 256 blocks");
                Console.WriteLine($"gorge sections: {rv.GorgeSections} ({rv.GorgeSteps} steps)");
                foreach (var s in spots.Values.OrderByDescending(s => s.n).Take(60))
                    Console.WriteLine($"  {s.lat,8:0.000},{s.lon,9:0.000}  Q {s.q,7:0}  lake up to {s.up,3} above  {s.n,4} samples");
                return 0;
            }

            // reach mode: pools of reaches and their inflows, e.g. reach 189100
            if (args.Length > 3 && args[2] == "reach")
            {
                foreach (string a in args.Skip(3))
                {
                    int r = int.Parse(a);
                    var re = data.Rivers[r];
                    Console.WriteLine($"reach {r} Q {re.Q:0} next {re.Next}: {model.Rivers.DebugReach(r)}");
                    Console.WriteLine("   levels " + string.Join(" ", re.Level.Select((l, i) => l + (re.Fixed[i] ? "f" : "") + "->y" + model.RiverWaterTop(l))));
                    for (int i = 0; i < data.Rivers.Count; i++)
                    {
                        if (data.Rivers[i].Next == r && data.Rivers[i].Q >= 150)
                            Console.WriteLine($"   inflow {i} Q {data.Rivers[i].Q:0}: {model.Rivers.DebugReach(i)}");
                    }
                }
                return 0;
            }

            // chain mode: follow a river downstream from a reach, e.g. chain 189100 60
            if (args.Length > 4 && args[2] == "chain")
            {
                int r = int.Parse(args[3]), steps = int.Parse(args[4]);
                for (int k = 0; k < steps && r >= 0; k++)
                {
                    var re = data.Rivers[r];
                    int nn = re.Lon.Length;
                    int ins = 0;
                    for (int i = 0; i < data.Rivers.Count; i++) if (data.Rivers[i].Next == r && data.Rivers[i].Q >= 150) ins++;
                    Console.WriteLine($"{r,7} Q {re.Q,6:0} {re.Lat[0]:0.000},{re.Lon[0]:0.000} lev {re.Level[0]}{(re.Fixed[0] ? "f" : "")}..{re.Level[nn - 1]}{(re.Fixed[nn - 1] ? "f" : "")} inflows {ins}  {model.Rivers.DebugReach(r)}");
                    r = re.Next;
                }
                return 0;
            }

            // drops mode: the river drops near places and how high they are, e.g. drops 56.58,25.6,400 -5.5,13.6,300
            if (args.Length > 3 && args[2] == "drops")
            {
                foreach (string a in args.Skip(3))
                {
                    var f = a.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    geo.FromGeo(f[0], f[1], out double wx, out double wz);
                    double half = f.Length > 2 ? f[2] : 300;
                    // a drop of n blocks is n steps at one place
                    var near = model.Rivers.Drops().Where(d => Math.Abs(d.X - wx) <= half && Math.Abs(d.Z - wz) <= half)
                        .GroupBy(d => (Math.Round(d.X, 1), Math.Round(d.Z, 1)))
                        .Select(g => (X: g.Key.Item1, Z: g.Key.Item2, Height: g.Count(), Below: g.Min(d => d.Top)))
                        .OrderByDescending(d => d.Height).ThenBy(d => Math.Abs(d.X - wx) + Math.Abs(d.Z - wz)).ToList();
                    Console.WriteLine($"{a}: x={wx:0} z={wz:0}, {near.Count} drops within {half:0} blocks ({near.Count(d => d.Height >= 2)} of 2+)");
                    foreach (var d in near.Take(12))
                    {
                        var g = geo.ToGeo(d.X, d.Z);
                        Console.WriteLine($"  {d.Height} blocks  x={d.X:0} z={d.Z:0}  {g.Lat:0.0000},{g.Lon:0.0000}  water below y{d.Below}");
                    }
                }
                return 0;
            }

            // where mode: world position and column for places, e.g. where 65.8,-168.9 89.9,0
            if (args.Length > 3 && args[2] == "where")
            {
                foreach (string a in args.Skip(3))
                {
                    var f = a.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    geo.FromGeo(f[0], f[1], out double wx, out double wz);
                    var c = model.GetColumn(wx, wz);
                    var g = geo.ToGeo(wx, wz);
                    string kind = c.Water == TerrainModel.WaterSea ? "sea" : c.River ? "river" : c.Water != 0 ? "lake" : "land";
                    Console.WriteLine($"  {a,-18} x={wx,7:0} z={wz,7:0}  {kind,-5} top {c.Top} water {c.WaterTop}  elev {c.Elev:0} m{(c.Gutter || g.InGutter ? "  GUTTER" : "")}  seam lon {geo.SeamLon0:0.00}");
                }
                return 0;
            }

            // line mode: columns along any line, e.g. line 141070 53225 141070 53260 36  (x0 z0 x1 z1 samples)
            if (args.Length > 7 && args[2] == "line")
            {
                double lx0 = double.Parse(args[3]), lz0 = double.Parse(args[4]), lx1 = double.Parse(args[5]), lz1 = double.Parse(args[6]);
                int ln = int.Parse(args[7]);
                for (int i = 0; i < ln; i++)
                {
                    double t = ln > 1 ? (double)i / (ln - 1) : 0, xx = lx0 + (lx1 - lx0) * t, zz = lz0 + (lz1 - lz0) * t;
                    var c = model.GetColumn(xx, zz);
                    var gg = geo.ToGeo(xx, zz);
                    var ts = data.SampleTerrain(gg.Lat, gg.Lon);
                    Console.WriteLine($"  x={xx:0} z={zz:0} water {c.Water} top {c.Top} wtop {c.WaterTop}  h {c.Elev:0.0}  data {ts.Elev:0.0} ocean {ts.Ocean:0.00} lake {ts.Lake:0.00} level {ts.LakeLevel:0} shore {ts.ShoreLevel:0}");
                }
                return 0;
            }

            // row mode: columns along a line, e.g. row 123187 124087 24300 20  (x0 x1 z step)
            if (args.Length > 6 && args[2] == "row")
            {
                int x0 = int.Parse(args[3]), x1 = int.Parse(args[4]), rz = int.Parse(args[5]), st = int.Parse(args[6]);
                for (int xx = x0; xx <= x1; xx += st)
                {
                    var c = model.GetColumn(xx, rz);
                    var gg = geo.ToGeo(xx, rz);
                    var ts = data.SampleTerrain(gg.Lat, gg.Lon);
                    Console.WriteLine($"  x={xx} {gg.Lat:0.000},{gg.Lon:0.000}  water {c.Water} top {c.Top} wtop {c.WaterTop}  h {c.Elev:0}  data elev {ts.Elev:0} ocean {ts.Ocean:0.00} lake {ts.Lake:0.00} relief {ts.Relief:0}");
                }
                return 0;
            }

            // settle mode: the settled water around a position, e.g. settle 75554 26696
            if (args.Length > 4 && args[2] == "settle")
            {
                int sx = int.Parse(args[3]), sz = int.Parse(args[4]);
                const int E = 24;
                var ext = new TerrainModel.Column[E * E];
                for (int j = 0; j < E; j++) for (int i = 0; i < E; i++) ext[j * E + i] = model.GetColumn(sx - E / 2 + i, sz - E / 2 + j);
                TerrainModel.SettleSteps(ext, E, E, model.Rivers?.PoolLayout >= 1, model.Rivers?.PoolLayout >= 4, model.Rivers?.PoolLayout >= 5, model.Rivers?.PoolLayout >= 6, model.Rivers?.PoolLayout >= 7, model.Rivers?.PoolLayout >= 8, model.Rivers?.PoolLayout >= 10);
                for (int j = E / 2 - 3; j <= E / 2 + 3; j++)
                {
                    for (int i = E / 2 - 3; i <= E / 2 + 3; i++)
                    {
                        var c = ext[j * E + i];
                        Console.Write($"[{c.Water}{(c.River ? "r" : " ")} t{c.Top} w{c.WaterTop} R{c.Ramp} F{c.Fall}] ");
                    }
                    Console.WriteLine();
                }
                return 0;
            }

            // profile mode: the water along a river's centre line downstream from the river nearest a world position, e.g.
            // profile 253975 49892 2000: every change of the water top (distance, level, ramp), the gaps between steps and
            // whether the level ever rises
            if (args.Length > 5 && args[2] == "profile")
            {
                int px0 = int.Parse(args[3]), pz0 = int.Parse(args[4]);
                double want = double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
                var rv = model.Rivers;
                if (!rv.Nearest(px0, pz0, out var h0)) { Console.WriteLine("no river"); return 1; }
                int r = h0.Reach;
                double skip = h0.Along, walked = 0;
                int last = int.MinValue, rises = 0;
                double lastStep = double.NaN;
                var gaps = new List<double>();
                var line = new System.Text.StringBuilder();
                while (r >= 0 && walked < want && rv.ReachLine(r, out var lx, out var lz))
                {
                    double s0 = 0;
                    for (int k = 1; k < lx.Length && walked < want; k++)
                    {
                        double dx = lx[k] - lx[k - 1], dz = lz[k] - lz[k - 1], len = Math.Sqrt(dx * dx + dz * dz);
                        for (double u = 0; u < len && walked < want; u += 1)
                        {
                            if (s0 + u < skip) continue;
                            double x = lx[k - 1] + dx * u / len, z = lz[k - 1] + dz * u / len;
                            var c = model.GetColumn(Math.Floor(x) + 0.5, Math.Floor(z) + 0.5);
                            walked++;
                            if (c.Water == TerrainModel.WaterNone) continue;
                            if (c.WaterTop != last)
                            {
                                if (last != int.MinValue && c.WaterTop > last) rises++;
                                if (last != int.MinValue && c.WaterTop < last)
                                {
                                    if (!double.IsNaN(lastStep)) gaps.Add(walked - lastStep);
                                    lastStep = walked;
                                }
                                line.Append($" {walked:0}:y{c.WaterTop}");
                                last = c.WaterTop;
                            }
                        }
                        s0 += len;
                    }
                    skip = 0;
                    r = (rv.Reaches ?? data.Rivers)[r].Next;
                }
                Console.WriteLine($"profile from {px0},{pz0} ({walked:0} blocks, pool layout {rv.PoolLayout}):{line}");
                Console.WriteLine($"  rises {rises}; gaps between level changes: n {gaps.Count}, under 7: {gaps.Count(g => g < 7)}, " +
                    $"min {(gaps.Count > 0 ? gaps.Min() : 0):0}, median {(gaps.Count > 0 ? gaps.OrderBy(g => g).ElementAt(gaps.Count / 2) : 0):0}");
                return 0;
            }

            // area mode: the settled water of a square around a world position, one map per layer (water top, ramp, rapid
            // marks, river/rapids reach), e.g. area 253975 49893 40
            if (args.Length > 5 && args[2] == "area")
            {
                int ax0 = int.Parse(args[3]), az0 = int.Parse(args[4]), half = int.Parse(args[5]);
                const int M = 12;
                int E = 2 * (half + M) + 1;
                var ext = new TerrainModel.Column[E * E];
                for (int j = 0; j < E; j++) for (int i = 0; i < E; i++) ext[j * E + i] = model.GetColumn(ax0 - half - M + i, az0 - half - M + j);
                int pl = model.Rivers?.PoolLayout ?? 0;
                TerrainModel.SettleSteps(ext, E, E, pl >= 1, pl >= 4, pl >= 5, pl >= 6, pl >= 7, pl >= 8, pl >= 10);
                int baseY = int.MaxValue;
                foreach (var c in ext) if (c.Water != TerrainModel.WaterNone && c.WaterTop >= 0) baseY = Math.Min(baseY, c.WaterTop);
                Console.WriteLine($"area {ax0},{az0} +-{half}, pool layout {pl}; rows z, columns x (west left); water tops as y - {baseY}");
                Console.WriteLine("legend: digit/letter = water top - base (a=10..), '.' land, then the ramp height, else Q river in a rapids reach, r river, L lake, w other water");
                var sbw = new System.Text.StringBuilder();
                for (int j = M; j < E - M; j++)
                {
                    sbw.Append($"z{az0 - half - M + j,7} ");
                    for (int i = M; i < E - M; i++)
                    {
                        var c = ext[j * E + i];
                        if (c.Water == TerrainModel.WaterNone || c.WaterTop < 0) { sbw.Append(" . "); continue; }
                        int dy = c.WaterTop - baseY;
                        sbw.Append(' ').Append(dy < 10 ? (char)('0' + dy) : dy < 36 ? (char)('a' + dy - 10) : '+')
                            .Append(c.Ramp > 0 ? (char)('0' + c.Ramp) : c.River ? (c.RapidsReach ? 'Q' : 'r') : c.Water == TerrainModel.WaterLake ? 'L' : 'w');
                    }
                    sbw.AppendLine();
                }
                Console.Write(sbw.ToString());
                // the surface faces between neighbouring river columns of the area (a pool's top 7/8 above its water top, a
                // ramp L/8 above the block over it); spills are not drawn here
                double Surf(in TerrainModel.Column c) => c.Ramp > 0 ? c.WaterTop + 1 + c.Ramp / 8.0 : c.WaterTop + 7 / 8.0;
                int f30 = 0, f55 = 0;
                double fmax = 0;
                for (int j = M; j < E - M; j++)
                    for (int i = M; i < E - M; i++)
                    {
                        var c = ext[j * E + i];
                        if (!c.River) continue;
                        foreach (int k2 in new[] { j * E + i + 1, (j + 1) * E + i })
                        {
                            var d = ext[k2];
                            if (!d.River) continue;
                            double df = Math.Abs(Surf(c) - Surf(d));
                            fmax = Math.Max(fmax, df);
                            if (df > 0.3) f30++;
                            if (df > 0.55) f55++;
                        }
                    }
                int spills = 0;
                for (int k2 = 0; k2 < ext.Length; k2++) if (ext[k2].Spill > 0) spills++;
                Console.WriteLine($"river surface faces over 0.3: {f30}, over 0.55: {f55}, largest {fmax:0.00}; spill columns {spills}");
                return 0;
            }

            // col mode: the model's columns around a world position, e.g. col 133569 18768
            if (args.Length > 4 && args[2] == "col")
            {
                int qx = int.Parse(args[3]), qz = int.Parse(args[4]);
                for (int dz = -2; dz <= 2; dz++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var c = model.GetColumn(qx + dx, qz + dz);
                        Console.Write($"[{c.Water} t{c.Top} w{c.WaterTop} r{(c.River ? 1 : 0)} e{c.Elev:0.0}] ");
                    }
                    Console.WriteLine();
                }
                // the river query at the column and beside it (pool layout 2+: chute, shoal, plunge pool)
                foreach (var (hx, hz) in new[] { (qx, qz), (qx + 1, qz), (qx, qz + 1) })
                    if (model.Rivers != null && model.Rivers.Nearest(hx, hz, out var h))
                        Console.WriteLine($"  river at {hx},{hz}: q {h.Q:0} dist {h.Dist:0.0} water y{h.WaterTop} ramp {h.Ramp} rapid {h.Rapid} chute {h.Chute} lip {h.Lip} shoal {h.Shoal:0.00} plunge {h.Plunge + h.PlungeDepth:0.0} reach {h.Reach} along {h.Along:0.0}: {model.Rivers.DebugReach(h.Reach)}");
                return 0;
            }

            // mouths mode: every river end (no reach downstream) - does its river water touch the sea or a lake?
            // channels mode: every river exit channel and canal the world dredges (SeaChannels), with its course in lat/lon, to
            // tests/out/channels.json, for tools/water_atlas.py --channels to check against the GLO-90 water body mask
            if (args.Length > 2 && args[2] == "channels")
            {
                if (Environment.GetEnvironmentVariable("COASTS") == "1" && !data.IslandOverlay)
                {
                    data.SetIslandOverlay(true);
                    model = BuildTestModel(data, geo, vs, sea, riverEnds, out _);
                }
                if (model.Channels == null) { Console.WriteLine("no channels in this model"); return 1; }
                geo.FromGeo(0, 0, out double chAx, out double chAz);
                geo.FromGeo(0, 1, out double chBx, out double chBz);
                double mPerBlock = 111320.0 / Math.Abs(chBx - chAx);
                using var w = new StreamWriter(Path.Combine(outDir, "channels.json"));
                w.Write("{\"metres_per_block\":" + mPerBlock.ToString("0.###", CultureInfo.InvariantCulture) + ",\"channels\":[");
                for (int i = 0; i < model.Channels.Channels.Count; i++)
                {
                    var ch = model.Channels.Channels[i];
                    var sb = new StringBuilder();
                    sb.Append(i > 0 ? ",\n" : "\n").Append("{\"name\":\"").Append((ch.Name ?? "").Replace("\"", "'")).Append("\",\"reach\":").Append(ch.Reach)
                      .Append(",\"q\":").Append(ch.Q.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(",\"ship\":").Append(ch.Ship ? "true" : "false").Append(",\"canal\":").Append(ch.Canal ? "true" : "false")
                      .Append(",\"lake\":").Append(ch.Lake ? "true" : "false").Append(",\"depth\":").Append(ch.Depth)
                      .Append(",\"half_width\":").Append(ch.HalfWidth.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(",\"max_half_width\":").Append(ch.MaxHalfWidth.ToString("0.#", CultureInfo.InvariantCulture))
                      .Append(",\"length\":").Append(ch.Length.ToString("0", CultureInfo.InvariantCulture)).Append(",\"course\":[");
                    for (int k = 0; k < ch.X.Length; k++)
                    {
                        var g = geo.ToGeo(ch.X[k], ch.Z[k]);
                        sb.Append(k > 0 ? "," : "").Append('[').Append(g.Lat.ToString("0.#####", CultureInfo.InvariantCulture)).Append(',')
                          .Append(g.Lon.ToString("0.#####", CultureInfo.InvariantCulture)).Append(']');
                    }
                    // which pieces may cut land (rules 4: bars only; before: all)
                    sb.Append("],\"fine\":").Append(ch.Fine ? "true" : "false").Append(",\"land\":[");
                    for (int k = 0; ch.LandPieces != null && k < ch.LandPieces.Length; k++) sb.Append(k > 0 ? "," : "").Append(ch.LandPieces[k] ? '1' : '0');
                    w.Write(sb.Append("]}").ToString());
                }
                w.Write("]}");
                Console.WriteLine($"{model.Channels.Channels.Count} channels ({mPerBlock:0.##} m per block) to {Path.Combine(outDir, "channels.json")}");
                // the river courses the world lays out (after the river ends rules), in rivers.bin's format (version 2, Fixed
                // unused), and how each changed reach ends (RiverEnds.End) as a byte per reach (255: unchanged)
                if (ends != null)
                {
                    var rs = ends.Reaches;
                    using var raw = new MemoryStream();
                    using (var bw = new BinaryWriter(raw, Encoding.UTF8, true))
                        foreach (var re in rs)
                        {
                            bw.Write(re.Q); bw.Write(re.Next); bw.Write(re.Lon.Length);
                            for (int k = 0; k < re.Lon.Length; k++) { bw.Write(re.Lon[k]); bw.Write(re.Lat[k]); bw.Write(re.Level[k]); bw.Write((byte)0); }
                        }
                    using (var f = File.Create(Path.Combine(outDir, "game-rivers.bin")))
                    {
                        f.Write(Encoding.ASCII.GetBytes("EMRV")); f.Write(BitConverter.GetBytes(2)); f.Write(BitConverter.GetBytes(rs.Count));
                        using var br = new System.IO.Compression.BrotliStream(f, System.IO.Compression.CompressionLevel.Fastest);
                        raw.Position = 0; raw.CopyTo(br);
                    }
                    var endBytes = Enumerable.Repeat((byte)255, rs.Count).ToArray();
                    foreach (var kv in ends.Ends) endBytes[kv.Key] = (byte)kv.Value;
                    File.WriteAllBytes(Path.Combine(outDir, "game-river-ends.bin"), endBytes);
                    Console.WriteLine($"{rs.Count} river reaches ({ends.Ends.Count(kv => kv.Value == RiverEnds.End.Led)} led on) to game-rivers.bin");
                }
                return 0;
            }

            if (args.Length > 2 && args[2] == "mouths")
            {
                int total = 0, nSea = 0, nLake = 0, inland = 0, open = 0;
                var bad = new List<string>();
                for (int r = 0; r < data.Rivers.Count; r++)
                {
                    var re = data.Rivers[r];
                    if (!model.Rivers.Kept(r) || model.Rivers.Kept(re.Next)) continue;
                    int nv = re.Lon.Length;
                    geo.FromGeo(re.Lat[nv - 1], re.Lon[nv - 1], out double ex, out double ez);
                    total++;
                    double hw = model.Rivers.HalfWidth(re.Q);
                    int R = (int)(3 * hw + 60);
                    bool touchesSea = false, touchesLake = false, anyRiver = false;
                    // around every vertex of the last reach (the river may meet the sea before its data ends)
                    for (int vi = nv - 1; vi >= 0 && !touchesSea; vi--)
                    {
                    geo.FromGeo(re.Lat[vi], re.Lon[vi], out double vx, out double vz);
                    R = (int)(hw + 6);
                    var prev = new TerrainModel.Column[2 * R + 1];
                    for (int j = -R; j <= R && !touchesSea; j++)
                    {
                        TerrainModel.Column left = default;
                        for (int i = -R; i <= R; i++)
                        {
                            var c = model.GetColumn(vx + i, vz + j);
                            if (c.River) anyRiver = true;
                            var up = prev[i + R];
                            // a river column next to a sea (or lake) column, left or above
                            for (int side = 0; side < 2; side++)
                            {
                                if (side == 0 ? i == -R : j == -R) continue;
                                var d = side == 0 ? left : up;
                                bool pair = (c.River && !d.River && d.Water != TerrainModel.WaterNone) || (d.River && !c.River && c.Water != TerrainModel.WaterNone);
                                if (!pair) continue;
                                var w = c.River ? d : c;
                                if (w.Water == TerrainModel.WaterSea) touchesSea = true; else touchesLake = true;
                            }
                            prev[i + R] = c;
                            left = c;
                        }
                    }
                    }
                    bool atSeaLevel = re.Level[nv - 1] <= 10;
                    // ending right in the sea or a lake counts as open too
                    var endCol = model.GetColumn(ex, ez);
                    if (!endCol.River && endCol.Water == TerrainModel.WaterSea) touchesSea = true;
                    else if (!endCol.River && endCol.Water != TerrainModel.WaterNone) touchesLake = true;
                    if (touchesSea) nSea++;
                    else if (touchesLake) nLake++;
                    else if (!atSeaLevel) inland++;
                    else
                    {
                        open++;
                        // nearest sea column in the model (rings every 4 blocks up to 800)
                        double best = -1;
                        for (int rad = 4; rad <= 800 && best < 0; rad += 4)
                        {
                            int steps = Math.Max(8, (int)(2 * Math.PI * rad / 4));
                            for (int q = 0; q < steps; q++)
                            {
                                double a = 2 * Math.PI * q / steps;
                                var c = model.GetColumn(ex + rad * Math.Cos(a), ez + rad * Math.Sin(a));
                                if (c.Water == TerrainModel.WaterSea) { best = rad; break; }
                            }
                        }
                        bad.Add($"  Q {re.Q,6:0}  {re.Lat[nv - 1]:0.000},{re.Lon[nv - 1]:0.000}  level {re.Level[nv - 1]} m  end: water {endCol.Water} elev {endCol.Elev:0} river {endCol.River}  nearest sea {(best < 0 ? "> 800" : best.ToString())} blocks");
                    }
                }
                Console.WriteLine($"river ends: {total}; into the sea {nSea}, into a lake {nLake}, inland sinks above 10 m {inland}, at sea level but not touching water {open}");
                foreach (var b in bad.OrderByDescending(x => x)) Console.WriteLine(b);
                return 0;
            }

            // probe mode: renders at 1 block/px and river mouths around places, e.g. probe 57.03,24.02,300
            if (args.Length > 2 && args[2] == "probe")
            {
                foreach (string p in args.Skip(3))
                {
                    var f = p.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    double plat = f[0], plon = f[1];
                    int half = (int)f[2];
                    RenderAt(model, geo, outDir, $"probe-{plat:0.00}_{plon:0.00}", plat, plon, 2 * half, 2 * half, 1);
                    geo.FromGeo(plat, plon, out double px, out double pz);
                    for (int r = 0; r < data.Rivers.Count; r++)
                    {
                        var re = data.Rivers[r];
                        if (!model.Rivers.Kept(r)) continue;
                        int nv = re.Lon.Length;
                        geo.FromGeo(re.Lat[nv - 1], re.Lon[nv - 1], out double ex, out double ez);
                        if (Math.Abs(ex - px) > half || Math.Abs(ez - pz) > half || (re.Next >= 0 && Environment.GetEnvironmentVariable("ALLREACHES") != "1")) continue;
                        Console.WriteLine($"  reach {r} Q {re.Q:0} next {re.Next} ends at {re.Lat[nv - 1]:0.0000},{re.Lon[nv - 1]:0.0000} (x {ex:0} z {ez:0}) level {re.Level[nv - 1]} fixed {re.Fixed[nv - 1]}");
                        // along the last direction beyond the end
                        geo.FromGeo(re.Lat[nv - 2], re.Lon[nv - 2], out double qx, out double qz);
                        double dx = ex - qx, dz = ez - qz, dl = Math.Sqrt(dx * dx + dz * dz);
                        dx /= dl; dz /= dl;
                        var sb = new System.Text.StringBuilder("    beyond: ");
                        for (int k = -10; k <= 60; k += 2)
                        {
                            var c = model.GetColumn(ex + dx * k, ez + dz * k);
                            sb.Append(c.Water == TerrainModel.WaterSea ? "~" : c.River ? "r" : c.Water != TerrainModel.WaterNone ? "l" : c.Beach ? "b" : c.Top >= sea ? "^" : ".");
                        }
                        Console.WriteLine(sb.ToString());
                        if (re.Next < 0)
                        {
                            for (int k = 0; k <= 60; k += 4)
                            {
                                var c = model.GetColumn(ex + dx * k, ez + dz * k);
                                model.Rivers.Nearest(ex + dx * k, ez + dz * k, out var hh);
                                Console.WriteLine($"      +{k,2}: water {c.Water} top {c.Top} wtop {c.WaterTop} elev {c.Elev:0.0} beach {c.Beach} river {c.River} near {c.NearRiver} | hit d {hh.Dist:0.0} mouth {hh.Mouth} wt {hh.WaterTop} q {hh.Q:0}");
                            }
                        }
                    }
                }
                return 0;
            }

            Console.WriteLine("geometry");
            foreach (var (lat, lon) in new[] { (51.5, -0.1), (27.99, 86.93), (-33.9, 151.2), (65.9, -168.2), (64.0, -171.0), (0.0, 0.0), (-78.0, 170.0) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                var g = geo.ToGeo(x, z);
                double err = Math.Abs(Geo.Wrap180(g.Lon - lon)) + Math.Abs(g.Lat - lat);
                Check(err < 1e-6 && !g.InGutter, $"round trip {lat},{lon} -> x={x:0} z={z:0} -> {g.Lat:0.0000},{g.Lon:0.0000}");
            }
            var mid = geo.ToGeo(25600 + 102400, L / 2.0);
            Console.WriteLine($"  ring middle is at lon {mid.Lon:0.00} (seam centre {geo.SeamLon0:0.00})");
            Check(geo.ToGeo(25600, 30000).InGutter && geo.ToGeo(25600 + 204799, 30000).InGutter, "jump lines are inside the gutter");
            CheckGutterBridges(model, geo);
            CheckSeamLand(model, geo);
            CheckRealSeam(data, inp, vs, sea, riverEnds);
            CheckFrozenGround(model, geo);
            CheckDryLandBelowSea(model);
            CheckSpawnSearch(model, geo, W, L);

            Console.WriteLine("vertical scale (world height " + mapHeight + ", sea " + sea + ")");
            foreach (double margin in new[] { 32000, double.NaN })
            {
                // Cylinder World 64000 x 32000: ring 64000 - 2 x 6400
                var badInp = new LayoutInput
                {
                    WorldWidth = 64000, GutterMargin = margin,
                    CwRingStart = 6400, CwRingLength = 51200, CwEquatorZ = 16000, CwPoleDistance = 16000 - 48, CwBandInner = 608,
                };
                string badLayout = EarthLayout.Sanitize(badInp);
                var fixedGeo = EarthLayout.Build(badInp, data, out _);
                Check(badLayout.Contains("GutterMargin") && badInp.GutterMargin == 512
                    && EarthGeometry.IsUsableLayout(fixedGeo.RingLength, fixedGeo.Gutter, fixedGeo.BlocksPerDegreeZ, fixedGeo.EquatorZ),
                    $"a gutter margin that leaves no Earth ({margin}) takes the default");
            }
            Check(!EarthGeometry.IsUsableLayout(64000, 64000, 100, 0) && !EarthGeometry.IsUsableLayout(64000, 2048, 0, 0), "a saved layout with no Earth is detected");
            double lc = 0, sc = -5, tm = .0625, md = .43;
            string replaced = VerticalScale.Sanitize(ref lc, ref sc, ref tm, ref md);
            Check(lc == 200 && sc == 20 && replaced.Contains("LandCurve") && replaced.Contains("SeaCurve") && !replaced.Contains("TopMargin"), "curve settings of 0 or below take their defaults");
            Check(!new VerticalScale(mapHeight, sea, mapHeight - 24, 0, 60, 20).IsUsable && VerticalScale.ForWorld(mapHeight, sea).IsUsable, "a saved scale with a zero curve is detected; the defaults are usable");
            foreach (double h in new[] { -11000, -4000, -200, -50, 0, 50, 100, 500, 1000, 2000, 4500, 8849 })
            {
                Console.WriteLine($"  {h,7} m -> y {vs.Y(h),7:0.0}   back {vs.Elevation(vs.Y(h)),8:0.0}");
            }

            Console.WriteLine("places");
            foreach (var (name, lat, lon) in new[] { ("Everest", 27.988, 86.925), ("Mont Blanc", 45.833, 6.865), ("London", 51.5, -0.12), ("Amsterdam", 52.37, 4.9),
                ("Dead Sea", 31.5, 35.5), ("Caspian", 42.0, 51.0), ("Baikal", 53.5, 108.0), ("Lake Superior", 47.7, -87.5), ("Greenland", 72.5, -38.5),
                ("South Pole", -89.5, 0.0), ("Mariana", 11.35, 142.2), ("Qattara", 29.8, 27.0), ("Bering Strait", 65.9, -168.6) })
            {
                geo.FromGeo(lat, lon, out double x, out double z);
                var c = model.GetColumn(x, z);
                model.ClimateAt(lat, lon, out double t, out double rain, out double sy);
                Console.WriteLine($"  {name,-14} x={x,7:0} z={z,6:0} elev {c.Elev,7:0} m  top {c.Top,3} water {c.Water} wtop {c.WaterTop,3} ice {(c.IceBottom <= c.Top ? c.Top - c.IceBottom + 1 : 0),3}  T {t,6:0.0} °C rain {rain,4:0}{(c.Gutter ? " GUTTER" : "")}");
            }

            // speed
            sw.Restart();
            int n = 0;
            geo.FromGeo(46, 8, out double ax, out double az);
            for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) { model.GetColumn(ax + i, az + j); n++; }
            Console.WriteLine($"  {sw.Elapsed.TotalMilliseconds * 1000 / n:0.00} µs per column");

            if (model.Rivers != null)
            {
                Console.WriteLine("rivers (pools, ramps, walls)");
                foreach (var (name, lat, lon) in new[] { ("Daugava Plavinas", 56.58, 25.6), ("Daugava Riga", 56.9, 24.2), ("Rhine Loreley", 50.14, 7.73),
                    ("Rhine Netherlands", 51.85, 5.9), ("Danube Budapest", 47.5, 19.05), ("Seine Paris", 48.86, 2.3), ("Nile Luxor", 25.7, 32.65),
                    ("Nile Aswan", 24.0, 32.89), ("Yangtze 3 Gorges", 30.82, 111.0), ("Niagara", 43.08, -79.07), ("Victoria Falls", -17.92, 25.86),
                    ("Congo Livingstone", -5.60, 13.60) })
                {
                    RiverCheck(model, geo, name, lat, lon, 1200);
                }
                CheckRiverFalls(model);
                CheckLakeOutlets(model, data);
                CheckSeaChannels(model);
                CheckSeaDepth(model);
                CheckCanals(model);
                CheckRiverEnds(model, ends);
            }

            CheckIslands(data, vs, outDir, false);
            CheckEstuaryAndDeltas(data, geo, vs, sea);
            CheckRiverWidths(data);
            CheckSeamPieces(data);
            CheckClimateStore(data, geo);
            CheckGeothermalPixel(data, geo, vs);
            CheckCanalsWithoutRivers(data, geo, vs);
            CheckVolcanoPlans(data, geo, vs);
            CheckGrassSeasonAt(data, geo, vs);
            CheckRealLapse(data, geo, root);
            CheckClimateClientV1(data, geo, root);
            CheckFineTemperature(data, geo, vs);
            CheckDataViews(data);
            CheckLakeShores(data, geo, vs);
            CheckGridLakeEdge(data, geo, vs);
            CheckCoasts(data, vs, outDir, false);
            if (Environment.GetEnvironmentVariable("RIVERDEBUG") == "1") return failures == 0 ? 0 : 1;
            Console.WriteLine("renders");
            Render(model, outDir, "ring", geo.RingStart, 0, 1600, 800, 128);
            RenderAt(model, geo, outDir, "europe", 50, 10, 1800, 1100, 24);
            RenderAt(model, geo, outDir, "alps", 46.2, 9.5, 1600, 900, 4);
            RenderAt(model, geo, outDir, "montblanc", 45.83, 6.86, 1024, 1024, 1);
            RenderAt(model, geo, outDir, "netherlands", 52.4, 4.8, 1024, 1024, 2);
            RenderAt(model, geo, outDir, "norway", 61.0, 6.5, 1200, 1000, 4);
            RenderAt(model, geo, outDir, "himalaya", 28.0, 86.9, 1200, 900, 8);
            RenderAt(model, geo, outDir, "deadsea", 31.5, 35.5, 800, 1000, 4);
            RenderAt(model, geo, outDir, "greatlakes", 45.0, -84.0, 1600, 1000, 16);
            // across the jump line in the Bering Strait
            geo.FromGeo(65.8, -168.6, out _, out double bz);
            Render(model, outDir, "bering-seam", geo.RingStart + geo.RingLength - 8000, bz - 6000, 1600, 1500, 10);
            Render(model, outDir, "seam-close", geo.RingStart + geo.RingLength - 1536, bz - 1024, 1024, 1024, 3);
            RenderAt(model, geo, outDir, "river-daugava", 56.8, 24.6, 1600, 1000, 3);
            RenderAt(model, geo, outDir, "river-rhine", 50.5, 7.4, 1200, 1200, 4);
            RenderAt(model, geo, outDir, "river-danube-budapest", 47.5, 19.05, 1200, 900, 3);
            RenderAt(model, geo, outDir, "river-seine-paris", 48.9, 2.35, 1000, 800, 3);
            RenderAt(model, geo, outDir, "river-nile", 26.0, 32.5, 1000, 1400, 6);
            RenderAt(model, geo, outDir, "river-europe", 50, 15, 1800, 1100, 24);

            Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Decision 0046: the rapids decision at named river places, and the share of river columns in rapids reaches
        /// around each (as RiverCheck counts them).</summary>
        static void RapidsSites(TerrainModel model, EarthGeometry geo)
        {
            var rv = model.Rivers;
            Console.WriteLine($"rapids sites (pool layout {rv.PoolLayout}, RapidsPower {RiverNetwork.RapidsPower}, size scale {rv.SizeScale:0.##})");
            var (lo, ln, la) = rv.RapidsLengths();
            Console.WriteLine($"  river length in rapids reaches: layouts 8-10 {100 * lo / la:0.0} %, layout 11 {100 * ln / la:0.0} % (of {la / 1000:0} k blocks)");
            foreach (var (name, lat, lon) in new[] {
                ("Olt Avrig (report)", 45.6955, 24.2755), ("Olt Racovita", 45.5637, 24.2618), ("Tanaro/Po (report)", 45.0277, 8.8445),
                ("Po Piacenza", 45.06, 9.70), ("Daugava Plavinas", 56.58, 25.6), ("Daugava Riga", 56.9, 24.2), ("Rhine Loreley", 50.14, 7.73),
                ("Rhine Netherlands", 51.85, 5.9), ("Aare Bern", 46.95, 7.45), ("Rhone Geneva", 46.2, 6.1), ("Inn Innsbruck", 47.27, 11.40),
                ("Danube Budapest", 47.5, 19.05), ("Danube Iron Gates", 44.67, 22.5), ("Seine Paris", 48.86, 2.3), ("Nile Luxor", 25.7, 32.65),
                ("Yangtze 3 Gorges", 30.82, 111.0), ("Niagara", 43.08, -79.07), ("Victoria Falls", -17.92, 25.86),
                ("Congo Livingstone", -5.60, 13.60), ("Colorado Grand Canyon", 36.10, -112.10), ("Snake Hells Canyon", 45.25, -116.70) })
            {
                geo.FromGeo(lat, lon, out double cx, out double cz);
                if (!rv.Nearest(cx, cz, out var hit) || hit.Dist > 200) { Console.WriteLine($"  {name,-22} no river within 200 blocks"); continue; }
                double m = rv.ReachValleySlope(hit.Reach), power = m / 1000 * Math.Sqrt(hit.Q);
                int river = 0, rapids = 0, drops = 0;
                var rampHist = new int[8];
                for (int dz = -400; dz <= 400; dz += 2)
                    for (int dx = -400; dx <= 400; dx += 2)
                    {
                        var c = model.GetColumn(cx + dx, cz + dz);
                        if (!c.River) continue;
                        river++;
                        if (c.RapidsReach) rapids++;
                        if (c.Rapid && c.Ramp > 0) drops++;
                        if (c.RapidsReach) rampHist[Math.Min((int)c.Ramp, 7)]++;
                    }
                Console.WriteLine($"  {name,-22} Q {hit.Q,8:0} m3/s  valley {m,6:0.00} m/km  power {power,7:0.0000}  layouts 8-10 {(m >= RiverNetwork.RapidsSlope ? "rapids" : "calm  ")}  " +
                    $"11 {(RiverNetwork.RapidsByPower(m, hit.Q) ? "rapids" : "calm  ")}  rapids columns within 400: {(river > 0 ? 100.0 * rapids / river : 0),5:0.0} % of {river}, rapid ramps {(river > 0 ? 100.0 * drops / river : 0),5:0.0} %");
                if (rapids > 0) Console.WriteLine($"  {"",-22} rapids columns by ramp height 0..7 (layout 11: white water only with a ramp): {string.Join(" ", rampHist.Select(n => $"{100.0 * n / rapids:0.0}%"))}");
            }
        }

        /// <summary>
        /// River water in a square around a place: steps between neighbouring river columns (1 block with a ramp below is
        /// fine; without one, or 2+ blocks, is a seam), river water next to lower dry land (a water wall), and the depth.
        /// </summary>
        static void RiverCheck(TerrainModel model, EarthGeometry geo, string name, double lat, double lon, int half)
        {
            geo.FromGeo(lat, lon, out double cx, out double cz);
            int n = 2 * half;
            var cols = new TerrainModel.Column[n * n];
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(cx - half + i, cz - half + j);
            // as the world generator does: water settled between neighbouring columns
            if (Environment.GetEnvironmentVariable("NOSETTLE") != "1") TerrainModel.SettleSteps(cols, n, n, model.Rivers.PoolLayout >= 1, model.Rivers.PoolLayout >= 4, model.Rivers.PoolLayout >= 5, model.Rivers.PoolLayout >= 6, model.Rivers.PoolLayout >= 7, model.Rivers.PoolLayout >= 8, model.Rivers.PoolLayout >= 10);
            int river = 0, ramps = 0, stepOk = 0, stepBare = 0, stepBig = 0, walls = 0, riverLake = 0, rapidsCols = 0;
            var depth = new int[16];
            int minTop = int.MaxValue, maxTop = int.MinValue;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    var c = cols[j * n + i];
                    if (!c.River) continue;
                    river++;
                    if (c.RapidsReach) rapidsCols++;
                    if (c.Ramp > 0) ramps++;
                    depth[Math.Clamp(c.WaterTop - c.Top, 0, 15)]++;
                    minTop = Math.Min(minTop, c.WaterTop);
                    maxTop = Math.Max(maxTop, c.WaterTop);
                    int surf = c.WaterTop + (c.Ramp > 0 ? 1 : 0);
                    foreach (var (di, dj) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= n || jj >= n) continue;
                        var d = cols[jj * n + ii];
                        if (d.Water == TerrainModel.WaterNone)
                        {
                            if (d.Top < surf) walls++;
                            continue;
                        }
                        if (!d.River)
                        {
                            if (d.WaterTop != c.WaterTop && (di > 0 || dj > 0))
                            {
                                riverLake++;
                                if (riverLake <= 3 && Environment.GetEnvironmentVariable("RIVERDEBUG") == "1")
                                {
                                    var gg = geo.ToGeo(cx - half + i, cz - half + j);
                                    Console.WriteLine($"     river/lake at {cx - half + i},{cz - half + j} ({gg.Lat:0.000},{gg.Lon:0.000}): river wt {c.WaterTop} ramp {c.Ramp} nearLake {c.NearLakeTop} / water {d.Water} wt {d.WaterTop}");
                                }
                            }
                            continue;
                        }
                        if (di < 0 || dj < 0 || d.WaterTop == c.WaterTop) continue;
                        var (hi, lo) = d.WaterTop > c.WaterTop ? (d, c) : (c, d);
                        int diff = hi.WaterTop - lo.WaterTop;
                        if (diff >= 2) stepBig++;
                        else if (lo.Ramp >= 5) stepOk++;
                        else
                        {
                            stepBare++;
                            if (stepBare <= 4 && Environment.GetEnvironmentVariable("RIVERDEBUG") == "1")
                            {
                                double x1 = cx - half + i, z1 = cz - half + j, x2 = cx - half + ii, z2 = cz - half + jj;
                                model.Rivers.Nearest(x1, z1, out var h1);
                                model.Rivers.Nearest(x2, z2, out var h2);
                                Console.WriteLine($"     bare step at {x1},{z1} (wt {c.WaterTop} ramp {c.Ramp} lake {c.NearLakeTop} hit wt {h1.WaterTop} r {h1.Ramp} q {h1.Q:0} d {h1.Dist:0.0} dir {h1.DirX:0.00},{h1.DirZ:0.00})" +
                                    $" / {x2},{z2} (wt {d.WaterTop} ramp {d.Ramp} lake {d.NearLakeTop} hit wt {h2.WaterTop} r {h2.Ramp} q {h2.Q:0} d {h2.Dist:0.0} dir {h2.DirX:0.00},{h2.DirZ:0.00})");
                            }
                        }
                    }
                }
            }
            // lake water (not a river's) next to lower dry land (a water wall), and next to water 2+ blocks lower (a curtain's top)
            int lakeWalls = 0, lakeCurtain = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    var c = cols[j * n + i];
                    if (c.River || (c.Water != TerrainModel.WaterLake && c.Water != TerrainModel.WaterSaltLake)) continue;
                    bool wall = false, curtain = false;
                    foreach (var (di, dj) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= n || jj >= n) continue;
                        var d = cols[jj * n + ii];
                        if (d.Water == TerrainModel.WaterNone) wall |= d.Top < c.WaterTop;
                        else curtain |= d.WaterTop <= c.WaterTop - 2;
                    }
                    if (wall) lakeWalls++;
                    if (curtain) lakeCurtain++;
                }
            }
            string dist = string.Join(" ", Enumerable.Range(0, 16).Where(k => depth[k] > 0).Select(k => $"{k}:{depth[k]}"));
            Console.WriteLine($"  {name,-18} river cols {river,7}  levels {(river > 0 ? $"{minTop}..{maxTop}" : "-"),9}  ramp cols {ramps,6}  steps ok {stepOk,4} bare {stepBare,3} 2+ {stepBig,3}  river/lake {riverLake,3}  walls {walls,4}  lake walls {lakeWalls,3} curtain {lakeCurtain,3}  rapids reach {rapidsCols,6}  depth {dist}");
        }

        /// <summary>
        /// The listed waterfalls (RiverFalls): each sits on its river and drops it at once by its height on the lowland scale, as
        /// a curtain of falling water across the river (settled as the world generator does), without water walls beside it.
        /// </summary>
        static void CheckRiverFalls(TerrainModel model)
        {
            var placed = model.Rivers.Falls;
            if (Environment.GetEnvironmentVariable("NOFALLS") == "1") return;
            Console.WriteLine("waterfalls");
            Check(placed.Count == RiverFalls.All.Count, $"all {RiverFalls.All.Count} listed waterfalls are on a river ({placed.Count})");
            foreach (var f in placed)
            {
                int half = (int)(model.Rivers.HalfWidth(f.Q) + 48), n = 2 * half;
                var cols = new TerrainModel.Column[n * n];
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) cols[j * n + i] = model.GetColumn(f.X - half + i, f.Z - half + j);
                TerrainModel.SettleSteps(cols, n, n, model.Rivers.PoolLayout >= 1, model.Rivers.PoolLayout >= 4, model.Rivers.PoolLayout >= 5, model.Rivers.PoolLayout >= 6, model.Rivers.PoolLayout >= 7, model.Rivers.PoolLayout >= 8, model.Rivers.PoolLayout >= 10);
                int curtain = 0, maxFall = 0, walls = 0;
                for (int j = 8; j < n - 8; j++)
                {
                    for (int i = 8; i < n - 8; i++)
                    {
                        var c = cols[j * n + i];
                        if (c.Fall > 0) { curtain++; maxFall = Math.Max(maxFall, c.Fall); }
                        if (!c.River) continue;
                        int surf = c.WaterTop + (c.Ramp > 0 ? 1 : 0);
                        foreach (var (di, dj) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                        {
                            var d = cols[(j + dj) * n + i + di];
                            if (d.Water == TerrainModel.WaterNone && d.Top < surf) walls++;
                        }
                    }
                }
                double width = 2 * model.Rivers.HalfWidth(f.Q);
                Console.WriteLine($"  {f.Name,-16} Q {f.Q,6:0}  drop {f.Drop,2} blocks  curtain {curtain} columns, highest {maxFall} (river {width:0} wide)  walls {walls}");
                // (a one-block step may sit right at the lip: one more)
                Check(maxFall >= f.Drop && maxFall <= f.Drop + 1 && curtain >= 0.7 * width && walls == 0, $"{f.Name}: one curtain of {f.Drop} blocks across the river");
            }
            int Drop(string name) => placed.FirstOrDefault(f => f.Name == name).Drop;
            Check(Drop("Victoria Falls") > Drop("Niagara Falls"), "Victoria Falls is taller than Niagara");
        }

        /// <summary>
        /// Lakes end at their outlet (decision 0055, outlets.bin): Lake Erie's tongue down the upper Niagara is land with the
        /// river through it, Erie itself is still a lake, and no listed waterfall has lake water standing above its lower river
        /// within 3 km below the lip (Natural Earth's Erie ended 3 km past the Horseshoe Falls, over the gorge).
        /// </summary>
        static void CheckLakeOutlets(TerrainModel model, EarthData data)
        {
            if (!data.OutletOverlay) { Console.WriteLine("lake outlets: overlay off (OUTLETS=0 or no outlets.bin)"); return; }
            var geo = model.Geometry;
            Console.WriteLine($"lake outlets: {data.Outlets.Patches.Length} grid patches");
            TerrainModel.Column At(double lat, double lon) { geo.FromGeo(lat, lon, out double x, out double z); return model.GetColumn(x, z); }
            bool Lake(TerrainModel.Column c) => c.Water == TerrainModel.WaterLake && !c.River;
            Check(Lake(At(42.60, -79.60)), "Lake Erie is still a lake");
            foreach (var (name, lat, lon) in new[] { ("gorge below the falls", 43.100, -79.066), ("above the falls (Chippawa)", 43.06, -79.04),
                ("Tonawanda channel", 43.03, -78.90), ("Grand Island west channel", 43.00, -79.02) })
            {
                var c = At(lat, lon);
                Check(!Lake(c), $"Niagara {name} is not Lake Erie (water {c.Water}, river {c.River})");
            }
            double span = 0.027 * geo.BlocksPerDegreeX; // 3 km
            foreach (var f in model.Rivers.Falls)
            {
                int raised = 0;
                for (double dz = -span; dz <= span; dz += Math.Max(1, span / 60))
                    for (double dx = -span; dx <= span; dx += Math.Max(1, span / 60))
                    {
                        var c = model.GetColumn(f.X + dx, f.Z + dz);
                        if (!Lake(c)) continue;
                        model.Rivers.Nearest(f.X + dx, f.Z + dz, out var hit);
                        // lake water beside a river more than 3 blocks lower: standing above the gorge
                        if (hit.Dist < 3 * model.Rivers.HalfWidth(f.Q) && c.WaterTop > hit.WaterTop + 3) raised++;
                    }
                Check(raised == 0, $"{f.Name}: no lake water above the river below it ({raised} samples)");
            }
        }

        /// <summary>
        /// River exits to the sea (SeaChannels): every ship channel has a carrack's width (11 blocks) of channel-deep water all
        /// along it, from the river's end out to open water, and no land below the sea beside it (water walls).
        /// </summary>
        /// <summary>
        /// The least sea away from the shore (TerrainModel.MinSeaDepth: 6 in new worlds, room for a carrack over the banks): no
        /// open-sea column on the shallow banks is shallower, and the banks still stand out from the deep water around them.
        /// </summary>
        static void CheckSeaDepth(TerrainModel model)
        {
            var geo = model.Geometry;
            int min = model.MinSeaDepth;
            Console.WriteLine($"sea depth: at least {min} blocks away from the shore");
            foreach (var (name, la0, la1, lo0, lo1) in new[] { ("Great Bahama Bank", 21.5, 25.5, -79.6, -75.5), ("Florida Bay", 24.4, 25.6, -81.8, -80.2),
                ("Torres Strait", -11.0, -9.0, 141.0, 144.0), ("Gulf of Riga", 57.0, 58.5, 22.5, 24.5) })
            {
                int open = 0, shallow = 0, atMin = 0;
                for (double la = la0; la <= la1; la += 0.03)
                    for (double lo = lo0; lo <= lo1; lo += 0.03)
                    {
                        geo.FromGeo(la, lo, out double x, out double z);
                        var c = model.GetColumn(x, z);
                        if (c.Water != TerrainModel.WaterSea || c.River) continue;
                        // open sea: no land or river within 48 blocks (the cays are a few blocks across)
                        bool all = true;
                        for (int r = 8; r <= 48 && all; r += 8)
                            for (int a = 0; a < 16 && all; a++)
                            {
                                var n = model.GetColumn(x + r * Math.Cos(a * Math.PI / 8), z + r * Math.Sin(a * Math.PI / 8));
                                all = n.Water == TerrainModel.WaterSea && !n.River;
                            }
                        if (!all) continue;
                        open++;
                        int d = c.WaterTop - c.Top;
                        if (d < min) shallow++;
                        else if (d == min) atMin++;
                    }
                Console.WriteLine($"  {name}: {open} open-sea samples, {shallow} shallower than {min}, {atMin} at {min}");
                Check(open > 100 && shallow <= open / 200, $"{name}: open sea at least {min} blocks deep");
                if (name == "Great Bahama Bank") Check(atMin >= open / 5, $"{name}: the bank still shallower than the sea around it");
            }
        }

        static void CheckSeaChannels(TerrainModel model)
        {
            if (model.Channels == null) return;
            var sc = model.Channels;
            Console.WriteLine($"sea channels: {sc.Channels.Count} (river ends already in deep water {sc.AlreadyDeep}, no way out {sc.NoWay})");
            int ok = 0, walls = 0;
            var narrow = new List<string>();
            int skipped = 0;
            foreach (var ch in sc.Channels.Where(c => c.Ship))
            {
                // a river that doesn't come down to the sea before the coast (the Klamath ends above it, behind a cliff in the
                // data): its mouth is the river's (RiverAudit), and the channel never cuts high ground
                var start = model.GetColumn(ch.X[0], ch.Z[0]);
                if (start.River && start.WaterTop > model.SeaLevel - 1) { skipped++; continue; }
                int worst = int.MaxValue;
                for (int i = 1; i < ch.X.Length; i++)
                {
                    double dx = ch.X[i] - ch.X[i - 1], dz = ch.Z[i] - ch.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                    double nx = -dz / len, nz = dx / len;
                    for (double u = 0; u < len; u += 4)
                    {
                        double x = ch.X[i - 1] + dx * u / len, z = ch.Z[i - 1] + dz * u / len;
                        // the river's own water above sea level (its last steps reaching the coast) is the river's, not the channel's
                        int run = 0, best = 0, wrun = 0, wbest = 0;
                        bool riverAbove = false;
                        for (int k = -40; k <= 40; k++)
                        {
                            var c = model.GetColumn(x + nx * k, z + nz * k);
                            if (c.Water != TerrainModel.WaterNone && c.WaterTop - c.Top >= ch.Depth) best = Math.Max(best, ++run); else run = 0;
                            if (c.Water != TerrainModel.WaterNone) wbest = Math.Max(wbest, ++wrun); else wrun = 0;
                            if (c.River && c.WaterTop > model.SeaLevel - 1) riverAbove = true;
                            if (c.Water != TerrainModel.WaterNone) continue;
                            foreach (var (ax, az) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                var d = model.GetColumn(x + nx * k + ax, z + nz * k + az);
                                if (d.Water == TerrainModel.WaterSea && c.Top < d.WaterTop)
                                {
                                    if (walls++ < 5) { var gw = model.Geometry.ToGeo(x + nx * k, z + nz * k);
                                        Console.WriteLine($"  water wall at {gw.Lat:0.0000},{gw.Lon:0.0000}: top {c.Top} beside sea at {d.WaterTop} (channel Q {ch.Q:0})"); }
                                }
                            }
                        }
                        // a natural narrows (a sound between high shores) narrower than a carrack's lane: all of its water deep enough
                        if (!riverAbove) worst = Math.Min(worst, wbest < 11 && best >= wbest ? 11 : best);
                    }
                }
                if (worst >= 11) ok++;
                else narrow.Add($"  Q {ch.Q:0} from {model.Geometry.ToGeo(ch.X[0], ch.Z[0]).Lat:0.000},{model.Geometry.ToGeo(ch.X[0], ch.Z[0]).Lon:0.000}: narrowest {worst}");
            }
            int ships = sc.Channels.Count(c => c.Ship) - skipped;
            Console.WriteLine($"  {skipped} rivers end above sea level (not checked)");
            foreach (var n in narrow.Take(10)) Console.WriteLine(n);
            Check(ok == ships, $"{ok} of {ships} ship channels have 11 blocks of channel-deep water (the river's own depth, at least {SeaChannels.ShipDepth}) all along");
            // channels v2 (worlds with river ends) are as deep as the river; v1 (older worlds) are ShipDepth for ships
            if (model.Data.LakeOverlay)
                Check(sc.Channels.Where(c => c.Ship).All(c => c.Depth >= SeaChannels.ShipDepth && c.Depth >= Math.Round(model.Rivers.Depth(c.Q))),
                    "ship channels are as deep as their river at its end");
            else
                Check(sc.Channels.Where(c => c.Ship).All(c => c.Depth == SeaChannels.ShipDepth), $"ship channels are {SeaChannels.ShipDepth} deep (channels v1)");
            Check(walls == 0, $"no water walls beside the channels ({walls})");
            Check(sc.NoWay <= 10, $"few river ends find no way out to the sea ({sc.NoWay})");
            foreach (var (name, lat, lon) in new[] { ("Tagus (Lisbon)", 38.784, -9.027), ("Ob", 66.586, 70.248), ("Yenisei", 70.213, 82.672) })
            {
                var ch = sc.Channels.FirstOrDefault(c => { var g = model.Geometry.ToGeo(c.X[0], c.Z[0]); return Math.Abs(g.Lat - lat) < 0.05 && Math.Abs(g.Lon - lon) < 0.05; });
                Check(ch != null, $"{name} has its channel out to sea{(ch != null ? $" ({ch.Length:0} blocks)" : "")}");
            }
            if (!model.Data.LakeOverlay) return; // the channel rules of version 1 (NOENDS=1) stop inside the bar
            // Lisbon: the channel goes on past the bar outside the narrows (1-5 deep at 9.27-9.30°W) to open water
            var tagus = sc.Channels.FirstOrDefault(c => { var g = model.Geometry.ToGeo(c.X[0], c.Z[0]); return Math.Abs(g.Lat - 38.784) < 0.05 && Math.Abs(g.Lon + 9.027) < 0.05; });
            if (tagus != null)
            {
                var end = model.Geometry.ToGeo(tagus.X[^1], tagus.Z[^1]);
                Console.WriteLine($"  Tagus channel: {tagus.Depth} deep (the river {model.Rivers.Depth(tagus.Q):0.0}), to {end.Lat:0.000},{end.Lon:0.000}, {tagus.Length:0} blocks");
                model.Geometry.FromGeo(38.69, -9.22, out double nx, out double nz);
                double fromNarrows = Math.Sqrt(Math.Pow(tagus.X[^1] - nx, 2) + Math.Pow(tagus.Z[^1] - nz, 2));
                Check(fromNarrows > 0.1 * model.Geometry.BlocksPerDegreeZ, $"the Tagus channel at Lisbon goes on past the bar ({fromNarrows:0} blocks from the narrows)");
                // across the bar, north to south at 9.30°W: a ship-wide lane of channel-deep water
                model.Geometry.FromGeo(38.72, -9.30, out double bx, out double bz0);
                model.Geometry.FromGeo(38.60, -9.30, out _, out double bz1);
                int run = 0, best = 0;
                for (double z = bz0; z <= bz1; z += 1)
                {
                    var c = model.GetColumn(bx, z);
                    if (c.Water != TerrainModel.WaterNone && c.WaterTop - c.Top >= tagus.Depth) best = Math.Max(best, ++run); else run = 0;
                }
                Check(best >= 11, $"a lane across the bar off Lisbon is {tagus.Depth} deep ({best} blocks wide)");
            }
        }

        /// <summary>
        /// Lake shores (TerrainModel.LakeShores, shores.bin): around lakes whose land falls away from them (Bolsena's and Bracciano's
        /// outlet valleys) old worlds keep the flat plateau at the lake's level with its cliff; with eased shores the land slopes
        /// down instead, and no lake water stands against lower dry land (no spills). Terrain only, without rivers.
        /// </summary>
        static void CheckLakeShores(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            if (data.Shores == null) { Check(false, "earthdata/shores.bin is there"); return; }
            Console.WriteLine($"lake shores: {data.Shores.Patches.Length} grid cells eased");
            bool was = data.ShoreOverlay;
            var lakes = new[] { ("Lake Bolsena", 42.60, 11.93, 0.35), ("Lake Bracciano", 42.12, 12.23, 0.3), ("Lake Trasimeno", 43.13, 12.10, 0.3),
                ("Lake Geneva", 46.45, 6.55, 0.45), ("Lake Constance", 47.60, 9.40, 0.4), ("Lake Garda", 45.65, 10.68, 0.4), ("Lake Titicaca", -15.85, -69.40, 0.7) };
            // the steepest step between dry neighbours (blocks), and lake water beside lower dry land
            (int step, int spills, int land, double atLat, double atLon) Scan(bool eased, double lat, double lon, double half)
            {
                data.SetShoreOverlay(eased);
                // the eased model as a new world has it (GenFixRules 2: no crease where the noise's dips switch, EA-0040)
                var m = new TerrainModel(data, geo, vs, 12345) { LakeShores = data.ShoreOverlay, GenFixRules = eased ? TerrainModel.GenFixRulesVersion : 0 };
                geo.FromGeo(lat + half, lon - half, out double x0, out double z0);
                geo.FromGeo(lat - half, lon + half, out double x1, out double z1);
                int w = (int)(x1 - x0), h = (int)(z1 - z0), step = 0, spills = 0, land = 0;
                double sx = 0, sz = 0;
                var prev = new TerrainModel.Column[w];
                for (int j = 0; j < h; j++)
                {
                    TerrainModel.Column left = default;
                    for (int i = 0; i < w; i++)
                    {
                        var c = m.GetColumn(x0 + i, z0 + j);
                        if (c.Water == TerrainModel.WaterNone) land++;
                        foreach (var (n, has) in new[] { (left, i > 0), (prev[i], j > 0) })
                        {
                            if (!has) continue;
                            bool cd = c.Water == TerrainModel.WaterNone, nd = n.Water == TerrainModel.WaterNone;
                            if (cd && nd && Math.Abs(c.Top - n.Top) > step) { step = Math.Abs(c.Top - n.Top); sx = x0 + i; sz = z0 + j; }
                            else if (cd && (n.Water == TerrainModel.WaterLake || n.Water == TerrainModel.WaterSaltLake) && c.Top < n.WaterTop) spills++;
                            else if (nd && (c.Water == TerrainModel.WaterLake || c.Water == TerrainModel.WaterSaltLake) && n.Top < c.WaterTop) spills++;
                        }
                        prev[i] = left = c;
                    }
                }
                var at = geo.ToGeo(sx, sz);
                return (step, spills, land, at.Lat, at.Lon);
            }
            foreach (var (name, lat, lon, half) in lakes)
            {
                var old = Scan(false, lat, lon, half);
                var now = Scan(true, lat, lon, half);
                Console.WriteLine($"  {name}: steepest dry step {old.step} -> {now.step} blocks (at {old.atLat:0.000},{old.atLon:0.000} -> {now.atLat:0.000},{now.atLon:0.000}), spills {old.spills} -> {now.spills}");
                Check(now.spills <= old.spills, $"{name}: no more lake water against lower land than before ({now.spills})");
                Check(now.step <= old.step, $"{name}: no steeper steps than before ({now.step})");
                if (name == "Lake Bolsena" || name == "Lake Bracciano")
                {
                    Check(old.step >= 8, $"{name}: old worlds keep the plateau's cliff ({old.step} blocks)");
                    Check(now.step <= 4, $"{name}: with eased shores the land slopes down to the outlet valley ({now.step} blocks at most)");
                }
            }
            data.SetShoreOverlay(was);
        }

        /// <summary>
        /// GenFixRules 3 (EA-0072): on the built-in heights Lake Geneva's water at the Rhône's mouth ends along an irregular
        /// line, not along the straight line where the grid lake's share reaches 0 (a rectangular corner before): the longest
        /// straight run of lake water beside dry land along one row or column, in a window around the mouth, rules 2 and 3.
        /// </summary>
        static void CheckGridLakeEdge(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            bool was = data.ShoreOverlay;
            data.SetShoreOverlay(true);
            int Run(int gen, bool beach)
            {
                var m = new TerrainModel(data, geo, vs, 12345) { LakeShores = data.ShoreOverlay, GenFixRules = gen };
                geo.FromGeo(46.40, 6.86, out double x0, out double z0);
                geo.FromGeo(46.37, 6.94, out double x1, out double z1);
                int w = (int)(x1 - x0), h = (int)(z1 - z0), best = 0;
                var wet = new bool[w + 1, h + 1];
                for (int j = 0; j <= h; j++)
                    for (int i = 0; i <= w; i++)
                    {
                        var c = m.GetColumn(x0 + i, z0 + j);
                        wet[i, j] = c.Water != TerrainModel.WaterNone || beach && c.Beach;
                    }
                for (int j = 0; j < h; j++)
                {
                    int run = 0;
                    for (int i = 0; i <= w; i++) { run = wet[i, j] && !wet[i, j + 1] ? run + 1 : 0; best = Math.Max(best, run); }
                }
                for (int i = 0; i < w; i++)
                {
                    int run = 0;
                    for (int j = 0; j <= h; j++) { run = wet[i, j] && !wet[i + 1, j] ? run + 1 : 0; best = Math.Max(best, run); }
                }
                return best;
            }
            int before = Run(2, false), now = Run(TerrainModel.GenFixRulesVersion, false);
            int beachBefore = Run(2, true), beachNow = Run(TerrainModel.GenFixRulesVersion, true);
            Console.WriteLine($"grid lake edge at the Rhone's mouth: longest straight shore {before} -> {now} blocks, beach's landward edge {beachBefore} -> {beachNow}");
            Check(now <= 12 && now < before, $"Lake Geneva at the Rhone's mouth ends along an irregular line on the built-in heights ({now} blocks straight at most)");
            Check(beachNow <= 12 && beachNow < beachBefore, $"its beach ends along an irregular line too ({beachNow} blocks straight at most)");
            data.SetShoreOverlay(was);
        }

        /// <summary>The test world's model: rivers (with falls and the canals) and their exit channels; with riverEnds as in a new
        /// world from 0.14.1: the terminal lakes overlay, rivers continued downstream, led on to water or faded, channels v2, 6 blocks of
        /// sea at least.</summary>
        static TerrainModel BuildTestModel(EarthData data, EarthGeometry geo, VerticalScale vs, int sea, bool riverEnds, out RiverEnds.Result ends)
        {
            // ISLANDS=1: the island layer as a new world has it (estuary islands kept whole, decision 0013)
            bool islands = Environment.GetEnvironmentVariable("ISLANDS") == "1";
            if (islands) data.SetIslandOverlay(true);
            data.SetLakeOverlay(riverEnds);
            data.SetShoreOverlay(riverEnds);
            // the lakes' tongues past their outlet as land, as a new world has them (OUTLETS=0: the lakes as before)
            data.SetOutletOverlay(riverEnds && Environment.GetEnvironmentVariable("OUTLETS") != "0");
            var model = new TerrainModel(data, geo, vs, 12345)
            {
                GutterSeabedY = sea - 40,
                MinSeaDepth = riverEnds ? TerrainModel.ShipSeaDepth : TerrainModel.OldSeaDepth,
                LakeShores = data.ShoreOverlay,
            };
            if (islands && data.IslandOverlay) model.Islands = new IslandLayer(data.Islands, geo, 4);
            ends = null;
            if (data.Rivers == null) return model;
            // RIVERS_FILE=<path>: another reach file in rivers.bin's format (the tall line's rivers-glo90.bin) in place of rivers.bin
            var source = data.Rivers;
            // COURSES=n: the reaches of that river courses rules (1: rivers-glo90.bin, 2: with rivers-glo90-snap.bin, 3: and rivers-glo90-arms.bin)
            if (int.TryParse(Environment.GetEnvironmentVariable("COURSES"), out int courses) && courses > 0)
            {
                source = data.RiverReaches(courses);
                Console.WriteLine($"rivers of courses rules {courses}: {source.Count} reaches");
            }
            if (Environment.GetEnvironmentVariable("RIVERS_FILE") is string rf && rf.Length > 0)
            {
                using var fs = File.OpenRead(rf);
                source = RiverData.Read(fs);
                Console.WriteLine($"rivers from {rf}: {source.Count} reaches");
            }
            var reaches = OutletData.ReleaseRiverPoints(data, source);
            if (riverEnds)
            {
                ends = RiverEnds.Prepare(data, reaches);
                reaches = ends.Reaches;
            }
            if (Environment.GetEnvironmentVariable("DELTAS") != "0" && riverEnds)
            {
                var deltas = RiverDeltas.Apply(reaches);
                reaches = deltas.Reaches;
                foreach (var line in deltas.Report) Console.WriteLine("  delta " + line);
            }
            model.Rivers = new RiverNetwork(reaches, geo, 100, 1.5, model.RiverWaterTop, Environment.GetEnvironmentVariable("STREAMS") ?? "ends", 20,
                // SIZESCALE=auto: the widths and windows of a world this size, as EarthParams.BuildRivers gives them (ring / 204 800)
                Environment.GetEnvironmentVariable("SIZESCALE") == "auto" ? geo.RingLength / 204800.0 : 1,
                double.Parse(Environment.GetEnvironmentVariable("STRAIGHTEN") ?? "0.5", System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(Environment.GetEnvironmentVariable("SMOOTH") ?? "2", System.Globalization.CultureInfo.InvariantCulture),
                falls: Environment.GetEnvironmentVariable("NOFALLS") == "1" ? null : RiverFalls.All,
                seaLevelPiece: SeaChannels.CanalCrossing(SeaChannels.CanalCourses(geo, Canals)),
                continueDownstream: riverEnds, fadeEnds: ends?.Fade, geometrySeams: riverEnds,
                widthRules: riverEnds && Environment.GetEnvironmentVariable("WIDTHS") != "0" ? RiverNetwork.WidthRulesVersion : 0,
                poolLayout: int.TryParse(Environment.GetEnvironmentVariable("POOLS"), out int pools) ? pools : RiverNetwork.PoolLayoutVersion,
                // RAPIDS=off: the "River rapids: off" world option (no rapids reaches; rapid drops stay)
                rapids: Environment.GetEnvironmentVariable("RAPIDS") != "off");
            // COASTS=1 with the island overlay: islands and outline coasts before the channels, as a world builds them (EarthParams);
            // CHANNELRULES=n: that version of the channel rules
            if (Environment.GetEnvironmentVariable("COASTS") == "1" && data.IslandOverlay)
            {
                model.Islands = new IslandLayer(data.Islands, geo, 4);
                model.Coasts = new CoastLayer(data.Coasts, geo, 4);
            }
            if (Environment.GetEnvironmentVariable("NOCHANNELS") != "1")
                model.Channels = SeaChannels.Build(model, true, Canals, int.TryParse(Environment.GetEnvironmentVariable("CHANNELRULES"), out int chRules) ? chRules
                    : riverEnds ? SeaChannels.Version : 1);
            return model;
        }

        /// <summary>
        /// Seam crossings at a 4 million block ring (Cylinder World's largest Earth, 10 m per block): the river data's longest
        /// pieces are up to ~18 000 blocks there. With geometry seams (worlds with river ends) every real piece is indexed and
        /// none spans the seam; the older 4000-block test (kept for older worlds) drops the long ones.
        /// </summary>
        static void CheckSeamPieces(EarthData data)
        {
            if (data.Rivers == null) return;
            Console.WriteLine("river pieces at a 4 million block ring");
            var inp = new LayoutInput
            {
                WorldWidth = 4_100_000,
                CwRingStart = 50_000, CwRingLength = 4_000_000, CwEquatorZ = 1_024_000, CwPoleDistance = 1_024_000 - 48, CwBandInner = 608,
            };
            var big = EarthLayout.Build(inp, data, out _);
            // every reach with a piece over 4000 blocks, and every reach crossing the seam; each stands alone (no reach below)
            var pick = new List<RiverReach>();
            double expected = 0;
            int longPieces = 0, seamReaches = 0;
            // no real river crosses the seam (it runs through the Pacific): add one across it in the Bering Strait
            double seamLon = big.SeamLon0;
            var across = new RiverReach
            {
                Q = 500, Lat = new[] { 65.90f, 65.91f, 65.92f, 65.93f },
                Lon = new[] { (float)Geo.Wrap180(seamLon - 0.2), (float)Geo.Wrap180(seamLon - 0.05), (float)Geo.Wrap180(seamLon + 0.05), (float)Geo.Wrap180(seamLon + 0.2) },
                Level = new short[4], Fixed = new bool[4],
            };
            foreach (var re in data.Rivers.Append(across))
            {
                var x = new double[re.Lon.Length];
                var z = new double[re.Lon.Length];
                for (int k = 0; k < x.Length; k++) big.FromGeo(re.Lat[k], re.Lon[k], out x[k], out z[k]);
                bool longPiece = false, seam = false;
                double len = 0;
                for (int k = 1; k < x.Length; k++)
                {
                    if (big.AcrossSeam(x[k - 1], x[k])) { seam = true; continue; }
                    double l = Math.Sqrt((x[k] - x[k - 1]) * (x[k] - x[k - 1]) + (z[k] - z[k - 1]) * (z[k] - z[k - 1]));
                    if (l > 4000) { longPiece = true; longPieces++; }
                    len += l;
                }
                if (!longPiece && !seam) continue;
                if (seam) seamReaches++;
                pick.Add(new RiverReach { Q = Math.Max(re.Q, 200), Next = -1, Lon = re.Lon, Lat = re.Lat, Level = re.Level, Fixed = re.Fixed });
                expected += len;
            }
            double scale = big.RingLength / 204800.0;
            var now = new RiverNetwork(pick, big, 100, 1.5, _ => 100, "none", 20, scale, 0, 0, geometrySeams: true).IndexedExtent();
            var old = new RiverNetwork(pick, big, 100, 1.5, _ => 100, "none", 20, scale, 0, 0).IndexedExtent();
            Console.WriteLine($"  {pick.Count} reaches ({longPieces} pieces over 4000 blocks, {seamReaches} crossing the seam): " +
                $"{expected / 1000:0} k blocks; indexed {now.Length / 1000:0} k now, {old.Length / 1000:0} k with the old test");
            Check(longPieces > 0 && Math.Abs(now.Length - expected) <= 1e-4 * expected, "with geometry seams every real piece is indexed");
            Check(seamReaches > 0 && now.MaxDx < big.RingLength / 4, $"no indexed piece spans the seam (widest {now.MaxDx:0} blocks)");
            Check(old.Length < now.Length, "older worlds keep the 4000-block test (long pieces dropped as before)");
        }

        /// <summary>
        /// The climate map's temperature byte (-20..40 °C) holds the real surface temperature plus the altitude compensation;
        /// vanilla reads it back less its own altitude adjustment, cut to -20..40 °C again ((byte - (y - sea) / 1.5) / 4.25
        /// - 20, VS Climate.GetScaledAdjustedTemperatureFloat). With ClimateCorrection added (Earth's climate hook), the
        /// surface gets its real temperature at every world height, also where the stored value doesn't fit (warm highlands
        /// in tall worlds) and beyond -20..40 °C (the Antarctic plateau).
        /// </summary>
        static void CheckClimateStore(EarthData data, EarthGeometry geo)
        {
            Console.WriteLine("climate map temperature at heights 256-1024");
            foreach (int height in new[] { 256, 384, 768, 1024 })
            {
                int sea = (int)(0.4313725490196078 * height);
                var m = new TerrainModel(data, geo, VerticalScale.ForWorld(height, sea), 12345);
                double worstNew = 0, worstOld = 0;
                string where = "";
                foreach (var (name, lat, lon) in new[] { ("Addis Ababa", 9.03, 38.74), ("Bogota", 4.71, -74.07), ("La Paz", -16.5, -68.15),
                    ("Lhasa", 29.65, 91.1), ("Vostok", -78.46, 106.84), ("Death Valley", 36.46, -116.87), ("Everest", 27.988, 86.925), ("Riga", 56.95, 24.1) })
                {
                    geo.FromGeo(lat, lon, out double x, out double z);
                    int b = (m.ClimatePixel(x, z, 0, 1, 1) >> 16) & 255;
                    m.ClimateAt(m.Geometry.ToGeo(x, z).Lat, m.Geometry.ToGeo(x, z).Lon, out double tReal, out _, out double y);
                    double vanilla = (b - Math.Max(0, y - sea) / 1.5) / 4.25 - 20;
                    double real = m.TrueTemperature(x, z, y);
                    vanilla = Math.Clamp(Math.Clamp(b / 4.25 - 20, -20, 40) - (y - sea) / 6.375, -20, 40);
                    double fixedNow = vanilla + m.ClimateCorrection(x, z, y);
                    double fixedOld = vanilla + (real - Math.Clamp(real, -20, 40));
                    if (Math.Abs(fixedNow - real) > worstNew) { worstNew = Math.Abs(fixedNow - real); where = name; }
                    worstOld = Math.Max(worstOld, Math.Abs(fixedOld - real));
                }
                Console.WriteLine($"  height {height}: worst {worstNew:0.00} °C off ({where}); the correction before 0.14.1 was up to {worstOld:0.0} °C off");
                // a deep mine: no "elevation" of tens of kilometres below the seabed curve, the surface's correction below it
                geo.FromGeo(50.0, 10.0, out double mx, out double mz);
                double mine = m.TrueTemperature(mx, mz, 10), surfaceT = m.TrueTemperature(mx, mz, sea);
                m.StoredTemperature(mx, mz, out double mineSurface);
                Check(mine - surfaceT < 4 && Math.Abs(m.ClimateCorrection(mx, mz, 10) - m.ClimateCorrection(mx, mz, mineSurface)) < 1e-9,
                    $"height {height}: a mine at y 10 stays within a few degrees of the surface ({mine:0.0} °C) and keeps the surface's correction");
                Check(worstNew < 1, $"height {height}: real surface temperatures within 1 °C at highlands, plateaus and lowlands");
            }
        }

        /// <summary>
        /// The client in worlds before climate rules v2 (decision 0009): it has no terrain, so it can't know the stored value and
        /// undoes only its own cut to -50..40 °C (Climate.GetScaledAdjustedTemperatureFloatClient). That is the server's reading
        /// wherever the stored value fits the byte; where the byte itself is cut at -20 (the coldest ice) the client reads warm.
        /// Before round 4 it undid the server's -20 cut and counted the cold beyond -20 twice (Vostok about -78 instead of -55).
        /// </summary>
        static void CheckClimateClientV1(EarthData data, EarthGeometry geo, string root)
        {
            Console.WriteLine("climate rules v1: client against server");
            var client = EarthData.LoadFromFolder(root, false);
            client.SetClimateFix(data.ClimateFixApplied);
            foreach (int height in new[] { 256, 384 })
            {
                int sea = (int)(0.4313725490196078 * height);
                var vs = VerticalScale.ForWorld(height, sea);
                var m = new TerrainModel(data, geo, vs, 12345) { ClimateRules = 1 };
                var c = new TerrainModel(client, geo, vs, 12345) { ClimateRules = 1, ClientClimate = true };
                double changedAbove = 0, worstCut = 0, worstBefore = 0;
                foreach (var (name, lat, lon) in new[] { ("Addis Ababa", 9.03, 38.74), ("La Paz", -16.5, -68.15), ("Lhasa", 29.65, 91.1),
                    ("Vostok", -78.46, 106.84), ("Dome A", -80.37, 77.35), ("Summit Greenland", 72.58, -38.46), ("Everest", 27.988, 86.925),
                    ("Oymyakon", 63.46, 142.79), ("Riga", 56.95, 24.1) })
                {
                    geo.FromGeo(lat, lon, out double x, out double z);
                    double stored = m.StoredTemperature(x, z, out double surface);
                    int b = (m.ClimatePixel(x, z, 0, 1, 1) >> 16) & 255;
                    double y = Math.Floor(surface) + 1, real = m.TrueTemperature(x, z, y);
                    double baseT = Math.Clamp(b / 4.25 - 20, -20, 40) - (y - sea) / 6.375;
                    double server = Math.Clamp(baseT, -20, 40) + m.ClimateCorrection(x, z, y);
                    double onClient = Math.Clamp(baseT, -50, 40) + c.ClimateCorrection(x, z, y);
                    double before = Math.Clamp(baseT, -50, 40) + (real - Math.Clamp(real, -20, 40));
                    if (real >= -20) changedAbove = Math.Max(changedAbove, Math.Abs(onClient - before));
                    else
                    {
                        worstCut = Math.Max(worstCut, Math.Abs(onClient - server));
                        worstBefore = Math.Max(worstBefore, Math.Abs(before - server));
                    }
                    Console.WriteLine($"  height {height} {name}: real {real:0.0}, server {server:0.0}, client {onClient:0.0} (before round 4 {before:0.0}){(stored < -20 ? ", stored value cut at -20" : "")}");
                }
                // (above -20 °C nothing changes: the client's v1 reading is vanilla's, exact at the climate's smooth surface)
                Check(changedAbove < 1e-9, $"height {height}: above -20 °C the client reads as before");
                Check(worstCut < worstBefore, $"height {height}: below -20 °C the client is closer to the server than before ({worstCut:0.0} against {worstBefore:0.0} °C)");
            }
        }

        /// <summary>
        /// Climate rules v2 (decision 0009): the climate map stores the sea-level temperature and vanilla's altitude adjustment
        /// (patched, ClimateLapse) subtracts the real lapse at the block's height. Vanilla's own reading is then the real
        /// temperature wherever that lies in -20..40 °C, at every world height (warm highlands of tall worlds included); Earth's
        /// hook adds only the cuts, and a client (climate without terrain) computes the same correction as the server.
        /// </summary>
        static void CheckRealLapse(EarthData data, EarthGeometry geo, string root)
        {
            Console.WriteLine("climate rules v2: real lapse at heights 256-1024");
            var client = EarthData.LoadFromFolder(root, false);
            client.SetClimateFix(data.ClimateFixApplied);
            foreach (int height in new[] { 256, 384, 512, 768, 1024 })
            {
                int sea = (int)(0.4313725490196078 * height);
                var vs = VerticalScale.ForWorld(height, sea);
                var m = new TerrainModel(data, geo, vs, 12345) { ClimateRules = TerrainModel.RealLapseRules };
                var c = new TerrainModel(client, geo, vs, 12345) { ClimateRules = TerrainModel.RealLapseRules, ClientClimate = true };
                double worstVanilla = 0, worstFixed = 0, worstClient = 0, worstSides = 0, coldest = double.MaxValue;
                string where = "";
                foreach (var (name, lat, lon) in new[] { ("Addis Ababa", 9.03, 38.74), ("Bogota", 4.71, -74.07), ("La Paz", -16.5, -68.15),
                    ("Lhasa", 29.65, 91.1), ("Vostok", -78.46, 106.84), ("Death Valley", 36.46, -116.87), ("Everest", 27.988, 86.925), ("Riga", 56.95, 24.1),
                    ("Mexico City", 19.43, -99.13), ("Quito", -0.18, -78.47) })
                {
                    geo.FromGeo(lat, lon, out double x, out double z);
                    var g = m.Geometry.ToGeo(x, z);
                    m.ClimateAt(g.Lat, g.Lon, out _, out _, out double ySurface);
                    int b = (m.ClimatePixel(x, z, 0, 1, 1) >> 16) & 255;
                    // the surface, above it, a mine, and high in the air (below -20 °C everywhere, the cut the two sides make differently)
                    foreach (double y in new[] { Math.Floor(ySurface) + 1, ySurface + 20, 10.0, height - 30.0 })
                    {
                        double real = m.TrueTemperature(x, z, y);
                        coldest = Math.Min(coldest, real);
                        // vanilla's GetScaledAdjustedTemperatureFloat (server, cut to -20..40) and ...FloatClient (client, -50..40) with
                        // the real lapse in byte units (ClimateLapse), each plus that side's Earth correction: the complete readings
                        double lapseBytes = m.LapseAt(y) * 4.25;
                        double vanilla = Math.Clamp((b - lapseBytes) / 4.25 - 20, -20, 40);
                        double vanillaClient = Math.Clamp((b - lapseBytes) / 4.25 - 20, -50, 40);
                        double server = vanilla + m.ClimateCorrection(x, z, y), onClient = vanillaClient + c.ClimateCorrection(x, z, y);
                        if (real > -20 && real < 40 && Math.Abs(vanilla - real) > worstVanilla) { worstVanilla = Math.Abs(vanilla - real); where = name; }
                        worstFixed = Math.Max(worstFixed, Math.Abs(server - real));
                        worstClient = Math.Max(worstClient, Math.Abs(onClient - real));
                        worstSides = Math.Max(worstSides, Math.Abs(onClient - server));
                    }
                }
                Console.WriteLine($"  height {height}: vanilla's own reading worst {worstVanilla:0.00} °C off ({where}); complete readings: server {worstFixed:0.00} °C off, "
                    + $"client {worstClient:0.00} °C off, client and server differ by {worstSides:0.000} °C (down to {coldest:0} °C)");
                Check(worstVanilla < 0.3, $"height {height}: vanilla's reading is the real temperature within the byte's step where it is in -20..40 °C");
                Check(worstFixed < 0.3, $"height {height}: the server's reading with Earth's hook is the real temperature everywhere (Vostok, the mine, high air)");
                // (they differ only by the byte's step: below -20 °C the server's cut hides it, the client's doesn't)
                Check(worstClient < 0.3 && worstSides < 0.3, $"height {height}: the client's reading is the server's within the byte's step, also below -20 °C");
            }
        }

        /// <summary>
        /// CHELSA's temperatures on the 2' grid (temperature.bin, climate rules v2, decision 0009) against summit and valley
        /// stations, next to the 0.25° grid's: at each station's own height, and at the drawn summits of Mauna Loa and Mauna
        /// Kea as EarthWorldGen.BareRock reads them (warmest month at the block's height; below 1 °C nival, below 0.5 °C snow
        /// blocks). The station values are approximate published normals (periods differ; within about 1 °C).
        /// </summary>
        static void CheckFineTemperature(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            Console.WriteLine("CHELSA temperatures (climate rules v2) at stations");
            data.SetFineTemperature(true);
            bool have = data.FineTemperatures;
            data.SetFineTemperature(false);
            if (!have) { Check(false, "earthdata/temperature.bin is there: " + data.FineTemperatureError); return; }
            var m = new TerrainModel(data, geo, vs, 12345) { ClimateRules = TerrainModel.RealLapseRules };
            // name, lat, lon, station height (m), yearly mean and warmest month (°C; NaN where no normal was found)
            var stations = new (string Name, double Lat, double Lon, double H, double Mean, double Warm)[]
            {
                ("Mauna Loa Observatory", 19.536, -155.576, 3397, 7.0, 8.6),   // 1961-1990 slope observatory table
                ("Mauna Kea summit", 19.821, -155.468, 4205, double.NaN, double.NaN),
                ("Hilo", 19.721, -155.048, 11, 23.4, 24.8),
                ("Quito", -0.178, -78.467, 2850, 14.0, 14.5),
                ("La Paz El Alto", -16.513, -68.192, 4061, 6.5, 8.5),
                ("Kilimanjaro summit", -3.076, 37.353, 5895, -7.1, double.NaN),
                ("Sonnblick", 47.054, 12.958, 3106, -4.7, 2.5),
                ("Saentis", 47.250, 9.343, 2502, -1.6, 6.5),
                ("Jungfraujoch", 46.548, 7.985, 3571, -7.0, -0.5),
                ("Geneva", 46.248, 6.128, 411, 10.5, 20.0),
                ("Lhasa", 29.65, 91.13, 3650, 8.5, 16.0),
                ("Kathmandu", 27.70, 85.36, 1337, 18.5, 24.0),
                ("Pyramid lab (Khumbu)", 27.959, 86.813, 5050, -2.5, 3.5),
            };
            double ErrOf(double v, double want) => double.IsNaN(want) ? 0 : Math.Abs(v - want);
            double worstOld = 0, worstNew = 0;
            Console.WriteLine("  station (m): yearly mean old -> CHELSA (normal), warmest month old -> CHELSA (normal)");
            foreach (var st in stations)
            {
                data.SetFineTemperature(false);
                var a = data.SampleClimate(st.Lat, st.Lon);
                data.SetFineTemperature(true);
                var b = data.SampleClimate(st.Lat, st.Lon);
                double lapse = 0.0065 * st.H;
                double oldMean = a.TMean - lapse, newMean = b.TMean - lapse, oldWarm = a.TWarm - lapse, newWarm = b.TWarm - lapse;
                Console.WriteLine($"  {st.Name} ({st.H:0}): {oldMean:0.0} -> {newMean:0.0} ({(double.IsNaN(st.Mean) ? "-" : st.Mean.ToString("0.0"))}), "
                    + $"{oldWarm:0.0} -> {newWarm:0.0} ({(double.IsNaN(st.Warm) ? "-" : st.Warm.ToString("0.0"))})");
                worstOld = Math.Max(worstOld, Math.Max(ErrOf(oldMean, st.Mean), ErrOf(oldWarm, st.Warm)));
                worstNew = Math.Max(worstNew, Math.Max(ErrOf(newMean, st.Mean), ErrOf(newWarm, st.Warm)));
            }
            Console.WriteLine($"  worst against the normals: old {worstOld:0.0} °C, CHELSA {worstNew:0.0} °C");
            Check(worstNew < 3, "CHELSA within 3 °C of every station normal at the station's height");

            // the drawn summits as BareRock sees them: the highest column within 150 blocks, the climate at its chunk's centre
            foreach (var (name, lat, lon) in new[] { ("Mauna Loa", 19.475, -155.608), ("Mauna Kea", 19.821, -155.468) })
            {
                geo.FromGeo(lat, lon, out double x0, out double z0);
                double bx = x0, bz = z0; int top = int.MinValue;
                for (int dz = -150; dz <= 150; dz += 2)
                    for (int dx = -150; dx <= 150; dx += 2)
                    {
                        var c = m.GetColumn(x0 + dx, z0 + dz);
                        if (c.Top > top) { top = c.Top; bx = x0 + dx; bz = z0 + dz; }
                    }
                var g = m.TerrainGeoAt(Math.Floor(bx / 32) * 32 + 16, Math.Floor(bz / 32) * 32 + 16);
                data.SetFineTemperature(false);
                double oldWarm = data.SampleClimate(g.Lat, g.Lon).TWarm - 0.0065 * vs.Elevation(top);
                data.SetFineTemperature(true);
                double newWarm = data.SampleClimate(g.Lat, g.Lon).TWarm - 0.0065 * vs.Elevation(top);
                Console.WriteLine($"  {name} drawn summit y={top} ({vs.Elevation(top):0} m): warmest month {oldWarm:0.0} -> {newWarm:0.0} °C");
                Check(newWarm >= 1, $"{name}: no permanent snow on the drawn summit with CHELSA (warmest month {newWarm:0.0} °C, nival below 1)");
            }

            // north of 84°N CHELSA has nothing: the coarse grid's values, and no step where the two meet
            data.SetFineTemperature(true);
            var north = data.SampleClimate(87, 20);
            data.SetFineTemperature(false);
            var northOld = data.SampleClimate(87, 20);
            Check(north.TMean == northOld.TMean && north.TWarm == northOld.TWarm, "north of 84°N the climate grid's temperatures stay");
            // (measured against CHELSA's own sharpest change per 0.01° in a band of the same width further south: ice margins and
            // coasts change that fast in the data itself)
            data.SetFineTemperature(true);
            double MaxStep(double north, double south)
            {
                double max = 0;
                for (double lon = -180; lon < 180; lon += 0.5)
                {
                    double prev = data.SampleClimate(north, lon).TMean;
                    for (double lat = north - 0.01; lat >= south; lat -= 0.01)
                    {
                        double t = data.SampleClimate(lat, lon).TMean;
                        max = Math.Max(max, Math.Abs(t - prev));
                        prev = t;
                    }
                }
                return max;
            }
            double seamStep = MaxStep(84.2, 83.4), ownStep = MaxStep(61, 60.2);
            Check(seamStep <= ownStep, $"no step where CHELSA fades into the climate grid at 83-84°N: largest change per 0.01° {seamStep:0.00} °C, "
                + $"CHELSA's own at 60-61°N {ownStep:0.00} °C");
            data.SetFineTemperature(false);
        }

        /// <summary>
        /// What a world reads of the data is a frozen view (EarthData.View): switching a choice publishes a new view and leaves
        /// the old one as it was (its tiles, kept lake levels and climate), switching back reads the same as before, a choice
        /// that stays the same keeps the view, a climate choice keeps the decoded terrain, and every climate fix is against
        /// climate.bin itself. Leaves the data's choices as it found them.
        /// </summary>
        static void CheckDataViews(EarthData data)
        {
            Console.WriteLine("data views");
            var start = data.View;
            // the Aral Sea (a terminal lake), Victoria Falls, the Rhine in the Netherlands, a Bahamas reef, the Alps, Lop Nur
            var places = new[] { (45.0, 60.0), (44.7, 59.5), (-17.92, 25.86), (51.85, 5.9), (24.4, -77.9), (46.5, 8.0), (40.5, 90.3) };
            string Terrain(EarthDataView v) => string.Join(";", places.Select(p =>
            {
                var s = v.Terrain.SampleTerrain(p.Item1, p.Item2, true);
                return string.Join(",", new[] { s.Elev, s.Ocean, s.Lake, s.LakeLevel, s.ShoreLevel, s.LakeNear, s.SeaNear, s.LandElev, s.SeaElev,
                    v.Terrain.KeptLakeLevelNear(p.Item1, p.Item2), v.Terrain.TerminalShoreWeight(p.Item1, p.Item2) }.Select(d => d.ToString("R", CultureInfo.InvariantCulture)));
            }));
            string Climate(EarthDataView v) => string.Join(";", places.Select(p =>
            {
                var c = v.Climate.SampleClimate(p.Item1, p.Item2);
                return string.Join(",", new[] { c.TMean, c.TCold, c.TWarm, c.Precip, c.Rain, c.DrySummer, c.PenmanFactor }.Select(d => d.ToString("R", CultureInfo.InvariantCulture)));
            }));
            string t0 = Terrain(start), c0 = Climate(start);

            data.SetIslandOverlay(data.IslandOverlay);
            Check(ReferenceEquals(start, data.View), "a choice that stays the same keeps the view");

            if (data.TerminalLakes != null)
            {
                data.SetLakeOverlay(!start.Choice.LakeOverlay);
                var other = data.View;
                Check(!ReferenceEquals(start.Terrain, other.Terrain) && other.Choice.LakeOverlay != start.Choice.LakeOverlay, "switching the terminal lakes makes a new terrain view");
                Check(Terrain(other) != t0, "the terminal lakes change the terrain read at the Aral Sea and Lop Nur");
                Check(Terrain(start) == t0, "the view before the switch keeps its terrain and kept lake levels");
                data.SetLakeOverlay(start.Choice.LakeOverlay);
                Check(Terrain(data.View) == t0, "switching back reads the same terrain");
            }

            var beforeRain = data.View;
            data.SetFineRain(!start.Choice.FineRain);
            Check(ReferenceEquals(beforeRain.Terrain, data.View.Terrain), "a climate choice keeps the decoded terrain");
            data.SetFineRain(start.Choice.FineRain);

            data.SetClimateFix(0);
            string none = Climate(data.View);
            data.SetClimateFix(EarthData.ClimateFixVersion);
            string newest = Climate(data.View);
            data.SetClimateFix(0);
            Check(Climate(data.View) == none, "every climate fix is against climate.bin itself: no fix after one reads climate.bin's own");
            data.SetClimateFix(EarthData.ClimateFixVersion);
            Check(Climate(data.View) == newest, "and the fix again reads the same");
            data.SetClimateFix(start.Choice.ClimateFix);
            Check(Climate(start) == c0 && Climate(data.View) == c0, "the view before keeps its climate, and switching back reads it");
            Check(data.View.Choice == start.Choice, "the choices are as they were");
        }

        /// <summary>A reach of the river data by where it ends (within 0.01°): a terminal one, or one with a reach below; -1 if none.</summary>
        static int ReachEndingAt(List<RiverReach> reaches, double lat, double lon, bool terminal = true)
        {
            int best = -1;
            for (int r = 0; r < reaches.Count; r++)
            {
                var re = reaches[r];
                if ((re.Next < 0) != terminal) continue;
                // the biggest of the reaches ending there (a side channel may end at the same point)
                if (Math.Abs(re.Lat[^1] - lat) < 0.01 && Math.Abs(re.Lon[^1] - lon) < 0.01 && (best < 0 || re.Q > reaches[best].Q)) best = r;
            }
            return best;
        }

        /// <summary>Whether standing water (lake or sea, not a river) is within `radius` blocks of (x, z).</summary>
        static bool StandingWaterNear(TerrainModel model, double x, double z, double radius)
        {
            for (double r = 0; r <= radius; r += 2)
                for (int a = 0; a < 32; a++)
                {
                    var c = model.GetColumn(x + r * Math.Cos(a * Math.PI / 16), z + r * Math.Sin(a * Math.PI / 16));
                    if (c.Water != TerrainModel.WaterNone && !c.River) return true;
                }
            return false;
        }

        /// <summary>
        /// River ends (RiverEnds, EarthParams.RiverEnds): the terminal lakes are there at their level; the rivers that ended in a
        /// dry basin end in their lake (the Amu Darya in the Aral Sea, the Tarim in Lop Nur, ...), are led on to the sea (rivers
        /// HydroRIVERS stops at the head of a fjord) or fade out (wadis); rivers losing water downstream continue.
        /// </summary>
        static void CheckRiverEnds(TerrainModel model, RiverEnds.Result ends)
        {
            if (ends == null || model.Rivers == null) return;
            var rv = model.Rivers;
            var data = model.Data;
            var geo = model.Geometry;
            // the reaches the model was built from (COURSES, as BuildModel reads it), before RiverEnds: ends.Ends counts in their
            // numbering, which is rivers.bin's up to courses rules 2 and not from rules 3 (the side arms come first)
            var orig = int.TryParse(Environment.GetEnvironmentVariable("COURSES"), out int origCourses) && origCourses > 0
                ? data.RiverReaches(origCourses) : data.Rivers;
            Console.WriteLine($"river ends: {string.Join(", ", ends.Ends.GroupBy(e => e.Value).Select(g => $"{g.Count()} {g.Key}"))}; {ends.LakeVertices} river vertices in terminal lakes");
            Check(data.TerminalLakes != null && data.LakeOverlay, "earthdata/lakes.bin is there and on");
            if (data.TerminalLakes == null) return;
            foreach (var lake in data.TerminalLakes.Lakes)
            {
                geo.FromGeo(lake.Lat, lake.Lon, out double x, out double z);
                bool water = false;
                int wtop = -1;
                for (int k = -8; k <= 8 && !water; k += 2)
                {
                    var c = model.GetColumn(x + 8 * k, z);
                    if (c.Water == TerrainModel.WaterLake || c.Water == TerrainModel.WaterSaltLake) { water = true; wtop = c.WaterTop; }
                }
                Check(water, $"{lake.Name} ({(lake.Revived ? "revived" : "natural")}, {lake.Level} m): lake water at its seed (water top {wtop})");
            }
            // green shores: the land by the revived Aral Sea is greener out to ~40 km, a salt pan's shore is not
            foreach (var (name, lat, lon, dLat, green) in new[] { ("Aral Sea", 45.0, 59.5, -0.01, true), ("Lake Chad", 13.3, 14.0, 0.01, true),
                ("Kati Thanda-Lake Eyre", -28.4, 137.3, 0.01, false) })
            {
                // from the seed out to the first dry land, then 5 km and 80 km further
                double la = lat;
                for (int i = 0; i < 400; i++, la += dLat)
                {
                    geo.FromGeo(la, lon, out double sx, out double sz);
                    if (model.GetColumn(sx, sz).Water == TerrainModel.WaterNone) break;
                }
                double At(double l) => data.TerminalShoreWeight(l, lon);
                double near = At(la + Math.Sign(dLat) * 0.05), far = At(la + Math.Sign(dLat) * 0.72);
                Console.WriteLine($"  {name} shore at {la:0.00}: green {near:0.00} 5 km out, {far:0.00} 80 km out");
                if (green) Check(near >= 0.5 && far == 0, $"{name}: green shore, easing out into the dry land");
                else Check(near == 0, $"{name}: a salt pan's shore stays as dry as it is");
            }
            var led = new[] { RiverEnds.End.IntoLake, RiverEnds.End.Led };
            var named = new (string Name, double Lat, double Lon, RiverEnds.End[] Kinds)[]
            {
                ("Amu Darya into the Aral Sea", 44.906, 59.715, led),
                ("Helmand into the Hamun lakes", 31.490, 61.752, led),
                ("Tarim into Lop Nur", 40.202, 90.373, led),
                ("Kor into Tashk", 29.740, 53.544, led),
                ("Manas into Manas Lake", 45.810, 85.969, led),
                ("Diamantina into Lake Eyre", -28.427, 137.206, led),
                ("Sturt Creek into Paraku", -20.215, 127.452, led),
                ("Lauca into Coipasa", -19.185, -68.127, led),
                ("Boteti into Makgadikgadi", -20.940, 25.952, led),
                ("Qom river into Namak Lake", 34.706, 51.823, led),
                ("Namsen to the sea at Namsos", 64.477, 11.644, new[] { RiverEnds.End.Led }),
                ("Angermanalven to the Gulf of Bothnia", 63.165, 17.615, new[] { RiverEnds.End.Led }),
                ("Hawkesbury to Broken Bay", -33.473, 151.152, new[] { RiverEnds.End.Led }),
                ("Derwent to Hobart", -42.744, 147.185, new[] { RiverEnds.End.Led }),
                ("Wadi ar-Rummah fades in the sand", 26.773, 44.235, new[] { RiverEnds.End.Faded }),
            };
            foreach (var (name, lat, lon, kinds) in named)
            {
                int r = ReachEndingAt(orig, lat, lon);
                if (r < 0) { Check(false, $"{name}: its reach is in the data"); continue; }
                var kind = ends.Ends.TryGetValue(r, out var k) ? k : RiverEnds.End.Water;
                string at = "";
                bool ok = kinds.Contains(kind) && rv.Kept(r);
                if (ok && rv.ReachLine(r, out var lx, out var lz))
                {
                    if (kind == RiverEnds.End.Faded)
                    {
                        rv.Nearest(lx[^1], lz[^1], out var hit);
                        ok = hit.Q <= rv.StreamMinQ * 1.5;
                        at = $", {hit.Q:0} m³/s at its end";
                    }
                    else
                    {
                        ok = StandingWaterNear(model, lx[^1], lz[^1], 2 * rv.HalfWidth(orig[r].Q) + 48);
                        if (!ok) at = ", no lake or sea at its end";
                    }
                }
                Check(ok, $"{name}: {kind}{at}");
            }
            foreach (var (name, lat, lon) in new[] { ("Darling below Menindee", -31.740, 143.248), ("Niger below Bamako", 12.531, -6.685),
                ("James River (Dakota)", 43.890, -97.990), ("Rio Grande to the Gulf", 26.023, -97.719), ("Colorado to the Gulf of California", 32.052, -115.156) })
            {
                int r = ReachEndingAt(orig, lat, lon, false);
                Check(r >= 0 && rv.Kept(r) && rv.Kept(orig[r].Next), $"{name}: the river continues where its discharge dips");
            }
            // no river shown ends at full width away from water any more
            int dead = 0;
            var deadList = new List<string>();
            for (int r = 0; r < rv.Reaches.Count; r++)
            {
                var re = rv.Reaches[r];
                if (!rv.Kept(r) || (re.Next >= 0 && rv.Kept(re.Next)) || ends.Fade[r]) continue;
                if (ends.Ends.TryGetValue(r, out var k) && k == RiverEnds.End.Antimeridian) continue;
                if (!rv.ReachLine(r, out var lx, out var lz)) continue;
                if (StandingWaterNear(model, lx[^1], lz[^1], 2 * rv.HalfWidth(re.Q) + 48)) continue;
                dead++;
                if (deadList.Count < 15) deadList.Add($"    reach {r} Q {re.Q:0} ends {re.Lat[^1]:0.000},{re.Lon[^1]:0.000} level {re.Level[^1]} fixed {re.Fixed[^1]} {(ends.Ends.TryGetValue(r, out var e) ? e.ToString() : "")}");
            }
            foreach (var d in deadList) Console.WriteLine(d);
            Check(dead == 0, $"no river ends at full width away from water ({dead})");
        }

        /// <summary>Renders of the terminal lakes and river ends, before (a world without river ends) and after.</summary>
        static void RenderEnds(EarthData data, EarthGeometry geo, VerticalScale vs, int sea, string outDir)
        {
            var places = new (string, double, double, int, int, double)[]
            {
                ("aral", 45.0, 60.0, 1200, 1000, 2), ("lopnur", 40.3, 90.2, 1000, 800, 1), ("hamun", 31.0, 61.5, 900, 900, 1),
                ("eyre", -28.4, 137.4, 1000, 1000, 1.5), ("chad", 13.3, 14.2, 1200, 1000, 1.5), ("lisbon", 38.7, -9.2, 900, 600, 0.5),
                ("namsos", 64.45, 11.6, 700, 600, 0.5), ("rummah", 26.8, 44.0, 900, 600, 1),
            };
            foreach (bool after in new[] { false, true })
            {
                var m = BuildTestModel(data, geo, vs, sea, after, out _);
                foreach (var (name, lat, lon, w, h, step) in places) RenderAt(m, geo, outDir, $"ends-{name}-{(after ? "after" : "before")}", lat, lon, w, h, step);
                // rainfall with the green belts (desert tan to green at VS rain 145), water blue
                foreach (var (name, lat, lon, half) in new[] { ("aral", 45.0, 60.0, 3.0), ("chad", 13.3, 14.2, 2.0) })
                {
                    int w = 800, h = 600;
                    var rgb = new byte[w * h * 3];
                    for (int j = 0; j < h; j++)
                        for (int i = 0; i < w; i++)
                        {
                            double la = lat + half * 0.75 * (1 - 2.0 * j / h), lo = lon + half * (2.0 * i / w - 1);
                            geo.FromGeo(la, lo, out double px, out double pz);
                            var c = m.GetColumn(px, pz);
                            int k = 3 * (j * w + i);
                            if (c.Water != TerrainModel.WaterNone) { rgb[k] = 70; rgb[k + 1] = 120; rgb[k + 2] = 200; continue; }
                            double t = Math.Clamp(m.RiparianRain(px, pz, data.SampleClimate(la, lo).Rain) / 145, 0, 1);
                            rgb[k] = (byte)(210 - 150 * t); rgb[k + 1] = (byte)(185 - 45 * t); rgb[k + 2] = (byte)(120 - 60 * t);
                        }
                    Png.WriteRgb(Path.Combine(outDir, $"ends-rain-{name}-{(after ? "after" : "before")}.png"), w, h, rgb);
                }
            }
        }

        /// <summary>The canals the test world has (both, unless CANALS says otherwise, e.g. CANALS= for none).</summary>
        static readonly string[] Canals = (Environment.GetEnvironmentVariable("CANALS") ?? "suez,panama").Split(',', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// The sea-level canals: a carrack's width (11 blocks) of ship-deep water from sea to sea along each course, their lakes at
        /// sea level, and no water standing above lower land (water walls) anywhere around them (rivers crossing come down).
        /// </summary>
        /// <summary>A canal in a world without rivers: no river exits to route (the parallel search was handed an empty range
        /// and threw, Codex review 3), the canal still made.</summary>
        static void CheckCanalsWithoutRivers(EarthData data, EarthGeometry geo, VerticalScale vs)
        {
            var m = new TerrainModel(data, geo, vs, 12345);
            SeaChannels sc = null;
            try { sc = SeaChannels.Build(m, true, new[] { "suez" }, SeaChannels.Version); }
            catch (Exception e) { Console.WriteLine("  canal without rivers: " + e.GetType().Name + ": " + e.Message); }
            Check(sc != null && sc.Channels.Any(c => c.Canal && !c.Lake), "a canal without rivers: the model's channels build, with the canal");
        }

        static void CheckCanals(TerrainModel model)
        {
            if (model.Channels == null) return;
            foreach (var ch in model.Channels.Channels.Where(c => c.Canal && !c.Lake))
            {
                int worst = int.MaxValue, walls = 0, cols = 0;
                double minX = ch.X.Min() - 200, maxX = ch.X.Max() + 200, minZ = ch.Z.Min() - 200, maxZ = ch.Z.Max() + 200;
                for (int i = 1; i < ch.X.Length; i++)
                {
                    double dx = ch.X[i] - ch.X[i - 1], dz = ch.Z[i] - ch.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                    double nx = -dz / len, nz = dx / len;
                    for (double u = 0; u < len; u += 2)
                    {
                        double x = ch.X[i - 1] + dx * u / len, z = ch.Z[i - 1] + dz * u / len;
                        int run = 0, best = 0;
                        for (int k = -60; k <= 60; k++)
                        {
                            var c = model.GetColumn(x + nx * k, z + nz * k);
                            if (c.Water != TerrainModel.WaterNone && c.WaterTop - c.Top >= SeaChannels.ShipDepth) best = Math.Max(best, ++run); else run = 0;
                        }
                        worst = Math.Min(worst, best);
                    }
                }
                // water walls: in a band around the canal and its lakes, water whose neighbour is land below its surface
                var lakes = model.Channels.Channels.Where(c => c.Lake && c.Name == ch.Name).ToList();
                for (double z = minZ; z <= maxZ; z += 1)
                {
                    for (double x = minX; x <= maxX; x += 1)
                    {
                        double dc = double.MaxValue;
                        for (int i = 1; i < ch.X.Length; i++)
                        {
                            double dx = ch.X[i] - ch.X[i - 1], dz = ch.Z[i] - ch.Z[i - 1], l2 = dx * dx + dz * dz;
                            double tt = Math.Clamp(((x - ch.X[i - 1]) * dx + (z - ch.Z[i - 1]) * dz) / l2, 0, 1);
                            dc = Math.Min(dc, Math.Sqrt(Math.Pow(ch.X[i - 1] + tt * dx - x, 2) + Math.Pow(ch.Z[i - 1] + tt * dz - z, 2)));
                        }
                        foreach (var l in lakes) dc = Math.Min(dc, Math.Sqrt(Math.Pow(l.X[0] - x, 2) + Math.Pow(l.Z[0] - z, 2)) - l.HalfWidth * 1.2);
                        if (dc > ch.HalfWidth + 40) continue;
                        var c = model.GetColumn(x, z);
                        // (river banks beside a ramp are raised by the world generator's settling, SettleSteps)
                        if (c.Water == TerrainModel.WaterNone || c.River) continue;
                        cols++;
                        foreach (var (ax, az) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            var d = model.GetColumn(x + ax, z + az);
                            if (d.Water == TerrainModel.WaterNone && d.Top < c.WaterTop + (c.Ramp > 0 ? 1 : 0) && ++walls <= 6 && Environment.GetEnvironmentVariable("RIVERDEBUG") == "1") { var gw = model.Geometry.ToGeo(x, z); Console.WriteLine($"     wall at {gw.Lat:0.0000},{gw.Lon:0.0000}: water {c.Water} river {c.River} wtop {c.WaterTop} ramp {c.Ramp} / land top {d.Top} elev {d.Elev:0.0} beach {d.Beach}"); }
                        }
                    }
                }
                Console.WriteLine($"  {ch.Name}: {ch.Length:0} blocks, narrowest {worst} blocks of {SeaChannels.ShipDepth}-deep water, {cols} water columns around it, {walls} water walls");
                Check(worst >= 11, $"{ch.Name}: a carrack can sail through from sea to sea");
                Check(walls == 0, $"{ch.Name}: no water walls around it");
                foreach (var l in lakes)
                {
                    var c = model.GetColumn(l.X[0], l.Z[0]);
                    Check(c.Water == TerrainModel.WaterSea && c.WaterTop == model.SeaLevel - 1, $"{ch.Name}: its lake at {model.Geometry.ToGeo(l.X[0], l.Z[0]).Lat:0.00},{model.Geometry.ToGeo(l.X[0], l.Z[0]).Lon:0.00} is sea water at sea level");
                }
            }
        }

        /// <summary>
        /// Small islands from their outlines (islands.bin, EarthParams.Islands) in worlds with rings of 204 800, 512 000 and
        /// 4 000 000 blocks: ETOPO's false islands on the Great Bahama Bank's rims are sea, the real islands are land, more
        /// of them show the bigger the world, a 0.04 km² cay comes out at its true size where it is wide enough, shores
        /// have beaches and the sea floor comes up to them, and old saves (no overlay) still see the grid as it was.
        /// With renders, writes before/after maps of the Bahamas for each world size to tests/out/.
        /// </summary>
        /// <summary>
        /// The upper Tagus estuary (decision 0013): the river's centre line runs down the estuary over small islands; with
        /// island rules v3 none of their land is cut (rules v2 cut a straight strip). The Mekong's delta split: the Bassac and
        /// the Tiền (to the Cổ Chiên mouth) carry half each.
        /// </summary>
        static void CheckEstuaryAndDeltas(EarthData data, EarthGeometry geo, VerticalScale vs, int sea)
        {
            Console.WriteLine("estuary islands and delta branches");
            bool overlay = data.IslandOverlay;
            try
            {
                data.SetIslandOverlay(true);
                var model = BuildTestModel(data, geo, vs, sea, true, out _);
                int cut3 = 0, cut2 = 0, island = 0;
                foreach (int rules in new[] { 3, 2 })
                {
                    model.Islands = new IslandLayer(data.Islands, geo, 4, rules);
                    for (double lat = 38.86; lat <= 38.925; lat += 0.002)
                    {
                        for (double lon = -9.03; lon <= -8.98; lon += 0.0005)
                        {
                            geo.FromGeo(lat, lon, out double x, out double z);
                            var c = model.GetColumn(x, z);
                            if (!model.Islands.IsLand(c.Lat, c.Lon) || data.SampleTerrain(c.Lat, c.Lon, true).Ocean < 0.5) continue;
                            if (rules == 3) island++;
                            if (c.River || c.Water != TerrainModel.WaterNone)
                            {
                                if (rules == 3 && cut3 < 8) Console.WriteLine($"  cut island sample {lat:0.0000},{lon:0.0000}: river {c.River}, water {c.Water}, top {c.Top}, island over sea {c.IslandOverSea}");
                                if (rules == 3) cut3++; else cut2++;
                            }
                        }
                    }
                }
                Console.WriteLine($"  Tagus estuary islands: {island} island samples over the grid's sea, cut by the river: {cut3} (rules v3), {cut2} (rules v2)");
                Check(island > 50 && cut3 == 0, "rules v3: the Tagus's islands stay whole");
                Check(cut2 > 0, "rules v2 (older worlds) unchanged: the river cut through them");
            }
            finally { data.SetIslandOverlay(overlay); }

            if (data.Rivers == null) return;
            var res = RiverDeltas.Apply(data.Rivers);
            foreach (var line in res.Report) Console.WriteLine("  " + line);
            float MouthQ(double lat, double lon)
            {
                float q = 0;
                double best = 0.08;
                foreach (var re in res.Reaches)
                {
                    int n = re.Lat.Length;
                    double d = Math.Abs(re.Lat[n - 1] - lat) + Math.Abs(re.Lon[n - 1] - lon);
                    if (re.Next < 0 && d < best) { best = d; q = re.Q; }
                }
                return q;
            }
            float bassac = MouthQ(9.487, 106.332), tien = MouthQ(9.852, 106.496);
            Console.WriteLine($"  Mekong: Bassac {bassac:0} m³/s, Tiền (Cổ Chiên) {tien:0} m³/s at their mouths");
            Check(bassac > 6000 && bassac < 9000 && tien > 6000 && tien < 9000, "the Mekong's Bassac and Tiền share its water");
            Check(res.Report.TrueForAll(l => !l.Contains("no river")), "every delta split and branch is found in the river data");
        }

        static EarthGeometry RingGeometry(EarthData data, double ring)
        {
            var inp = new LayoutInput
            {
                WorldWidth = (int)(ring * 1.25),
                CwRingStart = ring / 8, CwRingLength = ring, CwEquatorZ = ring / 4, CwPoleDistance = ring / 4 - 48, CwBandInner = 608,
            };
            return EarthLayout.Build(inp, data, out _);
        }

        /// <summary>A new world's terrain model at any ring size (islands v3, river ends, deltas, channels; widthRules as given).</summary>
        static TerrainModel RingModel(EarthData data, VerticalScale vs, double ring, int widthRules, out EarthGeometry geo)
        {
            geo = RingGeometry(data, ring);
            data.SetIslandOverlay(true);
            data.SetLakeOverlay(true);
            data.SetShoreOverlay(true);
            var m = new TerrainModel(data, geo, vs, 12345)
            {
                GutterSeabedY = vs.SeaLevel - 40, GutterBridges = true, MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = data.ShoreOverlay,
            };
            m.Islands = new IslandLayer(data.Islands, geo, 4);
            // (the big land's outline coasts with RINGCOASTS=1, as a new world has them where the grid's cells are 32+ blocks wide)
            if (Environment.GetEnvironmentVariable("RINGCOASTS") == "1") m.Coasts = new CoastLayer(data.Coasts, geo, 4);
            if (data.Rivers == null) return m;
            var ends = RiverEnds.Prepare(data, data.Rivers);
            var reaches = RiverDeltas.Apply(ends.Reaches).Reaches;
            // (no canals unless RINGCANALS names them, e.g. RINGCANALS=suez,panama)
            var canals = Environment.GetEnvironmentVariable("RINGCANALS")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            m.Rivers = new RiverNetwork(reaches, geo, 100, 1.5, m.RiverWaterTop, "ends", 20, ring / 204800, 0.5, 2, falls: RiverFalls.All,
                seaLevelPiece: SeaChannels.CanalCrossing(SeaChannels.CanalCourses(geo, canals)),
                continueDownstream: true, fadeEnds: ends.Fade, geometrySeams: true, widthRules: widthRules);
            m.Channels = SeaChannels.Build(m, true, canals, SeaChannels.Version);
            return m;
        }

        /// <summary>
        /// River widths (decision 0014): with width rules 1 the standard world's gameplay widths are a floor in blocks and a river is
        /// as wide as it really is (about 8·√Q m) where that is wider. Worlds up to the standard ring are unchanged; widths never
        /// shrink with more water; on a 4 million block ring the Amazon is at its real width and small rivers keep the floor.
        /// </summary>
        static void CheckRiverWidths(EarthData data)
        {
            Console.WriteLine("river widths (water width in blocks; old rules / new rules)");
            var qs = new[] { 20.0, 50, 100, 300, 650, 1000, 2300, 7500, 15000, 40000, 200000 };
            Console.WriteLine("  ring       m/block  " + string.Join("", qs.Select(q => $"{q,14:0}")));
            bool sameSmall = true, monotonic = true;
            double amazon4M = 0, daugava4M = 0, small4M = 0, old4M = 0;
            foreach (double ring in new[] { 102400.0, 204800, 512000, 1000000, 4000000 })
            {
                var geo = RingGeometry(data, ring);
                var none = new List<RiverReach>();
                var oldNet = new RiverNetwork(none, geo, 100, 1.5, l => 0, "ends", 20, ring / 204800, widthRules: 0);
                var newNet = new RiverNetwork(none, geo, 100, 1.5, l => 0, "ends", 20, ring / 204800, widthRules: RiverNetwork.WidthRulesVersion);
                var sb = new System.Text.StringBuilder($"  {ring,9:0}  {newNet.MetresPerBlock,7:0.0}  ");
                double prev = 0;
                foreach (double q in qs)
                {
                    double o = 2 * oldNet.HalfWidth(q), n = 2 * newNet.HalfWidth(q);
                    sb.Append($"{o,6:0}/{n,-6:0} ".PadLeft(14));
                    if (ring <= 204800 && Math.Abs(o - n) > 1e-9) sameSmall = false;
                    if (n < prev - 1e-9) monotonic = false;
                    prev = n;
                    if (ring == 4000000)
                    {
                        if (q == 200000) { amazon4M = n; old4M = o; }
                        if (q == 650) daugava4M = n;
                        if (q == 100) small4M = n;
                    }
                }
                Console.WriteLine(sb);
            }
            Check(sameSmall, "worlds up to the standard 204 800 ring keep their widths");
            Check(monotonic, "a river with more water is never narrower");
            Check(amazon4M > 300 && amazon4M < old4M, $"4M ring: the Amazon at its real width ({amazon4M:0} blocks, was {old4M:0})");
            Check(Math.Abs(daugava4M - 21) < 1.5 && Math.Abs(small4M - 12) < 1, $"4M ring: small rivers keep the standard world's widths (Daugava {daugava4M:0}, 100 m³/s {small4M:0})");
        }

        static void CheckIslands(EarthData data, VerticalScale vs, string outDir, bool renders)
        {
            Console.WriteLine("islands");
            if (data.Islands == null) { Check(false, "earthdata/islands.bin is there"); return; }
            Console.WriteLine($"  {data.Islands.Islands.Length} islands, {data.Islands.Patches.Length} grid patches");
            int sea = vs.SeaLevel;
            // ETOPO's island chains on the Great Bahama Bank's southern rim and around the Tongue of the Ocean (2' grid cells
            // that were land), where there is only reef
            var falseLand = new[] { (23.45, -78.85), (23.21, -78.72), (23.12, -78.68), (23.05, -78.58), (22.82, -78.33), (22.37, -77.53),
                (22.30, -77.08), (22.32, -76.80), (22.27, -77.25), (22.25, -77.37), (23.95, -76.88), (23.95, -76.72), (23.65, -76.52), (23.48, -76.55) };
            var realLand = new[] { ("Andros", 24.70, -78.00), ("New Providence", 25.04, -77.35), ("Grand Bahama", 26.62, -78.40), ("Cuba", 22.0, -79.5) };
            bool before = data.IslandOverlay;
            try
            {
                var counts = new List<int>();
                foreach (double ring in new[] { 204800.0, 512000.0, 4000000.0 })
                {
                    var inp = new LayoutInput
                    {
                        WorldWidth = (int)(ring * 1.25),
                        CwRingStart = ring / 8, CwRingLength = ring, CwEquatorZ = ring / 4, CwPoleDistance = ring / 4 - 48, CwBandInner = 608,
                    };
                    var geo = EarthLayout.Build(inp, data, out _);
                    var model = new TerrainModel(data, geo, vs, 12345) { GutterSeabedY = sea - 40 };
                    string tag = ring >= 1e6 ? $"{ring / 1e6:0}M" : $"{ring / 1e3:0}k";
                    double mPerBlock = 111320 / geo.BlocksPerDegreeZ;
                    data.SetIslandOverlay(false);
                    int oldLand = 0;
                    foreach (var (lat, lon) in falseLand)
                    {
                        geo.FromGeo(lat, lon, out double x, out double z);
                        if (model.GetColumn(x, z).Water == TerrainModel.WaterNone) oldLand++;
                    }
                    // old saves (no overlay) keep the islands they had: what these spots were, as the player saw them
                    Check(oldLand >= falseLand.Length / 2, $"{tag}: old saves keep their rim islands ({oldLand}/{falseLand.Length} spots are land)");
                    if (renders) RenderBahamas(model, geo, outDir, $"islands-{tag}-before");
                    // the Tongue of the Ocean's eastern rim, close up: false islands before, the real cays after
                    if (renders && ring >= 1e6) RenderAt(model, geo, outDir, "islands-4M-tongue-before", 23.95, -76.95, 1400, 1000, 8);
                    if (renders && ring == 512000) foreach (var (dn, dla, dlo, ds) in Deltas) RenderAt(model, geo, outDir, $"islands-512k-{dn}-before", dla, dlo, 1000, 800, ds);
                    data.SetIslandOverlay(true);
                    model.Islands = new IslandLayer(data.Islands, geo, 4);
                    counts.Add(model.Islands.Count);
                    Console.WriteLine($"  ring {ring:0}: {mPerBlock:0.0} m per block, {model.Islands.Count} of {model.Islands.Total} islands at least 4 blocks wide");
                    if (renders) RenderBahamas(model, geo, outDir, $"islands-{tag}-after");
                    if (renders && ring >= 1e6) RenderAt(model, geo, outDir, "islands-4M-tongue-after", 23.95, -76.95, 1400, 1000, 8);
                    if (renders && ring == 512000) foreach (var (dn, dla, dlo, ds) in Deltas) RenderAt(model, geo, outDir, $"islands-512k-{dn}-after", dla, dlo, 1000, 800, ds);
                    if (ring >= 1e6) CheckRounded(data, model, geo, outDir, renders);

                    int seaHits = 0;
                    foreach (var (lat, lon) in falseLand)
                    {
                        geo.FromGeo(lat, lon, out double x, out double z);
                        var c = model.GetColumn(x, z);
                        if (c.Water == TerrainModel.WaterSea) { seaHits++; continue; }
                        var cell = data.NearestCell(lat, lon);
                        bool isl = model.Islands.Query(lat, lon, out var hit);
                        Console.WriteLine($"    {lat:0.00},{lon:0.00}: water {c.Water} top {c.Top} elev {c.Elev:0.0}; grid flags {cell.Flags} elev {cell.Elev:0.0}; island {(isl ? $"{(hit.Inside ? "inside" : "near")} d {hit.Dist:0.0} h {hit.Height:0}" : "none")}");
                    }
                    Check(seaHits == falseLand.Length, $"{tag}: ETOPO's false islands on the rims are sea ({seaHits}/{falseLand.Length})");
                    foreach (var (name, lat, lon) in realLand)
                    {
                        geo.FromGeo(lat, lon, out double x, out double z);
                        var c = model.GetColumn(x, z);
                        Check(c.Water == TerrainModel.WaterNone && c.Top >= sea - 1, $"{tag}: {name} is land (top {c.Top}, water {c.Water})");
                    }

                    // the tiny cay on the southern rim: 0.04 km², 40-50 m across
                    var cay = data.Islands.Islands.Where(i => Math.Abs(i.MinLat - 22.75) < 0.05 && Math.Abs(i.MinLon + 78.12) < 0.05).OrderBy(i => i.AreaKm2).FirstOrDefault();
                    if (cay == null) { Check(false, "Guinchos Cay is in islands.bin"); continue; }
                    geo.FromGeo(0.5 * (cay.MinLat + cay.MaxLat), 0.5 * (cay.MinLon + cay.MaxLon), out double cx, out double cz);
                    double kmX = geo.BlocksPerDegreeX / (111.32 * Math.Cos(22.75 * Math.PI / 180)), kmZ = geo.BlocksPerDegreeZ / 111.32;
                    double expect = cay.AreaKm2 * kmX * kmZ;
                    int land = 0, steps = 0, shoreLand = 0, shoreBeach = 0;
                    int half = (int)Math.Max(16, 3 * Math.Sqrt(expect));
                    for (int dz = -half; dz <= half; dz++)
                    {
                        for (int dx = -half; dx <= half; dx++)
                        {
                            var c = model.GetColumn(cx + dx, cz + dz);
                            if (c.Water != TerrainModel.WaterNone) continue;
                            land++;
                            foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                var n = model.GetColumn(cx + dx + ox, cz + dz + oz);
                                if (n.Water != TerrainModel.WaterSea) continue;
                                shoreLand++;
                                if (c.Beach) shoreBeach++;
                                if (c.Top - n.Top > 2) steps++;
                                break;
                            }
                        }
                    }
                    bool wide = cay.WidthKm * Math.Min(kmX, kmZ) >= 4;
                    Console.WriteLine($"  {tag}: Guinchos Cay {cay.AreaKm2:0.000} km², {cay.WidthKm * 1000:0} m wide: {land} land columns (true size {expect:0})");
                    if (!wide) Check(land == 0, $"{tag}: Guinchos Cay is too narrow for this world and left out");
                    else
                    {
                        Check(land >= 0.5 * expect && land <= 1.6 * expect, $"{tag}: Guinchos Cay at its true size ({land} of {expect:0} columns)");
                        Check(shoreLand > 0 && shoreBeach >= 0.8 * shoreLand, $"{tag}: its shore is beach ({shoreBeach}/{shoreLand})");
                        Check(steps == 0, $"{tag}: the sea floor comes up to its shore (no step over 2 blocks: {steps})");
                    }

                    var sw = Stopwatch.StartNew();
                    geo.FromGeo(24.2, -76.6, out double ax, out double az);
                    int n2 = 0;
                    for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) { model.GetColumn(ax + 4 * i, az + 4 * j); n2++; }
                    Console.WriteLine($"  {tag}: {sw.Elapsed.TotalMilliseconds * 1000 / n2:0.00} µs per column among the Exumas");
                }
                Check(counts[0] < counts[1] && counts[1] < counts[2], $"the bigger the world, the more islands ({string.Join(" < ", counts)})");
            }
            finally { data.SetIslandOverlay(before); }
        }

        /// <summary>
        /// Island rules v2 round off GSHHG's raster staircases (runs of ~550 m with 45° cuts, ~50 blocks a step at 4M): Čiovo
        /// off Trogir keeps its land within a few percent of rules v1 (the cuts only take the corners), and some of the columns
        /// along its coast change. With renders, writes Čiovo at 1 block/px with rules v1 (before) and v2 (after).
        /// </summary>
        static void CheckRounded(EarthData data, TerrainModel model, EarthGeometry geo, string outDir, bool renders)
        {
            var layer = model.Islands;
            Check(layer.Rules == IslandLayer.RulesVersion && IslandLayer.RulesVersion >= 2, "new worlds' islands have rounded corners");
            const double lat = 43.505, lon = 16.27;
            int w = 1600, h = 900;
            bool[] Land(IslandLayer l)
            {
                var land = new bool[w / 2 * (h / 2)];
                for (int z = 0; z < h / 2; z++) for (int x = 0; x < w / 2; x++) land[z * (w / 2) + x] = l.IsLand(lat + (h / 2 - 2 * z) / geo.BlocksPerDegreeZ, lon + (2 * x - w / 2) / geo.BlocksPerDegreeX);
                return land;
            }
            var v1 = new IslandLayer(data.Islands, geo, 4, 1);
            bool[] a = Land(v1), b = Land(layer);
            int before = a.Count(t => t), after = b.Count(t => t), changed = a.Where((t, i) => t != b[i]).Count();
            if (renders)
            {
                model.Islands = v1;
                RenderAt(model, geo, outDir, "islands-4M-ciovo-before", lat, lon, w, h, 1);
                model.Islands = layer;
                RenderAt(model, geo, outDir, "islands-4M-ciovo-after", lat, lon, w, h, 1);
            }
            Console.WriteLine($"  4M: Čiovo {before} island samples with rules v1, {after} with v2 ({100.0 * (after - before) / Math.Max(before, 1):+0.0;-0.0}%), {changed} changed");
            Check(before > 1000 && Math.Abs(after - before) <= 0.03 * before, $"4M: rounding Čiovo's corners keeps its size ({after} of {before})");
            Check(changed > 0.005 * before, $"4M: rounding changes Čiovo's coast ({changed} samples)");
        }

        /// <summary>Places for the coast checks and renders: name, centre, window (degrees of longitude and latitude), and a close-up centre.</summary>
        static readonly (string Name, double Lat, double Lon, double SpanLon, double SpanLat, double CloseLat, double CloseLon)[] CoastSites =
        {
            ("norway", 61.0, 6.5, 1.6, 0.8, 61.2, 6.55),       // the Sognefjord, a fjord coast
            ("mekong", 9.75, 106.35, 1.3, 1.1, 9.62, 106.45),  // the Mekong delta
            ("dalmatia", 43.55, 16.1, 1.2, 0.7, 43.52, 16.27), // a rocky coast with islands off it
        };

        /// <summary>
        /// The big land's coasts from their outlines (coasts.bin, EarthParams.Coasts): off where a 2' cell is under 32 blocks
        /// (204 800 ring), on at 512 000 and 4 000 000. Away from coasts (inland, open sea, south of 60°S) columns are the same
        /// as without; near them land and sea follow the outline; the Sognefjord stays open from its mouth to Flåm and the
        /// Fjærlandsfjord; the Mekong reaches the sea; polders are land and the IJsselmeer water; small islands off the coast stay;
        /// rocky coasts rise steeper than deltas; the seam gutter is the same one ring apart. With renders, before/after maps
        /// of the three sites for both sizes (the before maps with the outline in red) and close-ups at one block per pixel
        /// for 4M.
        /// </summary>
        static void CheckCoasts(EarthData data, VerticalScale vs, string outDir, bool renders)
        {
            Console.WriteLine("coasts");
            if (data.Coasts == null || data.Islands == null) { Check(false, "earthdata/coasts.bin and islands.bin are there"); return; }
            string file = Path.Combine(FindRootOf(outDir), "earthdata", "coasts.bin");
            if (File.Exists(file)) Console.WriteLine($"  coasts.bin {new FileInfo(file).Length / 1e6:0.0} MB");
            int sea = vs.SeaLevel;
            bool before = data.IslandOverlay, lakesBefore = data.LakeOverlay, shoresBefore = data.ShoreOverlay, outletsBefore = data.OutletOverlay;
            try
            {
                // the overlays a new world has (as the full run leaves them; the coasts mode alone had them off)
                data.SetIslandOverlay(true);
                data.SetLakeOverlay(true);
                data.SetShoreOverlay(true);
                data.SetOutletOverlay(Environment.GetEnvironmentVariable("OUTLETS") != "0");
                foreach (double ring in new[] { 204800.0, 512000.0, 4000000.0 })
                {
                    var inp = new LayoutInput
                    {
                        WorldWidth = (int)(ring * 1.25),
                        CwRingStart = ring / 8, CwRingLength = ring, CwEquatorZ = ring / 4, CwPoleDistance = ring / 4 - 48, CwBandInner = 608,
                    };
                    var geo = EarthLayout.Build(inp, data, out _);
                    string tag = ring >= 1e6 ? $"{ring / 1e6:0}M" : $"{ring / 1e3:0}k";
                    double cell = CoastLayer.CellBlocks(geo);
                    if (cell < 32)
                    {
                        Check(true, $"{tag}: a 2' cell is {cell:0} blocks: no outline coasts (the grid is fine enough)");
                        continue;
                    }
                    var sw = Stopwatch.StartNew();
                    var plain = CoastModel(data, geo, vs, null, false);
                    long riverMs = sw.ElapsedMilliseconds;
                    var model = CoastModel(data, geo, vs, plain.Rivers, true);
                    var coastsLayer = model.Coasts;
                    Console.WriteLine($"  ring {ring:0}: {111320 / geo.BlocksPerDegreeZ:0.0} m per block, a 2' cell {cell:0} blocks, profile {coastsLayer.Reach:0} blocks " +
                        $"(models in {sw.ElapsedMilliseconds} ms, {riverMs} ms of it the first with its rivers)");

                    // away from coasts nothing changes: inland, the open sea, and the Antarctic (no outlines south of 60°S)
                    int same = 0, total = 0;
                    foreach (var (la, lo) in new[] { (55.75, 37.6), (23.0, 13.0), (62.0, 100.0), (-5.0, -60.0), (40.0, -40.0), (-30.0, -120.0), (10.0, 160.0),
                        (-75.0, 0.0), (-64.5, -61.5), (-63.0, -57.0), (-77.5, 166.5), (-66.0, 110.0) })
                    {
                        geo.FromGeo(la, lo, out double x, out double z);
                        for (int k = 0; k < 64; k++)
                        {
                            var a = plain.GetColumn(x + 7 * k, z + 3 * k);
                            var b = model.GetColumn(x + 7 * k, z + 3 * k);
                            total++;
                            if (a.Top == b.Top && a.Water == b.Water && a.WaterTop == b.WaterTop && a.IceBottom == b.IceBottom && a.Beach == b.Beach) same++;
                        }
                    }
                    Check(same == total, $"{tag}: inland, open sea and the Antarctic coast unchanged ({same}/{total} columns the same)");

                    foreach (var site in CoastSites)
                    {
                        // land and sea against the outline, over the window (well clear of the line, small islands and rivers aside)
                        int agreeA = 0, agreeB = 0, n = 0, beachN = 0, beach = 0, inner = 0;
                        double innerSum = 0, innerMetres = 0;
                        for (int j = 0; j < 160; j++)
                        {
                            for (int i = 0; i < 160; i++)
                            {
                                double la = site.Lat + site.SpanLat * ((j + 0.5) / 160 - 0.5), lo = site.Lon + site.SpanLon * ((i + 0.37) / 160 - 0.5);
                                geo.FromGeo(la, lo, out double x, out double z);
                                var g = geo.ToGeo(x, z);
                                double d = coastsLayer.SignedDistance(g.Lat, g.Lon);
                                var s = data.SampleTerrain(g.Lat, g.Lon);
                                if (double.IsNaN(d) || s.SeaNear < 0.25 || s.Ice > 0) continue;
                                if (model.Islands.Query(g.Lat, g.Lon, out _)) continue;
                                var b = model.GetColumn(x, z);
                                if (b.River || b.NearRiver || b.Water == TerrainModel.WaterLake || b.Water == TerrainModel.WaterSaltLake) continue;
                                if (d > 0 && d <= 2 && b.Water == TerrainModel.WaterNone) { beachN++; if (b.Beach) beach++; }
                                if (d >= 3 && d <= 6 && b.Water == TerrainModel.WaterNone) { inner++; innerSum += b.Top - sea; innerMetres += b.Elev; }
                                if (Math.Abs(d) < 3) continue;
                                var a = plain.GetColumn(x, z);
                                n++;
                                if ((a.Water == TerrainModel.WaterNone) == (d > 0)) agreeB++;
                                if ((b.Water == TerrainModel.WaterNone) == (d > 0)) agreeA++;
                                // COASTSPOTS=<site>: the spots that don't follow the outline after
                                else if (Environment.GetEnvironmentVariable("COASTSPOTS") == site.Name)
                                    Console.WriteLine($"    off {g.Lat:0.0000},{g.Lon:0.0000} d {d:0.0} water {b.Water} top {b.Top} elev {b.Elev:0.0} before water {a.Water} top {a.Top} sea {s.Ocean:0.00}");
                            }
                        }
                        double rise = innerSum / Math.Max(1, inner);
                        Console.WriteLine($"  {tag} {site.Name}: land/sea as the outline at {100.0 * agreeB / Math.Max(1, n):0.0}% of {n} spots before, {100.0 * agreeA / Math.Max(1, n):0.0}% after; " +
                            $"beach on {100.0 * beach / Math.Max(1, beachN):0}% of the coast's land, {rise:0.0} blocks up 3-6 blocks in");
                        Check(n > 1000 && agreeA >= 0.995 * n, $"{tag} {site.Name}: land and sea follow the outline ({agreeA}/{n})");
                        if (site.Name == "mekong") Check(beach >= 0.5 * beachN, $"{tag} mekong: the delta's coast is mostly beach ({beach}/{beachN})");
                        // in metres, not blocks: 2 blocks were about 8 m at height 384, where the check was set; at 256 a block
                        // near sea level is 5.9 m, so the same karst coast (about 11 m up 30-60 m in at 4M) read 1.2 blocks
                        double riseMetres = innerMetres / Math.Max(1, inner);
                        if (site.Name == "dalmatia") Check(inner >= 10 && riseMetres >= 8, $"{tag} dalmatia: the rocky coast rises steeply ({riseMetres:0.0} m, {rise:0.0} blocks up 3-6 blocks in, {inner} columns)");

                        if (renders)
                        {
                            double step = site.SpanLon * geo.BlocksPerDegreeX / 1200;
                            int h = (int)Math.Round(site.SpanLat * geo.BlocksPerDegreeZ / step);
                            RenderCoast(plain, coastsLayer, geo, outDir, $"coasts-{tag}-{site.Name}-before", site.Lat, site.Lon, 1200, h, step, true);
                            RenderCoast(model, coastsLayer, geo, outDir, $"coasts-{tag}-{site.Name}-after", site.Lat, site.Lon, 1200, h, step, false);
                            if (ring >= 1e6)
                            {
                                RenderCoast(plain, coastsLayer, geo, outDir, $"coasts-{tag}-{site.Name}-close-before", site.CloseLat, site.CloseLon, 1200, 900, 1, true);
                                RenderCoast(model, coastsLayer, geo, outDir, $"coasts-{tag}-{site.Name}-close-after", site.CloseLat, site.CloseLon, 1200, 900, 1, false);
                            }
                        }
                    }

                    // the Sognefjord from its mouth to Flåm and to the end of the Fjærlandsfjord stays open sea (GSHHG ends the Nærøyfjord,
                    // 250 m wide, 8 km short of Gudvangen: narrower than its trace)
                    int bfsStep = ring >= 1e6 ? 6 : 1;
                    bool Sea(TerrainModel.Column c) => c.Water == TerrainModel.WaterSea;
                    foreach (var (end, ela, elo) in new[] { ("Flåm", 60.87, 7.11), ("Fjærland", 61.40, 6.77) })
                    {
                        bool okA = Connected(model, geo, 61.10, 5.45, ela, elo, Sea, bfsStep, out int visA);
                        bool okB = Connected(plain, geo, 61.10, 5.45, ela, elo, Sea, bfsStep, out _);
                        Check(okA, $"{tag}: the Sognefjord is open sea from its mouth to {end} ({visA} cells{(okB ? ", as before" : ", it wasn't before")})");
                    }
                    // the Mekong (Bassac at Cần Thơ) reaches the open sea
                    bool Water(TerrainModel.Column c) => c.Water != TerrainModel.WaterNone && !c.Gutter;
                    bool mek = Connected(model, geo, 10.04, 105.79, 9.35, 106.75, Water, ring >= 1e6 ? 4 : 1, out int visM);
                    Check(mek, $"{tag}: the Mekong at Cần Thơ reaches the open sea ({visM} cells)");

                    // polders are land, the IJsselmeer stays water; small islands off the coast are still there
                    foreach (var (name, la, lo, land) in new[] { ("Noordoostpolder", 52.70, 5.75, true), ("Haarlemmermeer", 52.30, 4.70, true), ("Flevoland", 52.45, 5.50, true),
                        ("the IJsselmeer", 52.80, 5.30, false), ("Brač (an island off Split)", 43.31, 16.62, true), ("the Split Channel", 43.45, 16.50, false) })
                    {
                        geo.FromGeo(la, lo, out double x, out double z);
                        var a = plain.GetColumn(x, z);
                        var b = model.GetColumn(x, z);
                        Check((b.Water == TerrainModel.WaterNone) == land, $"{tag}: {name} is {(land ? "land" : "water")} (top {b.Top}, water {b.Water}; was {(a.Water == TerrainModel.WaterNone ? "land" : "water")})");
                    }

                    // no seams along the outline tiles' edges (whole degrees): a column there is land or sea as one of its neighbours
                    // a block either side is (before, a rounding error put the whole-degree meridians a ring away: thin sea lines)
                    int seams = 0, edgeCols = 0;
                    for (double t = 0; t <= 1; t += 0.002)
                    {
                        foreach (var (la, lo, alongLon) in new[] { (-1.5 + 2 * t, -50.0, false), (-1.5 + 2 * t, -49.0, false), (0.0, -51 + 3 * t, true), (-1.0, -51 + 3 * t, true) })
                        {
                            geo.FromGeo(la, lo, out double x, out double z);
                            var c = model.GetColumn(x, z);
                            var a = alongLon ? model.GetColumn(x, z - 1) : model.GetColumn(x - 1, z);
                            var b = alongLon ? model.GetColumn(x, z + 1) : model.GetColumn(x + 1, z);
                            edgeCols++;
                            bool land = c.Water == TerrainModel.WaterNone;
                            if (land != (a.Water == TerrainModel.WaterNone) && land != (b.Water == TerrainModel.WaterNone)) seams++;
                        }
                    }
                    Check(seams == 0, $"{tag}: no seams along whole degrees at the Amazon's mouth ({seams} of {edgeCols} columns unlike both neighbours)");

                    // the seam gutter: its water as without coasts, and no more differences one ring apart than without (the
                    // gutter's edges follow the natural terrain, whose noise isn't periodic: a few columns differ either way)
                    int mismA = 0, mismB = 0, cols = 0, wet = 0;
                    foreach (double la in new[] { 66.0, 64.0, 60.0, 52.0, -50.0 })
                    {
                        double z = geo.EquatorZ - la * geo.BlocksPerDegreeZ;
                        for (double x = geo.RingStart - geo.Gutter / 2; x < geo.RingStart + geo.Gutter / 2; x += 13)
                        {
                            var a = model.GetColumn(x, z);
                            var b = model.GetColumn(x + geo.RingLength, z);
                            var c = plain.GetColumn(x, z);
                            var d = plain.GetColumn(x + geo.RingLength, z);
                            cols++;
                            if (a.Top != b.Top || a.Water != b.Water) mismA++;
                            if (c.Top != d.Top || c.Water != d.Water) mismB++;
                            if (a.Water == c.Water) wet++;
                        }
                    }
                    Check(mismA <= mismB && wet == cols, $"{tag}: the seam gutter's water as before; one ring apart {mismA} of {cols} columns differ ({mismB} without coasts)");

                    // cost per column: along the Norwegian coast, in the Mekong delta, and inland
                    foreach (var (where, la, lo) in new[] { ("the Norwegian coast", 61.05, 5.2), ("the Mekong delta", 9.6, 106.4), ("inland (Alps)", 46.0, 8.0) })
                    {
                        geo.FromGeo(la, lo, out double x, out double z);
                        double Time(TerrainModel m)
                        {
                            for (int j = 0; j < 16; j++) for (int i = 0; i < 256; i++) m.GetColumn(x + i, z + j);   // warm the caches
                            var t = Stopwatch.StartNew();
                            for (int j = 0; j < 256; j++) for (int i = 0; i < 256; i++) m.GetColumn(x + i, z + j);
                            return t.Elapsed.TotalMilliseconds * 1000 / 65536;
                        }
                        double tb = Time(plain), ta = Time(model);
                        Console.WriteLine($"  {tag}: {where}: {tb:0.00} µs per column before, {ta:0.00} µs after");
                    }
                }
            }
            finally
            {
                data.SetIslandOverlay(before);
                data.SetLakeOverlay(lakesBefore);
                data.SetShoreOverlay(shoresBefore);
                data.SetOutletOverlay(outletsBefore);
            }
        }

        /// <summary>A model as a world with this geometry would have it: islands, rivers (these, or new ones) and their exit channels,
        /// with or without the coast outlines.</summary>
        static TerrainModel CoastModel(EarthData data, EarthGeometry geo, VerticalScale vs, RiverNetwork rivers, bool coasts)
        {
            var m = new TerrainModel(data, geo, vs, 12345)
            {
                GutterSeabedY = vs.SeaLevel - 40, GutterBridges = true,
                MinSeaDepth = TerrainModel.ShipSeaDepth, LakeShores = data.ShoreOverlay,
            };
            m.Islands = new IslandLayer(data.Islands, geo, 4);
            if (coasts) m.Coasts = new CoastLayer(data.Coasts, geo, 4);
            if (data.Rivers != null)
            {
                // the river network depends only on the geometry and the vertical scale: one for both models (COURSES=n: the
                // reaches of that river courses rules, as the world builds them; CHANNELRULES=n: that version of the channels)
                var reaches = int.TryParse(Environment.GetEnvironmentVariable("COURSES"), out int courses) && courses > 0 ? data.RiverReaches(courses) : data.Rivers;
                m.Rivers = rivers ?? new RiverNetwork(reaches, geo, 100, 1.5, m.RiverWaterTop, "ends", 20, geo.RingLength / 204800, 0.5, 2, falls: RiverFalls.All);
                if (Environment.GetEnvironmentVariable("NOCHANNELS") != "1")
                    m.Channels = SeaChannels.Build(m, true, null, int.TryParse(Environment.GetEnvironmentVariable("CHANNELRULES"), out int chRules) ? chRules
                        : SeaChannels.Version);
            }
            return m;
        }

        /// <summary>Before/after renders of one place for a ring size (coastrender mode).</summary>
        static void RenderCoastPlace(EarthData data, VerticalScale vs, string outDir, double lat, double lon, double ring, int w, int h, double step)
        {
            data.SetIslandOverlay(true);
            var inp = new LayoutInput
            {
                WorldWidth = (int)(ring * 1.25),
                CwRingStart = ring / 8, CwRingLength = ring, CwEquatorZ = ring / 4, CwPoleDistance = ring / 4 - 48, CwBandInner = 608,
            };
            var geo = EarthLayout.Build(inp, data, out _);
            var plain = CoastModel(data, geo, vs, null, false);
            var model = CoastModel(data, geo, vs, plain.Rivers, true);
            string name = string.Format(System.Globalization.CultureInfo.InvariantCulture, "coastrender-{0:0.###}_{1:0.###}-{2:0}", lat, lon, ring);
            RenderCoast(plain, model.Coasts, geo, outDir, name + "-before", lat, lon, w, h, step, true);
            RenderCoast(model, model.Coasts, geo, outDir, name + "-after", lat, lon, w, h, step, false);
        }

        /// <summary>Is there a way between two places (each moved to the nearest column that passes) through columns that pass,
        /// sampled every <paramref name="step"/> blocks, within the box around them (plus a margin)?</summary>
        static bool Connected(TerrainModel m, EarthGeometry geo, double lat0, double lon0, double lat1, double lon1, Func<TerrainModel.Column, bool> pass, int step, out int visited)
        {
            visited = 0;
            geo.FromGeo(lat0, lon0, out double x0, out double z0);
            geo.FromGeo(lat1, lon1, out double x1, out double z1);
            if (!Nearest(m, ref x0, ref z0, pass, step) || !Nearest(m, ref x1, ref z1, pass, step)) return false;
            double margin = 0.15 * geo.BlocksPerDegreeZ;
            double minX = Math.Min(x0, x1) - margin, maxX = Math.Max(x0, x1) + margin, minZ = Math.Min(z0, z1) - margin, maxZ = Math.Max(z0, z1) + margin;
            long Key(long i, long j) => (i << 32) ^ (j & 0xFFFFFFFF);
            long ti = (long)Math.Round((x1 - x0) / step), tj = (long)Math.Round((z1 - z0) / step);
            var seen = new HashSet<long> { Key(0, 0) };
            var queue = new Queue<(long, long)>();
            queue.Enqueue((0, 0));
            while (queue.Count > 0 && visited < 4000000)
            {
                var (i, j) = queue.Dequeue();
                visited++;
                if (Math.Abs(i - ti) <= 1 && Math.Abs(j - tj) <= 1) return true;
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    long ni = i + di, nj = j + dj;
                    double x = x0 + ni * step, z = z0 + nj * step;
                    if (x < minX || x > maxX || z < minZ || z > maxZ || !seen.Add(Key(ni, nj))) continue;
                    if (pass(m.GetColumn(x, z))) queue.Enqueue((ni, nj));
                }
            }
            return false;
        }

        static bool Nearest(TerrainModel m, ref double x, ref double z, Func<TerrainModel.Column, bool> pass, int step)
        {
            for (int r = 0; r <= 400; r += step)
            {
                for (int dz = -r; dz <= r; dz += step)
                {
                    for (int dx = -r; dx <= r; dx += step)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        if (pass(m.GetColumn(x + dx, z + dz))) { x += dx; z += dz; return true; }
                    }
                }
            }
            return false;
        }

        /// <summary>A map around a place; with <paramref name="outline"/> the big land's outline drawn over it in red.</summary>
        static void RenderCoast(TerrainModel model, CoastLayer coasts, EarthGeometry geo, string dir, string name, double lat, double lon, int w, int h, double step, bool outline)
        {
            geo.FromGeo(lat, lon, out double cx, out double cz);
            double x0 = cx - w * step / 2, z0 = cz - h * step / 2;
            var sw = Stopwatch.StartNew();
            Func<int, int, (byte, byte, byte)?> over = null;
            if (outline)
            {
                over = (i, j) =>
                {
                    var g = geo.ToGeo(x0 + i * step, z0 + j * step);
                    double d = coasts.SignedDistance(g.Lat, g.Lon);
                    return !double.IsNaN(d) && Math.Abs(d) < Math.Max(0.75, 0.5 * step) ? ((byte)230, (byte)30, (byte)30) : null;
                };
            }
            var rgb = MapColors.Render(model, x0, z0, w, h, step, over);
            Png.WriteRgb(Path.Combine(dir, name + ".png"), w, h, rgb);
            Console.WriteLine($"  {name}.png  {w}x{h} at {step:0.##} blocks/px, {sw.ElapsedMilliseconds} ms");
        }

        static string FindRootOf(string outDir) => Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(outDir)));

        /// <summary>River deltas that GSHHG splits into islands between the channels (close-ups at 512k, blocks per pixel).</summary>
        static readonly (string, double, double, double)[] Deltas =
            { ("lena", 72.6, 127.0, 16), ("mekong", 9.8, 106.3, 6), ("ob", 66.7, 70.0, 12), ("yellowriver", 37.7, 119.0, 4) };

        /// <summary>The Bahamas and north Cuba at the same 1600 px width for any world size.</summary>
        static void RenderBahamas(TerrainModel model, EarthGeometry geo, string dir, string name)
        {
            geo.FromGeo(26.2, -81.6, out double x0, out double z0);
            geo.FromGeo(21.8, -72.8, out double x1, out double z1);
            double step = (x1 - x0) / 1600;
            Render(model, dir, name, x0, z0, 1600, (int)((z1 - z0) / step), step);
        }

        static void RenderAt(TerrainModel model, EarthGeometry geo, string dir, string name, double lat, double lon, int w, int h, double step)
        {
            geo.FromGeo(lat, lon, out double x, out double z);
            Render(model, dir, name, x - w * step / 2, z - h * step / 2, w, h, step);
        }

        static void Render(TerrainModel model, string dir, string name, double x0, double z0, int w, int h, double step)
        {
            var sw = Stopwatch.StartNew();
            var rgb = MapColors.Render(model, x0, z0, w, h, step);
            Png.WriteRgb(Path.Combine(dir, name + ".png"), w, h, rgb);
            Console.WriteLine($"  {name}.png  {w}x{h} at {step} blocks/px, x {x0:0}..{x0 + w * step:0}, {sw.ElapsedMilliseconds} ms");
        }

        static string FindRoot()
        {
            string d = AppContext.BaseDirectory;
            while (d != null && !Directory.Exists(Path.Combine(d, "earthdata"))) d = Path.GetDirectoryName(d);
            return d ?? throw new DirectoryNotFoundException("earthdata/ not found above " + AppContext.BaseDirectory);
        }
    }
}
