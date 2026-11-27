namespace Clickloom.Windows;

/// <summary>
/// While capturing, all physical legacy edges for one button are blocked. Raw Input alone
/// chooses the source's action or native replay; no last-active-device/timestamp matching.
/// Caller serializes access. A cycle keeps the decision made on its down edge.
/// </summary>
internal sealed class DriverlessButtonGate
{
    private enum Route { Bypass, Native, Claimed }
    private readonly Dictionary<string, Route> _held = new(StringComparer.OrdinalIgnoreCase);
    private int _hookHeld;
    private bool _requested;
    // Counters prove delivery for this button; source identity comes only from Raw Input.
    public long RawDowns { get; private set; }
    public long HookDowns { get; private set; }
    public long NextHookDown() => ++HookDowns;
    public bool Capturing { get; private set; }

    public void Request(bool enabled, bool nativeButtonDown = false)
    {
        _requested = enabled;
        if (enabled && !Capturing && !nativeButtonDown && _held.Count == 0 && _hookHeld == 0)
        {
            Capturing = true;
            RawDowns = HookDowns = 0;
        }
        Drain();
    }

    public bool Hook(bool pressed)
    {
        bool block = Capturing;
        _hookHeld = pressed ? _hookHeld + 1 : Math.Max(0, _hookHeld - 1);
        // Do not arm from the up edge: Raw Input may still be processing that cycle.
        Drain();
        return block;
    }

    public ButtonGateResult Raw(string path, bool pressed, bool replace)
    {
        if (pressed)
        {
            if (Capturing) RawDowns++;
            if (_held.ContainsKey(path)) return default;
            var route = !Capturing ? Route.Bypass : _requested && replace ? Route.Claimed : Route.Native;
            bool alreadyNative = _held.Values.Contains(Route.Native);
            _held.Add(path, route);
            return new(route == Route.Claimed, route == Route.Native && !alreadyNative ? true : null);
        }
        if (!_held.Remove(path, out var released)) return default;
        bool releaseNative = released == Route.Native && !_held.Values.Contains(Route.Native);
        Drain();
        return new(false, releaseNative ? false : null) { ReleasedClaim = released == Route.Claimed };
    }

    // Removal cannot leave a synthetic button held indefinitely.
    public bool RemoveSources(IReadOnlySet<string> remaining)
    {
        bool hadNative = _held.Values.Contains(Route.Native);
        foreach (string path in _held.Keys.Where(p => !remaining.Contains(p)).ToArray()) _held.Remove(path);
        if (_held.Count == 0) _hookHeld = 0;
        Drain();
        return hadNative && !_held.Values.Contains(Route.Native);
    }

    public void Reset()
    {
        _requested = false; Capturing = false; _held.Clear(); _hookHeld = 0;
    }
    private void Drain()
    {
        if (!_requested && _held.Count == 0 && _hookHeld == 0) Capturing = false;
    }
}
internal readonly record struct ButtonGateResult(bool Claimed, bool? NativePressed)
{
    public bool ReleasedClaim { get; init; }
}
