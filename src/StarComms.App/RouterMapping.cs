using System.Net;
using Mono.Nat;

namespace StarComms.App;

public sealed class RouterMapping : IAsyncDisposable
{
    readonly List<(INatDevice Device, Mapping Mapping)> owned = [];
    readonly CancellationTokenSource stop = new();
    Task? renewal;
    public IPEndPoint? Endpoint { get; private set; }
    public event Action? LeaseLost;
    static async Task Remove(INatDevice device, Mapping mapping)
    {
        try { await device.DeletePortMapAsync(mapping).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }

    public async Task<IPAddress?> Prepare(int port)
    {
        var found = new List<INatDevice>(); object sync = new();
        EventHandler<DeviceEventArgs> handler = (_, e) => { lock (sync) if (!found.Contains(e.Device)) found.Add(e.Device); };
        NatUtility.DeviceFound += handler;
        try { NatUtility.StartDiscovery(); await Task.Delay(8000, stop.Token); }
        finally { NatUtility.StopDiscovery(); NatUtility.DeviceFound -= handler; }
        INatDevice[] devices; lock (sync) devices = found.ToArray();
        return await Map(devices, port);
    }

    internal async Task<IPAddress?> Map(IEnumerable<INatDevice> discovered, int port)
    {
        var devices = discovered.OrderBy(d => d.NatProtocol == NatProtocol.Pmp ? 1 : 0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(2000);
        foreach (var device in devices) {
            try {
                var external = await device.GetExternalIPAsync().WaitAsync(deadline.Token);
                for (int candidate = port; candidate <= Math.Min(port + 2, 65535); candidate++) {
                    if (device.NatProtocol == NatProtocol.Upnp) {
                        var existing = await device.GetAllMappingsAsync().WaitAsync(deadline.Token);
                        if (existing.Any(m => m.PublicPort == candidate)) continue;
                    }
                    var attempt = new List<Mapping>(2);
                    try {
                        foreach (var protocol in new[] { Protocol.Tcp, Protocol.Udp }) {
                            var request = new Mapping(protocol, port, candidate, 7200, "StarComms");
                            var creating = device.CreatePortMapAsync(request);
                            Mapping map;
                            try { map = await creating.WaitAsync(deadline.Token); }
                            catch (OperationCanceledException) { _ = RemoveLateMapping(device, creating); throw; }
                            attempt.Add(map);
                            if (map.Lifetime <= 0) break;
                        }
                        if (attempt.Count == 2 && attempt.All(m => m.Lifetime > 0) && attempt[0].PublicPort == attempt[1].PublicPort) {
                            owned.AddRange(attempt.Select(m => (device, m)));
                            attempt.Clear();
                            Endpoint = new(external, owned[0].Mapping.PublicPort);
                            renewal = Task.Run(Renew);
                            return external;
                        }
                    } catch (Exception) when (!deadline.IsCancellationRequested) {
                        // Another candidate can still be available.
                    } finally {
                        foreach (var map in attempt) await Remove(device, map);
                    }
                }
            } catch (Exception e) when (e is not OutOfMemoryException) { }
        }
        return null;
    }
    static async Task RemoveLateMapping(INatDevice device, Task<Mapping> pending)
    {
        try { await Remove(device, await pending); } catch { }
    }
    async Task Renew()
    {
        try {
            while (!stop.IsCancellationRequested) {
                var seconds = owned.Min(x => x.Mapping.Lifetime) / 2;
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), stop.Token);
                for (int i = 0; i < owned.Count; i++) {
                    var (device, previous) = owned[i];
                    var next = await device.CreatePortMapAsync(previous).WaitAsync(TimeSpan.FromSeconds(2), stop.Token);
                    if (next.PublicPort != previous.PublicPort || next.Lifetime <= 0) {
                        await Remove(device, next);
                        throw new InvalidOperationException();
                    }
                    owned[i] = (device, next);
                }
            }
        } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { Endpoint = null; LeaseLost?.Invoke(); }
    }
    public void Cancel() => stop.Cancel();
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); if (renewal != null) try { await renewal; } catch { }
        foreach (var group in owned.GroupBy(p => p.Device)) {
            using var deadline = new CancellationTokenSource(2000);
            foreach (var p in group) try { await p.Device.DeletePortMapAsync(p.Mapping).WaitAsync(deadline.Token); } catch { }
        }
        owned.Clear();
    }
}
