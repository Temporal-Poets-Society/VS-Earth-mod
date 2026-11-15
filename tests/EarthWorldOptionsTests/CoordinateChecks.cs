using System;
using EarthMap.Core;

/// <summary>
/// (2.0.1, the author: "make custom starting coordinates format work with copy paste from Google maps coordinates") Start
/// coordinates (on the Player spawn and death tab since 2.0.3), read by Core.Coordinates: what Google Maps gives (the decimal pair of a clicked place, degrees,
/// minutes and seconds, a link) and the plain form from before, in both hemispheres and signs, at 0 and ±180; and the
/// reasons it gives for what it can't read.
/// </summary>
static class CoordinateChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Is(string text, double lat, double lon)
        {
            bool ok = Coordinates.TryParse(text, out double a, out double b, out string why);
            check(ok && Math.Abs(a - lat) < 1e-6 && Math.Abs(b - lon) < 1e-6,
                $"coordinates '{text}' read as {lat}, {lon}" + (ok ? $" (got {a}, {b})" : $" (refused: {why})"));
        }
        void Not(string text, string reason)
        {
            bool ok = Coordinates.TryParse(text, out double a, out double b, out string why);
            check(!ok && why != null && why.Contains(reason, StringComparison.Ordinal),
                $"coordinates '{text}' refused ({reason})" + (ok ? $", but read as {a}, {b}" : $", said: {why}"));
        }
        const double rigaLat = 56 + 56 / 60.0 + 58.6 / 3600, rigaLon = 24 + 6 / 60.0 + 18.7 / 3600;

        // the decimal pair Google Maps shows when you click a place, and the plain form from before
        Is("56.949600, 24.105200", 56.9496, 24.1052);
        Is("  56.949600 ,   24.105200  ", 56.9496, 24.1052);
        Is("56.9496,24.1052", 56.9496, 24.1052);
        Is("56.95 24.11", 56.95, 24.11);
        Is("56.95;24.11", 56.95, 24.11);
        Is("-33.856784, 151.215297", -33.856784, 151.215297);   // Sydney Opera House
        Is("-22.951916, -43.210487", -22.951916, -43.210487);   // Christ the Redeemer
        Is("40.689247, -74.044502", 40.689247, -74.044502);     // Statue of Liberty
        Is("0, 0", 0, 0);
        Is("0.000000, 180.000000", 0, 180);
        Is("-0.5, -180", -0.5, -180);
        Is("90, 0", 90, 0);
        Is("-90, 45", -90, 45);
        Is("+51.5007, -0.1246", 51.5007, -0.1246);
        Is("56.9496° N, 24.1052° E", 56.9496, 24.1052);
        Is("33.8568° S, 151.2153° E", -33.8568, 151.2153);
        Is("56.9496N 24.1052E", 56.9496, 24.1052);

        // degrees, minutes and seconds, as Google Maps shows them and as browsers and word processors copy the quotes
        Is("56°56'58.6\"N 24°06'18.7\"E", rigaLat, rigaLon);
        Is("56°56′58.6″N 24°06′18.7″E", rigaLat, rigaLon);
        Is("56°56’58.6”N, 24°06’18.7”E", rigaLat, rigaLon);
        Is("56º56'58.6''N 24º06'18.7''E", rigaLat, rigaLon);
        Is("N 56°56'58.6\" E 24°06'18.7\"", rigaLat, rigaLon);
        Is("24°06'18.7\"E 56°56'58.6\"N", rigaLat, rigaLon);
        Is("33°51'24.4\"S 151°12'55.1\"E", -(33 + 51 / 60.0 + 24.4 / 3600), 151 + 12 / 60.0 + 55.1 / 3600);
        Is("22°57'06.9\"S 43°12'37.8\"W", -(22 + 57 / 60.0 + 6.9 / 3600), -(43 + 12 / 60.0 + 37.8 / 3600));
        Is("40°41'21.3\"N 74°02'40.2\"W", 40 + 41 / 60.0 + 21.3 / 3600, -(74 + 2 / 60.0 + 40.2 / 3600));
        Is("0°00'00.0\"N 180°00'00.0\"E", 0, 180);
        Is("0°00'00.0\"S 180°00'00.0\"W", 0, -180);
        Is("56°56.977'N 24°06.312'E", 56 + 56.977 / 60, 24 + 6.312 / 60);   // degrees and decimal minutes
        Is("56° N 24° E", 56, 24);
        // Fable's re-review of ee0110f: two typographic single quotes for seconds (P2), nothing between the two (P3)
        Is("56°56’58.6’’N 24°06’18.7’’E", rigaLat, rigaLon);
        Is("56°56′58.6′′N 24°06′18.7′′E", rigaLat, rigaLon);
        Is("56°56'58.6\"N24°06'18.7\"E", rigaLat, rigaLon);
        Is("56.9496N24.1052E", 56.9496, 24.1052);

        // links: the place's pin where there is one, else the map's centre, else a query
        Is("https://www.google.com/maps/@56.9496,24.1052,15z", 56.9496, 24.1052);
        Is("https://www.google.com/maps/@-33.8567844,151.2152967,17z?entry=ttu", -33.8567844, 151.2152967);
        Is("https://www.google.com/maps/place/Riga/@56.9713962,23.9890814,11z/data=!3m1!4b1!4m6!3m5!1s0x46eecfb0e5073ded:0x400cfcd68f2fe30!8m2!3d56.9676941!4d24.1056221!16zL20vMDZwcDA",
            56.9676941, 24.1056221);
        Is("https://maps.google.com/?q=40.689247,-74.044502", 40.689247, -74.044502);
        Is("https://www.google.com/maps/search/?api=1&query=-22.951916%2C-43.210487", -22.951916, -43.210487);

        // what it can't read, and why
        Not("", "nothing");
        Not("Riga", "not two coordinates");
        Not("95, 10", "latitude");
        Not("10, 190", "longitude");
        Not("56.95", "not two coordinates");
        Not("5695", "not two coordinates");
        Not("56.9524.11", "not two coordinates");
        Not("56°61'00\"N 24°06'18.7\"E", "minutes");
        Not("56°56'61\"N 24°06'18.7\"E", "seconds");
        Not("56°56'58.6\"N 24°06'18.7\"N", "same kind of hemisphere");
        Not("-56.95 S, 24.11 E", "negative");
        Not("https://maps.app.goo.gl/AbCdEf123", "link");
        Not("https://www.google.com/maps/dir//56.9496,24.1052/@56.9,24.0,12z", "directions");   // P1: its @ is neither end
        Not("https://www.google.com/maps/place/Riga", "link");
        check(Coordinates.Formats.Contains("Google Maps", StringComparison.Ordinal) && Coordinates.Formats.Contains("56°56'58.6\"N", StringComparison.Ordinal),
            "the formats named in the message: Google Maps' decimal pair, degrees minutes seconds, a link");
        // the spawn's reader is the same
        check(SpawnSearch.TryParseLatLon("56°56'58.6\"N 24°06'18.7\"E", out double la, out double lo) && Math.Abs(la - rigaLat) < 1e-9 && Math.Abs(lo - rigaLon) < 1e-9,
            "SpawnSearch.TryParseLatLon reads what Coordinates reads");
    }
}
