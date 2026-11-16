using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

/// <summary>
/// Decision 0089 (2.1.2): the Mod DB reads a mod's modinfo.json and worldconfig.json with anegostudios/modpeek
/// (LibModPeek ParseJSON.cs and Validate.cs) and refuses the upload on any error, where the game itself reads more. 2.1.1's
/// World height slider carried the game's skipValues and alarm keys, and the upload failed ("Unexpected property
/// 'worldConfigAttributes' on WorldConfigAttributes[1]": modpeek names the parent, not the key). These checks apply
/// modpeek's rules to the repository's files and, when one is built, to the release zip's.
/// </summary>
static class ModDbChecks
{
    // modpeek's keys (compared lower-case), its version pattern and its world config data types (EnumDataType)
    static readonly HashSet<string> ModInfoKeys = new() { "custom", "$schema", "name", "modid", "version", "networkversion", "texturesize", "side", "type",
        "requiredonclient", "requiredonserver", "iconpath", "description", "website", "backgroundpaths", "authors", "contributors", "dependencies" };
    static readonly HashSet<string> RootKeys = new() { "$schema", "playstyles", "worldconfigattributes" };
    static readonly HashSet<string> PlayStyleKeys = new() { "code", "playlistcode", "langcode", "listorder", "mods", "worldtype", "worldconfig" };
    static readonly HashSet<string> AttributeKeys = new() { "datatype", "category", "code", "min", "max", "step", "oncustomizescreen", "default", "values",
        "names", "onlyduringworldcreate" };
    static readonly Regex Version = new(@"^\d{1,5}\.\d{1,4}\.\d{1,4}(?:-(?:rc|pre|dev)\.\d{1,4})?$");

    public static void Run(string root, Action<bool, string> check)
    {
        string modinfo = File.ReadAllText(Path.Combine(root, "modinfo.json")), worldconfig = File.ReadAllText(Path.Combine(root, "worldconfig.json"));
        var errors = Errors(modinfo, worldconfig);
        check(errors.Count == 0, "modinfo.json and worldconfig.json pass the Mod DB's parser (modpeek)" + (errors.Count > 0 ? ": " + string.Join("; ", errors) : ""));

        // negative controls: the check catches what the Mod DB refused, and a mismatched names/values pair
        var refused = Errors(modinfo, worldconfig.Replace("\"code\":\"earthWorldHeight\",", "\"code\":\"earthWorldHeight\",\"skipValues\":[\"64...192\"],\"alarm\":512,"));
        check(refused.Count == 2 && refused.All(e => e.Contains("earthWorldHeight")), "The Mod DB check refuses 2.1.1's World height keys: " + string.Join("; ", refused));
        check(Errors(modinfo.Replace("\"version\": \"", "\"version\": \"v"), worldconfig).Any(e => e.Contains("version")), "The Mod DB check refuses a version outside modpeek's pattern");

        using var manifest = JsonDocument.Parse(modinfo);
        string version = manifest.RootElement.GetProperty("version").GetString();
        string zipPath = Path.Combine(Environment.GetEnvironmentVariable("EARTH_RELEASE_DIR") is { Length: > 0 } dir ? dir : Path.Combine(root, "release"), "earth-" + version + ".zip");
        if (!File.Exists(zipPath)) { Console.WriteLine("Skipping the Mod DB check of the release zip: none at " + zipPath); return; }
        using var zip = ZipFile.OpenRead(zipPath);
        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        var zipErrors = zip.GetEntry("modinfo.json") == null || zip.GetEntry("worldconfig.json") == null
            ? new List<string> { "modinfo.json or worldconfig.json missing at the zip's root" }
            : Errors(Read("modinfo.json"), Read("worldconfig.json"));
        check(zipErrors.Count == 0, "The release zip's modinfo.json and worldconfig.json pass the Mod DB's parser" + (zipErrors.Count > 0 ? ": " + string.Join("; ", zipErrors) : ""));
    }

    /// <summary>What modpeek would report for these two files (its parse and validate steps), one line each.</summary>
    public static List<string> Errors(string modinfo, string worldconfig)
    {
        var e = new List<string>();
        using (var doc = JsonDocument.Parse(modinfo))
        {
            var m = doc.RootElement;
            foreach (var p in m.EnumerateObject())
                if (!ModInfoKeys.Contains(p.Name.ToLowerInvariant())) e.Add("modinfo.json: unexpected key " + p.Name);
            string S(string key) => m.EnumerateObject().Where(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                .Select(p => p.Value.GetString()).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(S("name"))) e.Add("modinfo.json: no name");
            if (!ModInfo.IsValidModID(S("modid") ?? "")) e.Add("modinfo.json: modid " + S("modid"));
            if (!Version.IsMatch(S("version") ?? "")) e.Add("modinfo.json: version " + S("version"));
            if (S("networkversion") is { Length: > 0 } nv && !Version.IsMatch(nv)) e.Add("modinfo.json: networkversion " + nv);
            if (S("side") is string side && !Enum.TryParse<EnumAppSide>(side, true, out _)) e.Add("modinfo.json: side " + side);
            if (S("type") is string type && !Enum.TryParse<EnumModType>(type, true, out _)) e.Add("modinfo.json: type " + type);
            if (S("website") is { Length: > 0 } site && !Uri.TryCreate(site, UriKind.Absolute, out _)) e.Add("modinfo.json: website " + site);
            foreach (var p in m.EnumerateObject().Where(p => p.Name.Equals("dependencies", StringComparison.OrdinalIgnoreCase)))
                foreach (var d in p.Value.EnumerateObject())
                {
                    if (!ModInfo.IsValidModID(d.Name)) e.Add("modinfo.json: dependency id " + d.Name);
                    string v = d.Value.ValueKind == JsonValueKind.String ? d.Value.GetString() : null;
                    if (!string.IsNullOrEmpty(v) && v != "*" && !Version.IsMatch(v)) e.Add("modinfo.json: dependency " + d.Name + " version " + v);
                }
        }
        using (var doc = JsonDocument.Parse(worldconfig))
        {
            var w = doc.RootElement;
            foreach (var p in w.EnumerateObject())
                if (!RootKeys.Contains(p.Name.ToLowerInvariant())) e.Add("worldconfig.json: unexpected key " + p.Name);
            JsonElement Prop(JsonElement o, string key) => o.EnumerateObject().Where(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault();
            var styles = Prop(w, "playStyles");
            if (styles.ValueKind != JsonValueKind.Array) e.Add("worldconfig.json: no playStyles");
            else
            {
                int i = 0;
                foreach (var s in styles.EnumerateArray())
                {
                    string at = "playStyles[" + i++ + "]";
                    foreach (var p in s.EnumerateObject())
                        if (!PlayStyleKeys.Contains(p.Name.ToLowerInvariant())) e.Add(at + ": unexpected key " + p.Name);
                    foreach (string req in new[] { "code", "playListCode", "langCode", "worldType" })
                        if (Prop(s, req) is var v && (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))) e.Add(at + ": no " + req);
                    if (Prop(s, "listOrder") is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Number }) e.Add(at + ": listOrder not a number");
                    if (Prop(s, "worldConfig") is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Object }) e.Add(at + ": worldConfig not an object");
                    if (Prop(s, "mods") is { ValueKind: JsonValueKind.Array } mods)
                        foreach (var mod in mods.EnumerateArray())
                            if (mod.ValueKind != JsonValueKind.String || !ModInfo.IsValidModID(mod.GetString())) e.Add(at + ": mod id " + mod);
                }
            }
            var attrs = Prop(w, "worldConfigAttributes");
            if (attrs.ValueKind != JsonValueKind.Array) e.Add("worldconfig.json: no worldConfigAttributes");
            else
            {
                int i = 0;
                foreach (var a in attrs.EnumerateArray())
                {
                    string code = Prop(a, "code") is { ValueKind: JsonValueKind.String } c ? c.GetString() : null;
                    string at = "worldConfigAttributes[" + i++ + "] " + code;
                    foreach (var p in a.EnumerateObject())
                    {
                        string k = p.Name.ToLowerInvariant();
                        if (!AttributeKeys.Contains(k)) { e.Add(at + ": unexpected key " + p.Name); continue; }
                        var kind = p.Value.ValueKind;
                        bool ok = k switch
                        {
                            "datatype" => kind == JsonValueKind.String && Enum.TryParse<EnumDataType>(p.Value.GetString(), true, out _),
                            "category" or "code" or "default" => kind is JsonValueKind.String or JsonValueKind.Null,
                            "min" or "max" or "step" => kind == JsonValueKind.Number,
                            "oncustomizescreen" or "onlyduringworldcreate" => kind is JsonValueKind.True or JsonValueKind.False,
                            _ => kind == JsonValueKind.Array && p.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),   // values, names
                        };
                        if (!ok) e.Add(at + ": " + p.Name + " " + p.Value.GetRawText());
                    }
                    if (Prop(a, "category") is var cat && (cat.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cat.GetString()))) e.Add(at + ": no category");
                    if (string.IsNullOrWhiteSpace(code)) e.Add(at + ": no code");
                    if (Prop(a, "names") is { ValueKind: JsonValueKind.Array } names
                        && !(Prop(a, "values") is { ValueKind: JsonValueKind.Array } values && values.GetArrayLength() == names.GetArrayLength()))
                        e.Add(at + ": names and values differ in length");
                }
            }
        }
        return e;
    }
}
