using System.Windows;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    private void ResetProfileButtons_Click(object sender, RoutedEventArgs e)
    {
        var profile = EditingProfile;
        var dialog = new ResetButtonsWindow(profile.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var proposed = ProfileDefaults.ResetButtons(_settings, profile.Id);
        if (!CommitSettings(proposed, "Default buttons restored · " + profile.Name + " · Assignments paused")) return;
        _service?.SetEnabled(false); _service?.DismissRing(); CloseRingVisual();
        RefreshBindings(); CloseEditor(); UpdateEngineState();
    }
}
