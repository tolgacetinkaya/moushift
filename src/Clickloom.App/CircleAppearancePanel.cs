using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Clickloom.Core;

namespace Clickloom.App;

internal sealed class CircleAppearancePanel : ScrollViewer
{
    private static readonly (string Name, string Hex)[] Swatches =
    [
        ("Ink", "#142328"), ("Slate", "#415B68"), ("Mist", "#CBD9DE"), ("Snow", "#F2F8FA"),
        ("Mint", "#7DE6C7"), ("Teal", "#218577"), ("Sky", "#69C5F4"), ("Blue", "#5167CB"),
        ("Lilac", "#AE8CFF"), ("Rose", "#EF709F"), ("Coral", "#F6856A"), ("Gold", "#F6C563")
    ];
    private readonly StackPanel _content = new() { Margin = new(24, 14, 24, 18) };
    private readonly string _deviceKey;
    private readonly Action _changed;
    private readonly Func<RingSlot?>? _selectedItem;
    private readonly Action<RingOutline?>? _setOutline;
    private readonly TextBlock _warning = Label("", "Danger");
    private Button _backgroundEdit = null!;

    internal CircleAppearancePanel(string deviceKey, Action changed, Func<RingSlot?>? selectedItem = null, Action<RingOutline?>? setOutline = null)
    {
        _deviceKey = deviceKey;
        _changed = changed;
        _selectedItem = selectedItem;
        _setOutline = setOutline;
        Content = _content;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Refresh();
    }

    internal void Refresh()
    {
        var palette = ThemeManager.CirclePaletteFor(_deviceKey);
        var background = (SolidColorBrush)CircleVisuals.LabelSurface(palette);
        var ink = (SolidColorBrush)CircleVisuals.LabelInk(palette);
        _content.Children.Clear();
        var slot = _selectedItem?.Invoke();
        if (slot is not null)
            AddItemOutline(slot, palette);
        else if (_selectedItem is not null)
            _content.Children.Add(Label("Select an item on the Circle to customize its outline.", "Muted"));
        _content.Children.Add(Heading("Mouse colors"));
        _content.Children.Add(Label("Applies to this mouse across all its apps.", "Muted"));
        _backgroundEdit = AddColor("Background", palette.Background, background.Color, value => Save(palette with { Background = value }));
        AddColor("Text and icons", palette.Text, ink.Color, value => Save(palette with { Text = value }));
        _content.Children.Add(new Border
        {
            Background = background, CornerRadius = new(9), Margin = new(0, 12, 0, 4), Padding = new(12),
            Child = new TextBlock { Text = "Circle text preview", Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold }
        });
        _warning.Text = CircleVisuals.Contrast(background.Color, ink.Color) < 4.5
            ? "Low contrast. Try a lighter or darker text color." : "";
        if (SystemParameters.HighContrast)
            _warning.Text = "Windows high contrast colors take precedence while enabled.";
        _content.Children.Add(_warning);

        _content.Children.Add(Heading("Default folder outline"));
        AddOutlineColors(palette.Folder is { Length: > 0 } custom ? custom : CircleVisuals.DefaultFolderColors(),
            colors => Save(palette with { Folder = colors }), palette.Folder is { Length: > 0 } ? () => Save(palette with { Folder = null }) : null);
    }

    private void AddItemOutline(RingSlot slot, CircleColors palette)
    {
        _content.Children.Add(Heading($"{slot.Name} · Outline"));
        var outline = slot.Outline;
        var mode = new ComboBox { ItemsSource = new[] { "Default", "Custom", "Off" }, SelectedIndex = outline is null ? 0 : outline.Enabled ? 1 : 2 };
        AutomationProperties.SetName(mode, "Item outline");
        RingOutline Editable() => outline?.DeepClone() ?? new RingOutline
        {
            Colors = [.. (palette.Folder is { Length: > 0 } colors ? colors : CircleVisuals.DefaultFolderColors())]
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex == 0) { _setOutline!(null); return; }
            var updated = Editable();
            updated.Enabled = mode.SelectedIndex == 1;
            _setOutline!(updated);
        };
        _content.Children.Add(mode);
        if (outline is not { Enabled: true }) return;
        _content.Children.Add(Heading("Outline thickness"));
        var thickness = new ComboBox();
        for (double value = RingOutline.MinimumThickness; value <= RingOutline.MaximumThickness; value += .5)
            thickness.Items.Add(value);
        if (!thickness.Items.Contains(outline.Thickness)) thickness.Items.Add(outline.Thickness);
        thickness.SelectedItem = outline.Thickness;
        AutomationProperties.SetName(thickness, "Outline thickness");
        thickness.SelectionChanged += (_, _) =>
        {
            if (thickness.SelectedItem is not double value) return;
            var updated = Editable();
            updated.Thickness = value;
            _setOutline!(updated);
        };
        _content.Children.Add(thickness);
        AddOutlineColors([.. outline.Colors], colors =>
        {
            var updated = Editable();
            updated.Colors = [.. colors];
            _setOutline!(updated);
        });
    }

    private void AddOutlineColors(string[] colors, Action<string[]> save, Action? restore = null)
    {
        _content.Children.Add(Label("Choose one color for a moving highlight, or add colors for a flowing gradient.", "Muted"));
        for (int i = 0; i < colors.Length; i++)
        {
            int index = i;
            var pick = Swatch($"Outline color {i + 1}", colors[i], CircleColors.Parse(colors[i])!.Value, picked =>
            {
                var next = (string[])colors.Clone();
                next[index] = picked;
                save(next);
            });
            var remove = new Button { Content = "Remove", IsEnabled = colors.Length > 1 };
            AutomationProperties.SetName(remove, $"Remove outline color {i + 1}");
            remove.Click += (_, _) => save(colors.Where((_, j) => j != index).ToArray());
            _content.Children.Add(Row(pick, remove));
        }
        var add = new Button { Content = "+ Add color", IsEnabled = colors.Length < CircleColors.MaximumFolderColors };
        add.Click += (_, _) => OpenPicker("Add outline color", CircleColors.Parse(colors[^1])!.Value,
            color => save(colors.Append(color).ToArray()));
        if (restore is null) _content.Children.Add(add);
        else
        {
            var reset = new Button { Content = "Reset palette" };
            reset.Click += (_, _) => restore();
            _content.Children.Add(Row(add, reset));
        }
    }

    private Button AddColor(string title, string? value, Color effective, Action<string?> change)
    {
        _content.Children.Add(Heading(title));
        var reset = new Button { Content = "Auto", IsEnabled = value is not null, ToolTip = "Use the automatic color" };
        AutomationProperties.SetName(reset, $"Use automatic {title.ToLowerInvariant()} color");
        reset.Click += (_, _) => change(null);
        var edit = Swatch(title, value ?? "Automatic", effective, color => change(color));
        _content.Children.Add(Row(edit, reset));
        return edit;
    }

    private Button Swatch(string name, string value, Color color, Action<string> change)
    {
        var swatch = new Border
        {
            Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0),
            Background = new SolidColorBrush(color), BorderBrush = ThemeManager.Brush("ControlEdge"), BorderThickness = new(1), CornerRadius = new(3)
        };
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, new TextBlock { Text = value } } },
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(button, $"{name}, {value}");
        button.Click += (_, _) => OpenPicker(name, color, change);
        return button;
    }

    private void OpenPicker(string title, Color initial, Action<string> change)
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold });
        var preview = new Border { Height = 48, CornerRadius = new(8), Margin = new(0, 12, 0, 16), BorderBrush = ThemeManager.Brush("ControlEdge"), BorderThickness = new(1) };
        _content.Children.Add(preview);
        var options = new UniformGrid { Columns = 4 };

        var hex = new TextBox { Text = CircleColors.Format(initial), Margin = new(0, 6, 0, 0), MaxLength = 7 };
        AutomationProperties.SetName(hex, "Hex color");
        var picker = new CircleColorPicker(initial, color => hex.Text = CircleColors.Format(color));
        _content.Children.Add(picker);
        _content.Children.Add(options);
        var choices = new List<(Button Button, string Hex)>();
        foreach (var (name, value) in Swatches)
        {
            var color = new SolidColorBrush(CircleColors.Parse(value)!.Value);
            var sample = new Button
            {
                Content = new Border { Height = 20, CornerRadius = new(4), Background = color },
                Margin = new(2), Padding = new(4), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = $"{name} · {value}"
            };
            AutomationProperties.SetName(sample, $"{name}, {value}");
            sample.Click += (_, _) => hex.Text = value;
            choices.Add((sample, value));
            options.Children.Add(sample);
        }
        _content.Children.Add(Heading("Hex color"));
        _content.Children.Add(hex);
        var validation = Label("", "Danger");
        _content.Children.Add(validation);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Refresh();
        var apply = new Button { Content = "Apply color", Style = (Style)FindResource("Primary") };
        Color? Selected() => CircleColors.Parse(hex.Text.StartsWith('#') ? hex.Text : "#" + hex.Text);
        void Update()
        {
            var selectedColor = Selected();
            if (selectedColor is { } color)
            {
                picker.SetColor(color);
                preview.Background = new SolidColorBrush(color);
                validation.Text = "";
                apply.IsEnabled = true;
            }
            else
            {
                validation.Text = "Enter six hex digits, for example #7DE6C7.";
                apply.IsEnabled = false;
            }
            foreach (var (button, value) in choices)
            {
                bool selected = selectedColor == CircleColors.Parse(value);
                button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "ControlEdge");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
            }
        }
        apply.Click += (_, _) => { if (Selected() is { } color) change(CircleColors.Format(color)); };
        hex.TextChanged += (_, _) => Update();
        hex.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && apply.IsEnabled)
            {
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                args.Handled = true;
            }
        };
        _content.Children.Add(Row(apply, cancel));
        Update();
        ScrollToTop();
        picker.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    internal void ShowBackgroundPicker() => _backgroundEdit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal void VerifyPicker()
    {
        Button Command(string label) => _content.Children.OfType<DockPanel>().SelectMany(row => row.Children.OfType<Button>())
            .Single(button => Equals(button.Content, label));

        ShowBackgroundPicker();
        var hex = _content.Children.OfType<TextBox>().Single();
        hex.Text = "bad";
        if (Command("Apply color").IsEnabled) throw new InvalidOperationException("Invalid custom color can be applied.");
        var swatch = _content.Children.OfType<UniformGrid>().Single().Children.OfType<Button>().First();
        swatch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (hex.Text != Swatches[0].Hex) throw new InvalidOperationException("The color swatch did not update the picker.");
        var before = ThemeManager.CirclePaletteFor(_deviceKey);
        Command("Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (ThemeManager.CirclePaletteFor(_deviceKey) != before) throw new InvalidOperationException("Cancel saved the picked color.");

        ShowBackgroundPicker();
        _content.Children.OfType<TextBox>().Single().Text = "a1b2c3";
        Command("Apply color").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (ThemeManager.CirclePaletteFor(_deviceKey).Background != "#A1B2C3")
            throw new InvalidOperationException("Applying a custom color did not save it for this mouse.");
    }

    private void Save(CircleColors palette)
    {
        try { ThemeManager.SetCirclePalette(_deviceKey, palette); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Refresh();
            _warning.Text = "Could not save Circle colors.";
            return;
        }
        Refresh();
        _changed();
    }

    private static DockPanel Row(Button main, Button side)
    {
        side.Margin = new(6, 0, 0, 0);
        DockPanel.SetDock(side, Dock.Right);
        return new DockPanel { Margin = new(0, 6, 0, 0), Children = { side, main } };
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 16, 0, 0) };
    private static TextBlock Label(string text, string brush)
        => new() { Text = text, Foreground = ThemeManager.Brush(brush), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
}
