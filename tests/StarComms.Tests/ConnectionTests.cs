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
        var forward = Task.Run(async () => {
            IPEndPoint? remote = null;
            var server = new IPEndPoint(IPAddress.Loopback, host.Port);
            try {
                while (!cancel.IsCancellationRequested) {
                    var packet = await udp.ReceiveAsync(cancel.Token);
                    var fromHost = packet.RemoteEndPoint.Equals(server);
                    if (!fromHost) remote = packet.RemoteEndPoint;
                    if (remote != null) await udp.SendAsync(packet.Buffer, fromHost ? remote : server, cancel.Token);
                }
            } catch (OperationCanceledException) { }
        });
        await pilot.Probe();
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
