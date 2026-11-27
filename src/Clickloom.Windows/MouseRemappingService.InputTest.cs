using Clickloom.Core;

namespace Clickloom.Windows;

public sealed record StandardInputInspection(string ApplicationName, string BindingProfileName, string? CircleProfileName,
    InputAction Action, string Support);

public sealed partial class MouseRemappingService
{
    public StandardInputInspection? InspectStandardInput(RawMouseButtonEvent input, string deviceKey)
    {
        if (_disposed || !_rawInputReady || _enabled) return null;
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot.OwnWindow || snapshot.ForegroundWindow == 0 || snapshot.ForegroundWindow != NativeMethods.GetForegroundWindow())
            return null;

        lock (_settingsGate)
        {
            if (_rawCircles.DeviceKey(input.DevicePath) != deviceKey) return null;
            var profile = ProfileResolver.ResolveDeviceOnly(_settings, snapshot.ApplicationPath, deviceKey);
            var action = profile is null ? new InputAction() : ProfileResolver.ResolveDeviceBinding(_settings, profile, input.Control);
            var circle = action.Kind == ActionKind.Ring ? ProfileResolver.ResolveCircle(_settings, snapshot.ApplicationPath, deviceKey) : null;
            bool standardButton = input.Control is >= MouseControl.LeftButton and <= MouseControl.XButton2;
            bool tilt = input.Control is MouseControl.WheelLeft or MouseControl.WheelRight;
            var support = action.Kind == ActionKind.Passthrough ? "Windows default input."
                : CanRouteDevice(deviceKey) ? "This mouse has a verified remapping route."
                : !HasDeviceRouting && (standardButton && action.IsTaskViewAssignment || (standardButton || tilt) && action.Kind == ActionKind.Ring)
                    ? input.Control == MouseControl.MiddleButton
                        ? "Driverless assignment. This test only observes input."
                        : "Driverless assignment. Original input is retained when enabled."
                    : "Saved assignment. This input has no active remapping route for it.";

            return new(Path.GetFileName(snapshot.ApplicationPath) ?? "Desktop or unknown app", profile?.Name ?? "Default",
                circle?.Name, action.DeepClone(), support);
        }
    }
}
