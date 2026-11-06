using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clickloom.Core;
using Clickloom.Windows;
using Microsoft.Win32;

namespace Clickloom.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProfileRow> _profiles = [];
    private readonly ObservableCollection<BindingRow> _bindings = [];
    private readonly ObservableCollection<DeviceCard> _deviceCards = [];
    private DeviceCard? _selectedDevice;
    private bool _editingSavedProfile;
    private bool _deviceScope;
    private RingWindow? _ringWindow;
    private bool _ringPreview;
    private AppSettings _settings = AppSettings.CreateDefault();
    private readonly SettingsStore _store;
    private MouseRemappingService? _service;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _trayToggle;
    private System.Drawing.Icon? _trayIcon;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;
    private bool _loading = true;
    private bool _exiting;
    private bool _disposed;
    private Guid _editingProfileId;
    private int _draftKeyCode;
    private KeyModifiers _draftModifiers;
    private DateTime _lastInputShown;
    private DateTime _lastErrorShown;
    private readonly bool _previewOnly;
    private int _presencePolling;
    private int _presenceTicks;

    private static readonly (MouseControl Control, string Name)[] MouseNames =
        InputPresentation.MouseInputs.Select(input => (input.Control, input.Name)).ToArray();

    public MainWindow(bool previewOnly = false)
    {
        _previewOnly = previewOnly;
        InitializeComponent();
        Title = "Moushift · " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        AboutVersionText.Text = Title + " · Windows";
        ThemeCombo.ItemsSource = ThemeManager.Choices; ThemeCombo.SelectedValue = ThemeManager.Current;
        DiagramLinks.Configure(HeroMouse, (MouseControl.LeftButton, LeftCallout), (MouseControl.RightButton, RightCallout),
            (MouseControl.MiddleButton, MiddleCallout), (MouseControl.XButton1, BackCallout), (MouseControl.XButton2, ForwardCallout));
        DiagramPane.SizeChanged += (_, _) => DiagramLinks.InvalidateVisual();
        string dataRoot = Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickloom");
        _store = new SettingsStore(Path.Combine(dataRoot, "profiles.json"));
        _workspacePreferences = WorkspacePreferences.Load(dataRoot);
        var loaded = _store.Load();
        _settings = loaded.Settings;
        _editingProfileId = _settings.ActiveProfileId;
        ProfilesList.ItemsSource = _profiles;
        BindingsList.ItemsSource = _bindings;
        DeviceCardsList.ItemsSource = _deviceCards;
        Resources["DeviceTileWidth"] = 280d;
        Resources["DeviceTileHeight"] = 640d;
        ActionCombo.ItemsSource = new[]
        {
            new ActionChoice(ActionKind.Passthrough, "Default"),
            new ActionChoice(ActionKind.Shortcut, "Keyboard shortcut"),
            new ActionChoice(ActionKind.MouseButton, "Mouse button"),
            new ActionChoice(ActionKind.Text, "Type text"),
            new ActionChoice(ActionKind.LaunchApplication, "Open application"),
            new ActionChoice(ActionKind.Ring, "Click Circle"),
            new ActionChoice(ActionKind.Disabled, "No action")
        };
        MouseOutputCombo.ItemsSource = MouseNames.Take(5).Select(p => new MouseChoice(p.Control, p.Name)).ToArray();
        AutoSwitchCheck.IsChecked = _settings.AutoSwitchProfiles;
        PopulateProfiles();
        _loading = false;
        RefreshBindings(MouseControl.XButton1);
        if (!previewOnly) CreateTray();
        try
        {
            if (!previewOnly)
            {
            _service = new MouseRemappingService(DeviceSupportPreferences.Load(dataRoot));
            _service.ApplySettings(_settings);
            _service.StateChanged += Service_StateChanged;
            _service.ActionError += Service_ActionError;
            _service.InputObserved += (_, input) => ObserveInput(MouseName(input));
            _service.AuxiliaryInputObserved += (_, key) => ObserveInput($"Extra button · F{key - 0x7C + 13}");
            _service.RingRequested += (_, request) => Dispatcher.BeginInvoke(() =>
            { if (_service.IsCurrentRing(request.Id)) OpenRing(request.Slots, request); });
            _service.RingTriggerReleased += (_, id) => Dispatcher.BeginInvoke(() =>
            { if (_service.IsCurrentRing(id)) _ringWindow?.ReleaseTrigger(); });
            _service.RingClicked += (_, pointer) => Dispatcher.BeginInvoke(() =>
            { if (_service.IsCurrentRing(pointer.Id)) _ringWindow?.ClickAtScreen(pointer.X, pointer.Y); });
            _service.RingPressed += (_, pointer) => Dispatcher.BeginInvoke(() =>
            { if (_service.IsCurrentRing(pointer.Id)) _ringWindow?.PressAtScreen(pointer.X, pointer.Y); });
            _service.CirclePreferenceRequested += (_, change) => Dispatcher.BeginInvoke(() =>
            {
                try { if (CommitSettings(CirclePreferences.Apply(_settings, change.DeviceKey, change.Kind, change.ProfileId), "Circle preference updated for this mouse.")) RefreshBindings(); }
                catch (ArgumentException error) { StatusText.Text = error.Message; }
            });
            _service.RingScrolled += (_, scroll) => Dispatcher.BeginInvoke(() =>
            { if (_service.IsCurrentRing(scroll.Id)) _ringWindow?.ScrollAtCursor(scroll.Delta); });
            _service.RingDismissed += (_, _) => Dispatcher.BeginInvoke(CloseRingVisual);
            _service.Start();
            ShortcutChoice.CaptureService = _service;
            UpdateEngineState();
            }
            else { EnableButton.IsEnabled = true; EnableButton.Content = "Input setup"; EnableButton.Style = (Style)FindResource("Quiet"); EngineBadge.Text = "Input setup required"; }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not start input engine: " + ex.Message;
            EnableButton.IsEnabled = false;
        }
        if (loaded.Warning is not null) StatusText.Text = loaded.Warning;
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) =>
        {
            UpdateEngineState(); UpdateDeviceActivity(); if (++_presenceTicks % 2 == 0) RefreshDevices();
        };
        if (!previewOnly) { _statusTimer.Start(); RefreshDevices(); }
        ShowPage("devices"); CloseEditor();
        if (!previewOnly) SourceInitialized += (_, _) => StartDeviceActivity();
        Closing += Window_Closing;
        Closed += (_, _) => DisposeRuntime();
        Deactivated += (_, _) => { if (_ringPreview) CloseRingVisual(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _ringWindow is not null) { _service?.DismissRing(); CloseRingVisual(); e.Handled = true; } };
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                bool healthy = _service is { IsRunning: true, IsEnabled: false } && string.IsNullOrWhiteSpace(_service.LastError);
                if (healthy && !_service!.CanEnable)
                {
                    bool refused = false;
                    try { _service.SetEnabled(true); } catch (InvalidOperationException) { refused = true; }
                    healthy &= refused && !_service.IsEnabled;
                    if (!refused) _service.SetEnabled(false);
                }
                if (healthy) Console.WriteLine("PASS Windows startup: mouse hook, auxiliary hook and " + _service!.EmergencyShortcut + " ready; remapping disabled; device-only activation guard checked.");
                else Console.Error.WriteLine("FAIL Windows startup: " + (_service?.LastError ?? "Input service unavailable."));
                _exiting = true; DisposeRuntime(); Close(); System.Windows.Application.Current.Shutdown(healthy ? 0 : 1);
            };
            timer.Start();
        }
    }

    internal Size? PreviewRenderSize { get; set; }
    internal void RenderPreview(string outputPath, string? page, bool verifyEditor)
    {
        try
        {
            if (verifyEditor)
            {
                DeviceCard_Click(new System.Windows.Controls.Button { Tag = _deviceCards.FirstOrDefault(c => c.DeviceKey is not null) ?? new DeviceCard("test:editor", "Editor test mouse", "TEST", "Fixture", ThemeManager.Brush("Accent"), "test:editor") }, new RoutedEventArgs());
                if (MappingPage.Visibility != Visibility.Visible || DevicesPage.Visibility != Visibility.Visible || EditorPane.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Device selection did not retain the gallery beside its inspector with the action form closed.");
                if (BackCallout.Tag?.ToString() != "XButton1" || ForwardCallout.Tag?.ToString() != "XButton2"
                    || Grid.GetRow(BackCallout) <= Grid.GetRow(ForwardCallout)
                    || MouseName(MouseControl.XButton1) != "Back button" || MouseName(MouseControl.XButton2) != "Forward button")
                    throw new InvalidOperationException("Back/front side-button identity or physical diagram position is reversed.");
                MouseCallout_Click(BackCallout, new RoutedEventArgs());
                if (EditorPane.Visibility != Visibility.Visible || EditorColumn.Width.Value <= 0)
                    throw new InvalidOperationException("Mouse callout did not open its action panel.");
                // Exercise the actual editor/controller/storage with an isolated data root;
                // this does not install hooks, inject input, or change any device settings.
                BindingsList.SelectedItem = _bindings.Single(b => b.Mouse == MouseControl.XButton1);
                ActionCombo.SelectedValue = ActionKind.Shortcut;
                _draftKeyCode = 0x43; _draftModifiers = KeyModifiers.Control; UpdateShortcutDisplay();
                SaveBinding_Click(this, new RoutedEventArgs());
                var saved = _store.Load();
                var shortcut = saved.Settings.Profiles.Single(p => p.Id == _editingProfileId).Bindings.Single(b => b.Input == MouseControl.XButton1).Action;
                if (shortcut.Kind != ActionKind.Shortcut || shortcut.KeyCode != 0x43 || shortcut.Modifiers != KeyModifiers.Control)
                    throw new InvalidOperationException("Shortcut editor roundtrip failed.");
                ShortcutChoice.SetShortcut(9, KeyModifiers.Alt); ShortcutChoice_Changed(this, EventArgs.Empty);
                SaveBinding_Click(this, new RoutedEventArgs());
                shortcut = _store.Load().Settings.Profiles.Single(p => p.Id == _editingProfileId).Bindings.Single(b => b.Input == MouseControl.XButton1).Action;
                if (shortcut.KeyCode != 9 || shortcut.Modifiers != KeyModifiers.Alt) throw new InvalidOperationException("Manual Alt+Tab selection failed.");
                BindingsList.SelectedItem = _bindings.Single(b => b.Mouse == MouseControl.XButton2);
                ShortcutChoice.SetShortcut(9, KeyModifiers.Alt); ShortcutChoice_Changed(this, EventArgs.Empty);
                SaveBinding_Click(this, new RoutedEventArgs());
                var secondShortcut = _store.Load().Settings.Profiles.Single(p => p.Id == _editingProfileId).Bindings.Single(b => b.Input == MouseControl.XButton2).Action;
                if (secondShortcut.Kind != ActionKind.Shortcut || secondShortcut.KeyCode != 9 || secondShortcut.Modifiers != KeyModifiers.Alt)
                    throw new InvalidOperationException("A shortcut edit retained Passthrough or lost Alt.");
                ResetBinding_Click(this, new RoutedEventArgs());
                BindingsList.SelectedItem = _bindings.Single(b => b.Mouse == MouseControl.XButton1);
                if (BackCalloutAction.Text != "Alt + Tab" || ForwardCalloutAction.Text != "Default")
                    throw new InvalidOperationException("A saved back-button shortcut is shown on the forward button.");
                var ringEditor = new RingEditorWindow(EditingProfile.RingSlots); ringEditor.VerifyEditing(); ringEditor.Close();
                new RingWindow(EditingProfile.RingSlots, true).VerifyNoActivationAndSelection();
                ActionCombo.SelectedValue = ActionKind.Ring; SaveBinding_Click(this, new RoutedEventArgs());
                if (_store.Load().Settings.Profiles.Single(p => p.Id == _editingProfileId).Bindings.Single(b => b.Input == MouseControl.XButton1).Action.Kind != ActionKind.Ring)
                    throw new InvalidOperationException("Ring trigger persistence failed.");
                ActionCombo.SelectedValue = ActionKind.Shortcut;
                ShortcutChoice.SetShortcut(9, KeyModifiers.Alt); ShortcutChoice_Changed(this, EventArgs.Empty);
                SaveBinding_Click(this, new RoutedEventArgs());
                ShowAuxiliaryCheck.IsChecked = true;
                BindingsList.SelectedItem = _bindings.Single(b => b.VirtualKey == 0x7C);
                ActionCombo.SelectedValue = ActionKind.Text; TextActionBox.Text = "Moushift test ✓";
                SaveBinding_Click(this, new RoutedEventArgs());
                if (_store.Load().Settings.Profiles.Single(p => p.Id == _editingProfileId).AuxiliaryKeys.Single(b => b.VirtualKey == 0x7C).Action.Text != "Moushift test ✓")
                    throw new InvalidOperationException("Auxiliary editor roundtrip failed.");
                ResetBinding_Click(this, new RoutedEventArgs());
                if (_store.Load().Settings.Profiles.Single(p => p.Id == _editingProfileId).AuxiliaryKeys.Count != 0)
                    throw new InvalidOperationException("Auxiliary reset failed.");
                ShowAuxiliaryCheck.IsChecked = false;
                RefreshBindings(MouseControl.XButton1);
                VerifyProfileScreens();
                VerifyAppearance();
                StatusText.Text = "Ready";
                Console.WriteLine("PASS WPF editor: side-button labels/positions/assignment, profile navigation/rename, shortcuts, ring editing and persistence, text/reset; no hooks/input.");
            }
            if (page == "mapping-expanded")
            {
                ShowPage("mappings"); SourceExpander.IsExpanded = true;
                ShowAuxiliaryCheck.IsChecked = true; RefreshBindings(MouseControl.XButton1);
                ScopeExpander.IsExpanded = true; CloseEditor();
            }
            else if (page == "devices-sample")
            {
                ShowPage("devices"); _deviceCards.Clear();
                _deviceCards.Add(new("preview-only", "Razer mouse · sample device", "RAZER", "Design preview only", (Brush)FindResource("Accent")));
                EmptyDevicesBox.Visibility = Visibility.Collapsed;
                DevicesSubtitle.Text = "Design preview · Sample connection";
            }
            else if (page?.StartsWith("action-") == true || page == "shortcut-manual")
            {
                ShowPage("mappings"); RefreshBindings(MouseControl.XButton1); OpenEditor();
                ActionCombo.SelectedValue = page switch { "action-text" => ActionKind.Text, "action-application" => ActionKind.LaunchApplication, "action-mouse" => ActionKind.MouseButton, "action-ring" => ActionKind.Ring, _ => ActionKind.Shortcut };
                TextActionBox.Text = "Hello, have a great day."; ApplicationPathBox.Text = @"C:\Windows\System32\notepad.exe";
                if (page == "shortcut-manual") ShortcutChoice.ShowManualEditor();
            }
            else if (page == "theme-preview")
            {
                // In-memory visual fixture; no hooks or profile writes.
                EditingProfile.Bindings.Single(b => b.Input == MouseControl.XButton1).Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
                ShowPage("mappings"); RefreshBindings(MouseControl.XButton1); OpenEditor();
            }
            else if (page == "about-details") { ShowPage("about"); UpdateLayout(); AboutPage.ScrollToEnd(); }
            else if (!string.IsNullOrWhiteSpace(page) && page is not ("ring" or "ring-editor" or "ring-adjustment" or "ring-folder" or "profile-name")) { ShowPage(page); if (page == "devices") RefreshDevices(); }
            FrameworkElement? detachedSurface = null;
            if (PreviewRenderSize is { } renderSize)
            {
                // Preview only: Windows may cap the HWND to the current monitor's
                // track height. Arrange the real WPF surface at the requested size.
                var content = (FrameworkElement)Content;
                Content = null; UpdateLayout();
                if (VisualTreeHelper.GetParent(content) is ContentPresenter presenter) presenter.Content = null;
                var host = new Border { Width = renderSize.Width, Height = renderSize.Height, Child = content };
                detachedSurface = host;
                host.Measure(renderSize); host.Arrange(new Rect(renderSize)); host.UpdateLayout();
                if (Math.Abs(host.ActualWidth - renderSize.Width) > 1 || Math.Abs(host.ActualHeight - renderSize.Height) > 1)
                    throw new InvalidOperationException("Preview layout did not retain the requested size.");
            }
            UpdateLayout();
            Window? ringPreview = null;
            if (page == "ring") { ringPreview = new RingWindow(EditingProfile.RingSlots, true); ringPreview.Show(); ((RingWindow)ringPreview).PreviewSelection(0); ringPreview.UpdateLayout(); }
            else if (page is "ring-editor" or "ring-adjustment" or "ring-folder")
            {
                var ringEditor = new RingEditorWindow(EditingProfile.RingSlots, null, EditingProfile.Name);
                if (page == "ring-adjustment") ringEditor.ShowAdjustmentPreview();
                if (page == "ring-folder") ringEditor.ShowFolderPreview();
                ringPreview = ringEditor; ringPreview.Show(); ringPreview.UpdateLayout();
            }
            else if (page == "profile-name") { ringPreview = new ProfileNameWindow(EditingProfile.Name); ringPreview.Show(); ringPreview.UpdateLayout(); }
            var surface = ringPreview?.Content as FrameworkElement ?? detachedSurface ?? Content as FrameworkElement
                ?? throw new InvalidOperationException("Preview surface is unavailable.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            if (page == "ring")
            {
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    var rectangle = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
                    drawing.DrawRectangle((Brush)FindResource("Bg"), null, rectangle);
                    drawing.DrawRectangle(new VisualBrush(surface) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rectangle }, null, rectangle);
                }
                bitmap.Render(visual);
            }
            else bitmap.Render(surface);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(outputPath)) encoder.Save(stream);
            ringPreview?.Close();
            Console.WriteLine("PASS WPF preview: " + Path.GetFullPath(outputPath));
            ExitApp();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _exiting = true; DisposeRuntime(); Close(); System.Windows.Application.Current.Shutdown(1);
        }
    }

    private MappingProfile EditingProfile => _settings.Profiles.FirstOrDefault(p => p.Id == _editingProfileId)
        ?? _settings.Profiles.First(p => string.IsNullOrWhiteSpace(p.ApplicationPath));

    private void PopulateProfiles()
    {
        bool prior = _loading; _loading = true;
        _profiles.Clear();
        foreach (var profile in _settings.Profiles.Where(profile => string.Equals(profile.DeviceKey, _deviceScope ? _selectedDevice?.DeviceKey : null, StringComparison.Ordinal)))
            _profiles.Add(new(profile.Id, profile.ApplicationPath is null ? "All applications" : profile.Name,
                string.IsNullOrWhiteSpace(profile.ApplicationPath) ? "All applications" : Path.GetFileName(profile.ApplicationPath)));
        ProfilesList.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _editingProfileId) ?? _profiles[0];
        _editingProfileId = ((ProfileRow)ProfilesList.SelectedItem).Id;
        _loading = prior;
        UpdateProfileContext();
        UpdateProfileManager();
    }

    private void RefreshBindings(MouseControl? select = null, int? auxiliaryKey = null)
    {
        bool prior = _loading; _loading = true;
        var old = BindingsList.SelectedItem as BindingRow;
        Guid? selectedLearned = select is null && auxiliaryKey is null ? old?.LearnedId : null;
        if (select is null && auxiliaryKey is null) { select = old?.Mouse; auxiliaryKey = old?.VirtualKey; }
        _bindings.Clear();
        foreach (var (control, name) in MouseNames.OrderBy(p => p.Control == MouseControl.XButton1 ? 4 : p.Control == MouseControl.XButton2 ? 3 : Array.FindIndex(MouseNames, n => n.Control == p.Control)))
        {
            var action = ProfileResolver.ResolveDeviceBinding(_settings, EditingProfile, control);
            string summary = action.Kind == ActionKind.Passthrough ? control switch
            { MouseControl.LeftButton => "Left click", MouseControl.RightButton => "Right click", MouseControl.MiddleButton => "Middle click", _ => "Default" } : DescribeAction(action);
            var learned = _settings.LearnedControls.Where(c => c.DeviceKey == EditingProfile.DeviceKey && c.Identity.StandardControl == control).OrderByDescending(c => c.ObservedAt).FirstOrDefault();
            _bindings.Add(new(control, null, learned?.Name ?? name, summary));
        }
        if (ShowAuxiliaryCheck.IsChecked == true)
            for (int key = 0x7C; key <= 0x87; key++)
            {
                var action = EditingProfile.AuxiliaryKeys.FirstOrDefault(b => b.VirtualKey == key)?.Action ?? new InputAction();
                _bindings.Add(new(null, key, $"Extra button · F{key - 0x7C + 13}", DescribeAction(action)));
            }
        foreach (var learned in _settings.LearnedControls.Where(c => c.DeviceKey == EditingProfile.DeviceKey && c.Identity.StandardControl is null))
        {
            var action = EditingProfile.LearnedBindings.FirstOrDefault(b => b.ControlId == learned.Id)?.Action ?? new();
            _bindings.Add(new(null, null, learned.Name, "Learned · " + (action.Kind == ActionKind.Passthrough ? "No assignment" : "Draft: " + DescribeAction(action)), learned.Id));
        }
        BindingsList.SelectedItem = _bindings.FirstOrDefault(b => selectedLearned is not null ? b.LearnedId == selectedLearned : auxiliaryKey is not null
            ? b.VirtualKey == auxiliaryKey : b.Mouse == select && b.VirtualKey is null) ?? _bindings[3];
        _loading = prior;
        LoadSelectedBinding();
        ApplyWorkspaceSizing();
        if (IsInitialized) PlaceMarkers();
        if (IsLoaded && SourceExpander.IsExpanded)
        {
            BindingsList.UpdateLayout();
            BindingsList.ScrollIntoView(BindingsList.SelectedIndex < 5 ? _bindings[0] : BindingsList.SelectedItem);
        }
    }

    private void LoadSelectedBinding()
    {
        if (BindingsList.SelectedItem is not BindingRow row) return;
        _loadingEditor = true;
        InputAction action = row.LearnedId is { } learnedId ? EditingProfile.LearnedBindings.FirstOrDefault(b => b.ControlId == learnedId)?.Action ?? new()
            : row.Mouse is { } mouse
            ? ProfileResolver.ResolveDeviceBinding(_settings, EditingProfile, mouse)
            : EditingProfile.AuxiliaryKeys.FirstOrDefault(b => b.VirtualKey == row.VirtualKey)?.Action ?? new();
        SelectedInputTitle.Text = row.Name;
        SavedActionText.Text = "Saved: " + (action.Kind == ActionKind.Passthrough ? "Default behavior" : DescribeAction(action));
        ApplyBindingButton.Content = row.LearnedId is not null ? "Save draft" : "Apply";
        if (row.LearnedId is not null) SavedActionText.Text = "Draft: " + DescribeAction(action) + " · not active";
        else if (action.Kind == ActionKind.Ring && !(EditingProfile.DeviceKey is { } routeKey && _service?.CanRouteDevice(routeKey) == true))
            SavedActionText.Text = "Saved: Click Circle + original input";
        if (row.Mouse is { } inheritedMouse && EditingProfile.ApplicationPath is not null && action.Kind == ActionKind.Ring
            && EditingProfile.Bindings.FirstOrDefault(b => b.Input == inheritedMouse)?.Action.Kind is null or ActionKind.Passthrough)
            SavedActionText.Text = "Inherited from General: Click Circle";
        SelectedInputHint.Text = row.LearnedId is not null ? "Learned HID input · assignment drafts are saved, but exclusive remapping is not available for this input yet."
            : row.VirtualKey is not null
            ? "Assign an extra mouse button that sends a keyboard key."
            : InputPresentation.Position(row.Mouse!.Value);
        _draftKeyCode = action.KeyCode; _draftModifiers = action.Modifiers;
        ActionCombo.SelectedValue = action.Kind;
        MouseOutputCombo.SelectedValue = action.MouseOutput;
        TextActionBox.Text = action.Text;
        ApplicationPathBox.Text = action.ApplicationPath;
        ArgumentsBox.Text = action.Arguments;
        AuxiliaryHint.Visibility = row.VirtualKey is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionPanels();
        UpdateShortcutDisplay();
        UpdateCallouts();
        _loadingEditor = false; MarkEditorSaved();
    }

    private void UpdateActionPanels()
    {
        if (!IsInitialized || ActionCombo.SelectedItem is not ActionChoice choice) return;
        ShortcutPanel.Visibility = choice.Kind == ActionKind.Shortcut ? Visibility.Visible : Visibility.Collapsed;
        MouseOutputPanel.Visibility = choice.Kind == ActionKind.MouseButton ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = choice.Kind == ActionKind.Text ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = choice.Kind == ActionKind.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
        RingPanel.Visibility = choice.Kind == ActionKind.Ring ? Visibility.Visible : Visibility.Collapsed;
        ActionDescriptionBox.Visibility = choice.Kind == ActionKind.Ring ? Visibility.Visible : Visibility.Collapsed;
        ActionDescription.Text = choice.Kind switch
        {
            ActionKind.Passthrough => "Keeps the button's normal behavior.",
            ActionKind.Disabled => "This input performs no action.",
            ActionKind.Shortcut => "Runs once per press.",
            ActionKind.MouseButton => "Supports holding and dragging.",
            ActionKind.Text => "Types text into the focused application.",
            ActionKind.LaunchApplication => "Opens your selected local application.",
            ActionKind.Ring => EditingProfile.DeviceKey is { } key && _service?.CanRouteDevice(key) == true ? "Opens the Click Circle beside your pointer."
                : "Circle opens alongside the original input. This mode does not replace the input. Per-mouse replacement requires exclusive input support; no driver is installed automatically.",
            _ => ""
        };
    }

    private bool CommitSettings(AppSettings proposed, string success)
    {
        try
        {
            _store.Save(proposed);
            _settings = proposed;
            _service?.ApplySettings(_settings);
            UpdateProfileManager();
            StatusText.Text = success;
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not save: " + ex.Message;
            System.Windows.MessageBox.Show(this, ex.Message, "Could not save assignment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void SaveBinding_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsList.SelectedItem is not BindingRow row || ActionCombo.SelectedItem is not ActionChoice choice) return;
        var action = new InputAction
        {
            Kind = choice.Kind, KeyCode = choice.Kind == ActionKind.Shortcut ? _draftKeyCode : 0,
            Modifiers = choice.Kind == ActionKind.Shortcut ? _draftModifiers : KeyModifiers.None,
            Text = TextActionBox.Text, ApplicationPath = ApplicationPathBox.Text.Trim(), Arguments = ArgumentsBox.Text,
            MouseOutput = MouseOutputCombo.SelectedValue is MouseControl output ? output : MouseControl.LeftButton
        };
        var proposed = _settings.DeepClone();
        var profile = proposed.Profiles.Single(p => p.Id == _editingProfileId);
        SetBinding(profile, row, action);
        if (CommitSettings(proposed, (row.LearnedId is not null ? "Draft saved · original HID function is unchanged · " : "Saved · ") + $"{row.Name} → {DescribeAction(action)} · {profile.Name}"))
        { RefreshBindings(row.Mouse, row.VirtualKey); if (row.LearnedId is { } id) BindingsList.SelectedItem = _bindings.FirstOrDefault(b => b.LearnedId == id); }
    }

    private static void SetBinding(MappingProfile profile, BindingRow row, InputAction action)
    {
        if (row.LearnedId is { } controlId)
        {
            profile.LearnedBindings.RemoveAll(b => b.ControlId == controlId);
            if (action.Kind != ActionKind.Passthrough) profile.LearnedBindings.Add(new() { ControlId = controlId, Action = action });
        }
        else if (row.Mouse is { } mouse)
        {
            var binding = profile.Bindings.FirstOrDefault(b => b.Input == mouse);
            if (binding is null) profile.Bindings.Add(new() { Input = mouse, Action = action });
            else binding.Action = action;
        }
        else if (row.VirtualKey is { } key)
        {
            profile.AuxiliaryKeys.RemoveAll(b => b.VirtualKey == key);
            if (action.Kind != ActionKind.Passthrough) profile.AuxiliaryKeys.Add(new() { VirtualKey = key, Action = action });
        }
    }

    private void ResetBinding_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsList.SelectedItem is not BindingRow row) return;
        var proposed = _settings.DeepClone();
        SetBinding(proposed.Profiles.Single(p => p.Id == _editingProfileId), row, new());
        if (CommitSettings(proposed, $"Restored default behavior for {row.Name}.")) RefreshBindings(row.Mouse, row.VirtualKey);
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfilesList.SelectedItem is not ProfileRow row) return;
        _editingProfileId = row.Id;
        if (EditingProfile.DeviceKey is null && string.IsNullOrWhiteSpace(EditingProfile.ApplicationPath) && _settings.ActiveProfileId != row.Id)
        {
            var next = _settings.DeepClone(); next.ActiveProfileId = row.Id;
            CommitSettings(next, $"Global profile selected: {row.Name}");
        }
        UpdateProfileContext(); RefreshBindings();
    }

    private void UpdateProfileContext()
    {
        var profile = EditingProfile;
        bool loading = _loading; _loading = true;
        AutoSwitchCheck.IsChecked = _settings.Profiles.FirstOrDefault(p => p.DeviceKey == profile.DeviceKey && p.ApplicationPath is null)?.AutoSwitchApplications ?? _settings.AutoSwitchProfiles;
        _loading = loading;
        string target = string.IsNullOrWhiteSpace(profile.ApplicationPath) ? "All applications" : Path.GetFileName(profile.ApplicationPath);
        ProfileContext.Text = profile.ApplicationPath is null ? "Default settings for this mouse."
            : $"Settings for {profile.Name} on this mouse. Open Edit Circle to customize its menu.";
        ScopeLabel.Text = profile.DeviceKey is null ? "Shared profile" : "Device-specific profile";
        ScopeDescription.Text = profile.DeviceKey is null ? "Applies to mice that send the same Windows input." : "Applies only to the selected input source.";
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice?.DeviceKey is null) { ShowPage("devices"); StatusText.Text = "Choose a mouse first."; return; }
        OpenCircleWorkspace();
        _circleEditor?.ShowApplicationPicker();
    }

    private void AutoSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (_selectedDevice?.DeviceKey is not { } key) return;
        var next = _settings.DeepClone();
        var defaults = next.Profiles.First(p => p.DeviceKey == key && p.ApplicationPath is null);
        defaults.AutoSwitchApplications = AutoSwitchCheck.IsChecked == true;
        if (!CommitSettings(next, (defaults.AutoSwitchApplications == true ? "Application switching on · " : "Application switching off · ") + _selectedDevice.Name)) UpdateProfileContext();
    }

    private void BindingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_loading) { LoadSelectedBinding(); OpenEditor(); } }
    private void ShowAuxiliary_Changed(object sender, RoutedEventArgs e)
    { if (!_loading) { RefreshBindings(); if (ShowAuxiliaryCheck.IsChecked == true) SourceExpander.IsExpanded = true; } }
    private void ActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateActionPanels(); MarkEditorChanged(); }

    private void UpdateShortcutDisplay() => ShortcutChoice.SetShortcut(_draftKeyCode, _draftModifiers);
    private void ShortcutChoice_Changed(object? sender, EventArgs e)
    {
        _draftKeyCode = ShortcutChoice.KeyCode; _draftModifiers = ShortcutChoice.Modifiers;
        ActionCombo.SelectedValue = ActionKind.Shortcut;
        UpdateShortcutDisplay();
        MarkEditorChanged();
    }

    private void ShortcutPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        ActionCombo.SelectedValue = ActionKind.Shortcut;
        (_draftKeyCode, _draftModifiers) = button.Tag?.ToString() switch
        {
            "copy" => (0x43, KeyModifiers.Control), "paste" => (0x56, KeyModifiers.Control),
            "undo" => (0x5A, KeyModifiers.Control), "close" => (0x57, KeyModifiers.Control),
            "media" => (0xB3, KeyModifiers.None), "mute" => (0xAD, KeyModifiers.None),
            "alt-tab" => (9, KeyModifiers.Alt),
            _ => (_draftKeyCode, _draftModifiers)
        };
        UpdateShortcutDisplay();
        MarkEditorChanged();
    }

    private void BrowseAction_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose an application to open", Filter = "Windows application (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ApplicationPathBox.Text = dialog.FileName;
    }

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_service is null) return;
            if (!_service.CanEnable)
            {
                ShowPage("setup"); SetupGuideExpander.IsExpanded = true;
                StatusText.Text = _service.HasDeviceRouting
                    ? "Cannot enable: no verified mouse input source. Your saved profiles are unchanged."
                    : "Connect a mouse and wait for input observation. Click Circle does not require a driver.";
                return;
            }
            _service.SetEnabled(!_service.IsEnabled);
            StatusText.Text = _service.IsEnabled
                ? _service.DriverlessCircleMode ? "Click Circle enabled for each mouse · Assigned middle clicks are replaced. Other middle clicks are forwarded."
                    : "Assignments enabled · Your saved actions are active."
                : "Assignments paused · Normal mouse behavior.";
            UpdateEngineState();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Service_StateChanged(object? sender, EventArgs e)
    { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(UpdateEngineState); }
    private void Service_ActionError(object? sender, string error)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = "Could not send input: " + error;
            if (_tray is not null && DateTime.UtcNow - _lastErrorShown > TimeSpan.FromSeconds(15))
            {
                _lastErrorShown = DateTime.UtcNow;
                _tray.ShowBalloonTip(4500, "Moushift · Action failed", error, System.Windows.Forms.ToolTipIcon.Warning);
            }
        });
    }
    private void ObserveInput(string name)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (DateTime.UtcNow - _lastInputShown < TimeSpan.FromMilliseconds(65)) return;
            _lastInputShown = DateTime.UtcNow; InputStatusText.Text = "↳ " + name;
        });
    }
    private void UpdateEngineState()
    {
        if (_disposed || _service is null) return;
        if (!_service.IsEnabled && !_ringPreview) CloseRingVisual();
        bool enabled = _service.IsEnabled;
        EngineBadge.Text = !_service.IsRunning ? "Input unavailable" : !_service.CanEnable ? "Waiting for mouse" : enabled ? _service.DriverlessCircleMode ? "Circle enabled · Driverless" : "Enabled" : "Paused";
        EngineBadge.Foreground = (Brush)FindResource(enabled ? "Accent" : "Muted");
        EnableButton.Content = !_service.CanEnable ? "Input status" : enabled ? "Pause" : _service.DriverlessCircleMode ? "Enable Circle" : "Enable";
        EnableButton.Style = (Style)FindResource(_service.CanEnable && !enabled ? "Primary" : typeof(System.Windows.Controls.Button));
        EnableButton.IsEnabled = _service.IsRunning;
        if (_service.IsRunning)
        {
            EmergencyShortcutLabel.Text = _service.EmergencyShortcut.Replace("+", " + ");
            EmergencyHelp.Text = _service.EmergencyShortcut + " pauses assignments. Standard mouse actions pause inside the editor. Closing minimizes to the system tray; choose Quit to exit.";
        }
        if (_tray is not null) _tray.Text = !_service.CanEnable ? "Moushift · Waiting for mouse" : enabled ? _service.DriverlessCircleMode ? "Moushift · Driverless Circle enabled" : "Moushift · Assignments enabled" : "Moushift · Paused";
        if (_trayToggle is not null) { _trayToggle.Text = !_service.CanEnable ? "Input status" : enabled ? "Pause" : _service.DriverlessCircleMode ? "Enable Click Circle" : "Enable assignments"; _trayToggle.Enabled = _service.IsRunning; }
        if (!string.IsNullOrWhiteSpace(_service.LastError)) StatusText.Text = _service.LastError;
        string inputStatus = _service.InputStatus;
        foreach (var label in InputPresentation.MouseInputs) inputStatus = inputStatus.Replace(label.Control + ":", label.Name + ":", StringComparison.Ordinal);
        LastInputResultText.Text = inputStatus;
        // Explicit, temporary diagnostic snapshot: no typed text or profile actions.
        string? diagnosticFile = Environment.GetEnvironmentVariable("CLICKLOOM_DIAGNOSTICS_FILE");
        if (!string.IsNullOrWhiteSpace(diagnosticFile) && Path.IsPathFullyQualified(diagnosticFile))
        {
            try
            {
                File.WriteAllText(diagnosticFile, System.Text.Json.JsonSerializer.Serialize(new
                {
                    Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3),
                    _service.IsRunning, _service.IsEnabled, _service.InputStatus, _service.LastError,
                    _service.CanEnable, _service.DriverlessCircleMode, _service.HasDriverlessCircleSources, _service.DriverlessCircleTriggerCount,
                    CircleOpen = _service.IsRingOpen,
                    _service.RecentCircleInput,
                    _service.ActiveProfileName, Foreground = Path.GetFileName(_service.ForegroundApplication)
                }));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        if (DateTime.UtcNow - _lastInputShown > TimeSpan.FromSeconds(4))
            InputStatusText.Text = !_service.CanEnable ? "Waiting for connected mouse input" : _service.IsEnabled ? _service.DriverlessCircleMode ? "Per-mouse Circle · Middle-click routing" : "Per-mouse assignments enabled" : "Paused";
    }

    private void MappingsNav_Click(object sender, RoutedEventArgs e) { ButtonsContent.Visibility = Visibility.Visible; SetInspectorTab(false); }
    private void DevicesNav_Click(object sender, RoutedEventArgs e) { ShowPage("devices"); RefreshDevices(); }
    private void AboutNav_Click(object sender, RoutedEventArgs e) => ShowPage("about");
    private void ShowPage(string page)
    {
        if (page == "mappings" && _selectedDevice?.DeviceKey is null) { ShowPage("devices"); StatusText.Text = "Choose a mouse to edit its separate profile."; return; }
        if (!TryCloseCircleEditor()) return;
        if (page == "devices")
        {
            _selectedDevice = null; _editingSavedProfile = false;
            foreach (var card in _deviceCards) card.IsSelected = false;
        }
        bool workspace = page is "devices" or "mappings";
        bool detail = page == "mappings" && _selectedDevice?.DeviceKey is not null;
        DeviceDetailPage.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        DevicesWorkspace.Visibility = workspace ? Visibility.Visible : Visibility.Collapsed;
        ProfileToolbar.Visibility = workspace ? Visibility.Visible : Visibility.Collapsed;
        SetupPage.Visibility = page == "setup" ? Visibility.Visible : Visibility.Collapsed;
        MappingPage.Visibility = detail ? Visibility.Visible : Visibility.Collapsed;
        InspectorEmpty.Visibility = MappingPage.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        if (workspace && _selectedDevice?.DeviceKey is not null)
        {
            _selectedDevice ??= GlobalDeviceCard();
            DeviceHeading.Text = _selectedDevice.Name;
            DetailHeading.Text = _selectedDevice.Name;
            DetailBatteryInfo.Content = _selectedDevice;
            HeroMouse.SetResourceReference(MouseIllustration.AccentProperty, "Accent");
            DeviceScopeCheck.IsEnabled = _selectedDevice.DeviceKey is not null && (_service?.HasDeviceRouting == true || _service?.CanTriggerCircleForDevice(_selectedDevice.DeviceKey) == true);
            DeviceScopeHint.Text = _service?.CanRouteDevice(_selectedDevice.Key) == true ? "Actions apply only to this mouse." : "Your settings are saved for this mouse. Complete device setup to activate.";
            ImportPreviousButton.Visibility = _settings.Profiles.Any(p => p.DeviceKey is null && p.ApplicationPath is null && p.Bindings.Any(b => b.Action.Kind != ActionKind.Passthrough)) ? Visibility.Visible : Visibility.Collapsed;
        }
        else CloseEditor();
        if (page == "setup") UpdateSetupStatus();
        MappingsNav.Visibility = Visibility.Visible;
        foreach (var (navigation, selected) in new[] { (DevicesNav, workspace), (ProfilesNav, page == "profiles"), (AboutNav, page == "about"), (AppearanceNav, page is "appearance" or "setup"), (SetupNav, page == "setup") })
        {
            navigation.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, selected ? "Ink" : "Muted");
            navigation.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            navigation.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, selected ? "Selection" : "Sidebar");
            navigation.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, selected ? "Accent" : "Sidebar");
            navigation.BorderThickness = new Thickness(selected && (navigation == DevicesNav || navigation == ProfilesNav || navigation == AppearanceNav || navigation == AboutNav) ? 3 : 0, 0, 0, 0);
        }
        PageTitle.Text = page switch { "devices" => "Devices", "about" => "Help", "profiles" => "Profiles", "appearance" => "Settings", "setup" => "Input support", _ => "Customize" };
        if (workspace) SetInspectorTab(ButtonsContent.Visibility == Visibility.Collapsed);
        ApplyWorkspaceSizing();
    }
    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private void RefreshDevices()
    {
        if (_disposed || Interlocked.Exchange(ref _presencePolling, 1) != 0) return;
        _ = RefreshDevicesAsync();
    }
    private async Task RefreshDevicesAsync()
    {
        try
        {
            var result = await Task.Run(MouseConnectionCatalog.Enumerate);
            if (_disposed) return;
            ApplyConnectionInventory(result);
        }
        catch (Exception ex)
        {
            DevicesSubtitle.Text = "Could not refresh connections";
            PresenceDetails.Text = "Device list may be out of date: " + ex.Message;
        }
        finally { Interlocked.Exchange(ref _presencePolling, 0); }
    }

    private static DeviceCard GlobalDeviceCard() => new("global", "Shared assignments", "MOUSHIFT", "Global button assignments", new SolidColorBrush(Color.FromRgb(169, 158, 188)));
    private static string DisplayDeviceName(string name) => name;
    private void SharedMappings_Click(object sender, RoutedEventArgs e) => DeviceCard_Click(new System.Windows.Controls.Button { Tag = GlobalDeviceCard() }, e);

    private void DeviceCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: DeviceCard device }) return;
        if (device.DeviceKey is null) { ShowPage("devices"); StatusText.Text = "Choose a mouse. Previous shared assignments are kept for import."; return; }
        if (!TryCloseCircleEditor()) return;
        _selectedDevice = device;
        _editingSavedProfile = false;
        var profile = _settings.Profiles.FirstOrDefault(p => p.DeviceKey == device.DeviceKey && p.ApplicationPath is null);
        if (profile is null)
        {
            var next = _settings.DeepClone();
            profile = ProfileResolver.CreateDeviceDefault(device.DeviceKey, device.Name, _settings.Profiles.First(p => p.DeviceKey is null && p.ApplicationPath is null).RingSlots);
            next.Profiles.Add(profile);
            if (!CommitSettings(next, "Created a separate profile for " + device.Name)) return;
        }
        _loading = true; _deviceScope = true; DeviceScopeCheck.IsChecked = true; ShowAuxiliaryCheck.IsChecked = false; ShowAuxiliaryCheck.IsEnabled = false; _loading = false;
        _editingProfileId = profile.Id;
        foreach (var row in _deviceCards) row.IsSelected = row.DeviceKey == device.DeviceKey;
        PopulateProfiles(); RefreshBindings(); CloseEditor(); ButtonsContent.Visibility = Visibility.Visible; ShowPage("mappings");
    }

    private void MouseCallout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || !Enum.TryParse<MouseControl>(button.Tag?.ToString(), out var input)) return;
        BindingsList.SelectedItem = _bindings.Single(row => row.Mouse == input);
        LoadSelectedBinding();
        OpenEditor();
    }
    private void OpenEditor() { EditorPane.Visibility = Visibility.Visible; SourceExpander.Visibility = Visibility.Collapsed; ButtonsContent.Visibility = Visibility.Visible; SetInspectorTab(false); }
    private void CloseEditor() { EditorPane.Visibility = Visibility.Collapsed; SourceExpander.Visibility = Visibility.Visible; }
    private void CloseEditor_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void DeviceScope_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        string? key = _selectedDevice?.DeviceKey;
        if (DeviceScopeCheck.IsChecked == true && (key is null || !(_service?.HasDeviceRouting == true || _service?.CanTriggerCircleForDevice(key) == true)))
        { _loading = true; DeviceScopeCheck.IsChecked = false; _loading = false; return; }
        _deviceScope = DeviceScopeCheck.IsChecked == true;
        var profile = _deviceScope ? _settings.Profiles.FirstOrDefault(p => p.DeviceKey == key && p.ApplicationPath is null)
            : _settings.Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId && p.DeviceKey is null);
        if (profile is null && _deviceScope)
        {
            var next = _settings.DeepClone(); profile = next.Profiles.First(p => p.DeviceKey is null && p.ApplicationPath is null).DeepClone();
            profile.Id = Guid.NewGuid(); profile.Name = "Global"; profile.DeviceKey = key; profile.AuxiliaryKeys = []; next.Profiles.Add(profile);
            if (!CommitSettings(next, "Created a profile for this device."))
            { _deviceScope = false; _loading = true; DeviceScopeCheck.IsChecked = false; _loading = false; return; }
        }
        _editingProfileId = profile?.Id ?? _settings.ActiveProfileId;
        ShowAuxiliaryCheck.IsEnabled = !_deviceScope;
        if (_deviceScope) ShowAuxiliaryCheck.IsChecked = false;
        PopulateProfiles(); RefreshBindings();
    }

    private void EditRing_Click(object sender, RoutedEventArgs e)
    {
        if (EditingProfile.DeviceKey is null) { ShowPage("devices"); StatusText.Text = "Choose a mouse for its Click Circle."; return; }
        OpenCircleWorkspace();
    }
    private void PreviewRing_Click(object sender, RoutedEventArgs e) { _service?.DismissRing(); OpenRing(EditingProfile.RingSlots, null); }
    private void OpenRing(IReadOnlyList<RingSlot> slots, RingRequest? request)
    {
        CloseRingVisual(); _ringPreview = request is null;
        var window = new RingWindow(slots, _ringPreview, request?.Driverless == true); _ringWindow = window;
        window.Invoked += (_, invocation) =>
        {
            if (request is not null) _service?.InvokeRingAction(request.Id, invocation);
            else StatusText.Text = "Preview: " + RingNavigation.Resolve(slots, invocation.Path)?.Name + " (action not executed)";
        };
        window.Closed += (_, _) =>
        { if (ReferenceEquals(_ringWindow, window)) { _ringWindow = null; _ringPreview = false; if (request is not null) _service?.DismissRing(); } };
        window.Show(); var bounds = window.PositionAtCursor();
        if (request is not null) _service?.SetRingBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }
    private void CloseRingVisual() { var ring = _ringWindow; _ringWindow = null; _ringPreview = false; ring?.Close(); }

    private void UpdateCallouts()
    {
        if (!IsInitialized) return;
        var selected = (BindingsList.SelectedItem as BindingRow)?.Mouse;
        DiagramLinks.Selected = selected; DiagramLinks.InvalidateVisual();
        HeroMouse.Selected = selected; HeroMouse.InvalidateVisual();
        foreach (var (input, button, label, fallback) in new[]
        {
            (MouseControl.LeftButton, LeftCallout, LeftCalloutAction, "Left click"),
            (MouseControl.RightButton, RightCallout, RightCalloutAction, "Right click"),
            (MouseControl.MiddleButton, MiddleCallout, MiddleCalloutAction, "Middle button"),
            (MouseControl.XButton1, BackCallout, BackCalloutAction, "Default"),
            (MouseControl.XButton2, ForwardCallout, ForwardCalloutAction, "Default")
        })
        {
            var action = ProfileResolver.ResolveDeviceBinding(_settings, EditingProfile, input);
            label.Text = action is null || action.Kind == ActionKind.Passthrough ? fallback : DescribeAction(action);
            button.BorderBrush = ThemeManager.Brush(selected == input ? "Accent" : "Ink");
            button.Background = ThemeManager.Brush(selected == input ? "PrimaryFill" : "Panel");
            button.ToolTip = MouseName(input) + " · " + label.Text;
        }
    }
    private static string DisplayId(string? id) => string.IsNullOrWhiteSpace(id) ? "—" : id;

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.GetDirectoryName(_store.FilePath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = '"' + folder + '"', UseShellExecute = false });
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void CreateTray()
    {
        Icon = BrandMark.Bitmap(64);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(BrandMark.Bitmap(64)));
        using var stream = new System.IO.MemoryStream(); encoder.Save(stream); stream.Position = 0;
        using var image = new System.Drawing.Bitmap(stream);
        nint icon = image.GetHicon();
        try { using var temporary = System.Drawing.Icon.FromHandle(icon); _trayIcon = (System.Drawing.Icon)temporary.Clone(); }
        finally { DestroyIcon(icon); }
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Moushift", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _trayToggle = new System.Windows.Forms.ToolStripMenuItem("Enable assignments") { Enabled = false };
        _trayToggle.Click += (_, _) => Dispatcher.Invoke(() => Enable_Click(this, new RoutedEventArgs()));
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray = new System.Windows.Forms.NotifyIcon { Icon = _trayIcon, Text = "Moushift · Paused", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }
    private void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        if (_previewOnly) { ReleaseCircleWorkspace(); _exiting = true; DisposeRuntime(); System.Windows.Application.Current.Shutdown(); return; }
        if (!TryCloseCircleEditor()) { e.Cancel = true; return; }
        e.Cancel = true; Hide();
        _tray?.ShowBalloonTip(2500, "Moushift is still running", "Choose Quit in the tray menu to close Moushift completely.", System.Windows.Forms.ToolTipIcon.Info);
    }
    private void ExitApp() { if (!TryCloseCircleEditor()) return; _exiting = true; DisposeRuntime(); Close(); System.Windows.Application.Current.Shutdown(); }
    private void DisposeRuntime()
    {
        if (_disposed) return;
        _disposed = true; _statusTimer?.Stop();
        CloseRingVisual();
        StopDeviceActivity();
        _service?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose();
    }

    private static string MouseName(MouseControl input) => MouseNames.FirstOrDefault(p => p.Control == input).Name ?? input.ToString();
    private static string DescribeAction(InputAction action) => InputPresentation.Action(action);
    private static string ShortcutName(int virtualKey, KeyModifiers modifiers) => InputPresentation.Shortcut(virtualKey, modifiers);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
    private sealed record ProfileRow(Guid Id, string Name, string Subtitle) { public override string ToString() => Name; }
    private sealed record BindingRow(MouseControl? Mouse, int? VirtualKey, string Name, string Summary, Guid? LearnedId = null)
    {
        public string Number => Mouse switch { MouseControl.LeftButton => "1", MouseControl.RightButton => "2", MouseControl.MiddleButton => "3", MouseControl.XButton2 => "4", MouseControl.XButton1 => "5", _ => "·" };
        public string Glyph => Mouse switch { MouseControl.XButton1 => "\uE72B", MouseControl.XButton2 => "\uE72A", MouseControl.WheelUp => "\uE74A", MouseControl.WheelDown => "\uE74B", MouseControl.WheelLeft => "\uE76B", MouseControl.WheelRight => "\uE76C", _ => "\uE962" };
    }
    private sealed record ActionChoice(ActionKind Kind, string Name) { public override string ToString() => Name; }
    private sealed record MouseChoice(MouseControl Control, string Name) { public override string ToString() => Name; }
    private sealed record DeviceRow(string Name, string Details);
    private sealed record DeviceCard(string Key, string Name, string Manufacturer, string Detail, Brush Accent, string? DeviceKey = null) : INotifyPropertyChanged
    {
        public bool CanEdit => DeviceKey is not null;
        private int? _batteryPercent;
        public int? BatteryPercent
        {
            get => _batteryPercent;
            set
            {
                var next = value is >= 0 and <= 100 ? value : null;
                if (_batteryPercent == next) return; _batteryPercent = next;
                foreach (string name in new[] { nameof(BatteryPercent), nameof(BatteryLabel), nameof(BatteryFillWidth), nameof(BatteryDescription), nameof(BatteryVisibility) }) PropertyChanged?.Invoke(this, new(name));
            }
        }
        public Visibility BatteryVisibility => BatteryPercent is null ? Visibility.Collapsed : Visibility.Visible;
        public string BatteryLabel => BatteryPercent is { } value ? value + "%" : "";
        public double BatteryFillWidth => (BatteryPercent ?? 0) * .22;
        public string BatteryDescription => BatteryPercent is not null
            ? "Battery level reported by Windows; it may be cached and does not confirm mouse power."
            : "This connection does not expose a battery percentage to Windows. No manufacturer protocol is used.";
        public string ConnectionLabel => Detail.Contains("receiver", StringComparison.OrdinalIgnoreCase) ? "Receiver connected" : "Windows connection";
        private string _activity = "No input observed yet";
        public string Activity { get => _activity; set { if (_activity == value) return; _activity = value; PropertyChanged?.Invoke(this, new(nameof(Activity))); } }
        private bool _selected;
        public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        private string _routing = "Setup required";
        public string Routing { get => _routing; set { if (_routing == value) return; _routing = value; PropertyChanged?.Invoke(this, new(nameof(Routing))); } }
        public override string ToString() => Name;
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
