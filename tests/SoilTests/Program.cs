using EarthMap.Core;

// Earth's soil fertility floor (decision 0048): land that isn't arid by the real aridity index has soil.
string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
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
Console.WriteLine($"SoilTests: {passed} checks passed");
