using System.Runtime.InteropServices;
using Clickloom.Core;

namespace Clickloom.Windows;

public static class NativeDiagnostics
{
    public static IReadOnlyList<string> SelfCheck()
    {
        var errors = new List<string>();
        var expected = IntPtr.Size == 8 ? 40 : 28;
        if (Marshal.SizeOf<NativeMethods.Input>() != expected)
            errors.Add($"INPUT layout is {Marshal.SizeOf<NativeMethods.Input>()}; expected {expected}.");
        if (Enum.GetValues<MouseControl>().Length != 9)
            errors.Add("MouseControl must declare five buttons and four wheel directions.");
        if (Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)).ToInt32() != (IntPtr.Size == 8 ? 8 : 4))
            errors.Add("INPUT union alignment is invalid.");
        errors.AddRange(MouseRemappingService.CheckRoutingPrimitives());
        return errors;
    }
}
