using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StarComms.Core;

namespace StarComms.App;
public partial class MainWindow : Window
{
    readonly MainViewModel vm = new(); RawInput? input; bool closing;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public MainWindow()
    {
        InitializeComponent(); DataContext = vm; Width = Math.Clamp(vm.Settings.Width, 900, 2400); Height = Math.Clamp(vm.Settings.Height, 650, 1600);
        SourceInitialized += (_, _) => { try { input = new(new WindowInteropHelper(this).Handle); input.Input += vm.Raw; input.Interrupted += vm.Audio.Disarm; input.LockChanged += locked => { vm.Audio.SessionLocked = locked; vm.Audio.Disarm(); }; } catch { vm.Message = UiText.GlobalButtonsAreUnavailableUseTheButtonIn; } };
        Loaded += async (_, _) => await vm.Initialize(); timer.Tick += (_, _) => vm.Tick(); timer.Start();
        Hold(ShipTalk, VoiceChannel.Ship); Hold(CommandTalk, VoiceChannel.Command);
        PreviewGotKeyboardFocus += (_, e) => { if (e.NewFocus is TextBox) { vm.Audio.Editing = true; vm.Audio.Disarm(); } else vm.Audio.Editing = false; };
        Closing += async (_, e) => {
            if (closing) return; e.Cancel = true; if (!vm.ConfirmLeave()) return; closing = true; timer.Stop(); input?.Dispose(); vm.Settings.Width = ActualWidth; vm.Settings.Height = ActualHeight;
            try { await vm.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } await Dispatcher.InvokeAsync(Close);
        };
    }
    void FleetSelection(object sender, RoutedPropertyChangedEventArgs<object> e) => vm.Inspect(e.NewValue);
    void FleetRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { IsSelected: true } item && vm.CreatingShip) vm.Inspect(item.DataContext);
    }
    void Hold(Button button, VoiceChannel channel)
    {
        string source = "button:" + channel;
        button.PreviewMouseLeftButtonDown += (_, e) => { button.CaptureMouse(); if (!vm.SelectAutomatic(channel)) vm.Audio.Input(source, channel, true); e.Handled = true; };
        button.PreviewMouseLeftButtonUp += (_, e) => { vm.Audio.Input(source, channel, false); button.ReleaseMouseCapture(); e.Handled = true; };
        button.LostMouseCapture += (_, _) => vm.Audio.Input(source, channel, false);
        button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { if (!vm.SelectAutomatic(channel)) vm.Audio.Input(source, channel, true); e.Handled = true; } };
        button.PreviewKeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) { vm.Audio.Input(source, channel, false); e.Handled = true; } };
        button.LostKeyboardFocus += (_, _) => vm.Audio.Input(source, channel, false);
    }
}
