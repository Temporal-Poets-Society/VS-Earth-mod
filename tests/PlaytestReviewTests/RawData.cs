using System;
using System.IO;

/// <summary>(FP-19) Earth's raw data: EARTH_RAW, else the repo's rawdata link (the main checkout), else the main checkout's
/// rawdata seen from a worktree three folders deep (.claude\worktrees\name).</summary>
static class RawData
{
    public static string Folder(string root)
    {
        if (Environment.GetEnvironmentVariable("EARTH_RAW") is { Length: > 0 } raw) return raw;
        string here = Path.Combine(root, "rawdata");
        return Directory.Exists(here) ? here : Path.Combine(root, "../../../rawdata");
    }
}
