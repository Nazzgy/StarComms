using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using StarComms.Core;
using StarComms.Engine;

namespace StarComms.App;

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public string Nickname { get; set; } = "";
    public string HostPublicEndpoint { get; set; } = "";
    public string LastAddress { get; set; } = "";
    public string LastFleetInvitation { get; set; } = "";
    public string LastFleetName { get; set; } = "";
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public int ActivationThresholdDb { get; set; } = VoiceActivation.DefaultThresholdDb;
    public bool ShipAutomatic { get; set; } = true;
    public bool CommandAutomatic { get; set; }
    public string? ShipBinding { get; set; }
    public string? CommandBinding { get; set; }
    public float ShipVolume { get; set; } = 1;
    public float CommandVolume { get; set; } = 1;
    public bool Ducking { get; set; } = true;
    public bool LoggingEnabled { get; set; }
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 760;
}
public static class LocalData
{
    internal static readonly string ConfigPath = Path.ChangeExtension(Environment.ProcessPath!, ".cfg");
    public static string Root => Path.GetDirectoryName(ConfigPath)!;
    private static readonly string MutexName = @"Local\StarComms.Config." + Convert.ToHexString(
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ConfigPath.ToUpperInvariant())));

    private static Dictionary<string,string> Load()
    {
        var fields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string[] lines;
        try { lines = File.ReadAllLines(ConfigPath); }
        catch (FileNotFoundException) { return fields; }
        foreach (var line in lines) {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#')) continue;
            int separator = text.IndexOf('=');
            if (separator < 1) throw new InvalidDataException("Invalid StarComms.cfg line.");
            fields[text[..separator].Trim()] = text[(separator + 1)..].Trim();
        }
        return fields;
    }

    private static T WithConfig<T>(Func<Dictionary<string,string>, T> operation)
    {
        using var mutex = new Mutex(false, MutexName);
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try {
            var fields = Load();
            var original = new Dictionary<string,string>(fields, StringComparer.OrdinalIgnoreCase);
            var result = operation(fields);
            if (original.Count != fields.Count || fields.Any(pair => original.GetValueOrDefault(pair.Key) != pair.Value)) {
                var temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    File.WriteAllLines(temporary, fields.Select(pair => $"{pair.Key}={pair.Value}"));
                    File.Move(temporary, ConfigPath, true);
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result;
        } finally { mutex.ReleaseMutex(); }
    }

    internal static void Migrate(string? legacyDirectory)
    {
        WithConfig(fields => {
            if (fields.ContainsKey("profileVersion")) return false;
            var directory = legacyDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarComms");
            if (legacyDirectory == null && !File.Exists(Path.Combine(directory, "settings.json"))
                && !File.Exists(Path.Combine(directory, "identity.dat")) && Directory.Exists(Path.Combine(Root, "local-profile")))
                directory = Path.Combine(Root, "local-profile");
            foreach (var key in new[] { "settings", "trusted-hosts", "fleet-template" }) {
                var file = Path.Combine(directory, key + ".json");
                if (!fields.ContainsKey(key) && File.Exists(file)) {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    fields[key] = JsonSerializer.Serialize(json.RootElement, Wire.Json);
                }
            }
            var identity = Path.Combine(directory, "identity.dat");
            if (!fields.ContainsKey("identity") && File.Exists(identity))
                fields["identity"] = Convert.ToBase64String(File.ReadAllBytes(identity));
            fields["profileVersion"] = "1";
            return true;
        });
    }

    internal static string? Language => WithConfig(fields => fields.GetValueOrDefault("language")?.ToLowerInvariant());
    internal static void SetLanguage(string language) => WithConfig(fields => fields["language"] = language);
    public static T Read<T>(string key, Func<T> defaults) =>
        WithConfig(fields => fields.TryGetValue(key, out var value) ? JsonSerializer.Deserialize<T>(value, Wire.Json) ?? defaults() : defaults());
    public static void Save<T>(string key, T value) => WithConfig(fields => fields[key] = JsonSerializer.Serialize(value, Wire.Json));

    public static X509Certificate2 Identity() => WithConfig(fields => {
        if (fields.TryGetValue("identity", out var identity)) {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(identity), null, DataProtectionScope.CurrentUser);
            try {
                var existing = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                if (existing.NotAfter.ToUniversalTime() > DateTime.UtcNow && existing.HasPrivateKey) return existing;
                existing.Dispose();
            } finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var cert = Certificates.Create();
        var raw = cert.Export(X509ContentType.Pfx);
        try { fields["identity"] = Convert.ToBase64String(ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(raw); }
        return cert;
    });
}
