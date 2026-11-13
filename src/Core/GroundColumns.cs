using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// One self-describing raster tile (EGT1, written by tools/ground/tiles.py): a latitude/longitude rectangle holding one
    /// or more fields on one grid, each with its scale, nodata value, interpolation and (for categories) a legend of names.
    /// Tiles stack by priority in <see cref="GroundData"/>. A tile keeps its compressed bytes and unpacks its cells the first
    /// time a column inside it asks; unpacked tiles share a memory budget (<see cref="BudgetBytes"/>) and the least recently
    /// used are packed again, so a world-wide tile set (EGTS) costs only the regions being generated.
    /// </summary>
    public sealed class GroundTile
    {
        public string Name, Source, Licence;
        public double West, North, Step;
        public int Nx, Ny;
        public readonly List<Field> Fields = new();

        public sealed class Field
        {
            public string Name;
            public bool Category;
            public string[] Legend;
            public double Scale = 1, Offset;
            /// <summary>"curve": "log1p": the scaled value v stands for exp(v) - 1 (depths log-coded in one byte); interpolated in v.</summary>
            public bool Log1p;
            internal int Index, Size, Start;
            internal double Nodata;
            internal bool NodataNaN;
        }

        /// <summary>Unpacked cells, one array per field (byte, ushort, short or float).</summary>
        sealed class Planes { public Array[] Arrays; public long Bytes; }

        byte[] packed; int bodyOffset, bodyLength;
        // tiles of a set read from a file (ReadAllFromFile): the body stays on disk until a column inside needs it
        string filePath; long fileBodyOffset;
        volatile Planes planes;
        long lastUse;
        readonly object gate = new();

        /// <summary>Memory for unpacked tiles across all ground tiles of the process (default 192 MB).</summary>
        public static long BudgetBytes = 192L << 20;
        static readonly object cacheGate = new();
        /// <summary>The tiles holding unpacked cells, with their bytes. Held weakly: the list keeps the process's budget, not the
        /// tiles. A single-player game goes on to another world in the same process, and a strong list kept a closed world's
        /// tiles (with their cells and file bytes) alive for good; a tile collected with its world drops out here with its bytes.</summary>
        static readonly List<(WeakReference<GroundTile> Tile, long Bytes)> unpacked = new();
        static long unpackedBytes, clock;

        /// <summary>Tiles in memory now and their bytes (tests and logs).</summary>
        public static (int Tiles, long Bytes) Unpacked { get { lock (cacheGate) { DropCollected(); return (unpacked.Count, unpackedBytes); } } }

        /// <summary>Takes the tiles the garbage collector has taken out of the budget (under <see cref="cacheGate"/>).</summary>
        static void DropCollected()
        {
            for (int i = unpacked.Count - 1; i >= 0; i--)
            {
                if (unpacked[i].Tile.TryGetTarget(out _)) continue;
                unpackedBytes -= unpacked[i].Bytes;
                unpacked.RemoveAt(i);
            }
        }

        /// <summary>A file holding one tile (EGT1) or a tile set (EGTS: count, then length-prefixed EGT1 tiles).</summary>
        public static List<GroundTile> ReadAll(byte[] file)
        {
            if (file != null && file.Length >= 8 && Encoding.ASCII.GetString(file, 0, 4) == "EGTS")
            {
                int count = (int)BitConverter.ToUInt32(file, 4), o = 8;
                var list = new List<GroundTile>(count);
                for (int i = 0; i < count; i++)
                {
                    if (o + 4 > file.Length) throw new FormatException("ground tile set is truncated");
                    int len = (int)BitConverter.ToUInt32(file, o);
                    if (len < 8 || o + 4 + len > file.Length) throw new FormatException("ground tile set is truncated");
                    list.Add(Read(file, o + 4, len, false));
                    o += 4 + len;
                }
                if (o != file.Length) throw new FormatException("ground tile set has trailing bytes");
                return list;
            }
            return new List<GroundTile> { Read(file) };
        }

        /// <summary>A tile set (or one tile) in a file, read lazily: only the headers now, each tile's cells from the file the
        /// first time a column inside it is generated (sets of hundreds of MB never sit in memory whole).</summary>
        public static List<GroundTile> ReadAllFromFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var br = new BinaryReader(fs);
            var list = new List<GroundTile>();
            GroundTile Next(long start, long length)
            {
                fs.Position = start;
                byte[] head8 = br.ReadBytes(8);
                if (head8.Length < 8) throw new FormatException("ground tile set is truncated");
                int headLength = (int)BitConverter.ToUInt32(head8, 4);
                if (headLength <= 0 || 8L + headLength > length) throw new FormatException("ground tile header is truncated");
                byte[] buf = new byte[8 + headLength];
                Buffer.BlockCopy(head8, 0, buf, 0, 8);
                if (fs.Read(buf, 8, headLength) != headLength) throw new FormatException("ground tile header is truncated");
                var t = Read(buf, 0, (int)length, false);
                t.packed = null; t.filePath = path; t.fileBodyOffset = start + 8 + headLength;
                return t;
            }
            byte[] magic = br.ReadBytes(8);
            if (magic.Length == 8 && Encoding.ASCII.GetString(magic, 0, 4) == "EGTS")
            {
                int count = (int)BitConverter.ToUInt32(magic, 4);
                long o = 8;
                for (int i = 0; i < count; i++)
                {
                    if (o + 4 > fs.Length) throw new FormatException("ground tile set is truncated");
                    fs.Position = o;
                    long len = br.ReadUInt32();
                    if (len < 8 || o + 4 + len > fs.Length) throw new FormatException("ground tile set is truncated");
                    list.Add(Next(o + 4, len));
                    o += 4 + len;
                }
                if (o != fs.Length) throw new FormatException("ground tile set has trailing bytes");
            }
            else list.Add(Next(0, fs.Length));
            return list;
        }

        /// <summary>One EGT1 tile, unpacked once here to check it.</summary>
        public static GroundTile Read(byte[] file) => Read(file, 0, file?.Length ?? 0, true);

        static GroundTile Read(byte[] file, int start, int length, bool check)
        {
            if (file == null || length < 8 || Encoding.ASCII.GetString(file, start, 4) != "EGT1") throw new FormatException("not an Earth ground tile");
            int headLength = (int)BitConverter.ToUInt32(file, start + 4);
            if (headLength <= 0 || 8 + headLength > length) throw new FormatException("ground tile header is truncated");
            using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(file, start + 8, headLength));
            var h = doc.RootElement;
            var t = new GroundTile
            {
                Name = h.GetProperty("name").GetString(), Source = Str(h, "source"), Licence = Str(h, "licence"),
                West = h.GetProperty("west").GetDouble(), North = h.GetProperty("north").GetDouble(), Step = h.GetProperty("step").GetDouble(),
                Nx = h.GetProperty("nx").GetInt32(), Ny = h.GetProperty("ny").GetInt32(),
                packed = file, bodyOffset = start + 8 + headLength, bodyLength = length - 8 - headLength,
            };
            if (t.Nx < 1 || t.Ny < 1 || !(t.Step > 0)) throw new FormatException("ground tile " + t.Name + " has an invalid grid");
            int n = t.Nx * t.Ny, o = 0;
            foreach (var e in h.GetProperty("fields").EnumerateArray())
            {
                string type = e.GetProperty("type").GetString();
                var f = new Field
                {
                    Name = e.GetProperty("name").GetString(),
                    Scale = e.TryGetProperty("scale", out var s) ? s.GetDouble() : 1,
                    Offset = e.TryGetProperty("offset", out var off) ? off.GetDouble() : 0,
                    Category = e.TryGetProperty("interp", out var ip) && ip.GetString() == "category",
                    Log1p = e.TryGetProperty("curve", out var cv) && cv.GetString() == "log1p",
                    Index = t.Fields.Count, Start = o,
                };
                var nd = e.GetProperty("nodata");
                if (nd.ValueKind == JsonValueKind.String) f.NodataNaN = true; else f.Nodata = nd.GetDouble();
                if (e.TryGetProperty("legend", out var lg)) f.Legend = lg.EnumerateArray().Select(x => string.Intern(x.GetString())).ToArray();
                if (f.Category && f.Legend == null) throw new FormatException($"ground tile {t.Name}: category field {f.Name} has no legend");
                f.Size = type switch { "u8" => 1, "u16" => 2, "i16" => 2, "f32" => 4, _ => throw new FormatException($"ground tile {t.Name}: unknown type {type}") };
                if (type == "i16") f.Size = -2;
                o += n * Math.Abs(f.Size);
                t.Fields.Add(f);
            }
            t.bodyBytes = o;
            if (check) t.Unpack();
            return t;
        }
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : "";
        int bodyBytes;

        Planes Unpack()
        {
            byte[] body, src = packed;
            int srcOffset = bodyOffset;
            if (src == null)
            {
                src = new byte[bodyLength]; srcOffset = 0;
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = fileBodyOffset;
                int got = 0;
                while (got < bodyLength) { int r = fs.Read(src, got, bodyLength - got); if (r <= 0) throw new FormatException($"ground tile {Name} is truncated in {filePath}"); got += r; }
            }
            using (var z = new ZLibStream(new MemoryStream(src, srcOffset, bodyLength), CompressionMode.Decompress))
            using (var m = new MemoryStream(bodyBytes))
            {
                z.CopyTo(m);
                body = m.ToArray();
            }
            if (body.Length != bodyBytes) throw new FormatException($"ground tile {Name} holds {body.Length} bytes, its header describes {bodyBytes}");
            int n = Nx * Ny;
            var arrays = new Array[Fields.Count];
            foreach (var f in Fields)
            {
                Array a = f.Size switch { 1 => new byte[n], 2 => new ushort[n], -2 => new short[n], _ => new float[n] };
                Buffer.BlockCopy(body, f.Start, a, 0, n * Math.Abs(f.Size));
                arrays[f.Index] = a;
            }
            return new Planes { Arrays = arrays, Bytes = body.Length };
        }

        /// <summary>The tile's cells, unpacked if needed; the result stays valid for the caller even if the tile is packed
        /// again meanwhile.</summary>
        Planes Cells()
        {
            var p = planes;
            long tick = Interlocked.Increment(ref clock);
            Volatile.Write(ref lastUse, tick);
            if (p != null) return p;
            lock (gate)
            {
                p = planes;
                if (p != null) return p;
                p = Unpack();
                planes = p;
            }
            lock (cacheGate)
            {
                DropCollected();
                unpacked.Add((new WeakReference<GroundTile>(this), p.Bytes));
                unpackedBytes += p.Bytes;
                while (unpackedBytes > BudgetBytes && unpacked.Count > 1)
                {
                    // the least recently used tile (the first of equals) is packed again
                    int oldest = -1;
                    GroundTile oldestTile = null;
                    for (int i = 0; i < unpacked.Count; i++)
                    {
                        if (!unpacked[i].Tile.TryGetTarget(out var t) || t == this) continue;
                        if (oldestTile == null || Volatile.Read(ref t.lastUse) < Volatile.Read(ref oldestTile.lastUse)) { oldest = i; oldestTile = t; }
                    }
                    if (oldestTile == null) break;
                    oldestTile.planes = null;
                    unpackedBytes -= unpacked[oldest].Bytes;
                    unpacked.RemoveAt(oldest);
                }
            }
            return p;
        }

        /// <summary>Stored value at a cell, or NaN for nodata.</summary>
        static double Raw(Planes p, Field f, int i)
        {
            double v = p.Arrays[f.Index] switch { byte[] b => b[i], ushort[] u => u[i], short[] s => s[i], float[] x => x[i], _ => double.NaN };
            if (f.NodataNaN ? double.IsNaN(v) : v == f.Nodata) return double.NaN;
            return v;
        }

        public bool Contains(double lat, double lon) => lon >= West && lon < West + Nx * Step && lat <= North && lat > North - Ny * Step;

        /// <summary>Bilinear over the cell centres; nodata corners drop out (their weight goes to the others).</summary>
        public bool TryLinear(Field f, double lat, double lon, out double value)
        {
            value = double.NaN;
            if (!Contains(lat, lon)) return false;
            double fx = Math.Clamp((lon - West) / Step - .5, 0, Nx - 1), fy = Math.Clamp((North - lat) / Step - .5, 0, Ny - 1);
            int x0 = Math.Min((int)fx, Nx - 1), y0 = Math.Min((int)fy, Ny - 1);
            int x1 = Math.Min(x0 + 1, Nx - 1), y1 = Math.Min(y0 + 1, Ny - 1);
            var p = Cells();
            double tx = fx - x0, ty = fy - y0, sum = 0, weight = 0;
            void Add(int x, int y, double w)
            {
                if (w <= 0) return;
                double v = Raw(p, f, y * Nx + x);
                if (double.IsNaN(v)) return;
                sum += v * w; weight += w;
            }
            Add(x0, y0, (1 - tx) * (1 - ty)); Add(x1, y0, tx * (1 - ty)); Add(x0, y1, (1 - tx) * ty); Add(x1, y1, tx * ty);
            if (weight < 1e-9) return false;
            value = sum / weight * f.Scale + f.Offset;
            if (f.Log1p) value = Math.Exp(value) - 1;
            return true;
        }

        /// <summary>
        /// The category at a warped position, so the tile's cells never show as squares: the warp is about 0.8 cell (at most
        /// 0.14 degree) with a wavelength of about 1.7 cells (at most 0.35 degree).
        /// </summary>
        public bool TryCategory(Field f, double lat, double lon, long seed, int salt, out string name)
        {
            name = null;
            double wave = Math.Min(1.7 * Step, .35), amp = Math.Min(.8 * Step, .14);
            double u = lon / wave, v = lat / wave;
            double wl = lon + (GeologyTransitions.Noise(u, v, seed, salt) * 2 - 1) * amp;
            double wt = lat + (GeologyTransitions.Noise(u, v, seed, salt + 1) * 2 - 1) * amp * .75;
            if (!Contains(wt, wl)) { wl = lon; wt = lat; if (!Contains(wt, wl)) return false; }
            int i = Math.Clamp((int)Math.Floor((wl - West) / Step), 0, Nx - 1), j = Math.Clamp((int)Math.Floor((North - wt) / Step), 0, Ny - 1);
            double raw = Raw(Cells(), f, j * Nx + i);
            if (double.IsNaN(raw) || raw < 0 || raw >= f.Legend.Length) return false;
            name = f.Legend[(int)raw];
            return true;
        }

        /// <summary>The scaled value of one cell (column <paramref name="i"/>, row <paramref name="j"/> from the north), unwarped; NaN for nodata.</summary>
        public double ValueAtCell(Field f, int i, int j)
        {
            if (i < 0 || i >= Nx || j < 0 || j >= Ny) return double.NaN;
            double raw = Raw(Cells(), f, j * Nx + i);
            if (double.IsNaN(raw)) return double.NaN;
            double v = raw * f.Scale + f.Offset;
            return f.Log1p ? Math.Exp(v) - 1 : v;
        }

        /// <summary>The category of one cell (column <paramref name="i"/>, row <paramref name="j"/> from the north), unwarped; null for nodata.</summary>
        public string CategoryAtCell(Field f, int i, int j)
        {
            if (i < 0 || i >= Nx || j < 0 || j >= Ny) return null;
            double raw = Raw(Cells(), f, j * Nx + i);
            return double.IsNaN(raw) || raw < 0 || raw >= f.Legend.Length ? null : f.Legend[(int)raw];
        }
    }

    /// <summary>
    /// The tile stack: for a field at a position, the highest-priority tile that covers the position and has a value there
    /// answers; nodata falls through to the next. Field names are the generator's vocabulary (<see cref="GroundColumns.FieldNames"/>).
    /// </summary>
    public sealed class GroundData
    {
        readonly Dictionary<string, List<(GroundTile Tile, GroundTile.Field Field, int Priority)>> byField = new(StringComparer.Ordinal);
        public readonly List<(GroundTile Tile, int Priority)> Tiles = new();

        public void Add(GroundTile tile, int priority)
        {
            Tiles.Add((tile, priority));
            foreach (var f in tile.Fields)
            {
                if (!byField.TryGetValue(f.Name, out var list)) byField[f.Name] = list = new();
                int at = list.FindIndex(x => x.Priority < priority);   // after every tile of the same or higher priority
                list.Insert(at < 0 ? list.Count : at, (tile, f, priority));
            }
        }

        static readonly List<(GroundTile, GroundTile.Field, int)> none = new();
        public List<(GroundTile Tile, GroundTile.Field Field, int Priority)> Sources(string field) => byField.TryGetValue(field, out var l) ? l : none;

        public bool TryNumber(string field, double lat, double lon, out double value, out string source)
        {
            foreach (var (t, f, _) in Sources(field))
                if (!f.Category && t.TryLinear(f, lat, lon, out value)) { source = t.Name; return true; }
            value = double.NaN; source = null; return false;
        }

        public bool TryCategory(string field, double lat, double lon, long seed, out string name, out string source)
        {
            int salt = 4200 + (field.Length * 31 + field[0]) * 7;
            foreach (var (t, f, _) in Sources(field))
                if (f.Category && t.TryCategory(f, lat, lon, seed, salt, out name)) { source = t.Name; return true; }
            name = null; source = null; return false;
        }
    }

    /// <summary>
    /// Ground columns: soil, Quaternary cover, bedrock and basement in metres below each column's own top, mapped to blocks by
    /// a log depth curve (one block per metre at the surface, the floor depth at the bottom of a lowland column). Fields come
    /// from a stack of data tiles; how a column is built from them (recipes per unit name, stratigraphic templates,
    /// compaction, fallbacks) comes from the rules. Game-independent. Design: docs/research/geology-columns-2026-11-06.
    /// </summary>
    public sealed class GroundColumns
    {
        public const int RulesVersion = 1;
        public const string RulesAsset = "earth:config/ground-rules-v1.json";

        /// <summary>The fields the generator reads. Tiles may hold any of them, under exactly these names.</summary>
        public static class FieldNames
        {
            public const string Quaternary = "quaternary_m", Basement = "basement_m", Cover = "cover_unit", Subcrop = "subcrop",
                SubcropLith = "subcrop_lith", Soil = "soil_group", Clay = "clay_pct", Ph = "ph", Seabed = "seabed",
                BasementRock = "basement_rock", Province = "province", Era = "bedrock_era";
        }
        /// <summary>Natural fertility classes the rules name, poorest first.</summary>
        public static readonly string[] Fertility = { "verylow", "low", "medium", "high" };
        /// <summary>
        /// The vanilla soil code of each class. Natural high fertility is <c>soil-compost</c> ("High fertility soil", which
        /// vanilla only scatters in small patches); <c>soil-high</c> is terra preta, crafted from compost, charcoal and
        /// bonemeal, and never generated here (the author, 6 November).
        /// </summary>
        public static readonly string[] FertilityBlockCodes = { "verylow", "low", "medium", "compost" };

        static JsonDocument Parse(string json) => JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        /// <summary>The tiles a rules file lists: an asset location, or a file in Earth's data folder ("file", too big for
        /// Earth's archives); its priority (higher answers first); optional ones may be missing.</summary>
        public static List<(string Asset, string File, int Priority, bool Optional)> TileList(string rulesJson)
        {
            using var doc = Parse(rulesJson);
            return doc.RootElement.GetProperty("tiles").EnumerateArray()
                .Select(t => (t.TryGetProperty("asset", out var a) ? a.GetString() : null, t.TryGetProperty("file", out var f) ? f.GetString() : null,
                              t.GetProperty("priority").GetInt32(), t.TryGetProperty("optional", out var o) && o.GetBoolean())).ToList();
        }

        /// <summary>Optional tiles that were not found (their area falls back to lower-priority tiles).</summary>
        public List<string> MissingTiles { get; private set; } = new();

        /// <summary>Loads the rules and every tile they list: assets through <paramref name="readAsset"/> (null for a missing
        /// asset), data-folder files through <paramref name="dataFile"/> (their full path; read lazily).</summary>
        public static GroundColumns Load(string rulesJson, Func<string, byte[]> readAsset, DepthCurve curve, long seed, Func<string, string> dataFile = null)
        {
            var data = new GroundData();
            var missing = new List<string>();
            foreach (var (asset, file, priority, optional) in TileList(rulesJson))
            {
                List<GroundTile> list = null;
                if (file != null)
                {
                    string path = dataFile?.Invoke(file);
                    if (path != null && File.Exists(path)) list = GroundTile.ReadAllFromFile(path);
                }
                else
                {
                    byte[] bytes = readAsset(asset);
                    if (bytes != null) list = GroundTile.ReadAll(bytes);
                }
                if (list == null)
                {
                    if (!optional) throw new FileNotFoundException("ground tile " + (asset ?? file) + " is missing");
                    missing.Add(asset ?? file);
                    continue;
                }
                foreach (var tile in list) data.Add(tile, priority);
            }
            return new GroundColumns(data, rulesJson, curve, seed) { MissingTiles = missing };
        }

        // ------------------------------------------------------------------ depth curve

        /// <summary>
        /// Metres below the top to blocks. Starts at <see cref="Top"/> metres per block, the terrain's own vertical scale at sea
        /// level, so digging matches the height of the hills around (the author, 6 November: not deeper than real). Linear, or
        /// logarithmic below that, blocks(d) = (d0/Top) ln(1 + d/d0), which keeps the top near the terrain's scale and shrinks
        /// thick deep layers. Thin layers keep at least one block (rules: minLayerMetres), whatever the curve.
        /// </summary>
        public readonly struct DepthCurve
        {
            /// <summary>Earth-tab choices (earthGroundDepth): linear at the terrain's scale; terrain scale at the top and 2000 or
            /// 4000 m at y = 1 under a lowland; linear reaching at least 2000 m at y = 1.</summary>
            public const string Terrain = "terrain", Log2000 = "log2000", Log4000 = "log4000", Linear2000 = "linear2000";
            public static readonly string[] Choices = { Log4000, Terrain, Log2000, Linear2000 };
            /// <summary>The default (the author, 7 November, after the 1:10 bench shafts): terrain scale at the top, the strong log
            /// (fitted to 4000 m at y = 1, so 2000 m lands about 30 blocks above it with basement below). All four stay as
            /// settings so players can choose and the default can change (the author, 7 November).</summary>
            public const string Default = Log4000;

            /// <summary>Metres per block at the top; the knee (positive infinity: linear).</summary>
            public readonly double Top, D0;
            public DepthCurve(double top, double d0) { Top = top > 0 && double.IsFinite(top) ? top : 1; D0 = d0 > 0 ? d0 : double.PositiveInfinity; }
            public bool Linear => double.IsPositiveInfinity(D0);
            public double Blocks(double metres) { double m = Math.Max(0, metres); return Linear ? m / Top : D0 / Top * Math.Log(1 + m / D0); }
            public double Metres(double blocks) { double b = Math.Max(0, blocks); return Linear ? b * Top : D0 * (Math.Exp(b * Top / D0) - 1); }

            /// <summary><paramref name="top"/> metres per block at the top and <paramref name="floorMetres"/> after
            /// <paramref name="blocks"/> blocks; linear when that floor is no deeper than the linear one.</summary>
            public static DepthCurve Fit(double top, double floorMetres, double blocks)
            {
                if (!(top > 0) || !(blocks > 1) || !(floorMetres > top * blocks)) return new DepthCurve(top, double.PositiveInfinity);
                double lo = 1e-3, hi = 1e12;
                for (int i = 0; i < 200; i++)
                {
                    double mid = Math.Sqrt(lo * hi);
                    double b = mid / top * Math.Log(1 + floorMetres / mid); // rises with d0 towards floor/top
                    if (b > blocks) hi = mid; else lo = mid;
                }
                return new DepthCurve(top, Math.Sqrt(lo * hi));
            }

            /// <summary>The curve for an Earth-tab choice in a world whose terrain has <paramref name="terrainMetresPerBlock"/>
            /// at sea level. The deep end is fitted to a lowland column: sea level + 5 down to y = 1. Unknown or missing choices
            /// (saves from before the option): <see cref="Default"/>.</summary>
            public static DepthCurve For(string choice, double terrainMetresPerBlock, int seaLevel)
            {
                double t = terrainMetresPerBlock > 0 && double.IsFinite(terrainMetresPerBlock) ? terrainMetresPerBlock : 1;
                int n = seaLevel + 5 - 1;
                return (Array.IndexOf(Choices, choice) >= 0 ? choice : Default) switch
                {
                    Terrain => new DepthCurve(t, double.PositiveInfinity),
                    Log2000 => Fit(t, 2000, n),
                    Linear2000 => new DepthCurve(Math.Max(t, 2000.0 / n), double.PositiveInfinity),
                    _ => Fit(t, 4000, n),
                };
            }

            public override string ToString() => Linear
                ? string.Format(CultureInfo.InvariantCulture, "linear {0:0.0} m per block", Top)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0} m per block at the top, knee {1:0} m ({2:0.0} m per block at 1000 m)", Top, D0, Top * (1 + 1000 / D0));
        }

        // ------------------------------------------------------------------ samples

        /// <summary>The fields at one position after the tile stack and the rules' fallbacks; the sources say where each came from (null = a rule).</summary>
        public struct Sample
        {
            public double Lat, Lon;
            public double QuaternaryMetres, BasementMetres, Clay, Ph;
            /// <summary>Recipe name of the surface sediment unit (after mapping the source's unit name).</summary>
            public string Cover;
            public string CoverUnit, Subcrop, SubcropLith, Soil, Seabed, BasementRock, Province, Era;
            public string QSource, BasementSource, CoverSource, SubcropSource, SoilSource, SeabedSource;
            /// <summary>The cover.thickness rule that corrected <see cref="QuaternaryMetres"/>, null when none (shown after QSource).</summary>
            public string QRule;
            /// <summary>Loose cover below the active layer is frozen (the rules' <c>frozen</c> condition holds).</summary>
            public bool Frozen;
            /// <summary>Earth's regional geology maps rock here, so the default template hands the rock to it.</summary>
            public bool RegionalRock;
            /// <summary>A real desert (<see cref="Place.Arid"/>): dry land without topsoil.</summary>
            public bool Arid;
            /// <summary>Decision 0070: a shore strip's class (<see cref="Place.Shore"/>), 0 for none; and how many blocks the
            /// column stands above the water beside it (<see cref="Place.ShoreRiseBlocks"/>).</summary>
            public byte Shore;
            public int ShoreRiseBlocks;
            /// <summary>The world's shore rules (<see cref="Place.ShoreRules"/>).</summary>
            public int ShoreRules;
            /// <summary>The place's height above the mean ground around it (m) and slope, NaN when unknown; the factor the
            /// rules' relief section applied to the cover thickness (1 when none).</summary>
            public double Relief, Slope, ReliefFactor;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "cover {0} ({1}, {2}) {3:0.#} m ({4}) over {5}{6} ({7}), basement at {8:0} m ({9}); soil {10} ({11}){12}{13}{14}{15}",
                Cover, CoverUnit ?? "-", CoverSource ?? "rule", QuaternaryMetres, QRule == null ? QSource ?? "rule" : QSource + ", " + QRule, Subcrop ?? "default unit", SubcropLith != null ? " " + SubcropLith : "",
                SubcropSource ?? "rule", BasementMetres, BasementSource ?? "rule", Soil ?? "default", SoilSource ?? "rule",
                double.IsNaN(Clay) ? "" : string.Format(CultureInfo.InvariantCulture, ", clay {0:0} %, pH {1:0.0}", Clay, Ph),
                Seabed != null ? $", seabed {Seabed} ({SeabedSource})" : "", (Frozen ? ", frozen" : "") + (Arid ? ", arid (no topsoil)" : ""),
                Province != null || Era != null ? $"; province {Province ?? "-"}, bedrock era {Era ?? "-"}" : "")
                + (double.IsNaN(Relief) ? "" : string.Format(CultureInfo.InvariantCulture, "; relief {0:+0;-0;0} m, slope {1:0.00}, cover x{2:0.00}", Relief, Slope, ReliefFactor));
        }

        /// <summary>What the game knows about a column's place: real elevation (m; negative under the sea), its top, whether
        /// it lies in a river's valley, and whether Earth's regional geology maps rock there. <see cref="Unknown"/>: none of it.</summary>
        public struct Place
        {
            public double Elevation;
            public Surface Surface;
            public bool Lake, NearRiver, RegionalRock;
            /// <summary>Decision 0048, soil rules 2: a real desert (SoilFertility.AridGround), so dry land here has no topsoil
            /// and its cover's own top shows. False in older worlds and wherever the game doesn't say.</summary>
            public bool Arid;
            /// <summary>Decision 0048, soil rules 3: in a river's natural flood (FloodplainGrid), so arid ground here keeps its
            /// topsoil unless its cover is wind-blown sand (<see cref="WindSand"/>). False in older worlds.</summary>
            public bool Floodplain;
            /// <summary>Decision 0070: a beach of the shore strip, with OSM's class there (Core.ShoreMaterial: Sand .. Mud, Beach
            /// untagged, Rule none), so its material is the coast's (<see cref="ShoreSubstrate"/>); 0: beach sand, as before.
            /// Shore rules 2: also the sea's or a lake's shallows in front of a shore (Surface.Water).</summary>
            public byte Shore;
            /// <summary>Shore rules 2: a foredune's height above the water beside it (blocks), so its sand reaches down to the
            /// beach and the step up from it is sand; 0 elsewhere.</summary>
            public int ShoreRiseBlocks;
            /// <summary>The world's shore rules (EarthParams.ShoreRules, Core.ShoreMaterial.RulesVersion): 3 makes a shore on thin
            /// glacial till bare rock where OSM maps no beach (<see cref="ShoreSubstrate"/>); 0 where the game doesn't say.</summary>
            public int ShoreRules;
            /// <summary>Relief and Slope are known (GroundRelief): metres above the mean ground within the rules' radius
            /// (negative in hollows and valleys), and rise over run.</summary>
            public bool HasRelief;
            public double Relief, Slope;
            /// <summary>The mean of <see cref="ReliefWeight"/> over the neighbourhood (GroundRelief's norm); 0 or less: 1.</summary>
            public double ReliefNorm;
            public static Place Unknown => new() { Elevation = double.NaN, RegionalRock = true };
            internal string SurfaceName => Surface switch
            {
                Surface.Water => Lake ? "lake" : "sea", Surface.RiverBed => "riverbed", Surface.Beach => "beach", _ => "land",
            };
        }

        public readonly GroundData Data;
        public readonly DepthCurve Curve;
        /// <summary>A layer at least this thick keeps one block at any depth; thinner ones vanish where the curve gives them none.</summary>
        public readonly double MinLayerMetres;
        public readonly long Seed;
        /// <summary>Where the generator is switched on: west, south, east, north.</summary>
        public readonly double ExtentWest, ExtentSouth, ExtentEast, ExtentNorth;
        /// <summary>Material names in rules order; each has one or more candidate block codes, first that exists wins.</summary>
        public readonly string[] Materials;
        public readonly string[][] Candidates;
        public readonly int Topsoil, Peat, BeachSand;
        public string Fingerprint { get; }

        sealed class Layer { public int Material; public double Share, Max = double.PositiveInfinity, Vary, Metres; public string Lith, Name; }
        sealed class Recipe { public double TypicalMetres, MaxMetres; public Layer[] Layers; }
        sealed class Unit { public string Name; public Layer[] Layers; public double FadeFrom = double.NaN, FadeTo; }
        /// <summary>A stratigraphic template. HandOff: no bedrock here; the column stops below the cover and the rock
        /// beneath stays as Earth's regional geology (or vanilla) placed it. Area: west, south, east, north where it applies.</summary>
        sealed class Template
        {
            public string Name; public Unit[] Units; public bool HandOff; public double[] Area;
            /// <summary>Generic templates: bedrock era name -> the unit the column starts at ("*" any other, "basement").</summary>
            public Dictionary<string, string> Eras;
            /// <summary>Basement bodies of this template, null for the rules' basement mix.</summary>
            public (int Material, double Share)[] Basement;
        }
        sealed class Substrate { public int Material; public double Metres; }
        sealed class SoilRule { public int Fertility; public double Topsoil; public bool Peat; }

        readonly Dictionary<string, string> coverUnits = new(StringComparer.Ordinal);
        readonly string coverDefault;
        /// <summary>
        /// Where a rule applies; every key given must hold. <c>areas</c>: boxes west, south, east, north. <c>field</c> with
        /// <c>values</c> (a category) or <c>min</c>/<c>max</c> (a number), and more such tests in <c>fields</c>. <c>elevation</c>
        /// [from, to) in real metres (unknown elevation fails). <c>absLat</c> [from, to). <c>surface</c>: land, beach, sea,
        /// lake, riverbed. <c>nearRiver</c>: in a river's water, bank or valley.
        /// </summary>
        sealed class Condition
        {
            sealed class Test { public string Field; public HashSet<string> Values; public double Min = double.NegativeInfinity, Max = double.PositiveInfinity; }
            Condition[] anyOf; double[][] areas; Test[] tests; double[] elevation, absLat, relief, slope; HashSet<string> surfaces; bool? nearRiver;

            public static Condition Parse(JsonElement e)
            {
                static double[] Pair(JsonElement a) => new[] { a[0].GetDouble(), a[1].GetDouble() };
                static Test TestOf(JsonElement t) => new Test
                {
                    Field = t.GetProperty("field").GetString(),
                    Values = t.TryGetProperty("values", out var vs) ? new HashSet<string>(vs.EnumerateArray().Select(x => x.GetString()), StringComparer.Ordinal) : null,
                    Min = t.TryGetProperty("min", out var mn) ? mn.GetDouble() : double.NegativeInfinity,
                    Max = t.TryGetProperty("max", out var mx) ? mx.GetDouble() : double.PositiveInfinity,
                };
                var tests = new List<Test>();
                if (e.TryGetProperty("field", out _)) tests.Add(TestOf(e));
                if (e.TryGetProperty("fields", out var fs)) tests.AddRange(fs.EnumerateArray().Select(TestOf));
                return new Condition
                {
                    areas = e.TryGetProperty("areas", out var ar) ? ar.EnumerateArray().Select(a => a.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray() : null,
                    tests = tests.ToArray(),
                    elevation = e.TryGetProperty("elevation", out var el) ? Pair(el) : null,
                    absLat = e.TryGetProperty("absLat", out var al) ? Pair(al) : null,
                    relief = e.TryGetProperty("relief", out var rl) ? Pair(rl) : null,
                    slope = e.TryGetProperty("slope", out var sl) ? Pair(sl) : null,
                    anyOf = e.TryGetProperty("anyOf", out var ao) ? ao.EnumerateArray().Select(Parse).ToArray() : null,
                    surfaces = e.TryGetProperty("surface", out var sf) ? new HashSet<string>(sf.EnumerateArray().Select(x => x.GetString()), StringComparer.Ordinal) : null,
                    nearRiver = e.TryGetProperty("nearRiver", out var nr) ? nr.GetBoolean() : null,
                };
            }

            /// <param name="soil">The sample's soil group, already read (soil tests are the most common).</param>
            public bool Holds(GroundColumns g, double lat, double lon, in Place p, string soil)
            {
                // plain loops here and below: this runs for every column of every chunk, and a lambda would allocate each time
                if (areas != null)
                {
                    bool inArea = false;
                    foreach (var b in areas) if (lon >= b[0] && lon < b[2] && lat >= b[1] && lat < b[3]) { inArea = true; break; }
                    if (!inArea) return false;
                }
                if (absLat != null && (Math.Abs(lat) < absLat[0] || Math.Abs(lat) >= absLat[1])) return false;
                if (elevation != null && !(p.Elevation >= elevation[0] && p.Elevation < elevation[1])) return false;
                if (surfaces != null && !surfaces.Contains(p.SurfaceName)) return false;
                if (nearRiver != null && p.NearRiver != nearRiver.Value) return false;
                // relief and slope: unknown (no terrain, tests) never matches
                if (relief != null && !(p.HasRelief && p.Relief >= relief[0] && p.Relief < relief[1])) return false;
                if (slope != null && !(p.HasRelief && p.Slope >= slope[0] && p.Slope < slope[1])) return false;
                if (anyOf != null)
                {
                    bool any = false;
                    foreach (var c in anyOf) if (c.Holds(g, lat, lon, p, soil)) { any = true; break; }
                    if (!any) return false;
                }
                foreach (var t in tests)
                {
                    if (t.Values != null)
                    {
                        string v = soil;
                        if (t.Field != FieldNames.Soil) g.Data.TryCategory(t.Field, lat, lon, g.Seed, out v, out _);
                        if (v == null || !t.Values.Contains(v)) return false;
                    }
                    else if (!g.Data.TryNumber(t.Field, lat, lon, out double x, out _) || x < t.Min || x >= t.Max) return false;
                }
                return true;
            }
        }

        /// <summary>Unit maps that replace coverUnits where their condition holds (first match wins). Keys: a unit name,
        /// "default" (no unit mapped here) or "*" (any unit, and no unit when "default" is not given).</summary>
        sealed class CoverRegion
        {
            public Condition When; public Dictionary<string, string> Units;
            public string Recipe(string unit)
                => unit == null ? (Units.TryGetValue("default", out var d) ? d : Units.GetValueOrDefault("*"))
                   : Units.TryGetValue(unit, out var r) ? r : Units.GetValueOrDefault("*");
        }
        readonly List<CoverRegion> coverRegions = new();
        /// <summary>Rules' cover.thickness corrections: where a data set is known to run thick or thin (Shangguan on the glaciated
        /// shields, under sand seas), its value is scaled and held within minMetres..maxMetres. First match wins; only values
        /// read from a tile whose source names one of Sources, and only for the listed recipes when Recipes is given.</summary>
        sealed class ThicknessRule
        {
            public string Name; public Condition When; public string[] Sources; public HashSet<string> Recipes;
            public double Scale = 1, MinMetres, MaxMetres = double.PositiveInfinity;
        }
        readonly List<ThicknessRule> thicknessRules = new();
        /// <summary>Rules' relief section: the radius (real m) for the mean ground around a place, 0 when the rules have none.</summary>
        public readonly double ReliefRadiusMetres;
        readonly double reliefScale, reliefMin, reliefMax, reliefSlopeHalf;
        readonly Condition reliefWhen;
        readonly HashSet<string> reliefSkip;
        /// <summary>The cover factor for a place's height above the mean ground, before the slope and the neighbourhood norm.</summary>
        public double ReliefWeight(double relief) => Math.Clamp(Math.Exp(-relief / reliefScale), reliefMin, reliefMax);
        readonly List<(Condition When, string Class)> seabedZones = new();
        Condition frozenWhen;
        double frozenBelow;
        int[] frozenTo;
        /// <summary>Each loose material that freezes below the active layer, with the material it becomes there.</summary>
        public IEnumerable<(int Material, int Frozen)> FrozenPairs()
        {
            for (int m = 0; m < frozenTo.Length; m++) if (frozenTo[m] >= 0) yield return (m, frozenTo[m]);
        }
        readonly Dictionary<string, Template> provinceTemplates = new(StringComparer.Ordinal);
        readonly Dictionary<string, Recipe> recipes = new(StringComparer.Ordinal);
        readonly Dictionary<string, Substrate> seabed = new(StringComparer.Ordinal);
        readonly Substrate seabedDefault, riverBed, beach;
        readonly int shoreGravel, shoreMud, shoreCarbonate, shoreTephra;
        readonly Dictionary<string, Template> templates = new(StringComparer.Ordinal);
        readonly Template defaultTemplate;
        readonly Template[] areaTemplates;
        readonly Dictionary<string, string> subcrop = new(StringComparer.Ordinal);
        readonly string defaultUnit;
        readonly Dictionary<string, int> basementRocks = new(StringComparer.Ordinal);
        readonly (int Material, double Share)[] basementMix;
        readonly double basementBodyDegrees;
        readonly int basementDefault;
        readonly double defaultBasementMetres;
        readonly double[] compactBelow;
        readonly int[] compactTo;
        readonly Dictionary<string, SoilRule> soils = new(StringComparer.Ordinal);
        readonly SoilRule soilDefault;
        readonly double fertilityClayBump, fertilityPhBump;
        readonly HashSet<string> unknownNames = new(StringComparer.Ordinal);

        /// <summary>Source names the rules do not know, each reported once ("field:name"). A new map needs rule entries for its names.</summary>
        public event Action<string> UnknownName;

        /// <param name="curve">Metres to blocks for this world (<see cref="DepthCurve.For"/>).</param>
        public GroundColumns(GroundData data, string rulesJson, DepthCurve curve, long seed)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Seed = seed;
            Curve = curve;
            using var doc = Parse(rulesJson);
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != RulesVersion) throw new FormatException("ground rules version mismatch");
            var ext = root.GetProperty("extent");
            ExtentWest = ext[0].GetDouble(); ExtentSouth = ext[1].GetDouble(); ExtentEast = ext[2].GetDouble(); ExtentNorth = ext[3].GetDouble();
            MinLayerMetres = root.GetProperty("curve").GetProperty("minLayerMetres").GetDouble();

            var names = new List<string>(); var cands = new List<string[]>();
            foreach (var m in root.GetProperty("materials").EnumerateObject())
            {
                names.Add(m.Name);
                cands.Add(m.Value.EnumerateArray().Select(e => e.GetString()).ToArray());
            }
            Materials = names.ToArray(); Candidates = cands.ToArray();
            Topsoil = Mat("topsoil"); Peat = Mat("peat"); BeachSand = Mat("beach-sand");
            int Opt(string name) => Array.IndexOf(Materials, name);
            shoreGravel = Mat("gravel"); shoreMud = Mat("mud"); shoreCarbonate = Opt("carbonate-sand"); shoreTephra = Opt("tephra");

            Layer[] Layers(JsonElement arr) => arr.EnumerateArray().Select(e => new Layer
            {
                Material = Mat(e.GetProperty("m").GetString()),
                Share = e.TryGetProperty("share", out var s) ? s.GetDouble() : 0,
                Max = e.TryGetProperty("max", out var x) ? x.GetDouble() : double.PositiveInfinity,
                Vary = e.TryGetProperty("vary", out var v) ? v.GetDouble() : 0,
                Metres = e.TryGetProperty("metres", out var mm) ? mm.GetDouble() : 0,
                Lith = e.TryGetProperty("lith", out var l) ? l.GetString() : null,
                Name = e.TryGetProperty("name", out var n) ? n.GetString() : null,
            }).ToArray();

            var cover = root.GetProperty("cover");
            foreach (var u in cover.GetProperty("units").EnumerateObject()) coverUnits[u.Name] = u.Value.GetString();
            coverDefault = cover.GetProperty("default").GetString();
            foreach (var r in cover.GetProperty("recipes").EnumerateObject())
                recipes[r.Name] = new Recipe
                {
                    TypicalMetres = r.Value.GetProperty("typicalMetres").GetDouble(), Layers = Layers(r.Value.GetProperty("layers")),
                    MaxMetres = r.Value.TryGetProperty("maxMetres", out var mx) ? mx.GetDouble() : double.PositiveInfinity,
                };
            if (cover.TryGetProperty("thickness", out var ths))
                foreach (var t in ths.EnumerateArray())
                {
                    var rule = new ThicknessRule
                    {
                        Name = t.GetProperty("name").GetString(), When = Condition.Parse(t),
                        Sources = t.TryGetProperty("sources", out var so) ? so.EnumerateArray().Select(x => x.GetString()).ToArray() : null,
                        Recipes = t.TryGetProperty("recipes", out var rs) ? new HashSet<string>(rs.EnumerateArray().Select(x => x.GetString()), StringComparer.Ordinal) : null,
                        Scale = t.TryGetProperty("scale", out var sc) ? sc.GetDouble() : 1,
                        MinMetres = t.TryGetProperty("minMetres", out var tn) ? tn.GetDouble() : 0,
                        MaxMetres = t.TryGetProperty("maxMetres", out var tx) ? tx.GetDouble() : double.PositiveInfinity,
                    };
                    foreach (var r in rule.Recipes ?? Enumerable.Empty<string>()) if (!recipes.ContainsKey(r)) throw new KeyNotFoundException("thickness rule '" + rule.Name + "' names undefined recipe '" + r + "'");
                    thicknessRules.Add(rule);
                }
            if (cover.TryGetProperty("regions", out var regs))
                foreach (var r in regs.EnumerateArray())
                {
                    var units = new Dictionary<string, string>(StringComparer.Ordinal);
                    // a unit mapped to null: this region does not apply to it (the next region or the unit map decides)
                    foreach (var u in r.GetProperty("units").EnumerateObject()) units[u.Name] = u.Value.ValueKind == JsonValueKind.Null ? null : u.Value.GetString();
                    coverRegions.Add(new CoverRegion { Units = units, When = Condition.Parse(r) });
                }
            foreach (var v in coverUnits.Values.Append(coverDefault).Concat(coverRegions.SelectMany(r => r.Units.Values)).Where(v => v != null))
                if (!recipes.ContainsKey(v)) throw new KeyNotFoundException("cover unit maps to undefined recipe '" + v + "'");

            var sb = root.GetProperty("seabed");
            Substrate Sub(JsonElement e) => new Substrate { Material = Mat(e.GetProperty("m").GetString()), Metres = e.GetProperty("metres").GetDouble() };
            foreach (var c in sb.GetProperty("classes").EnumerateObject()) seabed[c.Name] = Sub(c.Value);
            seabedDefault = Sub(sb.GetProperty("default")); riverBed = Sub(sb.GetProperty("riverBed"));
            beach = new Substrate { Material = BeachSand, Metres = sb.GetProperty("beachMetres").GetDouble() };
            if (sb.TryGetProperty("zones", out var zones))
                foreach (var z in zones.EnumerateArray())
                {
                    string cls = z.GetProperty("class").GetString();
                    if (!seabed.ContainsKey(cls)) throw new KeyNotFoundException("seabed zone names undefined class '" + cls + "'");
                    seabedZones.Add((Condition.Parse(z), cls));
                }

            var bed = root.GetProperty("bedrock");
            foreach (var t in bed.GetProperty("templates").EnumerateObject())
                templates[t.Name] = new Template
                {
                    Name = t.Name,
                    HandOff = t.Value.TryGetProperty("handOff", out var ho) && ho.GetBoolean(),
                    Area = t.Value.TryGetProperty("area", out var ar) ? ar.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null,
                    Units = !t.Value.TryGetProperty("units", out var units) ? Array.Empty<Unit>() : units.EnumerateArray().Select(u =>
                    {
                        var unit = new Unit { Name = u.GetProperty("name").GetString(), Layers = Layers(u.GetProperty("layers")) };
                        if (u.TryGetProperty("fadeLon", out var f)) { unit.FadeFrom = f[0].GetDouble(); unit.FadeTo = f[1].GetDouble(); }
                        return unit;
                    }).ToArray(),
                    Eras = t.Value.TryGetProperty("eras", out var eras) ? eras.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString(), StringComparer.Ordinal) : null,
                    Basement = t.Value.TryGetProperty("basement", out var tb) ? Mix(tb) : null,
                };
            (int, double)[] Mix(JsonElement arr) => arr.EnumerateArray().Select(e => (Mat(e.GetProperty("m").GetString()), e.GetProperty("share").GetDouble())).ToArray();
            foreach (var t in bed.GetProperty("templates").EnumerateObject())
                if (t.Value.TryGetProperty("like", out var like)) // same beds and eras as another template, own basement
                {
                    var src = templates.TryGetValue(like.GetString(), out var l) && l.Eras != null ? l
                        : throw new KeyNotFoundException($"template '{t.Name}' is like '{like.GetString()}', which is not a generic template");
                    templates[t.Name].Units = src.Units; templates[t.Name].Eras = src.Eras;
                }
            foreach (var t in templates.Values)
                if (t.Eras != null)
                    foreach (var u in t.Eras.Values)
                        if (u != "basement" && !t.Units.Any(x => x.Name == u))
                            throw new KeyNotFoundException($"template '{t.Name}' maps an era to unit '{u}', which it does not have");
            if (bed.TryGetProperty("provinces", out var provs))
                foreach (var p in provs.EnumerateObject())
                    provinceTemplates[p.Name] = templates.TryGetValue(p.Value.GetString(), out var pt) ? pt
                        : throw new KeyNotFoundException($"province '{p.Name}' maps to undefined template '{p.Value.GetString()}'");
            defaultTemplate = templates[bed.GetProperty("defaultTemplate").GetString()];
            areaTemplates = templates.Values.Where(t => t.Area is { Length: 4 }).ToArray();
            foreach (var s in bed.GetProperty("subcrop").EnumerateObject()) subcrop[s.Name] = s.Value.GetString();
            defaultUnit = bed.GetProperty("defaultUnit").GetString();
            defaultBasementMetres = bed.GetProperty("defaultBasementMetres").GetDouble();
            foreach (var t in templates.Values)
                foreach (var u in subcrop.Values.Append(defaultUnit).Distinct())
                    if (u != "basement" && !t.HandOff && (t == defaultTemplate || t.Area != null) && !t.Units.Any(x => x.Name == u))
                        throw new KeyNotFoundException($"subcrop maps to unit '{u}', which template '{t.Name}' does not have");

            var bs = root.GetProperty("basement");
            basementDefault = Mat(bs.GetProperty("default").GetString());
            if (bs.TryGetProperty("rocks", out var rocks)) foreach (var r in rocks.EnumerateObject()) basementRocks[r.Name] = Mat(r.Value.GetString());
            basementMix = bs.TryGetProperty("mix", out var mix) ? Mix(mix) : Array.Empty<(int, double)>();
            basementBodyDegrees = (bs.TryGetProperty("bodyKm", out var bk) ? bk.GetDouble() : 40) / 111.0;

            compactBelow = new double[Materials.Length]; compactTo = new int[Materials.Length];
            Array.Fill(compactBelow, double.PositiveInfinity); Array.Fill(compactTo, -1);
            if (root.TryGetProperty("compaction", out var comp))
                foreach (var c in comp.EnumerateObject())
                    if (c.Value.ValueKind == JsonValueKind.Object)
                    {
                        int m = Mat(c.Name);
                        compactBelow[m] = c.Value.GetProperty("belowMetres").GetDouble(); compactTo[m] = Mat(c.Value.GetProperty("m").GetString());
                    }
            frozenTo = new int[Materials.Length];
            Array.Fill(frozenTo, -1);
            if (root.TryGetProperty("frozen", out var fz))
            {
                frozenWhen = Condition.Parse(fz);
                frozenBelow = fz.GetProperty("belowMetres").GetDouble();
                foreach (var f in fz.GetProperty("materials").EnumerateObject()) frozenTo[Mat(f.Name)] = Mat(f.Value.GetString());
            }

            if (root.TryGetProperty("relief", out var rf))
            {
                ReliefRadiusMetres = rf.GetProperty("radiusMetres").GetDouble();
                reliefScale = rf.GetProperty("scaleMetres").GetDouble();
                reliefMin = rf.GetProperty("min").GetDouble(); reliefMax = rf.GetProperty("max").GetDouble();
                reliefSlopeHalf = rf.GetProperty("slopeHalf").GetDouble();
                reliefWhen = rf.TryGetProperty("when", out var rw) ? Condition.Parse(rw) : null;
                reliefSkip = new HashSet<string>(rf.GetProperty("skipRecipes").EnumerateArray().Select(x => x.GetString()), StringComparer.Ordinal);
                foreach (var r in reliefSkip) if (!recipes.ContainsKey(r)) throw new KeyNotFoundException("relief skips undefined recipe '" + r + "'");
            }

            var soilsEl = root.GetProperty("soils");
            SoilRule Soil(JsonElement e)
            {
                var r = new SoilRule { Fertility = Array.IndexOf(Fertility, e.GetProperty("fertility").GetString()), Topsoil = e.GetProperty("topsoil").GetDouble(), Peat = e.TryGetProperty("peat", out var p) && p.GetBoolean() };
                if (r.Fertility < 0) throw new FormatException("unknown fertility " + e.GetProperty("fertility").GetString());
                return r;
            }
            soilDefault = Soil(soilsEl.GetProperty("default"));
            foreach (var s in soilsEl.GetProperty("groups").EnumerateObject()) soils[s.Name] = Soil(s.Value);
            var bump = soilsEl.GetProperty("bump");
            fertilityClayBump = bump.GetProperty("clayPct").GetDouble(); fertilityPhBump = bump.GetProperty("ph").GetDouble();

            Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(rulesJson))).Substring(0, 16);
        }

        int Mat(string name)
        {
            int i = Array.IndexOf(Materials, name);
            if (i < 0) throw new KeyNotFoundException("ground rules use undefined material '" + name + "'");
            return i;
        }

        void Unknown(string field, string name)
        {
            string key = field + ":" + name;
            bool added;
            lock (unknownNames) added = unknownNames.Add(key);
            if (added) UnknownName?.Invoke(key);
        }

        public bool Covers(double lat, double lon) => lon >= ExtentWest && lon < ExtentEast && lat >= ExtentSouth && lat < ExtentNorth;

        /// <summary>All fields at a position: tile stack first, then the rules' fallbacks.</summary>
        public Sample At(double lat, double lon) => At(lat, lon, Place.Unknown);

        /// <summary>
        /// Decision 0048, soil rules 3: covers of wind-blown sand (GUM's eolian units as dunes, Arenosols as sand sheets),
        /// which keep no topsoil in a river's flood: the flood reaches the dunes beside the Tarim, the Indus and the Amu
        /// Darya, but dunes are not floodplain.
        /// </summary>
        public static bool WindSand(string recipe) => recipe is "dune" or "dune-on-till" or "sand-sheet";

        /// <summary>Whether <paramref name="source"/> contains any of <paramref name="names"/>.</summary>
        static bool NamesAny(string source, string[] names)
        {
            foreach (string n in names) if (source.Contains(n, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>All fields at a position, with what the game knows about the place (elevation, water, rivers).</summary>
        public Sample At(double lat, double lon, in Place place)
        {
            var s = new Sample { Lat = lat, Lon = lon, Clay = double.NaN, Ph = double.NaN, RegionalRock = place.RegionalRock, Arid = place.Arid, Shore = place.Surface is Surface.Beach or Surface.Water ? place.Shore : (byte)0, ShoreRiseBlocks = place.Surface == Surface.Beach ? place.ShoreRiseBlocks : 0, ShoreRules = place.ShoreRules };
            if (Data.TryCategory(FieldNames.Soil, lat, lon, Seed, out s.Soil, out s.SoilSource))
            {
                Data.TryNumber(FieldNames.Clay, lat, lon, out s.Clay, out _);
                Data.TryNumber(FieldNames.Ph, lat, lon, out s.Ph, out _);
            }
            string unit = Data.TryCategory(FieldNames.Cover, lat, lon, Seed, out string u, out s.CoverSource) ? u : null;
            s.CoverUnit = unit;
            string chosen = null;
            foreach (var r in coverRegions)
            {
                string recipe = r.Recipe(unit);
                if (recipe != null && r.When.Holds(this, lat, lon, place, s.Soil)) { chosen = recipe; break; }
            }
            if (chosen != null) s.Cover = chosen;
            else if (unit == null) s.Cover = coverDefault;
            else if (!coverUnits.TryGetValue(unit, out s.Cover)) { Unknown(FieldNames.Cover, unit); s.Cover = coverDefault; }
            // 0048 soil rules 3: a river's flood leaves soil on the desert ground it covers, but dunes and sand sheets stay sand
            if (s.Arid && place.Floodplain && !WindSand(s.Cover)) s.Arid = false;
            if (!Data.TryNumber(FieldNames.Quaternary, lat, lon, out s.QuaternaryMetres, out s.QSource)) s.QuaternaryMetres = recipes[s.Cover].TypicalMetres;
            else
                foreach (var t in thicknessRules)
                {
                    if (t.Recipes != null && !t.Recipes.Contains(s.Cover)) continue;
                    if (t.Sources != null && (s.QSource == null || !NamesAny(s.QSource, t.Sources))) continue;
                    if (!t.When.Holds(this, lat, lon, place, s.Soil)) continue;
                    s.QuaternaryMetres = Math.Clamp(s.QuaternaryMetres * t.Scale, t.MinMetres, Math.Max(t.MinMetres, t.MaxMetres));
                    s.QRule = t.Name;
                    break;
                }
            s.Relief = place.HasRelief ? place.Relief : double.NaN; s.Slope = place.HasRelief ? place.Slope : double.NaN; s.ReliefFactor = 1;
            if (place.HasRelief && ReliefRadiusMetres > 0 && !reliefSkip.Contains(s.Cover) && (reliefWhen == null || reliefWhen.Holds(this, lat, lon, place, s.Soil)))
            {
                // crests and steep slopes shed their cover, hollows and valley floors collect it
                double sl = place.Slope / reliefSlopeHalf;
                // divided by the neighbourhood's mean weight: sediment moves from crests to hollows, the total stays the data's
                s.ReliefFactor = ReliefWeight(place.Relief) / (1 + sl * sl) / (place.ReliefNorm > 0 ? place.ReliefNorm : 1);
                s.QuaternaryMetres *= s.ReliefFactor;
            }
            s.QuaternaryMetres = Math.Min(s.QuaternaryMetres, recipes[s.Cover].MaxMetres);
            if (!Data.TryNumber(FieldNames.Basement, lat, lon, out s.BasementMetres, out s.BasementSource)) s.BasementMetres = defaultBasementMetres;
            if (Data.TryCategory(FieldNames.Subcrop, lat, lon, Seed, out s.Subcrop, out s.SubcropSource))
                Data.TryCategory(FieldNames.SubcropLith, lat, lon, Seed, out s.SubcropLith, out _);
            Data.TryCategory(FieldNames.Seabed, lat, lon, Seed, out s.Seabed, out s.SeabedSource);
            if (s.Seabed == null && place.Surface == Surface.Water)
                foreach (var (when, cls) in seabedZones)
                    if (when.Holds(this, lat, lon, place, s.Soil)) { s.Seabed = cls; s.SeabedSource = "rule"; break; }
            Data.TryCategory(FieldNames.BasementRock, lat, lon, Seed, out s.BasementRock, out _);
            Data.TryCategory(FieldNames.Province, lat, lon, Seed, out s.Province, out _);
            Data.TryCategory(FieldNames.Era, lat, lon, Seed, out s.Era, out _);
            s.Frozen = frozenWhen != null && frozenWhen.Holds(this, lat, lon, place, s.Soil);
            return s;
        }

        // ------------------------------------------------------------------ columns

        /// <summary>What the column's top is: dry land, beach, a sea or lake floor, or a river bed.</summary>
        public enum Surface { Land, Beach, Water, RiverBed }

        /// <summary>
        /// Which part of the column a layer belongs to (decision 0060): the top (soil, top peat, beach sand or floor substrate),
        /// the Quaternary cover, cover below its compaction depth (now rock), a bedrock bed, or the basement.
        /// </summary>
        public enum Part : byte { Top, Cover, DeepCover, Bed, Basement }
        public static readonly string[] PartNames = { "top", "cover", "deep-cover", "bed", "basement" };

        /// <summary>
        /// One column as block intervals from the top: block depth d (0 = the top block) belongs to the first layer whose
        /// <c>End</c> exceeds d. The last layer is the basement and runs to the bottom of the world.
        /// </summary>
        public sealed class Column
        {
            public const int Max = 64;
            public int Count;
            public readonly int[] End = new int[Max], Material = new int[Max];
            /// <summary>Real depth (m) at the bottom of each layer. Not Curve.Metres(End): kept thin layers push the blocks below down.</summary>
            public readonly double[] EndMetres = new double[Max];
            /// <summary>Index into <see cref="Fertility"/> for the topsoil, or -1 when the column has none.</summary>
            public int TopsoilFertility = -1;
            /// <summary>True when no template gives bedrock here: the column ends at the bottom of its cover
            /// (<see cref="Depth"/>) and the rock below is left as it was.</summary>
            public bool HandOff;
            /// <summary>
            /// The layers as built, by part (decision 0060). Blocks are the same as <see cref="End"/>/<see cref="Material"/>, but a
            /// layer of the same material in another part, recipe or unit is a segment of its own. Segment i covers block depths
            /// from SegEnd[i-1] (0 for the first) to SegEnd[i]. SegSource: the top's kind (soil, peat, beach, riverbed, seabed),
            /// the cover recipe, the bed's template unit, or the basement's template.
            /// </summary>
            public int Segments;
            public readonly int[] SegEnd = new int[Max], SegMaterial = new int[Max];
            public readonly Part[] SegPart = new Part[Max];
            public readonly string[] SegSource = new string[Max];
            /// <summary>The surface, cover recipe and bedrock template the column was built with.</summary>
            public Surface Surface;
            public string Recipe, Template;
            internal Part part;
            internal string source;
            /// <summary>Blocks the column covers from the top (int.MaxValue when it runs to the bottom of the world).</summary>
            public int Depth => Count > 0 ? End[Count - 1] : 0;
            public int MaterialAt(int depth)
            {
                for (int i = 0; i < Count; i++) if (depth < End[i]) return Material[i];
                return Count > 0 ? Material[Count - 1] : -1;
            }
            /// <summary>The segment holding block depth <paramref name="depth"/>; the last one below the column's end.</summary>
            public int SegmentAt(int depth)
            {
                for (int i = 0; i < Segments; i++) if (depth < SegEnd[i]) return i;
                return Segments - 1;
            }
            /// <summary>
            /// The next layer, down to block <paramref name="end"/> and <paramref name="endMetres"/>. One the curve gives no block
            /// gets one when <paramref name="keep"/> is set (the blocks below shift down until a thick layer takes up the shift),
            /// else it merges into the layer above.
            /// </summary>
            internal void Add(int material, int end, double endMetres, bool keep)
            {
                AddBlocks(material, end, endMetres, keep);
                // the segment: whatever blocks this layer added, in the part being built
                int bottom = Count > 0 ? End[Count - 1] : 0, prev = Segments > 0 ? SegEnd[Segments - 1] : 0;
                if (bottom <= prev) return;
                int m = Material[Count - 1];
                if (Segments > 0 && (Segments == Max || SegPart[Segments - 1] == part && SegMaterial[Segments - 1] == m && SegSource[Segments - 1] == source))
                { SegEnd[Segments - 1] = bottom; return; }
                SegEnd[Segments] = bottom; SegMaterial[Segments] = m; SegPart[Segments] = part; SegSource[Segments] = source; Segments++;
            }

            void AddBlocks(int material, int end, double endMetres, bool keep)
            {
                int last = Count > 0 ? End[Count - 1] : 0;
                if (Count > 0 && Material[Count - 1] == material)
                {
                    End[Count - 1] = Math.Max(last, end); EndMetres[Count - 1] = Math.Max(EndMetres[Count - 1], endMetres);
                    return;
                }
                if (end <= last)
                {
                    if (!keep) { if (Count > 0) EndMetres[Count - 1] = Math.Max(EndMetres[Count - 1], endMetres); return; }
                    end = last + 1;
                }
                if (Count == Max) { End[Count - 1] = end; EndMetres[Count - 1] = endMetres; return; }
                Material[Count] = material; End[Count] = end; EndMetres[Count] = endMetres; Count++;
            }
        }

        /// <summary>Natural topsoil fertility class (index into <see cref="Fertility"/>), humus depth in metres, and whether it is peat.</summary>
        public (int Fertility, double Metres, bool Peat) SoilOf(in Sample s)
        {
            var r = soilDefault;
            if (s.Soil != null && !soils.TryGetValue(s.Soil, out r)) { Unknown(FieldNames.Soil, s.Soil); r = soilDefault; }
            int f = r.Fertility;
            if (s.Clay >= fertilityClayBump && s.Ph >= fertilityPhBump && f < 2) f++; // base-rich clay loams (Zemgale): one class up
            return (f, r.Topsoil, r.Peat);
        }

        int B(double metres) => (int)Math.Round(Curve.Blocks(metres), MidpointRounding.AwayFromZero);

        void Put(Column col, int material, double from, double to) => col.Add(material, B(to), to, to - from >= MinLayerMetres);

        /// <summary>A layer from <paramref name="from"/> to <paramref name="to"/> metres; the part below its material's compaction depth is rock.</summary>
        void AddLayer(Column col, int material, double from, double to)
        {
            int rock = compactTo[material];
            double below = compactBelow[material];
            if (rock >= 0 && to > below)
            {
                if (from < below) Put(col, material, from, below);
                var part = col.part;
                if (part == Part.Cover) col.part = Part.DeepCover; // cover turned to rock; compacted beds stay beds
                Put(col, rock, Math.Max(from, below), to);
                col.part = part;
                return;
            }
            Put(col, material, from, to);
        }

        double Vary(Layer l, in Sample s, int salt)
            => l.Vary <= 0 ? 1 : 1 + l.Vary * (GeologyTransitions.Noise(s.Lon / .045, s.Lat / .03, Seed, 5100 + salt) * 2 - 1);

        /// <summary>Decision 0070: a shore strip steeper than this (rise over run) is bare rock unless OSM says it is a beach.</summary>
        public const double ShoreCliffSlope = 1.0;
        /// <summary>Metres of the shore's loose material: sand (the rules' beachMetres), gravel and stones, mud.</summary>
        const double ShoreGravelMetres = 1.5, ShoreMudMetres = 2;

        /// <summary>
        /// Decision 0070: a shore strip's material (<paramref name="bare"/>: none, the bedrock bare). OSM's surface where it
        /// gives one (sand, gravel, the two mixed in patches, rock, mud); else, and for OSM's beaches without one, the coast's
        /// ground: a cliff (ShoreCliffSlope) is rock; the sea floor's mapped class beside it (EMODnet) where there is one;
        /// otherwise the cover: wind-blown, coastal, marine, deltaic and river sand is sand (carbonate sand on carbonate
        /// shelves and karst, black tephra sand on volcanic ground), glacial till and slope debris are gravel and stones in
        /// patches with sand, peat, lake and marsh clays are mud, and thin cover or weathered rock is bare rock (gravel on
        /// gentle ground). An OSM beach is never bare. Shore rules 3 (EA-0056): thin till over glacially scoured rock is bare
        /// rock where OSM maps no beach.
        /// </summary>
        (int Material, double Metres, string Name) ShoreSubstrate(in Sample s, out bool bare, bool shallows = false)
        {
            bare = false;
            var sand = (beach.Material, beach.Metres, "sand");
            var gravel = (shoreGravel, ShoreGravelMetres, "gravel");
            var mud = (shoreMud, ShoreMudMetres, "mud");
            // sand or gravel along whole stretches of a coast (about a kilometre), not in blotches (shore rules 1 drew 40 m
            // patches, which a wide beach showed as camouflage)
            bool patchGravel = GeologyTransitions.Noise(s.Lon / .016, s.Lat / .009, Seed, 7070) < .5;
            var mixed = patchGravel ? gravel : sand;
            switch (s.Shore)
            {
                case ShoreMaterial.Sand: return sand;
                case ShoreMaterial.Gravel: return gravel;
                case ShoreMaterial.Mixed: return mixed;
                case ShoreMaterial.Mud: return mud;
                case ShoreMaterial.Rock: bare = true; return (0, 0, "rock");
            }
            bool osmBeach = s.Shore == ShoreMaterial.Beach;
            bool steep = !double.IsNaN(s.Slope) && s.Slope > ShoreCliffSlope;
            if (steep && !osmBeach) { bare = true; return (0, 0, "rock (cliff)"); }
            // the mapped sea floor speaks for the shallows only: a basin's mud is not the shore's (the Gulf of Finland's
            // EMODnet mud laid silt over its low islands)
            if (shallows && s.Seabed != null && s.SeabedSource != null)
            {
                switch (s.Seabed)
                {
                    case "2. Sand": case "13. muddy Sand": return sand;
                    case "3. Coarse-grained sediment": return gravel;
                    case "4. Mixed sediment": return mixed;
                    case "11. Mud": case "12. sandy Mud": return mud;
                    case "5. Rock & boulders":
                        if (osmBeach) return gravel;
                        bare = true; return (0, 0, "rock (sea floor)");
                }
            }
            // shore rules 3 (EA-0056): a coast of thin till over glacially scoured rock is bare rock to the water where OSM
            // maps no beach: the waves have washed the till off (Bergen and the fjords, Bohuslän and the skerries, Lofoten,
            // Connemara, Maine). GLO-90's 90 m cells read these shores at a slope of 0.05 or so, so the cliff test above never
            // fires there; the pocket beaches between the rocks are OSM's, and stay gravel, stones or sand. Knob-till (the
            // ice-scoured crests' patchy till) is the same ground.
            if (s.ShoreRules >= 3 && s.Cover is "thin-till" or "knob-till" && !osmBeach) { bare = true; return (0, 0, "rock (scoured till)"); }
            switch (s.Cover)
            {
                case "dune": case "dune-on-till": case "sand-sheet": case "coastal": case "marine": case "marine-on-till":
                case "delta": case "alluvium": case "alluvium-on-till": case "outwash": case "sediment": case "sand-on-saprolite":
                    return sand;
                case "carbonate-shelf": case "residual-karst":
                    return shoreCarbonate >= 0 ? (shoreCarbonate, beach.Metres, "carbonate sand") : sand;
                case "tephra":
                    return shoreTephra >= 0 ? (shoreTephra, beach.Metres, "tephra sand") : sand;
                case "till": case "thin-till": case "knob-till": case "loess-on-till": case "colluvium": case "fan":
                    // a glaciated shield's shores are stony: gravel and boulders on the rock, not sand (the Finnish and
                    // Swedish archipelagos, the Canadian Shield)
                    return s.Province == "shield" ? gravel : mixed;
                case "peat": case "peat-on-till": case "lakeclay": case "lake": case "playa": case "pelagic-clay":
                    return osmBeach ? sand : mud;
                case "thin": case "residual": case "residual-mafic": case "saprolite": case "saprolite-mafic": case "laterite":
                case "calcrete": case "gypcrete":
                    if (osmBeach || (!double.IsNaN(s.Slope) && s.Slope < 0.15)) return gravel;
                    bare = true; return (0, 0, "rock");
            }
            return sand;
        }

        /// <summary>Builds the column at a sample.</summary>
        public void Build(in Sample s, Surface surface, Column col)
        {
            col.Count = 0; col.TopsoilFertility = -1; col.HandOff = false;
            col.Segments = 0; col.Surface = surface; col.Recipe = s.Cover; col.Template = null;
            double q = Math.Max(0, s.QuaternaryMetres), basement = Math.Max(q + 1, s.BasementMetres);
            double top = 0; // metres already used at the top
            var recipe = recipes[s.Cover];
            bool peatCover = recipe.Layers.Length > 0 && recipe.Layers[0].Material == Peat;

            // 1. the top: soil on land, beach sand, or the floor substrate under water
            col.part = Part.Top;
            col.source = surface switch { Surface.Beach => "beach", Surface.RiverBed => "riverbed", Surface.Water => "seabed", _ => "soil" };
            if (surface == Surface.Land)
            {
                var soil = SoilOf(s);
                if (soil.Peat && !peatCover) { col.source = "peat"; col.Add(Peat, Math.Max(1, B(1.5)), 1.5, true); top = 1.5; }
                // a real desert (0048 soil rules 2): no humus horizon; the cover below is the ground
                else if (!peatCover && !s.Arid)
                {
                    // humus horizon: at least one block; one more for each whole metre beyond the first (chernozem 1.2 m: 2 blocks),
                    // at any depth scale: the soil is what players farm
                    col.TopsoilFertility = soil.Fertility;
                    col.Add(Topsoil, Math.Max(1, (int)Math.Ceiling(soil.Metres - .15)), soil.Metres, true);
                    top = soil.Metres;
                }
            }
            else if (surface == Surface.Beach && s.Shore != 0)
            {
                // decision 0070: the coast's own material; bare rock takes the loose cover away down to the bedrock
                var sub = ShoreSubstrate(s, out bool bare);
                col.source = "shore " + sub.Name;
                if (bare) q = 0;
                // (a foredune's sand down to the beach beside it)
                else if (sub.Metres > 0) { col.Add(sub.Material, Math.Max(Math.Max(1, B(sub.Metres)), s.ShoreRiseBlocks + 1), sub.Metres, true); top = sub.Metres; }
            }
            else if (surface == Surface.Water && s.Shore != 0)
            {
                // shore rules 2: the shallows in front of a shore are of its material (sand in front of a sandy beach, stones
                // or bare rock off a shield's shore), not the open floor's mud
                var floor = ShoreSubstrate(s, out bool bare, shallows: true);
                col.source = "shallows " + floor.Name;
                if (bare) q = 0;
                else if (floor.Metres > 0) { col.Add(floor.Material, Math.Max(1, B(floor.Metres)), floor.Metres, true); top = floor.Metres; }
            }
            else
            {
                Substrate sub = surface == Surface.Beach ? beach : surface == Surface.RiverBed ? riverBed : seabedDefault;
                if (surface == Surface.Water && s.Seabed != null)
                {
                    if (seabed.TryGetValue(s.Seabed, out var mapped)) sub = mapped;
                    else Unknown(FieldNames.Seabed, s.Seabed);
                }
                if (sub.Metres > 0) { col.Add(sub.Material, Math.Max(1, B(sub.Metres)), sub.Metres, true); top = sub.Metres; }
            }

            // 2. Quaternary cover: shares of q varied a little by position; capped layers leave their excess to the last layer
            col.part = Part.Cover; col.source = s.Cover;
            var layers = recipe.Layers;
            double used = 0;
            for (int i = 0; i < layers.Length && used < q; i++)
            {
                double t = i == layers.Length - 1 ? q - used : Math.Min(layers[i].Share * q * Vary(layers[i], s, i), layers[i].Max);
                t = Math.Min(t, q - used);
                double from = used;
                used += t;
                if (used <= top) continue;
                from = Math.Max(from, top);
                int m = layers[i].Material, frozen = s.Frozen ? frozenTo[m] : -1;
                if (frozen >= 0 && used > frozenBelow) // permafrost: the loose cover below the active layer is frozen
                {
                    // frozen down to where the material turns to rock anyway (compaction)
                    double f0 = Math.Max(from, frozenBelow), f1 = Math.Min(used, compactBelow[m]);
                    if (from < frozenBelow) AddLayer(col, m, from, frozenBelow);
                    if (f1 > f0) AddLayer(col, frozen, f0, f1);
                    if (used > Math.Max(f0, f1)) AddLayer(col, m, Math.Max(f0, f1), used);
                }
                else AddLayer(col, m, from, used);
            }

            // 3. bedrock from the subcrop unit down to the basement, nominal thicknesses scaled to fit. Where the map names a
            //    lithology, the column starts at the first layer of that kind in its unit (younger layers taken as eroded).
            double depth = Math.Max(q, top);
            var template = TemplateAt(s);
            col.Template = template.Name;
            col.part = Part.Bed;
            if (template.HandOff) { col.HandOff = true; return; }
            string unitName = defaultUnit;
            if (template.Eras != null)
            {
                // generic template: the mapped bedrock era picks the unit the column starts at
                if (s.Era == null || !template.Eras.TryGetValue(s.Era, out unitName))
                {
                    if (s.Era != null && !template.Eras.ContainsKey("*")) Unknown(FieldNames.Era, s.Era);
                    unitName = template.Eras.TryGetValue("*", out var any) ? any : template.Units.Length > 0 ? template.Units[0].Name : "basement";
                }
            }
            else if (s.Subcrop != null && !subcrop.TryGetValue(s.Subcrop, out unitName)) { Unknown(FieldNames.Subcrop, s.Subcrop); unitName = defaultUnit; }
            if (unitName != "basement")
            {
                int named = -1;
                for (int i = 0; i < template.Units.Length; i++) if (template.Units[i].Name == unitName) { named = i; break; }
                if (named < 0) { Unknown("unit", template.Name + "/" + unitName); named = 0; }
                int first = named;
                while (first < template.Units.Length && Fade(template.Units[first], s.Lon) <= 0) first++; // faded out here: the next one
                int k0 = 0;
                if (first == named && first < template.Units.Length && s.SubcropLith != null)
                {
                    var unitLayers = template.Units[first].Layers;
                    int hit = -1;
                    for (int i = 0; i < unitLayers.Length; i++) if (unitLayers[i].Lith == s.SubcropLith) { hit = i; break; }
                    if (hit > 0) k0 = hit;
                }
                double nominal = 0;
                for (int i = first; i < template.Units.Length; i++)
                {
                    double f = Fade(template.Units[i], s.Lon);
                    for (int k = i == first ? k0 : 0; k < template.Units[i].Layers.Length; k++) nominal += template.Units[i].Layers[k].Metres * f;
                }
                double scale = nominal > 0 ? Math.Max(0, basement - depth) / nominal : 0;
                // generic templates stretch to a deep basin but are cut at the basement where it is shallower: a thin pile keeps
                // its youngest beds instead of a sliver of every era
                bool cut = template.Eras != null && scale < 1;
                if (cut) scale = 1;
                for (int i = first; i < template.Units.Length && !(cut && depth >= basement); i++)
                {
                    var u = template.Units[i];
                    double f = Fade(u, s.Lon);
                    if (f <= 0) continue;
                    col.source = u.Name;
                    for (int k = i == first ? k0 : 0; k < u.Layers.Length; k++)
                    {
                        double from = depth;
                        depth += u.Layers[k].Metres * f * scale;
                        if (cut && depth >= basement) { AddLayer(col, u.Layers[k].Material, from, basement); depth = basement; break; }
                        AddLayer(col, u.Layers[k].Material, from, depth);
                    }
                }
            }

            // 4. basement to the bottom of the world: mapped rock, else a deterministic mix of large bodies, else the default
            col.part = Part.Basement; col.source = template.Name;
            col.Add(BasementAt(s, template), int.MaxValue, double.PositiveInfinity, true);
        }

        /// <summary>
        /// The first template whose area holds the position, else the default. Where the default hands the rock to Earth's
        /// regional geology but that maps none here (loose sediment or unmapped ground, which would keep vanilla strata), the
        /// province's generic template ("*": any other province).
        /// </summary>
        Template TemplateAt(in Sample s)
        {
            foreach (var t in areaTemplates)
                if (s.Lon >= t.Area[0] && s.Lat >= t.Area[1] && s.Lon < t.Area[2] && s.Lat < t.Area[3]) return t;
            if (defaultTemplate.HandOff && !s.RegionalRock
                && (s.Province != null && provinceTemplates.TryGetValue(s.Province, out var pt) || provinceTemplates.TryGetValue("*", out pt)))
                return pt;
            return defaultTemplate;
        }

        int BasementAt(in Sample s, Template template)
        {
            if (s.BasementRock != null)
            {
                if (basementRocks.TryGetValue(s.BasementRock, out int m)) return m;
                Unknown(FieldNames.BasementRock, s.BasementRock);
            }
            var mix = template.Basement ?? basementMix;
            if (mix.Length == 0) return basementDefault;
            double v = GeologyTransitions.Noise(s.Lon * Math.Cos(s.Lat * Math.PI / 180) / basementBodyDegrees, s.Lat / basementBodyDegrees, Seed, 5311);
            double acc = 0, total = 0;
            foreach (var (_, share) in mix) total += share;   // in order, as Sum adds them
            foreach (var (material, share) in mix) { acc += share / total; if (v < acc) return material; }
            return mix[^1].Material;
        }

        static double Fade(Unit u, double lon)
            => double.IsNaN(u.FadeFrom) ? 1 : Math.Clamp((u.FadeTo - lon) / (u.FadeTo - u.FadeFrom), 0, 1);

        public string Describe(Column col)
        {
            var sb = new StringBuilder();
            int start = 0;
            double startMetres = 0;
            if (col.HandOff && col.Count == 0) return "no ground layers; regional rock from the top";
            for (int i = 0; i < col.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                string name = Materials[col.Material[i]];
                if (col.Material[i] == Topsoil && col.TopsoilFertility >= 0) name += "(" + Fertility[col.TopsoilFertility] + ")";
                if (col.End[i] == int.MaxValue) sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} from {1:0} m (block {2})", name, startMetres, start));
                else sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} {1} blk ({2:0.#}-{3:0.#} m)", name, col.End[i] - start, startMetres, col.EndMetres[i]));
                start = col.End[i]; startMetres = col.EndMetres[i];
            }
            if (col.HandOff) sb.Append(string.Format(CultureInfo.InvariantCulture, " / regional rock from {0:0.#} m (block {1})", startMetres, start));
            return sb.ToString();
        }
    }
}
