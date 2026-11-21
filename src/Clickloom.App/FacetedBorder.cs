using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Clickloom.App;

/// <summary>Uses Border layout with a softly rounded six-sided outline.</summary>
public sealed class FacetedBorder : Border
{
    protected override void OnRender(DrawingContext drawing)
    {
        double stroke = BorderBrush is null ? 0 : BorderThickness.Left;
        if (ActualWidth <= stroke || ActualHeight <= stroke) return;
        double inset = stroke / 2;
        double right = ActualWidth - inset;
        double bottom = ActualHeight - inset;
        double cut = Math.Min((right - inset) * .18, Math.Clamp((bottom - inset) * .26, 4, 18));
        double centerY = ActualHeight / 2;
        Point[] vertices =
        [
            new(inset + cut, inset), new(right - cut, inset), new(right, centerY),
            new(right - cut, bottom), new(inset + cut, bottom), new(inset, centerY)
        ];

        double rounding = Math.Clamp(CornerRadius.TopLeft, 3, 7);
        var outline = new StreamGeometry();
        using (var path = outline.Open())
        {
            path.BeginFigure(Toward(vertices[^1], vertices[0], rounding), isFilled: true, isClosed: true);
            for (int i = 0; i < vertices.Length; i++)
            {
                Point vertex = vertices[i];
                path.LineTo(Toward(vertex, vertices[(i + vertices.Length - 1) % vertices.Length], rounding), true, false);
                path.QuadraticBezierTo(vertex, Toward(vertex, vertices[(i + 1) % vertices.Length], rounding), true, false);
            }
        }
        outline.Freeze();
        drawing.DrawGeometry(Background, stroke > 0 ? new Pen(BorderBrush, stroke) : null, outline);
    }

    private static Point Toward(Point origin, Point destination, double amount)
    {
        Vector direction = destination - origin;
        return origin + direction * Math.Min(.32, amount / direction.Length);
    }
}
