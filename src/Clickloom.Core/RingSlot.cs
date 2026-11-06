namespace Clickloom.Core;

public enum RingSlotKind { Command, Folder, Adjustment }

public sealed class RingSlot
{
    public string DefinitionId { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Name { get; set; } = "Action";
    public RingSlotKind Kind { get; set; }
    public InputAction Action { get; set; } = new() { Kind = ActionKind.Disabled };
    public InputAction DecreaseAction { get; set; } = new() { Kind = ActionKind.Disabled };
    public InputAction IncreaseAction { get; set; } = new() { Kind = ActionKind.Disabled };
    public List<RingSlot> Children { get; set; } = [];
    public RingSlot DeepClone() => new()
    {
        Name = Name, Kind = Kind, DefinitionId = DefinitionId, Icon = Icon, Action = Action.DeepClone(),
        DecreaseAction = DecreaseAction.DeepClone(), IncreaseAction = IncreaseAction.DeepClone(),
        Children = Children.Select(child => child.DeepClone()).ToList()
    };
}

public static class RingNavigation
{
    public const int MaximumFolderItems = 9;
    public const int MaximumFolderDepth = 2;
    public static RingSlot? Resolve(IReadOnlyList<RingSlot> slots, IReadOnlyList<int> path)
    {
        if (path.Count is < 1 or > MaximumFolderDepth + 1) return null;
        RingSlot? slot = null;
        for (int depth = 0; depth < path.Count; depth++)
        {
            int index = path[depth];
            if (index < 0 || index >= slots.Count) return null;
            slot = slots[index];
            if (depth < path.Count - 1)
            { if (slot.Kind != RingSlotKind.Folder) return null; slots = slot.Children; }
        }
        return slot;
    }
    public static List<RingSlot> EmptyFolder() => Enumerable.Range(1, 8)
        .Select(index => new RingSlot { Name = "Action " + index }).ToList();
}

public static class RingDefaults
{
    public static List<RingSlot> Create() =>
    [
        Shortcut("Copy", 0x43, KeyModifiers.Control), Shortcut("Paste", 0x56, KeyModifiers.Control),
        Shortcut("Undo", 0x5A, KeyModifiers.Control), Shortcut("Redo", 0x59, KeyModifiers.Control),
        Shortcut("Switch windows", 0x09, KeyModifiers.Alt), Shortcut("Task View", 0x09, KeyModifiers.Windows),
        Shortcut("Play / pause", 0xB3, KeyModifiers.None), Shortcut("Mute", 0xAD, KeyModifiers.None)
    ];
    private static RingSlot Shortcut(string name, int key, KeyModifiers modifiers) => new()
    { Name = name, Action = new() { Kind = ActionKind.Shortcut, KeyCode = key, Modifiers = modifiers } };
}
