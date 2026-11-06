namespace Clickloom.Core;

public sealed record RingInvocation(IReadOnlyList<int> Path, int Direction = 0);
