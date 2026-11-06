using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

/// <summary>Pick a drawing position, explicitly listen, confirm two presses, then save a detached control.</summary>
internal sealed class HidLearningWindow : Window
{
    private readonly MouseConnection _connection;
    private readonly Func<IReadOnlyList<HidEndpoint>, Action<ObservedControl>, Action<string>, IDisposable>? _begin;
    private readonly List<LearnedControl> _existing;
    private readonly MouseIllustration _drawing = new() { Width = 320, Height = 460 };
    private readonly Canvas _picture = new() { Width = 360, Height = 480 };
    private readonly System.Windows.Shapes.Ellipse _marker = new() { Width = 24, Height = 24, StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBox _name = new() { MaxLength = 48, Text = "Extra button" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
    private readonly TextBlock _detected = new() { Text = "No input learned yet", TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly TextBlock _capabilities = new() { Text = "Discovering HID capabilities…", TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly ListBox _saved = new() { Height = 110, DisplayMemberPath = "Name" };
    private readonly Button _listen = new() { Content = "Listen for button", IsEnabled = false }, _save = new() { Content = "Save button", IsEnabled = false };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly bool _preview;
    private IReadOnlyList<HidEndpoint> _endpoints = [];
    private InputLearningSession? _session;
    private IDisposable? _capture;
    private InputIdentity? _identity;
    private Guid _id = Guid.NewGuid();
    private DateTimeOffset _observedAt;
    private double _x, _y;
    private bool _position, _closed;
    private long _expires;
    internal LearnedControl? Result { get; private set; }
    public HidLearningWindow(MouseConnection connection, IEnumerable<LearnedControl> existing,
        Func<IReadOnlyList<HidEndpoint>, Action<ObservedControl>, Action<string>, IDisposable>? begin, bool preview = false)
    {
        _connection = connection; _existing = existing.Where(c => c.DeviceKey == connection.Key).Select(c => c.DeepClone()).ToList(); _begin = begin; _preview = preview;
        Title = "Teach a mouse button"; Width = 970; Height = 810; MinWidth = 860; MinHeight = 740; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Segoe UI"); FontSize = 13; Foreground = B("Ink"); Background = B("Bg");
        var root = new DockPanel { Margin = new(28) }; var header = new StackPanel { Margin = new(0, 0, 0, 22) };
        header.Children.Add(T("Teach a button", 26)); header.Children.Add(T(connection.Name + " · This connection only", 13, "Muted"));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new(0, 18, 0, 0) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0) }); _save.Style = (Style)FindResource("Primary"); _save.Click += (_, _) => Save(); buttons.Children.Add(_save);
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(T("Saved locally · No device settings are changed", 11, "Muted")); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new(390) }); body.ColumnDefinitions.Add(new());
        var left = new StackPanel(); left.Children.Add(T("1   Choose its position", 16));
        Canvas.SetLeft(_drawing, 20); Canvas.SetTop(_drawing, 10); _picture.Children.Add(_drawing);
        _marker.Fill = B("RingSelection"); _marker.Stroke = B("Accent"); _picture.Children.Add(_marker); left.Children.Add(_picture);
        left.Children.Add(T("Click the mouse drawing where this button is located. You can move the marker before saving.", 12, "Muted"));
        _drawing.MouseLeftButtonUp += (_, e) => { if (_drawing.TryNormalize(e.GetPosition(_drawing), out double x, out double y)) PickPosition(x, y); };
        _drawing.SizeChanged += (_, _) => PlaceMarker(); body.Children.Add(left);
        var form = new StackPanel { Margin = new(20, 0, 0, 0) };
        form.Children.Add(T("2   Press the physical button", 16)); form.Children.Add(T("Release all buttons, click Listen, then press the same physical button twice. Original hardware functions still occur while learning.", 12, "Muted"));
        _listen.Margin = new(0, 15, 0, 14); _listen.Click += (_, _) => { if (_capture is null) Listen(); else Stop("Listening cancelled."); }; form.Children.Add(_listen);
        _status.Foreground = B("Muted"); _status.Text = "Choose a point on the drawing first."; form.Children.Add(_status);
        _detected.Margin = new(0, 16, 0, 14); form.Children.Add(_detected);
        form.Children.Add(T("3   Name this button", 16)); _name.Margin = new(0, 10, 0, 12); form.Children.Add(_name);
        System.Windows.Automation.AutomationProperties.SetName(_name, "Learned button name");
        var discovery = new Expander { Header = "Discovered HID capabilities", Content = _capabilities, Margin = new(0, 12, 0, 12) }; _capabilities.Foreground = B("Muted"); form.Children.Add(discovery);
        form.Children.Add(T("Previously learned", 12, "Muted")); _saved.ItemsSource = _existing; _saved.SelectionChanged += (_, _) => { if (_saved.SelectedItem is LearnedControl control) LoadSaved(control); }; form.Children.Add(_saved);
        var fresh = new Button { Content = "+ New button", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        fresh.Click += (_, _) => { Stop(""); _saved.SelectedIndex = -1; _identity = null; _id = Guid.NewGuid(); _position = false; _marker.Visibility = Visibility.Collapsed; _listen.IsEnabled = false; _save.IsEnabled = false; _name.Text = "Extra button"; _detected.Text = "No input learned yet"; _status.Text = "Choose a new position on the drawing."; }; form.Children.Add(fresh);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll); root.Children.Add(body); Content = root;
        _timer.Tick += (_, _) => { if (_capture is HidInputCapture { IsDisposed: true }) Stop("Device changed. Close and reopen learning to rescan."); else if (_capture is not null && Environment.TickCount64 >= _expires) Stop("No confirmed input. Try again; a firmware-only button may send no accessible event."); };
        Loaded += async (_, _) => { if (!_preview) await Discover(); }; Closed += (_, _) => { _closed = true; Stop(""); };
    }
    private async Task Discover()
    {
        try
        {
            var endpoints = await Task.Run(HidInventory.Enumerate); if (_closed) return;
            _endpoints = endpoints.Where(e => e.PhysicalKey.Equals(_connection.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            int buttons = _endpoints.Sum(e => e.Buttons.Sum(b => b.Count));
            _capabilities.Text = $"{_endpoints.Count} associated HID collections · {buttons} declared button usages\nMouse buttons/wheel are observed through Windows Raw Input. Declared usages are not proof that every button produces an accessible event.\nOnly selected-connection inputs are used. Ordinary keyboard typing is ignored.";
            if (_endpoints.Any(e => e.CapabilityError.Length > 0)) _capabilities.Text += "\nSome collections have no readable button capabilities.";
        }
        catch (Exception e) { if (!_closed) _capabilities.Text = "Descriptor discovery unavailable: " + e.Message + "\nStandard mouse learning can still be attempted."; }
    }
    private void PickPosition(double x, double y)
    {
        if (_capture is not null) Stop("Position changed. Click Listen to begin again.");
        _x = x; _y = y; _position = true; PlaceMarker(); _listen.IsEnabled = true;
        _save.IsEnabled = _identity is not null; _status.Text = _identity is null ? "Position selected. Click Listen and press your button twice." : "Position updated. Save to keep this input and its new location.";
    }
    private void PlaceMarker()
    {
        if (!_position) return; var point = _drawing.NormalizedAnchor(_x, _y);
        Canvas.SetLeft(_marker, point.X + 20 - 12); Canvas.SetTop(_marker, point.Y + 10 - 12); _marker.Visibility = Visibility.Visible;
    }
    private void Listen()
    {
        if (!_position || _closed) return;
        if (_begin is null) { _status.Text = "Input observation is unavailable in this preview."; return; }
        _identity = null; _save.IsEnabled = false; _session = new(_connection.Key, Environment.TickCount64 + 350); _expires = Environment.TickCount64 + 25000;
        try { _capture = _begin(_endpoints, Observe, message => { if (!_closed) _status.Text = message; }); _listen.Content = "Listening… Cancel"; _status.Text = "Press the same button twice. Moushift does not block its original function."; _timer.Start(); }
        catch (Exception e) { Stop("Cannot start input learning: " + e.Message); }
    }
    private void Observe(ObservedControl signal)
    {
        if (_closed || _session is null || !_session.Observe(signal.DeviceKey, signal.Identity, signal.Pressed, signal.Pulse, Environment.TickCount64)) return;
        _detected.Text = Label(signal.Identity) + " · " + _session.Confirmations + "/2 presses";
        if (_session.Result is not { } identity) return;
        _identity = identity; _observedAt = DateTimeOffset.UtcNow;
        var existing = _existing.FirstOrDefault(c => c.Identity.Key == identity.Key);
        if (existing is not null) { _id = existing.Id; if (_name.Text == "Extra button") _name.Text = existing.Name; }
        else if (_name.Text == "Extra button") _name.Text = Label(identity);
        Stop(identity.StandardControl is not null ? "Input confirmed. Save the position; standard assignment support is configured separately." : "Input confirmed. This HID input can be saved; exclusive remapping is not available for this input yet.");
        _save.IsEnabled = true;
    }
    private void LoadSaved(LearnedControl control)
    { Stop(""); _id = control.Id; _identity = control.Identity; _observedAt = control.ObservedAt; _name.Text = control.Name; PickPosition(control.X, control.Y); _detected.Text = Label(control.Identity) + " · previously confirmed"; _save.IsEnabled = true; }
    private void Stop(string message) { _capture?.Dispose(); _capture = null; _timer.Stop(); _listen.Content = "Listen for button"; if (message.Length > 0) _status.Text = message; }
    private LearnedControl? BuildResult()
    {
        if (!_position || _identity is null || string.IsNullOrWhiteSpace(_name.Text)) return null;
        return new() { Id = _id, DeviceKey = _connection.Key, Name = _name.Text.Trim(), Identity = _identity, X = _x, Y = _y, ObservedAt = _observedAt };
    }
    private void Save() { Result = BuildResult(); if (Result is null) { _status.Text = "Choose a position, confirm an input and enter a name."; return; } Stop(""); DialogResult = true; }
    private static string Label(InputIdentity input) => input.StandardControl is { } control ? InputPresentation.Name(control)
        : input.Kind == LearnedInputKind.KeyboardKey ? "Extra key F" + (input.Usage - 0x7C + 13) : $"HID button {input.Usage}";
    private static Brush B(string key) => ThemeManager.Brush(key);
    private static TextBlock T(string value, double size, string color = "Ink") => new() { Text = value, FontSize = size, Foreground = B(color), TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 8) };
    internal void PreparePreview()
    {
        PickPosition(.25, .64); _identity = new(new string('A', 64), LearnedInputKind.HidButton, 9, 8, 2); _name.Text = "Custom button"; _observedAt = DateTimeOffset.UtcNow;
        _detected.Text = "HID button 8 · 2/2 presses"; _status.Text = "Example input · preview data. Location chosen by you."; _capabilities.Text = "Preview data · no hardware was accessed"; _save.IsEnabled = true;
    }
    internal void VerifyLearning()
    {
        PreparePreview(); _identity = null; _save.IsEnabled = false; _session = new(_connection.Key, 0);
        var identity = new InputIdentity(new string('A', 64), LearnedInputKind.HidButton, 9, 8, 2);
        Observe(new("other:mouse", identity, true, false));
        if (_session.Confirmations != 0) throw new InvalidOperationException("Another mouse entered the learning view.");
        Observe(new(_connection.Key, identity, true, false));
        if (_identity is not null || _save.IsEnabled) throw new InvalidOperationException("A single press enabled saving.");
        Observe(new(_connection.Key, identity, false, false)); Observe(new(_connection.Key, identity, true, false));
        var result = BuildResult() ?? throw new InvalidOperationException("Learning window lost the position or input.");
        if (result.Identity.Usage != 8 || result.Name != "Custom button" || result.X != .25 || result.Y != .64) throw new InvalidOperationException("Learned control identity/position mismatch.");
        _name.Text = ""; if (BuildResult() is not null) throw new InvalidOperationException("Unnamed button accepted."); _name.Text = "Custom button";
        Console.WriteLine("PASS HID learning view: explicit position, confirmed identity and editable label; no device writes.");
    }
}
