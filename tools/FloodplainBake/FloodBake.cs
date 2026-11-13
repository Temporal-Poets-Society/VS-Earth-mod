using System.Threading.Tasks;

namespace FloodplainBake;

/// <summary>
/// The natural flood of the rivers that keep water through the dry season (decision 0048, soil rules 3). From every such
/// course the flood stands <see cref="FloodHeight"/> above the river's water (its "top") and falls <see cref="FallPerKm"/>
/// per km of the way out; it covers the land it reaches without rising (connected land at or below it: closed basins
/// beside a valley stay dry). The highest flood reaching a place counts, but never above the top of the river nearest to
/// the place in a straight line: a flood from far upstream does not run down a valley above the river beside it. Nor does
/// it cover ground more than <see cref="DepthFactor"/> flood heights below that river's water: the lowlands falling away
/// from a river raised on its own silt (the Karakum beside the Amu Darya, the Salton basin beside the Colorado) drain
/// elsewhere and are not its floodplain.
/// Grid: 7.5" cells (480 per degree) in 0.25 degree tiles, loaded on demand; each cell's ground and water from the caller.
/// </summary>
public sealed class FloodBake
{
    public const int CellsPerDegree = 480, TileCells = 120, Cols = 360 * CellsPerDegree, Rows = 180 * CellsPerDegree;
    const int TilesAcross = Cols / TileCells, N = TileCells * TileCells, Bucket = 48;
    public double FallPerKm = 0.15, HeightA = 1.2, HeightB = 0.3, HeightMin = 1.5, HeightMax = 15, DepthFactor = 1, CapSearchKm = 200;

    /// <summary>The flood's height (m) above a river of mean discharge q (m³/s): Nile 12.6, Euphrates 8.7, 100 m³/s 4.8.</summary>
    public double FloodHeight(double q) => Math.Clamp(HeightA * Math.Pow(Math.Max(q, 0), HeightB), HeightMin, HeightMax);

    sealed class Tile
    {
        public readonly float[] Ground = new float[N], Stage = new float[N], Cap = new float[N], Floor = new float[N];
        public readonly bool[] Blocked = new bool[N];
    }
    /// <summary>A cell's ground (m) and whether water other than a river covers it (sea, lake), by its centre.</summary>
    public delegate (float Ground, bool Blocked) CellSampler(double lat, double lon);

    readonly CellSampler sample;
    readonly Dictionary<int, Tile> tiles = new();
    readonly Dictionary<long, (float Top, float Floor)> seeds = new();
    readonly Dictionary<long, List<(int Row, int Col, float Top, float Floor)>> buckets = new();
    readonly PriorityQueue<long, float> heap = new();
    public long Pops, FloodedCells;
    public int TileCount => tiles.Count;
    public int SeedCells => seeds.Count;
    public Action<string> Log = Console.WriteLine;

    public FloodBake(CellSampler sampler) { sample = sampler; }

    public static double CentreLat(int row) => 90 - (row + .5) / CellsPerDegree;
    public static double CentreLon(int col) => -180 + (col + .5) / CellsPerDegree;
    public static int RowOf(double lat) => Math.Clamp((int)Math.Floor((90 - lat) * CellsPerDegree), 0, Rows - 1);
    public static int ColOf(double lon) => ((int)Math.Floor((lon + 180) * CellsPerDegree) % Cols + Cols) % Cols;
    static long Cell(int row, int col) => (long)row * Cols + col;
    static int TileOf(int row, int col) => row / TileCells * TilesAcross + col / TileCells;

    /// <summary>
    /// Seeds the flood along a course piece of mean discharge q (m³/s) from (lat0, lon0) to (lat1, lon1), its water going
    /// from water0 to water1 (m).
    /// </summary>
    public void SeedSegment(double lat0, double lon0, double water0, double lat1, double lon1, double water1, double q)
    {
        double h = FloodHeight(q);
        double dLat = (lat1 - lat0) * CellsPerDegree, dLon = (lon1 - lon0) * CellsPerDegree;
        int n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dLat), Math.Abs(dLon)) * 2));
        for (int k = 0; k <= n; k++)
        {
            double t = (double)k / n;
            long cell = Cell(RowOf(lat0 + t * (lat1 - lat0)), ColOf(lon0 + t * (lon1 - lon0)));
            double water = water0 + t * (water1 - water0);
            float top = (float)(water + h), floor = (float)(water - DepthFactor * h);
            if (!seeds.TryGetValue(cell, out var old) || top > old.Top) seeds[cell] = (top, floor);
        }
    }

    /// <summary>The top and floor of the seed nearest to a cell in a straight line, NaN when none is within CapSearchKm.</summary>
    (float Top, float Floor) Nearest(int row, int col)
    {
        double kmZ = 111.32 / CellsPerDegree, kmX = kmZ * Math.Max(.05, Math.Cos(CentreLat(row) * Math.PI / 180));
        double bucketKm = Bucket * Math.Min(kmZ, kmX) * .95;
        int br = row / Bucket, bc = col / Bucket, across = Cols / Bucket;
        double best = double.MaxValue;
        float top = float.NaN, floor = float.NaN;
        for (int ring = 0; ring <= 1000 && (ring - 1) * bucketKm <= Math.Min(best, CapSearchKm); ring++)
            for (int dr = -ring; dr <= ring; dr++)
                for (int dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dr), Math.Abs(dc)) != ring) continue;
                    if (!buckets.TryGetValue((long)(br + dr) * across + ((bc + dc) % across + across) % across, out var list)) continue;
                    foreach (var s in list)
                    {
                        double dz = (s.Row - row) * kmZ, dx = Math.Abs(s.Col - col);
                        dx = Math.Min(dx, Cols - dx) * kmX;
                        double d = Math.Sqrt(dz * dz + dx * dx);
                        if (d < best - 1e-9 || (d < best + 1e-9 && s.Top > top)) { best = d; top = s.Top; floor = s.Floor; }
                    }
                }
        return best <= CapSearchKm ? (top, floor) : (float.NaN, float.NaN);
    }

    Tile Get(int row, int col)
    {
        int key = TileOf(row, col);
        if (tiles.TryGetValue(key, out var t)) return t;
        t = new Tile();
        Array.Fill(t.Stage, float.NegativeInfinity);
        int tr = key / TilesAcross * TileCells, tc = key % TilesAcross * TileCells;
        Parallel.For(0, TileCells, r =>
        {
            for (int c = 0; c < TileCells; c++)
            {
                int i = r * TileCells + c;
                (t.Ground[i], t.Blocked[i]) = sample(CentreLat(tr + r), CentreLon(tc + c));
                (t.Cap[i], t.Floor[i]) = Nearest(tr + r, tc + c);
            }
        });
        tiles[key] = t;
        return t;
    }

    void Offer(int row, int col, float stage, bool seed)
    {
        var t = Get(row, col);
        int i = row % TileCells * TileCells + col % TileCells;
        if (!float.IsNaN(t.Cap[i]) && stage > t.Cap[i]) stage = t.Cap[i];
        if (!seed && (t.Blocked[i] || stage < t.Ground[i] || t.Ground[i] < t.Floor[i])) return;
        if (stage <= t.Stage[i]) return;
        t.Stage[i] = stage;
        heap.Enqueue(Cell(row, col), -stage);
    }

    /// <summary>Spreads the flood from every seed (label-correcting: a place improved later is spread again).</summary>
    public void Run()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (cell, s) in seeds)
        {
            int row = (int)(cell / Cols), col = (int)(cell % Cols);
            long b = (long)(row / Bucket) * (Cols / Bucket) + col / Bucket;
            if (!buckets.TryGetValue(b, out var list)) buckets[b] = list = new();
            list.Add((row, col, s.Top, s.Floor));
        }
        // seed order by place, so neighbouring tiles load together and the result does not depend on hashing
        foreach (var (cell, s) in seeds.OrderBy(kv => kv.Key)) Offer((int)(cell / Cols), (int)(cell % Cols), s.Top, true);
        Log($"  {seeds.Count} seed cells, {tiles.Count} tiles, {sw.Elapsed.TotalSeconds:0} s");
        long next = 5_000_000;
        while (heap.TryDequeue(out long cell, out float neg))
        {
            Pops++;
            int row = (int)(cell / Cols), col = (int)(cell % Cols);
            float stage = -neg;
            if (Get(row, col).Stage[row % TileCells * TileCells + col % TileCells] > stage) continue;
            double kmZ = 111.32 / CellsPerDegree, kmX = kmZ * Math.Cos(CentreLat(row) * Math.PI / 180);
            for (int dr = -1; dr <= 1; dr++)
            {
                int r2 = row + dr;
                if (r2 < 0 || r2 >= Rows) continue;
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    Offer(r2, ((col + dc) % Cols + Cols) % Cols, (float)(stage - FallPerKm * Math.Sqrt(dr * dr * kmZ * kmZ + dc * dc * kmX * kmX)), false);
                }
            }
            if (Pops >= next) { Log($"  {Pops / 1_000_000}M spread, {tiles.Count} tiles, {sw.Elapsed.TotalSeconds:0} s"); next += 5_000_000; }
        }
        foreach (var t in tiles.Values) for (int i = 0; i < N; i++) if (!float.IsNegativeInfinity(t.Stage[i])) FloodedCells++;
        Log($"  flood done: {FloodedCells} cells flooded, {Pops} spread, {tiles.Count} tiles, {sw.Elapsed.TotalSeconds:0} s");
    }

    /// <summary>Every flooded cell (row, column, flood level m), in place order.</summary>
    public IEnumerable<(int Row, int Col, float Stage)> Flooded()
    {
        foreach (var key in tiles.Keys.OrderBy(k => k))
        {
            var t = tiles[key];
            int tr = key / TilesAcross, tc = key % TilesAcross;
            for (int i = 0; i < N; i++)
                if (!float.IsNegativeInfinity(t.Stage[i])) yield return (tr * TileCells + i / TileCells, tc * TileCells + i % TileCells, t.Stage[i]);
        }
    }
}
