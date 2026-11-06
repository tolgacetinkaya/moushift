using System.Windows;
using System.Windows.Controls;

namespace Clickloom.App;

internal sealed class ConfirmActionWindow : Window
{
    public ConfirmActionWindow(string title, string heading, string explanation, string accept)
    {
        Title = title; Width = 480; Height = 335; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.Brush("Bg"); Foreground = ThemeManager.Brush("Ink"); FontFamily = new("Segoe UI"); FontSize = 13;
        var root = new Grid { Margin = new(26) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = heading, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = new TextBlock { Text = explanation, Style = (Style)System.Windows.Application.Current.FindResource("Caption"), Margin = new(0, 18, 0, 18) }; Grid.SetRow(body, 1); root.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, IsDefault = true, Margin = new(0, 0, 10, 0) });
        var confirm = new Button { Content = accept, Style = (Style)System.Windows.Application.Current.FindResource("Primary") }; confirm.Click += (_, _) => DialogResult = true; buttons.Children.Add(confirm);
        Grid.SetRow(buttons, 2); root.Children.Add(buttons); Content = root;
    }
}
