using System.Text.Json;

namespace Clickloom.Core;

/// <summary>A private editing transaction. Snapshots never share objects with saved profiles.</summary>
public sealed class CircleDocument
{
    private readonly Stack<List<RingSlot>> _undo = new(), _redo = new();
    public List<RingSlot> Slots { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    private readonly string _original;
    public bool IsChanged => JsonSerializer.Serialize(Slots) != _original;
    public CircleDocument(IEnumerable<RingSlot> slots)
    {
        Slots = Copy(slots); _original = JsonSerializer.Serialize(Slots);
    }
    private static List<RingSlot> Copy(IEnumerable<RingSlot> slots) => slots.Select(s => s.DeepClone()).ToList();
    private static List<RingSlot> Page(List<RingSlot> root, IReadOnlyList<int> parent)
    {
        if (parent.Count == 0) return root;
        var folder = RingNavigation.Resolve(root, parent);
        if (folder?.Kind != RingSlotKind.Folder) throw new InvalidOperationException("Folder no longer exists.");
        return folder.Children;
    }
    public bool Change(Action<List<RingSlot>> edit, out string error)
    {
        error = "";
        var candidate = Copy(Slots);
        try { edit(candidate); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { error = e.Message; return false; }
        var fixture = AppSettings.CreateDefault(); fixture.Profiles[0].RingSlots = candidate;
        var errors = SettingsValidator.Validate(fixture);
        if (errors.Count > 0) { error = string.Join(" ", errors); return false; }
        if (JsonSerializer.Serialize(candidate) == JsonSerializer.Serialize(Slots)) return true;
        _undo.Push(Copy(Slots)); if (_undo.Count > 100) TrimHistory();
        _redo.Clear(); Slots = candidate; return true;
    }
    private void TrimHistory()
    {
        var recent = _undo.Take(100).Reverse().ToArray(); _undo.Clear();
        foreach (var entry in recent) _undo.Push(entry);
    }
    /// <summary>Folders are containers: dropping an action on one appends a child, never replaces the folder.</summary>
    public (int[] Path, bool Insert) DropTarget(IReadOnlyList<int> target, bool insert, RingSlotKind incomingKind)
    {
        if (!insert && incomingKind != RingSlotKind.Folder && RingNavigation.Resolve(Slots, target) is { Kind: RingSlotKind.Folder } folder)
            return (target.Append(folder.Children.Count).ToArray(), true);
        return (target.ToArray(), insert);
    }
    public bool Place(IReadOnlyList<int> path, RingSlot slot, bool insert, out string error) => Change(root =>
    {
        if (path.Count == 0) throw new InvalidOperationException("Choose a position.");
        var page = Page(root, path.Take(path.Count - 1).ToArray()); int index = path[^1];
        if (path.Count > 1 && slot.Kind == RingSlotKind.Folder)
            throw new InvalidOperationException("New folders belong on the main circle. Existing nested folders are preserved.");
        if (insert)
        {
            if (path.Count == 1 || page.Count >= RingNavigation.MaximumFolderItems || index < 0 || index > page.Count)
                throw new InvalidOperationException("A folder can contain up to nine actions.");
            page.Insert(index, slot.DeepClone());
        }
        else
        {
            if (index < 0 || index >= page.Count) throw new InvalidOperationException("This position no longer exists.");
            page[index] = slot.DeepClone();
        }
    }, out error);
    public bool Swap(IReadOnlyList<int> source, IReadOnlyList<int> target, out string error) => Change(root =>
    {
        if (source.Count == 0 || target.Count == 0) throw new InvalidOperationException("Choose two positions.");
        if (source.SequenceEqual(target)) return;
        if (IsAncestor(source, target) || IsAncestor(target, source)) throw new InvalidOperationException("A folder cannot be moved into itself.");
        var a = Page(root, source.Take(source.Count - 1).ToArray()); var b = Page(root, target.Take(target.Count - 1).ToArray());
        int i = source[^1], j = target[^1];
        if (i < 0 || i >= a.Count || j < 0 || j >= b.Count) throw new InvalidOperationException("Position no longer exists.");
        if (target.Count > 1 && a[i].Kind == RingSlotKind.Folder || source.Count > 1 && b[j].Kind == RingSlotKind.Folder)
            throw new InvalidOperationException("Folders stay on the main circle.");
        (a[i], b[j]) = (b[j], a[i]);
    }, out error);
    public bool MoveInto(IReadOnlyList<int> source, IReadOnlyList<int> target, out string error) => Change(root =>
    {
        if (target.Count < 2 || source.Count == 0 || IsAncestor(source, target)) throw new InvalidOperationException("Invalid folder move.");
        var a = Page(root, source.Take(source.Count - 1).ToArray()); var b = Page(root, target.Take(target.Count - 1).ToArray());
        int i = source[^1], j = target[^1];
        if (i < 0 || i >= a.Count || j < 0 || j > b.Count || a[i].Kind == RingSlotKind.Folder)
            throw new InvalidOperationException("Choose an action to move into this folder.");
        if (b.Count >= RingNavigation.MaximumFolderItems && !ReferenceEquals(a, b)) throw new InvalidOperationException("This folder already contains nine actions.");
        var item = a[i];
        if (source.Count == 1) a[i] = new RingSlot { Name = "Empty" };
        else { a.RemoveAt(i); if (ReferenceEquals(a, b) && i < j) j--; }
        b.Insert(j, item);
    }, out error);
    public bool Remove(IReadOnlyList<int> path, out string error) => Change(root =>
    {
        if (path.Count == 0) throw new InvalidOperationException("Choose an action.");
        var page = Page(root, path.Take(path.Count - 1).ToArray()); int index = path[^1];
        if (index < 0 || index >= page.Count) throw new InvalidOperationException("Position no longer exists.");
        if (path.Count == 1) page[index] = new RingSlot { Name = "Empty" }; else page.RemoveAt(index);
    }, out error);
    private static bool IsAncestor(IReadOnlyList<int> a, IReadOnlyList<int> b) => a.Count < b.Count && a.SequenceEqual(b.Take(a.Count));
    public void Undo() { if (!CanUndo) return; _redo.Push(Copy(Slots)); Slots = _undo.Pop(); }
    public void Redo() { if (!CanRedo) return; _undo.Push(Copy(Slots)); Slots = _redo.Pop(); }
}
