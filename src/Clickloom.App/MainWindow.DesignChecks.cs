using System.IO;
using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    internal void AddThirdDesignDevice()
    {
        _deviceCards.Add(new("preview:razer", "Razer Mouse Dock Pro", "Razer", "Receiver connected", ThemeManager.Brush("Accent"), "preview:razer"));
        ApplyWorkspaceSizing();
    }

    internal void PrepareWorkspaceCapture(string[] args)
    {
        if (args.Contains("--collapsed-nav")) ToggleSidebar_Click(this, new RoutedEventArgs());
        if (args.Contains("--edit-positions")) ShowButtonPositions();
        if (args.Contains("--edit-positions") && args.Contains("--edit-button")) PrepareDesignButtonEditor();
        if (args.Contains("--edit-circle")) OpenCircleWorkspace();
        if (args.Contains("--teach-button")) PrepareLearningWorkspaceFixture();
        if (args.Contains("--left-side"))
        {
            LeftViewButton.IsChecked = true;
            _learningEditor?.SelectView(MouseDrawingView.Left);
        }
        UpdateLayout(); PlaceMarkers();
        if (_circleEditor is not null && args.Contains("--folder")) _circleEditor.ShowFolderPreview();
        if (_circleEditor is not null && args.Contains("--drag-preview")) { UpdateLayout(); _circleEditor.ShowDragPresentationPreview(); }
        if (args.Contains("--three-devices")) BackToGallery_Click(this, new RoutedEventArgs());
    }

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
        var a = _deviceCards[0]; var b = _deviceCards[1];
        BackToGallery_Click(this, new RoutedEventArgs()); UpdateLayout();
        if (_selectedDevice is not null || DeviceInspector.Visibility != Visibility.Collapsed || InspectorSplitter.Visibility != Visibility.Collapsed
            || InspectorColumn.ActualWidth != 0 || InspectorSplitterColumn.ActualWidth != 0 || DevicesPage.Visibility != Visibility.Visible)
            throw new InvalidOperationException("The gallery reserved a settings panel without a selected mouse.");
        DeviceCard_Click(new Button { Tag = b }, new RoutedEventArgs());
        UpdateLayout();
        if (DeviceHeading.Text != b.Name || DetailHeading.Text != b.Name || EditingProfile.DeviceKey != b.DeviceKey || DevicesPage.Visibility != Visibility.Collapsed
            || DeviceDetailPage.Visibility != Visibility.Visible || MappingPage.Visibility != Visibility.Visible || DeviceInspector.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A card did not open only its mouse in the center with its settings on the right.");
        if (!ReferenceEquals(DetailBatteryInfo.Content, b) || b.BatteryLabel != "60%" || a.BatteryVisibility != Visibility.Collapsed || b.BatteryVisibility != Visibility.Visible)
            throw new InvalidOperationException("Battery information did not follow the selected device or invented a missing percentage.");
        b.BatteryPercent = null;
        if (b.BatteryVisibility != Visibility.Collapsed || b.BatteryLabel.Length != 0) throw new InvalidOperationException("Missing battery left a badge or placeholder.");
        b.BatteryPercent = 0;
        if (b.BatteryVisibility != Visibility.Visible || b.BatteryLabel != "0%") throw new InvalidOperationException("An empty battery was hidden as unknown.");
        b.BatteryPercent = 60;
        LeftViewButton.IsChecked = true; UpdateLayout(); PlaceMarkers();
        if (HeroMouse.View != MouseDrawingView.Left || RightCallout.Visibility != Visibility.Collapsed || BackCallout.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Side elevation did not show the accessible buttons.");
        var sideAnchor = HeroMouse.NormalizedAnchor(.26, .61);
        if (!HeroMouse.TryNormalize(sideAnchor, out double sx, out double sy) || Math.Abs(sx - .26) > .0001 || Math.Abs(sy - .61) > .0001)
            throw new InvalidOperationException("Side-view position transforms changed the saved coordinate.");
        TopViewButton.IsChecked = true;
        DeviceCard_Click(new Button { Tag = a }, new RoutedEventArgs());
        // Both fixture profiles now exist. Selecting / browsing / theming must be read-only.
        byte[] before = File.ReadAllBytes(_store.FilePath);
        Guid id = EditingProfile.Id;
        CircleTab_Click(this, new RoutedEventArgs()); UpdateLayout();
        if (CircleWorkspaceHost.Visibility != Visibility.Visible || DevicesWorkspace.Visibility != Visibility.Collapsed || EditingProfile.Id != id || _circleEditor is null)
            throw new InvalidOperationException("Circle did not open in the central workspace for the selected mouse.");
        _circleEditor.VerifyHostedSurface();
        if (!TryCloseCircleEditor()) throw new InvalidOperationException("Unchanged Circle could not close.");
        PrepareLearningWorkspaceFixture(); UpdateLayout();
        if (LearningWorkspaceHost.Visibility != Visibility.Visible || DevicesWorkspace.Visibility != Visibility.Collapsed
            || IdentifyButton.Style != FindResource("Primary") || _learningEditor is null)
            throw new InvalidOperationException("Teach a button did not open inside the main window from a visible button.");
        _learningEditor.VerifyHostedLearning(); CloseLearningWorkspace();
        MappingsNav_Click(this, new RoutedEventArgs());
        var preferences = _workspacePreferences;
        ToggleSidebar_Click(this, new RoutedEventArgs()); UpdateLayout();
        if (SidebarColumn.Width.Value != 68 || (Visibility)Resources["SidebarLabelVisibility"] != Visibility.Collapsed)
            throw new InvalidOperationException("The sidebar did not collapse to an icon rail.");
        ToggleSidebar_Click(this, new RoutedEventArgs());
        RememberInspectorWidth(490); UpdateLayout();
        ApplyWorkspaceSizing();
        if (InspectorColumn.Width.Value != 490) throw new InvalidOperationException("Layout refresh reset the dragged inspector width.");
        var folder = Path.Combine(Path.GetDirectoryName(_store.FilePath)!, "layout-fixture");
        _workspacePreferences.Save(folder);
        if (WorkspacePreferences.Load(folder) != _workspacePreferences) throw new InvalidOperationException("Workspace layout did not survive reload.");
        _workspacePreferences = preferences; ApplyWorkspaceSizing();
        ShowButtonPositions(); UpdateLayout(); PlaceMarkers();
        if (DeviceDetailPage.Visibility != Visibility.Visible || DevicesPage.Visibility != Visibility.Collapsed || DiagramStage.ActualWidth < 300)
            throw new InvalidOperationException("Button positions did not open in the center.");
        BackToGallery_Click(this, new RoutedEventArgs());
        UpdateLayout();
        if (_selectedDevice is not null || DeviceInspector.Visibility != Visibility.Collapsed || InspectorColumn.ActualWidth != 0)
            throw new InvalidOperationException("Returning to All mice retained the selected mouse inspector.");
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
        if (HeroMouse.RenderSize != size || DevicesPage.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("An action form resized the selected mouse or reopened the gallery.");
        CloseEditor(); ThemeCombo.SelectedValue = original; ThemeManager.Apply(original);
        if (MappingPage.ActualWidth < 300 || DeviceDetailPage.ActualWidth < 340 || CloseBindingButton.Style == FindResource("Quiet"))
            throw new InvalidOperationException("Inspector, gallery or explicit Close button lost its usable layout.");
        Console.WriteLine("PASS WPF design: gallery without inspector, single-click selected mouse detail, back navigation, resizable inspector, embedded Circle, scoped profiles and stable artwork.");
    }
}
