using Clickloom.Core;

namespace Clickloom.Windows;

/// <summary>Exact Raw Input source identity; only Circle triggers, never anonymous remapping.</summary>
internal sealed class RawCircleRouter
{
    private Dictionary<string, string> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Path, MouseControl Control)> _held = [];
    private readonly Dictionary<string, WheelBurst> _wheels = new(StringComparer.OrdinalIgnoreCase);
    private sealed record WheelBurst(long Tick, int Direction, int Total, bool Fired, string? Foreground);
    public int Count => _devices.Count;
    public bool ContainsDevice(string key) => _devices.Values.Contains(key, StringComparer.Ordinal);
    public string? DeviceKey(string path) => _devices.GetValueOrDefault(path);

    public bool Update(IReadOnlyList<MouseConnection> connections)
    {
        var next = connections.Where(c => !c.IsAuxiliary).SelectMany(c => c.DevicePaths.Select(path => (Path: path, c.Key)))
            .Where(p => !string.IsNullOrWhiteSpace(p.Path)).GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
        bool changed = _devices.Count != next.Count || _devices.Any(p => !next.TryGetValue(p.Key, out var key) || key != p.Value);
        _held.RemoveWhere(p => !next.TryGetValue(p.Path, out var key) || !_devices.TryGetValue(p.Path, out var previous) || key != previous);
        // Preserve held tilt bursts when a different mouse arrives or leaves.
        foreach (string path in _wheels.Keys.Where(path => !next.TryGetValue(path, out var key)
            || !_devices.TryGetValue(path, out var previous) || key != previous).ToArray())
            _wheels.Remove(path);
        _devices = next;
        return changed;
    }

    public MappingProfile? Press(AppSettings settings, string path, MouseControl control, bool pressed, bool enabled, string? foreground)
    {
        var identity = (path.ToUpperInvariant(), control);
        if (!pressed) { _held.Remove(identity); return null; }
        if (control is not (MouseControl.LeftButton or MouseControl.RightButton or MouseControl.MiddleButton or MouseControl.XButton1 or MouseControl.XButton2)) return null;
        if (!_devices.TryGetValue(path, out string? key) || !_held.Add(identity) || !enabled) return null;
        var profile = ProfileResolver.ResolveDeviceInput(settings, foreground, key);
        return profile?.Bindings.FirstOrDefault(b => b.Input == control)?.Action.Kind == ActionKind.Ring ? profile : null;
    }

    // A tilt may repeat until released; high-resolution deltas may be smaller than one detent.
    // One action per burst, with a quiet interval required before repeating the same direction.
    public MappingProfile? Wheel(AppSettings settings, string path, short delta, bool enabled, string? foreground, long tick)
    {
        if (delta == 0 || !_devices.TryGetValue(path, out var key)) return null;
        int direction = Math.Sign(delta);
        _wheels.TryGetValue(path, out var previous);
        bool fresh = previous is null || tick - previous.Tick > 300 || previous.Direction != direction;
        int total = Math.Min(120, Math.Abs((int)delta) + (fresh ? 0 : previous!.Total));
        bool fired = !enabled || (!fresh && (previous!.Fired || previous.Foreground != foreground));
        _wheels[path] = new(tick, direction, total, fired || total >= 120, foreground);
        if (fired || total < 120) return null;
        var control = direction > 0 ? MouseControl.WheelRight : MouseControl.WheelLeft;
        var profile = ProfileResolver.ResolveDeviceInput(settings, foreground, key);
        return profile?.Bindings.FirstOrDefault(b => b.Input == control)?.Action.Kind == ActionKind.Ring ? profile : null;
    }
}

public sealed record RawMouseButtonEvent(string DevicePath, MouseControl Control, bool Pressed);
public sealed record RawMouseWheelEvent(string DevicePath, short Delta);
