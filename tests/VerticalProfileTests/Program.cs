using EarthMap.Core;
using System.Globalization;
using System.Text.Json;

int failures = 0, assertions = 0;
void Check(bool ok, string message) { assertions++; if (!ok) { failures++; Console.Error.WriteLine("FAIL " + message); } }
var invariant = CultureInfo.InvariantCulture;
foreach (int height in new[] {256,384,512,768})
{
    int sea = (int)(height * .4313725490196078);
    var baseline = VerticalScale.ForWorld(height, sea);
    var legacy = new VerticalTerrainOptions {Budget="legacy",Curve="legacy"}.Build(height,sea,.0625,200,20,.43);
    foreach (double e in new[] {-11000.0,-4000,-200,0,100,500,2000,8849})
        Check(baseline.Y(e)==legacy.Y(e), $"legacy bit-identical h{height} at {e}");
    foreach (string budget in new[] {"legacy","110","128"})
    foreach (string curve in VerticalTerrainOptions.Curves)
    {
        var o = new VerticalTerrainOptions {Budget=budget, Curve=curve};
        int chosenSea = o.SeaLevel(height,sea);
        var v = o.Build(height,chosenSea,.0625,200,20,.43);
        Check(v.IsUsable, "usable " + curve);
        Check(Math.Abs(v.Y(8849)-v.TopY)<1e-9, "Everest reference");
        Check(v.Y(-11000)>=12 && v.TopY<=height-(curve=="legacy"?12:2), "seabed clearance; Everest at most the second-highest block");
        if(budget!="legacy") Check(v.MaxDepth==47, "fixed water depth");
        double previous=double.NegativeInfinity;
        for(int e=-11000;e<=9000;e+=17)
        {
            double y=v.Y(e);
            Check(y>previous,$"monotonic {height}/{budget}/{curve} at {e}");
            Check(Math.Abs(v.Elevation(y)-e)<1e-7,$"inverse {height}/{budget}/{curve} at {e}");
            previous=y;
        }
        if(curve=="linear") Check(Math.Abs((v.Y(4000)-chosenSea)/(v.Y(2000)-chosenSea)-2)<1e-12,"linear height ratio");
        if(curve=="log2000") Check(v.Y(8849)-v.Y(4000) > legacy.Y(8849)-legacy.Y(4000),"major summit interval expands");
        if(curve!="legacy") Check(Math.Abs(v.TopY-(height-2))<1e-9,"Everest at the second-highest block");
        // A 100 m real rise over 100 m horizontal distance must retain a physical gradient of 1 across mappings.
        double gradient=Math.Abs(v.Elevation(v.Y(1100))-v.Elevation(v.Y(1000)))/100;
        Check(Math.Abs(gradient-1)<1e-10,"physical gradient independent of height curve");
    }
}
Check(new VerticalTerrainOptions().CurveMetres(200)==1000 && new VerticalTerrainOptions().SeaLevel(512,220)==110,"defaults: log 1000 m, sea 110");
Check(new VerticalTerrainOptions {Curve="log250"}.CurveMetres(200)==250 && new VerticalTerrainOptions {Curve="legacy"}.CurveMetres(200)==200,"log presets and legacy");
{ var d=new VerticalTerrainOptions().Build(512,110,.0625,200,20,.43); Check(d.TopY==510 && Math.Abs(d.Y(8849)-510)<1e-9 && d.MaxDepth==47,"default 512 layout: sea 110, Everest 510, floor 62"); }
Check(VerticalTerrainOptions.Choice("bogus","legacy","legacy","linear")=="legacy","unknown profile fallback");
Check(new VerticalTerrainOptions {Budget="128"}.SeaLevel(512,220)==128,"512 has 384 blocks above sea");
Check(new VerticalTerrainOptions {Budget="110"}.SeaLevel(384,165)==110,"256-world coastal depth stays 110");

// Real-data comparison: inspect actual sampled peaks separately from the ideal scale's calibration point.
if(args.Length>0)
{
    var data=EarthData.LoadFromFolder(args[0],true);
    data.SetClimateFix(true);
    string output=args.Length>1?args[1]:Path.Combine(args[0],"tests/out/vertical-profiles.csv");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0],"tests/fixtures/mountain-sites.json")));
    if (args.Contains("--source-peaks"))
        SourcePeakSurvey.Write(data, sites.RootElement, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "source-peaks.csv"));
    data.SetIslandOverlay(true);
    using var csv=new StreamWriter(output);
    csv.WriteLine("height,curve,site,sourceMetres,mappedY,aboveSea,sourceWarmMonth,rain,forestPotential");
    foreach(int height in new[]{384,512})
    foreach(string curve in VerticalTerrainOptions.Curves)
    {
        var o=new VerticalTerrainOptions{Budget="128",Curve=curve};
        var v=o.Build(height,128,.0625,200,20,.43);
        var geo=new EarthGeometry{RingStart=51200,RingLength=409600,Wraps=true,Gutter=1024,EquatorZ=102400,BlocksPerDegreeZ=204800.0/180};
        var m=new TerrainModel(data,geo,v,386217){ClimateRules=TerrainModel.RealRainRules,LocalAlpineEcology=true,DetailScale=0};
        if(data.Islands!=null) m.Islands=new IslandLayer(data.Islands,geo,4,IslandLayer.RulesVersion);
        foreach(var entry in sites.RootElement.EnumerateArray())
        {
            string site=entry.GetProperty("name").GetString()!;
            double lat=entry.GetProperty("latitude").GetDouble(),lon=entry.GetProperty("longitude").GetDouble();
            geo.FromGeo(lat,lon,out double x,out double z);
            var c=m.GetColumn(x,z);
            double warm=data.SampleClimate(lat,lon).TWarm-.0065*c.Elev;
            double forest=m.ForestPotential(x,z);
            Check(double.IsFinite(forest)&&forest>=0&&forest<=1,site+" valid forest potential");
            int pixel=m.ClimatePixel(x,z,0,1,1);
            double encoded=(pixel>>16&255)/4.25-20;
            double real=data.SampleClimate(lat,lon).TMean;
            Check(Math.Abs(encoded-Math.Clamp(real,-20,40))<.3,site+" climate byte no altitude compensation");
            double rain=Math.Clamp(Math.Round(m.RiparianRain(x,z,data.SampleClimate(lat,lon).Rain)),0,255);
            Check(Math.Abs((pixel>>8&255)-rain)<=2,site+" climate byte real rain (rules 3)");
            // the pixel for readers that apply vanilla's own adjustments (Wmap) keeps vanilla's rain compensation
            int vanillaPixel=m.ClimatePixelForVanillaLapse(x,z,0,1,1);
            Check(c.Top<=v.SeaLevel+20||(vanillaPixel>>8&255)<=(pixel>>8&255),site+" vanilla-lapse pixel keeps altitude rain compensation");
            csv.WriteLine(string.Join(",",height,curve,site,c.Elev.ToString("F2",invariant),c.Top,c.Top-128,warm.ToString("F2",invariant),data.SampleClimate(lat,lon).Rain.ToString("F2",invariant),forest.ToString("F3",invariant)));
        }
    }
    Console.WriteLine("Real-data comparisons: "+output);
    // decision 0031's data files, read through the game's loaders
    data.SetFineRain(true); data.SetGlaciers(true); data.SetSummits(true);
    Check(data.FineRain && data.HasGlaciers, "rain.bin and glaciers.bin load ("+data.FineRainError+data.GlaciersError+")");
    if (data.FineRain) Check(data.SampleClimate(-15.96,-5.70).Rain>60, "St Helena's Peaks not desert with CHELSA rain");
    if (data.HasGlaciers) Check(data.GlacierCoverage(61.65,7.05)>.8 && data.GlacierCoverage(48.85,2.35)==0 && data.GlacierCoverage(64.4,-16.8)>.9, "glaciers: Jostedalsbreen and Vatnajokull, not Paris");
    if (data.HasSummits)
    {
        double max=0;
        for(double la=27.9;la<=28.05;la+=1/240.0) for(double lo=86.85;lo<=87.0;lo+=1/240.0) max=Math.Max(max,data.SampleTerrain(la,lo).Elev+data.SummitExcess(la,lo));
        Check(max>8150, "30\" summits: Everest above 8150 m with the excess ("+max.ToString("F0",invariant)+")");
        Check(data.SummitExcess(52.5,5.0)==0, "no summit excess over the Netherlands");
    }
    Console.WriteLine($"rain St Helena {data.SampleClimate(-15.96,-5.70).Rain:F0}, Jostedalsbreen glacier {data.GlacierCoverage(61.65,7.05):F2}, summits {(data.HasSummits?"on":data.SummitsError)}");
}
Console.WriteLine($"{assertions} assertions, {failures} failures");
return failures==0?0:1;
