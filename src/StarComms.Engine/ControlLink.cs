using System.Threading.Channels;

namespace StarComms.Engine;

public sealed class ControlLink : IAsyncDisposable
{
    readonly Stream stream;
    readonly Channel<Envelope> outbound = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly CancellationTokenSource stop = new();
    readonly Task writer;
    public CancellationToken Token => stop.Token;
    public ControlLink(Stream stream) { this.stream = stream; writer = Task.Run(WriteLoop); }
    public bool Send(string type, object body, Guid? id = null, Guid? replyTo = null) => Send(Envelope.Make(type, body, id, replyTo));
    public bool Send(Envelope e) { if (outbound.Writer.TryWrite(e)) return true; stop.Cancel(); stream.Dispose(); return false; }
    async Task WriteLoop()
    {
        try { await foreach (var e in outbound.Reader.ReadAllAsync(stop.Token)) await Wire.Write(stream, e, stop.Token); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { stop.Cancel(); stream.Dispose(); }
    }
    public Task<Envelope> Read(CancellationToken ct) => Wire.Read(stream, ct);
    public async ValueTask DisposeAsync() { outbound.Writer.TryComplete(); try { await writer.WaitAsync(TimeSpan.FromMilliseconds(300)); } catch { } stop.Cancel(); stream.Dispose(); try { await writer; } catch { } }
}
public sealed class RateGate(double rate, double capacity, TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    readonly double maximum = capacity;
    double tokens = capacity;
    long stamp = (clock ?? TimeProvider.System).GetTimestamp();
    public bool Take()
    {
        var now = time.GetTimestamp(); tokens = Math.Min(maximum, tokens + time.GetElapsedTime(stamp, now).TotalSeconds * rate); stamp = now;
        if (tokens < 1) return false; tokens--; return true;
    }
}
