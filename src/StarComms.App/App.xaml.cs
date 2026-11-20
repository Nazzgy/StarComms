using System.Windows;

namespace StarComms.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--configure-firewall" && int.TryParse(e.Args[1], out int port)) { try { NetworkSetup.ConfigureFirewall(port); Shutdown(0); } catch { Shutdown(1); } return; }
        if (e.Args is ["--test-profile" or "--profile", var path] && System.IO.Path.IsPathFullyQualified(path))
            LocalData.ProfileDirectory = path;
        while (!LocalData.CanWriteProfile(LocalData.Root))
        {
            var message = $"Do složky {LocalData.Root} nelze zapisovat. Vyber složku pro nastavení a identitu hostitele. Prázdná složka vytvoří nový profil.";
            if (MessageBox.Show(message, "StarComms · " + StarComms.Core.VersionInfo.Display,
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) { Shutdown(); return; }
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Vyber složku profilu StarComms" };
            if (picker.ShowDialog() != true) { Shutdown(); return; }
            LocalData.ProfileDirectory = picker.FolderName;
        }
        var window = new MainWindow();
        if (e.Args is ["--check-startup"])
        {
            window.ContentRendered += (_, _) => window.Dispatcher.InvokeAsync(window.Close);
        }
        window.Show();
    }
}
