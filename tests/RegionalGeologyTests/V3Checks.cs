using EarthMap.Core;

/// <summary>Geology rules v3 (decision 0101, EA-0173): envelopes, the covered fill, basements, gypsum and rock salt.</summary>
static partial class Program
{
    static void V3Categories()
    {
        string P(double lat, double lon) => cat3.At(lat, lon).Profile.Id;
        // envelopes the 0.5-degree grid lost
        foreach (var (name, lat, lon, profile) in new[]
        {
            ("Troodos", 34.93, 32.95, "ophiolite"), ("Semail, Oman", 23.4, 57.6, "ophiolite"), ("Kempirsai", 50.4, 57.8, "ophiolite"),
            ("Bushveld western limb", -25.6, 27.5, "layered-intrusion"), ("Great Dyke", -18.5, 30.28, "layered-intrusion"), ("Sudbury", 46.6, -81.05, "layered-intrusion"),
            ("Iceland", 64.9, -19.0, "oceanic-volcanic"), ("Hawaii (Big Island)", 19.6, -155.5, "oceanic-volcanic"),
            ("Dartmoor", 50.58, -3.93, "felsic-plutonic"), ("Land's End", 50.10, -5.60, "felsic-plutonic"), ("Bangka", -2.2, 106.1, "felsic-plutonic"),
        })
            Check(P(lat, lon) == profile, $"v3 {name} is {profile} (got {P(lat, lon)})");
        Check(cat2.At(34.93, 32.95).Profile.Id != "ophiolite" && cat2.At(50.58, -3.93).Profile.Id != "felsic-plutonic", "v2 keeps its own categories there (worlds made with v2 unchanged)");
        // continental flood basalt on continental crust, not mantle
        var deccan = cat3.At(19.0, 75.0).Profile;
        Check(deccan.Id == "mafic-volcanic" && deccan.Basement == "granite" && cat2.At(19.0, 75.0).Profile.Basement == "peridotite", "v3 Deccan basalt sits on granite (v2: peridotite)");
        // the evaporitic profile carries rock salt; no profile starts with a rock without sand or gravel (tuff, halite)
        Check(cat3.ProfileById("evaporitic").Beds.Count(b => b.Material == "halite") == 2 && cat3.ProfileById("evaporitic").Beds.Count(b => b.Material == "gypsum") == 2, "v3 evaporite basins carry two gypsum and two rock-salt beds");
        foreach (var p in cat3.AllProfiles) if (p.Beds.Length > 0) Check(p.Beds[0].Material is not ("tuff" or "halite"), p.Id + " never starts with tuff or halite");
        // unconsolidated cover: the nearest bedrock within 2 cells, else sedimentary basin fill; never vanilla's strata again
        int covered = 0, coveredV3 = 0, filledV3 = 0, basin = 0;
        for (int z = 0; z < 360; z++) for (int x = 0; x < 720; x++)
        {
            double lat = 89.75 - z * .5, lon = -179.75 + x * .5;
            var s2 = cat2.At(lat, lon);
            if (s2.Profile.Id != "covered") continue;
            covered++;
            var s3 = cat3.At(lat, lon);
            if (!s3.HasRegionalBedrock) coveredV3++;
            else if (s3.Profile.Id == "basin-fill") basin++;
            else filledV3++;
        }
        Info($"v2 covered cells {covered}: v3 nearest bedrock {filledV3}, basin fill {basin}, still without bedrock {coveredV3}");
        Check(covered > 10000 && coveredV3 == 0 && basin > 0 && filledV3 > 0, "v3 leaves no unconsolidated cover to vanilla's strata (nearest bedrock within 2 cells, else basin fill)");
        // clay and peat read their relief over 1.2 km at any scale (12 blocks at 1:100, as rules 1 and 2 at every scale)
        Check(RegionalGeologyWorld.FootprintBlocks(111195 / 100.0) == 12 && RegionalGeologyWorld.FootprintBlocks(111195 / 10.0) == 120
            && RegionalGeologyWorld.FootprintBlocks(111195 / 200.0) == 6 && RegionalGeologyWorld.FootprintBlocks(111195 / 5.0) == 240, "v3 relief footprint: 1.2 km in blocks at 1:100, 1:10, 1:200 and 1:5");
        Check(cat3.At(61.0, 75.0).HasRegionalBedrock && cat3.At(-3.0, -62.0).HasRegionalBedrock, "West Siberia and Amazonia have bedrock under their cover in v3");
    }
}
