using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace EarthMap.Core
{
    /// <summary>
    /// The Earth tab's GLO-90 land heights set to "Download all before the world starts" (EA-0133: the author asked for a
    /// button that downloads everything before the game is joined). Vintage Story 1.22 runs no mod code in the main menu
    /// or on the world-creation screen: the menu reads each mod's info with Mono.Cecil and loads no mod assembly
    /// (ScreenManager.loadMods, ModContainer.LoadModInfo), and the Earth tab is the game's own drawing of worldconfig.json.
    /// The first place Earth's code runs is the server's start-up, and on a single-player world that is behind the game's
    /// loading screen, which shows the server's story-event log lines as they come (GuiScreenConnectingToServer, every
    /// 400 ms) and whose Cancel stops the server (seen by mods as IServerAPI.IsShuttingDown). So the server's start-up waits
    /// here, after the world's model is built and before the spawn chunks are made, while download all runs, and writes
    /// its progress as such lines.
    /// </summary>
    public static class DownloadFirst
    {
        /// <summary>A progress line at most every <see cref="Gap"/>, and only when the download moved on by <see cref="Step"/>
        /// percent; one at least every <see cref="Quiet"/>, so the screen doesn't look stuck on a slow connection. (On the
        /// bench, 15 November 2026: 1.8 GB in 31 s; the whole release at a tenth of that speed takes about 25 minutes,
        /// some 20 lines.)</summary>
        public static readonly TimeSpan Gap = TimeSpan.FromSeconds(20), Quiet = TimeSpan.FromMinutes(3);
        public const int Step = 5;

        /// <summary>The loading screen stops updating once all its lines pass 10,000 characters
        /// (GuiScreenConnectingToServer.updateLogText): with the game's own 20-odd lines and the time stamp the screen puts
        /// before each, at most this many progress lines of about 100 characters.</summary>
        public const int MaxLines = 40;

        public enum End
        {
            /// <summary>Every square the source has is here.</summary>
            Done,
            /// <summary>The download ended with squares it couldn't have, or stopped on an error.</summary>
            Failed,
            /// <summary>The server is stopping (the loading screen's Cancel): the download was stopped.</summary>
            Stopped,
        }

        /// <summary>The first line, said when the download starts. No size beyond the release's own: where a part of the
        /// release is mostly missing it is read whole (DemDownloader.AllFromRepo), so a size from the missing squares
        /// alone came out far too small (488 MB said, 1.8 GB read on the bench); the progress lines give the real one once
        /// the release's manifest is read.</summary>
        public static string StartLine(int here, int source, long releaseBytes)
        {
            return $"GLO-90 land heights: this new world waits while the {N(Math.Max(0, source - here))} squares not on this machine " +
                $"are downloaded from Earth's data release (up to {GB(releaseBytes)}; {N(here)} of {N(source)} already here). Cancel " +
                "goes back to the menu; opening the world again then plays while the rest downloads.";
        }

        /// <summary>
        /// Waits for the running download all of <paramref name="dl"/> to end, saying its progress through
        /// <paramref name="say"/>; stops it when <paramref name="stopping"/> turns true. Returns how it ended and the last line
        /// said.
        /// </summary>
        public static (End How, string Line) Wait(DemDownloader dl, Func<bool> stopping, Action<string> say, int pollMs = 250,
            TimeSpan? gap = null, TimeSpan? quiet = null)
        {
            var b = dl.All ?? throw new InvalidOperationException("download all hasn't been started");
            TimeSpan gapAt = gap ?? Gap, quietAt = quiet ?? Quiet, last = TimeSpan.Zero;
            var clock = Stopwatch.StartNew();
            int lastPercent = 0, lines = 0;
            while (b.Running)
            {
                if (stopping())
                {
                    dl.CancelAll();
                    string stopped = "GLO-90 land heights: the download is stopped with the world. Open the world again to play while " +
                        "the rest downloads.";
                    say(stopped);
                    return (End.Stopped, stopped);
                }
                Thread.Sleep(pollMs);
                var now = clock.Elapsed;
                int percent = Percent(b, dl.BytesDownloaded - b.DownloadedAtStart);
                if (b.Running && lines < MaxLines && (now - last >= gapAt && percent >= lastPercent + Step || now - last >= quietAt))
                {
                    say(Progress(b, dl.BytesDownloaded - b.DownloadedAtStart, now));
                    last = now;
                    lastPercent = percent;
                    lines++;
                }
            }
            long got = dl.BytesDownloaded - b.DownloadedAtStart;
            string took = Minutes(b.Ended > b.Started ? b.Ended - b.Started : clock.Elapsed);
            (End how, string line) = b.Cancelled
                ? (End.Stopped, "GLO-90 land heights: the download was stopped. The world starts; its squares download as it reaches them.")
                : b.Error != null
                    ? (End.Failed, $"GLO-90 land heights: the download stopped ({b.Error})" + (got > 0 ? $" after {GB(got)}" : "") +
                        ". The world starts; the rest downloads while you play, and the land the world reaches fetches its own squares meanwhile.")
                    : b.Failed > 0
                        ? (End.Failed, $"GLO-90 land heights: {N(b.Done + b.Skipped)} of {N(b.Total)} squares here ({GB(got)} in {took}); " +
                            $"{N(b.Failed)} couldn't be had. The world starts; they are tried again while you play.")
                        : (End.Done, $"GLO-90 land heights: every square is here ({GB(got)} downloaded in {took}). The world starts.");
            say(line);
            return (how, line);
        }

        /// <summary>How far the download is, in whole percent: by bytes once the release's manifest gave the size, else by
        /// squares.</summary>
        public static int Percent(DemDownloader.Batch b, long downloaded)
        {
            if (b.Expected > 0) return (int)Math.Clamp(downloaded * 100 / b.Expected, 0, 100);
            int todo = b.Total - b.Skipped;
            return todo <= 0 ? 100 : Math.Clamp((b.Done + b.Failed) * 100 / todo, 0, 100);
        }

        /// <summary>One progress line: percent, bytes, squares here and, once there is a rate, the time left.</summary>
        public static string Progress(DemDownloader.Batch b, long downloaded, TimeSpan elapsed)
        {
            string bytes = b.Expected > 0 ? $"{GB(downloaded)} of {GB(b.Expected)}" : GB(downloaded);
            string left = "";
            if (b.Expected > downloaded && downloaded > 0 && elapsed.TotalSeconds >= 10)
            {
                double minutes = (b.Expected - downloaded) / (downloaded / elapsed.TotalMinutes);
                left = minutes < 1 ? ", under a minute left"
                    : minutes < 60 ? $", about {Math.Ceiling(minutes):F0} min left"
                    : $", about {(int)(minutes / 60)} h {(int)(minutes % 60):00} min left";
            }
            return $"GLO-90 land heights: {Percent(b, downloaded)}% ({bytes}; {N(b.Done + b.Skipped)} of {N(b.Total)} squares here){left}.";
        }

        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        static string GB(long bytes) => bytes >= 1e9
            ? (bytes / 1e9).ToString(bytes < 10e9 ? "0.0" : "0", CultureInfo.InvariantCulture) + " GB"
            : (bytes / 1e6).ToString("0", CultureInfo.InvariantCulture) + " MB";

        static string Minutes(TimeSpan t) => t.TotalMinutes < 1 ? $"{Math.Max(1, (int)t.TotalSeconds)} s"
            : t.TotalMinutes < 60 ? $"{t.TotalMinutes:0} min" : $"{(int)t.TotalHours} h {t.Minutes:00} min";
    }
}
