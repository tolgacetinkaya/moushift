using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

/// <summary>A non-activating Circle. The painted root is 210 DIP; the transparent canvas accommodates folder arcs.</summary>
public sealed class RingWindow : Window
{
    public event EventHandler<RingInvocation>? Invoked;
    internal const double CircleSize = 460;
    private static readonly Point Center = new(230, 230);
    private readonly IReadOnlyList<RingSlot> _root;
    private readonly List<int> _path = [];
    private readonly Canvas _canvas = new() { Width = CircleSize, Height = CircleSize, Background = Brushes.Transparent };
    private readonly List<Node> _nodes = [];
    private readonly TextBlock _title = new() { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Width = 68, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 44 };
    private readonly TextBlock _hint = new() { FontSize = 10, Width = 250, TextAlignment = TextAlignment.Center };
    private readonly System.Windows.Threading.DispatcherTimer _pointer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly bool _preview;
    private readonly long _opened = Stopwatch.GetTimestamp();
    private int[]? _hovered, _pressed;
    private bool _pressStarted, _pressCancelled, _adjusted;

    public RingWindow(IReadOnlyList<RingSlot> slots, bool preview = false)
    {
        _root = slots; _preview = preview;
        Width = Height = CircleSize; FontFamily = new("Segoe UI"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Content = _canvas; Draw();
        MouseMove += (_, e) => Hover(Hit(e.GetPosition(_canvas)));
        MouseLeave += (_, _) => Hover(null);
        if (preview)
        {
            _canvas.MouseLeftButtonDown += (_, e) => { Press(e.GetPosition(_canvas)); e.Handled = true; };
            _canvas.MouseLeftButtonUp += (_, e) => { Click(e.GetPosition(_canvas)); e.Handled = true; };
            MouseWheel += (_, e) => { ScrollAtCursor(Math.Sign(e.Delta)); e.Handled = true; };
        }
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | (nint)0x08000080);
            HwndSource.FromHwnd(hwnd)?.AddHook((nint window, int message, nint wParam, nint lParam, ref bool handled) =>
            { if (message == 0x21) { handled = true; return 3; } return 0; });
        };
        _pointer.Tick += (_, _) => { if (IsVisible) Hover(Hit(CursorPoint())); };
        Loaded += (_, _) => _pointer.Start(); Closed += (_, _) => _pointer.Stop();
    }
    private void Draw()
    {
        if (_title.Parent is Panel parent) parent.Children.Remove(_title);
        _canvas.Children.Clear(); _nodes.Clear(); _hovered = null;
        var guide = new System.Windows.Shapes.Ellipse { Width = 164, Height = 164, Stroke = ThemeManager.Brush("Line"), StrokeThickness = .6, Opacity = .5, IsHitTestVisible = false };
        Put(guide, Center, 164, 164);
        for (int i = 0; i < _root.Count; i++) AddNode(_root[i], [i], CircleVisuals.Root(Center, i), _path.Count == 0 || _path[0] == i);
        if (_path.Count > 0 && RingNavigation.Resolve(_root, _path) is { } folder)
            for (int i = 0; i < folder.Children.Count; i++) AddNode(folder.Children[i], _path.Append(i).ToArray(), CircleVisuals.Child(Center, _path[0], i, folder.Children.Count), true);
        _title.Foreground = ThemeManager.Brush("Ink");
        var centerContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        centerContent.Children.Add(new TextBlock { Text = _path.Count == 0 ? "\uE8BB" : "\uE72B", FontFamily = new("Segoe Fluent Icons"), FontSize = 12, Foreground = ThemeManager.Brush("MetalEdge"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 5) });
        centerContent.Children.Add(_title);
        Put(new Border { Width = 76, Height = 76, CornerRadius = new(38), Background = ThemeManager.Brush("RingIdle"), BorderBrush = ThemeManager.Brush("Line"), BorderThickness = new(1), Child = centerContent }, Center, 76, 76);
        _hint.Foreground = ThemeManager.Brush("Muted"); _hint.Text = _path.Count > 0 ? "Select an action · center to go back" : "Click Circle";
        Canvas.SetLeft(_hint, 105); Canvas.SetTop(_hint, 440); _canvas.Children.Add(_hint); UpdateCenter();
    }
    private void AddNode(RingSlot slot, int[] path, Point position, bool emphasized)
    {
        var visual = new Border { Width = 46, Height = 46, CornerRadius = new(23), Background = ThemeManager.Brush("RingIdle"), BorderBrush = ThemeManager.Brush("Line"), BorderThickness = new(1), Opacity = emphasized ? 1 : .5, IsHitTestVisible = false,
            Child = new TextBlock { Text = CircleVisuals.Icon(slot), FontFamily = new("Segoe Fluent Icons"), FontSize = 20, Foreground = ThemeManager.Brush(CircleVisuals.Empty(slot) ? "Muted" : "Ink"), HorizontalAlignment = HorizontalAlignment.Center } };
        System.Windows.Automation.AutomationProperties.SetName(visual, slot.Name); Put(visual, position, 46, 46); _nodes.Add(new(path, position, visual));
    }
    private void Put(FrameworkElement item, Point point, double width, double height) { Canvas.SetLeft(item, point.X - width / 2); Canvas.SetTop(item, point.Y - height / 2); _canvas.Children.Add(item); }
    private int[]? Hit(Point point) => _nodes.LastOrDefault(n => (n.Position - point).Length <= 24)?.Path;
    private static bool Same(int[]? a, int[]? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);
    private Point CursorPoint() { GetCursorPos(out var p); return _canvas.PointFromScreen(new Point(p.X, p.Y)); }
    private void Hover(int[]? path)
    {
        if (_pressStarted && !Same(_pressed, path)) _pressCancelled = true;
        if (Same(path, _hovered)) return; _hovered = path;
        foreach (var node in _nodes)
        { bool active = Same(node.Path, path); node.Visual.Background = ThemeManager.Brush(active ? "RingSelection" : "RingIdle"); node.Visual.BorderBrush = ThemeManager.Brush(active ? "Accent" : "Line"); node.Visual.BorderThickness = new(active ? 1.6 : 1); }
        UpdateCenter();
    }
    private void UpdateCenter()
    {
        var slot = _hovered is null ? null : RingNavigation.Resolve(_root, _hovered);
        _title.Text = slot?.Name ?? (_path.Count > 0 ? RingNavigation.Resolve(_root, _path)?.Name : _preview ? "Preview" : "Circle");
        _hint.Text = slot?.Kind switch { RingSlotKind.Folder => "Open folder", RingSlotKind.Adjustment => "Scroll to adjust", _ when slot is not null => "Click to run", _ => _path.Count > 0 ? "Center to go back" : "Hold · point · release" };
    }
    private void Activate(int[] path)
    {
        var slot = RingNavigation.Resolve(_root, path); if (slot is null) return;
        if (slot.Kind == RingSlotKind.Folder) { _path.Clear(); _path.AddRange(path); Draw(); return; }
        if (slot.Kind == RingSlotKind.Adjustment) { Hover(path); return; }
        if (CircleVisuals.Empty(slot)) return;
        Invoked?.Invoke(this, new RingInvocation(path)); Close();
    }
    private void Press(Point point) { _pressStarted = true; _pressCancelled = false; _pressed = Hit(point); Hover(_pressed); }
    public void PressAtCursor() => Press(CursorPoint());
    public void PressAtScreen(int x, int y) => Press(_canvas.PointFromScreen(new Point(x, y)));
    private void Click(Point point)
    {
        var hit = Hit(point); Hover(hit);
        bool cancelled = _pressStarted && (_pressCancelled || !Same(_pressed, hit));
        _pressStarted = false; _pressed = null;
        if (cancelled) return;
        if (hit is not null) { Activate(hit); return; }
        if ((point - Center).Length <= 38 && _path.Count > 0) { _path.RemoveAt(_path.Count - 1); Draw(); }
        else Close();
    }
    public void ClickAtCursor() => Click(CursorPoint());
    public void ClickAtScreen(int x, int y) => Click(_canvas.PointFromScreen(new Point(x, y)));
    public void ReleaseTrigger()
    {
        if (_adjusted) { _adjusted = false; return; }
        if (Stopwatch.GetElapsedTime(_opened).TotalMilliseconds < 501) return;
        var hit = Hit(CursorPoint()); if (hit is not null) Activate(hit);
    }
    public void ScrollAtCursor(int direction) { var hit = Hit(CursorPoint()); Hover(hit); Adjust(hit, direction); }
    private void Adjust(int[]? path, int direction)
    {
        if (path is null || RingNavigation.Resolve(_root, path)?.Kind != RingSlotKind.Adjustment || direction == 0) return;
        _adjusted = true; Invoked?.Invoke(this, new RingInvocation(path, Math.Sign(direction)));
        _hint.Text = direction > 0 ? "Increase +" : "Decrease −";
    }
    public (int Left, int Top, int Width, int Height) PositionAtCursor()
    {
        nint hwnd = new WindowInteropHelper(this).Handle; GetCursorPos(out var point);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea; GetWindowRect(hwnd, out var rect);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int left = Math.Clamp(point.X - width / 2, area.Left, Math.Max(area.Left, area.Right - width));
        int top = Math.Clamp(point.Y - height / 2, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(hwnd, (nint)(-1), left, top, 0, 0, 0x0011); GetWindowRect(hwnd, out rect);
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    internal void PreviewSelection(int index) => Hover([index]);
    internal void ShowFolderPreview() { if (_root.FirstOrDefault(s => s.Kind == RingSlotKind.Folder) is { } folder) Activate([Array.IndexOf(_root.ToArray(), folder)]); }
    internal void VerifyNoActivationAndSelection(bool showWindow = true)
    {
        foreach (var node in _nodes) if (!Same(Hit(node.Position), node.Path)) throw new InvalidOperationException("Circle hit testing differs from its nodes.");
        if (Hit(Center) is not null || Hit(new(0, 0)) is not null) throw new InvalidOperationException("Blank space invokes actions.");
        foreach (string theme in new[] { "copper", "pink" })
        { string previous = ThemeManager.Current; ThemeManager.Apply(theme); if (((SolidColorBrush)ThemeManager.Brush("RingSelection")).Color.A is < 50 or > 90) throw new InvalidOperationException("Selection is no longer translucent."); ThemeManager.Apply(previous); }
        nint before = showWindow ? GetForegroundWindow() : 0; if (showWindow) Show();
        if (showWindow && before != 0 && GetForegroundWindow() != before) throw new InvalidOperationException("Circle stole keyboard focus.");
        RingInvocation? selection = null; Invoked += (_, value) => selection = value;
        Activate([4]); if (selection is null || !selection.Path.SequenceEqual(new[] { 4 })) throw new InvalidOperationException("Command selection was lost.");
        var folder = new RingSlot { Name = "Work", Kind = RingSlotKind.Folder, Children = RingDefaults.Create().Append(RingDefaults.Create()[0]).ToList() };
        var test = new RingWindow(new[] { folder }, true); RingInvocation? child = null; test.Invoked += (_, value) => child = value;
        test.Activate([0]); if (test._nodes.Count != 10 || test._path.Count != 1) throw new InvalidOperationException("Nine-child folder did not open alongside its parent.");
        test.Activate([0, 8]); if (child is null || !child.Path.SequenceEqual(new[] { 0, 8 })) throw new InvalidOperationException("Ninth folder action lost its path.");
        var adjust = new RingWindow(new[] { new RingSlot { Name = "Volume", Kind = RingSlotKind.Adjustment } }, true); var directions = new List<int>(); adjust.Invoked += (_, value) => directions.Add(value.Direction); adjust.Adjust([0], 1); adjust.Adjust([0], -1); adjust.Close();
        if (!directions.SequenceEqual(new[] { 1, -1 })) throw new InvalidOperationException("Adjustment directions were lost.");
        var capture = new RingWindow(RingDefaults.Create(), true); int count = 0; capture.Invoked += (_, _) => count++;
        var first = CircleVisuals.Root(Center, 0); capture.Press(first); capture.Hover([1]); capture.Click(first);
        if (count != 0) throw new InvalidOperationException("Leaving a pressed action failed to cancel it."); capture.Close();
        Console.WriteLine("PASS Circle: discrete hit targets, translucent themes, nine-child arc, capture cancellation and adjustment; no input injected.");
    }
    private sealed record Node(int[] Path, Point Position, Border Visual);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
