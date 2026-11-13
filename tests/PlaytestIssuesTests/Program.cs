using EarthMap.Core;
using System.Globalization;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string output = Path.Combine(root, "tests/out/playtest-issues", args.Length > 1 ? args[1] : "before");
Directory.CreateDirectory(output);
var data = EarthData.LoadFromFolder(root, true);
var iceSheets = MountainEcology.ReadIceSheets(File.ReadAllBytes(Path.Combine(root, "assets/earth/config/ground/global-glaciation.egts")));
data.SetFineTemperature(true); data.SetFineRain(true); data.SetSummits(true); data.SetGlaciers(true); data.SetClimateFix(true);
data.SetIslandOverlay(true); data.SetLakeOverlay(true); data.SetShoreOverlay(true);
var geo = new EarthGeometry { RingStart=51200, RingLength=409600, Wraps=true, EquatorZ=102400, BlocksPerDegreeZ=204800.0/180, SeamLon0=-169 };
var vertical = new VerticalTerrainOptions().Build(512,110,.0625,200,20,.43);
var model = new TerrainModel(data,geo,vertical,386217) { Summits=true, LocalAlpineEcology=true, ClimateRules=3, DetailScale=1, GutterSeabed=3 };
model.Islands=new IslandLayer(data.Islands,geo,4,IslandLayer.RulesVersion);
model.Coasts=new CoastLayer(data.Coasts,geo,4);
model.LakeShores=true;
// "after": the current rules; "v1": the current rules with alpine surface rules 1 (0032), for comparing rules 2
bool after = args.Length > 1 && (args[1] == "after" || args[1] == "v1");
if (after) { model.SummitRules=KnownSummits.Version; model.AlpineSurfaceRules=args[1] == "v1" ? 1 : MountainEcology.SurfaceVersion; }
var sites = new (string Name,double Lat,double Lon,double Height)[] {
    ("Everest",27.9881,86.9250,8849), ("Lhotse",27.9617,86.9330,8516), ("Makalu",27.8897,87.0883,8485),
    ("K2",35.8808,76.5158,8611), ("Mont Blanc",45.8326,6.8652,4808), ("Fuji",35.3606,138.7274,3776),
    ("Kilimanjaro",-3.0674,37.3556,5895), ("Denali",63.0695,-151.0074,6190), ("Aconcagua",-32.6532,-70.0109,6961),
    ("Norway reported",61.6,8.5,0), ("Jostedalsbreen",61.65,7.05,0), ("Alps reported",45.8,6.9,0),
    ("Fuji reported",35.2,138.5,0), ("Hoggar",23.3,5.8,0), ("Santorini",36.4,25.4,0), ("St Helena",-15.97,-5.71,0),
    ("Chamonix valley",45.923,6.869,0), ("Norway valley",61.46,7.14,0), ("Fuji lower forest",35.31,138.72,0),
    ("Bhutan reported",27.0782,89.6604,0), ("Tibet west",32.5,80.1,0), ("Tibet east",34.5,100.2,0), ("Pamir",38.17,73.97,0),
    ("Colorado Front Range",40.05,-105.6,0), ("Altiplano",-16.0,-69.5,0) };
using var csv = new StreamWriter(Path.Combine(output,"sites.csv"));
csv.WriteLine("site,lat,lon,referenceM,idealY,pointM,pointY,maxM,maxY,warmAtPoint,rain,forest,shrub,rockPct,screePct,soilPct,icePct,landColumns");
foreach (var site in sites)
{
    geo.FromGeo(site.Lat,site.Lon,out double x,out double z);
    int cx=(int)Math.Round(x),cz=(int)Math.Round(z);
    var c=model.GetColumn(cx,cz); var cs=data.SampleClimate(site.Lat,site.Lon);
    var pointGeo=geo.ToGeo(cx,cz);var pointClimate=data.SampleClimate(pointGeo.Lat,pointGeo.Lon);
    double max=c.Elev; int maxY=c.Top,rock=0,scree=0,soil=0,ice=0,land=0;
    const int size=225,half=size/2;
    byte[] rgb=new byte[size*size*3];
    for(int j=0;j<size;j++) for(int i=0;i<size;i++)
    {
        int px=cx+i-half,pz=cz+j-half;
        var cc=model.GetColumn(px,pz); if(cc.Elev>max) {max=cc.Elev; maxY=cc.Top;}
        if(cc.Water!=TerrainModel.WaterNone || cc.Top<vertical.SeaLevel)
        {
            rgb[(j*size+i)*3]=50;rgb[(j*size+i)*3+1]=110;rgb[(j*size+i)*3+2]=170;
            continue;
        }
        land++;
        double sx=Math.Abs(model.GetColumn(px+1,pz).Top-model.GetColumn(px-1,pz).Top)/2.0;
        double sz=Math.Abs(model.GetColumn(px,pz+1).Top-model.GetColumn(px,pz-1).Top)/2.0;
        double slope=Math.Max(sx,sz);
        var gg=geo.ToGeo(px,pz); var climate=data.SampleClimate(gg.Lat,gg.Lon);
        // pre.8 uses the containing chunk's centre climate for its surface pass.
        var chunkGeo=geo.ToGeo((px>>5)*32+16,(pz>>5)*32+16);
        var chunkClimate=data.SampleClimate(chunkGeo.Lat,chunkGeo.Lon);
        double warm=chunkClimate.TWarm-.0065*vertical.Elevation(cc.Top);
        double snow=.5+2.5*Geo.SmoothStep((chunkClimate.Rain-110)/90);
        double mx=111320*Math.Cos(gg.Lat*Math.PI/180)/geo.BlocksPerDegreeX, mz=111320/geo.BlocksPerDegreeZ;
        double real=Math.Max(Math.Abs(vertical.Elevation(model.GetColumn(px+1,pz).Top)-vertical.Elevation(model.GetColumn(px-1,pz).Top))/(2*mx),Math.Abs(vertical.Elevation(model.GetColumn(px,pz+1).Top)-vertical.Elevation(model.GetColumn(px,pz-1).Top))/(2*mz));
        if(after)
        {
            warm=climate.TWarm-.0065*vertical.Elevation(cc.Top);
            snow=.5+2.5*Geo.SmoothStep((climate.Rain-110)/90);
            real=MountainEcology.Slope(vertical.Elevation(model.GetColumn(px-2,pz).Top),vertical.Elevation(model.GetColumn(px+2,pz).Top),
                vertical.Elevation(model.GetColumn(px,pz-2).Top),vertical.Elevation(model.GetColumn(px,pz+2).Top),mx,mz,4);
        }
        int type;
        if(data.GlacierCoverage(gg.Lat,gg.Lon)>=.5) {type=3;ice++;}
        else if(after)
        {
            double volcanic=MountainEcology.LooseVolcanicGround(gg.Lat,gg.Lon,vertical.Elevation(cc.Top));
            var cover=model.AlpineSurfaceRules>=2
                ?MountainEcology.Surface2(warm,snow,real,volcanic,MountainEcology.TurfPatch(px,pz,386217),slope,climate.Rain,
                    MountainEcology.IceSheetGround(iceSheets,chunkGeo.Lat,chunkGeo.Lon,386217))
                :MountainEcology.Surface(warm,snow,real,volcanic,GeologyTransitions.Hash(386217,px>>2,0,pz>>2,31),slope);
            type=cover switch { MountainEcology.Cover.Rock=>0, MountainEcology.Cover.Scree=>1, MountainEcology.Cover.Snow=>3, _=>2 };
            if(type==0)rock++;else if(type==1)scree++;else if(type==2)soil++;else ice++;
        }
        else if(warm<snow) {type=slope<1.25?3:0;if(type==0)rock++;else ice++;}
        else if(warm<3)
        {
            bool pocket=GeologyTransitions.Hash(386217,px>>2,0,pz>>2,31)<.1+.1*(warm-snow)/Math.Max(.5,3-snow);
            type=slope>=1?0:pocket?2:1;
            if(type==0)rock++;else if(type==1)scree++;else soil++;
        }
        else if((warm<6&&slope>=.5)||(warm<10&&slope>=1)||(warm>=10&&slope>=1.5&&real>=.6)) {type=0;rock++;}
        else {type=2;soil++;}
        byte[][] colors={new byte[]{120,110,100},new byte[]{165,145,112},new byte[]{75,125,65},new byte[]{225,240,250}};
        double shade=.8+.2*Math.Clamp((sz-sx)/2+.5,0,1);
        for(int k=0;k<3;k++)rgb[(j*size+i)*3+k]=(byte)(colors[type][k]*shade);
    }
    double n=Math.Max(1,land)/100.0;
    double forest=after?model.ForestPotential(cx,cz):Geo.SmoothStep((pointClimate.Rain-60)/80)*Geo.SmoothStep((pointClimate.TWarm-.0065*c.Elev-8)/5);
    double shrub=after?model.ShrubPotential(cx,cz):Geo.SmoothStep((pointClimate.Rain-40)/60)*Geo.SmoothStep((pointClimate.TWarm-.0065*c.Elev-1)/5);
    csv.WriteLine($"{site.Name},{site.Lat},{site.Lon},{site.Height},{(site.Height>0?vertical.Y(site.Height):0):F2},{c.Elev:F1},{c.Top},{max:F1},{maxY},{cs.TWarm-.0065*vertical.Elevation(c.Top):F2},{cs.Rain:F1},{forest:F4},{shrub:F4},{rock/n:F2},{scree/n:F2},{soil/n:F2},{ice/n:F2},{land}");
    Png.WriteRgb(Path.Combine(output,site.Name.Replace(' ','-')+".png"),size,size,rgb);
    Console.WriteLine($"{site.Name}: {c.Elev:F0} m y{c.Top}, nearby max {max:F0} m y{maxY}, warm {cs.TWarm-.0065*vertical.Elevation(c.Top):F1}, rain {cs.Rain:F0}");
}
if(after)TreeSurvey.Write(root,output,data);
return after ? Regression.Run(root,output,data,model) : 0;
