using EarthMap.Core;

// Uses only pre.8 public APIs, so tools/run-playtest-issues.ps1 also compiles it against the untouched baseline core.
static class TreeSurvey
{
    public static void Write(string root, string output, EarthData data)
    {
        WriteGlobal(root,output,data);
        var h=new Harness(root,false);
        using var csv=new StreamWriter(Path.Combine(output,"tree-picks.csv"));
        csv.WriteLine("providers,lat,lon,altitude,rain,wet,temperature,warmMonth,generator,expectedShare");
        foreach(var config in new[] { h.Make("vanilla",Array.Empty<string>(),true),h.Make("all-supported",h.AllProviders,true),h.Make("unregional",Array.Empty<string>(),false) })
        foreach(double elev in new double[] {800,1400,2200})
        foreach(bool wet in new[]{false,true})
        {
            const double lat=23.3315,lon=5.8356;
            var cs=data.SampleClimate(lat,lon);
            float[] factors=new float[RegionalTreePicker.Groups];
            RegionalTreePicker.ForestTypeFactors(cs.TMean,cs.TCold,cs.TWarm,cs.DrySummer,cs.Rain,elev,factors,true);
            var site=new TreeSite {Lat=lat,Lon=lon,Elevation=elev,Temp=(int)Math.Round(cs.TMean-.0065*elev),Rain=wet?110:(int)cs.Rain,
                Fert=70,Forest=40,HeightRel=(float)(VerticalScale.ForWorld(384,165).Y(elev)/384),Factors=factors,
                Region=config.Catalog?.Geography.Find(lat,lon)??-1,ColdMonth=(float)(cs.TCold-.0065*elev),WarmMonth=(float)(cs.TWarm-.0065*elev),DrySummer=(float)cs.DrySummer,Wet=wet};
            // AmbientRain was added with this fix. The original core has no such field.
            object boxed=site;
            typeof(TreeSite).GetField("AmbientRain")?.SetValue(boxed,(float?)cs.Rain);
            site=(TreeSite)boxed;
            float[] shares=new float[config.TreeRegs.Length];
            config.Trees.Expected(site,shares);
            for(int i=0;i<shares.Length;i++) if(shares[i]>0)
                csv.WriteLine($"{config.Name},{lat},{lon},{elev},{site.Rain},{wet},{site.Temp},{site.WarmMonth:F2},{config.TreeRegs[i].Generator},{shares[i]:F6}");
        }
    }
    static void WriteGlobal(string root,string output,EarthData data)
    {
        var h=new Harness(root,false);var ranges=h.Make("native",Array.Empty<string>(),true).Catalog;
        var native=typeof(RegionalTreeCatalog).GetMethod("NativeAt");
        var hot=typeof(TreeSite).Assembly.GetType("EarthMap.Core.MountainEcology")?.GetMethod("HotDesert");
        using var csv=new StreamWriter(Path.Combine(output,"global-tree-picks.csv"));
        csv.WriteLine("site,providers,rain,warm,drySummer,totalShare,pineShare,acaciaShare");
        foreach(var config in new[]{h.Make("vanilla",Array.Empty<string>(),true),h.Make("all-supported",h.AllProviders,true),h.Make("unregional",Array.Empty<string>(),false)})
        foreach(var p in new[]{("Hoggar",23.3,5.8),("Arabia",23.3,45.0),("Namib",-23.5,15.5),("Thar",26.9,70.9),("Australia",-25.5,134.0),("Atacama",-23.5,-68.5),("Santorini",36.4,25.4)})
        {
            var cs=data.SampleClimate(p.Item2,p.Item3);double elev=data.SampleTerrain(p.Item2,p.Item3).Elev;
            var site=new TreeSite{Lat=p.Item2,Lon=p.Item3,Elevation=elev,Temp=24,Rain=120,Fert=70,Forest=40,HeightRel=.75f,Wet=true,
                Factors=Enumerable.Repeat(1f,RegionalTreePicker.Groups).ToArray(),Region=config.Catalog?.Geography.Find(p.Item2,p.Item3)??-1,
                ColdMonth=(float)(cs.TCold-.0065*elev),WarmMonth=(float)(cs.TWarm-.0065*elev),DrySummer=(float)cs.DrySummer};
            object boxed=site;typeof(TreeSite).GetField("AmbientRain")?.SetValue(boxed,(float?)cs.Rain);site=(TreeSite)boxed;
            float[] shares=new float[config.TreeRegs.Length];config.Trees.Expected(site,shares);
            bool desert=hot!=null && (bool)hot.Invoke(null,new object[]{cs.Rain,(double)site.WarmMonth,cs.DrySummer});
            if(config.Catalog==null && desert && native!=null)
                for(int i=0;i<shares.Length;i++)if(!(bool)native.Invoke(ranges,new object[]{config.TreeRegs[i].Generator,site}))shares[i]=0;
            csv.WriteLine($"{p.Item1},{config.Name},{cs.Rain:F2},{site.WarmMonth:F2},{cs.DrySummer:F3},{shares.Sum():F4},{shares.Where((v,i)=>config.TreeRegs[i].Generator.Contains("pine")).Sum():F4},{shares.Where((v,i)=>config.TreeRegs[i].Generator.Contains("acacia")).Sum():F4}");
        }
    }
}
