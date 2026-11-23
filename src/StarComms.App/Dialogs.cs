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
            var close = new Button { Content = UiText.Close, HorizontalAlignment = HorizontalAlignment.Right };
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
public sealed class ShareInvitationWindow : ToolWindow
{
    public ShareInvitationWindow(string invitation, string description) : base(UiText.InvitePlayers)
    {
        Width = 540; Height = 360; MinHeight = 300;
        Body.Children.Add(new TextBlock { Text = UiText.SendThisLinkToYourFriendThenAccept, TextWrapping = TextWrapping.Wrap });
        var link = Field(UiText.PlayerInvitation, invitation);
        link.IsReadOnly = true;
        Body.Children.Add(new TextBlock { Text = description });
        Button(UiText.CopyInvitation, () => {
            link.Focus();
            link.SelectAll();
            try { Clipboard.SetText(invitation); }
            catch (System.Runtime.InteropServices.ExternalException) { Error.Text = UiText.TheLinkIsSelectedPressCtrlCTo; return; }
            if (Owner?.DataContext is MainViewModel vm) vm.Message = UiText.InvitationCopiedSendItToYourFriend;
            Close();
        });
    }
}
public sealed class InvitationWindow : ToolWindow
{
    public InvitationWindow(Host host, System.Net.IPEndPoint? mapped, Settings settings, bool localOnly = false, bool internetOnly = false) : base(localOnly ? UiText.InvitePlayers : UiText.Hosting)
    {
        if (localOnly) {
            var local = NetworkSetup.Candidates(host.Port).FirstOrDefault(c =>
                !NetworkSetup.InternetCandidate(System.Net.IPEndPoint.Parse(c.Address).Address));
            Body.Children.Add(new TextBlock { Text = UiText.OnTheSameNetwork, FontWeight = FontWeights.SemiBold });
            if (local != null) {
                Body.Children.Add(new TextBlock { Text = UiText.IfYouShareARouterSendYourFriend });
                Button(UiText.CopyLocalInvitation, () => {
                    Clipboard.SetText(JoinAddress.Parse(local.Address).Invitation(host.Fingerprint));
                    if (Owner?.DataContext is MainViewModel vm) vm.Message = UiText.LocalInvitationCopiedSendItToAPlayer;
                    Close();
                });
            } else Body.Children.Add(new TextBlock { Text = UiText.ThisComputerHasNoLocalAddressAvailableYet });
            Body.Children.Add(new TextBlock { Text = UiText.PlayingFromDifferentHomes, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,16,0,4) });
            Body.Children.Add(new TextBlock { Text = UiText.StarcommsCouldNotFindAPublicAddressAnother });
            Button(UiText.InternetConnectionSettings, () => new InvitationWindow(host, mapped, settings, internetOnly: true) { Owner = this }.ShowDialog());
            return;
        }
        var state = new TextBlock();
        void RefreshState()
        {
            var players = host.VerifiedPlayers;
            state.Text = players.Length == 0 ? UiText.HostingIsActiveWaitingForAnotherPlayerTo :
                UiText.ControlAndVoiceConnectionsVerifiedBy + string.Join(", ", players) + ".";
        }
        void HostChanged() => Dispatcher.BeginInvoke(RefreshState);
        host.Changed += HostChanged;
        Closed += (_, _) => host.Changed -= HostChanged;
        RefreshState(); Body.Children.Add(state);
        var candidates = NetworkSetup.InvitationCandidates(host.Port, mapped);
        if (internetOnly) candidates = candidates.Where(c => NetworkSetup.InternetCandidate(System.Net.IPEndPoint.Parse(c.Address).Address)).ToArray();
        Body.Children.Add(new TextBlock { Text = mapped == null
            ? UiText.AutomaticPortMappingIsUnconfirmedYouMayNeed
            : NetworkSetup.InternetCandidate(mapped.Address) ? string.Format(UiText.RouterAssignedAddress, mapped) : string.Format(UiText.TheRouterReportsAPrivateAddress, mapped) });
        var list = new ComboBox { ItemsSource = candidates, DisplayMemberPath = "Address", SelectedIndex = candidates.Length > 0 ? 0 : -1 }; if (candidates.Length > 0) Body.Children.Add(list);
        var detail = new TextBlock { Text = candidates.FirstOrDefault()?.Description ?? "" }; Body.Children.Add(detail); list.SelectionChanged += (_, _) => detail.Text = (list.SelectedItem as AddressCandidate)?.Description ?? "";
        var manual = Field(UiText.PublicIpForInvitationsOptionalPort, settings.HostPublicEndpoint);
        Body.Children.Add(new TextBlock { Text = UiText.CloseThisWindowToSaveTheAddressClear, TextWrapping = TextWrapping.Wrap });
        void SavePublicEndpoint()
        {
            string value = manual.Text.Trim();
            if (value.Length > 0) {
                var address = JoinAddress.Parse(value);
                if (address.Fingerprint != null || !NetworkSetup.InternetCandidate(address.Address))
                    throw new FormatException(UiText.EnterAPublicIpAddressOptionallyFollowedBy);
                value = System.Net.IPAddress.TryParse(value, out _) ? new System.Net.IPEndPoint(address.Address, mapped?.Port ?? host.Port).ToString() : address.Endpoint;
            }
            if (value == settings.HostPublicEndpoint) return;
            var previous = settings.HostPublicEndpoint;
            settings.HostPublicEndpoint = value;
            try { LocalData.Save("settings", settings); }
            catch { settings.HostPublicEndpoint = previous; throw; }
        }
        Closing += (_, e) => {
            try { SavePublicEndpoint(); }
            catch (Exception ex) { e.Cancel = true; Error.Text = ex is FormatException ? ex.Message : Errors.Text(ex); }
        };
        string CopyEndpoint() {
            SavePublicEndpoint();
            return settings.HostPublicEndpoint.Length > 0 ? settings.HostPublicEndpoint : (list.SelectedItem as AddressCandidate)?.Address ?? "";
        }
        Button(UiText.CopyConnectionInvitation, () => Clipboard.SetText(JoinAddress.Parse(CopyEndpoint()).Invitation(host.Fingerprint)));
        Button(UiText.CopyIpAndPortOnly, () => Clipboard.SetText(JoinAddress.Parse(CopyEndpoint()).Endpoint));
        Body.Children.Add(new TextBlock { Text = string.Format(UiText.TcpAndUdpPortNmembersNcertificateFingerprint, host.Port, host.Fleet.Snapshot.Members.Length, host.Fingerprint) });
        Button(UiText.AllowHostingInWindows, () => NetworkSetup.RequestFirewall(host.Port));
        Body.Children.Add(new TextBlock { Text = UiText.ForManualSetupForwardThisPortForBoth });
    }
}
public sealed class AboutWindow : ToolWindow
{
    public AboutWindow() : base(UiText.AboutAndLicenses)
    {
        Body.Children.Add(new TextBlock { Text = UiText.StarcommsConnectsCrewsAndCaptainsThroughTheHost });
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
        Button(UiText.Back, () => DialogResult = false);
        Button(confirm, () => DialogResult = true);
    }
    public static bool Ask(string title, string question, string confirm, Window? owner = null) =>
        new ConfirmationWindow(title, question, confirm, owner).ShowDialog() == true;
}

internal sealed class ShipEditor : ToolWindow
{
    public ShipEditor(Window owner, Ship? ship, Action<string,string> save, string? submitLabel = null) : base(ship == null ? UiText.AddShip : UiText.EditShip)
    {
        Owner = owner;
        Width = 520; Height = 445;
        var name = Field(UiText.ShipName, ship?.Name ?? "");
        var model = new ShipModelPicker { Text = ship?.Model ?? "" };
        Body.Children.Add(model);
        Button(submitLabel ?? (ship == null ? UiText.CreateShip : UiText.SaveChanges), () => { save(name.Text, model.Text); DialogResult = true; });
        Loaded += (_, _) => name.Focus();
    }
}
