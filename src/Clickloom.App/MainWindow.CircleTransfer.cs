using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;
using Microsoft.Win32;

namespace Clickloom.App;

public partial class MainWindow
{
    private const string CircleLayoutExtension = ".moushift-circle";
    private const string CircleLayoutFilter = "Moushift Circle layout (*.moushift-circle)|*.moushift-circle";

    private MappingProfile? SelectedCircleLayoutProfile()
    {
        if (_selectedDevice?.DeviceKey is not { } deviceKey) return null;
        return _settings.Profiles.FirstOrDefault(profile => profile.Id == _editingProfileId && profile.DeviceKey == deviceKey);
    }

    private void ApplyCircleTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCircleLayoutProfile() is not { } profile
            || CircleTemplateList.SelectedItem is not ComboBoxItem { Tag: string templateId, Content: string name }
            || !Enum.TryParse<CircleTemplate>(templateId, out var template)) return;

        var slots = CircleLayoutTransfer.CreateTemplate(template);
        if (ConfirmCircleReplacement(profile, $"Apply the {name} template?", slots, null, "Apply template"))
            ReplaceCircleLayout(profile.Id, slots, name + " template applied.");
    }

    private void ExportCircleLayout_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCircleLayoutProfile() is not { } profile) return;

        try
        {
            var layout = CircleLayoutTransfer.Export(profile.RingSlots);
            if (layout.OmittedActions > 0)
            {
                var confirm = new ConfirmActionWindow("Export Circle", "Export this layout for sharing?",
                    OmittedCircleActionsMessage(layout.OmittedActions)
                    + "\n\nThose positions will be empty in the exported file. The saved Circle keeps its actions.",
                    "Continue") { Owner = this };
                if (confirm.ShowDialog() != true) return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export saved Circle layout", Filter = CircleLayoutFilter, DefaultExt = CircleLayoutExtension,
                FileName = "Circle", OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;

            var directory = Path.GetDirectoryName(dialog.FileName)!;
            var temporary = Path.Combine(directory, $".moushift-{Guid.NewGuid():N}.tmp");
            bool ownsTemporary = false;
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    output.Write(layout.Content);
                }
                File.Move(temporary, dialog.FileName, overwrite: true);
                ownsTemporary = false;
            }
            finally
            {
                if (ownsTemporary) File.Delete(temporary);
            }
            StatusText.Text = "Circle layout exported.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowCircleLayoutError("Could not export Circle", error.Message);
        }
    }

    private void ImportCircleLayout_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCircleLayoutProfile() is not { } profile) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import Circle layout", Filter = CircleLayoutFilter, DefaultExt = CircleLayoutExtension,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        PortableCircleLayout layout;
        try
        {
            using var input = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            layout = CircleLayoutTransfer.Read(input);
        }
        catch (JsonException)
        {
            ShowCircleLayoutError("Could not import Circle", "The selected file is not a supported Circle layout.");
            return;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowCircleLayoutError("Could not import Circle", error.Message);
            return;
        }

        string? note = layout.OmittedActions == 0 ? null
            : OmittedCircleActionsMessage(layout.OmittedActions) + " Those positions will be empty.";
        if (ConfirmCircleReplacement(profile, "Import this Circle layout?", layout.Slots, note, "Import layout"))
            ReplaceCircleLayout(profile.Id, layout.Slots, "Circle layout imported.");
    }

    private bool ConfirmCircleReplacement(MappingProfile profile, string heading, IReadOnlyList<RingSlot> slots, string? note, string accept)
    {
        string deviceName = profile.DeviceName ?? "Selected mouse";
        string profileName = profile.ApplicationPath is null ? "All applications" : profile.Name;
        string explanation = $"{deviceName} · {profileName}\n\nThis replaces the saved Circle layout for this profile only.";
        string positions = string.Join(" · ", slots.Select(slot => slot.Kind switch
        {
            RingSlotKind.Folder => $"{slot.Name} ({slot.Children.Count} positions)",
            RingSlotKind.Command when slot.Action.Kind == ActionKind.Disabled => "Empty",
            _ => slot.Name
        }));
        explanation += "\n\n" + positions;
        if (note is not null) explanation += "\n\n" + note;
        var dialog = new ConfirmActionWindow("Replace Circle layout", heading, explanation, accept) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private void ReplaceCircleLayout(Guid profileId, List<RingSlot> slots, string status)
    {
        // A modal file picker or confirmation can dispatch device-change notifications.
        if (SelectedCircleLayoutProfile()?.Id != profileId)
        {
            ShowCircleLayoutError("Could not replace Circle", "The selected mouse or profile changed. Select it and try again.");
            return;
        }

        var next = _settings.DeepClone();
        next.Profiles.Single(profile => profile.Id == profileId).RingSlots = slots;
        CommitSettings(next, status);
    }

    private static string OmittedCircleActionsMessage(int count) => count == 1
        ? "One action is outside the shareable Circle action set."
        : $"{count} actions are outside the shareable Circle action set.";

    private void ShowCircleLayoutError(string title, string message)
    {
        StatusText.Text = title;
        System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
