using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

/// <summary>Editable spatial map using our own vector drawing.</summary>
public sealed class RingLayout : FrameworkElement
{
    public IReadOnlyList<RingSlot> Slots { get; set; } = [];
    public int SelectedIndex { get; set; }
    public event EventHandler<int>? Selected;
    public RingLayout()
    {
        Width = Height = 250; Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) =>
        {
            int hit = RingGeometry.Hit(e.GetPosition(this), new(125, 125), 33, 111, Slots.Count, 3.4);
            if (hit >= 0) { Selected?.Invoke(this, hit); e.Handled = true; }
        };
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        for (int i = 0; i < Slots.Count; i++)
        {
            bool selected = i == SelectedIndex;
            var shape = RingGeometry.Sector(new(125,125),33,111,i,3.4);
            dc.DrawGeometry(ThemeManager.Brush("RingIdle"), null, shape);
            dc.DrawGeometry(selected ? ThemeManager.Brush("RingSelection") : Brushes.Transparent, new Pen(ThemeManager.Brush(selected ? "Accent" : "Line"), selected ? 1.7 : .8), shape);
            var c = RingGeometry.Polar(new(125,125),75,-90+i*45);
            DrawText(dc, (i+1).ToString("00"),c,14,ThemeManager.Brush("Ink"));
        }
        dc.DrawEllipse(ThemeManager.Brush("Panel"),new Pen(ThemeManager.Brush("Line"),1),new(125,125),30,30);
        DrawText(dc,"CIRCLE",new(125,125),10,ThemeManager.Brush("Muted"));
    }
    private void DrawText(DrawingContext dc, string value, Point center, int size, Brush brush)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
