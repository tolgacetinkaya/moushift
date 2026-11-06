using System.Windows;
using Clickloom.Core;

namespace Clickloom.App;

internal static class CircleVisuals
{
    public static Point Root(Point center, int index, double radius = 82) => RingGeometry.Polar(center, radius, -90 + index * 45);
    public static Point Child(Point center, int parent, int index, int count, double radius = 174)
        => RingGeometry.Polar(center, radius, -90 + parent * 45 + (index - (count - 1) / 2.0) * 22.5);
    public static string Icon(RingSlot slot)
    {
        if (!string.IsNullOrEmpty(slot.Icon)) return slot.Icon;
        if (slot.Kind == RingSlotKind.Folder) return "\uE8B7";
        if (slot.Kind == RingSlotKind.Adjustment) return "\uE767";
        return slot.Action.Kind switch
        {
            ActionKind.Text => "\uE8D2", ActionKind.LaunchApplication => "\uE8A7", ActionKind.OpenResource => "\uE8B7",
            ActionKind.MouseButton => "\uE962", ActionKind.Disabled or ActionKind.Passthrough => "\uE710",
            _ => slot.Action.KeyCode switch { 0x43 => "\uE8C8", 0x56 => "\uE77F", 0x5A => "\uE7A7", 0x59 => "\uE7A6", 0x09 => "\uE8C4", 0xB3 => "\uE768", 0xAD => "\uE74F", _ => "\uE765" }
        };
    }
    public static bool Empty(RingSlot slot) => slot.Kind == RingSlotKind.Command && slot.Action.Kind is ActionKind.Disabled or ActionKind.Passthrough;
}
