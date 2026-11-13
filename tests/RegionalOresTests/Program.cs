using EarthMap.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

// Usage: dotnet run -- [repoRoot] [--write-results]
string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Directory.GetCurrentDirectory();
bool write = args.Contains("--write-results");
int checks = 0;
var report = new Dictionary<string, object>();
void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL: " + label); }
static double Clamp01(double v) => Math.Clamp(v, 0, 1);

var json = File.ReadAllText(Path.Combine(root, "assets/earth/config/ore-heatmaps-v1.json"));
var cat = new RegionalOreCatalog(json);
var geology = new RegionalGeologyCatalog(File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-grids-v1.json")), File.ReadAllText(Path.Combine(root, "assets/earth/config/geology-rules-v1.json")));
var tuning = new OreTuning();

// ---------------------------------------------------------------- data and bindings
Check(cat.Layers.Count == 44, "42 resources plus peat/clay heat layers");
Check(RegionalOreCatalog.Version == 3 && RegionalOreCatalog.DataVersion == 1, "rules v3 on the unchanged v1 data format");
Check(cat.ResourceFor("surfacecopper") == "Cu" && cat.ResourceFor("cassiterite") == "Sn", "Progression ore bindings");
Check(cat.ResourceFor("bogironlarge") == null, "Bog iron excluded from bedrock adapter");
Check(cat.ResourceFor("earth-bauxite") == "Al", "Bauxite gets a deposit map, not an unrestricted stratum");
Check(cat.At("Cu", 57, 25) == cat.At("Cu", 57, 385), "Longitude wrapping (nearest)");
Check(cat.At("Cu", 57, 25, true).Any == 0 && cat.At("Cu", 91, 25).Any == 0, "Artificial gutter and poles excluded");
Check(cat.At("C_coal", 53, -1).Any > 0, "British coal evidence retained");
Check(cat.At("C_coal", -32.8, 151.5).Any > 0, "Australian coal evidence retained");
Check(cat.At("gypsum", 56.9, 24.4).Any > 0, "Latvian gypsum retained");
Check(cat.At("Sn", 50.2, -5.2).Any > 0, "Cornwall tin evidence retained");
var deep = new RegionalOreCatalog.Evidence(.1, .2, .8, 700);
Check(deep.Accessible(10, 5) == .2 && deep.Accessible(139, 5) == .2 && deep.Accessible(140, 5) == .8, "700 m evidence only at 140 blocks under 5 m/block");
bool rejected = false; try { new RegionalOreCatalog(json.Replace("\"version\":1", "\"version\":9")); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Unsupported data format fails visibly (caller disables the subsystem)");
// Fingerprint: content hash ignores labels / dataset id, reacts to bindings.
var relabel = new RegionalOreCatalog(json.Replace("\"label\":\"Iron\"", "\"label\":\"Iron (renamed)\"").Replace(cat.DatasetId, "other-dataset-id"));
Check(relabel.ContentHash == cat.ContentHash && new RegionalOreCatalog(json).ContentHash == cat.ContentHash, "Content hash is stable and ignores labels/dataset id");
Check(new RegionalOreCatalog(json.Replace("\"surfacecassiterite\"", "\"surfacecassiterite2\"")).ContentHash != cat.ContentHash, "Content hash reacts to deposit bindings");

// ---------------------------------------------------------------- class assignment of every bound deposit code
var classes = new Dictionary<string, OreClass>();
foreach (var layer in cat.Layers.Values)
    foreach (string d in layer.Deposits ?? Array.Empty<string>())
    {
        var cls = OreRules.Classify(d, layer.Code);
        classes[d] = cls;
        var expected = d switch
        {
            "clay" or "redclay" or "peat" => OreClass.Soil,
            "surfacecopper" or "surfacecassiterite" => OreClass.Surface,
            "quartz" => OreClass.Veins,
            _ when layer.Code is "Cu" or "Sn" or "Fe" or "C_coal" => OreClass.Progression,
            _ => OreClass.Standard,
        };
        Check(cls == expected, "class of " + d + " (" + layer.Code + ") is " + expected);
        foreach (bool child in new[] { false, true })
            Check(tuning.For(layer.Code, cls, child).IsValid(out _), "valid default profile " + d + (child ? " (child)" : ""));
    }
foreach (string d in new[] { "nativecopper", "malachite", "cassiterite", "limonite", "hematite", "magnetite", "lignite", "bituminouscoal", "anthracite" }) Check(classes[d] == OreClass.Progression, d + " is a progression ore");
Check(classes.Count == cat.Layers.Values.Sum(l => l.Deposits?.Length ?? 0) && classes.Count > 70, "every bound deposit code classified (" + classes.Count + ")");
Check(OreRules.KeepsNativeDepth("clay", OreClass.Soil) && OreRules.KeepsNativeDepth("peat", OreClass.Soil) && OreRules.KeepsNativeDepth("earth-bauxite", OreClass.Standard)
    && OreRules.KeepsNativeDepth("surfacecopper", OreClass.Surface) && !OreRules.KeepsNativeDepth("cassiterite", OreClass.Progression), "soil, surface and bauxite keep native burial");
report["classes"] = classes.GroupBy(p => p.Value).ToDictionary(g => g.Key.ToString(), g => g.Count());

// ---------------------------------------------------------------- tuning validation and save-safe fallback
string userSaved = "{\"BackgroundChance\":0.08,\"HotspotChance\":1,\"FrequencyMultiplier\":0.4,\"RadiusMultiplier\":1.6,\"ThicknessMultiplier\":1.25,\"Concentration\":0.85,\"QualityBias\":0,\"DepthMultiplier\":1,\"MinDepthBlocks\":0,\"MaxDepthBlocks\":0,\"MetresPerBlock\":5,\"SurfaceFrequencyMultiplier\":1,\"BogIronChance\":0.025,\"Resources\":{}}";
var loaded = OreTuning.Load(userSaved, cat.Layers.Keys, out string problem);
Check(problem == null && JsonSerializer.Serialize(loaded) == JsonSerializer.Serialize(new OreTuning()), "the existing worlds' saved tuning loads unchanged");
var bad = OreTuning.Load("{not json", cat.Layers.Keys, out problem);
Check(problem != null && JsonSerializer.Serialize(bad) == JsonSerializer.Serialize(new OreTuning()), "corrupt tuning JSON falls back to defaults");
bad = OreTuning.Load("{\"BackgroundChance\":2}", cat.Layers.Keys, out problem);
Check(problem != null && bad.BackgroundChance == .08, "out-of-range tuning falls back to defaults");
bad = OreTuning.Load("{\"Resources\":{\"Cu\":{\"MinDepthBlocks\":30,\"MaxDepthBlocks\":20}}}", cat.Layers.Keys, out problem);
Check(problem != null && bad.Resources.Count == 0, "invalid per-resource depth interval falls back to defaults");
var partial = OreTuning.Load("{\"FrequencyMultiplier\":0.5,\"Resources\":{\"Xx\":{\"Concentration\":0.5},\"Sn\":{\"BackgroundChance\":0.3}}}", cat.Layers.Keys, out problem);
Check(problem != null && problem.Contains("Xx") && !partial.Resources.ContainsKey("Xx") && partial.Resources["Sn"].BackgroundChance == .3 && partial.FrequencyMultiplier == .5, "unknown override dropped, the rest kept");
Check(OreTuning.Load(null, cat.Layers.Keys, out problem) != null && problem == null, "missing tuning means defaults");
rejected = false; try { new OreTuning { Resources = { ["Cu"] = new OreOverride { MinDepthBlocks = 30, MaxDepthBlocks = 20 } } }.Validate(cat.Layers.Keys); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Validate still throws for tools/tests");
var custom = new OreTuning { Resources = { ["Cu"] = new OreOverride { MinDepthBlocks = 30, MaxDepthBlocks = 80, Concentration = .5, BackgroundChance = .5 } } };
Check(custom.For("Cu", OreClass.Progression).Depth(10) == 30 && custom.For("Cu", OreClass.Progression).Depth(200) == 80 && custom.For("Sn", OreClass.Progression).Concentration == .85, "per-resource overrides isolated");
Check(custom.For("Cu", OreClass.Progression).Background == .5, "per-resource override wins over the built-in class rule");
var soilDepth = new OreTuning { MinDepthBlocks = 40, Resources = { ["clay"] = new OreOverride { MinDepthBlocks = 25 } } };
Check(soilDepth.For("clay", OreClass.Soil).MinDepth == 0 && soilDepth.For("peat", OreClass.Soil).MinDepth == 0 && soilDepth.For("Cu", OreClass.Progression).MinDepth == 40, "MinDepthBlocks never applies to soil");

// ---------------------------------------------------------------- vertical model: deposits above ground are skipped
var pd = tuning.For("Fe", OreClass.Progression);
Check(double.IsNaN(OreRules.Burial(OreAnchor.AbsoluteY, 100, 80, pd, false)), "sea-level deposit above an 80-block seabed is skipped (0.12 clamped it under the seabed)");
Check(double.IsNaN(OreRules.Burial(OreAnchor.SurfaceFraction, 1.02, 120, pd, false)), "surface-fraction deposit above ground is skipped");
Check(OreRules.Burial(OreAnchor.AbsoluteY, 60, 80, pd, false) == 20 && OreRules.Burial(OreAnchor.SurfaceFraction, .75, 120, pd, false) == 30, "regular burial");
Check(OreRules.Burial(OreAnchor.BelowSurface, 0, 100, soilDepth.For("clay", OreClass.Soil), true) == 0, "soil stays at the surface");
var deepTuning = new OreTuning { MinDepthBlocks = 25 }.For("Pb", OreClass.Standard);
Check(OreRules.Burial(OreAnchor.AbsoluteY, 79, 80, deepTuning, false) == 25 && double.IsNaN(OreRules.Burial(OreAnchor.AbsoluteY, 81, 80, deepTuning, false)), "MinDepth applies to rock deposits, never resurrects above-ground ones");

// ---------------------------------------------------------------- textures (mean 1, bounded, deterministic)
double softSum = 0, softMin = 9, softMax = 0, cluSum = 0, cluMin = 9, cluMax = 0; int tn = 0;
for (int z = -300; z < 300; z++) for (int x = -300; x < 300; x++)
{
    double s = OreRules.SoftTexture(4242, x, z, 77, OreRules.SoftAmplitude), c = OreRules.ClusteredTexture(4242, x, z, 77);
    softSum += s; softMin = Math.Min(softMin, s); softMax = Math.Max(softMax, s);
    cluSum += c; cluMin = Math.Min(cluMin, c); cluMax = Math.Max(cluMax, c); tn++;
}
Check(Math.Abs(softSum / tn - 1) < .02 && softMin >= 1 - OreRules.SoftAmplitude - 1e-9 && softMax <= 1 + OreRules.SoftAmplitude + 1e-9, "soft texture mean 1, within 1 +/- amplitude");
Check(Math.Abs(cluSum / tn - 1) < .05 && cluMin >= .3 - 1e-9 && cluMax > 3, "clustered texture mean 1, 0.3 floor, rich patches");
Check(OreRules.ClusteredTexture(1, 5, 6, 7) == OreRules.ClusteredTexture(1, 5, 6, 7) && OreRules.ClusteredTexture(1, 5, 6, 7) != OreRules.ClusteredTexture(1, 5, 6, 8), "textures deterministic, salted per code");
double jump = 0;
for (int x = -100; x < 100; x++) jump = Math.Max(jump, Math.Abs(OreRules.SoftTexture(3, x + 1, 9, 5, .3) - OreRules.SoftTexture(3, x, 9, 5, .3)));
Check(jump < .3, "texture varies smoothly between neighbouring chunks");
report["textures"] = new { softMean = softSum / tn, softMin, softMax, clusteredMean = cluSum / tn, clusteredMin = cluMin, clusteredMax = cluMax };
Check(OreRules.VanillaOreMapMean(1, 1, 0) == .0705 && OreRules.VanillaOreMapMean(1.5, 1.5, 40) == .0303 && double.IsNaN(OreRules.VanillaOreMapMean(2, 1, 0)), "measured vanilla ore-map means");

// ---------------------------------------------------------------- bilinear evidence lookup
int cu = cat.LayerIndex("Cu"), sn = cat.LayerIndex("Sn");
for (int i = 0; i < 2000; i++)
{
    int x = (i * 7919) % 720, y = 20 + (i * 104729) % 320;
    double lat = 90 - (y + .5) / 2, lon = (x + .5) / 2 - 180;
    var e = cat.At("Cu", lat, lon);
    Check(Math.Abs(cat.Heat(cu, lat, lon, 0, 5) - Math.Max(e.Observed, e.Potential)) < 1e-12, "bilinear equals the cell value at cell centres");
}
double maxStep = 0, maxCellJump = 0;
for (double lat = 30.05; lat < 60; lat += 1.37)
{
    double prev = cat.Heat(cu, lat, -10, 0, 5);
    for (double lon = -10 + .002; lon < 40; lon += .002)
    {
        double h = cat.Heat(cu, lat, lon, 0, 5);
        maxStep = Math.Max(maxStep, Math.Abs(h - prev)); prev = h;
    }
    for (double lon = -10; lon < 40; lon += .5) { var a = cat.At("Cu", lat, lon); var b = cat.At("Cu", lat, lon + .5); maxCellJump = Math.Max(maxCellJump, Math.Abs(Math.Max(a.Observed, a.Potential) - Math.Max(b.Observed, b.Potential))); }
}
Check(maxStep <= 2 * .002 * 2 * 1.0001 + 1e-9, "bilinear is continuous (bounded by cell slope; no step at cell borders)");
Check(maxCellJump > .2, "the grid itself has large steps that nearest-cell lookup would expose");
Check(Math.Abs(cat.Heat(cu, 57, 179.999, 0, 5) - cat.Heat(cu, 57, -179.999, 0, 5)) < .01 && cat.Heat(cu, 57, 25, 0, 5) == cat.Heat(cu, 57, 385, 0, 5), "longitude wraps across the antimeridian");
Check(cat.Heat(cu, 89.99, 10, 0, 5) == cat.Heat(cu, 89.76, 10, 0, 5) && cat.Heat(cu, 91, 10, 0, 5) == 0 && cat.Heat(-1, 10, 10, 0, 5) == 0, "latitude clamps; invalid input gives 0");
double shallow = 0, deepHeat = 0; int deepCells = 0;
for (int y = 0; y < 360; y += 3) for (int x = 0; x < 720; x += 3)
{
    double lat = 90 - (y + .5) / 2, lon = (x + .5) / 2 - 180;
    var e = cat.At("Sn", lat, lon);
    if (e.Deep <= Math.Max(e.Observed, e.Potential) || e.MinimumMetres < 50) continue;
    deepCells++; shallow += cat.Heat(sn, lat, lon, 0, 5); deepHeat += cat.Heat(sn, lat, lon, e.MinimumMetres / 5.0 + 1, 5);
}
Check(deepCells == 0 || deepHeat > shallow, "buried evidence only becomes accessible at depth (interpolated per cell)");
report["bilinear"] = new { maxStepPer0002deg = maxStep, maxNearestCellJump = maxCellJump };

// ---------------------------------------------------------------- rate-class targets vs vanilla (land-weighted, real grid)
var land = new List<(double W, double Lat, double Lon)>();
for (int y = 0; y < 360; y++) for (int x = 0; x < 720; x++)
{
    double lat = 89.75 - y * .5, lon = -179.75 + x * .5;
    if (geology.At(lat, lon).Lithology is 0 or 11 or 16) continue; // ocean, water bodies, ice
    land.Add((Math.Cos(lat * Math.PI / 180), lat, lon));
}
double wsum = land.Sum(c => c.W);
double LandMean(Func<double, double, double> f) => land.Sum(c => c.W * f(c.Lat, c.Lon)) / wsum;
var targets = new Dictionary<string, object>();
foreach (string res in new[] { "Cu", "Sn", "Fe", "C_coal" })
{
    var p = tuning.For(res, OreClass.Progression);
    int layer = cat.LayerIndex(res);
    // Nominal volume per attempt relative to vanilla (radius^2 x thickness x concentration). The scratch harness with
    // the real VS NatFloat/noise measured 2.75-2.88 for these variants, so this nominal value is the conservative side.
    double g = p.Radius * p.Radius * p.Thickness * p.Concentration;
    double floorVol = p.Ratio(0) * g, hotVol = p.Ratio(1) * g;
    double landAtt = LandMean((lat, lon) => p.Ratio(cat.Heat(layer, lat, lon, 60, p.MetresPerBlock)));
    double landVol = landAtt * g;
    Check(floorVol >= .3, res + " no-evidence floor >= 30% of vanilla volume (" + floorVol.ToString("0.00") + ")");
    Check(landVol >= .7, res + " land average >= 70% of vanilla volume (" + landVol.ToString("0.00") + ")");
    Check(hotVol >= 3 && hotVol <= 5, res + " hotspot 3-5x vanilla volume (" + hotVol.ToString("0.00") + ")");
    Check(landAtt < 1 && p.Radius > 1 && p.Thickness > 1, res + " keeps fewer, larger deposits");
    targets[res] = new { attemptsFloor = p.Ratio(0), attemptsLand = landAtt, attemptsHot = p.Ratio(1), volumeFloor = floorVol, volumeLand = landVol, volumeHot = hotVol };
}
foreach (string res in new[] { "clay", "peat" })
{
    var p = tuning.For(res, OreClass.Soil);
    int layer = cat.LayerIndex(res);
    double landAtt = LandMean((lat, lon) => p.Ratio(cat.Heat(layer, lat, lon, 0, 5)));
    Check(Math.Abs(p.Ratio(0) - 1) < 1e-9, res + " attempts = vanilla where the geology gate allows");
    Check(p.Ratio(0) * (1 - OreRules.SoftAmplitude) >= .5, res + " never below 50% of vanilla where eligible");
    Check(p.Ratio(1) >= 1.5 && p.Ratio(1) <= 2, res + " evidence boost 1.5-2x");
    Check(p.Radius == 1 && p.Thickness == 1 && p.Concentration == 1 && p.MinDepth == 0, res + " uses native geometry and burial");
    targets[res] = new { attemptsFloor = p.Ratio(0), attemptsLand = landAtt, attemptsHot = p.Ratio(1), minimumWithTexture = p.Ratio(0) * (1 - OreRules.SoftAmplitude) };
}
foreach (string res in new[] { "Cu", "Sn" })
{
    var p = tuning.For(res, OreClass.Surface);
    Check(p.Ratio(0) * (1 - OreRules.SurfaceAmplitude) >= .5, res + " surface starters >= 50% of vanilla count anywhere");
    Check(p.Ratio(0) >= 1 - 1e-9, res + " surface starters at vanilla's attempts without evidence (rules v3)");
    Check(p.Ratio(1) >= 1.8 && p.Ratio(1) <= 2.2, res + " surface starters about 2x at hotspots");
    Check(p.Radius == 1 && p.Thickness == 1 && p.Concentration == 1, res + " surface starters keep native size");
    targets["surface-" + res] = new { attemptsMin = p.Ratio(0) * (1 - OreRules.SurfaceAmplitude), attemptsFloor = p.Ratio(0), attemptsHot = p.Ratio(1) };
}
// ---------------------------------------------------------------- rules v3: fill any rock, coal measures
Check(OreRules.SimilarHost("limestone", new[] { "claystone", "sandstone", "shale", "chalk", "limestone" }) == "limestone", "a host rock is its own host");
Check(OreRules.SimilarHost("limestone", new[] { "andesite", "granite", "basalt", "claystone", "sandstone", "shale", "chalk" }) == "chalk", "surface copper in limestone takes chalk's ore (Baltic carbonates)");
Check(OreRules.SimilarHost("dolostone", new[] { "granite", "shale", "chalk" }) == "chalk" && OreRules.SimilarHost("marble", new[] { "granite", "chalk" }) == "chalk", "carbonates take chalk");
Check(OreRules.SimilarHost("gneiss", new[] { "claystone", "granite" }) == "granite" && OreRules.SimilarHost("granite", new[] { "claystone", "shale" }) == "claystone", "basement takes the nearest igneous host, else the first");
Check(OreRules.SimilarHost("granite", Array.Empty<string>()) == null && OreRules.SimilarHost(null, new[] { "shale" }) == "shale", "no hosts, unknown rock");
Check(OreRules.BedStrength(OreRules.CoalMeasures, .45) == 0 && OreRules.BedStrength(OreRules.CoalMeasures, .75) == 1 && Math.Abs(OreRules.BedStrength(OreRules.CoalMeasures, .6) - .5) < 1e-9, "coal basin ramps from evidence .45 to .75");
var districts = new Dictionary<string, double>();
void District(OreRules.BedSet set, string name, double lat, double lon, double min, bool atMost = false)
{
    double b = OreRules.BedStrength(set, cat.Heat(cat.LayerIndex(set.Layer), lat, lon, 60, 5));
    districts[set.Layer + " " + name] = Math.Round(b, 2);
    Check(atMost ? b <= min : b >= min, name + (atMost ? " is not " : " is ") + set.Name + " (" + b.ToString("0.00") + ")");
}
District(OreRules.CoalMeasures, "Upper Silesia", 50.25, 19.0, .9); District(OreRules.CoalMeasures, "Yorkshire", 53.6, -1.4, .9);
District(OreRules.CoalMeasures, "South Wales", 51.7, -3.4, .9); District(OreRules.CoalMeasures, "Ruhr", 51.45, 7.2, .6); District(OreRules.CoalMeasures, "Appalachian", 39.5, -80.5, .9);
District(OreRules.CoalMeasures, "Latvia", 57.0, 25.0, 0, true); District(OreRules.CoalMeasures, "the Finnish shield", 64.0, 26.0, 0, true);
District(OreRules.IronFormations, "Kiruna", 67.85, 20.2, .7); District(OreRules.IronFormations, "Krivoy Rog", 47.9, 33.4, .8); District(OreRules.IronFormations, "Lorraine", 49.3, 6.0, .8);
District(OreRules.IronFormations, "Carajas", -6.0, -50.2, .8); District(OreRules.IronFormations, "Latvia", 57.0, 25.0, 0, true);
var cuDistrict = OreRules.District("Cu", "nativecopper", 30);
District(cuDistrict, "Chuquicamata", -22.3, -68.9, .5); District(cuDistrict, "Latvia", 57.0, 25.0, 0, true);
Check(cuDistrict.Beds.Length == 1 && cuDistrict.Beds[0].Depth == 30 && OreRules.District("Cu", "nativecopper", 300).Beds[0].Depth == 250 && cuDistrict.Salt != OreRules.District("Pb", "galena", 30).Salt, "district beds: one bed at the deposit's usual depth (6-250), salted per resource");

Check(!new OreTuning().OreBodiesOff("C_coal", OreClass.Progression) && !new OreTuning().OreBodiesOff("Pb", OreClass.Standard), "ore bodies on by default");
// ---------------------------------------------------------------- tunable maps (decision 0006): weights, district ramp, richness
{
    var plain = new OreTuning();
    foreach (var (res, cls) in new[] { ("Pb", OreClass.Standard), ("Cu", OreClass.Progression), ("Cu", OreClass.Surface), ("clay", OreClass.Soil) })
    {
        var p0 = plain.For(res, cls);
        var bg2 = new OreTuning { BackgroundWeight = 2 }.For(res, cls);
        var ev3 = new OreTuning { EvidenceWeight = 3 }.For(res, cls);
        var ev0 = new OreTuning { EvidenceWeight = 0 }.For(res, cls);
        Check(Math.Abs(bg2.Ratio(0) - 2 * p0.Ratio(0)) < 1e-12 && Math.Abs(bg2.Ratio(1) - bg2.Ratio(0) - (p0.Ratio(1) - p0.Ratio(0))) < 1e-12,
            res + "/" + cls + ": the background weight scales the floor only");
        Check(Math.Abs(ev3.Ratio(0) - p0.Ratio(0)) < 1e-12 && Math.Abs(ev3.Ratio(1) - ev3.Ratio(0) - 3 * (p0.Ratio(1) - p0.Ratio(0))) < 1e-12,
            res + "/" + cls + ": the evidence weight scales the evidence part only");
        Check(Math.Abs(ev0.Ratio(1) - ev0.Ratio(0)) < 1e-12, res + "/" + cls + ": evidence weight 0 is the background map alone");
    }
    var perRes = new OreTuning { EvidenceWeight = 2, Resources = { ["Pb"] = new OreOverride { EvidenceWeight = .5, BackgroundWeight = 0 } } };
    Check(perRes.For("Pb", OreClass.Standard).EvidenceWeight == .5 && perRes.For("Pb", OreClass.Standard).Ratio(0) == 0 && perRes.For("Zn", OreClass.Standard).EvidenceWeight == 2,
        "a per-resource weight replaces the global one, for that resource only");
    Check(new OreTuning { EvidenceWeight = 0 }.OreBodiesOff("C_coal", OreClass.Progression) && new OreTuning { Resources = { ["Fe"] = new OreOverride { EvidenceWeight = 0 } } }.OreBodiesOff("Fe", OreClass.Progression),
        "evidence weight 0 switches the ore bodies off");
    var ramp = new OreTuning { Resources = { ["C_coal"] = new OreOverride { DistrictFrom = .6, DistrictTo = .8 }, ["Cu"] = new OreOverride { DistrictTo = .95 } } };
    var coalSet = ramp.DistrictSet(OreRules.CoalMeasures);
    Check(coalSet.From == .6 && coalSet.To == .8 && coalSet.Beds == OreRules.CoalMeasures.Beds && ramp.DistrictSet(OreRules.IronFormations) == OreRules.IronFormations
        && ramp.DistrictSet(OreRules.District("Cu", "nativecopper", 30)).From == .7 && ramp.DistrictSet(OreRules.District("Cu", "nativecopper", 30)).To == .95,
        "per-resource district ramps replace the built-in one (each end separately)");
    Check(OreRules.District("Pb", "galena", 30).From == .7 && OreRules.District("Pb", "galena", 30).To == .9 && OreRules.CoalMeasures.From == OreRules.DistrictRamp("C_coal").From,
        "built-in ramps: coal .45-.75, iron .55-.85, others .7-.9");
    var rich = new OreTuning { CoalBasins = 2, Resources = { ["C_coal"] = new OreOverride { Richness = .5 }, ["Pb"] = new OreOverride { Richness = 3 } } };
    Check(rich.DistrictRichness("C_coal", rich.CoalBasins) == 1 && rich.DistrictRichness("Pb", 2) == OreRules.MaxRichness && rich.DistrictRichness("Zn", 2) == 2,
        "ore-body richness = family setting x resource Richness, at most 3");
    foreach (var (badJson, what) in new[] {
        ("{\"Resources\":{\"C_coal\":{\"DistrictFrom\":0.8}}}", "a ramp whose start passes the built-in end"),
        ("{\"Resources\":{\"Fe\":{\"DistrictFrom\":0.5,\"DistrictTo\":0.52}}}", "a ramp narrower than 0.05"),
        ("{\"Resources\":{\"Cu\":{\"DistrictTo\":1.5}}}", "a ramp past 1"),
        ("{\"Resources\":{\"Cu\":{\"Richness\":4}}}", "richness above 3"),
        ("{\"EvidenceWeight\":11}", "an evidence weight above 10"),
        ("{\"Resources\":{\"Cu\":{\"BackgroundWeight\":-1}}}", "a negative background weight") })
    {
        var t = OreTuning.Load(badJson, cat.Layers.Keys, out string why);
        Check(why != null && JsonSerializer.Serialize(t) == JsonSerializer.Serialize(new OreTuning()), "invalid tuning falls back to defaults: " + what + " (" + why + ")");
    }
    var good = OreTuning.Load("{\"EvidenceWeight\":1.5,\"Resources\":{\"C_coal\":{\"DistrictFrom\":0.3,\"DistrictTo\":0.6,\"Richness\":0.5}}}", cat.Layers.Keys, out string goodProblem);
    Check(goodProblem == null && good.EvidenceWeight == 1.5 && good.DistrictRamp("C_coal") == (.3, .6) && good.Resources["C_coal"].Richness == .5, "map weights, ramps and richness load from the saved tuning");
}
Check(new OreTuning { Resources = { ["C_coal"] = new OreOverride { HotspotChance = 0 } } }.OreBodiesOff("C_coal", OreClass.Progression)
    && new OreTuning { Resources = { ["Cu"] = new OreOverride { Concentration = 0 } } }.OreBodiesOff("Cu", OreClass.Progression)
    && new OreTuning { HotspotChance = 0 }.OreBodiesOff("Pb", OreClass.Standard) && !new OreTuning { HotspotChance = 0 }.OreBodiesOff("Fe", OreClass.Progression),
    "a hotspot chance or concentration of 0 switches ore bodies off (an explicit 0 hotspot even with a background)");

// beds of a set over a full district (2048 x 2048 blocks, every 4th column): cover, thickness, overlap, continuity, blocks per chunk
var bedReport = new Dictionary<string, object>();
foreach (var set in new[] { OreRules.CoalMeasures, OreRules.IronFormations, cuDistrict })
{
    Span<OreRules.Bed> buf = stackalloc OreRules.Bed[OreRules.MaxBeds];
    Check(OreRules.BedsAt(set, 1, 5, 5, 0, 1, buf) == 0 && OreRules.BedsAt(set, 1, 5, 5, 1, 0, buf) == 0, set.Name + ": nothing outside the district or with its setting at 0");
    long blocks = 0; int bedStep = 0, overlaps = 0, bedShallow = 0, tooThick = 0; var cover = new int[set.Beds.Length];
    var prev = new int[set.Beds.Length];
    for (int z = 0; z < 512; z++)
    {
        Array.Fill(prev, -1);
        for (int x = 0; x < 512; x++)
        {
            int count = OreRules.BedsAt(set, 777, 4 * x, 4 * z, 1, 1, buf); // every 4th block over 2048 x 2048: many ore bodies
            var seen = new int[set.Beds.Length]; Array.Fill(seen, -1);
            int lastBottom = 0;
            for (int i = 0; i < count; i++)
            {
                var bed = buf[i];
                if (bed.Thickness < 1 || bed.Thickness > set.Beds[bed.Index].MaxThickness || bed.Depth < 2) tooThick++;
                if (bed.Depth <= lastBottom) overlaps++;
                lastBottom = bed.Depth + bed.Thickness - 1;
                if (bed.Depth <= 4) bedShallow++;
                blocks += bed.Thickness; cover[bed.Index]++; seen[bed.Index] = bed.Depth;
            }
            for (int k = 0; k < seen.Length; k++)
            {
                if (seen[k] >= 0 && prev[k] >= 0) bedStep = Math.Max(bedStep, Math.Abs(seen[k] - prev[k]));
                prev[k] = seen[k];
            }
        }
    }
    double cols = 512.0 * 512, perChunk = blocks / cols * 1024;
    Check(tooThick == 0, set.Name + ": every bed 1 to its greatest thickness, at least 2 below the surface");
    Check(overlaps == 0, set.Name + ": beds never overlap");
    Check(bedStep <= 2, set.Name + ": beds are continuous (depth step " + bedStep + " over 4 blocks)");
    Check(OreRules.BedsAt(set, 777, 123, 456, .7, 1, buf) == OreRules.BedsAt(set, 777, 123, 456, .7, 1, buf), set.Name + ": deterministic");
    int thin = 0; long thinBlocks = 0;
    for (int i = 0; i < 20000; i++) { int c = OreRules.BedsAt(set, 777, i % 200, i / 200, .2, 1, buf); thin += c > 0 ? 1 : 0; for (int j = 0; j < c; j++) thinBlocks += buf[j].Thickness; }
    Check(thinBlocks / 20000.0 * 1024 < perChunk / 3, set.Name + ": thin and patchy at the district's edge");
    bedReport[set.Name] = new { blocksPerChunk = Math.Round(perChunk), cover = cover.Select(c => Math.Round(c / cols, 3)), edgeBlocksPerChunk = Math.Round(thinBlocks / 20000.0 * 1024), bedShallow };
    if (set == OreRules.CoalMeasures)
    {
        Check(cover.All(c => c / cols > .6 && c / cols < .97), "each coal seam covers most of a full basin (" + string.Join("/", cover.Select(c => (c / cols).ToString("0.00"))) + ")");
        Check(bedShallow > 0, "the top coal seam comes within 4 blocks of the surface in places");
        Check(perChunk > 1500, "a full coal basin holds over 1500 coal blocks per chunk (" + perChunk.ToString("0") + ")");
    }
    else
    {
        // separate ore bodies: about a quarter to a half of the ground, still thousands of blocks per chunk on average
        Check(cover.All(c => c / cols > .15 && c / cols < .6), set.Name + ": separate ore bodies (" + string.Join("/", cover.Select(c => (c / cols).ToString("0.00"))) + ")");
        Check(perChunk > 500, set.Name + ": over 500 ore blocks per chunk across a full district (" + perChunk.ToString("0") + "; vanilla iron: a few dozen)");
    }
}
report["oreBodies"] = new { districts, sets = bedReport };

var quartz = tuning.For("SiO2", OreClass.Veins);
Check(Math.Abs(quartz.Ratio(0) - 1) < 1e-9 && quartz.Ratio(1) <= 2 && quartz.Radius == 1, "quartz veins at the vanilla rate, never gated by silica evidence");
// Standard class keeps 0.12's effective volume: floor ~0.3-0.4, hotspots 3-5x vanilla volume; land average = floor
// where evidence is sparse (no-evidence floor cannot exceed the band), up to ~0.8 where evidence is widespread.
var standardLand = new Dictionary<string, object>();
foreach (var layer in cat.Layers.Values)
{
    if (!(layer.Deposits ?? Array.Empty<string>()).Any(d => OreRules.Classify(d, layer.Code) == OreClass.Standard)) continue;
    var p = tuning.For(layer.Code, OreClass.Standard);
    Check(Math.Abs(p.Ratio(0) - tuning.FrequencyMultiplier * OreRules.StandardCompensation * tuning.BackgroundChance) < 1e-12, layer.Code + " standard = FrequencyMultiplier x compensation x chance");
    double g = p.Radius * p.Radius * p.Thickness * p.Concentration; // nominal; harness: 2.74-2.86 for typical standard discs
    int li = cat.LayerIndex(layer.Code);
    double floorVol = p.Ratio(0) * g, hotVol = p.Ratio(1) * g, landVol = LandMean((lat, lon) => p.Ratio(cat.Heat(li, lat, lon, 60, p.MetresPerBlock))) * g;
    Check(floorVol >= .3 && floorVol <= .45, layer.Code + " standard no-evidence floor 0.3-0.45 of vanilla volume (" + floorVol.ToString("0.00") + ")");
    Check(hotVol >= 3 && hotVol <= 5, layer.Code + " standard hotspot 3-5x vanilla volume (" + hotVol.ToString("0.00") + ")");
    Check(landVol >= floorVol && landVol <= .9, layer.Code + " standard land average between floor and 0.9 (" + landVol.ToString("0.00") + ")");
    standardLand[layer.Code] = Math.Round(landVol, 3);
}
report["standardLandVolume"] = standardLand;
var std = tuning.For("Pb", OreClass.Standard);
targets["standard"] = new { attemptsFloor = std.Ratio(0), attemptsHot = std.Ratio(1), nominalVolumeFloor = std.Ratio(0) * std.Radius * std.Radius * std.Thickness * std.Concentration, nominalVolumeHot = std.Ratio(1) * std.Radius * std.Radius * std.Thickness * std.Concentration };
// 0.12-authored providers (uranium, wolframite, corundum, gypsum, bauxite) are created with a default vanilla map:
// 0.12 attempts = tries x 0.4 x chance x (0.2 + 0.8 x 0.0705); v2 = tries x 0.0705 x standard ratio.
double v1Authored = .4 * (.2 + .8 * .0705), v2Authored = OreRules.VanillaOreMapMean(1, 1, 0) * tuning.FrequencyMultiplier * OreRules.StandardCompensation;
Check(Math.Abs(v2Authored / v1Authored - 1) < .12, "Earth-authored variants keep their 0.12 attempt rate within 12% (" + (v2Authored / v1Authored).ToString("0.00") + ")");
// Gem pockets keep native radius (vanilla truncates 1-block pockets; Earth's rounded 1.6x radius would quadruple them).
Check(OreRules.EffectiveRadiusMultiplier(1, 1.6) == 1 && OreRules.EffectiveRadiusMultiplier(2, 1.6) == 1.6 && OreRules.EffectiveRadiusMultiplier(26, 1.6) == 1.6, "gem pockets keep native radius");
Check(!new OreTuning { FrequencyMultiplier = 11 }.TryValidate(cat.Layers.Keys, out _) && new OreTuning { FrequencyMultiplier = 10 }.TryValidate(cat.Layers.Keys, out _), "raw FrequencyMultiplier keeps the 0..10 bound");
report["targets"] = targets;

// ---------------------------------------------------------------- children: frequency once, parent driven by max heat
var silver = tuning.For("Ag", OreClass.Standard, child: true);
var galena = tuning.For("Pb", OreClass.Standard);
Check(silver.Frequency == tuning.FrequencyMultiplier * OreRules.ChildCompensation && galena.Frequency == tuning.FrequencyMultiplier * OreRules.StandardCompensation, "child and standard compensations");
// Child point clouds: ~0.86-0.90x vanilla volume per attempt (harness), so overall child volume = Ratio x ~0.88.
Check(silver.Ratio(0) * .86 >= .3 && silver.Ratio(0) * .9 <= .45 && silver.Ratio(1) * .86 >= 3 && silver.Ratio(1) * .9 <= 5, "children land in the standard volume band");
foreach (double hp in new[] { 0, .3, 1.0 })
    foreach (double hc in new[] { 0, .5, 1.0 })
    {
        double parentRatio = galena.Ratio(Math.Max(hp, hc)); // parent attempts driven by max(parent, child) heat
        double overall = parentRatio * OreRules.ChildRatio(silver, hc, parentRatio);
        Check(Math.Abs(overall - silver.Ratio(hc)) < 1e-12, "child rate follows only its own evidence (hp=" + hp + ", hc=" + hc + ")");
        Check(parentRatio >= galena.Ratio(hp), "parent attempts boosted by a valuable child");
    }
Check(OreRules.ChildRatio(silver, 1, 1e-9) == OreRules.MaxChildBoost && OreRules.ChildRatio(silver, 1, 0) == 0 && OreRules.ChildRatio(null, 1, 1) == 0, "child boost capped; no parent attempts means no children");
var gold = tuning.For("Au", OreClass.Standard, child: true);
Check(Math.Abs(quartz.Ratio(0) * OreRules.ChildRatio(gold, 0, quartz.Ratio(0)) - gold.Ratio(0)) < 1e-12, "gold in quartz: quartz at vanilla rate, gold at its own background");

// ---------------------------------------------------------------- determinism across threads
var cells = Enumerable.Range(0, 20000).Select(i => (Lat: -60 + (i * 0.00611) % 130, Lon: -180 + (i * 0.0377) % 360, Cx: i % 997 - 500, Cz: i / 7 - 1400)).ToArray();
double Compute(int i)
{
    var c = cells[i];
    return cat.Heat(cu, c.Lat, c.Lon, 40, 5) + 3 * cat.Heat(sn, c.Lat, c.Lon, 200, 5) + 7 * OreRules.ClusteredTexture(99, c.Cx, c.Cz, 5)
        + 11 * OreRules.SoftTexture(99, c.Cx, c.Cz, 6, .3) + 13 * OreRules.ChildRatio(silver, Clamp01(c.Lat / 90), galena.Ratio(Clamp01(c.Lon / 180)));
}
var sequential = Enumerable.Range(0, cells.Length).Select(Compute).ToArray();
var parallel = new double[cells.Length];
var watch = Stopwatch.StartNew();
Parallel.For(0, cells.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => parallel[i] = Compute(i));
watch.Stop();
Check(sequential.SequenceEqual(parallel), "identical results on 8 threads (no shared mutable state)");
var bag = new ConcurrentBag<bool>();
Parallel.For(0, 20000, i => bag.Add(cat.At("Cu", -22.3, -68.9) == cat.At("Cu", -22.3, 291.1)));
Check(bag.All(b => b), "threaded seam consistency");

// ---------------------------------------------------------------- hash, grade and column helpers
int[] buckets = new int[10]; int n = 200000;
for (int i = 0; i < n; i++) { double u = RegionalOreCatalog.Random(18371, i, 17, -i, 49); Check(u >= 0 && u < 1, "hash range"); buckets[(int)(u * 10)]++; }
double chi = buckets.Sum(b => Math.Pow(b - n / 10.0, 2) / (n / 10.0));
Check(chi < 30, "coordinate hash is uniform (chi-square, 9 dof)");
int lowRich = 0, highRich = 0;
for (int i = 0; i < 100000; i++)
{
    double u = RegionalOreCatalog.Random(5, i, 1, 2, 3);
    if (RegionalOreCatalog.GradeIndex(RegionalOreCatalog.GradeQuantile(u, 0, 0), 4) >= 2) lowRich++;
    if (RegionalOreCatalog.GradeIndex(RegionalOreCatalog.GradeQuantile(u, .8, 0), 4) >= 2) highRich++;
}
Check(lowRich > 0 && highRich > lowRich * 1.5, "occasional rich background, more rich ore in strong areas");
Check(RegionalOreCatalog.InColumn(31, 31, 0, 0) && !RegionalOreCatalog.InColumn(32, 31, 0, 0) && RegionalOreCatalog.InColumn(-1, -1, -1, -1), "child deposits cannot modulo-wrap into the wrong chunk");
Check(OreRules.Taper(OreRules.DiscEdge(0, 0, 10, 10, 10, 0)) == 1 && OreRules.DiscEdge(10, 0, 10, 10, 10, 0) <= 0 && OreRules.DiscEdge(0, 0, 3, 3, 3, 1) == 1, "vanilla disc shape: distortion only above radius 3");

// ---------------------------------------------------------------- geology contacts (shared helper)
int mixedLeft = 0, mixedRight = 0;
for (int z = 0; z < 1024; z++) for (int x = 192; x < 320; x++)
{
    var a = GeologyTransitions.Warp(x, z, 123, 96); var b = GeologyTransitions.Warp(x + 1, z, 123, 96);
    Check(Math.Abs(a.X - x) <= 96 && Math.Abs(a.Z - z) <= 96, "transition width bounded");
    Check(Math.Abs(b.X - a.X) < 5 && Math.Abs(b.Z - a.Z) < 5, "no chunk-boundary jump in contact field");
    if (x < 256 && a.X >= 256) mixedLeft++;
    if (x >= 256 && a.X < 256) mixedRight++;
}
Check(mixedLeft > 500 && mixedRight > 500, "contact zone interleaves on both sides");
Check(GeologyTransitions.Warp(31, 32, 123, 0) == (31d, 32d), "zero-width legacy behaviour");

// ---------------------------------------------------------------- micro-benchmark of the per-chunk factor ingredients
var bench = Stopwatch.StartNew(); double sink = 0;
for (int i = 0; i < 200000; i++) sink += cat.Heat(cu, 48.6 + i * 1e-5, 7.78, 120, 5) * OreRules.ClusteredTexture(7, i, i >> 3, 11);
bench.Stop();
report["benchmark"] = new { heatPlusTextureNs = bench.Elapsed.TotalMilliseconds * 1e6 / 200000, parallelMs = watch.ElapsedMilliseconds, sink = Math.Round(sink, 3) };

var result = new { status = "PASS", checks, rulesVersion = RegionalOreCatalog.Version, layers = cat.Layers.Count, contentHash = cat.ContentHash[..16], defaultTuning = new OreTuning(), report };
string output = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
if (write)
{
    // Inputs of tools/render_ore_heatmaps.py (defaultTuning, contact comparison).
    string outDir = Path.Combine(root, "docs/research/ore-heatmaps-v1");
    File.WriteAllText(Path.Combine(outDir, "core-test-results.json"), output);
    var candidates = new List<(double Lat, double Lon)>();
    for (double lat = 45.25; lat < 61; lat += .5) for (double lon = 0; lon < 31; lon += .5)
    {
        var a = geology.At(lat, lon - .01); var b = geology.At(lat, lon + .01);
        if (a.HasRegionalBedrock && b.HasRegionalBedrock && a.Profile.Id != b.Profile.Id) candidates.Add((lat, lon));
    }
    var location = candidates.OrderBy(p => Math.Pow(p.Lat - 57, 2) + Math.Pow(p.Lon - 25, 2)).First();
    int size = 256; double centerX = 128000 + (location.Lon - 14.15) * 705.4, centerZ = 51200 - location.Lat * 568.9;
    var before = new int[size * size]; var after = new int[size * size]; var profiles = new Dictionary<string, int>();
    int Id(double x, double z)
    {
        string label = geology.At(location.Lat - (z - centerZ) / 568.9, location.Lon + (x - centerX) / 705.4).Profile.Label;
        if (!profiles.TryGetValue(label, out int id)) { id = profiles.Count; profiles.Add(label, id); }
        return id;
    }
    for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
    {
        double wx = centerX + (x - size / 2) * 2, wz = centerZ + (z - size / 2) * 2;
        var shifted = GeologyTransitions.Warp(wx, wz, 123, 96);
        before[z * size + x] = Id(wx, wz); after[z * size + x] = Id(shifted.X, shifted.Z);
    }
    File.WriteAllText(Path.Combine(outDir, "geology-contact-sample.json"), JsonSerializer.Serialize(new { size, blockStep = 2, latitude = location.Lat, longitude = location.Lon, transitionBlocks = 96, profiles, before, after }));
}
Console.WriteLine(output);
