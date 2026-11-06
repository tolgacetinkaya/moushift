using Clickloom.Core;

namespace Clickloom.Windows;

public sealed partial class MouseRemappingService
{
    private readonly object _middleGateLock = new();
    private readonly DriverlessMiddleGate _middleGate = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(bool Pressed, nint Target)> _middleReplays = new();
    private int _middleReplayCount, _middleReplayReset;
    private long _rawMiddleDowns, _hookMiddleDowns;
    private readonly ManualResetEventSlim _rawMiddleArrived = new(false);
    private bool _nativeMiddleDown; // Action worker ownership only.

    private void RefreshMiddleGate()
    {
        bool requested = _running && _enabled && _rawInputReady && !HasDeviceRouting;
        bool down = (NativeMethods.GetAsyncKeyState(4) & 0x8000) != 0;
        lock (_middleGateLock)
        {
            bool wasCapturing = _middleGate.Capturing;
            _middleGate.Request(requested, down);
            if (!wasCapturing && _middleGate.Capturing)
            { _rawMiddleDowns = 0; _hookMiddleDowns = 0; _rawMiddleArrived.Reset(); }
        }
    }

    private void ResetMiddleGate()
    {
        lock (_middleGateLock) _middleGate.Reset();
        Interlocked.Exchange(ref _middleReplayReset, 1);
        _wake.Set();
    }

    private bool BlockNativeMiddle(bool pressed)
    {
        long required = 0;
        lock (_middleGateLock)
            if (pressed && _middleGate.Capturing) required = ++_hookMiddleDowns;
        if (required != 0)
        {
            // The UI's Raw Input receiver and this hook run on different threads. Give the
            // receiver a bounded handoff. Counts check delivery health, not source identity:
            // every physical legacy middle edge is blocked and only Raw Input routes it.
            long deadline = Environment.TickCount64 + 25;
            while (Interlocked.Read(ref _rawMiddleDowns) < required)
            {
                _rawMiddleArrived.Reset();
                if (Interlocked.Read(ref _rawMiddleDowns) >= required) break;
                int remaining = (int)(deadline - Environment.TickCount64);
                if (remaining <= 0 || !_rawMiddleArrived.Wait(remaining)) break;
            }
            if (Interlocked.Read(ref _rawMiddleDowns) < required)
            {
                ResetMiddleGate();
                DisarmFromHook("Mouse source delivery is delayed; middle click passed through. Enable Circle to retry.");
                return false;
            }
        }
        lock (_middleGateLock) return _middleGate.Hook(pressed);
    }

    private MiddleGateResult RouteRawMiddle(string path, bool pressed, bool circle, nint target)
    {
        MiddleGateResult result;
        lock (_middleGateLock)
        {
            if (pressed && _middleGate.Capturing) Interlocked.Increment(ref _rawMiddleDowns);
            result = _middleGate.Raw(path, pressed, circle);
            _rawMiddleArrived.Set();
        }
        if (result.NativePressed is { } native) QueueMiddleReplay(native, target);
        TraceCircle(result.Circle ? "middle-circle" : result.NativePressed is not null ? "middle-native-replay" : "middle-edge", "driverless middle gate", "MiddleButton", pressed);
        return result;
    }

    private void QueueMiddleReplay(bool pressed, nint target)
    {
        if (Interlocked.Increment(ref _middleReplayCount) > 64)
        {
            Interlocked.Decrement(ref _middleReplayCount);
            ResetMiddleGate();
            DisarmFromHook("Middle-button forwarding queue is full; Moushift paused.");
            return;
        }
        _middleReplays.Enqueue((pressed, target)); _wake.Set();
    }

    private void DrainMiddleReplays()
    {
        if (Interlocked.Exchange(ref _middleReplayReset, 0) != 0)
        {
            while (_middleReplays.TryDequeue(out _)) Interlocked.Decrement(ref _middleReplayCount);
            try { ReleaseNativeMiddle(); }
            catch (Exception error)
            {
                // Keep ownership and retry cleanup after focus leaves an inaccessible target.
                Interlocked.Exchange(ref _middleReplayReset, 1);
                if (_enabled) DisarmFromHook("Middle-button release failed: " + error.Message);
                else _lastError = "Middle-button release pending: " + error.Message;
                return;
            }
        }
        while (_middleReplays.TryDequeue(out var edge))
        {
            Interlocked.Decrement(ref _middleReplayCount);
            try
            {
                // Never send a delayed DOWN into a different foreground window. An owned UP
                // still releases across focus/pause changes, just like a physical hold.
                if (edge.Pressed)
                {
                    if (_nativeMiddleDown || edge.Target == 0 || NativeMethods.GetForegroundWindow() != edge.Target) continue;
                    SendMouse(MouseControl.MiddleButton, false); _nativeMiddleDown = true;
                }
                else ReleaseNativeMiddle();
            }
            catch (Exception error)
            {
                ResetMiddleGate(); DisarmFromHook("Middle-button forwarding failed: " + error.Message);
                break;
            }
        }
    }

    private void ReleaseNativeMiddle()
    {
        if (!_nativeMiddleDown) return;
        SendMouse(MouseControl.MiddleButton, true);
        _nativeMiddleDown = false;
    }
}
