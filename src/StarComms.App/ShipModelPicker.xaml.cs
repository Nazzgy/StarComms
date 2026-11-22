using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace StarComms.App;

public partial class ShipModelPicker : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(ShipModelPicker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) => ((ShipModelPicker)sender).UpdateLabel()));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public ShipModelPicker()
    {
        InitializeComponent();
        Makers.ItemsSource = ShipCatalog.ByManufacturer.Keys;
        UpdateLabel();
        Unloaded += (_, _) => Menu.IsOpen = false;
    }

    void UpdateLabel()
    {
        if (Display != null) Display.Text = string.IsNullOrWhiteSpace(Text) ? "Vybrat typ lodi…" : Text;
    }

    void Opened(object sender, EventArgs e)
    {
        Search.Clear();
        Makers.SelectedItem = ShipCatalog.ByManufacturer.FirstOrDefault(pair => pair.Value.Contains(Text)).Key
            ?? ShipCatalog.ByManufacturer.Keys.First();
        UpdateModels();
        Models.SelectedItem = Text;
        if (Models.SelectedItem != null) Models.ScrollIntoView(Models.SelectedItem);
        Clear.Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (Menu.IsOpen) Search.Focus(); });
    }

    void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Models == null) return;
        if (Search.Text.Trim().Length > 0) Makers.SelectedIndex = -1;
        else if (Makers.SelectedIndex < 0) Makers.SelectedIndex = 0;
        UpdateModels();
    }

    void MakerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Models == null) return;
        if (Makers.SelectedItem != null && Search.Text.Length > 0) Search.Clear();
        UpdateModels();
    }

    void UpdateModels()
    {
        string query = Search.Text.Trim();
        Models.ItemsSource = Makers.SelectedItem is string maker ? ShipCatalog.ByManufacturer[maker] :
            ShipCatalog.ByManufacturer.SelectMany(pair => pair.Value.Where(model =>
                (pair.Key + " " + model).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
                    (pair.Key + " " + model).Contains(word, StringComparison.OrdinalIgnoreCase))));
        NoResults.Visibility = Models.HasItems ? Visibility.Collapsed : Visibility.Visible;
        Custom.Visibility = query.Length > 0 && !ShipCatalog.Models.Contains(query, StringComparer.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void Commit(string value)
    {
        SetCurrentValue(TextProperty, value);
        Trigger.IsChecked = false;
        Trigger.Focus();
    }

    void ModelClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin &&
            ItemsControl.ContainerFromElement(Models, origin) is ListBoxItem { Content: string model }) Commit(model);
    }

    void MenuKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Trigger.IsChecked = false; Trigger.Focus(); }
        else if (Search.IsKeyboardFocused && e.Key == Key.Down && Models.HasItems) {
            Models.SelectedIndex = 0;
            (Models.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        }
        else if (e.Key == Key.Enter && Models.IsKeyboardFocusWithin && Models.SelectedItem is string model) Commit(model);
        else if (e.Key == Key.Enter && Search.IsKeyboardFocused && Models.Items.Count == 1) Commit((string)Models.Items[0]);
        else return;
        e.Handled = true;
    }

    void UseCustom(object sender, RoutedEventArgs e) => Commit(Search.Text.Trim());
    void ClearType(object sender, RoutedEventArgs e) => Commit("");
}
