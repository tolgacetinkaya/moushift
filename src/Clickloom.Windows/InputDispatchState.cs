using Clickloom.Core;

namespace Clickloom.Windows;

internal readonly record struct DispatchStamp(long EnableEpoch, long RoutingGeneration);
internal readonly record struct InputRoutingIdentity(nint WindowHandle, uint ProcessId, string? ApplicationPath, Guid ProfileId, long SettingsRevision);

/// <summary>Versions queued work without changing ownership of an already-held output.</summary>
internal sealed class InputDispatchState
{
    private long _enableEpoch;
    private long _routingGeneration;
    private InputRoutingIdentity? _routingIdentity;

    public DispatchStamp Current => new(Interlocked.Read(ref _enableEpoch), Interlocked.Read(ref _routingGeneration));
    public long AdvanceEnableEpoch() => Interlocked.Increment(ref _enableEpoch);
    public long AdvanceRouting() => Interlocked.Increment(ref _routingGeneration);

    // The foreground poll has a single owner; manual settings changes invalidate
    // routing immediately through AdvanceRouting, before the next poll can run.
    public DispatchStamp UpdateRouting(InputRoutingIdentity identity)
    {
        if (_routingIdentity != identity)
        {
            _routingIdentity = identity;
            AdvanceRouting();
        }
        return Current;
    }

    public static bool CanExecute(DispatchStamp queued, DispatchStamp current, bool enabled, bool isRelease) =>
        enabled && queued.EnableEpoch == current.EnableEpoch && (isRelease || queued.RoutingGeneration == current.RoutingGeneration);
}

internal sealed record InputGesture(bool Suppressed, InputAction? Action, long Epoch, long Token, Guid? RingClickId = null);

/// <summary>Retains the DOWN decision until its physical UP, including pass-through gestures.</summary>
internal sealed class InputGestureLedger<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, InputGesture> _held = new();
    public bool TryGet(TKey input, out InputGesture gesture) => _held.TryGetValue(input, out gesture!);
    public void Begin(TKey input, InputGesture gesture)
    {
        if (!_held.TryAdd(input, gesture)) throw new InvalidOperationException("A held input cannot be rebound before its release.");
    }
    public bool Release(TKey input, out InputGesture gesture) => _held.Remove(input, out gesture!);
}

internal static class AuxiliaryInputFilter
{
    public static bool ShouldObserve(uint virtualKey, uint flags, nuint extraInfo)
    {
        // Vendor software may inject an explicitly opted-in F13-F24 source.
        // Only our own tagged output is excluded; the source device is unknown.
        _ = flags;
        return virtualKey is >= 0x7C and <= 0x87 && extraInfo != NativeMethods.InjectionMarker;
    }
}
