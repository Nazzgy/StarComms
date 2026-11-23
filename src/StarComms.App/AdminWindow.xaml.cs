using System.Windows;
using System.Windows.Controls;
using StarComms.Core;
using StarComms.Engine;

namespace StarComms.App;

public partial class AdminWindow : Window
{
    readonly Host host;
    readonly DiagnosticLog log;
    FleetSnapshot snapshot;
    bool refreshing;
    public sealed record PlayerRow(uint Id, string Label, Member Member);
    sealed record ShipTemplate(string Name, string Model);
    sealed record FleetTemplate(string Name, ShipTemplate[] Ships);

    public AdminWindow(Host host, DiagnosticLog log, uint? selectedShip)
    {
        this.host = host;
        this.log = log;
        snapshot = host.Fleet.Snapshot;
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        VersionLabel.Text = VersionInfo.Display;
        host.Changed += FleetChanged;
        Closed += (_, _) => host.Changed -= FleetChanged;
        Refresh(selectedShip);
    }

    void FleetChanged() => Dispatcher.BeginInvoke(() => Refresh(Ships.SelectedValue as uint?));

    void Refresh(uint? selectedShip)
    {
        refreshing = true;
        try {
            snapshot = host.Fleet.Snapshot;
            var player = Players.SelectedValue;
            Ships.ItemsSource = snapshot.Ships;
            Ships.SelectedValue = selectedShip;
            if (Ships.SelectedIndex < 0 && Ships.Items.Count > 0) Ships.SelectedIndex = 0;
            Players.ItemsSource = snapshot.Members.Select(m => Row(m, true)).ToArray();
            Players.SelectedValue = player;
            NewShip.IsEnabled = snapshot.Ships.Length < 12;
            UpdateShip();
        } finally { refreshing = false; }
    }

    PlayerRow Row(Member member, bool showShip)
    {
        var label = member.Nickname;
        if (member.IsCaptain) label += UiText.CaptainLabel;
        if (member.IsHost) label += UiText.Host;
        if (showShip) label += " · " + (snapshot.Ships.FirstOrDefault(s => s.ShipId == member.ShipId)?.Name ?? UiText.NoShip);
        if (member.Status == MemberStatus.Reconnecting) label += UiText.ReconnectingLabel;
        return new(member.MemberId, label, member);
    }

    void ShipChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) UpdateShip();
    }

    void UpdateShip()
    {
        var ship = Ships.SelectedItem as Ship;
        Details.Visibility = ship == null ? Visibility.Collapsed : Visibility.Visible;
        ShipTitle.Text = ship?.Name ?? UiText.TheFleetHasNoShipsYet;
        if (ship == null) { ShipInfo.Text = UiText.SelectAddShipToGetStarted; return; }
        var captain = snapshot.Members.FirstOrDefault(m => m.MemberId == ship.CaptainMemberId);
        ShipInfo.Text = string.Join(" · ", new[] {ship.Model, captain == null ? UiText.NoCaptain : UiText.CaptainLabelLabel + captain.Nickname}.Where(x => x.Length > 0));
        var selectedCrew = Crew.SelectedValue;
        var selectedBoarding = Boarding.SelectedValue;
        Crew.ItemsSource = snapshot.Members.Where(m => m.ShipId == ship.ShipId).Select(m => Row(m, false)).ToArray();
        Crew.SelectedValue = selectedCrew;
        if (Crew.Items.Count == 1) Crew.SelectedIndex = 0;
        Boarding.ItemsSource = snapshot.Members.Where(m => m.ShipId != ship.ShipId).Select(m => Row(m, true)).ToArray();
        Boarding.SelectedValue = selectedBoarding;
        if (Boarding.Items.Count == 1) Boarding.SelectedIndex = 0;
        EmptyCrew.Visibility = Crew.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Crew.Visibility = Promote.Visibility = Crew.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Boarding.Visibility = Board.Visibility = Boarding.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NobodyToBoard.Visibility = Boarding.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    void UpdateActions()
    {
        Promote.IsEnabled = Crew.SelectedItem is PlayerRow { Member.IsCaptain: false, Member.Status: MemberStatus.Online };
        Board.IsEnabled = Boarding.SelectedItem != null;
        RemovePlayer.IsEnabled = Players.SelectedItem is PlayerRow { Member.IsHost: false };
    }
    void CrewChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    void BoardingChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();
    void PlayerChanged(object sender, SelectionChangedEventArgs e) => UpdateActions();

    void Run(Action action, string success)
    {
        try { action(); Feedback.Text = success; }
        catch (Exception e) { Feedback.Text = e is RuleException { Message: "FULL" } ? UiText.AFleetCanHaveUpTo12Ships : Errors.Text(e); }
    }

    void Mutate(Action<Fleet> change)
    {
        host.Mutate(change);
        log.Event("FLEET_CHANGED");
    }

    void Appoint(object sender, RoutedEventArgs e)
    {
        if (Ships.SelectedItem is Ship ship && Crew.SelectedItem is PlayerRow row)
            Run(() => Mutate(f => f.Captain(ship.ShipId, row.Id)), string.Format(UiText.NowCommands, ship.Name, row.Member.Nickname));
    }

    void Move(object sender, RoutedEventArgs e)
    {
        if (Ships.SelectedItem is Ship ship && Boarding.SelectedItem is PlayerRow row)
            Run(() => Mutate(f => f.Move(row.Id, ship.ShipId, row.Member.MembershipEpoch)), string.Format(UiText.IsAboard, row.Member.Nickname, ship.Name));
    }

    void Kick(object sender, RoutedEventArgs e)
    {
        if (Players.SelectedItem is PlayerRow row && !row.Member.IsHost &&
            ConfirmationWindow.Ask(UiText.RemovePlayer, string.Format(UiText.WillBeDisconnectedFromTheFleet, row.Member.Nickname), UiText.Remove, this))
            Run(() => host.Remove(row.Id), string.Format(UiText.WasRemoved, row.Member.Nickname));
    }

    void Create(object sender, RoutedEventArgs e) => EditShip(null);
    void Rename(object sender, RoutedEventArgs e) { if (Ships.SelectedItem is Ship ship) EditShip(ship); }

    void EditShip(Ship? ship)
    {
        var editor = new ShipEditor(this, ship, (name, model) =>
        {
            uint? id = ship?.ShipId;
            Mutate(f => {
                if (ship == null) id = f.CreateShip(name, model).ShipId;
                else f.RenameShip(ship.ShipId, name, model);
            });
            Refresh(id);
        });
        if (editor.ShowDialog() == true) Feedback.Text = ship == null ? UiText.ShipAdded : UiText.ShipChangesSaved;
    }

    void Delete(object sender, RoutedEventArgs e)
    {
        if (Ships.SelectedItem is Ship ship && ConfirmationWindow.Ask(UiText.RemoveShipLabel, string.Format(UiText.WillBeRemovedItsCrewCanBoardAnother, ship.Name), UiText.RemoveShip, this))
            Run(() => Mutate(f => f.DeleteShip(ship.ShipId)), string.Format(UiText.WasRemovedLabel, ship.Name));
    }

    void SaveTemplate(object sender, RoutedEventArgs e) => Run(() =>
        LocalData.Save("fleet-template", new FleetTemplate(snapshot.FleetName, snapshot.Ships.Select(s => new ShipTemplate(s.Name, s.Model)).ToArray())), UiText.ShipTemplateSaved);

    void LoadTemplate(object sender, RoutedEventArgs e)
    {
        var template = LocalData.Read("fleet-template", () => new FleetTemplate("", []));
        if (template.Ships is not { Length: > 0 }) { Feedback.Text = UiText.TheTemplateContainsNoShips; return; }
        Run(() => Mutate(f => {
            if (f.Snapshot.Ships.Length + template.Ships.Length > 12) throw new RuleException("FULL");
            foreach (var s in template.Ships) { Fleet.Text(s.Name, 1, 32); Fleet.Text(s.Model, 0, 48); }
            foreach (var s in template.Ships) f.CreateShip(s.Name, s.Model);
        }), string.Format(UiText.ShipsAdded, template.Ships.Length));
    }
}
