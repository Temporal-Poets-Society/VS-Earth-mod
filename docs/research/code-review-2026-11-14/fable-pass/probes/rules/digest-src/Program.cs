using System.Globalization;
using System.Reflection;
using System.Text;
using EarthMap;
using EarthMap.Core;

// Probe (code review, saved worlds): the same saved EarthParams through two builds of src/Core, every TerrainModel.Column
// field hashed over fixed places. "make" writes a new world's params from the build's own constants; "hash" loads a params
// file (fields this build has that the file lacks stay 0, as when an older save is read) and prints one hash per field.
static partial class Digest
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

    static EarthParams Load(string file)
    {
        var p = new EarthParams();
        foreach (string line in File.ReadAllLines(file))
        {
            int eq = line.IndexOf('='); if (eq < 0) continue;
            string name = line[..eq], rest = line[(eq + 1)..]; char t = rest[0]; string val = rest[2..];
            var f = typeof(EarthParams).GetField(name) ?? throw new Exception("this build has no EarthParams." + name);
            object v;
            if (t == 'd') v = BitConverter.Int64BitsToDouble(Convert.ToInt64(val[..16], 16));
            else if (t == 'i') v = int.Parse(val, CultureInfo.InvariantCulture);
            else if (t == 'l') v = long.Parse(val, CultureInfo.InvariantCulture);
            else if (t == 'b') v = bool.Parse(val);
            else if (t == 's') v = val;
            else throw new Exception("type " + t);
            f.SetValue(p, v);
        }
        return p;
    }

    sealed class Fnv { public ulong H = 14695981039346656037UL; public void Add(ulong v) { for (int i = 0; i < 8; i++) { H ^= (byte)(v >> (8 * i)); H *= 1099511628211UL; } } }

    static ulong Bits(object v)
    {
        if (v is double d) return (ulong)BitConverter.DoubleToInt64Bits(d);
        if (v is float f) return (uint)BitConverter.SingleToInt32Bits(f);
        if (v is int i) return (ulong)(long)i;
        if (v is long l) return (ulong)l;
        if (v is bool b) return b ? 1UL : 0UL;
        if (v is byte y) return y;
        if (v is sbyte sy) return (ulong)(long)sy;
        if (v is short sh) return (ulong)(long)sh;
        if (v is ushort us) return us;
        if (v is uint ui) return ui;
        return (ulong)(v == null ? 0 : v.GetHashCode());
    }

    static partial void MakeParams(EarthData data, ref EarthParams p);
    static partial void BuildAndAttach(EarthData data, EarthParams p, string store, string tiles, ref TerrainModel model);
    static partial void LoadData(string root, string store, ref EarthData data);

    static int Main(string[] a)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = a[0], root = a[1], file = a[2], outFile = a.Length > 3 && a[3] != "-" ? a[3] : null, store = a.Length > 4 ? a[4] : null, tiles = a.Length > 5 ? a[5] : null;
        if (mode == "squares")
        {
            // the 1° squares the place patches touch (for copying tiles)
            var set = new SortedSet<string>();
            foreach (var pl in Places)
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                {
                    int la = (int)Math.Floor(pl.Lat) + dj, lo = (int)Math.Floor(pl.Lon) + di; if (lo < -180) lo += 360; if (lo > 179) lo -= 360;
                    set.Add((la >= 0 ? "N" : "S") + Math.Abs(la).ToString("00") + (lo >= 0 ? "E" : "W") + Math.Abs(lo).ToString("000"));
                }
            File.WriteAllLines(file, set);
            return 0;
        }
        EarthData data = null;
        LoadData(root, store, ref data);
        data ??= EarthData.LoadFromFolder(root, true);
        if (mode == "make")
        {
            EarthParams made = null;
            MakeParams(data, ref made);
            if (made == null) { Console.WriteLine("this build makes no params"); return 2; }
            Dump(made, file);
            Console.WriteLine("params written to " + file);
            return 0;
        }
        var p = Load(file);
        TerrainModel model = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BuildAndAttach(data, p, store, tiles, ref model);
        model ??= p.BuildModel(data);
        Console.WriteLine("model built in " + sw.ElapsedMilliseconds + " ms; rivers " + (model.Rivers != null ? model.Rivers.SegmentCount : 0) + " segments");
        var fields = typeof(TerrainModel.Column).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var hashes = fields.Select(_ => new Fnv()).ToArray();
        var climate = new Fnv();
        var pixel = typeof(TerrainModel).GetMethod("ClimatePixel", new[] { typeof(double), typeof(double), typeof(int), typeof(float), typeof(float) });
        var csFields = typeof(EarthData.ClimateSample).GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        var csHashes = csFields.Select(_ => new Fnv()).ToArray();
        BinaryWriter dump = outFile != null ? new BinaryWriter(File.Create(outFile)) : null;
        long n = 0;
        var perPlace = new StringBuilder();
        void Column(double x, double z, Fnv place)
        {
            var col = model.GetColumn(x, z);
            object c = col;
            for (int i = 0; i < fields.Length; i++)
            {
                ulong b = Bits(fields[i].GetValue(c)); hashes[i].Add(b);
                string fn = fields[i].Name;
                if (place != null && (fn == "Top" || fn == "WaterTop" || fn == "Water" || fn == "River" || fn == "IceBottom")) place.Add(b);
            }
            if (dump != null) { dump.Write(col.Top); dump.Write(col.WaterTop); dump.Write((int)col.Water | (col.River ? 256 : 0) | (col.Beach ? 512 : 0)); }
            if ((n & 7) == 0)
            {
                if (pixel != null) climate.Add((ulong)(long)(int)pixel.Invoke(model, new object[] { x, z, 0, 1f, 1f }));
                var g = model.Geometry.ToGeo(x, z);
                object cs = data.SampleClimate(g.Lat, g.Lon);
                for (int i = 0; i < csFields.Length; i++) csHashes[i].Add(Bits(csFields[i].GetValue(cs)));
            }
            n++;
        }
        int patch = int.Parse(Environment.GetEnvironmentVariable("PATCH") ?? "128");
        foreach (var pl in Places)
        {
            model.Geometry.FromGeo(pl.Lat, pl.Lon, out double px, out double pz);
            long x0 = (long)Math.Floor(px) - patch / 2, z0 = (long)Math.Floor(pz) - patch / 2;
            var h = new Fnv();
            for (int j = 0; j < patch; j++) for (int i = 0; i < patch; i++) Column(x0 + i, z0 + j, h);
            perPlace.Append("place " + pl.Name.PadRight(18) + " " + h.H.ToString("X16") + "\n");
        }
        if (Environment.GetEnvironmentVariable("RANDOM") != "0")
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
            perPlace.Append("place " + "random segments".PadRight(18) + " " + h.H.ToString("X16") + "\n");
        }
        dump?.Dispose();
        Console.WriteLine("columns " + n + " in " + sw.ElapsedMilliseconds + " ms");
        for (int i = 0; i < fields.Length; i++) Console.WriteLine("field " + fields[i].Name.PadRight(18) + " " + hashes[i].H.ToString("X16"));
        Console.WriteLine("climatepixel " + (pixel != null ? climate.H.ToString("X16") : "n/a"));
        for (int i = 0; i < csFields.Length; i++) Console.WriteLine("climate " + csFields[i].Name.PadRight(16) + " " + csHashes[i].H.ToString("X16"));
        Console.Write(perPlace);
        return 0;
    }
}
