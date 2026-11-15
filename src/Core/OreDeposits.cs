using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Decision 0065 (ore rules v4): the deposit catalogue (assets/earth/config/ore-deposits-v1/*.json, schema in
    /// tools/ore-data/README.md) read, checked and planned at a world's scale. Pure: no game types. The game side
    /// (EarthOreDeposits) supplies the blocks and the layer gate through <see cref="IOreColumn"/>; the offline tests use the
    /// same code with a model column.
    /// </summary>
    public sealed class OreDepositCatalog
    {
        public const int SchemaVersion = 1;
        public const string Folder = "config/ore-deposits-v1/";
        public enum Shape : byte { Stock, Lens, Veins, Blanket, Dome }
        static readonly string[] ShapeNames = { "stock", "lens", "veins", "blanket", "dome" };

        /// <summary>One ore of a deposit or bed: its deposit code, share of the tonnage and zone (0 throughout, 1 top,
        /// -1 bottom). <see cref="Slot"/> is the game's index of the code's blocks (-1: no generator in this game).</summary>
        public sealed class Ore
        {
            public string Code; public double Share; public sbyte Zone;
            public int Slot = -1;
            /// <summary>Optional (a file's "oreMetal" with the deposit's "grades"): the grade band key whose grade sets this ore's
            /// tier, and that grade; null for the body's own grade and band. A VMS body's sphalerite takes its tier from Zn.</summary>
            public string Metal; public double Grade = double.NaN;
            /// <summary>The band of <see cref="Metal"/>, resolved when a plan is built (bands may come from any file).</summary>
            public GradeBand Band;
            public override string ToString() => Code;
        }
        public sealed class GradeBand { public string Unit; public double Medium, Rich, Bountiful; }
        public sealed class Deposit
        {
            public string Id, Name, Group, Layer, Unit, Age, Src, Q, File;
            public double Lat, Lon, Mt, Grade = double.NaN, Density, TopM, BaseM, LenKm, WidKm, StrikeDeg, DipDeg;
            public Shape Shape;
            public Ore[] Ores;
            /// <summary>The 0060 layer classes the body may fill (a bit per OreLayers.Class), from its "layers" or the
            /// blanket default; 0: the 0060 table of its ore codes.</summary>
            public int Classes;
            public string[] Estimated = Array.Empty<string>();
            public override string ToString() => Id + " " + Name;
        }
        public sealed class Bed
        {
            public Ore Ore; public double TopM, ThickM, Cover, Grade = double.NaN;
            public string Unit;
        }
        public sealed class Area
        {
            public string Id, Name, Group, Layer, Age, Src, File;
            /// <summary>Rings as lon, lat pairs (closed or not), outer and holes alike (even-odd).</summary>
            public List<double[]> Rings = new();
            public Bed[] Beds;
            public double Mt = double.NaN, West, East, South, North;
            public override string ToString() => Id + " " + Name;
        }

        public readonly List<Deposit> Deposits = new();
        public readonly List<Area> Areas = new();
        public readonly Dictionary<string, GradeBand> Bands = new(StringComparer.Ordinal);
        public readonly List<string> Problems = new();
        public readonly List<string> Files = new();
        Dictionary<string, string[]> fileOreMetal;
        readonly HashSet<string> ids = new(StringComparer.Ordinal);
        readonly List<(string Name, string Text)> texts = new();
        /// <summary>Entries skipped by the checks (logged in <see cref="Problems"/>).</summary>
        public int Skipped;

        /// <summary>Hash of every file read (names and text, by name): saved in the world's ore fingerprint.</summary>
        public string ContentHash
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var (name, text) in texts.OrderBy(t => t.Name, StringComparer.Ordinal)) sb.Append(name).Append('\n').Append(text).Append('\n');
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).Substring(0, 16);
            }
        }

        void Problem(string file, string what) { if (Problems.Count < 500) Problems.Add(file + ": " + what); }

        /// <summary>Reads one catalogue file. Never throws: a bad file or entry is logged in <see cref="Problems"/> and skipped.</summary>
        public void AddFile(string name, string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { Problem(name, "not a JSON object; file skipped"); return; }
                if (!root.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != SchemaVersion) { Problem(name, "schema version is not " + SchemaVersion + "; file skipped"); return; }
                string group = Str(root, "group") ?? System.IO.Path.GetFileNameWithoutExtension(name);
                texts.Add((name, json));
                Files.Add(name);
                if (root.TryGetProperty("gradeBands", out var bands) && bands.ValueKind == JsonValueKind.Object)
                    foreach (var p in bands.EnumerateObject())
                    {
                        var b = new GradeBand { Unit = Str(p.Value, "unit") ?? "%", Medium = Num(p.Value, "medium"), Rich = Num(p.Value, "rich"), Bountiful = Num(p.Value, "bountiful") };
                        if (!(b.Medium > 0 && b.Rich > b.Medium && b.Bountiful > b.Rich)) { Problem(name, "grade band " + p.Name + " is not increasing (medium < rich < bountiful); ignored"); continue; }
                        // files that share a band (a co-product's oreMetal) repeat it; only a differing copy is a problem
                        if (!Bands.TryAdd(p.Name, b) && Bands[p.Name] is var first
                            && (first.Unit != b.Unit || first.Medium != b.Medium || first.Rich != b.Rich || first.Bountiful != b.Bountiful))
                            Problem(name, "grade band " + p.Name + " differs from an earlier file's; the first is kept");
                    }
                // optional: which metal's grade sets each ore code's tier ("Sb|Ag": the first the body has a grade for)
                fileOreMetal = null;
                if (root.TryGetProperty("oreMetal", out var om) && om.ValueKind == JsonValueKind.Object)
                {
                    fileOreMetal = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    foreach (var p in om.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String)
                            fileOreMetal[p.Name] = p.Value.GetString().Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                if (root.TryGetProperty("deposits", out var deps) && deps.ValueKind == JsonValueKind.Array)
                    foreach (var e in deps.EnumerateArray())
                    {
                        string why = null;
                        Deposit d = null;
                        try { d = ReadDeposit(e, group, name, out why); }
                        catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException) { why = ex.Message; }
                        if (d == null) { Skipped++; Problem(name, "deposit " + (Str(e, "id") ?? "?") + " skipped: " + why); continue; }
                        if (!ids.Add(d.Id)) { Skipped++; Problem(name, "deposit id " + d.Id + " is used twice; the second is skipped"); continue; }
                        Deposits.Add(d);
                    }
                if (root.TryGetProperty("areas", out var areas) && areas.ValueKind == JsonValueKind.Array)
                    foreach (var e in areas.EnumerateArray())
                    {
                        string why = null;
                        Area a = null;
                        try { a = ReadArea(e, group, name, out why); }
                        catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException) { why = ex.Message; }
                        if (a == null) { Skipped++; Problem(name, "area " + (Str(e, "id") ?? "?") + " skipped: " + why); continue; }
                        if (!ids.Add(a.Id)) { Skipped++; Problem(name, "area id " + a.Id + " is used twice; the second is skipped"); continue; }
                        Areas.Add(a);
                    }
            }
            catch (JsonException e) { Problem(name, "invalid JSON (" + e.Message + "); file skipped"); }
        }

        static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static double Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
        static bool Ok(double v) => double.IsFinite(v);

        Ore[] ReadOres(JsonElement e, out string why)
        {
            why = null;
            if (!e.TryGetProperty("ores", out var arr) || arr.ValueKind != JsonValueKind.Array) { why = "no ores"; return null; }
            var list = new List<Ore>();
            foreach (var o in arr.EnumerateArray())
            {
                string code = Str(o, "code");
                double share = Num(o, "share");
                if (string.IsNullOrWhiteSpace(code)) { why = "an ore without a code"; return null; }
                if (!Ok(share)) share = 1;
                if (!(share > 0)) continue;
                string zone = Str(o, "zone");
                list.Add(new Ore { Code = code.Trim(), Share = share, Zone = (sbyte)(zone == "top" ? 1 : zone == "bottom" ? -1 : 0) });
            }
            if (list.Count == 0) { why = "no ore with a positive share"; return null; }
            double sum = list.Sum(o => o.Share);
            foreach (var o in list) o.Share /= sum;
            return list.ToArray();
        }

        Deposit ReadDeposit(JsonElement e, string group, string file, out string why)
        {
            why = null;
            var d = new Deposit { Id = Str(e, "id"), Name = Str(e, "name") ?? Str(e, "id"), Group = group, Layer = Str(e, "layer"), File = file,
                Lat = Num(e, "lat"), Lon = Num(e, "lon"), Mt = Num(e, "mt"), Grade = Num(e, "grade"), Unit = Str(e, "unit"), Density = Num(e, "density"),
                TopM = Num(e, "topM"), BaseM = Num(e, "baseM"), LenKm = Num(e, "lenKm"), WidKm = Num(e, "widKm"), StrikeDeg = Num(e, "strikeDeg"), DipDeg = Num(e, "dipDeg"),
                Age = Str(e, "age"), Src = Str(e, "src"), Q = Str(e, "q") };
            if (string.IsNullOrWhiteSpace(d.Id)) { why = "no id"; return null; }
            if (!(Ok(d.Lat) && Math.Abs(d.Lat) <= 90 && Ok(d.Lon) && Math.Abs(d.Lon) <= 180)) { why = "lat/lon missing or out of range"; return null; }
            if (!(Ok(d.Mt) && d.Mt > 0)) { why = "mt missing or not positive"; return null; }
            int shape = Array.IndexOf(ShapeNames, Str(e, "shape"));
            if (shape < 0) { why = "unknown shape '" + Str(e, "shape") + "'"; return null; }
            d.Shape = (Shape)shape;
            d.Ores = ReadOres(e, out why);
            if (d.Ores == null) return null;
            if (fileOreMetal != null && e.TryGetProperty("grades", out var grades) && grades.ValueKind == JsonValueKind.Object)
                foreach (var o in d.Ores)
                {
                    if (!fileOreMetal.TryGetValue(o.Code, out var metals)) continue;
                    foreach (string m in metals)
                    {
                        double g = Num(grades, m);
                        if (Ok(g) && g >= 0) { o.Metal = m; o.Grade = g; break; }
                    }
                }
            if (e.TryGetProperty("est", out var est) && est.ValueKind == JsonValueKind.Array) d.Estimated = est.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToArray();
            if (e.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
                foreach (var l in layers.EnumerateArray())
                {
                    int c = l.ValueKind == JsonValueKind.String ? Array.IndexOf(OreLayers.ClassNames, l.GetString()) : -1;
                    if (c > 0) d.Classes |= 1 << c;
                    else Problem(file, d.Id + ": unknown layer " + l.GetRawText() + " ignored");
                }
            if (d.Classes == 0 && d.Shape == Shape.Blanket) d.Classes = BlanketLayers;
            var defaulted = new List<string>();
            if (!(Ok(d.Density) && d.Density >= .8 && d.Density <= 8)) { if (Ok(d.Density)) Problem(file, d.Id + ": density " + d.Density + " out of 0.8..8; default used"); d.Density = DefaultDensity(d.Shape, d.Ores[0].Code); }
            if (!(Ok(d.TopM) && d.TopM >= 0 && d.TopM < 20000)) { d.TopM = d.Shape is Shape.Blanket or Shape.Stock ? 0 : 50; defaulted.Add("topM"); }
            if (!(Ok(d.BaseM) && d.BaseM > d.TopM && d.BaseM < 20000))
            {
                d.BaseM = d.TopM + d.Shape switch { Shape.Blanket => 10, Shape.Veins => 500, Shape.Stock => 800, Shape.Dome => 3000, _ => 300 };
                defaulted.Add("baseM");
            }
            double volume = d.Mt * 1e6 / d.Density;
            if (!(Ok(d.LenKm) && d.LenKm > 0) || !(Ok(d.WidKm) && d.WidKm > 0))
            {
                // half the footprint ellipse filled over the depth range, footprint twice as long as wide (one given: the other from it)
                double area = volume / (.5 * (d.BaseM - d.TopM));
                if (Ok(d.LenKm) && d.LenKm > 0) d.WidKm = Math.Min(d.LenKm, area / (Math.PI / 4 * d.LenKm * 1e3) / 1e3);
                else if (Ok(d.WidKm) && d.WidKm > 0) d.LenKm = Math.Max(d.WidKm, area / (Math.PI / 4 * d.WidKm * 1e3) / 1e3);
                else { d.WidKm = Math.Sqrt(area / (Math.PI / 4 * 2)) / 1e3; d.LenKm = 2 * d.WidKm; }
                defaulted.Add("lenKm/widKm");
            }
            if (d.LenKm < d.WidKm) { (d.LenKm, d.WidKm) = (d.WidKm, d.LenKm); d.StrikeDeg = (Ok(d.StrikeDeg) ? d.StrikeDeg : 0) + 90; }
            if (!Ok(d.StrikeDeg)) d.StrikeDeg = 0;
            if (!(Ok(d.DipDeg) && d.DipDeg >= 0 && d.DipDeg <= 90)) d.DipDeg = d.Shape is Shape.Lens ? 45 : d.Shape is Shape.Blanket ? 0 : d.Shape is Shape.Veins ? 80 : 90;
            if (!Ok(d.Grade) || d.Grade < 0) d.Grade = double.NaN;
            if (string.IsNullOrWhiteSpace(d.Layer)) d.Layer = "?";
            if (defaulted.Count > 0) Problem(file, d.Id + ": " + string.Join(", ", defaulted) + " missing; runtime default used");
            return d;
        }

        Area ReadArea(JsonElement e, string group, string file, out string why)
        {
            why = null;
            var a = new Area { Id = Str(e, "id"), Name = Str(e, "name") ?? Str(e, "id"), Group = group, Layer = Str(e, "layer") ?? "?", Age = Str(e, "age"), Src = Str(e, "src"), Mt = Num(e, "mt"), File = file };
            if (string.IsNullOrWhiteSpace(a.Id)) { why = "no id"; return null; }
            if (!e.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) { why = "no geometry"; return null; }
            string type = Str(g, "type");
            if (!g.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array) { why = "geometry without coordinates"; return null; }
            void Ring(JsonElement ring)
            {
                var pts = new List<double>();
                foreach (var p in ring.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 2) continue;
                    double lon = p[0].GetDouble(), lat = p[1].GetDouble();
                    if (!(Ok(lon) && Ok(lat) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180)) continue;
                    pts.Add(lon); pts.Add(lat);
                }
                if (pts.Count >= 6) a.Rings.Add(pts.ToArray());
            }
            if (type == "Polygon") foreach (var r in coords.EnumerateArray()) Ring(r);
            else if (type == "MultiPolygon") foreach (var poly in coords.EnumerateArray()) foreach (var r in poly.EnumerateArray()) Ring(r);
            else { why = "geometry type " + type + " (Polygon or MultiPolygon only)"; return null; }
            if (a.Rings.Count == 0) { why = "no ring with three points"; return null; }
            a.West = 180; a.East = -180; a.South = 90; a.North = -90;
            foreach (var r in a.Rings)
                for (int i = 0; i < r.Length; i += 2) { a.West = Math.Min(a.West, r[i]); a.East = Math.Max(a.East, r[i]); a.South = Math.Min(a.South, r[i + 1]); a.North = Math.Max(a.North, r[i + 1]); }
            if (a.East - a.West > 180) { why = "crosses the antimeridian (split it into two polygons)"; return null; }
            if (!e.TryGetProperty("beds", out var beds) || beds.ValueKind != JsonValueKind.Array) { why = "no beds"; return null; }
            var list = new List<Bed>();
            foreach (var b in beds.EnumerateArray())
            {
                string code = Str(b, "code");
                double top = Num(b, "topM"), thick = Num(b, "thickM"), cover = Num(b, "cover");
                if (string.IsNullOrWhiteSpace(code) || !(Ok(top) && top >= 0 && top < 20000) || !(Ok(thick) && thick > 0 && thick < 5000)) { Problem(file, a.Id + ": a bed without code, topM or thickM; bed skipped"); continue; }
                if (!(Ok(cover) && cover > 0)) cover = 1;
                var bed = new Bed { Ore = new Ore { Code = code.Trim(), Share = 1 }, TopM = top, ThickM = thick, Cover = Math.Min(1, cover), Grade = Num(b, "grade"), Unit = Str(b, "unit") };
                if (Ok(bed.Grade) && fileOreMetal != null && fileOreMetal.TryGetValue(bed.Ore.Code, out var metals) && metals.Length > 0) { bed.Ore.Metal = metals[0]; bed.Ore.Grade = bed.Grade; }
                list.Add(bed);
            }
            if (list.Count == 0) { why = "no usable bed"; return null; }
            a.Beds = list.ToArray();
            return a;
        }

        /// <summary>Density (t/m³) when the source gives none: by the first ore, else by shape.</summary>
        public static double DefaultDensity(Shape shape, string code) => code switch
        {
            "lignite" => 1.2, "bituminouscoal" => 1.35, "anthracite" => 1.55, "peat" => 1.1,
            "halite" or "sylvite" or "earth-saltflat" or "earth-saltflat-sylvite" => 2.15, "earth-gypsum" or "gypsum" => 2.3,
            "earth-bauxite" => 2.2, "earth-placergold" or "earth-placertin" or "earth-placerplatinum" => 2.0, "earth-bogiron" => 1.6, "earth-kaolin" => 2.1,
            "hematite" or "magnetite" => 3.5, "limonite" => 3.0, "chromite" => 3.8, "ilmenite" => 3.6,
            "borax" or "kernite" => 1.8, "phosphorite" => 2.6, "sulfur" => 2.1,
            _ => shape switch { Shape.Lens => 3.2, Shape.Blanket => 2.0, Shape.Dome => 2.15, _ => 2.7 },
        };

        /// <summary>A blanket's layers when it names none: alluvial, outwash, laterite and saprolite (0065 §4).</summary>
        public static readonly int BlanketLayers = 1 << (int)OreLayers.Class.Alluvial | 1 << (int)OreLayers.Class.Outwash
            | 1 << (int)OreLayers.Class.Laterite | 1 << (int)OreLayers.Class.Saprolite;

        /// <summary>
        /// Leaves out ore codes the game cannot place (no installed provider, e.g. Geology Additions' codes without that
        /// mod): their share goes to the body's other codes, and a body or bed with none left is left out. Call before
        /// planning. Returns one line per body or area left out.
        /// </summary>
        public List<string> DropOres(Func<string, bool> installed)
        {
            var gone = new List<string>();
            for (int i = Deposits.Count - 1; i >= 0; i--)
            {
                var d = Deposits[i];
                if (d.Ores.All(o => installed(o.Code))) continue;
                var keep = d.Ores.Where(o => installed(o.Code)).ToArray();
                if (keep.Length == 0)
                {
                    gone.Add(d.Id + " (" + d.Name + "): " + string.Join("/", d.Ores.Select(o => o.Code)));
                    Deposits.RemoveAt(i);
                    continue;
                }
                double sum = keep.Sum(o => o.Share);
                foreach (var o in keep) o.Share /= sum;
                d.Ores = keep;
            }
            for (int i = Areas.Count - 1; i >= 0; i--)
            {
                var a = Areas[i];
                if (a.Beds.All(b => installed(b.Ore.Code))) continue;
                var keep = a.Beds.Where(b => installed(b.Ore.Code)).ToArray();
                if (keep.Length == 0)
                {
                    gone.Add("area " + a.Id + " (" + a.Name + "): " + string.Join("/", a.Beds.Select(b => b.Ore.Code)));
                    Areas.RemoveAt(i);
                    continue;
                }
                a.Beds = keep;
            }
            gone.Reverse();
            return gone;
        }

        /// <summary>The tier (0 poor, 1 medium, 2 rich, 3 bountiful) of a grade in a band; medium without a grade or band.</summary>
        public static int Tier(GradeBand band, double grade)
        {
            if (band == null || !double.IsFinite(grade)) return 1;
            return grade < band.Medium ? 0 : grade < band.Rich ? 1 : grade < band.Bountiful ? 2 : 3;
        }
        public GradeBand BandFor(string layer, string unit)
        {
            if (layer == null || !Bands.TryGetValue(layer, out var b)) return null;
            return unit == null || b.Unit == null || string.Equals(b.Unit, unit, StringComparison.OrdinalIgnoreCase) ? b : null;
        }
    }

    /// <summary>A world's scale for the catalogue: where the Earth lies in the world (the world's own EarthGeometry, so a body
    /// sits where the terrain has its place) and the ground depth curve.</summary>
    public sealed class OreScale
    {
        public const double MetresPerDegree = 111320;
        /// <summary>The scale where today's (rules v3) background calibration is kept: the default "earth" play style, 1:100,
        /// whose blocks are 100 m across at the equator.</summary>
        public const double ReferenceMetresPerBlock = 100;
        public readonly EarthGeometry Geometry;
        public readonly GroundColumns.DepthCurve Curve;
        public double RingLength => Geometry.RingLength;
        public double Gutter => Geometry.Gutter;
        public double BlocksPerDegreeZ => Geometry.BlocksPerDegreeZ;
        public double BlocksPerDegreeX => Geometry.BlocksPerDegreeX;
        /// <summary>h of decision 0065: metres per block north-south.</summary>
        public double Hz => MetresPerDegree / BlocksPerDegreeZ;
        /// <summary>Metres per block east-west at a latitude (the cylinder stretches the Earth east-west away from the Equator).</summary>
        public double HxAt(double lat) => MetresPerDegree * Math.Max(.02, Math.Cos(lat * Math.PI / 180)) / BlocksPerDegreeX;
        public OreScale(EarthGeometry geometry, GroundColumns.DepthCurve curve)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry)); Curve = curve;
        }
        /// <summary>A layout from its numbers (the tests).</summary>
        public OreScale(double ringStart, double ringLength, double gutter, double equatorZ, double blocksPerDegreeZ, double seamLon0, GroundColumns.DepthCurve curve)
            : this(new EarthGeometry { RingStart = ringStart, RingLength = ringLength, Wraps = true, Gutter = gutter, EquatorZ = equatorZ, BlocksPerDegreeZ = blocksPerDegreeZ, SeamLon0 = seamLon0 }, curve) { }
        public double ZOf(double lat) { Geometry.FromGeo(lat, 0, out _, out double z); return z; }
        public double XOf(double lon) { Geometry.FromGeo(0, lon, out double x, out _); return x; }
    }

    /// <summary>Where a body's or bed's blocks may go: <see cref="Basin"/> lets coal and evaporites take any sedimentary bed
    /// (inside a catalogue basin or a salt dome); <see cref="Classes"/> is the body's own layer classes (a bit per
    /// OreLayers.Class), replacing the 0060 table of its codes, 0 for that table.</summary>
    public readonly struct OreWhere
    {
        public readonly bool Basin;
        public readonly int Classes;
        public OreWhere(bool basin, int classes) { Basin = basin; Classes = classes; }
        public override string ToString()
        {
            int k = Classes;
            return (Basin ? "basin " : "") + (k == 0 ? "code table" : string.Join("/",
                Enumerable.Range(1, OreLayers.ClassNames.Length - 1).Where(c => (k & 1 << c) != 0).Select(c => OreLayers.ClassNames[c])));
        }
    }

    /// <summary>One block column the catalogue places into: the game's chunk column, or a test's model column. Depths are
    /// blocks below the column's top (0 = the top block).</summary>
    public interface IOreColumn
    {
        /// <summary>The terrain top (y).</summary>
        int Top { get; }
        /// <summary>Nothing at or below this y (the rock floor, at least 1).</summary>
        int Floor { get; }
        /// <summary>Whether <paramref name="ore"/> may go at <paramref name="depth"/>: a host or similar rock in a layer it may use
        /// (<paramref name="where"/>: the body's own layers, or the code's 0060 table with the basin rule).</summary>
        bool Allows(OreDepositCatalog.Ore ore, int depth, OreWhere where);
        /// <summary>Places the ore at a depth with a grade tier (0..3); false when nothing was placed.</summary>
        bool Place(OreDepositCatalog.Ore ore, int depth, int tier, double roll, OreWhere where);
    }

    /// <summary>
    /// The catalogue planned at one world's scale (decision 0065 §2): every deposit an ore body of its real volume over the
    /// world's block volume (h_x × h_z × the depth curve's metres per block), every area's beds at their real depth and
    /// thickness. Placement is a pure function of the world seed, the body and the block, so chunks agree at their borders.
    /// </summary>
    public sealed class OreDepositPlan
    {
        public readonly OreDepositCatalog Catalog;
        public readonly OreScale Scale;
        public readonly long Seed;
        /// <summary>The world's deposit rate (globalDepositSpawnRate) times any setting: multiplies every body's volume.</summary>
        public readonly double Rate;
        public readonly Body[] Bodies;
        public readonly AreaPlan[] Areas;
        /// <summary>(CR-O9) The bodies and areas left out because they could reach past the ring's usable span (see
        /// <see cref="RingWest"/>), one line each for the log: placement neither wraps nor clips them at the seam, so their
        /// blocks would land in the gutter or beyond the jump line while their far side stayed missing. None of the shipped
        /// catalogue comes near the seam, so this list is empty there and nothing else is planned differently.</summary>
        public readonly List<string> OffRing = new();
        const int Cell = 512;
        readonly Dictionary<long, int[]> index = new();

        /// <summary>Lognormal spread of the grade inside a body (sigma of ln grade).</summary>
        public const double GradeSpread = .5;

        /// <summary>
        /// Ore rules v5 (EA-0058): a blanket that may only use cover layers (placers in alluvium and outwash, laterites,
        /// saprolite, peat, playas) and whose footprint has no column with one of its layers within its depth window moves to
        /// the nearest place that has one, within <see cref="RadiusKm"/>. A catalogue position is often a district's or a
        /// town's rather than the valley floor itself, and where the ground data has no river there the column has no
        /// alluvium: the Iss and Tura, Nizhny Tagil and Kondyor platinum placers placed nothing in rules v4. Placers lie in
        /// their valleys, so the body goes there. A blanket whose footprint has its layer anywhere stays where it is.
        /// </summary>
        public sealed class ValleySearch
        {
            /// <summary>How many blocks of column (x, z) between the two depths (blocks below its top, inclusive) lie in a layer
            /// of the given classes (a bit per OreLayers.Class), from the world's model: (x, z, classes, fromDepth, toDepth).</summary>
            public Func<int, int, int, int, int, int> LayerBlocks;
            /// <summary>How far the body may move (real km).</summary>
            public double RadiusKm = 30;
            /// <summary>Spacing of the sampled points (real m): the footprint's grid and the rings around it.</summary>
            public double StepMetres = 200;
            /// <summary>How far a movable blanket may grow (its axes times this at most) to find room for its volume in the
            /// columns that have its layers; v4 grows any body at most 1.9 times.</summary>
            public double MaxGrowth = 4;
        }
        readonly ValleySearch search;
        /// <summary>The layer classes a blanket may move for (cover layers only).</summary>
        public const int CoverClasses = 1 << (int)OreLayers.Class.Alluvial | 1 << (int)OreLayers.Class.Outwash | 1 << (int)OreLayers.Class.Peat
            | 1 << (int)OreLayers.Class.Laterite | 1 << (int)OreLayers.Class.Saprolite | 1 << (int)OreLayers.Class.Playa;
        /// <summary>Whether the valley search may move a body: a blanket whose own layers are all cover layers.</summary>
        public static bool Movable(Body b) => b.D.Shape == OreDepositCatalog.Shape.Blanket && b.Where.Classes != 0 && (b.Where.Classes & ~CoverClasses) == 0;
        /// <summary>Bodies with an envelope of at most this many columns get their fill from the exact block count.</summary>
        const int ExactColumns = 40000;

        public sealed class Body
        {
            public OreDepositCatalog.Deposit D;
            public int Index, Salt;
            public double X, Z, Hx, Hz;
            /// <summary>Semi-axes along and across strike (m), strike unit vector in world x/z, dip (rad), the lens's half thickness (m).</summary>
            public double A, B, Sx, Sz, Dip, HalfThick, TopM, BaseM, LensMid;
            /// <summary>Real ore volume over the real envelope volume (0..1).</summary>
            public double FillReal;
            /// <summary>Veins: one sheet every this many blocks across strike (1: the whole envelope).</summary>
            public int Sheets = 1;
            /// <summary>Expected blocks (real volume at the world's scale, times the rate).</summary>
            public double Target;
            public int MinX, MaxX, MinZ, MaxZ;
            public OreDepositCatalog.GradeBand Band;
            public int BaseTier;
            /// <summary>The body's layers (its own or its codes' table) and the dome's basin rule.</summary>
            public OreWhere Where;
            public double TopShare, BottomShare;
            public OreDepositCatalog.Ore[] Top, Mid, Bottom;
            /// <summary>Per envelope block chance (after discretisation), or the compact cluster's block count.</summary>
            volatile bool planned;
            public double Fill;
            public bool Compact;
            public int CompactX, CompactZ, CompactDepth;
            public long Placed;
            /// <summary>Ore rules v5: how far the valley search moved the body (real m; 0 = not moved), and whether it found its
            /// layers at all (false: none within the radius, so it stays and places nothing).</summary>
            public double MovedM;
            public bool LayerFound = true;
            /// <summary>Ore rules v5: the valley search's time (ms; 0 when it did not run).</summary>
            public double SearchMs;
            internal readonly object Gate = new();
            public bool Planned => planned;
            internal void Done() => planned = true;
            internal Body Copy() => (Body)MemberwiseClone();
            /// <summary>Puts back what <see cref="OreDepositPlan.TryEnsure"/> changes (its search, growth, fill and compact cluster):
            /// a field Ensure comes to change must be listed here too.</summary>
            internal void RestoreFrom(Body s)
            {
                X = s.X; Z = s.Z; A = s.A; B = s.B; Sheets = s.Sheets; Fill = s.Fill;
                MinX = s.MinX; MaxX = s.MaxX; MinZ = s.MinZ; MaxZ = s.MaxZ;
                Compact = s.Compact; CompactX = s.CompactX; CompactZ = s.CompactZ; CompactDepth = s.CompactDepth;
                MovedM = s.MovedM; LayerFound = s.LayerFound; SearchMs = s.SearchMs;
            }
            public override string ToString() => D.ToString();
        }

        public sealed class AreaPlan
        {
            public OreDepositCatalog.Area A;
            public int Index, Salt, MinX, MaxX, MinZ, MaxZ;
            /// <summary>Per ring: x (world blocks), lat; x unwrapped from the ring's first point.</summary>
            public double[][] Rings;
            public bool Basin;
            /// <summary>(CR-O9) Its rings reach past the ring's usable span: left out (<see cref="AreasNear"/> skips it).</summary>
            public bool OffRing;
            public int PatchCell;
            public long Placed;
            public double[] Threshold;
            public override string ToString() => A.ToString();
        }

        public OreDepositPlan(OreDepositCatalog catalog, OreScale scale, long seed, double rate, ValleySearch search = null)
        {
            Catalog = catalog; Scale = scale; Seed = seed; Rate = double.IsFinite(rate) && rate > 0 ? rate : 0;
            this.search = search?.LayerBlocks != null && search.RadiusKm > 0 ? search : null;
            Bodies = new Body[catalog.Deposits.Count];
            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i] = Plan(catalog.Deposits[i], i);
                var b = Bodies[i];
                // (CR-O9) a body whose box could ever leave the ring's usable span is not indexed, so it is never placed or read;
                // true only within that box's reach of the seam (and of the valley search's radius, rules v5)
                bool movable = this.search != null && Movable(b);
                double moveX = movable ? this.search.RadiusKm * 1000 / b.Hx + Math.Max(0, this.search.MaxGrowth - 1.9) * Reach(b) / b.Hx : 0;
                double moveZ = movable ? this.search.RadiusKm * 1000 / b.Hz + Math.Max(0, this.search.MaxGrowth - 1.9) * Reach(b) / b.Hz : 0;
                double room = GrownReachX(b) + moveX;
                if (b.X - room < RingWest || b.X + room > RingEast) { OffRing.Add(b.D.ToString()); continue; }
                // indexed with room for the growth Ensure may give it (up to 1.9 times its reach, or a compact cluster) and for
                // the valley search's move
                double mx = Reach(b) / b.Hx * 2 + 20 + moveX, mz = Reach(b) / b.Hz * 2 + 20 + moveZ;
                int ix0 = (int)Math.Floor(b.X - mx), ix1 = (int)Math.Ceiling(b.X + mx), iz0 = (int)Math.Floor(b.Z - mz), iz1 = (int)Math.Ceiling(b.Z + mz);
                for (long cz = Floor(iz0, Cell); cz <= Floor(iz1, Cell); cz++)
                    for (long cx = Floor(ix0, Cell); cx <= Floor(ix1, Cell); cx++)
                    {
                        long key = cx << 32 ^ (uint)cz;
                        index[key] = index.TryGetValue(key, out var l) ? l.Append(i).ToArray() : new[] { i };
                    }
            }
            Areas = catalog.Areas.Select((a, i) => PlanArea(a, i)).ToArray();
            foreach (var a in Areas) if (a.OffRing) OffRing.Add("area " + a.A);
        }
        static long Floor(int v, int cell) => (long)Math.Floor((double)v / cell);
        /// <summary>(CR-O9) The ring's usable span in x, where XOf puts the Earth's 360°: from the gutter's west half to its east
        /// half (the whole ring in worlds without a gutter).</summary>
        double RingWest => Scale.Geometry.RingStart + Scale.Gutter / 2;
        double RingEast => Scale.Geometry.RingStart + Scale.RingLength - Scale.Gutter / 2;
        /// <summary>(CR-O9) The farthest a body's box can reach east or west of its centre (blocks): its Bounds after Ensure's
        /// growth (at most 1.9 times its reach, plus the rounding) or a compact cluster's box.</summary>
        static double GrownReachX(Body b) => Math.Max(1.9 * Reach(b) / b.Hx + 3, Math.Ceiling(Math.Cbrt(Math.Max(1, b.Target))) + 3);

        /// <summary>The bodies whose box reaches the area (x0..x1, z0..z1), each planned (<see cref="TryEnsure"/>); a body
        /// whose plan this thread could not finish from the real heights is left out. With <paramref name="startSearch"/> false a
        /// body ore rules v5 would first have to search for is not searched here but returned unplanned, with its catalogue
        /// place and box (/earth ores on the main thread: a 50 km square of searches one after another; ER-06).</summary>
        public IEnumerable<Body> BodiesNear(int x0, int z0, int x1, int z1, bool startSearch = true)
        {
            var seen = new HashSet<int>();
            for (long cz = Floor(z0, Cell); cz <= Floor(z1, Cell); cz++)
                for (long cx = Floor(x0, Cell); cx <= Floor(x1, Cell); cx++)
                    if (index.TryGetValue(cx << 32 ^ (uint)cz, out var list))
                        foreach (int i in list)
                        {
                            var b = Bodies[i];
                            if (!seen.Add(i)) continue;
                            bool unsearched = !startSearch && !b.Planned && Searches(b);
                            if (!unsearched && !TryEnsure(b)) continue;   // its final box
                            if (b.MaxX < x0 || b.MinX > x1 || b.MaxZ < z0 || b.MinZ > z1) continue;
                            yield return b;
                        }
        }

        /// <summary>Whether planning the body runs ore rules v5's valley search (a movable blanket in a world with the search).</summary>
        public bool Searches(Body b) => search != null && Movable(b);
        public IEnumerable<AreaPlan> AreasNear(int x0, int z0, int x1, int z1)
        {
            foreach (var a in Areas) if (!a.OffRing && !(a.MaxX < x0 || a.MinX > x1 || a.MaxZ < z0 || a.MinZ > z1)) yield return a;
        }

        // ------------------------------------------------------------------ bodies

        Body Plan(OreDepositCatalog.Deposit d, int index)
        {
            var b = new Body { D = d, Index = index, Salt = RegionalOreCatalog.StableCode(d.Id) };
            b.X = Scale.XOf(d.Lon); b.Z = Scale.ZOf(d.Lat);
            b.Hx = Scale.HxAt(d.Lat); b.Hz = Scale.Hz;
            double th = d.StrikeDeg * Math.PI / 180;
            b.Sx = Math.Sin(th); b.Sz = -Math.Cos(th);   // north is -z
            b.TopM = d.TopM; b.BaseM = d.BaseM;
            b.A = d.LenKm * 500; b.B = d.WidKm * 500;
            b.Dip = d.DipDeg * Math.PI / 180;
            double volume = d.Mt * 1e6 / d.Density;
            double depth = b.BaseM - b.TopM;
            double envelope;
            if (d.Shape == OreDepositCatalog.Shape.Lens)
            {
                // a dipping ellipsoid: strike half-length A, down-dip half-extent C, true half-thickness from the volume
                double sin = Math.Sin(b.Dip), cos = Math.Cos(b.Dip);
                double c = sin > .7 ? depth / 2 / sin : Math.Max(b.B / Math.Max(.2, cos), depth / 2 / Math.Max(.05, sin) * (sin > .05 ? 1 : 0));
                c = Math.Max(c, 1);
                b.B = c;   // lens: B is the down-dip half-extent
                b.HalfThick = 1.5 * volume / (Math.PI * b.A * c) / 2;
                if (b.HalfThick > c) { double k = Math.Sqrt(b.HalfThick / c); b.A *= k; b.B *= k; b.HalfThick = c; }
                envelope = 4.0 / 3 * Math.PI * b.A * b.B * b.HalfThick;
                // the lens hangs from its top: its centre deep enough that no part of it reaches above topM (or the surface)
                double hv = Math.Sqrt(Math.Pow(b.B * Math.Sin(b.Dip), 2) + Math.Pow(b.HalfThick * Math.Cos(b.Dip), 2));
                b.LensMid = Math.Max((b.TopM + b.BaseM) / 2, b.TopM + hv);
            }
            else
            {
                envelope = Math.PI * b.A * b.B * depth;
                if (volume > envelope)
                {
                    double k = Math.Sqrt(volume / envelope * 1.0001);
                    b.A *= k; b.B *= k; envelope = Math.PI * b.A * b.B * depth;
                }
            }
            b.FillReal = Math.Min(1, volume / envelope);
            if (d.Shape == OreDepositCatalog.Shape.Veins) b.Sheets = Math.Clamp((int)Math.Floor(.6 / Math.Max(1e-6, b.FillReal)), 1, 12);
            // the expected blocks: the real body over the world's block volume, by quadrature of its columns through the curve
            b.Target = b.FillReal * EnvelopeQuadrature(b) * Rate;
            Bounds(b);
            b.Band = Catalog.BandFor(d.Layer, d.Unit);
            b.Where = new OreWhere(d.Shape == OreDepositCatalog.Shape.Dome, d.Classes);
            foreach (var o in d.Ores) if (o.Metal != null) o.Band = Catalog.BandFor(o.Metal, null);
            b.BaseTier = OreDepositCatalog.Tier(b.Band, d.Grade);
            b.Top = d.Ores.Where(o => o.Zone > 0).ToArray(); b.Bottom = d.Ores.Where(o => o.Zone < 0).ToArray(); b.Mid = d.Ores.Where(o => o.Zone == 0).ToArray();
            b.TopShare = b.Top.Sum(o => o.Share); b.BottomShare = b.Bottom.Sum(o => o.Share);
            return b;
        }
        static double Reach(Body b) => b.D.Shape == OreDepositCatalog.Shape.Lens
            ? Math.Max(b.A, b.B * Math.Abs(Math.Cos(b.Dip)) + b.HalfThick * Math.Abs(Math.Sin(b.Dip)) + 1)
            : Math.Max(b.A, b.B);

        /// <summary>The body's depth interval (m below the surface) at a horizontal offset (m, world x and z), false outside.</summary>
        public static bool ColumnMetres(Body b, double dx, double dz, out double top, out double bottom)
        {
            top = bottom = 0;
            double u = dx * b.Sx + dz * b.Sz, w = dx * -b.Sz + dz * b.Sx;   // across strike: strike turned 90° clockwise (the dip side)
            if (b.D.Shape == OreDepositCatalog.Shape.Lens)
            {
                double sin = Math.Sin(b.Dip), cos = Math.Cos(b.Dip), c2 = b.B * b.B, e2 = b.HalfThick * b.HalfThick;
                // (u/A)² + ((w cos + t sin)/C)² + ((-w sin + t cos)/E)² <= 1, t: depth below the centre
                double qa = sin * sin / c2 + cos * cos / e2;
                double qb = 2 * w * (cos * sin / c2 - sin * cos / e2);
                double qc = u * u / (b.A * b.A) + w * w * (cos * cos / c2 + sin * sin / e2) - 1;
                double disc = qb * qb - 4 * qa * qc;
                if (disc <= 0) return false;
                double r = Math.Sqrt(disc), mid = b.LensMid;
                top = Math.Max(0, mid + (-qb - r) / (2 * qa)); bottom = mid + (-qb + r) / (2 * qa);
                return bottom > top;
            }
            if (u * u / (b.A * b.A) + w * w / (b.B * b.B) > 1) return false;
            top = b.TopM; bottom = b.BaseM;
            return true;
        }
        /// <summary>The body's block interval (block depths below the column's top, continuous) at a column.</summary>
        public bool ColumnBlocks(Body b, int x, int z, out double top, out double bottom)
        {
            top = bottom = 0;
            if (!ColumnMetres(b, (x + .5 - b.X) * b.Hx, (z + .5 - b.Z) * b.Hz, out double t, out double s)) return false;
            top = Scale.Curve.Blocks(t); bottom = Scale.Curve.Blocks(s);
            return bottom > top;
        }
        /// <summary>Veins: whether block depth d of column (x, z) lies on a sheet (steep sheets parallel to strike).</summary>
        public bool OnSheet(Body b, int x, int z, int d)
        {
            if (b.Sheets <= 1) return true;
            double w = ((x + .5 - b.X) * b.Hx * -b.Sz + (z + .5 - b.Z) * b.Hz * b.Sx) / Math.Max(1, Math.Min(b.Hx, b.Hz));
            double lean = Math.Abs(b.Dip) < 1.55 ? (d + .5) * Scale.Curve.Top / Math.Max(1, Math.Min(b.Hx, b.Hz)) / Math.Tan(b.Dip) : 0;
            double p = w - lean;
            long k = (long)Math.Floor(p);
            return ((k % b.Sheets) + b.Sheets) % b.Sheets == 0;
        }

        /// <summary>Σ over the real envelope of its blocks of depth, per world column area (quadrature, 64 × 64).</summary>
        double EnvelopeQuadrature(Body b)
        {
            const int n = 64;
            double reach = Reach(b), cell = 2 * reach / n, sum = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double dx = -reach + (i + .5) * cell, dz = -reach + (j + .5) * cell;
                    if (ColumnMetres(b, dx, dz, out double t, out double s)) sum += Scale.Curve.Blocks(s) - Scale.Curve.Blocks(t);
                }
            return sum * cell * cell / (b.Hx * b.Hz);
        }

        /// <summary>Fill per envelope block from the exact block count of the envelope (small bodies) or the quadrature; a body
        /// under one block, or one whose envelope holds fewer blocks than it needs, is placed as a compact cluster.</summary>
        public void Ensure(Body b) => TryEnsure(b);

        /// <summary>
        /// <see cref="Ensure"/>; false where ore rules v5's valley search read heights this thread could not get
        /// (FineElevation.MissedReads: a GLO-90 tile not on this machine, on a thread that may not fetch it, such as the main
        /// thread's prospecting pick, or a thread on the built-in heights). The body is then put back as it was and stays
        /// unplanned, so a later asker (the deposit pass, which waits for its tiles) plans it from the real heights. Before, the
        /// first asker's answer was kept for the session, and a body's place could differ between the chunks of two sessions
        /// (ER-06, code review 14 November 2026).
        /// </summary>
        public bool TryEnsure(Body b)
        {
            if (b.Planned) return true;
            lock (b.Gate)
            {
                if (b.Planned) return true;
                bool searches = Searches(b);
                var before = searches ? b.Copy() : null;
                // (L-1 of the Fable pass) a search that ends early (the world closing under a pool thread's search, or any
                // throw) leaves the body as it was, unplanned, for the next asker
                try { return PlanLocked(b, searches, before); }
                catch
                {
                    if (searches) b.RestoreFrom(before);
                    throw;
                }
            }
        }

        bool PlanLocked(Body b, bool searches, Body before)
        {
            {
                int missed = FineElevation.MissedReads;
                // ore rules v5 (EA-0058): a movable blanket goes where its layers are and is sized by the columns that have them
                bool layered = false;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (search != null && Movable(b))
                {
                    Relocate(b);
                    layered = b.LayerFound;
                }
                double grown = 1, cap = layered ? search.MaxGrowth : 1.9;
                var asked = layered ? new Dictionary<long, int>() : null;
                for (int round = 0; ; round++)
                {
                    double envelope = layered ? LayeredEnvelope(b, asked) : Envelope(b);
                    // no column of its layers in reach after all: sized as in rules v4
                    if (layered && !(envelope > 0)) { layered = false; cap = 1.9; envelope = Envelope(b); }
                    b.Fill = envelope > 0 ? b.Target / envelope : double.PositiveInfinity;
                    if (!(b.Fill > 1) || b.Target < 8 || round >= (layered ? 12 : 4)) break;
                    // the blocks of its envelope hold less than the body: fewer gaps between veins, or a wider body
                    if (b.Sheets > 1) b.Sheets = Math.Max(1, (int)Math.Floor(b.Sheets / b.Fill));
                    else
                    {
                        // a layered blanket grows in small steps: the columns with its layers are not spread evenly, so one step by
                        // the fill could overshoot into a far floodplain and thin the body out over it
                        double k = Math.Min(Math.Sqrt(b.Fill * 1.03), Math.Min(cap / grown, layered ? 1.3 : double.PositiveInfinity));
                        if (k <= 1.0001) break;
                        grown *= k; b.A *= k; b.B *= k;
                        Bounds(b);
                    }
                }
                if (search != null && Movable(b))
                    b.SearchMs = Math.Max(1e-3, (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                // a layered blanket that still lacks room keeps its fill above 1: each column with its layer takes more of it,
                // within that layer (PlaceBody places only where the column allows), rather than a cluster at its centre
                if (b.Target < 1 || b.Fill > 1 && !layered)
                {
                    b.Compact = true;
                    b.CompactX = (int)Math.Floor(b.X); b.CompactZ = (int)Math.Floor(b.Z);
                    b.CompactDepth = (int)Math.Floor(Scale.Curve.Blocks((b.TopM + b.BaseM) / 2));
                    int reach = (int)Math.Ceiling(Math.Cbrt(Math.Max(1, b.Target))) + 2;
                    b.MinX = Math.Min(b.MinX, b.CompactX - reach); b.MaxX = Math.Max(b.MaxX, b.CompactX + reach);
                    b.MinZ = Math.Min(b.MinZ, b.CompactZ - reach); b.MaxZ = Math.Max(b.MaxZ, b.CompactZ + reach);
                }
                if (searches && FineElevation.MissedReads != missed) { b.RestoreFrom(before); return false; }
                b.Done();
                return true;
            }
        }
        /// <summary>The blanket's depth window (blocks below the top), as PlaceBody snaps it: from its top (never the top block) to
        /// twice its base, at least 8.</summary>
        void Window(Body b, out int from, out int to)
        {
            from = Math.Max(1, (int)Math.Floor(Scale.Curve.Blocks(b.TopM)));
            to = Math.Max(8, 2 * (int)Math.Ceiling(Scale.Curve.Blocks(b.BaseM)));
        }

        /// <summary>Ore rules v5: the envelope's blocks that its layers can hold, column by column: the body's interval, at most
        /// the column's blocks of those layers within its window (a coarse grid would miss narrow valleys or count a whole
        /// cell for one; each column is asked once across the growth rounds).</summary>
        double LayeredEnvelope(Body b, Dictionary<long, int> asked)
        {
            Window(b, out int from, out int to);
            double envelope = 0;
            for (int z = b.MinZ; z <= b.MaxZ; z++)
            {
                CheckStop();
                for (int x = b.MinX; x <= b.MaxX; x++)
                {
                    if (!ColumnBlocks(b, x, z, out double t, out double s)) continue;
                    long key = (long)x << 32 | (uint)z;
                    if (!asked.TryGetValue(key, out int room)) asked[key] = room = search.LayerBlocks(x, z, b.Where.Classes, from, to);
                    envelope += Math.Min(s - t, room);
                }
            }
            return envelope;
        }

        /// <summary>Ore rules v5 (<see cref="ValleySearch"/>): keeps the body where its footprint has one of its layers anywhere,
        /// else moves its centre to the nearest sampled point that has one, ring by ring outwards (the first point of the
        /// nearest ring, clockwise from north), within the search radius. Deterministic: the model's columns only.</summary>
        /// <summary>
        /// (L-1 of the Fable pass) Set on a thread whose valley searches must end when the world closes: the prospecting pick's
        /// search on a pool thread (EarthOreDeposits.SearchLater), which could otherwise run seconds into the next world on the
        /// closed one's model. The deposit pass's searches never stop early.
        /// </summary>
        [ThreadStatic] static Func<bool> stopSearch;

        /// <summary><paramref name="work"/> on this thread with <paramref name="stop"/> asked during each valley search; a stop
        /// throws OperationCanceledException and the body stays as it was (<see cref="TryEnsure"/>).</summary>
        public static T WithStop<T>(Func<bool> stop, Func<T> work)
        {
            var was = stopSearch;
            stopSearch = stop;
            try { return work(); }
            finally { stopSearch = was; }
        }

        static void CheckStop()
        {
            if (stopSearch?.Invoke() == true) throw new OperationCanceledException("the world closed during an ore valley search");
        }

        void Relocate(Body b)
        {
            Window(b, out int from, out int to);
            int classes = b.Where.Classes;
            bool Has(double dxM, double dzM) => search.LayerBlocks((int)Math.Floor(b.X + dxM / b.Hx), (int)Math.Floor(b.Z + dzM / b.Hz), classes, from, to) > 0;
            // the footprint: a grid along and across strike, at most the step apart (at least a block) and two points per semi-axis
            double block = Math.Min(b.Hx, b.Hz);
            double su = Math.Max(Math.Min(search.StepMetres, b.A / 2), block), sw = Math.Max(Math.Min(search.StepMetres, b.B / 2), block);
            for (double u = -Math.Floor(b.A / su) * su; u <= b.A; u += su)
                for (double w = -Math.Floor(b.B / sw) * sw; w <= b.B; w += sw)
                {
                    if (u * u / (b.A * b.A) + w * w / (b.B * b.B) > 1) continue;
                    if (Has(u * b.Sx - w * b.Sz, u * b.Sz + w * b.Sx)) return;
                }
            // rings around the centre, from the footprint's minor axis outwards
            double step = Math.Max(search.StepMetres, block), max = search.RadiusKm * 1000;
            for (double r = Math.Max(step, Math.Floor(b.B / step) * step); r <= max; r += step)
            {
                CheckStop();
                int n = Math.Max(8, (int)Math.Ceiling(2 * Math.PI * r / step));
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * k / n, dx = r * Math.Sin(a), dz = -r * Math.Cos(a);
                    if (!Has(dx, dz)) continue;
                    b.X += dx / b.Hx; b.Z += dz / b.Hz;
                    b.MovedM = r;
                    Bounds(b);
                    return;
                }
            }
            b.LayerFound = false;
        }

        void Bounds(Body b)
        {
            double reachX = Reach(b) / b.Hx + 2, reachZ = Reach(b) / b.Hz + 2;
            b.MinX = (int)Math.Floor(b.X - reachX); b.MaxX = (int)Math.Ceiling(b.X + reachX);
            b.MinZ = (int)Math.Floor(b.Z - reachZ); b.MaxZ = (int)Math.Ceiling(b.Z + reachZ);
        }
        /// <summary>The envelope's blocks (veins: on their sheets), exact for small bodies, else from the quadrature.</summary>
        double Envelope(Body b)
        {
            long columns = (long)(b.MaxX - b.MinX + 1) * (b.MaxZ - b.MinZ + 1);
            if (columns > ExactColumns) return EnvelopeQuadrature(b) / b.Sheets;
            double envelope = 0;
            for (int z = b.MinZ; z <= b.MaxZ; z++)
                for (int x = b.MinX; x <= b.MaxX; x++)
                {
                    if (!ColumnBlocks(b, x, z, out double t, out double s)) continue;
                    if (b.Sheets <= 1) { envelope += s - t; continue; }
                    for (int d = (int)Math.Floor(t); d < s; d++) if (OnSheet(b, x, z, d)) envelope += Math.Min(s, d + 1) - Math.Max(t, d);
                }
            return envelope;
        }

        /// <summary>Offsets of a compact cluster in order (nearest the centre first, then by a fixed hash): a ball of blocks.</summary>
        const int BallRadius = 14, BallSide = 2 * BallRadius + 1;
        static readonly (int X, int Y, int Z)[] Ball = BuildBall(BallRadius);
        static (int, int, int)[] BuildBall(int r)
        {
            var list = new List<(int, int, int, double)>();
            for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++) for (int x = -r; x <= r; x++)
                    list.Add((x, y, z, x * x + z * z + y * y * 1.0 + RegionalOreCatalog.Random(7, x, y, z, 1) * .01));
            return list.OrderBy(t => t.Item4).Select(t => (t.Item1, t.Item2, t.Item3)).ToArray();
        }
        /// <summary>The ball's indices by column (x and z offset), ascending: a column walks only its own blocks, in the ball's order.</summary>
        static readonly int[][] BallColumns = BuildBallColumns();
        static int[][] BuildBallColumns()
        {
            var lists = new List<int>[BallSide * BallSide];
            for (int i = 0; i < Ball.Length; i++) (lists[(Ball[i].X + BallRadius) * BallSide + Ball[i].Z + BallRadius] ??= new List<int>()).Add(i);
            return lists.Select(l => l?.ToArray() ?? Array.Empty<int>()).ToArray();
        }
        /// <summary>Blocks of a compact cluster: its expected size, rounded up or down by chance (the expected value kept).</summary>
        public int CompactCount(Body b)
        {
            double t = b.Target;
            int n = (int)Math.Floor(t);
            if (RegionalOreCatalog.Random(Seed, b.CompactX, 1, b.CompactZ, b.Salt ^ 0x51ab) < t - n) n++;
            return Math.Min(n, Ball.Length);
        }

        double Roll(int x, int y, int z, int salt) => RegionalOreCatalog.Random(Seed, x, y, z, salt);

        OreDepositCatalog.Ore Choose(Body b, double u, double r)
        {
            OreDepositCatalog.Ore[] set = u < b.TopShare && b.Top.Length > 0 ? b.Top : u > 1 - b.BottomShare && b.Bottom.Length > 0 ? b.Bottom : b.Mid;
            if (set.Length == 0) set = b.Top.Length > 0 ? b.Top : b.Bottom;
            if (set.Length == 1) return set[0];
            double total = 0; foreach (var o in set) total += o.Share;
            double acc = 0; r *= total;
            foreach (var o in set) { acc += o.Share; if (r < acc) return o; }
            return set[^1];
        }
        /// <summary>A block's tier: the ore's own metal grade when the catalogue gives one, else the body's grade, with a
        /// lognormal spread (mean kept), through the band.</summary>
        int BlockTier(Body b, OreDepositCatalog.Ore ore, double r1, double r2)
        {
            bool own = ore.Band != null;
            var band = own ? ore.Band : b.Band;
            double grade = own ? ore.Grade : b.D.Grade;
            if (band == null || !double.IsFinite(grade)) return own ? 1 : b.BaseTier;
            double n = Math.Sqrt(-2 * Math.Log(Math.Max(1e-12, r1))) * Math.Cos(2 * Math.PI * r2);
            return OreDepositCatalog.Tier(band, grade * Math.Exp(GradeSpread * n - GradeSpread * GradeSpread / 2));
        }

        /// <summary>
        /// Places a body's blocks in one column (x, z). With <paramref name="place"/> false only counts what it would place
        /// (prospecting: the expected blocks, by chance). Returns the blocks placed (or expected) of <paramref name="only"/>
        /// (null: all codes).
        /// </summary>
        public double PlaceBody(Body b, int x, int z, IOreColumn col, bool place = true, string only = null)
        {
            Ensure(b);
            if (x < b.MinX || x > b.MaxX || z < b.MinZ || z > b.MaxZ) return 0;
            double count = 0;
            int top = col.Top, floor = Math.Max(1, col.Floor);
            if (b.Compact)
            {
                int n = place ? CompactCount(b) : 0;
                double each = 1;
                if (!place)
                {
                    // expected: every block of the rounded-up cluster weighted by its chance
                    n = Math.Min(Ball.Length, (int)Math.Ceiling(b.Target));
                    each = n > 0 ? b.Target / n : 0;
                }
                if (n == 0) return 0;
                int radius = Math.Max(1, (int)Math.Ceiling(Math.Cbrt(n * 3 / (4 * Math.PI))));
                int ox = x - b.CompactX, oz = z - b.CompactZ;
                if (ox < -BallRadius || ox > BallRadius || oz < -BallRadius || oz > BallRadius) return 0;
                // the cluster's first n blocks that lie in this column, in the ball's order
                foreach (int i in BallColumns[(ox + BallRadius) * BallSide + oz + BallRadius])
                {
                    if (i >= n) break;
                    var o = Ball[i];
                    int d = b.CompactDepth + o.Y;
                    // a blanket never takes the column's top block (soil or turf)
                    if (d < (b.D.Shape == OreDepositCatalog.Shape.Blanket ? 1 : 0) || top - d <= floor) continue;
                    // a single block takes any of the body's ores by share; a cluster is layered by height
                    double u = n <= 1 ? Roll(x, top - d, z, b.Salt + 15) : (o.Y + radius + .5) / (2.0 * radius + 1);
                    var ore = Choose(b, Math.Clamp(u, 0, .9999), Roll(x, top - d, z, b.Salt + 11));
                    if (ore.Slot < 0 && place || only != null && ore.Code != only) continue;
                    if (!col.Allows(ore, d, b.Where)) continue;
                    if (!place) { count += each; continue; }
                    if (col.Place(ore, d, BlockTier(b, ore, Roll(x, top - d, z, b.Salt + 12), Roll(x, top - d, z, b.Salt + 13)), Roll(x, top - d, z, b.Salt + 14), b.Where)) count++;
                }
                return count;
            }
            if (!ColumnBlocks(b, x, z, out double t, out double s)) return 0;
            if (b.D.Shape == OreDepositCatalog.Shape.Blanket)
            {
                // a sheet in a cover layer: its blocks (expected value kept) go into the first layer it may use below its top,
                // within twice its depth and at least 8 blocks (the column's alluvium or laterite need not sit at the source's metres)
                double expected = (s - t) * b.Fill;
                int n = (int)Math.Floor(expected);
                if (Roll(x, 3, z, b.Salt ^ 0x2b1) < expected - n) n++;
                var first = Choose(b, .5, Roll(x, 5, z, b.Salt + 11));
                if (only != null && b.D.Ores.All(o => o.Code != only)) return 0;
                // never the column's top block (soil or turf)
                int start = Snap(col, first, Math.Max(1, (int)Math.Floor(t)), Math.Max(8, 2 * (int)Math.Ceiling(s)), b.Where, downOnly: true);
                if (start < 0) return 0;
                int m = place ? n : Math.Max(1, (int)Math.Ceiling(expected));
                for (int i = 0; i < m; i++)
                {
                    int d = start + i, y = top - d;
                    if (y <= floor) break;
                    // each block a slot of the profile, jittered inside it: zones keep their order and a thin sheet its shares
                    var ore = Choose(b, Math.Clamp((i + Roll(x, y, z, b.Salt + 15)) / Math.Max(1, m), 0, .9999), Roll(x, y, z, b.Salt + 11));
                    if (only != null && ore.Code != only) continue;
                    if (!col.Allows(ore, d, b.Where)) continue;
                    if (!place) { count += expected / m; continue; }
                    if (ore.Slot < 0) continue;
                    if (col.Place(ore, d, BlockTier(b, ore, Roll(x, y, z, b.Salt + 12), Roll(x, y, z, b.Salt + 13)), Roll(x, y, z, b.Salt + 14), b.Where)) count++;
                }
                return count;
            }
            for (int d = (int)Math.Floor(t); d < s; d++)
            {
                int y = top - d;
                if (y <= floor) break;
                double occ = Math.Min(s, d + 1) - Math.Max(t, d);
                if (occ <= 0) continue;
                if (b.Sheets > 1 && !OnSheet(b, x, z, d)) continue;
                double p = occ * b.Fill;
                if (place ? Roll(x, y, z, b.Salt) >= p : false) continue;
                double u = (d + .5 - t) / (s - t);
                var ore = Choose(b, Math.Clamp(u, 0, .9999), Roll(x, y, z, b.Salt + 11));
                if (only != null && ore.Code != only) continue;
                if (!col.Allows(ore, d, b.Where)) continue;
                if (!place) { count += p; continue; }
                if (ore.Slot < 0) continue;
                if (col.Place(ore, d, BlockTier(b, ore, Roll(x, y, z, b.Salt + 12), Roll(x, y, z, b.Salt + 13)), Roll(x, y, z, b.Salt + 14), b.Where)) count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ areas

        static readonly double[] NoiseQuantiles = BuildQuantiles();
        /// <summary>Quantiles of OreRules.BlockNoise, so a bed's patches cover exactly its share of the area.</summary>
        static double[] BuildQuantiles()
        {
            var v = new List<double>(1 << 18);
            for (int z = 0; z < 512; z++) for (int x = 0; x < 512; x++) v.Add(OreRules.BlockNoise(12345, x, z, 16, 77));
            v.Sort();
            var q = new double[257];
            for (int i = 0; i <= 256; i++) q[i] = v[Math.Min(v.Count - 1, (int)((long)i * (v.Count - 1) / 256))];
            q[0] = double.NegativeInfinity; q[256] = double.PositiveInfinity;
            return q;
        }
        static double NoiseThreshold(double cover)
        {
            if (cover >= 1) return double.PositiveInfinity;
            double f = cover * 256; int i = (int)Math.Floor(f);
            if (i >= 255) return NoiseQuantiles[255] + (NoiseQuantiles[255] - NoiseQuantiles[254]) * (f - 255);
            double lo = i == 0 ? NoiseQuantiles[1] - (NoiseQuantiles[2] - NoiseQuantiles[1]) : NoiseQuantiles[i];
            return lo + (NoiseQuantiles[i + 1] - lo) * (f - i);
        }

        AreaPlan PlanArea(OreDepositCatalog.Area a, int i)
        {
            var p = new AreaPlan { A = a, Index = i, Salt = RegionalOreCatalog.StableCode(a.Id) };
            p.Basin = a.Beds.Any(b => IsBasinCode(b.Ore.Code));
            foreach (var bed in a.Beds) if (bed.Ore.Metal != null) bed.Ore.Band = Catalog.BandFor(bed.Ore.Metal, bed.Unit);
            p.Rings = a.Rings.Select(r =>
            {
                var o = new double[r.Length];
                double x0 = Scale.XOf(r[0]), span = Scale.RingLength - Scale.Gutter;
                for (int k = 0; k < r.Length; k += 2)
                {
                    double x = Scale.XOf(r[k]);
                    // keep a ring that crosses the world's seam in one piece (unwrapped next to its first point)
                    if (x - x0 > span / 2) x -= span; else if (x0 - x > span / 2) x += span;
                    o[k] = x; o[k + 1] = Scale.ZOf(r[k + 1]);
                }
                return o;
            }).ToArray();
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var r in p.Rings) for (int k = 0; k < r.Length; k += 2) { minX = Math.Min(minX, r[k]); maxX = Math.Max(maxX, r[k]); minZ = Math.Min(minZ, r[k + 1]); maxZ = Math.Max(maxZ, r[k + 1]); }
            p.MinX = (int)Math.Floor(minX) - 1; p.MaxX = (int)Math.Ceiling(maxX) + 1; p.MinZ = (int)Math.Floor(minZ) - 1; p.MaxZ = (int)Math.Ceiling(maxZ) + 1;
            // (CR-O9) a ring unwrapped across the seam lies partly outside the usable span: the area is left out
            p.OffRing = minX < RingWest || maxX > RingEast;
            // patches about 3 km across, at least 8 blocks
            p.PatchCell = Math.Max(8, (int)Math.Round(3000 / Scale.Hz));
            p.Threshold = a.Beds.Select(b => NoiseThreshold(b.Cover)).ToArray();
            return p;
        }
        /// <summary>Coal and evaporites: inside a catalogue basin (or a salt dome) they take any sedimentary bed (0065 §4).</summary>
        public static bool IsBasinCode(string code) => code is "lignite" or "bituminouscoal" or "anthracite" or "halite" or "sylvite" or "earth-gypsum" or "borax" or "kernite" or "sulfur";

        /// <summary>The x spans (world blocks, [x0, x1)) of an area along the block row z (centre z + .5), even-odd over its rings.</summary>
        public void Row(AreaPlan a, int z, List<double> xs)
        {
            xs.Clear();
            double zc = z + .5;
            foreach (var r in a.Rings)
            {
                int n = r.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = r[2 * i], zi = r[2 * i + 1], xj = r[2 * j], zj = r[2 * j + 1];
                    if ((zi > zc) == (zj > zc)) continue;
                    xs.Add(xi + (zc - zi) / (zj - zi) * (xj - xi));
                }
            }
            xs.Sort();
        }
        public static bool Inside(List<double> xs, double xc)
        {
            int n = 0;
            foreach (double v in xs) { if (v > xc) break; n++; }
            return (n & 1) == 1;
        }

        /// <summary>Real block range of a bed at a column: first block and blocks (stochastically rounded, expected value kept).</summary>
        public void BedBlocks(AreaPlan a, int k, int x, int z, out int first, out int count, out double expected)
        {
            var bed = a.A.Beds[k];
            double t = Scale.Curve.Blocks(bed.TopM), s = Scale.Curve.Blocks(bed.TopM + bed.ThickM);
            expected = (s - t) * Rate;
            first = (int)Math.Floor(t);
            count = (int)Math.Floor(expected);
            if (Roll(x, k, z, a.Salt ^ 0x3c1) < expected - count) count++;
        }
        /// <summary>Whether a bed is present at a column (its patches cover its share of the area).</summary>
        public bool BedPresent(AreaPlan a, int k, int x, int z)
            => a.Threshold[k] == double.PositiveInfinity || OreRules.BlockNoise(Seed, x, z, a.PatchCell, a.Salt + 31 * k) < a.Threshold[k];

        /// <summary>
        /// Places an area's beds in one column inside it. A bed starts at its real depth; where the column has no allowed host
        /// there it moves to the nearest allowed depth within half its depth (at least 8 blocks), down first, and keeps its
        /// thickness (blocks it cannot use are lost). Returns the blocks placed, or the expected blocks without <paramref name="place"/>.
        /// </summary>
        public double PlaceArea(AreaPlan a, int x, int z, IOreColumn col, bool place = true, string only = null)
        {
            double total = 0;
            var where = new OreWhere(a.Basin, 0);
            int top = col.Top, floor = Math.Max(1, col.Floor);
            for (int k = 0; k < a.A.Beds.Length; k++)
            {
                var bed = a.A.Beds[k];
                if (only != null && bed.Ore.Code != only || place && bed.Ore.Slot < 0) continue;
                if (!BedPresent(a, k, x, z)) continue;
                BedBlocks(a, k, x, z, out int first, out int count, out double expected);
                if (count <= 0 && place) continue;
                int start = Snap(col, bed.Ore, first, Math.Max(8, first / 2), where);
                if (start < 0) continue;
                if (!place)
                {
                    int ok = 0, n = Math.Max(1, (int)Math.Ceiling(expected));
                    for (int i = 0; i < n; i++) if (top - (start + i) > floor && col.Allows(bed.Ore, start + i, where)) ok++;
                    total += expected * ok / n;
                    continue;
                }
                var band = bed.Ore.Band ?? Catalog.BandFor(a.A.Layer, bed.Unit);
                for (int i = 0; i < count; i++)
                {
                    int d = start + i, y = top - d;
                    if (y <= floor) break;
                    if (!col.Allows(bed.Ore, d, where)) continue;
                    int tier = band != null && double.IsFinite(bed.Grade) ? OreDepositCatalog.Tier(band, bed.Grade) : 1;
                    if (col.Place(bed.Ore, d, tier, Roll(x, y, z, a.Salt + 14), where)) total++;
                }
            }
            return total;
        }
        static int Snap(IOreColumn col, OreDepositCatalog.Ore ore, int d, int window, OreWhere where, bool downOnly = false)
        {
            int floor = Math.Max(1, col.Floor);
            for (int k = 0; k <= window; k++)
            {
                int down = d + k, up = d - k;
                if (col.Top - down > floor && col.Allows(ore, down, where)) return down;
                if (!downOnly && k > 0 && up >= 0 && col.Allows(ore, up, where)) return up;
            }
            return -1;
        }

        /// <summary>Areas: expected blocks (real volume at the world's scale) over the whole area, by sampling its columns.</summary>
        public double AreaExpected(AreaPlan a)
        {
            double sum = 0;
            var xs = new List<double>();
            for (int z = a.MinZ; z <= a.MaxZ; z++)
            {
                Row(a, z, xs);
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int x0 = (int)Math.Ceiling(xs[i] - .5), x1 = (int)Math.Ceiling(xs[i + 1] - .5) - 1;
                    for (int k = 0; k < a.A.Beds.Length; k++)
                        sum += Math.Max(0, x1 - x0 + 1) * a.A.Beds[k].Cover * (Scale.Curve.Blocks(a.A.Beds[k].TopM + a.A.Beds[k].ThickM) - Scale.Curve.Blocks(a.A.Beds[k].TopM)) * Rate;
                }
            }
            return sum;
        }
    }

    /// <summary>Rules v4's background (decision 0065 §5), pure so the tests can check that older rules are untouched.</summary>
    public static class OreBackground
    {
        /// <summary>Ore rules saved in new worlds (EarthParams.OreRules): the version that changes generation (0 in older worlds,
        /// which run rules v3). Not RegionalOreCatalog.Version, the fix level saved as EarthParams.OreRulesVersion, though the
        /// comments and logs number both "v3" and "v4" in one series.</summary>
        public const int CatalogueRules = 4;
        /// <summary>Ore rules new worlds are made with. 5 (EA-0058): a placer or other cover blanket whose footprint has none of its
        /// layers moves to the nearest place that has them (<see cref="OreDepositPlan.ValleySearch"/>); otherwise as 4.</summary>
        public const int CurrentRules = 5;
        /// <summary>The first ore rules with the blankets' valley search (EA-0058), and in which a frozen loose block (permafrost
        /// sand or gravel) takes a deposit as the block it froze from would.</summary>
        public const int ValleySearchRules = 5;

        // (ER-10, code review 14 November 2026) What each ore rules version turns on, as named facts: every test of the saved ore
        // rules (EarthParams.OreRules, OreSession.Rules) goes through these two, and OreDepositsTests pins their first versions.
        // The ore layers' own versions are checked where their files are read (OreLayers). One strategy object per session in
        // place of the generator's flags waits for ore rules 6 (the review's W13).

        /// <summary>Ore rules 4 (decision 0065): the catalogue's deposits at their real size in their layers, background attempts
        /// by heat maps v2 at the heat that raises them (the Ores tab's choice of Earth's ore maps on ground columns).</summary>
        public static bool CatalogueBodies(int rules) => rules >= CatalogueRules;
        /// <summary>Ore rules 5 (EA-0058): a cover blanket off its layers moves to the nearest place that has them; a frozen loose
        /// block takes a deposit as the block it froze from would.</summary>
        public static bool ValleySearch(int rules) => rules >= ValleySearchRules;
        /// <summary>The heat that raises a background deposit above its class floor. Rules v4: none for the resources the
        /// catalogue places (their real deposits are the catalogue's, at their real size; a hotspot boost would count them
        /// twice and smear them over a 0.5° cell), the heat for quartz veins, clay, peat, bog iron and salt flats. Before v4:
        /// the heat. The heat never takes the floor away: no record is not no ore (the author, 8 November: "Lack of finding
        /// does not mean lack of resource"), so the background is the configurable class floor in its host rock and layer
        /// everywhere, without the surveys' bias towards well-mapped countries.</summary>
        public static double Evidence(int rules, string resource, OreClass cls, double heat, string deposit = null)
            => !CatalogueBodies(rules) || Uncatalogued(resource, cls, deposit) ? heat : 0;
        static bool Uncatalogued(string resource, OreClass cls, string deposit)
            => cls == OreClass.Veins || cls == OreClass.Soil && (resource is "clay" or "peat" || deposit is "earth-bogiron" or "earth-saltflat");
        /// <summary>Rules v4: a background deposit's radius scale at the world's scale (relative to 1:100), capped so the radius
        /// stays within <paramref name="cap"/> blocks and at least half a block; 1 before v4.</summary>
        public static double RadiusScale(int rules, double metresPerBlock, double nativeRadius, double cap = 64)
        {
            if (!CatalogueBodies(rules) || !(metresPerBlock > 0)) return 1;
            double r = OreScale.ReferenceMetresPerBlock / metresPerBlock;
            // the 1:100 play style (100.2 m per block north-south) keeps today's calibration exactly
            if (Math.Abs(r - 1) < .02) return 1;
            if (nativeRadius > 0) r = Math.Clamp(r, .5 / nativeRadius, cap / nativeRadius);
            return r;
        }
    }

    /// <summary>
    /// Earth's own ore and gem deposits (since 7 November 2026): ordinary deposit variants under either ore choice. Vanilla
    /// reads deposit files sorted by asset key, and earth: sorts before game:, so they come first and draw from the per-chunk
    /// random stream before every other deposit: loaded into an older world they would move all its deposits in new chunks
    /// (and the v1 heat maps would bind the new ores there). A world's set (EarthParams.DepositSet) says whether they load;
    /// the game side removes them from the deposit list where it is 0 (EarthOreLayers.PruneDeposits). A new Earth deposit
    /// file needs a new set version (the tests check that every file is listed).
    /// </summary>
    public static class EarthDepositSet
    {
        /// <summary>Saved in new worlds: Earth's ore and gem deposits load.</summary>
        public const int Version = 1;
        /// <summary>The deposit files of set 1 (EarthOreLayers' layer deposits have their own rule).</summary>
        public static readonly string[] Files = { "earth:worldgen/deposits/earth-gems.json", "earth:worldgen/deposits/earth-ores.json" };

        /// <summary>
        /// The set a world uses: the saved one, else the set for an RC or dev world saved before the field. Ore layers
        /// (0238684), ore rules v4 (18a20f4) and the water table rules (75c16c6, set in every new world) all came after these
        /// deposits on 7 and 8 November (e96456e), so a world with any of them generated with them; anything else is older
        /// (or not an Earth world) and gets 0.
        /// </summary>
        public static int Of(int saved, int oreLayers, int oreRules, int waterTableRules)
            => saved > 0 ? saved : oreLayers > 0 || OreBackground.CatalogueBodies(oreRules) || waterTableRules > 0 ? Version : 0;

        /// <summary>Whether a deposit variant read from <paramref name="fromFile"/> stays in a world of set <paramref name="set"/>.</summary>
        public static bool Keeps(string fromFile, int set) => set >= Version || Array.IndexOf(Files, fromFile) < 0;
    }
}
