using System;
using System.Collections.Generic;
using System.IO;

namespace EarthMap.Core
{
    /// <summary>
    /// The river courses a world's rivers are laid out on: rivers.bin's reaches (kept until the network is built, then read
    /// again on demand), and the rivers on GLO-90 (rivers-glo90.bin) with the overlays its river courses rules apply (the
    /// snap and the side arms, versions 1 and 2), read anew each time: only start-up and the river audit need them.
    /// </summary>
    public sealed class RiverSourceRepository
    {
        /// <summary>0053: the rivers on GLO-90 (tools/RiverSnap): rivers.bin's reaches with their courses snapped to
        /// GLO-90's valley floors and levels from GLO-90, in rivers.bin's format.</summary>
        public const string Glo90RiversFile = "rivers-glo90.bin";

        // where the data came from (a folder holding the files, or the mod zip), to read rivers.bin again after ReleaseRivers
        readonly Func<string, Stream> openSource;
        bool riversReleased;
        int glo90Courses = -1;
        ulong glo90Hash;
        long glo90HashLength = -1;

        /// <summary>rivers-glo90.bin's Sha8: as Glo90RiverCourses found it when the file is that one, else worked out.</summary>
        ulong Glo90Hash(byte[] file) => file.LongLength == glo90HashLength ? glo90Hash : EarthBinary.Sha8(file);

        internal RiverSourceRepository(Func<string, Stream> open) { openSource = open; }

        // EA-0108 (the Fable pass's FP-18): rivers.bin is read at the first ask, not when the data loads: worlds on river courses
        // 1 and later (every new world) build from rivers-glo90.bin and never read it, and it held 135 MB through the start's peak
        readonly object riversGate = new();
        bool riversWanted, riversRead;
        List<RiverReach> rivers;

        /// <summary>The large rivers (rivers.bin), read at the first ask; null when the file isn't there, wasn't requested, or
        /// after <see cref="ReleaseRivers"/> (<see cref="RiverReaches()"/> reads it again then).</summary>
        public List<RiverReach> Rivers
        {
            get
            {
                lock (riversGate)
                {
                    if (riversWanted && !riversRead && !riversReleased)
                    {
                        riversRead = true;
                        using var rs = openSource?.Invoke("rivers.bin");
                        if (rs != null) rivers = RiverData.Read(rs);
                    }
                    return rivers;
                }
            }
        }
        /// <summary>The moved reaches of the rivers on GLO-90 (rivers-glo90-snap.bin), or null.</summary>
        public RiverSnapData RiverSnap { get; private set; }
        /// <summary>The side arms on the snapped rivers on GLO-90 (rivers-glo90-arms.bin), or null.</summary>
        public RiverArmsData RiverArms { get; private set; }
        /// <summary>0063 (courses rules 4): the reaches moved onto the mask's water with their widths (rivers-glo90-snap-2.bin),
        /// or null.</summary>
        public RiverSnapData RiverSnap2 { get; private set; }
        /// <summary>0063 (courses rules 4): the side arms on those (rivers-glo90-arms-2.bin), or null.</summary>
        public RiverArmsData RiverArms2 { get; private set; }
        /// <summary>Decision 0092 (courses rules 5): rules 4's reaches moved on GLO-90's pixel-is-point grid
        /// (rivers-glo90-snap-3.bin), or null.</summary>
        public RiverSnapData RiverSnap3 { get; private set; }
        /// <summary>Decision 0092 (courses rules 5): the side arms on those (rivers-glo90-arms-3.bin), or null.</summary>
        public RiverArmsData RiverArms3 { get; private set; }

        /// <summary>rivers.bin, when it is there, read at the first ask of <see cref="Rivers"/> (EarthDataset.Load with terrain).</summary>
        internal void WantRivers() => riversWanted = true;

        /// <summary>The overlays of the rivers on GLO-90 that are there (EarthDataset.Load with terrain).</summary>
        internal void ReadOverlays(Func<string, Stream> open)
        {
            using var snap = open("rivers-glo90-snap.bin");
            if (snap != null) RiverSnap = RiverSnapData.Read(snap);
            using var arms = open("rivers-glo90-arms.bin");
            if (arms != null) RiverArms = RiverArmsData.Read(arms);
            using var snap2 = open("rivers-glo90-snap-2.bin");
            if (snap2 != null) RiverSnap2 = RiverSnapData.Read(snap2);
            using var arms2 = open("rivers-glo90-arms-2.bin");
            if (arms2 != null) RiverArms2 = RiverArmsData.Read(arms2);
            using var snap3 = open("rivers-glo90-snap-3.bin");
            if (snap3 != null) RiverSnap3 = RiverSnapData.Read(snap3);
            using var arms3 = open("rivers-glo90-arms-3.bin");
            if (arms3 != null) RiverArms3 = RiverArmsData.Read(arms3);
        }

        /// <summary>
        /// Drops the river reaches (about 3 M small objects) once the world's river network is built: nothing reads them while
        /// chunks generate (decision 0025). <see cref="RiverReaches()"/> reads them again from the mod for whoever needs them
        /// later (the river audit, another model).
        /// </summary>
        public void ReleaseRivers()
        {
            lock (riversGate)
            {
                // (EA-0108) one never read is released too: nothing reads it until RiverReaches() asks
                if (!riversWanted || openSource == null || riversRead && rivers == null) return;
                rivers = null;
                riversReleased = true;
            }
        }

        /// <summary>The rivers.bin reaches: as loaded, or read again from the mod after <see cref="ReleaseRivers"/> (a new list,
        /// equal to the released one); null without the file or when it wasn't requested.</summary>
        public List<RiverReach> RiverReaches()
        {
            bool had;
            lock (riversGate)
            {
                if (!riversReleased) return Rivers;
                had = riversRead;
            }
            using var s = openSource?.Invoke("rivers.bin");
            // (EA-0108) released before it was ever read: null without the file, as an ask before the release would have given
            if (s == null) { if (had) throw new FileNotFoundException("rivers.bin"); return null; }
            return RiverData.Read(s);
        }

        /// <summary>The reaches a world's river courses rules read: rivers.bin (0) or rivers-glo90.bin (1 and later, read anew
        /// each time: only start-up and the river audit need them).</summary>
        public List<RiverReach> RiverReaches(int courses)
        {
            if (courses < 1) return RiverReaches();
            byte[] file;
            using (var s = openSource?.Invoke(Glo90RiversFile) ?? throw new FileNotFoundException("earthdata/" + Glo90RiversFile)) file = EarthBinary.ReadAll(s);
            var list = RiverData.Read(file, Glo90RiversFile);
            // courses rules 5 (decision 0092): rules 4's on GLO-90's pixel-is-point grid (rivers-glo90-snap-3.bin,
            // rivers-glo90-arms-3.bin)
            if (courses >= RiverNetwork.Glo90PointCourses)
            {
                var snap3 = RiverSnap3 ?? throw new FileNotFoundException("earthdata/rivers-glo90-snap-3.bin");
                snap3.ApplyTo(list, Glo90Hash(file));
                var arms3 = RiverArms3 ?? throw new FileNotFoundException("earthdata/rivers-glo90-arms-3.bin");
                if (!arms3.Fits(snap3)) throw new InvalidDataException("rivers-glo90-arms-3.bin was made for another rivers-glo90-snap-3.bin");
                return arms3.ApplyTo(list);
            }
            // courses rules 4 (0063): every reach from 20 m³/s on the mask's water, smoothed, with the mask's widths, and
            // the side arms built on those (rivers-glo90-snap-2.bin, rivers-glo90-arms-2.bin)
            if (courses >= RiverNetwork.Glo90WidthCourses)
            {
                var snap2 = RiverSnap2 ?? throw new FileNotFoundException("earthdata/rivers-glo90-snap-2.bin");
                snap2.ApplyTo(list, Glo90Hash(file));
                var arms2 = RiverArms2 ?? throw new FileNotFoundException("earthdata/rivers-glo90-arms-2.bin");
                if (!arms2.Fits(snap2)) throw new InvalidDataException("rivers-glo90-arms-2.bin was made for another rivers-glo90-snap-2.bin");
                return arms2.ApplyTo(list);
            }
            // courses rules 2: with the reaches the water atlas moved into the water mask's rivers
            if (courses >= RiverNetwork.Glo90SnapCourses)
                (RiverSnap ?? throw new FileNotFoundException("earthdata/rivers-glo90-snap.bin")).ApplyTo(list, Glo90Hash(file));
            // courses rules 3: and the side arms round the islands the water mask shows in the rivers
            if (courses >= RiverNetwork.Glo90ArmsCourses)
            {
                var arms = RiverArms ?? throw new FileNotFoundException("earthdata/rivers-glo90-arms.bin");
                if (!arms.Fits(RiverSnap)) throw new InvalidDataException("rivers-glo90-arms.bin was made for another rivers-glo90-snap.bin");
                list = arms.ApplyTo(list);
            }
            return list;
        }

        /// <summary>The river courses rules a new world on GLO-90 gets: 5 with rivers-glo90-snap-3.bin made for the installed
        /// rivers-glo90.bin and rivers-glo90-arms-3.bin made for its result; else 4 with rivers-glo90-snap-2.bin made for the installed
        /// rivers-glo90.bin and rivers-glo90-arms-2.bin made for its result; else 3 with rivers-glo90-snap.bin made for the
        /// installed rivers-glo90.bin (or that file already snapped) and rivers-glo90-arms.bin made for the snapped rivers, 2
        /// with the snap alone, 1 with rivers-glo90.bin alone, 0 without it. Reads the file once; a file that can't be read
        /// gives 0 and its reason in Glo90RiversError.</summary>
        public int Glo90RiverCourses
        {
            get
            {
                if (glo90Courses >= 0) return glo90Courses;
                int v = 0;
                try
                {
                    using var s = openSource?.Invoke(Glo90RiversFile);
                    if (s != null)
                    {
                        byte[] bytes = EarthBinary.ReadAll(s);
                        ulong hash = EarthBinary.Sha8(bytes);
                        // (EA-0092 A6) kept for RiverReaches, which reads the same file again: one hash of it a start
                        glo90Hash = hash; glo90HashLength = bytes.LongLength;
                        v = RiverSnap3 != null && RiverSnap3.Fits(hash) && RiverArms3 != null && RiverArms3.Fits(RiverSnap3) ? RiverNetwork.Glo90PointCourses
                            : RiverSnap2 != null && RiverSnap2.Fits(hash) && RiverArms2 != null && RiverArms2.Fits(RiverSnap2) ? RiverNetwork.Glo90WidthCourses
                            : RiverSnap == null || !RiverSnap.Fits(hash) ? 1
                            : RiverArms != null && RiverArms.Fits(RiverSnap) ? RiverNetwork.Glo90ArmsCourses : RiverNetwork.Glo90SnapCourses;
                    }
                }
                catch (Exception e) { v = 0; Glo90RiversError = "earthdata/" + Glo90RiversFile + ": " + e.Message; }
                return glo90Courses = v;
            }
        }

        /// <summary>Why rivers-glo90.bin could not be read when Glo90RiverCourses looked (a new world on GLO-90 then gets
        /// courses rules 0, the built-in rivers), or null.</summary>
        public string Glo90RiversError { get; private set; }
    }
}
