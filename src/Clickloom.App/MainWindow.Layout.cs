using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;
using System.Windows.Media;
namespace Clickloom.App;
public partial class MainWindow
{
    internal void EnterInteractivePreview()
    {
        EngineBadge.Text = "UI preview · engine disabled";
        EnableButton.IsEnabled = false;
    }
    private bool _layoutEventsBound;
    private void Layout_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_layoutEventsBound) { _layoutEventsBound = true; HeroMouse.SizeChanged += (_, _) => PlaceMarkers(); }
        ApplyWorkspaceSizing();
    }
    private void Layout_SizeChanged(object sender, SizeChangedEventArgs e) { if (IsLoaded) ApplyWorkspaceSizing(); }
    private void ApplyWorkspaceSizing()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width, height = ActualHeight > 0 ? ActualHeight : Height;
        bool collapsed = _workspacePreferences.SidebarCollapsed;
        SidebarColumn.Width = new GridLength(collapsed ? 68 : 190);
        Resources["SidebarLabelVisibility"] = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Resources["SidebarIconMargin"] = collapsed ? new Thickness(0) : new Thickness(0, 0, 12, 0);
        SidebarToggleIcon.Text = collapsed ? "\uE76C" : "\uE76B";
        SidebarToggle.ToolTip = collapsed ? "Expand navigation" : "Collapse navigation";
        InspectorColumn.Width = new GridLength(Math.Clamp(_workspacePreferences.InspectorWidth, 320, Math.Min(640, Math.Max(320, width - SidebarColumn.Width.Value - 396))));
        double galleryWidth = width - SidebarColumn.Width.Value - InspectorColumn.Width.Value - 74;
        int visible = _deviceCards.Count >= 3 && galleryWidth >= 750 ? 3 : 2;
        Resources["DeviceTileWidth"] = Math.Clamp((galleryWidth - visible * 10) / visible, 218, 360);
        Resources["DeviceTileHeight"] = Math.Clamp(height - 365, 380, 640);
        // Action forms never determine the size of the artwork.
        DiagramStage.Height = Math.Clamp(height - 335, 300, 580);
        BindingsList.Height = double.NaN;
        HeroMouse.MaxWidth = 400; HeroMouse.MaxHeight = 500;
        EditorPane.Padding = new Thickness(0, 18, 0, 0);
        PlaceMarkers();
    }
    private void PlaceMarkers()
    {
        foreach (var (control, marker) in new[] { (MouseControl.LeftButton, LeftCallout), (MouseControl.RightButton, RightCallout), (MouseControl.MiddleButton, MiddleCallout), (MouseControl.XButton2, ForwardCallout), (MouseControl.XButton1, BackCallout) })
        {
            var point = HeroMouse.TranslatePoint(HeroMouse.Anchor(control), MarkerCanvas);
            Canvas.SetLeft(marker, point.X - 13); Canvas.SetTop(marker, point.Y - 13);
            marker.ToolTip = InputPresentation.Name(control);
        }
        PlaceLearnedMarkers();
    }
    internal void PrepareOffscreenWorkspace()
    {
        var device = new DeviceCard("preview:razer", "Razer Mouse Dock Pro", "Razer", "Preview fixture", ThemeManager.Brush("Accent"), "preview:razer");
        if (!_deviceCards.Any(d => d.Key == device.Key)) _deviceCards.Add(device);
        DeviceCard_Click(new Button { Tag = device }, new RoutedEventArgs());
        EditingProfile.Bindings.Single(b => b.Input == MouseControl.XButton1).Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
        RefreshBindings(MouseControl.XButton1); OpenEditor(); ApplyWorkspaceSizing(); StatusText.Text = "This mouse only · Assignments off";
    }
    internal void PrepareDesignFixture()
    {
        // Deterministic presentation data is never used by the live inventory or user's store.
        if (!_previewOnly) throw new InvalidOperationException("Design fixtures require preview mode.");
        _deviceCards.Clear();
        var a = new DeviceCard("preview:corsair", "Corsair Dark Core", "Corsair", "USB receiver connected · mouse availability unconfirmed", ThemeManager.Brush("Accent"), "preview:corsair");
        var b = new DeviceCard("preview:logi", "MX Master 4", "Logitech", "Bluetooth interface connected · mouse availability unconfirmed", ThemeManager.Brush("Accent"), "preview:logi");
        _deviceCards.Add(a); _deviceCards.Add(b);
        DeviceCard_Click(new Button { Tag = a }, new RoutedEventArgs());
        EditingProfile.Bindings.Single(x => x.Input == MouseControl.MiddleButton).Action = new() { Kind = ActionKind.Ring };
        a.Activity = "Input detected"; b.Activity = "Last input recently";
        EmptyDevicesBox.Visibility = Visibility.Collapsed;
        EngineBadge.Text = "Circle enabled"; EnableButton.Content = "Pause";
        EnableButton.Style = (Style)FindResource(typeof(Button));
        DevicesSubtitle.Text = "2 Windows mouse connections · 1 receiver";
        ReceiverConnectionsList.ItemsSource = _deviceCards;
        RefreshBindings(MouseControl.MiddleButton); CloseEditor(); ApplyWorkspaceSizing();
    }
    internal void PrepareOffscreenPage(string page)
    {
        ApplyConnectionInventory(Clickloom.Windows.MouseConnectionCatalog.Enumerate());
        ShowPage(page); ApplyWorkspaceSizing();
    }
    internal void FinishOffscreenWorkspaceLayout() { PlaceMarkers(); }
    private void CircleTab_Click(object sender, RoutedEventArgs e) => EditRing_Click(sender, e);
    private void SetInspectorTab(bool circle)
    {
        foreach (var (tab, selected) in new[] { (MappingsNav, !circle), (RingNav, circle) })
        {
            tab.SetResourceReference(Control.ForegroundProperty, selected ? "Accent" : "Muted");
            tab.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Panel");
            tab.SetResourceReference(Control.BackgroundProperty, "Panel");
            tab.BorderThickness = new Thickness(0, 0, 0, selected ? 2 : 0);
            tab.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
    private void ToggleTheme_Click(object sender, RoutedEventArgs e) => ThemeCombo.SelectedValue = ThemeManager.Current == "light" ? "dark" : "light";
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedValue is not string id) return;
        try { ThemeManager.Apply(id, !_previewOnly); UpdateCallouts(); CloseRingVisual(); }
        catch (Exception error) { StatusText.Text = "Could not save appearance: " + error.Message; }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
