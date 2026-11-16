using EarthMap;
using EarthMap.Core;
using ProtoBuf;
using System.Text.Json;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;

int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
string root=Path.GetFullPath(args.Length>0?args[0]:".");
var cfg=new EarthMapConfig();
// (EA-0089 C) a new world's params from the tab and earth.json, as WorldParamsBuilder makes them
EarthParams Freeze(ITreeAttribute wc,EarthMapConfig c,EarthData d=null,int height=384)=>ParamsBuilderChecks.NewParams(wc,c,d,height);
EarthParams RoundTrip(EarthParams p){using var s=new MemoryStream();Serializer.Serialize(s,p);s.Position=0;return Serializer.Deserialize<EarthParams>(s);}
foreach(string style in new[]{"surviveandbuild","exploration","wildernesssurvival","homosapiens","creativebuilding","earthmap","",null})
    Check(!EarthWorldOptions.CreatesEarth(true,"standard",style,true),"Foreign/missing style must stay vanilla: "+style);
Check(EarthWorldOptions.CreatesEarth(true,"standard","earth",true),"Earth style creates Earth");
Check(!EarthWorldOptions.CreatesEarth(false,"standard","earth",true),"Adding mod to an existing world never creates Earth");
Check(!EarthWorldOptions.CreatesEarth(true,"superflat","earth",true),"Superflat remains vanilla");
Check(!EarthWorldOptions.CreatesEarth(true,"standard","earth",false),"Config kill switch remains");
var wc=new TreeAttribute();
wc.SetString("earthRocks","vanilla");wc.SetString("earthOreDeposits","vanilla");wc.SetString("earthAnimals","vanilla");wc.SetBool("earthIncludeExtinctAnimals",false);
wc.SetString("earthWeather","vanilla");wc.SetString("earthRainFrequency","0.5");wc.SetString("earthStormFrequency","0");
var p=Freeze(wc,cfg);p=RoundTrip(p);
Check(!p.RegionalOres && !p.RegionalGeology,"Vanilla ores also disable regional rock and resource alterations");
Check(p.GroundColumns==0,"Vanilla rocks build no ground columns");
Check(p.CraftingRules==0,"Vanilla rocks and ores keep vanilla's recipes (decision 0077)");
// Rocks and ground: ground columns on top of regional geology; the ore choice is its own (the author, 7 November)
foreach(var (choice,columns) in new[]{("groundcolumns",true),("regional",false)})
foreach(var maps in new[]{true,false})
{
    var g=new TreeAttribute();g.SetString("earthRocks",choice);g.SetString("earthOreDeposits",maps?"maps":"vanilla");
    var pg=Freeze(g,new EarthMapConfig {GroundColumnsPrototype=!columns,RegionalOres=!maps});pg=RoundTrip(pg);
    Check(pg.RegionalGeology && pg.RegionalOres==maps && (pg.OreRulesVersion>0)==maps,choice+" keeps regional geology; Ores alone decides the ore maps");
    Check(pg.GroundColumns==(columns?EarthMap.Core.GroundColumns.RulesVersion:0),choice+": the tab, not the config flag, decides ground columns");
    Check(pg.DepositSet==EarthMap.Core.EarthDepositSet.Version,choice+": a new world saves Earth's deposit set (D2)");
    Check(pg.CraftingRules==(columns||maps?EarthCrafting.RulesVersion:0),choice+(maps?" with the ore maps":" with vanilla ores")+": Earth's crafting rules where Earth decides bauxite (decision 0077)");
}{
    var g=new TreeAttribute();g.SetString("earthRocks","vanilla");g.SetString("earthOreDeposits","maps");
    var pg=Freeze(g,new EarthMapConfig {GroundColumnsPrototype=true});pg=RoundTrip(pg);
    Check(!pg.RegionalGeology && pg.GroundColumns==0 && pg.RegionalOres,"Vanilla rocks with the Earth ore maps");
    Check(pg.CraftingRules==EarthCrafting.RulesVersion,"Vanilla rocks with the Earth ore maps get Earth's crafting rules");
}
// The earlier single "Rocks and ores" key (earthOres) still means the same where the two new keys are missing
foreach(var (legacyChoice,geology,columns,maps) in new[]{("vanilla",false,false,false),("regional",true,false,true),("groundcolumns",true,true,true)})
{
    var g=new TreeAttribute();g.SetString("earthOres",legacyChoice);
    var pg=Freeze(g,new EarthMapConfig {RegionalOres=!maps,RegionalGeology=!geology,GroundColumnsPrototype=!columns});
    Check(pg.RegionalGeology==geology && (pg.GroundColumns>0)==columns && pg.RegionalOres==maps,"Old Rocks and ores key "+legacyChoice+" keeps its meaning");
}
{
    var g=new TreeAttribute();g.SetString("earthOres","regional");g.SetString("earthRocks","vanilla");g.SetString("earthOreDeposits","vanilla");
    var pg=Freeze(g,new EarthMapConfig());
    Check(!pg.RegionalGeology && !pg.RegionalOres,"The new keys win over the old one");
}
// Ground depth: saved with ground columns only; unknown or missing values take the default, log4000 (the author, 7 November)
foreach (var (value,expect) in new[]{("terrain","terrain"),("linear2000","linear2000"),("bad","log4000"),(null,"log4000")})
{
    var g=new TreeAttribute();g.SetString("earthRocks","groundcolumns");if(value!=null)g.SetString("earthGroundDepth",value);
    var pg=Freeze(g,new EarthMapConfig());
    Check(RoundTrip(pg).GroundDepth==expect,"Ground depth "+(value??"missing")+" saves as "+expect);
}
{
    var g=new TreeAttribute();g.SetString("earthRocks","regional");g.SetString("earthGroundDepth","log2000");
    var pg=Freeze(g,new EarthMapConfig());
    Check(pg.GroundDepth==null,"No ground depth without ground columns");
}
var noKey=Freeze(new TreeAttribute(),new EarthMapConfig {GroundColumnsPrototype=true});
Check(noKey.GroundColumns==EarthMap.Core.GroundColumns.RulesVersion,"Without the tab key the config flag decides");
Check(!p.RegionalFauna && p.VanillaFaunaRanges==0 && !p.IncludeExtinctAnimals,"Vanilla animals disable both range systems");
Check(!p.Weather && p.RainFrequency==.5 && p.StormFrequency==0,"Weather choices serialize, including zero");
Check(!p.NoRiverRapids,"Rapids stay on when the tab has no rapids key");
var calm=new TreeAttribute();calm.SetString("earthRiverRapids","off");var pc=Freeze(calm,cfg);
Check(RoundTrip(pc).NoRiverRapids,"River rapids off serializes");
Check(!RoundTrip(new EarthParams {Enabled=true}).NoRiverRapids,"Older saves keep rapids reaches");
Check(noKey.SpringRules==EarthParams.SpringRulesCaves,"Mountain springs default to cave springs only");
// "off" is gone (the author, 8 November: "Instead of 'off' - better placement"): a stray value gets the default
foreach(var (value,expect) in new[]{("caves",EarthParams.SpringRulesCaves),("streams",EarthParams.SpringRulesStreams),("Streams ",EarthParams.SpringRulesStreams),("off",EarthParams.SpringRulesCaves),("vanilla",0),("Vanilla ",0)})
{
    var s=new TreeAttribute();s.SetString("earthMountainSprings",value);var ps=Freeze(s,cfg);
    Check(RoundTrip(ps).SpringRules==expect,"Mountain springs "+value.Trim()+" saves as "+expect);
}
Check(RoundTrip(new EarthParams {Enabled=true}).SpringRules==0,"Older saves keep vanilla's springs and mountain-side streams");
// surface ponds (the author, 8 November: "in Earth setting UI make default off")
Check(noKey.PondRules==EarthParams.PondRulesNone,"Surface ponds default to off");
foreach(var (value,expect) in new[]{("off",EarthParams.PondRulesNone),("wetlands",EarthParams.PondRulesWetlands),("Wetlands ",EarthParams.PondRulesWetlands),("vanilla",0),("stray",EarthParams.PondRulesNone)})
{
    var s=new TreeAttribute();s.SetString("earthSurfacePonds",value);var ps=Freeze(s,cfg);
    Check(RoundTrip(ps).PondRules==expect,"Surface ponds "+value.Trim()+" saves as "+expect);
}
Check(RoundTrip(new EarthParams {Enabled=true}).PondRules==0,"Older saves keep vanilla's surface ponds");
Check(EarthParams.PondRulesWetlands==WetlandGrid.PondRulesVersion,"The wetlands choice is pond rules 1 (0062's)");
// springs rules 2's pure rules (Core.StreamSprings)
Check(StreamSprings.SnapRadius(10)==45 && StreamSprings.SnapRadius(100)==4 && StreamSprings.SnapRadius(1000)==1 && StreamSprings.SnapRadius(1)==StreamSprings.MaxSnapBlocks,
    "Stream heads move up to 450 m at the world's scale, at least a block");
Check(StreamSprings.SeepFace(100,100,new[]{99,100,100,100},new[]{true,true,true,true},out int seepY,out int seepDir) && seepY==100 && seepDir==0,
    "A seep comes out where the table stands above a lower neighbour's ground");
Check(!StreamSprings.SeepFace(100,98,new[]{98,100,100,100},new[]{true,true,true,true},out _,out _) && !StreamSprings.SeepFace(100,100,new[]{99,100,100,100},new[]{false,true,true,true},out _,out _),
    "No seep where the table stays in the ground, or towards water");
Check(StreamSprings.SeepTries(0,12345)==0 && StreamSprings.SeepTries(1,12345)==(int)StreamSprings.SeepTriesFull && StreamSprings.SeepTries(.3,777)==StreamSprings.SeepTries(.3,777),
    "Seep tries: none without a water surplus, all at a full one, the same for the same chunk");
if(File.Exists(Path.Combine(root,"earthdata",StreamHeads.FileName)))
{
    StreamHeads sh;using(var f=File.OpenRead(Path.Combine(root,"earthdata",StreamHeads.FileName)))sh=StreamHeads.Read(f);
    var near=new List<StreamHeads.Head>();
    sh.Find(46.45,46.80,7.55,8.25,near);
    Check(sh.Count>2_000_000 && near.Count>50 && near.All(h=>h.Lat>=46.45 && h.Lat<=46.80 && h.Lon>=7.55 && h.Lon<=8.25 && sh.Discharge(h.Class)>=0.032),
        "Stream heads: the Bernese Oberland has its perennial heads, all inside the box");
    var dry=new List<StreamHeads.Head>();sh.Find(20,28,0,15,dry);
    Check(dry.Count==0,"Stream heads: none in the central Sahara (intermittent heads are left out)");
    var wrapped=new List<StreamHeads.Head>();sh.Find(46.45,46.80,7.55+360,8.25+360,wrapped);
    Check(wrapped.Count==near.Count && wrapped.All(h=>h.Lon>=367.55),"Stream heads: a box past 180 E finds the same heads at its own longitudes");
}
cfg.Weather=true;cfg.RainFrequency=3;cfg.StormFrequency=5;cfg.IncludeExtinctAnimals=true;
EarthWorldOptions.Runtime(cfg,p);
Check(!p.Weather && p.RainFrequency==.5 && p.StormFrequency==0 && !p.IncludeExtinctAnimals,"Restart config cannot change frozen choices");
Check(p.Enabled,"Saved enabled EarthParams stay enabled regardless of current style/config");
var old=RoundTrip(new EarthParams {Enabled=true,DetailScale=.5,LocalAlpineEcology=false,PhysicalRockExposure=true,
    TopY=480,LandH0=200,SeaD0=20,SeaLevel=220,MapHeight=512,MaxDepth=220});
EarthWorldOptions.Runtime(cfg,old);
Check(old.CreationOptionsVersion==0 && old.Weather && old.RainFrequency==3,"Missing marker keeps older restart weather behaviour");
Check(old.DetailScale==.5 && !old.LocalAlpineEcology && old.PhysicalRockExposure && old.LandH0==200,"Hidden legacy/test fields survive protobuf unchanged");
var hidden=new TreeAttribute();hidden.SetString("earthVerticalBudget","legacy");hidden.SetString("earthHeightCurve","legacy");
hidden.SetString("earthTerrainDetail","half");hidden.SetString("earthAlpineEcology","legacy");hidden.SetString("earthRockExposure","physical");
var terrain=EarthWorldOptions.Terrain(hidden);
Check(terrain.Budget=="legacy" && terrain.Curve=="legacy" && terrain.DetailMultiplier==.5 && !terrain.LocalEcology && terrain.PhysicalRock,"Hidden old world options still parse");
var bad=new TreeAttribute();bad.SetString("earthRainFrequency","NaN");bad.SetString("earthStormFrequency","-1");bad.SetString("earthOres","bad");bad.SetString("earthRocks","bad");bad.SetString("earthOreDeposits","bad");
p=Freeze(bad,cfg);
Check(p.RainFrequency==3 && p.StormFrequency==5 && p.RegionalOres,"Invalid options fall back safely");
p=Freeze(null,new EarthMapConfig());
Check(p.RegionalFauna && p.RegionalOres && p.Weather && p.RainFrequency==1,"No options use config fallbacks");
// Decision 0047: plants and animals as around 1318 by default; vanilla means placement by climate alone.
Check(!new EarthMapConfig().IncludeExtinctAnimals,"Animals extinct before 1318 are opt-in");
var nature=new TreeAttribute();nature.SetString("earthNature","1318");nature.SetString("earthAnimals","vanilla");
var n1318=Freeze(nature,new EarthMapConfig());n1318=RoundTrip(n1318);
Check(n1318.PlacementRules==EarthMap.Core.PlacementCatalogue.RulesVersion && n1318.RegionalFauna && n1318.RegionalTrees && !n1318.IncludeExtinctAnimals,
    "1318 saves placement rules and regional fauna; the old Animals key no longer decides");
Check(n1318.FaunaRangeProfile=="1318","Decision 0054: 1318 worlds save the 1318 animal ranges");
Check(!n1318.ChickensByRange && !new EarthParams().ChickensByRange,"Chickens everywhere is on by default and in older saves");
var limited=new TreeAttribute();limited.SetString("earthNature","1318");limited.SetBool(EarthWorldOptions.ChickensEverywhereKey,false);
var nLimited=Freeze(limited,new EarthMapConfig());nLimited=RoundTrip(nLimited);
Check(nLimited.ChickensByRange,"Chickens everywhere turned off is saved");
var cfgCurrent=new EarthMapConfig {FaunaRangeProfile="current"};
var nCurrent=Freeze(nature,cfgCurrent);
Check(nCurrent.FaunaRangeProfile=="current","Decision 0054: earth.json's \"current\" profile still wins in a 1318 world");
nature.SetString("earthNature","vanilla");nature.SetString("earthAnimals","regional");
var nVanilla=Freeze(nature,new EarthMapConfig());
Check(nVanilla.PlacementRules==0 && !nVanilla.RegionalFauna && nVanilla.VanillaFaunaRanges==0 && !nVanilla.RegionalTrees && nVanilla.TreeRulesVersion==0,
    "Vanilla nature: no catalogue, fauna ranges or regional trees");
var oldPreset=new TreeAttribute();oldPreset.SetString("earthAnimals","vanilla");
var nOld=Freeze(oldPreset,new EarthMapConfig());
Check(nOld.PlacementRules==0 && !nOld.RegionalFauna && nOld.RegionalTrees,"Without earthNature the old Animals key decides and trees stay");
Check(RoundTrip(new EarthParams {Enabled=true}).PlacementRules==0,"Older saves (no field) keep vanilla placement");
// EA-0061, decision 0075: Biomes (earth / vanilla), independent of Nature
// Decision 0059 (the author, 15 November 2026): Earth writes Hydrate or Diedrate's aquifer data only with its own rock and
// ground and its own biomes; with vanilla rock and ground or vanilla biomes, HoD's own defaults
foreach(var (rocks,biomes,writes) in new[]{("groundcolumns","earth",true),("regional","earth",true),("vanilla","earth",false),("groundcolumns","vanilla",false),("regional","vanilla",false)})
{
    var g=new TreeAttribute();g.SetString("earthRocks",rocks);g.SetString("earthBiomes",biomes);
    var pg=RoundTrip(Freeze(g,new EarthMapConfig()));
    Check(pg.WaterTableRules>=1 && pg.WritesHodAquifers()==writes,$"rocks {rocks}, biomes {biomes}: Earth {(writes?"writes":"leaves")} Hydrate or Diedrate's aquifer data (water table rules {pg.WaterTableRules})");
}
var oldWorld=new EarthParams{WaterTableRules=0,RegionalGeology=true};
Check(!oldWorld.WritesHodAquifers(),"a world made before the water table writes no aquifer data");
EarthParams Biomes(string nature,string biomes){var t=new TreeAttribute();if(nature!=null)t.SetString("earthNature",nature);if(biomes!=null)t.SetString("earthBiomes",biomes);
    return RoundTrip(Freeze(t,new EarthMapConfig()));}
bool EarthBiomes(EarthParams b)=>b.VanillaBiomes==0 && b.SiteRules==SiteNiche.RulesVersion && b.ZoneRules==ClimateZones.RulesVersion && b.FireRules==FireRegime.RulesVersion
    && b.WetlandRules==WetlandGrid.RulesVersion && b.GroundCoverRules==GroundCover.RulesVersion && b.MoistureRules==GroundMoisture.RulesVersion && b.GrassWinterRules==GrassWinter.RulesVersion
    && b.AlpineSurfaceRules==MountainEcology.SurfaceVersion;
bool VanillaBiomes(EarthParams b)=>b.VanillaBiomes==EarthParams.VanillaBiomesVersion && b.SiteRules==0 && b.ZoneRules==0 && b.FireRules==0 && b.WetlandRules==0 && b.GroundCoverRules==0 && b.MoistureRules==0 && b.GrassWinterRules==0 && b.AlpineSurfaceRules==0;
bool KeepsTheRest(EarthParams b,EarthParams e)=>b.SoilRules==SoilFertility.RulesVersion && b.AridityRules==e.AridityRules && b.PondRules==e.PondRules && b.ShoreZoneRules==e.ShoreZoneRules && b.ShoreRules==e.ShoreRules && b.SpringRules==e.SpringRules && b.LocalAlpineEcology;
Check(EarthBiomes(Biomes(null,null)) && EarthBiomes(Biomes("1318","earth")) && EarthBiomes(Biomes("1318","bad")),"Earth's biomes by default, and for an unknown value");
foreach(string n in new[]{"1318","vanilla"})
{
    var bv=Biomes(n,"vanilla");
    Check(VanillaBiomes(bv) && KeepsTheRest(bv,Biomes(n,"earth")),"Vanilla biomes zero the biome rules only, with Nature "+n);
    Check((bv.PlacementRules>0)==(n=="1318") && bv.RegionalFauna==(n=="1318"),"Nature still decides placement and animals with vanilla biomes: "+n);
    var be=Biomes(n,"earth");
    Check(EarthBiomes(be) && (be.PlacementRules>0)==(n=="1318"),"Earth's biomes with Nature "+n);
}
Check(RoundTrip(new EarthParams {Enabled=true}).VanillaBiomes==0,"Older saves (no field) keep Earth's biomes");
Check(RoundTrip(new EarthParams {Enabled=true}).SiteRules==0 && RoundTrip(new EarthParams {Enabled=true,SiteRules=1}).SiteRules==1,"Site rules (EA-0054): older saves keep 0, new worlds keep theirs");
// decision 0081 (review of 14 Nov): a packed config asset reads again after the server drops unpatched assets' bytes
{
    byte[] json=System.Text.Encoding.UTF8.GetBytes("{\"a\":1}");
    using var ms=new MemoryStream();
    using(var br=new System.IO.Compression.BrotliStream(ms,System.IO.Compression.CompressionLevel.Optimal,true))br.Write(json,0,json.Length);
    var asset=new EarthMap.PackedAssets.Unpacked(new Vintagestory.API.Common.AssetLocation("earth","config/x.json"),ms.ToArray(),null);
    string first=asset.ToText();
    asset.Data=null;   // AssetManager.UnloadUnpatchedAssets
    Check(first=="{\"a\":1}" && asset.IsLoaded() && asset.ToText()==first,"A packed asset unpacks again after its bytes are dropped (0081)");
}
// Tall worlds: the Earth tab's height slider (the menu stores an int) and the rock floor.
int Height(Action<TreeAttribute> set,out string badHeight){var t=new TreeAttribute();set(t);return EarthWorldOptions.Height(t,out badHeight);}
Check(Height(t=>{},out var hb)==0 && hb==null,"No height key keeps the World generation tab's height");
Check(Height(t=>t.SetInt("earthWorldHeight",0),out hb)==0 && hb==null,"A config's 0 keeps the World generation tab's height");
Check(Height(t=>t.SetInt("earthWorldHeight",1024),out hb)==1024 && hb==null,"Slider int 1024");
Check(Height(t=>t.SetString("earthWorldHeight","768"),out hb)==768 && hb==null,"Server config string 768");
foreach(string badValue in new[]{"128","1000","2048","tall"})
    Check(Height(t=>t.SetString("earthWorldHeight",badValue),out hb)==0 && hb==badValue,"Bad height keeps the tab's and is reported: "+badValue);
int Floor(string value,int height){var t=new TreeAttribute();if(value!=null)t.SetString("earthRockFloor",value);
    return RoundTrip(Freeze(t,new EarthMapConfig(),height:height)).RockFloorDepth;}
Check(Floor(null,1024)==160 && Floor("auto",1024)==160 && Floor("auto",512)==0 && Floor("auto",256)==0,"Automatic rock floor: 160 only above 512");
Check(Floor("off",1024)==0 && Floor("192",512)==192 && Floor("96",256)==96,"Chosen rock floors serialize");
Check(Floor("7",1024)==160 && Floor("9999",512)==0,"Invalid rock floor falls back to automatic");
Check(RoundTrip(new EarthParams {Enabled=true,MapHeight=1024}).RockFloorDepth==0,"Older saves (no field) have no rock floor");
// 0053, author 5 November: Automatic mountain heights are linear only in worlds 512 and more blocks high.
string AutoCurve(string size,int slider,int mapHeight){var t=new TreeAttribute();t.SetString("earthHeightCurve","auto");t.SetString("earthWorldSize",size);
    t.SetInt("earthWorldHeight",slider);return EarthWorldOptions.Terrain(t,mapHeight).Curve;}
Check(AutoCurve("5009920",64,0)=="linear" && AutoCurve("2002944",64,0)=="linear","Automatic curve: linear at 1:10 and 1:25 (automatic heights 1024, 512)");
Check(AutoCurve("1002496",64,0)=="log1000" && AutoCurve("500736",64,0)=="log1000" && AutoCurve("125440",64,0)=="log1000","Automatic curve: log 1000 at 1:50 and smaller (automatic height 384)");
// 0068 (2.0.0-rc.5): the size is a scale; a width from before (above) still reads as the same scale
Check(AutoCurve("10",64,0)=="linear" && AutoCurve("25",64,0)=="linear" && AutoCurve("5",64,0)=="linear" && AutoCurve("50",64,0)=="log1000" && AutoCurve("100",64,0)=="log1000" && AutoCurve("400",64,0)=="log1000","Automatic curve by scale: linear at 1:5 to 1:25, log 1000 from 1:50");
TreeAttribute SizeTree(string size){var t=new TreeAttribute();t.SetString("earthWorldSize",size);return t;}
Check(Math.Abs(EarthWorldOptions.MetresPerBlock(SizeTree("10"))-EarthWorldOptions.MetresPerBlock(SizeTree("5009920")))<1e-9 && Math.Abs(EarthWorldOptions.MetresPerBlock(SizeTree("100"))-EarthWorldOptions.MetresPerBlock(SizeTree("500736")))<1e-9,"A scale and the width it had before rc.5 give the same metres per block");
Check(Math.Abs(EarthWorldOptions.MetresPerBlock(SizeTree("5"))-5.0)<0.01 && Math.Abs(EarthWorldOptions.MetresPerBlock(SizeTree("1:10"))-10.0)<0.02,"1:5 is 5 m per block, 1:10 (typed as 1:10) 10 m");
Check(AutoCurve("500736",512,0)=="linear" && AutoCurve("5009920",384,0)=="log1000","Automatic curve follows a height chosen by hand");
Check(AutoCurve("500736",0,512)=="linear" && AutoCurve("5009920",0,384)=="log1000" && AutoCurve("5009920",0,0)=="log1000","Automatic curve follows the map's height when known; the World generation tab's unknown counts as low");
// 0061's quick step: Streams "auto" (the default) draws every stream at 1:25 and finer (rings of the 1:10, 1:25, 1:50 and 1:100 sizes)
Check(new EarthMapConfig().Streams=="auto","Streams defaults to auto");
Check(EarthWorldOptions.StreamsFor("auto",4007936)=="all" && EarthWorldOptions.StreamsFor("Auto",1602560)=="all","Automatic streams: all at 1:10 and 1:25");
Check(EarthWorldOptions.StreamsFor("auto",801792)=="ends" && EarthWorldOptions.StreamsFor("auto",400384)=="ends" && EarthWorldOptions.StreamsFor("auto",0)=="ends","Automatic streams: ends at 1:50 and smaller");
Check(EarthWorldOptions.StreamsFor("ends",4007936)=="ends" && EarthWorldOptions.StreamsFor("none",4007936)=="none" && EarthWorldOptions.StreamsFor("all",400384)=="all","A chosen Streams value is kept at any scale");
using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"worldconfig.json")));
var styleConfig=doc.RootElement.GetProperty("playStyles").EnumerateArray().Single(p=>p.GetProperty("code").GetString()=="earth");
Check(styleConfig.GetProperty("worldType").GetString()=="standard" && doc.RootElement.GetProperty("playStyles").EnumerateArray().All(p=>p.GetProperty("worldType").GetString()=="standard"),"Standard-world play styles (\"earth\" is 1:100)");
using var standard=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/standard-survival-1.22.7.json")));
var values=styleConfig.GetProperty("worldConfig");
foreach(var entry in standard.RootElement.EnumerateObject()) Check(values.GetProperty(entry.Name).GetRawText()==entry.Value.GetRawText(),"Standard default differs: "+entry.Name);
using var lang=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"assets/game/lang/worldconfig-en.json")));
string langCode=styleConfig.GetProperty("langCode").GetString();
Check(lang.RootElement.TryGetProperty("playstyle-"+langCode,out _),"Missing play style name");
Check(lang.RootElement.TryGetProperty("playstyle-desc-"+langCode,out _),"Missing play style description (game's actual lang key)");
var gameConfig=Newtonsoft.Json.JsonConvert.DeserializeObject<ModWorldConfiguration>(File.ReadAllText(Path.Combine(root,"worldconfig.json")));
Check(gameConfig.PlayStyles?.Length==EarthWorldOptions.PlayStyleCodes.Length && gameConfig.PlayStyles.All(p=>EarthWorldOptions.IsEarthStyle(p.Code) && p.WorldConfig["gameMode"].AsString()=="survival"),
    "Game API must deserialize a non-null play style and its Standard settings");
foreach(var attr in doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray())
{
    string code=attr.GetProperty("code").GetString();
    Check(code.StartsWith("earth"),"Only Earth-owned keys");
    Check(!new[]{"earthTerrainDetail","earthAlpineEcology","earthRockExposure"}.Contains(code),"Test option still in menu");
    Check(attr.GetProperty("onlyDuringWorldCreate").GetBoolean(),"Creation only");
    Check(lang.RootElement.TryGetProperty("worldattribute-"+code+"-desc",out _),"Missing option description");
    if(attr.TryGetProperty("values",out var options))foreach(var option in options.EnumerateArray())
        Check(option.GetString()!="legacy" && lang.RootElement.TryGetProperty("worldconfig-"+code+"-"+option.GetString(),out _),"Missing/legacy menu value");
}
// 2.0.0-rc.6: the game's text field (GuiElementTextInput.RenderInteractiveElements) turns GL clipping off after it draws,
// so in a scrolling option list everything drawn after it ran over the screen's edges on a small window (the author: "on
// smaller resolution in Earth setting GUI if scroll up then text goes over window limit"). No mod code runs on that screen,
// so the field is the last option of a tab short enough never to scroll, and every tab of Earth's own has a name. Since
// 2.0.3 (decision 0085, the author's choice) Start and Start coordinates are the last options of vanilla's Player spawn and
// death tab ("spawnndeath", named by the game; mods' options follow the game's own there), after Starting climate, which
// the random start uses. On the scrolling Earth tab even the best place (right before World height, the only slider:
// a slider's resting text pushes and pops the list's clip) left the typed coordinates and that slider's bar drawn above
// the list whenever the tab was scrolled.
var vanillaTabs=new HashSet<string>{"gamemode","spawnndeath","survivalchallenges","temporalstability","worldgen","multiplayer"};
var onScreen=doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Where(a=>!a.TryGetProperty("onCustomizeScreen",out var s)||s.GetBoolean()).ToList();
foreach(var tab in onScreen.GroupBy(a=>a.GetProperty("category").GetString()))
{
    Check(vanillaTabs.Contains(tab.Key) || lang.RootElement.TryGetProperty("worldconfig-category-"+tab.Key,out _),"Tab name for "+tab.Key);
    var list=tab.ToList();
    for(int i=0;i<list.Count;i++)
    {
        string type=list[i].GetProperty("dataType").GetString().ToLowerInvariant();
        if(type is "string" or "intinput" or "doubleinput")
            Check(i==list.Count-1 && list.Count<=4,$"text field {list[i].GetProperty("code").GetString()} is the last option of a tab that never scrolls ({tab.Key}: {i+1} of {list.Count})");
    }
}
{
    var spawnTab=onScreen.Where(a=>a.GetProperty("category").GetString()=="spawnndeath").Select(a=>a.GetProperty("code").GetString()).ToList();
    Check(spawnTab.SequenceEqual(new[]{"earthSpawn","earthSpawnCoordinates"}),
        "Start, then Start coordinates, are Earth's only options on the Player spawn and death tab (2.0.3): "+string.Join(", ",spawnTab));
    Check(!onScreen.Any(a=>a.GetProperty("category").GetString()=="earthstart") && !lang.RootElement.TryGetProperty("worldconfig-category-earthstart",out _),
        "No Earth start tab any more (2.0.3)");
    foreach(var tabTextKey in new[]{"worldattribute-earthSpawn-desc","playstyle-desc-preset-earth","playstyle-desc-preset-earth-200",
        "playstyle-desc-preset-earth-50","playstyle-desc-preset-earth-25","playstyle-desc-preset-earth-10"})
    {
        string tabText=lang.RootElement.TryGetProperty(tabTextKey,out var tabTextEl)?tabTextEl.GetString()??"":"";
        Check(tabText.Length>0 && !tabText.Contains("Earth start tab") && !tabText.Contains("Spawn & Death"),tabTextKey+" names the tabs as the game does");
    }
}
foreach(var attr in doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray())
{
    if(attr.GetProperty("code").GetString()=="earthRocks")Check(attr.GetProperty("default").GetString()=="vanilla","Rocks and ground defaults to vanilla (the author, 15 November 2026, Earth 2.0.1, decision 0084)");
    if(attr.GetProperty("code").GetString()=="earthOreDeposits")Check(attr.GetProperty("default").GetString()=="vanilla","Ores default to vanilla for now (the author, 6 and 7 November)");
    Check(attr.GetProperty("code").GetString()!="earthOres","The single Rocks and ores menu entry is split in two");
    if(attr.GetProperty("code").GetString()=="earthGroundDepth")Check(attr.GetProperty("default").GetString()==EarthMap.Core.GroundColumns.DepthCurve.Default,"Ground depth menu default matches the code's");
}
SummitChecks.Run(root, Check);
string Elev(string v,string size){var t=new TreeAttribute();if(v!=null)t.SetString("earthElevation",v);t.SetString("earthWorldSize",size);return EarthWorldOptions.Elevation(t,null);}
Check(new[]{"125440","500736","5009920"}.All(size=>Elev(null,size)=="glo90" && Elev("auto",size)=="glo90") && Elev("builtin","125440")=="builtin" && Elev("glo90coarse","500736")=="glo90coarse",
    "GLO-90 only: Automatic (and no key) is full GLO-90 at every Earth size; a chosen value stays");
string Dl(string v){var t=new TreeAttribute();if(v!=null)t.SetString("earthGlo90Download",v);return EarthWorldOptions.Glo90Download(t);}
Check(Dl(null)==null && Dl("needed")==null && Dl("release")=="release" && Dl("copernicus")=="copernicus" && Dl("bogus")==null,
    "GLO-90 land heights: download all only when the Earth tab asks, from the release or Copernicus");
bool First(string v){var t=new TreeAttribute();if(v!=null)t.SetString("earthGlo90Download",v);return EarthWorldOptions.Glo90DownloadFirst(t);}
Check(Dl("first")=="release" && First("first") && !First("release") && !First(null) && !First("copernicus"),
    "EA-0133: \"before the world starts\" downloads from the release, and only it waits");
Check(Attr0("earthGlo90Download").GetProperty("values").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(new[]{"needed","release","first","copernicus"}),
    "EA-0133: the Earth tab offers download all before the world starts beside the other three");
JsonElement Attr0(string code)=>doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Single(a=>a.GetProperty("code").GetString()==code);
Check(doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Single(a=>a.GetProperty("code").GetString()=="earthGlo90Download").GetProperty("default").GetString()=="release"
    && doc.RootElement.GetProperty("playStyles").EnumerateArray().Single(p=>p.GetProperty("code").GetString()=="earth").GetProperty("worldConfig").GetProperty("earthGlo90Download").GetString()=="release",
    "GLO-90 land heights: new Earth worlds download all from Earth's data release by default (an older world without the key does not)");
Check(!doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Single(a=>a.GetProperty("code").GetString()=="earthGlo90Download").TryGetProperty("onCustomizeScreen",out _),
    "GLO-90 land heights is on the Earth tab");
foreach(var offTab in new[]{"earthElevation","earthRainFrequency","earthStormFrequency"})
    Check(doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Single(a=>a.GetProperty("code").GetString()==offTab).TryGetProperty("onCustomizeScreen",out var shown) && !shown.GetBoolean(),
        offTab+" is kept for configs but not on the Earth tab");
// 2.0.1 (the author, 15 November 2026; decision 0084): every play style takes vanilla rocks, as the row's default does
foreach(var style in doc.RootElement.GetProperty("playStyles").EnumerateArray())
    Check(style.GetProperty("worldConfig").GetProperty("earthRocks").GetString()=="vanilla",style.GetProperty("code").GetString()+" takes vanilla rocks and ground");
// 2.0.1: rain and storm frequency off the screen, kept for admins. The game writes every attribute's default into a new world
// (WorldConfig.allDefaultValues), so their default is empty and no play style sets them: earth.json decides, unless a
// world config sets a value
foreach(var freqKey in new[]{"earthRainFrequency","earthStormFrequency"})
{
    Check(Attr(freqKey).GetProperty("default").GetString()=="" && Attr(freqKey).GetProperty("dataType").GetString()=="string",freqKey+": an empty string by default (earth.json decides)");
    Check(doc.RootElement.GetProperty("playStyles").EnumerateArray().All(ps=>!ps.GetProperty("worldConfig").TryGetProperty(freqKey,out _)),freqKey+": no play style sets it");
}
{
    var admin=new EarthMapConfig{RainFrequency=2,StormFrequency=4};
    var none=new TreeAttribute();none.SetString("earthRainFrequency","");none.SetString("earthStormFrequency","");
    var tabNone=EarthWorldOptions.ReadTab(none,admin,null,384);
    Check(tabNone.RainFrequency==2 && tabNone.StormFrequency==4,"an empty world-config value takes earth.json's rain and storm frequency");
    var set=new TreeAttribute();set.SetString("earthRainFrequency","0.5");set.SetString("earthStormFrequency","0");
    var tabSet=EarthWorldOptions.ReadTab(set,admin,null,384);
    Check(tabSet.RainFrequency==0.5 && tabSet.StormFrequency==0,"a world config's own rain and storm frequency win over earth.json's");
    var plain=EarthWorldOptions.ReadTab(none,new EarthMapConfig(),null,384);
    Check(plain.RainFrequency==1 && plain.StormFrequency==1,"without either, the real climate's (1)");
}
// 2.0.1 (the author: "panama and Suez can be one setting"): one canals switch, off by default as both were. The two keys
// before it stay declared but off the screen (Fable's re-review of ee0110f, F1: the game copies only declared options into
// a new world, and its Modify world screen writes a missing option's default into an older one), and are read whenever the
// new switch is off
{
    var canals=Attr("earthCanals");
    Check(canals.GetProperty("dataType").GetString()=="bool" && canals.GetProperty("default").GetString()=="false" && canals.GetProperty("category").GetString()=="earth"
        && !canals.TryGetProperty("onCustomizeScreen",out _),"Map · Suez and Panama canals: one switch on the Earth tab, off by default");
    foreach(var oldKey in new[]{"earthSuezCanal","earthPanamaCanal"})
        Check(Attr(oldKey).GetProperty("dataType").GetString()=="bool" && Attr(oldKey).GetProperty("default").GetString()=="false"
            && Attr(oldKey).TryGetProperty("onCustomizeScreen",out var shownOld) && !shownOld.GetBoolean(),oldKey+": still declared (a server's configuration reaches a new world), off the screen, off by default");
    Check(doc.RootElement.GetProperty("playStyles").EnumerateArray().All(ps=>ps.GetProperty("worldConfig").TryGetProperty("earthCanals",out var c) && !c.GetBoolean()
        && !ps.GetProperty("worldConfig").TryGetProperty("earthSuezCanal",out _) && !ps.GetProperty("worldConfig").TryGetProperty("earthPanamaCanal",out _)),
        "every play style: canals off by the new key alone");
    string Canals(bool? both,bool? suez,bool? panama){var t=new TreeAttribute();if(both is bool b)t.SetBool("earthCanals",b);if(suez is bool s)t.SetBool("earthSuezCanal",s);if(panama is bool p)t.SetBool("earthPanamaCanal",p);return EarthWorldOptions.CanalCodes(t);}
    Check(Canals(true,null,null)=="suez,panama" && Canals(true,false,false)=="suez,panama" && Canals(false,false,false)=="" && Canals(false,null,null)=="",
        "a new world: the canals switch on gives both; off with the old keys off (their defaults), none");
    Check(Canals(false,true,null)=="suez" && Canals(false,null,true)=="panama" && Canals(false,true,true)=="suez,panama",
        "the switch off: a server configuration's old keys, each on its own; and an older world edited on the Modify world screen (canals off written in) keeps its own");
    Check(Canals(null,true,true)=="suez,panama" && Canals(null,true,null)=="suez" && Canals(null,null,true)=="panama" && Canals(null,false,false)=="" && Canals(null,null,null)==""
        && EarthWorldOptions.CanalCodes(null)=="","a world configuration from before 2.0.1 (no canals key): Suez and Panama each as before");
}
CoordinateChecks.Run(Check);
SpawnTimeChecks.Run(Check);
// Scale presets (5 November): one Earth play style per scale, each with its world height and mountain heights; no 1:400,
// no Automatic on the tab
JsonElement Attr(string code)=>doc.RootElement.GetProperty("worldConfigAttributes").EnumerateArray().Single(a=>a.GetProperty("code").GetString()==code);
var styles=doc.RootElement.GetProperty("playStyles").EnumerateArray().ToList();
var want=new (string code,string size,int height,string curve)[]{("earth-200","200",384,"log1000"),("earth","100",384,"log1000"),
    ("earth-50","50",384,"log1000"),("earth-25","25",512,"linear"),("earth-10","10",1024,"linear")};
Check(styles.Count==want.Length && want.All(w=>styles.Any(p=>p.GetProperty("code").GetString()==w.code)),"one Earth play style per scale");
foreach(var w in want){
    var c=styles.Single(p=>p.GetProperty("code").GetString()==w.code).GetProperty("worldConfig");
    Check(c.GetProperty("earthWorldSize").GetString()==w.size && c.GetProperty("earthWorldHeight").GetInt32()==w.height && c.GetProperty("earthHeightCurve").GetString()==w.curve,
        w.code+": size, world height and mountain heights for its scale");
    Check(EarthWorldOptions.IsEarthStyle(w.code) && EarthWorldOptions.CreatesEarth(true,"standard",w.code,true),w.code+" creates Earth");
    Check(Attr("earthWorldSize").GetProperty("values").EnumerateArray().Any(v=>v.GetString()==w.size),w.code+"'s size is on the Earth size row");
    Check(Attr("earthHeightCurve").GetProperty("values").EnumerateArray().Any(v=>v.GetString()==w.curve),w.code+"'s curve is on the Mountain heights row");
}
// 0072: each play style shows its scale's equator-to-pole distance (10,000 km over the scale); a new world gets its exact one
foreach(var w in want)
    Check(styles.Single(p=>p.GetProperty("code").GetString()==w.code).GetProperty("worldConfig").GetProperty("polarEquatorDistance").GetString()==(10_000_000/int.Parse(w.size)).ToString(),
        w.code+": polarEquatorDistance is 10,000 km over its scale");
{
    // the world's own distance: Cylinder World's pole distance (half the length less its 48-block walls) over 90° of latitude
    double Bpd(int scale){EarthMap.Core.SpawnSearch.SizeFor(scale.ToString(),out _,out int length,out _);return (length/2.0-48)/90;}
    foreach(int scale in new[]{200,100,50,25,10,5})
    {
        int d=EarthWorldOptions.PolarEquatorDistance(Bpd(scale));
        Check(Math.Abs(d-10_000_000.0/scale)<=0.005*10_000_000/scale,$"1:{scale}: polarEquatorDistance {d} is 10,000 km over the scale to 0.5%");
    }
    Check(EarthWorldOptions.PolarEquatorDistance(Bpd(100))==100_048 && EarthWorldOptions.PolarEquatorDistance(Bpd(10))==1_001_936 && EarthWorldOptions.PolarEquatorDistance(Bpd(200))==50_128,
        "polarEquatorDistance at 1:100, 1:10 and 1:200 (the numbers decision 0072 names)");
    Check(EarthWorldOptions.PolarEquatorDistance(0)==0 && EarthWorldOptions.PolarEquatorDistance(double.NaN)==0 && EarthWorldOptions.PolarEquatorDistance(-5)==0,
        "no polarEquatorDistance from an unusable geometry");
    var t=new TreeAttribute();t.SetString("polarEquatorDistance","100000");
    string line=EarthWorldOptions.ApplyPolarEquatorDistance(t,Bpd(10));
    Check(t.GetAsString("polarEquatorDistance")=="1001936" && line!=null && line.Contains("was 100000"),"a new 1:10 world's polarEquatorDistance replaces the play style's");
    var u=new TreeAttribute();u.SetString("polarEquatorDistance","42");
    Check(EarthWorldOptions.ApplyPolarEquatorDistance(u,double.NaN)==null && u.GetAsString("polarEquatorDistance")=="42","an unusable geometry leaves polarEquatorDistance as it is");
    var v=new TreeAttribute();
    Check(EarthWorldOptions.ApplyPolarEquatorDistance(v,Bpd(100))!=null && v.GetAsString("polarEquatorDistance")=="100048","polarEquatorDistance is written where it was not set");
}
string Rest(JsonElement p){var d=new SortedDictionary<string,string>();foreach(var kv in p.GetProperty("worldConfig").EnumerateObject())
    if(kv.Name!="earthWorldSize"&&kv.Name!="earthWorldHeight"&&kv.Name!="earthHeightCurve"&&kv.Name!="polarEquatorDistance")d[kv.Name]=kv.Value.ToString();
    return string.Join(";",d.Select(kv=>kv.Key+"="+kv.Value))+"|"+string.Join(",",p.GetProperty("mods").EnumerateArray().Select(m=>m.GetString()))+"|"+p.GetProperty("worldType").GetString();}
Check(styles.Select(Rest).Distinct().Count()==1,"the Earth play styles differ only in size, world height, mountain heights and polarEquatorDistance");
Check(!Attr("earthWorldSize").GetProperty("values").EnumerateArray().Any(v=>v.GetString()=="125440"||v.GetString()=="400"),"1:400 is not offered");
Check(Attr("earthWorldSize").GetProperty("values").EnumerateArray().Any(v=>v.GetString()=="5") && Attr("earthWorldSize").GetProperty("default").GetString()=="100","1:5 is offered (the author: \"as extreme 1:5 scale\"); 1:100 is the default");
Check(!Attr("earthHeightCurve").GetProperty("values").EnumerateArray().Any(v=>v.GetString()=="auto") && !Attr("earthHeightCurve").TryGetProperty("onCustomizeScreen",out _),
    "Mountain heights is on the Earth tab, without Automatic");
Check(Attr("earthWorldHeight").GetProperty("default").GetString()=="384" && Attr("earthWorldHeight").GetProperty("min").GetInt32()==EarthWorldOptions.MinHeight
    && Attr("earthWorldHeight").GetProperty("max").GetInt32()==EarthWorldOptions.MaxHeight && Attr("earthWorldHeight").GetProperty("step").GetInt32()==64
    && !Attr("earthWorldHeight").TryGetProperty("values",out _),"World height: 256 to 1024 in steps of 64, every value one Height() accepts (2.1.2, decision 0089)");
for(int hv=EarthWorldOptions.MinHeight;hv<=EarthWorldOptions.MaxHeight;hv+=64)
    if(Height(t=>t.SetInt("earthWorldHeight",hv),out hb)!=hv || hb!=null) Check(false,"the World height slider's "+hv+" is a height Height() takes");
using(var langDoc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"assets","game","lang","worldconfig-en.json")))){
    var tabLang=langDoc.RootElement;
    foreach(var w in want){
        string lc=styles.Single(p=>p.GetProperty("code").GetString()==w.code).GetProperty("langCode").GetString();
        Check(tabLang.TryGetProperty("playstyle-"+lc,out _) && tabLang.TryGetProperty("playstyle-desc-"+lc,out _),w.code+" has a name and description");
        EarthMap.Core.SpawnSearch.SizeFor(w.size,out int sw,out int sl,out _);
        string label=tabLang.GetProperty("worldconfig-earthWorldSize-"+w.size).GetString();
        string Spaced(int n)=>n.ToString("#,0",System.Globalization.CultureInfo.InvariantCulture).Replace(","," ");
        Check(label.StartsWith("1:"+w.size+" ") && label.Contains(Spaced(sw)+" × "+Spaced(sl)),w.code+"'s size label gives its scale and the world's size: "+label);
    }
}
Check(Attr("earthBiomes").GetProperty("default").GetString()=="earth","Biomes defaults to Earth's");
// ER-05, ER-24 (code review 14 November 2026): the saved rules against this build's newest, and EarthParams' field table
{
    var fields=typeof(EarthParams).GetFields().Select(f=>(Field:f,Tag:f.GetCustomAttributes(typeof(ProtoBuf.ProtoMemberAttribute),false).Cast<ProtoBuf.ProtoMemberAttribute>().FirstOrDefault()?.Tag))
        .Where(t=>t.Tag!=null).OrderBy(t=>t.Tag).ToList();
    string Table()=>string.Join("\n",fields.Select(t=>t.Tag+" "+t.Field.Name+" "+t.Field.FieldType.Name));
    // (S-R2 of the Fable pass: the one check of this, moved here from PlaytestReviewTests) a number names one field
    Check(fields.Select(t=>t.Tag).Distinct().Count()==fields.Count,"EarthParams' protobuf numbers are unique");
    if(Environment.GetEnvironmentVariable("PRINT_PARAMS_TABLE")=="1")Console.WriteLine(Table());
    // append only: a number keeps its name and type for good (a removed field's number stays burnt, listed in the header)
    Check(Table()==PinnedParamsTable.Replace("\r",""),"EarthParams' protobuf numbers, names and types are as pinned (append new fields at the end of PinnedParamsTable)");
    var rules=new EarthParams().RulesAgainstBuild().Select(r=>r.Name).ToList();
    Check(rules.Distinct().Count()==rules.Count,"each rules field once in RulesAgainstBuild");
    foreach(var (f,_) in fields.Where(t=>t.Field.FieldType==typeof(int)))
        Check(rules.Contains(f.Name)^EarthParams.PlainNumbers.Contains(f.Name),"EarthParams."+f.Name+" is a rules field in RulesAgainstBuild or a plain number, not both");
    // new worlds of every tab choice are never newer than this build
    foreach(var (rocks,ores,nat,biomes,springs,ponds) in new[]{("groundcolumns","maps","1318","earth","streams","wetlands"),("vanilla","vanilla","vanilla","vanilla","vanilla","vanilla"),("regional","maps","1318","earth","caves","off")}){
        var t=new TreeAttribute();t.SetString("earthRocks",rocks);t.SetString("earthOreDeposits",ores);t.SetString("earthNature",nat);t.SetString("earthBiomes",biomes);
        t.SetString("earthMountainSprings",springs);t.SetString("earthSurfacePonds",ponds);
        var np=Freeze(t,new EarthMapConfig());
        Check(np.RulesNewerThanBuild().Count==0,"a new world ("+rocks+", "+ores+", "+nat+") is not newer than this build: "+string.Join(", ",np.RulesNewerThanBuild()));
    }
    // (S-L5 of the Fable pass) and with the data a server has: the GLO-90 rules a new world freezes from it (the included
    // heights, the rivers on GLO-90), against this build's newest. The data store: EARTH_STORE, else the player's or the
    // AgentBridge bench's ModData\earth; without one this part is skipped, said.
    string store=Environment.GetEnvironmentVariable("EARTH_STORE");
    string appData=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    store=new[]{store,Path.Combine(appData,"VintagestoryData","ModData","earth"),Path.Combine(appData,"VintagestoryData-agentbridge","ModData","earth")}
        .FirstOrDefault(s=>!string.IsNullOrEmpty(s)&&Directory.Exists(Path.Combine(s,"data")));
    if(store==null)Console.WriteLine("SKIP S-L5: no Earth data store on this machine (EARTH_STORE); new worlds frozen without data only");
    else{
        // (as the server loads it: with the terrain, whose load reads the rivers' GLO-90 snap and arms overlays)
        var data=EarthData.Load(name=>{string a=Path.Combine(root,"earthdata",name),b=Path.Combine(store,"data",name);return File.Exists(a)?File.OpenRead(a):File.Exists(b)?File.OpenRead(b):null;},true);
        int Newest(string field)=>new EarthParams().RulesAgainstBuild().First(r=>r.Name==field).Newest;
        foreach(string elevation in new[]{"glo90","glo90coarse","auto"}){
            var t=new TreeAttribute();t.SetString(EarthWorldOptions.ElevationKey,elevation);
            var np=Freeze(t,new EarthMapConfig(),data);
            Check(np.RulesNewerThanBuild().Count==0&&np.RiverCourses==Newest("RiverCourses")&&np.CoarseElevation==Newest("CoarseElevation")
                    &&np.FineElevation==(elevation=="glo90coarse"?0:Newest("FineElevation")),
                "S-L5: a new world on "+elevation+", frozen with the data store's data, gets this build's newest GLO-90 rules (river courses "+np.RiverCourses+
                ", included heights "+np.CoarseElevation+", GLO-90 "+np.FineElevation+") and none newer: "+string.Join(", ",np.RulesNewerThanBuild()));
        }
        // (EA-0084, ER-10: one list of each rule's newest for the builder and the refusal) a new world with every choice on gets
        // the newest of every rule, as RulesAgainstBuild names it: a constant in WorldParamsBuilder left behind a rules bump fails
        // here, not only one above it (EA-0089 C: the builder sets every field offline, so only two are named)
        {
            var t=new TreeAttribute();t.SetString(EarthWorldOptions.ElevationKey,"glo90");
            foreach(var (k,v) in new[]{("earthRocks","groundcolumns"),("earthOreDeposits","maps"),("earthNature","1318"),("earthBiomes","earth"),("earthMountainSprings","streams"),("earthSurfacePonds","off")})t.SetString(k,v);
            var full=Freeze(t,new EarthMapConfig(),data);
            // GroundData is the ground download's, set at the world's first start (EarthGeology); VanillaBiomes 1 is the "vanilla" choice
            var notFreezes=new HashSet<string>{"GroundData","VanillaBiomes"};
            var behind=full.RulesAgainstBuild().Where(r=>!notFreezes.Contains(r.Name)&&r.Saved!=r.Newest).Select(r=>r.Name+" "+r.Saved+" (newest "+r.Newest+")").ToList();
            Check(behind.Count==0,"EA-0084: a new world with every choice on gets the newest of every rule, as RulesAgainstBuild names it: "+string.Join(", ",behind));
        }
    }
    // a world at this build's newest everywhere passes; one step above in any field is named
    var newest=new EarthParams();
    void Set(EarthParams q,string name,int v)=>typeof(EarthParams).GetField(name).SetValue(q,v);
    foreach(var r in newest.RulesAgainstBuild().ToList())Set(newest,r.Name,r.Newest);
    Check(newest.RulesNewerThanBuild().Count==0,"a world at this build's newest rules is not refused");
    foreach(var r in new EarthParams().RulesAgainstBuild().ToList()){
        var q=RoundTrip(newest);Set(q,r.Name,r.Newest+1);
        Check(q.RulesNewerThanBuild().Count==1 && q.RulesNewerThanBuild()[0].StartsWith(r.Name+" "),"a world saved with "+r.Name+" above this build's is refused");
    }
    var rec=RoundTrip(new EarthParams {Enabled=true,CreatedWith="2.0.0-rc.13",LastSavedWith="2.0.0"});
    Check(rec.CreatedWith=="2.0.0-rc.13" && rec.LastSavedWith=="2.0.0" && RoundTrip(new EarthParams()).CreatedWith==null,"CreatedWith and LastSavedWith are saved; older worlds have none");
    // FP-04: a field of a newer Earth (here 300, a varint 1) is seen, and kept when the params are saved again
    var withNew=new EarthParams {Enabled=true,GenFixRules=4};
    byte[] bytes;
    using(var ms=new MemoryStream()){ProtoBuf.Serializer.Serialize(ms,withNew);ms.Write(new byte[]{0xE0,0x12,0x01});bytes=ms.ToArray();}
    var read=ProtoBuf.Serializer.Deserialize<EarthParams>(new MemoryStream(bytes));
    Check(read.HasUnknownFields && read.GenFixRules==4 && !RoundTrip(withNew).HasUnknownFields,"FP-04: a field this build doesn't know is seen; a record of known fields has none");
    byte[] again;
    using(var ms=new MemoryStream()){ProtoBuf.Serializer.Serialize(ms,read);again=ms.ToArray();}
    Check(again.Length==bytes.Length && ProtoBuf.Serializer.Deserialize<EarthParams>(new MemoryStream(again)).HasUnknownFields,"FP-04: saved again, the unknown field is kept ("+again.Length+" of "+bytes.Length+" bytes)");
}
PackageChecks.Run(root, Check);
ModDbChecks.Run(root, Check);
ConfigStoreChecks.Run(Check);
WorldUpgradeChecks.Run(Check);
ParamsBuilderChecks.Run(root, Check);
Console.WriteLine($"Earth world options: {checks} checks passed.");

partial class Program
{
    /// <summary>EarthParams' fields by protobuf number: number, name, type (ER-24). Append only.</summary>
    const string PinnedParamsTable = @"1 Version Int32
2 Enabled Boolean
3 RingStart Double
4 RingLength Double
5 Wraps Boolean
6 Gutter Double
7 EquatorZ Double
8 BlocksPerDegreeZ Double
9 SeamLon0 Double
10 Cylinder Boolean
11 MapHeight Int32
12 SeaLevel Int32
13 TopY Double
14 LandH0 Double
15 MaxDepth Double
16 SeaD0 Double
17 GutterSeabedY Int32
18 NoiseSeed Int64
19 DetailScale Double
20 Zones List`1
21 SpawnLat Double
22 SpawnLon Double
23 SpawnX Int32
24 SpawnZ Int32
25 StoryPlaced Boolean
26 ExtendTemperature Boolean
27 RealSeasons Boolean
28 Rivers Boolean
29 RiverMinDischarge Double
30 RiverWidthScale Double
31 RiverRelief Double
32 StreamMode String
33 StreamMinDischarge Double
34 RiverStraighten Double
35 RiverSmoothWidths Double
36 RegionalTrees Boolean
37 TreeRulesVersion Int32
38 RegionalGeology Boolean
39 GeologyRulesVersion Int32
40 GeologyProviderSignature String
41 RegionalFauna Boolean
42 FaunaRulesVersion Int32
43 IncludeExtinctAnimals Boolean
44 RegionalOres Boolean
45 OreRulesVersion Int32
46 OreSettingsJson String
47 OreDataSignature String
48 GeologyTransitionBlocks Double
49 TerrainPlanSignature String
50 GutterBridges Boolean
51 RiverFalls Int32
52 RiverChannels Int32
53 Canals String
55 Islands Int32
56 MinIslandBlocks Double
57 RiverEnds Int32
58 ClimateRules Int32
59 MinSeaDepth Int32
60 LakeShores Int32
61 Coasts Int32
62 RiverDeltas Int32
63 RiverWidths Int32
64 GutterSeabed Int32
65 Weather Boolean
66 WeatherPace Double
67 RainFrequency Double
68 StormFrequency Double
69 WeatherRules Int32
70 ClimateData Int32
71 WeatherDataSignature String
72 WeatherLocalSun Boolean
73 RiverPoolLayout Int32
74 LinearLand Boolean
75 LocalAlpineEcology Boolean
76 PhysicalRockExposure Boolean
77 TerrainProfile String
78 VanillaFaunaRanges Int32
79 FineRain Int32
80 Summits Int32
81 Glaciers Int32
82 AlpineSurfaceRules Int32
83 PeakCatalogSignature String
84 AridRiverRules Int32
85 StraitRules Int32
86 ReservoirRules Int32
87 CreationOptionsVersion Int32
88 FaunaRangeProfile String
89 SummitMinElevation Int32
90 NoRiverRapids Boolean
91 PlacementRules Int32
92 SoilRules Int32
93 RockFloorDepth Int32
95 FineElevation Int32
96 CoarseElevation Int32
97 RiverCourses Int32
98 DetailRules Int32
99 ChickensByRange Boolean
100 LakeOutlets Int32
101 SeaReferee Int32
102 LakeOutlines Int32
103 RiverOutlines Int32
104 GeothermalRules Int32
105 DamRules Int32
106 Works1318 Int32
107 DepositSet Int32
108 TemperatureRangeFrozen Int32
109 WeatherDataMade String
120 GroundCoverRules Int32
121 ZoneRules Int32
122 MoistureRules Int32
123 GrassWinterRules Int32
124 FireRules Int32
125 WetlandRules Int32
126 PondRules Int32
127 ChannelRules Int32
128 AridityRules Int32
129 SpringRules Int32
190 GroundColumns Int32
191 GroundDepth String
192 GroundData Int32
193 OreLayers Int32
194 WaterTableRules Int32
195 OreRules Int32
196 PerennialRules Int32
197 FlowRules Int32
198 ShoreZoneRules Int32
199 ShoreRules Int32
200 GenFixRules Int32
201 WeatherModelRules Int32
202 VanillaBiomes Int32
203 CraftingRules Int32
204 SiteRules Int32
205 CreatedWith String
206 LastSavedWith String
209 WeatherGroundSignature String
210 SeasonRules Int32
212 Climate1318 Int32";
}
