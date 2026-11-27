using Clickloom.Core;

namespace Clickloom.Windows;

public sealed partial class MouseRemappingService
{
    private readonly object _buttonGateLock = new();
    private readonly Dictionary<MouseControl, DriverlessButtonGate> _buttonGates = new()
    {
        [MouseControl.MiddleButton] = new(),
        [MouseControl.XButton1] = new(),
        [MouseControl.XButton2] = new()
    };
    private readonly System.Collections.Concurrent.ConcurrentQueue<(MouseControl Control, bool Pressed, nint Target)> _buttonReplays = new();
    private int _buttonReplayCount, _buttonReplayReset;
    private readonly HashSet<MouseControl> _nativeButtonsDown = []; // Action worker ownership only.
    private volatile int _sideCaptureMask;

    // Caller holds _settingsGate. Arm only directions used by a connected mouse, across its profiles.
    private void UpdateSideCapturePolicy()
    {
        int mask = 0;
        foreach (var profile in _settings.Profiles)
        {
            if (profile.DeviceKey is null || !_rawCircles.ContainsDevice(profile.DeviceKey)) continue;
            foreach (var binding in profile.Bindings)
                if (binding.Input is MouseControl.XButton1 or MouseControl.XButton2
                    && RawCircleRouter.ReplacesNative(binding.Input, binding.Action))
                    mask |= 1 << (int)binding.Input;
        }
        _sideCaptureMask = mask;
    }

    private void RefreshButtonGates()
    {
        lock (_buttonGateLock)
        {
            bool enabled = _running && _enabled && _rawInputReady && !HasDeviceRouting;
            foreach (var (control, gate) in _buttonGates)
            {
                bool requested = enabled && (control == MouseControl.MiddleButton || (_sideCaptureMask & (1 << (int)control)) != 0);
                gate.Request(requested, requested && !gate.Capturing && IsPhysicalOutputHeld(control));
            }
            if (!enabled) ClearDirectionalCircles();
        }
    }

    private void ResetButtonGates()
    {
        lock (_buttonGateLock)
        {
            foreach (var gate in _buttonGates.Values) gate.Reset();
            ClearDirectionalCircles();
            Monitor.PulseAll(_buttonGateLock);
        }
        Interlocked.Exchange(ref _buttonReplayReset, 1);
        _wake.Set();
    }

    private bool BlockNativeButton(MouseControl control, bool pressed)
    {
        lock (_buttonGateLock)
        {
            var gate = _buttonGates[control];
            if (pressed && gate.Capturing)
            {
                // Wait only for delivery of this direction. Raw Input alone decides which mouse acted.
                long required = gate.NextHookDown(), deadline = Environment.TickCount64 + 25;
                while (gate.Capturing && gate.RawDowns < required)
                {
                    int remaining = (int)(deadline - Environment.TickCount64);
                    if (remaining <= 0 || !Monitor.Wait(_buttonGateLock, remaining)) break;
                }
                if (gate.Capturing && gate.RawDowns < required)
                {
                    gate.Reset();
                    QueueButtonReplay(control, false, 0);
                    if (control == MouseControl.MiddleButton) ClearDirectionalCircles();
                    DisarmFromHook($"Mouse source delivery is delayed; {control} passed through. Enable to retry.");
                    return false;
                }
            }
            return gate.Hook(pressed);
        }
    }

    private ButtonGateResult RouteRawButton(MouseControl control, string path, bool pressed, bool replace, nint target)
    {
        lock (_buttonGateLock)
        {
            var result = _buttonGates[control].Raw(path, pressed, replace);
            if (result.NativePressed is { } native) QueueButtonReplay(control, native, target);
            if (pressed) Monitor.PulseAll(_buttonGateLock);
            TraceCircle(result.Claimed ? "button-action" : result.NativePressed.HasValue ? "button-native-replay" : "button-edge",
                "driverless button gate", control.ToString(), pressed);
            return result;
        }
    }

    private void QueueButtonReplay(MouseControl control, bool pressed, nint target)
    {
        if (Interlocked.Increment(ref _buttonReplayCount) > 64)
        {
            Interlocked.Decrement(ref _buttonReplayCount);
            ResetButtonGates();
            DisarmFromHook("Button forwarding queue is full; Moushift paused.");
            return;
        }
        _buttonReplays.Enqueue((control, pressed, target)); _wake.Set();
    }

    private void DrainButtonReplays()
    {
        if (Interlocked.Exchange(ref _buttonReplayReset, 0) != 0)
        {
            while (_buttonReplays.TryDequeue(out _)) Interlocked.Decrement(ref _buttonReplayCount);
            try { ReleaseNativeButtons(); }
            catch (Exception error)
            {
                // Keep ownership and retry cleanup after focus leaves an inaccessible target.
                Interlocked.Exchange(ref _buttonReplayReset, 1);
                if (_enabled) DisarmFromHook("Button release failed: " + error.Message);
                else _lastError = "Button release pending: " + error.Message;
                return;
            }
        }
        while (_buttonReplays.TryDequeue(out var edge))
        {
            Interlocked.Decrement(ref _buttonReplayCount);
            try
            {
                // Never replay a delayed DOWN into a different window. Owned UPs survive focus changes and pause.
                if (edge.Pressed)
                {
                    if (_nativeButtonsDown.Contains(edge.Control) || edge.Target == 0 || NativeMethods.GetForegroundWindow() != edge.Target) continue;
                    SendMouse(edge.Control, false);
                    _nativeButtonsDown.Add(edge.Control);
                }
                else ReleaseNativeButton(edge.Control);
            }
            catch (Exception error)
            {
                ResetButtonGates(); DisarmFromHook("Button forwarding failed: " + error.Message);
                break;
            }
        }
    }

    private void ReleaseNativeButton(MouseControl control)
    {
        if (!_nativeButtonsDown.Contains(control)) return;
        SendMouse(control, true);
        _nativeButtonsDown.Remove(control);
    }

    private void ReleaseNativeButtons()
    {
        foreach (var control in _buttonGates.Keys) ReleaseNativeButton(control);
    }
}
