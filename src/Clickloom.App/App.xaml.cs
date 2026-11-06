using System.Threading;
using System.Windows;

namespace Clickloom.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--ui-preview") && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR")))
            Environment.SetEnvironmentVariable("CLICKLOOM_DATA_DIR", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Clickloom", "ui-preview"));
        ThemeManager.Initialize(e.Args.FirstOrDefault(a => a.StartsWith("--theme="))?.Split('=', 2)[1]);
        if (OffscreenPreview.Run(e.Args)) { Shutdown(); return; }
        int previewIndex = Array.IndexOf(e.Args, "--render-preview");
        bool preview = previewIndex >= 0 || e.Args.Contains("--ui-preview");
        bool first = true;
        if (!preview) _instance = new Mutex(true, "Local\\Clickloom.Desktop", out first);
        if (!first)
        {
            System.Windows.MessageBox.Show("Moushift is already running. Open it from the system tray.", "Moushift");
            Shutdown(); return;
        }
        var window = new MainWindow(preview);
        if (e.Args.Contains("--ui-preview")) window.EnterInteractivePreview();
        MainWindow = window;
        if (previewIndex >= 0)
        {
            if (previewIndex + 1 >= e.Args.Length) throw new ArgumentException("--render-preview requires an output PNG path.");
            if (e.Args.Contains("--compact")) { window.Width = 1130; window.Height = 760; }
            if (e.Args.Contains("--wide")) { window.Width = 3828; window.Height = 2050; window.PreviewRenderSize = new Size(3828, 2050); }
            string? page = e.Args.FirstOrDefault(a => a.StartsWith("--page="))?.Split('=', 2)[1];
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                () => window.RenderPreview(e.Args[previewIndex + 1], page, e.Args.Contains("--verify-ui")));
        }
        // A tray start must never show then hide a window: that flashes over the user's work.
        if (e.Args.Contains("--minimized")) new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        else window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
