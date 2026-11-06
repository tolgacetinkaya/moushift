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
        int brand = Array.IndexOf(args, "--export-brand-assets");
        if (brand >= 0)
        {
            BrandMark.Export(args[brand + 1]); return true;
        }
        int circle = Array.IndexOf(args, "--render-circle");
        int reset = Array.IndexOf(args, "--render-reset-dialog");
        int workspace = Array.IndexOf(args, "--render-workspace");
        int devices = Array.IndexOf(args, "--render-devices"), setup = Array.IndexOf(args, "--render-setup");
        int settings = Array.IndexOf(args, "--render-settings");
        int editor = Array.IndexOf(args, "--render-circle-editor");
        int appPicker = Array.IndexOf(args, "--render-app-picker");
        int index = appPicker >= 0 ? appPicker : circle >= 0 ? circle : reset >= 0 ? reset : workspace >= 0 ? workspace : devices >= 0 ? devices : setup >= 0 ? setup : editor >= 0 ? editor : settings;
        if (index < 0) return false;
        if (index + 1 >= args.Length) throw new ArgumentException("An output PNG path is required.");
        if (args.Contains("--verify-circle")) new RingWindow(RingDefaults.Create(), true).VerifyNoActivationAndSelection(false);
        Window window;
        if (appPicker >= 0)
        {
            var view = new Window { Width = 1130, Height = 820, FontFamily = new("Segoe UI"), FontSize = 13 };
            var picker = new ApplicationPicker(view, "Razer Basilisk V3 Pro", [@"C:\Apps\Chrome\chrome.exe"], true, (_, _) => { }, () => { },
                [new("Adobe Photoshop", @"C:\Apps\Adobe\Photoshop.exe", "Installed app"), new("Google Chrome", @"C:\Apps\Chrome\chrome.exe", "Installed app"),
                 new("Microsoft Excel", @"C:\Apps\Office\EXCEL.EXE", "Registered app"), new("Microsoft Visual Studio Code", @"C:\Apps\Code\Code.exe", "Installed app"),
                 new("Spotify", @"C:\Apps\Spotify\Spotify.exe", "Windows app"), new("Windows utility", null, "Windows app")]);
            picker.VerifySelection(); view.Content = picker; window = view;
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
            if (args.Contains("--design-fixture")) main.PrepareDesignFixture();
            else if (workspace >= 0) main.PrepareOffscreenWorkspace(); else main.PrepareOffscreenPage(devices >= 0 ? "devices" : settings >= 0 ? "appearance" : "setup");
            if (args.Contains("--three-devices")) main.AddThirdDesignDevice();
            if (args.Contains("--edit-button")) main.PrepareDesignButtonEditor();
            window = main;
        }
        var content = (FrameworkElement)window.Content; window.Content = null;
        var size = new Size(window.Width, window.Height);
        var host = new Border { Width = size.Width, Height = size.Height, Background = ThemeManager.Brush("Bg"), Child = content, Resources = window.Resources };
        System.Windows.Documents.TextElement.SetForeground(host, ThemeManager.Brush("Ink"));
        System.Windows.Documents.TextElement.SetFontFamily(host, window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(host, window.FontSize);
        host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
        if (window is MainWindow mainWindow)
        {
            mainWindow.FinishOffscreenWorkspaceLayout(); host.UpdateLayout();
            if (args.Contains("--verify-design")) { mainWindow.VerifyDesignWorkspace(); host.UpdateLayout(); }
            mainWindow.PrepareWorkspaceCapture(args); host.UpdateLayout();
        }
        // Flush resource/binding invalidations after a live theme switch on the detached tree.
        var frame = new System.Windows.Threading.DispatcherFrame();
        host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        host.UpdateLayout();
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
