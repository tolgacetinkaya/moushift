using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clickloom.App;

internal sealed record ThemeChoice(string Id, string Name) { public override string ToString() => Name; }

internal static class ThemeManager
{
    public static readonly ThemeChoice[] Choices = [new("copper", "Copper + Mint"), new("pink", "Gunmetal + Pink")];
    public static string Current { get; private set; } = "copper";
    public static event Action? Changed;
    private static string PreferencesPath => Path.Combine(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickloom"), "appearance.json");

    public static void Initialize(string? requested)
    {
        string? saved = null;
        try { if (File.Exists(PreferencesPath)) saved = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(PreferencesPath))?.Theme; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* A bad appearance file never blocks input settings. */ }
        Apply(requested ?? saved ?? "copper");
    }

    public static void Apply(string id, bool persist = false)
    {
        Current = id == "pink" ? "pink" : "copper";
        bool pink = Current == "pink";
        var resources = System.Windows.Application.Current.Resources;
        var colors = new Dictionary<string, string>
        {
            ["Bg"] = pink ? "#1A1920" : "#171E1C", ["Panel"] = pink ? "#202127" : "#1B2521",
            ["PanelLight"] = pink ? "#272830" : "#24312B", ["Line"] = pink ? "#41434D" : "#3D5148",
            ["Ink"] = pink ? "#F0EDF4" : "#E8F2ED", ["Muted"] = pink ? "#B9B5C2" : "#B4C8BE",
            ["Accent"] = pink ? "#FF2DAA" : "#52AA92", ["Selection"] = pink ? "#543E2639" : "#A6243B33",
            ["Hover"] = pink ? "#303039" : "#293D33", ["PrimaryFill"] = pink ? "#FF2DAA" : "#306B59",
            ["PrimaryInk"] = pink ? "#17141C" : "#F0FFF9", ["MetalEdge"] = pink ? "#737680" : "#B97550",
            ["RingIdle"] = pink ? "#ED1D1D23" : "#ED17211C", ["RingSelection"] = pink ? "#42FF2DAA" : "#4852AA92",
            ["Danger"] = "#FF8D8D"
        };
        // Keep the existing brush instances alive: C#-created dialog controls also update.
        foreach (var (key, value) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            if (resources[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = color;
            else resources[key] = new SolidColorBrush(color);
        }
        var texture = new BitmapImage(new Uri($"pack://application:,,,/Assets/{(pink ? "gunmetal" : "copper")}-metal.png"));
        resources["Metal"] = new ImageBrush(texture) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center, Opacity = .82 };
        if (persist)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            string temporary = PreferencesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Appearance(Current)));
            File.Move(temporary, PreferencesPath, true);
        }
        Changed?.Invoke();
    }

    public static Brush Brush(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    private sealed record Appearance(string Theme);
}
