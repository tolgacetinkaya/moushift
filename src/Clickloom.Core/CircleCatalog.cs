namespace Clickloom.Core;

public sealed record CircleActionDefinition(string Id, string Provider, string Category, string Title, string Icon,
    RingSlot Template, bool NeedsConfiguration = false)
{
    public RingSlot Create()
    {
        var slot = Template.DeepClone(); slot.DefinitionId = Id; slot.Name = Title; slot.Icon = Icon; return slot;
    }
}

/// <summary>Original, executable Windows actions; no vendor catalog or private IPC dependency.</summary>
public static class CircleCatalog
{
    public static IReadOnlyList<CircleActionDefinition> Actions { get; } = Build();
    private static List<CircleActionDefinition> Build()
    {
        var result = new List<CircleActionDefinition>();
        void Key(string id, string category, string title, string icon, int key, KeyModifiers mods = KeyModifiers.None) =>
            result.Add(new(id, "System Actions", category, title, icon, new() { Action = new() { Kind = ActionKind.Shortcut, KeyCode = key, Modifiers = mods } }));
        void Resource(string id, string title, string icon, string path) => result.Add(new(id, "System Actions", "Open", title, icon,
            new() { Action = new() { Kind = ActionKind.OpenResource, ApplicationPath = path } }));
        Key("media.play", "Media & Volume", "Play / pause", "\uE768", 0xB3);
        Key("media.next", "Media & Volume", "Next track", "\uE893", 0xB0);
        Key("media.previous", "Media & Volume", "Previous track", "\uE892", 0xB1);
        Key("media.stop", "Media & Volume", "Stop playback", "\uE71A", 0xB2);
        Key("volume.mute", "Media & Volume", "Toggle mute", "\uE74F", 0xAD);
        Key("volume.up", "Media & Volume", "Volume up", "\uE767", 0xAF);
        Key("volume.down", "Media & Volume", "Volume down", "\uE993", 0xAE);
        result.Add(new("volume.adjust", "System Actions", "Media & Volume", "Volume", "\uE767", new() { Kind = RingSlotKind.Adjustment,
            IncreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAF }, DecreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAE } }));
        Key("keyboard.copy", "Keyboard", "Copy", "\uE8C8", 0x43, KeyModifiers.Control);
        Key("keyboard.paste", "Keyboard", "Paste", "\uE77F", 0x56, KeyModifiers.Control);
        Key("keyboard.cut", "Keyboard", "Cut", "\uE8C6", 0x58, KeyModifiers.Control);
        Key("keyboard.undo", "Keyboard", "Undo", "\uE7A7", 0x5A, KeyModifiers.Control);
        Key("keyboard.redo", "Keyboard", "Redo", "\uE7A6", 0x59, KeyModifiers.Control);
        Key("keyboard.select", "Keyboard", "Select all", "\uE8B3", 0x41, KeyModifiers.Control);
        Key("keyboard.save", "Keyboard", "Save", "\uE74E", 0x53, KeyModifiers.Control);
        Key("keyboard.find", "Keyboard", "Find", "\uE721", 0x46, KeyModifiers.Control);
        Key("keyboard.emoji", "Keyboard", "Emoji picker", "\uE899", 0xBE, KeyModifiers.Windows);
        Key("navigation.switch", "Navigation", "Switch windows", "\uE8C4", 0x09, KeyModifiers.Alt);
        Key("navigation.tasks", "Navigation", "Task View", "\uE7F4", 0x09, KeyModifiers.Windows);
        Key("navigation.desktop", "Navigation", "Show desktop", "\uE977", 0x44, KeyModifiers.Windows);
        Key("navigation.newDesktop", "Navigation", "Add desktop", "\uE710", 0x44, KeyModifiers.Windows | KeyModifiers.Control);
        Key("navigation.nextDesktop", "Navigation", "Next desktop", "\uE76C", 0x27, KeyModifiers.Windows | KeyModifiers.Control);
        Key("navigation.previousDesktop", "Navigation", "Previous desktop", "\uE76B", 0x25, KeyModifiers.Windows | KeyModifiers.Control);
        Key("navigation.closeDesktop", "Navigation", "Close desktop", "\uE711", 0x73, KeyModifiers.Windows | KeyModifiers.Control);
        Key("navigation.back", "Navigation", "Back", "\uE72B", 0x25, KeyModifiers.Alt);
        Key("navigation.forward", "Navigation", "Forward", "\uE72A", 0x27, KeyModifiers.Alt);
        Key("utility.screenshot", "Utilities", "Screenshot", "\uE722", 0x53, KeyModifiers.Windows | KeyModifiers.Shift);
        Key("utility.search", "Utilities", "Windows Search", "\uE721", 0x53, KeyModifiers.Windows);
        Key("utility.magnifier", "Utilities", "Magnifier", "\uE71E", 0xBB, KeyModifiers.Windows);
        Key("utility.clipboard", "Utilities", "Clipboard history", "\uE77F", 0x56, KeyModifiers.Windows);
        Key("system.lock", "System", "Lock workstation", "\uE72E", 0x4C, KeyModifiers.Windows);
        Key("system.snapLeft", "System", "Snap window left", "\uE8A0", 0x25, KeyModifiers.Windows);
        Key("system.snapRight", "System", "Snap window right", "\uE8A1", 0x27, KeyModifiers.Windows);
        Key("system.maximize", "System", "Maximize window", "\uE922", 0x26, KeyModifiers.Windows);
        Key("system.minimize", "System", "Minimize window", "\uE921", 0x28, KeyModifiers.Windows);
        Key("open.explorer", "Open", "File Explorer", "\uE8B7", 0x45, KeyModifiers.Windows);
        Resource("open.settings", "Windows Settings", "\uE713", "ms-settings:");
        Resource("open.sound", "Sound settings", "\uE767", "ms-settings:sound");
        Resource("open.display", "Display settings", "\uE7F4", "ms-settings:display");
        result.Add(new("open.application", "System Actions", "Open", "Open application", "\uE8A7", new() { Action = new() { Kind = ActionKind.LaunchApplication } }, true));
        foreach (var (id, title, icon) in new[] { ("open.url", "Web page", "\uE774"), ("open.file", "Open file", "\uE8A5"), ("open.directory", "Open folder", "\uE8B7") })
            result.Add(new(id, "System Actions", "Open", title, icon, new() { Action = new() { Kind = ActionKind.OpenResource } }, true));
        result.Add(new("keyboard.shortcut", "System Actions", "Keyboard", "Keyboard shortcut", "\uE765", new() { Action = new() { Kind = ActionKind.Shortcut } }, true));
        result.Add(new("keyboard.text", "System Actions", "Keyboard", "Type text", "\uE8D2", new() { Action = new() { Kind = ActionKind.Text } }, true));
        foreach (var control in new[] { MouseControl.LeftButton, MouseControl.MiddleButton, MouseControl.RightButton, MouseControl.XButton1, MouseControl.XButton2 })
            result.Add(new("mouse." + control, "System Actions", "Mouse", control switch { MouseControl.XButton1 => "Back button", MouseControl.XButton2 => "Forward button", _ => control.ToString().Replace("Button", " click") }, "\uE962",
                new() { Action = new() { Kind = ActionKind.MouseButton, MouseOutput = control } }));
        result.Add(new("zoom.adjust", "System Actions", "Utilities", "Zoom", "\uE71E", new() { Kind = RingSlotKind.Adjustment,
            IncreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xBB, Modifiers = KeyModifiers.Control },
            DecreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xBD, Modifiers = KeyModifiers.Control } }));
        result.Add(new("circle.folder", "Circle Actions", "Folders", "Folder", "\uE8B7", new() { Kind = RingSlotKind.Folder }, true));
        result.Add(new("circle.profile", "Circle Actions", "Device navigation", "Switch Circle profile", "\uE8F1", new() { Action = new() { Kind = ActionKind.SelectCircleProfile } }, true));
        result.Add(new("circle.adapt", "Circle Actions", "Device navigation", "Toggle Adapt to app", "\uE8AB", new() { Action = new() { Kind = ActionKind.ToggleCircleAdapt } }));
        return result;
    }
}
