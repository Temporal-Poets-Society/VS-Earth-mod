using System.Threading.Tasks;

namespace EarthMap.Core
{
    /// <summary>
    /// (FP-01, the Fable pass of the 14 November 2026 code review) Chunk generation's wait for a GLO-90 tile ended because the
    /// server is stopping (DemDownloader.AwaitForGeneration, DemDownloader.Stopping). The game disposes the mods only once its
    /// threads have paused, and a generation thread pauses only between its chunks: a wait that went on would keep it from
    /// pausing, and the game would dispose Earth under it. The chunk that waited is noted to be made again at the next start;
    /// nothing is refused (EarthWorldGen).
    /// A TaskCanceledException: where it leaves a part of world generation the game does not guard (a map region's handlers,
    /// which wait for tiles too since FP-11), the game's thread loop ends that thread quietly (ServerThread.Process catches
    /// TaskCanceledException; any other exception is logged as fatal and stops the server once more), and the region, which
    /// the game stores only once its handlers return, is made again at the next start. Seen on the bench, 14 November 2026.
    /// </summary>
    public sealed class ServerStoppingException : TaskCanceledException
    {
        public ServerStoppingException(string message) : base(message) { }
    }
}
