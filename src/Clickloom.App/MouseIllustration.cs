using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clickloom.Core;
namespace Clickloom.App;
/// <summary>User-approved original raster artwork with a separate selection overlay.</summary>
public sealed class MouseIllustration : FrameworkElement
{
    private static readonly BitmapImage Image = new(new Uri("pack://application:,,,/Assets/mouse.png"));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(MouseIllustration), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public MouseControl? Selected { get; set; }
    private Rect ImageRect
    {
        get
        {
            double scale = Math.Min(ActualWidth / Image.PixelWidth, ActualHeight / Image.PixelHeight);
            var size = new Size(Image.PixelWidth * scale, Image.PixelHeight * scale);
            return new((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2, size.Width, size.Height);
        }
    }
    internal Point Anchor(MouseControl input)
    {
        var point = input switch
        {
            MouseControl.LeftButton => new Point(.355, .295), MouseControl.RightButton => new Point(.66, .295),
            MouseControl.MiddleButton => new Point(.535, .235), MouseControl.XButton1 => new Point(.20, .52),
            MouseControl.XButton2 => new Point(.18, .39), _ => new Point(.535, .235)
        };
        var r = ImageRect; return new(r.Left + point.X * r.Width, r.Top + point.Y * r.Height);
    }
    internal Point NormalizedAnchor(double x, double y) { var r = ImageRect; return new(r.Left + x * r.Width, r.Top + y * r.Height); }
    internal bool TryNormalize(Point point, out double x, out double y)
    {
        var r = ImageRect; x = y = 0;
        if (r.Width <= 0 || r.Height <= 0 || !r.Contains(point)) return false;
        x = (point.X - r.Left) / r.Width; y = (point.Y - r.Top) / r.Height; return true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var r = ImageRect; dc.DrawImage(Image, r);
        if (Selected is not (MouseControl.XButton1 or MouseControl.XButton2)) return;
        bool back = Selected == MouseControl.XButton1;
        var box = new Rect(r.Left + (back ? .246 : .223) * r.Width, r.Top + (back ? .463 : .337) * r.Height, .043 * r.Width, .109 * r.Height);
        var tint = Accent.Clone(); tint.Opacity = .16;
        dc.DrawRoundedRectangle(tint, new Pen(Accent, 1.8), box, 9, 9);
    }
}
