using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Clickloom.Core;

namespace Clickloom.App;

public sealed partial class RingEditorWindow
{
    private FrameworkElement _dragHost = null!;
    private CircleDragAdorner? _dragCard;
    private FrameworkElement? _dragSource;
    private double _dragSourceOpacity;
    private DropVisual? _dropVisual;
    private readonly Dictionary<string, Border> _nodeViews = [];
    private static string PathKey(IEnumerable<int> path) => string.Join("/", path);

    private void AttachDragLayer(Grid root)
    {
        _dragHost = root; root.AllowDrop = true;
        root.PreviewDragOver += (_, _) => _dragCard?.FollowCursorOnce();
        root.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DragFormat)) return;
            e.Handled = true; e.Effects = DragDropEffects.None; ClearDropVisual(); StopFolderHover();
            _dragCard?.SetIntent(CircleDragIntent.Waiting, "Choose a Circle position");
        };
        root.Drop += (_, e) => { if (e.Data.GetDataPresent(DragFormat)) { e.Handled = true; e.Effects = DragDropEffects.None; EndDragPresentation(); } };
    }
    private void BeginDragPresentation(FrameworkElement source, PlacementDrag payload)
    {
        EndDragPresentation();
        var slot = payload.Definition?.Create() ?? (payload.Source is null ? null : RingNavigation.Resolve(Slots, payload.Source));
        if (slot is null) return;
        _dragSource = source; _dragSourceOpacity = source.Opacity;
        _dragCard = new CircleDragAdorner(_dragHost, slot.Name, CircleVisuals.Icon(slot));
        if (SystemParameters.ClientAreaAnimation)
            source.BeginAnimation(OpacityProperty, new DoubleAnimation(_dragSourceOpacity * .38, TimeSpan.FromMilliseconds(100)));
        else source.Opacity = _dragSourceOpacity * .38;
        _dragCard.StartFollowing();
    }
    private void EndDragPresentation()
    {
        StopFolderHover(); ClearDropVisual(); _dragCard?.Dispose(); _dragCard = null;
        if (_dragSource is { } source) { source.BeginAnimation(OpacityProperty, null); source.Opacity = _dragSourceOpacity; _dragSource = null; }
    }
    private void StopFolderHover() { _hoverTimer.Stop(); _hoverFolder = null; }
    private bool QueueFolderHover(PlacementDrag drag, int[] target, bool insert)
    {
        var incoming = drag.Definition?.Template ?? (drag.Source is null ? null : RingNavigation.Resolve(Slots, drag.Source));
        bool eligible = drag.ProfileId == _profileId && !insert && incoming?.Kind != RingSlotKind.Folder
            && RingNavigation.Resolve(Slots, target)?.Kind == RingSlotKind.Folder && !_folder.SequenceEqual(target) && drag.Source?.SequenceEqual(target) != true;
        if (!eligible) return false;
        if (_hoverFolder is null || !_hoverFolder.SequenceEqual(target)) { _hoverFolder = target; _hoverTimer.Stop(); _hoverTimer.Start(); }
        return true;
    }
    private void ShowDropVisual(FrameworkElement item)
    {
        if (item is not Border border || ReferenceEquals(_dropVisual?.Border, border)) return;
        ClearDropVisual();
        _dropVisual = new(border, border.Background, border.BorderBrush, border.BorderThickness, border.RenderTransform, border.RenderTransformOrigin);
        border.Background = B("RingSelection"); border.BorderBrush = B("Accent"); border.BorderThickness = new(2);
        if (!SystemParameters.ClientAreaAnimation) return;
        var scale = new ScaleTransform(1.08, 1.08); border.RenderTransform = scale; border.RenderTransformOrigin = new(.5, .5);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(1, 1.08, 130)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(1, 1.08, 130));
    }
    private void ClearDropVisual()
    {
        if (_dropVisual is not { } visual) return; _dropVisual = null;
        visual.Border.Background = visual.Background; visual.Border.BorderBrush = visual.Stroke; visual.Border.BorderThickness = visual.Thickness;
        visual.Border.RenderTransform = visual.Transform; visual.Border.RenderTransformOrigin = visual.Origin;
    }
    private static DoubleAnimation Ease(double from, double to, int ms) => new(from, to, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
    private void AnimatePlacement(int[] path)
    {
        if (!_nodeViews.TryGetValue(PathKey(path), out var node) || !SystemParameters.ClientAreaAnimation) return;
        var scale = new ScaleTransform(1, 1); node.RenderTransform = scale; node.RenderTransformOrigin = new(.5, .5);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(.84, 1, 180)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(.84, 1, 180));
    }
    private bool DescribeDrop(PlacementDrag drag, int[] target, bool insert, out DropPlan plan, out string error)
    {
        plan = new(target, insert, CircleDragIntent.Waiting, "Choose a position"); error = "";
        if (drag.ProfileId != _profileId) { error = "Choose a position in this profile"; return false; }
        var slot = drag.Definition?.Template ?? (drag.Source is null ? null : RingNavigation.Resolve(Slots, drag.Source));
        if (slot is null) { error = "This action no longer exists"; return false; }
        if (drag.Source?.SequenceEqual(target) == true && !insert) { error = "Choose a different position"; return false; }
        if (target.Length > 1 && slot.Kind == RingSlotKind.Folder) { error = "Folders stay on the main Circle"; return false; }
        if (drag.Definition is not null && slot.Kind == RingSlotKind.Folder && RingNavigation.Resolve(Slots, target)?.Kind == RingSlotKind.Folder)
        { error = "Choose a main Circle position for a new folder"; return false; }
        var destination = Document.DropTarget(target, insert, slot.Kind);
        if (destination.Insert)
        {
            var parentPath = destination.Path.Take(destination.Path.Length - 1).ToArray(); var folder = RingNavigation.Resolve(Slots, parentPath);
            bool sameFolder = drag.Source is { Length: > 1 } source && source.Take(source.Length - 1).SequenceEqual(parentPath);
            if (folder is null || folder.Kind != RingSlotKind.Folder) { error = "Choose a folder"; return false; }
            if (folder.Children.Count >= 9 && !sameFolder) { error = "Folder is full · 9 actions"; return false; }
            plan = new(destination.Path, true, CircleDragIntent.Add, "Add inside “" + folder.Name + "”");
        }
        else
        {
            var existing = RingNavigation.Resolve(Slots, destination.Path);
            if (existing is null) { error = "Choose an existing position"; return false; }
            var intent = drag.Definition is null ? CircleDragIntent.Swap : CircleVisuals.Empty(existing) ? CircleDragIntent.Add : CircleDragIntent.Replace;
            plan = new(destination.Path, false, intent, intent switch { CircleDragIntent.Swap => "Swap with “" + existing.Name + "”", CircleDragIntent.Replace => "Replace “" + existing.Name + "”", _ => "Add to Circle" });
        }
        return true;
    }
    internal void VerifyDragPresentation()
    {
        string before = System.Text.Json.JsonSerializer.Serialize(Slots);
        var definition = CircleCatalog.Actions.Single(x => x.Id == "keyboard.copy");
        var source = new Border { Opacity = .8 }; BeginDragPresentation(source, new(_profileId, definition, null));
        if (_dragCard is not { IsHitTestVisible: false } card) throw new InvalidOperationException("Drag card blocks mouse input.");
        card.PositionAt(new Point(100, 120)); var first = card.Position; card.PositionAt(new Point(320, 270));
        if (card.Position == first) throw new InvalidOperationException("Drag card did not follow the pointer position.");
        if (DescribeDrop(new(_profileId, definition, null), [2], false, out var plan, out _)) card.SetIntent(plan.Intent, plan.Description);
        if (DescribeDrop(new(_profileId, null, [0]), [0], false, out _, out _)
            || DescribeDrop(new(_profileId, CircleCatalog.Actions.Single(x => x.Id == "circle.folder"), null), [2, 0], false, out _, out _))
            throw new InvalidOperationException("Invalid drops were advertised as allowed.");
        EndDragPresentation();
        if (_dragCard is not null || Math.Abs(source.Opacity - .8) > .001 || (AdornerLayer.GetAdornerLayer(_dragHost)?.GetAdorners(_dragHost)?.Length ?? 0) != 0)
            throw new InvalidOperationException("Drag completion left a card or faded source behind.");
        if (System.Text.Json.JsonSerializer.Serialize(Slots) != before) throw new InvalidOperationException("Drag preview changed settings without a drop.");
        Console.WriteLine("PASS drag presentation: pointer movement, mouse-transparent card, source restoration and cleanup; no settings changes.");
    }
    internal void ShowDragPresentationPreview(bool blocked = false)
    {
        EndDragPresentation(); var definition = CircleCatalog.Actions.Single(x => x.Id == "keyboard.copy");
        _dragCard = new CircleDragAdorner(_dragHost, definition.Title, definition.Icon, false);
        _dragCard.SetIntent(blocked ? CircleDragIntent.Blocked : CircleDragIntent.Add, blocked ? "Folder is full · 9 actions" : "Add inside “My shortcuts”");
        if (_nodeViews.TryGetValue("2", out var node))
        {
            _dragCard.PositionAt(node.TranslatePoint(new Point(node.ActualWidth / 2, node.ActualHeight / 2), _dragHost));
            if (!blocked) ShowDropVisual(node);
        }
        else _dragCard.PositionAt(new Point(520, 430));
    }
    private sealed record DropVisual(Border Border, Brush Background, Brush Stroke, Thickness Thickness, Transform Transform, Point Origin);
    private sealed record DropPlan(int[] Path, bool Insert, CircleDragIntent Intent, string Description);
}
