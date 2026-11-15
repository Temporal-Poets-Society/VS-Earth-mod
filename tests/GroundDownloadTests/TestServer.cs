using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// A small HTTP server on 127.0.0.1 for the transport's checks: serves the files of a folder with range requests, and on
/// request misbehaves the way real servers and networks do (a body that stalls after the headers, a connection cut short,
/// a range ignored, a file not there, a body without a length that never ends). One request per connection.
/// </summary>
sealed class TestServer : IDisposable
{
    public enum Mode { Normal, Stall, Cut, IgnoreRange, NotFound, Endless }

    readonly TcpListener listener;
    readonly string folder;
    readonly CancellationTokenSource stop = new();
    /// <summary>Each request: the file's name and the first byte asked for (0 without a range).</summary>
    public readonly List<(string Name, long From)> Requests = new();
    /// <summary>How to answer the n-th request (from 0) for a name; Normal when unset.</summary>
    public Func<string, int, Mode> Plan = (_, _) => Mode.Normal;
    /// <summary>Bytes of the body sent before a Cut.</summary>
    public int CutAfter = 100_000;

    public TestServer(string folder)
    {
        this.folder = folder;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(Accept);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";

    async Task Accept()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception) { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var s = client.GetStream();
                var head = new StringBuilder();
                var one = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n") && await s.ReadAsync(one, stop.Token) == 1) head.Append((char)one[0]);
                var lines = head.ToString().Split("\r\n");
                string name = Uri.UnescapeDataString(lines[0].Split(' ')[1].TrimStart('/'));
                long from = 0, to = -1;
                bool ranged = false;
                foreach (var l in lines)
                {
                    if (!l.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) continue;
                    var r = l.Substring(13).Split('-');
                    from = long.Parse(r[0]);
                    to = r[1].Length > 0 ? long.Parse(r[1]) : -1;
                    ranged = true;
                }
                Mode mode;
                lock (Requests)
                {
                    mode = Plan(name, Requests.Count(q => q.Name == name));
                    Requests.Add((name, from));
                }
                string path = Path.Combine(folder, name);
                if (mode == Mode.NotFound || !File.Exists(path))
                {
                    await Write(s, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return;
                }
                byte[] all = File.ReadAllBytes(path);
                if (mode == Mode.Endless)
                {
                    // no length, the file over and over until the client goes
                    await Write(s, "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n");
                    while (!stop.IsCancellationRequested) await s.WriteAsync(all, stop.Token);
                    return;
                }
                if (mode == Mode.IgnoreRange) ranged = false;
                if (!ranged) { from = 0; to = all.Length - 1; }
                else if (to < 0 || to >= all.Length) to = all.Length - 1;
                int length = (int)(to - from + 1);
                await Write(s, ranged
                    ? $"HTTP/1.1 206 Partial Content\r\nContent-Length: {length}\r\nContent-Range: bytes {from}-{to}/{all.Length}\r\nConnection: close\r\n\r\n"
                    : $"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n");
                if (mode == Mode.Stall) { await Task.Delay(Timeout.Infinite, stop.Token); return; }
                int send = mode == Mode.Cut ? Math.Min(length, CutAfter) : length;
                await s.WriteAsync(all.AsMemory((int)from, send), stop.Token);
                await s.FlushAsync(stop.Token);
                if (mode == Mode.Cut) client.Client.LingerState = new LingerOption(true, 0);
            }
            catch (Exception) { }
        }
    }

    Task Write(NetworkStream s, string text) => s.WriteAsync(Encoding.ASCII.GetBytes(text), stop.Token).AsTask();

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}
