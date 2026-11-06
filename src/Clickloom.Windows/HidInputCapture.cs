using System.Runtime.InteropServices;
using System.Text;
using Clickloom.Core;

namespace Clickloom.Windows;

/// <summary>Temporary, selected-connection Raw Input listener. Never suppresses input, writes reports, or logs text.</summary>
public sealed class HidInputCapture : IDisposable
{
    private readonly MouseConnection _connection;
    private readonly Dictionary<string, HidEndpoint> _endpoints;
    private readonly HashSet<string> _allowed;
    private readonly Dictionary<nint, string?> _paths = [];
    private readonly Dictionary<nint, nint> _preparsed = [];
    private readonly Dictionary<string, HashSet<ushort>> _pressed = [];
    private readonly List<Registration> _extra = [];
    private readonly Action<ObservedControl> _observed;
    private readonly Action<string> _status;
    private readonly HashSet<string> _reported = [];
    private bool _disposed;
    public int RegisteredCollections => _extra.Count + 1;
    public bool IsDisposed => _disposed;
    public string? StopReason { get; private set; }
    internal HidInputCapture(nint window, MouseConnection connection, IReadOnlyList<HidEndpoint> endpoints, Action<ObservedControl> observed, Action<string> status)
    {
        _connection = connection; _observed = observed; _status = status;
        _endpoints = endpoints.Where(e => e.PhysicalKey.Equals(connection.Key, StringComparison.OrdinalIgnoreCase)).GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _allowed = new HashSet<string>(connection.DevicePaths.Concat(_endpoints.Keys), StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in _endpoints.Values.Where(e => e.InputLength > 0).DistinctBy(e => (e.UsagePage, e.Usage)))
        {
            if (endpoint.UsagePage == 0 || endpoint.Usage == 0 || endpoint.UsagePage == 1 && endpoint.Usage == 2) continue;
            var registration = new Registration { Page = endpoint.UsagePage, Usage = endpoint.Usage, Flags = 0x2100, Window = window };
            if (RegisterRawInputDevices([registration], 1, (uint)Marshal.SizeOf<Registration>())) _extra.Add(registration);
            else _status($"Collection {endpoint.UsagePage:X4}:{endpoint.Usage:X4} is not accessible ({Marshal.GetLastWin32Error()}).");
        }
    }
    internal void Observe(nint rawInput)
    {
        if (_disposed) return;
        uint headerSize = (uint)(8 + 2 * IntPtr.Size), headerLength = headerSize;
        var header = new byte[headerSize];
        if (GetRawInputData(rawInput, 0x10000005, header, ref headerLength, headerSize) == uint.MaxValue) return;
        uint type = BitConverter.ToUInt32(header); nint device = IntPtr.Size == 8 ? (nint)BitConverter.ToInt64(header, 8) : (nint)BitConverter.ToInt32(header, 8);
        if (device == 0) return;
        if (!_paths.TryGetValue(device, out var path))
        {
            uint chars = 0;
            if (GetName(device, 0x20000007, null, ref chars) == uint.MaxValue || chars is 0 or > 65536) return;
            var name = new StringBuilder((int)chars + 1);
            if (GetName(device, 0x20000007, name, ref chars) == uint.MaxValue) return;
            path = name.ToString(); _paths[device] = _allowed.Contains(path) ? path : null;
        }
        if (path is null || !_allowed.Contains(path)) return;
        uint length = 0;
        if (GetRawInputData(rawInput, 0x10000003, null, ref length, headerSize) == uint.MaxValue || length < headerSize || length > 1024 * 1024) return;
        var bytes = new byte[length];
        if (GetRawInputData(rawInput, 0x10000003, bytes, ref length, headerSize) == uint.MaxValue || length < headerSize || length > bytes.Length) return;
        string endpoint = HidCapabilities.EndpointId(path); var payload = bytes.AsSpan((int)headerSize, (int)length - (int)headerSize);
        if (type == 0) { foreach (var signal in RawInputDecoder.Mouse(_connection.Key, endpoint, payload)) { if (_disposed) break; _observed(signal); } }
        else if (type == 1) { if (RawInputDecoder.Keyboard(_connection.Key, endpoint, payload) is { } key) _observed(key); }
        else if (type == 2 && _endpoints.TryGetValue(path, out var description)) ObserveHid(device, endpoint, description, payload);
    }
    private void ObserveHid(nint device, string endpoint, HidEndpoint description, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8) return;
        uint size = BitConverter.ToUInt32(payload), count = BitConverter.ToUInt32(payload[4..]);
        if (size is 0 or > 65536 || count > 256 || (long)size * count > payload.Length - 8) return;
        if (!_preparsed.TryGetValue(device, out var data))
        {
            uint length = 0;
            if (GetDeviceData(device, 0x20000005, 0, ref length) == uint.MaxValue || length is 0 or > 65536) return;
            data = Marshal.AllocHGlobal((int)length);
            if (GetDeviceData(device, 0x20000005, data, ref length) == uint.MaxValue) { Marshal.FreeHGlobal(data); return; }
            _preparsed[device] = data;
        }
        if (description.Buttons.Count == 0 && _reported.Add(endpoint)) _status("This HID collection sends data but declares no readable button usages.");
        var signals = new List<ObservedControl>(); bool reportIds = description.Buttons.Any(c => c.ReportId != 0);
        for (int index = 0; index < count; index++)
        {
            var report = payload.Slice(8 + index * (int)size, (int)size).ToArray();
            foreach (var group in description.Buttons.Where(c => c.ReportId == (reportIds ? report[0] : 0)).GroupBy(c => (c.UsagePage, c.ReportId, c.Collection)))
            {
                var current = group.SelectMany(c => HidCapabilities.Pressed(data, c, report)).Where(u => group.Key.UsagePage != 7 || u is >= 0x68 and <= 0x73).ToHashSet();
                string key = endpoint + ":" + group.Key;
                _pressed.TryGetValue(key, out var previous); previous ??= [];
                foreach (ushort usage in current.Except(previous)) signals.Add(new(_connection.Key, new(endpoint, LearnedInputKind.HidButton, group.Key.UsagePage, usage, group.Key.ReportId, group.Key.Collection), true, false));
                foreach (ushort usage in previous.Except(current)) signals.Add(new(_connection.Key, new(endpoint, LearnedInputKind.HidButton, group.Key.UsagePage, usage, group.Key.ReportId, group.Key.Collection), false, false));
                _pressed[key] = current;
            }
        }
        // A confirmation callback may dispose this capture. Finish every native parser call before invoking it.
        foreach (var signal in signals) { if (_disposed) break; _observed(signal); }
    }
    internal void DeviceChanged(nint change, nint device, string? path)
    {
        if (_disposed) return;
        // Registration can announce current collections. Arrival notifications and
        // other peripherals are not a loss of the selected mouse's capture.
        if (change == 2 && _allowed.Contains(path ?? _paths.GetValueOrDefault(device) ?? ""))
        { Fail("The selected mouse input was disconnected. Reconnect it and choose Listen again."); return; }
        _paths.Remove(device);
        if (_preparsed.Remove(device, out nint data)) Marshal.FreeHGlobal(data);
    }
    internal void Fail(string message) { if (_disposed) return; StopReason = message; _status(message); Dispose(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var registration in _extra) RegisterRawInputDevices([new Registration { Page = registration.Page, Usage = registration.Usage, Flags = 1 }], 1, (uint)Marshal.SizeOf<Registration>());
        foreach (nint data in _preparsed.Values) Marshal.FreeHGlobal(data);
        _preparsed.Clear(); _pressed.Clear(); _paths.Clear(); _extra.Clear();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page, Usage; public uint Flags; public nint Window; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, [Out] byte[]? bytes, ref uint size, uint header);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode)] private static extern uint GetName(nint device, uint command, StringBuilder? name, ref uint length);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")] private static extern uint GetDeviceData(nint device, uint command, nint buffer, ref uint size);
}
