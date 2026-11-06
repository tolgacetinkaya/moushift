using Clickloom.Core;

namespace Clickloom.Windows;

/// <summary>Exact Raw Input source identity; only Circle triggers, never anonymous remapping.</summary>
internal sealed class RawCircleRouter
{
    private Dictionary<string, string> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Path, MouseControl Control)> _held = [];
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
}

public sealed record RawMouseButtonEvent(string DevicePath, MouseControl Control, bool Pressed);
