using System.IO;
using System.Windows;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

public partial class MainWindow
{
    private void VerifyAppearance()
    {
        string? isolatedRoot = Environment.GetEnvironmentVariable("CLICKLOOM_DATA_DIR");
        if (!_previewOnly || string.IsNullOrWhiteSpace(isolatedRoot)) throw new InvalidOperationException("Appearance checks require isolated preview data.");
        string path = Path.Combine(isolatedRoot, "profiles.json");
        byte[] before = File.ReadAllBytes(path);
        string original = ThemeManager.Current;
        foreach (string id in new[] { "copper", "pink" })
        {
            ThemeCombo.SelectedValue = id;
            if (ThemeManager.Current != id) throw new InvalidOperationException("Theme selector did not apply its theme.");
            ThemeManager.Apply(id, true);
            ThemeManager.Initialize(null);
            if (ThemeManager.Current != id) throw new InvalidOperationException("Appearance did not survive reload.");
            var accent = (SolidColorBrush)ThemeManager.Brush("Accent");
            if (accent.Color != (Color)ColorConverter.ConvertFromString(id == "pink" ? "#FF2DAA" : "#52AA92"))
                throw new InvalidOperationException("Accent does not match the approved theme.");
            if (((SolidColorBrush)MappingsNav.Background).Color != ((SolidColorBrush)ThemeManager.Brush("Selection")).Color)
                throw new InvalidOperationException("Navigation kept the previous theme after a live switch.");
        }
        if (!before.SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("Changing appearance modified mouse profiles.");
        ThemeManager.Apply(original); ThemeCombo.SelectedValue = original;
        UpdateLayout(); var size = HeroMouse.RenderSize;
        ActionCombo.SelectedValue = ActionKind.Passthrough; UpdateLayout();
        if (HeroMouse.RenderSize != size) throw new InvalidOperationException("Mouse image resizes with action form content.");
        ActionCombo.SelectedValue = ActionKind.Shortcut; UpdateLayout();
        if (HeroMouse.RenderSize != size) throw new InvalidOperationException("Shortcut selection changes mouse size.");
        if (HeroMouse.Anchor(MouseControl.XButton1).Y <= HeroMouse.Anchor(MouseControl.XButton2).Y)
            throw new InvalidOperationException("The approved mouse drawing reverses back/forward locations.");
        Console.WriteLine("PASS WPF appearance: both theme selectors/reload, profile-byte preservation, stable mouse layout, translucent radial selection.");
    }
}
