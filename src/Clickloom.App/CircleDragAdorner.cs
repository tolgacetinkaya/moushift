using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Clickloom.App;

internal enum CircleDragIntent { Waiting, Add, Replace, Swap, Blocked }

/// <summary>A mouse-transparent card in the existing editor window, not a popup or a new HWND.</summary>
internal sealed class CircleDragAdorner : Adorner, IDisposable
{
    private const double CardWidth = 236, CardHeight = 78;
    private readonly FrameworkElement _host;
    private readonly AdornerLayer _layer;
    private readonly Border _card;
    private readonly TextBlock _operation, _badge;
    private readonly ScaleTransform _scale = new(1, 1);
    private Point _position;
    private bool _following, _disposed;
    internal CircleDragIntent Intent { get; private set; }
    internal Point Position => _position;
    internal string Operation => _operation.Text;

    internal CircleDragAdorner(FrameworkElement host, string title, string icon, bool animate = true) : base(host)
    {
        _host = host; _layer = AdornerLayer.GetAdornerLayer(host) ?? throw new InvalidOperationException("The Circle drag layer is unavailable.");
        IsHitTestVisible = false; Focusable = false;
        var content = new DockPanel();
        var symbol = new Border { Width = 40, Height = 40, CornerRadius = new(12), Background = ThemeManager.Brush("RingSelection"), Margin = new(0, 0, 12, 0),
            Child = new TextBlock { Text = icon, FontFamily = new("Segoe Fluent Icons"), FontSize = 21, Foreground = ThemeManager.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        content.Children.Add(symbol);
        _badge = new TextBlock { Width = 22, FontSize = 19, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_badge, Dock.Right); content.Children.Add(_badge);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = title, Foreground = ThemeManager.Brush("Ink"), FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        _operation = new TextBlock { FontSize = 10.5, Margin = new(0, 5, 0, 0), Foreground = ThemeManager.Brush("Muted"), TextTrimming = TextTrimming.CharacterEllipsis };
        labels.Children.Add(_operation); content.Children.Add(labels);
        _card = new Border { Width = CardWidth, Height = CardHeight, CornerRadius = new(14), Padding = new(12), BorderThickness = new(1),
            Background = ThemeManager.Brush("PanelLight"), BorderBrush = ThemeManager.Brush("Line"), Child = content,
            RenderTransformOrigin = new(.1, .5), RenderTransform = _scale,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Opacity = .3, Color = Colors.Black } };
        AddVisualChild(_card); _layer.Add(this); SetIntent(CircleDragIntent.Waiting, "Choose a Circle position");
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Lift(.92, 1)); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Lift(.92, 1));
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(100)) { FillBehavior = FillBehavior.Stop });
        }
    }
    private static DoubleAnimation Lift(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(140))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
    internal void SetIntent(CircleDragIntent intent, string description)
    {
        if (_disposed) return; Intent = intent; _operation.Text = description;
        bool active = intent is CircleDragIntent.Add or CircleDragIntent.Replace or CircleDragIntent.Swap;
        _badge.Text = intent switch { CircleDragIntent.Add => "+", CircleDragIntent.Replace => "↺", CircleDragIntent.Swap => "⇄", CircleDragIntent.Blocked => "×", _ => "↗" };
        _badge.Foreground = ThemeManager.Brush(intent == CircleDragIntent.Blocked ? "Danger" : active ? "Accent" : "Muted");
        _card.BorderBrush = ThemeManager.Brush(intent == CircleDragIntent.Blocked ? "Danger" : active ? "Accent" : "Line");
    }
    internal void PositionAt(Point pointer)
    {
        if (_disposed) return;
        var next = new Point(Math.Clamp(pointer.X + 18, 14, Math.Max(14, _host.ActualWidth - CardWidth - 18)),
            Math.Clamp(pointer.Y + 16, 14, Math.Max(14, _host.ActualHeight - CardHeight - 18)));
        if (_position == next) return; _position = next;
        _layer.Update(_host);
    }
    internal void FollowCursorOnce()
    {
        if (_disposed || !GetCursorPos(out var cursor) || PresentationSource.FromVisual(_host) is null) return;
        var point = _host.PointFromScreen(new Point(cursor.X, cursor.Y));
        Visibility = point.X >= 0 && point.Y >= 0 && point.X <= _host.ActualWidth && point.Y <= _host.ActualHeight ? Visibility.Visible : Visibility.Hidden;
        PositionAt(point);
    }
    internal void StartFollowing()
    {
        if (_following || _disposed) return; _following = true;
        CompositionTarget.Rendering += OnRendering; FollowCursorOnce();
    }
    private void OnRendering(object? sender, EventArgs e) => FollowCursorOnce();
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _card : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureOverride(Size constraint) { _card.Measure(new Size(CardWidth, CardHeight)); return _card.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize) { _card.Arrange(new Rect(0, 0, CardWidth, CardHeight)); return finalSize; }
    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var group = new GeneralTransformGroup(); group.Children.Add(base.GetDesiredTransform(transform)); group.Children.Add(new TranslateTransform(_position.X, _position.Y)); return group;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (_following) { CompositionTarget.Rendering -= OnRendering; _following = false; }
        _card.BeginAnimation(OpacityProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _layer.Remove(this);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
}
