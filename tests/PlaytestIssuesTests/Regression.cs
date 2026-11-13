using EarthMap.Core;

static class Regression
{
    public static int Run(string root, string output, EarthData data, TerrainModel model)
    {
        int checks=0,failures=0;
        void Check(bool ok,string label) {checks++;if(!ok){failures++;Console.Error.WriteLine("FAIL "+label);}}
        using var profiles=new StreamWriter(Path.Combine(output,"summit-profiles.csv"));
        profiles.WriteLine("peak,axis,distanceM,beforeM,afterM,beforeY,afterY");
        var geo=model.Geometry;
        foreach(var peak in KnownSummits.Peaks)
        {
            geo.FromGeo(peak.Lat,peak.Lon,out double x,out double z); x=Math.Round(x);z=Math.Round(z);
            foreach(int height in new[]{256,384,512,768}) foreach(long seed in new long[]{0,386217,12345})
            {
                var v=new VerticalTerrainOptions().Build(height,110,.0625,200,20,.43);
                var m=new TerrainModel(data,geo,v,seed){Summits=true,SummitRules=KnownSummits.Version,DetailScale=1};
                var c=m.GetColumn(x,z);
                Check(Math.Abs(c.Elev-peak.Height)<.001,$"{peak.Name} exact height, h{height} seed{seed}");
                Check(c.Top==(int)Math.Floor(v.Y(peak.Height)),$"{peak.Name} mapped top, h{height} seed{seed}");
            }
            foreach(string axis in new[]{"east","north"})
            {
                double metres=axis=="east"?111320*Math.Cos(peak.Lat*Math.PI/180)/geo.BlocksPerDegreeX:111320/geo.BlocksPerDegreeZ;
                double last=0;
                for(int d=-6000;d<=6000;d+=100)
                {
                    double px=x+(axis=="east"?d/metres:0),pz=z+(axis=="north"?d/metres:0);
                    model.SummitRules=1;var old=model.GetColumn(px,pz);
                    model.SummitRules=KnownSummits.Version;var current=model.GetColumn(px,pz);
                    profiles.WriteLine($"{peak.Name},{axis},{d},{old.Elev:F3},{current.Elev:F3},{old.Top},{current.Top}");
                    double correction=current.Elev-old.Elev;
                    if(d>-6000)Check(Math.Abs(correction-last)<250,$"{peak.Name} no abrupt correction at {axis} {d}");
                    last=correction;
                    if(Math.Abs(d)>peak.Radius && peak.Name!="Lhotse" && peak.Name!="Everest")
                        Check(current.Elev==old.Elev,$"{peak.Name} unchanged outside bounded cap");
                }
            }
        }
        model.SummitRules=KnownSummits.Version;
        Check(MountainEcology.PlantingSuitability(35.36,138.73,2500,13,230,false,false)==0,"Fuji no trees at 2500 m on mobile scree");
        Check(MountainEcology.PlantingSuitability(35.36,138.73,2200,15,230,false,false)==1,"Fuji lower forest kept");
        Check(MountainEcology.TreeSuitability(9.9)==0 && MountainEcology.TreeSuitability(15)==1,"climate treeline, independent of elevation");
        Check(MountainEcology.PlantingSuitability(23.3,5.8,1400,25,38,false,false)==.02,"sparse hot desert upland trees");
        Check(MountainEcology.PlantingSuitability(23.3,5.8,1400,25,38,false,true)==.15,"wadis retain sparse trees");
        // #21's American plants: vanilla's cacti are placement-catalogue taxa (ranges step 3; the Americas box is gone).
        // Native range and climate limits, on Earth's own climate at each site's real elevation.
        var placement=new PlacementCatalogue(File.ReadAllText(Path.Combine(root,"assets/earth/config/placement.json")),
            BotanicalRegions.Shared(File.ReadAllText(Path.Combine(root,"assets/earth/config/treeregions.json"))));
        bool Grows(string id,double lat,double lon,double elev)
        {
            var t=placement.Find(id); var c=data.SampleClimate(lat,lon); double lapse=.0065*elev;
            return placement.InRange(t,placement.RegionAt(lat,lon)) && PlacementCatalogue.ClimateAllows(t,c.TMean-lapse,c.TCold-lapse,c.Rain);
        }
        foreach(var (id,yes,no) in new[]{
            ("carnegiea-gigantea",new[]{("Phoenix",33.45,-112.07,340.0),("Tucson",32.22,-110.97,730),("Organ Pipe",31.95,-112.8,500),("Hermosillo",29.07,-110.96,210)},
                new[]{("Prescott",34.54,-112.47,1650.0),("Flagstaff",35.2,-111.65,2100),("Yuma",32.69,-114.63,40),("Hoggar",23.3,5.8,1400),("El Paso",31.76,-106.49,1140)}),
            ("ferocactus-barrel",new[]{("Tucson",32.22,-110.97,730.0),("El Paso",31.76,-106.49,1140),("Chihuahua",28.63,-106.07,1440)},
                new[]{("Houston",29.76,-95.37,15.0),("Flagstaff",35.2,-111.65,2100),("Hoggar",23.3,5.8,1400)}),
            ("cleistocactus-strausii",new[]{("Tarija",-21.53,-64.73,1850.0),("Sucre",-19.04,-65.26,2800),("Cochabamba",-17.39,-66.16,2550)},
                new[]{("Riberalta",-11.0,-66.07,140.0),("Villamontes",-21.26,-63.47,390),("Hoggar",23.3,5.8,1400),("Tucson",32.22,-110.97,730)})})
        {
            foreach(var s in yes) Check(Grows(id,s.Item2,s.Item3,s.Item4),id+" grows at "+s.Item1);
            foreach(var s in no) Check(!Grows(id,s.Item2,s.Item3,s.Item4),id+" not at "+s.Item1);
        }
        Check(MountainEcology.Surface(6.7,2,.15,0,.8,.5)==MountainEcology.Cover.Scree,"Norway cold exposed ground is scree");
        Check(MountainEcology.Surface(7,2,.1,0,.1,.5)==MountainEcology.Cover.Soil,"alpine grass pockets retained");
        Check(MountainEcology.Surface(20,2,.5,0,.8,2)==MountainEcology.Cover.Soil,"warm island slopes keep soil");
        Check(MountainEcology.Surface(20,2,.8,0,.8,2)==MountainEcology.Cover.Rock,"real lowland cliffs retained");
        // surface rules 2: closed alpine turf from the tree line to about 6 C, thinning to the snow line and in dry mountains
        Check(MountainEcology.Surface2(7.9,3,.15,0,.9,.5,235,0)==MountainEcology.Cover.Soil,"v2: wet Bhutan ridge at 4,100 m is turf");
        Check(MountainEcology.Surface2(7.9,3,.5,0,.5,1,235,0)==MountainEcology.Cover.Soil,"v2: steep alpine meadow keeps most of its turf");
        Check(MountainEcology.Surface2(7.9,3,.85,0,.1,2,235,0)==MountainEcology.Cover.Rock,"v2: rock from a rise of 0.8 in the turf belt");
        Check(MountainEcology.Surface2(3.5,3,.7,0,.1,2,180,0)==MountainEcology.Cover.Rock,"v2: rock from 0.65 in the subnival belt");
        Check(MountainEcology.Surface2(3.5,3,.15,0,.5,.5,180,0)==MountainEcology.Cover.Scree,"v2: subnival ground is mostly scree");
        Check(MountainEcology.Surface2(3.5,3,.15,0,.1,.5,180,0)==MountainEcology.Cover.Soil,"v2: subnival plant patches");
        Check(MountainEcology.Surface2(2.5,3,.15,0,.5,.5,180,0)==MountainEcology.Cover.Snow,"v2: the snow line as rules 1");
        Check(MountainEcology.Surface2(6,.5,.15,0,.5,.5,40,0)==MountainEcology.Cover.Scree,"v2: dry alpine steppe (western Tibet) is gravel");
        Check(MountainEcology.Surface2(7,.5,.15,0,.5,.5,100,0)==MountainEcology.Cover.Soil,"v2: wet Tibetan meadow is turf");
        Check(MountainEcology.Surface2(11,2,.6,0,.5,1.2,30,0)==MountainEcology.Cover.Soil,"v2: dry montane forest slopes keep their ground");
        Check(MountainEcology.Surface2(20,2,.8,0,.8,2,100,0)==MountainEcology.Cover.Rock,"v2: lowland cliffs as rules 1");
        Check(MountainEcology.Surface2(6.7,2,.15,0,.8,.5,150,0)==MountainEcology.Cover.Soil,"v2: Norway mid-alpine benches are turf");
        Check(MountainEcology.TurfShare(2,200)==0 && MountainEcology.TurfShare(7,200)>.999 && MountainEcology.TurfShare(7,30)<.25,"v2: turf share by warmth and rain");
        // ice-sheet ground (Jotunheimen, play-test issue 16): block fields and gravel above the low-alpine heath
        Check(MountainEcology.TurfShare(4.5,200,1)==0 && MountainEcology.TurfShare(10.5,200,1)>.999 && MountainEcology.TurfShare(7,200,1)<.45,"v2: ice-sheet ground closes its turf only near the tree line");
        Check(MountainEcology.Surface2(6.9,2,.15,0,.5,.5,200,1)==MountainEcology.Cover.Scree,"v2: Jotunheimen at 1,800 m is mostly block field");
        Check(MountainEcology.Surface2(6.9,2,.15,0,.5,.5,200,0)==MountainEcology.Cover.Soil,"v2: the same warmth outside the ice sheets is turf");
        Check(MountainEcology.Surface2(7,2,.6,0,.1,1,200,1)==MountainEcology.Cover.Rock,"v2: ice-worn slopes above the tree line are bare from a rise of 0.55");
        Check(MountainEcology.Surface2(7,2,.6,0,.1,1,200,0)==MountainEcology.Cover.Soil,"v2: the same slope outside the ice sheets is turf");
        Check(MountainEcology.Surface2(11,2,.6,0,.5,1,200,1)==MountainEcology.Cover.Soil && MountainEcology.Surface2(11,2,.7,0,.9,1,200,0)==MountainEcology.Cover.Soil,
            "v2: steep forest slopes keep their ground (fjord sides, the montane belt)");
        {
            var ice=MountainEcology.ReadIceSheets(File.ReadAllBytes(Path.Combine(root,"assets/earth/config/ground/global-glaciation.egts")));
            foreach(var (name,lat,lon,want) in new[]{("Jotunheimen",61.6,8.5,1.0),("Cairngorms",57.08,-3.67,1.0),("Banff",51.4,-116.2,1.0),
                ("Alps",45.8,6.9,0.0),("Caucasus",43.2,42.5,0.0),("Bhutan",27.08,89.66,0.0),("Colorado Front Range",40.05,-105.6,0.0)})
                Check(Math.Abs(MountainEcology.IceSheetGround(ice,lat,lon,386217)-want)<.01,$"v2: {name} {(want>0?"was":"was not")} under the last ice sheets");
        }
        {
            // the patch value is uniform (a share s of the ground lies below s) and smooth (neighbours differ little)
            int n=0,below2=0,below5=0,below8=0;double step=0;
            for(int zz=0;zz<400;zz+=2)for(int xx=0;xx<400;xx+=2)
            {
                double v=MountainEcology.TurfPatch(xx*7.3+1000,zz*5.1-2000,386217);n++;
                if(v<.2)below2++;if(v<.5)below5++;if(v<.8)below8++;
                step+=Math.Abs(MountainEcology.TurfPatch(xx*7.3+1001,zz*5.1-2000,386217)-v);
            }
            Check(Math.Abs(below2/(double)n-.2)<.03 && Math.Abs(below5/(double)n-.5)<.03 && Math.Abs(below8/(double)n-.8)<.03,
                $"v2: turf patches uniform ({below2/(double)n:F3}, {below5/(double)n:F3}, {below8/(double)n:F3})");
            Check(step/n<.08,$"v2: turf patches smooth (mean step to the next block {step/n:F3})");
        }
        var h=new Harness(root,false);
        foreach(var config in new[]{h.Make("vanilla",Array.Empty<string>(),true),h.Make("full",h.AllProviders,true),h.Make("unregional",Array.Empty<string>(),false)})
        foreach(int rain in new[]{38,110}) foreach(int temp in new[]{10,17,24})
        {
            float[] factors=Enumerable.Repeat(1f,RegionalTreePicker.Groups).ToArray();
            var s=new TreeSite{Lat=23.3,Lon=5.8,Elevation=1400,Temp=temp,Rain=rain,AmbientRain=38,Fert=70,Forest=40,HeightRel=.75f,
                Region=config.Catalog?.Geography.Find(23.3,5.8)??-1,ColdMonth=8,WarmMonth=25,Factors=factors,Wet=rain>70};
            float[] shares=new float[config.TreeRegs.Length];config.Trees.Expected(s,shares);
            Check(shares.Sum()>0,config.Name+" desert retains eligible analogue/native tree");
            Check(!shares.Where((p,i)=>p>0 && config.TreeRegs[i].Generator.Contains("pine")).Any(),config.Name+" no pine in any tier, including wet wadis");
            var random=new TestRandom(9981);
            for(int i=0;i<100;i++)
            {
                int picked=config.Trees.Pick(s,ref random,out _,out _);
                Check(picked<0 || !config.TreeRegs[picked].Generator.Contains("pine"),config.Name+" actual random pick has no pine");
            }
        }
        // Ranges step 4: vanilla structures (ruins, trader camps) and villages only where people lived around 1318
        // (people-1318.json): settled regions and a warmest month >= 0 C at the ground; no latitude lines (was 0016's 70 N / 60 S).
        var peopleRegions=BotanicalRegions.Shared(File.ReadAllText(Path.Combine(root,"assets/earth/config/treeregions.json")));
        var people=new PeopleRange(File.ReadAllText(Path.Combine(root,"assets/earth/config/people-1318.json")),peopleRegions);
        bool Lived(double lat,double lon){model.Geometry.FromGeo(lat,lon,out double x,out double z);return people.Allows(lat,lon,model.WarmMonthAtGround(x,z));}
        foreach(var (n,lat,lon) in new[]{("Hammerfest",70.66,23.68),("Vardø",70.37,31.1),("Utqiagvik",71.29,-156.79),("Resolute",74.7,-94.83),("Qaanaaq",77.47,-69.23),
            ("Nuuk",64.18,-51.72),("Reykjavik",64.13,-21.9),("Ushuaia",-54.8,-68.3),("Lisbon",38.72,-9.14),("Lhasa",29.65,91.1),("Raoul Island",-29.27,-177.92),("Norfolk Island",-29.04,167.95)})
            Check(Lived(lat,lon),"people lived around 1318 at "+n);
        foreach(var (n,lat,lon) in new[]{("Longyearbyen",78.22,15.65),("Esperanza",-63.4,-57.0),("King George Island",-62.2,-58.9),("McMurdo",-77.85,166.67),
            ("Greenland ice 65N",65.0,-42.0),("Greenland Summit",72.58,-38.46),("South Georgia",-54.28,-36.5),("Kerguelen",-49.35,70.2),("Falklands",-51.7,-57.85),
            ("Azores",37.74,-25.67),("St Helena",-15.97,-5.71),("Mauritius",-20.2,57.5),("Galapagos",-0.74,-90.31),("Bermuda",32.3,-64.78),("Everest",27.99,86.93),("Aconcagua",-32.65,-70.01)})
            Check(!Lived(lat,lon),"nobody lived around 1318 at "+n);
        // Every land column south of 60 S on a 0.5 degree grid (Antarctica by region, not by latitude), and the change against the
        // old lines on a 1 degree land grid, by region.
        int south=0,southOk=0; var gained=new Dictionary<string,int>(); var lost=new Dictionary<string,int>();
        for(double lat=-89.75;lat<90;lat+=.5) for(double lon=-179.75;lon<180;lon+=.5)
        {
            model.Geometry.FromGeo(lat,lon,out double x,out double z);
            if(model.GetColumn(x,z).Water!=TerrainModel.WaterNone) continue;
            bool now=people.Allows(lat,lon,model.WarmMonthAtGround(x,z));
            if(lat<-60){south++; if(!now) southOk++; continue;}
            if(Math.Abs((lat*2)%2)!=.5 || Math.Abs((lon*2)%2)!=.5) continue;   // 1 degree grid for the comparison
            bool old=lat<=70;
            if(now==old) continue;
            string region=people.Settled(lat,lon)?"climate":"region";
            string key=(now?"gained ":"lost ")+peopleRegions.Code(peopleRegions.FindLand(lat,lon))+(now?"":" ("+region+")");
            var d=now?gained:lost; d[key]=d.TryGetValue(key,out int c)?c+1:1;
        }
        Console.WriteLine($"people-1318: {south} land samples south of 60 S, {southOk} without structures; against 70 N / 60 S on a 1 degree grid: "
            +$"gained {gained.Values.Sum()} ({string.Join(", ",gained.OrderByDescending(p=>p.Value).Take(10).Select(p=>p.Key.Substring(7)+" "+p.Value))}), "
            +$"lost {lost.Values.Sum()} ({string.Join(", ",lost.OrderByDescending(p=>p.Value).Take(12).Select(p=>p.Key.Substring(5)+" "+p.Value))})");
        Check(south>10000 && southOk==south,"no structures on any land south of 60 S");
        Console.WriteLine($"Playtest regression: {checks} checks, {failures} failures");
        return failures==0?0:1;
    }
}
