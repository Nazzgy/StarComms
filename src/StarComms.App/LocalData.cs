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
    internal static string? ProfileDirectory { get; set; }
    public static string Root => ProfileDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarComms");
    public static bool CanWriteProfile(string directory)
    {
        try {
            Directory.CreateDirectory(directory);
            using var probe = new FileStream(Path.Combine(directory, Guid.NewGuid() + ".tmp"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        } catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
    }
    public static bool CorruptSettings { get; private set; }
    public static T Read<T>(string name, Func<T> defaults)
    {
        string path = Path.Combine(Root, name); if (!File.Exists(path)) return defaults();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Wire.Json) ?? defaults(); }
        catch { CorruptSettings = true; try { File.Move(path, path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); } catch { } return defaults(); }
    }
    public static void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(Root); string path = Path.Combine(Root, name); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Wire.Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static X509Certificate2 Identity()
    {
        Directory.CreateDirectory(Root); string path = Path.Combine(Root, "identity.dat");
        if (File.Exists(path)) {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try { var existing = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable); if (existing.NotAfter.ToUniversalTime() > DateTime.UtcNow && existing.HasPrivateKey) return existing; existing.Dispose(); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var cert = Certificates.Create(); var raw = cert.Export(X509ContentType.Pfx);
        try { File.WriteAllBytes(path + ".tmp", ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser)); File.Move(path + ".tmp", path, true); } finally { CryptographicOperations.ZeroMemory(raw); } return cert;
    }
}
