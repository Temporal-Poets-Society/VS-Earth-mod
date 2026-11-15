using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EarthMap.Core;

namespace VolcanoTests
{
    /// <summary>
    /// Volcano interiors of geothermal rules 3 (decision 0078, EA-0067) at every play style's scale: every volcano with a
    /// body is planned; its chamber fits under its vent; no lava block touches anything but lava or the solid rock around
    /// it (so none flows out); every dyke, ring dyke and sill block is joined to the conduit or chamber (no loose pieces).
    /// Cross-sections through named volcanoes go to tests/out/volcano-*.png.
    ///   dotnet run -c Release --project tests\VolcanoTests -- "&lt;repo root&gt;" [scales, e.g. 10,100] [names, e.g. Etna;Hekla]
    /// Heights are the built-in grid's (no GLO-90 offline), so summits are lower and smoother than in a world.
    /// </summary>
    static class Program
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        /// <summary>The play styles: metres per block, world height, height curve (worldconfig.json).</summary>
        static readonly (int Scale, int Height, string Curve)[] Presets =
        {
            (200, 384, "log1000"), (100, 384, "log1000"), (50, 384, "log1000"), (25, 512, "linear"), (10, 1024, "linear"), (5, 1024, "linear"),
        };

        static readonly string[] Named = { "Etna", "Vesuvius", "Fujisan", "Hekla", "Kilauea", "Mauna Loa", "Campi Flegrei", "Shasta", "Nyiragongo", "Stromboli", "Erebus", "Santorini" };

        static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            var scales = args.Length > 1 && args[1] != "all" ? args[1].Split(',').Select(int.Parse).ToHashSet() : null;
            var names = args.Length > 2 ? args[2].Split(';') : Named;
            string outDir = Path.Combine(root, "tests", "out");
            Directory.CreateDirectory(outDir);
            var sw = Stopwatch.StartNew();
            var data = EarthData.LoadFromFolder(root, true);
            Console.WriteLine($"data loaded in {sw.ElapsedMilliseconds} ms; {Geothermal.Bodies.Count} bodies, rules {Geothermal.RulesVersion}");
            var magma = Geothermal.Bodies.GroupBy(b => b.MagmaFamily).Select(g => $"{g.Key} {g.Count()}");
            Console.WriteLine("magma families: " + string.Join(", ", magma) + $" (GVP unknown {Geothermal.Bodies.Count(b => b.RockMagma == Geothermal.Magma.Unknown)})");
            Check(Geothermal.FindVolcano("Etna").MagmaFamily == Geothermal.Magma.Basaltic && Geothermal.FindVolcano("Vesuvius").MagmaFamily == Geothermal.Magma.Intermediate
                && Geothermal.FindVolcano("Pinatubo").MagmaFamily == Geothermal.Magma.Silicic, "magma: Etna basaltic, Vesuvius intermediate, Pinatubo silicic");

            foreach (var (scale, height, curve) in Presets)
            {
                if (scales != null && !scales.Contains(scale)) continue;
                double bpd = Geo.MetresPerDegree / scale;
                var geo = new EarthGeometry { RingStart = 0, RingLength = 360 * bpd, Wraps = true, Gutter = 0, EquatorZ = 92 * bpd, BlocksPerDegreeZ = bpd };
                var vs = new VerticalTerrainOptions { Budget = "110", Curve = curve }.Build(height, 110, .0625, 200, 20, .43);
                var m = new TerrainModel(data, geo, vs, 12345) { GeothermalRules = Geothermal.RulesVersion };
                Console.WriteLine($"1:{scale} ({m.MetresPerBlock:0} m per block, height {height}, {curve}; underground {1 / VolcanoPlan.BlocksPerMetreUnder(vs):0} m per block)");
                sw.Restart();
                int bad = 0, edifices = 0, dykes = 0, sills = 0, rings = 0, outReach = 0;
                double margin = double.MaxValue;
                string firstBad = null, firstOut = null;
                // ER-06: EarthVolcanoes indexes a body by its GVP place and this reach, and skips a chunk outside it before
                // planning; a plan's own reach from its vent (rules 2 and 3) must stay inside it
                double search = VolcanoPlan.SearchM;
                foreach (var l in Geothermal.LavaLakes) search = Math.Max(search, l.SearchM);
                void InIndex(VolcanoPlan p, int rules)
                {
                    var v = p.Volcano;
                    geo.FromGeo(v.VentLat, v.VentLon, out double gx, out double gz);
                    double index = VolcanoPlan.MaxReach(m.MetresPerBlock, rules, v.Form) + search / m.MetresPerBlock + 2;
                    double need = Math.Max(Math.Abs(p.VentX - gx), Math.Abs(p.VentZ - gz)) + p.Reach;
                    margin = Math.Min(margin, index - need);
                    if (need > index) { outReach++; firstOut ??= $"{v.Name} rules {rules}: reach {need:0.0} from its GVP place over the index's {index:0.0}"; }
                }
                                foreach (var v in Geothermal.Bodies)
                {
                    VolcanoPlan p;
                    try { p = new VolcanoPlan(v, m, 3); InIndex(p, 3); InIndex(new VolcanoPlan(v, m, 2), 2); }
                    catch (Exception e) { bad++; firstBad ??= v.Name + ": " + e.Message; continue; }
                    var i = p.Interior;
                    string why = Problem(p, VolcanoPlan.MaxReach(m.MetresPerBlock, 3, v.Form));
                    if (why != null) { bad++; firstBad ??= v.Name + ": " + why; }
                    if (i.Edifice) edifices++;
                    dykes += i.Dykes.Count;
                    if (i.Sill) sills++;
                    if (i.Ring) rings++;
                }
                Console.WriteLine($"  {Geothermal.Bodies.Count} planned in {sw.ElapsedMilliseconds} ms: {edifices} layered cones, {dykes} dykes, {sills} sills, {rings} ring dykes");
                Check(bad == 0, $"1:{scale}: every chamber under rock and its conduit, every plan within the index's reach ({bad} broken{(firstBad != null ? ", first " + firstBad : "")})");
                Check(outReach == 0, $"1:{scale}: every plan's reach (rules 2 and 3) from its GVP place within EarthVolcanoes' index ({outReach} over{(firstOut != null ? ", first " + firstOut : "")}; smallest margin {margin:0.0} blocks)");
                if (scale <= 50) Check(ConduitUnderTop(data, geo, vs, out string conduitNote), $"1:{scale}: GenFixRules 4 (ER-19): a conduit without a crater keeps lava 2 blocks under a lower column's top and nothing above it; rules 3 as before ({conduitNote})");
                foreach (string name in names)
                {
                    var v = Geothermal.FindVolcano(name);
                    if (v == null || !v.HasBody) { Console.WriteLine($"  {name}: no body"); continue; }
                    var p = new VolcanoPlan(v, m, 3);
                    var i = p.Interior;
                    Console.WriteLine($"  {name} ({v.Form}, {v.MagmaFamily}, {(v.Active ? "active" : "dormant")}): vent {p.VentTop} (summit {p.SummitY}), cone {(i.Edifice ? $"base y {i.BaseY} radius {i.EdgeR:0} relief {i.EdificeRelief:0}" : "none")}; " +
                        $"chamber y {i.ChamberY:0} ({i.ChamberBottom}-{i.ChamberTop}) {2 * i.A:0} wide; conduit r {i.R0:0.0} (cupola {i.RCupola:0.0}, funnel {i.RFunnel:0.0}, bow {i.Meander:0.0}) " +
                        $"lava to {(i.Active ? i.ConduitLavaTop.ToString() : "-")}; {i.Dykes.Count} dykes{(i.Sill ? $", sill y {i.SillY} r {i.SillR:0}" : "")}{(i.Ring ? $", ring r {i.RingR:0}" : "")}; reach {i.Reach:0}");
                    if (scale >= 10)
                    {
                        int leaks = Leaks(p, out int lava);
                        Check(lava > 0 && leaks == 0, $"1:{scale} {name}: {lava} lava blocks, {leaks} next to open rock or air");
                        int loose = Loose(p, m, out int sheets);
                        Check(loose == 0, $"1:{scale} {name}: {sheets} dyke and sill blocks, {loose} not joined to the conduit or chamber");
                    }
                    Render(p, m, Path.Combine(outDir, $"volcano-{name.Replace(' ', '-').ToLowerInvariant()}-1to{scale}.png"));
                }
            }
            Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>ER-19: the active vents without a crater, each column of the conduit given a top 6 blocks under its lava's
        /// top (the ground falling away from a wet vent): with GenFixRules 4 no lava above top - 2 and no part above top, and
        /// lava still below; with rules 3 (GenFixRules 3) the old lava above top, unchanged.</summary>
        static bool ConduitUnderTop(EarthData data, EarthGeometry geo, VerticalScale vs, out string note)
        {
            var m3 = new TerrainModel(data, geo, vs, 12345) { GeothermalRules = Geothermal.RulesVersion, GenFixRules = 3 };
            var m4 = new TerrainModel(data, geo, vs, 12345) { GeothermalRules = Geothermal.RulesVersion, GenFixRules = TerrainModel.GenFixConduitUnderTop };
            int vents = 0, oldLava = 0, badLava = 0, badSolid = 0, lavaBelow = 0, sameElsewhere = 0, differ = 0;
            foreach (var v in Geothermal.Bodies)
            {
                if (!v.Active || !v.HasBody) continue;
                var p4 = new VolcanoPlan(v, m4, 3);
                if (p4.Crater) continue;
                var p3 = new VolcanoPlan(v, m3, 3);
                var i4 = p4.Interior; var i3 = p3.Interior;
                vents++;
                int r = (int)Math.Ceiling(Math.Max(i4.RCupola, i4.RFunnel) + i4.Meander + 2);
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        var c4 = i4.Prepare(p4.VentX + dx, p4.VentZ + dz); var c3 = i3.Prepare(p3.VentX + dx, p3.VentZ + dz);
                        int top = i4.ConduitLavaTop - 6;
                        for (int y = top - 4; y <= i4.ConduitTop + 2; y++)
                        {
                            var a4 = i4.At(c4, y, top); var a3 = i3.At(c3, y, top);
                            if (a3 == VolcanoInterior.Part.Lava && y > top - 2) oldLava++;
                            if (a4 == VolcanoInterior.Part.Lava && y > top - 2) badLava++;
                            if (a4 >= VolcanoInterior.Part.Margin && y > top) badSolid++;
                            if (a4 == VolcanoInterior.Part.Lava && y <= top - 2) lavaBelow++;
                        }
                        // a column whose top is at or above the vent's: the same parts with either rules
                        for (int y = i4.ConduitLavaTop - 4; y <= i4.ConduitTop; y++)
                            if (i4.At(c4, y, i4.ConduitTop + 1) == i3.At(c3, y, i4.ConduitTop + 1)) sameElsewhere++; else differ++;
                    }
            }
            note = $"{vents} vents: rules 3 lava above top-2 {oldLava}, rules 4 {badLava}, solid above top {badSolid}, lava below {lavaBelow}; high columns {differ} parts differ";
            return vents > 0 && oldLava > 0 && badLava == 0 && badSolid == 0 && lavaBelow > 0 && differ == 0 && sameElsewhere > 0;
        }

        static string Problem(VolcanoPlan p, double maxReach)
        {
            var i = p.Interior;
            if (i.ChamberBottom - VolcanoInterior.HotZone < 1) return $"chamber bottom {i.ChamberBottom} near the world's bottom";
            if (i.ChamberTop > i.ConduitTop - 6) return $"chamber top {i.ChamberTop} near the conduit's top {i.ConduitTop}";
            if (i.Active && !p.Lake && i.ConduitLavaTop >= p.VentTop) return "conduit lava at the surface";
            if (!(i.A > 0 && i.BTop > 0 && i.BBot > 0)) return "empty chamber";
            if (i.Reach > maxReach) return $"reach {i.Reach:0} over the index's {maxReach:0}";
            foreach (var d in i.Dykes) if (!(d.Y1 > d.Y0 && d.S1 > d.S0)) return "a dyke without length";
            return null;
        }

        /// <summary>Lava blocks with a side next to anything but lava or solid parts (a lava lake's conduit may meet its lake above).</summary>
        static int Leaks(VolcanoPlan p, out int lava)
        {
            var i = p.Interior;
            lava = 0;
            int leaks = 0;
            int r = (int)Math.Ceiling(Math.Max(i.A * 1.13, Math.Max(i.RCupola, i.RFunnel) + i.Meander) + 2);
            int y0 = i.ChamberBottom - 1, y1 = i.ConduitTop + 1;
            var cols = new Dictionary<long, VolcanoInterior.ColumnPrep>();
            VolcanoInterior.Part At(int x, int y, int z)
            {
                long k = (long)x << 32 | (uint)z;
                if (!cols.TryGetValue(k, out var c)) cols[k] = c = i.Prepare(x, z);
                return i.At(c, y, int.MaxValue);
            }
            for (int x = p.VentX - r; x <= p.VentX + r; x++)
            {
                for (int z = p.VentZ - r; z <= p.VentZ + r; z++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (At(x, y, z) != VolcanoInterior.Part.Lava) continue;
                        lava++;
                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        {
                            if (p.Lake && dy == 1 && y == p.LakeFloorY) continue;
                            if (At(x + dx, y + dy, z + dz) < VolcanoInterior.Part.Margin) leaks++;
                        }
                    }
                    if (cols.Count > 4_000_000) cols.Clear();
                }
            }
            return leaks;
        }

        /// <summary>Dyke and sill blocks not joined (by sides, through other dyke and sill blocks) to the chamber or conduit.</summary>
        static int Loose(VolcanoPlan p, TerrainModel m, out int sheets)
        {
            var i = p.Interior;
            var cells = new HashSet<(int, int, int)>();
            var tops = new Dictionary<long, int>();
            int Top(int x, int z)
            {
                long k = (long)x << 32 | (uint)z;
                if (!tops.TryGetValue(k, out int t)) tops[k] = t = m.GetColumn(x, z).Top;
                return t;
            }
            void Add(int x, int z, int ylo, int yhi)
            {
                var c = i.Prepare(x, z);
                int top = Top(x, z);
                for (int y = ylo; y <= yhi; y++)
                {
                    var part = i.At(c, y, top);
                    if (part == VolcanoInterior.Part.Dyke || part == VolcanoInterior.Part.Sill) cells.Add((x, y, z));
                }
            }
            foreach (var d in i.Dykes)
            {
                int ext = (int)Math.Ceiling(d.S1 + 2);
                int ylo = (int)Math.Floor(d.Y0 - d.Width - 2), yhi = (int)Math.Ceiling(d.Y1 + d.Width + 2);
                double cx = p.VentX + d.OX, cz = p.VentZ + d.OZ;
                for (int x = (int)Math.Floor(cx - ext); x <= cx + ext; x++)
                {
                    for (int z = (int)Math.Floor(cz - ext); z <= cz + ext; z++)
                    {
                        double ex = x - cx, ez = z - cz, s = ex * d.Cos + ez * d.Sin, pp = -ex * d.Sin + ez * d.Cos;
                        if (Math.Abs(pp) > 1.6 || s < -1 || s > d.S1 + 1) continue;
                        Add(x, z, ylo, yhi);
                    }
                }
            }
            if (i.Ring || i.Sill)
            {
                int ext = (int)Math.Ceiling(Math.Max(i.Ring ? i.RingR + 0.08 * (i.RingY1 - i.RingY0) + 2 : 0, i.Sill ? i.SillR + 2 : 0));
                int ylo = (int)Math.Floor(Math.Min(i.Ring ? i.RingY0 : int.MaxValue, i.Sill ? i.SillY - 2 : int.MaxValue));
                int yhi = (int)Math.Ceiling(Math.Max(i.Ring ? i.RingY1 : int.MinValue, i.Sill ? i.SillY + 2 : int.MinValue));
                for (int x = p.VentX - ext; x <= p.VentX + ext; x++)
                    for (int z = p.VentZ - ext; z <= p.VentZ + ext; z++)
                        Add(x, z, ylo, yhi);
            }
            sheets = cells.Count;
            var seen = new HashSet<(int, int, int)>();
            var queue = new Queue<(int, int, int)>();
            var steps = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
            foreach (var (x, y, z) in cells)
            {
                foreach (var (dx, dy, dz) in steps)
                {
                    var n = i.At(i.Prepare(x + dx, z + dz), y + dy, int.MaxValue);
                    if (n >= VolcanoInterior.Part.Margin) { if (seen.Add((x, y, z))) queue.Enqueue((x, y, z)); break; }
                }
            }
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                foreach (var (dx, dy, dz) in steps)
                {
                    var n = (x + dx, y + dy, z + dz);
                    if (cells.Contains(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }
            return cells.Count - seen.Count;
        }

        /// <summary>A section through the vent from west to east: sky, the regional rock, the cone's lava flows and ash beds,
        /// and the interior's parts.</summary>
        static void Render(VolcanoPlan p, TerrainModel m, string path)
        {
            var i = p.Interior;
            int half = (int)Math.Ceiling(Math.Min(i.Reach, Math.Max(i.A * 2.5, i.Edifice ? i.EdgeR * 1.1 : 0) + 8));
            int width = 2 * half + 1;
            int step = Math.Max(1, width / 1600);
            int w = width / step, ytop = Math.Min(m.Vertical.MapHeight - 1, p.SummitY + 20), h = ytop + 1;
            var rgb = new byte[w * h * 3];
            int z = p.VentZ;
            for (int px = 0; px < w; px++)
            {
                int x = p.VentX - half + px * step;
                var col = m.GetColumn(x, z);
                int top = Math.Min(col.Top, p.InCrater(x, z) ? Math.Min(col.Top, p.CraterSurface(x, z)) : col.Top);
                if (p.Lake && p.InLake(p.Dist(x, z))) top = p.LakeFloorY;
                var c = i.Prepare(x, z);
                for (int y = 0; y < h; y++)
                {
                    (byte, byte, byte) colour;
                    var part = i.At(c, y, top);
                    if (part == VolcanoInterior.Part.Lava) colour = (235, 90, 20);
                    else if (part == VolcanoInterior.Part.Cumulate) colour = (90, 120, 70);
                    else if (part == VolcanoInterior.Part.Margin) colour = (60, 50, 55);
                    else if (part is VolcanoInterior.Part.Sleeve or VolcanoInterior.Part.Crust) colour = (25, 25, 30);
                    else if (part == VolcanoInterior.Part.Plug) colour = (80, 70, 80);
                    else if (part is VolcanoInterior.Part.Dyke or VolcanoInterior.Part.Sill) colour = (40, 30, 45);
                    else if (p.Lake && p.InLake(p.Dist(x, z)) && y > p.LakeFloorY && y <= p.LakeY) colour = (255, 140, 30);
                    else if (y > top) colour = y <= m.Vertical.SeaLevel - 1 && col.Water != TerrainModel.WaterNone ? ((byte)60, (byte)90, (byte)160) : ((byte)200, (byte)220, (byte)240);
                    else
                    {
                        int layer = y >= top - 2 ? -1 : i.Layer(c, y);
                        colour = layer == 0 ? ((byte)95, (byte)95, (byte)100) : layer == 1 ? ((byte)150, (byte)85, (byte)70) : ((byte)170, (byte)165, (byte)150);
                        if (y >= top - 2 && y <= top) colour = (120, 140, 80);
                        if (part == VolcanoInterior.Part.Hot) colour = ((byte)(colour.Item1 * 0.85 + 30), (byte)(colour.Item2 * 0.85), (byte)(colour.Item3 * 0.85));
                    }
                    int o = ((h - 1 - y) * w + px) * 3;
                    rgb[o] = colour.Item1; rgb[o + 1] = colour.Item2; rgb[o + 2] = colour.Item3;
                }
            }
            WritePng(path, w, h, rgb);
        }

        static void WritePng(string path, int w, int h, byte[] rgb)
        {
            using var f = File.Create(path);
            f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] body)
            {
                var len = BitConverter.GetBytes(body.Length); Array.Reverse(len); f.Write(len);
                var t = System.Text.Encoding.ASCII.GetBytes(type);
                f.Write(t); f.Write(body);
                uint crc = Crc(t.Concat(body).ToArray());
                var cb = BitConverter.GetBytes(crc); Array.Reverse(cb); f.Write(cb);
            }
            var ihdr = new byte[13];
            var wb = BitConverter.GetBytes(w); Array.Reverse(wb); wb.CopyTo(ihdr, 0);
            var hb = BitConverter.GetBytes(h); Array.Reverse(hb); hb.CopyTo(ihdr, 4);
            ihdr[8] = 8; ihdr[9] = 2;
            Chunk("IHDR", ihdr);
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
            {
                for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgb, y * w * 3, w * 3); }
            }
            Chunk("IDAT", ms.ToArray());
            Chunk("IEND", Array.Empty<byte>());
        }

        static uint Crc(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return ~c;
        }
    }
}
