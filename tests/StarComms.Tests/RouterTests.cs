using System.Net;
using Mono.Nat;
using StarComms.App;
using Xunit;

namespace StarComms.Tests;

public sealed class RouterTests
{
    sealed class Router : INatDevice
    {
        public IPEndPoint DeviceEndpoint => new(IPAddress.Loopback, 1234);
        public DateTime LastSeen => DateTime.UtcNow;
        public NatProtocol NatProtocol { get; init; } = NatProtocol.Upnp;
        public List<Mapping> Existing { get; } = [];
        public List<Mapping> Created { get; } = [];
        public List<Mapping> Removed { get; } = [];
        public required Func<Mapping, Mapping> Assign { get; init; }
        public Task<Mapping> CreatePortMapAsync(Mapping requested) {
            var actual = Assign(requested);
            Created.Add(actual);
            return Task.FromResult(actual);
        }
        public Task<Mapping> DeletePortMapAsync(Mapping mapping) { Removed.Add(mapping); return Task.FromResult(mapping); }
        public Task<Mapping[]> GetAllMappingsAsync() => NatProtocol == NatProtocol.Upnp
            ? Task.FromResult(Existing.ToArray()) : throw new NotSupportedException();
        public Task<IPAddress> GetExternalIPAsync() => Task.FromResult(IPAddress.Parse("100.64.0.2"));
        public Task<Mapping> GetSpecificMappingAsync(Protocol protocol, int port) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ForeignPortPreservedAndPartialPairRemoved()
    {
        var router = new Router { Assign = req => req.PublicPort == 49556 && req.Protocol == Protocol.Udp ?
            new Mapping(req.Protocol, req.PrivatePort, 50002, 7200, "Test") : req };
        router.Existing.Add(new Mapping(Protocol.Tcp, 2222, 49555, 3600, "Cizí"));
        var lease = new RouterMapping();
        await lease.Map([router], 49555);
        Assert.Equal(49557, lease.Endpoint!.Port);
        Assert.Equal(4, router.Created.Count);
        Assert.Equal(2, router.Removed.Count);
        await lease.DisposeAsync();
        Assert.Equal(4, router.Removed.Count);
        Assert.DoesNotContain(router.Removed, m => m.PublicPort == 49555);
    }

    [Fact]
    public async Task PmpActualPortAndFailedRenewal()
    {
        int requests = 0;
        var router = new Router {
            NatProtocol = NatProtocol.Pmp,
            Assign = req => ++requests <= 2 ? new Mapping(req.Protocol, req.PrivatePort, 55001, 2, "Test") : throw new IOException()
        };
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new RouterMapping();
        lease.LeaseLost += () => expired.TrySetResult();
        await lease.Map([router], 49555);
        Assert.Equal(55001, lease.Endpoint!.Port);
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Null(lease.Endpoint);
        await lease.DisposeAsync();
        Assert.Equal(2, router.Removed.Count);
    }
}
