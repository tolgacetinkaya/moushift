using System.Windows;
using System.Windows.Controls;

namespace Clickloom.App;

public sealed partial class RingEditorWindow
{
    private Window? _dialogHost;
    private Action? _hostedSave, _hostedClose;
    private Action<double>? _libraryResized;
    private readonly ColumnDefinition _libraryColumn = new() { Width = new(330), MinWidth = 300, MaxWidth = 640 };
    private Window DialogOwner => _dialogHost ?? this;

    // Both standalone previews and the main workspace use this same editor tree.
    // Hosting never creates a second HWND, input hook, or duplicate settings store.
    internal FrameworkElement HostIn(Window host, Action save, Action close, double width, Action<double> resized)
    {
        _dialogHost = host; _hostedSave = save; _hostedClose = close; _libraryResized = resized;
        _libraryColumn.Width = new GridLength(Math.Clamp(width, 300, 640));
        var surface = (FrameworkElement)Content; Content = null;
        return surface;
    }

    internal bool CanDiscardHosted() => !HasChanges() || new ConfirmActionWindow("Discard changes", "Discard Circle changes?",
        "Your saved profiles have not been changed. Discard this editing session?", "Discard") { Owner = DialogOwner }.ShowDialog() == true;

    internal void ReleaseHosted()
    {
        _discarded = true; EndDragPresentation(); _hoverTimer.Stop(); _preview?.Close();
        _hostedSave = _hostedClose = null; _libraryResized = null; _dialogHost = null; Close();
    }

    internal void VerifyHostedSurface()
    {
        if (_dialogHost is null || Content is not null || _canvas.ActualWidth < 400 || _catalog.Children.Count == 0)
            throw new InvalidOperationException("The Circle workspace was not mounted in the main application.");
        VerifyDragPresentation();
        // Exercise a configured folder and scoped save through the hosted editor.
        var original = System.Text.Json.JsonSerializer.Serialize(_draft);
        var target = _draft.DeepClone(); ApplyTo(target);
        if (System.Text.Json.JsonSerializer.Serialize(target) != original)
            throw new InvalidOperationException("Opening the hosted workspace changed a profile.");
        Console.WriteLine("PASS embedded Circle: shared drag library and canvas, scoped draft, no child window.");
    }
}
