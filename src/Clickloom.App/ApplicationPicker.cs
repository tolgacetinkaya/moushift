using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Clickloom.Windows;
using Microsoft.Win32;

namespace Clickloom.App;

/// <summary>An inline selection surface: browsing never launches an app or changes a saved profile.</summary>
internal sealed class ApplicationPicker : Grid, IDisposable
{
    private readonly TextBox _search = new();
    private readonly ListBox _list = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = Label("Finding your apps…", 13, "Muted");
    private readonly TextBlock _selection = Label("Choose an app to continue.", 12, "Muted");
    private readonly Button _choose = new() { Content = "Add & edit Circle", IsEnabled = false };
    private readonly Button _refresh = new() { Content = "Refresh list" };
    private readonly CheckBox _automatic = new() { Content = "Switch Circle automatically with the active app" };
    private readonly HashSet<string> _existing;
    private readonly Action<ApplicationEntry, bool> _selected;
    private readonly Window _owner;
    private readonly IReadOnlyList<ApplicationEntry>? _fixture;
    private IReadOnlyList<ApplicationEntry> _apps = [];
    private CancellationTokenSource? _scan;
    private bool _partial, _disposed, _started;

    internal ApplicationPicker(Window owner, string deviceName, IEnumerable<string> existingPaths,
        bool automatic, Action<ApplicationEntry, bool> selected, Action cancel, IReadOnlyList<ApplicationEntry>? fixture = null)
    {
        _owner = owner; _existing = new(existingPaths, StringComparer.OrdinalIgnoreCase); _selected = selected; _fixture = fixture;
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(this, System.Windows.Input.KeyboardNavigationMode.Cycle);
        Background = ThemeManager.Brush("Bg");
        var content = new Grid { Margin = new(28), MaxWidth = 1040 };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) content.RowDefinitions.Add(new() { Height = height });
        Children.Add(content);
        var header = new StackPanel();
        var back = new Button { Content = "‹ Back to Circle", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 18) };
        back.Click += (_, _) => cancel(); header.Children.Add(back);
        header.Children.Add(Label("Choose an app", 27));
        header.Children.Add(Label(deviceName + " · A separate Circle for this mouse", 14, "Muted"));
        var explanation = Label("Use a different Circle when this app is in front. Other apps keep your All applications Circle.", 13, "Muted");
        explanation.Margin = new(0, 10, 0, 18); header.Children.Add(explanation); content.Children.Add(header);
        var filters = new DockPanel { Margin = new(0, 0, 0, 14) };
        _refresh.Margin = new(12, 0, 0, 0); _refresh.Click += async (_, _) => await LoadApps(); DockPanel.SetDock(_refresh, Dock.Right); filters.Children.Add(_refresh);
        var search = new Grid(); _search.ToolTip = "Search installed apps"; System.Windows.Automation.AutomationProperties.SetName(_search, "Search installed apps");
        var hint = Label("Search installed apps…", 14, "Muted"); hint.Margin = new(14, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false;
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filter(); };
        search.Children.Add(_search); search.Children.Add(hint); filters.Children.Add(search); Grid.SetRow(filters, 1); content.Children.Add(filters);
        _list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _list.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        var stack = new FrameworkElementFactory(typeof(StackPanel)); stack.SetValue(MarginProperty, new Thickness(8, 7, 8, 7));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppRow.Name))); title.SetValue(TextBlock.FontSizeProperty, 16.0); title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); stack.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppRow.Summary))); detail.SetValue(TextBlock.ForegroundProperty, ThemeManager.Brush("Muted")); detail.SetValue(TextBlock.FontSizeProperty, 12.0); detail.SetValue(MarginProperty, new Thickness(0, 5, 0, 0)); detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); stack.AppendChild(detail);
        _list.ItemTemplate = new DataTemplate { VisualTree = stack };
        _list.SelectionChanged += (_, _) => UpdateSelection();
        _list.MouseDoubleClick += (_, _) => Choose();
        var listSurface = new Border { Child = _list, Background = ThemeManager.Brush("Panel"), CornerRadius = new(14) };
        Grid.SetRow(listSurface, 2); content.Children.Add(listSurface);
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) };
        footer.Children.Add(_status); _selection.Margin = new(0, 8, 0, 12); footer.Children.Add(_selection);
        _automatic.IsChecked = automatic; _automatic.Margin = new(0, 0, 0, 16); footer.Children.Add(_automatic);
        var buttons = new DockPanel(); _choose.Style = (Style)FindResource("Primary"); _choose.Click += (_, _) => Choose(); DockPanel.SetDock(_choose, Dock.Right); buttons.Children.Add(_choose);
        var browse = new Button { Content = "Browse for an app…", HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) => Browse(); buttons.Children.Add(browse); footer.Children.Add(buttons); Grid.SetRow(footer, 3); content.Children.Add(footer);
        Loaded += async (_, _) => { if (!_started) { _started = true; _search.Focus(); await LoadApps(); } };
        Unloaded += (_, _) => Dispose();
        if (fixture is not null) { _apps = fixture; Filter(); }
    }
    private static TextBlock Label(string text, double size, string color = "Ink") => new() { Text = text, FontSize = size, Foreground = ThemeManager.Brush(color), TextWrapping = TextWrapping.Wrap };
    private async Task LoadApps()
    {
        if (_disposed) return;
        _scan?.Cancel(); _scan?.Dispose(); _scan = new(); var token = _scan.Token;
        _refresh.IsEnabled = false; _status.Text = "Finding your apps… You can search or browse while this loads.";
        try
        {
            var result = _fixture is null ? await ApplicationCatalog.ScanAsync(token).WaitAsync(token) : new ApplicationCatalogResult(_fixture, false);
            if (_disposed || token.IsCancellationRequested) return;
            _apps = result.Applications; _partial = result.Partial; Filter();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_disposed) _status.Text = "Couldn't read the app list. Try Refresh list or Browse for an app."; }
        finally { if (!_disposed && !token.IsCancellationRequested) _refresh.IsEnabled = true; }
    }
    private void Filter()
    {
        string? selected = (_list.SelectedItem as AppRow)?.Entry.ExecutablePath;
        string query = _search.Text.Trim();
        var rows = _apps.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (a.ExecutablePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(a => new AppRow(a, a.ExecutablePath is { } path && _existing.Contains(path))).ToArray();
        _list.ItemsSource = rows; _list.SelectedItem = rows.FirstOrDefault(r => r.Entry.ExecutablePath is not null && r.Entry.ExecutablePath == selected);
        _status.Text = rows.Length == 0 ? "No apps found. Try another name or Browse for an app."
            : $"{rows.Length} apps" + (_partial ? " · Some Windows entries couldn't be read." : " · Installed and currently running apps");
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        var row = _list.SelectedItem as AppRow;
        _choose.IsEnabled = row?.Entry.CanSelect == true;
        _choose.Content = row?.Existing == true ? "Edit existing Circle" : "Add & edit Circle";
        _selection.Text = row?.Entry.Detail ?? "Not listed? Open the app and refresh, or choose its .exe with Browse.";
    }
    private void Choose()
    {
        if (_list.SelectedItem is not AppRow { Entry.CanSelect: true } row) return;
        if (_fixture is null && ApplicationCatalog.NormalizeExecutable(row.Entry.ExecutablePath) is null)
        { _status.Text = "This app moved or was removed. Refresh the list or browse for its new location."; return; }
        _selected(row.Entry, _automatic.IsChecked == true);
    }
    private void Browse()
    {
        var dialog = new OpenFileDialog { Title = "Choose an app", Filter = "Windows application (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(_owner) != true) return;
        // A late catalog result must not remove the executable just chosen by the user.
        _scan?.Cancel(); _refresh.IsEnabled = true;
        var app = new ApplicationEntry(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, "Selected file");
        _apps = ApplicationCatalog.Merge(_apps.Append(app)); _search.Text = ""; Filter();
        _list.SelectedItem = _list.Items.Cast<AppRow>().First(r => string.Equals(r.Entry.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _scan?.Cancel(); _scan?.Dispose(); }
    internal void VerifySelection()
    {
        if (_fixture is null) throw new InvalidOperationException("Use isolated fixture apps for picker checks.");
        _search.Text = "chrome";
        if (_list.Items.Count != 1) throw new InvalidOperationException("App name search failed.");
        _list.SelectedIndex = 0;
        if (!_choose.IsEnabled || (string)_choose.Content != "Edit existing Circle") throw new InvalidOperationException("Existing app Circle was not recognized.");
        _search.Text = "Windows utility"; _list.SelectedIndex = 0;
        if (_choose.IsEnabled) throw new InvalidOperationException("An unresolved executable was selectable.");
        _search.Text = "no-match";
        if (_list.Items.Count != 0 || _choose.IsEnabled) throw new InvalidOperationException("Empty search kept a stale selection.");
        _search.Text = "";
        Console.WriteLine("PASS inline app picker: search, existing profile, unresolved app and stale selection; no discovery or settings writes.");
    }
    private sealed record AppRow(ApplicationEntry Entry, bool Existing)
    {
        public string Name => Entry.Name;
        public string Summary => Existing ? "Circle already added for this mouse" : !Entry.CanSelect ? "Choose an executable with Browse" : Entry.Source + " · " + System.IO.Path.GetFileName(Entry.ExecutablePath);
    }
}
