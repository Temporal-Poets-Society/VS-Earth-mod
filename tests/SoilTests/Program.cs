using EarthMap.Core;

// Earth's soil fertility floor (decision 0048): land that isn't arid by the real aridity index has soil.
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
// soil rules 4 offline census (decision 0105): SoilTests <root> census <ModData/earth> [step in degrees]
// every input along a line: SoilTests <root> probe <ModData/earth> lat0 lon0 lat1 lon1 steps
if (args.Length > 7 && args[1] == "probe")
{
    double A(int i) => double.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
    return Probe.Run(root, args[2], A(3), A(4), A(5), A(6), int.Parse(args[7]));
}
if (args.Length > 2 && args[1] == "census")
    return Census.Run(root, args[2], args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : .25);
int passed = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL: " + msg); passed++; }
bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

// ---------------------------------------------------------------- aridity from Earth's rainfall (tools/bake_rain.py)
Check(Near(SoilFertility.Aridity(50, 0), .2) && Near(SoilFertility.Aridity(95, 0), .5) && Near(SoilFertility.Aridity(15, 0), .05), "Rain knots give their aridity");
Check(Near(SoilFertility.Aridity(37.5, 1), .2), "Summer drought's 25% rain cut is undone");
Check(SoilFertility.Aridity(0, 0) == 0 && SoilFertility.Aridity(255, 0) == 5, "Rain outside the curve is clamped");
Check(SoilFertility.Aridity(70, 0) > .29 && SoilFertility.Aridity(70, 0) < .31, "Rain 70 (Earth's hot-desert limit) is an aridity of about 0.3");
Check(!SoilFertility.HasSoil(49, 0, 20) && SoilFertility.HasSoil(50, 0, 20), "Arid below an index of 0.2");
Check(!SoilFertility.HasSoil(200, 0, -10.5) && SoilFertility.HasSoil(200, 0, -10), "Colder than vanilla's grassed topsoil keeps vanilla");
Check(SoilFertility.Apply(40, 60, 0, 18) == SoilFertility.Floor && SoilFertility.Apply(120, 60, 0, 18) == 120 && SoilFertility.Apply(40, 30, 0, 25) == 40,
    "The floor only raises fertility, and only off the deserts");

// ---------------------------------------------------------------- real places
var data = EarthData.LoadFromFolder(root, true);
data.SetClimateFix(true);
data.SetFineRain(true);
// vanilla Climate.GetFertility (VintagestoryAPI 1.22) at the sea-level end of the vertical scale
double Vanilla(double rain, double temp)
{
    double t = Math.Clamp((temp + 20) * 4.25, 0, 255);
    return Math.Min(255, rain / 2 + Math.Max(0, rain * t / 512));
}
(double ai, bool soil, double vanilla, double temp) At(double lat, double lon)
{
    var c = data.SampleClimate(lat, lon);
    double t = c.TMean - .0065 * Math.Max(0, data.SampleTerrain(lat, lon).Elev);
    return (SoilFertility.Aridity(c.Rain, c.DrySummer), SoilFertility.HasSoil(c.Rain, c.DrySummer, t), Vanilla(c.Rain, t), t);
}
var soil = new (string name, double lat, double lon)[]
{
    ("Athens", 37.98, 23.73), ("Seville", 37.39, -5.98), ("Tunis", 36.81, 10.18), ("Madrid", 40.42, -3.70), ("Murcia", 37.99, -1.13),
    ("Jerusalem", 31.77, 35.21), ("Ankara", 39.93, 32.86), ("Los Angeles", 34.05, -118.24), ("Adelaide", -34.93, 138.60),
    ("Perth", -31.95, 115.86), ("Santiago", -33.45, -70.67), ("Cape Town", -33.92, 18.42), ("Odesa steppe", 46.80, 31.00),
    ("Kazakh steppe", 50.30, 68.00), ("Ulaanbaatar", 47.92, 106.92), ("Amarillo", 35.22, -101.83), ("Denver", 39.74, -104.99),
    ("Bamako", 12.64, -8.00), ("Kano", 12.00, 8.52), ("Dodoma", -6.16, 35.75), ("Mendoza", -32.89, -68.83),
};
var desert = new (string name, double lat, double lon)[]
{
    ("Western Desert", 25.0, 28.0), ("Rub al Khali", 20.0, 50.0), ("Atacama", -23.5, -69.5), ("Taklamakan", 39.0, 83.0),
    ("Death Valley", 36.5, -116.9), ("Namib", -24.0, 15.2), ("Simpson Desert", -25.0, 137.0),
};
foreach (var (name, lat, lon) in soil)
{
    var s = At(lat, lon);
    Console.WriteLine($"{name,-15} aridity {s.ai:F2}  vanilla fertility {s.vanilla / 255:F2}  {s.temp:F1} °C  -> {(s.soil ? "soil" : "vanilla")}");
    Check(s.soil, name + " has soil (aridity " + s.ai.ToString("F2") + ")");
}
foreach (var (name, lat, lon) in desert)
{
    var s = At(lat, lon);
    Console.WriteLine($"{name,-15} aridity {s.ai:F2}  vanilla fertility {s.vanilla / 255:F2}  {s.temp:F1} °C  -> {(s.soil ? "soil" : "vanilla")}");
    Check(!s.soil, name + " stays desert (aridity " + s.ai.ToString("F2") + ")");
}
var athens = At(37.98, 23.73);
Check(athens.vanilla < 64, "Vanilla's own fertility puts Athens below its soil threshold (the reported bug)");
// rules 2: ground columns leave the same deserts without topsoil and keep it on the semi-arid land (no temperature test)
Check(SoilFertility.RulesVersion >= SoilFertility.GroundRules, "New worlds get the ground-column desert rule");
Check(SoilFertility.AridGround(49, 0) && !SoilFertility.AridGround(50, 0) && !SoilFertility.AridGround(37.5, 1), "Real desert below an index of 0.2, summer drought undone");
foreach (var (name, lat, lon) in desert) { var c = data.SampleClimate(lat, lon); Check(SoilFertility.AridGround(c.Rain, c.DrySummer), name + " is a real desert: no topsoil in ground columns"); }
foreach (var (name, lat, lon) in soil) { var c = data.SampleClimate(lat, lon); Check(!SoilFertility.AridGround(c.Rain, c.DrySummer), name + " keeps its topsoil in ground columns"); }
// a river's green belt (TerrainModel.RiparianRain lifts the rain towards 145) is no desert
Check(!SoilFertility.AridGround(5 + (145 - 5) * .5, 0), "Half way into the Nile's green belt is no desert");
// rules 3: arid land in a river's natural flood keeps its topsoil (FloodplainGrid; tests/FloodplainTests checks the grid)
Check(SoilFertility.FloodplainRules > SoilFertility.GroundRules && SoilFertility.RulesVersion >= SoilFertility.FloodplainRules, "New worlds get the floodplain rule after the desert rule");

// ---------------------------------------------------------------- ranges step 5a: tall grass kept on dry land (ground-cover.json)
var cover = new GroundCover(File.ReadAllText(Path.Combine(root, "assets/earth/config/ground-cover.json")));
bool Close(double a, double b) => Math.Abs(a - b) < 1e-9;
Check(Close(cover.TallGrassKept(.2), .15) && Close(cover.TallGrassKept(.3), .3) && Close(cover.TallGrassKept(.5), .7) && Close(cover.TallGrassKept(.75), 1),
    "Ground cover knots read as written");
Check(cover.TallGrassKept(.01) == 0 && cover.TallGrassKept(0) == 0 && cover.TallGrassKept(5) == 1, "End knots hold beyond the ends");
for (double ai = .02; ai < 3; ai *= 1.05) Check(cover.TallGrassKept(ai * 1.05) >= cover.TallGrassKept(ai), "Wetter land never keeps less grass (" + ai.ToString("F3") + ")");
Check(Close(cover.TallGrassKept(50, 0), cover.TallGrassKept(.2)), "Rain 50 is an aridity of 0.2 for the grass, as for the soil");
// the hash keeps the share it is given, and the same columns every time
foreach (double share in new[] { .15, .5, .7 })
{
    int kept = 0, n = 0;
    for (int x = -300; x < 300; x++) for (int z = -300; z < 300; z++) { n++; if (GroundCover.Keeps(x, z, share)) kept++; }
    Check(Math.Abs((double)kept / n - share) < .005, $"Keeps {share:P0} of columns (got {(double)kept / n:P2})");
}
Check(GroundCover.Keeps(1234, -5678, .5) == GroundCover.Keeps(1234, -5678, .5) && GroundCover.Keeps(7, 7, 1) && !GroundCover.Keeps(7, 7, 0), "Same column, same answer");
double Grass(double lat, double lon) { var c = data.SampleClimate(lat, lon); return cover.TallGrassKept(c.Rain, c.DrySummer); }
foreach (var (name, lat, lon, lo, hi) in new (string, double, double, double, double)[]
{
    ("Tucson", 32.22, -110.97, .15, .3), ("El Paso", 31.76, -106.49, .15, .3), ("Alice Springs", -23.7, 133.88, .15, .3), ("Murcia", 37.99, -1.13, .15, .3),
    ("Kazakh steppe", 50.3, 68.0, .4, .6), ("Athens", 37.98, 23.73, .4, .6), ("Madrid", 40.42, -3.70, .55, .75), ("Seville", 37.39, -5.98, .55, .75),
    ("Denver", 39.74, -104.99, .8, .95), ("Crimea steppe", 45.3, 34.0, .8, .95),
    ("Riga", 56.95, 24.1, 1, 1), ("Paris", 48.86, 2.35, 1, 1), ("Pampas", -35.5, -60.0, 1, 1), ("Hungarian Puszta", 47.5, 21.0, 1, 1),
})
{
    double g = Grass(lat, lon);
    Console.WriteLine($"{name,-15} tall grass kept {g:P0}");
    Check(g >= lo - 1e-9 && g <= hi + 1e-9, $"{name} keeps {lo:P0}-{hi:P0} of vanilla's tall grass (got {g:P0})");
}
// ---------------------------------------------------------------- grass colour by each place's growing season (GrassPhenology)
// monthly normals (°C, mm; January first) of a few stations, rounded from the WMO 1991-2020 normals
foreach (var (name, temps, precip, want) in new (string, double[], double[], int)[]
{
    ("Singapore", new double[] { 26.5, 27.1, 27.6, 28, 28.4, 28.3, 27.9, 27.8, 27.7, 27.6, 27, 26.5 }, new double[] { 220, 105, 170, 165, 160, 135, 150, 150, 135, 155, 255, 290 }, GrassPhenology.Evergreen),
    ("London", new double[] { 5.2, 5.3, 7.6, 9.9, 13.3, 16.5, 18.7, 18.5, 15.7, 12, 8, 5.5 }, new double[] { 55, 41, 42, 44, 49, 45, 45, 50, 49, 69, 59, 55 }, GrassPhenology.Evergreen),
    ("Seville", new double[] { 10.9, 12.4, 15.6, 17.6, 21.3, 25.6, 28.4, 28.1, 25, 20.4, 15, 11.8 }, new double[] { 66, 52, 40, 48, 26, 9, 1, 5, 23, 58, 85, 90 }, GrassPhenology.SummerDry),
    ("Astana", new double[] { -14, -14, -7, 5, 13, 19, 21, 19, 12, 4, -5, -12 }, new double[] { 16, 13, 16, 27, 35, 40, 50, 30, 22, 26, 22, 19 }, GrassPhenology.Steppe),
    ("Riga", new double[] { -2.4, -2.6, 0.6, 6.5, 12, 15.9, 18.4, 17.5, 13, 7.5, 2.8, -0.5 }, new double[] { 44, 36, 34, 37, 44, 64, 79, 83, 74, 68, 57, 47 }, 3),
    ("Yakutsk", new double[] { -38, -33, -20, -4, 7, 16, 19, 15, 6, -5, -27, -37 }, new double[] { 9, 8, 7, 11, 21, 36, 41, 44, 28, 21, 15, 10 }, 4),
})
{
    int map = GrassPhenology.MapFor(temps, precip);
    Console.WriteLine($"{name,-15} grass season {GrassPhenology.Codes[map]} (growing share {GrassPhenology.GrowingShare(temps):P0})");
    Check(map == want, $"{name}'s grass follows {GrassPhenology.Codes[want]} (got {GrassPhenology.Codes[map]})");
}

// ---------------------------------------------------------------- tall grass through the winter (GrassWinter)
Check(string.Join(",", GrassWinter.Heights) == "veryshort,short,mediumshort,medium,tall,verytall", "Grass heights in vanilla's order (worldproperties/block/tallgrass.json)");
Check(GrassWinter.AfterSnow(5) == 1 && GrassWinter.AfterSnow(3) == 1 && GrassWinter.AfterSnow(1) == 1 && GrassWinter.AfterSnow(0) == 0 && GrassWinter.AfterSnow(-1) == 1,
    "Snow leaves tufts short at most, never removes them, and keeps very short ones");
// vanilla's block layers (survival worldgen/blocklayers.json "tallgrass", first match)
Check(GrassWinter.TargetHeight(25, .6, .1) == 5 && GrassWinter.TargetHeight(17, .6, .35) == 4 && GrassWinter.TargetHeight(10, .5, .5) == 3
    && GrassWinter.TargetHeight(-5, .5, .7) == 2 && GrassWinter.TargetHeight(-5, .5, .9) == 1 && GrassWinter.TargetHeight(-12, .3, 0) == 0
    && GrassWinter.TargetHeight(-20, .5, 0) == -1 && GrassWinter.TargetHeight(25, .1, 0) == -1, "Regrowth targets vanilla's tall grass heights");
Check(GrassWinter.GrowChance(4.9, 9) == 0 && GrassWinter.GrowChance(double.NaN, 9) == 0, "No regrowth while the month is below 5 °C");
for (double t = 5; t < 30; t += 1) Check(GrassWinter.GrowChance(t + 1, 9) >= GrassWinter.GrowChance(t, 9), $"Warmer months regrow no slower ({t} °C)");
foreach (int days in new[] { 3, 9, 30 })
{
    double stages = GrassWinter.GrowChance(10, days) * days * 4.7;
    Check(Math.Abs(stages - 3) < 1e-9, $"About three stages a month at 10 °C with {days}-day months (got {stages:0.00})");
}
// bare grassy soil sprouts again where vanilla's world generation puts grass: most columns on open land, few under forest
double CoverShare(double forest, int fertility)
{
    int n = 0, k = 0;
    for (int x = 0; x < 200; x++) for (int z = 0; z < 200; z++) { n++; if (GrassWinter.Covers(x, z, forest, .6, 10, fertility)) k++; }
    return k / (double)n;
}
double open = CoverShare(.15, 90), wood = CoverShare(.9, 90);
Console.WriteLine($"Grass sprouts on {open:P0} of open prairie columns, {wood:P0} under forest");
Check(open > .7 && wood < .35 && open > 2 * wood, "Grass sprouts again mostly on open land, sparsely under forest");
Check(CoverShare(.15, 0) == 0, "No grass sprouts on soil vanilla gives no fertility");
// ---------------------------------------------------------------- soil rules 4: Earth soil (decision 0105, EA-0199)
{
    string rules4 = File.ReadAllText(Path.Combine(root, "assets/earth/config/soil-rules-v4.json"));
    var ts = new Topsoil(rules4, null, 1);
    const int V = Topsoil.VanillaTiers, R = Topsoil.RealisticTiers;
    Topsoil.Tier D(int tiers, string cls, bool arid = false, bool flood = false, double temp = 10, double wet = 0, bool dune = false, double warm = 20)
        => ts.Decide(tiers, new Topsoil.Place { Class = cls, Arid = arid, Flood = flood, Temperature = temp, Dune = dune, WarmestMonth = warm });
    Check(ts.Version == Topsoil.RulesVersion && Topsoil.RulesVersion > SoilFertility.RulesVersion, "Soil rules 4 follow rules 3");
    // the class table of section 4.1 (graded with the author, 19 November 2026)
    Check(D(R, "Chernozems") == Topsoil.Tier.BlackEarth && D(V, "Chernozems") == Topsoil.Tier.High, "Chernozems: black earth (realistic), high (vanilla tiers)");
    Check(D(R, "Phaeozems") == Topsoil.Tier.High && D(V, "Phaeozems") == Topsoil.Tier.High, "Phaeozems high in both");
    Check(D(R, "Nitisols") == Topsoil.Tier.High && D(V, "Nitisols") == Topsoil.Tier.Medium && D(R, "Andosols") == Topsoil.Tier.High && D(V, "Andosols") == Topsoil.Tier.Medium,
        "Nitisols and Andosols high (realistic), medium (vanilla tiers)");
    foreach (var c in new[] { "Cambisols", "Luvisols", "Kastanozems", "Vertisols", "Umbrisols", "Fluvisols" })
        Check(D(R, c) == Topsoil.Tier.Medium && D(V, c) == Topsoil.Tier.Medium, c + " medium in both");
    // Podzols low in both: poor, but farmed for centuries with manure (the author's rc.14 look near Riga, 21 November 2026)
    foreach (var c in new[] { "Acrisols", "Cryosols", "Regosols", "Calcisols", "Albeluvisols", "Gleysols", "Lixisols", "Alisols", "Planosols", "Solonetz", "Stagnosols", "Podzols" })
        Check(D(R, c) == Topsoil.Tier.Low && D(V, c) == Topsoil.Tier.Low, c + " low in both");
    foreach (var c in new[] { "Ferralsols", "Arenosols", "Leptosols", "Plinthosols", "Durisols", "Histosols" })
        Check(D(R, c) == Topsoil.Tier.Barren && D(V, c) == Topsoil.Tier.Low, c + " barren (realistic), low (vanilla tiers)");
    Check(D(R, "Gypsisols") == Topsoil.Tier.Barren && D(V, "Solonchaks") == Topsoil.Tier.Barren, "Gypsum and salt soils barren in both");
    // vanilla tiers: barren only on inhospitable land
    // vanilla tiers: barren only on inhospitable land; polar and alpine ground (beyond the tree line) barren in both sets
    foreach (var c in new[] { "Cambisols", "Leptosols", "Andosols", "Cryosols", "Chernozems" })
        Check(D(V, c, warm: ts.TundraWarmestBelow - .5) == Topsoil.Tier.Barren && D(R, c, warm: ts.TundraWarmestBelow - .5) == Topsoil.Tier.Barren, c + " beyond the tree line barren");
    Check(D(V, "Leptosols", warm: ts.TundraWarmestBelow + .5) == Topsoil.Tier.Low && D(V, "Cryosols", warm: 16) == Topsoil.Tier.Low && D(V, "Andosols", warm: 12) == Topsoil.Tier.Medium,
        "Below the tree line thin soils and permafrost soils keep their class's tier (vanilla tiers)");
    Check(D(V, "Cambisols", warm: double.NaN) == Topsoil.Tier.Medium, "An unknown warmest month never makes barren ground");
    Check(D(V, "Arenosols", dune: true) == Topsoil.Tier.Barren && D(V, "Arenosols") == Topsoil.Tier.Low && D(V, "Cambisols", dune: true) == Topsoil.Tier.Medium,
        "Dune sand barren, other sands low; a mapped dune unit does not touch other classes");
    // deserts, floods and oases
    foreach (var c in new[] { "Calcisols", "Cambisols", "Chernozems", "Leptosols" })
        Check(D(R, c, arid: true) == Topsoil.Tier.None && D(V, c, arid: true) == Topsoil.Tier.None, "Arid " + c + ": no topsoil");
    Check(D(R, "Fluvisols", arid: true, flood: true) == Topsoil.Tier.High && D(V, "Fluvisols", arid: true, flood: true) == Topsoil.Tier.High, "River silt in a flood high");
    foreach (var c in new[] { "Leptosols", "Arenosols", "Calcisols", "Regosols", "Podzols" })
        Check(D(R, c, arid: true, flood: true) == Topsoil.Tier.Medium && D(V, c, arid: true, flood: true) == Topsoil.Tier.Medium, "Fresh river silt at least medium over " + c);
    Check(D(R, "Solonchaks", arid: true, flood: true) == Topsoil.Tier.Low && D(V, "Gypsisols", arid: true, flood: true) == Topsoil.Tier.Low, "Salty silt low");
    Check(D(R, "Chernozems", flood: true) == Topsoil.Tier.BlackEarth && D(R, "Cambisols", flood: true) == Topsoil.Tier.Medium, "A flood never lowers a tier");
    Check(D(R, "Calcisols", arid: true, wet: 1) == Topsoil.Tier.None && D(V, "Fluvisols", arid: true, wet: 1) == Topsoil.Tier.None,
        "Arid ground outside a flood has none, whatever wetland GLWD's 0.1° cell holds (rc.12's oasis rule drew straight edges)");
    // where the rule says nothing: vanilla's rule (soil rules 3)
    Check(D(R, "Cambisols", temp: SoilFertility.MinTemperature - .5) == Topsoil.Tier.Vanilla && D(R, null) == Topsoil.Tier.Vanilla && D(Topsoil.ClimateOnly, "Chernozems") == Topsoil.Tier.Vanilla,
        "Colder than vanilla's grassed topsoil, unmapped or climate only: vanilla's rule");
    Check(D(V, "Nonexistentsols") == Topsoil.Tier.Low && D(R, "Nonexistentsols") == Topsoil.Tier.Low, "An unknown class takes the default");
    // every class SoilGrids maps has its own rule, and vanilla tiers never reach black earth
    var wrb = new[] { "Acrisols", "Albeluvisols", "Alisols", "Andosols", "Arenosols", "Calcisols", "Cambisols", "Chernozems", "Cryosols", "Durisols", "Ferralsols", "Fluvisols",
        "Gleysols", "Gypsisols", "Histosols", "Kastanozems", "Leptosols", "Lixisols", "Luvisols", "Nitisols", "Phaeozems", "Planosols", "Plinthosols", "Podzols", "Regosols",
        "Solonchaks", "Solonetz", "Stagnosols", "Umbrisols", "Vertisols" };
    string unknown = null;
    ts.UnknownClass += n => unknown = n;
    foreach (var c in wrb)
        foreach (bool arid in new[] { false, true }) foreach (bool flood in new[] { false, true }) foreach (bool dune in new[] { false, true }) foreach (double t in new[] { -9.0, -6.0, 0.5, 10.0 }) foreach (double warm in new[] { 5.0, 20.0 })
        {
            var v = D(V, c, arid, flood, t, 0, dune, warm);
            Check(v != Topsoil.Tier.BlackEarth && v != Topsoil.Tier.Vanilla, c + ": vanilla tiers stay within barren to high");
        }
    Check(unknown == null, "Every SoilGrids class has a rule (" + unknown + " has none)");
    // the layers' fertility against vanilla's blocklayers.json (1.22.7): low topsoil from 0.25, low subsoil 0.3, medium topsoil 0.5,
    // medium subsoil 0.6; no soil layer below 0.22
    int F(Topsoil.Tier t) => ts.LayerFertility(t, 0);
    Check(F(Topsoil.Tier.Barren) >= 77 && F(Topsoil.Tier.Barren) < 128 && F(Topsoil.Tier.Low) >= 77 && F(Topsoil.Tier.Low) < 128, "Barren and low are laid as low topsoil over low subsoil");
    Check(F(Topsoil.Tier.Medium) >= 153, "Medium is laid as medium topsoil over medium subsoil (a forest floor lies on medium soil)");
    Check(F(Topsoil.Tier.High) >= 153 && F(Topsoil.Tier.BlackEarth) >= 153, "High and black earth are laid as medium over medium subsoil");
    Check(ts.LayerFertility(Topsoil.Tier.None, 200) < 56 && ts.LayerFertility(Topsoil.Tier.None, 20) == 20 && ts.LayerFertility(Topsoil.Tier.Vanilla, 123) == 123,
        "No topsoil lies below vanilla's soil layer; vanilla's rule passes through");
    Check(Topsoil.Swapped(Topsoil.Tier.Barren) && Topsoil.Swapped(Topsoil.Tier.High) && Topsoil.Swapped(Topsoil.Tier.BlackEarth)
        && !Topsoil.Swapped(Topsoil.Tier.Low) && !Topsoil.Swapped(Topsoil.Tier.Medium) && !Topsoil.Swapped(Topsoil.Tier.None) && !Topsoil.Swapped(Topsoil.Tier.Vanilla),
        "The pass swaps only the tiers vanilla's layers never lay");
    Check(string.Join(",", Topsoil.BlockCodes) == "verylow,low,medium,compost,high", "Tiers in the soil blocks' fertility codes, poorest first");
    Check(ts.FloodRain >= .12 && ts.FloodRain < .33, "A flood's rain lays grassed soil (vanilla's 0.12) without passing for a wet climate (0.33)");
    // the 2' maps and the dune unit come from the data store; the 30" set is optional
    var tiles = Topsoil.TileList(rules4);
    Check(tiles.Count(x => x.File != null) == 1 && tiles.Single(x => x.File != null).Optional && tiles.Where(x => x.Asset != null).All(x => !x.Optional), "Only the 30\" set is optional");
}
Console.WriteLine($"SoilTests: {passed} checks passed");
return 0;
