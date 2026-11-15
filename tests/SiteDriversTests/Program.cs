using System.Globalization;
using EarthMap.Core;

// Offline checks of the site drivers (src/Core/SiteDrivers.cs; EA-0054 step 1, habitat proposal docs/research/habitats-2026-11-06).
// Usage: dotnet run --project tests/SiteDriversTests
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) failures++; }
const double Deg = Math.PI / 180;

// 1. flat daily Ra: numerical integration vs FAO-56 eq 21 (omega_s by eq 25)
double worst = 0; string worstAt = "";
foreach (double lat in new[] { -60, -30, 0, 30, 57, 66.5, 80 })
    foreach (int day in new[] { 1, 80, 172, 266, 355 })
    {
        double closed = SolarRadiation.DailyRaFlat(lat, day), num = SolarRadiation.DailyRa(lat, 0, 0, day);
        if (closed <= 1) continue;
        double err = Math.Abs(num - closed) / closed;
        if (err > worst) { worst = err; worstAt = $"lat {lat} day {day}: {num:0.000} vs {closed:0.000}"; }
    }
Console.WriteLine($"flat daily Ra, integration vs closed form: worst {worst * 100:0.000} % ({worstAt})");
Check(worst < 0.01, "flat daily Ra within 1 % of FAO-56 eq 21");

// 2. annual flat Ra
double AnnualRa(double lat, double slope = 0, double aspect = 0) { double s = 0; for (int d = 1; d <= 365; d++) s += SolarRadiation.DailyRa(lat, slope, aspect, d); return s; }
double AnnualClosed(double lat) { double s = 0; for (int d = 1; d <= 365; d++) s += SolarRadiation.DailyRaFlat(lat, d); return s; }
foreach (double lat in new[] { 0, 30, 45, 57, 60, 66.5, 80, -30, -60 })
    Console.WriteLine($"annual flat Ra at {lat,5}: integrated {AnnualRa(lat),7:0} MJ/m2, closed form {AnnualClosed(lat),7:0}");
double eq = AnnualRa(0), n60 = AnnualRa(60);
Check(eq >= 12900 && eq <= 13500, $"equator annual Ra {eq:0} in 12,900-13,500");
Check(n60 >= 7100 && n60 <= 7600, $"60N annual Ra {n60:0} in 7,100-7,600");
Check(Math.Abs(AnnualRa(57) - 7830) / 7830 < 0.01, $"57N annual Ra {AnnualRa(57):0} within 1 % of 7,830");

// 3. slopes: 30 degrees, south- and north-facing
double flat57 = AnnualRa(57), s57 = AnnualRa(57, 30 * Deg, Math.PI) / flat57, nn57 = AnnualRa(57, 30 * Deg, 0) / flat57;
double flat30s = AnnualRa(-30), n30s = AnnualRa(-30, 30 * Deg, 0) / flat30s, s30s = AnnualRa(-30, 30 * Deg, Math.PI) / flat30s;
double e57 = AnnualRa(57, 30 * Deg, Math.PI / 2) / flat57, w57 = AnnualRa(57, 30 * Deg, 1.5 * Math.PI) / flat57;
Console.WriteLine($"57N 30deg slope / flat: south {s57:0.000}, north {nn57:0.000}, east {e57:0.000}, west {w57:0.000}");
Console.WriteLine($"30S 30deg slope / flat: north {n30s:0.000}, south {s30s:0.000}");
Check(s57 > 1.15 && Math.Abs(s57 - 1.47) < 0.02, "57N south-facing clearly sunnier than flat (~1.47)");
Check(nn57 < 0.85 && Math.Abs(nn57 - 0.50) < 0.02, "57N north-facing clearly shadier than flat (~0.50)");
Check(n30s > 1.05 && s30s < 0.9 && Math.Abs(n30s - 1.12) < 0.02 && Math.Abs(s30s - 0.64) < 0.02, "30S: north-facing is the sunny side (~1.12 / ~0.64)");
Check(Math.Abs(e57 - w57) < 0.005, "east and west slopes symmetric");

// 4. polar day and night
double p172 = SolarRadiation.DailyRa(80, 0, 0, 172), e172 = SolarRadiation.DailyRa(0, 0, 0, 172), m172 = SolarRadiation.DailyRa(57, 0, 0, 172), p355 = SolarRadiation.DailyRa(80, 0, 0, 355);
Console.WriteLine($"day 172 Ra: 80N {p172:0.0}, 57N {m172:0.0}, equator {e172:0.0}; day 355 at 80N {p355:0.000}");
Check(p172 > 0 && p172 > e172, "80N day 172: Ra above the equator's");
Check(p355 == 0, "80N day 355: polar night, Ra = 0");
Check(Math.Abs(p172 - 44.7) < 0.45 && Math.Abs(e172 - 33.4) < 0.35 && Math.Abs(m172 - 41.4) < 0.42, "day 172 values within ~1 % of 44.7 / 41.4 / 33.4");
Check(SolarRadiation.DailyRa(-80, 0, 0, 355) > 40 && SolarRadiation.DailyRa(-80, 0, 0, 172) == 0, "80S: polar day on day 355, polar night on day 172");
Check(SolarRadiation.DailyRa(80, 45 * Deg, 0, 172) > 0, "80N day 172: a north-facing slope gets midnight sun");

// 5. heat
double riga = Heat.DegreeDays(6.9, -3.1, 17.9, 57), tundra = Heat.DegreeDays(-8, -25, 8, 70), tropic = Heat.DegreeDays(26, 26, 26, 0);
double rigaSouth = Heat.DegreeDays(6.9, -3.1, 17.9, -57);
Console.WriteLine($"GDD5: Riga-like {riga:0}, tundra-like {tundra:0}, tropical {tropic:0}, Riga mirrored south {rigaSouth:0}");
Check(riga >= 1450 && riga <= 1900, "Riga-like GDD5 in 1,450-1,900");
Check(tundra > 0 && tundra < 400, "tundra-like GDD5 small");
Check(Math.Abs(tropic - 7665) < 50, "tropical GDD5 ~7,665 (21 x 365)");
Check(Math.Abs(riga - rigaSouth) < 1, "GDD5 does not depend on the hemisphere");
Check(Heat.DayTemperature(6.9, -3.1, 17.9, 57, 200) > Heat.DayTemperature(6.9, -3.1, 17.9, 57, 20)
    && Heat.DayTemperature(6.9, -3.1, 17.9, -35, 20) > Heat.DayTemperature(6.9, -3.1, 17.9, -35, 200), "warm season day 200 north, day 20 south");

// 6. light with a growing season; north vs south hemisphere phase
var rigaLight = SolarRadiation.Clear(57, 0, 0, 10, Heat.Cycle(6.9, -3.1, 17.9, 57));
var mirrorLight = SolarRadiation.Clear(-57, 0, 0, 10, Heat.Cycle(6.9, -3.1, 17.9, -57));
var wrongPhase = SolarRadiation.Clear(-57, 0, 0, 10, Heat.Cycle(6.9, -3.1, 17.9, 57));
Console.WriteLine($"57N flat, 10 m: clear-sky {rigaLight.Annual:0} MJ/m2/yr, growing season {rigaLight.Growing:0} over {rigaLight.GrowingDays} days; 57S mirrored {mirrorLight.Growing:0}; 57S with a northern phase {wrongPhase.Growing:0}");
// not an exact mirror: perihelion is in early January, so southern summers get a few per cent more (FAO-56 dr)
Check(Math.Abs(rigaLight.Growing - mirrorLight.Growing) / rigaLight.Growing < 0.06, "growing-season light mirrors between hemispheres (within the perihelion effect)");
Check(wrongPhase.Growing < 0.6 * mirrorLight.Growing, "a northern phase in the south would put the season in the dark (phase matters)");
Check(Math.Abs(rigaLight.Annual / AnnualRa(57) - SolarRadiation.ClearSkyFactor(10)) < 0.005, "clear-sky = (0.75 + 2e-5 z) Ra");

// 7. the cheap table against the exact path
double worstAnnual = 0, worstIndex = 0, worstGrowing = 0; string worstSite = "";
var rnd = new Random(7);
for (int k = 0; k < 60; k++)
{
    double lat = -75 + 150 * rnd.NextDouble(), slope = 45 * Deg * rnd.NextDouble(), aspect = 2 * Math.PI * rnd.NextDouble();
    var cyc = Heat.Cycle(5, -10, 20, lat);
    var ex = SolarRadiation.Clear(lat, slope, aspect, 200, cyc); var tb = SolarTable.Clear(lat, slope, aspect, 200, cyc);
    double ea = Math.Abs(tb.Annual - ex.Annual) / ex.Annual;
    if (ea > worstAnnual) { worstAnnual = ea; worstSite = $"lat {lat:0.0}, slope {slope / Deg:0}, aspect {aspect / Deg:0}"; }
    worstIndex = Math.Max(worstIndex, Math.Abs(tb.Index - ex.Index));
    if (ex.Growing > 0) worstGrowing = Math.Max(worstGrowing, Math.Abs(tb.Growing - ex.Growing) / ex.Growing);
}
Console.WriteLine($"table vs exact, 60 random sites (|lat| < 75, slope < 45 deg): annual worst {worstAnnual * 100:0.00} % ({worstSite}), growing worst {worstGrowing * 100:0.00} %, index worst {worstIndex:0.000}");
Check(worstAnnual < 0.01 && worstIndex < 0.01 && worstGrowing < 0.01, "table within 1 % annual, 1 % growing, 0.01 index");
double worstPolar = 0;
foreach (double lat in new[] { -88.5, -80.2, -70.7, 66.3, 72.5, 79.9, 85.1 })
    foreach (double sl in new[] { 0, 20, 45 })
        foreach (double az in new[] { 0, 100, 180, 260 })
        {
            var ex = SolarRadiation.Clear(lat, sl * Deg, az * Deg, 0, null); var tb = SolarTable.Clear(lat, sl * Deg, az * Deg, 0, null);
            worstPolar = Math.Max(worstPolar, Math.Abs(tb.Annual - ex.Annual) / ex.Annual);
        }
Console.WriteLine($"table vs exact, polar latitudes 66-89, slopes 0-45: annual worst {worstPolar * 100:0.00} %");
Check(worstPolar < 0.03, "table within 3 % annual at polar latitudes");

// 8. soil rules
SiteSoil.Input Soil(string group, string cover, double clay, double ph, double q = 10) => new()
{
    Known = true, Group = group, Cover = cover, Clay = clay, Ph = ph, QuaternaryMetres = q,
};
var podzol = SiteSoil.Classify(Soil("Podzols", "outwash", 5, 4.5));
var luvisol = SiteSoil.Classify(Soil("Luvisols", "till", 30, 7));
var histosol = SiteSoil.Classify(Soil("Histosols", "peat", 10, 4));
var fen = SiteSoil.Classify(Soil("Histosols", "peat", 10, 6));
var albLight = SiteSoil.Classify(Soil("Albeluvisols", "till", 15, 5.5));
var albHeavy = SiteSoil.Classify(Soil("Albeluvisols", "till", 22, 5.5));
var thin = SiteSoil.Classify(Soil("Leptosols", "thin", 15, 6, 0.3));
var unknown = SiteSoil.Classify(SiteSoil.Input.Unknown);
Console.WriteLine($"Podzol/outwash: {podzol.Describe()}; Luvisol clay 30 pH 7: {luvisol.Describe()}; Histosol pH 4: {histosol.Describe()}; pH 6: {fen.Describe()}");
Console.WriteLine($"Albeluvisol clay 15: {albLight.Describe()}; clay 22: {albHeavy.Describe()}; Leptosol 0.3 m: {thin.Describe()}; unknown: {unknown.Describe()}");
Check(podzol.Texture == SiteSoil.Texture.Sand && podzol.Richness == 0 && podzol.Permeability == 2, "Podzol on outwash -> sand, A, free-draining");
Check(luvisol.Texture == SiteSoil.Texture.Clay && luvisol.Richness == 3 && luvisol.Permeability == 0, "Luvisol clay 30 pH 7 -> clay, D, tight");
Check(histosol.Texture == SiteSoil.Texture.Peat && histosol.Richness == 0, "Histosol pH 4 -> peat, A");
Check(fen.Texture == SiteSoil.Texture.Peat && fen.Richness == 2, "Histosol pH 6 -> peat, C");
var bogOnOutwash = SiteSoil.Classify(Soil("Histosols", "outwash", 15, 5.1));
Check(bogOnOutwash.Texture == SiteSoil.Texture.Peat && bogOnOutwash.Richness == 1 && bogOnOutwash.Permeability == 0, "Histosol pH 5.1 on outwash (Teiči) -> peat, B, not free-draining");
Check(albLight.Richness == 1 && albHeavy.Richness == 2, "Albeluvisols B when light (clay <= 17), else C");
Check(thin.Texture == SiteSoil.Texture.ShallowOverRock, "Quaternary < 0.5 m -> shallow over rock");
Check(!unknown.Known && unknown.Texture == SiteSoil.Texture.Loam && unknown.Richness == 2 && unknown.Permeability == 1, "unknown soil -> loam, C, medium");
Check(SiteSoil.Classify(Soil("Stagnosols", "till", 18, 5.5)).Permeability == 0 && SiteSoil.Classify(Soil("Cambisols", "lakeclay", 20, 6)).Permeability == 0, "Stagnosols and lake clay are tight");
Check(SiteSoil.Classify(Soil("Phaeozems", "loess", 20, 7)).Richness == 3 && SiteSoil.Classify(Soil("Cambisols", "till", 20, 5.5)).Richness == 2, "Phaeozems D, acid Cambisol C");

// 9. wetness
var gleyIn = Soil("Gleysols", "till", 20, 6);
var gley = SiteSoil.Classify(gleyIn);
var gleyDeep = SiteWetness.FromTable(5, 3, gleyIn, gley, false);
var gleyShallow = SiteWetness.FromTable(1.2, 1, gleyIn, gley, false);
var gleyUnknown = SiteWetness.FromTable(double.NaN, double.NaN, gleyIn, gley, false);
Console.WriteLine($"Gleysol, table 5 m: {gleyDeep.Describe()}; 1.2 m: {gleyShallow.Describe()}; unknown: {gleyUnknown.Describe()}");
Check(gleyDeep.Class >= 3 && gleyShallow.Class >= 4 && gleyUnknown.Class == 3 && gleyUnknown.AtLeast, "Gleysol -> wetness >= 3 (>= 4 within 1.5 m)");
var podIn = Soil("Podzols", "outwash", 5, 4.5);
var dune = Soil("Arenosols", "dune", 3, 5);
var loam = Soil("Cambisols", "till", 18, 6);
var loamS = SiteSoil.Classify(loam);
int W(double depth, double hand, SiteSoil.Input s) => SiteWetness.FromTable(depth, hand, s, SiteSoil.Classify(s), false).Class;
Check(W(5, 10, podIn) == 1 && W(5, 25, podIn) == 0 && W(5, 5, dune) == 0 && W(5, 2, dune) == 1, "dry and very dry sands (dune > 3 m, Podzol > 20 m above drainage)");
Check(W(5, 5, loam) == 2 && W(1.5, 1, loam) == 3 && W(0.5, 1, loam) == 4 && W(0.1, 0, loam) == 5 && W(-0.2, 0, loam) == 6, "fresh, moist, wet, waterlogged, flooded by table depth");
Check(W(5, 5, Soil("Histosols", "peat", 10, 4)) == 5, "Histosols at least waterlogged");
Check(SiteWetness.FromTable(double.NaN, double.NaN, loam, loamS, false).Class == -1, "no water table, plain soil -> unknown");
Check(SiteWetness.FromTable(1, 1, loam, loamS, true).Class == 7, "open water -> 7");
Check(W(5, 5, Soil("Podzols", "peat-on-till", 10, 4.5)) == 5, "peat cover over sand: at least waterlogged");
Check(SiteSoil.Classify(Soil("Ferralsols", "saprolite", 40, 5)).Richness == 0 && SiteSoil.Classify(Soil("Chernozems", "loess", 25, 7.2)).Richness == 3
    && SiteSoil.Classify(Soil("Leptosols", "thin", 10, 6, 0.2)).Richness == 1, "Ferralsols A, Chernozems D, Leptosols B (widened groups)");

// 10. water supply and disturbance
double aiRiga = WaterSupply.AridityIndex(125, 0), aiDesert = WaterSupply.AridityIndex(10, 0), aiMed = WaterSupply.AridityIndex(90, 0.93);
Console.WriteLine($"aridity index: rain 125 -> {aiRiga:0.00} ({WaterSupply.AridityNames[WaterSupply.AridityClass(aiRiga)]}), rain 10 -> {aiDesert:0.000} ({WaterSupply.AridityNames[WaterSupply.AridityClass(aiDesert)]}), rain 90 dry summer .93 -> {aiMed:0.00} ({WaterSupply.AridityNames[WaterSupply.AridityClass(aiMed)]})");
Check(WaterSupply.AridityClass(aiRiga) == 4 && WaterSupply.AridityClass(aiDesert) == 0, "Riga humid, rain 10 hyper-arid");
double fireSandBoreal = Disturbance.FireIndex(125, 0, podzol), fireMed = Disturbance.FireIndex(90, 0.93, SiteSoil.Classify(Soil("Cambisols", "thin", 20, 7))),
    fireClay = Disturbance.FireIndex(125, 0, luvisol), fireDesert = Disturbance.FireIndex(10, 0, podzol);
Console.WriteLine($"fire: boreal sand {fireSandBoreal:0.00}, Mediterranean {fireMed:0.00}, humid clay {fireClay:0.00}, desert {fireDesert:0.00}");
Check(fireMed > fireSandBoreal && fireSandBoreal > fireClay && fireDesert < 0.01, "fire: Mediterranean > boreal sand > humid clay; no fuel in the desert");
var dist = Disturbance.Compute(150, 0.8, 40, 125, 0, podzol);
Check(dist.CoastClass == "shore" && dist.Flood == 2 && Disturbance.Compute(double.NaN, 2, 100, 125, 0, podzol).Flood == 1, "coast and flood classes");

// 11. the whole record
var input = SiteInput.Create(56.95, 24.10);
input.ElevationM = 10; input.TMean = 6.9; input.TCold = -3.1; input.TWarm = 17.9; input.Rain = 125; input.DrySummer = 0;
input.SlopeRad = 3 * Deg; input.AspectRad = Math.PI; input.Soil = podIn; input.WaterTableDepthM = 4; input.HeightAboveDrainageM = 8; input.WetlandShare = 0; input.SeaDistanceM = 1500;
var site = SiteDrivers.Compute(input);
Console.WriteLine("--- site record, Riga-like dune sand ---");
Console.WriteLine(site.Describe());
Check(site.Wetness.Class == 1 && site.SiteName == "dry heath", "Podzol on outwash, table 4 m down: dry, A -> dry heath");
var bog = input; bog.Soil = Soil("Histosols", "peat", 5, 4.2); bog.WaterTableDepthM = 0.1; bog.HeightAboveDrainageM = 0.5;
var carr = input; carr.Soil = Soil("Gleysols", "alluvium", 30, 6.8); carr.WaterTableDepthM = 0.2;
var rich = input; rich.Soil = Soil("Luvisols", "till", 28, 6.5); rich.WaterTableDepthM = 5;
Check(SiteDrivers.Compute(bog).SiteName == "raised bog" && SiteDrivers.Compute(carr).SiteName == "rich fen or alder carr" && SiteDrivers.Compute(rich).SiteName == "broadleaf forest",
    "site types: acid peat at the table -> raised bog; rich gley at the table -> alder carr; fresh base-rich clay loam -> broadleaf forest");
Check(SiteDrivers.SiteTypeName(-1, 2) == "unknown wetness" && SiteDrivers.SiteTypeName(7, 0) == "open water", "unknown and open water names");
var podClay = SiteSoil.Classify(Soil("Podzols", "lakeclay", 25, 5.9));
Check(podClay.Permeability == 0 && podClay.Texture == SiteSoil.Texture.Clay && podClay.Richness == 0, "Podzol on lake clay (Kemeri, in game): clay, tight, A (not free-draining sand)");
int Arid(double ai) => SiteWetness.FromTable(60, 10, loam, loamS, false, ai).Class;
Check(Arid(0.01) == 0 && Arid(0.3) == 1 && Arid(0.9) == 2 && SiteWetness.FromTable(0.5, 1, loam, loamS, false, 0.01).Class == 4,
    "deep table: hyper-arid very dry, semi-arid dry, humid fresh; a shallow table stays wet in a desert (oasis)");
Check(SiteDrivers.SiteTypeName(0, 1, 0.01, 7000) == "desert" && SiteDrivers.SiteTypeName(1, 3, 0.3, 3000) == "steppe or dry savanna"
    && SiteDrivers.SiteTypeName(2, 2, 1.2, 300) == "tundra" && SiteDrivers.SiteTypeName(5, 0, 0.01, 7000) == "raised bog", "names by climate where it allows no closed forest");
input.FastLight = true;
var fast = SiteDrivers.Compute(input);
Check(Math.Abs(fast.Light.Annual - site.Light.Annual) / site.Light.Annual < 0.02, "fast light path agrees with the exact one");

// 12. site rules 1 (EA-0054 step 2): a column's site on EIVE's scales and its productivity
Check(SiteNiche.Moisture(2, 1.0) == 4.6f && SiteNiche.Moisture(5, 1.0) == 7.2f && SiteNiche.Moisture(5, 0.01) == 7.2f,
    "EIVE moisture: fresh 4.6, waterlogged 7.2 whatever the climate");
Check(SiteNiche.Moisture(2, 0.05) < SiteNiche.Moisture(2, 1.0) && SiteNiche.Moisture(2, 3.0) == 5.6f && SiteNiche.Moisture(0, 0.01) >= 0.5f,
    "deep tables: a dry climate dries fresh ground, a very humid one moistens it by at most 1");
Check(SiteNiche.Moisture(0, 0) <= SiteNiche.Moisture(0, 0.05) && SiteNiche.Moisture(1, 0) == SiteNiche.Moisture(1, 0.01) && float.IsFinite(SiteNiche.Moisture(2, double.NaN)),
    "no rain at all is the driest, not unshifted (review of step 2)");
Check(SiteNiche.Nutrients(0) < SiteNiche.Nutrients(1) && SiteNiche.Nutrients(3) == 6.5f, "EIVE nutrients rise with richness A..D");
Check(SiteNiche.Productivity(2, 3) == 1f && SiteNiche.Productivity(5, 0) < 0.6f && SiteNiche.Productivity(1, 0) < SiteNiche.Productivity(2, 2),
    "productivity: full on fresh rich ground, stunted on a raised bog, less on dry poor sand");
var bogCol = SiteNiche.From(0.1, 0.5, false, Soil("Histosols", "peat", 5, 4.2), 1.1);
var deep = SiteNiche.From(double.NaN, double.NaN, false, loam, 1.0);
Check(bogCol.Ok && bogCol.Wetness == 5 && bogCol.Richness == 0 && bogCol.Ph == 4.2f && bogCol.Wetness >= SiteNiche.WetGround && !deep.Ok,
    "a peat column at the table: waterlogged, A, its pH, wet ground; no table: no site");
Check(SiteNiche.From(-0.5, 0, true, loam, 1.0).OpenWater, "open water column");

var noRain = SiteInput.Create(23, 10); noRain.TMean = 25; noRain.TCold = 15; noRain.TWarm = 33; noRain.Rain = 0; noRain.AridityIndex = 0; noRain.WaterTableDepthM = 60; noRain.HeightAboveDrainageM = 10;
Check(SiteDrivers.Compute(noRain).AridityIndex == 0 && SiteDrivers.Compute(noRain).Wetness.Class == 0, "a climate aridity of 0 (no rain) is taken as given, as the trees take it");

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
