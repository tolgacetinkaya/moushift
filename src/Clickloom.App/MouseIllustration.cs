using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clickloom.Core;
namespace Clickloom.App;
/// <summary>Illustrates a generic mouse with calibrated button positions for each view.</summary>
public sealed class MouseIllustration : FrameworkElement
{
    private static readonly BitmapImage TopImage = new(new Uri("pack://application:,,,/Assets/mouse-extended-top.png"));
    private static readonly BitmapImage SideImage = new(new Uri("pack://application:,,,/Assets/mouse-extended-left.png"));
    // Learned coordinates refer to the original drawings. Register their outlines to the
    // new artwork so those positions still sit in the equivalent area without rewriting profiles.
    private static readonly Rect LearnedTopFrame = RegisterFrame(new(1073, 1466), new(121, 103, 823, 1256), TopImage, new(84, 78, 831, 1408));
    private static readonly Rect LearnedSideFrame = RegisterFrame(new(2172, 724), new(180, 98, 1812, 526), SideImage, new(144, 34, 1868, 661));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(MouseIllustration), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public MouseControl? Selected { get; set; }
    public static readonly DependencyProperty ViewProperty = DependencyProperty.Register(nameof(View), typeof(MouseDrawingView), typeof(MouseIllustration), new FrameworkPropertyMetadata(MouseDrawingView.Top, FrameworkPropertyMetadataOptions.AffectsRender));
    public MouseDrawingView View { get => (MouseDrawingView)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    private BitmapSource Artwork => View == MouseDrawingView.Left ? SideImage : TopImage;
    private Rect ImageRect
    {
        get
        {
            double width = Artwork.PixelWidth, height = Artwork.PixelHeight;
            double scale = Math.Min(ActualWidth / width, ActualHeight / height);
            var size = new Size(width * scale, height * scale);
            return new((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2, size.Width, size.Height);
        }
    }
    internal Point Anchor(MouseControl input)
    {
        var point = input switch
        {
            MouseControl.LeftButton => new Point(.35, .305), MouseControl.RightButton => new Point(.72, .305),
            MouseControl.XButton1 => new Point(.215, .51), MouseControl.XButton2 => new Point(.18, .354),
            _ => new Point(.523, .234)
        };
        if (View == MouseDrawingView.Left) point = input switch
        {
            MouseControl.XButton1 => new(.51, .36), MouseControl.XButton2 => new(.354, .435),
            _ => new(.243, .175)
        };
        return PointIn(ImageRect, point.X, point.Y);
    }

    private static Rect RegisterFrame(Size previousSize, Rect previousOutline, BitmapSource artwork, Rect outline)
    {
        double horizontalScale = outline.Width / previousOutline.Width / artwork.PixelWidth;
        double verticalScale = outline.Height / previousOutline.Height / artwork.PixelHeight;
        return new(outline.X / artwork.PixelWidth - previousOutline.X * horizontalScale,
            outline.Y / artwork.PixelHeight - previousOutline.Y * verticalScale,
            previousSize.Width * horizontalScale, previousSize.Height * verticalScale);
    }

    private static Point PointIn(Rect frame, double x, double y) => new(frame.X + frame.Width * x, frame.Y + frame.Height * y);

    private Rect LearnedFrame
    {
        get
        {
            var image = ImageRect;
            var registration = View == MouseDrawingView.Left ? LearnedSideFrame : LearnedTopFrame;
            return new(PointIn(image, registration.X, registration.Y),
                new Size(registration.Width * image.Width, registration.Height * image.Height));
        }
    }

    internal Point NormalizedAnchor(double x, double y) => PointIn(LearnedFrame, x, y);
    internal bool TryNormalize(Point point, out double x, out double y)
    {
        var r = LearnedFrame; x = y = 0;
        if (r.Width <= 0 || r.Height <= 0 || !r.Contains(point)) return false;
        x = (point.X - r.Left) / r.Width; y = (point.Y - r.Top) / r.Height; return true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var r = ImageRect;
        dc.DrawImage(Artwork, r);
        if (Selected is not (MouseControl.XButton1 or MouseControl.XButton2)) return;
        var tint = Accent.Clone(); tint.Opacity = .16;
        bool side = View == MouseDrawingView.Left;
        dc.DrawEllipse(tint, new Pen(Accent, 1.4), Anchor(Selected.Value), r.Width * (side ? .063 : .039), r.Height * (side ? .068 : .051));
    }
}
