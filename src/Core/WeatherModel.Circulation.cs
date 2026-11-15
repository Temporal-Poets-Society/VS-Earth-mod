using System;

namespace EarthMap.Core
{
    // (EA-0089, review W-19) WeatherModel, the prevailing winds and the steering winds aloft (static); moved unchanged
    // from WeatherModel.cs
    public sealed partial class WeatherModel
    {
        // ------------------------------------------------------------------------------------------ prevailing winds

        /// <summary>
        /// Sea's belts beyond the subtropical ridges, by |latitude| (degrees): (latitude, eastward, poleward) in wind units.
        /// Westerlies from the south-west, polar easterlies from the north-east; calm at the ridges and the polar front.
        /// </summary>
        static readonly double[,] Belts =
        {
            { 0, -0.10, 0.00 }, { 5, -0.20, -0.10 }, { 10, -0.30, -0.15 }, { 15, -0.33, -0.14 }, { 22, -0.28, -0.10 },
            { 28, -0.12, -0.03 }, { 32, 0.00, 0.00 }, { 38, 0.20, 0.06 }, { 45, 0.34, 0.10 }, { 52, 0.36, 0.08 },
            { 58, 0.30, 0.06 }, { 64, 0.16, 0.03 }, { 70, 0.00, 0.00 }, { 76, -0.12, -0.04 }, { 84, -0.18, -0.04 }, { 90, -0.10, 0.00 },
        };

        static WeatherWind Belt(double lat)
        {
            double a = Math.Min(90, Math.Abs(lat)), hs = lat >= 0 ? 1 : -1;
            int i = 0;
            while (i < Belts.GetLength(0) - 2 && a > Belts[i + 1, 0]) i++;
            double t = WeatherNoise.SmoothStep(Belts[i, 0], Belts[i + 1, 0], a);
            double east = WeatherNoise.Lerp(Belts[i, 1], Belts[i + 1, 1], t);
            double pole = WeatherNoise.Lerp(Belts[i, 2], Belts[i + 1, 2], t);
            if (hs < 0)
            {
                double forties = 1 + 0.25 * WeatherNoise.Band(a, 36, 42, 55, 62);   // the roaring forties
                east *= forties;
                pole *= forties;
            }
            return new WeatherWind(east, -pole * hs);
        }

        /// <summary>Degrees the extratropical belts and the subtropical ridges sit north of their yearly place: they follow
        /// the ITCZ (the sun) part of the way.</summary>
        static double BeltShift(double itcz) => Math.Clamp(0.5 * (itcz - 4), -8, 8);

        /// <summary>0..1: how much of the tropical (trade wind and monsoon) regime holds at a latitude.</summary>
        static double TropicalWeight(double lat, double itcz)
        {
            double s = BeltShift(itcz), ridgeN = 30 + s, ridgeS = -30 + s;
            return (1 - WeatherNoise.SmoothStep(ridgeN - 4, ridgeN + 3, lat)) * WeatherNoise.SmoothStep(ridgeS - 3, ridgeS + 4, lat);
        }

        /// <summary>
        /// The month's prevailing wind at a latitude, given the ITCZ's latitude there and the land fraction (friction).
        /// Between the subtropical ridges the air flows toward the ITCZ: where that is toward the equator it's turned west
        /// (the trade winds, NE in the north and SE in the south); where the ITCZ has moved far from the equator and the
        /// air flows away from the equator to reach it, the earth's turning makes it a westerly: the SW monsoon of India and
        /// West Africa, the NW monsoon of northern Australia.
        /// </summary>
        public static WeatherWind PrevailingAt(double lat, double itcz, double land)
        {
            double wTrop = TropicalWeight(lat, itcz);
            WeatherWind w = default;
            if (wTrop > 0)
            {
                double s = BeltShift(itcz), d = lat - itcz;
                double ridge = d >= 0 ? 30 + s : -30 + s;
                double span = Math.Max(4, Math.Abs(ridge - itcz));
                double m = 0.31 * WeatherNoise.SmoothStep(0, 6, Math.Abs(d)) * (1 - WeatherNoise.SmoothStep(0.55, 1.0, Math.Abs(d) / span));
                double flowNorth = d >= 0 ? -1 : 1;
                bool equatorward = flowNorth * lat <= 0;
                double east = equatorward ? -0.9 * m : 1.05 * m * WeatherNoise.SmoothStep(0, 6, Math.Abs(lat));
                double north = flowNorth * 0.45 * m;
                w = new WeatherWind(east, -north) * wTrop;
            }
            if (wTrop < 1) w += Belt(lat - 0.5 * BeltShift(itcz)) * (1 - wTrop);
            return w * (1 - 0.35 * Math.Clamp(land, 0, 1));
        }

        /// <summary>The month's prevailing wind at a place (what the wind is on average).</summary>
        public WeatherWind Prevailing(double x, double z, double hours)
        {
            Place(x, z, hours, out double lat, out double lon, out double yr, out var n);
            return PrevailingFor(lat, Data.ItczLat(lon, yr), n);
        }

        /// <summary>
        /// The month's prevailing wind from the normals: the reanalysis' mean 10 m wind where weather.bin has it (it knows
        /// the East Asian winter monsoon, the winds round the oceans' highs, the katabatic winds off the ice sheets),
        /// otherwise the winds laid out around the ITCZ (<see cref="PrevailingAt"/>).
        /// </summary>
        WeatherWind PrevailingFor(double lat, double itcz, in WeatherData.Normals n) =>
            Data.HasWindsAndClouds ? new WeatherWind(n.WindE / MetersPerSecondPerUnit, -n.WindN / MetersPerSecondPerUnit) : PrevailingAt(lat, itcz, n.Land);

        /// <summary>Metres per second for one vanilla wind unit (vanilla's "storm" ≈ Beaufort 10, Sea's Beaufort scale).</summary>
        public const double MetersPerSecondPerUnit = 25;

        /// <summary>
        /// How fast weather systems move here, km/h (east, north): the winds a few kilometres up, which are the prevailing
        /// winds without the ground's friction, stronger and more westerly in the mid-latitudes.
        /// </summary>
        public static void SteeringKmh(double lat, double itcz, out double east, out double north)
        {
            var p = PrevailingAt(lat, itcz, 0);
            double trop = TropicalWeight(lat, itcz);
            double k = 100 * (1 - 0.3 * trop);
            east = p.X * k + 14 * WeatherNoise.Band(Math.Abs(lat), 26, 36, 62, 72);
            north = -p.Z * k * (1 - 0.85 * trop);
        }

        /// <summary>The steering wind in blocks per weather hour (world axes).</summary>
        void SteeringBlocks(double lat, double itcz, out double vx, out double vz)
        {
            SteeringKmh(lat, itcz, out double east, out double north);
            double cos = Math.Max(0.25, PortableMath.Cos(lat * Math.PI / 180));
            vx = east / (111.32 * cos) * Geo.BlocksPerDegreeX;
            vz = -north / 111.32 * Geo.BlocksPerDegreeZ;
        }
    }
}
