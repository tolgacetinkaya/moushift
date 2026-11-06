using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

public partial class MainWindow
{
    private readonly List<Button> _learnedMarkers = [];
    private void PlaceLearnedMarkers()
    {
        foreach (var marker in _learnedMarkers) MarkerCanvas.Children.Remove(marker); _learnedMarkers.Clear();
        string? key = EditingProfile.DeviceKey;
        foreach (var control in _settings.LearnedControls.Where(c => c.DeviceKey == key && c.View == HeroMouse.View))
        {
            var point = HeroMouse.TranslatePoint(HeroMouse.NormalizedAnchor(control.X, control.Y), MarkerCanvas);
            if (control.Identity.StandardControl is { } standard)
            {
                var button = standard switch { MouseControl.LeftButton => LeftCallout, MouseControl.RightButton => RightCallout, MouseControl.MiddleButton => MiddleCallout, MouseControl.XButton1 => BackCallout, MouseControl.XButton2 => ForwardCallout, _ => null };
                if (button is not null) { button.Visibility = Visibility.Visible; Canvas.SetLeft(button, point.X - 13); Canvas.SetTop(button, point.Y - 13); button.ToolTip = control.Name; continue; }
            }
            var marker = new Button { Content = "+", Width = 26, Height = 26, Padding = new(0), Style = (Style)FindResource("Marker"), ToolTip = control.Name + " · Learned input", Tag = control.Id };
            System.Windows.Automation.AutomationProperties.SetName(marker, control.Name);
            marker.Click += (_, _) =>
            {
                BindingsList.SelectedItem = control.Identity.StandardControl is { } input ? _bindings.FirstOrDefault(b => b.Mouse == input) : _bindings.FirstOrDefault(b => b.LearnedId == control.Id);
                OpenEditor();
            };
            Canvas.SetLeft(marker, point.X - 13); Canvas.SetTop(marker, point.Y - 13); MarkerCanvas.Children.Add(marker); _learnedMarkers.Add(marker);
        }
    }
}
