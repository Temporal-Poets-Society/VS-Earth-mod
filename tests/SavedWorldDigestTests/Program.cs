using System.Globalization;
using System.Reflection;
using System.Text;
using EarthMap;
using EarthMap.Core;

// EA-0103 (Fable pass FP-10, code review 14 November 2026): the terrain of a world saved by a published Earth must never
// change. Each worlds\<name>.params.txt is a world's saved EarthParams as that release made it (a new 1:100 world with the
// default Earth tab, from the release's own constants); worlds\<name>.hash.txt pins one hash per TerrainModel.Column field,
// for the climate pixel and the climate sample, and per place, over 35 places of 128 x 128 columns (and 500 random rows
// on the built-in heights)
// (adapted from the review's probe, docs\research\code-review-2026-11-14\fable-pass\probes\rules\digest-src). A
// difference is a change to old worlds' terrain: fix it, or put the change behind a rules version for new worlds.
//
// The built-in-heights world (v1.0.2) needs only earthdata\; the GLO-90 worlds need the data store and its GLO-90 tiles
// (EARTH_STORE, else the player's or the AgentBridge bench's ModData\earth) and are skipped without them (exit 3).
// The hashes were taken on Windows x64 with the game's .NET 10: the terrain uses Math.Log, Exp and Pow, whose last bits
// may differ on another platform (code review T-L11). Build with -p:VintageStoryPath (the game's API and protobuf-net).
//
// Usage:
//   dotnet run -c Release --project tests\SavedWorldDigestTests -- <repo root> [name ...]   check (all worlds, or those named)
//   ... -- <repo root> --make <name>      write worlds\<name>.params.txt for a new world of this build (at a release)
//   ... -- <repo root> --write <name>     write worlds\<name>.hash.txt from this build (at a release, after --make)
static class Digest
{
    static readonly (string Name, double Lat, double Lon)[] Places =
    {
        ("Daugava mouth",57.05,24.03),("Rotterdam",51.95,4.10),("Damietta",31.50,31.80),("Rhone mouth",43.35,4.80),("Bergen",60.39,5.30),
        ("Lake Geneva",46.40,6.50),("Karkheh",31.50,48.30),("Niger inland",16.70,-3.00),("Colorado",36.10,-113.30),("Matterhorn",45.976,7.658),
        ("Venice",45.40,12.40),("Lena",64.00,126.00),("Amazon mouth",-0.50,-50.50),("Mississippi delta",29.20,-89.30),("Dead Sea",31.50,35.50),
        ("Aral",45.00,60.00),("Tigris",33.30,44.40),("Santorini",36.40,25.40),("Etna",37.75,15.00),("Panama",9.10,-79.70),("Suez",30.50,32.35),
        ("Bosporus",41.10,29.05),("Messina",38.20,15.60),("IJsselmeer",52.70,5.30),("Riga",56.95,24.10),("Okavango",-19.30,22.90),
        ("Lake Victoria",-0.40,32.60),("Ganges delta",22.00,90.00),("Bering seam",64.00,-169.00),("Fuji",35.36,138.73),("Open ocean",0.0,-140.0),
        ("Niagara",43.08,-79.07),("Antarctic coast",-70.0,10.0),("Mekong",10.5,105.5),("Caspian north",46.5,49.5),
    };
    const int Patch = 128;

    static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
        string worlds = Path.Combine(root, "tests", "SavedWorldDigestTests", "worlds");
        string store = FindStore();
        if (args.Length > 2 && args[1] == "--make") { Make(root, store, Path.Combine(worlds, args[2] + ".params.txt")); return 0; }
        if (args.Length > 2 && args[1] == "--write")
        {
            var lines = Hash(root, store, LoadParams(Path.Combine(worlds, args[2] + ".params.txt")), out string skip);
            if (lines == null) { Console.WriteLine("cannot write: " + skip); return 2; }
            File.WriteAllLines(Path.Combine(worlds, args[2] + ".hash.txt"), lines);
            Console.WriteLine("wrote " + args[2] + ".hash.txt (" + lines.Count + " lines)");
            return 0;
        }
        var names = args.Length > 1 ? args.Skip(1).ToArray()
            : Directory.GetFiles(worlds, "*.params.txt").Select(f => Path.GetFileName(f)[..^".params.txt".Length]).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        int failures = 0, checkedWorlds = 0, skipped = 0;
        string skipWhy = null;
        Console.WriteLine("data store: " + (store ?? "none (GLO-90 worlds are skipped)"));
        foreach (string name in names)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var p = LoadParams(Path.Combine(worlds, name + ".params.txt"));
            var got = Hash(root, store, p, out string skip);
            if (got == null) { Console.WriteLine($"skip {name}: {skip}"); skipped++; skipWhy = skip; continue; }
            var want = File.ReadAllLines(Path.Combine(worlds, name + ".hash.txt")).Where(l => l.Length > 0).ToList();
            var gotByKey = got.ToDictionary(Key, l => l);
            int bad = 0;
            foreach (string w in want)
            {
                if (!gotByKey.TryGetValue(Key(w), out string g)) { Console.WriteLine($"FAIL {name}: {Key(w)} is gone from this build"); bad++; continue; }
                if (g != w) { Console.WriteLine($"FAIL {name}: {Key(w)}: {Value(w)} pinned, {Value(g)} now"); bad++; }
            }
            var known = want.Select(Key).ToHashSet();
            foreach (string g in got.Where(g => !known.Contains(Key(g)))) Console.WriteLine($"  {name}: {Key(g)} is new in this build ({Value(g)}; not pinned)");
            checkedWorlds++;
            Console.WriteLine((bad == 0 ? "ok   " : "FAIL ") + $"{name}: {want.Count} pinned hashes {(bad == 0 ? "all equal" : bad + " differ")} ({sw.Elapsed.TotalSeconds:0} s)");
            if (bad > 0) failures++;
        }
        // exit 3: what was checked passed, but some worlds were skipped (Run-All shows it as a partial pass)
        if (failures > 0) { Console.WriteLine($"{failures} FAILED: an old world's terrain changed ({checkedWorlds} checked, {skipped} skipped)"); return 1; }
        if (skipped > 0) { Console.WriteLine($"PASS for {checkedWorlds} saved worlds, {skipped} skipped: {skipWhy}"); return 3; }
        Console.WriteLine($"ALL PASS ({checkedWorlds} saved worlds' terrain unchanged)");
        return 0;
    }

    static string Key(string line) { int i = line.LastIndexOf(' '); return line[..i].TrimEnd(); }
    static string Value(string line) => line[(line.LastIndexOf(' ') + 1)..];

    static string FindStore()
    {
        string env = Environment.GetEnvironmentVariable("EARTH_STORE");
        if (env == "none") return null;   // checks the built-in-heights worlds only
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (string s in new[] { env, Path.Combine(appData, "VintagestoryData", "ModData", "earth"), Path.Combine(appData, "VintagestoryData-agentbridge", "ModData", "earth") })
            if (!string.IsNullOrEmpty(s) && Directory.Exists(Path.Combine(s, "data")) && Directory.Exists(Path.Combine(s, "glo90"))) return s;
        return null;
    }

    // ------------------------------------------------------------------ params files

    static void Dump(EarthParams p, string file)
    {
        var sb = new StringBuilder();
        foreach (var f in typeof(EarthParams).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (f.GetCustomAttributes().All(a => a.GetType().Name != "ProtoMemberAttribute")) continue;
            object v = f.GetValue(p);
            if (v is double d) sb.Append(f.Name).Append("=d:").Append(BitConverter.DoubleToInt64Bits(d).ToString("X16")).Append(" (").Append(d.ToString("R", CultureInfo.InvariantCulture)).Append(")\n");
            else if (v is int or long or bool) sb.Append(f.Name).Append('=').Append(v is bool ? "b" : v is int ? "i" : "l").Append(':').Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');
            else if (v is string s) sb.Append(f.Name).Append("=s:").Append(s).Append('\n');
        }
        File.WriteAllText(file, sb.ToString());
    }

    /// <summary>A params file as an older save reads: fields the file lacks stay 0.</summary>
    static EarthParams LoadParams(string file)
    {
        var p = new EarthParams();
        foreach (string line in File.ReadAllLines(file))
        {
            int eq = line.IndexOf('='); if (eq < 0) continue;
            string name = line[..eq], rest = line[(eq + 1)..]; char t = rest[0]; string val = rest[2..];
            var f = typeof(EarthParams).GetField(name) ?? throw new Exception("this build has no EarthParams." + name + " (a saved field must never go)");
            object v = t switch
            {
                'd' => BitConverter.Int64BitsToDouble(Convert.ToInt64(val[..16], 16)),
                'i' => int.Parse(val, CultureInfo.InvariantCulture),
                'l' => long.Parse(val, CultureInfo.InvariantCulture),
                'b' => bool.Parse(val),
                's' => val,
                _ => throw new Exception("type " + t),
            };
            f.SetValue(p, v);
        }
        return p;
    }

    // ------------------------------------------------------------------ the model as a server builds it

    static EarthData LoadData(string root, string store) => store == null ? EarthData.LoadFromFolder(root, true) : EarthData.Load(name =>
    {
        string a = Path.Combine(root, "earthdata", name);
        if (File.Exists(a)) return File.OpenRead(a);
        string b = Path.Combine(store, "data", name);
        return File.Exists(b) ? File.OpenRead(b) : null;
    }, true);

    /// <summary>EarthMapSystem's model build and EarthElevationSystem.AttachServer, offline (the review's probe).</summary>
    static TerrainModel Build(string root, string store, EarthData data, EarthParams p)
    {
        if (p.FineElevation < 1) return p.BuildModel(data);
        string g = Path.Combine(store, "glo90");
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(File.ReadAllBytes(Path.Combine(g, DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules))))) : null;
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(File.ReadAllBytes(Path.Combine(g, RiverFlowData.FileName))) : null;
        var model = p.BuildModel(data, dams, null, flow);
        var fine = new FineElevation(g, File.ReadAllBytes(Path.Combine(root, "data", "glo90-tiles.bin")));
        fine.SetFallback(Array.Empty<int>());
        model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
        model.Fine = fine;
        if (dams != null && p.DamCourseError == null) fine.Dams = dams;
        if (p.LakeOutlines >= 1)
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(g, LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion))))), model.Geometry);
        if (p.RiverOutlines >= 1)
        {
            int v = RiverOutlineData.Usable(p.RiverOutlines);
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(g, RiverOutlineData.FileName(v)))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
        }
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
        return model;
    }

    sealed class Fnv { public ulong H = 14695981039346656037UL; public void Add(ulong v) { for (int i = 0; i < 8; i++) { H ^= (byte)(v >> (8 * i)); H *= 1099511628211UL; } } }

    static ulong Bits(object v) => v switch
    {
        double d => (ulong)BitConverter.DoubleToInt64Bits(d),
        float f => (uint)BitConverter.SingleToInt32Bits(f),
        int i => (ulong)(long)i,
        long l => (ulong)l,
        bool b => b ? 1UL : 0UL,
        byte y => y,
        sbyte sy => (ulong)(long)sy,
        short sh => (ulong)(long)sh,
        ushort us => us,
        uint ui => ui,
        null => 0,
        _ => (ulong)v.GetHashCode(),
    };

    /// <summary>The digest lines (as the hash files hold them), or null with the reason when this machine lacks the data.</summary>
    static List<string> Hash(string root, string store, EarthParams p, out string skip)
    {
        skip = null;
        if (p.FineElevation >= 1 && store == null) { skip = "a GLO-90 world needs the data store (EARTH_STORE or ModData\\earth)"; return null; }
        var data = LoadData(root, store);
        var model = Build(root, store, data, p);
        var fields = typeof(TerrainModel.Column).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var hashes = fields.Select(_ => new Fnv()).ToArray();
        var climate = new Fnv();
        var pixel = typeof(TerrainModel).GetMethod("ClimatePixel", new[] { typeof(double), typeof(double), typeof(int), typeof(float), typeof(float) });
        var csFields = typeof(EarthData.ClimateSample).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var csHashes = csFields.Select(_ => new Fnv()).ToArray();
        long n = 0;
        var places = new List<string>();
        void Column(double x, double z, Fnv place)
        {
            object c = model.GetColumn(x, z);
            for (int i = 0; i < fields.Length; i++)
            {
                ulong b = Bits(fields[i].GetValue(c)); hashes[i].Add(b);
                string fn = fields[i].Name;
                if (fn is "Top" or "WaterTop" or "Water" or "River" or "IceBottom") place.Add(b);
            }
            if ((n & 7) == 0)
            {
                if (pixel != null) climate.Add((ulong)(long)(int)pixel.Invoke(model, new object[] { x, z, 0, 1f, 1f }));
                var g = model.Geometry.ToGeo(x, z);
                object cs = data.SampleClimate(g.Lat, g.Lon);
                for (int i = 0; i < csFields.Length; i++) csHashes[i].Add(Bits(csFields[i].GetValue(cs)));
            }
            n++;
        }
        foreach (var pl in Places)
        {
            model.Geometry.FromGeo(pl.Lat, pl.Lon, out double px, out double pz);
            long x0 = (long)Math.Floor(px) - Patch / 2, z0 = (long)Math.Floor(pz) - Patch / 2;
            var h = new Fnv();
            for (int j = 0; j < Patch; j++) for (int i = 0; i < Patch; i++) Column(x0 + i, z0 + j, h);
            places.Add("place " + pl.Name.PadRight(18) + " " + h.H.ToString("X16"));
        }
        // 500 random rows of 200 columns across the land, for the built-in heights only (the GLO-90 worlds read the tiles of
        // the 35 places alone, as the review pinned them: a random row there would read tiles all over the world)
        if (p.FineElevation < 1)
        {
            var rnd = new Random(20261114);
            var h = new Fnv();
            for (int s = 0; s < 500; s++)
            {
                double lat = -62 + 135 * rnd.NextDouble(), lon = -180 + 360 * rnd.NextDouble();
                model.Geometry.FromGeo(lat, lon, out double px, out double pz);
                long x0 = (long)Math.Floor(px), z0 = (long)Math.Floor(pz);
                for (int i = 0; i < 200; i++) Column(x0 + i, z0, h);
            }
            places.Add("place " + "random segments".PadRight(18) + " " + h.H.ToString("X16"));
        }
        var lines = new List<string>();
        for (int i = 0; i < fields.Length; i++) lines.Add("field " + fields[i].Name.PadRight(18) + " " + hashes[i].H.ToString("X16"));
        lines.Add("climatepixel " + (pixel != null ? climate.H.ToString("X16") : "n/a"));
        for (int i = 0; i < csFields.Length; i++) lines.Add("climate " + csFields[i].Name.PadRight(16) + " " + csHashes[i].H.ToString("X16"));
        lines.AddRange(places);
        return lines;
    }

    // ------------------------------------------------------------------ a new world of this build (at a release)

    /// <summary>A new 1:100 world of the "earth" play style (its Earth tab as worldconfig.json sets it: full GLO-90, the real
    /// seam, height 384), seed 386217, as this build's WorldParamsBuilder makes it (EA-0089 C; before, these lines copied
    /// ComputeParams and EarthWorldOptions.Freeze by hand and filled in only what the terrain model reads).</summary>
    static void Make(string root, string store, string file)
    {
        var data = LoadData(root, store);
        var wc = new Vintagestory.API.Datastructures.TreeAttribute();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "worldconfig.json"))))
            foreach (var kv in doc.RootElement.GetProperty("playStyles").EnumerateArray().First(s => s.GetProperty("code").GetString() == "earth").GetProperty("worldConfig").EnumerateObject())
                switch (kv.Value.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.True: case System.Text.Json.JsonValueKind.False: wc.SetBool(kv.Name, kv.Value.GetBoolean()); break;
                    case System.Text.Json.JsonValueKind.Number: wc.SetInt(kv.Name, kv.Value.GetInt32()); break;
                    default: wc.SetString(kv.Name, kv.Value.GetString()); break;
                }
        SpawnSearch.SizeFor("100", out int W, out int L, out _);
        const int H = 384;
        var w = new NewWorld
        {
            MapSizeX = W, MapSizeY = H, SeaLevel = EarthWorldOptions.Terrain(wc, H).SeaLevel(H, 165), Seed = 386217, WorldConfig = wc, HydrologyQuery = 3,
            // Cylinder World 1.1.0's ring for a 1:100 world (its log: jump lines at x=16384, seam band half-width 17056,
            // seabed y=70), checked against a real new world's saved params for 2.0.0
            CwJumpInset = (W - 2 * L) / 2, CwCircumference = 2 * L, CwSeamHalfWidth = (W - 2 * L) / 2 + 672,
            CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwSeabedY = 70, CylinderKeepsEarthSeam = () => true,
        };
        var log = DispatchProxy.Create<Vintagestory.API.Common.ILogger, QuietLog>();
        var p = WorldParamsBuilder.Build(w, new EarthMapConfig(), data, log, out _, out string bad);
        if (bad.Length > 0) throw new InvalidOperationException("this build's defaults can't make terrain: " + bad);
        Dump(p, file);
        Console.WriteLine("params written to " + file + " (check them against a real new world's earth-params before pinning)");
    }
}

/// <summary>An ILogger that says nothing (Make's builder logs only for a broken earth.json, which the defaults are not).</summary>
public class QuietLog : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
        => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
}
