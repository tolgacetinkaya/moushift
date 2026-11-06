using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    private bool _loadingEditor = true;

    private void EditorField_Changed(object sender, RoutedEventArgs e) => MarkEditorChanged();
    private void MarkEditorChanged()
    {
        if (_loadingEditor || _loading || !IsInitialized || DraftStatusText is null) return;
        DraftStatusText.Text = "Unsaved changes";
        DraftStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
    }
    private void MarkEditorSaved()
    {
        DraftStatusText.Text = "No changes";
        DraftStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
    }
    private void UpdateProfileManager()
    {
        ProfileManagerList.ItemsSource = _settings.Profiles.Where(p => p.DeviceKey is not null).Select(profile => new ProfileSummary(
            profile.Id, profile.Name,
            profile.ApplicationPath is null ? "All applications" : System.IO.Path.GetFileName(profile.ApplicationPath),
            (profile.DeviceName ?? _connections.FirstOrDefault(c => c.Key == profile.DeviceKey)?.Name ?? "Disconnected mouse") + " · "
                + (profile.Bindings.Count(binding => binding.Action.Kind != ActionKind.Passthrough) + profile.AuxiliaryKeys.Count) + " assigned buttons")).ToArray();
    }
    private void ProfilesNav_Click(object sender, RoutedEventArgs e) { UpdateProfileManager(); ShowPage("profiles"); }
    private void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var profile = _settings.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null || profile.DeviceKey is null) { ShowPage("devices"); return; }
        _editingSavedProfile = true;
        _selectedDevice = profile.DeviceKey is null ? GlobalDeviceCard()
            : new(profile.DeviceKey, profile.DeviceName ?? _connections.FirstOrDefault(c => c.Key == profile.DeviceKey)?.Name ?? "Saved mouse", "PROFILE", "Saved profile", (System.Windows.Media.Brush)FindResource("Accent"), profile.DeviceKey);
        _loading = true; _deviceScope = profile.DeviceKey is not null; DeviceScopeCheck.IsChecked = _deviceScope;
        ShowAuxiliaryCheck.IsEnabled = !_deviceScope;
        if (_deviceScope) ShowAuxiliaryCheck.IsChecked = false;
        _loading = false;
        _editingProfileId = profile.Id;
        foreach (var row in _deviceCards) row.IsSelected = row.DeviceKey == profile.DeviceKey;
        ButtonsContent.Visibility = Visibility.Visible;
        PopulateProfiles(); RefreshBindings(); CloseEditor(); ShowPage("mappings");
    }
    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var profile = _settings.Profiles.Single(p => p.Id == id);
        var dialog = new ProfileNameWindow(profile.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ApplyProfileName(id, dialog.ProfileName);
    }
    private bool ApplyProfileName(Guid id, string name)
    {
        var proposed = _settings.DeepClone(); proposed.Profiles.Single(p => p.Id == id).Name = name.Trim();
        if (!CommitSettings(proposed, "Profile name updated.")) return false;
        PopulateProfiles(); UpdateProfileContext(); UpdateProfileManager(); return true;
    }
    private void VerifyProfileScreens()
    {
        var shared = _settings.Profiles.First(p => p.DeviceKey == EditingProfile.DeviceKey && p.ApplicationPath is null);
        var proposed = _settings.DeepClone();
        string target = Environment.ProcessPath ?? @"C:\Windows\System32\notepad.exe";
        var profile = proposed.Profiles.FirstOrDefault(p => p.DeviceKey == shared.DeviceKey && p.ApplicationPath == target);
        bool added = profile is null;
        if (profile is null)
        {
            profile = shared.DeepClone(); profile.Id = Guid.NewGuid(); profile.Name = "Test application"; profile.ApplicationPath = target;
            proposed.Profiles.Add(profile);
        }
        if (!CommitSettings(proposed, "UI test profili")) throw new InvalidOperationException("Profile fixture save failed.");
        ProfilesNav_Click(this, new RoutedEventArgs());
        if (ProfilesPage.Visibility != Visibility.Visible || MappingPage.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Profile navigation did not open its own screen.");
        OpenProfile_Click(new Button { Tag = profile.Id }, new RoutedEventArgs());
        if (EditingProfile.Id != profile.Id || MappingPage.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Profile manager opened a different profile.");
        string oldName = profile.Name;
        if (!ApplyProfileName(profile.Id, "Rename test")) throw new InvalidOperationException("Profile rename failed.");
        var saved = _store.Load().Settings.Profiles.Single(p => p.Id == profile.Id);
        if (saved.Name != "Rename test" || saved.ApplicationPath != target || saved.DeviceKey != shared.DeviceKey)
            throw new InvalidOperationException("Profile rename changed its identity or routing target.");
        OpenProfile_Click(new Button { Tag = shared.Id }, new RoutedEventArgs());
        var restore = _settings.DeepClone();
        if (added) restore.Profiles.RemoveAll(p => p.Id == profile.Id);
        else restore.Profiles.Single(p => p.Id == profile.Id).Name = oldName;
        if (!CommitSettings(restore, "Ready")) throw new InvalidOperationException("UI profile cleanup failed.");
        PopulateProfiles(); RefreshBindings(MouseControl.XButton1); OpenEditor();
    }
    private sealed record ProfileSummary(Guid Id, string Name, string Target, string Scope);
}
