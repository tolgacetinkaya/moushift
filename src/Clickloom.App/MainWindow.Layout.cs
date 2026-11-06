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
        bool compact = width < 1250 || height < 850;
        SidebarColumn.Width = new GridLength(width < 1250 ? 210 : 232);
        // Action forms never determine the size of the artwork.
        DiagramStage.Height = compact ? 350 : 480;
        BindingsList.Height = 190;
        HeroMouse.MaxWidth = 400; HeroMouse.MaxHeight = 500;
        EditorPane.Padding = new Thickness(compact ? 12 : 16);
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
    internal void PrepareOffscreenPage(string page)
    {
        var connections = Clickloom.Windows.MouseConnectionCatalog.Enumerate(); _connections = connections;
        SyncDeviceRows(_deviceCards, connections.Where(d => !d.IsAuxiliary));
        DeviceCardsList.ItemsSource = _deviceCards;
        EmptyDevicesBox.Visibility = _deviceCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DevicesSubtitle.Text = _deviceCards.Count + " mouse connections · separate profiles";
        ShowPage(page); ApplyWorkspaceSizing();
    }
    internal void FinishOffscreenWorkspaceLayout() { PlaceMarkers(); }
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
