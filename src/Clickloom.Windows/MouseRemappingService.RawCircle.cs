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
            ResetMiddleGate();
            if (_enabled && !HasResolvedDeviceRoutes) SetEnabled(false);
            _dispatchState.AdvanceRouting(); DismissRing();
        }
        RefreshMiddleGate();
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
        bool ready = _running && _enabled && !snapshot.OwnWindow && Volatile.Read(ref _captureSession) is null
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
                ? _rawCircles.Wheel(_settings, input.DevicePath, delta, ready && !IsRingOpen, snapshot.ApplicationPath, Environment.TickCount64)
                : _rawCircles.Press(_settings, input.DevicePath, input.Control, input.Pressed,
                    ready && (input.Control == MouseControl.MiddleButton || !IsRingOpen), snapshot.ApplicationPath);
        }
        if (input.Control == MouseControl.MiddleButton)
        {
            var result = RouteRawMiddle(input.DevicePath, input.Pressed, profile is not null, NativeMethods.GetForegroundWindow());
            if (!result.Circle || IsRingOpen) return;
        }
        if (profile is null) return;
        if (!Enqueue(new(new InputAction { Kind = ActionKind.Ring }, false, false, Interlocked.Increment(ref _token),
            stamp.EnableEpoch, stamp.RoutingGeneration, snapshot.ForegroundWindow, profile, true)))
        { DisarmFromHook("Click Circle queue is full; observation paused."); return; }
        Volatile.Write(ref _inputStatus, (profile.DeviceName ?? "Mouse") + " · " + input.Control
            + (input.Control == MouseControl.MiddleButton ? " · Click Circle; native middle click replaced." : " · driverless Click Circle; original input retained."));
    }
}

public sealed record CircleInputTrace(long Tick, string Kind, string Source, string? Button, bool? Pressed);
