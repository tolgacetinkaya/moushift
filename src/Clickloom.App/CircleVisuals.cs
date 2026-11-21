using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Clickloom.Core;

namespace Clickloom.App;

internal static class CircleVisuals
{
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public const string UndoIcon = "\uE7A7";
    public const string RedoIcon = "\uE7A6";
    public const string BackIcon = "\uE72B";
    private static readonly Geometry UndoArrow = Geometry.Parse("M 9,3 L 4,8 L 9,13 M 4,8 H 15 A 5,5 0 0 1 15,18 H 10");

    public static Brush Glass()
    {
        if (SystemParameters.HighContrast) return ThemeManager.Brush("RingIdle");
        return new RadialGradientBrush
        {
            Center = new(.38, .18), GradientOrigin = new(.33, .06), RadiusX = .88, RadiusY = .97,
            GradientStops = { GlassStop("CircleGlassTop", 0), GlassStop("CircleGlassMid", .58), GlassStop("CircleGlassBottom", 1) }
        };
    }

    public static Effect? GlassInkShadow()
    {
        if (SystemParameters.HighContrast) return null;
        var shadow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 4, Opacity = .9 };
        BindingOperations.SetBinding(shadow, DropShadowEffect.ColorProperty, BrushColor("CircleInkHalo"));
        return shadow;
    }

    private static GradientStop GlassStop(string brush, double offset)
    {
        var stop = new GradientStop { Offset = offset };
        BindingOperations.SetBinding(stop, GradientStop.ColorProperty, BrushColor(brush));
        return stop;
    }

    private static Binding BrushColor(string name) => new(nameof(SolidColorBrush.Color)) { Source = ThemeManager.Brush(name), Mode = BindingMode.OneWay };

    public static FrameworkElement IconVisual(string icon, double size, Brush foreground)
    {
        if (icon is UndoIcon or RedoIcon)
        {
            return new System.Windows.Shapes.Path
            {
                Data = UndoArrow, Stroke = foreground, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = size, Height = size, Stretch = Stretch.Uniform,
                RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = icon == RedoIcon ? new ScaleTransform(-1, 1) : Transform.Identity
            };
        }
        return new TextBlock { Text = icon, FontFamily = IconFont, FontSize = size, Foreground = foreground };
    }
    public static Point Root(Point center, int index, double radius = 82) => RingGeometry.Polar(center, radius, -90 + index * 45);
    public static Point Child(Point center, int parent, int index, int count, double radius = 174)
        => RingGeometry.Polar(center, radius, -90 + parent * 45 + (index - (count - 1) / 2.0) * 22.5);
    public static string Icon(RingSlot slot)
    {
        if (!string.IsNullOrEmpty(slot.Icon)) return slot.Icon;
        if (slot.Kind == RingSlotKind.Folder) return "\uE8B7";
        if (slot.Kind == RingSlotKind.Adjustment) return "\uE767";
        if (slot.Action is { Kind: ActionKind.Shortcut, KeyCode: 0x09, Modifiers: KeyModifiers.Windows }) return "\uE7C4";
        return slot.Action.Kind switch
        {
            ActionKind.Text => "\uE8D2", ActionKind.LaunchApplication => "\uE8A7", ActionKind.OpenResource => "\uE8B7",
            ActionKind.MouseButton => "\uE962", ActionKind.Disabled or ActionKind.Passthrough => "\uE710",
            _ => slot.Action.KeyCode switch { 0x43 => "\uE8C8", 0x56 => "\uE77F", 0x5A => UndoIcon, 0x59 => RedoIcon, 0x09 => "\uE8C4", 0xB3 => "\uE768", 0xAD => "\uE74F", _ => "\uE765" }
        };
    }
    public static bool Empty(RingSlot slot) => slot.Kind == RingSlotKind.Command && slot.Action.Kind is ActionKind.Disabled or ActionKind.Passthrough;
}
