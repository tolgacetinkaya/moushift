namespace Clickloom.Windows;

public enum AppliedBindingStatus { Unavailable, Active, Paused }

internal sealed class AssignmentActivation(bool? remembered = null)
{
    private const int Startup = 0, Enabled = 1, Paused = 2, Waiting = 3;
    private int _state = remembered switch { true => Waiting, false => Paused, null => Startup };
    public bool IsWaiting => Volatile.Read(ref _state) == Waiting;
    public bool? Preference => Volatile.Read(ref _state) switch { Startup => null, Paused => false, _ => true };

    public void RequestOn()
    {
        int previous = Volatile.Read(ref _state);
        if (previous is Startup or Paused) Interlocked.CompareExchange(ref _state, Waiting, previous);
    }

    public bool Suspend() => Interlocked.CompareExchange(ref _state, Waiting, Enabled) == Enabled;
    public bool IsEnabled => Volatile.Read(ref _state) == Enabled;
    public bool IsPaused => Volatile.Read(ref _state) == Paused;

    // An Apply racing an emergency pause must never turn input back on.
    public bool Enable(bool onlyFromStartup)
    {
        if (!onlyFromStartup) return Interlocked.Exchange(ref _state, Enabled) != Enabled;
        int previous = Volatile.Read(ref _state);
        return (previous is Startup or Waiting) && Interlocked.CompareExchange(ref _state, Enabled, previous) == previous;
    }

    public bool Pause() => Interlocked.Exchange(ref _state, Paused) == Enabled;
}
