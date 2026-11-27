using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Ellipse = System.Windows.Shapes.Ellipse;
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
        if (args.Contains("--verify-circle-colors")) VerifyCircleColors();
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
            bool folderRoot = args.Contains("--folder-root");
            if (folderRoot || args.Contains("--folder")) slots[2] = new() { Name = "Work tools", Kind = RingSlotKind.Folder, Children = CircleCatalog.Actions.Where(a => !a.NeedsConfiguration).Skip(8).Take(9).Select(a => a.Create()).ToList() };
            var view = new RingWindow(slots);
            if (args.Contains("--folder")) view.ShowFolderPreview();
            view.PreviewSelection(folderRoot ? 2 : 0); window = view;
        }
        else if (editor >= 0)
        {
            var profile = ProfileResolver.CreateDeviceDefault("preview:mouse", "Razer Basilisk V3 Pro");
            var state = AppSettings.CreateDefault(); state.Profiles.Add(profile);
            var view = new RingEditorWindow(state, profile.Id, null, profile.DeviceName);
            if (args.Contains("--verify-editor")) view.VerifyEditing();
            if (args.Contains("--folder") || args.Contains("--empty-folder")) view.ShowFolderPreview(args.Contains("--empty-folder"));
            if (args.Contains("--show-circle-colors")) view.ShowColorSettings(args.Contains("--show-color-picker"));
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
        var background = circle < 0 ? ThemeManager.Brush("Bg") : args.FirstOrDefault(a => a.StartsWith("--background=", StringComparison.Ordinal)) switch
        {
            "--background=black" => System.Windows.Media.Brushes.Black,
            "--background=white" => System.Windows.Media.Brushes.White,
            _ => ThemeManager.Brush("Bg")
        };
        var host = new Border { Width = size.Width, Height = size.Height, Background = background, Child = content, Resources = window.Resources };
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

    private static Color OutlineSample(Ellipse ring, double phase)
    {
        var bitmap = (BitmapSource)((ImageBrush)ring.Stroke).ImageSource;
        double angle = phase * Math.Tau;
        int x = (int)((.5 + .42 * Math.Sin(angle)) * bitmap.PixelWidth);
        int y = (int)((.5 - .42 * Math.Cos(angle)) * bitmap.PixelHeight);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static bool Near(Color sampled, string hex)
    {
        var expected = CircleColors.Parse(hex)!.Value;
        return Math.Abs(sampled.R - expected.R) <= 4 && Math.Abs(sampled.G - expected.G) <= 4 && Math.Abs(sampled.B - expected.B) <= 4;
    }

    private static void VerifyCircleColors()
    {
        const string dataDirectory = "CLICKLOOM_DATA_DIR";
        var originalDirectory = Environment.GetEnvironmentVariable(dataDirectory);
        var originalTheme = ThemeManager.Current;
        var directory = Directory.CreateTempSubdirectory("moushift-circle-colors-");
        string file = Path.Combine(directory.FullName, "appearance.json");
        Environment.SetEnvironmentVariable(dataDirectory, directory.FullName);
        try
        {
            File.WriteAllText(file, """{"Theme":"light"}""");
            ThemeManager.Initialize(null);
            if (ThemeManager.Current != "light" || ThemeManager.CirclePaletteFor("a") != new CircleColors())
                throw new InvalidOperationException("Legacy theme preferences did not load.");
            File.WriteAllText(file, """{"Theme":"light","Circle":{"Background":"#243B42"}}""");
            ThemeManager.Initialize(null);
            ThemeManager.SetCirclePalette("a", new("#f5eedf", "#21205e", ["#ff4a63", "invalid", "#26bde0", "#ffeb48"]));
            new CircleAppearancePanel("b", () => { }).VerifyPicker();
            ThemeManager.Apply("dark", true);
            ThemeManager.Initialize(null);
            var palette = ThemeManager.CirclePaletteFor("A");
            if (ThemeManager.Current != "dark" || palette.Background != "#F5EEDF" || palette.Text != "#21205E"
                || ThemeManager.CirclePaletteFor("b").Background != "#A1B2C3" || ThemeManager.CirclePaletteFor("unknown").Background != "#243B42"
                || palette.Folder?.SequenceEqual(new[] { "#FF4A63", "#26BDE0", "#FFEB48" }) != true)
                throw new InvalidOperationException("The palette did not survive switching themes and reopening.");

            if (!SystemParameters.HighContrast)
            {
                var folders = new RingSlot[] { new() { Name = "Work", Kind = RingSlotKind.Folder } };
                var window = new RingWindow(folders, deviceKey: "a");
                var nodes = ((Canvas)window.Content).Children.OfType<Border>().ToArray();
                var outline = ((Grid)nodes[0].Child).Children.OfType<Ellipse>().Single();
                if (!Near(OutlineSample(outline, 0), "#FF4A63") || !Near(OutlineSample(outline, 1d / 3), "#26BDE0") ||
                    !Near(OutlineSample(outline, 2d / 3), "#FFEB48") ||
                    ((SolidColorBrush)nodes[1].Background).Color != CircleColors.Parse(palette.Background) ||
                    ((StackPanel)nodes[1].Child).Children.OfType<TextBlock>().Any(text => ((SolidColorBrush)text.Foreground).Color != CircleColors.Parse(palette.Text)))
                    throw new InvalidOperationException("The desktop Circle did not use the customized palette.");
                window.Close();

                var command = RingDefaults.Create()[0];
                command.Outline = new() { Thickness = 7, Colors = ["#FF8800", "#2244AA"] };
                var styled = new RingWindow([command], deviceKey: "a");
                var item = ((Canvas)styled.Content).Children.OfType<Border>().First();
                var customRing = ((Grid)item.Child).Children.OfType<Ellipse>().Single();
                if (customRing.StrokeThickness != 7 || !Near(OutlineSample(customRing, 0), "#FF8800")
                    || !Near(OutlineSample(customRing, .25), "#916655") || !Near(OutlineSample(customRing, .5), "#2244AA")
                    || !Near(OutlineSample(customRing, .99), "#FF8800"))
                    throw new InvalidOperationException("A command outline did not blend its colors around one seamless turn.");
                AngularOutline.Apply(customRing, [.. command.Outline.Colors], true);
                var flow = (ClockGroup)customRing.Resources[AngularOutline.MotionKey];
                flow.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(2.1), TimeSeekOrigin.BeginTime);
                var layers = (DrawingGroup)((DrawingBrush)customRing.Stroke).Drawing;
                var wash = (SolidColorBrush)((GeometryDrawing)layers.Children[1]).Brush;
                if (!Near(wash.Color, "#2244AA") || Math.Abs(((RotateTransform)customRing.RenderTransform).Angle - 126) > .1)
                    throw new InvalidOperationException($"The traveling wave and palette crossfade did not advance (color {wash.Color}, angle {((RotateTransform)customRing.RenderTransform).Angle}).");
                AngularOutline.Stop(customRing);
                styled.Close();

                var singleColorPalette = palette with { Text = null, Folder = ["#FF4A63"] };
                var marked = (Grid)CircleVisuals.WithOutline(folders[0], new Border(), true, singleColorPalette);
                var single = marked.Children.OfType<Ellipse>().Single();
                if (OutlineSample(single, 0).A <= OutlineSample(single, .5).A || !Near(OutlineSample(single, .25), "#FF4A63") ||
                    CircleVisuals.Contrast(((SolidColorBrush)CircleVisuals.LabelInk(singleColorPalette)).Color, CircleColors.Parse(palette.Background)!.Value) < 4.5)
                    throw new InvalidOperationException("Single-color highlight or automatic text contrast was lost.");
            }
            Console.WriteLine("PASS Circle colors: inline picker, isolated mouse palettes, legacy preferences, custom overlay and single/multi-color outlines.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(dataDirectory, originalDirectory);
            ThemeManager.Initialize(originalTheme);
            File.Delete(file);
            directory.Delete();
        }
    }
}
