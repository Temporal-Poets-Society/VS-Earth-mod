#if GLO90
using EarthMap;
using EarthMap.Core;
static partial class Digest
{
    // The data as the server loads it: the mod's own files first, then the data store.
    static partial void LoadData(string root, string store, ref EarthData data)
    {
        if (store == null) return;
        data = EarthData.Load(name =>
        {
            string a = Path.Combine(root, "earthdata", name);
            if (File.Exists(a)) return File.OpenRead(a);
            string b = Path.Combine(store, "data", name);
            return File.Exists(b) ? File.OpenRead(b) : null;
        }, true);
    }

    // EarthMapSystem.BuildAndAttachModel and EarthElevationSystem.AttachServer, offline: the dam and flow files into the
    // model's build, then GLO-90's tiles, the lake and river outlines and the flow on the model.
    static partial void BuildAndAttach(EarthData data, EarthParams p, string store, string tiles, ref TerrainModel model)
    {
        if (p.FineElevation < 1 || store == null || tiles == null) return;
        string g = Path.Combine(store, "glo90");
        var dams = p.DamRules >= 1 ? DamRemovalData.Read(File.ReadAllBytes(Path.Combine(g, DamRemovalData.FileNameFor(Math.Min(p.DamRules, DamRemovalData.NewestRules))))) : null;
        var flow = p.FlowRules >= 1 ? RiverFlowData.Read(File.ReadAllBytes(Path.Combine(g, RiverFlowData.FileName))) : null;
        model = p.BuildModel(data, dams, null, flow);
        Console.WriteLine("dam course levels " + p.DamCourseLevels + " (" + (p.DamCourseError ?? "ok") + "), flow reroutes " + p.FlowReroutes);
        var fine = new FineElevation(tiles, File.ReadAllBytes(Path.Combine(tiles, "glo90-tiles.bin")));
        fine.SetFallback(Array.Empty<int>());
        model.FineRules = Math.Min(p.FineElevation, FineElevation.Version);
        model.Fine = fine;
        if (dams != null && p.DamCourseError == null) fine.Dams = dams;
        if (p.LakeOutlines >= 1)
            model.LakeOutlines = new LakeOutlineLayer(LakeOutlineData.Read(File.ReadAllBytes(Path.Combine(g, LakeOutlineData.FileNameFor(Math.Clamp(p.LakeOutlines, 1, LakeOutlineData.FileVersion))))), model.Geometry);
        if (p.RiverOutlines >= 1)
        {
            int v = RiverOutlineData.Usable(p.RiverOutlines);
#if RC8
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(g, RiverOutlineData.FileName(v)))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt);
#else
            model.RiverOutlines = RiverOutlineData.Layer(RiverOutlineData.Read(File.ReadAllBytes(Path.Combine(g, RiverOutlineData.FileName(v)))), model.Geometry, p.DropsNarrowRiverPieces, model.CourseDrawsAt,
                p.ChannelRules >= TerrainModel.FlowChannelRules ? model.FlowCarriesAt : null);
#endif
        }
        if (flow != null) { model.Flow = flow; model.FlowRules = Math.Min(p.FlowRules, RiverFlowData.FileVersion); }
    }
}
#endif
