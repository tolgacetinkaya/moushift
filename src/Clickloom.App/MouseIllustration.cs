using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clickloom.Core;
namespace Clickloom.App;
/// <summary>User-approved original raster artwork with a separate selection overlay.</summary>
public sealed class MouseIllustration : FrameworkElement
{
    private static readonly BitmapImage Image = new(new Uri("pack://application:,,,/Assets/mouse.png"));
    private static readonly BitmapImage SideImage = new(new Uri("pack://application:,,,/Assets/mouse-left.png"));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(MouseIllustration), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public MouseControl? Selected { get; set; }
    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(nameof(View), typeof(MouseDrawingView), typeof(MouseIllustration), new FrameworkPropertyMetadata(MouseDrawingView.Top, FrameworkPropertyMetadataOptions.AffectsRender));
    public MouseDrawingView View { get => (MouseDrawingView)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    private Rect ImageRect
    {
        get
        {
            double width = View == MouseDrawingView.Left ? SideImage.PixelWidth : Image.PixelWidth;
            double height = View == MouseDrawingView.Left ? SideImage.PixelHeight : Image.PixelHeight;
            double scale = Math.Min(ActualWidth / width, ActualHeight / height);
            var size = new Size(width * scale, height * scale);
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
        if (View == MouseDrawingView.Left) point = input switch
        {
            MouseControl.MiddleButton => new(.26, .33), MouseControl.XButton1 => new(.54, .41),
            MouseControl.XButton2 => new(.425, .45), _ => new(.23, .35)
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
        var r = ImageRect;
        if (View == MouseDrawingView.Left) { DrawLeftSide(dc, r); return; }
        dc.DrawImage(Image, r);
        if (Selected is not (MouseControl.XButton1 or MouseControl.XButton2)) return;
        bool back = Selected == MouseControl.XButton1;
        var box = new Rect(r.Left + (back ? .246 : .223) * r.Width, r.Top + (back ? .463 : .337) * r.Height, .043 * r.Width, .109 * r.Height);
        var tint = Accent.Clone(); tint.Opacity = .16;
        dc.DrawRoundedRectangle(tint, new Pen(Accent, 1.8), box, 9, 9);
    }

    private void DrawLeftSide(DrawingContext dc, Rect r)
    {
        dc.DrawImage(SideImage, r);
        if (Selected is not (MouseControl.XButton1 or MouseControl.XButton2)) return;
        var center = Anchor(Selected.Value);
        var tint = Accent.Clone(); tint.Opacity = .16;
        dc.DrawEllipse(tint, new Pen(Accent, 1.8), center, r.Width * .045, r.Height * .045);
    }
}
