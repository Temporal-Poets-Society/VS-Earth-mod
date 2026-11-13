using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The storms of an Earth world, on the real Earth (latitude, longitude, kilometres), as a pure function of the key
    /// and the game time:
    ///  - Every 12 weather hours each 5° cell rolls whether a storm forms in it. Tropical cyclones form over warm sea
    ///    (the month's temperature 26-28 °C and up) 4-27° from the equator, so in the basins and seasons they have on the
    ///    Earth (none in the South Atlantic); mid-latitude lows form 28-70° from the equator, more over the sea and in
    ///    winter. At StormFrequency 1 about 85 tropical cyclones and 1000 lows form per 365 weather days, as on the Earth.
    ///  - Each one's track is worked out from where it formed, 3 hours at a time: carried by the steering winds (tropical
    ///    cyclones drift west in the trades and poleward, then turn east in the westerlies), tropical ones weakening over
    ///    land and cool water.
    ///  - Their winds turn around the centre (anticlockwise in the north) and spiral inward, strongest at the eyewall;
    ///    their rain (eyewall and spiral bands, or the band ahead of a low) and low pressure come with them.
    /// The storms alive during each 12-hour slot are worked out once for the whole Earth and cached (least recently used
    /// slots go first, so the server catching up on hundreds of past hours, vanilla's snow cover, keeps its window).
    /// </summary>
    public sealed class WeatherStorms
    {
        public const double CellDeg = 5, SlotHours = 12, Step = 3;
        public const double TropicalMaxLife = 240, LowMaxLife = 120, MaxLife = TropicalMaxLife;
        // chance per cell and slot at full suitability (cos(latitude) for the cell's area), calibrated by the tests
        public const double TropicalRate = 0.0016, LowRate = 0.0025;

        readonly WeatherModel model;
        readonly long key;

        public WeatherStorms(WeatherModel model, long key)
        {
            this.model = model;
            this.key = key ^ 0x5707A11L;
            formed = new SlotCache(1024, BuildFormed);
            alive = new SlotCache(512, BuildAlive);
        }

        public sealed class Storm
        {
            public long Id;
            public bool Tropical;
            /// <summary>Weather hours it forms at, and how long it lives.</summary>
            public double T0, Life;
            /// <summary>Strongest winds at its peak (wind units, 1.5 at most) and the radius its winds reach (km).</summary>
            public double Peak, RadiusKm;
            /// <summary>Per step of 3 weather hours from T0: centre and how much of the peak is left after land and cool water.</summary>
            public double[] Lat, Lon, Keep;

            public bool AliveAt(double wh) => wh > T0 && wh < T0 + Life;

            public string Name => (Tropical ? "Tropical cyclone " : "Low ") + ((Id & 0xFFFF) % 900 + 100);

            /// <summary>Radius of the strongest winds (km): the eyewall of a tropical cyclone, far out for a low.</summary>
            public double MaxWindKm => RadiusKm * (Tropical ? 0.12 : 0.35);

            /// <summary>Centre and strength (wind units at the strongest winds) at a weather hour.</summary>
            public void At(double wh, out double lat, out double lon, out double intensity)
            {
                double a = (wh - T0) / Life;
                double f = (wh - T0) / Step;
                int i = Math.Clamp((int)Math.Floor(f), 0, Lat.Length - 2);
                double t = Math.Clamp(f - i, 0, 1);
                lat = Lat[i] + (Lat[i + 1] - Lat[i]) * t;
                lon = Geo.Wrap180(Lon[i] + Geo.Wrap180(Lon[i + 1] - Lon[i]) * t);
                double keep = Keep[i] + (Keep[i + 1] - Keep[i]) * t;
                intensity = a <= 0 || a >= 1 ? 0 : Peak * PortableMath.Pow(PortableMath.Sin(Math.PI * a), 1.2) * keep;
            }

            /// <summary>Direction of travel (east, north) at a weather hour, unit length.</summary>
            public void Heading(double wh, out double east, out double north)
            {
                int i = Math.Clamp((int)Math.Floor((wh - T0) / Step), 0, Lat.Length - 2);
                north = Lat[i + 1] - Lat[i];
                east = Geo.Wrap180(Lon[i + 1] - Lon[i]) * PortableMath.Cos(Lat[i] * Math.PI / 180);
                double l = Math.Sqrt(east * east + north * north);
                if (l < 1e-9) { east = 1; north = 0; }
                else { east /= l; north /= l; }
            }
        }

        /// <summary>A storm's part of the weather at a place.</summary>
        public struct Effect
        {
            public WeatherWind Wind;
            /// <summary>Added to the rain field (standard deviations).</summary>
            public double RainBoost;
            public double PressureDrop;
            /// <summary>The storm whose wind is strongest here (null where none has wind here, e.g. exactly at a centre).</summary>
            public Storm Storm;
            /// <summary>0..1: how much the place is inside storms now, all together (1 − the product of each one's absence;
            /// <see cref="PresenceOf"/>). For the open sea contracts' storm presence.</summary>
            public double Presence;
            /// <summary>The storm with the highest presence here, or null.</summary>
            public Storm Present;
        }

        // ------------------------------------------------------------------------------------------ genesis

        readonly SlotCache formed, alive;

        /// <summary>
        /// Arrays per slot, each built once (concurrent askers wait for the one building it), and when the cache is over its
        /// capacity the least recently used quarter is dropped. Results never depend on what is cached.
        /// </summary>
        sealed class SlotCache
        {
            sealed class Entry
            {
                public Lazy<Storm[]> Value;
                public long Used;
            }

            sealed class Last
            {
                public long Slot;
                public Entry Entry;
            }

            readonly ConcurrentDictionary<long, Entry> map = new ConcurrentDictionary<long, Entry>();
            readonly Func<long, Storm[]> build;
            readonly int capacity;
            long clock;
            int count, trimming;
            // the slot asked for last: nearly every lookup in a game hour asks for the same one (no dictionary, no locks)
            volatile Last last;

            public SlotCache(int capacity, Func<long, Storm[]> build)
            {
                this.capacity = capacity;
                this.build = build;
            }

            public Storm[] Get(long slot)
            {
                var l = last;
                if (l != null && l.Slot == slot) return l.Entry.Value.Value;
                if (!map.TryGetValue(slot, out var e))
                {
                    // (stamped before it goes in, so a trim right after can't take it for the least recently used)
                    var made = new Entry { Value = new Lazy<Storm[]>(() => build(slot), LazyThreadSafetyMode.ExecutionAndPublication), Used = Interlocked.Increment(ref clock) };
                    e = map.GetOrAdd(slot, made);
                    // (ConcurrentDictionary.Count takes every lock: counted here instead, only when something was added)
                    if (e == made && Interlocked.Increment(ref count) > capacity) Trim();
                }
                Volatile.Write(ref e.Used, Interlocked.Increment(ref clock));
                last = new Last { Slot = slot, Entry = e };
                return e.Value.Value;
            }

            void Trim()
            {
                if (Interlocked.Exchange(ref trimming, 1) == 1) return;
                try
                {
                    // an atomic snapshot, each entry's recency read once (they keep changing while we sort)
                    var snapshot = map.ToArray();
                    if (snapshot.Length <= capacity) return;
                    var order = new (long used, long key, Entry entry)[snapshot.Length];
                    for (int i = 0; i < snapshot.Length; i++) order[i] = (Volatile.Read(ref snapshot[i].Value.Used), snapshot[i].Key, snapshot[i].Value);
                    Array.Sort(order, (a, b) => a.used.CompareTo(b.used));
                    int drop = snapshot.Length / 4;
                    for (int i = 0; i < order.Length && drop > 0; i++)
                    {
                        // never one still being built (askers are waiting on it) or one used since the snapshot
                        var en = order[i].entry;
                        if (!en.Value.IsValueCreated || Volatile.Read(ref en.Used) != order[i].used) continue;
                        if (((ICollection<KeyValuePair<long, Entry>>)map).Remove(new KeyValuePair<long, Entry>(order[i].key, en)))
                        {
                            Interlocked.Decrement(ref count);
                            drop--;
                        }
                    }
                }
                finally { Volatile.Write(ref trimming, 0); }
            }
        }

        static readonly int Bands = (int)(140 / CellDeg), Columns = (int)(360 / CellDeg);

        /// <summary>The storms forming during one slot, anywhere.</summary>
        Storm[] Formed(long slot) => formed.Get(slot);

        Storm[] BuildFormed(long slot)
        {
            var found = new List<Storm>();
            for (int b = 0; b < Bands; b++)
                for (int c = 0; c < Columns; c++)
                    if (TryForm(b, c, slot, out var s)) found.Add(s);
            return found.ToArray();
        }

        /// <summary>0..1: how suitable a place and month is for a tropical cyclone / a mid-latitude low to form.</summary>
        /// <remarks>Tropical cyclones need warm sea and the ITCZ (the monsoon trough) on their side of the equator: they spin
        /// up in its disturbances. That keeps them out of the South Atlantic, whose rain band never crosses the equator.</remarks>
        public static void Suitability(double lat, double yearRel, double itcz, WeatherData.Normals n, out double tropical, out double low)
        {
            double a = Math.Abs(lat), sea = 1 - Math.Clamp(n.Land, 0, 1), hs = lat >= 0 ? 1 : -1;
            tropical = WeatherNoise.Band(a, 4, 8, 20, 27) * WeatherNoise.SmoothStep(26, 28, n.Temp) * sea * sea * WeatherNoise.SmoothStep(1, 5, hs * itcz);
            // more lows in each hemisphere's winter (January in the north, July in the south)
            double winter = 1 + 0.4 * PortableMath.Cos(2 * Math.PI * (yearRel - (lat >= 0 ? 0.04 : 0.54)));
            low = WeatherNoise.Band(a, 28, 36, 62, 70) * (0.55 + 0.45 * sea) * winter;
        }

        bool TryForm(int band, int col, long slot, out Storm storm)
        {
            storm = null;
            ulong h = WeatherNoise.Hash(key, band, col, slot);
            double lat = 70 - (band + WeatherNoise.Unit(h, 1)) * CellDeg;
            double lon = -180 + (col + WeatherNoise.Unit(h, 2)) * CellDeg;
            double a = Math.Abs(lat);
            if (a < 4 || a > 70) return false;
            double wh0 = (slot + WeatherNoise.Unit(h, 3)) * SlotHours;
            double hours0 = wh0 / Math.Max(1e-6, model.Settings.Pace);
            double yr = model.YearRel(hours0);
            var n = model.Data.Sample(lat, lon, yr);
            Suitability(lat, yr, model.Data.ItczLat(lon, yr), n, out double trop, out double low);
            double area = PortableMath.Cos(lat * Math.PI / 180);
            double pt = TropicalRate * trop * area, pl = LowRate * low * area;
            double p = (pt + pl) * model.Settings.StormFrequency;
            if (p <= 0 || WeatherNoise.Unit(h, 0) >= p) return false;

            bool tropical = pt > pl;
            var s = new Storm
            {
                Id = (long)(h & 0x7FFFFFFFFFFFL),
                Tropical = tropical,
                T0 = wh0,
                Life = tropical ? WeatherNoise.Lerp(72, TropicalMaxLife, WeatherNoise.Unit(h, 4)) : WeatherNoise.Lerp(48, LowMaxLife, WeatherNoise.Unit(h, 4)),
                Peak = tropical ? WeatherNoise.Lerp(0.9, 1.5, PortableMath.Pow(WeatherNoise.Unit(h, 5), 1.5)) : WeatherNoise.Lerp(0.55, 1.05, WeatherNoise.Unit(h, 5)),
                RadiusKm = tropical ? WeatherNoise.Lerp(320, 650, WeatherNoise.Unit(h, 6)) : WeatherNoise.Lerp(900, 1700, WeatherNoise.Unit(h, 6)),
            };
            Track(s, lat, lon, h);
            storm = s;
            return true;
        }

        /// <summary>The storm's track: carried by the steering winds 3 hours at a time, tropical ones also drifting poleward
        /// and west (the beta drift), lows poleward; tropical ones weaken over land and cool water.</summary>
        void Track(Storm s, double lat, double lon, ulong h)
        {
            int steps = (int)Math.Ceiling(s.Life / Step) + 2;
            s.Lat = new double[steps];
            s.Lon = new double[steps];
            s.Keep = new double[steps];
            double keep = 1, pace = Math.Max(1e-6, model.Settings.Pace);
            double wobble = (WeatherNoise.Unit(h, 7) - 0.5) * 8;   // km/h of its own across the steering
            for (int i = 0; i < steps; i++)
            {
                s.Lat[i] = lat;
                s.Lon[i] = lon;
                s.Keep[i] = keep;
                double wh = s.T0 + i * Step, yr = model.YearRel(wh / pace);
                var n = model.Data.Sample(lat, lon, yr);
                double itcz = model.Data.ItczLat(lon, yr);
                WeatherModel.SteeringKmh(lat, itcz, out double east, out double north);
                double hs = lat >= 0 ? 1 : -1;
                if (s.Tropical)
                {
                    east = 0.75 * east - 2.5;
                    north = 0.75 * north + 8 * hs;
                    if (n.Land > 0.5) keep *= PortableMath.Exp(-Step / 14);
                    else if (n.Temp < 25) keep *= PortableMath.Exp(-Step / 36);
                }
                else
                {
                    // lows move a little slower than the winds aloft, and slow down as they age and fill
                    double age = (wh - s.T0) / s.Life;
                    east *= 0.8 - 0.35 * age;
                    north = north * 0.8 + 5 * hs;
                    if (n.Land > 0.5) keep *= PortableMath.Exp(-Step / 90);
                }
                double speed = Math.Sqrt(east * east + north * north);
                if (speed > 1e-6)
                {
                    double pe = -north / speed, pn = east / speed;
                    east += pe * wobble;
                    north += pn * wobble;
                }
                speed = Math.Sqrt(east * east + north * north);
                double cap = s.Tropical ? 45 : 60;
                if (speed > cap) { east *= cap / speed; north *= cap / speed; }
                lat = Math.Clamp(lat + north * Step / 111.32, -85, 85);
                lon = Geo.Wrap180(lon + east * Step / (111.32 * Math.Max(0.1, PortableMath.Cos(lat * Math.PI / 180))));
            }
        }

        /// <summary>Every storm alive at some time during the slot containing weather hour wh, anywhere.</summary>
        public Storm[] AliveIn(double wh)
        {
            long slot = (long)Math.Floor(wh / SlotHours);
            return alive.Get(slot);
        }

        Storm[] BuildAlive(long slot)
        {
            var found = new List<Storm>();
            double lo = slot * SlotHours, hi = lo + SlotHours;
            if (model.Settings.StormFrequency > 0)
            {
                long first = (long)Math.Floor((lo - MaxLife) / SlotHours);
                for (long sl = first; sl <= slot; sl++)
                    foreach (var s in Formed(sl))
                        if (s.T0 < hi && s.T0 + s.Life > lo) found.Add(s);
            }
            return found.ToArray();
        }

        // ------------------------------------------------------------------------------------------ effects

        /// <summary>The storms' wind, rain and pressure at a place (real latitude and longitude) and game time.</summary>
        public Effect Influence(double lat, double lon, double hours)
        {
            var e = new Effect();
            if (model.Settings.StormFrequency <= 0) return e;
            double wh = hours * model.Settings.Pace;
            double best = 0, bestPresence = 0, absent = 1;
            foreach (var s in AliveIn(wh))
            {
                if (!s.AliveAt(wh)) continue;
                s.At(wh, out double clat, out double clon, out double inten);
                if (inten <= 0) continue;
                double dn = (lat - clat) * 111.32;
                if (Math.Abs(dn) >= s.RadiusKm) continue;
                double de = Geo.Wrap180(lon - clon) * 111.32 * PortableMath.Cos((lat + clat) * Math.PI / 360);
                double r = Math.Sqrt(de * de + dn * dn);
                if (r >= s.RadiusKm) continue;
                var w = WindOf(s, wh, de, dn, r, clat, inten);
                e.Wind += w;
                // rain and pressure scale with the strength now
                e.RainBoost += RainOf(s, wh, de, dn, r, clat) * inten;
                e.PressureDrop += (s.Tropical ? 70 : 32) * inten * PortableMath.Pow(1 - r / s.RadiusKm, 2);
                double ws = w.Strength;
                if (ws > best) { best = ws; e.Storm = s; }
                double p = PresenceOf(s, r, inten);
                absent *= 1 - p;
                if (p > bestPresence) { bestPresence = p; e.Present = s; }
            }
            e.Presence = 1 - absent;
            return e;
        }

        /// <summary>
        /// 0..1: how much a place r km from a storm's centre is inside it now: 1 within the radius of its strongest winds (a
        /// tropical cyclone's eye and eyewall, the broad band of a low's strongest winds), falling outward as its winds do
        /// (<see cref="WindOf"/>) to 0 at its edge; times how much of its peak strength it has now (none as it forms, less as it
        /// fills or weakens over land and cool water). Near 1 is a storm's core near its peak.
        /// </summary>
        public static double PresenceOf(Storm s, double r, double inten)
        {
            double outer = s.RadiusKm, rm = s.MaxWindKm;
            if (r >= outer || inten <= 0 || s.Peak <= 0) return 0;
            double shape = r <= rm ? 1 : PortableMath.Pow(rm / r, 0.65) * (1 - WeatherNoise.SmoothStep(0.45 * outer, outer, r));
            return Math.Clamp(shape * inten / s.Peak, 0, 1);
        }

        /// <summary>A storm's wind at (de, dn) km from its centre (east, north), r km away.</summary>
        static WeatherWind WindOf(Storm s, double wh, double de, double dn, double r, double clat, double inten)
        {
            double outer = s.RadiusKm, rm = s.MaxWindKm;
            if (r < 1e-6) return default;
            double fade = 1 - WeatherNoise.SmoothStep(0.45 * outer, outer, r);
            double f = (r < rm ? r / rm : PortableMath.Pow(rm / r, 0.65)) * fade;
            double hs = clat >= 0 ? 1 : -1;
            // anticlockwise seen from above in the north (world axes: +X east, +Z south), clockwise in the south; spiralling in
            double tx = -dn / r * hs, tz = -de / r * hs;
            double ix = -de / r * 0.35, iz = dn / r * 0.35;
            double ux = tx + ix, uz = tz + iz, ul = Math.Sqrt(ux * ux + uz * uz);
            ux /= ul; uz /= ul;
            // stronger where the storm's own motion adds to its winds (the "dangerous semicircle")
            s.Heading(wh, out double me, out double mn);
            double carry = 0.12 * inten * fade * Math.Min(1, r / rm);
            return new WeatherWind(ux * inten * f + me * carry, uz * inten * f - mn * carry);
        }

        /// <summary>A storm's rain (added to the rain field, standard deviations at full strength): a tropical cyclone's
        /// eyewall and spiral bands around a clear eye; a low's broad band ahead of and poleward of its centre.</summary>
        static double RainOf(Storm s, double wh, double de, double dn, double r, double clat)
        {
            double R = s.RadiusKm;
            if (s.Tropical)
            {
                double rm = s.MaxWindKm;
                double eyewall = 3.4 * PortableMath.Exp(-PortableMath.Pow((r - rm) / (0.6 * rm), 2));
                double angle = PortableMath.Atan2(dn, de) * (clat >= 0 ? 1 : -1);
                double bands = 1.9 * PortableMath.Exp(-r / (0.55 * R)) * (0.55 + 0.45 * PortableMath.Sin(2 * angle + r / (0.11 * R)));
                double eye = WeatherNoise.SmoothStep(0.25 * rm, 0.6 * rm, r);
                return (eyewall + bands) * eye;
            }
            s.Heading(wh, out double me, out double mn);
            double hs = clat >= 0 ? 1 : -1;
            // centre of the rain shield: ahead and poleward of the low
            double ce = 0.3 * R * me, cn = 0.3 * R * mn + 0.12 * R * hs;
            double d2 = (de - ce) * (de - ce) + (dn - cn) * (dn - cn);
            return 2.3 * PortableMath.Exp(-d2 / PortableMath.Pow(0.5 * R, 2));
        }
    }
}
