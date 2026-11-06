using System.Text.Json.Serialization;

namespace Clickloom.Core;

public enum LearnedInputKind { MouseButton, Wheel, KeyboardKey, HidButton }

public sealed record InputIdentity(string Endpoint, LearnedInputKind Kind, ushort UsagePage, ushort Usage, byte ReportId = 0, ushort Collection = 0, int Direction = 0)
{
    [JsonIgnore] public string Key => $"{Endpoint.ToUpperInvariant()}:{Kind}:{UsagePage:X4}:{Usage:X4}:{ReportId:X2}:{Collection:X4}:{Direction}";
    [JsonIgnore] public MouseControl? StandardControl => Kind switch
    {
        LearnedInputKind.MouseButton => Usage switch { 1 => MouseControl.LeftButton, 2 => MouseControl.RightButton, 3 => MouseControl.MiddleButton, 4 => MouseControl.XButton1, 5 => MouseControl.XButton2, _ => null },
        LearnedInputKind.Wheel when Usage == 0x38 => Direction > 0 ? MouseControl.WheelUp : MouseControl.WheelDown,
        LearnedInputKind.Wheel when Usage == 0x238 => Direction > 0 ? MouseControl.WheelRight : MouseControl.WheelLeft,
        _ => null
    };
}

public sealed class LearnedControl
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceKey { get; set; } = "";
    public string Name { get; set; } = "Extra button";
    public InputIdentity Identity { get; set; } = new("", LearnedInputKind.HidButton, 0, 0);
    public double X { get; set; } = .5;
    public double Y { get; set; } = .5;
    public DateTimeOffset ObservedAt { get; set; }
    public LearnedControl DeepClone() => new() { Id = Id, DeviceKey = DeviceKey, Name = Name, Identity = Identity with { }, X = X, Y = Y, ObservedAt = ObservedAt };
}

public sealed class LearnedBinding
{
    public Guid ControlId { get; set; }
    public InputAction Action { get; set; } = new();
    public LearnedBinding DeepClone() => new() { ControlId = ControlId, Action = Action.DeepClone() };
}

/// <summary>Explicit learning only. Requires two matching down transitions after arming, from the selected device.</summary>
public sealed class InputLearningSession(string deviceKey, long armAtMilliseconds)
{
    private readonly Dictionary<string, int> _counts = [];
    private readonly HashSet<string> _held = [];
    private long? _lastPulse;
    public int Confirmations { get; private set; }
    public InputIdentity? Result { get; private set; }
    public bool Observe(string sourceDevice, InputIdentity identity, bool pressed, bool pulse, long nowMilliseconds)
    {
        if (!string.Equals(sourceDevice, deviceKey, StringComparison.Ordinal)) return false;
        if (!pressed) { _held.Remove(identity.Key); return false; }
        bool fresh = pulse || _held.Add(identity.Key);
        if (!fresh || nowMilliseconds < armAtMilliseconds || Result is not null) return false;
        if (pulse && _lastPulse is { } previous && nowMilliseconds - previous < 180) return false;
        if (pulse) _lastPulse = nowMilliseconds;
        _counts.TryGetValue(identity.Key, out int count); Confirmations = count + 1; _counts[identity.Key] = Confirmations;
        if (Confirmations >= 2) Result = identity;
        return true;
    }
}

public static class LearnedControls
{
    public static AppSettings Save(AppSettings original, LearnedControl control)
    {
        var next = original.DeepClone();
        var existing = next.LearnedControls.FirstOrDefault(c => c.DeviceKey == control.DeviceKey && c.Identity.Key == control.Identity.Key);
        var saved = control.DeepClone(); if (existing is not null) saved.Id = existing.Id;
        next.LearnedControls.RemoveAll(c => c.Id == saved.Id);
        next.LearnedControls.Add(saved);
        return next;
    }
}
