using System.Windows;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

internal sealed partial class HidLearningWindow
{
    private Action<LearnedControl>? _hostedSave;
    private Action? _hostedClose;

    internal FrameworkElement HostIn(Action<LearnedControl> save, Action close)
    {
        _hostedSave = save; _hostedClose = close;
        var surface = (FrameworkElement)Content; Content = null;
        if (!_preview) _ = Discover();
        return surface;
    }

    internal void UpdateConnection(MouseConnection? connection)
    {
        if (_closed) return;
        if (connection is null)
        {
            if (_connected) Stop("This mouse is disconnected. Reconnect it, then choose Listen again.");
            _connected = false; _listen.IsEnabled = false; return;
        }
        if (!string.Equals(connection.Key, _connection.Key, StringComparison.OrdinalIgnoreCase)) return;
        bool changed = !_connected || !_connection.DevicePaths.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(connection.DevicePaths);
        _connection = connection; _connected = true;
        if (changed) { Stop("Connection restored or changed. Rescanning inputs…"); _endpoints = []; _identity = null; _save.IsEnabled = false; _detected.Text = "No input learned yet"; _ = Discover(); }
        else _listen.IsEnabled = _position && !_discovering;
    }

    internal void ReleaseHosted()
    {
        _closed = true; Stop(""); _hostedSave = null; _hostedClose = null; Close();
    }

    internal void VerifyHostedLearning()
    {
        if (Content is not null || _hostedSave is null || _hostedClose is null)
            throw new InvalidOperationException("Learning was not embedded in the main window.");
        VerifyLearning();
        _identity = null; _save.IsEnabled = false;
        Listen();
        if (_capture is null || _listen.Content?.ToString() != "Listening… Cancel")
            throw new InvalidOperationException("Listen stopped before an input could be learned.");
        // Deterministic UI fixture: skip the physical click-release arming delay.
        _session = new(_connection.Key, 0);
        var identity = new InputIdentity(new string('A', 64), LearnedInputKind.MouseButton, 9, 4);
        Observe(new("other:mouse", identity, true, false));
        Observe(new(_connection.Key, identity, true, false));
        if (_save.IsEnabled) throw new InvalidOperationException("One press enabled Save.");
        Observe(new(_connection.Key, identity, false, false));
        Observe(new(_connection.Key, identity, true, false));
        if (!_save.IsEnabled || _capture is not null) throw new InvalidOperationException("Two presses did not complete and clean up learning.");
        Stop(""); Listen();
        if (_capture is null) throw new InvalidOperationException("Listen could not restart.");
        UpdateConnection(null);
        if (_capture is not null || _listen.IsEnabled) throw new InvalidOperationException("A disconnected mouse retained a listener.");
        UpdateConnection(_connection); Listen();
        if (_capture is null) throw new InvalidOperationException("Listen did not recover after reconnect.");
        Stop(""); SelectView(MouseDrawingView.Left); PickPosition(.26, .61);
        _identity = identity; _save.IsEnabled = true;
        if (BuildResult()?.View != MouseDrawingView.Left) throw new InvalidOperationException("Left-side position lost its elevation.");
        SelectView(MouseDrawingView.Top);
        if (_position || _save.IsEnabled || _listen.IsEnabled) throw new InvalidOperationException("Switching elevation silently reused old coordinates.");
        SelectView(MouseDrawingView.Left); PickPosition(.26, .61); Listen();
        _session = new(_connection.Key, 0); _armedAt = 0; _expires = Environment.TickCount64 - 1;
        UpdateListeningProgress();
        if (_capture is not null || !_status.Text.Contains("No input received")) throw new InvalidOperationException("No-input timeout did not explain retry.");
        Listen(); _session = new(_connection.Key, 0); Observe(new(_connection.Key, identity, true, false));
        _armedAt = 0; _expires = Environment.TickCount64 - 1; UpdateListeningProgress();
        if (_capture is not null || !_status.Text.StartsWith("One press received")) throw new InvalidOperationException("Partial confirmation was reported as no input.");
        SelectView(MouseDrawingView.Top); Stop("Ready to listen again."); PreparePreview();
        Console.WriteLine("PASS embedded learning: explicit Listen, selected-source two presses, cancel/retry, disconnect/reconnect, no secondary window or input injection.");
    }
}
