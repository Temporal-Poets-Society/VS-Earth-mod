using EarthMap.Core;

/// <summary>
/// The river flow query's answers beyond the model's own column (src/Core/RiverQuery.cs, EarthMapSystem.RiverAt): pool
/// layout 11's "drawn as rapid water" per chunk, on synthetic columns and a stand-in for the generator's drawn sources.
/// </summary>
static class RiverQueryChecks
{
    static int checks;
    static void Require(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("RiverQuery: " + message);
    }

    /// <summary>A river column of the model: fresh water from its bed to its surface.</summary>
    static TerrainModel.Column River(int top, int waterTop, bool rapidsReach = false, byte ramp = 0) => new()
    {
        Top = top, WaterTop = waterTop, Water = TerrainModel.WaterLake, River = true, RapidsReach = rapidsReach, Ramp = ramp,
    };

    /// <summary>Whether the call threw the query's "not ready" (no answer to keep), and what it answered otherwise.</summary>
    static bool NotReady(Func<bool> ask, out bool answer)
    {
        answer = false;
        try { answer = ask(); return false; }
        catch (RiverQueryNotReadyException) { return true; }
    }

    public static void Run()
    {
        SettlesOnlyWhereRapidCanBeDrawn();
        MayBeDrawnRapidIsNecessary();
        FailedChunkWaits();
        NotReadyIsNoAnswer();
        SpanHoldsWater();
        Console.WriteLine($"PASS RiverQuery: {checks} checks (a chunk settled only for a column that can be drawn rapid, failed chunks wait, no answer while not ready, spans hold water)");
    }

    /// <summary>
    /// Hydrology review round 2, item 3: a rock breaking a chute's surface (TerrainModel.Rivers: about one chute column in 8
    /// has its bed at the surface + 1, River kept) gave RiverSpanAt a bed above its water surface. The generator draws no
    /// water there, so there is no span (Hydrology rc.4 reads a reversed span the same way: none).
    /// </summary>
    static void SpanHoldsWater()
    {
        Require(RiverQuery.Span(River(60, 64), out int bed, out int top) && bed == 60 && top == 64, "a river column's span is its bed and surface");
        Require(!RiverQuery.Span(River(65, 64, rapidsReach: true), out bed, out top) && bed == -1 && top == -1, "a rock breaking a chute's surface has no span");
        Require(!RiverQuery.Span(River(64, 64), out _, out _), "nor a column whose bed is at its surface (no water drawn)");
        Require(!RiverQuery.Span(new TerrainModel.Column { Top = 60, WaterTop = 64, Water = TerrainModel.WaterLake }, out _, out _), "nor a lake's column");
    }

    /// <summary>
    /// Hydrology review round 2, item 1: while a chunk's drawn rapid sources aren't the generator's yet (DrawnRapidSources
    /// null: a GLO-90 tile still to come, generation not set up), the query answered the model's rough guess, which
    /// Hydrology keeps for good (and its rapids rule turns those sources rapid in the save). Now it throws
    /// RiverQueryNotReadyException, which Hydrology never keeps, and answers what was drawn once the chunk is ready.
    /// </summary>
    static void NotReadyIsNoAnswer()
    {
        long now = 0;
        bool ready = false;
        int asked = 0;
        var drawn = new bool[32 * 32];
        drawn[5 * 32 + 7] = true; // column (7, 5) of chunk (-1, 2): x = -25, z = 69
        var query = new RiverQuery((cx, cz) => { asked++; return ready && cx == -1 && cz == 2 ? drawn : null; }, () => now);
        // a rapids reach's column under a ramp: the old guess said "rapids" here
        var guessRapid = River(60, 64, rapidsReach: true, ramp: 3);
        Func<int, int, TerrainModel.Column> reach = (x, z) => guessRapid;
        Require(NotReady(() => query.DrawnRapid(-25, 69, guessRapid, reach), out bool guess), "a chunk not ready gives no answer (it answered " + guess + ")");
        ready = true;
        Require(NotReady(() => query.DrawnRapid(-26, 69, guessRapid, reach), out _) && asked == 1, "nor while it waits (" + asked + " asks)");
        now += RiverQuery.RetryMs;
        Require(!NotReady(() => query.DrawnRapid(-25, 69, guessRapid, reach), out bool rapid) && rapid, "a ready chunk answers what was drawn (rapid)");
        Require(!NotReady(() => query.DrawnRapid(-26, 69, guessRapid, reach), out bool calm) && !calm, "and its other columns as drawn (calm, though the guess said rapid)");
        Require(asked == 2, "a ready chunk is asked once and kept (" + asked + " asks)");
    }

    /// <summary>
    /// Hydrology review round 2, item 2: layout 11 settled a chunk (72 x 72 model columns) for the first query of any river
    /// column in it, though only a rapids reach's columns and the upper edges of drops can be drawn rapid. A calm reach's
    /// column away from its steps, and a rock breaking a chute's surface, now answer "not rapids" without settling.
    /// </summary>
    static void SettlesOnlyWhereRapidCanBeDrawn()
    {
        int asked = 0;
        var drawn = new bool[32 * 32];
        var query = new RiverQuery((cx, cz) => { asked++; return drawn; });
        var calm = River(60, 64);
        Func<int, int, TerrainModel.Column> pool = (x, z) => calm;
        Require(!query.DrawnRapid(10, 10, calm, pool) && asked == 0, "a calm pool's column answers calm without settling (" + asked + " settlings)");
        var rock = River(65, 64, rapidsReach: true);
        Require(!query.DrawnRapid(11, 10, rock, pool) && asked == 0, "a rock breaking a chute's surface (bed above the water) answers calm without settling");
        var dry = new TerrainModel.Column { Top = 70, WaterTop = -1 };
        Require(!RiverQuery.MayBeDrawnRapid(0, 0, River(60, 64), (x, z) => dry), "a calm column between banks is never rapid");
        // the upper edge of a calm reach's 1-block step (a chute where the water falls 2 within Hydrology's window) and of a
        // curtain's lip: settled, the chunk's answer
        var below = River(58, 63);
        Func<int, int, TerrainModel.Column> step = (x, z) => x == 13 ? below : calm;
        drawn[10 * 32 + 12] = true;
        Require(query.DrawnRapid(12, 10, calm, step) && asked == 1, "an upper edge is settled and answers as drawn");
        Require(RiverQuery.MayBeDrawnRapid(12, 10, calm, (x, z) => x == 13 ? River(50, 60) : calm), "a curtain's lip can be drawn rapid");
        Require(!RiverQuery.MayBeDrawnRapid(12, 10, calm, (x, z) => x == 13 ? River(60, 65) : calm), "the foot of a drop is no upper edge");
        Require(!RiverQuery.MayBeDrawnRapid(12, 10, calm, (x, z) => x == 13 ? new TerrainModel.Column { Top = 50, WaterTop = 55, Water = TerrainModel.WaterSaltLake } : calm),
            "other water standing lower is no drop of this river's water");
        query.DrawnRapid(40, 10, River(60, 64, rapidsReach: true), pool);
        Require(asked == 2, "a rapids reach's column is settled (" + asked + " settlings)");
    }

    /// <summary>
    /// <see cref="RiverQuery.MayBeDrawnRapid"/> lets through every column the generator draws rapid: random stepped rivers
    /// (1-block steps, falls of 2 to 4, rapids reaches, rocks in chutes, banks) settled with pool layout 11's rules
    /// (RiverSettling) and judged by the surface pass's rule (ChunkSettler.DrawnRapidSources and RampWord.RapidSource, as
    /// written there). Columns outside a rapids reach are among them (curtain lips, chutes from Hydrology's window), so the
    /// rapids reaches alone would not do.
    /// </summary>
    static void MayBeDrawnRapidIsNecessary()
    {
        var rng = new Random(20261109);
        int rapid = 0, outsideReach = 0, letThrough = 0, total = 0;
        for (int trial = 0; trial < 300; trial++)
        {
            const int w = 72, h = 40;
            var cols = new TerrainModel.Column[w * h];
            // drops along x at random places, each line slanting across the river (several rows per column)
            int drops = rng.Next(3, 10);
            var at = new int[drops]; var size = new int[drops]; var slant = new double[drops]; var reach = new bool[drops + 1];
            for (int i = 0; i < drops; i++) { at[i] = rng.Next(4, w - 4); size[i] = rng.Next(5) == 0 ? rng.Next(2, 5) : 1; slant[i] = rng.NextDouble() * 1.6 - 0.8; }
            for (int i = 0; i <= drops; i++) reach[i] = rng.Next(3) == 0;
            int z0 = rng.Next(4, 12), width = rng.Next(4, 22);
            for (int x = 0; x < w; x++)
            {
                int shift = (int)Math.Round(3 * Math.Sin(x / 9.0 + trial));
                for (int z = 0; z < h; z++)
                {
                    int level = 120, seg = 0;
                    for (int i = 0; i < drops; i++) if (x + slant[i] * (z - 20) >= at[i]) { level -= size[i]; seg++; }
                    ref var c = ref cols[z * w + x];
                    bool water = z >= z0 + shift && z < z0 + shift + width;
                    if (!water) { c = new TerrainModel.Column { Top = level + rng.Next(0, 3), WaterTop = -1 }; continue; }
                    c = River(level - rng.Next(1, 4), level, reach[Math.Min(seg, drops)]);
                    if (c.RapidsReach && rng.Next(8) == 0) c.Top = level + 1;
                }
            }
            var before = (TerrainModel.Column[])cols.Clone();
            RiverSettling.Settle(cols, w, h, SettleRules.ForPoolLayout(11));
            var dry = new TerrainModel.Column { Top = 200, WaterTop = -1 };
            Func<int, int, TerrainModel.Column> model = (x, z) => x < 0 || x >= w || z < 0 || z >= h ? dry : before[z * w + x];
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var c = cols[z * w + x];
                    if (!c.River) continue;
                    total++;
                    bool may = RiverQuery.MayBeDrawnRapid(x, z, before[z * w + x], model);
                    if (may) letThrough++;
                    if (!DrawnRapid(c)) continue;
                    rapid++;
                    if (!c.RapidsReach) outsideReach++;
                    Require(may, $"trial {trial}: column ({x}, {z}) is drawn rapid (edge {c.Edge}, ramp {c.Ramp}, rapids reach {c.RapidsReach}) but was not let through");
                }
        }
        Console.WriteLine($"  RiverQuery.MayBeDrawnRapid: {rapid:n0} of {total:n0} river columns drawn rapid ({outsideReach:n0} outside a rapids reach), {letThrough:n0} let through to settling");
        Require(rapid > 1000 && outsideReach > 100 && letThrough < total, "the stepped rivers have rapid columns, also outside rapids reaches, and calm columns left out");
    }

    /// <summary>The surface pass's rule for a settled column far from the map's ends, pool layout 11 (ChunkSettler.
    /// DrawnRapidSources with RampWord.TryOf and RapidSource; the frozen-lakes rule only takes rapids away).</summary>
    static bool DrawnRapid(in TerrainModel.Column c)
    {
        bool freshTop = c.WaterTop > c.Top && c.Water == TerrainModel.WaterLake;
        bool rapidTop = c.RapidsReach && c.Ramp > 0, calmRiver = c.River && !rapidTop;
        bool word = c.Ramp > 0 || c.Fall > 0 || c.Edge > 0 || rapidTop || calmRiver;
        return freshTop && word && (c.Edge != 0 ? c.Edge != TerrainModel.EdgeStep : rapidTop);
    }

    /// <summary>
    /// Hydrology review round 2, item 2: a chunk whose settling threw was settled again (and threw again) on every water
    /// update there, and one that was not ready settled again each time too. Now either waits RetryMs before it is asked
    /// again; meanwhile the query gives no answer at once (RiverQueryNotReadyException, carrying a failure's exception),
    /// without settling.
    /// </summary>
    static void FailedChunkWaits()
    {
        long now = 1000;
        int asked = 0;
        bool fail = true;
        Exception said = null;
        var query = new RiverQuery((cx, cz) => { asked++; return fail ? throw new IOException("tile unreadable") : new bool[32 * 32]; }, () => now, (cx, cz, e) => said = e);
        var reach = River(60, 64, rapidsReach: true);
        Func<int, int, TerrainModel.Column> pool = (x, z) => reach;
        bool Throws<T>(Func<bool> ask) where T : Exception { try { ask(); return false; } catch (T) { return true; } }
        Require(Throws<IOException>(() => query.DrawnRapid(5, 5, reach, pool)) && asked == 1 && said is IOException, "the first failure is the settling's own, and said");
        said = null;
        Require(Throws<RiverQueryNotReadyException>(() => query.DrawnRapid(6, 5, reach, pool)) && asked == 1 && said == null, "a failed chunk is not settled again at once");
        now += RiverQuery.RetryMs - 1;
        Require(Throws<RiverQueryNotReadyException>(() => query.DrawnRapid(5, 6, reach, pool)) && asked == 1, "nor until RetryMs has passed");
        now += 1;
        fail = false;
        Require(!query.DrawnRapid(5, 5, reach, pool) && asked == 2, "then it is asked again");
        Require(!Throws<Exception>(() => query.DrawnRapid(40, 5, reach, pool)) && asked == 3, "other chunks are asked as usual");
        // a chunk not ready (null) waits too, rather than being settled again for every water update
        int notReady = 0;
        var waiting = new RiverQuery((cx, cz) => { notReady++; return null; }, () => now);
        Require(Throws<RiverQueryNotReadyException>(() => waiting.DrawnRapid(5, 5, reach, pool)) && Throws<RiverQueryNotReadyException>(() => waiting.DrawnRapid(5, 5, reach, pool)),
            "a chunk not ready gives no answer");
        Require(notReady == 1, "a chunk not ready is not settled again at once (" + notReady + " settlings)");
        now += RiverQuery.RetryMs;
        Throws<RiverQueryNotReadyException>(() => waiting.DrawnRapid(5, 5, reach, pool));
        Require(notReady == 2, "and is asked again after RetryMs");
    }
}
