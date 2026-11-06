using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Clickloom.Windows;

public sealed record DeviceSourceInfo(string Key, string Name, string HardwareId, bool SessionOnly);

/// <summary>Optional BYO device driver adapter. Never downloads or installs a driver.</summary>
internal sealed class InterceptionBackend : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] internal struct MouseStroke
    { public ushort State, Flags; public short Rolling; public int X, Y; public uint Information; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Predicate(int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Filter(nint context, Predicate predicate, ushort mask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ushort GetFilter(nint context, int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Wait(nint context, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Transfer(nint context, int device, ref MouseStroke stroke, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Hardware(nint context, int device, [Out] byte[] buffer, uint length);
    private readonly nint _library, _context;
    private readonly Destroy _destroy;
    private readonly Filter _filter;
    private readonly GetFilter _getFilter;
    private readonly Wait _wait;
    private readonly Transfer _receive, _send;
    private readonly Hardware _hardware;
    private readonly Predicate _mice = device => device is >= 11 and <= 20 ? 1 : 0;
    private readonly Dictionary<int, DeviceSourceInfo> _sources = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _capturing;
    public bool IsCapturing => _capturing;
    public IReadOnlyList<DeviceSourceInfo> Sources => _sources.Values.ToArray();
    public event EventHandler<string>? Faulted;
    public delegate bool ProcessInput(DeviceSourceInfo source, Clickloom.Core.MouseControl input, bool release, short wheelDelta);

    public InterceptionBackend(string dllPath)
    {
        if (!Path.IsPathFullyQualified(dllPath) || !File.Exists(dllPath)) throw new FileNotFoundException("An absolute interception.dll path is required.", dllPath);
        _library = NativeLibrary.Load(dllPath);
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        try
        {
            _destroy = Export<Destroy>("interception_destroy_context"); _filter = Export<Filter>("interception_set_filter");
            _getFilter = Export<GetFilter>("interception_get_filter");
            _wait = Export<Wait>("interception_wait_with_timeout"); _receive = Export<Transfer>("interception_receive"); _send = Export<Transfer>("interception_send");
            _hardware = Export<Hardware>("interception_get_hardware_id");
            _context = Export<Create>("interception_create_context")();
            if (_context == 0) throw new InvalidOperationException("The input driver context could not be opened.");
            if (Marshal.SizeOf<MouseStroke>() != 20) throw new InvalidOperationException("Input driver packet ABI mismatch.");
            var identities = new List<(int Device, string Id)>();
            for (int device = 11; device <= 20; device++)
            {
                var buffer = new byte[4096]; uint bytes = _hardware(_context, device, buffer, (uint)buffer.Length);
                if (bytes == 0 || bytes > buffer.Length) continue;
                string id = Encoding.Unicode.GetString(buffer, 0, (int)bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (id.Length > 0) identities.Add((device, id));
            }
            if (identities.Count == 0) throw new InvalidOperationException("No mice were found through the input driver. The library alone does not install the driver.");
            string session = Guid.NewGuid().ToString("N");
            foreach (var item in identities)
            {
                bool duplicate = identities.Count(other => string.Equals(other.Id, item.Id, StringComparison.OrdinalIgnoreCase)) > 1;
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Id.ToUpperInvariant())));
                string key = duplicate ? $"interception-session:{session}:{item.Device}" : "interception:" + hash;
                string brand = item.Id.Contains("VID_1532", StringComparison.OrdinalIgnoreCase) ? "Razer" : item.Id.Contains("VID_1B1C", StringComparison.OrdinalIgnoreCase) ? "Corsair" : item.Id.Contains("VID_046D", StringComparison.OrdinalIgnoreCase) ? "Logitech" : "Mouse";
                _sources.Add(item.Device, new(key, $"{brand} · Device {item.Device - 10}", item.Id, duplicate));
            }
        }
        catch { if (_context != 0) _destroy?.Invoke(_context); NativeLibrary.Free(_library); throw; }
    }

    public void Start(ProcessInput process)
    {
        if (_thread is not null) throw new InvalidOperationException("Device input is already running.");
        _thread = new Thread(() => Run(process)) { IsBackground = true, Name = "Moushift device input" };
        // Mouse movement stays in Windows. Only button/wheel-bearing packets are filtered.
        _capturing = true;
        try
        {
            _filter(_context, _mice, 0x0FFF);
            if (_sources.Keys.Any(device => _getFilter(_context, device) != 0x0FFF))
                throw new InvalidOperationException("The driver did not confirm its input filter.");
            _thread.Start();
        }
        catch { _filter(_context, _mice, 0); _capturing = false; _thread = null; throw; }
    }
    private void Run(ProcessInput process)
    {
        try
        {
            while (!_stopping)
            {
                int device = _wait(_context, 100);
                if (device == 0) continue;
                var stroke = new MouseStroke();
                if (_receive(_context, device, ref stroke, 1) != 1) throw new IOException("Device input could not be read.");
                var original = stroke;
                try
                {
                    if (stroke.Information != (uint)NativeMethods.InjectionMarker && _sources.TryGetValue(device, out var source))
                    {
                        var buffer = new byte[4096]; uint bytes = _hardware(_context, device, buffer, (uint)buffer.Length);
                        string id = bytes is > 0 and <= 4096 ? Encoding.Unicode.GetString(buffer, 0, (int)bytes).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" : "";
                        if (!string.Equals(id, source.HardwareId, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Mouse identity changed. Input was forwarded; restart device support before enabling assignments.");
                        stroke = Rewrite(stroke, (input, up, delta) => process(source, input, up, delta));
                    }
                }
                catch { _send(_context, device, ref original, 1); throw; }
                // Keep motion and unhandled flag bits even if a mapped button was consumed.
                if (stroke.State != 0 || stroke.Flags != 0 || stroke.Rolling != 0 || stroke.X != 0 || stroke.Y != 0)
                    if (_send(_context, device, ref stroke, 1) != 1) throw new IOException("Device input could not be forwarded to Windows.");
            }
        }
        catch (Exception ex) { try { Faulted?.Invoke(this, ex.Message); } catch { } }
        finally { _filter(_context, _mice, 0); _capturing = false; }
    }
    internal static MouseStroke Rewrite(MouseStroke stroke, Func<Clickloom.Core.MouseControl, bool, short, bool> process)
    {
        var inputs = new[] { Clickloom.Core.MouseControl.LeftButton, Clickloom.Core.MouseControl.RightButton, Clickloom.Core.MouseControl.MiddleButton, Clickloom.Core.MouseControl.XButton1, Clickloom.Core.MouseControl.XButton2 };
        for (int index = 0; index < inputs.Length; index++)
            for (int edge = 0; edge < 2; edge++)
            {
                ushort bit = (ushort)(1 << (index * 2 + edge));
                if ((stroke.State & bit) != 0 && process(inputs[index], edge == 1, 0)) stroke.State &= (ushort)~bit;
            }
        short originalDelta = stroke.Rolling;
        foreach (var (bit, positive, negative) in new[] { (0x400, Clickloom.Core.MouseControl.WheelUp, Clickloom.Core.MouseControl.WheelDown), (0x800, Clickloom.Core.MouseControl.WheelRight, Clickloom.Core.MouseControl.WheelLeft) })
            if ((stroke.State & bit) != 0 && originalDelta != 0 && process(originalDelta > 0 ? positive : negative, false, originalDelta))
                stroke.State &= (ushort)~bit;
        if ((stroke.State & 0xC00) == 0) stroke.Rolling = 0;
        return stroke;
    }
    public void Dispose()
    {
        _stopping = true;
        if (_thread is not null && !_thread.Join(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("Device input has not stopped; native resources are still in use.");
        _filter(_context, _mice, 0); _capturing = false; _destroy(_context); NativeLibrary.Free(_library);
    }
}
