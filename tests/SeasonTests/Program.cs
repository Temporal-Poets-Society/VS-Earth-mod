using System;
using EarthMap.Core;

namespace EarthMap.Tests
{
    /// <summary>
    /// Decision 0086 (seasons rules 1): the pure parts of the live temperature's year and day.
    ///  - SeasonCurve.Mean averages each month to that month's mean (random cells, and real shapes: a monsoon, a maritime
    ///    lag, a flat tropical year), is continuous across the year's end, and gives a constant year back as that constant.
    ///  - SeasonCurve.Linear passes through each month's value at its middle and is periodic.
    ///  - DayShape.At has a 24-hour mean of 0 and a range of 1 for every day length, is continuous through the day, has its
    ///    minimum near sunrise and its maximum after the solar noon, and handles polar day and night.
    ///   dotnet run -c Release
    /// </summary>
    static class Program
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        /// <summary>Hours of daylight at a latitude on a day of the year (the sun's centre on the horizon; real calendar).</summary>
        static double DayLength(double lat, int doy)
        {
            double decl = 23.44 * Math.PI / 180 * Math.Sin(2 * Math.PI * (284 + doy) / 365.0);
            double x = -Math.Tan(lat * Math.PI / 180) * Math.Tan(decl);
            return x <= -1 ? 24 : x >= 1 ? 0 : 24 * Math.Acos(x) / Math.PI;
        }

        static double MonthAverage(float[] means, int month, int samples = 2000)
        {
            double s = 0;
            for (int k = 0; k < samples; k++) s += SeasonCurve.Mean(means, (month + (k + 0.5) / samples) / 12.0);
            return s / samples;
        }

        /// <summary>`stations root tsv`: today's day-night range at the stations of tools/check_seasons.py's table (vanilla's
        /// 18 - 13 x rainfall with Earth's rainfall there), against their observed range, for decision 0086's comparison.</summary>
        static int Stations(string root, string tsv)
        {
            var data = EarthData.LoadFromFolder(root, false);
            var lines = System.IO.File.ReadAllLines(tsv);
            var head = lines[0].Split('\t');
            int iLat = Array.IndexOf(head, "lat"), iLon = Array.IndexOf(head, "lon"), iObs = Array.IndexOf(head, "obs_range"), iLay = Array.IndexOf(head, "layer_range");
            double se0 = 0, se1 = 0, b0 = 0, b1 = 0;
            int n = 0;
            foreach (var line in lines[1..])
            {
                var f = line.Split('\t');
                double lat = double.Parse(f[iLat], System.Globalization.CultureInfo.InvariantCulture), lon = double.Parse(f[iLon], System.Globalization.CultureInfo.InvariantCulture);
                double obs = double.Parse(f[iObs], System.Globalization.CultureInfo.InvariantCulture), lay = double.Parse(f[iLay], System.Globalization.CultureInfo.InvariantCulture);
                double vanilla = 18 - 13 * data.SampleClimate(lat, lon).Rain / 255.0;
                se0 += (vanilla - obs) * (vanilla - obs); b0 += vanilla - obs;
                se1 += (lay - obs) * (lay - obs); b1 += lay - obs;
                n++;
            }
            Console.WriteLine($"day-night range over {n} station-months: layer rms {Math.Sqrt(se1 / n):0.00} C (bias {b1 / n:+0.00;-0.00}), rules 0 (vanilla's 18 - 13 x rainfall) rms {Math.Sqrt(se0 / n):0.00} C (bias {b0 / n:+0.00;-0.00})");
            return 0;
        }

        /// <summary>`spell root`: the weather's spell field (WeatherModel.SeasonWeather's spell) sampled once a game day over
        /// a game year at 40 places: its spread per place, which rules 1 multiplies by the place's own day-to-day spread, so
        /// it must be about 1 (and its mean about 0) for the layer's spread to come out as the data says.</summary>
        static int Spell(string root)
        {
            var data = EarthData.LoadFromFolder(root, false);
            int W = 256000, L = 102400;
            var inp = new LayoutInput { WorldWidth = W, CwRingStart = 25600, CwRingLength = 204800, CwEquatorZ = L / 2.0, CwPoleDistance = L / 2.0 - 48, CwBandInner = 608 };
            var geo = EarthLayout.Build(inp, data, out _);
            var m = new WeatherModel(data.Weather, geo, new WeatherSettings { DaysPerYear = 108, HoursPerDay = 24 }, 12345) { Ground = data.WeatherGround };
            var rng = new Random(7);
            double sumSd = 0, sumMean = 0, minSd = double.MaxValue, maxSd = 0;
            int places = 40;
            for (int p = 0; p < places; p++)
            {
                double lat = rng.NextDouble() * 120 - 55, lon = rng.NextDouble() * 360 - 180;
                geo.FromGeo(lat, lon, out double x, out double z);
                double s = 0, s2 = 0;
                int n = 0;
                for (int day = 0; day < 108 * 3; day++)
                {
                    m.SeasonWeather(x, z, 3 * 108 * 24 + day * 24 + 12, 0, out double spell);
                    s += spell; s2 += spell * spell; n++;
                }
                double mean = s / n, sd = Math.Sqrt(Math.Max(0, s2 / n - mean * mean));
                sumSd += sd; sumMean += mean; minSd = Math.Min(minSd, sd); maxSd = Math.Max(maxSd, sd);
            }
            Console.WriteLine($"the spell field over three game years at {places} places: spread mean {sumSd / places:0.00} (from {minSd:0.00} to {maxSd:0.00}), its mean {sumMean / places:0.00}");
            return 0;
        }

        /// <summary>`paleo root`: (decision 0087) a world with the 1318 temperatures reads temperature-1318.bin through the same
        /// SampleClimate every generation reader uses: the yearly mean, coldest and warmest month at a few places, modern and
        /// 1318, and their difference (as baked: Paris about -1.1 C, Moscow's January about -2.3 C).</summary>
        static int Paleo(string root)
        {
            var data = EarthData.LoadFromFolder(root, false);
            data.SetFineTemperature(true);
            var places = new (string, double, double)[] { ("Paris", 48.9, 2.3), ("the Alps (Davos)", 46.8, 9.8), ("Moscow", 55.8, 37.6),
                ("Greenland, Eastern Settlement", 61.0, -45.5), ("Iceland", 64.1, -21.9), ("Mumbai", 19.1, 72.9) };
            var modern = new EarthData.ClimateSample[places.Length];
            for (int i = 0; i < places.Length; i++) modern[i] = data.SampleClimate(places[i].Item2, places[i].Item3);
            data.SetPaleo1318(true);
            Check(data.Paleo1318, "a world with Climate1318 reads temperature-1318.bin (" + (data.Paleo1318Error ?? "read") + ")");
            var past = new EarthData.ClimateSample[places.Length];
            for (int i = 0; i < places.Length; i++)
            {
                var p = past[i] = data.SampleClimate(places[i].Item2, places[i].Item3);
                Console.WriteLine($"  {places[i].Item1,-30} mean {modern[i].TMean,6:0.0} -> {p.TMean,6:0.0} ({p.TMean - modern[i].TMean:+0.00;-0.00}), " +
                    $"coldest {modern[i].TCold,6:0.0} -> {p.TCold,6:0.0} ({p.TCold - modern[i].TCold:+0.00;-0.00}), warmest {modern[i].TWarm,6:0.0} -> {p.TWarm,6:0.0} ({p.TWarm - modern[i].TWarm:+0.00;-0.00})");
            }
            // (Fable's release step) pinned: what world generation reads in a 1318 world at two places, as baked on
            // 16 November 2026 (temperature-1318.bin; a rebake that moves them is a change to new worlds: say so in 0087)
            Check(Math.Abs(past[0].TMean - 10.5) < 0.05 && Math.Abs(past[0].TCold - 3.0) < 0.05 && Math.Abs(past[0].TWarm - 18.5) < 0.05,
                $"pinned: Paris as around 1318 reads 10.5 / 3.0 / 18.5 C (year, coldest, warmest month at sea level): {past[0].TMean:0.00} / {past[0].TCold:0.00} / {past[0].TWarm:0.00}");
            Check(Math.Abs(past[2].TCold - (-8.5)) < 0.05 && Math.Abs(past[2].TMean - 5.0) < 0.05,
                $"pinned: Moscow as around 1318 reads 5.0 a year, -8.5 in its coldest month: {past[2].TMean:0.00} / {past[2].TCold:0.00}");
            Check(past[0].TMean < modern[0].TMean && past[2].TCold < modern[2].TCold, "1318 is colder than the modern normals there");
            data.SetPaleo1318(false);
            var back = data.SampleClimate(places[0].Item2, places[0].Item3);
            Check(back.TMean == modern[0].TMean, "turning it off gives the modern temperatures back exactly");
            return failures == 0 ? 0 : 1;
        }

        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "paleo") { Paleo(args[1]); Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED"); return failures == 0 ? 0 : 1; }
            if (args.Length >= 3 && args[0] == "stations") return Stations(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "spell") return Spell(args[1]);
            var rng = new Random(20261116);
            // the month averages, over random cells and shaped ones
            double worst = 0;
            var shapes = new System.Collections.Generic.List<float[]>
            {
                new float[] { 24.0f, 24.5f, 27.0f, 28.5f, 30.0f, 29.0f, 27.5f, 27.0f, 27.5f, 28.5f, 27.5f, 25.5f },   // Bombay
                new float[] { 9.0f, 8.5f, 9.5f, 10.5f, 12.0f, 13.5f, 14.5f, 15.0f, 16.0f, 14.5f, 12.0f, 9.5f },       // a lagging coast
                new float[] { 27.0f, 27.5f, 28.0f, 28.0f, 28.0f, 28.0f, 27.5f, 27.5f, 27.5f, 27.5f, 27.0f, 26.5f },  // Singapore
                new float[] { -38.5f, -33.0f, -20.0f, -5.0f, 7.5f, 16.5f, 19.5f, 15.0f, 6.0f, -8.0f, -27.0f, -37.0f }, // Yakutsk
            };
            for (int c = 0; c < 200; c++)
            {
                var m = new float[12];
                for (int j = 0; j < 12; j++) m[j] = (float)(rng.NextDouble() * 60 - 30);
                shapes.Add(m);
            }
            foreach (var m in shapes)
                for (int j = 0; j < 12; j++) worst = Math.Max(worst, Math.Abs(MonthAverage(m, j) - m[j]));
            Check(worst < 0.02, $"each month's average is its mean (worst {worst:0.0000} C over {shapes.Count} cells, means up to 60 C apart)");
            var flat = new float[12];
            Array.Fill(flat, 7.5f);
            double flatErr = 0;
            for (int k = 0; k < 1000; k++) flatErr = Math.Max(flatErr, Math.Abs(SeasonCurve.Mean(flat, k / 1000.0) - 7.5));
            Check(flatErr < 1e-4, $"a constant year stays constant ({flatErr:0.00000})");
            double jump = Math.Abs(SeasonCurve.Mean(shapes[3], 0.999999) - SeasonCurve.Mean(shapes[3], 0.0));
            Check(jump < 0.01, $"continuous across the year's end ({jump:0.0000} C)");
            double bombayPeak = 0, at = 0;
            for (int k = 0; k < 1200; k++) { double v = SeasonCurve.Mean(shapes[0], k / 1200.0); if (v > bombayPeak) { bombayPeak = v; at = k / 100.0; } }
            Check(at >= 4 && at < 5, $"Bombay's warmest day falls in May (at month {at:0.00})");

            // the linear quantities
            var r = new float[] { 4, 5, 7, 9, 11, 12, 12, 11, 9, 7, 5, 4 };
            bool mids = true;
            for (int j = 0; j < 12; j++) mids &= Math.Abs(SeasonCurve.Linear(r, (j + 0.5) / 12) - r[j]) < 1e-5;
            Check(mids, "the range and spread pass through each month's value at its middle");
            Check(Math.Abs(SeasonCurve.Linear(r, 0.0) - (r[11] + r[0]) / 2.0) < 1e-5, "and are periodic (the year's start between December and January)");

            // the day's shape
            double worstMean = 0, worstRange = 0, worstStep = 0;
            bool timing = true;
            for (double length = 0; length <= 24.0001; length += 0.25)
            {
                double rise = 12 - length / 2, set = 12 + length / 2;
                double s = 0, lo = double.MaxValue, hi = double.MinValue, prev = DayShape.At(0, rise, set), tLo = 0, tHi = 0;
                int n = 4800;
                for (int k = 0; k < n; k++)
                {
                    double h = (k + 0.5) * 24.0 / n, v = DayShape.At(h, rise, set);
                    s += v;
                    if (v < lo) { lo = v; tLo = h; }
                    if (v > hi) { hi = v; tHi = h; }
                    worstStep = Math.Max(worstStep, Math.Abs(v - prev));
                    prev = v;
                }
                worstMean = Math.Max(worstMean, Math.Abs(s / n));
                worstRange = Math.Max(worstRange, Math.Abs(hi - lo - 1));
                if (length >= 4 && length <= 20)
                    timing &= Math.Abs(tLo - (rise + DayShape.MinAfterSunrise)) < 0.1 && Math.Abs(tHi - (12 + DayShape.WarmestAfterNoon)) < 0.1;
            }
            Check(worstMean < 0.003, $"the day's mean is 0 for every day length (worst {worstMean:0.0000})");
            Check(worstRange < 0.01, $"its range is 1 (worst {worstRange:0.0000} off)");
            Check(worstStep < 0.02, $"continuous through the day, short polar-summer nights falling fast (largest step between 18-second samples {worstStep:0.0000})");
            Check(timing, "days of 4 to 20 hours: coldest 0.49 h after sunrise, warmest 2.09 h after noon");
            Check(Math.Abs(DayShape.At(12 + DayShape.WarmestAfterNoon, 0, 24) - 0.5) < 0.003 && Math.Abs(DayShape.At(DayShape.WarmestAfterNoon, 12, 12) + 0.5) < 0.003,
                "polar day and night: a plain swing, warmest at the same hour after noon as the fitted shape");
            Check(Math.Abs(DayShape.At(13 + DayShape.WarmestAfterNoon, 0, 24, 13) - 0.5) < 0.003,
                "a sun that doesn't set takes its noon from the sun's highest hour (AstraTerra's local clock)");
            // (Fable's review F1) no switch with the day's length: the shape for a day 0.01 h longer stays close
            double worstLength = 0, atLength = 0;
            for (double length = 0; length < 23.99; length += 0.01)
            {
                double r0 = 12 - length / 2, r1 = 12 - (length + 0.01) / 2;
                for (int k = 0; k < 288; k++)
                {
                    double h = k / 12.0, d = Math.Abs(DayShape.At(h, r1, 24 - r1) - DayShape.At(h, r0, 24 - r0));
                    if (d > worstLength) { worstLength = d; atLength = length; }
                }
            }
            Check(worstLength < 0.05, $"continuous in the day's length: a day 0.01 h longer moves the shape by at most {worstLength:0.000} of the range (at {atLength:0.00} h; a switch moved it 0.77)");
            // and day by day at the polar edges: the sun's day length from latitudes 60 to 80 over the year
            double worstDay = 0, atLat = 0;
            for (double lat = 60; lat <= 80; lat += 2)
                for (int doy = 0; doy < 365; doy++)
                {
                    double l0 = DayLength(lat, doy), l1 = DayLength(lat, doy + 1);
                    for (int k = 0; k < 288; k++)
                    {
                        double h = k / 12.0, d = Math.Abs(DayShape.At(h, 12 - l1 / 2, 12 + l1 / 2) - DayShape.At(h, 12 - l0 / 2, 12 + l0 / 2)) / Math.Max(Math.Abs(l1 - l0), 0.05);
                        if (d > worstDay) { worstDay = d; atLat = lat; }
                    }
                }
            Check(worstDay < 3, $"day by day at 60-80° through the year, the shape changes with the day's length by at most {worstDay:0.00} of the range an hour (at {atLat:0}°; the sun's own times move it, no jump)");
            // a sun on another clock (AstraTerra's local time): the same shape, moved with the sun
            Check(Math.Abs(DayShape.At(10, 4, 18) - DayShape.At(13, 7, 21)) < 1e-9, "the shape follows the sun's own times");

            // with a repo root (tests/Run-All.ps1 passes it): the 1318 climate as world generation reads it, pinned
            if (args.Length >= 1 && System.IO.Directory.Exists(System.IO.Path.Combine(args[0], "earthdata"))) Paleo(args[0]);
            Console.WriteLine(failures == 0 ? "ALL OK" : $"{failures} FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
