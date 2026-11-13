/// <summary>
/// Full-pack plausibility probes: places with the genera that dominate their real forests (national forest inventories
/// and standard vegetation descriptions), expressed in the genera the tested pack can supply. Selection shares of trees
/// on inland upland sites (no river, lake or sea nearby), averaged over 3x3 points 0.05° apart.
/// </summary>
sealed record Probe(string Name, double Lat, double Lon, string[] Expect, int Need, string[] Implausible, double MaxImplausible = 0.25,
    int MinGenera = 0, double MaxGenus = 1)
{
    public static bool Is(string label, string genus) => label.Split('/').Contains(genus);

    /// <summary>Problems with a genus mix (empty = plausible). No trees at all is accepted only where nothing is expected to dominate.</summary>
    public List<string> Problems(Dictionary<string, double> genera)
    {
        var problems = new List<string>();
        if (genera.Count == 0) { if (Need > 1) problems.Add("no trees"); return problems; }
        var top = genera.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToArray();
        int hits = Expect.Count(g => top.Any(t => Is(t, g)));
        if (hits < Math.Min(Need, Expect.Length)) problems.Add($"top-3 {string.Join(", ", top)} include {hits} of the expected {string.Join("/", Expect)} (need {Need})");
        foreach (string g in Implausible)
        {
            double s = genera.Where(kv => Is(kv.Key, g)).Sum(kv => kv.Value);
            if (s > MaxImplausible) problems.Add($"{g} {s:P0} > {MaxImplausible:P0}");
        }
        if (genera.Count < MinGenera) problems.Add($"only {genera.Count} genera");
        var max = genera.Max(kv => kv.Value);
        if (max > MaxGenus) problems.Add($"largest genus {max:P0} > {MaxGenus:P0}");
        return problems;
    }
}

static class Probes
{
    static readonly string[] Temperate = { "Quercus", "Betula", "Pinus", "Abies", "Picea", "Acer", "Fagus", "Larix" };
    static readonly string[] NoOakBirch = { "Quercus", "Betula" };

    public static readonly Probe[] All =
    {
        // Latvia: pine 34%, birch 31%, spruce 18%, grey/black alder 12%, aspen, oak, ash (State Forest Service)
        new("Latvia", 57.0, 25.0, new[] { "Pinus", "Picea", "Betula" }, 3, new[] { "Alnus", "Quercus", "Populus" }, 0.12),
        // Finland (NFI): pine 50%, spruce 30%, birch 17%; oak only in the far south-west
        new("Finland south", 61.5, 25.0, new[] { "Pinus", "Picea", "Betula" }, 3, new[] { "Alnus", "Quercus" }, 0.1),
        new("Finland central", 64.5, 26.0, new[] { "Pinus", "Picea", "Betula" }, 3, new[] { "Alnus", "Quercus", "Acer", "Taxus" }, 0.05),
        new("Finland north", 67.5, 26.5, new[] { "Pinus", "Picea", "Betula" }, 2, new[] { "Alnus", "Quercus", "Acer" }, 0.05),
        // Sweden (NFI): spruce 40%, pine 39%, birch 12%; the south also beech and oak
        new("Sweden south", 57.0, 14.5, new[] { "Picea", "Pinus", "Betula", "Quercus", "Fagus" }, 2, new[] { "Alnus", "Populus" }, 0.12),
        new("Norway east", 60.5, 10.5, new[] { "Picea", "Pinus", "Betula" }, 2, new[] { "Alnus", "Quercus" }, 0.12),
        new("Scottish Highlands", 57.0, -4.5, new[] { "Pinus", "Betula", "Quercus" }, 2, new[] { "Alnus", "Populus" }, 0.12),
        // Upper Rhine plain: oak, hornbeam, beech, maple, pine; floodplain species only by the river
        new("Upper Rhine", 48.6, 7.8, new[] { "Quercus", "Fagus", "Acer", "Pinus", "Carpinus" }, 2, new[] { "Alnus", "Populus" }, 0.1),
        new("Black Forest", 48.0, 8.1, new[] { "Abies", "Picea", "Fagus", "Pinus" }, 2, new[] { "Alnus", "Populus" }, 0.1),
        new("Poland east", 52.5, 22.0, new[] { "Pinus", "Quercus", "Betula", "Picea" }, 2, new[] { "Alnus", "Populus" }, 0.12),
        new("Moscow", 55.75, 37.6, new[] { "Picea", "Pinus", "Betula" }, 2, new[] { "Alnus", "Populus" }, 0.15),
        new("West Siberian taiga", 60.0, 75.0, new[] { "Pinus", "Picea", "Abies", "Betula", "Larix" }, 2, new[] { "Quercus", "Acer" }, 0.05),
        new("Ontario boreal", 48.0, -84.0, new[] { "Picea", "Abies", "Betula", "Populus", "Pinus", "Larix" }, 2, new[] { "Quercus" }, 0.15),
        new("Quebec boreal", 50.0, -73.0, new[] { "Picea", "Abies", "Betula", "Populus", "Larix" }, 2, new[] { "Quercus", "Acer" }, 0.15),
        // Appalachian mixed mesophytic and oak forest
        new("Appalachians", 37.5, -80.5, new[] { "Quercus", "Acer", "Fagus", "Liriodendron", "Pinus", "Tsuga", "Oxydendrum", "Tilia", "Fraxinus" }, 1, new[] { "Alnus", "Betula" }, 0.25),
        new("Pacific Northwest", 46.0, -122.5, new[] { "Pseudotsuga", "Thuja", "Tsuga", "Abies", "Picea" }, 2, new[] { "Betula" }, 0.15),
        // Kaibab Plateau: ponderosa pine, fir, aspen, Douglas fir; dry: few trees
        new("Kaibab Plateau", 36.4, -112.2, new[] { "Pinus", "Abies", "Picea", "Populus", "Pseudotsuga" }, 1, new[] { "Betula", "Quercus" }, 0.25),
        // central Florida: pines, oaks, cabbage palm
        new("Florida central", 28.5, -81.5, new[] { "Pinus", "Quercus", "Sabal", "Taxodium" }, 1, new[] { "Betula", "Lysiloma", "Roystonea", "Abies" }, 0.1),
        new("Amazon", -3.0, -60.0, new[] { "Ceiba", "Swietenia", "Peltogyne", "Cecropia" }, 1, new[] { "Quercus", "Pinus", "Betula", "Abies" }, 0.05),
        new("Atlantic Forest", -23.0, -45.0, new[] { "Ceiba", "Cyathea", "Araucaria", "Jacaranda", "Peltogyne", "Quercus", "Abies" }, 1, new[] { "Betula", "Larix" }, 0.05),
        new("Congo basin", 0.5, 20.0, new[] { "Lophira", "Entandrophragma", "Ceiba", "Diospyros", "Peltogyne" }, 1, new[] { "Quercus", "Pinus", "Betula", "Abies" }, 0.05),
        // Kenya highlands: podocarps, junipers, tree heath, tree ferns
        new("Kenya highlands", -0.5, 36.5, new[] { "Erica", "Abies", "Cyathea", "Afrocarpus", "Dalbergia" }, 1, new[] { "Quercus", "Betula", "Calophyllum" }, 0.05),
        // Highveld / Karoo edge: acacia savanna or nothing
        new("Highveld", -28.74, 24.77, new[] { "Vachellia", "Searsia", "Diospyros", "Combretum" }, 1, new[] { "Quercus", "Betula", "Pinus", "Abies" }, 0.05),
        new("Borneo", 2.0, 113.0, new[] { "Ceiba", "Peltogyne", "Bambusa", "Shorea" }, 1, new[] { "Quercus", "Pinus", "Betula" }, 0.05, MinGenera: 3, MaxGenus: 0.6),
        new("Honshu", 35.5, 137.0, new[] { "Quercus", "Pinus", "Acer", "Fagus" }, 2, new[] { "Betula", "Alnus" }, 0.15),
        new("SE Australia", -35.5, 148.5, new[] { "Eucalyptus", "Acacia", "Dicksonia", "Cyathea", "Alsophila", "Nothofagus" }, 1, new[] { "Betula", "Pinus" }, 0.05, MaxGenus: 0.75),
        new("New Zealand", -40.0, 175.5, new[] { "Podocarpus", "Dacrydium", "Nothofagus", "Alsophila", "Cyathea", "Rhopalostylis" }, 2, new[] { "Betula", "Pinus", "Quercus" }, 0.05),
    };
}
