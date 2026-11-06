using System.IO;
using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    internal void PrepareDesignButtonEditor()
    {
        RefreshBindings(MouseControl.XButton1); OpenEditor();
        ActionCombo.SelectedValue = ActionKind.Shortcut;
        ShortcutChoice.SetShortcut(9, KeyModifiers.Alt);
    }

    internal void VerifyDesignWorkspace()
    {
        if (!_previewOnly || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR")))
            throw new InvalidOperationException("Design checks require isolated data.");
        string original = ThemeManager.Current;
        var gallery = DeviceCardsList.RenderSize;
        var a = _deviceCards[0]; var b = _deviceCards[1];
        DeviceCard_Click(new Button { Tag = b }, new RoutedEventArgs());
        if (DeviceHeading.Text != b.Name || EditingProfile.DeviceKey != b.DeviceKey || DevicesPage.Visibility != Visibility.Visible || MappingPage.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Selection did not retain the gallery beside this mouse's inspector.");
        DeviceCard_Click(new Button { Tag = a }, new RoutedEventArgs());
        // Both fixture profiles now exist. Selecting / browsing / theming must be read-only.
        byte[] before = File.ReadAllBytes(_store.FilePath);
        Guid id = EditingProfile.Id;
        CircleTab_Click(this, new RoutedEventArgs());
        if (ButtonsContent.Visibility != Visibility.Collapsed || CircleSummary.Visibility != Visibility.Visible || EditingProfile.Id != id)
            throw new InvalidOperationException("Circle tab changed the selected profile.");
        MappingsNav_Click(this, new RoutedEventArgs());
        foreach (string theme in new[] { "light", "dark" })
        {
            ThemeCombo.SelectedValue = theme;
            if (((System.Windows.Media.SolidColorBrush)ThemeManager.Brush("Accent")).CanFreeze)
                throw new InvalidOperationException("Theme brushes may freeze and strand dialog colors.");
            ThemeManager.Apply(theme, true); ThemeManager.Initialize(null);
            if (ThemeManager.Current != theme) throw new InvalidOperationException("Theme did not survive reload.");
            ThemeCombo.SelectedValue = theme;
        }
        DeviceCard_Click(new Button { Tag = b }, new RoutedEventArgs());
        DeviceCard_Click(new Button { Tag = a }, new RoutedEventArgs());
        if (!before.SequenceEqual(File.ReadAllBytes(_store.FilePath))) throw new InvalidOperationException("Browsing or appearance rewrote profile data.");
        RefreshBindings(MouseControl.XButton1); OpenEditor(); UpdateLayout();
        var size = HeroMouse.RenderSize;
        ActionCombo.SelectedValue = ActionKind.Shortcut; UpdateLayout();
        if (HeroMouse.RenderSize != size || DeviceCardsList.RenderSize != gallery)
            throw new InvalidOperationException("An action form resized the mouse gallery.");
        CloseEditor(); ThemeCombo.SelectedValue = original; ThemeManager.Apply(original);
        if (MappingPage.ActualWidth < 300 || DevicesPage.ActualWidth < 400 || InspectorCircle.Children.Count < 9)
            throw new InvalidOperationException("Inspector, gallery or Circle preview lost its usable layout.");
        Console.WriteLine("PASS WPF design: persistent gallery + selected-device inspector, tabs, live light/dark reload, profile-byte preservation, stable artwork.");
    }
}
