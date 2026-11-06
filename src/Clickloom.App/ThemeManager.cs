using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Clickloom.App;

internal sealed record ThemeChoice(string Id, string Name) { public override string ToString() => Name; }

internal static class ThemeManager
{
    public static readonly ThemeChoice[] Choices = [new("light", "Light"), new("dark", "Dark")];
    public static string Current { get; private set; } = "light";
    public static event Action? Changed;
    private static readonly Dictionary<string, ColorToken> Tokens = [];
    private static string PreferencesPath => Path.Combine(Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clickloom"), "appearance.json");

    public static void Initialize(string? requested)
    {
        string? saved = null;
        try { if (File.Exists(PreferencesPath)) saved = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(PreferencesPath))?.Theme; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* A bad appearance file never blocks input settings. */ }
        Apply(requested ?? saved ?? "light");
    }

    public static void Apply(string id, bool persist = false)
    {
        // Legacy theme preferences still load without rewriting any profile or preference file.
        Current = id is "dark" or "copper" or "pink" ? "dark" : "light";
        bool dark = Current == "dark";
        var resources = System.Windows.Application.Current.Resources;
        var colors = new Dictionary<string, string>
        {
            ["Bg"] = dark ? "#202126" : "#E9EBF0", ["Panel"] = dark ? "#27282E" : "#FFFFFF",
            ["Sidebar"] = dark ? "#222329" : "#E9EBF0", ["PanelLight"] = dark ? "#303138" : "#F3F4F7",
            ["Line"] = dark ? "#42444E" : "#CCD0D9", ["Ink"] = dark ? "#F2F2F5" : "#20232B",
            ["Muted"] = dark ? "#B5B7C2" : "#616672", ["Accent"] = dark ? "#45FFBA" : "#006747",
            // 0x40 alpha is 25% opacity (75% transparency); labels and outlines stay opaque.
            ["Selection"] = dark ? "#4045FFBA" : "#4000B87C", ["Hover"] = dark ? "#35363F" : "#E0E3EB",
            ["PrimaryFill"] = dark ? "#4045FFBA" : "#4000B87C", ["PrimaryInk"] = dark ? "#FFFFFF" : "#005C40",
            ["MetalEdge"] = dark ? "#42444E" : "#CCD0D9", ["Metal"] = dark ? "#222329" : "#E9EBF0",
            ["RingIdle"] = dark ? "#F027282E" : "#F5FFFFFF", ["RingSelection"] = dark ? "#4045FFBA" : "#4000B87C",
            ["Danger"] = dark ? "#FFA1AA" : "#B0293F"
        };
        // An expression keeps these brushes unfrozen. WPF templates otherwise freeze a brush
        // and leave direct C# references displaying an old theme after it is replaced.
        foreach (var (key, value) in colors)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            if (Tokens.TryGetValue(key, out var token)) token.Value = color;
            else
            {
                token = new ColorToken { Value = color }; Tokens[key] = token;
                var brush = new SolidColorBrush();
                BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(ColorToken.Value)) { Source = token, Mode = BindingMode.OneWay });
                resources[key] = brush;
            }
        }
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
    private sealed class ColorToken : INotifyPropertyChanged
    {
        private Color _value;
        public Color Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
