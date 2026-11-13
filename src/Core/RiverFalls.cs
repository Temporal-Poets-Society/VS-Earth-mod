using System;
using System.Collections.Generic;

namespace EarthMap.Core
{
    /// <summary>A waterfall on a river of the network: where its lip is and how high it falls (m).</summary>
    public sealed class RiverFall
    {
        public string Name;
        public double Lat, Lon, Height;
    }

    /// <summary>A waterfall as placed on this world's rivers.</summary>
    public struct PlacedFall
    {
        public string Name;
        public int Reach;
        /// <summary>Discharge of the river there (m³/s).</summary>
        public double Q;
        /// <summary>World position of the lip on the river's centre line, and the drop (blocks).</summary>
        public double X, Z;
        public int Drop;
    }

    /// <summary>
    /// The great waterfalls of the large rivers. The river levels are the lowest ground in a 5' window, smoothed along the
    /// river, which spreads a fall over kilometres of gentle steps, and the vertical scale compresses relief up high:
    /// Victoria Falls (108 m at 880 m) came out as four one-block steps over 400 blocks, while Niagara (near sea level,
    /// with Lake Erie's level held up to the escarpment) was a 15-block cascade. A listed fall drops the river at once by its
    /// height on the scale of the lowlands (RiverNetwork), so it is a curtain of falling water as tall as a cliff of that
    /// height by the sea, and the river below runs in a gorge until the valley comes down to it.
    /// </summary>
    public static class RiverFalls
    {
        /// <summary>Version of this list, stored per world (EarthParams.RiverFalls; 0: no falls). Frozen: every world saved
        /// with version 1 rebuilds its rivers from this list at each start, so editing an entry changes its rivers' levels in
        /// chunks generated after the edit. A changed list needs a new version, kept beside this one.</summary>
        public const int Version = 1;

        public static readonly IReadOnlyList<RiverFall> All = new[]
        {
            F("Victoria Falls", -17.9243, 25.8572, 108),
            F("Niagara Falls", 43.0775, -79.0753, 51),
            F("Iguazu Falls", -25.6953, -54.4367, 82),
            F("Kaieteur Falls", 5.1750, -59.4800, 226),
            F("Rhine Falls", 47.6779, 8.6153, 23),
            F("Khone Phapheng", 13.9480, 105.9400, 21),
            F("Murchison Falls", 2.2786, 31.6853, 43),
            F("Dettifoss", 65.8146, -16.3846, 44),
            F("Gullfoss", 64.3271, -20.1199, 32),
            F("Shoshone Falls", 42.5937, -114.4007, 65),
            F("Ruacana Falls", -17.3961, 14.2167, 120),
            F("Augrabies Falls", -28.5917, 20.3406, 56),
            F("Hukou Waterfall", 36.1428, 110.4436, 20),
            F("Kalandula Falls", -9.0747, 15.9978, 105),
        };

        static RiverFall F(string name, double lat, double lon, double height) => new RiverFall { Name = name, Lat = lat, Lon = lon, Height = height };
    }
}
