using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Clickloom.Windows;

internal static class ShortcutTraceProbe
{
    public static int Run()
    {
        var rows = new ConcurrentQueue<object>();
        var time = Stopwatch.StartNew();
        NativeMethods.HookProc keyboard = (code, message, pointer) =>
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(pointer);
                // Explicit whitelist: no letters, numbers, text, clipboard or positions.
                if (data.VirtualKey is 9 or 13 or 0x12 or 0xA4 or 0xA5)
                    rows.Enqueue(new { Ms = time.ElapsedMilliseconds, Kind = "keyboard", Message = (uint)message,
                        Key = data.VirtualKey, data.ScanCode, data.Flags, Own = data.ExtraInfo == NativeMethods.InjectionMarker,
                        Window = NativeMethods.GetForegroundWindow().ToInt64() });
            }
            return NativeMethods.CallNextHookEx(0, code, message, pointer);
        };
        NativeMethods.HookProc mouse = (code, message, pointer) =>
        {
            if (code >= 0 && (uint)message is 0x20B or 0x20C)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(pointer);
                rows.Enqueue(new { Ms = time.ElapsedMilliseconds, Kind = "mouse", Message = (uint)message,
                    Button = data.MouseData >> 16, data.Flags, Own = data.ExtraInfo == NativeMethods.InjectionMarker,
                    Window = NativeMethods.GetForegroundWindow().ToInt64() });
            }
            return NativeMethods.CallNextHookEx(0, code, message, pointer);
        };
        NativeMethods.PeekMessage(out _, 0, 0, 0, 0);
        uint thread = NativeMethods.GetCurrentThreadId();
        nint keyHook = NativeMethods.SetWindowsHookEx(13, keyboard, NativeMethods.GetModuleHandle(null), 0);
        if (keyHook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        nint mouseHook = NativeMethods.SetWindowsHookEx(14, mouse, NativeMethods.GetModuleHandle(null), 0);
        try
        {
            if (mouseHook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Console.WriteLine("READY: passive 40s trace, side buttons and Alt/Tab/Enter only; nothing suppressed or injected.");
            using var timeout = new Timer(_ => NativeMethods.PostThreadMessage(thread, NativeMethods.WmQuit, 0, 0), null, 40000, Timeout.Infinite);
            while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg); NativeMethods.DispatchMessage(ref msg);
                while (rows.TryDequeue(out var row)) Console.WriteLine(JsonSerializer.Serialize(row));
            }
            while (rows.TryDequeue(out var row)) Console.WriteLine(JsonSerializer.Serialize(row));
        }
        finally
        {
            NativeMethods.UnhookWindowsHookEx(keyHook);
            if (mouseHook != 0) NativeMethods.UnhookWindowsHookEx(mouseHook);
            GC.KeepAlive(keyboard); GC.KeepAlive(mouse);
        }
        return 0;
    }
}
