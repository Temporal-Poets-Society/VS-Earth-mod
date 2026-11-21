using EarthMap.Core;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Site = EarthMap.Core.RegionalFaunaCatalog.Site;
using Rule = EarthMap.Core.RegionalFaunaCatalog.Rule;

// Usage: RegionalFaunaTests [repo root] [--no-terrain] [--coverage]
//   default      pure catalogue tests + real-terrain tests on the user's world layout (Cylinder World 512000 x 204800, height 384)
//   --coverage   also regenerates docs/research/regional-fauna-2026-09-29/coverage.csv and native-climate-scan.csv
string root=Path.GetFullPath(args.FirstOrDefault(a=>!a.StartsWith("--")) ?? Directory.GetCurrentDirectory());
bool terrain=!args.Contains("--no-terrain"), coverage=args.Contains("--coverage");
var inv=CultureInfo.InvariantCulture;
int count=0; var failures=new List<string>();
void Check(bool ok,string why){count++; if(!ok){failures.Add(why); Console.WriteLine("FAIL "+why);}}
string rulesJson=File.ReadAllText(Path.Combine(root,"assets/earth/config/fauna-species.json"));
string regionsJson=File.ReadAllText(Path.Combine(root,"assets/earth/config/treeregions.json"));
var file=JsonSerializer.Deserialize<RegionalFaunaCatalog.RuleFile>(rulesJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
var all=new RegionalFaunaCatalog(rulesJson,regionsJson,file.Providers.Select(p=>p.Id));
var none=new RegionalFaunaCatalog(rulesJson,regionsJson,Array.Empty<string>());
Rule R(string provider,params string[] tokens)=>all.Rules.Single(r=>r.Provider==provider && r.Tokens.SequenceEqual(tokens));
Site S(double lat,double lon,bool sea=false,bool fresh=false,bool salt=false,bool coast=false,double elev=100,double depth=3)=>
    new(lat,lon,all.RegionIndexAt(lat,lon),elev,depth,sea,fresh,salt,coast);
bool At(Rule r,double lat,double lon,bool extinct=true,bool sea=false,bool fresh=false,bool salt=false,bool coast=false,double elev=100)=>
    RegionalFaunaCatalog.Allows(r,S(lat,lon,sea,fresh,salt,coast,elev),extinct);
string Key(Rule r)=>r.Provider+":"+string.Join("-",r.Tokens);

// ---------------------------------------------------------------- data integrity and inventory
Check(file.Version==RegionalFaunaCatalog.RulesVersion,$"data file version {file.Version} matches code rules version {RegionalFaunaCatalog.RulesVersion}");
Check(all.Problems.Count==0,"shipped catalogue loads without data problems: "+string.Join("; ",all.Problems));
Check(RegionalFaunaCatalog.ProviderIds(rulesJson).SequenceEqual(file.Providers.Select(p=>p.Id)),"cheap provider scan equals the full parse");
Check(none.Rules.Count==0 && !none.Controls("caninae:caninae-canina-adult-female-canis-lupus"),"absent providers contribute no rules and control nothing");
Check(none.Allows("game:hare-adult",default,true),"vanilla fallback unchanged");
Check(all.Allows("othermod:rhino",default,true),"unrelated mods untouched");
Check(!all.Allows("rhinocerotidae:brand-new-rhino",default,true),"unknown species of a mapped provider fail closed");
// Every audited entity code (all variant cross-products, valid or not): no ambiguity, and every rule is reachable.
var auditCodes=new List<string>();
using(var inventory=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/research/fauna-packs-2026-11-17/entity-spawn-audit.json"))))
foreach(var asset in inventory.RootElement.EnumerateArray())
{
    string provider=asset.GetProperty("modid").GetString()!;
    if(!file.Providers.Any(p=>p.Id==provider))continue;
    var codes=new List<string>{asset.GetProperty("code").GetString()!};
    var groups=asset.GetProperty("variantgroups");
    if(groups.ValueKind==JsonValueKind.Array)foreach(var group in groups.EnumerateArray())
        if(group.TryGetProperty("states",out var states)) codes=codes.SelectMany(c=>states.EnumerateArray().Select(s=>c+"-"+s.GetString())).ToList();
    auditCodes.AddRange(codes.Select(c=>provider+":"+c));
}
var reached=new HashSet<Rule>();
int ambiguousCodes=0;
foreach(var code in auditCodes) { var r=all.Find(code,out bool isAmbiguous); if(isAmbiguous) ambiguousCodes++; if(r!=null) reached.Add(r); }
Check(ambiguousCodes==0,$"no audited entity code matches two taxa ({ambiguousCodes} ambiguous of {auditCodes.Count})");
Check(all.Rules.All(reached.Contains),"every rule maps at least one audited provider entity: "+string.Join(", ",all.Rules.Where(r=>!reached.Contains(r)).Select(Key)));
foreach(var r in all.Rules) foreach(string age in new[]{"adult","baby","carcass"})
    Check(all.Find(r.Provider+":family-"+age+"-female-"+string.Join("-",r.Tokens))==r,"unique complete token match for "+Key(r)+"/"+age);
Check(all.Find("caninae:caninae-vulpini-adult-female-vulpes-bengalensis")?.Tokens.Last()=="bengalensis","Vulpes vulpes needs two adjacent tokens, not one");
Check(all.Find("rhinocerotidae:rhinocerotidae-carcass-dicerosbicornis")==R("rhinocerotidae","dicerosbicornis"),"species carcass keeps the species gate");

// ---------------------------------------------------------------- held list, policy, licensing
var expectedHeld=new[]{"manidae:mysteria","capreolinae:bretzianebrascensis","capreolinae:odocoileuslucasi",
    "capreolinae:torontoceroshypnogeos","capreolinae:rangifertarandusdomesticus","caninae:speothos-pacivorus","bovinae:bubalusbrevicornis",
    "bovinae:bubalusmurrensis","bovinae:syncerusantiquus","bovinae:bostaurustaurus","bovinae:bubalusbubalis"};
var held=all.Rules.Where(r=>r.Quarantined).Select(Key).OrderBy(k=>k).ToArray();
Check(held.SequenceEqual(expectedHeld.OrderBy(k=>k)),"held list is exactly the reviewed set: "+string.Join(", ",held));
Check(all.Rules.Where(r=>r.Quarantined).All(r=>!string.IsNullOrWhiteSpace(r.Note) && !r.Note.StartsWith("Unresolved")),"every held taxon states a specific reason");
Check(all.Rules.Where(r=>r.Extinct && !r.Quarantined).All(r=>!string.IsNullOrWhiteSpace(r.Period)),"every enabled extinct taxon states the period its range represents: "+
    string.Join(", ",all.Rules.Where(r=>r.Extinct && !r.Quarantined && string.IsNullOrWhiteSpace(r.Period)).Select(Key)));
Check(!all.Rules.Any(r=>(r.Evidence??"").Contains("iucnredlist.org")),"no rule relies on IUCN Red List pages");
Check(!all.Rules.Any(r=>!r.Quarantined && r.Extinct && r.Mask!=null),"extinct taxa never use PHYLACINE counterfactual rasters");

// ---------------------------------------------------------------- geographic regressions (pure)
var lupaster=R("caninae","canis","lupastor");
Check(!lupaster.Quarantined && !lupaster.Extinct,"(1a) African golden wolf is an enabled wild canid, not a held domestic dog");
Check(At(lupaster,-2,36) && At(lupaster,32,-6),"(1a) golden wolf in Kenya and Morocco");
Check(!At(lupaster,50,10) && !At(lupaster,26,80) && !At(lupaster,43,22) && !At(lupaster,36,138),"(1a) golden wolf not in Europe or Asia (Eurasian golden jackal range of the shared raster)");
var aurochs=R("bovinae","bosprimigeniusprimigenius");
Check(!At(aurochs,53,-8) && At(aurochs,52,-1) && At(aurochs,52,20),"(1d) aurochs in Britain and Poland, not Ireland");
var adelie=R("spheniscidae","pygoscelisadeliae"); var emperor=R("spheniscidae","aptenodytesforsteri");
// Rules v4: no latitude rule; Antarctic sites are ANT by geography. At each longitude, the first point south of 60 S on the
// ANT outline is Antarctic coast; the terrain-based coverage test below checks Earth's own coastline.
foreach(double lon in new[]{-170d,-120d,-60d,0d,45d,90d,140d})
{
    double lat=-60; while(lat>-85 && all.Geography.CodeAt(lat,lon)!="ANT") lat-=.05;
    Check(lat>-85 && At(adelie,lat,lon,coast:true) && At(emperor,lat,lon,coast:true),$"(1e) Antarctic penguins on the Antarctic coast (lon {lon}, {lat:0.00})");
}
Check(all.RegionAt(-75,10)=="ANT" && all.RegionAt(-62,-120,false)==null,"(1e) Antarctica is ANT by its outline; the open Southern Ocean has no region (no latitude rule)");
Check(!At(emperor,70,25,coast:true),"no Arctic penguins");
var seacow=R("sirenia","gigas");
Check(At(seacow,55,166,sea:true) && At(seacow,52,-175,sea:true) && At(seacow,43,145,sea:true) && At(seacow,36.6,-122,sea:true),"(6) Steller's sea cow on the North Pacific rim from Japan to California");
Check(!At(seacow,20,-157,sea:true) && !At(seacow,55,166),"(6) not Hawaii; never on land");
var dingo=R("caninae","canis","lupusdingo");
Check(At(dingo,-25,133) && !At(dingo,-42,147) && !At(dingo,-6,145),"(3) dingo on mainland Australia, not Tasmania or New Guinea");
var cyclotis=R("elephantidae","loxodontacyclotis");
Check(At(cyclotis,-1,11.5) && !At(cyclotis,-2,36) && !At(cyclotis,-20,25),"(3) forest elephant in Gabon, not East/Southern African savanna");
var lycaon=R("caninae","canis","lycaon");
Check(At(lycaon,45.8,-78.4) && !At(lycaon,54,-85) && !At(lycaon,45,10),"(3) eastern wolf in Algonquin, not Hudson Bay lowlands");
Check(At(R("viverridae","genettaplesictoides"),35,33) && !At(R("viverridae","genettaplesictoides"),35,25),"(3) Cyprus genet on Cyprus, not Crete");
Check(At(R("bovinae","bubaluscebuensis"),10.3,123.9) && !At(R("bovinae","bubaluscebuensis"),14.6,121),"(3) Cebu buffalo on Cebu, not Luzon");
Check(At(R("vombatidae","vombatushacketti"),-33.5,115.5) && !At(R("vombatidae","vombatushacketti"),-37,145),"(3) Hackett's wombat in south-western Australia only");
Check(At(R("caninae","dusicyon","avus"),-35,-61) && !At(R("caninae","dusicyon","avus"),-3,-60),"(3) Dusicyon avus in the Pampas, not Amazonia");
// EA-0156: the packs of 17 November 2026 (Bovinae 1.0.3's steppe bison, Caninae's domestic dog, Moschidae 1.0.1's musk deer).
var bison=R("bovinae","bospriscus");
Check(bison.Extinct && !bison.Quarantined && At(bison,50,10) && At(bison,63,130) && At(bison,64,-147) && At(bison,61,-135)
    && !At(bison,40,-100) && !At(bison,36,138) && !At(bison,10,20) && !At(bison,50,10,false),
    "steppe bison: extinct, from Germany and Yakutia to Alaska and Yukon; not the Great Plains, Japan or Africa; off without extinct animals");
var dog=R("caninae","canis","familiaris");
Check(!dog.Quarantined && !dog.Extinct && dog.Mask==null && At(dog,51,10) && At(dog,19.4,-99.1) && At(dog,-13.5,-72) && At(dog,64,-51) && At(dog,-41.3,174.8)
    && At(dog,19.6,-155.5) && At(dog,62,130),
    "domestic dog (gone feral, decision 0054): Germany, Mexico, the Andes, Greenland, New Zealand, Hawaii, Yakutia");
Check(!At(dog,-25,133) && !At(dog,-42,147) && !At(dog,-27.1,-109.4) && !At(dog,-3,-60) && !At(dog,-50,-70) && !At(dog,-20.5,57.5),
    "domestic dog: not Australia (the dingo's), Tasmania, Easter Island, Amazonia, Patagonia or uninhabited Mauritius");
Check(all.Find("caninae:caninae-canina-baby-female-canis-familiaris")==dog && all.Find("caninae:caninae-canina-adult-male-canis-familiaris-semitamed")==dog,
    "the pack's dog pups and semi-tame dogs map to the dog rule");
foreach(var (tok,lat,lon,name) in new[]{("moschusmoschiferus",64.6,128.5,"Siberian musk deer, Yakutia"),("moschusmoschiferus",48.24,142.5,"Siberian musk deer, Sakhalin"),
    ("moschuscupreus",34.5,74.5,"Kashmir musk deer, Kashmir"),("moschusfuscus",28.5,92.0,"black musk deer, eastern Himalaya"),
    ("moschusberezovskii",28.0,105.0,"dwarf musk deer, south China")})
    Check(!R("moschidae",tok).Extinct && R("moschidae",tok).Mask!=null && R("moschidae",tok).CurrentMask!=null && At(R("moschidae",tok),lat,lon),"Moschidae: "+name);
Check(!At(R("moschidae","moschusmoschiferus"),55,37) && !At(R("moschidae","moschuscupreus"),62,130),"musk deer: none in European Russia; the Kashmir musk deer not in Siberia");
Check(file.Providers.Any(p=>p.Id=="moschidae" && p.Version=="1.0.1") && file.Providers.Any(p=>p.Id=="bovinae" && p.Version=="1.0.3") && file.Providers.Length==21,
    "21 reviewed providers, Moschidae 1.0.1 among them");
// Previous single-point regressions, unchanged behaviour.
var rhino=R("rhinocerotidae","dicerosbicornis"); var woolly=R("rhinocerotidae","coelodontaantiquitatis");
Check(At(rhino,-2,36) && !At(rhino,15,100),"black rhino East Africa, not equally warm Asia");
Check(At(woolly,60,90) && !At(woolly,64,-150) && !At(woolly,43,143) && !At(woolly,60,90,false),"woolly rhino Eurasia; not Alaska; Japan island barrier; extinct toggle");
Check(At(rhino,-2,36,false),"extinct toggle keeps living species");
Check(At(R("elephantidae","mammuthusexilis"),34.0,-120.1) && !At(R("elephantidae","mammuthusexilis"),34.0,-118.5),"pygmy mammoth Santa Rosae, not mainland");
Check(At(R("dinornithidae","dinornisrobustus"),-44,171) && !At(R("dinornithidae","dinornisrobustus"),-38,176),"South Island moa cannot cross Cook Strait");
Check(!At(R("spheniscidae","spheniscusdemersus"),-30,20) && At(R("spheniscidae","spheniscusdemersus"),-34,18.4,coast:true) && !At(R("spheniscidae","spheniscusdemersus"),-34,18.4,salt:true,coast:true),"African penguin only on the Cape sea coast");
Check(At(R("chelonioidea","cheloniamydas"),0,-130,sea:true) && !At(R("chelonioidea","cheloniamydas"),0,-130,salt:true),"turtle in open tropical sea, not a salt lake");
Check(!At(R("sirenia","inunguis"),-3,-60,sea:true) && At(R("sirenia","inunguis"),-3,-60,fresh:true),"Amazon manatee in fresh water only");
Check(At(R("iniidae","lipotesvexillifer"),30.5,114.3,fresh:true) && !At(R("iniidae","lipotesvexillifer"),30.5,114.3) && !At(R("iniidae","lipotesvexillifer"),23,113,fresh:true),"baiji in middle/lower Yangtze fresh water only, not the Pearl River");
Check(!RegionalFaunaCatalog.Allows(rhino,new Site(-2,36,all.Geography.IndexOf("KEN"),outsideEarth:true),true),"Earth gutter and polar filler excluded");

// ---------------------------------------------------------------- generic carcasses
Check(RegionalFaunaCatalog.GenericCarcassPrefix("pantherinae:pantherinae-panthera-carcass")=="pantherinae:pantherinae-panthera"
    && RegionalFaunaCatalog.GenericCarcassPrefix("rhinocerotidae:rhinocerotidae-carcass-dicerosbicornis")==null,"generic carcass prefix");
var groupsByCode=new Dictionary<string,Rule[]>();
foreach(var code in auditCodes.Where(c=>RegionalFaunaCatalog.GenericCarcassPrefix(c)!=null).Distinct())
{
    var g=all.GroupRules(code,auditCodes);
    if(g!=null) groupsByCode[code]=g;
}
foreach(var code in new[]{"caninae:caninae-canina-carcass","caninae:caninae-cerdocyonina-carcass","pantherinae:pantherinae-neofelis-carcass",
    "pantherinae:pantherinae-panthera-carcass","machairodontinae:machairodontinae-carcass","casuariidae:casuariidae-carcass",
    // new in Cervinae 0.1.19 and Vombatidae 0.4.8 (EA-0156)
    "cervinae:cervidae-cervini-carcass","vombatidae:diprotodontidae-carcass","vombatidae:thylacoleonidae-carcass"})
    Check(groupsByCode.TryGetValue(code,out var g) && g.Length>0 && g.All(r=>!r.Quarantined && code.StartsWith(r.Provider+":")),"(4) "+code+" has a non-empty species group");
var canina=groupsByCode["caninae:caninae-canina-carcass"];
Check(canina.Contains(R("caninae","canis","lupus")) && !canina.Contains(R("caninae","vulpes","vulpes")),"(4) canina carcass group: wolves yes, foxes no");
// (Hawaii until EA-0156: its feral dogs (decision 0054) now count for the group; Easter Island had none)
Check(RegionalFaunaCatalog.AllowsAny(canina,S(44.5,-110.5),true) && !RegionalFaunaCatalog.AllowsAny(canina,S(-27.1,-109.4),true) && !RegionalFaunaCatalog.AllowsAny(canina,S(-42,147),true),"(4) canid carcass where canids live (Yellowstone), not Easter Island or Tasmania");
var sabre=groupsByCode["machairodontinae:machairodontinae-carcass"];
Check(RegionalFaunaCatalog.AllowsAny(sabre,S(40,-100),true) && !RegionalFaunaCatalog.AllowsAny(sabre,S(40,-100),false),"(4) sabre-tooth carcass follows the extinct toggle of its all-extinct group");
Check(!RegionalFaunaCatalog.AllowsAny(groupsByCode["casuariidae:casuariidae-carcass"],S(50,10),true) && RegionalFaunaCatalog.AllowsAny(groupsByCode["casuariidae:casuariidae-carcass"],S(-25,133),true),"(4) cassowary/emu carcass in Australia, not Europe");

// ---------------------------------------------------------------- save safety of the catalogue itself
string Synthetic(int version,string entries)=>"{\"version\":"+version+",\"providers\":[{\"id\":\"p\",\"version\":\"1\"}],\"entries\":["+entries+"]}";
var amb=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"A\",\"tokens\":[\"a\"],\"regions\":[\"GRB\"]},{\"provider\":\"p\",\"taxon\":\"AB\",\"tokens\":[\"a\",\"b\"],\"regions\":[\"GRB\"]}"),regionsJson,new[]{"p"});
Check(amb.Find("p:x-a-b",out bool isAmb)==null && isAmb && amb.Find("p:x-a")?.Taxon=="A","ambiguous code resolves to unknown without throwing");
var bad=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"Bad\",\"tokens\":[\"bad\"],\"regions\":[\"XXX\"]},{\"provider\":\"p\",\"taxon\":\"Wet\",\"tokens\":[\"wet\"],\"water\":\"lava\",\"regions\":[\"GRB\"]},{\"provider\":\"p\",\"taxon\":\"Free\",\"tokens\":[\"free\"]},{\"provider\":\"p\",\"taxon\":\"Ok\",\"tokens\":[\"ok\"],\"regions\":[\"GRB\"]}"),regionsJson,new[]{"p"});
Check(bad.Rules.Count(r=>r.Quarantined)==3 && bad.Problems.Count==3 && !bad.Find("p:ok").Quarantined,"invalid rules are held with a logged problem; valid rules still load");
bool threw=false; try { new RegionalFaunaCatalog(Synthetic(1,""),regionsJson,new[]{"p"}); } catch(ArgumentException) { threw=true; }
Check(threw,"a wrong-version data file is rejected as a whole (EarthFaunaSystem logs an error and stays vanilla)");
string zeros=Convert.ToBase64String(new byte[6390]), ones=Convert.ToBase64String(Enumerable.Repeat((byte)255,6390).ToArray());
string profiled=Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"M\",\"tokens\":[\"m\"],\"mask\":\""+zeros+"\",\"currentMask\":\""+ones+"\"}");
var natural=new RegionalFaunaCatalog(profiled,regionsJson,new[]{"p"});
var currentCat=new RegionalFaunaCatalog(profiled,regionsJson,new[]{"p"},RegionalFaunaCatalog.CurrentProfile);
Check(natural.RangeProfile=="natural" && !natural.Allows("p:x-m",new Site(50,10,all.Geography.IndexOf("GER")),true),"natural profile uses present-natural rasters");
Check(currentCat.RangeProfile=="current" && currentCat.CurrentRangeRules==1 && currentCat.Allows("p:x-m",new Site(50,10,all.Geography.IndexOf("GER")),true),"current profile uses current rasters where present");
var noCurrent=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"M\",\"tokens\":[\"m\"],\"mask\":\""+zeros+"\"}"),regionsJson,new[]{"p"},RegionalFaunaCatalog.CurrentProfile);
Check(noCurrent.RangeProfile=="natural" && noCurrent.Problems.Count==1,"current profile falls back to natural (with a warning) when the catalogue has no current rasters");
// The shipped catalogue carries PHYLACINE current rasters for every raster-based living taxon.
var current=new RegionalFaunaCatalog(rulesJson,regionsJson,file.Providers.Select(p=>p.Id),RegionalFaunaCatalog.CurrentProfile);
Check(current.RangeProfile=="current" && current.Problems.Count==0 && current.CurrentRangeRules==file.Entries.Count(e=>e.Mask!=null) && file.Entries.All(e=>e.Mask==null || e.CurrentMask!=null),
    $"(5) current profile active with a current raster for every present-natural rule ({current.CurrentRangeRules})");
Rule C(string provider,params string[] tokens)=>current.Rules.Single(r=>r.Provider==provider && r.Tokens.SequenceEqual(tokens));
bool Both(Rule n,Rule c,double lat,double lon,bool expectNatural,bool expectCurrent)=>At(n,lat,lon)==expectNatural && RegionalFaunaCatalog.Allows(c,S(lat,lon),true)==expectCurrent;
Check(Both(R("pantherinae","pardus"),C("pantherinae","pardus"),57,-4,true,false) && Both(R("pantherinae","pardus"),C("pantherinae","pardus"),57,25,true,false) && Both(R("pantherinae","pardus"),C("pantherinae","pardus"),-2,36,true,true),"(5) leopard: Scotland/Latvia only in the natural profile; Kenya in both");
Check(Both(R("pantherinae","tigris"),C("pantherinae","tigris"),36,138,true,false) && Both(R("pantherinae","tigris"),C("pantherinae","tigris"),48,135,true,true),"(5) tiger: Japan only in the natural profile; Amur in both");
Check(Both(R("caninae","cuon","alpinus"),C("caninae","cuon","alpinus"),28,-81,true,false),"(5) dhole: Florida only in the natural profile");
Check(Both(R("caninae","canis","latrans"),C("caninae","canis","latrans"),44,-72.7,false,true),"(5) coyote: Vermont only in the current profile (human-assisted expansion)");
Check(Both(R("pantherinae","leo"),C("pantherinae","leo"),39,22,true,false) && Both(R("pantherinae","leo"),C("pantherinae","leo"),21.1,70.8,true,true),"(5) lion: Greece only natural; Gir in both");
Check(RegionalFaunaCatalog.Allows(C("caninae","canis","lupastor"),S(-2,36),true) && !RegionalFaunaCatalog.Allows(C("caninae","canis","lupastor"),S(50,10),true),"(5) split taxa keep their region restriction in the current profile");
Check(current.Rules.Where(r=>r.Mask==null).All(r=>R(r.Provider,r.Tokens).Mask==null),"(5) rules without PHYLACINE rasters are identical in both profiles");
Check(new RegionalFaunaCatalog(profiled,regionsJson,new[]{"p"},"bogus").RangeProfile=="natural","unknown profile falls back to natural");

// ---------------------------------------------------------------- decision 0054: animals as around 1318
var c1318=new RegionalFaunaCatalog(rulesJson,regionsJson,file.Providers.Select(p=>p.Id),RegionalFaunaCatalog.Profile1318);
Check(c1318.RangeProfile=="1318" && c1318.Problems.Count==0,"1318 profile loads without data problems: "+string.Join("; ",c1318.Problems));
Rule Y(string provider,params string[] tokens)=>c1318.Rules.Single(r=>r.Provider==provider && r.Tokens.SequenceEqual(tokens));
bool In1318(Rule r,double lat,double lon)=>RegionalFaunaCatalog.Allows(r,S(lat,lon),true);
Check(file.Entries.Where(e=>e.Lost1318Regions!=null || e.Lost1318Envelopes!=null).All(e=>!e.Extinct && e.Mask!=null && !string.IsNullOrEmpty(e.Evidence1318)),
    "1318 losses apply to living raster taxa only, each with evidence");
Check(At(R("pantherinae","pardus"),37.4,22.1) && !In1318(Y("pantherinae","pardus"),37.4,22.1) && !In1318(Y("pantherinae","pardus"),57,25) && In1318(Y("pantherinae","pardus"),-2,36),
    "1318 leopard: not in the Peloponnese (the playtest leopard) or Latvia; Kenya yes");
Check(!In1318(Y("pantherinae","leo"),39,22) && In1318(Y("pantherinae","leo"),-2,36),"1318 lion: not in Greece; East Africa yes");
Check(!In1318(Y("pantherinae","tigris"),36,138),"1318 tiger: never in Holocene Japan");
Check(!In1318(Y("caninae","cuon","alpinus"),28,-81) && !In1318(Y("caninae","cuon","alpinus"),47,-71),"1318 dhole: not in North America");
Check(!In1318(Y("thylacinidae","sarcophilusharrisii"),-37.5,145) && In1318(Y("thylacinidae","sarcophilusharrisii"),-42,146.5),"1318 Tasmanian devil: Tasmania, not Victoria");
Check(In1318(Y("caninae","canis","lupus"),51,10) && In1318(Y("caninae","canis","lupus"),45,-100),"1318 wolf: still in Germany and on the Great Plains");
Check(c1318.Rules.Where(r=>r.Mask==null || r.Extinct).All(r=>R(r.Provider,r.Tokens) is var n && n.Lost1318Regions==null && n.Lost1318Envelopes==null),
    "rules without a raster (birds, splits by region, extinct taxa) are unchanged by the 1318 profile");
var lost1318=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"M\",\"tokens\":[\"m\"],\"mask\":\""+ones+"\",\"lost1318Regions\":[\"GER\"],\"lost1318Envelopes\":[[0,40,5,45]]}"),regionsJson,new[]{"p"},RegionalFaunaCatalog.Profile1318);
var keep1318=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"M\",\"tokens\":[\"m\"],\"mask\":\""+ones+"\",\"lost1318Regions\":[\"GER\"]}"),regionsJson,new[]{"p"});
Check(!lost1318.Allows("p:x-m",new Site(50,10,all.Geography.IndexOf("GER")),true) && !lost1318.Allows("p:x-m",new Site(42,2,all.Geography.IndexOf("FRA")),true) && lost1318.Allows("p:x-m",new Site(47,2,all.Geography.IndexOf("FRA")),true)
    && keep1318.Allows("p:x-m",new Site(50,10,all.Geography.IndexOf("GER")),true),"1318 losses (regions and boxes) apply in the 1318 profile only");
var bad1318=new RegionalFaunaCatalog(Synthetic(RegionalFaunaCatalog.RulesVersion,"{\"provider\":\"p\",\"taxon\":\"M\",\"tokens\":[\"m\"],\"mask\":\""+ones+"\",\"lost1318Regions\":[\"XXX\"]}"),regionsJson,new[]{"p"},RegionalFaunaCatalog.Profile1318);
Check(bad1318.Rules[0].Quarantined && bad1318.Problems.Count==1,"an unknown 1318 region holds the rule with a logged problem");

// Vanilla animals (fauna-vanilla.json, decisions 0031 and 0054).
string vanillaJson=File.ReadAllText(Path.Combine(root,"assets/earth/config/fauna-vanilla.json"));
var plantRegions=new BotanicalRegions(regionsJson);
var vNatural=new VanillaFaunaRanges(vanillaJson);
var v1318=new VanillaFaunaRanges(vanillaJson,plantRegions);
Check(!vNatural.Uses1318 && v1318.Uses1318 && v1318.Count==vNatural.Count+1,"vanilla ranges load in both modes (butterflies need the region index)");
Check(vNatural.Allows("hyena-spotted-adult-male",48,2) && !v1318.Allows("hyena-spotted-adult-male",48,2) && !v1318.Allows("hyena-spotted-adult-male",22,79)
    && v1318.Allows("hyena-spotted-adult-male",-2,36),"1318 spotted hyena: Africa, not France or India (Pleistocene cave hyenas)");
Check(!v1318.Allows("goat-muskox-adult-male",62,9) && v1318.Allows("goat-muskox-adult-male",72,-110),"1318 muskox: Arctic America, not Norway");
Check(!vNatural.Allows("chicken-hen",52,24) && v1318.Allows("chicken-hen",52,24) && v1318.Allows("chicken-hen",-2,36) && v1318.Allows("chicken-hen",19.6,-155.5)
    && !v1318.Allows("chicken-hen",19.4,-99.1) && !v1318.Allows("chicken-hen",-33,151) && !v1318.Allows("chicken-hen",-41.3,174.8),
    "1318 chickens (feral): Poland, Kenya and Hawaii; not Mexico, Australia or New Zealand");
var vEverywhere=new VanillaFaunaRanges(vanillaJson,plantRegions,chickensEverywhere:true);
Check(vEverywhere.Allows("chicken-hen",19.4,-99.1) && vEverywhere.Allows("chicken-rooster",-33,151) && vEverywhere.Allows("chicken-hen",45,-75)
    && !vEverywhere.Allows("hyena-spotted-adult-male",48,2) && !vEverywhere.Allows("pig-eurasian-adult-male",40,-100) && vEverywhere.Count==v1318.Count
    && !vEverywhere.Allows("chicken-hen",-77.8,166.7) && vEverywhere.Covers("chicken-hen"),
    "Chickens everywhere (0054 stop-gap): chickens in Mexico, Australia and Canada; other animals keep their 1318 ranges");
Check(v1318.Allows("goat-valais-adult-male",46.2,7.5) && !v1318.Allows("goat-valais-adult-male",35,51) && v1318.Allows("goat-sirohi-adult-male",25,73)
    && !v1318.Allows("goat-valais-adult-male",40,-100),"1318 goat breeds: Valais in the Alps, not Iran; Sirohi in Rajasthan; none in the Americas");
Check(v1318.Allows("goat-turdag-adult-male",42.5,46.5) && !vNatural.Allows("goat-turdag-adult-male",30,53),"the tur is the East Caucasian tur, not the wild goat of Iran");
Check(v1318.Allows("pig-eurasian-adult-male",19.6,-155.5) && !vNatural.Allows("pig-eurasian-adult-male",19.6,-155.5) && !v1318.Allows("pig-eurasian-adult-male",40,-100),
    "1318 pigs (feral): Hawaii yes, North America no");
Check(v1318.Allows("wolf-eurasian-adult-male",51,10) && v1318.Allows("bear-polar-adult-male",71.3,-156.5) && v1318.Allows("otheranimal-x",0,0) && !v1318.Covers("otheranimal-x"),
    "vanilla animals without 1318 changes keep their natural ranges; unmapped animals are not limited");
// Ranges proposal, step 2: the old "no vanilla land animals south of 60 S" guard and the altitude constants became data.
var vRegions=new VanillaFaunaRanges(vanillaJson,plantRegions,use1318:false);
Check(!vRegions.Uses1318 && vRegions.Count==v1318.Count && vRegions.Allows("hyena-spotted-adult-male",48,2),"natural ranges with the region index (use1318: false)");
Check(vRegions.Allows("butterfly-x",48,2) && vRegions.Allows("butterfly-x",-2,36) && vRegions.Allows("butterfly-x",-54.3,-36.5) && !vRegions.Allows("butterfly-x",-77.8,166.7)
    && !v1318.Allows("butterfly-x",-62.2,-58.9) && vRegions.Covers("butterfly-x") && vNatural.Allows("butterfly-x",-77.8,166.7) && !vNatural.Covers("butterfly-x"),
    "butterflies: France, Kenya and South Georgia, not Antarctica; without the region index, not limited");
var vanillaKeys=new List<string>();
using(var vdoc=System.Text.Json.JsonDocument.Parse(vanillaJson))
    foreach(var e in vdoc.RootElement.GetProperty("entries").EnumerateArray())
        vanillaKeys.Add(e.GetProperty("family").GetString()+(e.TryGetProperty("type",out var vt)?"-"+vt.GetString():""));
// The constants of EarthWorldGen.AboveItsRange before step 2 (alpine families 0.5 C, the rest 6 C), except goats: they were
// allowed anywhere, and since 7 November need a thawing summer (0 C), so none on ice caps or the highest summits.
var oldAlpine=new HashSet<string>{"goat","sheep","hare","fox","wolf","bear","butterfly"};
double OldMinWarm(string key){string f=key.Split('-')[0]; return f=="goat"?0:oldAlpine.Contains(f)?0.5:6;}
var warmBad=new List<string>();
foreach(var v in new[]{vNatural,v1318,vEverywhere,vRegions}) foreach(var k in vanillaKeys)
{ double a=v.MinWarmMonth(k+"-adult-male"), b=OldMinWarm(k); if(!(double.IsNaN(a)&&double.IsNaN(b) || a==b)) warmBad.Add(k+" "+a+" vs "+b); }
Check(vanillaKeys.Count==48 && warmBad.Count==0,$"altitude limits as data match the old constants for all {vanillaKeys.Count} vanilla types ({string.Join(", ",warmBad.Take(5))})");
long vBefore=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++) v1318.Allows("chicken-hen",52+(i%7),24);
Check(GC.GetAllocatedBytesForCurrentThread()-vBefore==0,"1318 vanilla range checks allocate nothing");

// ---------------------------------------------------------------- allocation and concurrency of the hot path
var kenya=S(-2,36);
RegionalFaunaCatalog.Allows(rhino,kenya,true);
long before=GC.GetAllocatedBytesForCurrentThread();
int hits=0; for(int i=0;i<100000;i++) if(RegionalFaunaCatalog.Allows(all.Rules[i%all.Rules.Count],kenya,true)) hits++;
long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
Check(allocated==0,$"rule evaluation allocates nothing ({allocated} bytes per 100,000 checks)");
var sw=Stopwatch.StartNew();
Parallel.For(0,100000,i=>{if(!RegionalFaunaCatalog.Allows(rhino,kenya,true))throw new Exception("Unsafe concurrent lookup");});
Console.WriteLine($"100,000 parallel rule checks {sw.ElapsedMilliseconds} ms; {all.Rules.Count} taxa, {all.Rules.Count(r=>r.Quarantined)} held, {all.Rules.Count(r=>r.Extinct&&!r.Quarantined)} enabled extinct.");

// ---------------------------------------------------------------- EA-0178: water creatures' height band below their water's surface
{
    const int sea=110, height=384;
    // vanilla's bands: freshwater fish, catfish, perch and trout, coelacanths, groupers, river dolphins (Iniidae), sea turtles
    var bands=new (float min,float max)[]{(.8f,1f),(.7f,.9f),(.9f,1f),(.3f,.6f),(.8f,.95f),(.1f,.99f),(.5f,.98f)};
    bool same=true;
    foreach(var (min,max) in bands) for(int y=1;y<sea;y++)
    {
        double rel=WaterDepthBand.RelativeY(y,sea,height);
        same&=WaterDepthBand.Allows(min,max,y,sea,sea,height)==(min<=rel && rel<=max);
    }
    Check(same,"water at sea level (vanilla's seas and lakes: first block above the water = sea level) gets exactly vanilla's height test");
    Check(Math.Abs(WaterDepthBand.RelativeY(sea+10,sea,height)-(1+10.0/(height-sea)))<1e-12 && WaterDepthBand.RelativeY(55,sea,height)==.5,
        "vanilla's relative height: y/sea at or below the sea, 1 + share of the sky above it");
    // a lake whose surface is 20 blocks above the sea (Lake Pielinen at 1:100): vanilla's own test leaves it no fish at all
    int lake=sea+20;
    Check(WaterDepthBand.RelativeY(lake-1,sea,height)>1 && WaterDepthBand.Allows(.8f,1f,lake-1,lake,sea,height),
        "a freshwater fish just under a lake's surface above the sea: refused by vanilla's test, kept by its depth");
    bool sameDepths=true;
    foreach(var (min,max) in bands) for(int d=1;d<lake;d++)
        sameDepths&=WaterDepthBand.Allows(min,max,lake-d,lake,sea,height)==WaterDepthBand.Allows(min,max,sea-d,sea,sea,height);
    Check(sameDepths,"a lake above the sea has the sea's depth bands, block for block");
    Check(WaterDepthBand.Allows(.8f,1f,lake-21,lake,sea,height) && !WaterDepthBand.Allows(.8f,1f,lake-23,lake,sea,height),
        "the freshwater band ends about 22 blocks below the lake's surface, as below the sea's (0.8 of 110 = 88; vanilla's 0.8f is a hair above)");
    Check(!WaterDepthBand.Allows(.7f,.9f,lake-5,lake,sea,height) && WaterDepthBand.Allows(.7f,.9f,lake-15,lake,sea,height),
        "catfish (0.7..0.9) keep to a lake's deep water: not 5 below its surface, yes 15 below");
    // water below sea level (the Caspian): measured from its own surface too
    Check(WaterDepthBand.Allows(.8f,1f,sea-5,sea-4,sea,height) && !WaterDepthBand.Allows(.8f,1f,sea-30,sea-4,sea,height),
        "water whose surface is below the sea: a fish 1 below it kept, 26 below refused");
}

// ---------------------------------------------------------------- real terrain (the user's world layout)
if(terrain)
{
    sw.Restart();
    var data=EarthData.LoadFromFolder(root,true);
    // Cylinder World 512000 x 204800: jump inset W/10, ring 0.8 W, walls 48 deep; height 384, sea level 165.
    var geo=EarthLayout.Build(new LayoutInput{WorldWidth=512000,CwRingStart=51200,CwRingLength=409600,
        CwEquatorZ=102400,CwPoleDistance=102400-48,CwBandInner=608},data,out _);
    var model=new TerrainModel(data,geo,VerticalScale.ForWorld(384,165),12345){GutterSeabedY=125};
    model.Rivers=new RiverNetwork(data.Rivers,geo,100,1.5,model.RiverWaterTop,"ends",20,geo.RingLength/204800.0,0.5,2);
    Console.WriteLine($"terrain model ready in {sw.ElapsedMilliseconds} ms (gutter {geo.Gutter}, {geo.BlocksPerDegreeX:F1} x {geo.BlocksPerDegreeZ:F1} blocks/degree)");
    Site At2(double lat,double lon,bool coastCheck=true){geo.FromGeo(lat,lon,out double x,out double z);return all.SiteAt(model,x,z,coastCheck);}

    // Geometry round trips through the mod's Cylinder World layout, the wrap and the gutter.
    double worst=0; bool wrapOk=true;
    for(double lat=-85;lat<=85;lat+=5) for(double lon=-179.5;lon<180;lon+=7.25)
    {
        geo.FromGeo(lat,lon,out double x,out double z);
        var g=geo.ToGeo(x,z); var w=geo.ToGeo(x+geo.RingLength,z); var w2=geo.ToGeo(x-3*geo.RingLength,z);
        worst=Math.Max(worst,Math.Max(Math.Abs(g.Lat-lat),Math.Abs(((g.Lon-lon+540)%360)-180)));
        wrapOk&=!g.InGutter && Math.Abs(w.Lon-g.Lon)<1e-6 && Math.Abs(w2.Lon-g.Lon)<1e-6 && w.Lat==g.Lat;
    }
    Check(worst<1e-6,$"FromGeo/ToGeo round trip (worst error {worst:E1} degrees)");
    Check(wrapOk,"positions one or more rings away wrap to the same Earth coordinate");
    var gut=all.SiteAt(model,geo.RingStart+geo.Gutter/4,geo.EquatorZ); var pole=all.SiteAt(model,geo.RingStart+geo.RingLength/2,geo.EquatorZ-95*geo.BlocksPerDegreeZ);
    Check(gut.OutsideEarth && pole.OutsideEarth && !all.Rules.Any(r=>RegionalFaunaCatalog.Allows(r,gut,true) || RegionalFaunaCatalog.Allows(r,pole,true)),"no rule admits the seam gutter or polar filler");
    geo.FromGeo(-38,176,out double kx,out double kz);
    Check(RegionalFaunaCatalog.Allows(R("dinornithidae","apteryxmantelli"),all.SiteAt(model,kx+geo.RingLength,kz),true)==RegionalFaunaCatalog.Allows(R("dinornithidae","apteryxmantelli"),all.SiteAt(model,kx,kz),true),"wrapped seam keeps the same island verdict");

    // Habitat classification on real terrain.
    var ocean=At2(0,-130); Check(ocean.Marine && !ocean.Coastal && !ocean.Freshwater && ocean.Depth>20,"open Pacific: deep marine, not coastal");
    var sahara=At2(24,13); Check(!sahara.Marine && !sahara.Freshwater && !sahara.SalineLake && !sahara.Coastal,"central Sahara: dry inland land");
    Site? FindNear(double lat,double lon,double radiusDeg,Func<Site,bool> want)
    {
        for(double r=0;r<=radiusDeg;r+=.01) for(int a=0;a<16;a++)
        { var s=At2(lat+r*Math.Sin(a*Math.PI/8),lon+r*Math.Cos(a*Math.PI/8)); if(want(s)) return s; if(r==0) break; }
        return null;
    }
    var shore=FindNear(-34.05,18.4,.3,s=>!s.Marine && !s.Freshwater && s.Coastal);
    var shoreSea=FindNear(-34.05,18.4,.3,s=>s.Marine && s.Coastal);
    Check(shore!=null && shoreSea!=null,"Cape Peninsula has coastal land and coastal sea columns");
    Check(FindNear(-34.05,18.4,.3,s=>!s.Marine && !s.Coastal && !s.Freshwater)!=null,"a few km inland is no longer coast");
    var amazon=FindNear(-3.15,-59.9,.4,s=>s.Freshwater && !s.Marine);
    Check(amazon!=null,"Amazon near Manaus has freshwater (river) columns");
    var caspian=At2(42,51);
    Check(caspian.SalineLake && !caspian.Marine,"Caspian Sea is a saline lake, not ocean");
    Check(all.Geography.Code(At2(-70,10).Region)=="ANT" && all.Geography.Code(At2(-78,166).Region)=="ANT","Antarctic terrain resolves to ANT");

    // (1e) Antarctic coast coverage: first land south of 60 S at every degree of longitude (reviewer's benchmark).
    int coasts=0, okA=0, okE=0;
    for(double lon=-179.5;lon<180;lon+=1)
    {
        double px=0,pz=0;
        for(double lat=-60;lat>-86;lat-=.01)
        {
            geo.FromGeo(lat,lon,out double x,out double z);
            if(model.GetColumn(x,z).Water!=TerrainModel.WaterNone){px=x;pz=z;continue;}
            coasts++;
            var land=all.SiteAt(model,x,z); var sea=all.SiteAt(model,px,pz);
            if(RegionalFaunaCatalog.Allows(adelie,land,true)||RegionalFaunaCatalog.Allows(adelie,sea,true)) okA++;
            if(RegionalFaunaCatalog.Allows(emperor,land,true)||RegionalFaunaCatalog.Allows(emperor,sea,true)) okE++;
            break;
        }
    }
    Console.WriteLine($"(1e) Antarctic first-coast samples {coasts}: Adelie eligible {okA}, emperor {okE} (before: 170/360).");
    Check(coasts>=350 && okA>=coasts*.95 && okE>=coasts*.95,$"(1e) Antarctic penguins eligible on >=95% of the sampled Antarctic coast ({okA}/{coasts})");

    // Ranges proposal, step 2: no vanilla land animal is eligible on any land south of 60 S without the old latitude guard,
    // in any mode (natural, 1318, Chickens everywhere): first coast at every degree of longitude and a 0.5 degree grid.
    int southLand=0; var southBad=new HashSet<string>();
    void SouthCheck(double lat,double lon)
    {
        geo.FromGeo(lat,lon,out double x,out double z);
        if(model.GetColumn(x,z).Water!=TerrainModel.WaterNone) return;
        southLand++;
        var g=geo.ToGeo(x,z);
        foreach(var v in new[]{vRegions,v1318,vEverywhere}) foreach(var k in vanillaKeys)
            if(v.Allows(k+"-adult-male",g.Lat,g.Lon)) southBad.Add($"{k} ({(v==vRegions?"natural":v==v1318?"1318":"everywhere")}) at {g.Lat:F2},{g.Lon:F2}");
    }
    for(double lon=-179.5;lon<180;lon+=1)
        for(double lat=-60;lat>-86;lat-=.01)
        { geo.FromGeo(lat,lon,out double x,out double z); if(model.GetColumn(x,z).Water==TerrainModel.WaterNone){SouthCheck(lat,lon);break;} }
    for(double lat=-60.25;lat>-86;lat-=.5) for(double lon=-179.75;lon<180;lon+=.5) SouthCheck(lat,lon);
    Console.WriteLine($"step 2: {southLand} land samples south of 60 S, {southBad.Count} vanilla eligibilities"+(southBad.Count>0?": "+string.Join("; ",southBad.Take(8)):""));
    Check(southLand>10000 && southBad.Count==0,"step 2: no vanilla land animal on any land south of 60 S (ranges, no latitude guard)");
    // North of 60 S the new region-based ranges change nothing: butterflies and Chickens-everywhere chickens (unlimited
    // before step 2) are allowed on every land column of a 1 degree grid.
    int northLand=0; var northBad=new List<string>();
    for(double lat=-59.5;lat<84;lat+=1) for(double lon=-179.5;lon<180;lon+=1)
    {
        geo.FromGeo(lat,lon,out double x,out double z);
        if(model.GetColumn(x,z).Water!=TerrainModel.WaterNone) continue;
        northLand++;
        if(!vRegions.Allows("butterfly-x",lat,lon)||!v1318.Allows("butterfly-x",lat,lon)||!vEverywhere.Allows("chicken-hen",lat,lon)) northBad.Add($"{lat},{lon} {plantRegions.Code(plantRegions.FindLand(lat,lon))}");
    }
    Console.WriteLine($"step 2: {northLand} land samples north of 60 S, {northBad.Count} newly excluded"+(northBad.Count>0?": "+string.Join("; ",northBad.Take(8)):""));
    Check(northLand>10000 && northBad.Count==0,"step 2: north of 60 S butterflies and Chickens-everywhere chickens keep every land column");

    // (1b, 1c) Small-island birds, measured as the reviewer did (0.01 degree grid within 0.3 degrees).
    foreach(var (name,provider,tok,lat,lon,min) in new[]{("Snares penguin (Snares window; no terrain land there)","spheniscidae","eudyptesrobustus",-48.02,166.6,0),("Snares penguin (Stewart Island stand-in)","spheniscidae","eudyptesrobustus",-47.1,167.85,20),("Lord Howe swamphen","dinornithidae","porphyrioalbus",-31.55,159.08,20),("Lord Howe woodhen","dinornithidae","hypotaenidiasylvestris",-31.55,159.08,20)})
    {
        var r=R(provider,tok); int ok=0,n=0;
        for(double a=lat-.3;a<=lat+.3;a+=.01) for(double o=lon-.3;o<=lon+.3;o+=.01){n++; if(RegionalFaunaCatalog.Allows(r,At2(a,o,r.NeedsCoast),true)) ok++;}
        Console.WriteLine($"{name}: eligible {ok}/{n} sampled columns within 0.3 degrees");
        Check(ok>=min,$"(1b/1c) {name} has eligible island columns ({ok}/{n})");
    }

    // FEASIBILITY: every enabled rule has at least one eligible column on real terrain.
    var regionPolys=new Dictionary<string,List<double[]>>(); // code -> polygon bounding boxes [w,s,e,n]
    using(var rd=JsonDocument.Parse(regionsJson))
    foreach(var reg in rd.RootElement.GetProperty("regions").EnumerateArray())
    {
        var list=regionPolys[reg.GetProperty("code").GetString()!]=new List<double[]>();
        foreach(var poly in reg.GetProperty("polygons").EnumerateArray())
        {
            var ring=poly[0].EnumerateArray().Select(p=>(p[0].GetDouble(),p[1].GetDouble())).ToArray();
            list.Add(new[]{ring.Min(p=>p.Item1),ring.Min(p=>p.Item2),ring.Max(p=>p.Item1),ring.Max(p=>p.Item2)});
        }
    }
    var islands=new (double,double)[]{(-48.02,166.6),(-31.55,159.08),(-50.7,166.1),(-52.55,169.15),(-49.68,178.77),(-47.75,179.05),(-43.9,-176.5),(-54.6,158.9),
        (-37.1,-12.3),(-37.8,77.55),(-46.4,51.8),(-49.3,69.5),(-53.1,73.5),(-46.9,37.75),(-54.3,-36.5),(-54.4,3.4),(-51.7,-57.8),(-0.65,-90.35),(10.3,123.9),
        (35,33),(34.0,-120.1),(33.4,-118.4),(40,9),(42.1,9.1),(-42,147),(-40.0,144.0),(-5,120),(-1,121),(-34.05,18.4),(-77.5,166.5),(-65,-64)};
    Site GeoSite(Rule r,double lat,double lon)=>new(lat,lon,all.RegionIndexAt(lat,lon),r.MinElevation.HasValue?r.MinElevation.Value+1:r.MaxElevation.HasValue?Math.Min(0,r.MaxElevation.Value-1):100,0,
        r.Water=="marine",r.Water is "fresh" or "amphibious",false,r.NeedsCoast);
    IEnumerable<(double,double)> Grid(double[] b,double minStep,double maxStep,int target)
    {
        double w=b[0],s=b[1],e=b[2]<b[0]?b[2]+360:b[2],n=b[3];
        double step=Math.Clamp(Math.Max(e-w,n-s)/target,minStep,maxStep);
        for(double la=s+step/2;la<=n;la+=step) for(double lo=w+step/2;lo<=e;lo+=step) yield return (la,((lo+540)%360)-180);
        yield return ((s+n)/2,(((w+e)/2)+540)%360-180);
    }
    int[] waterOffsets={0,16,-16,48,-48,96,-96,192,-192,384,-384};
    var rand=new Random(1); long terrainChecks=0; sw.Restart();
    var infeasible=Infeasible(all);
    Console.WriteLine($"Feasibility: {all.Rules.Count(r=>!r.Quarantined)-infeasible.Count}/{all.Rules.Count(r=>!r.Quarantined)} enabled rules have an eligible column ({terrainChecks} terrain checks, {sw.ElapsedMilliseconds} ms).");
    Check(infeasible.Count==0,"FEASIBILITY: every enabled rule has an eligible land/water column on real terrain; impossible: "+string.Join("; ",infeasible));
    // "current" profile: rules whose PHYLACINE current range is empty or tiny may legitimately have no column (reported, not failed).
    var currentInfeasible=Infeasible(current);
    var unexpected=currentInfeasible.Where(k=>!(current.Rules.First(r=>Key(r)==k.Split(' ')[0]) is var r && r.CurrentMask!=null && Convert.FromBase64String(r.CurrentMask).Sum(b=>System.Numerics.BitOperations.PopCount(b))<=5)).ToList();
    Console.WriteLine($"Feasibility (current profile): {current.Rules.Count(r=>!r.Quarantined)-currentInfeasible.Count}/{current.Rules.Count(r=>!r.Quarantined)}; no column: {string.Join("; ",currentInfeasible)}");
    Check(unexpected.Count==0,"current profile: only rules with an empty or <=5-cell current range lack a column: "+string.Join("; ",unexpected));
    // Decision 0054: the 1318 losses never leave a living taxon without a column (its current range is always kept).
    var infeasible1318=Infeasible(c1318);
    Console.WriteLine($"Feasibility (1318 profile): {c1318.Rules.Count(r=>!r.Quarantined)-infeasible1318.Count}/{c1318.Rules.Count(r=>!r.Quarantined)}");
    Check(infeasible1318.Count==0,"FEASIBILITY (1318): every enabled rule has an eligible column; impossible: "+string.Join("; ",infeasible1318));
    List<string> Infeasible(RegionalFaunaCatalog cat)
    {
    var result=new List<string>();
    foreach(var r in cat.Rules.Where(r=>!r.Quarantined))
    {
        var pts=new List<(double,double)>(islands);
        foreach(var b in r.Envelopes??Array.Empty<double[]>()) pts.AddRange(Grid(b,.01,.5,40));
        foreach(var code in r.Regions??Array.Empty<string>())
            if(code=="ANT") pts.AddRange(Grid(new[]{-180d,-80,179.9,-60},.5,2,80));
            else foreach(var b in regionPolys[code]) pts.AddRange(Grid(b,.01,1,12));
        foreach(var (row,col) in RegionalFaunaCatalog.MaskCells(r)) { var c=RegionalFaunaCatalog.MaskCellCenter(row,col); pts.Add(c); pts.Add((c.Lat+.3,c.Lon+.3)); pts.Add((c.Lat-.3,c.Lon-.3)); }
        var geoOk=pts.Where(p=>RegionalFaunaCatalog.Allows(r,GeoSite(r,p.Item1,p.Item2),true)).OrderBy(_=>rand.Next()).Take(4000).ToArray();
        bool found=false;
        foreach(var (lat,lon) in geoOk)
        {
            geo.FromGeo(lat,lon,out double x,out double z);
            foreach(int dx in r.Water=="land"?waterOffsets.Take(3):waterOffsets) foreach(int dz in r.Water=="land"?new[]{0}:waterOffsets.Take(5))
            {
                terrainChecks++;
                if(RegionalFaunaCatalog.Allows(r,cat.SiteAt(model,x+dx,z+dz,r.NeedsCoast),true)){found=true;break;}
            }
            if(found) break;
        }
        if(!found) result.Add($"{Key(r)} ({r.Taxon}; {geoOk.Length} geographic samples)");
    }
    return result;
    }
    // Island-scale boxes are drawn around the animal's island: most of the box's suitable-habitat columns must be eligible.
    // (Catches region lists or exclusions that contradict the box, e.g. the old Snares/ATP and Lord Howe/NFK rules.)
    foreach(var r in all.Rules.Where(r=>!r.Quarantined && r.Envelopes!=null && r.Envelopes.Sum(b=>((b[2]-b[0]+360)%360)*(b[3]-b[1]))<25))
    {
        var habitat=new Rule{Provider=r.Provider,Taxon=r.Taxon,Tokens=r.Tokens,Water=r.Water,MinElevation=r.MinElevation,MaxElevation=r.MaxElevation,MaxDepth=r.MaxDepth};
        int suitable=0, eligible=0;
        foreach(var b in r.Envelopes)
        {
            double e=b[2]<b[0]?b[2]+360:b[2], step=Math.Max(.005,Math.Sqrt((e-b[0])*(b[3]-b[1])/2500));
            for(double la=b[1];la<=b[3];la+=step) for(double lo=b[0];lo<=e;lo+=step)
            {
                var s=At2(la,((lo+540)%360)-180,r.NeedsCoast);
                if(!RegionalFaunaCatalog.Allows(habitat,s,true)) continue;
                suitable++; if(RegionalFaunaCatalog.Allows(r,s,true)) eligible++;
            }
        }
        if(suitable>0) Check(eligible>=suitable*.5,$"island box of {Key(r)} is usable: {eligible}/{suitable} suitable columns eligible");
    }

    // Site cost per uncached candidate, for candidates near a player (as spawning queries them; terrain caches warm).
    var near=new List<(double,double)>();
    foreach(var (lat,lon) in new[]{(50d,10d),(-2d,36d),(-34.05,18.4),(60d,90d)}) { geo.FromGeo(lat,lon,out double x0,out double z0); for(int i=0;i<5000;i++) near.Add((x0+rand.Next(-200,200),z0+rand.Next(-200,200))); }
    foreach(var (x,z) in near.Take(2000)) all.SiteAt(model,x,z,true);
    sw.Restart(); foreach(var (x,z) in near) all.SiteAt(model,x,z,false); double plain=sw.Elapsed.TotalMilliseconds*1000/near.Count;
    sw.Restart(); foreach(var (x,z) in near) all.SiteAt(model,x,z,true); double withCoast=sw.Elapsed.TotalMilliseconds*1000/near.Count;
    Console.WriteLine($"SiteAt cost near players: {plain:F1} us without coast check, {withCoast:F1} us with coast check (per uncached candidate column).");
    var climateHigh=RegionalFaunaClimate.At(model,At2X(9,39).x,At2X(9,39).z,true); var climateLow=RegionalFaunaClimate.At(model,At2X(9,33).x,At2X(9,33).z,true);
    Check(climateHigh.Temperature<climateLow.Temperature-5,$"ground climate: Ethiopian highland ({climateHigh.Temperature:F1} C) colder than Sudan lowland ({climateLow.Temperature:F1} C)");
    var ice=RegionalFaunaClimate.At(model,At2X(-80,0).x,At2X(-80,0).z,false); var iceExt=RegionalFaunaClimate.At(model,At2X(-80,0).x,At2X(-80,0).z,true);
    Check(ice.Temperature>=-20 && iceExt.Temperature<-20 && ice.Rainfall>=0 && ice.Rainfall<=1,"ExtendTemperature=false clamps to the game's -20..40 C; rain stays 0..1");
    (double x,double z) At2X(double lat,double lon){geo.FromGeo(lat,lon,out double x,out double z);return (x,z);}

    if(coverage) Coverage(model,geo,data);
}

Console.WriteLine(failures.Count==0?$"PASS {count} fauna assertions.":$"FAILED {failures.Count} of {count} fauna assertions.");
return failures.Count==0?0:1;

// Broad audit of local annual climate and geographic eligibility (writes the research CSVs). Not a population test.
void Coverage(TerrainModel model,EarthGeometry geo,EarthData data)
{
    using var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/RegionalFaunaTests/coverage-sites.json")));
    using var raw=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/research/regional-fauna-2026-09-29/provider-climate.json")));
    var limits=new Dictionary<Rule,JsonElement>();
    foreach(var r in all.Rules) limits[r]=raw.RootElement.EnumerateArray().First(t=>t.GetProperty("provider").GetString()==r.Provider && t.GetProperty("tokens").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(r.Tokens)).GetProperty("climate");
    double Get(Rule r,string key,double def)=>limits[r].TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():def;
    int sea=model.SeaLevel, top=384;
    bool Viable(Rule r,double temp,double rain,double y){double yr=y>sea?1+(y-sea)/(top-sea):y/sea;return temp>=Get(r,"minTemp",-100)&&temp<=Get(r,"maxTemp",100)&&rain>=Get(r,"minRain",0)&&rain<=Get(r,"maxRain",1)&&yr>=Get(r,"minY",0)&&yr<=Get(r,"maxY",2);}
    string Quote(string t)=>"\""+t.Replace("\"","\"\"")+"\"";
    var lines=new List<string>{"site,latitude,longitude,habitat,region,range_candidates_extinct_on,range_candidates_extinct_off,worldgen_climate_possible_on,worldgen_climate_possible_off,interpretation,candidates,climate_exclusions,ground_temperature,ground_rainfall"};
    foreach(var p in sites.RootElement.EnumerateArray())
    {
        string name=p[0].GetString()!,water=p[3].GetString()!;double lat=p[1].GetDouble(),lon=p[2].GetDouble();
        geo.FromGeo(lat,lon,out double sx,out double sz);
        var climate=RegionalFaunaClimate.At(model,sx,sz,true);
        var site=new Site(lat,lon,all.RegionIndexAt(lat,lon),Math.Max(0,data.ElevBilinear(lat,lon)),3,water=="marine",water=="fresh",water=="salt",water=="coast");
        var natives=all.Rules.Where(r=>RegionalFaunaCatalog.Allows(r,site,true)).ToArray();
        var viable=natives.Where(r=>Viable(r,climate.Temperature,climate.Rainfall,water=="marine"?sea-4:climate.SurfaceY)).ToArray();
        string category=natives.Length==0?"content/range-data gap; vanilla background unchanged":viable.Length==0?"environmental constraint risk":"range candidates available; provider vegetation/population/collision still apply";
        lines.Add(string.Join(",",Quote(name),lat.ToString(inv),lon.ToString(inv),water,all.Geography.Code(site.Region),natives.Length,natives.Count(r=>!r.Extinct),viable.Length,viable.Count(r=>!r.Extinct),Quote(category),
            Quote(string.Join(";",natives.Select(r=>r.Taxon))),Quote(string.Join(";",natives.Except(viable).Select(r=>r.Taxon))),climate.Temperature.ToString("F2",inv),climate.Rainfall.ToString("F3",inv)));
    }
    File.WriteAllLines(Path.Combine(root,"docs/research/regional-fauna-2026-09-29/coverage.csv"),lines);
    var stats=all.Rules.ToDictionary(r=>r,r=>new double[]{0,0,100,-100,1,0});
    void Scan(double lat,double lon)
    {
        geo.FromGeo(lat,lon,out double sx,out double sz);
        var site=all.SiteAt(model,sx,sz,true);
        var ground=RegionalFaunaClimate.At(model,sx,sz,true);
        foreach(var r in all.Rules)
        {
            if(!RegionalFaunaCatalog.Allows(r,site,true))continue;
            var s=stats[r];s[0]++;s[2]=Math.Min(s[2],ground.Temperature);s[3]=Math.Max(s[3],ground.Temperature);s[4]=Math.Min(s[4],ground.Rainfall);s[5]=Math.Max(s[5],ground.Rainfall);
            if(Viable(r,ground.Temperature,ground.Rainfall,site.Marine?sea-4:ground.SurfaceY+1))s[1]++;
        }
    }
    for(double lat=-79;lat<81;lat+=2)for(double lon=-179;lon<180;lon+=2)Scan(lat,lon);
    foreach(var b in new[]{new[]{165d,-48d,179d,-34d},new[]{144d,-44d,149d,-40d},new[]{32d,34d,35d,36d},new[]{8d,39d,10d,41d}})
        for(double lat=b[1];lat<=b[3];lat+=.25)for(double lon=b[0];lon<=b[2];lon+=.25)Scan(lat,lon);
    var nativeLines=new List<string>{"provider,taxon,extinct,held,geographic_samples,worldgen_climate_possible_samples,min_ground_temperature,max_ground_temperature,min_ground_rain,max_ground_rain"};
    foreach(var r in all.Rules){var s=stats[r];nativeLines.Add(string.Join(",",r.Provider,Quote(r.Taxon),r.Extinct,r.Quarantined,string.Join(",",s.Select(v=>v.ToString(inv)))));}
    File.WriteAllLines(Path.Combine(root,"docs/research/regional-fauna-2026-09-29/native-climate-scan.csv"),nativeLines);
    Console.WriteLine($"Coverage: wrote {lines.Count-1} scenario probes and {nativeLines.Count-1} per-taxon terrain scans (real terrain habitat incl. coast). Not a population test.");
}
