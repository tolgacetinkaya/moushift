using System.Windows;
using System.Windows.Media;

namespace Clickloom.App;

public sealed class DashboardBatteryDial : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(int), typeof(DashboardBatteryDial), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DashboardBatteryDial), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(DashboardBatteryDial), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Percent { get => (int)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        const int tickCount = 36;
        int charged = (int)Math.Round(Math.Clamp(Percent, 0, 100) * tickCount / 100d);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Max(0, Math.Min(center.X, center.Y) - 5);
        for (int i = 0; i < tickCount; i++)
        {
            double angle = i * Math.Tau / tickCount;
            var tick = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            drawing.DrawEllipse(i < charged ? Accent : Track, null, tick, 2, 2);
        }
    }
}
