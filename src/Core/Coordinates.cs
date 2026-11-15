using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EarthMap.Core
{
    /// <summary>
    /// The Earth tab's start coordinates (2.0.1, the author: "make custom starting coordinates format work with copy paste
    /// from Google maps coordinates"). Reads what people paste from Google Maps, and the plain form from before:
    /// <list type="bullet">
    /// <item>decimal degrees, as Google Maps shows them when a place is clicked: <c>56.949600, 24.105200</c> (south and west
    /// negative; also <c>lat lon</c> and <c>lat;lon</c>, with any spaces), with or without degree signs and hemispheres
    /// (<c>56.9496° N, 24.1052° E</c>);</item>
    /// <item>degrees, minutes and seconds with hemispheres: <c>56°56'58.6"N 24°06'18.7"E</c>, with the quote characters Google
    /// and browsers use (′ ″ ’ ” “ '' and º for °), hemisphere letters before or after;</item>
    /// <item>a pasted Google Maps link: the place's pin (<c>!3d56.9496!4d24.1052</c>) where the link has one, else the map's
    /// centre (<c>@56.9496,24.1052,15z</c>), else <c>q=</c>, <c>query=</c>, <c>ll=</c> or <c>center=</c>.</item>
    /// </list>
    /// Latitude must be within ±90 and longitude within ±180.
    /// </summary>
    public static class Coordinates
    {
        /// <summary>What is accepted, for messages and the option's text.</summary>
        public const string Formats = "decimal degrees as Google Maps shows them when you click a place (56.949600, 24.105200; " +
            "south and west negative), degrees, minutes and seconds (56°56'58.6\"N 24°06'18.7\"E), or a Google Maps link with " +
            "@latitude,longitude in it";

        static readonly Regex Pin = new(@"!3d(?<lat>[+-]?\d+(?:\.\d+)?)!4d(?<lon>[+-]?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        static readonly Regex At = new(@"@(?<lat>[+-]?\d+(?:\.\d+)?),(?<lon>[+-]?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        static readonly Regex Query = new(@"[?&](?:q|query|ll|center)=(?<lat>[+-]?\d+(?:\.\d+)?)(?:,|%2C|\+|%20)+(?<lon>[+-]?\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // one coordinate: an optional hemisphere, the degrees (signed), optional minutes and seconds, and a hemisphere after
        // them only where none came before (so "N 56°56'58.6" E 24°06'18.7"" doesn't give the first the second's letter)
        const string Part = @"(?:(?<h1{0}>[NSEW])\s*)?(?<d{0}>[+-]?\d+(?:\.\d+)?)\s*(?:°\s*)?" +
            @"(?:(?<m{0}>\d+(?:\.\d+)?)\s*'\s*(?:(?<s{0}>\d+(?:\.\d+)?)\s*""\s*)?)?(?(h1{0})|(?:(?<h2{0}>[NSEW])(?![A-Z]))?)";
        // the two separated by a comma, a semicolon or spaces, or by nothing after a hemisphere letter or a seconds mark
        // ("56°56'58.6"N24°06'18.7"E"; P3), never between digits ("56.95" is not 5 and 6.95)
        static readonly Regex Pair = new(@"^\s*" + string.Format(Part, "a") + @"(?:\s*[,;]\s*|\s+|(?<=[NSEW""]))" + string.Format(Part, "b") + @"\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Reads <paramref name="text"/> as a latitude and longitude in degrees; false with the reason otherwise.</summary>
        public static bool TryParse(string text, out double lat, out double lon, out string why)
        {
            lat = lon = double.NaN;
            why = null;
            string s = (text ?? "").Trim();
            if (s.Length == 0) { why = "nothing was given"; return false; }

            // a link: the place's pin, else the map's centre, else a query; a directions link has no one place (its @ is the
            // map's centre, neither end: Fable's re-review, P1)
            if (s.Contains("/maps/dir/", StringComparison.OrdinalIgnoreCase) || s.Contains("/dir/", StringComparison.Ordinal) && s.Contains("google", StringComparison.OrdinalIgnoreCase))
            {
                why = "a directions link has no one place in it: copy the place's own link, or the numbers Google Maps shows for it";
                return false;
            }
            var m = Pin.Match(s);
            if (!m.Success) m = At.Match(s);
            if (!m.Success) m = Query.Match(s);
            if (m.Success)
                return Number(m.Groups["lat"].Value, out lat) & Number(m.Groups["lon"].Value, out lon) && InRange(lat, lon, out why);
            if (s.Contains("://", StringComparison.Ordinal) || s.StartsWith("maps.", StringComparison.OrdinalIgnoreCase) || s.Contains("goo.gl", StringComparison.OrdinalIgnoreCase))
            {
                why = "the link has no coordinates in it (a short share link doesn't: open it and copy the address with @latitude,longitude, or the numbers Google Maps shows for the place)";
                return false;
            }

            s = Normalize(s);
            m = Pair.Match(s);
            if (!m.Success) { why = "it is not two coordinates"; return false; }
            if (!Component(m, "a", out double a, out char ha, out why) || !Component(m, "b", out double b, out char hb, out why)) return false;
            // hemispheres decide which is which; without them the first is the latitude
            bool aLon = ha is 'E' or 'W', bLat = hb is 'N' or 'S';
            if ((ha is 'N' or 'S' && hb is 'N' or 'S') || (aLon && hb is 'E' or 'W'))
            {
                why = "both coordinates have the same kind of hemisphere (one needs N or S, the other E or W)";
                return false;
            }
            if (aLon || bLat) { lat = b; lon = a; }
            else { lat = a; lon = b; }
            return InRange(lat, lon, out why);
        }

        /// <summary>The quote, degree and minus characters Google, browsers and word processors use, as ASCII.</summary>
        static string Normalize(string s) => s
            .Replace("º", "°").Replace("˚", "°")
            // single marks first, so two of them (58.6’’, 58.6′′) become one seconds mark below (P2)
            .Replace("′", "'").Replace("’", "'").Replace("‘", "'").Replace("´", "'").Replace("`", "'")
            .Replace("''", "\"").Replace("″", "\"").Replace("”", "\"").Replace("“", "\"").Replace("„", "\"")
            .Replace("−", "-").Replace("–", "-")
            .Replace(' ', ' ').Replace(' ', ' ');

        static bool Component(Match m, string p, out double value, out char hemisphere, out string why)
        {
            value = double.NaN;
            why = null;
            string h1 = m.Groups["h1" + p].Value, h2 = m.Groups["h2" + p].Value;
            if (h1.Length > 0 && h2.Length > 0) { hemisphere = '\0'; why = "a coordinate has two hemisphere letters"; return false; }
            hemisphere = char.ToUpperInvariant((h1 + h2).Length > 0 ? (h1 + h2)[0] : '\0');
            string d = m.Groups["d" + p].Value, mins = m.Groups["m" + p].Value, secs = m.Groups["s" + p].Value;
            if (!Number(d, out double deg)) { why = "a number can't be read"; return false; }
            bool negative = d.StartsWith("-", StringComparison.Ordinal);
            if (negative && hemisphere != '\0') { why = "a coordinate is negative and has a hemisphere letter too"; return false; }
            double minutes = 0, seconds = 0;
            if (mins.Length > 0)
            {
                if (d.Contains('.')) { why = "degrees with a fraction can't also have minutes"; return false; }
                if (!Number(mins, out minutes) || minutes >= 60) { why = "minutes must be below 60"; return false; }
                if (secs.Length > 0)
                {
                    if (mins.Contains('.')) { why = "minutes with a fraction can't also have seconds"; return false; }
                    if (!Number(secs, out seconds) || seconds >= 60) { why = "seconds must be below 60"; return false; }
                }
            }
            value = Math.Abs(deg) + minutes / 60 + seconds / 3600;
            if (negative || hemisphere is 'S' or 'W') value = -value;
            return true;
        }

        static bool Number(string s, out double value) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

        static bool InRange(double lat, double lon, out string why)
        {
            why = !double.IsFinite(lat) || !double.IsFinite(lon) ? "a number can't be read"
                : Math.Abs(lat) > 90 ? "the latitude is beyond ±90"
                : Math.Abs(lon) > 180 ? "the longitude is beyond ±180" : null;
            return why == null;
        }
    }
}
