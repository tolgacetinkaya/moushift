using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Clickloom.Core;

namespace Clickloom.App;

/// <summary>Render real WPF content without showing a native window, starting hooks or a main window.</summary>
internal static class OffscreenPreview
{
    internal static bool Run(string[] args)
    {
        int circle = Array.IndexOf(args, "--render-circle");
        int reset = Array.IndexOf(args, "--render-reset-dialog");
        int workspace = Array.IndexOf(args, "--render-workspace");
        int devices = Array.IndexOf(args, "--render-devices"), setup = Array.IndexOf(args, "--render-setup");
        int settings = Array.IndexOf(args, "--render-settings");
        int editor = Array.IndexOf(args, "--render-circle-editor");
        int learning = Array.IndexOf(args, "--render-hid-learning");
        int index = learning >= 0 ? learning : circle >= 0 ? circle : reset >= 0 ? reset : workspace >= 0 ? workspace : devices >= 0 ? devices : setup >= 0 ? setup : editor >= 0 ? editor : settings;
        if (index < 0) return false;
        if (index + 1 >= args.Length) throw new ArgumentException("An output PNG path is required.");
        if (args.Contains("--verify-circle")) new RingWindow(RingDefaults.Create(), true).VerifyNoActivationAndSelection(false);
        Window window;
        if (learning >= 0)
        {
            var connection = new Clickloom.Windows.MouseConnection("preview:mouse", "HID mouse", "", "USB", false, false, []);
            var view = new HidLearningWindow(connection, [], null, true); view.PreparePreview();
            if (args.Contains("--verify-hid")) view.VerifyLearning(); window = view;
        }
        else if (circle >= 0)
        {
            var slots = RingDefaults.Create();
            if (args.Contains("--folder")) slots[2] = new() { Name = "Work tools", Kind = RingSlotKind.Folder, Children = CircleCatalog.Actions.Where(a => !a.NeedsConfiguration).Skip(8).Take(9).Select(a => a.Create()).ToList() };
            var view = new RingWindow(slots);
            if (args.Contains("--folder")) view.ShowFolderPreview();
            view.PreviewSelection(0); window = view;
        }
        else if (editor >= 0)
        {
            var profile = ProfileResolver.CreateDeviceDefault("preview:mouse", "Razer Basilisk V3 Pro");
            var state = AppSettings.CreateDefault(); state.Profiles.Add(profile);
            var view = new RingEditorWindow(state, profile.Id, null, profile.DeviceName);
            if (args.Contains("--verify-editor")) view.VerifyEditing();
            if (args.Contains("--folder") || args.Contains("--empty-folder")) view.ShowFolderPreview(args.Contains("--empty-folder"));
            window = view;
        }
        else if (reset >= 0) window = new ResetButtonsWindow("Global");
        else {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR"))) Environment.SetEnvironmentVariable("CLICKLOOM_DATA_DIR", Path.Combine(Path.GetTempPath(), "Clickloom", "offscreen"));
            var main = new MainWindow(true);
            if (args.Contains("--compact")) { main.Width = 1130; main.Height = 760; }
            if (args.Contains("--verify-device-ui")) main.VerifyDeviceProfileEditor();
            if (workspace >= 0) main.PrepareOffscreenWorkspace(); else main.PrepareOffscreenPage(devices >= 0 ? "devices" : settings >= 0 ? "appearance" : "setup");
            window = main;
        }
        var content = (FrameworkElement)window.Content; window.Content = null;
        var size = new Size(window.Width, window.Height);
        var host = new Border { Width = size.Width, Height = size.Height, Background = ThemeManager.Brush("Bg"), Child = content };
        System.Windows.Documents.TextElement.SetForeground(host, ThemeManager.Brush("Ink"));
        System.Windows.Documents.TextElement.SetFontFamily(host, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(host, window.FontSize);
        host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
        if (window is MainWindow mainWindow) { mainWindow.FinishOffscreenWorkspaceLayout(); host.UpdateLayout(); }
        if (window is RingEditorWindow circleEditor)
        {
            if (args.Contains("--verify-editor")) circleEditor.VerifyDragPresentation();
            if (args.Contains("--drag-preview")) circleEditor.ShowDragPresentationPreview(args.Contains("--blocked-drop"));
            host.UpdateLayout();
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(host);
        string output = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);
        window.Close(); Console.WriteLine("PASS offscreen WPF: " + output + "; no window shown, hooks or input.");
        return true;
    }
}
