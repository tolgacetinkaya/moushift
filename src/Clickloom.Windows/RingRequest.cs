using Clickloom.Core;

namespace Clickloom.Windows;

public sealed record RingRequest(Guid Id, nint TargetWindow, long EnableEpoch, long RoutingGeneration, IReadOnlyList<RingSlot> Slots, long TriggerToken = 0, string? DeviceKey = null, bool Driverless = false);

public sealed record RingScrollRequest(Guid Id, int Delta);
public sealed record RingPointerRequest(Guid Id, int X, int Y);
public sealed record CirclePreferenceRequest(string DeviceKey, ActionKind Kind, Guid? ProfileId);
