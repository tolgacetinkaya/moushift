using System.Runtime.InteropServices;
using System.Text;

namespace Clickloom.Windows;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14, WmQuit = 0x12, WmHotKey = 0x312;
    internal const uint PmNoRemove = 0, KeyUp = 2, Unicode = 4;
    internal static readonly nuint InjectionMarker = 0x434C4943;
    internal delegate nint HookProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHookData
    { public Point Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardHookData
    { public uint VirtualKey, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice
    { public nint Handle; public uint Type; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct RawDeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint Id;
        [FieldOffset(12)] public uint ButtonCount;
    }

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputDeviceList([Out] RawDevice[]? list, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetRawInputDeviceName(nint device, uint command, StringBuilder? name, ref uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] internal static extern uint GetRawInputDeviceInformation(nint device, uint command, ref RawDeviceInfo data, ref uint size);
}
