using System.Net;
using System.Net.Sockets;
using StarComms.Core;
using StarComms.Engine;
using Xunit;

namespace StarComms.Tests;

public sealed class ConnectionTests
{
    static async Task Until(Func<bool> condition, int seconds = 6)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!condition()) await Task.Delay(10, limit.Token);
    }

    [Fact]
    public async Task CrewCanCreateShipsAndKeepTheirCurrentSeat()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Let", cert);
        host.Start();
        await using var observer = await host.ConnectLocal("Hostitel");
        await using var player = new Client();
        host.Changed += () => { foreach (var request in host.Pending) host.Decide(request.Id, true); };
        await player.Connect(new(IPAddress.Loopback, host.Port, host.Fingerprint), "Pilot");
        var initial = player.Self!.MembershipEpoch;
        await player.Mutate("CreateShip", new CreateShip("Polaris", initial));
        await Until(() => player.Self?.ShipId != null);
        var seat = player.Self!.ShipId;
        Assert.False(player.Self.IsHost);
        Assert.False(player.Self.IsCaptain);
        await Assert.ThrowsAsync<RuleException>(() => player.Mutate("CreateShip", new CreateShip("Stará žádost", initial)));
        await player.Mutate("CreateShip", new CreateShip("Záchrana", player.Self.MembershipEpoch, "Cutlass Red"));
        await Until(() => observer.Snapshot?.Ships.Length == 2);
        Assert.Equal(seat, host.Fleet.Get(player.MemberId).ShipId);
        var vulture = observer.Snapshot!.Ships.Single(s => s.Name == "Záchrana");
        Assert.Equal("Cutlass Red", vulture.Model);
        await observer.Mutate("JoinShip", new JoinShip(vulture.ShipId, observer.Self!.MembershipEpoch));
        var occupied = await Assert.ThrowsAsync<RuleException>(() => player.Mutate("DeleteEmptyShip", new DeleteEmptyShip(vulture.ShipId)));
        Assert.Equal("SHIP_OCCUPIED", occupied.Message);
        await observer.Mutate("JoinShip", new JoinShip(seat!.Value, host.Fleet.Get(observer.MemberId).MembershipEpoch));
        await player.Mutate("DeleteEmptyShip", new DeleteEmptyShip(vulture.ShipId));
        await Until(() => observer.Snapshot?.Ships.Length == 1);
        Assert.Equal(seat, observer.Snapshot!.Ships.Single().ShipId);
    }

    [Fact]
    public async Task OnlyOneCrewMemberWinsCaptainClaim()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Let", cert);
        host.Start();
        host.Changed += () => { foreach (var request in host.Pending) host.Decide(request.Id, true); };
        var address = new JoinAddress(IPAddress.Loopback, host.Port, host.Fingerprint);
        await using var a = new Client();
        await using var b = new Client();
        await a.Connect(address, "Alfa");
        await b.Connect(address, "Bravo");
        await a.Mutate("CreateShip", new CreateShip("Polaris", a.Self!.MembershipEpoch));
        await Until(() => a.Self?.ShipId != null);
        var ship = a.Self!.ShipId!.Value;
        async Task<string?> Claim(Client player) {
            try { await player.Mutate("ClaimCaptain", new ClaimCaptain(ship, player.Self!.MembershipEpoch)); return null; }
            catch (RuleException ex) { return ex.Message; }
        }
        Assert.Equal("FORBIDDEN", await Claim(b));
        await b.Mutate("JoinShip", new JoinShip(ship, b.Self!.MembershipEpoch));
        await Until(() => b.Self?.ShipId == ship);
        var outcomes = await Task.WhenAll(Claim(a), Claim(b));
        Assert.Single(outcomes, result => result == null);
        Assert.Single(outcomes, result => result == "CAPTAIN_TAKEN");
        var winner = Assert.Single(host.Fleet.Snapshot.Members, member => member.IsCaptain);
        Assert.Equal(winner.MemberId, host.Fleet.Snapshot.Ships.Single().CaptainMemberId);
    }

    [Fact]
    public async Task CancelPendingThenConnectAndLeave()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Test žádostí", cert);
        host.Start();
        var address = new JoinAddress(IPAddress.Loopback, host.Port, host.Fingerprint);
        await using var applicant = new Client();
        using var cancel = new CancellationTokenSource();
        var pending = applicant.Connect(address, "Pilot", ct: cancel.Token);
        await Until(() => host.Pending.Length == 1);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await applicant.DisposeAsync();
        await Until(() => host.Pending.Length == 0);
        Assert.Empty(host.Fleet.Snapshot.Members);

        await using var denied = new Client();
        var attempt = denied.Connect(address, "Pilot");
        await Until(() => host.Pending.Length == 1);
        host.Decide(host.Pending.Single().Id, false);
        Assert.Equal("DENIED", (await Assert.ThrowsAsync<RuleException>(() => attempt)).Message);
        Assert.Empty(host.Fleet.Snapshot.Members);
    }

    [Fact]
    public async Task BlockedUdpAndReplacedCaptainResume()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Test obnovy", cert);
        host.Start();
        await using var local = await host.ConnectLocal("Hostitel");
        host.Changed += () => { foreach (var p in host.Pending) host.Decide(p.Id, true); };
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var udp = new UdpClient(endpoint);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task<(TcpClient Down, TcpClient Up)> Bridge()
        {
            var down = await listener.AcceptTcpClientAsync(cancel.Token);
            var up = new TcpClient();
            await up.ConnectAsync(IPAddress.Loopback, host.Port, cancel.Token);
            async Task Pump(NetworkStream source, NetworkStream destination)
            {
                try { await source.CopyToAsync(destination, cancel.Token); }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { }
            }
            _ = Pump(down.GetStream(), up.GetStream());
            _ = Pump(up.GetStream(), down.GetStream());
            return (down, up);
        }

        await using var pilot = new Client();
        var connect = pilot.Connect(new(IPAddress.Loopback, endpoint.Port, host.Fingerprint), "Kapitán", ct: cancel.Token);
        var first = await Bridge();
        await connect;
        Assert.False(pilot.VoiceReady);
        await Until(() => host.PlayersAwaitingVoice.SequenceEqual(new[] { "Kapitán" }));
        var forward = Task.Run(async () => {
            IPEndPoint? remote = null;
            var server = new IPEndPoint(IPAddress.Loopback, host.Port);
            try {
                while (!cancel.IsCancellationRequested) {
                    UdpReceiveResult packet;
                    try { packet = await udp.ReceiveAsync(cancel.Token); }
                    // Windows reports ICMP port-unreachable when the pilot replaces its UDP socket.
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { continue; }
                    var fromHost = packet.RemoteEndPoint.Equals(server);
                    if (!fromHost) remote = packet.RemoteEndPoint;
                    if (remote != null) await udp.SendAsync(packet.Buffer, fromHost ? remote : server, cancel.Token);
                }
            } catch (OperationCanceledException) { }
        });
        await pilot.Probe();
        Assert.Empty(host.PlayersAwaitingVoice);
        Assert.True(pilot.VoiceReady);
        uint originalId = pilot.MemberId;
        uint ship = 0;
        host.Mutate(f => {
            ship = f.CreateShip("Polaris").ShipId;
            f.Move(originalId, ship); f.Move(local.MemberId, ship); f.Captain(ship, originalId);
        });
        await Until(() => pilot.Self?.IsCaptain == true);
        uint originalEpoch = pilot.Self!.MembershipEpoch;
        first.Down.Dispose();
        first.Up.Dispose();
        await Until(() => host.Fleet.Get(originalId).Status == MemberStatus.Reconnecting);
        host.Mutate(f => f.Captain(ship, local.MemberId));
        var second = await Bridge();
        try {
            await Until(() => pilot.VoiceReady && pilot.Self?.MembershipEpoch > originalEpoch, 12);
            Assert.Equal(originalId, pilot.MemberId);
            Assert.Equal(ship, pilot.Self!.ShipId);
            Assert.False(pilot.Self.IsCaptain);
            Assert.True(host.Fleet.Get(local.MemberId).IsCaptain);
            await pilot.DisposeAsync();
            await Until(() => host.Fleet.Snapshot.Members.All(m => m.MemberId != originalId));
        } finally { second.Down.Dispose(); second.Up.Dispose(); cancel.Cancel(); await forward; }
    }
}
