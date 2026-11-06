using Clickloom.Core;
using System.Windows.Input;

namespace Clickloom.App;

internal sealed record MouseInputLabel(MouseControl Control, string Name, string Position)
{ public override string ToString() => Name; }

internal static class InputPresentation
{
    public static readonly MouseInputLabel[] MouseInputs =
    [
        new(MouseControl.LeftButton, "Left button", "Primary left button"),
        new(MouseControl.RightButton, "Right button", "Primary right button"),
        new(MouseControl.MiddleButton, "Wheel button", "Press the wheel"),
        new(MouseControl.XButton1, "Back button", "Rear side button"),
        new(MouseControl.XButton2, "Forward button", "Front side button"),
        new(MouseControl.WheelUp, "Scroll up", "Wheel up"),
        new(MouseControl.WheelDown, "Scroll down", "Wheel down"),
        new(MouseControl.WheelLeft, "Scroll left", "Wheel left"),
        new(MouseControl.WheelRight, "Scroll right", "Wheel right")
    ];
    public static string Name(MouseControl control) => MouseInputs.Single(p => p.Control == control).Name;
    public static string Position(MouseControl control) => MouseInputs.Single(p => p.Control == control).Position;
    public static string Action(InputAction action) => action.Kind switch
    {
        ActionKind.Passthrough => "Default",
        ActionKind.Disabled => "No action",
        ActionKind.Shortcut => Shortcut(action.KeyCode, action.Modifiers),
        ActionKind.MouseButton => Name(action.MouseOutput),
        ActionKind.Text => "Type text",
        ActionKind.LaunchApplication => System.IO.Path.GetFileNameWithoutExtension(action.ApplicationPath),
        ActionKind.Ring => "Click Circle",
        ActionKind.OpenResource => action.ApplicationPath,
        ActionKind.SelectCircleProfile => "Switch Circle profile",
        ActionKind.ToggleCircleAdapt => "Toggle Adapt to app",
        _ => "Default"
    };
    public static string Shortcut(int code, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        parts.Add(code == 0 ? (parts.Count > 0 ? "…" : "Press keys…") : KeyName(code));
        return string.Join(" + ", parts);
    }
    public static string KeyName(int code) => code switch
    {
        8 => "Backspace", 9 => "Tab", 13 => "Enter", 27 => "Esc", 32 => "Space",
        33 => "Page Up", 34 => "Page Down", 35 => "End", 36 => "Home",
        37 => "Left arrow", 38 => "Up arrow", 39 => "Right arrow", 40 => "Down arrow", 45 => "Insert", 46 => "Delete",
        0x14 => "Caps Lock", 0x13 => "Pause", 0x2C => "Print Screen", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        0xB3 => "Play / pause", 0xAD => "Mute", 0xAE => "Volume down", 0xAF => "Volume up",
        0xB0 => "Next track", 0xB1 => "Previous track", 0xA6 => "Browser back", 0xA7 => "Browser forward",
        >= 0x60 and <= 0x69 => "Numpad " + (code - 0x60),
        _ => KeyInterop.KeyFromVirtualKey(code).ToString()
    };
}
