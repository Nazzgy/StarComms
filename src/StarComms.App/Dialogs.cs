using System.Windows;
using System.Windows.Controls;
using StarComms.Core;
using StarComms.Engine;

namespace StarComms.App;

public class ToolWindow : Window
{
    protected StackPanel Body { get; } = new() { Margin = new Thickness(24) };
    protected TextBlock Error { get; } = new() { Foreground = System.Windows.Media.Brushes.Salmon };
    public ToolWindow(string title)
    {
        SetResourceReference(StyleProperty, "AppWindow");
        Title = "StarComms · " + title; Width = 650; Height = 650; MinWidth = 500; MinHeight = 400; Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel(); var version = new TextBlock { Text = VersionInfo.Display, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) }; DockPanel.SetDock(version, Dock.Bottom); dock.Children.Add(version);
        if (this is not ConfirmationWindow) {
            var close = new Button { Content = "Zavřít", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => Close();
            DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);
        }
        dock.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = dock; Body.Children.Add(new TextBlock { Text = title, FontSize = 28 }); Body.Children.Add(Error);
    }
    protected void Button(string label, Action action)
    {
        var button = new Button { Content = label }; button.Click += (_, _) => { try { action(); } catch (Exception e) { Error.Text = Errors.Text(e); } }; Body.Children.Add(button);
    }
    protected TextBox Field(string label, string value = "") { Body.Children.Add(new TextBlock { Text = label }); var input = new TextBox { Text = value }; System.Windows.Automation.AutomationProperties.SetName(input, label); Body.Children.Add(input); return input; }
}
public sealed class InvitationWindow : ToolWindow
{
    public InvitationWindow(Host host, System.Net.IPEndPoint? mapped) : base("Hostování běží")
    {
        var state = new TextBlock();
        void RefreshState()
        {
            var players = host.VerifiedPlayers;
            state.Text = players.Length == 0 ? "Hostování běží. Připojení dalším hráčem zatím neověřeno." :
                "Řídicí i hlasové spojení ověřili: " + string.Join(", ", players) + ".";
        }
        void HostChanged() => Dispatcher.BeginInvoke(RefreshState);
        host.Changed += HostChanged;
        Closed += (_, _) => host.Changed -= HostChanged;
        RefreshState(); Body.Children.Add(state);
        var candidates = NetworkSetup.InvitationCandidates(host.Port, mapped);
        Body.Children.Add(new TextBlock { Text = mapped == null
            ? "Automatické mapování routeru zatím není potvrzené. Pro připojení přes internet může být nutné přesměrování portu."
            : $"Adresa přidělená routerem: {mapped}" });
        var list = new ComboBox { ItemsSource = candidates, DisplayMemberPath = "Address", SelectedIndex = candidates.Length > 0 ? 0 : -1 }; Body.Children.Add(list);
        var detail = new TextBlock { Text = candidates.FirstOrDefault()?.Description ?? "" }; Body.Children.Add(detail); list.SelectionChanged += (_, _) => detail.Text = (list.SelectedItem as AddressCandidate)?.Description ?? "";
        var manual = Field("Ruční vnější IP a port (neověřená adresa)", candidates.FirstOrDefault()?.Address ?? "");
        list.SelectionChanged += (_, _) => manual.Text = (list.SelectedItem as AddressCandidate)?.Address ?? "";
        Button("Kopírovat připojení", () => Clipboard.SetText(JoinAddress.Parse(manual.Text).Invitation(host.Fingerprint)));
        Button("Kopírovat jen IP a port", () => Clipboard.SetText(JoinAddress.Parse(manual.Text).Endpoint));
        Body.Children.Add(new TextBlock { Text = $"TCP i UDP port: {host.Port}\nPočet členů: {host.Fleet.Snapshot.Members.Length}/12\nOtisk certifikátu: {host.Fingerprint}" });
        Button("Povolit hostování ve Windows", () => NetworkSetup.RequestFirewall(host.Port));
        Body.Children.Add(new TextBlock { Text = "Pokud router není připraven, hostitel může ručně přesměrovat tento TCP i UDP port. Připojující se hráči router nenastavují. CGNAT nebo chybějící IPv6 může přímé spojení znemožnit." });
    }
}
public sealed class AboutWindow : ToolWindow
{
    public AboutWindow() : base("O aplikaci a licence")
    {
        Body.Children.Add(new TextBlock { Text = "StarComms propojuje posádky a kapitány přes počítač hostitele. Hovory se nenahrávají." });
        using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("StarComms.Notices")!;
        using var reader = new System.IO.StreamReader(stream);
        Body.Children.Add(new TextBox {
            Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.NoWrap,
            Height = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        });
    }
}

public sealed class ConfirmationWindow : ToolWindow
{
    ConfirmationWindow(string title, string question, string confirm, Window? owner) : base(title)
    {
        if (owner != null) Owner = owner;
        Width = 520; Height = 330; MinHeight = 300;
        Body.Children.Add(new TextBlock { Text = question });
        Button("Zpět", () => DialogResult = false);
        Button(confirm, () => DialogResult = true);
    }
    public static bool Ask(string title, string question, string confirm, Window? owner = null) =>
        new ConfirmationWindow(title, question, confirm, owner).ShowDialog() == true;
}

internal sealed class ShipEditor : ToolWindow
{
    public ShipEditor(Window owner, Ship? ship, Action<string,string> save, string? submitLabel = null) : base(ship == null ? "Přidat loď" : "Upravit loď")
    {
        Owner = owner;
        Width = 520; Height = 445;
        var name = Field("Název lodi", ship?.Name ?? "");
        var model = new ShipModelPicker { Text = ship?.Model ?? "" };
        Body.Children.Add(model);
        Button(submitLabel ?? (ship == null ? "Vytvořit loď" : "Uložit změny"), () => { save(name.Text, model.Text); DialogResult = true; });
        Loaded += (_, _) => name.Focus();
    }
}
