using StarComms.Core;
using System.Windows;

namespace StarComms.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--configure-firewall" && int.TryParse(e.Args[1], out int port)) { try { NetworkSetup.ConfigureFirewall(port); Shutdown(0); } catch { Shutdown(1); } return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow window;
        try {
            var legacy = e.Args is ["--test-profile" or "--profile", var path] && System.IO.Path.IsPathFullyQualified(path) ? path : null;
            LocalData.Migrate(legacy);
            if (!PortableLanguage.Initialize()) { Shutdown(); return; }
            window = new MainWindow();
        } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.IO.InvalidDataException) {
            MessageBox.Show($"Zkontroluj CFG, původní profil a práva zápisu vedle EXE.\nCheck the CFG, legacy profile and write access beside the EXE.\n\n{ex.Message}",
                "StarComms · " + VersionInfo.Display, MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1); return;
        }
        if (e.Args is ["--check-startup"])
        {
            window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(window.Close);
        }
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }
}
