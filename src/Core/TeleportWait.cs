using System;
using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// EA-0121 (15 November 2026): /earth tp into a square whose GLO-90 tile isn't on this machine fetches the tile first
    /// (ER-30), and waited for it without a limit: on the bench it twice never teleported and never said anything more.
    /// The wait now ends after <see cref="Limit"/>; this decides what the teleport does then.
    /// </summary>
    public static class TeleportWait
    {
        /// <summary>How long /earth tp waits for a square's tile before it gives up and says so.</summary>
        public static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

        /// <summary>SU-0010: how often a teleport waiting for its land asks for the column again (an autosave drops every request in
        /// progress; repeats are merged).</summary>
        public static readonly TimeSpan LandAskEvery = TimeSpan.FromSeconds(5);

        /// <summary>
        /// SU-0010: the pause after one of Earth's teleports landed through vanilla's blocking load before the next may use it.
        /// One blocking load took 6-10 s with 30-60 players exploring at 1:10; the autosave tries to pause the chunk thread
        /// every few seconds and waits 3 s each time, so it gets in between two loads instead of failing until vanilla stops.
        /// </summary>
        public static readonly TimeSpan LineGap = TimeSpan.FromSeconds(3);

        /// <summary>SU-0010: when a teleport in vanilla's blocking load hasn't landed after this long, Earth's line goes on without it
        /// (the longest load seen was 31 s).</summary>
        public static readonly TimeSpan LineBusyLimit = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Once the wait for <paramref name="fetch"/> has ended (the fetch ended, or <paramref name="waited"/> ran out): teleport
        /// when the tile is here, or when the sources answered that they don't have the square (<paramref name="lacks"/>: it
        /// keeps the built-in heights for good, and its chunks are made on them). Otherwise no teleport (while the game runs a
        /// chunk whose tile can't be had is left unmade, EA-0099: the player would land over a hole) and the message says why:
        /// <paramref name="why"/>, the fetch's last failure for the square, where it knows one.
        /// </summary>
        public static (bool Teleport, string Message) After(Task<bool> fetch, bool lacks, string why, string square, string place, TimeSpan waited)
        {
            if (fetch.IsCompletedSuccessfully && (fetch.Result || lacks)) return (true, null);
            if (string.IsNullOrWhiteSpace(why)) why = fetch.IsFaulted ? fetch.Exception?.GetBaseException().Message : null;
            if (!fetch.IsCompleted)
                return (false, $"The GLO-90 heights of {place} ({square}) are not here after " +
                    (waited.TotalSeconds >= 90 ? $"{waited.TotalMinutes:0.#} minutes" : $"{waited.TotalSeconds:0} seconds") +
                    (why != null ? $" (the last try: {why})" : "") + "; you were not teleported. The download goes on: try /earth tp again in a while.");
            return (false, $"The GLO-90 heights of {place} ({square}) could not be had ({why ?? "see the server log"}); you were not teleported. " +
                "Chunks there are not made while the heights are missing; try again once the download works.");
        }
    }
}
