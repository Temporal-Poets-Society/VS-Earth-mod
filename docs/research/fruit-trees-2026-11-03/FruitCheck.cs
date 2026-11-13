using System; using System.IO; using System.IO.Compression; using System.Linq; using System.Reflection; using System.Collections.Generic;
var asm = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "EarthMap.dll"));
var T = asm.GetType("EarthMap.Core.EarthData");
var zips = args.Select(ZipFile.OpenRead).ToArray();
Stream Open(string n) { foreach (var z in zips) { var e = z.GetEntry("earthdata/" + n); if (e == null) continue; var ms = new MemoryStream(); using (var s = e.Open()) s.CopyTo(ms); ms.Position = 0; return ms; } return null; }
object d = T.GetMethod("Load").Invoke(null, new object[]{ (Func<string,Stream>)Open, true });
T.GetMethod("SetClimateFix").Invoke(d, new object[]{true}); T.GetMethod("SetFineRain").Invoke(d, new object[]{true});
var SM = T.GetMethod("SampleClimate", new[]{typeof(double),typeof(double)});
var TM = T.GetMethod("SampleTerrain", new[]{typeof(double),typeof(double)});
double G(object o, string n) => (double)o.GetType().GetField(n).GetValue(o);
var conds = new (string t, double a, double b, double c, double e)[] {
 ("pinkapple",-5,18,.5,1),("redapple",-9,18,.5,.9),("yellowapple",0,22,.6,1),("cherry",0,24,.6,1),("mango",28,50,.6,1),
 ("olive",22,50,.3,.5),("orange",25,30,.45,1),("peach",10,17,.55,1),("pear",-10,19,.5,1),("breadfruit",28,50,.6,1),("lychee",22,50,.6,1),("pomegranate",22,50,.3,.7)};
bool Ok((string t, double a, double b, double c, double e) c, double t, double r) => t >= c.a && t <= c.b && r >= c.c && r <= c.e;
// land cells: surface T = sea-level TMean - 6.5 K/km
IEnumerable<(double lat,double lon,double t,double r)> Land(double la0,double la1,double lo0,double lo1,double step){
 for (double la = la0; la <= la1; la += step) for (double lo = lo0; lo <= lo1; lo += step) {
  var ts = TM.Invoke(d, new object[]{la,lo}); if (G(ts,"Ocean") > .5) continue; double el = Math.Max(0, G(ts,"Elev"));
  var c = SM.Invoke(d, new object[]{la,lo}); yield return (la, lo, G(c,"TMean") - .0065*el, G(c,"Rain")/255); } }
void Report(string name, List<(double lat,double lon,double t,double r)> cells){
 Console.WriteLine($"{name}: {cells.Count} land cells; any fruit type {cells.Count(x=>conds.Any(c=>Ok(c,x.t,x.r)))}");
 foreach (var c in conds) { int n = cells.Count(x=>Ok(c,x.t,x.r)); if (n>0) Console.Write($" {c.t}={n}"); } Console.WriteLine(); }
Report("Greece (34.8-41.8N, 19.3-28.3E)", Land(34.8,41.8,19.3,28.3,.05).ToList());
Report("Mediterranean basin (30-46N, 10W-40E)", Land(30,46,-10,40,.1).ToList());
var g = Land(-50,55,-180,179.75,.25).ToList();
Report("World 50S-55N", g);
var ol = g.Where(x=>Ok(conds[5],x.t,x.r)).ToList();
Console.WriteLine("olive-eligible land by 10x20 deg box:");
foreach (var k in ol.GroupBy(x=>$"{Math.Floor(x.lat/10)*10}..{Math.Floor(x.lat/10)*10+10}N {Math.Floor(x.lon/20)*20}..{Math.Floor(x.lon/20)*20+20}E").OrderByDescending(k=>k.Count()).Take(10)) Console.WriteLine($"  {k.Key}: {k.Count()}");
