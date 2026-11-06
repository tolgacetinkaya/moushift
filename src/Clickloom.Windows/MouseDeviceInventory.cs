using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Clickloom.Windows;

public sealed record MouseDeviceInfo(string Name, string DevicePath, string VendorId, string ProductId, int ButtonCount);

public static class MouseDeviceInventory
{
    public static IReadOnlyList<MouseDeviceInfo> Enumerate()
    {
        var devices = new List<MouseDeviceInfo>();
        var structureSize = (uint)Marshal.SizeOf<NativeMethods.RawDevice>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint count = 0;
            if (NativeMethods.GetRawInputDeviceList(null, ref count, structureSize) == uint.MaxValue)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (count == 0) return devices;
            var native = new NativeMethods.RawDevice[count];
            var result = NativeMethods.GetRawInputDeviceList(native, ref count, structureSize);
            if (result == uint.MaxValue)
            {
                if (Marshal.GetLastWin32Error() == 122) continue;
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            foreach (var device in native.Take((int)result).Where(item => item.Type == 0))
            {
                uint length = 0;
                if (NativeMethods.GetRawInputDeviceName(device.Handle, 0x20000007, null, ref length) == uint.MaxValue) continue;
                if (length == 0 || length > 65536) continue;
                var builder = new StringBuilder((int)length + 1);
                if (NativeMethods.GetRawInputDeviceName(device.Handle, 0x20000007, builder, ref length) == uint.MaxValue) continue;
                var path = builder.ToString();
                var info = new NativeMethods.RawDeviceInfo { Size = (uint)Marshal.SizeOf<NativeMethods.RawDeviceInfo>() };
                var infoSize = info.Size;
                var readInfo = NativeMethods.GetRawInputDeviceInformation(device.Handle, 0x2000000B, ref info, ref infoSize) != uint.MaxValue;
                var vendor = Regex.Match(path, "VID[_&](?:02)?([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                var product = Regex.Match(path, "PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                devices.Add(new MouseDeviceInfo(FriendlyName(path, vendor, product), path, vendor, product, readInfo ? (int)info.ButtonCount : 0));
            }
            return devices;
        }
        throw new InvalidOperationException("Mouse devices changed repeatedly; refresh the device list.");
    }

    private static string FriendlyName(string path, string vendor, string product)
    {
        try
        {
            var parts = path.Replace(@"\\?\", "").Split('#');
            if (parts.Length >= 3)
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + string.Join('\\', parts.Take(3)));
                var name = key?.GetValue("FriendlyName") as string ?? key?.GetValue("DeviceDesc") as string;
                if (!string.IsNullOrWhiteSpace(name)) return name.Split(';').Last();
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return vendor.Length > 0 ? $"HID mouse · {vendor}:{product}" : "Windows pointing device";
    }
}
