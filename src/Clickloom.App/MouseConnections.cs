using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

public sealed class MouseConnections : FrameworkElement
{
    private MouseIllustration? _mouse;
    private (MouseControl Input, Button Label)[] _labels = [];
    public MouseControl? Selected { get; set; }
    internal void Configure(MouseIllustration mouse, params (MouseControl, Button)[] labels)
    { _mouse = mouse; _labels = labels; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        if (_mouse is null || _mouse.ActualWidth <= 0 || _mouse.ActualHeight <= 0) return;
        foreach (var (input, label) in _labels)
        {
            if (label.ActualWidth <= 0) continue;
            Point end = _mouse.TranslatePoint(_mouse.Anchor(input), this);
            Point middle = label.TranslatePoint(new Point(label.ActualWidth / 2, label.ActualHeight / 2), this);
            bool left = middle.X < end.X;
            Point start = label.TranslatePoint(new Point(left ? label.ActualWidth : 0, label.ActualHeight / 2), this);
            Brush color = new SolidColorBrush(Selected == input ? Color.FromRgb(116, 82, 203) : Color.FromRgb(190, 178, 210));
            var line = new StreamGeometry();
            using (var path = line.Open())
            {
                path.BeginFigure(start, false, false);
                path.LineTo(new Point(start.X + (end.X - start.X) * .65, start.Y), true, false);
                path.LineTo(end, true, false);
            }
            drawing.DrawGeometry(null, new Pen(color, 1.1), line);
            drawing.DrawEllipse(color, null, end, Selected == input ? 3.7 : 3, Selected == input ? 3.7 : 3);
        }
    }
}
