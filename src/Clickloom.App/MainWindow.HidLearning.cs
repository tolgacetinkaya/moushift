using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

public partial class MainWindow
{
    private readonly List<Button> _learnedMarkers = [];
    private void OpenHidLearning()
    {
        string? key = EditingProfile.DeviceKey;
        var connection = _connections.FirstOrDefault(c => c.Key == key);
        if (connection is null) { StatusText.Text = "Connect and select a mouse before learning its inputs."; return; }
        _service?.SetEnabled(false); FinishIdentification();
        if (_learningEditor is not null) return;
        Func<IReadOnlyList<HidEndpoint>, Action<ObservedControl>, Action<string>, IDisposable>? begin = _deviceActivity is null ? null
            : (endpoints, observed, status) =>
            {
                var current = _connections.FirstOrDefault(c => c.Key == key) ?? throw new InvalidOperationException("The selected mouse is disconnected.");
                return _deviceActivity.BeginCapture(new WindowInteropHelper(this).Handle, current, endpoints, observed, status);
            };
        _learningEditor = new HidLearningWindow(connection, _settings.LearnedControls, begin);
        MountLearningWorkspace(connection);
    }
    private HidLearningWindow? _learningEditor;
    private string? _learningDeviceKey;
    internal void PrepareLearningWorkspaceFixture()
    {
        if (!_previewOnly || _selectedDevice?.DeviceKey is not { } key) throw new InvalidOperationException("Learning fixture requires an isolated selected mouse.");
        var connection = new MouseConnection(key, _selectedDevice.Name, "Fixture", "USB", true, false, []);
        _learningEditor = new HidLearningWindow(connection, _settings.LearnedControls, (_, _, _) => new PreviewCapture(), true);
        MountLearningWorkspace(connection); _learningEditor.PreparePreview();
    }
    private sealed class PreviewCapture : IDisposable { public void Dispose() { } }
    private void MountLearningWorkspace(MouseConnection connection)
    {
        _learningDeviceKey = connection.Key;
        LearningWorkspaceHost.Content = _learningEditor!.HostIn(SaveLearnedControl, CloseLearningWorkspace);
        LearningWorkspaceHost.Visibility = Visibility.Visible; DevicesWorkspace.Visibility = Visibility.Collapsed;
    }
    private void SaveLearnedControl(LearnedControl control)
    {
        if (control.DeviceKey != _learningDeviceKey || control.DeviceKey != EditingProfile.DeviceKey)
        { StatusText.Text = "The learning source changed. Select the intended mouse again."; return; }
        string key = control.DeviceKey;
        var next = LearnedControls.Save(_settings, control);
        if (!CommitSettings(next, "Learned input and position saved for this mouse. Assignments remain paused.")) return;
        CloseLearningWorkspace();
        RefreshBindings(control.Identity.StandardControl);
        if (control.Identity.StandardControl is null)
        {
            var saved = _settings.LearnedControls.Single(c => c.DeviceKey == key && c.Identity.Key == control.Identity.Key);
            BindingsList.SelectedItem = _bindings.FirstOrDefault(b => b.LearnedId == saved.Id);
        }
        PlaceMarkers();
    }
    private void CloseLearningWorkspace()
    {
        _learningEditor?.ReleaseHosted(); _learningEditor = null; _learningDeviceKey = null;
        LearningWorkspaceHost.Content = null; LearningWorkspaceHost.Visibility = Visibility.Collapsed;
        DevicesWorkspace.Visibility = Visibility.Visible;
        if (_selectedDevice?.DeviceKey is not null) ShowButtonPositions();
    }
    private void PlaceLearnedMarkers()
    {
        foreach (var marker in _learnedMarkers) MarkerCanvas.Children.Remove(marker); _learnedMarkers.Clear();
        string? key = EditingProfile.DeviceKey;
        foreach (var control in _settings.LearnedControls.Where(c => c.DeviceKey == key))
        {
            var point = HeroMouse.TranslatePoint(HeroMouse.NormalizedAnchor(control.X, control.Y), MarkerCanvas);
            if (control.Identity.StandardControl is { } standard)
            {
                var button = standard switch { MouseControl.LeftButton => LeftCallout, MouseControl.RightButton => RightCallout, MouseControl.MiddleButton => MiddleCallout, MouseControl.XButton1 => BackCallout, MouseControl.XButton2 => ForwardCallout, _ => null };
                if (button is not null) { Canvas.SetLeft(button, point.X - 13); Canvas.SetTop(button, point.Y - 13); button.ToolTip = control.Name; continue; }
            }
            var marker = new Button { Content = "+", Width = 26, Height = 26, Padding = new(0), Style = (Style)FindResource("Marker"), ToolTip = control.Name + " · Learned input", Tag = control.Id };
            System.Windows.Automation.AutomationProperties.SetName(marker, control.Name);
            marker.Click += (_, _) =>
            {
                BindingsList.SelectedItem = control.Identity.StandardControl is { } input ? _bindings.FirstOrDefault(b => b.Mouse == input) : _bindings.FirstOrDefault(b => b.LearnedId == control.Id);
                OpenEditor();
            };
            Canvas.SetLeft(marker, point.X - 13); Canvas.SetTop(marker, point.Y - 13); MarkerCanvas.Children.Add(marker); _learnedMarkers.Add(marker);
        }
    }
}
