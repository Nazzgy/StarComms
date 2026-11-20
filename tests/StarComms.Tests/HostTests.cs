using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using StarComms.Core;
using StarComms.Engine;
using Xunit;

namespace StarComms.Tests;

public sealed class HostTests
{
    [Fact]
    public async Task HOST001_ReserveMatchingTcpAndUdpPorts()
    {
        using var cert = Certificates.Create();
        using var occupied = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var busy = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        Assert.InRange(busy, 1, 65534);
        await using var host = new Host("Přidělení portu", cert);
        Assert.Throws<RuleException>(() => host.Start(busy, busy));
        host.Start(busy, Math.Min(65535, busy + 9));
        Assert.True(host.Port > busy);
        await using var local = await host.ConnectLocal("Hostitel");
        Assert.True(local.VoiceReady);
    }

    [Fact]
    public async Task IPv6LoopbackWithPinnedCertificate()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("IPv6", cert);
        host.Start();
        host.Changed += () => { foreach (var pending in host.Pending) host.Decide(pending.Id, true); };
        await using var client = new Client();
        await client.Connect(new(IPAddress.IPv6Loopback, host.Port, host.Fingerprint), "Pilot");
        Assert.True(client.VoiceReady);
        Assert.Null(client.Self!.ShipId);
    }

    [Fact]
    public async Task ConcurrentAdmissionsRespectTwelveMemberLimit()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Kapacita", cert);
        host.Start();
        await using var local = await host.ConnectLocal("Hostitel");
        host.Changed += () => { foreach (var pending in host.Pending) host.Decide(pending.Id, true); };
        var clients = Enumerable.Range(0, 13).Select(_ => new Client()).ToArray();
        var address = new JoinAddress(IPAddress.Loopback, host.Port, host.Fingerprint);
        try {
            for (int i = 0; i < 11; i++) await clients[i].Connect(address, "Pilot " + i);
            async Task Join(int i)
            {
                var exc = await Assert.ThrowsAsync<RuleException>(() => clients[i].Connect(address, "Pilot " + i));
                Assert.Equal("FULL", exc.Message);
            }
            await Task.WhenAll(Join(11), Join(12));
            Assert.Equal(12, host.Fleet.Snapshot.Members.Length);
        } finally { foreach (var client in clients) await client.DisposeAsync(); }
    }

    [Fact]
    public async Task MutationReplyCanBeRequestedAgainAndOldEpochIsRejected()
    {
        using var cert = Certificates.Create();
        await using var host = new Host("Opakování", cert);
        host.Start();
        await using var local = await host.ConnectLocal("Hostitel");
        host.Changed += () => { foreach (var pending in host.Pending) host.Decide(pending.Id, true); };
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, host.Port);
        using var stream = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => {
            using var received = new X509Certificate2(certificate!);
            return Certificates.Valid(received, host.Fingerprint);
        });
        await stream.AuthenticateAsClientAsync("StarComms");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var ct = timeout.Token;
        async Task<Envelope> Receive(string type)
        {
            while (true) { var message = await Wire.Read(stream, ct); if (message.Type == type) return message; }
        }
        await Wire.Write(stream, Envelope.Make("ClientHello", new Hello(1, "0.1.0+test", "Pilot"), Guid.NewGuid()), ct);
        var accept = (await Receive("Accepted")).Read<Accepted>();
        var member = host.Fleet.Get(accept.MemberId);
        uint ship = 0;
        host.Mutate(f => ship = f.CreateShip("Carrack").ShipId);
        var join = Envelope.Make("JoinShip", new JoinShip(ship, member.MembershipEpoch), Guid.NewGuid());
        await Wire.Write(stream, join, ct);
        Assert.True((await Receive("Result")).Read<Result>().Ok);
        var version = host.Fleet.Snapshot.StateVersion;
        await Wire.Write(stream, join, ct);
        Assert.True((await Receive("Result")).Read<Result>().Ok);
        Assert.Equal(version, host.Fleet.Snapshot.StateVersion);
        await Wire.Write(stream, join with { Id = Guid.NewGuid() }, ct);
        Assert.Equal("STALE_STATE", (await Receive("Result")).Read<Result>().Code);
        Assert.Equal(version, host.Fleet.Snapshot.StateVersion);
    }
}
