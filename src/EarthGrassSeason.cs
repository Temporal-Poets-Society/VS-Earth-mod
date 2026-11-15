using System;
using EarthMap.Core;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace EarthMap
{
    /// <summary>Client settings (ModConfig/earth-client.json).</summary>
    public class EarthClientConfig
    {
        /// <summary>Ranges step 5b: grass greens in each place's wet months and turns straw in its dry months, from Earth's
        /// monthly rain. Off: vanilla's grass colour (by yearly rainfall and the temperature seasons).</summary>
        public bool SeasonalGrassColour = true;
    }

    /// <summary>
    /// Ranges step 5b, client: the grass colour follows the wet and dry seasons. Vanilla colours grass from a rainfall byte
    /// per block (ClientWorldMap.getColorMapData, read into the chunk mesh when it is built) through its plant tint, straw at low
    /// rainfall and green at high. For blocks tinted with the "seasonalGrass" map (grass on soil, tall grass) this replaces that
    /// byte with the rainfall of the time of year (<see cref="GrassSeason"/>): the yearly rainfall vanilla already reads, made
    /// wetter or drier by the place's own seasons. The mesh keeps the colour it was built with, so all chunks are drawn again
    /// twice a game month. Applies to every Earth world with weather data (nothing is saved). No shader is changed.
    /// The same blocks also follow the place's own growing season (Core.GrassPhenology): instead of vanilla's one calendar
    /// ("seasonalGrass"), each place gets one of Earth's season maps (earthgrass-*.png): dead and brown while it is cold,
    /// a bright fresh green in the weeks after it warms, summer green, then straw. In 1.22.7 the chunk mesh builds the colour
    /// data in ChunkTesselator.TesselateBlock, so that call is replaced (a transpiler); getColorMapData keeps the postfix for
    /// blocks drawn through the world accessor.
    /// </summary>
    public class EarthGrassSeason : ModSystem
    {
        const string HarmonyId = "earth.grassseason";
        const string GrassMap = "seasonalGrass";
        /// <summary>Blocks between the wetness curves' lattice corners.</summary>
        const int CornerSpacing = 32;
        /// <summary>The phenology maps' cells: 32 blocks (a chunk) a side.</summary>
        const int PhenologyShift = 5, PhenologyCell = 1 << PhenologyShift;

        static volatile bool active;
        static volatile float yearRel;
        static EarthGeometry geometry;
        static WeatherData weather;
        static readonly BoundedCache<long, float[]> curves = new(200_000);
        /// <summary>Counts the worlds the caches were built for: a mesh thread's last cell (below) from another world misses.
        /// Raised after the new geometry and weather are set.</summary>
        static volatile int generation;
        /// <summary>The patches are in (they stay for the process; they do nothing while no world has Earth's weather).</summary>
        static bool patched;

        ICoreClientAPI capi;
        EarthMapSystem earth;
        EarthParams builtFor;
        bool enabled = true;
        int drawnStep = -1;
        long tickId;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            try
            {
                var cfg = api.LoadModConfig<EarthClientConfig>("earth-client.json");
                if (cfg == null) { cfg = new EarthClientConfig(); api.StoreModConfig(cfg, "earth-client.json"); }
                enabled = cfg.SeasonalGrassColour;
            }
            catch (Exception e) { Mod.Logger.Error("Earth Map: earth-client.json can't be read ({0}); seasonal grass colour stays on.", e.Message); }
            if (!enabled) { Mod.Logger.Notification("Earth Map: seasonal grass colour is off (earth-client.json)."); return; }
            earth = api.ModLoader.GetModSystem<EarthMapSystem>();
            // ER-18: at once when the params are set (the two-second tick stays for the season's changes)
            if (earth != null) earth.ParamsReady += _ => Tick();
            if (!patched)
            {
                // PatchVanilla's GrassSeasonTable: the chunk mesh's colour data (vanilla's tesselator builds it itself, 1.22.7:
                // ChunkTesselator.TesselateBlock) and the world accessor's (microblocks and the like), checked first, both or
                // neither. Off (one line naming the member a game update changed): grass keeps vanilla's colour, nothing ticks.
                var pv = PatchVanilla.For(api, Mod, new Harmony(HarmonyId));
                pv.Apply(PatchVanilla.GrassSeasonTable());
                if (!pv.On(PatchVanilla.Feature.GrassSeason)) return;
                patched = true;
            }
            tickId = api.Event.RegisterGameTickListener(_ => Tick(), 2000);
        }

        public override void Dispose()
        {
            if (capi != null && tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
            active = false;
            geometry = null;
            weather = null;
            curves.Clear();
            phenologyMaps = null;
            monthShapes.Clear();
            phenologyCache.Clear();
            generation++;
            base.Dispose();
        }

        void Tick()
        {
            var p = earth?.Params;
            if (p != builtFor)
            {
                builtFor = p;
                curves.Clear();
                monthShapes.Clear();
                phenologyCache.Clear();
                var w = earth?.Data?.Weather;
                bool on = p != null && p.Enabled && w != null;
                geometry = on ? p.BuildGeometry() : null;
                weather = on ? w : null;
                generation++;
                active = on;
                drawnStep = -1;
                if (on) Mod.Logger.Notification("Earth Map: grass colour follows each place's wet and dry seasons (earth-client.json turns it off).");
            }
            if (!active) return;
            if (!phenologyResolved && TesselatorPatched && ResolvePhenologyMaps())
            {
                phenologyResolved = true;
                // chunks drawn before the maps were known
                if (phenologyMaps != null) RedrawAll();
            }
            var cal = capi.World.Calendar;
            double days = cal.TotalHours / Math.Max(1, cal.HoursPerDay), y = cal.DaysPerYear > 0 ? cal.DaysPerYear : 108;
            double yr = (days - y * Math.Floor(days / y)) / y;
            int step = (int)(yr * GrassSeason.Steps) % GrassSeason.Steps;
            // (CR-E2) the wetness of the whole step, at its centre, not of the moment: the meshes are redrawn only when the step
            // changes, so chunks meshed at different moments within it must get the same rain bytes (no colour seams between
            // neighbouring chunks at a monsoon's onset)
            yearRel = (float)((step + 0.5) / GrassSeason.Steps);
            // the meshes hold the colour they were built with
            if (drawnStep >= 0 && step != drawnStep) RedrawAll();
            drawnStep = step;
        }

        /// <summary>Draws every chunk again, through the client world's RedrawAllBlocks (by name: not in the API). (CR-A9) Said
        /// once when a game update took it away, as the grass then keeps the colour each chunk was first drawn with.</summary>
        void RedrawAll()
        {
            var redraw = AccessTools.Method(capi.World.GetType(), "RedrawAllBlocks");
            if (redraw == null)
            {
                if (!redrawMissingLogged)
                {
                    redrawMissingLogged = true;
                    Mod.Logger.Warning("Earth Map: the client world has no RedrawAllBlocks; grass keeps the colour each chunk was drawn with until the chunk is drawn again.");
                }
                return;
            }
            redraw.Invoke(capi.World, null);
        }
        bool redrawMissingLogged;

        internal static void ColorMapDataPostfix(Block block, int posX, int posZ, ref ColorMapData __result)
        {
            if (!active || block?.SeasonColorMap == null || !string.Equals(block.SeasonColorMap, GrassMap, StringComparison.Ordinal)) return;
            __result = Grass(__result.SeasonMapIndex, __result.ClimateMapIndex, __result.Temperature, __result.Rainfall, __result.FrostableBit != 0, posX, posZ);
        }

        // ---- the chunk mesh (1.22.7 builds ColorMapData in ChunkTesselator.TesselateBlock, not through getColorMapData)
        /// <summary>The transpiler found and replaced the mesh's colour data (PatchVanilla turns the feature off when not).</summary>
        internal static bool TesselatorPatched;
        /// <summary>Season map index + 1 of each GrassPhenology map once the colour maps are loaded; null before (or when
        /// they are missing), and grass then keeps vanilla's season map.</summary>
        static volatile int[] phenologyMaps;
        static readonly BoundedCache<long, float[]> monthShapes = new(100_000);
        static readonly BoundedCache<long, byte> phenologyCache = new(400_000);

        /// <summary>Replaces "new ColorMapData(season, climate, temperature, rain, frostable)" in TesselateBlock(Block block,
        /// ..., int posX, int posZ, ...) by <see cref="TesselatorColorMap"/> with the block and its position.</summary>
        internal static System.Collections.Generic.IEnumerable<CodeInstruction> TesselateTranspiler(System.Collections.Generic.IEnumerable<CodeInstruction> instructions, System.Reflection.MethodBase original)
        {
            var list = new System.Collections.Generic.List<CodeInstruction>(instructions);
            var ctor = AccessTools.Constructor(typeof(ColorMapData), new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) });
            var hook = AccessTools.Method(typeof(EarthGrassSeason), nameof(TesselatorColorMap));
            var ps = original.GetParameters();
            int block = Array.FindIndex(ps, p => p.ParameterType == typeof(Block));
            int x = Array.FindIndex(ps, p => p.Name == "posX" && p.ParameterType == typeof(int));
            int z = Array.FindIndex(ps, p => p.Name == "posZ" && p.ParameterType == typeof(int));
            if (ctor == null || hook == null || original.IsStatic || block < 0 || x < 0 || z < 0) return list;
            var result = new System.Collections.Generic.List<CodeInstruction>(list.Count + 8);
            foreach (var ins in list)
            {
                bool newobj = ins.opcode == System.Reflection.Emit.OpCodes.Newobj && Equals(ins.operand, ctor);
                bool inPlace = ins.opcode == System.Reflection.Emit.OpCodes.Call && Equals(ins.operand, ctor);
                if (!newobj && !inPlace) { result.Add(ins); continue; }
                var first = CodeInstruction.LoadArgument(block + 1);
                first.labels.AddRange(ins.labels);
                first.blocks.AddRange(ins.blocks);
                result.Add(first);
                result.Add(CodeInstruction.LoadArgument(x + 1));
                result.Add(CodeInstruction.LoadArgument(z + 1));
                result.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Call, hook));
                // "call .ctor" initialises a struct in place: its address is below the arguments
                if (inPlace) result.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Stobj, typeof(ColorMapData)));
                TesselatorPatched = true;
            }
            return result;
        }

        public static ColorMapData TesselatorColorMap(int season, int climate, int temperature, int rain, bool frostable, Block block, int posX, int posZ)
        {
            if (active && block?.SeasonColorMap != null && string.Equals(block.SeasonColorMap, GrassMap, StringComparison.Ordinal))
                return Grass(season, climate, temperature, rain, frostable, posX, posZ);
            return new ColorMapData(season, climate, temperature, rain, frostable);
        }

        /// <summary>Grass: the rainfall of the time of year (step 5b) and the place's own season map (GrassPhenology).</summary>
        static ColorMapData Grass(int season, int climate, int temperature, int rain, bool frostable, int posX, int posZ)
        {
            if (rain > 0)
            {
                double anomaly = Anomaly(posX, posZ);
                if (anomaly != 0) rain = GrassSeason.SeasonalRain(rain, anomaly);
            }
            var maps = phenologyMaps;
            if (maps != null)
            {
                int k = Phenology(posX, posZ, temperature);
                if (k >= 0 && k < maps.Length && maps[k] > 0) season = maps[k];
            }
            return new ColorMapData(season, climate, temperature, rain, frostable);
        }

        /// <summary>The GrassPhenology map at a block: the weather's monthly shape (per 32-block cell) around the block's own
        /// yearly mean, which the colour data's temperature byte carries (vanilla's scale, -20..40 °C, with Earth's lapse
        /// rate). -1 where there is no weather.</summary>
        static int Phenology(int x, int z, int temperatureByte)
        {
            int cx = x >> PhenologyShift, cz = z >> PhenologyShift, tb = temperatureByte & 0xFF;
            // a mesh thread draws a chunk's blocks together, all in one cell: its last cell's maps first
            var last = lastPhenology ??= new LastPhenology();
            int gen = generation;
            if (last.Generation == gen && last.Cx == cx && last.Cz == cz && last.Maps[tb] != LastPhenology.Unknown) return last.Maps[tb];
            int result = Phenology(cx, cz, temperatureByte, out bool known);
            if (!known) return result;
            if (last.Generation != gen || last.Cx != cx || last.Cz != cz)
            {
                last.Generation = gen; last.Cx = cx; last.Cz = cz;
                Array.Fill(last.Maps, LastPhenology.Unknown);
            }
            last.Maps[tb] = (sbyte)result;
            return result;
        }

        /// <summary>A mesh thread's last phenology cell: the map per temperature byte (<see cref="Unknown"/> until asked).</summary>
        sealed class LastPhenology
        {
            public const sbyte Unknown = -2;
            public int Generation = -1, Cx, Cz;
            public readonly sbyte[] Maps = new sbyte[256];
        }
        [ThreadStatic] static LastPhenology lastPhenology;

        /// <summary>The map at a 32-block cell for a temperature byte, through the shared caches; <paramref name="known"/> is
        /// false while there is no weather (-1, not cached).</summary>
        static int Phenology(int cx, int cz, int temperatureByte, out bool known)
        {
            known = true;
            long key = ((long)(cx & 0xFFFFFF) << 32) | ((long)(cz & 0xFFFFFF) << 8) | (uint)(temperatureByte & 0xFF);
            if (phenologyCache.TryGetValue(key, out byte b)) return b == 255 ? -1 : b;
            long cell = ((long)cx << 32) | (uint)cz;
            if (!monthShapes.TryGetValue(cell, out var shape))
            {
                var geo = geometry; var w = weather;
                if (geo == null || w == null) { known = false; return -1; }
                var g = geo.ToGeo((cx + 0.5) * PhenologyCell, (cz + 0.5) * PhenologyCell);
                shape = null;
                if (!g.InGutter && Math.Abs(g.Lat) <= 90)
                {
                    shape = new float[24];
                    Span<double> temp = stackalloc double[12], precip = stackalloc double[12];
                    w.MonthlyNormals(g.Lat, g.Lon, temp, precip);
                    double mean = 0;
                    for (int m = 0; m < 12; m++)
                    {
                        shape[m] = (float)temp[m];
                        shape[12 + m] = (float)precip[m];
                        mean += temp[m];
                    }
                    mean /= 12;
                    for (int m = 0; m < 12; m++) shape[m] -= (float)mean;
                    if (double.IsNaN(mean)) shape = null;
                }
                monthShapes.Set(cell, shape);
            }
            int result = -1;
            if (shape != null)
            {
                double yearly = temperatureByte / 4.25 - 20;
                Span<double> t = stackalloc double[12];
                Span<double> p = stackalloc double[12];
                for (int m = 0; m < 12; m++) { t[m] = yearly + shape[m]; p[m] = shape[12 + m]; }
                result = GrassPhenology.MapFor(t, p);
            }
            phenologyCache.Set(key, result < 0 ? (byte)255 : (byte)result);
            return result;
        }

        /// <summary>Finds Earth's grass season maps (assets/earth/patches/grass-season-colormaps.json) among the loaded colour
        /// maps; true once they are known (or known to be missing).</summary>
        bool ResolvePhenologyMaps()
        {
            var field = AccessTools.Field(AccessTools.TypeByName("Vintagestory.Common.GameMain"), "ColorMaps");
            if (field?.GetValue(capi.World) is not System.Collections.Generic.IDictionary<string, ColorMap> maps)
            {
                // (CR-A9) by name (not in the API): said rather than silently known as missing (resolved once a world)
                Mod.Logger.Warning("Earth Map: the client's colour maps can't be read (GameMain.ColorMaps {0}); grass keeps vanilla's seasons.",
                    field == null ? "not found" : "is not a dictionary of colour maps");
                return true;
            }
            var idx = new int[GrassPhenology.Codes.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                if (!maps.TryGetValue(GrassPhenology.Codes[i], out var cm) || !cm.LoadIntoBlockTextureAtlas)
                {
                    Mod.Logger.Warning("Earth Map: the grass season map {0} is missing; grass keeps vanilla's seasons.", GrassPhenology.Codes[i]);
                    return true;
                }
                if (cm.Pixels == null) return false; // not loaded yet
                if (cm.RectIndex < 0 || cm.RectIndex >= 40 || cm.ExtraFlags != 0)
                {
                    Mod.Logger.Warning("Earth Map: the grass season map {0} has no place in the shader's 40 colour maps; grass keeps vanilla's seasons.", cm.Code);
                    return true;
                }
                idx[i] = cm.RectIndex + 1;
            }
            phenologyMaps = idx;
            Mod.Logger.Notification("Earth Map: grass follows each place's growing season ({0} season maps).", idx.Length);
            return true;
        }
        bool phenologyResolved;

        /// <summary>The wetness anomaly now, bilinear between the curves at the corners of a 32-block lattice.</summary>
        static double Anomaly(int x, int z)
        {
            int cx = Math.DivRem(x, CornerSpacing, out int rx), cz = Math.DivRem(z, CornerSpacing, out int rz);
            if (rx < 0) { cx--; rx += CornerSpacing; }
            if (rz < 0) { cz--; rz += CornerSpacing; }
            double tx = rx / (double)CornerSpacing, tz = rz / (double)CornerSpacing, t = yearRel;
            // a mesh thread draws a chunk's blocks together, all between the same four corners: its last cell's curves first
            var last = lastCorners ??= new LastCorners();
            int gen = generation;
            if (last.Generation != gen || last.Cx != cx || last.Cz != cz)
            {
                var c00 = Curve(cx, cz); var c10 = Curve(cx + 1, cz); var c01 = Curve(cx, cz + 1); var c11 = Curve(cx + 1, cz + 1);
                if (c00 == null || c10 == null || c01 == null || c11 == null)
                    return (1 - tx) * (1 - tz) * At(c00, t) + tx * (1 - tz) * At(c10, t) + (1 - tx) * tz * At(c01, t) + tx * tz * At(c11, t);
                last.C00 = c00; last.C10 = c10; last.C01 = c01; last.C11 = c11;
                last.Generation = gen; last.Cx = cx; last.Cz = cz;
            }
            return (1 - tx) * (1 - tz) * GrassSeason.At(last.C00, t) + tx * (1 - tz) * GrassSeason.At(last.C10, t)
                + (1 - tx) * tz * GrassSeason.At(last.C01, t) + tx * tz * GrassSeason.At(last.C11, t);
        }

        /// <summary>A mesh thread's last lattice cell: the curves at its four corners.</summary>
        sealed class LastCorners
        {
            public int Generation = -1, Cx, Cz;
            public float[] C00, C10, C01, C11;
        }
        [ThreadStatic] static LastCorners lastCorners;

        /// <summary>The curve's value at a time of year; 0 without a curve (no weather yet).</summary>
        static double At(float[] curve, double t) => curve == null ? 0 : GrassSeason.At(curve, t);

        /// <summary>The wetness curve at a lattice corner, null while there is no weather.</summary>
        static float[] Curve(int cx, int cz)
        {
            long key = ((long)cx << 32) | (uint)cz;
            if (!curves.TryGetValue(key, out var curve))
            {
                var geo = geometry; var w = weather;
                if (geo == null || w == null) return null;
                var g = geo.ToGeo(cx * CornerSpacing, cz * CornerSpacing);
                curve = new float[GrassSeason.Steps];
                if (!g.InGutter && Math.Abs(g.Lat) <= 90) GrassSeason.Curve(w, g.Lat, g.Lon, curve);
                curves.Set(key, curve);
            }
            return curve;
        }
    }
}
