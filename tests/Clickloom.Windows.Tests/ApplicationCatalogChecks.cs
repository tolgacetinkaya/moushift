using Clickloom.Windows;

internal static class ApplicationCatalogChecks
{
    internal static void Run()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"); File.WriteAllText(file, "fixture only");
        try
        {
            Require(ApplicationCatalog.NormalizeExecutable('"' + file + '"') == file, "Quoted executable was not resolved.");
            Require(ApplicationCatalog.NormalizeExecutable('"' + file + "\" --run") is null, "Command line became an executable identity.");
            Require(ApplicationCatalog.NormalizeExecutable("shell:AppsFolder\\Example!App") is null, "Shell app ID became a path.");
            Require(ApplicationCatalog.NormalizeExecutable(file + ".missing.exe") is null, "Missing executable accepted.");
            var entries = ApplicationCatalog.Merge([new("Editor", file, "Installed app"), new("Editor alias", file.ToUpperInvariant(), "Registered app"), new("Editor", file + "2", "Running app"), new("Unresolved app", null, "Windows app")]);
            Require(entries.Count == 3, "Deduplication must use executable paths, not display names.");
            Require(entries.Single(e => e.ExecutablePath is null).CanSelect == false, "Unresolved apps must not be selectable.");
        }
        finally { File.Delete(file); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
