namespace Clickloom.Windows;

/// <summary>
/// While capturing, all physical middle-button legacy edges are blocked. Raw Input alone
/// chooses the source's action or native replay; no last-active-device/timestamp matching.
/// Caller serializes access. A cycle keeps the decision made on its down edge.
/// </summary>
internal sealed class DriverlessMiddleGate
{
    private enum Route { Bypass, Native, Circle }
    private readonly Dictionary<string, Route> _held = new(StringComparer.OrdinalIgnoreCase);
    private int _hookHeld;
    private bool _requested;
    public bool Capturing { get; private set; }

    public void Request(bool enabled, bool nativeButtonDown = false)
    {
        _requested = enabled;
        if (enabled && !Capturing && !nativeButtonDown && _held.Count == 0 && _hookHeld == 0)
            Capturing = true;
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

    public MiddleGateResult Raw(string path, bool pressed, bool useCircle)
    {
        if (pressed)
        {
            if (_held.ContainsKey(path)) return default;
            var route = !Capturing ? Route.Bypass : _requested && useCircle ? Route.Circle : Route.Native;
            bool alreadyNative = _held.Values.Contains(Route.Native);
            _held.Add(path, route);
            return new(route == Route.Circle, route == Route.Native && !alreadyNative ? true : null);
        }
        if (!_held.Remove(path, out var released)) return default;
        bool releaseNative = released == Route.Native && !_held.Values.Contains(Route.Native);
        Drain();
        return new(false, releaseNative ? false : null);
    }

    // Removal cannot leave a synthetic middle button held indefinitely.
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
internal readonly record struct MiddleGateResult(bool Circle, bool? NativePressed);
