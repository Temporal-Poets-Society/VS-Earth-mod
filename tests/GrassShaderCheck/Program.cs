using System.Reflection;
using System.Text.RegularExpressions;
using EarthMap.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

// EA-0154 (decision 0091): Earth's seven grass calendars in one colour map. The four chunk shaders with Earth's row for its map
// (Core.GrassSeasonShader.Patch) compile and link on this machine's driver, with the game's settings low and high; and the
// edited calcColorMapUvs, run on the GPU (transform feedback) for 2,000 world positions, takes for Earth's map and each band
// the row vanilla's code takes in a 16-row map, moved into the band, with the same u, and for any other map (or Earth's place
// without bits 13-15) vanilla's row unchanged.
//   dotnet run -c Release --project tests\GrassShaderCheck
static class Program
{
    static int failures, checks;
    static readonly Dictionary<string, string> includes = new();

    static void Check(bool ok, string what, string log = null)
    {
        checks++;
        if (!ok) { failures++; Console.WriteLine("  FAIL " + what + (string.IsNullOrWhiteSpace(log) ? "" : "\n       " + log.Trim().Replace("\n", "\n       "))); }
    }

    static int Main()
    {
        string vs = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "VintageStoryPath").Value;
        string shaders = Path.Combine(vs, "assets", "game", "shaders");
        foreach (string dir in new[] { Path.Combine(vs, "assets", "game", "shaderincludes"), shaders })
            foreach (string f in Directory.GetFiles(dir)) includes[Path.GetFileName(f).ToLowerInvariant()] = File.ReadAllText(f);

        NativeWindow window;
        try
        {
            window = new NativeWindow(new NativeWindowSettings { StartVisible = false, Title = "Earth grass shader check", APIVersion = new Version(3, 3), Profile = ContextProfile.Core });
            window.MakeCurrent();
        }
        catch (Exception e)
        {
            Console.WriteLine("SKIP: no OpenGL context here (" + e.GetType().Name + ": " + e.Message.Split('\n')[0] + ")");
            return 3;
        }
        using (window)
        {
            Console.WriteLine($"driver: {GL.GetString(StringName.Renderer)}, OpenGL {GL.GetString(StringName.Version)}");
            var low = new Dictionary<string, int>
            {
                ["USESSBO"] = 0, ["WAVINGSTUFF"] = 0, ["FOAMEFFECT"] = 0, ["SHINYEFFECT"] = 0, ["SSAOLEVEL"] = 0, ["NORMALVIEW"] = 0,
                ["GODRAYS"] = 0, ["MINBRIGHT"] = 0, ["SHADOWQUALITY"] = 0, ["DYNLIGHTS"] = 0, ["MAXANIMATEDELEMENTS"] = 230, ["FXAA"] = 0, ["BLOOM"] = 0, ["USEOIT"] = 1,
            };
            var high = new Dictionary<string, int>
            {
                ["USESSBO"] = 0, ["WAVINGSTUFF"] = 1, ["FOAMEFFECT"] = 1, ["SHINYEFFECT"] = 1, ["SSAOLEVEL"] = 2, ["NORMALVIEW"] = 0,
                ["GODRAYS"] = 1, ["MINBRIGHT"] = 0, ["SHADOWQUALITY"] = 2, ["DYNLIGHTS"] = 20, ["MAXANIMATEDELEMENTS"] = 230, ["FXAA"] = 1, ["BLOOM"] = 1, ["USEOIT"] = 1,
            };
            foreach (var (name, defs) in new[] { ("low settings", low), ("high settings", high) })
                foreach (string pass in GrassSeasonShader.ChunkPasses)
                {
                    string v = Load(shaders, pass + ".vsh"), f = Load(shaders, pass + ".fsh");
                    Delete(Build(pass + ", " + name + ": vanilla (the check itself)", v, f, defs));
                    string edited = GrassSeasonShader.Patch(v, 26);
                    Check(edited != null && GrassSeasonShader.Carries(edited, 26), pass + ": Earth's row goes in");
                    Delete(Build(pass + ", " + name + ": with Earth's grass season rows", edited, f, defs));
                    Delete(Build(pass + ", " + name + ": Earth's map at place 0", GrassSeasonShader.Patch(v, 0), f, defs));
                    Delete(Build(pass + ", " + name + ": Earth's map at place 39", GrassSeasonShader.Patch(v, 39), f, defs));
                }
            Rows();
        }
        Console.WriteLine(failures == 0 ? $"ALL OK ({checks} checks)" : $"{failures} FAILED of {checks}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>The edited calcColorMapUvs on the GPU: the season uv for Earth's map in each band against vanilla's 16-row row.</summary>
    static void Rows()
    {
        const int earth = 26, other = 3, n = 2000;
        string code = "#version 330 core\n" + Includes("#include noise3d.ash\n#include colormap.vsh\n", new HashSet<string>()) + @"
layout(location = 0) in int cmdIn;
layout(location = 1) in vec3 posIn;
void main() { calcColorMapUvs(cmdIn, vec4(posIn, 1.0), 1.0, false); gl_Position = vec4(0.0); }
";
        string edited = GrassSeasonShader.Patch(code, earth);
        Check(edited != null, "the transform feedback shader takes Earth's row");
        if (edited == null) return;
        int vsh = Compile(ShaderType.VertexShader, edited, out string log);
        if (vsh == 0) { Check(false, "the transform feedback shader compiles", log); return; }
        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vsh);
        GL.TransformFeedbackVaryings(prog, 1, new[] { "seasonColorMapUv" }, TransformFeedbackMode.InterleavedAttribs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linked);
        if (linked != 1) { Check(false, "the transform feedback shader links", GL.GetProgramInfoLog(prog)); return; }
        GL.UseProgram(prog);

        // an atlas 4096 high; vanilla's 16-row map at place 3, Earth's 168-row map at place 26 (rect x, y, width, height in uv)
        const float atlas = 4096f;
        float[] rects = new float[160];
        void Rect(int i, float x, float y, float w, float h) { rects[4 * i] = x; rects[4 * i + 1] = y; rects[4 * i + 2] = w; rects[4 * i + 3] = h; }
        Rect(other, 0.25f, 0.5f, 128 / atlas, 16 / atlas);
        Rect(earth, 0.5f, 0.125f, 128 / atlas, GrassSeasonShader.Height / atlas);
        GL.Uniform4(GL.GetUniformLocation(prog, "colorMapRects"), 40, rects);
        GL.Uniform1(GL.GetUniformLocation(prog, "seasonRel"), 0.37f);
        GL.Uniform1(GL.GetUniformLocation(prog, "seaLevel"), 110f);
        GL.Uniform1(GL.GetUniformLocation(prog, "atlasHeight"), atlas);
        GL.Uniform1(GL.GetUniformLocation(prog, "seasonTemperature"), 0f);

        var rnd = new Random(20261117);
        var pos = new float[3 * n];
        for (int i = 0; i < pos.Length; i++) pos[i] = (float)(rnd.NextDouble() * 4000 - 2000);
        // temperature and rain bytes, climate map 0: the same for every run
        int Data(int season, int bits) => season | (bits << GrassSeasonShader.BandShift) | (150 << 16) | (120 << 24);

        float[] Run(int season, int bits)
        {
            int vao = GL.GenVertexArray(), cmdBuf = GL.GenBuffer(), posBuf = GL.GenBuffer(), outBuf = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, cmdBuf);
            GL.BufferData(BufferTarget.ArrayBuffer, n * 4, Enumerable.Repeat(Data(season, bits), n).ToArray(), BufferUsageHint.StaticDraw);
            GL.VertexAttribIPointer(0, 1, VertexAttribIntegerType.Int, 4, IntPtr.Zero);
            GL.EnableVertexAttribArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, posBuf);
            GL.BufferData(BufferTarget.ArrayBuffer, pos.Length * 4, pos, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(1);
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, outBuf);
            GL.BufferData(BufferTarget.TransformFeedbackBuffer, n * 8, IntPtr.Zero, BufferUsageHint.StaticRead);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, outBuf);
            GL.Enable(EnableCap.RasterizerDiscard);
            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points);
            GL.DrawArrays(PrimitiveType.Points, 0, n);
            GL.EndTransformFeedback();
            GL.Disable(EnableCap.RasterizerDiscard);
            GL.Finish();
            var result = new float[2 * n];
            GL.GetBufferSubData(BufferTarget.TransformFeedbackBuffer, IntPtr.Zero, n * 8, result);
            GL.DeleteBuffer(cmdBuf); GL.DeleteBuffer(posBuf); GL.DeleteBuffer(outBuf); GL.DeleteVertexArray(vao);
            return result;
        }

        var vanilla = Run(other + 1, 0);
        double worst = 0; int clamped = 0;
        for (int band = 0; band < GrassSeasonShader.Bands; band++)
        {
            var packed = Run(earth + 1, band + 1);
            int wrong = 0;
            for (int i = 0; i < n; i++)
            {
                double u16 = (vanilla[2 * i] - rects[4 * other]) / rects[4 * other + 2], v16 = (vanilla[2 * i + 1] - rects[4 * other + 1]) / rects[4 * other + 3] * 16;
                double up = (packed[2 * i] - rects[4 * earth]) / rects[4 * earth + 2], vp = (packed[2 * i + 1] - rects[4 * earth + 1]) / rects[4 * earth + 3] * GrassSeasonShader.Height;
                double want = GrassSeasonShader.Row(band, v16), err = Math.Max(Math.Abs(vp - want), Math.Abs(up - u16) * 128);
                worst = Math.Max(worst, err);
                if (band == 0 && (v16 < 0.501 || v16 > 15.499)) clamped++;
                if (err > 0.01 || vp < band * GrassSeasonShader.Pitch + GrassSeasonShader.Pad + 0.499 || vp > band * GrassSeasonShader.Pitch + GrassSeasonShader.Pad + 15.501) wrong++;
            }
            Check(wrong == 0, $"GPU: band {band}: Earth's row is vanilla's 16-row row moved into the band, the same u ({wrong} of {n} off)");
        }
        Check(clamped > 100 && clamped < n, $"GPU: the noise reaches the band's first and last row centres at {clamped} of {n} places (the edge rows are tested)");
        Console.WriteLine($"  GPU rows: worst difference {worst:0.00000} texels over {GrassSeasonShader.Bands} bands and {n} places");

        // Earth's place without bits 13-15 (shaders kept from an Earth world, in another world): vanilla's row for that map
        var noBits = Run(earth + 1, 0);
        int moved = 0;
        for (int i = 0; i < n; i++)
        {
            double v16 = (vanilla[2 * i + 1] - rects[4 * other + 1]) / rects[4 * other + 3] * 16;
            double h = GrassSeasonShader.Height, vp = (noBits[2 * i + 1] - rects[4 * earth + 1]) / rects[4 * earth + 3] * h;
            // vanilla's row for a map of h rows: the noise clamped to rows 0.5 and 15.5 whatever the height (where the 16-row
            // run didn't clamp, its row over 16 is the noise itself)
            double noise = v16 / 16;
            if (v16 > 0.5001 && v16 < 15.4999 && Math.Abs(vp - Math.Clamp(noise * h, 0.5, 15.5)) > 0.01) moved++;
        }
        Check(moved == 0, $"GPU: Earth's place without bits 13-15 keeps vanilla's row ({moved} of {n} moved)");
        GL.DeleteProgram(prog);
    }

    static void Delete(int prog) { if (prog != 0) GL.DeleteProgram(prog); }

    static string Load(string dir, string file) => Includes(File.ReadAllText(Path.Combine(dir, file)), new HashSet<string>());

    /// <summary>#include lines replaced by the files, each once (ShaderRegistry.HandleIncludes).</summary>
    static string Includes(string code, HashSet<string> seen) => Regex.Replace(code, "^#include\\s+(.*)", m =>
    {
        string name = m.Groups[1].Value.Trim().ToLowerInvariant();
        if (!seen.Add(name)) return "";
        return includes.TryGetValue(name, out string text) ? Includes(text, seen) : "";
    }, RegexOptions.Multiline);

    /// <summary>The game's defines go on the line after #version (ClientPlatformWindows.CompileShader).</summary>
    static string Prefix(string code, Dictionary<string, int> defs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in defs) sb.Append("#define ").Append(kv.Key).Append(' ').Append(kv.Value).Append("\r\n");
        int start = code.IndexOf('\n', Math.Max(0, code.IndexOf("#version", StringComparison.Ordinal))) + 1;
        return code.Insert(start, sb.ToString());
    }

    static int Build(string what, string vertex, string fragment, Dictionary<string, int> defs)
    {
        if (vertex == null || fragment == null) { Check(false, what + ": the code couldn't be built (edit not found)"); return 0; }
        int vsh = Compile(ShaderType.VertexShader, Prefix(vertex, defs), out string vlog);
        int fsh = Compile(ShaderType.FragmentShader, Prefix(fragment, defs), out string flog);
        if (vsh == 0 || fsh == 0)
        {
            Check(false, what + ": compiles", (vsh == 0 ? "vertex: " + vlog : "") + (fsh == 0 ? "\nfragment: " + flog : ""));
            if (vsh != 0) GL.DeleteShader(vsh);
            if (fsh != 0) GL.DeleteShader(fsh);
            return 0;
        }
        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vsh); GL.AttachShader(prog, fsh); GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int linked);
        string llog = GL.GetProgramInfoLog(prog);
        GL.DeleteShader(vsh); GL.DeleteShader(fsh);
        Check(linked == 1, what + ": compiles and links", llog);
        if (linked == 1) return prog;
        GL.DeleteProgram(prog);
        return 0;
    }

    static int Compile(ShaderType type, string code, out string log)
    {
        int id = GL.CreateShader(type);
        GL.ShaderSource(id, code);
        GL.CompileShader(id);
        GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
        log = GL.GetShaderInfoLog(id);
        if (ok == 1) return id;
        GL.DeleteShader(id);
        return 0;
    }
}
