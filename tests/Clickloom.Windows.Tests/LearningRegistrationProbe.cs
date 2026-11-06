using System.Runtime.InteropServices;
using Clickloom.Windows;

internal static class LearningRegistrationProbe
{
    internal static int Run(string name)
    {
        var connection = MouseConnectionCatalog.Enumerate().FirstOrDefault(c => !c.IsAuxiliary && c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (connection is null) throw new InvalidOperationException("Requested mouse is not connected.");
        var endpoints = HidInventory.Enumerate().Where(e => e.PhysicalKey.Equals(connection.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
        RawMouseActivity? activity = null; int arrivals = 0, removals = 0;
        Procedure procedure = (window, message, first, second) =>
        {
            if (message == 0xFF) activity?.Observe(second);
            if (message == 0xFE) { if (first == 1) arrivals++; else if (first == 2) removals++; activity?.DeviceChanged(first, second); }
            return DefWindowProc(window, message, first, second);
        };
        string className = "Moushift.LearningProbe." + Guid.NewGuid().ToString("N");
        nint module = GetModuleHandle(null);
        var type = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(procedure), Instance = module, Name = className };
        if (RegisterClassEx(ref type) == 0) throw new InvalidOperationException("Could not create the message-only test class.");
        nint handle = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, -3, 0, module, 0);
        if (handle == 0) { UnregisterClass(className, module); throw new InvalidOperationException("Could not create the message-only test window."); }
        try
        {
            using (activity = new RawMouseActivity(handle))
            using (var capture = activity.BeginCapture(handle, connection, endpoints, _ => { }, message => Console.WriteLine(message)))
            {
                // Reproduce an arrival delivered immediately after Listen without injecting mouse input.
                PostMessage(handle, 0xFE, 1, 0);
                long until = Environment.TickCount64 + 1500;
                while (Environment.TickCount64 < until)
                {
                    while (PeekMessage(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                    Thread.Sleep(10);
                }
                if (capture.IsDisposed) throw new InvalidOperationException("Capture stopped after registration: " + capture.StopReason);
                Console.WriteLine($"PASS native learning registration: {connection.Name}; listener remained active; {capture.RegisteredCollections} collections; {arrivals} arrivals, {removals} removals. Hidden message-only window; no input injected or device settings changed.");
            }
        }
        finally { DestroyWindow(handle); UnregisterClass(className, module); GC.KeepAlive(procedure); }
        return 0;
    }
    private delegate nint Procedure(nint window, uint message, nint first, nint second);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    { public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu, Name; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint First; public nint Second; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extended, string type, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint first, nint second);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint first, nint second);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
}
