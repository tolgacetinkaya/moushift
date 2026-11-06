using System.Runtime.InteropServices;
using System.Text;

namespace Clickloom.Windows;

public sealed record MouseConnection(string Key, string Name, string Manufacturer, string Transport, bool IsReceiver, bool IsAuxiliary, string[] DevicePaths)
{
    public int? BatteryPercent { get; init; }
    public string Status => IsReceiver ? Transport + " receiver connected · mouse availability unconfirmed"
        : IsAuxiliary ? "Additional pointing interface"
        : Transport == "Bluetooth" ? "Bluetooth interface connected · mouse availability unconfirmed" : Transport + " mouse interface connected";
}

internal sealed record MouseConnectionIdentity(string Key, string Name, string Transport, bool IsAuxiliary, int? BatteryPercent = null);

/// <summary>Windows device-tree inventory. Never sends a HID feature/output report.</summary>
public static class MouseConnectionCatalog
{
    public static IReadOnlyList<MouseConnection> Enumerate() => Build(MouseDeviceInventory.Enumerate(), Resolve);
    public static string PhysicalKey(string devicePath) => Resolve(new("HID", devicePath, "", "", 0)).Key;

    internal static IReadOnlyList<MouseConnection> Build(IEnumerable<MouseDeviceInfo> interfaces, Func<MouseDeviceInfo, MouseConnectionIdentity> resolve)
    {
        return interfaces.Select(d => (Device: d, Identity: resolve(d)))
            .GroupBy(p => p.Identity.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First(); var id = first.Identity;
                string vendor = first.Device.VendorId;
                string brand = vendor switch { "1532" => "Razer", "1B1C" => "Corsair", "046D" => "Logitech", _ => "Windows" };
                string name = string.IsNullOrWhiteSpace(id.Name) ? first.Device.Name : id.Name;
                if (Generic(name) && id.Transport == "Bluetooth") name = (brand == "Windows" ? "" : brand + " ") + "Bluetooth mouse";
                bool receiver = new[] { "receiver", "dongle", "dock", "transceiver" }.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
                var levels = group.Select(p => p.Identity.BatteryPercent).Where(v => v is >= 0 and <= 100).Distinct().ToArray();
                return new MouseConnection(group.Key, name, brand, id.Transport, receiver, id.IsAuxiliary,
                    group.Select(p => p.Device.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                    { BatteryPercent = levels.Length == 1 ? levels[0] : null };
            }).OrderBy(d => d.IsAuxiliary).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static MouseConnectionIdentity Resolve(MouseDeviceInfo device)
    {
        string instance = device.DevicePath.Replace(@"\\?\", "");
        var parts = instance.Split('#');
        if (parts.Length >= 3) instance = string.Join('\\', parts.Take(3));
        string name = device.Name, key = instance, transport = "Windows";
        int? battery = null;
        bool auxiliary = instance.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase) || instance.Contains("RDP_", StringComparison.OrdinalIgnoreCase);
        if (CM_Locate_DevNode(out uint node, instance, 0) != 0) return new(key, name, transport, auxiliary);
        bool bluetooth = instance.Contains("BTH", StringComparison.OrdinalIgnoreCase) || instance.Contains("00001812", StringComparison.OrdinalIgnoreCase);
        byte[]? container = Property(node, new(new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2));
        if (container is { Length: >= 16 })
        {
            var value = new Guid(container.AsSpan(0, 16));
            if (value != Guid.Empty) key = "container:" + value.ToString("D");
        }
        for (int depth = 0; depth < 9; depth++)
        {
            var nodeId = new StringBuilder(1024);
            if (CM_Get_Device_ID(node, nodeId, nodeId.Capacity, 0) != 0) break;
            string id = nodeId.ToString();
            // Do not read a Bluetooth adapter's battery as the peripheral's.
            if (bluetooth && id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) break;
            bluetooth |= id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
            battery ??= ReadBattery(node, bluetooth);
            var friendly = TextProperty(node, new(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 14));
            var text = !string.IsNullOrWhiteSpace(friendly) && !Generic(friendly) ? friendly : TextProperty(node, new(new("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4));
            if (!string.IsNullOrWhiteSpace(text) && !Generic(text)) name = text;
            bluetooth |= id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase);
            if (id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            {
                if (!bluetooth) { transport = "USB"; key = id; }
                break;
            }
            if (bluetooth && id.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase)) break;
            if (CM_Get_Parent(out uint parent, node, 0) != 0) break;
            node = parent;
        }
        if (bluetooth) transport = "Bluetooth";
        auxiliary |= name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) || name.Contains("virtual", StringComparison.OrdinalIgnoreCase);
        return new(key, name, transport, auxiliary, battery);
    }
    private static int? ReadBattery(uint node, bool bluetooth)
    {
        // PKEY_Devices_BatteryLife: documented byte percentage; 101 means unknown.
        var key = new DeviceProperty(new("49cd1f76-5626-4b17-a4e8-18b4aa1a2213"), 10);
        var value = ReadBatteryProperty(node, key);
        if (value is not null || !bluetooth) return value;
        // Some Windows Bluetooth stacks expose their cached percentage under this
        // device-node property instead of the Shell BatteryLife key. Best effort:
        // its absence or a different data type remains unknown, never guessed.
        return ReadBatteryProperty(node, new(new("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2));
    }
    private static int? ReadBatteryProperty(uint node, DeviceProperty key)
    {
        var data = new byte[1]; uint size = 1;
        return CM_Get_DevNode_Property(node, ref key, out uint type, data, ref size, 0) == 0
            ? DecodeBattery(type, data.AsSpan(0, (int)Math.Min(size, 1))) : null;
    }
    internal static int? DecodeBattery(uint type, ReadOnlySpan<byte> data)
        => type == 3 && data.Length == 1 && data[0] <= 100 ? data[0] : null;
    private static bool Generic(string value) => value.Equals("USB Composite Device", StringComparison.OrdinalIgnoreCase)
        || value.Equals("USB Input Device", StringComparison.OrdinalIgnoreCase) || value.StartsWith("HID-compliant", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("Bluetooth LE Device", StringComparison.OrdinalIgnoreCase);
    private static string? TextProperty(uint node, DeviceProperty key)
    { var bytes = Property(node, key); return bytes is null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0'); }
    private static byte[]? Property(uint node, DeviceProperty key)
    {
        uint size = 0;
        uint result = CM_Get_DevNode_Property(node, ref key, out _, null, ref size, 0);
        if (result != 0x1A || size == 0 || size > 65536) return null;
        var bytes = new byte[size];
        return CM_Get_DevNode_Property(node, ref key, out _, bytes, ref size, 0) == 0 ? bytes : null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceProperty(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNode(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint node, StringBuilder buffer, int length, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")] private static extern uint CM_Get_DevNode_Property(uint node, ref DeviceProperty key, out uint type, [Out] byte[]? data, ref uint size, uint flags);
}
