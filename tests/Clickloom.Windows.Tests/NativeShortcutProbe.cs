using System.Reflection;
using System.Runtime.InteropServices;
using Clickloom.Core;
using Clickloom.Windows;

internal static class NativeShortcutProbe
{
    // Native round trip of the production sender. Swallow only our tagged output,
    // so this check cannot switch or type into the user's foreground application.
    public static int Run()
    {
        var received = new List<(uint Key, bool Up)>();
        NativeMethods.HookProc callback = (code, message, pointer) =>
        {
            if (code >= 0)
            {
                var key = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(pointer);
                if (key.ExtraInfo == NativeMethods.InjectionMarker)
                {
                    received.Add((key.VirtualKey, (key.Flags & 0x80) != 0));
                    return 1;
                }
            }
            return NativeMethods.CallNextHookEx(0, code, message, pointer);
        };
        NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
        uint thread = NativeMethods.GetCurrentThreadId();
        nint hook = NativeMethods.SetWindowsHookEx(13, callback, NativeMethods.GetModuleHandle(null), 0);
        if (hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Exception? error = null;
        var sender = new Thread(() =>
        {
            try
            {
                Thread.Sleep(120);
                using var service = new MouseRemappingService();
                typeof(MouseRemappingService).GetMethod("SendShortcut", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(service, [new InputAction { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt }]);
                Thread.Sleep(200);
            }
            catch (Exception failure) { error = failure; }
            finally { NativeMethods.PostThreadMessage(thread, NativeMethods.WmQuit, 0, 0); }
        }) { IsBackground = true };
        using var timeout = new Timer(_ => NativeMethods.PostThreadMessage(thread, NativeMethods.WmQuit, 0, 0), null, 5000, Timeout.Infinite);
        try
        {
            sender.Start();
            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            { NativeMethods.TranslateMessage(ref message); NativeMethods.DispatchMessage(ref message); }
        }
        finally { NativeMethods.UnhookWindowsHookEx(hook); GC.KeepAlive(callback); }
        if (!sender.Join(1000)) throw new TimeoutException("Native sender did not finish.");
        if (error is not null) throw error;
        if (received.Count != 4 || received[0].Key is not (0x12 or 0xA4) || received[0].Up
            || received[1] != (9u, false) || received[2] != (9u, true) || !received[3].Up || received[3].Key != received[0].Key)
            throw new InvalidOperationException("Alt+Tab native key edges did not match: " + string.Join(", ", received));
        Console.WriteLine("PASS native Alt+Tab output: production SendInput emitted Alt DOWN, Tab DOWN/UP, Alt UP; observer consumed tagged output before OS shortcuts. Physical mouse and target switch not tested.");
        return 0;
    }
}
