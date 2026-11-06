using System.Windows;
using System.Windows.Controls;

namespace Clickloom.App;

internal sealed class ResetButtonsWindow : Window
{
    public ResetButtonsWindow(string profileName)
    {
        Title = "Reset buttons"; Width = 470; Height = 350; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.Brush("Bg"); Foreground = ThemeManager.Brush("Ink");
        FontFamily = new("Segoe UI"); FontSize = 13;
        var root = new Grid { Margin = new(28) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "Restore default buttons?", FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var description = new StackPanel { Margin = new(0, 20, 0, 18) };
        description.Children.Add(new TextBlock { Text = profileName, Foreground = ThemeManager.Brush("Accent"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        description.Children.Add(new TextBlock { Text = "Mouse and F13–F24 assignments in this profile will return to normal. Clickloom will pause.\n\nYour Click Circle actions and other profiles stay saved.", Style = (Style)System.Windows.Application.Current.FindResource("Caption"), Margin = new(0, 10, 0, 0) });
        Grid.SetRow(description, 1); root.Children.Add(description);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, IsDefault = true, Margin = new(0, 0, 10, 0) });
        var reset = new Button { Content = "Reset buttons", Style = (Style)System.Windows.Application.Current.FindResource("Primary") };
        reset.Click += (_, _) => DialogResult = true; actions.Children.Add(reset);
        Grid.SetRow(actions, 2); root.Children.Add(actions); Content = root;
    }
}
