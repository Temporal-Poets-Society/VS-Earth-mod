using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Offline checks of the weather (decision 0017) on the standard Cylinder World 256000 x 102400 layout (ring 204 800,
    /// about 198 m per block) with the default calendar (108 days a year, 24 hours a day). Writes maps to tests/out/weather/.
    ///   dotnet run -c Release -- [dataFolder] [maps]
    /// </summary>
    static class Program
    {
        static int failures;
        static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static string FindRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "modinfo.json"))) dir = Path.GetDirectoryName(dir);
            return dir ?? Directory.GetCurrentDirectory();
        }

        const double DaysPerYear = 108, HoursPerDay = 24, YearHours = DaysPerYear * HoursPerDay;

        /// <summary>Game hours at the middle of month m of year y.</summary>
        static double MonthHours(int y, int m, double frac = 0.5) => (y * DaysPerYear + (m + frac) / 12.0 * DaysPerYear) * HoursPerDay;

        static int Main(string[] args)
        {
            string root = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : FindRoot();
            bool maps = args.Contains("maps");
            string outDir = Path.Combine(root, "tests", "out", "weather");
            Directory.CreateDirectory(outDir);
            var sw = Stopwatch.StartNew();
            var data = EarthData.LoadFromFolder(root, false);
            Console.WriteLine($"data loaded in {sw.ElapsedMilliseconds} ms");
            if (data.Weather == null) { Console.WriteLine("FAIL no weather.bin"); return 1; }
            CheckWeatherFilePinned(data.Weather, root);

            int W = 256000, L = 102400;
            var inp = new LayoutInput
            {
                WorldWidth = W,
                CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48,
                CwBandInner = 608,
            };
            var geo = EarthLayout.Build(inp, data, out _);
            var settings = new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay };
            sw.Restart();
            var model = new WeatherModel(data.Weather, geo, settings, 12345);
            Console.WriteLine($"model built in {sw.ElapsedMilliseconds} ms; {model.BlocksPerKm:0.00} blocks per km");

            CheckItcz(data.Weather);
            CheckMonthsOverLand(data.Weather);
            int di = Array.IndexOf(args, "dryspells");
            if (di >= 0)
            {
                DrySpellScan(data, geo, args[di + 1], args.Contains("detail"));
                return 0;
            }
            int fi = Array.IndexOf(args, "rainfreq");
            if (fi >= 0)
            {
                RainFrequencyScan(data, geo, args[fi + 1], args[fi + 2]);
                return 0;
            }
            if (args.Contains("rules6"))
            {
                CheckRules6(data.Weather, geo);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("lowcloud"))
            {
                LowCloudScan(data.Weather, geo);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("drydays"))
            {
                CheckDrySpells(model);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("moisture"))
            {
                CheckGroundMoisture(model);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("firedanger"))
            {
                CheckFireDanger(model, geo);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("thunderrate"))
            {
                FitShowerRate(data, inp);
                return 0;
            }
            if (args.Contains("thunder"))
            {
                CheckThunder(model, geo);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            if (args.Contains("speed"))
            {
                // quick: only the lookup speed and the catch-up
                CheckPerformance(model);
                CheckCatchUp(data, geo);
                Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
                return failures == 0 ? 0 : 1;
            }
            CheckClimateFix(data);
            CheckStripes(data.Weather);
            CheckDeterminism(data, geo, settings, model);
            CheckSites(model);
            CheckSpells(model);
            CheckDrySpells(model);
            CheckGroundMoisture(model);
            CheckStorms(model);
            CheckSeam(model, geo);
            CheckWinds(model);
            CheckThunder(model, geo);
            CheckPerformance(model);
            CheckRainOff(data, geo);
            CheckCatchUp(data, geo);
            if (maps) Maps(model, data.Weather, outDir);

            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void Pos(EarthGeometry geo, double lat, double lon, out double x, out double z) => geo.FromGeo(lat, lon, out x, out z);

        /// <summary>The shipped weather.bin as Earth 2.0.0-rc.2 has it (since a37fc76, 7 November 2026).</summary>
        const string WeatherSignature = "bc9ea5a04cb1bc1d", WeatherBytes = "3634099";

        /// <summary>
        /// Adversarial review A6: world generation's vegetation zones and fire rules read weather.bin's monthly normals, and a
        /// world freezes the file's signature when it is created (EarthParams.WeatherDataMade), so the shipped file must never
        /// change under its name. This fails for any change to it, on purpose.
        /// </summary>
        static void CheckWeatherFilePinned(WeatherData weather, string root)
        {
            long bytes = new FileInfo(Path.Combine(root, "earthdata", "weather.bin")).Length;
            Check(weather.Signature == WeatherSignature && bytes.ToString(Ci) == WeatherBytes,
                $"earthdata/weather.bin is the pinned file (signature {weather.Signature}, {bytes} bytes; pinned {WeatherSignature}, {WeatherBytes} bytes). " +
                "A changed weather.bin must not replace this one: worlds froze its signature at creation and their vegetation zones and fire " +
                "rules read it. Ship new normals under a new file name with a new zone/fire rules version that reads it, keep this file for the " +
                "worlds made with it, and only then pin the new one here.");
        }

        // ------------------------------------------------------------------------------------------ ITCZ

        static void CheckItcz(WeatherData d)
        {
            Console.WriteLine("ITCZ (tropical rain band)");
            double Yr(int m) => (m + 0.5) / 12;
            double indiaJul = d.ItczLat(78, Yr(6)), indiaJan = d.ItczLat(78, Yr(0));
            double africaJul = d.ItczLat(5, Yr(6)), africaJan = d.ItczLat(5, Yr(0));
            double pacJul = d.ItczLat(-140, Yr(6)), pacJan = d.ItczLat(-140, Yr(0));
            double ausJan = d.ItczLat(132, Yr(0));
            Console.WriteLine($"  India {indiaJan:0.0} (Jan) .. {indiaJul:0.0} (Jul); West Africa {africaJan:0.0} .. {africaJul:0.0}; central Pacific {pacJan:0.0} .. {pacJul:0.0}; northern Australia (Jan) {ausJan:0.0}");
            Check(indiaJul > 15 && indiaJan < 0, "over India the rain band moves north in summer (monsoon) and south of the equator in winter");
            Check(africaJul > 7 && africaJan < 5, "over West Africa it reaches the Sahel in July");
            Check(Math.Abs(pacJul - pacJan) < 10 && pacJul > 2, "over the central Pacific it moves much less than over land (in January the South Pacific rain band pulls it south)");
            Check(ausJan < -10, "over northern Australia it is south in January (the Australian monsoon)");
        }

        // ------------------------------------------------------------------------------------------ every month over land

        /// <summary>
        /// Every month has precipitation over the land. weather.bin until 2026-11-07 was baked from a UDel download cut short
        /// three quarters into November: December read 0 mm on 87 % of the land north of 60°S (Riga, Moscow, Chicago
        /// without December rain or snow). The complete normals leave at most 2.6 % at 0 mm (August: the Sahara and Sahel in
        /// their dry season). Antarctica stays out: UDel's few stations there read about 0 mm. The bake has the same limit
        /// (tools/bake_earth.py WX_ZERO_LAND_MAX).
        /// </summary>
        static void CheckMonthsOverLand(WeatherData d)
        {
            Console.WriteLine("every month has precipitation over the land");
            const double MaxZeroShare = 0.04;
            int[] days = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            var zero = new int[12];
            int land = 0;
            double step = 1.0 / d.Res;
            for (double lat = -60 + step / 2; lat < 90; lat += step)
                for (double lon = -180 + step / 2; lon < 180; lon += step)
                {
                    if (d.LandAt(lat, lon) < 0.5) continue;
                    land++;
                    for (int m = 0; m < 12; m++) if (d.MonthPrecip(m, lat, lon) == 0) zero[m]++;
                }
            Console.WriteLine("  land cells at 0 mm by month: " + string.Join(" ", zero.Select(z => (100.0 * z / land).ToString("0.0", Ci) + "%")));
            int worst = Enumerable.Range(0, 12).OrderByDescending(m => zero[m]).First();
            Check(zero[worst] <= MaxZeroShare * land, $"no month is 0 mm over more than {MaxZeroShare * 100:0} % of the land north of 60°S (worst: month {worst + 1}, {100.0 * zero[worst] / land:0.0} %)");

            Console.WriteLine("  mm per month         Oct    Nov    Dec    Jan    Feb");
            var sites = new (string name, double lat, double lon, bool wetWinter)[]
            {
                ("Riga", 56.95, 24.10, true), ("Moscow", 55.75, 37.62, true), ("Chicago", 41.88, -87.63, true),
                ("Beijing", 39.90, 116.40, false), ("Sahel (Niamey)", 13.51, 2.11, false), ("Tucson", 32.22, -110.97, true),
                ("Athens", 37.98, 23.73, true), ("Darwin", -12.46, 130.84, true),
            };
            foreach (var s in sites)
            {
                double Mm(int m) => d.MonthPrecip(m, s.lat, s.lon) * days[m];
                int[] ms = { 9, 10, 11, 0, 1 };
                Console.WriteLine($"  {s.name,-18}" + string.Concat(ms.Select(m => Mm(m).ToString("0", Ci).PadLeft(7))));
                // places with a real winter rain or snow season: December between November and January, not far below both
                if (s.wetWinter)
                    Check(Mm(11) > 0.5 * Math.Min(Mm(10), Mm(0)), $"{s.name}: December ({Mm(11):0} mm) is not far below November ({Mm(10):0}) and January ({Mm(0):0})");
            }
        }

        // ------------------------------------------------------------------------------------------ stripes

        /// <summary>
        /// No one-row stripes in the monthly rain: a land cell whose rain differs a lot from the cells north and south of
        /// it while those two agree is how UDel's missing months showed (the coarse ocean data filled whole runs of a row).
        /// </summary>
        /// <summary>
        /// (Weather rules 6, EA-0023) The cold-water low cloud decks in Klein and Hartmann's (1993) five stratocumulus
        /// regions at their seasonal peak, against the satellites' total cover there (ISCCP, MODIS, rounded; within 10
        /// points): the reanalysis' cover, how much colder the sea is than its latitude's open ocean, and the cover with the
        /// decks; the same along the Peruvian and Namibian coasts, where the reanalysis is clearest; and where else the
        /// decks add more than a fifth of the sky (the eastern subtropical oceans, and the China seas in winter).
        /// </summary>
        static void LowCloudScan(WeatherData d, EarthGeometry geo)
        {
            Console.WriteLine("low cloud decks over cold seas (rules 6)");
            var m6 = new WeatherModel(d, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = 6 }, 12345);
            // name, lat0, lat1, lon0, lon1, peak month (1-12), Klein and Hartmann's low stratiform peak (%)
            var regions = new (string Name, double La0, double La1, double Lo0, double Lo1, int Peak, double Target)[]
            {
                ("Peru", -20, -10, -90, -80, 10, 80), ("Namibia", -20, -10, 0, 10, 9, 80), ("California", 20, 30, -130, -120, 7, 75),
                ("Australia", -35, -25, 95, 105, 1, 55), ("Canary", 15, 25, -35, -25, 7, 60),
                ("Peru coast", -18, -6, -82, -76, 9, 80), ("Namibia coast", -28, -16, 10, 15, 9, 80),
            };
            foreach (var r in regions)
            {
                var line = new System.Text.StringBuilder($"  {r.Name,-10}");
                double peakCover = 0, peakNcep = 0;
                for (int mo = 0; mo < 12; mo++)
                {
                    double ncep = 0, cold = 0, cover = 0; int n = 0, nc = 0;
                    for (double la = r.La0 + 0.25; la < r.La1; la += 0.5)
                        for (double lo = r.Lo0 + 0.25; lo < r.Lo1; lo += 0.5)
                        {
                            geo.FromGeo(la, lo, out double x, out double z);
                            double h = MonthHours(0, mo);
                            var nn = d.Sample(la, lo, (mo + 0.5) / 12);
                            if (nn.Land > 0.5) continue;
                            ncep += nn.Cloud; cover += m6.MonthCover(x, z, h); n++;
                            double c = d.ColdWater(la, lo, (mo + 0.5) / 12, nn.Temp, nn.Land);
                            if (!double.IsNaN(c)) { cold += c; nc++; }
                        }
                    ncep /= n; cover /= n; cold /= Math.Max(1, nc);
                    line.Append($" {cover * 100,3:0}");
                    if (mo + 1 == r.Peak) { peakCover = cover; peakNcep = ncep; line.Append($"({ncep * 100:0}/{cold:0.0}K)"); }
                }
                Console.WriteLine(line);
                // (the reanalysis already above the satellites, as off Western Australia, is not the decks' doing)
                // (the coasts within 15: the 0.5° grid blurs the narrow upwelling strip, Benguela's most)
                double tolerance = r.Name.EndsWith("coast") ? 15 : 10;
                Check(Math.Abs(peakCover * 100 - r.Target) <= tolerance || (peakNcep * 100 > r.Target + 10 && peakCover <= peakNcep + 0.02),
                    $"{r.Name}: month {r.Peak} cover {peakCover * 100:0} % with the deck (reanalysis {peakNcep * 100:0} %), the satellites' about {r.Target:0} %");
            }
            // where else the decks add a fifth of the sky or more, in any month (0.5° cells)
            var where = new Dictionary<string, int>();
            int cells = 0;
            for (double la = -59.75; la < 60; la += 0.5)
                for (double lo = -179.75; lo < 180; lo += 0.5)
                    for (int mo = 0; mo < 12; mo += 3)
                    {
                        var nn = d.Sample(la, lo, (mo + 0.5) / 12);
                        if (nn.Land > 0.5) continue;
                        geo.FromGeo(la, lo, out double x, out double z);
                        double added = m6.MonthCover(x, z, MonthHours(0, mo)) - nn.Cloud;
                        if (added < 0.2) continue;
                        cells++;
                        string key = $"{(la < 0 ? "S" : "N")}{Math.Floor(Math.Abs(la) / 10) * 10:00} {(lo < 0 ? "W" : "E")}{Math.Floor(Math.Abs(lo) / 30) * 30:000}";
                        where[key] = where.TryGetValue(key, out int k) ? k + 1 : 1;
                    }
            Console.WriteLine($"  cells and seasons with a deck adding 20 % or more: {cells}; by 10° x 30° box:");
            foreach (var kv in where.OrderByDescending(kv => kv.Value).Take(24)) Console.WriteLine($"    {kv.Key}: {kv.Value}");
        }

        /// <summary>
        /// Weather rules 6 (13 November 2026): hail (EA-0019) and the weather's temperature (EA-0025) at stations, three game
        /// years every quarter hour. Hail days a year beside thunder days, where hail should be common (the High Plains,
        /// the Highveld, the Alpine foreland) and nearly absent (the wet tropics, Arctic coasts); and the spread of the
        /// weather's daily mean temperature in January and July against GHCN-Daily (1991-2020, tools/fit_weather_temperature.py)
        /// at the same stations. And rules 5 has none of it.
        /// </summary>
        static void CheckRules6(WeatherData d, EarthGeometry geo)
        {
            Console.WriteLine("weather rules 6: hail and the weather's temperature");
            var m6 = new WeatherModel(d, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = 6 }, 12345);
            var m5 = new WeatherModel(d, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Rules = 5 }, 12345);
            // name, lat, lon, real hail days a year (rough station climatologies), station sd of the daily mean in Jan and Jul
            // (station heights in metres; Tokyo's spread is the station fit's worst, 4.3 fitted against 1.9 measured in January)
            var sites = new (string Name, double Lat, double Lon, double Metres, double Hail, double SdJan, double SdJul)[]
            {
                ("Denver", 39.74, -104.99, 1610, 7, 5.8, 2.8), ("Kansas City", 39.10, -94.58, 300, 3, 6.2, 3.1), ("Munich", 48.14, 11.58, 520, 3, double.NaN, double.NaN),
                ("Johannesburg", -26.20, 28.05, 1750, 7, double.NaN, double.NaN), ("Moscow", 55.76, 37.62, 150, 1, 6.2, 3.3), ("London", 51.51, -0.13, 30, 1, double.NaN, double.NaN),
                ("Singapore", 1.35, 103.82, 15, 0, 1.0, 1.2), ("Mumbai", 19.08, 72.88, 10, 0, double.NaN, double.NaN), ("Tokyo", 35.68, 139.69, 40, 1, double.NaN, double.NaN),
            };
            const int years = 3;
            const double step = 0.25;
            double fullYearDays = years * DaysPerYear;
            int hailSomewhere = 0;
            foreach (var s in sites)
            {
                geo.FromGeo(s.Lat, s.Lon, out double x, out double z);
                int hailDays = 0, thunderDays = 0, hail5 = 0;
                var janMeans = new List<double>(); var julMeans = new List<double>();
                for (int day = 0; day < years * (int)DaysPerYear; day++)
                {
                    bool hail = false, thunder = false;
                    double sum = 0; int n = 0;
                    for (double h = 0; h < HoursPerDay; h += step)
                    {
                        double hours = day * HoursPerDay + h;
                        if (m6.HailAt(x, z, hours, s.Metres) >= 0.25) hail = true;
                        if (m6.ThunderAt(x, z, hours) > 0) thunder = true;
                        if (m5.HailAt(x, z, hours, s.Metres) > 0) hail5++;
                        if (h % 3 == 0) { sum += m6.TemperatureAnomaly(x, z, hours, s.Metres); n++; }
                    }
                    if (hail) hailDays++;
                    if (thunder) thunderDays++;
                    int month = (int)((day % DaysPerYear) / DaysPerYear * 12);
                    if (month == 0) janMeans.Add(sum / n);
                    if (month == 6) julMeans.Add(sum / n);
                }
                // a game year has DaysPerYear days: scale to a real year's 365
                double perYear = 365.0 / fullYearDays;
                double sdJan = Sd(janMeans), sdJul = Sd(julMeans);
                Console.WriteLine($"  {s.Name,-13} hail {hailDays * perYear,5:0.0} days a year (stations about {s.Hail}), thunder {thunderDays * perYear,5:0.0}; " +
                    $"daily mean spread Jan {sdJan:0.0} °C (station {s.SdJan:0.0}), Jul {sdJul:0.0} °C (station {s.SdJul:0.0})");
                Check(hail5 == 0, $"{s.Name}: rules 5 has no hail of Earth's");
                if (s.Hail >= 3) { Check(hailDays * perYear >= 0.5, $"{s.Name}: hail on at least half a day a year where hail is common (Denver less than real: its thunder is, EA-0022)"); hailSomewhere++; }
                if (s.Hail == 0) Check(hailDays * perYear < 1, $"{s.Name}: hail on under a day a year in the wet tropics");
                if (!double.IsNaN(s.SdJan)) Check(Math.Abs(sdJan - s.SdJan) <= 0.4 * s.SdJan + 0.5 && Math.Abs(sdJul - s.SdJul) <= 0.4 * s.SdJul + 0.5,
                    $"{s.Name}: the weather's daily mean spread within 40 % (+0.5 °C) of the station's in January and July");
                Check(m5.TemperatureAnomaly(x, z, 1000) == 0, $"{s.Name}: rules 5 leaves the temperature alone");
            }

            static double Sd(List<double> v)
            {
                if (v.Count < 2) return double.NaN;
                double mean = v.Average();
                return Math.Sqrt(v.Sum(a => (a - mean) * (a - mean)) / (v.Count - 1));
            }
        }

        static void CheckStripes(WeatherData d)
        {
            Console.WriteLine("monthly rain without one-row stripes");
            int stripes = 0, cells = 0;
            double step = 1.0 / d.Res;
            for (int m = 0; m < 12; m += 3)
                for (double lat = -55 + step / 2; lat < 70; lat += step)
                    for (double lon = -180 + step / 2; lon < 180; lon += step)
                    {
                        if (d.LandAt(lat, lon) < 0.9 || d.LandAt(lat + step, lon) < 0.9 || d.LandAt(lat - step, lon) < 0.9) continue;
                        double c = Math.Log(1 + d.MonthPrecip(m, lat, lon)), n = Math.Log(1 + d.MonthPrecip(m, lat + step, lon)), s = Math.Log(1 + d.MonthPrecip(m, lat - step, lon));
                        cells++;
                        if (Math.Abs(n - s) < 0.15 && Math.Abs(c - (n + s) / 2) > 0.4) stripes++;
                    }
            Console.WriteLine($"  {stripes} of {cells} land cells stand out from the agreeing rows around them");
            // the old bake had 1093 such cells (Siberia, Kazakhstan, Alaska); what is left (~240) lies on mountain coasts (the
            // Sierra Nevada and Cascades, Iberia, the Andes of Ecuador), where rain really changes that fast
            Check(stripes < cells / 600, "no rows of land cells stand out from their neighbours (UDel's missing months filled)");
        }

        // ------------------------------------------------------------------------------------------ climate fix

        /// <summary>
        /// climate.bin's one-row stripes (decision 0018): rows of 0.25° cells whose rain or warmest month differ a lot from
        /// both rows around them while those agree, without and with climatefix.bin; and the places the Wmap session
        /// measured (60°E and 75°E near 48.9°N, 20°E near 11.9°N in Chad).
        /// </summary>
        static void CheckClimateFix(EarthData d)
        {
            Console.WriteLine("climate.bin stripes (decision 0018)");
            if (!d.HasClimateFix) { Check(false, "climatefix.bin is there"); return; }
            // a stripe is a row offset the same way over a long stretch: per row and 5° of longitude (20 cells, at least 12 of
            // them land), the mean of each cell's difference from the mean of the rows north and south of it, in rain (over 6)
            // and in the warmest month (over 0.8 °C); single spots at mountains and coasts average out
            bool rainOnly = false;
            int Count()
            {
                int n = 0;
                double st = 1.0 / d.ClimateRes;
                for (double lat = -55 + st / 2; lat < 75; lat += st)
                    for (double lon0 = -180; lon0 < 180; lon0 += 5)
                    {
                        double sr = 0, sw = 0;
                        int k = 0;
                        for (double lon = lon0 + st / 2; lon < lon0 + 5; lon += st)
                        {
                            var c = d.SampleClimate(lat, lon);
                            var a = d.SampleClimate(lat + st, lon);
                            var b = d.SampleClimate(lat - st, lon);
                            if (c.Rain <= 0 || a.Rain <= 0 || b.Rain <= 0) continue;
                            sr += c.Rain - (a.Rain + b.Rain) / 2;
                            sw += c.TWarm - (a.TWarm + b.TWarm) / 2;
                            k++;
                        }
                        if (k >= 12 && Math.Abs(sr / k) > 6) n++;
                        if (k >= 12 && Math.Abs(sw / k) > 0.8 && !rainOnly) n++;
                    }
                return n;
            }
            int CountRain() { rainOnly = true; try { return Count(); } finally { rainOnly = false; } }
            string Probe()
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (lon, lats) in new[] { (60.0, new[] { 49.2, 48.88, 48.2 }), (75.0, new[] { 49.2, 48.88, 48.2 }), (20.0, new[] { 12.2, 11.85, 11.35 }) })
                {
                    sb.Append($"\n    {lon}E:");
                    foreach (double la in lats) { var c = d.SampleClimate(la, lon); sb.Append($" {la}N rain {c.Rain:0.0} tWarm {c.TWarm:0.0};"); }
                }
                return sb.ToString();
            }
            d.SetClimateFix(0);
            int before = Count();
            string pb = Probe();
            d.SetClimateFix(1);
            int after = Count();
            string pa = Probe();
            Console.WriteLine($"  without the fix (older worlds): {before} row spans offset like a stripe{pb}\n  with fix v1 (worlds from 0.15.1): {after}{pa}");
            Check(after < before / 3, "the fix removes most of climate.bin's one-row stripes");
            if (!d.HasClimateFixVersion(2)) { Check(false, "climatefix2.bin is there"); d.SetClimateFix(0); return; }
            // v2: the complete UDel normals (0018 Correction): no stripes either, and December's precipitation over the land
            double riga1 = d.SampleClimate(56.95, 24.1).Precip, moscow1 = d.SampleClimate(55.75, 37.62).Precip;
            d.SetClimateFix(2);
            int after2 = Count();
            string pa2 = Probe();
            double riga2 = d.SampleClimate(56.95, 24.1).Precip, moscow2 = d.SampleClimate(55.75, 37.62).Precip;
            Console.WriteLine($"  with fix v2 (new worlds): {after2}{pa2}\n    yearly precipitation v1 -> v2: Riga {riga1:0} -> {riga2:0} mm, Moscow {moscow1:0} -> {moscow2:0} mm");
            Check(after2 < before / 3, "fix v2 removes most of climate.bin's one-row stripes too");
            Check(riga2 > riga1 * 1.04 && moscow2 > moscow1 * 1.04, "fix v2 counts December's precipitation (Riga and Moscow wetter in the year)");
            // switching between versions in any order gives each world its own grid back exactly
            d.SetClimateFix(1);
            Check(Count() == after && Probe() == pa, "switching from v2 back to v1 gives v1's climate exactly");
            d.SetClimateFix(0);
            Check(Count() == before && Probe() == pb, "switching the fix off gives older worlds their climate back exactly");
            d.SetClimateFix(2);
            Check(Count() == after2 && Probe() == pa2, "switching from none to v2 gives v2's climate exactly");
            if (!d.HasClimateFixVersion(3)) { Check(false, "climatefix3.bin is there"); d.SetClimateFix(0); return; }
            // v3 (EA-0016): v2 with UDel's precipitation faded into GPCP's over a 1° coast band. The one-row switch drew rain
            // stripes along coasts where the two differ in level (Arctic coasts, Central America, SE Asia): 227 rain spans in
            // v2, 142 of them from the switch; 67 are left, nearly all following relief (CHELSA has them too). The warmest
            // month's spans stay (temperatures come from CHELSA, and v3 holds only the precipitation planes)
            int rain2 = CountRain();
            d.SetClimateFix(3);
            int after3 = Count(), rain3 = CountRain();
            string pa3 = Probe();
            double moscow3 = d.SampleClimate(55.75, 37.62).Precip;
            Console.WriteLine($"  with fix v3 (new worlds from 13 November 2026): {after3} (rain {rain2} -> {rain3}){pa3}\n    Moscow {moscow2:0} -> {moscow3:0} mm");
            Check(rain3 * 3 < rain2, "fix v3 removes most of the rain stripes along the coasts");
            Check(after3 - rain3 == after2 - rain2, "the temperatures are v2's (the warmest month's spans as in v2)");
            Check(Math.Abs(moscow3 - moscow2) < 1, "inland, away from the coast band, v3 is v2 (Moscow)");
            d.SetClimateFix(2);
            Check(Count() == after2 && Probe() == pa2, "switching from v3 back to v2 gives v2's climate exactly");
            d.SetClimateFix(0);
        }

        // ------------------------------------------------------------------------------------------ determinism

        static void CheckDeterminism(EarthData data, EarthGeometry geo, WeatherSettings settings, WeatherModel a)
        {
            Console.WriteLine("determinism");
            var b = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = settings.DaysPerYear, HoursPerDay = settings.HoursPerDay }, 12345);
            var c = new WeatherModel(data.Weather, geo, settings, 999);
            var rnd = new Random(7);
            int diff = 0, differs = 0;
            for (int i = 0; i < 2000; i++)
            {
                double x = 25600 + rnd.NextDouble() * 204800, z = 1000 + rnd.NextDouble() * 100000, h = rnd.NextDouble() * 20 * YearHours;
                var sa = a.Sample(x, z, h);
                var sb = b.Sample(x, z, h);
                if (sa.Cloudness != sb.Cloudness || sa.Wind.X != sb.Wind.X || sa.Wind.Z != sb.Wind.Z || a.WindAt(x, z, h, true).X != b.WindAt(x, z, h, true).X) diff++;
                if (Math.Abs(sa.Cloudness - c.Cloudness(x, z, h)) > 1e-9) differs++;
            }
            Check(diff == 0, "two models from the same key give the same weather everywhere (server and clients agree)");
            Check(differs > 200, $"another world's key gives other weather ({differs} of 2000 places differ)");
        }

        // ------------------------------------------------------------------------------------------ climate at places

        sealed class Stat { public int N, Rain; public double WindX, WindZ, Precip; public int Storms; }

        static Stat Measure(WeatherModel m, double x, double z, int month, int samples, int seed)
        {
            var st = new Stat();
            var rnd = new Random(seed);
            for (int i = 0; i < samples; i++)
            {
                double h = MonthHours(rnd.Next(0, 400), month, 0.25 + 0.5 * rnd.NextDouble()) + rnd.NextDouble();
                double c = m.Cloudness(x, z, h);
                st.N++;
                if (c > 0.5) { st.Rain++; st.Precip += c - 0.5; }
            }
            return st;
        }

        static void CheckSites(WeatherModel m)
        {
            Console.WriteLine("rain chance by place and month (measured over 400 years vs. the month's climate)");
            var sites = new (string name, double lat, double lon)[]
            {
                ("London", 51.5, -0.1), ("Mumbai", 19.1, 72.9), ("Rome", 41.9, 12.5), ("Tamanrasset", 22.8, 5.5), ("Singapore", 1.35, 103.8),
                ("Darwin", -12.4, 130.8), ("Moscow", 55.8, 37.6), ("Manaus", -3.1, -60.0), ("Riga", 56.95, 24.1), ("Lisbon", 38.7, -9.15),
                ("Cherrapunji", 25.3, 91.7), ("Lima", -12.05, -77.05),
            };
            var geo = m.Geo;
            var res = new Dictionary<string, (double jan, double jul, double expJan, double expJul)>();
            double worst = 0;
            foreach (var s in sites)
            {
                Pos(geo, s.lat, s.lon, out double x, out double z);
                var jan = Measure(m, x, z, 0, 3000, 1);
                var jul = Measure(m, x, z, 6, 3000, 2);
                var sj = m.Sample(x, z, MonthHours(3, 0));
                var su = m.Sample(x, z, MonthHours(3, 6));
                double fj = jan.Rain / (double)jan.N, fu = jul.Rain / (double)jul.N;
                res[s.name] = (fj, fu, sj.RainChance, su.RainChance);
                worst = Math.Max(worst, Math.Max(Math.Abs(fj - sj.RainChance) - 0.25 * sj.RainChance, Math.Abs(fu - su.RainChance) - 0.25 * su.RainChance));
                Console.WriteLine($"  {s.name,-12} Jan rains {fj * 100,5:0.0}% of the time (climate {sj.RainChance * 100,5:0.0}%, {sj.NormalPrecip,5:0.0} mm/day, prevailing {sj.Prevailing})" +
                    $"   Jul {fu * 100,5:0.0}% ({su.RainChance * 100,5:0.0}%, {su.NormalPrecip,5:0.0} mm/day, prevailing {su.Prevailing})");
            }
            Check(worst < 0.05, $"the rain chance measured matches the month's climate everywhere (worst excess {worst * 100:0.0} points beyond 25%)");
            var mumbai = res["Mumbai"];
            Check(mumbai.jul > 0.3 && mumbai.jan < 0.03, $"Mumbai: monsoon rain in July ({mumbai.jul * 100:0}%), dry January ({mumbai.jan * 100:0.0}%)");
            var rome = res["Rome"];
            Check(rome.jan > 2 * rome.jul, $"Rome: wet winter, dry summer ({rome.jan * 100:0}% vs {rome.jul * 100:0}%)");
            var tam = res["Tamanrasset"];
            Check(tam.jan < 0.02 && tam.jul < 0.04, $"Sahara: it hardly ever rains ({tam.jan * 100:0.0}%, {tam.jul * 100:0.0}%)");
            var lon = res["London"];
            Check(lon.jan > 0.06 && lon.jul > 0.04, $"London: some rain in every season ({lon.jan * 100:0}%, {lon.jul * 100:0}%)");

            // the share of the time with precipitation in routine airport reports (IEM's METAR archive, 2017-2023;
            // tools/iem_metar_precip.py): long light snowfalls in the cold, short showers in the heat
            var metar = new (string name, double jan, double jul)[]
            {
                ("London", 0.129, 0.073), ("Moscow", 0.476, 0.107), ("Riga", 0.301, 0.078), ("Rome", 0.075, 0.005),
                ("Lisbon", 0.069, 0.005), ("Singapore", 0.126, 0.077), ("Mumbai", 0.002, 0.340), ("Darwin", 0.188, 0),
            };
            double logSum = 0, worstLog = 0;
            int nLog = 0;
            string worstAt = "";
            var line = new List<string>();
            foreach (var (name, mj, mu) in metar)
                foreach (var (real, got, mon) in new[] { (mj, res[name].jan, "Jan"), (mu, res[name].jul, "Jul") })
                {
                    if (real < 0.02) continue;
                    double lr = Math.Log(Math.Max(got, 1e-4) / real);
                    logSum += lr; nLog++;
                    if (Math.Abs(lr) > Math.Abs(worstLog)) { worstLog = lr; worstAt = $"{name} {mon}"; }
                    line.Add($"{name} {mon} {got * 100:0}/{real * 100:0}");
                }
            Console.WriteLine("  raining, % of the time, model / airport reports: " + string.Join(", ", line));
            double meanRatio = Math.Exp(logSum / nLog);
            Check(meanRatio > 0.8 && meanRatio < 1.25 && Math.Abs(worstLog) < Math.Log(2),
                $"it rains for as much of the time as airport reports show (on average {meanRatio:0.00} times; furthest {worstAt}, {Math.Exp(worstLog):0.00} times)");

            // prevailing winds turn with the monsoon
            Pos(geo, 15, 70, out double ax, out double az);   // Arabian Sea off India
            var pJul = m.Sample(ax, az, MonthHours(3, 6)).Prevailing;
            var pJan = m.Sample(ax, az, MonthHours(3, 0)).Prevailing;
            Console.WriteLine($"  Arabian Sea: prevailing {pJan} in January, {pJul} in July");
            Check(pJul.FromDegrees > 180 && pJul.FromDegrees < 280 && pJul.Strength > 0.15, "the Arabian Sea's summer monsoon blows from the south-west");
            Check((pJan.FromDegrees < 100 || pJan.FromDegrees > 330), "its winter monsoon blows from the north-east");
            Pos(geo, 15, -40, out double tx, out double tz);   // the Atlantic trades
            var trade = m.Sample(tx, tz, MonthHours(3, 3)).Prevailing;
            Check(trade.FromDegrees > 20 && trade.FromDegrees < 110, $"the North Atlantic trades blow from the north-east ({trade})");
            Pos(geo, -45, 60, out double fx, out double fz);   // roaring forties
            var forties = m.Sample(fx, fz, MonthHours(3, 6)).Prevailing;
            Check(forties.FromDegrees > 220 && forties.FromDegrees < 320 && forties.Strength > 0.3, $"the roaring forties blow from the west, strongly ({forties})");
            if (m.Data.HasWindsAndClouds)
            {
                // what only the reanalysis knows: East Asia's cold winter monsoon, California's summer cloud deck over a dry sea
                // (NCEP/NCAR R1 has far too little of the decks off Peru and Namibia, so those can't be checked)
                Pos(geo, 31, 124, out double ex, out double ez);
                var eaJan = m.Sample(ex, ez, MonthHours(3, 0)).Prevailing;
                Console.WriteLine($"  East China Sea: prevailing {eaJan} in January");
                Check(eaJan.FromDegrees > 290 || eaJan.FromDegrees < 40, "East Asia's winter monsoon blows from the north / north-west");
                Pos(geo, 30, -125, out double px, out double pz);
                double cover = 0, rain = 0;
                for (int k = 0; k < 200; k++)
                {
                    var sp = m.Sample(px, pz, MonthHours(5 + k, 6) + k * 0.37);
                    cover += sp.CloudCover / 200;
                    if (sp.Cloudness > 0.5) rain += 1.0 / 200;
                }
                Console.WriteLine($"  off California in July: cloud cover {cover * 100:0}% on average, raining {rain * 100:0.0}% of the time");
                Check(cover > 0.6 && rain < 0.05, "the summer cloud deck off California: mostly cloudy, hardly any rain");
            }
            Pos(geo, 50, -25, out double nx, out double nz);
            var nAtl = m.Sample(nx, nz, MonthHours(3, 0)).Prevailing;
            Check(nAtl.FromDegrees > 200 && nAtl.FromDegrees < 300, $"the North Atlantic westerlies blow from the south-west / west ({nAtl})");
        }

        // ------------------------------------------------------------------------------------------ spells

        static void CheckSpells(WeatherModel m)
        {
            Console.WriteLine("how long rain lasts at one place (game hours; real speeds at 198 m per block)");
            foreach (var (name, lat, lon, month) in new[] { ("London", 51.5, -0.1, 0), ("Mumbai", 19.1, 72.9, 6), ("Singapore", 1.35, 103.8, 11) })
            {
                Pos(m.Geo, lat, lon, out double x, out double z);
                var spells = new List<double>();
                var dry = new List<double>();
                int flick = 0;
                const double step = 0.1;
                for (int y = 0; y < 40; y++)
                {
                    double start = MonthHours(10 + y, month, 0), end = MonthHours(10 + y, month, 1);
                    bool raining = m.Cloudness(x, z, start) > 0.5;
                    double since = start;
                    bool first = true;
                    for (double hh = start + step; hh < end; hh += step)
                    {
                        bool r = m.Cloudness(x, z, hh) > 0.5;
                        if (r == raining) continue;
                        double len = hh - since;
                        if (!first) (raining ? spells : dry).Add(len);
                        if (!first && len <= 0.25) flick++;
                        first = false;
                        raining = r;
                        since = hh;
                    }
                }
                double mean = spells.Count > 0 ? spells.Average() : 0, med = spells.Count > 0 ? spells.OrderBy(v => v).ElementAt(spells.Count / 2) : 0;
                double dryMean = dry.Count > 0 ? dry.Average() : 0;
                Console.WriteLine($"  {name} (month {month + 1}): {spells.Count} rain spells, mean {mean:0.0} h, median {med:0.0} h; dry spells mean {dryMean:0.0} h; {flick} changes within a quarter hour");
                Check(mean >= 1.5 && mean <= 14, $"{name}: rain spells last hours, not minutes or weeks");
                // (at the edge of a wet spell the showers come and go a while: about one spell in ten ends within a quarter hour)
                Check(flick <= spells.Count / 8 + 1, $"{name}: rain doesn't flicker on and off");
            }
        }

        // ------------------------------------------------------------------------------------------ storms

        /// <summary>Ranges step 5c: ground moisture is the decayed recent rain, wet in the wet season and dry in the dry one.</summary>
        static void CheckGroundMoisture(WeatherModel m)
        {
            double Mean(double lat, double lon, int month)
            {
                Pos(m.Geo, lat, lon, out double x, out double z);
                double s = 0;
                for (int y = 1; y <= 20; y++) for (int d = 0; d < 9; d++) s += GroundMoisture.At(m, x, z, MonthHours(y, month, (d + 0.5) / 9));
                return s / 180;
            }
            Pos(m.Geo, 12.0, 8.52, out double kx, out double kz);
            Check(GroundMoisture.At(m, kx, kz, MonthHours(2, 7)) == GroundMoisture.At(m, kx, kz, MonthHours(2, 7)), "ground moisture is a pure function of place and time");
            double kanoWet = Mean(12.0, 8.52, 7), kanoDry = Mean(12.0, 8.52, 1), sevilleSummer = Mean(37.39, -5.98, 6), sevilleAutumn = Mean(37.39, -5.98, 9);
            Console.WriteLine($"ground moisture: Kano August {kanoWet:F3}, February {kanoDry:F3}; Seville July {sevilleSummer:F3}, October {sevilleAutumn:F3}");
            Check(kanoWet > 0.12 && kanoDry < 0.03, "Kano's ground is wet in the monsoon and dry in the dry season");
            // (the rain that fell, in mm: Seville's October rain is several times its July's)
            Check(sevilleSummer < 0.03 && sevilleAutumn > 0.02 && sevilleAutumn > 4 * sevilleSummer, "Seville's ground dries out in summer and wets again in autumn");
        }

        static void CheckStorms(WeatherModel m)
        {
            Console.WriteLine("storms (per 365 weather days)");
            int years = 10;
            double span = years * 365 * 24;
            int trop = 0, low = 0, southAtl = 0, nwPac = 0, nAtl = 0, nAtlSeason = 0, westward = 0, poleward = 0, nAtlTracks = 0;
            double peakSum = 0;
            var seen = new HashSet<long>();
            for (double wh = 0; wh < span; wh += WeatherStorms.SlotHours)
            {
                foreach (var s in m.Storms.AliveIn(wh))
                {
                    if (s.T0 < 0 || s.T0 >= span || !seen.Add(s.Id)) continue;
                    double lat0 = s.Lat[0], lon0 = s.Lon[0];
                    if (s.Tropical)
                    {
                        trop++;
                        peakSum += s.Peak;
                        if (lat0 < -5 && lat0 > -40 && lon0 > -60 && lon0 < 15) southAtl++;
                        if (lat0 > 0 && lon0 > 110 && lon0 < 180) nwPac++;
                        if (lat0 > 0 && lon0 > -100 && lon0 < -10)
                        {
                            nAtl++;
                            double yr = m.YearRel(s.T0);
                            if (yr > 5 / 12.0 && yr < 11 / 12.0) nAtlSeason++;
                            nAtlTracks++;
                            int k = Math.Min(8, s.Lat.Length - 1);
                            if (Geo.Wrap180(s.Lon[k] - lon0) < 0) westward++;
                            int e = s.Lat.Length - 1;
                            if (s.Lat[e] > lat0 + 3) poleward++;
                        }
                    }
                    else low++;
                }
            }
            double perYear = 1.0 / years;
            Console.WriteLine($"  {trop * perYear:0} tropical cyclones a year (peak {peakSum / Math.Max(1, trop):0.00} wind units on average), {low * perYear:0} lows; NW Pacific {nwPac * perYear:0.0}, N Atlantic {nAtl * perYear:0.0} ({nAtlSeason} of {nAtl} in Jun-Nov), South Atlantic {southAtl * perYear:0.0}");
            Check(trop * perYear > 55 && trop * perYear < 120, "about as many tropical cyclones as on the Earth (~85 a year)");
            Check(low * perYear > 600 && low * perYear < 1500, "about a thousand mid-latitude lows a year");
            Check(southAtl <= trop / 100, "no tropical cyclones form in the cool South Atlantic");
            Check(nwPac > nAtl, "the western Pacific has more tropical cyclones than the Atlantic");
            Check(nAtl == 0 || nAtlSeason >= 0.8 * nAtl, "Atlantic hurricanes form in summer and autumn");
            Check(nAtlTracks == 0 || westward >= 0.6 * nAtlTracks, $"Atlantic tropical cyclones first move west ({westward} of {nAtlTracks})");
            Check(nAtlTracks == 0 || poleward >= 0.6 * nAtlTracks, $"and end up further north ({poleward} of {nAtlTracks})");

            // a storm's winds at sea
            double strongest = 0;
            WeatherStorms.Storm best = null;
            double bestWh = 0;
            for (double wh = 2 * 365 * 24; wh < 3 * 365 * 24 && best == null; wh += WeatherStorms.SlotHours)
                foreach (var s in m.Storms.AliveIn(wh))
                    if (s.Tropical && s.Peak > 1.2) { best = s; bestWh = s.T0 + s.Life * 0.5; break; }
            if (best != null)
            {
                best.At(bestWh, out double clat, out double clon, out double inten);
                double hours = bestWh / m.Settings.Pace;
                m.Geo.FromGeo(clat, clon, out double cx, out double cz);
                var ring = new List<string>();
                double bpkX = m.Geo.BlocksPerDegreeX / (111.32 * Math.Cos(clat * Math.PI / 180));
                foreach (double rKm in new[] { 0.0, best.MaxWindKm, 2 * best.MaxWindKm, 0.5 * best.RadiusKm, 0.9 * best.RadiusKm })
                {
                    var wE = m.WindAt(cx + rKm * bpkX, cz, hours, false);
                    var sE = m.Sample(cx + rKm * bpkX, cz, hours);
                    strongest = Math.Max(strongest, wE.Strength);
                    ring.Add($"{rKm:0} km: {wE.Strength:0.00} {(sE.Cloudness > 0.5 ? "rain" : "dry")} {sE.Pressure:0} hPa");
                }
                Console.WriteLine($"  {best.Name} at {clat:0.0},{clon:0.0} (intensity {inten:0.00}): east of the centre " + string.Join("; ", ring));
                Check(strongest > 0.8, "a strong tropical cyclone has storm-force winds near its eyewall");
                var sN = m.WindAt(cx + best.MaxWindKm * bpkX, cz, hours, false);
                // east of a northern storm the winds blow toward the north (anticlockwise)
                if (clat > 0) Check(sN.Z < 0, "its winds turn anticlockwise in the north");
            }
        }

        // ------------------------------------------------------------------------------------------ seam

        static void CheckSeam(WeatherModel m, EarthGeometry geo)
        {
            Console.WriteLine("Cylinder World ring");
            int same = 0, n = 0;
            double maxJump = 0;
            var rnd = new Random(3);
            for (int i = 0; i < 400; i++)
            {
                double z = 5000 + rnd.NextDouble() * 92000, h = rnd.NextDouble() * 5 * YearHours;
                double x = geo.RingStart + rnd.NextDouble() * geo.RingLength;
                if (Math.Abs(m.Cloudness(x, z, h) - m.Cloudness(x + geo.RingLength, z, h)) < 1e-6 && Math.Abs(m.WindAt(x, z, h, true).X - m.WindAt(x + geo.RingLength, z, h, true).X) < 1e-6) same++;
                n++;
                // across the jump line: the last block before it and the first after
                double a = m.Cloudness(geo.RingStart + geo.RingLength - 0.5, z, h), b = m.Cloudness(geo.RingStart + 0.5, z, h);
                maxJump = Math.Max(maxJump, Math.Abs(a - b));
            }
            Check(same == n, "the weather repeats exactly one ring over");
            Check(maxJump < 0.05, $"no step in the rain at the jump line (largest {maxJump:0.000})");
        }

        // ------------------------------------------------------------------------------------------ winds

        static void CheckWinds(WeatherModel m)
        {
            Console.WriteLine("wind strengths");
            var rnd = new Random(11);
            double sumSea = 0, max = 0;
            int nSea = 0, gales = 0;
            for (int i = 0; i < 20000; i++)
            {
                double lat = -60 + rnd.NextDouble() * 120, lon = -180 + rnd.NextDouble() * 360;
                if (m.Data.LandAt(lat, lon) > 0.1) continue;
                m.Geo.FromGeo(lat, lon, out double x, out double z);
                double h = rnd.NextDouble() * 10 * YearHours;
                var w = m.WindAt(x, z, h, true);
                sumSea += w.Strength;
                nSea++;
                max = Math.Max(max, w.Strength);
                if (w.Strength > 0.7) gales++;
            }
            double mean = sumSea / nSea;
            Console.WriteLine($"  open sea: mean {mean:0.00} wind units ({mean * 25:0.0} m/s), strongest {max:0.00}, gale or more {gales * 100.0 / nSea:0.0}% of the time");
            Check(mean > 0.2 && mean < 0.45, "the open sea's mean wind is a moderate breeze (5-11 m/s)");
            Check(max <= WeatherModel.MaxStrength + 1e-9, "never beyond vanilla's 1.5");
            Check(gales * 100.0 / nSea > 0.3 && gales * 100.0 / nSea < 12, "gales happen, but not most of the time");
        }

        // ------------------------------------------------------------------------------------------ performance

        static void CheckPerformance(WeatherModel m)
        {
            Console.WriteLine("speed");
            var rnd = new Random(5);
            int n = 200000;
            var xs = new double[n];
            var zs = new double[n];
            for (int i = 0; i < n; i++) { xs[i] = 100000 + rnd.NextDouble() * 2000; zs[i] = 40000 + rnd.NextDouble() * 2000; }
            double h0 = 3 * YearHours + 100;
            m.Cloudness(xs[0], zs[0], h0);
            var sw = Stopwatch.StartNew();
            double acc = 0;
            for (int i = 0; i < n; i++) acc += m.Cloudness(xs[i], zs[i], h0 + i * 1e-4);
            double rain = sw.Elapsed.TotalMilliseconds * 1000 / n;
            sw.Restart();
            for (int i = 0; i < n; i++) acc += m.WindAt(xs[i], zs[i], h0 + i * 1e-4, true).X;
            double wind = sw.Elapsed.TotalMilliseconds * 1000 / n;
            Console.WriteLine($"  rain cloudness {rain:0.00} µs, wind {wind:0.00} µs per call ({acc:0})");
            Check(rain < 5 && wind < 5, "fast enough to answer the game's rain and wind lookups (under 5 µs)");
        }

        // ------------------------------------------------------------------------------------------ thunder

        /// <summary>Thunder (Core.Thunder, WeatherModel.ThunderAt): three game years every quarter hour at stations with known
        /// yearly thunderstorm days; a game day has thunder when any of its samples does.</summary>
        static void CheckThunder(WeatherModel m, EarthGeometry geo)
        {
            Console.WriteLine("thunder");
            var d = m.Data;
            var sites = new (string name, double lat, double lon, double real)[]
            {
                ("Singapore", 1.35, 103.82, 170), ("Tampa", 27.98, -82.53, 85), ("Darwin", -12.46, 130.84, 85),
                ("Oklahoma City", 35.47, -97.52, 50), ("Denver", 39.74, -104.99, 40), ("Vienna", 48.21, 16.37, 26),
                ("Moscow", 55.75, 37.62, 23), ("Paris", 48.86, 2.35, 19), ("London", 51.50, -0.12, 13), ("Cairo", 30.04, 31.24, 3),
                ("Reykjavik", 64.13, -21.90, 1.5), ("Kampala", 0.31, 32.58, 240), ("Yakutsk", 62.03, 129.73, double.NaN),
                ("Central Pacific", 0.0, -150.0, double.NaN), ("Amazon", -3.0, -60.0, double.NaN),
            };
            const int Years = 20, PerHour = 4;
            var res = new Dictionary<string, (double days, double clim, double dry, double[] month, double hourShare)>();
            var sw = Stopwatch.StartNew();
            long calls = 0, mismatch = 0;
            foreach (var s in sites)
            {
                Pos(geo, s.lat, s.lon, out double x, out double z);
                int days = 0, thunderDays = 0;
                long thunderSamples = 0, drySamples = 0, samples = 0;
                var month = new double[12];
                var monthDays = new double[12];
                for (int day = 0; day < Years * (int)DaysPerYear; day++)
                {
                    bool any = false;
                    int mo = (int)(day % DaysPerYear / (DaysPerYear / 12));
                    for (int k = 0; k < 24 * PerHour; k++)
                    {
                        double h = (day + 1000 * DaysPerYear) * HoursPerDay + k / (double)PerHour;
                        double t = m.ThunderAt(x, z, h);
                        calls++;
                        samples++;
                        if (t <= 0) continue;
                        any = true;
                        thunderSamples++;
                        var sample = m.Sample(x, z, h);
                        if (Math.Abs(sample.Thunder - t) > 1e-12) mismatch++;
                        if (sample.Cloudness < 0.5) drySamples++;
                    }
                    days++;
                    monthDays[mo]++;
                    if (any) { thunderDays++; month[mo]++; }
                }
                double clim = 0;
                for (int mo = 0; mo < 12; mo++) clim += m.ThunderDays(x, z, (1000 * DaysPerYear + (mo + 0.5) * DaysPerYear / 12) * HoursPerDay) * 365.25 / 12;
                for (int mo = 0; mo < 12; mo++) month[mo] /= Math.Max(1, monthDays[mo]);
                double simDays = 365.25 * thunderDays / days;
                double dry = thunderSamples > 0 ? (double)drySamples / thunderSamples : double.NaN;
                res[s.name] = (simDays, clim, dry, month, (double)thunderSamples / samples);
                Console.WriteLine(string.Format(Ci, "  {0,-16} thunder days a year: real {1,5}, fitted {2,5:0.0}, in the weather {3,5:0.0}; thunder {4:0.00}% of the hours, {5:0}% of it dry; by month {6}",
                    s.name, double.IsNaN(s.real) ? "-" : s.real.ToString("0.#", Ci), clim, simDays, 100.0 * thunderSamples / samples, 100 * dry,
                    string.Join(" ", month.Select(v => (100 * v).ToString("0", Ci)))));
            }
            Console.WriteLine(string.Format(Ci, "  {0:0.00} µs per thunder lookup (with the rain lookups for the dry share)", sw.Elapsed.TotalMilliseconds * 1000 / calls));
            foreach (var s in sites.Where(s => !double.IsNaN(s.real) && s.name != "Kampala" && s.real >= 10))
                Check(res[s.name].days > s.real / 2 && res[s.name].days < s.real * 2,
                    $"{s.name}: {res[s.name].days:0} thunder days a year in the weather, within a factor of 2 of the real {s.real:0}");
            Check(res["Cairo"].days < 10 && res["Reykjavik"].days < 10, $"Cairo ({res["Cairo"].days:0.0}) and Reykjavik ({res["Reykjavik"].days:0.0}) hardly ever hear thunder");
            Check(res["Kampala"].days > 80, $"Kampala, one of the stormiest places, has {res["Kampala"].days:0} thunder days (real 240; WWLLN sees few African strokes)");
            foreach (var s in new[] { "Singapore", "Tampa", "Oklahoma City", "Vienna", "Moscow" })
                Check(Math.Abs(res[s].days / res[s].clim - 1) < 0.4, $"{s}: the weather's thunder days ({res[s].days:0}) match the fitted climate ({res[s].clim:0}) within 40%");
            var mos = res["Moscow"].month;
            Check(mos[0] + mos[1] + mos[11] < 0.01 && mos[5] + mos[6] + mos[7] > 0.25, "Moscow: no thunder in winter, most in June to August");
            Check(res["Yakutsk"].month[0] == 0 && res["Yakutsk"].month[6] > 0.05, "Yakutsk: none in January, some in July");
            Check(res["Central Pacific"].hourShare < res["Amazon"].hourShare / 3, $"far less over the open ocean ({res["Central Pacific"].hourShare * 100:0.00}% of the hours) than over the Amazon ({res["Amazon"].hourShare * 100:0.00}%)");
            Check(mismatch == 0, $"the weather sample's thunder is ThunderAt's ({mismatch} differ)");
            Check(res["Denver"].dry > res["Singapore"].dry + 0.1, $"dry lightning: more of Denver's thunder falls without rain at the ground ({res["Denver"].dry * 100:0}%) than Singapore's ({res["Singapore"].dry * 100:0}%)");
            // (Yakutsk's July air is half humid, so half its storms keep to the wet spells, when it often rains anyway)
            Check(res["Yakutsk"].dry > res["Moscow"].dry + 0.05, $"dry lightning: more of Yakutsk's thunder ({res["Yakutsk"].dry * 100:0}%) than Moscow's ({res["Moscow"].dry * 100:0}%)");
            foreach (var s in new[] { "Singapore", "Vienna", "Moscow", "London", "Amazon" })
                Check(res[s].dry < 0.25, $"{s}: a thunderstorm rains ({res[s].dry * 100:0}% of its thunder dry)");
            // the afternoon: over land thunder peaks in the afternoon and evening
            Pos(geo, 35.47, -97.52, out double ox, out double oz);
            double afternoon = 0, night = 0;
            for (int day = 0; day < 3 * (int)DaysPerYear; day++)
                for (int k = 0; k < 24 * PerHour; k++)
                {
                    double t = m.ThunderAt(ox, oz, (day + 2000 * DaysPerYear) * HoursPerDay + k / (double)PerHour);
                    if (k >= 12 * PerHour && k < 20 * PerHour) afternoon += t;
                    if (k < 8 * PerHour) night += t;
                }
            Check(afternoon > 2 * night, $"Oklahoma City: {afternoon / Math.Max(1e-9, night):0.0} times as much thunder from noon to 8 pm as from midnight to 8 am");
            // off with the rain
            var off = new WeatherModel(m.Data, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, RainFrequency = 0 }, 12345);
            Pos(geo, 1.35, 103.82, out double sx, out double sz);
            bool none = true;
            for (int k = 0; k < 2000 && none; k++) none = off.ThunderAt(sx, sz, 5000 + k * 0.7) == 0;
            Check(none, "no thunder when the rain is off (RainFrequency 0)");
        }

        /// <summary>
        /// The model's rain at a place, day by day over some game years, as the share of the days of each month (index 0..11)
        /// with 1 mm and more, wet spells (runs of such days) starting, under 1 and 5 mm in the ten days up to them, and
        /// passing vanilla's lightning-fire test. The model's daily rain is scaled to <paramref name="mmPerDay"/> (each month's
        /// mean, e.g. a station's), so that only how the rain is spread over the days counts.
        /// </summary>
        static double[][] RainDays(WeatherModel m, double x, double z, double[] mmPerDay, int years)
        {
            const int PerDay = 96;
            int days = years * (int)DaysPerYear;
            double t0 = 3000 * DaysPerYear * HoursPerDay;
            var dm = new double[days];
            var two = new double[days * 12];
            for (int d = 0; d < days; d++)
            {
                double sum = 0;
                for (int k = 0; k < PerDay; k++)
                {
                    double c = m.Cloudness(x, z, t0 + (d + k / (double)PerDay) * HoursPerDay);
                    sum += Math.Max(0, c - 0.5);
                    if (k % 8 == 0) two[d * 12 + k / 8] = Math.Clamp(c - 0.5, 0, 1);
                }
                dm[d] = sum / PerDay;
            }
            int Mo(int d) => (int)(d % DaysPerYear / (DaysPerYear / 12));
            var mean = new double[12];
            var cnt = new double[12];
            for (int d = 0; d < days; d++) { mean[Mo(d)] += dm[d]; cnt[Mo(d)]++; }
            var mm = new double[days];
            for (int d = 0; d < days; d++)
            {
                int mo = Mo(d);
                double s = mean[mo] / cnt[mo];
                mm[d] = s > 0 ? dm[d] * mmPerDay[mo] / s : 0;
            }
            var res = Enumerable.Range(0, 12).Select(_ => new double[5]).ToArray();
            var n = new double[12];
            for (int d = 10; d < days; d++)
            {
                int mo = Mo(d);
                n[mo]++;
                if (mm[d] >= 1) { res[mo][0]++; if (mm[d - 1] < 1) res[mo][1]++; }
                double w = 0;
                for (int k = 0; k < 10; k++) w += mm[d - k];
                if (w < 1) res[mo][2]++;
                if (w < 5) res[mo][3]++;
                double v = 0;
                for (int k = 0; k < 120; k++) v += two[d * 12 + 11 - k];
                if (v / 84 < 0.01) res[mo][4]++;
            }
            for (int mo = 0; mo < 12; mo++)
                for (int i = 0; i < 5; i++) res[mo][i] /= Math.Max(1, n[mo]);
            return res;
        }

        /// <summary>
        /// Calibration: the model's wet days (1 mm and more) and ten-day dry spells (under 1 and 5 mm, and vanilla's
        /// lightning-fire test) at the GHCN-Daily stations in the table tools/ghcnd_dryspells.py writes, month by month.
        /// </summary>
        static void DrySpellScan(EarthData data, EarthGeometry geo, string path, bool detail)
        {
            var stations = File.ReadAllLines(path).Skip(1).Select(l => l.Split('\t')).Where(c => c[12] == "1").GroupBy(c => c[0]).ToList();
            var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay }, 12345);
            var lines = new System.Collections.Concurrent.ConcurrentBag<(string name, int mo, double mm, double[] real, double[] mod, double temp)>();
            System.Threading.Tasks.Parallel.ForEach(stations, st =>
            {
                var c0 = st.First();
                Pos(geo, double.Parse(c0[2], Ci), double.Parse(c0[3], Ci), out double x, out double z);
                var realMm = new double[12];
                foreach (var c in st) realMm[int.Parse(c[4]) - 1] = double.Parse(c[6], Ci);
                var mod = RainDays(m, x, z, realMm, 20);
                foreach (var c in st)
                {
                    int mo = int.Parse(c[4]) - 1;
                    lines.Add((st.Key, mo, double.Parse(c[6], Ci),
                        new[] { double.Parse(c[7], Ci), double.Parse(c[8], Ci), double.Parse(c[9], Ci), double.Parse(c[11], Ci) },
                        mod[mo], m.Sample(x, z, MonthHours(3, mo)).NormalTemp));
                }
            });
            var all = lines.OrderBy(l => l.name).ThenBy(l => l.mo).ToList();
            if (detail)
                foreach (var g in all.GroupBy(l => l.name))
                    Console.WriteLine(string.Format(Ci, "  {0,-15} dry10<5mm real {1}\n  {2,-15}            model {3}\n  {2,-15}          vanilla {4}\n  {2,-15}   wet days real {5}\n  {2,-15}            model {6}", g.Key,
                        string.Join(" ", g.Select(l => (100 * l.real[3]).ToString("0", Ci).PadLeft(3))), "",
                        string.Join(" ", g.Select(l => (100 * l.mod[3]).ToString("0", Ci).PadLeft(3))),
                        string.Join(" ", g.Select(l => (100 * l.mod[4]).ToString("0", Ci).PadLeft(3))),
                        string.Join(" ", g.Select(l => (100 * l.real[0]).ToString("0", Ci).PadLeft(3))),
                        string.Join(" ", g.Select(l => (100 * l.mod[0]).ToString("0", Ci).PadLeft(3)))));
            foreach (var (label, sel) in new (string, Func<(string name, int mo, double mm, double[] real, double[] mod, double temp), bool>)[] {
                ("dry months (<0.7 mm/day)", l => l.mm < 0.7), ("middling (0.7-2.5)", l => l.mm >= 0.7 && l.mm < 2.5), ("wet (>=2.5)", l => l.mm >= 2.5),
                ("cold (<8 C)", l => l.temp < 8), ("mild (8-20 C)", l => l.temp >= 8 && l.temp < 20), ("warm (>=20 C)", l => l.temp >= 20) })
            {
                var s = all.Where(sel).ToList();
                if (s.Count == 0) continue;
                Console.WriteLine(string.Format(Ci, "{0,-26} n {1,3}: dry10<5 real {2:0.00} model {3:0.00} (mae {4:0.000}) vanilla {5:0.00} (mae {6:0.000}); dry10<1 real {7:0.00} model {8:0.00}; wet days real {9:0.000} model {10:0.000} (mae {11:0.000}); spells/day real {12:0.000} model {13:0.000}",
                    label, s.Count, s.Average(l => l.real[3]), s.Average(l => l.mod[3]), s.Average(l => Math.Abs(l.real[3] - l.mod[3])),
                    s.Average(l => l.mod[4]), s.Average(l => Math.Abs(l.real[3] - l.mod[4])),
                    s.Average(l => l.real[2]), s.Average(l => l.mod[2]),
                    s.Average(l => l.real[0]), s.Average(l => l.mod[0]), s.Average(l => Math.Abs(l.real[0] - l.mod[0])),
                    s.Average(l => l.real[1]), s.Average(l => l.mod[1])));
            }
        }

        /// <summary>
        /// Calibration of the rain chance: the model's wet days and ten-day dry spells at the GHCN-Daily stations (the table
        /// tools/ghcnd_dryspells.py writes) with the rain chance scaled by a range of factors (WeatherSettings.RainFrequency),
        /// one row per station, month and factor, written to <paramref name="outPath"/> as a table.
        /// </summary>
        static void RainFrequencyScan(EarthData data, EarthGeometry geo, string path, string outPath)
        {
            var factors = new[] { 0.35, 0.5, 0.7, 1.0, 1.4, 2.0, 2.8, 4.0, 5.6, 8.0 };
            var stations = File.ReadAllLines(path).Skip(1).Select(l => l.Split('\t')).Where(c => c[12] == "1").GroupBy(c => c[0]).ToList();
            var models = factors.Select(f => new WeatherModel(data.Weather, geo,
                new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, RainFrequency = f }, 12345)).ToArray();
            var rows = new System.Collections.Concurrent.ConcurrentBag<string>();
            var jobs = stations.SelectMany(st => Enumerable.Range(0, factors.Length).Select(i => (st, i))).ToList();
            System.Threading.Tasks.Parallel.ForEach(jobs, job =>
            {
                var (st, i) = job;
                var m = models[i];
                var c0 = st.First();
                double lat = double.Parse(c0[2], Ci), lon = double.Parse(c0[3], Ci);
                Pos(geo, lat, lon, out double x, out double z);
                var realMm = new double[12];
                foreach (var c in st) realMm[int.Parse(c[4]) - 1] = double.Parse(c[6], Ci);
                var mod = RainDays(m, x, z, realMm, 20);
                foreach (var c in st)
                {
                    int mo = int.Parse(c[4]) - 1;
                    var s = m.Sample(x, z, MonthHours(3, mo));
                    rows.Add(string.Join("\t", new[] { st.Key, (mo + 1).ToString(Ci), factors[i].ToString(Ci), c[6], s.NormalTemp.ToString("0.00", Ci),
                        s.NormalPrecip.ToString("0.000", Ci), s.Convective.ToString("0.000", Ci), s.RainChance.ToString("0.0000", Ci),
                        data.Weather.MonthCloud(mo, lat, lon).ToString("0.000", Ci), data.Weather.LandAt(lat, lon).ToString("0.000", Ci), c[7], c[8], c[9], c[11] }
                        .Concat(mod[mo].Select(v => v.ToString("0.0000", Ci)))));
                }
            });
            File.WriteAllLines(outPath, new[] { "name\tmonth\tfactor\tmm\ttemp\tprecip\tconv\tchance\tcloud\tland\twet\tspells\tdry1\tdry5\tm_wet\tm_spells\tm_dry1\tm_dry5\tm_fire" }
                .Concat(rows.OrderBy(r => r, StringComparer.Ordinal)));
            Console.WriteLine($"wrote {rows.Count} rows to {outPath}");
        }

        /// <summary>
        /// Rain comes in runs of wet days with dry spells between them, as at real stations (GHCN-Daily, 1971-2020; numbers
        /// from tools/ghcnd_dryspells.py): the share of a month's days with 1 mm and more, and with under 5 mm in the ten days
        /// up to them, with the model's rain scaled to the station's monthly mean.
        /// </summary>
        static void CheckDrySpells(WeatherModel m)
        {
            Console.WriteLine("wet days and ten-day dry spells against GHCN-Daily stations (real / model / vanilla's fire test)");
            var targets = new (string name, double lat, double lon, int month, double mm, double wet, double dry10)[]
            {
                ("Madrid", 40.412, -3.678, 8, 0.333, 0.048, 0.819),
                ("Madrid", 40.412, -3.678, 4, 1.660, 0.232, 0.371),
                ("Seville", 37.417, -5.879, 8, 0.173, 0.016, 0.962),
                ("Athens", 37.900, 23.750, 8, 0.219, 0.022, 0.891),
                ("Los Angeles", 34.024, -118.291, 7, 0.015, 0.004, 0.994),
                ("Sacramento", 38.555, -121.418, 7, 0.025, 0.002, 0.970),
                ("Phoenix", 33.428, -112.004, 6, 0.050, 0.008, 0.987),
                ("Elko", 40.824, -115.786, 8, 0.303, 0.063, 0.786),
                ("Darwin", -12.440, 130.838, 7, 0.047, 0.006, 0.976),
                ("Alice Springs", -23.795, 133.889, 8, 0.222, 0.040, 0.875),
                ("Denver", 39.763, -104.869, 7, 1.669, 0.198, 0.423),
                ("Dodge City", 37.761, -99.969, 7, 2.485, 0.211, 0.313),
                ("London", 51.478, -0.461, 7, 1.501, 0.261, 0.359),
                ("London", 51.478, -0.461, 1, 2.036, 0.404, 0.158),
                ("Vienna", 48.233, 16.350, 7, 2.266, 0.292, 0.191),
                ("Moscow", 55.833, 37.617, 7, 2.839, 0.337, 0.149),
                ("Moscow", 55.833, 37.617, 1, 1.579, 0.395, 0.173),
                ("Paris", 48.968, 2.428, 7, 1.819, 0.239, 0.267),
                ("Singapore", 1.350, 103.994, 11, 7.803, 0.509, 0.015),
                ("Tampa", 27.963, -82.540, 8, 6.886, 0.448, 0.028),
                ("Mumbai", 19.117, 72.850, 7, 25.875, 0.801, 0.000),
            };
            var got = new (string name, int month, double mm, double wet, double dry10, double mWet, double mDry10, double fire)[targets.Length];
            System.Threading.Tasks.Parallel.For(0, targets.Length, i =>
            {
                var t = targets[i];
                Pos(m.Geo, t.lat, t.lon, out double x, out double z);
                var mm = Enumerable.Repeat(t.mm, 12).ToArray();
                var r = RainDays(m, x, z, mm, 20)[t.month - 1];
                got[i] = (t.name, t.month, t.mm, t.wet, t.dry10, r[0], r[3], r[4]);
            });
            foreach (var g in got)
                Console.WriteLine(string.Format(Ci, "  {0,-14} month {1,2} ({2,6:0.00} mm/day): wet days {3,3:0}% / {4,3:0}%   dry ten days {5,3:0}% / {6,3:0}% / {7,3:0}%",
                    g.name, g.month, g.mm, 100 * g.wet, 100 * g.mWet, 100 * g.dry10, 100 * g.mDry10, 100 * g.fire));
            var dry = got.Where(g => g.mm < 0.7).ToList();
            var mid = got.Where(g => g.mm >= 0.7 && g.mm < 3).ToList();
            var wet = got.Where(g => g.mm >= 3).ToList();
            Check(Math.Abs(dry.Average(g => g.mWet - g.wet)) < 0.02,
                $"dry months: rain on few days ({dry.Average(g => g.mWet) * 100:0.0}% of them, real {dry.Average(g => g.wet) * 100:0.0}%)");
            Check(dry.Average(g => g.mDry10) > dry.Average(g => g.dry10) - 0.08,
                $"dry months: ten days without 5 mm of rain as often as at the stations ({dry.Average(g => g.mDry10) * 100:0}%, real {dry.Average(g => g.dry10) * 100:0}%)");
            Check(Math.Abs(mid.Average(g => g.mDry10 - g.dry10)) < 0.1 && mid.Average(g => Math.Abs(g.mDry10 - g.dry10)) < 0.15,
                $"middling months: dry spells of ten days now and then ({mid.Average(g => g.mDry10) * 100:0}%, real {mid.Average(g => g.dry10) * 100:0}%)");
            Check(Math.Abs(mid.Average(g => g.mWet - g.wet)) < 0.06,
                $"middling months: rain on as many days ({mid.Average(g => g.mWet) * 100:0}%, real {mid.Average(g => g.wet) * 100:0}%)");
            Check(Math.Abs(wet.Average(g => g.mWet - g.wet)) < 0.1,
                $"wet months: rain on as many days ({wet.Average(g => g.mWet) * 100:0}%, real {wet.Average(g => g.wet) * 100:0}%)");
            var madrid = got.First(g => g.name == "Madrid" && g.month == 8);
            Check(madrid.fire > 0.6, $"Madrid in August: dry enough for lightning to set fire on most days ({madrid.fire * 100:0}%)");
            var vienna = got.First(g => g.name == "Vienna" && g.month == 7);
            Check(vienna.mDry10 > 0.05 && vienna.mDry10 < 0.4, $"Vienna in July: a dry spell of ten days now and then ({vienna.mDry10 * 100:0}%, real {vienna.dry10 * 100:0}%)");
            // rules v4 gave Moscow's January rain or snow on half the days it has (long light snowfalls were cut short)
            foreach (var name in new[] { "Moscow", "London" })
            {
                var jan = got.First(g => g.name == name && g.month == 1);
                Check(jan.mWet > jan.wet - 0.08, $"{name} in January: rain or snow on most of the days it has ({jan.mWet * 100:0}%, real {jan.wet * 100:0}%)");
            }
        }

        /// <summary>
        /// Vanilla's test before lightning sets fire (ModSystemFireFromLightning, WeatherSystemBase.GetEnvironmentWetness): the
        /// rain of the last ten days in two-hour steps, sum of (cloudness - 0.5, 0..1) / 84, under 0.01. The share of the days
        /// in each month that pass it at places with a dry season, from Earth's rain.
        /// </summary>
        static void CheckFireDanger(WeatherModel m, EarthGeometry geo)
        {
            Console.WriteLine("fire danger: share of days with no rain in the ten days before (vanilla's lightning-fire test)");
            var sites = new (string name, double lat, double lon)[]
            {
                ("Madrid", 40.42, -3.70), ("Los Angeles", 34.05, -118.24), ("Phoenix", 33.45, -112.07), ("Denver", 39.74, -104.99),
                ("Elko", 40.83, -115.76), ("Yakutsk", 62.03, 129.73), ("Darwin", -12.46, 130.84), ("Alice Springs", -23.70, 133.88),
                ("Niamey", 13.51, 2.11), ("Kansas", 38.5, -98.0), ("Vienna", 48.21, 16.37), ("Singapore", 1.35, 103.82),
            };
            const int Years = 20;
            double stepH = 2 * HoursPerDay / 24;
            foreach (var s in sites)
            {
                Pos(geo, s.lat, s.lon, out double x, out double z);
                var dry = new double[12];
                var cnt = new double[12];
                // the rain of each 2-hour step, then a sliding ten-day window
                int steps = (int)(Years * DaysPerYear * HoursPerDay / stepH) + 120;
                double t0 = 3000 * DaysPerYear * HoursPerDay;
                var rain = new double[steps];
                for (int i = 0; i < steps; i++) rain[i] = Math.Clamp(m.Cloudness(x, z, t0 + i * stepH) - 0.5, 0, 1);
                double sum = 0;
                for (int i = 0; i < 120; i++) sum += rain[i];
                for (int i = 120; i < steps; i++)
                {
                    double h = t0 + i * stepH;
                    if (i % (int)(HoursPerDay / stepH) == 0)
                    {
                        int mo = (int)(m.YearRel(h) * 12) % 12;
                        cnt[mo]++;
                        if (sum / 84 < 0.01) dry[mo]++;
                    }
                    sum += rain[i] - rain[i - 120];
                }
                Console.WriteLine(string.Format(Ci, "  {0,-14} {1}", s.name, string.Join(" ", Enumerable.Range(0, 12).Select(i => (100 * dry[i] / Math.Max(1, cnt[i])).ToString("0", Ci).PadLeft(3)))));
            }
        }

        /// <summary>
        /// Measures how often the shower field starts a spell above a level at places with slow and fast steering winds, on
        /// the standard world and one four times as big, and fits WeatherModel's ShowerAcross and ShowerChange: Rice's
        /// ω² = across·(cells per hour)² + change / evolve², with ω from the share of days with a spell (so spells that come
        /// in clusters count once, as a thunder day does).
        ///   dotnet run -c Release -- [dataFolder] thunderrate
        /// </summary>
        static void FitShowerRate(EarthData data, LayoutInput standard)
        {
            var rows = new List<(double u2, double omega2)>();
            foreach (int scale in new[] { 1, 4 })
            {
                var inp = new LayoutInput
                {
                    WorldWidth = standard.WorldWidth * scale, CwRingStart = standard.CwRingStart * scale, CwRingLength = standard.CwRingLength * scale,
                    CwEquatorZ = standard.CwEquatorZ * scale, CwPoleDistance = standard.CwEquatorZ * scale - 48, CwBandInner = standard.CwBandInner,
                };
                var geo = EarthLayout.Build(inp, data, out _);
                var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay }, 4242);
                Console.WriteLine($"world x{scale}: {m.BlocksPerKm:0.00} blocks per km");
                foreach (var (lat, lon) in new[] { (0.0, -150.0), (12.0, -40.0), (31.0, -40.0), (40.0, -30.0), (48.0, -30.0), (55.0, 40.0), (-48.0, 90.0), (68.0, 100.0) })
                {
                    Pos(geo, lat, lon, out double x, out double z);
                    const int Days = 400, PerHour = 30;
                    var levels = new[] { 1.5, 2.0, 2.5 };
                    var hit = new int[levels.Length];
                    double u2 = 0;
                    long n = 0;
                    for (int day = 0; day < Days; day++)
                    {
                        var any = new bool[levels.Length];
                        for (int k = 0; k < 24 * PerHour; k++)
                        {
                            double g = m.ShowerField(x, z, (day + 500 * DaysPerYear) * HoursPerDay + k / (double)PerHour, out double u);
                            u2 += u * u;
                            n++;
                            for (int i = 0; i < levels.Length; i++) if (g > levels[i]) any[i] = true;
                        }
                        for (int i = 0; i < levels.Length; i++) if (any[i]) hit[i]++;
                    }
                    u2 /= n;
                    var line = new List<string>();
                    for (int i = 0; i < levels.Length; i++)
                    {
                        double share = hit[i] / (double)Days;
                        if (share <= 0 || share >= 0.98) continue;
                        double perHour = -Math.Log(1 - share) / HoursPerDay;
                        double omega = 2 * Math.PI * perHour / Math.Exp(-levels[i] * levels[i] / 2);
                        rows.Add((u2, omega * omega));
                        line.Add(string.Format(Ci, "θ {0}: {1:0.00} of days, ω {2:0.00}", levels[i], share, omega));
                    }
                    Console.WriteLine(string.Format(Ci, "  {0,6:0.0} {1,7:0.0}  {2:0.000} cells/h  ", lat, lon, Math.Sqrt(u2)) + string.Join("; ", line));
                }
            }
            // least squares for ω² = a·u² + b
            double suu = 0, su = 0, sw = 0, swu = 0, cnt = rows.Count;
            foreach (var r in rows) { suu += r.u2 * r.u2; su += r.u2; sw += r.omega2; swu += r.omega2 * r.u2; }
            double a = (cnt * swu - su * sw) / (cnt * suu - su * su), b = (sw - a * su) / cnt;
            Console.WriteLine(string.Format(Ci, "fit: ω² = {0:0.00}·(cells/h)² + {1:0.000}  →  ShowerAcross {0:0.00}, ShowerChange {2:0.00} (× evolve² {3})",
                a, b, b * WeatherModel.ShowerEvolveHours * WeatherModel.ShowerEvolveHours, WeatherModel.ShowerEvolveHours));
            foreach (var r in rows) Console.WriteLine(string.Format(Ci, "  u² {0:0.000}  ω² measured {1:0.000}, fitted {2:0.000}", r.u2, r.omega2, a * r.u2 + b));
        }

        // ------------------------------------------------------------------------------------------ settings and catch-up

        /// <summary>RainFrequency 0 means no rain at all, storms included (review 2026-10-21).</summary>
        static void CheckRainOff(EarthData data, EarthGeometry geo)
        {
            Console.WriteLine("rain switched off");
            var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, RainFrequency = 0 }, 12345);
            int raining = 0, n = 0;
            for (double wh = 2 * 365 * 24; wh < 3 * 365 * 24; wh += 97)
                foreach (var s in m.Storms.AliveIn(wh))
                {
                    if (!s.AliveAt(wh)) continue;
                    s.At(wh, out double la, out double lo, out _);
                    for (int k = 0; k < 8; k++)
                    {
                        geo.FromGeo(la + 0.3 * Math.Sin(k), lo + 0.3 * Math.Cos(k), out double x, out double z);
                        n++;
                        if (m.Cloudness(x, z, wh) > 0.5) raining++;
                    }
                }
            Check(n > 0 && raining == 0, $"RainFrequency 0: no rain anywhere, not even in storms ({raining} of {n} storm places)");
        }

        /// <summary>
        /// The server catching up on missed hours (vanilla's snow cover: up to 480 game hours, several corners per region),
        /// at the fastest pace (8): the storm caches keep the window instead of rebuilding it (review 2026-10-21).
        /// </summary>
        static void CheckCatchUp(EarthData data, EarthGeometry geo)
        {
            Console.WriteLine("catching up on missed hours");
            var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay, Pace = 8 }, 777);
            var sw = Stopwatch.StartNew();
            double h0 = 5 * YearHours;
            int calls = 0;
            for (int region = 0; region < 6; region++)
            {
                double x = 30000 + region * 30000, z = 30000 + region * 6000;
                for (int corner = 0; corner < 8; corner++)
                    for (int h = 0; h < 480; h++) { m.Cloudness(x + 511 * (corner & 1), z + 511 * ((corner >> 1) & 1), h0 + h + 0.5); calls++; }
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  6 regions x 8 corners x 480 hours at pace 8: {calls} rain lookups in {ms:0} ms");
            Check(ms < 20000, "catching up stays fast (the storms of each slot are worked out once)");

            // many threads asking for more slots than the caches hold (they trim while others read): no exceptions, and the
            // storms are the same as a fresh model's
            var shared = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay }, 31);
            var fresh = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = DaysPerYear, HoursPerDay = HoursPerDay }, 31);
            int errors = 0, mismatches = 0;
            var tasks = new System.Threading.Tasks.Task[6];
            for (int t = 0; t < tasks.Length; t++)
            {
                int seed = t;
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    var rnd = new Random(seed);
                    for (int i = 0; i < 400; i++)
                    {
                        try
                        {
                            double wh = (rnd.Next(0, 1400) + 0.5) * WeatherStorms.SlotHours;
                            var ids = shared.Storms.AliveIn(wh).Select(s => s.Id).OrderBy(v => v).ToArray();
                            if (i % 40 == 0)
                            {
                                long[] want;
                                lock (fresh) want = fresh.Storms.AliveIn(wh).Select(s => s.Id).OrderBy(v => v).ToArray();
                                if (!ids.SequenceEqual(want)) System.Threading.Interlocked.Increment(ref mismatches);
                            }
                        }
                        catch (Exception) { System.Threading.Interlocked.Increment(ref errors); }
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);
            Check(errors == 0 && mismatches == 0, $"storm caches under 6 threads and 1400 slots: {errors} errors, {mismatches} mismatches with a fresh model");
        }

        // ------------------------------------------------------------------------------------------ maps

        static void Maps(WeatherModel m, WeatherData d, string outDir)
        {
            Console.WriteLine("maps → " + outDir);
            int w = 1080, h = 480;   // 1/3 degree per pixel, 80N..80S
            double LatAt(int j) => 80 - (j + 0.5) * 160.0 / h;
            double LonAt(int i) => -180 + (i + 0.5) * 360.0 / w;

            foreach (int month in new[] { 0, 6 })
            {
                double hours = MonthHours(4, month) + 7;
                // the rain chance of the month and the prevailing winds
                var img = new byte[w * h * 3];
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        double lat = LatAt(j), lon = LonAt(i);
                        double f = WeatherModel.RainChanceFor(d.MonthPrecip(month, lat, lon), d.MonthTemp(month, lat, lon), d.MonthCloud(month, lat, lon), 1);
                        bool land = d.LandAt(lat, lon) > 0.5;
                        var (r, g, b) = Ramp(Math.Sqrt(f / 0.6));
                        if (land) { r = (byte)(r * 0.8 + 40); g = (byte)(g * 0.8 + 30); b = (byte)(b * 0.8 + 20); }
                        Set(img, w, i, j, r, g, b);
                    }
                for (double lat = -75; lat <= 75; lat += 5)
                    for (double lon = -177.5; lon < 180; lon += 5)
                    {
                        m.Geo.FromGeo(lat, lon, out double x, out double z);
                        var p = m.Prevailing(x, z, hours);
                        int ci = (int)((lon + 180) / 360 * w), cj = (int)((80 - lat) / 160 * h);
                        Arrow(img, w, h, ci, cj, p.X / 0.4 * 12, p.Z / 0.4 * 12);
                    }
                Png.WriteRgb(Path.Combine(outDir, $"climate-{(month == 0 ? "jan" : "jul")}.png"), w, h, img);

                // one moment: rain, clouds, storms, winds
                var snap = new byte[w * h * 3];
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        double lat = LatAt(j), lon = LonAt(i);
                        m.Geo.FromGeo(lat, lon, out double x, out double z);
                        var s = m.Sample(x, z, hours);
                        bool land = d.LandAt(lat, lon) > 0.5;
                        double cr = land ? 120 : 30, cg = land ? 110 : 60, cb = land ? 70 : 110;
                        double cover = s.CloudCover * 0.75;
                        cr += (215 - cr) * cover; cg += (215 - cg) * cover; cb += (220 - cb) * cover;
                        if (s.Cloudness > 0.5)
                        {
                            double p = Math.Min(1, (s.Cloudness - 0.5) / 0.8);
                            cr = 40 + 30 * (1 - p); cg = 90 + 60 * (1 - p); cb = 230;
                        }
                        Set(snap, w, i, j, (byte)cr, (byte)cg, (byte)cb);
                    }
                for (double lat = -75; lat <= 75; lat += 5)
                    for (double lon = -177.5; lon < 180; lon += 5)
                    {
                        m.Geo.FromGeo(lat, lon, out double x, out double z);
                        var wv = m.WindAt(x, z, hours, false);
                        int ci = (int)((lon + 180) / 360 * w), cj = (int)((80 - lat) / 160 * h);
                        Arrow(snap, w, h, ci, cj, wv.X / 0.4 * 12, wv.Z / 0.4 * 12);
                    }
                foreach (var s in m.Storms.AliveIn(hours * m.Settings.Pace))
                {
                    if (!s.AliveAt(hours * m.Settings.Pace)) continue;
                    for (int k = 0; k + 1 < s.Lat.Length; k++)
                        Line(snap, w, h, (int)((s.Lon[k] + 180) / 360 * w), (int)((80 - s.Lat[k]) / 160 * h), (int)((s.Lon[k + 1] + 180) / 360 * w), (int)((80 - s.Lat[k + 1]) / 160 * h),
                            s.Tropical ? (byte)255 : (byte)255, s.Tropical ? (byte)60 : (byte)200, s.Tropical ? (byte)60 : (byte)0);
                }
                Png.WriteRgb(Path.Combine(outDir, $"moment-{(month == 0 ? "jan" : "jul")}.png"), w, h, snap);
            }

            // a close look the size of a player's region: 40 000 x 20 000 blocks around the Bay of Biscay, one block in 50
            {
                int cw = 800, chh = 400;
                m.Geo.FromGeo(46, -6, out double x0, out double z0);
                double hours = MonthHours(4, 0) + 7;
                var img = new byte[cw * chh * 3];
                for (int j = 0; j < chh; j++)
                    for (int i = 0; i < cw; i++)
                    {
                        double x = x0 + (i - cw / 2) * 50, z = z0 + (j - chh / 2) * 50;
                        var g = m.Geo.ToGeo(x, z);
                        var s = m.Sample(x, z, hours);
                        bool land = d.LandAt(g.Lat, g.Lon) > 0.5;
                        double cr = land ? 120 : 30, cg = land ? 110 : 60, cb = land ? 70 : 110;
                        double cover = s.CloudCover * 0.75;
                        cr += (215 - cr) * cover; cg += (215 - cg) * cover; cb += (220 - cb) * cover;
                        if (s.Cloudness > 0.5) { double p = Math.Min(1, (s.Cloudness - 0.5) / 0.8); cr = 40 + 30 * (1 - p); cg = 90 + 60 * (1 - p); cb = 230; }
                        else if (s.Cloudness > 0) { double p = s.Cloudness / 0.5; cr *= 1 - 0.35 * p; cg *= 1 - 0.35 * p; cb *= 1 - 0.3 * p; }
                        Set(img, cw, i, j, (byte)cr, (byte)cg, (byte)cb);
                    }
                Png.WriteRgb(Path.Combine(outDir, "biscay-blocks.png"), cw, chh, img);
            }
        }

        static (byte, byte, byte) Ramp(double t)
        {
            t = Math.Clamp(t, 0, 1);
            // dry tan → green → blue
            double r = t < 0.5 ? 230 - 180 * t * 2 : 50 - 20 * (t - 0.5) * 2;
            double g = t < 0.5 ? 210 - 30 * t * 2 : 180 - 100 * (t - 0.5) * 2;
            double b = t < 0.5 ? 170 - 90 * t * 2 : 80 + 170 * (t - 0.5) * 2;
            return ((byte)r, (byte)g, (byte)b);
        }

        static void Set(byte[] img, int w, int i, int j, byte r, byte g, byte b)
        {
            int k = (j * w + i) * 3;
            img[k] = r; img[k + 1] = g; img[k + 2] = b;
        }

        static void Line(byte[] img, int w, int h, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            if (Math.Abs(x1 - x0) > w / 2) return;
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
            for (int k = 0; k <= n; k++)
            {
                int x = x0 + (x1 - x0) * k / n, y = y0 + (y1 - y0) * k / n;
                if (x >= 0 && x < w && y >= 0 && y < h) Set(img, w, x, y, r, g, b);
            }
        }

        static void Arrow(byte[] img, int w, int h, int cx, int cy, double dx, double dy)
        {
            double l = Math.Sqrt(dx * dx + dy * dy);
            if (l < 0.5) { Set(img, w, Math.Clamp(cx, 0, w - 1), Math.Clamp(cy, 0, h - 1), 0, 0, 0); return; }
            if (l > 14) { dx *= 14 / l; dy *= 14 / l; l = 14; }
            int x0 = (int)(cx - dx / 2), y0 = (int)(cy - dy / 2), x1 = (int)(cx + dx / 2), y1 = (int)(cy + dy / 2);
            Line(img, w, h, x0, y0, x1, y1, 0, 0, 0);
            double ux = dx / l, uy = dy / l;
            Line(img, w, h, x1, y1, (int)(x1 - 3 * ux - 2 * uy), (int)(y1 - 3 * uy + 2 * ux), 0, 0, 0);
            Line(img, w, h, x1, y1, (int)(x1 - 3 * ux + 2 * uy), (int)(y1 - 3 * uy - 2 * ux), 0, 0, 0);
        }
    }
}
