using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace EarthMap.Core
{
    /// <summary>
    /// Offline natural-range fauna eligibility, independent of the game and content providers.
    /// Immutable after construction; every query is thread-safe. The per-candidate check (the static <see cref="Allows(Rule, Site, bool)"/>
    /// on a resolved rule) does not allocate; resolving a code (<see cref="Find(string)"/>, <see cref="Controls"/>) does.
    /// </summary>
    public sealed class RegionalFaunaCatalog
    {
        /// <summary>2: corrected ranges (golden wolf, Snares/Lord Howe birds, aurochs, Antarctic latitude rule, Steller's sea cow),
        /// newly mapped taxa, generic carcass groups and the range-profile switch. 3: the "1318" profile (decision 0054): regions
        /// each living taxon had lost by about 1318. 4: one region index for every catalogue (BotanicalRegions); land and coastal
        /// sites the polygons miss take the nearest region, which replaces "south of 60°S is ANT" (ranges proposal, step 1).
        /// Rules only gate future natural spawns.</summary>
        public const int RulesVersion = 4;
        /// <summary>PHYLACINE "present-natural": the estimated range without human impact (default).</summary>
        public const string NaturalProfile = "natural";
        /// <summary>PHYLACINE "current": today's range, used where the catalogue carries it; other rules are unchanged.</summary>
        public const string CurrentProfile = "current";
        /// <summary>Decision 0054: the present-natural range minus the regions the taxon had lost (or never reached in the Holocene)
        /// by about 1318, never removing its current (PHYLACINE current) range: where it lives natively today, it lived then.
        /// The default of worlds placed as around 1318.</summary>
        public const string Profile1318 = "1318";
        /// <summary>Bytes of a PHYLACINE range raster: 142 rows of 360 cells, a bit each (also fauna-vanilla.json's).</summary>
        public const int MaskBytes = 142 * 360 / 8;

        public sealed class Provider { public string Id { get; set; } public string Version { get; set; } }
        public sealed class Rule
        {
            public string Provider { get; set; }
            public string Taxon { get; set; }
            public string[] Tokens { get; set; }
            public bool Extinct { get; set; }
            public bool Quarantined { get; set; }
            public string Note { get; set; }
            public string Evidence { get; set; }
            /// <summary>For extinct taxa: the period the range represents.</summary>
            public string Period { get; set; }
            public string Water { get; set; } = "land";
            public string[] Regions { get; set; }
            public string[] ExcludedRegions { get; set; }
            public double[][] Envelopes { get; set; }
            /// <summary>PHYLACINE present-natural occupancy bits.</summary>
            public string Mask { get; set; }
            /// <summary>PHYLACINE current occupancy bits (optional; used by the "current" profile).</summary>
            public string CurrentMask { get; set; }
            /// <summary>Regions (TDWG level 3) the taxon no longer lived in around 1318; only the "1318" profile reads them.</summary>
            public string[] Lost1318Regions { get; set; }
            /// <summary>Boxes [west, south, east, north] it no longer lived in around 1318 (parts of regions); "1318" profile only.</summary>
            public double[][] Lost1318Envelopes { get; set; }
            /// <summary>Why, with dates and sources (decision 0054).</summary>
            public string Evidence1318 { get; set; }
            public double? MinElevation { get; set; }
            public double? MaxElevation { get; set; }
            public double? MaxDepth { get; set; }
            [System.Text.Json.Serialization.JsonIgnore] internal byte[] Bits;
            /// <summary><see cref="Regions"/>, <see cref="ExcludedRegions"/> and <see cref="Envelopes"/> as read (null when absent).</summary>
            [System.Text.Json.Serialization.JsonIgnore] internal RegionSet Range, Excluded;
            [System.Text.Json.Serialization.JsonIgnore] internal GeoBox[] Boxes;
            /// <summary>The profile's exclusions (null outside the "1318" profile).</summary>
            [System.Text.Json.Serialization.JsonIgnore] internal RegionSet LostRegions;
            [System.Text.Json.Serialization.JsonIgnore] internal GeoBox[] LostBoxes;
            /// <summary>Current range, which the 1318 exclusions never remove (null outside the "1318" profile).</summary>
            [System.Text.Json.Serialization.JsonIgnore] internal byte[] KeptBits;
            /// <summary>Extinct, but still alive somewhere in 1318 (placement catalogue, decision 0047): spawns in worlds
            /// placed as around 1318 without the extinct-animals option. Set by the game adapter, not read from the file.</summary>
            [System.Text.Json.Serialization.JsonIgnore] public bool AliveIn1318 { get; set; }
            [System.Text.Json.Serialization.JsonIgnore] public bool NeedsCoast => Water == "coast" || Water == "amphibious";
        }
        public sealed class RuleFile { public int Version { get; set; } public Provider[] Providers { get; set; } public Rule[] Entries { get; set; } }
        public readonly struct Site
        {
            public readonly double Lat, Lon, Elevation, Depth;
            public readonly bool Marine, Freshwater, SalineLake, Coastal, OutsideEarth;
            readonly int regionPlusOne;
            /// <summary>Region index of the catalogue's <see cref="Geography"/>, -1 for none (also a default site's).</summary>
            public int Region => regionPlusOne - 1;
            public Site(double lat, double lon, int region, double elevation = 0, double depth = 0,
                bool marine = false, bool freshwater = false, bool salineLake = false, bool coastal = false, bool outsideEarth = false)
            { Lat=lat; Lon=lon; regionPlusOne=Math.Max(region,-1)+1; Elevation=elevation; Depth=depth; Marine=marine; Freshwater=freshwater; SalineLake=salineLake; Coastal=coastal; OutsideEarth=outsideEarth; }
            public string Habitat => OutsideEarth ? "outside the Earth" : Marine ? "marine" : Freshwater ? "freshwater" : SalineLake ? "saline lake" : "land";
        }

        public BotanicalRegions Geography { get; }
        public IReadOnlyList<Rule> Rules { get; }
        public IReadOnlyList<Provider> SupportedProviders { get; }
        /// <summary>The profile in effect: "natural" unless "current" (and current ranges exist) or "1318" was requested.</summary>
        public string RangeProfile { get; }
        /// <summary>Number of rules whose range comes from a PHYLACINE current raster in this profile.</summary>
        public int CurrentRangeRules { get; }
        /// <summary>Data problems found while loading; each affected rule is held, never thrown for.</summary>
        public IReadOnlyList<string> Problems { get; }
        readonly Dictionary<string, Rule[]> byProvider;

        /// <summary>Supported provider ids, read without building the catalogue (cheap check for "nothing installed").</summary>
        public static string[] ProviderIds(string rulesJson)
        {
            using var doc = JsonDocument.Parse(rulesJson);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (string.Equals(p.Name, "providers", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value.EnumerateArray().Select(v => v.TryGetProperty("id", out var id) ? id.GetString() : null).Where(v => v != null).ToArray();
            return Array.Empty<string>();
        }

        /// <summary>Throws only for an unreadable or wrong-version file; individual bad rules are held and listed in <see cref="Problems"/>.</summary>
        public RegionalFaunaCatalog(string rulesJson, string regionsJson, IEnumerable<string> installedProviders, string rangeProfile = NaturalProfile)
        {
            var file=JsonSerializer.Deserialize<RuleFile>(rulesJson,new JsonSerializerOptions {PropertyNameCaseInsensitive=true});
            if(file?.Version!=RulesVersion || file.Entries==null || file.Providers==null)
                throw new ArgumentException($"Unsupported regional fauna data version {file?.Version} (expected {RulesVersion})");
            Geography=BotanicalRegions.Shared(regionsJson);
            SupportedProviders=file.Providers;
            var problems=new List<string>();
            string profile=string.IsNullOrWhiteSpace(rangeProfile) ? NaturalProfile : rangeProfile.Trim().ToLowerInvariant();
            if(profile!=NaturalProfile && profile!=CurrentProfile && profile!=Profile1318)
            { problems.Add($"Unknown fauna range profile '{rangeProfile}'; using '{NaturalProfile}'."); profile=NaturalProfile; }
            if(profile==CurrentProfile && !file.Entries.Any(r=>r?.CurrentMask!=null))
            { problems.Add("Current-range profile requested, but this catalogue was built without PHYLACINE current rasters; using natural ranges."); profile=NaturalProfile; }
            RangeProfile=profile;
            var installed=new HashSet<string>(installedProviders ?? Array.Empty<string>(),StringComparer.Ordinal);
            var rules=new List<Rule>();
            int current=0;
            foreach(var r in file.Entries)
            {
                if(r==null || !installed.Contains(r.Provider ?? "")) continue;
                if(string.IsNullOrEmpty(r.Taxon) || r.Tokens==null || r.Tokens.Length==0 || r.Tokens.Any(string.IsNullOrEmpty))
                { problems.Add($"{r.Provider}: rule without taxon/tokens dropped."); continue; }
                rules.Add(r);
                string bad=Validate(r,profile,ref current);
                if(bad==null) continue;
                problems.Add($"{r.Provider}/{r.Taxon}: {bad}; held from natural spawning.");
                r.Quarantined=true; r.Bits=null;
                r.Note=(r.Note==null ? "" : r.Note+" ")+"Data problem: "+bad+".";
            }
            Rules=rules.ToArray();
            CurrentRangeRules=current;
            Problems=problems.ToArray();
            byProvider=Rules.GroupBy(r=>r.Provider).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        }

        string Validate(Rule r, string profile, ref int current)
        {
            if(r.Water!="land" && r.Water!="coast" && r.Water!="marine" && r.Water!="fresh" && r.Water!="amphibious") return "invalid water rule '"+r.Water+"'";
            if(r.Regions!=null) foreach(var c in r.Regions) if(!Geography.ContainsCode(c)) return "unknown region "+c;
            if(r.ExcludedRegions!=null) foreach(var c in r.ExcludedRegions) if(!Geography.ContainsCode(c)) return "unknown excluded region "+c;
            if(!GeoBox.TryFrom(r.Envelopes,out var boxes)) return "invalid envelope";
            if(r.Lost1318Regions!=null) foreach(var c in r.Lost1318Regions) if(!Geography.ContainsCode(c)) return "unknown 1318 lost region "+c;
            if(!GeoBox.TryFrom(r.Lost1318Envelopes,out var lostBoxes)) return "invalid 1318 lost envelope";
            // (every code is known by now)
            r.Range=r.Regions==null ? null : Geography.Set(r.Regions);
            r.Excluded=r.ExcludedRegions==null ? null : Geography.Set(r.ExcludedRegions);
            r.Boxes=boxes;
            if(profile==Profile1318)
            {
                r.LostRegions=r.Lost1318Regions is { Length: > 0 } ? Geography.Set(r.Lost1318Regions) : null;
                r.LostBoxes=lostBoxes is { Length: > 0 } ? lostBoxes : null;
                if((r.LostRegions!=null || r.LostBoxes!=null) && r.CurrentMask!=null)
                {
                    try { r.KeptBits=Convert.FromBase64String(r.CurrentMask); } catch(FormatException) { return "unreadable current range raster"; }
                    if(r.KeptBits.Length!=MaskBytes) return "invalid current range raster size";
                }
            }
            string text=profile==CurrentProfile && r.CurrentMask!=null ? r.CurrentMask : r.Mask;
            if(text!=null)
            {
                byte[] bits;
                try { bits=Convert.FromBase64String(text); } catch(FormatException) { return "unreadable range raster"; }
                if(bits.Length!=MaskBytes) return "invalid range raster size";
                r.Bits=bits;
                if(!ReferenceEquals(text,r.Mask)) current++;
            }
            if(!r.Quarantined && r.Bits==null && (r.Regions==null || r.Regions.Length==0) && (r.Envelopes==null || r.Envelopes.Length==0))
                return "unbounded range";
            return null;
        }

        public bool Controls(string code) { int i=code?.IndexOf(':')??-1; return i>=0 && byProvider.ContainsKey(code.Substring(0,i)); }
        /// <summary>The unique rule whose token sequence occurs in the code; null when none or ambiguous (never throws).</summary>
        public Rule Find(string code) => Find(code,out _);
        public Rule Find(string code, out bool ambiguous)
        {
            ambiguous=false;
            int i=code?.IndexOf(':')??-1;
            if(i<0 || !byProvider.TryGetValue(code.Substring(0,i),out var entries)) return null;
            var tokens=code.Substring(i+1).Split('-');
            Rule found=null;
            foreach(var r in entries) if(ContainsSequence(tokens,r.Tokens))
            {
                if(found!=null) { ambiguous=true; return null; }
                found=r;
            }
            return found;
        }
        static bool ContainsSequence(string[] haystack,string[] needle)
        {
            // Repeated genus/species names (Vulpes vulpes) require two adjacent tokens, not membership twice.
            for(int i=0;i<=haystack.Length-needle.Length;i++)
            { int j=0;while(j<needle.Length && haystack[i+j]==needle[j])j++;if(j==needle.Length)return true; }
            return false;
        }

        /// <summary>"dom:family-genus-carcass" → "dom:family-genus"; null unless the code is a generic (non-species) carcass.</summary>
        public static string GenericCarcassPrefix(string code)
        {
            const string suffix="-carcass";
            if(code==null || !code.EndsWith(suffix,StringComparison.Ordinal)) return null;
            int colon=code.IndexOf(':');
            return colon<0 || code.Length-suffix.Length<=colon+1 ? null : code.Substring(0,code.Length-suffix.Length);
        }
        /// <summary>
        /// Enabled species rules of a generic carcass's group: every other loaded code of the same provider that starts with the
        /// carcass's group prefix and maps to a rule. The carcass may appear wherever any of them may.
        /// </summary>
        public Rule[] GroupRules(string carcassCode, IEnumerable<string> loadedCodes)
        {
            string prefix=GenericCarcassPrefix(carcassCode);
            if(prefix==null || Find(carcassCode)!=null) return null;
            prefix+="-";
            var list=new List<Rule>();
            foreach(var code in loadedCodes)
            {
                if(code==null || code==carcassCode || !code.StartsWith(prefix,StringComparison.Ordinal)) continue;
                var r=Find(code);
                if(r!=null && !r.Quarantined && !list.Contains(r)) list.Add(r);
            }
            return list.ToArray();
        }

        /// <summary>
        /// TDWG region at a coordinate. Land and coastal sites (land or a sea/land edge within 24 blocks) use the land lookup,
        /// so every land column of the terrain has a region even where the polygons miss its coast (half of Antarctica's,
        /// which until rules v4 was patched with "south of 60°S is ANT"); open sea keeps the polygons' coast tolerance.
        /// </summary>
        public string RegionAt(double lat, double lon, bool landOrCoast = true) => Geography.Code(RegionIndexAt(lat, lon, landOrCoast));

        /// <summary><see cref="RegionAt"/> as a region index of <see cref="Geography"/> (-1: none), as a <see cref="Site"/> holds it.</summary>
        public int RegionIndexAt(double lat, double lon, bool landOrCoast = true)
            => Geography.At(lat, lon, landOrCoast ? BotanicalRegions.Surface.Land : BotanicalRegions.Surface.Sea);

        static readonly int[] CoastDX={-8,8,0,0,-24,24,0,0}, CoastDZ={0,0,-8,8,0,0,-24,24};
        /// <summary>
        /// Habitat of a world column on the Earth terrain model: region, elevation, water class and, when asked, whether a
        /// sea/land edge is within 8 or 24 blocks (blocks, so a large coastal country is not all "coast").
        /// </summary>
        public Site SiteAt(TerrainModel model, double x, double z, bool checkCoast = true)
        {
            var g=model.Geometry.ToGeo(x,z);
            if(g.InGutter || g.BeyondPole) return new Site(g.Lat,g.Lon,-1,outsideEarth:true);
            var c=model.GetColumn(x,z);
            bool marine=c.Water==TerrainModel.WaterSea, fresh=c.River || c.Water==TerrainModel.WaterLake, saline=c.Water==TerrainModel.WaterSaltLake;
            bool coast=false;
            if(checkCoast) for(int i=0;i<CoastDX.Length && !coast;i++)
            {
                var n=model.GetColumn(x+CoastDX[i],z+CoastDZ[i]);
                coast=marine ? n.Water==TerrainModel.WaterNone && !n.Gutter : n.Water==TerrainModel.WaterSea;
            }
            return new Site(g.Lat,g.Lon,RegionIndexAt(g.Lat,g.Lon,!marine || coast),c.Elev,Math.Max(0,c.WaterTop-c.Top),marine,fresh,saline,coast,false);
        }

        public bool Allows(string code, Site site, bool includeExtinct) => !Controls(code) || Allows(Find(code),site,includeExtinct);
        /// <summary>Held or (with extinct animals off) extinct: rejected without looking at the site.</summary>
        public static bool Blocked(Rule r, bool includeExtinct) => r==null || r.Quarantined || (!includeExtinct && r.Extinct && !r.AliveIn1318);
        public static bool Allows(Rule r, Site s, bool includeExtinct)
        {
            if(Blocked(r,includeExtinct) || s.OutsideEarth || !double.IsFinite(s.Lat) || !double.IsFinite(s.Lon) || s.Lat < -90 || s.Lat > 90) return false;
            if(r.Range!=null && !r.Range.Contains(s.Region)) return false;
            if(r.Excluded!=null && r.Excluded.Contains(s.Region)) return false;
            if(r.Boxes!=null && !GeoBox.Any(r.Boxes,s.Lat,s.Lon)) return false;
            if(r.Bits!=null && !InMask(r.Bits,s.Lat,s.Lon)) return false;
            if((r.LostRegions!=null && r.LostRegions.Contains(s.Region) || r.LostBoxes!=null && GeoBox.Any(r.LostBoxes,s.Lat,s.Lon))
                && (r.KeptBits==null || !InMask(r.KeptBits,s.Lat,s.Lon))) return false;
            if(s.Elevation<r.MinElevation || s.Elevation>r.MaxElevation || s.Depth>r.MaxDepth) return false;
            return r.Water switch
            {
                "land" => !s.Marine && !s.SalineLake && !s.Freshwater,
                "fresh" => s.Freshwater && !s.Marine && !s.SalineLake,
                "marine" => s.Marine && !s.SalineLake,
                "coast" => s.Coastal && !s.SalineLake,
                "amphibious" => !s.SalineLake && (s.Freshwater || s.Marine && s.Coastal),
                _ => false
            };
        }
        /// <summary>Generic carcass: allowed where any member of its group is.</summary>
        public static bool AllowsAny(Rule[] group, Site s, bool includeExtinct)
        {
            if(group==null) return false;
            foreach(var r in group) if(Allows(r,s,includeExtinct)) return true;
            return false;
        }
        // PHYLACINE 1.2.1's original 96 km Behrmann equal-area WGS84 grid, nearest cell; no interpolated occupancy.
        public static bool InMask(byte[] bits,double lat,double lon)
        {
            const double a=6378137, e2=0.0066943799901413165;
            double e=Math.Sqrt(e2), phi=lat*Math.PI/180, sin=Math.Sin(phi), k=Math.Cos(Math.PI/6)/Math.Sqrt(1-e2*.25);
            double q=(1-e2)*(sin/(1-e2*sin*sin)-Math.Log((1-e*sin)/(1+e*sin))/(2*e));
            double x=a*k*lon*Math.PI/180, y=a*q/(2*k);
            int col=(int)Math.Floor((x+17367529)/96486.27222222222), row=(int)Math.Floor((7348382-y)/96514.95774647887);
            if(col<0 || col>=360 || row<0 || row>=142) return false;
            int n=row*360+col;
            return (bits[n>>3] & (1<<(n&7)))!=0;
        }
        /// <summary>Centre of a PHYLACINE grid cell (row, col) in degrees; used by audits that sample every occupied cell.</summary>
        public static (double Lat, double Lon) MaskCellCenter(int row, int col)
        {
            const double a=6378137, e2=0.0066943799901413165;
            double e=Math.Sqrt(e2), k=Math.Cos(Math.PI/6)/Math.Sqrt(1-e2*.25);
            double x=-17367529+(col+.5)*96486.27222222222, y=7348382-(row+.5)*96514.95774647887;
            double lon=x/(a*k)*180/Math.PI, q=2*k*y/a;
            // Invert the authalic latitude iteratively (converges in a few steps away from the poles).
            double phi=Math.Asin(Math.Clamp(q/2,-1,1));
            for(int i=0;i<20;i++)
            {
                double sin=Math.Sin(phi), d=1-e2*sin*sin;
                double qp=(1-e2)*(sin/d-Math.Log((1-e*sin)/(1+e*sin))/(2*e));
                phi+=d*d/(2*Math.Cos(phi))*(q-qp)/(1-e2);
            }
            return (phi*180/Math.PI, lon);
        }
        /// <summary>Occupied cells of a rule's active range raster (empty when it has none).</summary>
        public static IEnumerable<(int Row, int Col)> MaskCells(Rule r)
        {
            if(r?.Bits==null) yield break;
            for(int n=0;n<142*360;n++) if((r.Bits[n>>3] & (1<<(n&7)))!=0) yield return (n/360, n%360);
        }
    }
}
