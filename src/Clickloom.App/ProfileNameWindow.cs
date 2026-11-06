using System.Windows;
using System.Windows.Controls;

namespace Clickloom.App;

internal sealed class ProfileNameWindow : Window
{
    private readonly TextBox _name;
    public string ProfileName => _name.Text.Trim();
    public ProfileNameWindow(string current)
    {
        Title = "Rename profile"; Width = 470; Height = 285; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Bg");
        Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Ink");
        FontFamily = new("Segoe UI"); FontSize = 13;
        var root = new StackPanel { Margin = new(28) };
        root.Children.Add(new TextBlock { Text = "Profile name", FontSize = 23, FontWeight = FontWeights.SemiBold });
        _name = new TextBox { Text = current, MaxLength = 128, Margin = new(0, 20, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(_name, "Profile name"); root.Children.Add(_name);
        var error = new TextBlock { Foreground = ThemeManager.Brush("Danger"), FontSize = 12, Height = 24 };
        root.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0) });
        var save = new Button { Content = "Save", IsDefault = true, Style = (Style)System.Windows.Application.Current.FindResource("Primary") };
        save.Click += (_, _) => { if (ProfileName.Length == 0) error.Text = "Enter a profile name."; else DialogResult = true; };
        buttons.Children.Add(save); root.Children.Add(buttons);
        var surface = new Grid { Background = Background }; surface.Children.Add(root); Content = surface;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
