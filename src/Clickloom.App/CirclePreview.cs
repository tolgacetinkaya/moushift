using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

/// <summary>A non-executing view of the selected profile's real Circle, using native Fluent icons.</summary>
public sealed class CirclePreview : Canvas
{
    public CirclePreview() { Width = Height = 236; IsHitTestVisible = false; }

    internal void SetSlots(IReadOnlyList<RingSlot> slots)
    {
        Children.Clear();
        for (int i = 0; i < Math.Min(slots.Count, 8); i++)
        {
            double angle = (-90 + i * 45) * Math.PI / 180;
            var node = Node(CircleVisuals.Icon(slots[i]), i == 0, 46);
            SetLeft(node, 118 + Math.Cos(angle) * 91 - 23);
            SetTop(node, 118 + Math.Sin(angle) * 91 - 23);
            node.ToolTip = slots[i].Name;
            Children.Add(node);
        }
        var center = Node("\uE80A", false, 50);
        SetLeft(center, 93); SetTop(center, 93); Children.Add(center);
    }

    private static Border Node(string icon, bool selected, double size)
    {
        var glyph = new TextBlock { Text = icon, FontFamily = new("Segoe Fluent Icons"), FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Accent" : "Ink");
        var border = new Border { Width = size, Height = size, CornerRadius = new(size / 2), BorderThickness = new(1), Child = glyph };
        border.SetResourceReference(Border.BackgroundProperty, selected ? "RingSelection" : "PanelLight");
        border.SetResourceReference(Border.BorderBrushProperty, selected ? "Accent" : "Line");
        return border;
    }
}
