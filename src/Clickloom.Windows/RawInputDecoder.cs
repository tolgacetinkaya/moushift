using Clickloom.Core;

namespace Clickloom.Windows;

public sealed record ObservedControl(string DeviceKey, InputIdentity Identity, bool Pressed, bool Pulse);

/// <summary>Decode only mouse state transitions; coordinates/text are neither emitted nor stored.</summary>
internal static class RawInputDecoder
{
    internal static IReadOnlyList<ObservedControl> Mouse(string deviceKey, string endpoint, ReadOnlySpan<byte> mouse)
    {
        if (mouse.Length < 24) return [];
        ushort flags = BitConverter.ToUInt16(mouse[4..]); short delta = BitConverter.ToInt16(mouse[6..]);
        var result = new List<ObservedControl>();
        for (ushort button = 1; button <= 5; button++)
            for (int edge = 0; edge < 2; edge++)
                if ((flags & 1 << ((button - 1) * 2 + edge)) != 0)
                    result.Add(new(deviceKey, new(endpoint, LearnedInputKind.MouseButton, 9, button), edge == 0, false));
        if (delta != 0)
        {
            if ((flags & 0x400) != 0) result.Add(new(deviceKey, new(endpoint, LearnedInputKind.Wheel, 1, 0x38, Direction: Math.Sign(delta)), true, true));
            if ((flags & 0x800) != 0) result.Add(new(deviceKey, new(endpoint, LearnedInputKind.Wheel, 12, 0x238, Direction: Math.Sign(delta)), true, true));
        }
        return result;
    }
    internal static ObservedControl? Keyboard(string deviceKey, string endpoint, ReadOnlySpan<byte> keyboard)
    {
        if (keyboard.Length < 16) return null;
        ushort flags = BitConverter.ToUInt16(keyboard[2..]), key = BitConverter.ToUInt16(keyboard[6..]);
        if (key is < 0x7C or > 0x87) return null; // F13–F24 only; never collect ordinary typing.
        return new(deviceKey, new(endpoint, LearnedInputKind.KeyboardKey, 7, key), (flags & 1) == 0, false);
    }
}
