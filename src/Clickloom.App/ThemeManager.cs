using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clickloom.App;

internal sealed record ThemeChoice(string Id, string Name) { public override string ToString() => Name; }

internal static class ThemeManager
{
    public static readonly ThemeChoice[] Choices = [new("light", "Light"), new("dark", "Dark")];
    public static string Current { get; private set; } = "light";
    public static event Action? Changed;
    private static readonly Dictionary<string, ColorToken> Tokens = [];
    private static readonly Lazy<BitmapImage> DarkWorkspace = new(() => LoadWorkspace("workspace-machined.png"));
    private static readonly Lazy<BitmapImage> LightWorkspace = new(() => LoadWorkspace("workspace-machined-light.png"));
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
        resources["WorkspaceArtwork"] = dark ? DarkWorkspace.Value : LightWorkspace.Value;
        var colors = new Dictionary<string, string>
        {
            ["Bg"] = dark ? "#21262A" : "#EBF0F1", ["Panel"] = dark ? "#282E32" : "#FAFCFC",
            ["Sidebar"] = dark ? "#212629" : "#E4EBED", ["PanelLight"] = dark ? "#323B3F" : "#EAF0F1",
            ["Line"] = dark ? "#526065" : "#C1CDD1", ["Ink"] = dark ? "#F2F7F6" : "#1E2B30",
            ["Muted"] = dark ? "#BAC9C9" : "#4E6269", ["Accent"] = dark ? "#8DDDC8" : "#216957",
            // 0x40 alpha is 25% opacity (75% transparency); labels and outlines stay opaque.
            ["Selection"] = dark ? "#405CB7A4" : "#4066B29F", ["Hover"] = dark ? "#3A484B" : "#DFEEEA",
            ["PrimaryFill"] = dark ? "#A4DFD1" : "#266956", ["PrimaryInk"] = dark ? "#142824" : "#FFFFFF",
            ["MetalEdge"] = dark ? "#536164" : "#C4D0D4", ["Metal"] = dark ? "#262D30" : "#EAF0F1",
            ["WorkspaceScrim"] = dark ? "#60191F22" : "#47EDF3F4",
            ["WorkspaceChrome"] = dark ? "#DD242C30" : "#EFF9FCFD",
            ["TransparentBrush"] = "#00000000",
            ["ControlSurface"] = dark ? "#334145" : "#FBFDFE",
            ["ControlEdge"] = dark ? "#798F92" : "#7E999A",
            ["RingIdle"] = dark ? "#F0283135" : "#F5F6FCFC", ["RingSelection"] = dark ? "#4077D6C1" : "#405FB29D",
            ["CircleGlassTop"] = dark ? "#A0687E83" : "#A8F5FDFF",
            ["CircleGlassMid"] = dark ? "#80263135" : "#80F8FCFD",
            ["CircleGlassBottom"] = dark ? "#66192329" : "#66C2D1D6",
            ["CircleGlassEdge"] = dark ? "#AD8DA7AB" : "#A17B959B",
            ["CircleInkHalo"] = dark ? "#000000" : "#FFFFFF",
            ["DashboardCard"] = dark ? "#CA222B2E" : "#EAFBFFFF",
            ["DashboardRaised"] = dark ? "#CE354345" : "#EAE4EEEC",
            ["DashboardStage"] = dark ? "#43232A2D" : "#52F7FCFC",
            ["DashboardPill"] = dark ? "#E0374848" : "#E5EFEC",
            ["DashboardEdge"] = dark ? "#8B7D9194" : "#B895A9AE",
            ["DashboardHover"] = dark ? "#F23B5252" : "#F1FAF8",
            ["DashboardGlow"] = dark ? "#55769097" : "#6196AEB3",
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
    private static BitmapImage LoadWorkspace(string name)
    {
        var bitmap = new BitmapImage(new Uri("pack://application:,,,/Assets/" + name));
        bitmap.Freeze();
        return bitmap;
    }
    private sealed record Appearance(string Theme);
    private sealed class ColorToken : INotifyPropertyChanged
    {
        private Color _value;
        public Color Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
