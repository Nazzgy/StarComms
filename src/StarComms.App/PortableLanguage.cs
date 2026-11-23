using System.IO;
using System.Windows;
using System.Windows.Controls;
using StarComms.Core;

namespace StarComms.App;

internal static class PortableLanguage
{

    public static bool Initialize()
    {
        string? language = LocalData.Language;
        if (language is not ("cs" or "en")) {
            var panel = new StackPanel { Margin = new Thickness(20) };
            var dialog = new Window {
                Title = "StarComms", Width = 360, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = panel
            };
            dialog.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
            panel.Children.Add(new TextBlock { Text = "Vyber jazyk / Choose your language", FontSize = 18, Margin = new Thickness(0,0,0,12) });
            void Option(string label, string code) {
                var button = new Button { Content = label };
                button.Click += (_, _) => { language = code; dialog.DialogResult = true; };
                panel.Children.Add(button);
            }
            Option("Čeština", "cs");
            Option("English", "en");
            panel.Children.Add(new TextBlock { Text = VersionInfo.Display, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) });
            if (dialog.ShowDialog() != true) return false;
            LocalData.SetLanguage(language!);
        }
        UiText.English = language == "en";
        return true;
    }
}
