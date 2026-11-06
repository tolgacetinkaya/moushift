using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Clickloom.Core;
using Microsoft.Win32;

namespace Clickloom.App;

public sealed partial class RingEditorWindow : Window
{
    private const string DragFormat = "Clickloom.CirclePlacement";
    private readonly Dictionary<Guid, CircleDocument> _documents = [];
    private readonly AppSettings _draft;
    private readonly string _initialSettings;
    private readonly string? _deviceKey;
    private readonly Clickloom.Windows.MouseRemappingService? _service;
    private Guid _profileId;
    private readonly Canvas _canvas = new() { Width = 620, Height = 570, Background = Brushes.Transparent };
    private readonly StackPanel _catalog = new();
    private readonly TextBox _search = new();
    private readonly Dictionary<string, Button> _providerButtons = [];
    private readonly TextBlock _searchHint = new() { Text = "Search actions…", Margin = new(12, 0, 0, 0), IsHitTestVisible = false };
    private readonly ComboBox _profiles = new() { MinWidth = 170, MaxWidth = 260, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly ComboBox _manual = new() { MinWidth = 150, MaxWidth = 220, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly CheckBox _adapt = new() { Content = "Adapt Circle to app" }, _inherit = new() { Content = "Use General Circle button" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _breadcrumb = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _selection = new() { FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
    private readonly Button _undo = new() { Content = "↶", ToolTip = "Undo (Ctrl+Z)" }, _redo = new() { Content = "↷", ToolTip = "Redo (Ctrl+Y)" };
    private readonly Button _edit = new() { Content = "Edit" }, _remove = new() { Content = "Remove" }, _open = new() { Content = "Open folder" };
    private readonly Button _addShortcut = new() { Content = "+ Add shortcut", Margin = new(12, 0, 0, 0) };
    private readonly List<int> _folder = [];
    private int[]? _selected;
    private bool _loading, _discarded;
    private RingWindow? _preview;
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private int[]? _hoverFolder;
    private string _provider = "System Actions";
    private CircleDocument Document => _documents[_profileId];
    private int[]? SelectedPath => _selected ?? (_folder.Count > 0 ? _folder.ToArray() : null);
    public List<RingSlot> Slots => Document.Slots;
    private MappingProfile Profile => _draft.Profiles.Single(p => p.Id == _profileId);
    private MappingProfile General => _draft.Profiles.First(p => p.DeviceKey == _deviceKey && p.ApplicationPath is null);
    private IEnumerable<MappingProfile> DeviceProfiles => _draft.Profiles.Where(p => p.DeviceKey == _deviceKey);
    private static Brush B(string key) => ThemeManager.Brush(key);
    private static TextBlock Text(string value, double size = 13, string color = "Ink") => new() { Text = value, FontSize = size, Foreground = B(color) };

    public RingEditorWindow(IEnumerable<RingSlot> slots, Clickloom.Windows.MouseRemappingService? captureService = null, string? profileName = null, int depth = 0)
        : this(SingleProfile(slots, profileName), Guid.Empty, captureService, profileName) { }
    private static AppSettings SingleProfile(IEnumerable<RingSlot> slots, string? name)
    { var s = AppSettings.CreateDefault(); s.Profiles[0].Name = name ?? "General"; s.Profiles[0].RingSlots = slots.Select(x => x.DeepClone()).ToList(); return s; }

    public RingEditorWindow(AppSettings settings, Guid profileId, Clickloom.Windows.MouseRemappingService? service, string? deviceName)
    {
        _draft = settings.DeepClone(); _initialSettings = System.Text.Json.JsonSerializer.Serialize(_draft);
        _profileId = profileId == Guid.Empty ? _draft.Profiles[0].Id : profileId; _deviceKey = Profile.DeviceKey; _service = service;
        foreach (var profile in DeviceProfiles) _documents[profile.Id] = new(profile.RingSlots);
        Title = "Click Circle · " + (deviceName ?? Profile.DeviceName ?? "Mouse");
        Width = 1220; Height = 880; MinWidth = 1080; MinHeight = 720;
        Background = B("Bg"); Foreground = B("Ink"); FontFamily = new("Segoe UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid();
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new() { Height = height });
        var header = new DockPanel { Margin = new(28, 22, 28, 20) };
        var history = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _undo.Click += (_, _) => History(false); _redo.Click += (_, _) => History(true); _undo.Margin = new(0, 0, 6, 0); history.Children.Add(_undo); history.Children.Add(_redo);
        DockPanel.SetDock(history, Dock.Right); header.Children.Add(history);
        var backToButtons = new Button { Content = "‹ Back to buttons", Margin = new(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        backToButtons.Click += (_, _) => { if (_hostedClose is not null) _hostedClose(); else Close(); };
        DockPanel.SetDock(backToButtons, Dock.Left); header.Children.Add(backToButtons);
        var heading = new StackPanel(); heading.Children.Add(Text("Click Circle", 27));
        var subtitle = Text(deviceName ?? Profile.DeviceName ?? "Circle workspace", 13, "Muted"); subtitle.Margin = new(0, 5, 0, 0); heading.Children.Add(subtitle); header.Children.Add(heading); root.Children.Add(header);
        var contexts = new DockPanel { Margin = new(28, 0, 28, 14) };
        var switches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _adapt.Margin = new(12, 0, 14, 0); switches.Children.Add(_adapt); switches.Children.Add(_manual); DockPanel.SetDock(switches, Dock.Right); contexts.Children.Add(switches);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal }; tabs.Children.Add(_profiles);
        var add = new Button { Content = "+ Application", Margin = new(8, 0, 0, 0), IsEnabled = _deviceKey is not null }; add.Click += (_, _) => AddApplication(); tabs.Children.Add(add); contexts.Children.Add(tabs); Grid.SetRow(contexts, 1); root.Children.Add(contexts);
        _profiles.SelectionChanged += (_, _) => { if (_loading || _profiles.SelectedValue is not Guid id) return; _profileId = id; ResetNavigation(); Refresh(); RefreshCatalog(); };
        _adapt.Checked += (_, _) => ChangeAdapt(); _adapt.Unchecked += (_, _) => ChangeAdapt();
        _manual.SelectionChanged += (_, _) => { if (!_loading && _manual.SelectedValue is Guid id) General.ManualCircleProfileId = id; };
        _inherit.Checked += (_, _) => { if (!_loading) Profile.InheritCircleTrigger = true; }; _inherit.Unchecked += (_, _) => { if (!_loading) Profile.InheritCircleTrigger = false; };

        var body = new Grid { Margin = new(16, 0, 16, 0) }; body.ColumnDefinitions.Add(new() { MinWidth = 340 }); body.ColumnDefinitions.Add(new() { Width = new(6) }); body.ColumnDefinitions.Add(_libraryColumn);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Style = (Style)FindResource("WorkspaceSplitter"), ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ToolTip = "Drag to resize action library" };
        Grid.SetColumn(splitter, 1); splitter.DragCompleted += (_, e) => { if (!e.Canceled) _libraryResized?.Invoke(_libraryColumn.ActualWidth); }; body.Children.Add(splitter);
        var workspace = new Grid { Margin = new(0, 0, 12, 0) };
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto }); workspace.RowDefinitions.Add(new()); workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var trail = new DockPanel { Margin = new(14, 12, 14, 0) }; var back = new Button { Content = "‹", ToolTip = "Back", Margin = new(0, 0, 10, 0) };
        _addShortcut.Click += (_, _) => AddShortcut(); DockPanel.SetDock(_addShortcut, Dock.Right); trail.Children.Add(_addShortcut);
        back.Click += (_, _) => { if (_folder.Count > 0) _folder.RemoveAt(_folder.Count - 1); _selected = null; Refresh(); }; trail.Children.Add(back); trail.Children.Add(_breadcrumb); workspace.Children.Add(trail);
        var view = new Viewbox { Child = _canvas, Stretch = Stretch.Uniform, MaxWidth = 680, MaxHeight = 600, Margin = new(4) }; Grid.SetRow(view, 1); workspace.Children.Add(view);
        var inspector = new DockPanel { Margin = new(16, 4, 16, 16) };
        var operations = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _edit.Click += (_, _) => EditSelected(); _remove.Click += (_, _) => RemoveSelected(); _open.Click += (_, _) => EnterSelected();
        foreach (var button in new[] { _open, _edit, _remove }) { button.Margin = new(5, 0, 0, 0); operations.Children.Add(button); }
        DockPanel.SetDock(operations, Dock.Right); inspector.Children.Add(operations);
        var detail = new StackPanel(); detail.Children.Add(_selection); _inherit.Margin = new(0, 10, 0, 0); detail.Children.Add(_inherit); inspector.Children.Add(detail); Grid.SetRow(inspector, 2); workspace.Children.Add(inspector);
        body.Children.Add(new Border { Background = B("Panel"), CornerRadius = new(16), Child = workspace });
        var library = new DockPanel { Margin = new(18) }; var libraryHead = new StackPanel(); libraryHead.Children.Add(Text("Action library", 19));
        var hint = Text("Drag an action onto the Circle.", 12, "Muted"); hint.Margin = new(0, 8, 0, 16); libraryHead.Children.Add(hint);
        _search.ToolTip = "Search actions"; System.Windows.Automation.AutomationProperties.SetName(_search, "Search actions");
        _search.TextChanged += (_, _) => { _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RefreshCatalog(); };
        var searchField = new Grid(); _searchHint.Foreground = B("Muted"); searchField.Children.Add(_search); searchField.Children.Add(_searchHint); libraryHead.Children.Add(searchField);
        var providers = new UniformGrid { Columns = 2, Margin = new(0, 12, 0, 14) };
        foreach (string provider in new[] { "System Actions", "Circle Actions" })
        { var button = new Button { Content = provider, FontSize = 11, Padding = new(8), Margin = new(0, 0, 4, 0) }; _providerButtons[provider] = button; button.Click += (_, _) => { _provider = provider; RefreshCatalog(); }; providers.Children.Add(button); }
        libraryHead.Children.Add(providers); DockPanel.SetDock(libraryHead, Dock.Top); library.Children.Add(libraryHead);
        library.Children.Add(new ScrollViewer { Content = _catalog, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var libraryBorder = new Border { Background = B("Panel"), CornerRadius = new(16), Child = library }; Grid.SetColumn(libraryBorder, 2); body.Children.Add(libraryBorder); Grid.SetRow(body, 2); root.Children.Add(body);
        var footer = new DockPanel { Margin = new(28, 16, 28, 20) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var preview = new Button { Content = "Preview", Margin = new(8, 0, 8, 0) }; preview.Click += (_, _) => { _preview?.Close(); _preview = new RingWindow(Slots, true); _preview.Show(); _preview.PositionAtCursor(); }; buttons.Children.Add(preview);
        var save = new Button { Content = "Save changes", Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => { var candidate = _draft.DeepClone(); ApplyTo(candidate); var errors = SettingsValidator.Validate(candidate); if (errors.Count > 0) SetStatus(string.Join(" ", errors), true); else if (_hostedSave is not null) _hostedSave(); else DialogResult = true; }; buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); _status.Foreground = B("Muted"); footer.Children.Add(_status); Grid.SetRow(footer, 3); root.Children.Add(footer);
        AttachDragLayer(root); Content = new AdornerDecorator { Child = root };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); if (_hoverFolder is { } path) { _folder.Clear(); _folder.AddRange(path); _hoverFolder = null; _selected = null; Refresh(); } };
        root.PreviewKeyDown += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || Keyboard.FocusedElement is TextBox) return; if (e.Key is Key.Z or Key.Y) { History(e.Key == Key.Y); e.Handled = true; } };
        Closing += (_, e) => { if (DialogResult == true || _discarded || !HasChanges()) return; if (new ConfirmActionWindow("Discard changes", "Discard Circle changes?", "Your saved profiles have not been changed. Discard this editing session?", "Discard") { Owner = DialogOwner }.ShowDialog() != true) e.Cancel = true; };
        Closed += (_, _) => { EndDragPresentation(); _preview?.Close(); }; RefreshProfiles(); Refresh(); RefreshCatalog();
    }
    private bool HasChanges() => _documents.Values.Any(d => d.IsChanged) || System.Text.Json.JsonSerializer.Serialize(_draft) != _initialSettings;
    public void ApplyTo(AppSettings target)
    {
        foreach (var profile in DeviceProfiles.ToArray())
        {
            var existing = target.Profiles.FirstOrDefault(p => p.Id == profile.Id && p.DeviceKey == _deviceKey);
            if (existing is null) { existing = profile.DeepClone(); target.Profiles.Add(existing); }
            existing.RingSlots = _documents[profile.Id].Slots.Select(s => s.DeepClone()).ToList();
            existing.CircleAutoSwitch = profile.CircleAutoSwitch; existing.ManualCircleProfileId = profile.ManualCircleProfileId; existing.InheritCircleTrigger = profile.InheritCircleTrigger;
        }
    }
    private void AddApplication()
    {
        if (_draft.Profiles.Count >= SettingsValidator.MaximumProfiles) { SetStatus("The profile limit has been reached.", true); return; }
        var picker = new OpenFileDialog { Title = "Choose an application", Filter = "Windows application (*.exe)|*.exe" }; if (picker.ShowDialog(DialogOwner) != true) return;
        var existing = DeviceProfiles.FirstOrDefault(p => string.Equals(p.ApplicationPath, picker.FileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _profileId = existing.Id;
        else
        {
            var profile = new MappingProfile { DeviceKey = _deviceKey, DeviceName = General.DeviceName, Name = System.IO.Path.GetFileNameWithoutExtension(picker.FileName), ApplicationPath = picker.FileName,
                RingSlots = _documents[General.Id].Slots.Select(s => s.DeepClone()).ToList(), Bindings = Enum.GetValues<MouseControl>().Select(c => new ButtonBinding { Input = c }).ToList() };
            _draft.Profiles.Add(profile); _documents[profile.Id] = new(profile.RingSlots); _profileId = profile.Id;
        }
        ResetNavigation(); RefreshProfiles(); Refresh(); RefreshCatalog();
    }
    private void RefreshProfiles()
    {
        _loading = true; var profiles = DeviceProfiles.Select(p => new ProfileChoice(p.Id, p.ApplicationPath is null ? "General" : p.Name)).ToArray(); _profiles.ItemsSource = profiles; _profiles.SelectedValue = _profileId;
        _manual.ItemsSource = profiles; _manual.SelectedValue = General.ManualCircleProfileId ?? General.Id;
        _adapt.IsChecked = General.CircleAutoSwitch ?? General.AutoSwitchApplications ?? _draft.AutoSwitchProfiles;
        _manual.Visibility = _adapt.IsChecked == true ? Visibility.Collapsed : Visibility.Visible; _loading = false;
    }
    private void ChangeAdapt() { if (_loading) return; General.CircleAutoSwitch = _adapt.IsChecked == true; _manual.Visibility = _adapt.IsChecked == true ? Visibility.Collapsed : Visibility.Visible; }
    private void History(bool redo) { if (redo) Document.Redo(); else Document.Undo(); ResetNavigation(); Refresh(); }
    private void ResetNavigation() { _folder.Clear(); _selected = null; }
    private void Refresh()
    {
        _loading = true; _inherit.IsChecked = Profile.InheritCircleTrigger; _inherit.Visibility = Profile.ApplicationPath is null ? Visibility.Collapsed : Visibility.Visible; _loading = false;
        _undo.IsEnabled = Document.CanUndo; _redo.IsEnabled = Document.CanRedo;
        if (_folder.Count > 0 && RingNavigation.Resolve(Slots, _folder)?.Kind != RingSlotKind.Folder) _folder.Clear();
        _breadcrumb.Text = _folder.Count == 0 ? "Main Circle" : "Main Circle / " + RingNavigation.Resolve(Slots, _folder)!.Name;
        ClearDropVisual(); _nodeViews.Clear(); _canvas.Children.Clear();
        _canvas.Width = _folder.Count == 0 ? 440 : 680; _canvas.Height = _folder.Count == 0 ? 400 : 570;
        var center = _folder.Count == 0 ? new Point(220, 180) : new Point(340, 268);
        Put(new System.Windows.Shapes.Ellipse { Width = 208, Height = 208, Stroke = B("Line"), StrokeThickness = .7, Opacity = .5, IsHitTestVisible = false }, center, 208, 208);
        for (int i = 0; i < Slots.Count; i++) Node(Slots[i], [i], CircleVisuals.Root(center, i, 104), _folder.Count == 0 || _folder[0] == i);
        if (_folder.Count > 0)
        {
            var children = RingNavigation.Resolve(Slots, _folder)!.Children;
            int positions = Math.Min(9, children.Count + 1);
            for (int i = 0; i < children.Count; i++) Node(children[i], _folder.Append(i).ToArray(), CircleVisuals.Child(center, _folder[0], i, positions, 190), true);
            if (children.Count < 9)
            {
                int[] append = _folder.Append(children.Count).ToArray();
                var point = CircleVisuals.Child(center, _folder[0], children.Count, positions, 190);
                var placeholder = new Border { Width = 58, Height = 58, CornerRadius = new(29), Background = B("RingSelection"), BorderBrush = B("Accent"), BorderThickness = new(1), AllowDrop = true, Cursor = Cursors.Hand, ToolTip = "Drop an action here, or click to add a shortcut", Child = Text("+", 25, "Accent") };
                ((TextBlock)placeholder.Child).HorizontalAlignment = HorizontalAlignment.Center;
                System.Windows.Automation.AutomationProperties.SetName(placeholder, "Add shortcut to folder");
                _nodeViews[PathKey(append)] = placeholder;
                WireDrop(placeholder, append, true); placeholder.MouseLeftButtonUp += (_, e) => { AddShortcut(); e.Handled = true; }; Put(placeholder, point, 58, 58);
                var label = Text(children.Count == 0 ? "Add your first shortcut" : "Add shortcut", 12, "Accent"); label.Width = 160; label.TextAlignment = TextAlignment.Center; label.IsHitTestVisible = false;
                Canvas.SetLeft(label, point.X - 80); Canvas.SetTop(label, point.Y + 35); _canvas.Children.Add(label);
            }
        }
        var middle = new Border { Width = 66, Height = 66, CornerRadius = new(33), Child = Text(_folder.Count == 0 ? "M" : "‹", 24), ToolTip = "Back to main Circle", Background = B("Bg"), BorderBrush = B("MetalEdge"), BorderThickness = new(1), Cursor = Cursors.Hand };
        ((TextBlock)middle.Child).HorizontalAlignment = HorizontalAlignment.Center;
        middle.MouseLeftButtonUp += (_, _) => { if (_folder.Count > 0) _folder.RemoveAt(_folder.Count - 1); _selected = null; Refresh(); }; Put(middle, center, 66, 66);
        var slot = SelectedPath is not { } selectedPath ? null : RingNavigation.Resolve(Slots, selectedPath);
        _selection.Text = slot?.Name ?? "Select a position"; _edit.IsEnabled = _remove.IsEnabled = slot is not null; _open.Visibility = slot?.Kind == RingSlotKind.Folder ? Visibility.Visible : Visibility.Collapsed;
        bool folderContext = _selected is null && _folder.Count > 0;
        _edit.Content = folderContext ? "Folder settings" : "Edit"; _remove.Content = folderContext ? "Delete folder" : "Remove";
        if (folderContext) _open.Visibility = Visibility.Collapsed;
        _addShortcut.Visibility = _folder.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _addShortcut.IsEnabled = _folder.Count > 0 && RingNavigation.Resolve(Slots, _folder)!.Children.Count < 9;
        SetStatus(_folder.Count > 0 ? "Drop actions onto this folder or its + node · Add shortcut for a custom key combination" : "Click a folder to open it · Drop actions onto folders to add inside");
    }
    private void Node(RingSlot slot, int[] path, Point point, bool emphasized)
    {
        bool selected = _selected?.SequenceEqual(path) == true;
        var node = new Border { Width = 52, Height = 52, CornerRadius = new(26), Background = B(selected ? "RingSelection" : "Bg"), BorderBrush = B(selected ? "Accent" : "Line"), BorderThickness = new(selected ? 1.7 : 1), Opacity = emphasized ? 1 : .45, Cursor = Cursors.Hand, AllowDrop = true,
            ToolTip = slot.Name + (slot.Kind == RingSlotKind.Folder ? " · Click to open · Drop actions to add inside" : " · Double-click to edit"), Child = new TextBlock { Text = CircleVisuals.Icon(slot), FontFamily = new("Segoe Fluent Icons"), FontSize = 21, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B(CircleVisuals.Empty(slot) ? "Muted" : "Ink") } };
        System.Windows.Automation.AutomationProperties.SetName(node, slot.Name); WireDrag(node, new PlacementDrag(_profileId, null, path)); WireDrop(node, path, false);
        _nodeViews[PathKey(path)] = node;
        node.Focusable = true;
        node.KeyDown += (_, e) => { _selected = path; if (e.Key == Key.Enter) { if (slot.Kind == RingSlotKind.Folder) EnterSelected(); else EditSelected(); e.Handled = true; } else if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; } };
        node.MouseLeftButtonUp += (_, e) => { SelectNode(path); e.Handled = true; };
        node.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { _selected = path; if (slot.Kind == RingSlotKind.Folder) EnterSelected(); else EditSelected(); e.Handled = true; } };
        Put(node, point, 52, 52);
        var label = Text(CircleVisuals.Empty(slot) ? "Add action" : slot.Name, 11.5, selected ? "Accent" : "Muted"); label.Width = 118; label.TextAlignment = TextAlignment.Center; label.TextTrimming = TextTrimming.CharacterEllipsis; label.Opacity = emphasized ? 1 : .55; label.IsHitTestVisible = false;
        double labelLeft = point.X - 59, labelTop = point.Y + 30;
        if (path.Length > 1)
        {
            // Folder captions sit outside the arc, leaving the next drop target clear.
            label.Width = 110;
            if (point.X > _canvas.Width / 2 + 32) { labelLeft = point.X + 34; labelTop = point.Y - 13; label.TextAlignment = TextAlignment.Left; }
            else if (point.X < _canvas.Width / 2 - 32) { labelLeft = point.X - 144; labelTop = point.Y - 13; label.TextAlignment = TextAlignment.Right; }
            else { labelLeft = point.X - 55; labelTop = point.Y < 268 ? point.Y - 57 : point.Y + 32; }
        }
        Canvas.SetLeft(label, labelLeft); Canvas.SetTop(label, labelTop); _canvas.Children.Add(label);
        if (path.Length > 1 && slot.Action.Kind == ActionKind.Shortcut)
        {
            var combination = Text(InputPresentation.Action(slot.Action), 10, "Muted"); combination.Width = label.Width; combination.TextAlignment = label.TextAlignment; combination.TextTrimming = TextTrimming.CharacterEllipsis; combination.IsHitTestVisible = false;
            Canvas.SetLeft(combination, labelLeft); Canvas.SetTop(combination, labelTop + 16); _canvas.Children.Add(combination);
        }
    }
    private void SelectNode(int[] path)
    { _selected = path; if (RingNavigation.Resolve(Slots, path)?.Kind == RingSlotKind.Folder) EnterSelected(); else Refresh(); }
    private void AddShortcut()
    { if (_folder.Count > 0) PlaceDefinition(CircleCatalog.Actions.Single(a => a.Id == "keyboard.shortcut"), _folder.Append(RingNavigation.Resolve(Slots, _folder)!.Children.Count).ToArray(), true); }
    private void Put(FrameworkElement item, Point point, double width, double height) { Canvas.SetLeft(item, point.X - width / 2); Canvas.SetTop(item, point.Y - height / 2); _canvas.Children.Add(item); }
    private void WireDrag(FrameworkElement item, PlacementDrag payload)
    {
        Point? start = null; item.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(item);
        item.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start is not { } origin) return; var delta = e.GetPosition(item) - origin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            try { BeginDragPresentation(item, payload); DragDrop.DoDragDrop(item, new DataObject(DragFormat, payload), payload.Definition is null ? DragDropEffects.Move : DragDropEffects.Copy); }
            finally { EndDragPresentation(); }
        };
        item.GiveFeedback += (_, e) => { _dragCard?.FollowCursorOnce(); if (e.Effects == DragDropEffects.None) { ClearDropVisual(); if (_dragCard?.Intent != CircleDragIntent.Blocked) _dragCard?.SetIntent(CircleDragIntent.Waiting, "Choose a Circle position"); } e.UseDefaultCursors = true; e.Handled = true; };
        item.QueryContinueDrag += (_, e) => { if (e.EscapePressed) { e.Action = DragAction.Cancel; e.Handled = true; } };
    }
    private void WireDrop(FrameworkElement item, int[] target, bool insert)
    {
        item.DragOver += (_, e) =>
        {
            e.Handled = true; e.Effects = DragDropEffects.None;
            if (e.Data.GetData(DragFormat) is not PlacementDrag drag) return;
            bool folderPeek = QueueFolderHover(drag, target, insert);
            if (!DescribeDrop(drag, target, insert, out var plan, out string error))
            { if (!folderPeek) StopFolderHover(); ClearDropVisual(); _dragCard?.SetIntent(CircleDragIntent.Blocked, error); SetStatus(error, true); return; }
            e.Effects = drag.Definition is null ? DragDropEffects.Move : DragDropEffects.Copy;
            SetStatus(plan.Description); _dragCard?.SetIntent(plan.Intent, plan.Description); ShowDropVisual(item);
        };
        item.DragLeave += (_, _) => { StopFolderHover(); ClearDropVisual(); };
        item.Drop += (_, e) =>
        {
            e.Handled = true; e.Effects = DragDropEffects.None;
            if (e.Data.GetData(DragFormat) is not PlacementDrag drag || !DescribeDrop(drag, target, insert, out var plan, out string problem))
            { EndDragPresentation(); return; }
            // Remove the visual before opening a parameter dialog. Cancelling the dialog preserves the old action.
            EndDragPresentation(); e.Effects = drag.Definition is null ? DragDropEffects.Move : DragDropEffects.Copy;
            if (drag.Definition is not null) { PlaceDefinition(drag.Definition, target, insert); return; } if (drag.Source is null) return;
            bool ok = plan.Insert ? Document.MoveInto(drag.Source, plan.Path, out string error) : Document.Swap(drag.Source, plan.Path, out error);
            if (ok && plan.Insert) OpenContainingFolder(plan.Path); Refresh(); if (!ok) SetStatus(error, true); else { AnimatePlacement(plan.Path); if (!plan.Insert) AnimatePlacement(drag.Source); }
        };
    }
    private void PlaceDefinition(CircleActionDefinition definition, int[] target, bool insert)
    {
        if (definition.Template.Kind == RingSlotKind.Folder && RingNavigation.Resolve(Slots, target)?.Kind == RingSlotKind.Folder) { SetStatus("Choose a main Circle position for a new folder.", true); return; }
        (target, insert) = Document.DropTarget(target, insert, definition.Template.Kind);
        if (insert && RingNavigation.Resolve(Slots, target.Take(target.Length - 1).ToArray())?.Children.Count >= 9) { SetStatus("This folder already contains nine actions.", true); return; }
        if (target.Length > 1 && definition.Template.Kind == RingSlotKind.Folder) { SetStatus("Folders belong on the main Circle.", true); return; }
        var old = insert ? null : RingNavigation.Resolve(Slots, target); var slot = definition.Create();
        if (definition.NeedsConfiguration) { var form = new CircleActionWindow(slot, _service, DeviceProfiles) { Owner = DialogOwner }; if (form.ShowDialog() != true) return; slot = form.Result; }
        if (!ConfirmRemoval(old)) return; CompletePlacement(target, slot, insert);
    }
    private bool CompletePlacement(int[] target, RingSlot slot, bool insert)
    {
        bool ok = Document.Place(target, slot, insert, out string error);
        if (ok)
        {
            _selected = target;
            if (slot.Kind == RingSlotKind.Folder) { _folder.Clear(); _folder.AddRange(target); _selected = null; }
            else if (insert) OpenContainingFolder(target);
        }
        Refresh(); if (!ok) SetStatus(error, true); else AnimatePlacement(target); return ok;
    }
    private void OpenContainingFolder(int[] path) { _folder.Clear(); _folder.AddRange(path.Take(path.Length - 1)); _selected = path; }
    private bool ConfirmRemoval(RingSlot? slot) => slot?.Kind != RingSlotKind.Folder || slot.Children.Count == 0
        || new ConfirmActionWindow("Remove folder", "Remove “" + slot.Name + "” ?", "This folder contains " + slot.Children.Count + " actions. You can undo this change until you close the editor.", "Continue") { Owner = DialogOwner }.ShowDialog() == true;
    private void EnterSelected() { if (_selected is null || RingNavigation.Resolve(Slots, _selected)?.Kind != RingSlotKind.Folder) return; _folder.Clear(); _folder.AddRange(_selected); _selected = null; Refresh(); }
    private void EditSelected()
    {
        if (SelectedPath is not { } selected || RingNavigation.Resolve(Slots, selected) is not { } slot) return;
        var form = new CircleActionWindow(slot, _service, DeviceProfiles) { Owner = DialogOwner }; if (form.ShowDialog() != true) return;
        var path = selected.ToArray(); bool ok = Document.Change(root => { var value = RingNavigation.Resolve(root, path)!; var replacement = form.Result; value.Name = replacement.Name; value.Icon = replacement.Icon; value.Action = replacement.Action.DeepClone(); value.IncreaseAction = replacement.IncreaseAction.DeepClone(); value.DecreaseAction = replacement.DecreaseAction.DeepClone(); }, out string error);
        Refresh(); if (!ok) SetStatus(error, true);
    }
    private void RemoveSelected() { if (SelectedPath is not { } path || !ConfirmRemoval(RingNavigation.Resolve(Slots, path))) return; bool ok = Document.Remove(path, out string error); _selected = null; Refresh(); if (!ok) SetStatus(error, true); }
    private void RefreshCatalog()
    {
        _catalog.Children.Clear(); string query = _search.Text.Trim();
        foreach (var (name, button) in _providerButtons) { button.Background = B(_provider == name ? "RingSelection" : "Bg"); button.BorderBrush = B(_provider == name ? "Accent" : "Line"); }
        var actions = CircleCatalog.Actions.Where(a => (query.Length > 0 || a.Provider == _provider) && (query.Length == 0 || (a.Title + " " + a.Category + " " + a.Provider).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var category in actions.GroupBy(a => (a.Provider, a.Category)))
        {
            var list = new StackPanel();
            foreach (var definition in category)
            {
                var content = new DockPanel(); content.Children.Add(new TextBlock { Text = definition.Icon, FontFamily = new("Segoe Fluent Icons"), FontSize = 17, Margin = new(0, 0, 12, 0), Foreground = B("Accent") });
                var title = Text(definition.Title, 12.5); title.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(title);
                var row = new Border { Child = content, Background = B("Bg"), CornerRadius = new(8), Padding = new(12), Margin = new(0, 3, 0, 3), Cursor = Cursors.Hand, ToolTip = "Drag to a position, or double-click to add" };
                WireDrag(row, new PlacementDrag(_profileId, definition, null)); row.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { if (_selected is not null) PlaceDefinition(definition, _selected, false); else if (_folder.Count > 0) PlaceDefinition(definition, _folder.Append(RingNavigation.Resolve(Slots, _folder)!.Children.Count).ToArray(), true); else SetStatus("Select a position first.", true); e.Handled = true; } }; list.Children.Add(row);
            }
            _catalog.Children.Add(new Expander { Header = category.Key.Category, Content = list, IsExpanded = query.Length > 0, Foreground = B("Muted"), Margin = new(0, 4, 0, 10), Padding = new(0, 6, 0, 0) });
        }
        if (actions.Length == 0) _catalog.Children.Add(Text("No matching actions", 13, "Muted"));
    }
    private void SetStatus(string text, bool error = false) { _status.Text = text; _status.Foreground = B(error ? "Danger" : "Muted"); }
    private sealed record PlacementDrag(Guid ProfileId, CircleActionDefinition? Definition, int[]? Source);
    private sealed record ProfileChoice(Guid Id, string Name) { public override string ToString() => Name; }
    internal void VerifyEditing()
    {
        var prior = Slots[0].DeepClone(); var folder = CircleCatalog.Actions.Single(a => a.Id == "circle.folder").Create(); folder.Name = "Work";
        if (!Document.Place([0], folder, false, out var error) || !Document.Place([0, 0], CircleCatalog.Actions.Single(a => a.Id == "keyboard.copy").Create(), true, out error) || !Document.Swap([0, 0], [1], out error)) throw new InvalidOperationException(error);
        Document.Undo(); if (Slots[0].Children[0].Action.KeyCode != 0x43) throw new InvalidOperationException("Swap undo lost contents.");
        Document.Undo(); Document.Undo(); if (Slots[0].Name != prior.Name) throw new InvalidOperationException("History lost the original document.");
        Document.Redo(); ResetNavigation(); Refresh(); _discarded = true;
        var fixture = AppSettings.CreateDefault();
        var a = ProfileResolver.CreateDeviceDefault("verify:a", "A"); var b = ProfileResolver.CreateDeviceDefault("verify:b", "B");
        var app = ProfileResolver.CreateDeviceDefault("verify:a", "A"); app.ApplicationPath = @"C:\Apps\editor.exe"; app.Name = "Editor";
        fixture.Profiles.AddRange([a, b, app]); string before = System.Text.Json.JsonSerializer.Serialize(b);
        var editor = new RingEditorWindow(fixture, a.Id, null, "A");
        if (editor._documents.ContainsKey(b.Id)) throw new InvalidOperationException("Another mouse appeared in the Circle workspace.");
        if (!editor.CompletePlacement([3], new RingSlot { Name = "My shortcuts", Kind = RingSlotKind.Folder }, false)
            || !editor._folder.SequenceEqual(new[] { 3 }) || editor._addShortcut.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Creating a folder did not open its shortcut contents.");
        var target = editor.Document.DropTarget([3], false, RingSlotKind.Command);
        if (!editor.CompletePlacement(target.Path, new() { Name = "Switch app", Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt } }, target.Insert))
            throw new InvalidOperationException("Could not add a shortcut inside a folder.");
        target = editor.Document.DropTarget([3], false, RingSlotKind.Command);
        editor.CompletePlacement(target.Path, new() { Name = "Save as", Action = new() { Kind = ActionKind.Shortcut, KeyCode = 0x53, Modifiers = KeyModifiers.Control | KeyModifiers.Shift } }, target.Insert);
        editor.PlaceDefinition(CircleCatalog.Actions.Single(x => x.Id == "keyboard.copy"), [3], false);
        if (editor.Slots[3].Kind != RingSlotKind.Folder || editor.Slots[3].Children.Count != 3
            || editor.Slots[3].Children[0].Action.Modifiers != KeyModifiers.Alt || editor.Slots[3].Children[1].Action.KeyCode != 0x53)
            throw new InvalidOperationException("Dropping an action replaced its folder or lost distinct shortcuts.");
        editor.Document.Undo(); editor.ResetNavigation(); editor.SelectNode([3]);
        if (!editor._folder.SequenceEqual(new[] { 3 }) || editor.Slots[3].Children.Count != 2)
            throw new InvalidOperationException("Single-click folder navigation or child undo failed.");
        editor._documents[app.Id].Place([0], CircleCatalog.Actions.Single(x => x.Id == "volume.adjust").Create(), false, out error);
        editor.ApplyTo(fixture); editor._discarded = true; editor.Close();
        if (System.Text.Json.JsonSerializer.Serialize(fixture.Profiles.Single(p => p.Id == b.Id)) != before
            || fixture.Profiles.Single(p => p.Id == app.Id).RingSlots[0].Kind != RingSlotKind.Adjustment
            || fixture.Profiles.Single(p => p.Id == a.Id).RingSlots[0].Kind != RingSlotKind.Command)
            throw new InvalidOperationException("Circle workspace save crossed a mouse/application boundary.");
        Console.WriteLine("PASS Circle editor: folder creation/open, distinct custom shortcuts, drop into folder, undo and scoped save; no live profiles changed.");
    }
    internal void ShowAdjustmentPreview() { Document.Place([0], CircleCatalog.Actions.Single(x => x.Id == "volume.adjust").Create(), false, out _); _selected = [0]; Refresh(); _discarded = true; }
    internal void ShowFolderPreview(bool empty = false)
    {
        var folder = new RingSlot { Name = "My shortcuts", Kind = RingSlotKind.Folder, Icon = "\uE821" };
        if (!empty) folder.Children = [new() { Name = "Switch app", Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt } }, new() { Name = "Save as", Action = new() { Kind = ActionKind.Shortcut, KeyCode = 0x53, Modifiers = KeyModifiers.Control | KeyModifiers.Shift } }];
        Document.Place([2], folder, false, out _); _folder.Clear(); _folder.Add(2); _selected = null; Refresh(); _discarded = true;
    }
}
