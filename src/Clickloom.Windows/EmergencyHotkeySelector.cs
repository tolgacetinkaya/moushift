namespace Clickloom.Windows;

internal static class EmergencyHotkeySelector
{
    public static bool TryRegister(Func<uint, bool> register, out uint selectedKey)
    {
        ArgumentNullException.ThrowIfNull(register);
        foreach (var key in new uint[] { 0x79, 0x78, 0x77 }) // F10, F9, F8.
        {
            if (!register(key)) continue;
            selectedKey = key;
            return true;
        }
        selectedKey = 0;
        return false;
    }

    public static string DisplayName(uint selectedKey) => selectedKey switch
    {
        0x79 => "Ctrl+Alt+F10",
        0x78 => "Ctrl+Alt+F9",
        0x77 => "Ctrl+Alt+F8",
        _ => "Not registered"
    };
}
