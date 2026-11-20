using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using StarComms.Core;
using StarComms.Engine;
using System.Net;
using System.Net.NetworkInformation;

namespace StarComms.App;

public sealed class Command(Func<object?, Task> execute) : ICommand
{
    bool executing;
    public bool CanExecute(object? p) => !executing;
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? p) { if (executing) return; executing = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty); try { await execute(p); } finally { executing = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
}
public sealed record ShipCard(uint ShipId, string Name, string Model, int Count, string Captain, bool IsOwnShip);
public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    readonly Dispatcher dispatcher = Application.Current.Dispatcher;
    public Settings Settings { get; } = LocalData.Read("settings.json", () => new Settings());
    readonly Dictionary<string, string> trusted = LocalData.Read("trusted-hosts.json", () => new Dictionary<string, string>());
    public AudioService Audio { get; } = new();
    readonly DiagnosticLog log = new();
    Host? host; Client? client; Client? connectingClient; RouterMapping? router;
    Task? routerPreparation;
    CancellationTokenSource? connecting;
    string page = "start", message = "", status = "Připojení";
    string? bindingTarget;
    Ship? selectedShip;
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Refresh() => dispatcher.BeginInvoke(() => { selectedShip = client?.Snapshot?.Ships.FirstOrDefault(s => s.ShipId == selectedShip?.ShipId) ?? client?.Snapshot?.Ships.FirstOrDefault(s => s.ShipId == client.Self?.ShipId); Notify(""); });
    public string Version => VersionInfo.Display;
    public bool StartVisible => page == "start";
    public bool FleetVisible => page == "fleet";
    public bool SettingsVisible => page == "settings";
    public string Message { get => message; set { message = value; Notify(); Notify(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;
    public string Status => client?.Status ?? connectingClient?.Status ?? status;
    public bool Busy => connecting != null;
    public bool IsHost => host != null;
    public bool NeedsProbe => client?.Snapshot != null && !client.VoiceReady;
    public bool CanRetry => client?.Status.StartsWith("Spojení se nepodařilo obnovit") == true;
    public string NetworkDetails { get; private set; } = "Zjišťuji dostupná rozhraní…";
    public bool IsCaptain => client?.Self?.IsCaptain == true;
    public bool CanTalk => client?.VoiceReady == true && client.Self?.ShipId != null && Audio.InputAvailable && Audio.OutputAvailable && !Audio.Muted;
    public bool CanCommand => CanTalk && IsCaptain && Captains.Any(m => m.MemberId != client!.MemberId && m.Status == MemberStatus.Online);
    public string Nickname { get => Settings.Nickname; set { Settings.Nickname = value; Notify(); } }
    public string Address { get => Settings.LastAddress; set { Settings.LastAddress = value; Notify(); } }
    public string FleetName { get; set; } = "Večerní let";
    public Ship[] Ships => client?.Snapshot?.Ships ?? [];
    public string ConnectedFleet => client?.Snapshot?.FleetName ?? "";
    public ShipCard[] ShipCards => client?.Snapshot is { } fleet
        ? fleet.Ships.Select(s => new ShipCard(s.ShipId, s.Name, s.Model,
            fleet.Members.Count(m => m.ShipId == s.ShipId),
            fleet.Members.FirstOrDefault(m => m.MemberId == s.CaptainMemberId) is { } captain ? "Kapitán " + captain.Nickname : "Bez kapitána", s.ShipId == client.Self?.ShipId)).ToArray()
        : [];
    public uint? SelectedShipId
    {
        get => selectedShip?.ShipId;
        set { if (value is uint id && Ships.FirstOrDefault(s => s.ShipId == id) is Ship s) SelectedShip = s; }
    }
    public Member[] Crew => client?.Snapshot?.Members.Where(m => SelectedShip != null && m.ShipId == SelectedShip.ShipId).ToArray() ?? [];
    public Member[] Captains => client?.Snapshot?.Members.Where(m => m.IsCaptain).ToArray() ?? [];
    public Admission[] Pending => host?.Pending ?? [];
    public Ship? SelectedShip { get => selectedShip; set { if (selectedShip?.ShipId != value?.ShipId) SelectedMember = null; selectedShip = value; Notify(nameof(CanBoard)); Notify(nameof(CanTransfer)); Notify(); Notify(nameof(Crew)); Notify(nameof(ShipContext)); } }
    Member? selectedMember;
    public Member? SelectedMember
    {
        get => selectedMember;
        set { selectedMember = value; Notify(nameof(CanTransfer)); Notify(); Notify(nameof(MemberVolume)); Notify(nameof(MemberSelected)); }
    }
    public bool CanBoard => SelectedShip != null && client?.Self is { Status: MemberStatus.Online } self && SelectedShip.ShipId != self.ShipId;
    public bool CanTransfer => IsCaptain && SelectedMember is { IsCaptain: false, Status: MemberStatus.Online } member
        && member.ShipId == client?.Self?.ShipId && member.ShipId == SelectedShip?.ShipId;
    public string MicrophoneAction => Audio.Muted ? "Zapnout mikrofon" : "Ztlumit mikrofon";
    public string CrewDestination => Ships.FirstOrDefault(s => s.ShipId == client?.Self?.ShipId) is Ship ship
        ? "Posádka · " + ship.Name : "Posádka · bez lodi";
    public bool MemberSelected => SelectedMember != null;
    public float MemberVolume
    {
        get => SelectedMember is Member m ? Audio.Mixer.MemberVolume(m.MemberId) : 1;
        set { if (SelectedMember is Member m) Audio.Mixer.MemberVolume(m.MemberId, value); }
    }
    public IReadOnlyDictionary<uint, VoiceChannel> Speakers { get; private set; } = new Dictionary<uint, VoiceChannel>();
    public string Receiving => string.Join("   ", Speakers.Where(s => s.Key != client?.MemberId).Select(s =>
        (s.Value == VoiceChannel.Command ? "Kapitáni" : "Posádka") + " · " +
        client?.Snapshot?.Members.FirstOrDefault(m => m.MemberId == s.Key)?.Nickname));
    void RefreshSpeakers()
    {
        var active = Audio.Mixer.ActiveSpeakers().ToDictionary(s => s.Member, s => s.Channel);
        if (client != null && Audio.Transmitting is VoiceChannel local) active[client.MemberId] = local;
        if (active.Count == Speakers.Count && active.All(s => Speakers.TryGetValue(s.Key, out var old) && old == s.Value)) return;
        Speakers = active;
        Notify(nameof(Speakers));
        Notify(nameof(Receiving));
    }
    public string ShipContext => SelectedShip == null ? "Vyber svou loď" : SelectedShip.ShipId == client?.Self?.ShipId ? "Tvoje loď" : "Pouze prohlížíš · audio se nemění";
    public string EmptyShips => Ships.Length == 0 ? IsHost ? "Vytvořit první loď ve správě flotily" : "Hostitel připravuje lodě" : "";
    public string Identity => client?.Self is Member m ? $"{m.Nickname} · {(m.IsCaptain ? "Kapitán" : "Posádka")}{(Audio.Muted ? " · mikrofon ztlumen" : "")}" : "";
    public string TalkStatus => Audio.Transmitting switch { VoiceChannel.Ship => "Mluvíš k posádce vlastní lodi", VoiceChannel.Command => "Mluvíš ke kapitánům. Posádky tě neslyší.", _ => CanTalk ? ShipAutomatic || IsCaptain && CommandAutomatic ? "Hlasová aktivace čeká na řeč" : "Pro mluvení drž tlačítko" : "Mikrofon se neodesílá" };
    public string? VersionWarning => client?.VersionWarning;
    public AudioDevice[] Inputs { get; private set; } = [];
    public AudioDevice[] Outputs { get; private set; } = [];
    public string InputId { get => Settings.Input; set => Settings.Input = value ?? ""; }
    public string OutputId { get => Settings.Output; set => Settings.Output = value ?? ""; }
    public string DeviceStatus => (Audio.InputAvailable ? "Mikrofon připraven" : "Mikrofon nedostupný") + " · " +
        (Audio.OutputAvailable ? "výstup připraven" : "výstup nedostupný");
    public double Level => Audio.InputAvailable ? Audio.LevelDb : -65;
    public int ThresholdDb
    {
        get => Math.Clamp(Settings.ActivationThresholdDb, -60, -15);
        set { Settings.ActivationThresholdDb = Math.Clamp(value, -60, -15); Audio.SetThreshold(ThresholdDb); Notify(); Notify(nameof(BelowThreshold)); Notify(nameof(AboveThreshold)); }
    }
    public GridLength BelowThreshold => new(65 + ThresholdDb, GridUnitType.Star);
    public GridLength AboveThreshold => new(-ThresholdDb, GridUnitType.Star);
    public string PreviewStatus => !Audio.InputAvailable ? "Mikrofon nedostupný" : Audio.PreviewSpeech ? "Řeč zachycena" : "Ticho";
    public float ShipVolume { get => Settings.ShipVolume; set { Settings.ShipVolume = value; Audio.Mixer.ShipVolume = value; } }
    public float CommandVolume { get => Settings.CommandVolume; set { Settings.CommandVolume = value; Audio.Mixer.CommandVolume = value; } }
    public bool Ducking { get => Settings.Ducking; set { Settings.Ducking = value; Audio.Mixer.Ducking = value; } }
    public bool Logs { get => Settings.LoggingEnabled; set { Settings.LoggingEnabled = value; _ = Run(async () => { await log.Set(value); Save(); Notify(nameof(Logs)); }); } }
    public bool ShipAutomatic { get => Settings.ShipAutomatic; set { Settings.ShipAutomatic = value; ApplyModes(); } }
    public bool CommandAutomatic { get => Settings.CommandAutomatic; set { Settings.CommandAutomatic = value; ApplyModes(); } }
    void ApplyModes() { Audio.ConfigureModes(ShipAutomatic, CommandAutomatic); Notify(""); }
    public bool SelectAutomatic(VoiceChannel channel)
    {
        if (!(channel == VoiceChannel.Ship ? ShipAutomatic : CommandAutomatic)) return false;
        if (ShipAutomatic && CommandAutomatic && IsCaptain) { Audio.SelectAutomatic(channel); Notify(""); }
        return true;
    }
    string AutoLabel(VoiceChannel channel) => ShipAutomatic && CommandAutomatic && IsCaptain && Audio.Preferred != channel
        ? "Vybrat pro hlasovou aktivaci" : "Hlasová aktivace";
    public string ShipKey => ShipAutomatic ? AutoLabel(VoiceChannel.Ship) : Settings.ShipBinding == null ? "Nastavit tlačítko ve zvuku" : "Drž " + RawInput.Label(Settings.ShipBinding);
    public string CommandKey => IsCaptain && !Captains.Any(m => m.MemberId != client?.MemberId && m.Status == MemberStatus.Online) ? "Čekáš na dalšího kapitána" : CommandAutomatic ? AutoLabel(VoiceChannel.Command) : Settings.CommandBinding == null ? "Nastavit tlačítko ve zvuku" : "Drž " + RawInput.Label(Settings.CommandBinding);
    public string ShipBindingLabel => "Mluvit k posádce · " + RawInput.Label(Settings.ShipBinding);
    public string CommandBindingLabel => "Mluvit ke kapitánům · " + RawInput.Label(Settings.CommandBinding);
    public ICommand ConnectCommand { get; }
    public ICommand HostCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand DoneCommand { get; }
    public ICommand DevicesCommand { get; }
    public ICommand ToneCommand { get; }
    public ICommand BindShipCommand { get; }
    public ICommand BindCommandCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand LeaveCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand TransferCommand { get; }
    public ICommand ProbeCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand AdmitCommand { get; }
    public ICommand DenyCommand { get; }
    public ICommand AdminCommand { get; }
    public string HostStatus => host == null ? "" : host.VerifiedPlayers.Length == 0
        ? "Hostování běží · čeká se na ověření spojení s hráčem" : "Spojení ověřeno · " + string.Join(", ", host.VerifiedPlayers);
    public ICommand CopyInvitationCommand { get; }
    public ICommand InvitationCommand { get; }
    public ICommand DiagnosticCommand { get; }
    public ICommand AboutCommand { get; }
    Command Cmd(Func<Task> action) => new(_ => Run(action));
    Command Act(Action action) => Cmd(() => { action(); return Task.CompletedTask; });
    async Task Run(Func<Task> action) { try { await action(); } catch (Exception e) { log.Event("OPERATION_FAILED"); Message = Errors.Text(e); } finally { Notify(""); } }
    public MainViewModel()
    {
        Audio.SetThreshold(ThresholdDb);
        ApplyModes();
        ConnectCommand = Cmd(Connect); HostCommand = Cmd(StartHost); CancelCommand = Act(() => connecting?.Cancel()); PasteCommand = Act(() => Address = Clipboard.GetText());
        SettingsCommand = Cmd(async () => { Audio.Paused = true; Audio.Disarm(); page = "settings"; Inputs = Audio.Devices(DataFlow.Capture); Outputs = Audio.Devices(DataFlow.Render); await Audio.Configure(InputId, OutputId); });
        DoneCommand = Cmd(async () => { if (client == null) await Audio.StopDevices(); bindingTarget = null; Audio.Editing = false; Audio.Disarm(); Save(); page = client == null ? "start" : "fleet"; Audio.Paused = false; });
        DevicesCommand = Cmd(async () => { await Audio.Configure(InputId, OutputId); if (Audio.InputAvailable && Audio.OutputAvailable) Message = "Mikrofon i sluchátka jsou připravené."; });
        ToneCommand = Cmd(async () => { await Audio.Tone(OutputId); Message = "Přehrání testovacího tónu dokončeno."; });
        BindShipCommand = Act(() => BeginBinding("ship")); BindCommandCommand = Act(() => BeginBinding("command"));
        MuteCommand = Act(() => Audio.Mute(!Audio.Muted)); LeaveCommand = Cmd(async () => { if (ConfirmLeave()) await Leave(); });
        JoinCommand = Cmd(async () => { if (client?.Self is Member m && SelectedShip != null) await client.Mutate("JoinShip", new JoinShip(SelectedShip.ShipId, m.MembershipEpoch)); });
        TransferCommand = Cmd(async () => { if (client?.Self is Member m && m.ShipId is uint ship && SelectedMember != null) await client.Mutate("TransferCaptain", new TransferCaptain(ship, SelectedMember.MemberId, m.MembershipEpoch)); });
        ProbeCommand = Cmd(async () => { if (client != null) await client.Probe(); }); RetryCommand = Cmd(async () => { if (client != null) await client.Retry(); });
        AdmitCommand = new Command(p => Run(() => { host?.Decide((Guid)p!, true); return Task.CompletedTask; })); DenyCommand = new Command(p => Run(() => { host?.Decide((Guid)p!, false); return Task.CompletedTask; }));
        AdminCommand = Act(() => { if (host != null) new AdminWindow(host, log, SelectedShipId).ShowDialog(); });
        CopyInvitationCommand = Act(() => {
            if (host == null) return;
            var candidate = NetworkSetup.InvitationCandidates(host.Port, router?.Endpoint).FirstOrDefault();
            if (candidate == null) { new InvitationWindow(host, router?.Endpoint).ShowDialog(); return; }
            Clipboard.SetText(JoinAddress.Parse(candidate.Address).Invitation(host.Fingerprint));
            Message = "Pozvánka zkopírována · " + candidate.Description;
        });
        InvitationCommand = Act(() => { if (host != null) new InvitationWindow(host, router?.Endpoint).ShowDialog(); });
        DiagnosticCommand = Act(() => Clipboard.SetText($"StarComms {VersionInfo.Full}\n{Environment.OSVersion}\n{Status}\nUDP: {client?.VoiceReady}\n" + string.Join("\n", NetworkSetup.Candidates(host?.Port ?? 49555).Select(a => a.Address + " " + a.Description))));
        AboutCommand = Act(() => new AboutWindow().ShowDialog());
        Audio.DefaultChanged += flow => dispatcher.BeginInvoke(new Action(async () => {
            if (page is "fleet" or "settings" && (flow == DataFlow.Capture && InputId == "" || flow == DataFlow.Render && OutputId == ""))
                await Run(() => Audio.Configure(InputId, OutputId));
        }));
        Audio.Error += error => dispatcher.BeginInvoke(() => Message = error);
        log.Failed += () => dispatcher.BeginInvoke(() => { Settings.LoggingEnabled = false; Save(); Notify(nameof(Logs)); Message = "Logování nelze zapnout nebo pokračovat. Do složky aplikace nejde zapisovat. " + log.Folder; });
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        ShipVolume = Settings.ShipVolume; CommandVolume = Settings.CommandVolume; Ducking = Settings.Ducking;
        if (LocalData.CorruptSettings) Message = "Nastavení bylo poškozené. Používám výchozí hodnoty.";
    }
    void NetworkChanged(object? s, EventArgs e) => dispatcher.BeginInvoke(() => { if (host != null) host.ResetVerification(); if (host != null) Message = "Síť se změnila. Zkontroluj aktuální adresu v připojení; dosažitelnost je znovu neověřená."; });
    public async Task Initialize()
    {
        if (Settings.LoggingEnabled) await log.Set(true);
        log.Event("START");
        var candidates = await Task.Run(() => NetworkSetup.Candidates(49555));
        NetworkDetails = candidates.Length == 0 ? "Žádné aktivní síťové rozhraní." :
            string.Join(Environment.NewLine, candidates.Select(c => $"{c.Address} · {c.Description}"));
        Notify(nameof(NetworkDetails));
    }
    void Save() { try { LocalData.Save("settings.json", Settings); } catch { Message = $"Nastavení se nepodařilo uložit do složky {LocalData.Root}."; } }
    public void Tick() { RefreshSpeakers(); Notify(nameof(DeviceStatus)); Notify(nameof(PreviewStatus)); Notify(nameof(Level)); Notify(nameof(TalkStatus)); Notify(nameof(CanTalk)); Notify(nameof(CanCommand)); }
    void BeginBinding(string target) { Audio.Disarm(); Audio.Editing = true; bindingTarget = target; Message = "Stiskni tlačítko. Escape přiřazování zruší."; }
    public void Raw(string key, bool down)
    {
        if (bindingTarget != null) {
            if (!down) return;
            if (key == "Escape") { bindingTarget = null; Audio.Editing = false; Message = "Přiřazování zrušeno."; return; }
            if (key == (bindingTarget == "ship" ? Settings.CommandBinding : Settings.ShipBinding)) { Message = "Každý kanál potřebuje jiné tlačítko."; return; }
            if (bindingTarget == "ship") Settings.ShipBinding = key; else Settings.CommandBinding = key;
            bindingTarget = null; Audio.Input(key, key == Settings.ShipBinding ? VoiceChannel.Ship : VoiceChannel.Command, true); Audio.Disarm(); Audio.Editing = false; Message = "Tlačítko nastaveno. Uvolni jej a znovu stiskni pro hovor."; Save(); Notify(""); return;
        }
        bool enabled = page == "fleet" && Application.Current.MainWindow.IsEnabled;
        if (key == Settings.ShipBinding) Audio.Input(key, VoiceChannel.Ship, down, enabled);
        if (key == Settings.CommandBinding) Audio.Input(key, VoiceChannel.Command, down, enabled);
    }
    async Task Connect()
    {
        if (connecting != null || client != null) return;
        var target = JoinAddress.Parse(Address); Fleet.Text(Nickname, 2, 24);
        bool pinnedInvitation = target.Fingerprint != null;
        if (target.Fingerprint == null && trusted.TryGetValue(target.Endpoint, out var pin)) target = target with { Fingerprint = pin };
        connecting = new(); Notify(nameof(Busy)); var next = new Client(); connectingClient = next; next.Changed += Refresh;
        try {
            try { await next.Connect(target, Nickname, ct: connecting.Token); }
            catch (IdentityException e) {
                if (pinnedInvitation) {
                    Message = "Certifikát hostitele neodpovídá pozvánce nebo už neplatí. Vyžádej si aktuální pozvánku.";
                    return;
                }
                await next.DisposeAsync();
                string title = target.Fingerprint == null ? "První připojení k tomuto hostiteli" : "Identita hostitele se změnila";
                if (!ConfirmationWindow.Ask(title, $"Otisk: {e.Fingerprint}\n\nPorovnej otisk s hostitelem.", "Důvěřovat a připojit")) return;
                target = target with { Fingerprint = e.Fingerprint }; next = new(); connectingClient = next; next.Changed += Refresh; await next.Connect(target, Nickname, ct: connecting.Token);
            }
            trusted[target.Endpoint] = target.Fingerprint!; LocalData.Save("trusted-hosts.json", trusted); client = next; Audio.Attach(client); page = "fleet"; Save(); log.Event("CONNECTED", client.MemberId); await Audio.Configure(InputId, OutputId);
        } finally { if (client != next) await next.DisposeAsync(); connecting.Dispose(); connecting = null; connectingClient = null; Notify(""); }
    }
    async Task StartHost()
    {
        if (host != null || client != null || connecting != null) return;
        Fleet.Text(Nickname, 2, 24); status = "Připravuji hostování…"; Notify(nameof(Status));
        try { host = new(FleetName, LocalData.Identity()); host.Changed += Refresh; host.Start(); client = await host.ConnectLocal(Nickname); client.Changed += Refresh; Audio.Attach(client); page = "fleet"; Save(); log.Event("HOST_STARTED"); await Audio.Configure(InputId, OutputId); router = new(); router.LeaseLost += Refresh; routerPreparation = PrepareRouter(router, host.Port); }
        catch { await Leave(); throw; }
    }
    async Task PrepareRouter(RouterMapping mapping, int port)
    {
        try {
            await mapping.Prepare(port);
            if (router == mapping) Refresh();
        } catch (OperationCanceledException) { }
        catch { if (router == mapping) Refresh(); }
    }
    public bool ConfirmLeave() => host == null || ConfirmationWindow.Ask("Ukončit hostování?", "Ukončením hostování odpojíš ostatní hráče.", "Ukončit hostování");
    async Task Leave()
    {
        Audio.Attach(null); await Audio.StopDevices(); if (client != null) await client.DisposeAsync(); if (host != null) await host.DisposeAsync(); if (router != null) { router.Cancel(); if (routerPreparation != null) await routerPreparation; await router.DisposeAsync(); } client = null; host = null; router = null; selectedShip = null; page = "start"; status = "Připojení"; log.Event("LEFT");
    }
    public async ValueTask DisposeAsync() { NetworkChange.NetworkAddressChanged -= NetworkChanged; connecting?.Cancel(); Save(); await Leave(); await Audio.DisposeAsync(); await log.DisposeAsync(); }
}
static class Errors
{
    public static string Text(Exception e) => e.Message switch {
        "INVALID_ADDRESS" => "Adresa není platná. Vlož znovu celé připojení od hostitele.", "INVALID_NAME" => "Zkontroluj délku a znaky jména.", "NAME_IN_USE" => "Tuto přezdívku už někdo používá. Zvol jinou.", "FULL" => "Flotila je plná. Může být připojeno nejvýše 12 hráčů.", "DENIED" => "Hostitel připojení nepřijal.", "VERSION_MISMATCH" => "Máte rozdílné verze protokolu. Získej stejnou verzi jako hostitel.", "STALE_STATE" => "Stav flotily se změnil. Aktualizuji zobrazení.", "RESUME_INVALID" => "Hostitel zahájil novou flotilu nebo platnost obnovení skončila. Připoj se znovu.", "FORBIDDEN" => "Pro tuto změnu nemáš oprávnění.", _ => e is UnauthorizedAccessException ? "Windows odepřel přístup při této operaci." : e is System.Security.Cryptography.CryptographicException crypto ? $"Windows nemohl použít identitu hostitele ({crypto.HResult:X8})." : e is OperationCanceledException ? "Připojování zrušeno nebo vypršel časový limit." : e is System.Net.Sockets.SocketException or System.IO.IOException ? "Hostitel neodpovídá. Ověř, že má spuštěné hostování, a zkus to znovu." : "Operaci se nepodařilo dokončit. Zkontroluj nastavení a zkus to znovu." };
}
