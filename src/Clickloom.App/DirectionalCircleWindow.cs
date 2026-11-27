using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

internal sealed class DirectionalCircleWindow : Window
{
    public DirectionalCircleSettings? Result { get; private set; }

    public DirectionalCircleWindow(MappingProfile profile, bool wheelAssigned, MouseRemappingService? service)
    {
        Title = "Quick gestures";
        Width = 620;
        Height = 620;
        MinWidth = 475;
        MinHeight = 430;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.Brush("Bg");
        Foreground = ThemeManager.Brush("Ink");
        FontFamily = new("Segoe UI");
        FontSize = 13;

        var root = new DockPanel { Margin = new(24) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        footer.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0) });
        var save = new Button { Content = "Save gestures", Style = FindStyle("Primary") };
        footer.Children.Add(save);

        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        body.Children.Add(new TextBlock { Text = "Quick gestures", FontSize = 25, FontWeight = FontWeights.SemiBold });
        AddCaption(body, (profile.DeviceName ?? "Mouse") + " · " + (profile.ApplicationPath is null ? "All applications" : profile.Name), 8);
        AddCaption(body, "Tap the wheel to open Click Circle. Hold the wheel for about a quarter second, swipe in one direction, then release to run its shortcut. A held wheel without a clear swipe does nothing.", 16);
        AddCaption(body, "Quick gestures use the built-in driverless mode and the wheel button’s Click Circle assignment.", 8);
        if (!wheelAssigned) AddCaption(body, "Assign Click Circle to Wheel button for this mouse to use these gestures.", 8);
        if (service?.HasDeviceRouting == true) AddCaption(body, "Exclusive input support is currently active. Quick gestures are available in driverless mode.", 8);

        var enabled = new CheckBox { Content = "Enable quick gestures for this Circle", IsChecked = profile.DirectionalCircle is not null, Margin = new(0, 15, 0, 14) };
        body.Children.Add(enabled);

        var grid = new UniformGrid { Columns = 2, IsEnabled = enabled.IsChecked == true };
        body.Children.Add(grid);
        enabled.Checked += (_, _) => grid.IsEnabled = true;
        enabled.Unchecked += (_, _) => grid.IsEnabled = false;
        var left = AddDirection(grid, "← Left", profile.DirectionalCircle?.Left, service);
        var right = AddDirection(grid, "→ Right", profile.DirectionalCircle?.Right, service);
        var up = AddDirection(grid, "↑ Up", profile.DirectionalCircle?.Up, service);
        var down = AddDirection(grid, "↓ Down", profile.DirectionalCircle?.Down, service);

        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("Ink"), Margin = new(0, 8, 0, 0) };
        body.Children.Add(error);
        save.Click += (_, _) =>
        {
            if (enabled.IsChecked == true)
            {
                var settings = new DirectionalCircleSettings(Read(left), Read(right), Read(up), Read(down));
                if (settings is { Left: null, Right: null, Up: null, Down: null })
                {
                    error.Text = "Choose at least one shortcut or turn off quick gestures.";
                    return;
                }
                Result = settings;
            }
            DialogResult = true;
        };
        Content = root;
    }

    private static ShortcutSelector AddDirection(UniformGrid grid, string caption, DirectionalShortcut? shortcut, MouseRemappingService? service)
    {
        var section = new StackPanel { Margin = new(0, 0, 12, 12) };
        var header = new DockPanel { Margin = new(0, 0, 0, 6) };
        var clear = new Button { Content = "Clear", Style = FindStyle("Quiet"), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(clear, Dock.Right);
        header.Children.Add(clear);
        header.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var selector = new ShortcutSelector { CaptureService = service };
        selector.SetShortcut(shortcut?.KeyCode ?? 0, shortcut?.Modifiers ?? KeyModifiers.None);
        clear.Click += (_, _) => selector.SetShortcut(0, KeyModifiers.None);
        section.Children.Add(header);
        section.Children.Add(selector);
        grid.Children.Add(section);
        return selector;
    }

    private static DirectionalShortcut? Read(ShortcutSelector selector) =>
        selector.KeyCode == 0 ? null : new(selector.KeyCode, selector.Modifiers);

    private static void AddCaption(StackPanel body, string text, double topMargin) =>
        body.Children.Add(new TextBlock { Text = text, Style = FindStyle("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new(0, topMargin, 0, 0) });

    private static Style FindStyle(string name) => (Style)System.Windows.Application.Current.FindResource(name);
}
