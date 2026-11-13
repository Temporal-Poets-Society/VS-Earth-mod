using System.Globalization;
using System.IO.Compression;
using EarthMap.Core;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length != 2) throw new ArgumentException("Usage: RawElevationSurvey <ETOPO 30s tif> <output.csv>");
using var raster = new RawEtpo(args[0]);
var vertical = new VerticalScale(512, 110, 510, 1000, 47, 20);
var sites = new (string Name, double Lat, double Lon, double Target)[] {
    ("Everest",27.9881,86.9250,8849), ("Lhotse",27.9617,86.9330,8516), ("Makalu",27.8897,87.0883,8485),
    ("K2",35.8808,76.5158,8611), ("Kangchenjunga",27.7025,88.1475,8586), ("Cho Oyu",28.0942,86.6608,8188),
    ("Nanga Parbat",35.2372,74.5892,8126), ("Mont Blanc",45.8326,6.8652,4808), ("Matterhorn",45.9764,7.6586,4478),
    ("Elbrus",43.3550,42.4392,5642), ("Fuji",35.3606,138.7274,3776), ("Kilimanjaro",-3.0674,37.3556,5895),
    ("Denali",63.0695,-151.0074,6190), ("Aconcagua",-32.6532,-70.0109,6961), ("Chimborazo",-1.4692,-78.8175,6263)
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
using var output = new StreamWriter(args[1]);
output.WriteLine("peak,latitude,longitude,referenceMetres,targetBlock,rawMaxMetres_002deg,rawMaxBlock_002deg,rawMaxMetres_01deg,rawMaxBlock_01deg,rawMaxLatitude,rawMaxLongitude");
foreach (var site in sites)
{
    var near = raster.Maximum(site.Lat, site.Lon, .02);
    var wide = raster.Maximum(site.Lat, site.Lon, .1);
    int target = (int)Math.Floor(vertical.Y(site.Target) + 1e-9);
    int rawBlock = (int)Math.Floor(vertical.Y(near.Height));
    output.WriteLine($"{site.Name},{site.Lat},{site.Lon},{site.Target},{target},{near.Height:F3},{rawBlock},{wide.Height:F3},{(int)Math.Floor(vertical.Y(wide.Height))},{near.Lat:F7},{near.Lon:F7}");
    Console.WriteLine($"{site.Name}: reference {site.Target:F0} m / Y{target}; raw 30\" max {near.Height:F3} m / Y{rawBlock}; 0.1-degree upper window {wide.Height:F3} m");
}

// Minimal read-only decoder for the existing NOAA float32, tiled, Deflate TIFF with floating-point predictor 3.
// It refuses other TIFF layouts; no general image library, package install or source-data rewrite is needed.
sealed class RawEtpo : IDisposable
{
    readonly FileStream file;
    readonly BinaryReader reader;
    readonly int width, height, tile;
    readonly uint[] offsets, lengths;
    readonly Dictionary<int, float[]> cache = new();

    public RawEtpo(string path)
    {
        file = File.OpenRead(path); reader = new BinaryReader(file);
        if (reader.ReadUInt16() != 0x4949 || reader.ReadUInt16() != 42) throw new InvalidDataException("Expected little-endian classic TIFF");
        file.Position = reader.ReadUInt32();
        int count = reader.ReadUInt16();
        var tags = new Dictionary<ushort, (ushort Type, uint Count, uint Value)>();
        for (int i = 0; i < count; i++) tags.Add(reader.ReadUInt16(), (reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadUInt32()));
        width = (int)tags[256].Value; height = (int)tags[257].Value; tile = (int)tags[322].Value;
        if (width != 43200 || height != 21600 || tags[258].Value != 32 || tags[259].Value != 8 || tags[317].Value != 3
            || tags[339].Value != 3 || tags[323].Value != tile || tile != 256 || tags[277].Value != 1)
            throw new InvalidDataException("Unexpected ETOPO TIFF layout");
        uint[] Values(ushort tag)
        {
            var t = tags[tag]; if (t.Type != 4) throw new InvalidDataException("Expected uint32 tile index");
            file.Position = t.Value; var values = new uint[t.Count];
            for (int i = 0; i < values.Length; i++) values[i] = reader.ReadUInt32();
            return values;
        }
        offsets = Values(324); lengths = Values(325);
        Console.WriteLine($"Raw ETOPO: {width}x{height}, {tile}x{tile} tiles, Deflate/predictor 3, {file.Length} bytes");
    }

    float[] Tile(int key)
    {
        if (cache.TryGetValue(key, out var pixels)) return pixels;
        file.Position = offsets[key]; byte[] compressed = reader.ReadBytes((int)lengths[key]);
        var raw = new byte[tile * tile * 4];
        using (var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress)) z.ReadExactly(raw);
        pixels = new float[tile * tile];
        var row = new byte[tile * 4];
        for (int y = 0; y < tile; y++)
        {
            int start = y * tile * 4;
            for (int b = 1; b < tile * 4; b++) raw[start + b] = unchecked((byte)(raw[start + b] + raw[start + b - 1]));
            for (int x = 0; x < tile; x++) for (int b = 0; b < 4; b++) row[x * 4 + b] = raw[start + (3 - b) * tile + x];
            for (int x = 0; x < tile; x++) pixels[y * tile + x] = BitConverter.ToSingle(row, x * 4);
        }
        cache[key] = pixels;
        return pixels;
    }

    public (double Height, double Lat, double Lon) Maximum(double lat, double lon, double radius)
    {
        int r0 = Math.Max(0, (int)Math.Floor((90 - lat - radius) * 120));
        int r1 = Math.Min(height - 1, (int)Math.Ceiling((90 - lat + radius) * 120));
        int c0 = Math.Max(0, (int)Math.Floor((lon - radius + 180) * 120));
        int c1 = Math.Min(width - 1, (int)Math.Ceiling((lon + radius + 180) * 120));
        double max = double.NegativeInfinity, bestLat = 0, bestLon = 0;
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
        {
            double value = Tile(r / tile * ((width + tile - 1) / tile) + c / tile)[r % tile * tile + c % tile];
            if (!double.IsFinite(value) || value < -12000 || value > 9000) throw new InvalidDataException("Decoded elevation out of range");
            if (value > max) { max = value; bestLat = 90 - (r + .5) / 120; bestLon = -180 + (c + .5) / 120; }
        }
        return (max, bestLat, bestLon);
    }

    public void Dispose() { reader.Dispose(); file.Dispose(); }
}
