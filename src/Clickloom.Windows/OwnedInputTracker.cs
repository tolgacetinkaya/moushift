namespace Clickloom.Windows;

/// <summary>Tracks only DOWN events actually accepted by SendInput, never planned events.</summary>
internal sealed class OwnedInputTracker
{
    private readonly record struct Identity(uint Kind, uint Key, uint Scan, uint Mode);
    private readonly Dictionary<Identity, NativeMethods.Input> _pending = new();
    private readonly List<Identity> _order = new();
    public int Count => _pending.Count;
    public NativeMethods.Input[] PendingReleases => _order.AsEnumerable().Reverse().Select(identity => _pending[identity]).ToArray();

    public void AcceptPrefix(NativeMethods.Input[] inputs, int accepted)
    {
        if (accepted < 0 || accepted > inputs.Length) throw new ArgumentOutOfRangeException(nameof(accepted));
        for (var index = 0; index < accepted; index++)
        {
            var input = inputs[index];
            if (input.Type == 1)
            {
                var keyboard = input.Data.Keyboard;
                var identity = new Identity(1, keyboard.VirtualKey, keyboard.Scan, keyboard.Flags & (NativeMethods.Unicode | 1u));
                if ((keyboard.Flags & NativeMethods.KeyUp) != 0) Remove(identity);
                else
                {
                    var release = input;
                    release.Data.Keyboard.Flags |= NativeMethods.KeyUp;
                    Add(identity, release);
                }
            }
            else if (input.Type == 0)
            {
                TrackMouse(input, 0x2, 0x4, 1);
                TrackMouse(input, 0x8, 0x10, 2);
                TrackMouse(input, 0x20, 0x40, 3);
                TrackMouse(input, 0x80, 0x100, 4 + input.Data.Mouse.MouseData);
            }
        }
    }

    private void TrackMouse(NativeMethods.Input input, uint down, uint up, uint key)
    {
        var identity = new Identity(0, key, 0, 0);
        if ((input.Data.Mouse.Flags & up) != 0) Remove(identity);
        else if ((input.Data.Mouse.Flags & down) != 0)
        {
            var release = input;
            release.Data.Mouse.Flags = up;
            Add(identity, release);
        }
    }

    private void Add(Identity identity, NativeMethods.Input release)
    {
        if (_pending.TryAdd(identity, release)) _order.Add(identity);
    }
    private void Remove(Identity identity)
    {
        if (_pending.Remove(identity)) _order.Remove(identity);
    }
}
