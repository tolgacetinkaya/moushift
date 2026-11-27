using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Clickloom.Core;

namespace Clickloom.App;

public sealed partial class RingEditorWindow
{
    private bool _myActionsChanged;
    private readonly Dictionary<string, CustomCircleAction> _deletedMyActions = [];

    private CustomCircleAction? FindMyAction(string? definitionId) => CustomCircleAction.IsReference(definitionId)
        ? _draft.MyActions?.Find(action => action.DefinitionId == definitionId)
        : null;

    private void AddMyActionControls()
    {
        var create = new Button
        {
            Content = "+ New action",
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = (_draft.MyActions?.Count ?? 0) < CustomCircleAction.MaximumActions,
            Margin = new(0, 0, 0, 10)
        };
        var menu = new ContextMenu { PlacementTarget = create, Placement = PlacementMode.Bottom };
        foreach (var (name, icon, kind) in new[]
        {
            ("Keyboard shortcut", "\uE765", ActionKind.Shortcut),
            ("Open application", "\uE8A7", ActionKind.LaunchApplication),
            ("Open file", "\uE8A5", ActionKind.OpenResource),
            ("Shortcut sequence", "\uE8FD", ActionKind.Disabled)
        })
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => EditMyAction(null, new RingSlot
            {
                Name = name,
                Icon = icon,
                Action = new InputAction { Kind = kind },
                Sequence = kind == ActionKind.Disabled ? [] : null
            });
            menu.Items.Add(item);
        }
        create.Click += (_, _) => menu.IsOpen = true;
        _catalog.Children.Add(create);

        var hint = Text("Saved actions can be used in any mouse's Circle.", 12, "Muted");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new(0, 0, 0, 10);
        _catalog.Children.Add(hint);
    }

    private FrameworkElement MyActionRow(FrameworkElement content, CustomCircleAction action)
    {
        var row = new DockPanel();
        var remove = new Button { Content = CircleVisuals.IconVisual("\uE74D", 12, B("Muted")), Margin = new(4, 3, 0, 3), Padding = new(8), ToolTip = "Delete saved action" };
        var edit = new Button { Content = CircleVisuals.IconVisual("\uE70F", 12, B("Ink")), Margin = new(4, 3, 0, 3), Padding = new(8), ToolTip = "Edit saved action" };
        AutomationProperties.SetName(remove, "Delete " + action.Slot.Name);
        AutomationProperties.SetName(edit, "Edit " + action.Slot.Name);
        remove.Click += (_, _) => DeleteMyAction(action);
        edit.Click += (_, _) => EditMyAction(action, action.Slot);
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(edit);
        row.Children.Add(content);
        return row;
    }

    private void EditMyAction(CustomCircleAction? existing, RingSlot slot)
    {
        var form = new CircleActionWindow(slot, _service, libraryAction: true) { Owner = DialogOwner };
        if (form.ShowDialog() != true) return;

        if (existing is null) (_draft.MyActions ??= []).Add(new CustomCircleAction { Slot = form.Result });
        else existing.Slot = form.Result;
        SyncMyActionDraft(existing is null
            ? "Action added. Use Save changes to keep it."
            : "Saved action updated. Use Save changes to update its Circle positions.");
    }

    private void DeleteMyAction(CustomCircleAction action)
    {
        var dialog = new ConfirmActionWindow("Delete saved action", $"Delete “{action.Slot.Name}”?",
            "Circle positions using this action keep their current settings and become independent.", "Delete")
        { Owner = DialogOwner };
        if (dialog.ShowDialog() != true) return;
        _draft.MyActions?.Remove(action);
        _deletedMyActions[action.DefinitionId] = action;
        SyncMyActionDraft("Saved action removed from the library. Its Circle positions keep the action.");
    }

    private Dictionary<string, CustomCircleAction> MyActionMap()
        => (_draft.MyActions ?? []).ToDictionary(action => action.DefinitionId);

    private void SyncMyActionDraft(string status)
    {
        _myActionsChanged = true;
        var actions = MyActionMap();
        foreach (var document in _documents.Values) document.RefreshActionSnapshots(actions, _deletedMyActions);
        Refresh();
        RefreshCatalog();
        SetStatus(status);
    }

    private void ApplyMyActions(AppSettings target)
    {
        if (!_myActionsChanged) return;
        target.MyActions = _draft.MyActions?.Select(action => action.DeepClone()).ToList();
        var actions = MyActionMap();
        foreach (var profile in target.Profiles) CustomCircleAction.UpdatePlacements(profile.RingSlots, actions, _deletedMyActions);
    }
}
