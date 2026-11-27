using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Clickloom.App;

internal static class AngularOutline
{
    internal const string MotionKey = "OutlineFlow";
    private static readonly Rect Unit = new(0, 0, 1, 1);
    private static readonly Dictionary<string, BitmapSource> Textures = new(StringComparer.Ordinal);

    internal static void Apply(Ellipse ring, string[] palette, bool animate)
    {
        var colors = palette.Select(value => CircleColors.Parse(value)!.Value).ToArray();
        var texture = Texture(string.Join(',', palette), colors);
        ring.Stroke = new ImageBrush(texture);
        if (!animate) return;

        var rotation = new RotateTransform();
        ring.RenderTransform = rotation;
        var motion = new ParallelTimeline();
        SolidColorBrush? wash = null;
        var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever };

        motion.Children.Add(turn);
        if (colors.Length > 1)
        {
            // A slower crossfade changes the dominant color independently of the moving wave.
            wash = new SolidColorBrush(colors[0]) { Opacity = .42 };
            var layers = new DrawingGroup();
            layers.Children.Add(new ImageDrawing(texture, Unit));
            layers.Children.Add(new GeometryDrawing(wash, null, new RectangleGeometry(Unit)));
            ring.Stroke = new DrawingBrush(layers);
            var fade = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 0; i <= colors.Length; i++)
                fade.KeyFrames.Add(new SplineColorKeyFrame(colors[i % colors.Length], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i * 2.1)), new KeySpline(.42, 0, .58, 1)));

            motion.Children.Add(fade);
        }
        var clock = (ClockGroup)motion.CreateClock(true);
        rotation.ApplyAnimationClock(RotateTransform.AngleProperty, (AnimationClock)clock.Children[0]);
        if (wash is not null) wash.ApplyAnimationClock(SolidColorBrush.ColorProperty, (AnimationClock)clock.Children[1]);
        ring.Resources[MotionKey] = clock;
        clock.Controller!.Begin();
    }

    internal static void Stop(Ellipse ring)
    {
        if (ring.Resources[MotionKey] is ClockGroup clock)
        {
            clock.Controller!.Remove();
            ring.Resources.Remove(MotionKey);
        }
        ring.RenderTransform = Transform.Identity;
    }

    private static BitmapSource Texture(string key, Color[] colors)
    {
        if (Textures.TryGetValue(key, out var cached)) return cached;
        const int size = 96;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double angle = Math.Atan2(x + .5 - size / 2, size / 2 - y - .5);
            double phase = (angle / Math.Tau + 1) % 1;
            double position = phase * colors.Length;
            int index = (int)position;
            double mix = (1 - Math.Cos((position - index) * Math.PI)) / 2;
            Color from = colors[index], to = colors[(index + 1) % colors.Length];
            byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * mix);
            int offset = (y * size + x) * 4;
            pixels[offset] = Blend(from.B, to.B);
            pixels[offset + 1] = Blend(from.G, to.G);
            pixels[offset + 2] = Blend(from.R, to.R);
            pixels[offset + 3] = colors.Length == 1 ? (byte)Math.Round(82 + 173 * (1 + Math.Cos(angle)) / 2) : (byte)255;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        // Cap editor palette history; animation only transforms the existing texture.
        if (Textures.Count >= 32) Textures.Clear();
        Textures.Add(key, bitmap);
        return bitmap;
    }
}
