using Clickloom.Core;

namespace Clickloom.Windows;

internal static class OwnWindowPolicy
{
    public static bool ShouldBypass(bool ownWindow, MouseControl input, InputAction? action, bool capturingShortcut)
    {
        if (!ownWindow) return false;
        if (capturingShortcut || input is not (MouseControl.XButton1 or MouseControl.XButton2)) return true;
        // Otherwise an Alt+Tab that returns to Clickloom changes the next press
        // into the original mouse action. Allow window switching on side buttons
        // while keeping settings controls and the shortcut field usable.
        return action is not { Kind: ActionKind.Shortcut, KeyCode: 9,
            Modifiers: KeyModifiers.Alt or (KeyModifiers.Alt | KeyModifiers.Shift) };
    }
}
