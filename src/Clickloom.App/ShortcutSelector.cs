using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Clickloom.Core;
using Clickloom.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Clickloom.App;

public sealed class ShortcutSelector : UserControl
{
    private readonly ComboBox _keys = new() { DisplayMemberPath = "Name", SelectedValuePath = "Code" };
    private readonly CheckBox _ctrl = new() { Content = "Ctrl", Margin = new(0, 0, 13, 0) };
    private readonly CheckBox _alt = new() { Content = "Alt", Margin = new(0, 0, 13, 0) };
    private readonly CheckBox _shift = new() { Content = "Shift", Margin = new(0, 0, 13, 0) };
    private readonly CheckBox _win = new() { Content = "Win" };
    private bool _loading;
    private readonly TextBox _capture = new() { IsReadOnly = true, FontSize = 14, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Background = ThemeManager.Brush("PanelLight"), Foreground = System.Windows.Media.Brushes.Transparent, CaretBrush = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly StackPanel _keyCaps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(11, 5, 11, 5), IsHitTestVisible = false };
    private readonly TextBlock _hint = new() { Text = "Click here and press your shortcut. Esc cancels.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Muted"), Margin = new(0, 9, 0, 14) };
    private long _captureId;
    private readonly Expander _manual = new() { Header = "Build shortcut manually", FontSize = 12 };
    internal void ShowManualEditor() => _manual.IsExpanded = true;
    private (int Key, KeyModifiers Modifiers) _before;
    public MouseRemappingService? CaptureService { get; set; }
    public int KeyCode { get; private set; }
    public KeyModifiers Modifiers { get; private set; }
    public event EventHandler? Changed;

    public ShortcutSelector()
    {
        var panel = new StackPanel();
        var recorder = new Grid(); recorder.Children.Add(_capture); recorder.Children.Add(_keyCaps);
        panel.Children.Add(recorder); panel.Children.Add(_hint);
        AutomationProperties.SetName(_capture, "Press shortcut keys");
        _capture.GotKeyboardFocus += (_, _) => BeginCapture();
        _capture.LostKeyboardFocus += (_, _) => EndCapture();
        _capture.PreviewKeyDown += CaptureFallback;
        Unloaded += (_, _) => EndCapture();
        var manual = new StackPanel();
        var modifiers = new WrapPanel { Margin = new(0, 0, 0, 11) };
        foreach (var box in new[] { _ctrl, _alt, _shift, _win }) { modifiers.Children.Add(box); box.Checked += Change; box.Unchecked += Change; }
        manual.Children.Add(modifiers);
        var keys = new List<KeyChoice> { new(9, "Tab"), new(13, "Enter"), new(27, "Escape"), new(32, "Space"), new(8, "Backspace"), new(46, "Delete"), new(45, "Insert"), new(36, "Home"), new(35, "End"), new(33, "Page Up"), new(34, "Page Down"), new(37, "← Left"), new(38, "↑ Up"), new(39, "→ Right"), new(40, "↓ Down") };
        keys.AddRange(Enumerable.Range(65, 26).Select(code => new KeyChoice(code, ((char)code).ToString())));
        keys.AddRange(Enumerable.Range(48, 10).Select(code => new KeyChoice(code, ((char)code).ToString())));
        keys.AddRange(Enumerable.Range(1, 24).Select(index => new KeyChoice(111 + index, "F" + index)));
        keys.AddRange([new(0xB3, "Play / pause"), new(0xAD, "Mute"), new(0xAE, "Volume down"), new(0xAF, "Volume up"), new(0xB1, "Previous track"), new(0xB0, "Next track"), new(0xA6, "Browser back"), new(0xA7, "Browser forward")]);
        _keys.ItemsSource = keys.Select(key => new KeyChoice(key.Code, InputPresentation.KeyName(key.Code))).ToArray();
        _keys.SelectionChanged += Change;
        AutomationProperties.SetName(_keys, "Shortcut main key");
        manual.Children.Add(_keys);
        var presets = new ComboBox { Margin = new(0, 11, 0, 0), DisplayMemberPath = "Name" };
        presets.ItemsSource = new[]
        {
            new Preset("Choose a shortcut…", 0, KeyModifiers.None),
            new Preset("Alt + Tab · Switch windows", 9, KeyModifiers.Alt),
            new Preset("Alt + Shift + Tab · Previous window", 9, KeyModifiers.Alt | KeyModifiers.Shift),
            new Preset("Win + Tab · Task View", 9, KeyModifiers.Windows),
            new Preset("Ctrl + Tab · Next tab", 9, KeyModifiers.Control),
            new Preset("Ctrl + Shift + Tab · Previous tab", 9, KeyModifiers.Control | KeyModifiers.Shift),
            new Preset("Alt + F4 · Close window", 0x73, KeyModifiers.Alt),
            new Preset("Ctrl + C · Copy", 67, KeyModifiers.Control),
            new Preset("Ctrl + V · Paste", 86, KeyModifiers.Control)
        };
        presets.SelectedIndex = 0;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedItem is not Preset { Code: > 0 } preset) return;
            SetShortcut(preset.Code, preset.Modifiers); Changed?.Invoke(this, EventArgs.Empty);
        };
        AutomationProperties.SetName(presets, "Shortcut presets");
        manual.Children.Add(presets);
        _manual.Content = manual; panel.Children.Add(_manual);
        SetShortcut(0, KeyModifiers.None);
        Content = panel;
    }
    public void SetShortcut(int keyCode, KeyModifiers modifiers)
    {
        _loading = true;
        KeyCode = keyCode; Modifiers = modifiers;
        if (keyCode > 0 && !((IEnumerable<KeyChoice>)_keys.ItemsSource).Any(key => key.Code == keyCode))
            _keys.ItemsSource = ((IEnumerable<KeyChoice>)_keys.ItemsSource).Append(new KeyChoice(keyCode, InputPresentation.KeyName(keyCode))).ToArray();
        _keys.SelectedValue = keyCode;
        _ctrl.IsChecked = modifiers.HasFlag(KeyModifiers.Control); _alt.IsChecked = modifiers.HasFlag(KeyModifiers.Alt);
        _shift.IsChecked = modifiers.HasFlag(KeyModifiers.Shift); _win.IsChecked = modifiers.HasFlag(KeyModifiers.Windows);
        _capture.Text = InputPresentation.Shortcut(keyCode, modifiers);
        _keyCaps.Children.Clear();
        if (keyCode == 0 && modifiers == KeyModifiers.None)
            _keyCaps.Children.Add(new TextBlock { Text = "Click and press keys", FontSize = 12, Foreground = ThemeManager.Brush("Muted") });
        else foreach (string part in _capture.Text.Split(" + "))
        {
            if (_keyCaps.Children.Count > 0) _keyCaps.Children.Add(new TextBlock { Text = "+", FontSize = 11, Foreground = ThemeManager.Brush("Muted"), Margin = new(6, 0, 6, 0) });
            _keyCaps.Children.Add(new Border { Background = ThemeManager.Brush("Panel"), BorderBrush = ThemeManager.Brush("Line"), BorderThickness = new(1), CornerRadius = new(5), Padding = new(9, 4, 9, 4), Child = new TextBlock { Text = part, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ThemeManager.Brush("Ink") } });
        }
        _loading = false;
    }
    private void Change(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        KeyCode = _keys.SelectedValue is int code ? code : 0;
        Modifiers = (_ctrl.IsChecked == true ? KeyModifiers.Control : 0) | (_alt.IsChecked == true ? KeyModifiers.Alt : 0)
            | (_shift.IsChecked == true ? KeyModifiers.Shift : 0) | (_win.IsChecked == true ? KeyModifiers.Windows : 0);
        SetShortcut(KeyCode, Modifiers); Changed?.Invoke(this, EventArgs.Empty);
    }
    private void BeginCapture()
    {
        EndCapture(); _before = (KeyCode, Modifiers);
        if (CaptureService is { IsRunning: true } service)
        {
            service.ShortcutCaptured += Captured;
            try { _captureId = service.BeginShortcutCapture(new WindowInteropHelper(Window.GetWindow(this)).Handle); _hint.Text = "Listening for keys. Esc cancels; click outside to finish."; }
            catch (Exception error) { service.ShortcutCaptured -= Captured; _hint.Text = error.Message; }
        }
        else _hint.Text = "Press keys. System shortcuts require the input engine.";
    }
    private void EndCapture()
    {
        if (CaptureService is { } service) { service.ShortcutCaptured -= Captured; service.EndShortcutCapture(_captureId); }
        _captureId = 0;
        _hint.Text = "Click here and press your shortcut. Esc cancels.";
    }
    private void Captured(object? sender, ShortcutCaptureUpdate update)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_captureId != update.SessionId || !_capture.IsKeyboardFocused) return;
            if (update.Cancelled)
            {
                SetShortcut(_before.Key, _before.Modifiers); Changed?.Invoke(this, EventArgs.Empty);
                _capture.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
            }
            SetShortcut(update.KeyCode, update.Modifiers); Changed?.Invoke(this, EventArgs.Empty);
        });
    }
    private void CaptureFallback(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_captureId != 0) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { SetShortcut(_before.Key, _before.Modifiers); Changed?.Invoke(this, EventArgs.Empty); _capture.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
        int code = KeyInterop.VirtualKeyFromKey(key);
        if (code is < 8 or > 254) return;
        KeyModifiers modifiers = (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? KeyModifiers.Control : 0)
            | (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : 0)
            | (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? KeyModifiers.Windows : 0);
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) code = 0;
        SetShortcut(code, modifiers); Changed?.Invoke(this, EventArgs.Empty);
    }
    private sealed record KeyChoice(int Code, string Name) { public override string ToString() => Name; }
    private sealed record Preset(string Name, int Code, KeyModifiers Modifiers) { public override string ToString() => Name; }
}
