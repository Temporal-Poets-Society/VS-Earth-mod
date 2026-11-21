using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EarthMap.Core;
using EarthMap.Tests;

/// <summary>
/// The ledger of human works undone (docs/works-1318-ledger.md, decision 0064; the author, 21 November 2026): its facts
/// block, written by tools/works_ledger.py, is what the mod's files and code are: the data files that undo human works (size
/// and SHA-256), the rules versions that turn them on, the works files' areas by kind, every terminal lake's level in
/// lakes.bin and lakes-2.bin, and the lakes of 1318 in tools/lakes-1318.json. A change to any of them, or a new change of
/// this kind, fails here until the ledger is written again with its entry.
/// </summary>
static class LedgerChecks
{
    public static void Run(string root, TestChecks t)
    {
        string page = Path.Combine(root, "docs", "works-1318-ledger.md");
        if (!File.Exists(page)) { t.Check(false, "ledger: docs/works-1318-ledger.md is missing (tools/works_ledger.py writes it)"); return; }
        var lines = File.ReadAllLines(page);
        int start = Array.IndexOf(lines, "```ledger-facts"), end = start < 0 ? -1 : Array.IndexOf(lines, "```", start + 1);
        if (start < 0 || end < 0) { t.Check(false, "ledger: no ```ledger-facts block in docs/works-1318-ledger.md"); return; }
        var pinned = lines[(start + 1)..end].ToHashSet();
        var actual = Facts(root);
        var missing = actual.Where(l => !pinned.Contains(l)).ToList();
        var extra = pinned.Where(l => !actual.Contains(l)).ToList();
        t.Check(missing.Count == 0 && extra.Count == 0,
            $"ledger: docs/works-1318-ledger.md's facts are the files' and the code's ({actual.Count} facts)" +
            (missing.Count + extra.Count == 0 ? "" : $"; not in the ledger: {string.Join(" | ", missing.Take(4))}; in it but not so: {string.Join(" | ", extra.Take(4))}. " +
             "Run tools/works_ledger.py (with the change's entry) and commit the page"));
    }

    static string G(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static List<string> Facts(string root)
    {
        string ed = Path.Combine(root, "earthdata");
        var f = new List<string>();
        foreach (var name in new[] { "works-1318-2.bin", "works-1318-3.bin", "works-1318-4.bin", "lakes.bin", LakeData.FileName2, "reservoirs-v1.json" })
        {
            byte[] b = File.ReadAllBytes(Path.Combine(ed, name));
            f.Add($"file {name} {b.Length} {Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant()}");
        }
        f.Add($"rules Works1318 {Works1318Data.RulesVersion}");
        f.Add($"rules RiverEnds {RiverEnds.Version}");
        f.Add($"rules DamRules {DamRemovalData.NewestRules}");
        f.Add($"rules ChannelRules {TerrainModel.ChannelRulesVersion}");
        foreach (var name in new[] { Works1318Data.FileName, Works1318Data.FileName3, Works1318Data.FileName4 })
        {
            var d = Works1318Data.Read(File.ReadAllBytes(Path.Combine(ed, name)));
            var count = new SortedDictionary<int, int>();
            for (int tile = 0; tile < d.TilesX * d.TilesY; tile++)
            {
                if (d.State(tile) != LakeOutlineData.HasLakes) continue;
                d.Decode(tile, out _, out byte[] flags, out _, out _, out _, out _);
                foreach (byte fl in flags) count[fl] = count.GetValueOrDefault(fl) + 1;
            }
            foreach (var kv in count) f.Add($"works {name} {kv.Key} {kv.Value}");
        }
        foreach (var name in new[] { "lakes.bin", LakeData.FileName2 })
        {
            using var s = File.OpenRead(Path.Combine(ed, name));
            foreach (var l in LakeData.Read(s, 360 * 30).Lakes) f.Add($"terminal {name} {G(l.Level)} {l.Name}");
        }
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "lakes-1318.json"))))
            foreach (var a in doc.RootElement.GetProperty("areas").EnumerateArray())
                f.Add($"lake1318 {a.GetProperty("key").GetString()} {a.GetProperty("kind").GetString()} " +
                      G(a.TryGetProperty("level", out var lv) ? lv.GetDouble() : 0));
        return f;
    }
}
