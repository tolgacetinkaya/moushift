using System.IO;
using System.Text.Json;

namespace Clickloom.App;

internal sealed record WorkspacePreferences(bool SidebarCollapsed = false, double InspectorWidth = 420)
{
    public static WorkspacePreferences Load(string folder)
    {
        try
        {
            var value = JsonSerializer.Deserialize<WorkspacePreferences>(File.ReadAllText(Path.Combine(folder, "workspace.json")));
            return value is not null && double.IsFinite(value.InspectorWidth)
                ? value with { InspectorWidth = Math.Clamp(value.InspectorWidth, 320, 640) } : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "workspace.json"), temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}
