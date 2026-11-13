using System.Security.Cryptography;
using System.Text;
using EarthMap.Core;

/// <summary>
/// Fixed world layouts and sample positions for the v1 golden-output check. The golden file was produced from the
/// baseline 0.12.2 code (commit db80766) before the v2 work; this file must stay unchanged so positions stay identical.
/// </summary>
sealed class GoldenWorld
{
    public string Name;
    public int W, L, MapHeight, Sea, RingStart, Ring, Count;
    public long Seed;
    public double Width;
    public TerrainModel Build(EarthData data)
    {
        var inp = new LayoutInput
        {
            WorldWidth = W,
            CwRingStart = RingStart, CwRingLength = Ring, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwBandInner = 608,
        };
        var geo = EarthLayout.Build(inp, data, out _);
        var vs = VerticalScale.ForWorld(MapHeight, Sea);
        var model = new TerrainModel(data, geo, vs, Seed) { GutterSeabedY = Sea - 40 };
        if (data.Rivers != null)
            model.Rivers = new RiverNetwork(data.Rivers, geo, 100, 1.5, model.RiverWaterTop, "ends", 20, Ring / 204800.0, 0.5, 2);
        return model;
    }
}

static class GoldenWorlds
{
    // The user's saves: Cylinder World 512000 x 204800, height 384, contact half-width 96; plus a small world.
    public static readonly GoldenWorld[] All =
    {
        new GoldenWorld { Name = "cyl512k-h384-s12345", W = 512000, L = 204800, MapHeight = 384, Sea = 165, RingStart = 51200, Ring = 409600, Seed = 12345, Width = 96, Count = 24000 },
        new GoldenWorld { Name = "cyl512k-h384-sneg", W = 512000, L = 204800, MapHeight = 384, Sea = 165, RingStart = 51200, Ring = 409600, Seed = -1234567, Width = 96, Count = 12000 },
        new GoldenWorld { Name = "cyl256k-h256-w48", W = 256000, L = 102400, MapHeight = 256, Sea = 110, RingStart = 25600, Ring = 204800, Seed = 424242, Width = 48, Count = 12000 },
    };
    public static readonly int[] Depths = { 0, 1, 3, 7, 12, 20, 33, 50, 75, 110, 150, 200, 260 };

    static ulong Mix(ulong v)
    {
        unchecked { v ^= v >> 30; v *= 0xbf58476d1ce4e5b9UL; v ^= v >> 27; v *= 0x94d049bb133111ebUL; v ^= v >> 31; return v; }
    }
    static double U(ulong a, ulong b) => (Mix(a * 0x9E3779B97F4A7C15UL ^ Mix(b + 0x632BE59BD9B4E019UL)) >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Deterministic positions; a third of them biased to land-rich latitudes 60S..75N.</summary>
    public static void Position(GoldenWorld w, int i, out int x, out int z)
    {
        ulong k = 1469598103934665603UL; // stable FNV-1a of the world name
        foreach (char c in w.Name) { k ^= c; k *= 1099511628211UL; }
        x = (int)Math.Floor(U(k, (ulong)i * 2 + 1) * w.W);
        double fz = U(k, (ulong)i * 2 + 2);
        if (i % 3 == 0)
        {
            double lat = -60 + fz * 135;
            z = (int)Math.Floor(w.L / 2.0 - lat * ((w.L / 2.0 - 48) / 90.0));
        }
        else z = (int)Math.Floor(fz * w.L);
    }
}

sealed class Hasher : IDisposable
{
    readonly IncrementalHash h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    readonly byte[] b = new byte[8];
    public void I(int v) { BitConverter.TryWriteBytes(b, v); h.AppendData(b, 0, 4); }
    public void D(double v) { BitConverter.TryWriteBytes(b, BitConverter.DoubleToInt64Bits(v)); h.AppendData(b, 0, 8); }
    public void S(string v) { var bytes = Encoding.UTF8.GetBytes(v ?? "\0"); I(bytes.Length); h.AppendData(bytes); }
    public void B(bool v) { b[0] = v ? (byte)1 : (byte)0; h.AppendData(b, 0, 1); }
    public void Sample(RegionalGeologyCatalog.Sample s)
    {
        I(s.Lithology); I(s.Sediment); S(s.Profile.Id); S(s.Province); B(s.HasRegionalBedrock); B(s.OutsideEarth);
    }
    public string Done() => Convert.ToHexString(h.GetHashAndReset());
    public void Dispose() => h.Dispose();
}
