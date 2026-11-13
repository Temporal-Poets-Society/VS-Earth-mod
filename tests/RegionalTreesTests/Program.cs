using EarthMap.Core;

string root = args.Length > 0 && !args[0].StartsWith("--") ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
string Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
if (args.Contains("--region-climate"))
{
    RegionClimate.Write(new Harness(root), Option("--region-climate") ?? Path.Combine(root, "tools/tree-region-climate.json"));
    return 0;
}
if (args.Contains("--report"))
{
    var harness = new Harness(root);
    if (args.Contains("--zones")) harness.Zones = new ClimateZones(File.ReadAllText(Path.Combine(root, "assets/earth/config/zones.json")));
    Report.Run(harness, args);
    return 0;
}
return Tests.Run(root, args.Contains("--quick"));
