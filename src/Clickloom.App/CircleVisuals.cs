using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Clickloom.Core;

namespace Clickloom.App;

internal static class CircleVisuals
{
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static Brush LabelSurface(CircleColors palette) => SystemParameters.HighContrast ? SystemColors.WindowBrush
        : CircleColors.Parse(palette.Background) is { } color ? new SolidColorBrush(color) : ThemeManager.Brush("CircleLabelSurface");
    public static Brush LabelInk(CircleColors palette) => SystemParameters.HighContrast ? SystemColors.WindowTextBrush
        : CircleColors.Parse(palette.Text) is { } color ? new SolidColorBrush(color)
        : CircleColors.Parse(palette.Background) is { } background
            ? (Contrast(background, Colors.White) >= Contrast(background, Colors.Black) ? Brushes.White : Brushes.Black)
            : ThemeManager.Brush("Ink");
    public static Brush GlyphInk(CircleColors palette, bool empty) => empty && !SystemParameters.HighContrast
        && palette is { Background: null, Text: null } ? ThemeManager.Brush("Muted") : LabelInk(palette);
    public const string UndoIcon = "\uE7A7";
    public const string RedoIcon = "\uE7A6";
    public const string BackIcon = "\uE72B";
    private static readonly Geometry UndoArrow = Geometry.Parse("M 9,3 L 4,8 L 9,13 M 4,8 H 15 A 5,5 0 0 1 15,18 H 10");

    public static Brush Glass(CircleColors palette)
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowBrush;
        var custom = CircleColors.Parse(palette.Background);
        return new RadialGradientBrush
        {
            Center = new(.38, .18), GradientOrigin = new(.33, .06), RadiusX = .88, RadiusY = .97,
            GradientStops = custom is { } color
                ? new() { new(Tint(color, 255, .16, 238), 0), new(color, .58), new(Tint(color, 0, .12, 246), 1) }
                : new() { GlassStop("CircleGlassTop", 0), GlassStop("CircleGlassMid", .58), GlassStop("CircleGlassBottom", 1) }
        };
    }

    private static Color Tint(Color color, byte target, double amount, byte alpha)
    {
        byte Blend(byte channel) => (byte)Math.Round(channel + (target - channel) * amount);
        return Color.FromArgb(alpha, Blend(color.R), Blend(color.G), Blend(color.B));
    }
    private static double Luminance(Color color) => .2126 * color.ScR + .7152 * color.ScG + .0722 * color.ScB;
    public static double Contrast(Color a, Color b)
    {
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    public static Effect? GlassInkShadow(CircleColors palette)
    {
        if (SystemParameters.HighContrast || palette is { Background: not null } or { Text: not null }) return null;
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

    public static string[] DefaultFolderColors() =>
        new[] { "CircleFolderLight", "CircleFolderShade" }.Select(name => CircleColors.Format(((SolidColorBrush)ThemeManager.Brush(name)).Color)).ToArray();

    public static FrameworkElement WithOutline(RingSlot slot, FrameworkElement content, bool emphasized, CircleColors palette)
    {
        if (!(slot.Outline?.Enabled ?? (slot.Kind == RingSlotKind.Folder))) return content;
        var ring = new Ellipse
        {
            StrokeThickness = slot.Outline?.Thickness ?? 3.5, Margin = new(1), IsHitTestVisible = false,
            RenderTransformOrigin = new(.5, .5)
        };
        var colors = slot.Outline?.Colors.ToArray() ?? palette.Folder;
        void StyleRing()
        {
            AngularOutline.Stop(ring);
            if (SystemParameters.HighContrast)
            {
                ring.Effect = null;
                ring.Stroke = SystemColors.HighlightBrush;
                return;
            }
            ring.Effect ??= new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 4, Opacity = .72 };
            AngularOutline.Apply(ring, colors is { Length: > 0 } ? colors : DefaultFolderColors(), emphasized && ring.IsLoaded && SystemParameters.ClientAreaAnimation);
        }
        StyleRing();
        ring.Loaded += (_, _) =>
        {
            StyleRing();
            ThemeManager.Changed += StyleRing;
        };
        ring.Unloaded += (_, _) =>
        {
            ThemeManager.Changed -= StyleRing;
            AngularOutline.Stop(ring);
        };
        return new Grid { Children = { content, ring } };
    }
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
    public static bool Empty(RingSlot slot) => slot.Kind == RingSlotKind.Command && slot.Sequence is null
        && slot.Action.Kind is ActionKind.Disabled or ActionKind.Passthrough;
}
