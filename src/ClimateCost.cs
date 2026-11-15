using System;
using System.Diagnostics;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EarthMap
{
    /// <summary>
    /// /earth cost (EA-0070): what Earth's climate hook costs in the running game, measured while switched on (off by default:
    /// nothing is counted). Calls by mode, the time in the hook and in the weather's temperature, and how many calls asked
    /// the same block and time as the call before on that thread (what an exact per-thread memo could save).
    /// </summary>
    static class ClimateCost
    {
        internal static volatile bool On;
        static long calls, live, ticks, weatherTicks, weatherCalls, repeats, started, rainCalls, rainTicks, windCalls, windTicks;

        [ThreadStatic] static int lastX, lastY, lastZ;
        [ThreadStatic] static double lastDays;
        [ThreadStatic] static bool lastSet;

        internal static void Start()
        {
            calls = live = ticks = weatherTicks = weatherCalls = repeats = rainCalls = rainTicks = windCalls = windTicks = 0;
            started = Stopwatch.GetTimestamp();
            On = true;
        }

        internal static void Count(BlockPos pos, EnumGetClimateMode mode, double totalDays, long elapsed)
        {
            Interlocked.Increment(ref calls);
            Interlocked.Add(ref ticks, elapsed);
            if (mode != EnumGetClimateMode.WorldGenValues) Interlocked.Increment(ref live);
            if (pos == null) return;
            if (lastSet && lastX == pos.X && lastY == pos.Y && lastZ == pos.Z && lastDays == totalDays) Interlocked.Increment(ref repeats);
            lastX = pos.X; lastY = pos.Y; lastZ = pos.Z; lastDays = totalDays; lastSet = true;
        }

        internal static void CountWeather(long elapsed)
        {
            Interlocked.Increment(ref weatherCalls);
            Interlocked.Add(ref weatherTicks, elapsed);
        }

        /// <summary>(ER-14, W-6.5) Earth's rain cloudness postfix: vanilla's own climate reading asks it before Earth's hook runs,
        /// so it is outside the hook's time.</summary>
        internal static void CountRain(long elapsed)
        {
            Interlocked.Increment(ref rainCalls);
            Interlocked.Add(ref rainTicks, elapsed);
        }

        /// <summary>(ER-14) Earth's wind lookups (GetWindSpeedAt).</summary>
        internal static void CountWind(long elapsed)
        {
            Interlocked.Increment(ref windCalls);
            Interlocked.Add(ref windTicks, elapsed);
        }

        internal static string Report()
        {
            if (started == 0) return "Not measured yet: /earth cost on, play a while, then /earth cost.";
            double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            double us(long t) => t * 1e6 / Stopwatch.Frequency;
            long n = Math.Max(1, calls);
            return $"Climate hook over {seconds:0} s ({(On ? "counting" : "stopped")}): {calls} calls ({calls / Math.Max(1, seconds):0}/s, " +
                $"{live} live), {us(ticks) / n:0.00} µs each, {us(ticks) / 1000 / Math.Max(1, seconds):0.0} ms per second in all; " +
                $"the weather's temperature {weatherCalls} calls, {us(weatherTicks) / Math.Max(1, weatherCalls):0.00} µs each; " +
                $"{repeats} calls ({100.0 * repeats / n:0.0} %) asked the same block and time as the one before on their thread. " +
                $"Outside the hook: the rain cloudness {rainCalls} calls ({rainCalls / Math.Max(1, seconds):0}/s), {us(rainTicks) / Math.Max(1, rainCalls):0.00} µs each, " +
                $"{us(rainTicks) / 1000 / Math.Max(1, seconds):0.0} ms per second; the wind {windCalls} calls ({windCalls / Math.Max(1, seconds):0}/s), " +
                $"{us(windTicks) / Math.Max(1, windCalls):0.00} µs each, {us(windTicks) / 1000 / Math.Max(1, seconds):0.0} ms per second.";
        }
    }
}
