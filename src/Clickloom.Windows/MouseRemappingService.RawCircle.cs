using Clickloom.Core;

namespace Clickloom.Windows;

public sealed partial class MouseRemappingService
{
    private readonly bool _circleDiagnostics = System.IO.Path.IsPathFullyQualified(Environment.GetEnvironmentVariable("CLICKLOOM_DIAGNOSTICS_FILE") ?? "");
    private readonly System.Collections.Concurrent.ConcurrentQueue<CircleInputTrace> _circleTrace = new();
    public IReadOnlyList<CircleInputTrace> RecentCircleInput => _circleTrace.ToArray();
    private void TraceCircle(string kind, string source = "", string? button = null, bool? pressed = null)
    {
        if (!_circleDiagnostics) return;
        _circleTrace.Enqueue(new(Environment.TickCount64, kind, source, button, pressed));
        while (_circleTrace.Count > 80) _circleTrace.TryDequeue(out _);
    }
    private readonly RawCircleRouter _rawCircles = new(); // Protected by _settingsGate.
    private volatile bool _rawInputReady;
    private long _rawCircleTriggerCount;
    public long DriverlessCircleTriggerCount => Interlocked.Read(ref _rawCircleTriggerCount);
    public bool DriverlessCircleMode => !HasDeviceRouting;
    public bool HasDriverlessCircleSources { get { lock (_settingsGate) return _rawInputReady && !HasDeviceRouting && _rawCircles.Count > 0; } }
    public bool CanEnable => HasResolvedDeviceRoutes || HasDriverlessCircleSources;
    public bool CanTriggerCircleForDevice(string key) { lock (_settingsGate) return _rawInputReady && !HasDeviceRouting && _rawCircles.ContainsDevice(key); }

    public void SetRawInputAvailable(bool available)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rawInputReady = available;
        if (!available)
        {
            ResetButtonGates();
            if (!HasResolvedDeviceRoutes) SuspendDeviceActions();
            _dispatchState.AdvanceRouting(); DismissRing();
        }
        RefreshButtonGates();
        RaiseStateChanged();
    }

    public void ObserveRawMouseButton(RawMouseButtonEvent input)
        => ObserveRawCircleInput(input, null);

    public void ObserveRawMouseWheel(RawMouseWheelEvent input)
    {
        if (input.Delta == 0) return;
        ObserveRawCircleInput(new(input.DevicePath, input.Delta > 0 ? MouseControl.WheelRight : MouseControl.WheelLeft, true), input.Delta);
    }

    private void ObserveRawCircleInput(RawMouseButtonEvent input, short? wheelDelta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_rawInputReady || HasDeviceRouting) return;
        var snapshot = Volatile.Read(ref _snapshot);
        var stamp = _dispatchState.Current;
        long tick = Environment.TickCount64;
        bool sideButton = input.Control is MouseControl.XButton1 or MouseControl.XButton2;
        bool ready = _running && _enabled && (!snapshot.OwnWindow || sideButton) && Volatile.Read(ref _captureSession) is null
            && snapshot.Generation == stamp.RoutingGeneration && snapshot.ForegroundWindow != 0
            && NativeMethods.GetForegroundWindow() == snapshot.ForegroundWindow;
        MappingProfile? profile;
        lock (_settingsGate)
        {
            if (_circleDiagnostics)
            {
                string? key = _rawCircles.DeviceKey(input.DevicePath);
                TraceCircle("raw", _settings.Profiles.FirstOrDefault(p => p.DeviceKey == key && p.ApplicationPath is null)?.DeviceName ?? "Unassigned/unknown mouse", input.Control.ToString(), input.Pressed);
            }
            if (wheelDelta.HasValue && _rawCircles.DeviceKey(input.DevicePath) is not null) Observe(input.Control);
            profile = wheelDelta is { } delta
                ? _rawCircles.Wheel(_settings, input.DevicePath, delta, ready && !IsRingOpen, snapshot.ApplicationPath, tick)
                : _rawCircles.Press(_settings, input.DevicePath, input.Control, input.Pressed,
                    ready && (input.Control == MouseControl.MiddleButton || !IsRingOpen), snapshot.ApplicationPath);
        }
        var action = profile?.Bindings.FirstOrDefault(binding => binding.Input == input.Control)?.Action;
        if (sideButton)
        {
            if (OwnWindowPolicy.ShouldBypass(snapshot.OwnWindow, input.Control, action, false))
                action = null;
            bool replace = RawCircleRouter.ReplacesNative(input.Control, action);
            var route = RouteRawButton(input.Control, input.DevicePath, input.Pressed, replace, NativeMethods.GetForegroundWindow());
            if (replace && !route.Claimed) return;
        }
        if (input.Control == MouseControl.MiddleButton)
        {
            var result = RouteRawButton(input.Control, input.DevicePath, input.Pressed, action is not null, NativeMethods.GetForegroundWindow());
            if (!input.Pressed)
                (profile, action) = EndDirectionalCircle(input.DevicePath, result.ReleasedClaim, ready, stamp, snapshot.ForegroundWindow, tick);
            else
            {
                if (!result.Claimed || IsRingOpen) return;
                if (profile?.DirectionalCircle is not null && action?.Kind == ActionKind.Ring)
                {
                    BeginDirectionalCircle(input.DevicePath, profile, stamp, snapshot.ForegroundWindow, tick);
                    return;
                }
            }
        }
        if (profile is null || action is null) return;
        bool opensCircle = action.Kind == ActionKind.Ring;
        if (!Enqueue(new(action, false, false, Interlocked.Increment(ref _token), stamp.EnableEpoch,
            stamp.RoutingGeneration, snapshot.ForegroundWindow, opensCircle ? profile : null, opensCircle)))
        { DisarmFromHook("Mouse action queue is full; observation paused."); return; }
        string actionName = opensCircle ? "Click Circle" : action.IsTaskViewAssignment ? "Task View"
            : sideButton ? "Keyboard shortcut" : "Quick gesture";
        string inputBehavior = input.Control == MouseControl.MiddleButton ? "native middle click replaced"
            : RawCircleRouter.ReplacesNative(input.Control, action) ? "native side button replaced" : "original input retained";
        Volatile.Write(ref _inputStatus, $"{profile.DeviceName ?? "Mouse"} · {input.Control} · {actionName}; {inputBehavior}.");
    }
}

public sealed record CircleInputTrace(long Tick, string Kind, string Source, string? Button, bool? Pressed);
