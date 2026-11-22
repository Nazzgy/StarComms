using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StarComms.Core;

namespace StarComms.Engine;

public sealed class IdentityException(string fingerprint) : Exception("IDENTITY_CHANGED") { public string Fingerprint { get; } = fingerprint; }
public sealed class Client : IAsyncDisposable
{
    readonly object sync = new();
    readonly CancellationTokenSource life = new();
    readonly SemaphoreSlim reconnectGate = new(1);
    readonly SemaphoreSlim probeGate = new(1);
    int disposed;
    CancellationTokenSource? transport;
    TcpClient? tcp; Socket? udp; ControlLink? link; DatagramCipher? outgoing, incoming;
    Task? controlTask, udpTask, heartbeatTask;
    JoinAddress? address; string nickname = ""; string? resumeToken; Guid? session;
    Accepted? accepted; bool intentional; long pong, keepAck; ulong ping; byte[]? probeNonce, keepNonce;
    TaskCompletionSource ready = NewSignal();
    readonly Dictionary<Guid, TaskCompletionSource<Result>> requests = [];
    readonly Dictionary<uint, BurstGuard> bursts = [];
    long snapshotRequested;
    public FleetSnapshot? Snapshot { get; private set; }
    public uint MemberId => accepted?.MemberId ?? 0;
    public Member? Self => Snapshot?.Members.FirstOrDefault(m => m.MemberId == MemberId);
    public bool VoiceReady { get; private set; }
    public string Status { get; private set; } = "Odpojeno";
    public string? VersionWarning { get; private set; }
    public event Action? Changed;
    public event Action<VoiceFrame>? Voice;
    public event Action<VoiceEnd>? End;
    public event Action? ResetAudio;
    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    void State(string value) { Status = value; Changed?.Invoke(); }
    public async Task Connect(JoinAddress target, string name, Guid? resumeSession = null, string? token = null, CancellationToken ct = default)
    {
        address = target; nickname = Fleet.Text(name, 2, 24); session = resumeSession; resumeToken = token; intentional = false;
        await Open(ct); StartLoops(); await Probe();
    }
    async Task Open(CancellationToken ct)
    {
        transport = CancellationTokenSource.CreateLinkedTokenSource(life.Token); var target = address!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, transport.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        tcp = new(target.Address.AddressFamily) { NoDelay = true }; State("Připojuji se…");
        await tcp.ConnectAsync(target.Address, target.Port, timeout.Token);
        string? seen = null;
        var tls = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => { if (cert == null) return false; using var c = new X509Certificate2(cert); seen = Certificates.Fingerprint(c); return Certificates.Valid(c, target.Fingerprint); });
        try { await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "StarComms", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token); }
        catch (AuthenticationException) when (seen != null) { tls.Dispose(); tcp.Dispose(); throw new IdentityException(seen); }
        link = new(tls); link.Send("ClientHello", new Hello(1, VersionInfo.Full, nickname, session, resumeToken), Guid.NewGuid()); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (true) {
            var e = await link.Read(timeout.Token);
            if (e.Type == "ServerHello") { var h = e.Read<ServerHello>(); if (h.ProtocolVersion != 1) throw new RuleException("VERSION_MISMATCH"); VersionWarning = h.AppVersion != VersionInfo.Full ? $"Máš jiné sestavení než hostitel: {VersionInfo.Full} / {h.AppVersion}" : null; }
            else if (e.Type == "AdmissionPending") { State("Čekám, až tě hostitel přijme…"); timeout.CancelAfter(TimeSpan.FromSeconds(60)); }
            else if (e.Type == "Rejected") throw new RuleException(e.Body.GetProperty("code").GetString() ?? "DENIED");
            else if (e.Type == "Accepted") {
                accepted = e.Read<Accepted>(); resumeToken = accepted.ResumeToken; session = accepted.Snapshot.SessionId; Snapshot = accepted.Snapshot;
                outgoing = new(accepted.UdpKeyC2H, accepted.NoncePrefixC2H, accepted.ConnectionId); incoming = new(accepted.UdpKeyH2C, accepted.NoncePrefixH2C, accepted.ConnectionId);
                udp = new(target.Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp); udp.Bind(new IPEndPoint(target.Address.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0)); udp.Connect(new IPEndPoint(target.Address, target.Port)); udp.Blocking = false;
                pong = keepAck = Environment.TickCount64; ready = NewSignal(); bursts.Clear(); State("Připojeno · připravuji hlas"); break;
            }
        }
    }
    void StartLoops() { controlTask = Task.Run(ControlLoop); udpTask = Task.Run(UdpLoop); heartbeatTask = Task.Run(Heartbeat); }
    public async Task Probe()
    {
        if (!await probeGate.WaitAsync(0)) return;
        try { await ProbeOnce(); } finally { probeGate.Release(); }
    }
    async Task ProbeOnce()
    {
        if (transport == null || transport.IsCancellationRequested) return;
        var token = transport.Token;
        VoiceReady = false; ResetAudio?.Invoke(); ready = NewSignal(); probeNonce = RandomNumberGenerator.GetBytes(16);
        State("Ověřuji hlasové spojení v obou směrech…");
        try { foreach (int delay in new[] { 0, 250, 500, 1000, 2000, 1250 }) {
            if (delay > 0 && await Task.WhenAny(ready.Task, Task.Delay(delay, token)) == ready.Task) return;
            token.ThrowIfCancellationRequested();
            if (delay == 1250) break;
            byte[] probe = new byte[17]; probe[0] = 1; probeNonce.CopyTo(probe, 1); SendUdp(probe);
        } State("Jsi ve flotile, ale hlasové spojení se nepodařilo ověřit."); } catch (OperationCanceledException) { }
    }
    void RequestSnapshot()
    { if (Environment.TickCount64 - snapshotRequested < 5000) return; snapshotRequested = Environment.TickCount64; link?.Send("SnapshotRequest", new { }, Guid.NewGuid()); }
    async Task ControlLoop()
    {
        bool lost = false;
        try { while (!transport!.IsCancellationRequested) {
            var e = await link!.Read(transport.Token);
            switch (e.Type) {
                case "FleetSnapshot":
                    var s = e.Read<FleetSnapshot>();
                    if (s.SessionId != session) throw new InvalidDataException();
                    bool changed = false;
                    lock (sync) { if (Snapshot == null || s.StateVersion >= Snapshot.StateVersion) { changed = s.StateVersion != Snapshot?.StateVersion; Snapshot = s; snapshotRequested = 0; } }
                    if (changed) ResetAudio?.Invoke(); Changed?.Invoke(); break;
                case "VoiceReady": if (e.Body.GetProperty("connectionId").GetUInt64() == accepted!.ConnectionId) { VoiceReady = true; keepAck = Environment.TickCount64; ready.TrySetResult(); State("Připojeno · hlas připraven"); } break;
                case "Ping": link.Send("Pong", new { nonce = e.Body.GetProperty("nonce").GetUInt64() }); break;
                case "Pong": if (e.Body.GetProperty("nonce").GetUInt64() == ping) pong = Environment.TickCount64; break;
                case "Result": lock (sync) if (e.ReplyTo is Guid id && requests.TryGetValue(id, out var completion)) completion.TrySetResult(e.Read<Result>()); break;
                case "VoiceEnd": var end = e.Read<VoiceEnd>(); lock (sync) { if (Valid(end.SenderId, end.SenderEpoch, end.RecipientEpoch, end.Channel, end.ShipId) && Guard(end.SenderId).Accept(end.SenderEpoch, end.TalkBurstId, end.Channel, end.ShipId, end.LastFrameIndex, true)) End?.Invoke(end); } break;
                case "Removed": intentional = true; resumeToken = null; State("Hostitel tě vyřadil z flotily."); return;
                case "SessionEnded": intentional = true; resumeToken = null; State("Hostitel ukončil flotilu."); return;
            }
        } } catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException or System.Text.Json.JsonException) { lost = true; }
        finally { VoiceReady = false; ResetAudio?.Invoke(); transport?.Cancel(); udp?.Dispose(); tcp?.Dispose(); Changed?.Invoke(); }
        if (lost && !intentional && !life.IsCancellationRequested) _ = Task.Run(Reconnect);
    }
    bool Valid(uint sender, uint epoch, uint recipientEpoch, VoiceChannel channel, uint ship)
    {
        var s = Snapshot; if (s == null) return false;
        var source = s.Members.FirstOrDefault(m => m.MemberId == sender);
        if (source == null || source.MembershipEpoch < epoch || (Self?.MembershipEpoch ?? 0) < recipientEpoch) RequestSnapshot();
        return Fleet.CanReceive(s, sender, epoch, MemberId, recipientEpoch, channel, ship);
    }
    BurstGuard Guard(uint id) { if (!bursts.TryGetValue(id, out var b)) bursts[id] = b = new(); return b; }
    async Task UdpLoop()
    {
        byte[] buffer = new byte[1201]; var socket = udp!; var token = transport!.Token;
        try { while (!token.IsCancellationRequested) {
            int n;
            try { n = await socket.ReceiveAsync(buffer, SocketFlags.None, token); }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            var p = incoming!.Decrypt(buffer.AsSpan(0, n)); if (p == null) continue;
            if (p[0] == 2 && p.Length == 33 && probeNonce != null && p.AsSpan(1, 16).SequenceEqual(probeNonce)) { p[0] = 3; SendUdp(p); }
            else if (p[0] == 5 && p.Length == 17 && keepNonce != null && p.AsSpan(1).SequenceEqual(keepNonce)) keepAck = Environment.TickCount64;
            else if (VoiceReady && p[0] == 16) {
                var f = VoiceFrame.Parse(p); if (f == null) continue;
                lock (sync) if (Valid(f.SenderId, f.SenderEpoch, f.RecipientEpoch, f.Channel, f.ShipId) && Guard(f.SenderId).Accept(f.SenderEpoch, f.TalkBurstId, f.Channel, f.ShipId, f.FrameIndex)) Voice?.Invoke(f);
            }
        } } catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException) { }
    }
    void SendUdp(byte[] data) { lock (sync) { try { if (udp != null && outgoing != null) udp.Send(outgoing.Encrypt(data)); } catch (Exception e) when (e is SocketException or ObjectDisposedException) { } } }
    public void SendVoice(VoiceChannel channel, ulong burst, uint index, byte[] opus)
    {
        lock (sync) { var m = Self; if (!VoiceReady || m?.ShipId == null || (channel == VoiceChannel.Command && !m.IsCaptain)) return; SendUdp(new VoiceFrame(channel, m.MemberId, m.MembershipEpoch, 0, channel == VoiceChannel.Ship ? m.ShipId.Value : 0, burst, index, opus).Encode()); }
    }
    public void EndVoice(VoiceChannel channel, ulong burst, uint index)
    { var m = Self; if (m?.ShipId != null) link?.Send("VoiceEnd", new VoiceEnd(m.MemberId, m.MembershipEpoch, 0, channel, channel == VoiceChannel.Ship ? m.ShipId.Value : 0, burst, index)); }
    public async Task Mutate(string type, object body)
    {
        var id = Guid.NewGuid(); var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously); lock (sync) requests[id] = tcs;
        try { link?.Send(type, body, id); var r = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), life.Token); if (!r.Ok) throw new RuleException(r.Code ?? "STALE_STATE"); }
        catch (TimeoutException) { RequestSnapshot(); throw new RuleException("STALE_STATE"); }
        finally { lock (sync) requests.Remove(id); }
    }
    async Task Heartbeat()
    {
        var token = transport!.Token;
        try { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); while (await timer.WaitForNextTickAsync(token)) {
            if (Environment.TickCount64 - pong > 6000) { tcp?.Dispose(); return; }
            link?.Send("Ping", new { nonce = ++ping }); keepNonce = RandomNumberGenerator.GetBytes(16); byte[] p = new byte[17]; p[0] = 4; keepNonce.CopyTo(p, 1); SendUdp(p);
            if (VoiceReady && Environment.TickCount64 - keepAck > 6000) await Probe();
        } } catch (OperationCanceledException) { }
    }
    async Task CloseTransport()
    {
        transport?.Cancel(); udp?.Dispose(); tcp?.Dispose();
        foreach (var task in new[] { udpTask, heartbeatTask }) if (task != null) try { await task; } catch { }
        if (link != null) await link.DisposeAsync(); outgoing?.Dispose(); incoming?.Dispose(); transport?.Dispose();
        udp = null; tcp = null; link = null; transport = null; outgoing = incoming = null;
    }
    async Task Reconnect()
    {
        if (!await reconnectGate.WaitAsync(0)) return;
        try {
            if (!intentional && !life.IsCancellationRequested) await ReconnectWithinBudget();
        } finally { reconnectGate.Release(); }
    }
    async Task ReconnectWithinBudget()
    {
        State("Spojení se přerušilo. Obnovuji připojení…"); await CloseTransport();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(life.Token); budget.CancelAfter(TimeSpan.FromSeconds(15)); long start = Environment.TickCount64;
        foreach (int at in new[] { 0, 1000, 3000, 6000, 10000 }) {
            try {
                var wait = at - (Environment.TickCount64 - start); if (wait > 0) await Task.Delay((int)wait, budget.Token);
                await Open(budget.Token); ResetAudio?.Invoke(); StartLoops(); await Probe(); return;
            } catch (RuleException e) when (e.Message == "RESUME_INVALID") {
                await CloseTransport();
                resumeToken = null; intentional = true;
                State("Hostitel zahájil novou flotilu nebo vypršel čas na obnovení. Připoj se znovu.");
                return;
            } catch (Exception e) when (e is IOException or SocketException or AuthenticationException or OperationCanceledException or RuleException or IdentityException or TimeoutException) {
                await CloseTransport(); if (budget.IsCancellationRequested) break;
            }
        }
        State("Spojení se nepodařilo obnovit. Zkusit znovu / Zadat jinou adresu.");
    }
    public async Task Retry() { if (controlTask is { IsCompleted: false }) return; await Reconnect(); }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        intentional = true;
        link?.Send("Leave", new { }, Guid.NewGuid());
        if (link != null) await link.DisposeAsync();
        life.Cancel();
        await reconnectGate.WaitAsync();
        try {
            transport?.Cancel(); tcp?.Dispose();
            if (controlTask != null) try { await controlTask; } catch { }
            await CloseTransport(); VoiceReady = false; ResetAudio?.Invoke();
        } finally { reconnectGate.Release(); }
    }
}
