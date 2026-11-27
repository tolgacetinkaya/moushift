using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;
using System.Windows.Media;
namespace Clickloom.App;
public partial class MainWindow
{
    internal void EnterInteractivePreview()
    {
        EngineBadge.Text = "Preview · hardware control off";
        EnableButton.Content = "Enable Circle";
        EnableButton.IsEnabled = false;
        StatusText.Text = "Preview · edits save in the preview only";
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
        Resources["SidebarLabelVisibility"] = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Resources["SidebarIconMargin"] = collapsed ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        SidebarToggleIcon.Text = collapsed ? "\uE76C" : "\uE76B";
        SidebarToggle.ToolTip = collapsed ? "Show navigation labels" : "Hide navigation labels";
        bool detail = DevicesWorkspace.Visibility == Visibility.Visible && DeviceDetailPage.Visibility == Visibility.Visible && _selectedDevice?.DeviceKey is not null;
        DeviceInspector.Visibility = InspectorSplitter.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.MinWidth = detail ? 320 : 0;
        InspectorColumn.Width = detail ? new GridLength(Math.Clamp(_workspacePreferences.InspectorWidth, 320, Math.Min(640, Math.Max(320, width - 396)))) : new GridLength(0);
        InspectorSplitterColumn.Width = new GridLength(detail ? 6 : 0);
        double galleryWidth = width - 296;
        int visible = Math.Clamp(_deviceCards.Count, 1, galleryWidth >= 555 ? 3 : 2);
        Resources["DeviceTileWidth"] = Math.Clamp((galleryWidth - visible * 10) / visible, 172, 310);
        Resources["DeviceTileHeight"] = Math.Clamp(height - 338, 350, 480);
        UpdateDashboardOverview();
        // Action forms never determine the size of the artwork.
        DiagramStage.MaxHeight = Math.Clamp(height - 334, 300, 550);
        BindingsList.Height = double.NaN;
        HeroMouse.MaxWidth = HeroMouse.View == MouseDrawingView.Left ? 620 : 400; HeroMouse.MaxHeight = 500;
        DiagramStage.MaxWidth = HeroMouse.View == MouseDrawingView.Left ? 660 : 440;
        EditorPane.Padding = new Thickness(0, 18, 0, 0);
        PlaceMarkers();
    }
    private void UpdateDashboardOverview()
    {
        int connections = _deviceCards.Count;
        int receivers = _deviceCards.Count(card => card.ConnectionLabel == "Receiver connected");
        DashboardConnectionCount.Text = connections.ToString();
        DashboardReceiverCount.Text = receivers == 1 ? "1 receiver included" : $"{receivers} receivers included";
        DashboardReceiverCount.Visibility = receivers > 0 ? Visibility.Visible : Visibility.Collapsed;
        var lowestBattery = _deviceCards.Where(card => card.BatteryPercent.HasValue).MinBy(card => card.BatteryPercent);
        DashboardBattery.DataContext = lowestBattery;
        DashboardBattery.Visibility = lowestBattery is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void PlaceMarkers()
    {
        foreach (var (control, marker) in new[] { (MouseControl.LeftButton, LeftCallout), (MouseControl.RightButton, RightCallout), (MouseControl.MiddleButton, MiddleCallout), (MouseControl.XButton2, ForwardCallout), (MouseControl.XButton1, BackCallout) })
        {
            marker.Visibility = HeroMouse.View == MouseDrawingView.Left && control is MouseControl.LeftButton or MouseControl.RightButton
                ? Visibility.Collapsed : Visibility.Visible;
            var point = HeroMouse.TranslatePoint(HeroMouse.Anchor(control), MarkerCanvas);
            Canvas.SetLeft(marker, point.X - 13); Canvas.SetTop(marker, point.Y - 13);
            marker.ToolTip = InputPresentation.Name(control);
        }
        PlaceLearnedMarkers();
    }
    private void MouseView_Changed(object sender, RoutedEventArgs e)
    {
        if (HeroMouse is null || MarkerCanvas is null) return;
        HeroMouse.View = LeftViewButton?.IsChecked == true ? MouseDrawingView.Left : MouseDrawingView.Top;
        HeroMouse.MaxWidth = HeroMouse.View == MouseDrawingView.Left ? 620 : 400;
        DiagramStage.MaxWidth = HeroMouse.View == MouseDrawingView.Left ? 660 : 440;
        HeroMouse.UpdateLayout();
        PlaceMarkers();
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
        b.BatteryPercent = 60; // Presentation-only value; never a live hardware reading.
        _deviceCards.Add(a); _deviceCards.Add(b);
        bool newProfile = !_settings.Profiles.Any(profile => profile.DeviceKey == a.DeviceKey && profile.ApplicationPath is null);
        DeviceCard_Click(new Button { Tag = a }, new RoutedEventArgs());
        if (newProfile && EditingProfile.DeviceKey == a.DeviceKey)
        {
            var settings = _settings.DeepClone();
            var sample = settings.Profiles.Single(profile => profile.Id == _editingProfileId);
            sample.Bindings.Single(binding => binding.Input == MouseControl.MiddleButton).Action = new() { Kind = ActionKind.Ring };
            CommitSettings(settings, "Sample Circle assignment created");
        }
        a.Activity = "Input detected"; b.Activity = "Last input recently";
        EmptyDevicesBox.Visibility = Visibility.Collapsed;
        EngineBadge.Text = "Circle enabled"; EnableButton.Content = "Pause";
        EnableButton.Style = (Style)FindResource(typeof(Button));
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
            tab.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "ControlEdge");
            tab.SetResourceReference(Control.BackgroundProperty, selected ? "Selection" : "ControlSurface");
            tab.BorderThickness = new Thickness(1);
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
