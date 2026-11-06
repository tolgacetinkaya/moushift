using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Clickloom.App;

public partial class MainWindow
{
    private WorkspacePreferences _workspacePreferences = new();
    private RingEditorWindow? _circleEditor;
    private bool _circleReturnToDetail;

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _workspacePreferences = _workspacePreferences with { SidebarCollapsed = !_workspacePreferences.SidebarCollapsed };
        ApplyWorkspaceSizing(); SaveWorkspacePreferences();
    }

    private void SaveWorkspacePreferences()
    {
        if (_previewOnly) return;
        try { _workspacePreferences.Save(Path.GetDirectoryName(_store.FilePath)!); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { StatusText.Text = "Could not save workspace layout: " + error.Message; }
    }

    private void InspectorSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled) { ApplyWorkspaceSizing(); return; }
        RememberInspectorWidth(InspectorColumn.ActualWidth);
    }

    private void RememberInspectorWidth(double width)
    {
        _workspacePreferences = _workspacePreferences with { InspectorWidth = Math.Clamp(width, 320, 640) };
        ApplyWorkspaceSizing(); SaveWorkspacePreferences();
    }

    private void ShowButtonPositions()
    {
        if (_selectedDevice?.DeviceKey is null) return;
        DevicesPage.Visibility = Visibility.Collapsed;
        DeviceDetailPage.Visibility = Visibility.Visible;
        DetailHeading.Text = _selectedDevice.Name;
        CloseEditor(); ApplyWorkspaceSizing(); UpdateLayout(); PlaceMarkers();
    }

    private void BackToGallery_Click(object sender, RoutedEventArgs e)
    {
        ShowPage("devices");
    }

    private void OpenCircleWorkspace()
    {
        if (_circleEditor is not null || EditingProfile.DeviceKey is null) return;
        _circleReturnToDetail = DeviceDetailPage.Visibility == Visibility.Visible;
        _circleEditor = new RingEditorWindow(_settings, _editingProfileId, _service, _selectedDevice?.Name ?? EditingProfile.DeviceName);
        CircleWorkspaceHost.Content = _circleEditor.HostIn(this, SaveCircleWorkspace, () => TryCloseCircleEditor(),
            InspectorColumn.Width.Value, RememberInspectorWidth);
        CircleWorkspaceHost.Visibility = Visibility.Visible;
        DevicesWorkspace.Visibility = Visibility.Collapsed;
    }

    private void SaveCircleWorkspace()
    {
        if (_circleEditor is null) return;
        var next = _settings.DeepClone(); _circleEditor.ApplyTo(next);
        if (!CommitSettings(next, "Click Circle saved for this mouse.")) return;
        _editingProfileId = _circleEditor.EditingProfileId;
        ReleaseCircleWorkspace(); PopulateProfiles(); RefreshBindings();
    }

    private bool TryCloseCircleEditor()
    {
        if (_circleEditor is null) return true;
        if (!_circleEditor.CanDiscardHosted()) return false;
        ReleaseCircleWorkspace(); return true;
    }

    private void ReleaseCircleWorkspace()
    {
        _circleEditor?.ReleaseHosted(); _circleEditor = null;
        CircleWorkspaceHost.Content = null; CircleWorkspaceHost.Visibility = Visibility.Collapsed;
        DevicesWorkspace.Visibility = Visibility.Visible;
        DevicesPage.Visibility = _circleReturnToDetail ? Visibility.Collapsed : Visibility.Visible;
        DeviceDetailPage.Visibility = _circleReturnToDetail ? Visibility.Visible : Visibility.Collapsed;
        ButtonsContent.Visibility = Visibility.Visible; CloseEditor(); SetInspectorTab(false); ApplyWorkspaceSizing();
    }
}
