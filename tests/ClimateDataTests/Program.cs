using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// EA-0077 (code review 14 November 2026 ER-02): the client climate files as predicted planes decode to the very values
    /// of the plain files they were made from. Reads the plain files from Git (tag <see cref="PlainRef"/>; they were the same
    /// bytes from a37fc76 through rc.13) and the predicted ones from earthdata/, both through the game's own readers
    /// (TiledGrid, FineTemperature, EarthDataset with its climate fixes, WeatherData), and compares every value: so the old
    /// files still read, and the new ones read the same. Also decodes small blocks made by tools/planecoding.py (every
    /// predictor and type, a step and a reference plane, the ends of each range) against the bytes they must give.
    /// EA-0087 (ER-13): the climate kept as its codes gives the floats the float planes held, for climate.bin and every fix
    /// (against a copy of the old float code), and terrain tiles kept as 16-bit codes give every cell's elevation and ice
    /// as the old float tiles did.
    ///   dotnet run -c Release -- [repo root]
    /// </summary>
    static class Program
    {
        /// <summary>The plain files' tag (every one of the seven unchanged since a37fc76, 7 November 2026).</summary>
        const string PlainRef = "v2.0.0-rc.12";
        /// <summary>weather.bin's signature, frozen into worlds (EarthParams.WeatherDataMade): the plain file's FNV-1a.</summary>
        const string WeatherSignature = "bc9ea5a04cb1bc1d";

        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static string FindRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "modinfo.json"))) dir = Path.GetDirectoryName(dir);
            return dir ?? Directory.GetCurrentDirectory();
        }

        static string root;

        static byte[] Plain(string name)
        {
            var psi = new ProcessStartInfo("git", $"show {PlainRef}:earthdata/{name}")
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            using var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
            string err = p.StandardError.ReadToEnd();
            copy.Wait();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new IOException($"git show {PlainRef}:earthdata/{name}: {err.Trim()}");
            return ms.ToArray();
        }

        static byte[] Current(string name) => File.ReadAllBytes(Path.Combine(root, "earthdata", name));

        static int CodingOf(byte[] file) => PlaneCoding.Coding(BitConverter.ToInt32(file, 4));

        static int Main(string[] args)
        {
            root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot();
            Console.WriteLine("Climate files as predicted planes (EA-0077), root " + root);
            var sw = Stopwatch.StartNew();
            DecodeVectors();
            foreach (var name in new[] { "rain.bin", "aridity.bin", "temperature.bin", "climate.bin", "climatefix.bin", "climatefix2.bin", "weather.bin" })
            {
                var plain = Plain(name);
                var now = Current(name);
                Check(CodingOf(plain) == PlaneCoding.Plain, $"{name} at {PlainRef} is plain ({plain.Length:N0} bytes)");
                Check(CodingOf(now) == PlaneCoding.Predicted, $"earthdata/{name} is predicted ({now.Length:N0} bytes, {plain.Length - now.Length:N0} fewer)");
            }
            Grid("rain.bin", "EMRN", 3);
            Grid("aridity.bin", "EMAI", 1);
            Temperature();
            Climate();
            Weather();
            ClimateAsFloats();
            TerrainTiles();
            FixesPinTheirClimate();
            DamagedBrotli();
            Console.WriteLine($"{sw.Elapsed.TotalSeconds:0} s; " + (failures == 0 ? "ALL OK" : failures + " FAILED"));
            return failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------------------------------ the decoder itself

        static readonly string[] Vectors =
        {
            "j4+AAAEAAAEAAAUAAAEAAAEDAAEAAP8B/oB/KXbDEF2qUp/sOYbTe8gVYq/8pPE+i9glgH8A/0DAtdTzEjFQ6gkoR2aFHz5dfJu6VHOSsdDvMTIAAQIorK6wsrS229nX1dPRAgQGCAoMhYOBf317MzMAAAAACgkIBwYFHR4fICEiIB8eHRwbCAkKCwwNAAECA//+kR+txDZX3E4/zVsWtbwuoO17uNVjDoDyAAAAAP//w2MCXb7heNnGZQVbShV21sho8a1NE3PTBQQBCgfLv7+/v79AhHt7e3t7ysrKyso18Q4ODg4OAAAAAAAAh2hJKgsT8PDRspN0aIemxeT8HgAfPl18AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdAw==",
            "Hx8BGJ4FdsP1hinS30zykRim5yDI239tQZcENOsKd+Mg8ghWMs44xqAa9u7+/5rc3QRmJgcsvfjbAQ7Mpm/Y0aVsQ7Yhwzd8pFdRjNy3V/g7dxuVonZ11CUC+cyGJWlqUSrAjqwUYIifz+RdnB8KxDFOsyMvtcO2RLIogwI=",
            "Hx8B+C8G7Mbl4qK0t1IWF1fwWYgRLi76O5/+1PczNwc35cIxC8Fsa0u0tBdFJCLF+fRWGBanGFBbwgEWUfMwpM/fSV1odzolKLB7/iPXgExOa7eAAk80ozDCwDS2sASAAAOGIkhYJUI/A3/qV/zgx74vTJm95FX2JE8I43X1bLUYDtAx8DRSxIzAc2xARnAPYNg3aZ3b6YSi/8c5mvvcWWIV87l/ntutsthzBPVBRLIzMECGAw==",
            "Hx8BIB4HuTnpOuGaafkMHvq5PVQ7+20SNWLXFzoWklexym9xt4aXtNCRpt1lBmsvP6pezXb6w012G2G5ZnEysYmnktU8cBsyFHs6oopYBCNBEcQu8I9+l39jdqh6nhHBWJ6hYv5f/NtzNyHuexfT3XBrXGEOzEhcDVe9v9/dFwkZr9nabd1IAzooPInEE++IrH0bDiXIIw0BKZvhzJ9jP0qnc4QjqQMcj3xDIpvIV47+vCAAwPehPIBup1CE2AnbCUTsiMxOUg==",
            "Hx8BoI7UTnlAuGpqboh5wtM2IOGN6y75Q0QsTULlSITGLXEf8SpeolaFrtlOC/+TLbJyCnYXBBZFnss2ZBsy8BG863KYjBxuwWS7jD/r2/t7nhNEyBMM0hYGg5VOOv/uP94JFBoF2YB3W5knaBNlcpzX6LF4TIyXeAX2Pl/bxzCFsdksllvDsM24DCaH+2ZSKastiVMEAQgFCAEUUIJgxH8GZB4q8ihE0e12C7foUbjdRlwRV6KEi0fpNRvR0xIuZqBLbcoYx3ZcoXG88nO8wtjfVwwmeZzPAA==",
            "Hx8B+B8HuUnzDvI29BMlVvFA7OfeVK+bRY0C2UPFQyWK5YXCSt4tQiP0XxImDQBWQzeHI93c4LQ0YAwn2lcaBAMccNi4IpgrFhJiDCGS4D/8e/Xf393af97tgwUu/i//Ny9We6gFywngmcHxIBTtBAfj71DzHgTgHoxOR5ieDYVCPTAFPSAUjX2HFu988QEN/unLMsBzsXQsW3pcvcVfyk9rsAJrANxXqpVq9R5gIRU3CA==",
        };
        const string VectorRaw = "AP8B/oB/KXbDEF2qUp/sOYbTe8gVYq/8pPE+i9glgH8A/0DAtdTzEjFQ6gkoR2aFHz5dfJu6VHOSsdDvA4D9fwAA+/8FAGQArhozGLgVPRPCEEcOWrXfsmSw6a1uq/OoBVCKTQ9LlEgZRp5Dseo26LvlQOPF4EreAAD//wEA/v8AgP9/N55wzqn+4i4bX1SPbjynbOCcGc1S/YstpdreChc7UGuJm8LL3HgVqU7ZhwnAOflpAID/f///AAABAP7/ztbT49jw3f3iCucXnC2hOqZHq1SwYbVuaoRvkXSeeat+uIPFONs96EL1RwJMD1EcAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwd";

        /// <summary>Blocks of 5 x 6 cells in six planes (u8, i8, i16 in steps of 5, u16, i16 against the third plane, u8)
        /// made by tools/planecoding.py, one per predictor.</summary>
        static void DecodeVectors()
        {
            var types = new[] { PlaneType.U8, PlaneType.I8, PlaneType.I16, PlaneType.U16, PlaneType.I16, PlaneType.U8 };
            byte[] expected = Convert.FromBase64String(VectorRaw);
            for (int p = 0; p < Vectors.Length; p++)
            {
                byte[] blob = Convert.FromBase64String(Vectors[p]);
                byte[] got = PlaneCoding.Decode(blob, 0, blob.Length, 5, 6, types, "vector " + p);
                Check(got.AsSpan().SequenceEqual(expected), $"predictor {(PlaneCoding.Predictor)p}: tools/planecoding.py's block decodes to its planes");
            }
            var cut = Convert.FromBase64String(Vectors[0]);
            Check(Throws(() => PlaneCoding.Decode(cut, 0, cut.Length - 4, 5, 6, types, "cut")), "a cut-short block fails with InvalidDataException");
            // a block naming predictor 9, step 0 or a later reference plane
            foreach (var head in new[] { new byte[] { 9, 1, 0 }, new byte[] { 0, 0, 0 }, new byte[] { 0, 1, 1 } })
            {
                var block = new byte[3 + 4];
                head.CopyTo(block, 0);
                var packed = new byte[64];
                System.IO.Compression.BrotliEncoder.TryCompress(block, packed, out int n);
                Check(Throws(() => PlaneCoding.Decode(packed, 0, n, 2, 2, new[] { PlaneType.U8 }, "bad")),
                    $"a plane coded ({head[0]}, {head[1]}, {head[2]}) fails with InvalidDataException");
            }
        }

        /// <summary>EA-0116 D-R1: Brotli data that can't be decoded is InvalidDataException from UnbrotliAll, as it says, not
        /// Brotli's own InvalidOperationException.</summary>
        static void DamagedBrotli()
        {
            var good = File.ReadAllBytes(Path.Combine(root, "earthdata", "rain.bin"));
            int kinds = 0, right = 0;
            var rng = new Random(7);
            for (int k = 0; k < 40; k++)
            {
                var bad = new byte[4096];
                rng.NextBytes(bad);
                if (k % 2 == 1) { Array.Copy(good, 64, bad, 0, 2048); for (int i = 300; i < 340; i++) bad[i] ^= 0x5A; }
                try { EarthBinary.UnbrotliAll(bad, 0, bad.Length, "test.bin"); right++; }
                catch (InvalidDataException) { right++; kinds |= 1; }
                catch (Exception) { kinds |= 2; }
            }
            Check(right == 40 && (kinds & 2) == 0, $"EA-0116 D-R1: 40 damaged Brotli streams: InvalidDataException or decoded, never another exception ({right} of 40)");
        }

        static bool Throws(Action a)
        {
            try { a(); return false; }
            catch (InvalidDataException) { return true; }
        }

        // ------------------------------------------------------------------------------------------ the files

        static void Grid(string name, string magic, int bytesPerCell)
        {
            var a = new TiledGrid(Plain(name), magic, bytesPerCell, 0, name);
            var b = new TiledGrid(Current(name), magic, bytesPerCell, 0, name);
            bool layout = a.Version == b.Version && a.Width == b.Width && a.Height == b.Height && a.Tile == b.Tile;
            int tiles = (a.Width / a.Tile) * (a.Height / a.Tile), same = 0, withData = 0;
            long cells = 0;
            for (int key = 0; key < tiles; key++)
            {
                byte[] x = a.Decode(key), y = b.Decode(key);
                if (x.AsSpan().SequenceEqual(y)) same++;
                if (x.Length > 0) { withData++; cells += x.Length; }
            }
            Check(layout && same == tiles, $"{name}: every tile decodes to the plain file's bytes ({same} of {tiles}, {withData} with data, {cells:N0} values)");
        }

        static void Temperature()
        {
            var a = FineTemperature.Read(new MemoryStream(Plain("temperature.bin")));
            var b = FineTemperature.Read(new MemoryStream(Current("temperature.bin")));
            int tile = 180;
            long cells = 0, differ = 0, none = 0;
            for (int ty = 0; ty < a.Height / tile; ty++)
                for (int tx = 0; tx < a.Width / tile; tx++)
                    for (int r = ty * tile; r < (ty + 1) * tile; r++)
                        for (int c = tx * tile; c < (tx + 1) * tile; c++)
                        {
                            bool ha = a.Cell(r, c, out float m1, out float c1, out float w1);
                            bool hb = b.Cell(r, c, out float m2, out float c2, out float w2);
                            cells++;
                            if (!ha) none++;
                            if (ha != hb || ha && (Bits(m1) != Bits(m2) || Bits(c1) != Bits(c2) || Bits(w1) != Bits(w2))) differ++;
                        }
            Check(a.Width == b.Width && a.Height == b.Height && differ == 0,
                $"temperature.bin: every cell's mean, coldest and warmest month are the plain file's ({cells:N0} cells, {none:N0} without CHELSA, {differ} differ)");
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        static EarthDataset LoadClimate(Func<string, byte[]> bytesOf)
        {
            var names = new HashSet<string> { "climate.bin", "climatefix.bin", "climatefix2.bin" };
            return EarthDataset.Load(name =>
            {
                if (names.Contains(name)) return new MemoryStream(bytesOf(name));
                string path = Path.Combine(root, "earthdata", name);
                // the rest as the folder has it, but weather.bin (compared on its own)
                return name != "weather.bin" && File.Exists(path) ? new MemoryStream(File.ReadAllBytes(path)) : null;
            }, false);
        }

        static void Climate()
        {
            var a = LoadClimate(Plain);
            var b = LoadClimate(Current);
            Check(a.ClimateFixAvailable == b.ClimateFixAvailable && b.ClimateFixAvailable == EarthData.ClimateFixVersion,
                $"climate fixes available: plain {a.ClimateFixAvailable}, predicted {b.ClimateFixAvailable}");
            for (int v = 0; v <= EarthData.ClimateFixVersion; v++)
            {
                if (v > 0 && !(a.HasClimateFixVersion(v) && b.HasClimateFixVersion(v))) { Check(false, $"climate fix {v} is there"); continue; }
                var pa = a.ClimatePlanes(v);
                var pb = b.ClimatePlanes(v);
                long differ = 0, cells = 0;
                for (int p = 0; p < 6; p++)
                    for (int i = 0; i < pa.Mean.Length; i++, cells++)
                        if (Bits(pa.Value(p, i)) != Bits(pb.Value(p, i))) differ++;
                Check(differ == 0, $"climate with fix {v}: all six planes as the plain files give them ({cells:N0} values, {differ} differ)");
            }
        }

        // ------------------------------------------------------------------------------------------ EA-0087: codes, not floats

        /// <summary>The climate planes as the code before EA-0087 kept them: floats read from climate.bin's plain bytes, each
        /// fix's floats laid on the version it starts from (fixes 1 and 2 on climate.bin, 3 and later on the one before).</summary>
        static float[][] OldFloats(int version, Dictionary<int, float[][]> made)
        {
            if (made.TryGetValue(version, out var have)) return have;
            float[][] planes;
            if (version == 0)
            {
                byte[] file = Plain("climate.bin");
                int w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12), n = w * h;
                byte[] raw = EarthBinary.Unbrotli(file, 16, file.Length - 16, n * 10, "climate.bin");
                planes = new float[6][];
                for (int p = 0; p < 6; p++) planes[p] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    planes[0][i] = BitConverter.ToInt16(raw, 2 * i) / 100f;
                    planes[1][i] = BitConverter.ToInt16(raw, 2 * n + 2 * i) / 100f;
                    planes[2][i] = BitConverter.ToInt16(raw, 4 * n + 2 * i) / 100f;
                    planes[3][i] = BitConverter.ToUInt16(raw, 6 * n + 2 * i);
                    planes[4][i] = raw[8 * n + i];
                    planes[5][i] = raw[9 * n + i] / 255f;
                }
            }
            else
            {
                planes = (float[][])(version >= 3 ? OldFloats(version - 1, made) : OldFloats(0, made)).Clone();
                // fixes 1 and 2 as plain at the tag; 3 and 4 are plain files in the folder (4 came after the tag)
                string fixName = version == 1 ? "climatefix.bin" : "climatefix" + version + ".bin";
                byte[] file = version <= 2 ? Plain(fixName) : Current(fixName);
                bool layered = version >= 3;
                int head = layered ? 24 : 20, count = BitConverter.ToInt32(file, 16), mask = layered ? BitConverter.ToInt32(file, 20) : 0x3F;
                int bytes = 0;
                for (int p = 0; p < 6; p++) if ((mask >> p & 1) != 0) bytes += p < 4 ? 2 : 1;
                byte[] raw = EarthBinary.Unbrotli(file, head, file.Length - head, count * (4 + bytes), "fix");
                var idx = new int[count];
                Buffer.BlockCopy(raw, 0, idx, 0, 4 * count);
                if (layered) for (int k = 1; k < count; k++) idx[k] += idx[k - 1];
                int o = 4 * count;
                for (int p = 0; p < 6; p++)
                {
                    if ((mask >> p & 1) == 0) continue;
                    var plane = planes[p] = (float[])planes[p].Clone();
                    for (int k = 0; k < count; k++)
                        plane[idx[k]] = p switch
                        {
                            0 or 1 or 2 => BitConverter.ToInt16(raw, o + 2 * k) / 100f,
                            3 => BitConverter.ToUInt16(raw, o + 2 * k),
                            4 => raw[o + k],
                            _ => raw[o + k] / 255f,
                        };
                    o += p < 4 ? 2 * count : count;
                }
            }
            return made[version] = planes;
        }

        static void ClimateAsFloats()
        {
            var d = LoadClimate(Current);
            var made = new Dictionary<int, float[][]>();
            for (int v = 0; v <= EarthData.ClimateFixVersion; v++)
            {
                var old = OldFloats(v, made);
                var now = d.ClimatePlanes(v);
                long differ = 0;
                for (int p = 0; p < 6; p++)
                    for (int i = 0; i < old[p].Length; i++)
                        if (Bits(old[p][i]) != Bits(now.Value(p, i))) differ++;
                Check(differ == 0, $"climate fix {v} as codes: every value is the float the old planes held ({differ} differ)");
            }
            // a fix-4 world keeps climate.bin's codes and fix 4's, not the versions between
            var w = LoadClimate(Current);
            var four = w.ClimatePlanes(4);
            var ground = w.WeatherGround;
            var kept = (ClimateCodes[])typeof(EarthDataset).GetField("planesByFix", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(w);
            Check(kept[4] == four && kept[1] == null && kept[2] == null && kept[3] == null && ground != null,
                $"a fix-4 world keeps fix 4's planes only (fix 1-3 kept: {kept[1] != null}, {kept[2] != null}, {kept[3] != null}; weather ground {(ground != null ? "made" : w.WeatherGroundError)})");
        }

        static void TerrainTiles()
        {
            var d = EarthDataset.Load(name =>
            {
                string path = Path.Combine(root, "earthdata", name);
                return File.Exists(path) ? new MemoryStream(File.ReadAllBytes(path)) : null;
            }, true);
            int n = d.Tile * d.Tile, tiles = d.TilesX * d.TilesY;
            long cells = 0, differ = 0, wide = 0;
            byte[] file = File.ReadAllBytes(Path.Combine(root, "earthdata", "terrain.bin"));
            var q = new int[n];
            for (int key = 0; key < tiles; key++)
            {
                var t = d.DecodeTile(key);
                if (t.IceWide != null) wide++;
                // the old decode: elevation ElevationOf(q), ice q * 4f, from the tile's planar residuals
                int off = BitConverter.ToInt32(file, 24 + 8 * key), len = BitConverter.ToInt32(file, 28 + 8 * key);
                byte[] raw = EarthBinary.Unbrotli(file, off, len, n * 7, "terrain.bin");
                UnPaeth(raw, 0, q, d.Tile);
                for (int i = 0; i < n; i++, cells++) if (Bits(d.ElevationOf(q[i])) != Bits(d.ElevationOfCode(t.ElevCode[i]))) differ++;
                UnPaeth(raw, 5 * n, q, d.Tile);
                for (int i = 0; i < n; i++) if (Bits(q[i] * 4f) != Bits(t.IceAt(i))) differ++;
            }
            Check(differ == 0, $"terrain.bin as 16-bit codes: every cell's elevation and ice as the float tiles had them ({tiles} tiles, {cells:N0} cells, {differ} differ, {wide} tiles with ice beyond 16 bits)");
        }

        static void UnPaeth(byte[] raw, int offset, int[] q, int size)
        {
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    int i = r * size + c;
                    int a = c > 0 ? q[i - 1] : 0, b = r > 0 ? q[i - size] : 0, ab = c > 0 && r > 0 ? q[i - size - 1] : 0;
                    q[i] = BitConverter.ToInt16(raw, offset + 2 * i) + a + b - ab;
                }
        }

        static void Weather()
        {
            var a = WeatherData.Read(new MemoryStream(Plain("weather.bin")));
            var b = WeatherData.Read(new MemoryStream(Current("weather.bin")));
            Check(a.Signature == WeatherSignature && b.Signature == WeatherSignature,
                $"weather.bin keeps the plain file's signature (plain {a.Signature}, predicted {b.Signature}, frozen {WeatherSignature})");
            Check(a.W == b.W && a.H == b.H && a.HasWindsAndClouds == b.HasWindsAndClouds && a.RawPlanes.SequenceEqual(b.RawPlanes),
                $"weather.bin: every plane's every cell is the plain file's ({a.RawPlanes.Length:N0} values)");
            // the stored hash of the planes ties the signature to them: a file naming other planes is refused (L-3)
            var other = Current("weather.bin");
            other[28] ^= 1;
            Check(Throws(() => WeatherData.Read(new MemoryStream(other))), "a weather.bin whose planes are not the ones its signature was made of is refused");
        }

        /// <summary>(L-2) The predicted fixes name the climate.bin they are differences from: with a climate.bin one value
        /// off, they are refused rather than read as another climate.</summary>
        static void FixesPinTheirClimate()
        {
            byte[] file = Plain("climate.bin");
            int w = BitConverter.ToInt32(file, 8), h = BitConverter.ToInt32(file, 12);
            byte[] raw = EarthBinary.Unbrotli(file, 16, file.Length - 16, w * h * 10, "climate.bin");
            raw[3] ^= 1;
            var packed = new byte[raw.Length];
            System.IO.Compression.BrotliEncoder.TryCompress(raw, packed, out int n, 5, 22);
            var changed = new byte[16 + n];
            Array.Copy(file, changed, 16);
            Array.Copy(packed, 0, changed, 16, n);
            Check(Throws(() => LoadClimate(name => name == "climate.bin" ? changed : Current(name))),
                "climate fixes 1 and 2 made against another climate.bin are refused");
        }
    }
}
