using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StarComms.Core;

namespace StarComms.Engine;

public sealed record Admission(Guid Id, string Nickname);
public sealed class Host : IAsyncDisposable
{
    sealed class Peer(Accepted credentials, ControlLink link, Socket udp)
    {
        public Accepted Credentials = credentials;
        public ControlLink Link = link;
        public Socket Udp = udp;
        public DatagramCipher In = new(credentials.UdpKeyC2H, credentials.NoncePrefixC2H, credentials.ConnectionId);
        public DatagramCipher Out = new(credentials.UdpKeyH2C, credentials.NoncePrefixH2C, credentials.ConnectionId);
        public IPEndPoint? Endpoint;
        public bool VerifiedRemote;
        public readonly long JoinedAt = Environment.TickCount64;
        public bool ProbeOverdue;
        public List<(IPEndPoint Endpoint, byte[] Nonce, byte[] Challenge, long Stamp)> Probes = [];
        public BurstGuard Burst = new();
        public RateGate VoiceRate = new(75, 20);
        public long Pong = Environment.TickCount64;
        public ulong Ping;
    }
    sealed record Reservation(uint MemberId, byte[] Hash, long? Disconnected);
    readonly X509Certificate2 certificate;
    readonly CancellationTokenSource stop = new();
    readonly List<(TcpListener Tcp, Socket Udp)> listeners = [];
    readonly List<Task> tasks = [];
    readonly Dictionary<ulong, Peer> peers = [];
    readonly Dictionary<uint, Reservation> reservations = [];
    readonly Dictionary<Guid, (Admission Info, TaskCompletionSource<bool> Reply)> pending = [];
    int connections;
    public Fleet Fleet { get; }
    public int Port { get; private set; }
    public string Fingerprint => Certificates.Fingerprint(certificate);
    public event Action? Changed;
    public event Action<Exception>? ConnectionError;
    public string[] VerifiedPlayers
    {
        get {
            lock (Fleet.Sync) return Fleet.Snapshot.Members.Where(m => peers.Values.Any(p => p.VerifiedRemote && p.Credentials.MemberId == m.MemberId))
                .Select(m => m.Nickname).ToArray();
        }
    }
    public string[] PlayersAwaitingVoice {
        get {
            lock (Fleet.Sync) return Fleet.Snapshot.Members.Where(m => !m.IsHost &&
                peers.Values.Any(p => p.Credentials.MemberId == m.MemberId && p.ProbeOverdue && p.Endpoint == null))
                .Select(m => m.Nickname).ToArray();
        }
    }
    public void ResetVerification()
    {
        lock (Fleet.Sync) foreach (var peer in peers.Values) peer.VerifiedRemote = false;
        Changed?.Invoke();
    }
    public Admission[] Pending { get { lock (Fleet.Sync) return pending.Values.Select(x => x.Info).ToArray(); } }
    public Host(string name, X509Certificate2 certificate) { Fleet = new(name); this.certificate = certificate; }
    public void Start(int firstPort = 49555, int lastPort = 49565)
    {
        for (int port = firstPort; port <= lastPort; port++) {
            bool conflict = false;
            foreach (var address in new[] { IPAddress.Any, IPAddress.IPv6Any }) {
                TcpListener? tcp = null; Socket? udp = null;
                try {
                    tcp = new(address, port); udp = new(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                    if (address.AddressFamily == AddressFamily.InterNetworkV6) { tcp.Server.DualMode = false; udp.DualMode = false; }
                    tcp.Start(24); udp.Bind(new IPEndPoint(address, port)); udp.Blocking = false; listeners.Add((tcp, udp));
                } catch (SocketException e) { tcp?.Stop(); udp?.Dispose(); if (e.SocketErrorCode == SocketError.AddressAlreadyInUse) conflict = true; }
            }
            if (!conflict && listeners.Count > 0) { Port = port; break; }
            foreach (var pair in listeners) { pair.Tcp.Stop(); pair.Udp.Dispose(); } listeners.Clear();
        }
        if (Port == 0) throw new RuleException("HOST_UNREACHABLE");
        foreach (var p in listeners) { tasks.Add(Task.Run(() => AcceptLoop(p.Tcp, p.Udp))); tasks.Add(Task.Run(() => UdpLoop(p.Udp))); }
        tasks.Add(Task.Run(Heartbeat));
    }
    public void Decide(Guid id, bool accept) { lock (Fleet.Sync) if (pending.TryGetValue(id, out var p)) p.Reply.TrySetResult(accept); }
    public void Mutate(Action<Fleet> mutation) { lock (Fleet.Sync) { mutation(Fleet); Broadcast(); } }
    public void Remove(uint member)
    {
        lock (Fleet.Sync) {
            if (Fleet.Get(member).IsHost) throw new RuleException("FORBIDDEN");
            foreach (var p in peers.Values.Where(p => p.Credentials.MemberId == member).ToArray()) { p.Link.Send("Removed", new { reason = "REMOVED_BY_HOST" }); _ = p.Link.DisposeAsync(); }
            reservations.Remove(member); Fleet.Remove(member); Broadcast();
        }
    }
    void Broadcast() { var s = Fleet.Snapshot; foreach (var p in peers.Values) p.Link.Send("FleetSnapshot", s); Changed?.Invoke(); }
    async Task AcceptLoop(TcpListener listener, Socket udp)
    {
        try { while (!stop.IsCancellationRequested) {
            var tcp = await listener.AcceptTcpClientAsync(stop.Token);
            if (Interlocked.Increment(ref connections) > 24) { Interlocked.Decrement(ref connections); tcp.Dispose(); continue; }
            var task = Serve(tcp, udp); lock (tasks) { tasks.RemoveAll(t => t.IsCompleted); tasks.Add(task); }
        } } catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    // Only the local caller can create the host member; network fields never select this path.
    public async Task<Client> ConnectLocal(string nickname)
    {
        var client = new Client(); var address = new JoinAddress(listeners.Any(p => p.Udp.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Loopback : IPAddress.IPv6Loopback, Port, Fingerprint);
        Member member; string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        lock (Fleet.Sync) { member = Fleet.Add(nickname, true); Fleet.SetStatus(member.MemberId, MemberStatus.Reconnecting); reservations[member.MemberId] = new(member.MemberId, SHA256.HashData(Convert.FromBase64String(token)), Environment.TickCount64); }
        await client.Connect(address, nickname, Fleet.SessionId, token); return client;
    }
    async Task Serve(TcpClient tcp, Socket udp)
    {
        Peer? peer = null; Guid pendingId = Guid.Empty; bool leave = false;
        try {
            using (tcp) {
                tcp.NoDelay = true; using var tls = new SslStream(tcp.GetStream(), false);
                using var init = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); init.CancelAfter(TimeSpan.FromSeconds(10));
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, init.Token);
                await using var link = new ControlLink(tls);
                try {
                init.CancelAfter(TimeSpan.FromSeconds(5)); var first = await link.Read(init.Token); if (first.Type != "ClientHello") return;
                var hello = first.Read<Hello>();
                link.Send("ServerHello", new ServerHello(1, VersionInfo.Full, Fleet.SessionId, Fleet.Name));
                if (hello.ProtocolVersion != 1) { link.Send("Rejected", new { code = "VERSION_MISMATCH" }); return; }
                Member member; string token;
                if (hello.ResumeToken != null) {
                    lock (Fleet.Sync) {
                        if (hello.ResumeSessionId != Fleet.SessionId) throw new RuleException("RESUME_INVALID");
                        byte[] raw; try { raw = Convert.FromBase64String(hello.ResumeToken); } catch { throw new RuleException("RESUME_INVALID"); }
                        if (raw.Length != 32) throw new RuleException("RESUME_INVALID");
                        var hash = SHA256.HashData(raw); var r = reservations.Values.FirstOrDefault(r => CryptographicOperations.FixedTimeEquals(hash, r.Hash));
                        if (r == null || r.Disconnected is long t && Environment.TickCount64 - t > 120000) throw new RuleException("RESUME_INVALID");
                        if (r.Disconnected == null) throw new RuleException("ALREADY_CONNECTED");
                        member = Fleet.Get(r.MemberId); Fleet.SetStatus(member.MemberId, MemberStatus.Online); token = hello.ResumeToken; reservations[member.MemberId] = r with { Disconnected = null };
                    }
                } else {
                    var name = Fleet.Text(hello.Nickname, 2, 24);
                    var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); pendingId = Guid.NewGuid();
                    lock (Fleet.Sync) { if (pending.Count >= 8) throw new RuleException("FULL"); pending.Add(pendingId, (new(pendingId, name), reply)); Changed?.Invoke(); }
                    link.Send("AdmissionPending", new { expiresInSeconds = 60 });
                    using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                        // A canceled/disconnected applicant must not occupy a pending slot until the full timeout.
                        var disconnected = link.Read(waiting.Token);
                        var decision = reply.Task.WaitAsync(TimeSpan.FromSeconds(60), stop.Token);
                        if (await Task.WhenAny(disconnected, decision) == disconnected) { await disconnected; return; }
                        bool admitted = await decision;
                        waiting.Cancel();
                        try { await disconnected; return; } catch (OperationCanceledException) { }
                        if (!admitted) { link.Send("Rejected", new { code = "DENIED" }); return; }
                    }
                    lock (Fleet.Sync) { member = Fleet.Add(name); token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); reservations.Add(member.MemberId, new(member.MemberId, SHA256.HashData(Convert.FromBase64String(token)), null)); }
                }
                lock (Fleet.Sync) {
                    pending.Remove(pendingId);
                    ulong id; do { id = BinaryPrimitives.ReadUInt64BigEndian(RandomNumberGenerator.GetBytes(8)); } while (id == 0 || peers.ContainsKey(id));
                    var accepted = new Accepted(member.MemberId, id, token, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(4), RandomNumberGenerator.GetBytes(4), Fleet.Snapshot);
                    peer = new(accepted, link, udp); peers.Add(id, peer); link.Send("Accepted", accepted); Broadcast();
                }
                var rate = new RateGate(20, 40); var pingRate = new RateGate(2, 8);
                var results = new Dictionary<Guid, (long Time, Envelope Result)>();
                using var connected = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, link.Token);
                while (!connected.IsCancellationRequested) {
                    var e = await link.Read(connected.Token);
                    if (e.Type is "Ping" or "Pong") {
                        if (!pingRate.Take()) throw new InvalidDataException();
                        var nonce = e.Body.GetProperty("nonce").GetUInt64();
                        if (e.Type == "Ping") link.Send("Pong", new { nonce }); else if (nonce == peer.Ping) peer.Pong = Environment.TickCount64; continue;
                    }
                    if (!rate.Take()) throw new InvalidDataException("RATE_LIMIT");
                    if (e.Type == "Leave") { leave = true; break; }
                    lock (Fleet.Sync) {
                        if (e.Type == "SnapshotRequest") { link.Send("FleetSnapshot", Fleet.Snapshot); continue; }
                        if (e.Type == "VoiceEnd") { RouteEnd(peer, e.Read<VoiceEnd>()); continue; }
                        if (e.Id == null) throw new InvalidDataException();
                        foreach (var id in results.Where(x => Environment.TickCount64 - x.Value.Time > 60000).Select(x => x.Key).ToArray()) results.Remove(id);
                        if (results.TryGetValue(e.Id.Value, out var cached)) { link.Send(cached.Result); continue; }
                        Result result;
                        try {
                            switch (e.Type) {
                                case "DeleteEmptyShip": Fleet.DeleteEmptyShip(e.Read<DeleteEmptyShip>().ShipId); break;
                                case "CreateShip":
                                    var request = e.Read<CreateShip>();
                                    var creator = Fleet.Get(member.MemberId);
                                    if (creator.Status != MemberStatus.Online || creator.MembershipEpoch != request.ExpectedMembershipEpoch)
                                        throw new RuleException("STALE_STATE");
                                    var created = Fleet.CreateShip(request.Name ?? "", request.Model ?? "");
                                    if (creator.ShipId == null) Fleet.Move(creator.MemberId, created.ShipId);
                                    break;
                                case "JoinShip": var j = e.Read<JoinShip>(); Fleet.Move(member.MemberId, j.ShipId, j.ExpectedMembershipEpoch); break;
                                case "ClaimCaptain":
                                    var claim = e.Read<ClaimCaptain>();
                                    Fleet.ClaimCaptain(claim.ShipId, member.MemberId, claim.ExpectedMembershipEpoch);
                                    break;
                                case "TransferCaptain": var c = e.Read<TransferCaptain>(); Fleet.Captain(c.ShipId, c.NewCaptainMemberId, member.MemberId, c.ExpectedMembershipEpoch); break;
                                default: throw new RuleException("FORBIDDEN");
                            }
                            result = new(true, null, Fleet.Snapshot.StateVersion); Broadcast();
                        } catch (RuleException ex) { result = new(false, ex.Message, Fleet.Snapshot.StateVersion); }
                        var response = Envelope.Make("Result", result, replyTo: e.Id); if (results.Count >= 64) results.Remove(results.MinBy(x => x.Value.Time).Key); results[e.Id.Value] = (Environment.TickCount64, response); link.Send(response);
                    }
                }
                } catch (RuleException ex) { link.Send("Rejected", new { code = ex.Message }); }
                  catch (TimeoutException) { link.Send("Rejected", new { code = "DENIED" }); }
            }
        } catch (Exception e) when (e is IOException or SocketException or AuthenticationException or OperationCanceledException or TimeoutException or InvalidDataException or RuleException or System.Text.Json.JsonException or FormatException or KeyNotFoundException or ObjectDisposedException) { ConnectionError?.Invoke(e); }
        finally {
            lock (Fleet.Sync) {
                pending.Remove(pendingId);
                if (peer != null) {
                    peers.Remove(peer.Credentials.ConnectionId); peer.In.Dispose(); peer.Out.Dispose(); var id = peer.Credentials.MemberId;
                    if (reservations.TryGetValue(id, out var r)) { if (leave) { reservations.Remove(id); Fleet.Remove(id); } else { reservations[id] = r with { Disconnected = Environment.TickCount64 }; Fleet.SetStatus(id, MemberStatus.Reconnecting); } }
                }
                Broadcast();
            }
            Interlocked.Decrement(ref connections); tcp.Dispose();
        }
    }
    bool ValidSender(Peer p, uint sender, uint epoch, VoiceChannel channel, uint ship)
    {
        if (sender != p.Credentials.MemberId) return false;
        var m = Fleet.Snapshot.Members.FirstOrDefault(m => m.MemberId == sender);
        return m != null && m.Status == MemberStatus.Online && m.MembershipEpoch == epoch && m.ShipId.HasValue && (channel == VoiceChannel.Ship ? m.ShipId == ship : channel == VoiceChannel.Command && m.IsCaptain && ship == 0);
    }
    void RouteEnd(Peer p, VoiceEnd e)
    {
        if (e.RecipientEpoch != 0 || !ValidSender(p, e.SenderId, e.SenderEpoch, e.Channel, e.ShipId) || !p.Burst.Accept(e.SenderEpoch, e.TalkBurstId, e.Channel, e.ShipId, e.LastFrameIndex, true)) return;
        var s = Fleet.Snapshot;
        foreach (var recipient in peers.Values) { var m = s.Members.FirstOrDefault(m => m.MemberId == recipient.Credentials.MemberId); if (m != null && Fleet.CanReceive(s, e.SenderId, e.SenderEpoch, m.MemberId, m.MembershipEpoch, e.Channel, e.ShipId)) recipient.Link.Send("VoiceEnd", e with { RecipientEpoch = m.MembershipEpoch }); }
    }
    async Task UdpLoop(Socket udp)
    {
        byte[] buffer = new byte[1201]; EndPoint any = new IPEndPoint(udp.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
        try { while (!stop.IsCancellationRequested) {
            SocketReceiveFromResult result;
            try { result = await udp.ReceiveFromAsync(buffer, SocketFlags.None, any, stop.Token); }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            if (result.ReceivedBytes is < 41 or > 1200) continue;
            lock (Fleet.Sync) {
                var id = BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(8)); if (!peers.TryGetValue(id, out var p)) continue;
                var data = p.In.Decrypt(buffer.AsSpan(0, result.ReceivedBytes)); if (data == null) continue;
                var source = (IPEndPoint)result.RemoteEndPoint;
                if (data[0] == 1 && data.Length == 17) {
                    p.Probes.RemoveAll(x => Environment.TickCount64 - x.Stamp > 5000); if (p.Probes.Count >= 5) p.Probes.RemoveAt(0);
                    var challenge = RandomNumberGenerator.GetBytes(16); var nonce = data[1..]; p.Probes.Add((source, nonce, challenge, Environment.TickCount64));
                    byte[] ack = new byte[33]; ack[0] = 2; nonce.CopyTo(ack, 1); challenge.CopyTo(ack, 17); Send(p, ack, source);
                } else if (data[0] == 3 && data.Length == 33) {
                    var index = p.Probes.FindIndex(x => x.Endpoint.Equals(source) && Environment.TickCount64 - x.Stamp <= 5000 && data.AsSpan(1, 16).SequenceEqual(x.Nonce) && data.AsSpan(17, 16).SequenceEqual(x.Challenge));
                    if (index >= 0) { p.Probes.RemoveAt(index); p.Endpoint = source; p.VerifiedRemote = !IPAddress.IsLoopback(source.Address); Changed?.Invoke(); p.Link.Send("VoiceReady", new { connectionId = id }); }
                } else if (p.Endpoint?.Equals(source) == true) {
                    if (data[0] == 4 && data.Length == 17) { data[0] = 5; Send(p, data, source); }
                    else if (data[0] == 16 && p.VoiceRate.Take()) {
                        var f = VoiceFrame.Parse(data); if (f == null || f.RecipientEpoch != 0 || !ValidSender(p, f.SenderId, f.SenderEpoch, f.Channel, f.ShipId) || !p.Burst.Accept(f.SenderEpoch, f.TalkBurstId, f.Channel, f.ShipId, f.FrameIndex)) continue;
                        var s = Fleet.Snapshot;
                        foreach (var target in peers.Values) {
                            var m = s.Members.FirstOrDefault(m => m.MemberId == target.Credentials.MemberId);
                            if (target.Endpoint != null && m != null && Fleet.CanReceive(s, f.SenderId, f.SenderEpoch, m.MemberId, m.MembershipEpoch, f.Channel, f.ShipId)) Send(target, (f with { RecipientEpoch = m.MembershipEpoch }).Encode(), target.Endpoint);
                        }
                    }
                }
            }
        } } catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException) { }
    }
    // UDP sends are nonblocking and immediate under the same authority lock as mutations; no stale send queue.
    static void Send(Peer p, byte[] data, IPEndPoint endpoint)
    { try { p.Udp.SendTo(p.Out.Encrypt(data), SocketFlags.None, endpoint); } catch (SocketException) { } }
    async Task Heartbeat()
    {
        try { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2)); while (await timer.WaitForNextTickAsync(stop.Token)) lock (Fleet.Sync) {
            foreach (var p in peers.Values) { if (Environment.TickCount64 - p.Pong > 6000) _ = p.Link.DisposeAsync(); else { p.Ping++; p.Link.Send("Ping", new { nonce = p.Ping }); } }
            foreach (var p in peers.Values) {
                if (p.Endpoint != null || p.ProbeOverdue || Environment.TickCount64 - p.JoinedAt < 6000) continue;
                p.ProbeOverdue = true;
                Changed?.Invoke();
            }
            foreach (var r in reservations.Values.Where(r => r.Disconnected is long t && Environment.TickCount64 - t > 120000).ToArray()) { reservations.Remove(r.MemberId); Fleet.Remove(r.MemberId); Broadcast(); }
        } } catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        Peer[] active; lock (Fleet.Sync) { active = peers.Values.ToArray(); foreach (var p in active) p.Link.Send("SessionEnded", new { reason = "HOST_LEFT" }); }
        await Task.WhenAll(active.Select(async p => await p.Link.DisposeAsync())); stop.Cancel(); foreach (var p in listeners) { p.Tcp.Stop(); p.Udp.Dispose(); }
        Task[] all; lock (tasks) all = tasks.ToArray(); try { await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
}
