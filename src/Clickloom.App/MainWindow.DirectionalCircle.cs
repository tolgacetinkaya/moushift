using System.Windows;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    private void DirectionalCircle_Click(object sender, RoutedEventArgs e)
    {
        var profile = EditingProfile;
        if (profile.DeviceKey is null) return;
        bool assigned = ProfileResolver.ResolveDeviceBinding(_settings, profile, MouseControl.MiddleButton).Kind == ActionKind.Ring;
        var editor = new DirectionalCircleWindow(profile, assigned, _service) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result == profile.DirectionalCircle) return;

        var next = _settings.DeepClone();
        next.Profiles.Single(p => p.Id == profile.Id).DirectionalCircle = editor.Result;
        CommitSettings(next, editor.Result is null ? "Quick gestures turned off." : "Quick gestures saved for " + profile.Name + ".");
    }
}
