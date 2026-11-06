using Clickloom.Core;
using Clickloom.Windows;
using System.Windows;

namespace Clickloom.App;

internal sealed partial class HidLearningWindow
{
    internal void SelectView(MouseDrawingView view)
    {
        if (view == MouseDrawingView.Left) _leftView.IsChecked = true;
        else _topView.IsChecked = true;
    }

    private void ChangeView(MouseDrawingView view)
    {
        if (_drawing.View == view) return;
        Stop("View changed. Choose the button's position on this drawing.");
        _drawing.View = view;
        _picture.Height = view == MouseDrawingView.Left ? 260 : 480;
        _drawing.Height = view == MouseDrawingView.Left ? 240 : 460;
        // A coordinate belongs to one elevation. Never silently project it onto another.
        _position = false; _marker.Visibility = Visibility.Collapsed;
        _listen.IsEnabled = false; _save.IsEnabled = false;
    }

    private void UpdateListeningProgress()
    {
        if (_capture is HidInputCapture { IsDisposed: true } ended)
        { Stop(ended.StopReason ?? "Input observation ended. Choose Listen to retry."); return; }
        if (_capture is null) return;
        long now = Environment.TickCount64;
        if (now < _armedAt) { _progress.Text = "Release all buttons…"; return; }
        if (now >= _expires)
        {
            int confirmed = _session?.Confirmations ?? 0;
            bool packets = _capture is HidInputCapture { ObservedPacketCount: > 0 };
            Stop(confirmed == 1 ? "One press received. Retry and press, release, then press the same button again."
                : packets ? "Mouse data received, but no button was confirmed. Try Back to check learning. DPI/sniper may be handled by firmware or use an unsupported report."
                : "No input received from this connection. Move this mouse and test Back with Listen. Check that the selected receiver belongs to it.");
            return;
        }
        int seconds = (int)Math.Ceiling((_expires - now) / 1000d);
        string observation = _capture is HidInputCapture native
            ? native.ObservedPacketCount > 0 ? "Mouse data received" : "Waiting for mouse data"
            : "Listening";
        _progress.Text = $"{observation} · {_session?.Confirmations ?? 0}/2 presses · {seconds}s left";
    }
}
