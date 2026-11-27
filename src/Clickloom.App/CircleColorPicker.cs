using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Clickloom.App;

internal sealed class CircleColorPicker : StackPanel
{
    private readonly ColorSurface _field;
    private readonly ColorSurface _strip;
    private readonly Action<Color> _changed;
    private Color _color;
    private double _hue, _saturation, _value;

    internal CircleColorPicker(Color initial, Action<Color> changed)
    {
        _changed = changed;
        _field = new ColorSurface(this, false) { Height = 124 };
        _strip = new ColorSurface(this, true) { Height = 28, Margin = new(0, 4, 0, 6) };
        Children.Add(_field);
        Children.Add(_strip);
        SetColor(initial);
    }

    internal void SetColor(Color color)
    {
        if (_color == color) return;
        _color = color;
        double red = color.R / 255d, green = color.G / 255d, blue = color.B / 255d;
        _value = Math.Max(red, Math.Max(green, blue));
        double delta = _value - Math.Min(red, Math.Min(green, blue));
        _saturation = _value == 0 ? 0 : delta / _value;
        if (delta > 0)
        {
            double sector = _value == red ? (green - blue) / delta :
                _value == green ? 2 + (blue - red) / delta : 4 + (red - green) / delta;
            _hue = (sector * 60 + 360) % 360;
        }
        RefreshSurfaces();
    }

    private static Color ToColor(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double secondary = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        (double red, double green, double blue) = ((int)(hue / 60) % 6) switch
        {
            0 => (chroma, secondary, 0d),
            1 => (secondary, chroma, 0d),
            2 => (0d, chroma, secondary),
            3 => (0d, secondary, chroma),
            4 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        byte Channel(double c) => (byte)Math.Round((c + value - chroma) * 255);
        return Color.FromRgb(Channel(red), Channel(green), Channel(blue));
    }

    private void Select(double hue, double saturation, double value)
    {
        _hue = Math.Clamp(hue, 0, 360);
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        _color = ToColor(_hue, _saturation, _value);
        RefreshSurfaces();
        _changed(_color);
    }

    private void RefreshSurfaces()
    {
        AutomationProperties.SetItemStatus(_field, $"Saturation {_saturation:P0}, brightness {_value:P0}");
        AutomationProperties.SetItemStatus(_strip, $"Hue {_hue:F0} degrees");
        _field.InvalidateVisual();
        _strip.InvalidateVisual();
    }

    private sealed class ColorSurface : FrameworkElement
    {
        private static readonly Pen DarkEdge = new(Brushes.Black, 3);
        private static readonly Pen LightEdge = new(Brushes.White, 1.5);
        private static readonly LinearGradientBrush HueBrush = CreateHueBrush();
        private static readonly LinearGradientBrush SaturationBrush = new(Colors.White, Color.FromArgb(0, 255, 255, 255), 0);
        private static readonly LinearGradientBrush ValueBrush = new(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
        private readonly CircleColorPicker _picker;
        private readonly bool _hueStrip;

        internal ColorSurface(CircleColorPicker picker, bool hueStrip)
        {
            _picker = picker;
            _hueStrip = hueStrip;
            Focusable = true;
            Cursor = Cursors.Cross;
            string name = hueStrip ? "Hue" : "Saturation and brightness";
            ToolTip = hueStrip ? "Choose a hue. Use the left and right arrows for fine adjustments."
                : "Drag to choose a color. Arrow keys adjust saturation and brightness.";
            AutomationProperties.SetName(this, name);
            AutomationProperties.SetHelpText(this, (string)ToolTip);
        }

        private static LinearGradientBrush CreateHueBrush()
        {
            var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
            for (int i = 0; i <= 6; i++) brush.GradientStops.Add(new(ToColor(i * 60, 1, 1), i / 6d));
            brush.Freeze();
            return brush;
        }

        private Rect Surface => new(6, 5, Math.Max(1, ActualWidth - 12), Math.Max(1, ActualHeight - 10));

        protected override void OnRender(DrawingContext dc)
        {
            var area = Surface;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            dc.DrawRoundedRectangle(_hueStrip ? HueBrush : new SolidColorBrush(ToColor(_picker._hue, 1, 1)), null, area, 4, 4);
            if (!_hueStrip)
            {
                dc.DrawRoundedRectangle(SaturationBrush, null, area, 4, 4);
                dc.DrawRoundedRectangle(ValueBrush, null, area, 4, 4);
            }
            if (IsKeyboardFocused)
                dc.DrawRoundedRectangle(null, new Pen(ThemeManager.Brush("Accent"), 2), area, 4, 4);

            var marker = new Point(area.X + area.Width * (_hueStrip ? _picker._hue / 360 : _picker._saturation),
                area.Y + area.Height * (_hueStrip ? 0.5 : 1 - _picker._value));
            dc.DrawEllipse(null, DarkEdge, marker, 5, 5);
            dc.DrawEllipse(null, LightEdge, marker, 5, 5);
        }

        private void Choose(Point point)
        {
            var area = Surface;
            double x = (point.X - area.X) / area.Width;
            if (_hueStrip) _picker.Select(x * 360, _picker._saturation, _picker._value);
            else _picker.Select(_picker._hue, x, 1 - (point.Y - area.Y) / area.Height);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            CaptureMouse();
            Choose(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
                Choose(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            Choose(e.GetPosition(this));
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            int horizontal = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            int vertical = e.Key == Key.Down ? -step : e.Key == Key.Up ? step : 0;
            if (horizontal == 0 && (_hueStrip || vertical == 0)) { base.OnKeyDown(e); return; }
            if (_hueStrip) _picker.Select(_picker._hue + horizontal, _picker._saturation, _picker._value);
            else _picker.Select(_picker._hue, _picker._saturation + horizontal / 100d, _picker._value + vertical / 100d);
            e.Handled = true;
        }

        protected override void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusedChanged(e);
            InvalidateVisual();
        }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
            => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);
    }
}
