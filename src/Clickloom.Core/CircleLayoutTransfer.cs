using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clickloom.Core;

public enum CircleTemplate { Browser, Media, WindowManagement }

public sealed record PortableCircleLayout(List<RingSlot> Slots, int OmittedActions);
public sealed record PortableCircleExport(byte[] Content, int OmittedActions);

public static class CircleLayoutTransfer
{
    private const int FileVersion = 1;
    private const int MainSlotCount = 8;
    private const int MaximumFileBytes = 512 * 1024;
    private const string Folder = "folder";
    private const string Empty = "empty";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 12,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, CircleActionDefinition> PortableActions =
        CircleCatalog.Actions.Where(action => !action.NeedsConfiguration
                && (action.Template.Kind == RingSlotKind.Adjustment || action.Template.Action.Kind == ActionKind.Shortcut))
            .Concat(BrowserShortcuts()).ToDictionary(action => action.Id, StringComparer.Ordinal);

    public static List<RingSlot> CreateTemplate(CircleTemplate template)
    {
        string[] actions = template switch
        {
            CircleTemplate.Browser => ["browser.newTab", "browser.reopenTab", "browser.closeTab", "browser.nextTab",
                "browser.address", "browser.reload", "navigation.back", "navigation.forward"],
            CircleTemplate.Media => ["media.play", "media.next", "volume.adjust", "volume.up",
                "media.stop", "volume.down", "volume.mute", "media.previous"],
            CircleTemplate.WindowManagement => ["navigation.tasks", "system.snapRight", "system.maximize", "navigation.nextDesktop",
                "navigation.desktop", "navigation.previousDesktop", "system.minimize", "system.snapLeft"],
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
        return actions.Select(id => PortableActions[id].Create()).ToList();
    }

    public static PortableCircleExport Export(IReadOnlyList<RingSlot> slots)
    {
        int omitted = 0;
        var layout = new LayoutFile(FileVersion, WriteSlots(slots, 0, ref omitted));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(layout, JsonOptions);
        if (bytes.Length > MaximumFileBytes)
            throw new InvalidDataException("Circle layout files must be 512 KB or smaller.");
        return new(bytes, omitted);
    }

    public static PortableCircleLayout Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var bytes = reader.ReadBytes(MaximumFileBytes + 1);
        if (bytes.Length > MaximumFileBytes)
            throw new InvalidDataException("Circle layout files must be 512 KB or smaller.");

        var layout = JsonSerializer.Deserialize<LayoutFile>(bytes, JsonOptions);
        if (layout?.Version != FileVersion || layout.Slots is null)
            throw new InvalidDataException("This Circle layout file version is unsupported.");

        int omitted = 0;
        var slots = ReadSlots(layout.Slots, 0, ref omitted);
        return new(slots, omitted);
    }

    private static List<LayoutSlot?> WriteSlots(IReadOnlyList<RingSlot> slots, int depth, ref int omitted)
    {
        ValidateSlotCount(slots.Count, depth);
        var portableSlots = new List<LayoutSlot?>(slots.Count);
        foreach (var slot in slots)
            portableSlots.Add(WriteSlot(slot, depth, ref omitted));
        return portableSlots;
    }

    private static LayoutSlot WriteSlot(RingSlot slot, int depth, ref int omitted)
    {
        if (slot.Kind == RingSlotKind.Folder)
            return new(Folder, PortableLabel(slot.Name), WriteSlots(slot.Children, depth + 1, ref omitted));

        bool privateAction = slot.DefinitionId?.StartsWith("my-action:", StringComparison.Ordinal) == true;
        var portableAction = PortableActions.Values.FirstOrDefault(definition => Matches(slot, definition.Template));
        // Recreate commands with their catalog titles and without references to local definitions.
        if (portableAction is not null) return new(portableAction.Id);

        if (slot.Kind != RingSlotKind.Command || slot.Action.Kind != ActionKind.Disabled || privateAction)
            omitted++;
        return new(Empty);
    }

    private static List<RingSlot> ReadSlots(List<LayoutSlot?> slots, int depth, ref int omitted)
    {
        ValidateSlotCount(slots.Count, depth);

        var result = new List<RingSlot>(slots.Count);
        foreach (var item in slots)
        {
            if (item?.Action is null || (item.Action != Folder && item.Children is not null))
                throw new InvalidDataException("The Circle layout contains an invalid position.");

            RingSlot slot;
            if (item.Action == Folder)
            {
                slot = CircleCatalog.Actions.Single(action => action.Id == "circle.folder").Create();
                slot.Children = ReadSlots(item.Children ?? [], depth + 1, ref omitted);
                if (PortableLabel(item.Label) is { } label) slot.Name = label;
            }
            else if (PortableActions.TryGetValue(item.Action, out var action))
            {
                slot = action.Create();
            }
            else
            {
                if (item.Action != Empty) omitted++;
                result.Add(new() { Name = "Action " + (result.Count + 1) });
                continue;
            }

            result.Add(slot);
        }
        return result;
    }

    private static void ValidateSlotCount(int count, int depth)
    {
        if (depth > RingNavigation.MaximumFolderDepth || (depth == 0 && count != MainSlotCount)
            || (depth > 0 && count > RingNavigation.MaximumFolderItems))
            throw new InvalidDataException("A Circle needs eight main positions and supports two folder levels with up to nine items each.");
    }

    private static bool Matches(RingSlot slot, RingSlot template) => slot.Kind == template.Kind && slot.Kind switch
    {
        RingSlotKind.Command => MatchesShortcut(slot.Action, template.Action),
        RingSlotKind.Adjustment => MatchesShortcut(slot.IncreaseAction, template.IncreaseAction)
            && MatchesShortcut(slot.DecreaseAction, template.DecreaseAction),
        _ => false
    };

    private static bool MatchesShortcut(InputAction action, InputAction template) =>
        action.Kind == ActionKind.Shortcut && template.Kind == ActionKind.Shortcut
        && action.KeyCode == template.KeyCode && action.Modifiers == template.Modifiers;

    private static string? PortableLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 48
            || label.Any(character => char.IsControl(character) || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format
                || character is '/' or '\\' or ':' or '@'))
            return null;
        return label.Trim();
    }

    private static IEnumerable<CircleActionDefinition> BrowserShortcuts()
    {
        yield return BrowserShortcut("browser.newTab", "New tab", "\uE710", 0x54);
        yield return BrowserShortcut("browser.reopenTab", "Reopen closed tab", "\uE7A7", 0x54, KeyModifiers.Control | KeyModifiers.Shift);
        yield return BrowserShortcut("browser.closeTab", "Close tab", "\uE711", 0x57);
        yield return BrowserShortcut("browser.nextTab", "Next tab", "\uE72A", 0x09);
        yield return BrowserShortcut("browser.address", "Address bar", "\uE721", 0x4C);
        yield return BrowserShortcut("browser.reload", "Reload page", "\uE72C", 0x52);
    }

    private static CircleActionDefinition BrowserShortcut(string id, string name, string icon, int key,
        KeyModifiers modifiers = KeyModifiers.Control) =>
        new(id, "System Actions", "Navigation", name, icon,
            new() { Action = new() { Kind = ActionKind.Shortcut, KeyCode = key, Modifiers = modifiers } });

    private sealed record LayoutFile(int Version, List<LayoutSlot?> Slots);
    private sealed record LayoutSlot(string? Action, string? Label = null, List<LayoutSlot?>? Children = null);
}
