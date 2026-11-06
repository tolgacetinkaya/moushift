using System.Windows;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    private void SetupNav_Click(object sender, RoutedEventArgs e) => ShowPage("setup");
    private void SetupGuide_Click(object sender, RoutedEventArgs e) => SetupGuideExpander.IsExpanded = true;
    private void SelectInputLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose an installed x64 input library", Filter = "Interception library (interception.dll)|interception.dll", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { DeviceSupportPreferences.Save(System.IO.Path.GetDirectoryName(_store.FilePath)!, dialog.FileName); StatusText.Text = "Input library saved. Quit and reopen Moushift to connect it."; }
        catch (Exception error) { StatusText.Text = error.Message; }
    }
    private void UpdateSetupStatus()
    {
        bool ready = _service?.HasResolvedDeviceRoutes == true;
        bool capturing = _service?.HasDeviceRouting == true;
        bool driverless = _service?.HasDriverlessCircleSources == true;
        SetupStatusTitle.Text = ready ? "Your mice have separate input sources" : driverless ? "Click Circle is ready without a driver" : "Waiting for a mouse connection";
        SetupStatusDescription.Text = ready ? "Profiles are isolated by mouse. A mouse with no profile keeps its normal buttons."
            : driverless ? "Each connected mouse opens its own Circle from a standard button assigned to Click Circle. Enable Circle, switch to another application and press that mouse's button. Original clicks still reach Windows. Other button actions and custom HID assignment drafts remain inactive without exclusive input support."
            : capturing ? "Input support is running, but no mouse has a verified input source. Assignments stay off until a source is matched. Your profiles are saved."
            : "Connect a mouse and wait for Windows input observation. Driverless Click Circle needs no library or driver installation. Exclusive button remapping is a separate optional feature.";
        SetupBackendState.Text = ready ? "Exclusive input ready" : driverless ? "Driverless Circle ready" : capturing ? "No verified source" : "Waiting for mouse";
        SetupConnectionRows.ItemsSource = _deviceCards;
    }
    private void ImportPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (EditingProfile.DeviceKey is null) return;
        var previous = _settings.Profiles.FirstOrDefault(p => p.DeviceKey is null && p.ApplicationPath == EditingProfile.ApplicationPath)
            ?? _settings.Profiles.First(p => p.DeviceKey is null && p.ApplicationPath is null);
        var dialog = new ConfirmActionWindow("Import previous assignments", "Use your previous assignments?",
            "Copy the previous mouse actions and Click Circle layout to " + (_selectedDevice?.Name ?? EditingProfile.DeviceName) + "?\n\nOnly the selected profile will change. Other mice keep their own settings.", "Import") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var next = _settings.DeepClone(); var target = next.Profiles.Single(p => p.Id == _editingProfileId);
        target.Bindings = previous.Bindings.Select(b => b.DeepClone()).ToList();
        target.RingSlots = previous.RingSlots.Select(s => s.DeepClone()).ToList();
        target.AuxiliaryKeys.Clear();
        if (CommitSettings(next, "Previous assignments imported for " + _selectedDevice?.Name)) RefreshBindings();
    }
    internal void VerifyDeviceProfileEditor()
    {
        if (!_previewOnly || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR"))) throw new InvalidOperationException("Device editor checks require isolated data.");
        var a = new DeviceCard("test:mouse-a", "Test mouse A", "TEST", "Fixture", ThemeManager.Brush("Accent"), "test:mouse-a");
        var b = new DeviceCard("test:mouse-b", "Test mouse B", "TEST", "Fixture", ThemeManager.Brush("Accent"), "test:mouse-b");
        var c = new DeviceCard("test:mouse-c", "Test mouse C", "TEST", "Fixture", ThemeManager.Brush("Accent"), "test:mouse-c");
        _deviceCards.Clear(); _deviceCards.Add(a); _deviceCards.Add(b); _deviceCards.Add(c);
        ShowPage("devices");
        DeviceCardsList.Measure(new Size(980, double.PositiveInfinity));
        DeviceCardsList.Arrange(new Rect(0, 0, 980, DeviceCardsList.DesiredSize.Height)); DeviceCardsList.UpdateLayout();
        System.Windows.Controls.Button Tile(DeviceCard device)
        {
            var presenter = DeviceCardsList.ItemContainerGenerator.ContainerFromItem(device) as System.Windows.Controls.ContentPresenter
                ?? throw new InvalidOperationException("A gallery card was not generated.");
            var tile = (System.Windows.Controls.Button)DeviceCardsList.ItemTemplate.FindName("DeviceTileButton", presenter);
            var name = (System.Windows.Controls.TextBlock)DeviceCardsList.ItemTemplate.FindName("DeviceTileName", presenter);
            var drawing = (MouseIllustration)DeviceCardsList.ItemTemplate.FindName("DeviceTileArtwork", presenter);
            if (!ReferenceEquals(tile.Tag, device) || name.Text != device.Name || drawing.ActualHeight < 180
                || name.TranslatePoint(new Point(0, name.ActualHeight), tile).Y >= drawing.TranslatePoint(new Point(), tile).Y)
                throw new InvalidOperationException("The gallery lost its device identity, title-above-artwork layout or original drawing.");
            return tile;
        }
        var firstCard = Tile(a); var secondCard = Tile(b); var thirdCard = Tile(c);
        var firstPosition = firstCard.TranslatePoint(new Point(), DeviceCardsList);
        var secondPosition = secondCard.TranslatePoint(new Point(), DeviceCardsList);
        var thirdPosition = thirdCard.TranslatePoint(new Point(), DeviceCardsList);
        if (Math.Abs(firstPosition.Y - thirdPosition.Y) > 1 || secondPosition.X <= firstPosition.X || thirdPosition.X <= secondPosition.X)
            throw new InvalidOperationException("Three device cards are not side by side.");
        firstCard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        BindingsList.SelectedItem = _bindings.Single(row => row.Mouse == MouseControl.MiddleButton);
        ActionCombo.SelectedValue = ActionKind.Ring; SaveBinding_Click(this, new RoutedEventArgs());
        BindingsList.SelectedItem = _bindings.Single(row => row.Mouse == MouseControl.XButton1);
        ActionCombo.SelectedValue = ActionKind.Shortcut; _draftKeyCode = 9; _draftModifiers = KeyModifiers.Alt; UpdateShortcutDisplay(); SaveBinding_Click(this, new RoutedEventArgs());
        Guid first = EditingProfile.Id;
        ShowPage("devices"); secondCard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (EditingProfile.Id == first || EditingProfile.Bindings.Any(x => x.Action.Kind != ActionKind.Passthrough)) throw new InvalidOperationException("Mouse B inherited mouse A assignments.");
        ShowPage("devices"); firstCard.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (EditingProfile.Id != first || EditingProfile.Bindings.Single(x => x.Input == MouseControl.MiddleButton).Action.Kind != ActionKind.Ring) throw new InvalidOperationException("Mouse A did not retain its separate assignment.");
        var shortcut = EditingProfile.Bindings.Single(x => x.Input == MouseControl.XButton1).Action;
        if (shortcut.Kind != ActionKind.Shortcut || shortcut.KeyCode != 9 || shortcut.Modifiers != KeyModifiers.Alt) throw new InvalidOperationException("Device shortcut lost its modifiers or appeared on another mouse.");
        if (_store.Load().Settings.Profiles.Single(p => p.DeviceKey == b.Key).Bindings.Any(x => x.Action.Kind != ActionKind.Passthrough)) throw new InvalidOperationException("Storage leaked assignments across mice.");
        if (DeviceHeading.Text != a.Name) throw new InvalidOperationException("A card opened another device's settings heading.");
        VerifyProfileScreens();
        var fixtures = new[] {
            new Clickloom.Windows.MouseConnection(a.Key, a.Name, "TEST", "USB", true, false, ["fixture:a"]),
            new Clickloom.Windows.MouseConnection(b.Key, b.Name, "TEST", "USB", false, false, ["fixture:b"]),
            new Clickloom.Windows.MouseConnection(c.Key, c.Name, "TEST", "USB", false, false, ["fixture:c"]) };
        string settingsBefore = System.Text.Json.JsonSerializer.Serialize(_settings);
        _lastDeviceInput[a.Key] = DateTime.UtcNow;
        ApplyConnectionInventory([fixtures[1]]);
        if (_deviceCards.Count != 1 || _deviceCards[0].Key != b.Key || _lastDeviceInput.ContainsKey(a.Key))
            throw new InvalidOperationException("Disconnected cards or old input activity remained in the gallery/sidebar.");
        ApplyConnectionInventory([]);
        if (_deviceCards.Count != 0 || EmptyDevicesBox.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Disconnected mice remained visible or the empty state was missing.");
        if (System.Text.Json.JsonSerializer.Serialize(_settings) != settingsBefore || _store.Load().Settings.Profiles.Single(p => p.Id == first).Bindings.Single(x => x.Input == MouseControl.MiddleButton).Action.Kind != ActionKind.Ring)
            throw new InvalidOperationException("Connection refresh changed a saved mouse assignment.");
        ApplyConnectionInventory(fixtures);
        if (_deviceCards.Count != 3 || !_deviceCards.Single(d => d.Key == a.Key).Detail.Contains("unconfirmed") || _deviceCards.Any(d => d.Activity == "Input just now"))
            throw new InvalidOperationException("A connected receiver was hidden or reconnect reused old activity.");
        CloseEditor(); Console.WriteLine("PASS isolated WPF gallery: three cards, scoped profiles, disconnect/reconnect, receiver status and preserved assignments; no windows or input.");
    }
}
