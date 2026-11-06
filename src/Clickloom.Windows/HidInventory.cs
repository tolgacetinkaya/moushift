using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clickloom.Windows;

public sealed record HidEndpoint(string Path, string Product, ushort Vendor, ushort ProductId,
    ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength, ushort FeatureLength, Guid Container)
{
    public string PhysicalKey { get; init; } = "";
    public IReadOnlyList<HidButtonCapability> Buttons { get; init; } = [];
    public string CapabilityError { get; init; } = "";
}

/// <summary>Reads HID descriptors; never installs a driver or changes a device setting.</summary>
public static class HidInventory
{
    public static IReadOnlyList<HidEndpoint> Enumerate() => Enumerate(out _);
    public static IReadOnlyList<HidEndpoint> Enumerate(out IReadOnlyList<string> diagnostics)
    {
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, 0, 0, 0x12);
        if (set == -1) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<HidEndpoint>();
        int interfaces = 0, detailFailures = 0, attributeFailures = 0, preparsedFailures = 0, capFailures = 0;
        var openFailures = new Dictionary<int, int>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var item = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref item))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                interfaces++;
                SetupDiGetDeviceInterfaceDetail(set, ref item, 0, 0, out uint length, 0);
                if (length is < 8 or > 65536) continue;
                nint detail = Marshal.AllocHGlobal((int)length);
                nint deviceInfo = Marshal.AllocHGlobal(Marshal.SizeOf<InterfaceData>());
                try
                {
                    Marshal.StructureToPtr(new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() }, deviceInfo, false);
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, length, out _, deviceInfo)) { detailFailures++; continue; }
                    var containerProperty = new PropertyKey { Format = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
                    var containerBytes = new byte[16];
                    Guid container = SetupDiGetDeviceProperty(set, deviceInfo, ref containerProperty, out _, containerBytes, 16, out _, 0)
                        ? new Guid(containerBytes) : Guid.Empty;
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null) continue;
                    using var handle = Open(path);
                    if (handle.IsInvalid) { int code = Marshal.GetLastWin32Error(); openFailures.TryGetValue(code, out int count); openFailures[code] = count + 1; continue; }
                    var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes)) { attributeFailures++; continue; }
                    if (!HidD_GetPreparsedData(handle, out var preparsed)) { preparsedFailures++; continue; }
                    var caps = new byte[64];
                    IReadOnlyList<HidButtonCapability> buttons = []; string capabilityError = "";
                    try
                    {
                        if (HidP_GetCaps(preparsed, caps) != 0x110000) { capFailures++; continue; }
                        (buttons, capabilityError) = HidCapabilities.Read(preparsed, caps);
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
                    var product = new byte[512];
                    string name = HidD_GetProductString(handle, product, product.Length)
                        ? System.Text.Encoding.Unicode.GetString(product).TrimEnd('\0') : "HID";
                    result.Add(new(path, name, attributes.Vendor, attributes.Product,
                        BitConverter.ToUInt16(caps, 2), BitConverter.ToUInt16(caps, 0),
                        BitConverter.ToUInt16(caps, 4), BitConverter.ToUInt16(caps, 6), BitConverter.ToUInt16(caps, 8), container)
                        { PhysicalKey = MouseConnectionCatalog.PhysicalKey(path), Buttons = buttons, CapabilityError = capabilityError });
                }
                finally { Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(deviceInfo); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        diagnostics = [$"HID interfaces={interfaces}; details failed={detailFailures}; attributes failed={attributeFailures}; preparsed failed={preparsedFailures}; caps failed={capFailures}",
            "Open failures: " + string.Join(", ", openFailures.Select(p => p.Key + "=" + p.Value))];
        return result;
    }

    internal static SafeFileHandle Open(string path) => CreateFile(path, 0, 3, 0, 3, 0, 0);
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, int length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsed);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(nint preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint preparsed, [Out] byte[] caps);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, nint enumerator, nint window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceProperty(nint set, nint device, ref PropertyKey key, out uint type, [Out] byte[] data, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
}
