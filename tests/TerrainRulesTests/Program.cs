using System.Reflection;
using System.Runtime.CompilerServices;
using EarthMap.Core;

// ER-10 (code review 14 November 2026): TerrainRules' facts against the comparisons they replaced. Each line says the fact,
// its family and the first version that turns it on, written from the code before it moved (TerrainModel's raw tests, e.g.
// "ChannelRules >= MouthChannelRules" with MouthChannelRules 3). Every version 0..12 of the fact's family, with the other
// families at 0 and at 12, must give exactly "version >= first". A fact added to TerrainRules without a line here fails.
int checks = 0, failures = 0;
void Check(bool ok, string what) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

var table = new (string Fact, string Family, int From)[]
{
    ("Dredged", "Channel", 1), ("MouthBed", "Channel", 3), ("OneBed", "Channel", 4), ("OneWidth", "Channel", 5),
    ("DryLandLedge", "GenFix", 1), ("EasedDips", "GenFix", 2), ("GridLakeEdge", "GenFix", 3),
    ("RiparianEnvelopes", "GenFix", 1), ("CornerColumns", "GenFix", 1), ("ScatteredBedGravel", "GenFix", 1),
    ("ConduitUnderTop", "GenFix", 4), ("WholeGroundColumn", "GenFix", 4), ("FlowAcrossTiles", "GenFix", 5), ("WholeLakeArea", "GenFix", 5),
    ("SeamNoiseInRing", "GenFix", 5), ("KeptLakeLevels", "GenFix", 5),
    ("SeaOf1318", "Works", 3), ("IslandsOf1318", "Works", 4),
    ("ShoreStrip", "Shore", 1), ("ShoreAtWater", "Shore", 2),
    ("FineSummitLift", "Fine", 2), ("FineSmooth", "Fine", 3), ("FineFootprintMean", "Fine", 4), ("FineBounded", "Fine", 5),
    ("SummitRestore", "Summit", 2), ("CellDetail", "Detail", 1), ("CoarseHeights", "Coarse", 1),
    ("RealLapse", "Climate", 2), ("RealRain", "Climate", 3), ("AridRivers", "Arid", 1), ("SolvedFlow", "Flow", 1),
    ("GeothermalActivity", "Geothermal", 1),
};
string[] families = { "Channel", "GenFix", "Works", "Shore", "Fine", "Summit", "Detail", "Coarse", "Climate", "Arid", "Flow", "Geothermal" };
TerrainRules Make(string family, int version, int others)
{
    object r = new TerrainRules();
    foreach (var f in families)
        typeof(TerrainRules).GetProperty(f).SetValue(r, f == family ? version : others);
    return (TerrainRules)r;
}
var facts = typeof(TerrainRules).GetProperties().Where(p => p.PropertyType == typeof(bool)).Select(p => p.Name).ToHashSet();
Check(facts.SetEquals(table.Select(t => t.Fact)), "every fact of TerrainRules has a line in the table, and no line names a missing one: "
    + string.Join(", ", facts.Except(table.Select(t => t.Fact)).Concat(table.Select(t => t.Fact).Except(facts))));
Check(families.ToHashSet().SetEquals(typeof(TerrainRules).GetProperties().Where(p => p.PropertyType == typeof(int)).Select(p => p.Name)),
    "the families are TerrainRules' int properties");
foreach (var (fact, family, from) in table)
{
    var prop = typeof(TerrainRules).GetProperty(fact);
    foreach (int others in new[] { 0, 12 })
        for (int v = 0; v <= 12; v++)
        {
            bool got = (bool)prop.GetValue(Make(family, v, others));
            Check(got == v >= from, $"{fact}: {family} {v} (others {others}) gives {got}, expected {v >= from}");
        }
}
// the constants the facts name (the comparisons named them before)
Check(TerrainModel.MouthChannelRules == 3 && TerrainModel.FlowChannelRules == 4 && RiverNetwork.PondChannelRules == 5
    && TerrainModel.RealLapseRules == 2 && TerrainModel.RealRainRules == 3 && TerrainModel.GenFixConduitUnderTop == 4
    && TerrainModel.GenFixWholeGroundColumn == 4, "the named versions behind the facts");

// TerrainModel's int properties read and write the record (as BuildModel, the server's attach steps and the tests set them)
var model = (TerrainModel)RuntimeHelpers.GetUninitializedObject(typeof(TerrainModel));
var pairs = new (string Property, string Family)[]
{
    ("ChannelRules", "Channel"), ("GenFixRules", "GenFix"), ("WorksRules", "Works"), ("ShoreRules", "Shore"), ("FineRules", "Fine"),
    ("SummitRules", "Summit"), ("DetailRules", "Detail"), ("CoarseRules", "Coarse"), ("ClimateRules", "Climate"),
    ("AridRiverRules", "Arid"), ("FlowRules", "Flow"), ("GeothermalRules", "Geothermal"),
};
int n = 1;
foreach (var (prop, family) in pairs) typeof(TerrainModel).GetProperty(prop).SetValue(model, n++);
n = 1;
foreach (var (prop, family) in pairs)
{
    int want = n++;
    Check((int)typeof(TerrainModel).GetProperty(prop).GetValue(model) == want && (int)typeof(TerrainRules).GetProperty(family).GetValue(model.Rules) == want,
        $"TerrainModel.{prop} is the record's {family}");
}
// EA-0084: the families read by number outside TerrainModel (Core/RulesFacts.cs): each fact against its first version, the
// version written from the comparison it replaced; every public bool fact of those classes has a line
var byNumber = new (Type Family, string Fact, int From)[]
{
    (typeof(PoolLayoutFacts), "SlopePools", 1), (typeof(PoolLayoutFacts), "LakeRims", 4), (typeof(PoolLayoutFacts), "StepSills", 5),
    (typeof(PoolLayoutFacts), "RapidsReaches", 8), (typeof(PoolLayoutFacts), "BlockSteps", 9), (typeof(PoolLayoutFacts), "DrawnRapids", 11),
    (typeof(AlpineSurfaceFacts), "Turf", 1), (typeof(AlpineSurfaceFacts), "IceSheetGround", 2),
    (typeof(ShoreZoneFacts), "Zones", 1), (typeof(ShoreZoneFacts), "AnyHeight", 2),
    (typeof(ZoneFacts), "Zones", 1), (typeof(ZoneFacts), "NoTreeFloor", 2),
    (typeof(GeothermalFacts), "Activity", 1), (typeof(GeothermalFacts), "VolcanoBodies", 2), (typeof(GeothermalFacts), "VolcanoInteriors", 3),
    (typeof(ShoreMaterialFacts), "Strip", 1), (typeof(ShoreMaterialFacts), "AtWater", 2), (typeof(ShoreMaterialFacts), "ScouredTill", 3),
    (typeof(GeologyFacts), "GenerationBlock", 2), (typeof(GeologyFacts), "CellContacts", 2), (typeof(GeologyFacts), "SmoothedBeds", 2),
    (typeof(GeologyFacts), "GravelLenses", 2),
    (typeof(GroundColumnFacts), "Columns", 1), (typeof(GroundColumnFacts), "WarmKarst", 2), (typeof(GroundColumnFacts), "NoBauxiteStrata", 2),
    (typeof(OreLayerFacts), "Layers", 1), (typeof(OreLayerFacts), "Under", 2), (typeof(OreLayerFacts), "Surface", 3),
    (typeof(OreLayerFacts), "Aluminosilicates", 4),
};
foreach (var family in byNumber.Select(b => b.Family).Distinct())
{
    var named = family.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(bool)).Select(m => m.Name).ToHashSet();
    Check(named.SetEquals(byNumber.Where(b => b.Family == family).Select(b => b.Fact)), family.Name + ": every fact has a line, and no line names a missing one");
}
foreach (var (family, fact, from) in byNumber)
    for (int v = 0; v <= 14; v++)
        Check((bool)family.GetMethod(fact).Invoke(null, new object[] { v }) == v >= from, $"{family.Name}.{fact}({v}) is {v >= from}");
for (int v = 0; v <= 14; v++)
    Check(PoolLayoutFacts.RiverQueryVersion(v) == (v >= 11 ? 3 : v >= 9 ? 2 : v >= 8 ? 1 : 0), $"PoolLayoutFacts.RiverQueryVersion({v}), as EarthMapSystem answered");
Check(RiverNetwork.PoolLayoutVersion == 11 && MountainEcology.SurfaceVersion == 2 && ShoreZones.RulesVersion == 2 && ClimateZones.RulesVersion == 2 && Geothermal.RulesVersion == 3
    && ShoreMaterial.RulesVersion == 3 && RegionalGeologyCatalog.RulesVersion == 2 && GroundColumns.RulesVersion == 2 && OreLayers.RulesVersion == 4
    && OreLayers.AluminosilicateVersion == 4,
    "each family's newest is its last fact's version");

Console.WriteLine(failures == 0 ? $"TerrainRulesTests: {checks} checks passed" : $"TerrainRulesTests: {failures} of {checks} checks failed");
return failures == 0 ? 0 : 1;
