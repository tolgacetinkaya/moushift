using Clickloom.Core;

namespace Clickloom.Windows;

public sealed record ShortcutCaptureUpdate(long SessionId, int KeyCode, KeyModifiers Modifiers, bool Cancelled);
internal readonly record struct CaptureDecision(bool Suppress, bool Publish, int KeyCode, KeyModifiers Modifiers, bool Cancelled);

/// <summary>Hook-thread ownership; only keys whose DOWN was consumed get a consumed UP.</summary>
internal sealed class ShortcutCaptureTracker
{
    private readonly HashSet<int> _owned = [];
    private readonly HashSet<int> _preexisting = [];
    private long _session;
    private int _primary;
    public bool HasOwnedKeys => _owned.Count != 0;
    public void SetBaseline(long session, IEnumerable<int> keys)
    {
        if (_session == session) return;
        _session = session; _primary = 0; _preexisting.Clear();
        _preexisting.UnionWith(keys.Where(key => !_owned.Contains(key)));
    }
    public bool WasDownAtStart(int key) => _preexisting.Contains(key);
    public static int NormalizeModifier(int key, uint scan, uint flags) => key switch
    {
        0x10 => scan == 0x36 ? 0xA1 : 0xA0,
        0x11 => (flags & 1) != 0 ? 0xA3 : 0xA2,
        0x12 => (flags & 1) != 0 ? 0xA5 : 0xA4,
        _ => key
    };
    public KeyModifiers ExistingModifiers =>
        (_preexisting.Overlaps([0x10, 0xA0, 0xA1]) ? KeyModifiers.Shift : 0)
        | (_preexisting.Overlaps([0x11, 0xA2, 0xA3]) ? KeyModifiers.Control : 0)
        | (_preexisting.Overlaps([0x12, 0xA4, 0xA5]) ? KeyModifiers.Alt : 0)
        | (_preexisting.Overlaps([0x5B, 0x5C]) ? KeyModifiers.Windows : 0);
    public CaptureDecision Process(long session, int key, bool release, bool active, bool previouslyDown, KeyModifiers externalModifiers)
    {
        if (_session != session) { _session = session; _primary = 0; }
        if (release) { _preexisting.Remove(key); return new(_owned.Remove(key), false, 0, 0, false); }
        if (_owned.Contains(key)) return new(true, false, 0, 0, false);
        if (!active || previouslyDown) return default;
        _owned.Add(key);
        KeyModifiers modifiers = externalModifiers;
        if (_owned.Overlaps([0x10, 0xA0, 0xA1])) modifiers |= KeyModifiers.Shift;
        if (_owned.Overlaps([0x11, 0xA2, 0xA3])) modifiers |= KeyModifiers.Control;
        if (_owned.Overlaps([0x12, 0xA4, 0xA5])) modifiers |= KeyModifiers.Alt;
        if (_owned.Overlaps([0x5B, 0x5C])) modifiers |= KeyModifiers.Windows;
        if (key == 27) return new(true, true, 0, modifiers, true);
        bool modifier = key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
        if (!modifier) _primary = key;
        int primary = _owned.Contains(_primary) ? _primary : 0;
        return new(true, true, primary, modifiers, false);
    }
}
