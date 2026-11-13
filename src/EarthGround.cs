using System;
using System.Collections.Generic;
using System.Linq;
using EarthMap.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace EarthMap
{
    /// <summary>
    /// Game side of the ground-columns prototype (<see cref="GroundColumns"/>): resolves the rules' materials to blocks and
    /// rewrites the natural ground of a column from its top down, after vanilla's soil layers and Earth's strata. Air, water,
    /// ice and anything that is not natural ground (caves, structures, glacier ice) stay as they are. Owned by EarthGeology.
    /// </summary>
    sealed class EarthGround
    {
        public readonly GroundColumns Model;
        readonly int[] materialBlock;
        readonly int[,] soil;          // [fertility, grass cover] -> soil block
        readonly sbyte[] grassOf;      // soil block -> its grass cover index, -1 for anything else
        readonly bool[] ground;        // natural ground this pass may rewrite
        readonly bool[] wavy,sand;     // vanilla's rippled sea-floor sand; plain sand blocks
        /// <summary>0060 (EarthParams.OreLayers): strata regional geology keeps (vanilla's bauxite, kimberlite, suevite) that
        /// the column rewrites anyway: in any of its layers (1: bauxite), or only in its soil and cover (2: kimberlite, suevite).</summary>
        byte[] preserved;
        public string Summary { get; }
        [ThreadStatic] static GroundColumns.Column column;
        static readonly string[] Grass={"none","verysparse","sparse","normal"};

        EarthGround(GroundColumns model,int[] materialBlock,int[,] soil,sbyte[] grassOf,bool[] ground,bool[] wavy,bool[] sand,string summary)
        {
            Model=model;this.materialBlock=materialBlock;this.soil=soil;this.grassOf=grassOf;this.ground=ground;this.wavy=wavy;this.sand=sand;Summary=summary;
        }

        /// <summary>VintagestoryData/ModData/earth: the 30″ ground sets are in its ground folder (Core.GroundDownloader).</summary>
        public static string DataFolder=>System.IO.Path.Combine(GamePaths.DataPath,"ModData","earth");

        /// <summary>Loads the rules and the tiles they list and resolves blocks; null (with <paramref name="error"/>) when anything is missing.</summary>
        /// <param name="dataFolderSets">Whether this world reads the 30″ sets in <see cref="DataFolder"/> (EarthParams.GroundData).</param>
        public static EarthGround TryCreate(ICoreServerAPI api,GroundColumns.DepthCurve curve,long seed,System.Func<int,bool> replaceableRock,bool dataFolderSets,out string error)
        {
            error=null;
            string rules=api.Assets.TryGet(new AssetLocation(GroundColumns.RulesAsset))?.ToText();
            if(rules==null){error=GroundColumns.RulesAsset+" is missing";return null;}
            GroundColumns model;
            // 30" sets are too big for Earth's archives: optional files in VintagestoryData/ModData/earth (e.g. ground/*.egts);
            // a world made with the 2' sets only never reads them
            // the 2' sets: the mod's own asset if it carries one (an Earth 1.0 ground pack still installed adds them as assets),
            // else the server's data store (decision 0066)
            try{model=GroundColumns.Load(rules,asset=>api.Assets.TryGet(new AssetLocation(asset))?.Data
                    ??EarthDataStore.Bytes(DataFolder,System.IO.Path.GetFileName(new AssetLocation(asset).Path)),curve,seed,
                file=>dataFolderSets?System.IO.Path.Combine(DataFolder,file):null);}
            catch(Exception e) when (e is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.IO.InvalidDataException or System.IO.FileNotFoundException)
            {error=e is System.IO.FileNotFoundException
                ?"a ground set is missing from the mod and from "+EarthDataStore.Folder(DataFolder)+": "+e.Message
                :"ground data is invalid: "+e.Message;return null;}
            // a name in the data the rules do not know: the rules' default is used; reported once so the rules can be extended
            model.UnknownName+=name=>api.Logger.Warning("Earth ground columns: no rule for {0}; the default is used.",name);

            int Id(string code){try{return api.World.GetBlock(new AssetLocation(code))?.Id??0;}catch(Exception){return 0;}}
            var soil=new int[GroundColumns.Fertility.Length,Grass.Length];
            for(int f=0;f<GroundColumns.Fertility.Length;f++)for(int g=0;g<Grass.Length;g++)
            {
                soil[f,g]=Id("game:soil-"+GroundColumns.FertilityBlockCodes[f]+"-"+Grass[g]);
                if(soil[f,g]<=0){error="game:soil-"+GroundColumns.FertilityBlockCodes[f]+"-"+Grass[g]+" does not exist";return null;}
            }
            var blocks=new int[model.Materials.Length];
            var chosen=new List<string>();
            for(int i=0;i<blocks.Length;i++)
            {
                if(i==model.Topsoil || model.Candidates[i].Any(c=>c.Contains('{')))continue; // placeholders: the soil table
                string code=model.Candidates[i].FirstOrDefault(c=>Id(c)>0);
                if(code==null){error="no block exists for ground material '"+model.Materials[i]+"' ("+string.Join(", ",model.Candidates[i])+")";return null;}
                blocks[i]=Id(code);
                if(code!=model.Candidates[i][0])chosen.Add(model.Materials[i]+"→"+code);
            }

            int count=api.World.Blocks.Count;
            var grassOf=new sbyte[count];var natural=new bool[count];var wavy=new bool[count];var plainSand=new bool[count];
            Array.Fill(grassOf,(sbyte)-1);
            foreach(var block in api.World.Blocks)
            {
                if(block?.Code==null || block.Id<=0 || block.Id>=count)continue;
                string path=block.Code.Path,domain=block.Code.Domain;
                bool rock=path.StartsWith("rock-",StringComparison.Ordinal);
                wavy[block.Id]=path.StartsWith("sandwavy-",StringComparison.Ordinal);
                plainSand[block.Id]=path.StartsWith("sand-",StringComparison.Ordinal);
                natural[block.Id]=(rock && replaceableRock(block.Id))
                    || path.StartsWith("soil-",StringComparison.Ordinal) || path.StartsWith("sand-",StringComparison.Ordinal)
                    || path.StartsWith("gravel-",StringComparison.Ordinal) || path.StartsWith("rawclay-",StringComparison.Ordinal)
                    || path.StartsWith("peat-",StringComparison.Ordinal) || path.StartsWith("muddygravel",StringComparison.Ordinal) || wavy[block.Id]
                    || (domain=="earth" && (path.StartsWith("till-",StringComparison.Ordinal) || path.StartsWith("silt",StringComparison.Ordinal) || path.StartsWith("marl-",StringComparison.Ordinal)));
                if(path.StartsWith("soil-",StringComparison.Ordinal))
                {
                    int g=Array.IndexOf(Grass,path.Substring(path.LastIndexOf('-')+1));
                    if(g>=0)grassOf[block.Id]=(sbyte)g;
                }
            }
            foreach(int id in blocks)if(id>0 && id<count)natural[id]=true;
            string summary=string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ground columns v{0} over {1:0.#}-{2:0.#} E, {3:0.#}-{4:0.#} N from {5} tiles ({6}); depth {7}, 1000 m at block {8:0}, layers from {9} m keep a block; rules {10}; block fallbacks: {11}",
                GroundColumns.RulesVersion,model.ExtentWest,model.ExtentEast,model.ExtentSouth,model.ExtentNorth,model.Data.Tiles.Count,
                string.Join(", ",model.Data.Tiles.Select(t=>t.Tile.Name.Split(' ')[0]).Distinct()),model.Curve,model.Curve.Blocks(1000),model.MinLayerMetres,model.Fingerprint,
                chosen.Count==0?"none":string.Join(", ",chosen))
                +(model.MissingTiles.Count==0?"":(dataFolderSets?"; not installed (coarser data used there): ":"; not read in this world (made with the 2' sets): ")+string.Join(", ",model.MissingTiles));
            return new EarthGround(model,blocks,soil,grassOf,natural,wavy,plainSand,summary);
        }

        public bool Covers(double lat,double lon)=>Model.Covers(lat,lon);

        /// <summary>0060: rock-bauxite is rewritten in any layer of the column, rock-kimberlite and rock-suevite in its soil and
        /// cover only (bedrock keeps them). Worlds without EarthParams.OreLayers never call this.</summary>
        public void RewritePreservedStrata(ICoreServerAPI api)
        {
            var p=new byte[ground.Length];
            foreach(var block in api.World.Blocks)
            {
                if(block?.Code==null || block.Id<=0 || block.Id>=p.Length)continue;
                string path=block.Code.Path;
                if(path=="rock-bauxite")p[block.Id]=1;
                else if(path is "rock-kimberlite" or "rock-suevite")p[block.Id]=2;
            }
            preserved=p;
        }

        /// <summary>The block of ground material <paramref name="m"/>; 0 for the topsoil (it depends on the place) and unknown materials.</summary>
        public int MaterialBlock(int m)=>m>=0 && m<materialBlock.Length && m!=Model.Topsoil?materialBlock[m]:0;

        /// <summary>Builds the column at a sample without writing blocks (the thread's column, valid until the next build).</summary>
        public GroundColumns.Column Build(in GroundColumns.Sample sample,GroundColumns.Surface surface)
        {
            var col=column??=new GroundColumns.Column();
            Model.Build(sample,surface,col);
            return col;
        }

        /// <summary>The block for layer material <paramref name="m"/> at depth <paramref name="depth"/> (0 = top).</summary>
        int BlockOf(GroundColumns.Column col,int m,int depth,int grass)
            =>m==Model.Topsoil?soil[Math.Max(0,col.TopsoilFertility),depth==0?grass:0]:materialBlock[m];

        /// <summary>Rewrites one column of a chunk column from <paramref name="top"/> (its top solid block) down to y = 1.
        /// Returns the column it built (the thread's column, valid until the next build).</summary>
        public GroundColumns.Column Write(IServerChunk[] chunks,int x,int z,int top,GroundColumns.Surface surface,in GroundColumns.Sample sample)
        {
            var col=column??=new GroundColumns.Column();
            Model.Build(sample,surface,col);
            int topId=chunks[top/32].Data.GetBlockIdUnsafe(((top%32)*32+z)*32+x);
            int grass=topId>0 && topId<grassOf.Length && grassOf[topId]>=0?grassOf[topId]:1;
            int layer=0;
            for(int y=top,d=0;y>=1;y--,d++)
            {
                while(layer<col.Count && d>=col.End[layer])layer++;
                if(layer==col.Count)break; // hand-off: the rock below stays as the strata pass placed it
                int index=((y%32)*32+z)*32+x;
                var data=chunks[y/32].Data;
                int old=data.GetBlockIdUnsafe(index);
                if(old<=0 || old>=ground.Length)continue;
                if(!ground[old] && !(preserved!=null && preserved[old]!=0
                    && (preserved[old]==1 || col.SegPart[col.SegmentAt(d)] is GroundColumns.Part.Top or GroundColumns.Part.Cover or GroundColumns.Part.DeepCover)))continue;
                int id=BlockOf(col,col.Material[layer],d,grass);
                if(wavy[old] && id>0 && id<sand.Length && sand[id])continue; // a sandy sea floor keeps vanilla's ripples
                if(id>0 && id!=old)data.SetBlockUnsafe(index,id);
            }
            return col;
        }

        /// <summary>
        /// The ground's blocks by depth below the top, for ore readings. Returns how many blocks from the top the ground
        /// owns: all of <paramref name="maxDepth"/>, or less where the column hands the rock below to regional geology.
        /// </summary>
        /// <param name="built">The column built (the thread's column, valid until the next build), for its layers (0060).</param>
        public int Column(in GroundColumns.Sample sample,GroundColumns.Surface surface,Span<int> idsByDepth,int maxDepth,out GroundColumns.Column built)
        {
            var col=built=column??=new GroundColumns.Column();
            Model.Build(sample,surface,col);
            int layer=0,d=0;
            for(;d<idsByDepth.Length && d<maxDepth;d++)
            {
                while(layer<col.Count && d>=col.End[layer])layer++;
                if(layer==col.Count)break;
                idsByDepth[d]=BlockOf(col,col.Material[layer],d,0);
            }
            return d;
        }

        public string Describe(in GroundColumns.Sample sample,GroundColumns.Surface surface)
        {
            var col=column??=new GroundColumns.Column();
            Model.Build(sample,surface,col);
            return "Ground: "+sample+". Column ("+surface.ToString().ToLowerInvariant()+"): "+Model.Describe(col);
        }
    }
}
