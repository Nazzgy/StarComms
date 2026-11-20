using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using StarComms.Core;

namespace StarComms.App;

public sealed class DiagnosticLog : IAsyncDisposable
{
    readonly SemaphoreSlim changes = new(1);
    Channel<string>? queue; Task? writer; long dropped;
    public bool Enabled => queue != null;
    public string Folder { get; }
    readonly long rotationBytes;
    public DiagnosticLog() : this(Path.GetDirectoryName(Environment.ProcessPath)!, 2 * 1024 * 1024) { }
    internal DiagnosticLog(string directory, long rotationBytes)
    {
        Folder = directory;
        this.rotationBytes = rotationBytes;
    }
    public event Action? Failed;
    // Only allow-listed event codes and numerical IDs enter the log; never arbitrary protocol bodies or exception text.
    public void Event(string code, uint member = 0)
    {
        var q = queue; if (q == null) return;
        if (code is not ("START" or "OPERATION_FAILED" or "CONNECTED" or "HOST_STARTED" or "FLEET_CHANGED" or "LEFT")) code = "ERROR";
        if (!q.Writer.TryWrite($"{DateTimeOffset.Now:O} INFO App {code} member={member}")) Interlocked.Increment(ref dropped);
    }
    public async Task Set(bool enabled)
    {
        await changes.WaitAsync();
        try {
            var old = queue; queue = null; old?.Writer.TryComplete(); if (writer != null) await writer; writer = null;
            if (!enabled) return;
            try {
                var stream = Open(0); var q = Channel.CreateBounded<string>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true }); queue = q;
                writer = Task.Run(async () => {
                    int part = 0; StreamWriter current = stream;
                    try {
                        await current.WriteLineAsync($"{DateTimeOffset.Now:O} INFO App START {VersionInfo.Full}");
                        await current.FlushAsync();
                        await foreach (string line in q.Reader.ReadAllAsync()) {
                            await current.WriteLineAsync(line); await current.FlushAsync();
                            long lost = Interlocked.Exchange(ref dropped, 0); if (lost > 0) await current.WriteLineAsync($"{DateTimeOffset.Now:O} WARN App DROPPED count={lost}");
                            if (current.BaseStream.Length >= rotationBytes) { await current.DisposeAsync(); current = Open(++part); }
                        }
                    } catch { queue = null; q.Writer.TryComplete(); Failed?.Invoke(); }
                    finally { try { await current.DisposeAsync(); } catch { } }
                });
            } catch { queue = null; Failed?.Invoke(); }
        } finally { changes.Release(); }
    }
    StreamWriter Open(int part)
    {
        string path = Path.Combine(Folder, $"StarComms-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{part:D2}.log");
        var result = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        var own = Directory.EnumerateFiles(Folder, "StarComms-*.log").Where(p => Regex.IsMatch(Path.GetFileName(p), @"^StarComms-\d{8}-\d{6}-\d{3}-\d+-\d{2,}\.log$")).OrderByDescending(File.GetLastWriteTimeUtc).Skip(5);
        foreach (var old in own) try { File.Delete(old); } catch { }
        return result;
    }
    public async ValueTask DisposeAsync() => await Set(false);
}
