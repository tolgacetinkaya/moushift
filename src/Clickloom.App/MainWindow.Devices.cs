using System.Windows;
using System.Windows.Interop;
using Clickloom.Windows;

namespace Clickloom.App;

public partial class MainWindow
{
    private RawMouseActivity? _deviceActivity;
    private HwndSource? _activitySource;
    private IReadOnlyList<MouseConnection> _connections = [];
    private readonly Dictionary<string, DateTime> _lastDeviceInput = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.ObjectModel.ObservableCollection<DeviceCard> _additionalDevices = [];

    private void StartDeviceActivity()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _activitySource = HwndSource.FromHwnd(handle);
            _deviceActivity = new RawMouseActivity(handle);
            _deviceActivity.ButtonObserved += (_, input) => _service?.ObserveRawMouseButton(input);
            _deviceActivity.WheelObserved += (_, input) => _service?.ObserveRawMouseWheel(input);
            _deviceActivity.ButtonObserved += (_, input) => ObserveInputTest(input);
            _deviceActivity.WheelObserved += (_, input) => ObserveInputTest(
                new(input.DevicePath, input.Delta > 0 ? Clickloom.Core.MouseControl.WheelRight : Clickloom.Core.MouseControl.WheelLeft, true));
            _deviceActivity.VerticalWheelObserved += (_, input) => ObserveInputTest(input);
            _deviceActivity.MotionObserved += (_, input) => _service?.ObserveRawMouseMotion(input);
            _service?.SetRawInputAvailable(true);
            _deviceActivity.Observed += (_, path) =>
            {
                var connection = _connections.FirstOrDefault(c => c.DevicePaths.Contains(path, StringComparer.OrdinalIgnoreCase));
                if (connection is null) return;
                _lastDeviceInput[connection.Key] = DateTime.UtcNow;
                UpdateDeviceActivity();
            };
            _activitySource?.AddHook(ActivityMessage);
        }
        catch (Exception error) { _service?.SetRawInputAvailable(false); StatusText.Text = "Device activity unavailable: " + error.Message; }
    }
    private nint ActivityMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0xFF) _deviceActivity?.Observe(lParam);
        if (message == 0xFE) { _deviceActivity?.DeviceChanged(wParam, lParam); _service?.InvalidateDeviceRoutes(); RefreshDevices(); }
        return 0;
    }
    private void UpdateDeviceActivity()
    {
        foreach (var card in _deviceCards.Concat(_additionalDevices))
        {
            card.Activity = !_lastDeviceInput.TryGetValue(card.Key, out var time) ? "No input observed yet"
                : DateTime.UtcNow - time < TimeSpan.FromSeconds(3) ? "Input just now"
                : DateTime.UtcNow - time < TimeSpan.FromMinutes(1) ? "Input within the last minute"
                : "Last input " + Math.Max(1, (int)(DateTime.UtcNow - time).TotalMinutes) + " min ago";
        }
    }
    private void StopDeviceActivity()
    {
        CloseInputTest();
        _service?.SetRawInputAvailable(false);
        _activitySource?.RemoveHook(ActivityMessage);
        _deviceActivity?.Dispose(); _deviceActivity = null;
    }
    private void SyncDeviceRows(System.Collections.ObjectModel.ObservableCollection<DeviceCard> target, IEnumerable<MouseConnection> devices)
    {
        var rows = devices.ToArray();
        for (int i = target.Count - 1; i >= 0; i--) if (!rows.Any(d => d.Key == target[i].Key)) target.RemoveAt(i);
        for (int i = 0; i < rows.Length; i++)
        {
            var d = rows[i]; var current = target.FirstOrDefault(c => c.Key == d.Key);
            string name = MouseDisplayName(d.Key, d.DisplayName);
            if (current is null) target.Insert(i, new(d.Key, name, d.Manufacturer, d.Status, ThemeManager.Brush("Accent"), d.IsAuxiliary ? null : d.Key));
            else if (current.Name != name || current.Detail != d.Status) target[target.IndexOf(current)] = new(d.Key, name, d.Manufacturer, d.Status, ThemeManager.Brush("Accent"), d.IsAuxiliary ? null : d.Key);
            else if (target.IndexOf(current) != i) target.Move(target.IndexOf(current), i);
        }
        foreach (var row in target) row.BatteryPercent = rows.First(d => d.Key == row.Key).BatteryPercent;
        foreach (var row in target) { row.IsSelected = row.DeviceKey is not null && row.DeviceKey == _selectedDevice?.DeviceKey; row.Routing = row.DeviceKey is null ? "Additional interface" : _service?.CanRouteDevice(row.Key) == true ? "Separate input ready" : _service?.CanTriggerCircleForDevice(row.Key) == true ? "Driverless Circle ready" : "Waiting for input"; }
        if (_selectedDevice?.DeviceKey is { } selected)
        {
            var current = _deviceCards.FirstOrDefault(c => c.DeviceKey == selected);
            if (current is not null) { _selectedDevice = current; DetailBatteryInfo.Content = current; }
        }
    }
    private void ApplyConnectionInventory(IReadOnlyList<MouseConnection> connections)
    {
        _connections = connections; _service?.UpdateDeviceInventory(connections);
        var connectedKeys = connections.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _lastDeviceInput.Keys.Where(k => !connectedKeys.Contains(k)).ToArray()) _lastDeviceInput.Remove(key);
        SyncDeviceRows(_deviceCards, connections.Where(c => !c.IsAuxiliary));
        SyncDeviceRows(_additionalDevices, connections.Where(c => c.IsAuxiliary));
        if (!_editingSavedProfile && _selectedDevice?.DeviceKey is { } selectedKey && !connectedKeys.Contains(selectedKey) && DevicesWorkspace.Visibility == Visibility.Visible)
        {
            if (_circleEditor is not null) _editingSavedProfile = true;
            else { _selectedDevice = null; ShowPage("devices"); }
        }
        AdditionalDevicesList.ItemsSource = _additionalDevices;
        ReceiverConnectionsList.ItemsSource = _deviceCards;
        AdditionalDevicesSection.Visibility = _additionalDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceActivity();
        DevicesList.ItemsSource = connections.Select(c => new DeviceRow(c.Name, c.Status + " · " + c.DevicePaths.Length + " input interfaces")).ToArray();
        DriverProfilesList.ItemsSource = _service?.HasDeviceRouting == true
            ? connections.Where(c => !c.IsAuxiliary && _service.CanRouteDevice(c.Key))
                .Select(c => new DeviceCard(c.Key, MouseDisplayName(c.Key, c.DisplayName), "DEVICE PROFILE", "Verified input route", ThemeManager.Brush("Accent"), c.Key)).ToArray() : [];
        EmptyDevicesBox.Visibility = _deviceCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PresenceDetails.Text = "Last scan at " + DateTime.Now.ToString("HH:mm:ss") + ".";
        ApplyWorkspaceSizing();
    }
    private string MouseDisplayName(string key, string fallback) =>
        _workspacePreferences.MouseNames?.GetValueOrDefault(key) is { Length: > 0 and <= 128 } name ? name : fallback;

    private void RenameMouse_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceKey is not { } key) return;
        var dialog = new ProfileNameWindow(_selectedDevice.Name, "mouse") { Owner = this };
        if (dialog.ShowDialog() != true || _selectedDevice?.DeviceKey != key) return;
        var names = new Dictionary<string, string>(_workspacePreferences.MouseNames ?? []);
        names[key] = dialog.ProfileName;
        if (!SaveWorkspacePreferences(_workspacePreferences with { MouseNames = names })) return;
        _selectedDevice = _selectedDevice with { Name = dialog.ProfileName };
        if (_connections.Any(connection => connection.Key == key))
            ApplyConnectionInventory(_connections);
        DetailHeading.Text = _selectedDevice.Name;
        System.Windows.Automation.AutomationProperties.SetName(MappingPage, "Assignments for " + DetailHeading.Text);
        StatusText.Text = "Mouse name updated.";
    }
    private void AppearanceNav_Click(object sender, RoutedEventArgs e) => ShowPage("appearance");
    private void Quit_Click(object sender, RoutedEventArgs e) => ExitApp();
}
