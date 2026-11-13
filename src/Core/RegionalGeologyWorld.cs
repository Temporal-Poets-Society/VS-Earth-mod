using System;

namespace EarthMap.Core
{
    /// <summary>
    /// Terrain-coupled geology helpers. They live apart from the catalogue and the transition maths so data-only
    /// consumers (e.g. the ore tests) can compile those without the terrain model.
    /// </summary>
    public static class RegionalGeologyWorld
    {
        /// <summary>
        /// The category sample for a world column: the column is displaced by the contact warp of this rules version
        /// (v1 frozen, v2 with the cell-scale octave) before lookup. Gutter/beyond-pole columns never borrow real Earth.
        /// </summary>
        public static RegionalGeologyCatalog.Sample Sample(RegionalGeologyCatalog catalog,EarthGeometry geometry,double x,double z,long seed,double width,double cellBlocks,out EarthGeometry.GeoPos original)
        {
            original=geometry.ToGeo(x,z);
            if(original.InGutter||original.BeyondPole)return catalog.At(original.Lat,original.Lon,true);
            var w=catalog.Version>=2
                ? GeologyTransitions.WarpV2(x,z,seed,width,cellBlocks,catalog.Generation.WarpLowWavelengthCells,catalog.Generation.WarpLowAmplitudeCells)
                : GeologyTransitions.Warp(x,z,seed,width);
            var blended=geometry.ToGeo(w.X,w.Z);
            // Never drag filler/gutter data into real Earth. Contacts within Earth have coherent irregular lobes.
            return blended.InGutter||blended.BeyondPole ? catalog.At(original.Lat,original.Lon) : catalog.At(blended.Lat,blended.Lon);
        }

        /// <summary>
        /// Physical setting of a deposit centre: the centre column plus four columns 12 blocks away. Relief uses the
        /// water surface where there is water (river/lake depth is not relief) and is converted to real metres.
        /// </summary>
        public static RegionalGeologyCatalog.Site SiteAt(TerrainModel model,double x,double z,out TerrainModel.Column center)
        {
            var c=center=model.GetColumn(x,z);
            var climate=model.Data.SampleClimate(c.Lat,c.Lon);
            bool fresh=c.River || c.Water==TerrainModel.WaterLake;
            int low=Math.Max(c.Top,c.WaterTop),high=low;
            for(int i=0;i<4;i++)
            {
                var n=model.GetColumn(x+(i==0?-12:i==1?12:0),z+(i==2?-12:i==3?12:0));
                int surface=Math.Max(n.Top,n.WaterTop);
                low=Math.Min(low,surface);high=Math.Max(high,surface);
                fresh|=n.River || n.Water==TerrainModel.WaterLake;
            }
            double reliefMetres=Math.Max(0,model.Vertical.Elevation(high)-model.Vertical.Elevation(low));
            double spanKm=24*111.195/Math.Max(1e-6,model.Geometry.BlocksPerDegreeZ);
            return new RegionalGeologyCatalog.Site(c.Water==TerrainModel.WaterSea,c.Water==TerrainModel.WaterSaltLake,c.IceBottom<=c.Top,c.WaterTop>c.Top,fresh,
                climate.TMean-.0065*Math.Max(0,c.Elev),climate.Precip,high-low,reliefMetres,spanKm,Math.Clamp(climate.Rain/255.0,0,1));
        }
        /// <summary>Centre-only setting (salt attempts need no relief).</summary>
        public static RegionalGeologyCatalog.Site SiteAtCentre(in TerrainModel.Column c)
            => new RegionalGeologyCatalog.Site(c.Water==TerrainModel.WaterSea,c.Water==TerrainModel.WaterSaltLake,c.IceBottom<=c.Top,c.WaterTop>c.Top,false,0,0,0);
    }
}
