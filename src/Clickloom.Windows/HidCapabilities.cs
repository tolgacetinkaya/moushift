using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Clickloom.Windows;

public sealed record HidButtonCapability(ushort UsagePage, byte ReportId, ushort Collection, ushort Minimum, ushort Maximum)
{
    public int Count => Maximum - Minimum + 1;
}

/// <summary>Windows HID parser only. No feature/output requests or vendor-specific protocols.</summary>
public static class HidCapabilities
{
    public static string EndpointId(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
    internal static (IReadOnlyList<HidButtonCapability> Buttons, string Error) Read(nint preparsed, byte[] caps)
    {
        ushort count = BitConverter.ToUInt16(caps, 46);
        if (count == 0) return ([], "No input button usages declared");
        if (count > 512) return ([], "Descriptor exceeds the supported capability limit");
        var buffer = new byte[count * 72];
        int status = HidP_GetButtonCaps(0, buffer, ref count, preparsed);
        return status == 0x110000 ? (ParseButtonCaps(buffer.AsSpan(0, count * 72)), "") : ([], "Button capabilities unavailable: " + status.ToString("X8"));
    }
    internal static IReadOnlyList<HidButtonCapability> ParseButtonCaps(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % 72 != 0) throw new ArgumentException("Invalid HID button capability buffer.");
        var result = new List<HidButtonCapability>();
        for (int offset = 0; offset < bytes.Length; offset += 72)
        {
            var cap = bytes.Slice(offset, 72); ushort page = BitConverter.ToUInt16(cap), min = BitConverter.ToUInt16(cap[56..]);
            ushort max = cap[12] != 0 ? BitConverter.ToUInt16(cap[58..]) : min;
            if (min == 0 || max < min || max - min > 2048) continue;
            result.Add(new(page, cap[2], BitConverter.ToUInt16(cap[6..]), min, max));
        }
        return result;
    }
    internal static IEnumerable<ushort> Pressed(nint preparsed, HidButtonCapability cap, byte[] report)
    {
        uint count = (uint)Math.Min(cap.Count, 2048); var usages = new ushort[count];
        int status = HidP_GetUsages(0, cap.UsagePage, cap.Collection, usages, ref count, preparsed, report, (uint)report.Length);
        return status == 0x110000 ? usages.Take((int)Math.Min(count, (uint)usages.Length)).Where(u => u >= cap.Minimum && u <= cap.Maximum).ToArray() : [];
    }
    [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int type, [Out] byte[] caps, ref ushort count, nint preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetUsages(int type, ushort page, ushort collection, [Out] ushort[] usages, ref uint count, nint preparsed, byte[] report, uint length);
}
