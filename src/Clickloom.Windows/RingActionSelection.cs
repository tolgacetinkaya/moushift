using Clickloom.Core;

namespace Clickloom.Windows;

internal static class RingActionSelection
{
    public static bool TryResolve(RingRequest request, RingInvocation invocation, out InputAction action, out bool close)
    {
        action = new(); close = false;
        var slot = RingNavigation.Resolve(request.Slots, invocation.Path);
        if (slot is null || slot.Kind == RingSlotKind.Folder
            || slot.Kind == RingSlotKind.Adjustment && invocation.Direction is not (-1 or 1)
            || slot.Kind == RingSlotKind.Command && invocation.Direction != 0) return false;
        close = slot.Kind == RingSlotKind.Command;
        action = close ? slot.Action : invocation.Direction > 0 ? slot.IncreaseAction : slot.DecreaseAction;
        return true;
    }
    public static bool CanDispatch(RingRequest request, DispatchStamp stamp, bool enabled, nint foreground) => enabled
        && stamp.EnableEpoch == request.EnableEpoch && stamp.RoutingGeneration == request.RoutingGeneration
        && request.TargetWindow != 0 && foreground == request.TargetWindow;

    public static bool OwnPointer(MouseControl control, bool inside, bool driverless = false) => driverless
        ? inside && control == MouseControl.LeftButton
        : inside || control is MouseControl.XButton1 or MouseControl.XButton2;

    // Raw Input and WH_MOUSE_LL have no guaranteed cross-thread delivery order.
    // A non-left original button must not dismiss the Circle it just opened through Raw Input.
    public static bool ForwardOriginalButton(bool driverless, MouseControl control) => driverless && control != MouseControl.LeftButton;
    public static bool ForwardOriginalWheel(bool driverless, bool horizontal) => driverless && horizontal;
}
