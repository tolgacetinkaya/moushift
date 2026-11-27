using Clickloom.Core;

namespace Clickloom.Windows;

public sealed partial class MouseRemappingService
{
    private const int DirectionalHoldMilliseconds = 180;
    private const int DirectionalGestureTimeoutMilliseconds = 2500;
    private const int DirectionalMovementCounts = 96;
    private const double DirectionalAxisRatio = 1.5;

    private readonly Dictionary<string, DirectionalHold> _directionalHolds = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _hasDirectionalHolds;

    private sealed class DirectionalHold(MappingProfile profile, DispatchStamp stamp, nint foreground, long start)
    {
        public MappingProfile Profile { get; } = profile;
        public DispatchStamp Stamp { get; } = stamp;
        public nint Foreground { get; } = foreground;
        public long Start { get; } = start;
        public long X { get; set; }
        public long Y { get; set; }
    }

    public void ObserveRawMouseMotion(RawMouseMotionEvent input)
    {
        if (!_hasDirectionalHolds) return;
        lock (_buttonGateLock)
        {
            if (!_directionalHolds.TryGetValue(input.DevicePath, out var hold)) return;
            long elapsed = Environment.TickCount64 - hold.Start;
            if (!_enabled || IsRingOpen || hold.Stamp != _dispatchState.Current
                || NativeMethods.GetForegroundWindow() != hold.Foreground
                || elapsed > DirectionalGestureTimeoutMilliseconds)
            {
                _directionalHolds.Remove(input.DevicePath);
                _hasDirectionalHolds = _directionalHolds.Count != 0;
                return;
            }
            if (elapsed < DirectionalHoldMilliseconds) return;
            hold.X += input.X;
            hold.Y += input.Y;
        }
    }

    private void BeginDirectionalCircle(string path, MappingProfile profile, DispatchStamp stamp, nint foreground, long tick)
    {
        lock (_buttonGateLock)
        {
            if (!_buttonGates[MouseControl.MiddleButton].Capturing || !_enabled || stamp != _dispatchState.Current) return;
            _directionalHolds[path] = new(profile, stamp, foreground, tick);
            _hasDirectionalHolds = true;
        }
    }

    private (MappingProfile? Profile, InputAction? Action) EndDirectionalCircle(string path, bool releasedCircle,
        bool ready, DispatchStamp stamp, nint foreground, long tick)
    {
        DirectionalHold? hold;
        lock (_buttonGateLock)
        {
            _directionalHolds.Remove(path, out hold);
            _hasDirectionalHolds = _directionalHolds.Count != 0;
        }
        if (!releasedCircle || !ready || IsRingOpen || hold is null || hold.Stamp != stamp
            || hold.Foreground != foreground || hold.Profile.DirectionalCircle is not { } gestures)
            return default;

        long elapsed = tick - hold.Start;
        if (elapsed < DirectionalHoldMilliseconds) return (hold.Profile, new() { Kind = ActionKind.Ring });
        if (elapsed > DirectionalGestureTimeoutMilliseconds) return default;

        long horizontal = Math.Abs(hold.X);
        long vertical = Math.Abs(hold.Y);
        DirectionalShortcut? shortcut = null;
        if (horizontal >= DirectionalMovementCounts && horizontal >= DirectionalAxisRatio * vertical)
            shortcut = hold.X < 0 ? gestures.Left : gestures.Right;
        else if (vertical >= DirectionalMovementCounts && vertical >= DirectionalAxisRatio * horizontal)
            shortcut = hold.Y < 0 ? gestures.Up : gestures.Down;
        return shortcut is null ? default : (hold.Profile, shortcut.ToAction());
    }

    // Caller holds the button gate lock.
    private void ClearDirectionalCircles()
    {
        _directionalHolds.Clear();
        _hasDirectionalHolds = false;
    }
}
