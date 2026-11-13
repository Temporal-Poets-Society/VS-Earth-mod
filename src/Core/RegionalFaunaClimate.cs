using System;

namespace EarthMap.Core
{
    /// <summary>Natural fauna consumes Earth's local surface climate, before game-map altitude compensation.</summary>
    public static class RegionalFaunaClimate
    {
        public readonly record struct Sample(double Temperature, double Rainfall, double SurfaceY);
        public static Sample At(TerrainModel model, double x, double z, bool extendTemperature)
        {
            var geo=model.Geometry.ToGeo(x,z);
            model.ClimateAt(geo.Lat,geo.Lon,out double temperature,out double rain,out double surfaceY);
            rain=model.RiparianRain(x,z,rain);
            return new Sample(extendTemperature?temperature:Math.Clamp(temperature,-20,40),Math.Clamp(rain/255,0,1),surfaceY);
        }
    }
}
