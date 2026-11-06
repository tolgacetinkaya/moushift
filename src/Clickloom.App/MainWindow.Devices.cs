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
            if (current is null) target.Insert(i, new(d.Key, d.Name, d.Manufacturer, d.Status, ThemeManager.Brush("Accent"), d.IsAuxiliary ? null : d.Key));
            else if (current.Name != d.Name || current.Detail != d.Status) target[target.IndexOf(current)] = new(d.Key, d.Name, d.Manufacturer, d.Status, ThemeManager.Brush("Accent"), d.IsAuxiliary ? null : d.Key);
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
            if (_circleEditor is not null || _learningEditor is not null) _editingSavedProfile = true;
            else { _selectedDevice = null; ShowPage("devices"); }
        }
        if (_learningDeviceKey is { } learningKey) _learningEditor?.UpdateConnection(connections.FirstOrDefault(c => c.Key == learningKey));
        AdditionalDevicesList.ItemsSource = _additionalDevices;
        ReceiverConnectionsList.ItemsSource = _deviceCards;
        AdditionalDevicesExpander.Visibility = _additionalDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceActivity();
        int receivers = connections.Count(c => !c.IsAuxiliary && c.IsReceiver);
        DevicesSubtitle.Text = $"{_deviceCards.Count} Windows mouse connections · {receivers} receivers · {_additionalDevices.Count} additional interfaces";
        DevicesList.ItemsSource = connections.Select(c => new DeviceRow(c.Name, c.Status + " · " + c.DevicePaths.Length + " input interfaces")).ToArray();
        DriverProfilesList.ItemsSource = _service?.HasDeviceRouting == true
            ? connections.Where(c => !c.IsAuxiliary && _service.CanRouteDevice(c.Key))
                .Select(c => new DeviceCard(c.Key, c.Name, "DEVICE PROFILE", "Verified input route", ThemeManager.Brush("Accent"), c.Key)).ToArray() : [];
        EmptyDevicesBox.Visibility = _deviceCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PresenceDetails.Text = "Current Windows connections only. Multiple interfaces from one device are grouped. Receiver presence cannot confirm wireless mouse power. Recent input is shown separately. Updated " + DateTime.Now.ToString("HH:mm:ss") + ".";
        ApplyWorkspaceSizing();
    }
    private void AppearanceNav_Click(object sender, RoutedEventArgs e) => ShowPage("appearance");
    private void Quit_Click(object sender, RoutedEventArgs e) => ExitApp();
}
