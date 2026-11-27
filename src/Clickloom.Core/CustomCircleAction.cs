using System.Text.Json.Serialization;

namespace Clickloom.Core;

public sealed class CustomCircleAction
{
    private const string Prefix = "my-action:";
    public const string Provider = "My Actions";
    public const int MaximumActions = 64;
    public const int MinimumSteps = 2;
    public const int MaximumSteps = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public RingSlot Slot { get; set; } = new();

    [JsonIgnore]
    public string DefinitionId => Prefix + Id.ToString("N");
    public static bool IsReference(string? definitionId) => definitionId?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    public CircleActionDefinition Definition() => new(DefinitionId, Provider, "Saved actions", Slot.Name, Slot.Icon, Slot);
    public CustomCircleAction DeepClone() => new() { Id = Id, Slot = Slot.DeepClone() };

    public static void UpdatePlacements(List<RingSlot> slots, IReadOnlyDictionary<string, CustomCircleAction> actions,
        IReadOnlyDictionary<string, CustomCircleAction> deleted)
    {
        foreach (var slot in slots)
        {
            if (slot.Kind == RingSlotKind.Folder)
                UpdatePlacements(slot.Children, actions, deleted);
            else if (slot.Kind == RingSlotKind.Command && IsReference(slot.DefinitionId))
            {
                bool linked = actions.TryGetValue(slot.DefinitionId, out var action);
                if (linked || deleted.TryGetValue(slot.DefinitionId, out action))
                {
                    var template = action!.Slot;
                    slot.Name = template.Name;
                    slot.Icon = template.Icon;
                    slot.Action = template.Action.DeepClone();
                    slot.Sequence = template.Sequence?.Select(step => step.DeepClone()).ToList();
                }
                if (!linked) slot.DefinitionId = "";
            }
        }
    }
}
