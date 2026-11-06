using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clickloom.App;

/// <summary>Original Moushift mark: rounded mouse-button strokes with the approved central star cutout.</summary>
public sealed class BrandMark : FrameworkElement
{
    // Shell icons have no theme resources: use the same canonical mint as dark-mode branding.
    internal const string ShellColor = "#45FFBA";
    internal const string PathData = "F0 M 4.883,69.336 L 13.477,36.23 C 15.43,27.832 21.875,20.898 30.762,20.898 C 38.477,20.312 43.652,24.023 47.363,31.348 L 50.879,40.137 L 61.328,21.777 C 65.039,15.43 71.191,13.281 77.148,13.281 C 87.891,12.695 95.312,20.605 95.02,30.762 C 95.117,33.105 94.336,36.621 93.848,39.551 L 87.305,72.168 C 85.254,82.715 77.93,89.16 67.773,89.062 C 60.547,89.453 55.273,86.426 53.223,80.469 L 56.055,74.121 L 49.707,88.77 L 42.871,74.121 L 38.574,82.617 C 35.449,86.914 30.469,89.258 24.609,89.16 C 12.988,89.453 4.102,81.934 4.883,69.336 Z M 38.477,37.988 L 50.586,47.461 L 62.598,37.988 L 55.566,67.09 L 58.301,71.289 Q 58.594,72.07 54.102,70.996 L 49.707,82.715 L 45.312,70.996 Q 40.723,72.07 41.211,71.289 L 44.336,67.09 Z";
    private static readonly Geometry Mark = Geometry.Parse(PathData);
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(BrandMark), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 100, size / 100));
        dc.DrawGeometry(Foreground, null, Mark); dc.Pop(); dc.Pop();
    }
    internal static BitmapSource Bitmap(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 100d, size / 100d));
            dc.DrawGeometry(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ShellColor)), null, Mark);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static void Export(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        byte[] Encode(int size)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(size)));
            using var stream = new System.IO.MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "moushift-mark.png"), Encode(256));
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "moushift-mark.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><path fill=\"" + ShellColor + "\" fill-rule=\"evenodd\" d=\"" + PathData[3..] + "\"/></svg>");
        var sizes = new[] { 32, 64, 256 }; var images = sizes.Select(Encode).ToArray();
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "Moushift.ico"));
        using var writer = new System.IO.BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
        }
        foreach (var bytes in images) writer.Write(bytes);
    }
}
