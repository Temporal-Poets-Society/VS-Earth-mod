using System.Security.Cryptography;
using EarthMap.Core;

// EA-0088 (D-R5 of the Fable pass, code review 14 November 2026): what the server's tools compute without the game.
//   OpenWater      /earth tp ... water: the nearest spot with room for a ship
//   SiteTerrain    /earth site: the ground's slope and aspect, the distance to the sea
//   WorldMapFrame  /earth map: the picture's frame, the size it may have, the markers
//   ColumnDigest   the self test's digest of a generated chunk column, its format pinned (every recorded digest uses it)
// Usage: dotnet run -c Release --project tests\ServerToolsTests

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
bool Near(double a, double b, double eps = 1e-9) => Math.Abs(a - b) <= eps;
// an aspect: on the circle, and within 0 to 2π
bool Aspect(double a, double b) => a >= 0 && a < 2 * Math.PI && Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI)) <= 1e-9;

// ---------------------------------------------------------------------------------------------------------- OpenWater
TerrainModel.Column Land(int top = 115) => new() { Top = top, WaterTop = top, Water = TerrainModel.WaterNone };
TerrainModel.Column Water(int depth, int level = 110, byte kind = TerrainModel.WaterLake) => new() { Top = level - depth, WaterTop = level, Water = kind };
// a round lake, 60 blocks across its radius, 10 deep, at x 500
Func<double, double, TerrainModel.Column> lake = (x, z) => Math.Sqrt((x - 500) * (x - 500) + z * z) <= 60 ? Water(10) : Land();
Check(OpenWater.Find(lake, 0, 0, 3000, CancellationToken.None, out double wx, out double wz), "open water: a lake 500 blocks away is found");
double fromStart = Math.Sqrt(wx * wx + wz * wz), fromCentre = Math.Sqrt((wx - 500) * (wx - 500) + wz * wz);
Check(OpenWater.At(lake, wx, wz) && fromCentre <= 60 - 8, "open water: the spot found has water 8 blocks all round");
Check(fromStart >= 440 - 4 && fromStart <= 452 + 4, $"open water: the nearest ring with room for a ship is taken ({fromStart:0} blocks)");
Check(OpenWater.Find(lake, 500, 0, 3000, CancellationToken.None, out wx, out wz) && wx == 500 && wz == 0, "open water: on open water the spot itself");
Check(!OpenWater.Find(lake, 0, 0, 400, CancellationToken.None, out wx, out wz) && wx == 0 && wz == 0, "open water: none within the distance: false, and the spot asked");
Check(!OpenWater.At((x, z) => Water(3), 0, 0) && OpenWater.At((x, z) => Water(4), 0, 0), "open water: at least 4 blocks deep");
Check(!OpenWater.At((x, z) => { var c = Water(10, kind: TerrainModel.WaterSea); c.Gutter = true; return c; }, 0, 0), "open water: never the seam's gutter");
Check(OpenWater.At((x, z) => Water(10, kind: TerrainModel.WaterSea), 0, 0), "open water: the sea counts");
Check(!OpenWater.At((x, z) => x > 6 ? Water(1) : Water(10), 0, 0), "open water: water under 2 blocks deep within 8 blocks is no room");
Check(!OpenWater.At((x, z) => x > 6 ? Water(10, level: 112) : Water(10), 0, 0), "open water: a level 2 blocks off within 8 blocks is no room (a weir, a fall)");
Check(OpenWater.At((x, z) => x > 6 ? Water(10, level: 111) : Water(10), 0, 0), "open water: a level a block off is room");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    bool threw = false;
    try { OpenWater.Find(lake, 0, 0, 3000, cancelled.Token, out _, out _); } catch (OperationCanceledException) { threw = true; }
    Check(threw, "open water: a cancelled search stops");
}

// -------------------------------------------------------------------------------------------------------- SiteTerrain
// planes through (1000, 2000): metres per block east mx, south mz; north is -z
foreach (var (mx, mz) in new[] { (30.0, 30.0), (10.0, 30.0) })
{
    double x0 = 1000, z0 = 2000;
    void Plane(double east, double north, out double slope, out double aspect)
        => SiteTerrain.PlaneSlope((x, z) => 100 + east * (x - x0) * mx + north * -(z - z0) * mz, x0, z0, 3, mx, mz, out slope, out aspect);
    Plane(0, 0, out double s, out double a);
    Check(Near(s, 0), "site: a flat plane has no slope");
    Plane(0.1, 0, out s, out a);
    Check(Near(s, Math.Atan(0.1)) && Aspect(a, 1.5 * Math.PI), $"site: ground rising east falls west (aspect 270°), {mx} m a block east");
    Plane(-0.1, 0, out s, out a);
    Check(Near(s, Math.Atan(0.1)) && Aspect(a, 0.5 * Math.PI), "site: ground rising west falls east (aspect 90°)");
    Plane(0, 0.2, out s, out a);
    Check(Near(s, Math.Atan(0.2)) && Aspect(a, Math.PI), "site: ground rising north falls south (aspect 180°)");
    Plane(0, -0.2, out s, out a);
    Check(Near(s, Math.Atan(0.2)) && Aspect(a, 0), "site: ground rising south falls north (aspect 0°)");
    Plane(0.3, 0.4, out s, out a);
    Check(Near(s, Math.Atan(0.5)) && Aspect(a, Math.PI + Math.Atan2(0.3, 0.4)), "site: a slope of 0.5 down to the south-west");
}
// one high block among the 25 heights tilts the plane little (the reason for the fit)
SiteTerrain.PlaneSlope((x, z) => x == 1003 && z == 2000 ? 140 : 100, 1000, 2000, 3, 30, 30, out double spike, out _);
Check(spike < Math.Atan(0.05), $"site: a single 40 m spike next to the spot tilts the 25-point plane under 3° ({spike * 180 / Math.PI:0.0}°)");

Check(SiteTerrain.SeaDistance((x, z) => true, 0, 0, 30, 10000) == 0, "site: on the sea the sea is 0 m away");
double coast = SiteTerrain.SeaDistance((x, z) => x >= 100, 0, 0, 30, 10000);
Check(coast >= 3000 && coast < 3000 * 1.15 + 1e-6, $"site: a coast 3 km east is found on the first ring past it ({coast:0} m)");
Check(double.IsPositiveInfinity(SiteTerrain.SeaDistance((x, z) => x >= 400, 0, 0, 30, 10000)), "site: a coast 12 km away is beyond the 10 km search");
Check(double.IsPositiveInfinity(SiteTerrain.SeaDistance((x, z) => false, 0, 0, 30, 10000)), "site: no sea is +∞");
Check(SiteTerrain.SeaDistance((x, z) => x * x + z * z >= 4, 0, 0, 1000, 100000) == 2000, "site: the first ring is 2 blocks out when a block is wider than 12.5 m");

// ------------------------------------------------------------------------------------------------------ WorldMapFrame
var geo = new EarthGeometry { RingStart = 1000, RingLength = 36000, EquatorZ = 50000, BlocksPerDegreeZ = 100 };
var frame = WorldMapFrame.For(geo, 1_000_000, 4);
Check(frame.Width == 9000 && frame.Height == 4500 && frame.Z0 == 41000, "world map: one ring wide, pole to pole, 4 blocks a pixel");
Check(frame.Bytes == 3L * 9000 * 4500, "world map: 3 bytes a pixel");
var trimmed = WorldMapFrame.For(new EarthGeometry { RingStart = 0, RingLength = 36000, EquatorZ = 5000, BlocksPerDegreeZ = 100 }, 12000, 10);
Check(trimmed.Z0 == 0 && trimmed.Height == 1200, "world map: the picture stops at the world's edges north and south");
Check(Near(frame.Wrap(999), 36999) && Near(frame.Wrap(37000), 1000) && Near(frame.Wrap(1000 + 5 * 36000 + 7), 1007), "world map: x wraps onto the ring");
Check(frame.Pixel(37000 + 20, 41000 + 40) == (5, 10), "world map: a column past the seam falls on the ring's start");
// the size a picture may have: the fewest blocks a pixel within the limit, for any world
var random = new Random(88);
int formulaShort = 0;
for (int n = 0; n < 2000; n++)
{
    var g = new EarthGeometry { RingStart = random.Next(-50000, 50000), RingLength = random.Next(1000, 400000), EquatorZ = random.Next(0, 300000), BlocksPerDegreeZ = 1 + random.NextDouble() * 400 };
    int bpp = random.Next(1, 40);
    long limit = random.Next(1_000_000, 120_000_001);
    var f = WorldMapFrame.For(g, 1_000_000, bpp);
    if (f.Bytes <= limit) continue;
    int fit = f.BlocksPerPixelWithin(limit);
    Check(WorldMapFrame.For(g, 1_000_000, fit).Bytes <= limit, $"world map: {fit} blocks a pixel fits {limit} bytes");
    Check(fit == bpp || WorldMapFrame.For(g, 1_000_000, fit - 1).Bytes > limit, $"world map: {fit} blocks a pixel is the fewest that fits");
    if (WorldMapFrame.For(g, 1_000_000, (int)Math.Ceiling(bpp * Math.Sqrt(f.Bytes / (double)limit))).Bytes > limit) formulaShort++;
}
Console.WriteLine($"world map: the square-root estimate alone was too small in {formulaShort} of the cases over the limit (the suggestion steps up from it)");
// markers: 5 x 5, the spawn on top whatever the order, cut at the picture's edges
var small = WorldMapFrame.For(new EarthGeometry { RingStart = 0, RingLength = 400, EquatorZ = 1000, BlocksPerDegreeZ = 1 }, 1_000_000, 4);
Check(small.Width == 100 && small.Height == 45, "world map: the small test picture");
var rgb = new byte[small.Bytes];
small.DrawMarkers(rgb, new[] { (200.0, 910.0 + 40, true), (200.0, 910.0 + 40, false), (0.0, 910.0, false), (399.0 + 400, 910.0 + 179, false), (200.0, 0.0, false) });
(byte R, byte G, byte B) At(int i, int j) { long k = 3L * (j * 100 + i); return (rgb[k], rgb[k + 1], rgb[k + 2]); }
Check(At(50, 10) == (255, 40, 40) && At(48, 8) == (255, 40, 40) && At(52, 12) == (255, 40, 40) && At(53, 10) == (0, 0, 0), "world map: the spawn is a red 5 x 5 square over a story marker");
Check(At(0, 0) == (255, 230, 0) && At(2, 2) == (255, 230, 0) && At(3, 0) == (0, 0, 0), "world map: a marker in a corner is cut at the edge");
Check(At(99, 44) == (0, 0, 0) || At(99, 44) == (255, 230, 0), "world map: a marker past the seam on the last row is drawn or cut, never out of bounds");
Check(Enumerable.Range(0, 100 * 45).Count(p => rgb[3 * p] != 0) == 25 + 9 + 4, "world map: the spawn's square, the corner's 3 x 3, the seam's 2 x 2, and nothing of a marker far north of the picture");

// ------------------------------------------------------------------------------------------------------- ColumnDigest
// two chunks, a canon that counts block 3 as block 2 (as the self test counts every loot vessel as the first), heights
int[] canon = { 0, 1, 2, 2, 4, 5 };
int Block(int chunk, int i) => i % 7 + chunk;
int Fluid(int i) => i % 3 == 0 ? 5 : 0;
var heights = Enumerable.Range(0, 1024).Select(i => (ushort)(i % 300)).ToArray();
byte[] digest; string chunkHashes;
using (var d = new ColumnDigest(canon))
{
    for (int c = 0; c < 2; c++) { int cc = c; d.AddChunk(i => Block(cc, i), Fluid); }
    d.AddHeights(heights); d.AddHeights(null);
    digest = d.Finish(); chunkHashes = d.Chunks;
}
// the same from the format as it is written down (SelfTest.HashGenerated before EA-0088)
var all = new List<byte>(); var shorts = new System.Text.StringBuilder();
for (int c = 0; c < 2; c++)
{
    var buffer = new byte[ColumnDigest.ChunkBlocks * 8];
    for (int i = 0; i < ColumnDigest.ChunkBlocks; i++)
    {
        int id = Block(c, i);
        BitConverter.TryWriteBytes(buffer.AsSpan(i * 8), (uint)id < (uint)canon.Length ? canon[id] : id);
        BitConverter.TryWriteBytes(buffer.AsSpan(i * 8 + 4), Fluid(i));
    }
    all.AddRange(buffer);
    shorts.Append(' ').Append(Convert.ToHexString(SHA256.HashData(buffer))[..8]);
}
foreach (ushort v in heights) all.AddRange(BitConverter.GetBytes(v));
Check(digest.AsSpan().SequenceEqual(SHA256.HashData(all.ToArray())) && chunkHashes == shorts.ToString(), "digest: the column hash and the chunks' short hashes as the self test wrote them");
// taken from this build (14 November 2026), after the line above showed it equal to the format as the self test wrote it
const string Pinned = "9A80C1A723D2AD47C0EE5F84B00CBAD7DB24DE7E564A0764919CCA205892AB5B 184E6B99 C5C597BA";
Check(Convert.ToHexString(digest) + chunkHashes == Pinned, "digest: the format is unchanged (a change makes every recorded digest look different)");
using (var d = new ColumnDigest(null))
{
    d.AddChunk(i => 7, i => 0);
    var expect = new byte[ColumnDigest.ChunkBlocks * 8];
    for (int i = 0; i < ColumnDigest.ChunkBlocks; i++) BitConverter.TryWriteBytes(expect.AsSpan(i * 8), 7);
    Check(d.Finish().AsSpan().SequenceEqual(SHA256.HashData(expect)), "digest: without a canon each id is hashed as it is");
}
var meta = ColumnDigest.Meta(new[] { 1, 2 }, new byte[][] { null, new byte[] { 9, 8 }, Array.Empty<byte>() });
var metaBytes = new List<byte>();
foreach (int v in new[] { 1, 2, -1, 2 }) metaBytes.AddRange(BitConverter.GetBytes(v));
metaBytes.InsertRange(16, new byte[] { 9, 8 });
metaBytes.AddRange(BitConverter.GetBytes(0));
Check(meta.AsSpan().SequenceEqual(SHA256.HashData(metaBytes.ToArray())), "digest: the meta hash: the top rock, then each piece of data after its length (-1 for none)");
Check(ColumnDigest.Meta(null, Array.Empty<byte[]>()).AsSpan().SequenceEqual(SHA256.HashData(Array.Empty<byte>())), "digest: no top rock and no data hash nothing");

ReleaseFileChecks.Run(Check);
Console.WriteLine($"Server tools: {checks} checks passed.");
