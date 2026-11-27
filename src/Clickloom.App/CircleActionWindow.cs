using System.Windows;
using System.Windows.Controls;
using Clickloom.Core;
using Microsoft.Win32;

namespace Clickloom.App;

/// <summary>Edits a detached action; cancelling cannot destroy the previous placement.</summary>
internal sealed class CircleActionWindow : Window
{
    private readonly TextBox _name = new() { MaxLength = 48 };
    private readonly TextBox _value = new() { MaxLength = 10000 };
    private readonly TextBox _arguments = new();
    private readonly ShortcutSelector _shortcut = new(), _up = new(), _down = new();
    private readonly ComboBox _icons = new() { DisplayMemberPath = "Name", SelectedValuePath = "Glyph" };
    private readonly ComboBox _target = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("Danger") };
    private readonly RingSlot _original;
    private readonly bool _libraryAction;
    private readonly List<ShortcutSelector> _steps = [];
    internal RingSlot Result { get; private set; }
    internal static readonly (string Name, string Glyph)[] IconChoices =
    [ ("Folder", "\uE8B7"), ("Keyboard", "\uE765"), ("Star", "\uE734"), ("Tools", "\uE713"), ("Play", "\uE768"),
      ("Work", "\uE821"), ("Code", "\uE943"), ("Web", "\uE774"), ("Copy", "\uE8C8"), ("Sound", "\uE767"), ("Mouse", "\uE962") ];
    public CircleActionWindow(RingSlot slot, Clickloom.Windows.MouseRemappingService? service, IEnumerable<MappingProfile>? profiles = null,
        bool libraryAction = false)
    {
        _original = slot.DeepClone(); Result = slot.DeepClone();
        _libraryAction = libraryAction;
        Title = slot.Sequence is not null ? "Shortcut sequence" : slot.Kind == RingSlotKind.Folder ? "Folder details" : "Action details";
        Width = 470; Height = 660; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeManager.Brush("Bg"); Foreground = ThemeManager.Brush("Ink"); FontFamily = new("Segoe UI"); FontSize = 13;
        _shortcut.CaptureService = _up.CaptureService = _down.CaptureService = service;
        var root = new DockPanel { Margin = new(26) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        footer.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new(0, 0, 8, 0) });
        var save = new Button { Content = "Apply", Style = (Style)FindResource("Primary") }; save.Click += (_, _) => Apply(); footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 24) });
        Add(form, "Name", _name); _name.Text = slot.Name;
        var choices = IconChoices.Select(x => new IconChoice(x.Name, x.Glyph)).ToList();
        if (!choices.Any(x => x.Glyph == CircleVisuals.Icon(slot))) choices.Add(new IconChoice("Current icon", CircleVisuals.Icon(slot)));
        _icons.ItemsSource = choices;
        _icons.SelectedValue = CircleVisuals.Icon(slot);
        Add(form, "Icon", _icons);
        _value.Text = slot.Action.Kind == ActionKind.Text ? slot.Action.Text : slot.Action.ApplicationPath; _arguments.Text = slot.Action.Arguments;
        _shortcut.SetShortcut(slot.Action.KeyCode, slot.Action.Modifiers);
        _up.SetShortcut(slot.IncreaseAction.KeyCode, slot.IncreaseAction.Modifiers); _down.SetShortcut(slot.DecreaseAction.KeyCode, slot.DecreaseAction.Modifiers);
        if (slot.Kind == RingSlotKind.Folder)
            form.Children.Add(Caption("A folder groups up to nine actions. Drag actions into it from the catalog. Existing contents are kept."));
        else if (slot.Kind == RingSlotKind.Adjustment) { Add(form, "Scroll up", _up); Add(form, "Scroll down", _down); }
        else if (slot.Action.Kind == ActionKind.SelectCircleProfile)
        {
            _target.ItemsSource = (profiles ?? []).Select(p => new ProfileChoice(p.Id, p.ApplicationPath is null ? "General" : p.Name)).ToArray();
            if (Guid.TryParse(slot.Action.Arguments, out var selected)) _target.SelectedValue = selected;
            Add(form, "Circle profile", _target);
            form.Children.Add(Caption("Switches only this mouse's Circle. Automatic Circle adaptation pauses until you turn it on again."));
        }
        else if (slot.Action.Kind == ActionKind.ToggleCircleAdapt) form.Children.Add(Caption("Turns automatic Circle selection on or off for this mouse. Button assignments and other mice are unchanged."));
        else if (slot.Sequence is not null) AddSequence(form, slot.Sequence, service);
        else if (slot.Action.Kind == ActionKind.Shortcut) Add(form, "Keyboard shortcut", _shortcut);
        else if (slot.Action.Kind == ActionKind.Text)
        { _value.AcceptsReturn = true; _value.TextWrapping = TextWrapping.Wrap; _value.Height = 140; Add(form, "Text to type", _value); }
        else if (slot.Action.Kind is ActionKind.LaunchApplication or ActionKind.OpenResource)
        {
            string caption = _libraryAction
                ? slot.Action.Kind == ActionKind.LaunchApplication ? "Application (.exe)" : "File path"
                : slot.DefinitionId == "open.url" ? "Web address" : "Application, file, folder or web address";
            Add(form, caption, _value);
            var browse = new Button { Content = "Choose…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
            browse.Click += (_, _) =>
            {
                if (slot.DefinitionId == "open.directory")
                { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) _value.Text = picker.FolderName; }
                else
                {
                    var picker = new OpenFileDialog { Filter = slot.Action.Kind == ActionKind.LaunchApplication ? "Applications (*.exe)|*.exe" : "All files (*.*)|*.*" };
                    if (picker.ShowDialog(this) == true) _value.Text = picker.FileName;
                }
            };
            form.Children.Add(browse);
            if (slot.Action.Kind == ActionKind.LaunchApplication && !_libraryAction) Add(form, "Arguments (optional)", _arguments);
        }
        else form.Children.Add(Caption(InputPresentation.Action(slot.Action)));
        _error.Margin = new(0, 16, 0, 0); form.Children.Add(_error);
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
    }

    private void AddSequence(StackPanel form, IReadOnlyList<InputAction> steps, Clickloom.Windows.MouseRemappingService? service)
    {
        form.Children.Add(Caption("Shortcuts run in order while the original app stays active."));
        var stepFields = new StackPanel();
        form.Children.Add(stepFields);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        var add = new Button { Content = "Add step", Margin = new(0, 0, 8, 0) };
        var remove = new Button { Content = "Remove last step" };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        form.Children.Add(buttons);

        void AddStep(InputAction? step)
        {
            var field = new ShortcutSelector { CaptureService = service };
            if (step is not null) field.SetShortcut(step.KeyCode, step.Modifiers);
            _steps.Add(field);
            var row = new StackPanel();
            Add(row, $"Step {_steps.Count}", field);
            stepFields.Children.Add(row);
        }

        void RefreshButtons()
        {
            add.IsEnabled = _steps.Count < CustomCircleAction.MaximumSteps;
            remove.IsEnabled = _steps.Count > CustomCircleAction.MinimumSteps;
        }

        foreach (var step in steps) AddStep(step);
        while (_steps.Count < CustomCircleAction.MinimumSteps) AddStep(null);
        add.Click += (_, _) => { AddStep(null); RefreshButtons(); };
        remove.Click += (_, _) =>
        {
            _steps.RemoveAt(_steps.Count - 1);
            stepFields.Children.RemoveAt(stepFields.Children.Count - 1);
            RefreshButtons();
        };
        RefreshButtons();
    }

    private void Apply()
    {
        var slot = _original.DeepClone(); slot.Name = _name.Text.Trim();
        if (_icons.SelectedItem is IconChoice icon) slot.Icon = icon.Glyph;
        if (slot.Kind == RingSlotKind.Adjustment)
        {
            slot.IncreaseAction = new() { Kind = _up.KeyCode == 0 ? ActionKind.Disabled : ActionKind.Shortcut, KeyCode = _up.KeyCode, Modifiers = _up.Modifiers };
            slot.DecreaseAction = new() { Kind = _down.KeyCode == 0 ? ActionKind.Disabled : ActionKind.Shortcut, KeyCode = _down.KeyCode, Modifiers = _down.Modifiers };
        }
        else if (slot.Sequence is not null)
            slot.Sequence = _steps.Select(step => new InputAction { Kind = ActionKind.Shortcut, KeyCode = step.KeyCode, Modifiers = step.Modifiers }).ToList();
        else if (slot.Action.Kind == ActionKind.SelectCircleProfile) slot.Action.Arguments = _target.SelectedValue is Guid target ? target.ToString() : "";
        else if (slot.Action.Kind == ActionKind.Shortcut) { slot.Action.KeyCode = _shortcut.KeyCode; slot.Action.Modifiers = _shortcut.Modifiers; }
        else if (slot.Action.Kind == ActionKind.Text) slot.Action.Text = _value.Text;
        else if (slot.Action.Kind is ActionKind.LaunchApplication or ActionKind.OpenResource)
        {
            slot.Action.ApplicationPath = _value.Text.Trim();
            slot.Action.Arguments = _libraryAction ? "" : _arguments.Text;
        }
        var fixture = AppSettings.CreateDefault(); fixture.Profiles[0].RingSlots[0] = slot;
        var errors = _libraryAction ? SettingsValidator.ValidateMyAction(slot) : SettingsValidator.Validate(fixture);
        if (errors.Count > 0) { _error.Text = string.Join(" ", errors); return; }
        Result = slot; DialogResult = true;
    }
    private static TextBlock Caption(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("Muted"), Margin = new(0, 12, 0, 12) };
    private static void Add(StackPanel form, string title, UIElement field)
    { System.Windows.Automation.AutomationProperties.SetName(field, title); form.Children.Add(new TextBlock { Text = title, Foreground = ThemeManager.Brush("Muted"), Margin = new(0, 16, 0, 8) }); form.Children.Add(field); }
    private sealed record IconChoice(string Name, string Glyph) { public override string ToString() => Name; }
    private sealed record ProfileChoice(Guid Id, string Name) { public override string ToString() => Name; }
}
