using System.Windows;
using System.Windows.Media;

namespace Clickloom.App;

internal static class RingGeometry
{
    public static Point Polar(Point center, double radius, double angle)
    { double a = angle * Math.PI / 180; return new(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)); }

    public static Geometry Sector(Point center, double inner, double outer, int index, double gap = 0)
    {
        double a = -112.5 + index * 45 + gap / 2, b = a + 45 - gap;
        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            c.BeginFigure(Polar(center, outer, a), true, true);
            c.ArcTo(Polar(center, outer, b), new(outer, outer), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(Polar(center, inner, b), true, false);
            c.ArcTo(Polar(center, inner, a), new(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        shape.Freeze(); return shape;
    }
    public static int Hit(Point point, Point center, double inner, double outer, int count, double gap = 0)
    {
        var delta = point - center;
        if (delta.Length < inner || delta.Length > outer) return -1;
        double angle = (Math.Atan2(delta.Y, delta.X) * 180 / Math.PI + 112.5 + 360) % 360;
        double within = angle % 45;
        if (within < gap / 2 || within > 45 - gap / 2) return -1;
        int index = (int)(angle / 45); return index < count ? index : -1;
    }
}
