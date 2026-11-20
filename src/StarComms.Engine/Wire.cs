using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarComms.Core;

namespace StarComms.Engine;

public sealed record JoinAddress(IPAddress Address, int Port, string? Fingerprint)
{
    public string Endpoint => new IPEndPoint(Address, Port).ToString();
    public string Invitation(string fingerprint) => $"starcomms://join?v=1&host={Uri.EscapeDataString(Address.ToString())}&port={Port}&fp={fingerprint.ToLowerInvariant()}";
    public static JoinAddress Parse(string text)
    {
        text = text.Trim(); if (text.Length is 0 or > 1024) throw new RuleException("INVALID_ADDRESS");
        string host = text; int port = 49555; string? fp = null;
        if (text.Contains("://")) {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "starcomms" || uri.Host != "join" || uri.AbsolutePath is not ("" or "/") || uri.Fragment != "") throw new RuleException("INVALID_ADDRESS");
            var p = new Dictionary<string, string>();
            foreach (var item in uri.Query.TrimStart('?').Split('&')) { var parts = item.Split('=', 2); if (parts.Length != 2 || !p.TryAdd(parts[0], Uri.UnescapeDataString(parts[1]))) throw new RuleException("INVALID_ADDRESS"); }
            if (p.Count != 4 || !p.TryGetValue("v", out var v) || v != "1" || !p.TryGetValue("host", out host!) || !p.TryGetValue("port", out var ps) || !int.TryParse(ps, out port) || !p.TryGetValue("fp", out fp) || fp.Length != 64 || !fp.All(Uri.IsHexDigit)) throw new RuleException("INVALID_ADDRESS");
        } else if (text.StartsWith('[')) {
            var end = text.IndexOf(']'); if (end < 0 || end + 2 >= text.Length || text[end + 1] != ':' || !int.TryParse(text[(end + 2)..], out port)) throw new RuleException("INVALID_ADDRESS"); host = text[1..end];
        } else if (text.Count(c => c == ':') == 1) { var parts = text.Split(':'); host = parts[0]; if (!int.TryParse(parts[1], out port)) throw new RuleException("INVALID_ADDRESS"); }
        if (port is < 1 or > 65535 || !IPAddress.TryParse(host, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6Multicast || (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.GetAddressBytes()[0] >= 224)) throw new RuleException("INVALID_ADDRESS");
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !string.Equals(ip.ToString(), host, StringComparison.Ordinal))
            throw new RuleException("INVALID_ADDRESS");
        return new(ip, port, fp?.ToLowerInvariant());
    }
}
public static class Certificates
{
    public static X509Certificate2 Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=StarComms", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }
    public static string Fingerprint(X509Certificate2 cert) => Convert.ToHexStringLower(SHA256.HashData(cert.RawData));
    public static bool Valid(X509Certificate2 cert, string? pin) => pin != null && Fingerprint(cert).Equals(pin, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow >= cert.NotBefore.ToUniversalTime() && DateTime.UtcNow <= cert.NotAfter.ToUniversalTime() && cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1"));
}
public sealed record Envelope(int V, string Type, Guid? Id, Guid? ReplyTo, JsonElement Body)
{
    public static Envelope Make(string type, object body, Guid? id = null, Guid? replyTo = null) => new(1, type, id, replyTo, JsonSerializer.SerializeToElement(body, Wire.Json));
    public T Read<T>() => Body.Deserialize<T>(Wire.Json) ?? throw new InvalidDataException();
}
public static class Wire
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new JsonStringEnumConverter() } };
    static readonly HashSet<string> Types = ["ClientHello", "ServerHello", "AdmissionPending", "Accepted", "Rejected", "VoiceReady", "SnapshotRequest", "FleetSnapshot", "JoinShip", "TransferCaptain", "Result", "VoiceEnd", "Ping", "Pong", "Leave", "SessionEnded", "Removed"];
    public static async Task<Envelope> Read(Stream stream, CancellationToken ct)
    {
        byte[] prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, ct); uint n = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (n is < 1 or > 65536) throw new InvalidDataException("FRAME_LENGTH");
        byte[] data = new byte[n]; await stream.ReadExactlyAsync(data, ct);
        var e = JsonSerializer.Deserialize<Envelope>(data, Json) ?? throw new InvalidDataException();
        if (e.V != 1 || !Types.Contains(e.Type) || e.Body.ValueKind != JsonValueKind.Object) throw new InvalidDataException("PROTOCOL"); return e;
    }
    public static async Task Write(Stream stream, Envelope e, CancellationToken ct)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(e, Json); if (data.Length > 65536) throw new InvalidDataException();
        byte[] prefix = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)data.Length); await stream.WriteAsync(prefix, ct); await stream.WriteAsync(data, ct);
    }
}
public sealed record Hello(int ProtocolVersion, string AppVersion, string Nickname, Guid? ResumeSessionId = null, string? ResumeToken = null);
public sealed record ServerHello(int ProtocolVersion, string AppVersion, Guid SessionId, string FleetName, int Capacity = 12);
public sealed record Accepted(uint MemberId, ulong ConnectionId, string ResumeToken, byte[] UdpKeyC2H, byte[] UdpKeyH2C, byte[] NoncePrefixC2H, byte[] NoncePrefixH2C, FleetSnapshot Snapshot);
public sealed record JoinShip(uint ShipId, uint ExpectedMembershipEpoch);
public sealed record TransferCaptain(uint ShipId, uint NewCaptainMemberId, uint ExpectedMembershipEpoch);
public sealed record Result(bool Ok, string? Code, ulong StateVersion);
public sealed record VoiceEnd(uint SenderId, uint SenderEpoch, uint RecipientEpoch, VoiceChannel Channel, uint ShipId, ulong TalkBurstId, uint LastFrameIndex);
