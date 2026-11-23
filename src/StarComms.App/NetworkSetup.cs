using StarComms.Core;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

namespace StarComms.App;

public sealed record AddressCandidate(string Address, string Description);
public static class NetworkSetup
{
    public static bool InternetCandidate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return b[0] is >= 0x20 and <= 0x3f && !(b[0] == 0x20 && b[1] == 1 && (b[2] == 0x0d && b[3] == 0xb8 || b[2] == 0 && b[3] < 0x20));
        return b[0] is > 0 and < 224 && b[0] != 10 && b[0] != 127 && !(b[0] == 100 && b[1] is >= 64 and <= 127) && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 88 && b[2] == 99)) && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    public static AddressCandidate[] InvitationCandidates(int port, IPEndPoint? mapped)
    {
        var candidates = Candidates(port);
        if (mapped == null) return candidates;
        var entry = new AddressCandidate(mapped.ToString(), InternetCandidate(mapped.Address)
            ? UiText.RouterAddressReachabilityUnverified : UiText.RouterAddressIsLocalOnly);
        return candidates.Prepend(entry).DistinctBy(c => c.Address)
            .OrderByDescending(c => InternetCandidate(IPEndPoint.Parse(c.Address).Address)).ToArray();
    }
    public static AddressCandidate[] Candidates(int port)
    {
        try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => !IPAddress.IsLoopback(a) && !a.IsIPv6LinkLocal && !a.IsIPv6Multicast && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Select(a => new AddressCandidate(new IPEndPoint(a, port).ToString(), InternetCandidate(a) ? a.AddressFamily == AddressFamily.InterNetworkV6 ? UiText.UnverifiedBothPlayersNeedWorkingIpv6 : UiText.InternetAddressUnverified : UiText.LocalNetworkOnlyLan)).OrderByDescending(a => InternetCandidate(IPEndPoint.Parse(a.Address).Address)).ToArray(); }
        catch { return []; }
    }
    public static void ConfigureFirewall(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        int profiles = policy.CurrentProfileTypes;
        foreach (int protocol in new[] { 6, 17 }) {
            string name = "StarComms " + (protocol == 6 ? "TCP" : "UDP");
            try { policy.Rules.Remove(name); } catch { }
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            rule.Name = name; rule.ApplicationName = Environment.ProcessPath; rule.Protocol = protocol; rule.LocalPorts = port.ToString(); rule.Direction = 1; rule.Action = 1; rule.Profiles = profiles; rule.Enabled = true; policy.Rules.Add(rule);
        }
    }
    public static void RequestFirewall(int port) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--configure-firewall " + port });
}
