using System.Runtime.InteropServices;
using System.Text;
using Clickloom.Core;

namespace Clickloom.Windows;

/// <summary>Source-identified standard mouse events and throttled activity; no input suppression or keyboard capture.</summary>
public sealed class RawMouseActivity : IDisposable
{
    private readonly Dictionary<nint, string> _paths = [];
    private readonly Dictionary<nint, DateTime> _reported = [];
    private HidInputCapture? _capture;
    public event EventHandler<string>? Observed;
    public event EventHandler<RawMouseButtonEvent>? ButtonObserved;
    public RawMouseActivity(nint window)
    {
        var devices = new[] { new RawRegistration { UsagePage = 1, Usage = 2, Flags = 0x2100, Window = window } };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawRegistration>()))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    public void DeviceChanged() { _capture?.DeviceChanged(); _capture = null; _paths.Clear(); _reported.Clear(); }
    public HidInputCapture BeginCapture(nint window, MouseConnection connection, IReadOnlyList<HidEndpoint> endpoints, Action<ObservedControl> observed, Action<string> status)
    {
        _capture?.Dispose();
        return _capture = new HidInputCapture(window, connection, endpoints, observed, status);
    }
    public void Observe(nint input)
    {
        try { _capture?.Observe(input); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException)
        { _capture?.DeviceChanged(); _capture = null; }
        uint size = (uint)Marshal.SizeOf<RawHeader>();
        if (GetRawInputData(input, 0x10000005, out var header, ref size, size) == uint.MaxValue || header.Type != 0 || header.Device == 0) return;
        if (!_paths.TryGetValue(header.Device, out string? path))
        {
            uint length = 0;
            if (GetRawInputDeviceInfo(header.Device, 0x20000007, null, ref length) == uint.MaxValue || length == 0 || length > 65536) return;
            var buffer = new StringBuilder((int)length + 1);
            if (GetRawInputDeviceInfo(header.Device, 0x20000007, buffer, ref length) == uint.MaxValue) return;
            _paths[header.Device] = path = buffer.ToString();
        }
        uint packetSize = (uint)Marshal.SizeOf<RawMousePacket>();
        if (GetRawMousePacket(input, 0x10000003, out var packet, ref packetSize, size) != uint.MaxValue
            && packetSize >= Marshal.SizeOf<RawMousePacket>() && packet.Mouse.ExtraInfo != (uint)NativeMethods.InjectionMarker)
        {
            // Button events must precede activity throttling: a click immediately after movement is still a click.
            foreach (var transition in DecodeButtons(packet.Mouse.ButtonFlags))
                ButtonObserved?.Invoke(this, new(path, transition.Control, transition.Pressed));
        }
        DateTime now = DateTime.UtcNow;
        if (_reported.TryGetValue(header.Device, out var last) && now - last < TimeSpan.FromMilliseconds(500)) return;
        _reported[header.Device] = now;
        Observed?.Invoke(this, path);
    }
    internal static IReadOnlyList<(MouseControl Control, bool Pressed)> DecodeButtons(ushort flags)
    {
        if ((flags & 0x3FF) == 0) return [];
        var transitions = new List<(MouseControl, bool)>();
        for (int button = 0; button < 5; button++)
            for (int edge = 0; edge < 2; edge++)
                if ((flags & (1 << (button * 2 + edge))) != 0) transitions.Add(((MouseControl)button, edge == 0));
        return transitions;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawMousePayload
    {
        public ushort Flags, Padding, ButtonFlags, ButtonData;
        public uint RawButtons;
        public int X, Y;
        public uint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawMousePacket { public RawHeader Header; public RawMousePayload Mouse; }
    public void Dispose()
    {
        _capture?.Dispose(); _capture = null;
        RegisterRawInputDevices([new RawRegistration { UsagePage = 1, Usage = 2, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawRegistration>());
    }
    [StructLayout(LayoutKind.Sequential)] private struct RawRegistration { public ushort UsagePage, Usage; public uint Flags; public nint Window; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader { public uint Type, Size; public nint Device; public nuint Parameter; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawRegistration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, out RawHeader data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)] private static extern uint GetRawMousePacket(nint input, uint command, out RawMousePacket data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);
}
