using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Clickloom.Windows;

public sealed record ApplicationEntry(string Name, string? ExecutablePath, string Source)
{
    public bool CanSelect => ExecutablePath is not null;
    public string Detail => ExecutablePath ?? "Windows did not expose an executable. Use Browse to choose it.";
}

public sealed record ApplicationCatalogResult(IReadOnlyList<ApplicationEntry> Applications, bool Partial);

/// <summary>Read-only Windows application discovery. No shell commands, launches or disk-wide scans.</summary>
public static class ApplicationCatalog
{
    private static readonly Lock ScanGate = new();
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(12);
    private static Task<ApplicationCatalogResult>? _activeScan;

    public static Task<ApplicationCatalogResult> ScanAsync(CancellationToken cancellation = default)
    {
        lock (ScanGate)
        {
            // Cancel the UI wait without spawning additional STA threads if Windows is still in COM.
            if (_activeScan is null || _activeScan.IsCompleted) _activeScan = StartScan();
            return _activeScan.WaitAsync(ScanTimeout, cancellation);
        }
    }

    private static Task<ApplicationCatalogResult> StartScan()
    {
        var completion = new TaskCompletionSource<ApplicationCatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var deadline = new CancellationTokenSource(ScanTimeout);
            try { completion.TrySetResult(Scan(deadline.Token)); }
            catch (OperationCanceledException) { completion.TrySetException(new TimeoutException("Windows application discovery timed out.")); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "Moushift application catalog" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    private static ApplicationCatalogResult Scan(CancellationToken cancellation)
    {
        var entries = new List<ApplicationEntry>(); bool partial = false;
        object? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            items = ((dynamic)folder!).Items();
            int count = ((dynamic)items).Count;
            for (int i = 0; i < Math.Min(count, 5000); i++)
            {
                cancellation.ThrowIfCancellationRequested(); object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string name = ((dynamic)item!).Name;
                    string? path = NormalizeDiscoveredExecutable(Property(item!, "System.Link.TargetParsingPath"));
                    string? appId = Property(item!, "System.AppUserModel.ID");
                    path ??= PackageApplicationPath.Resolve(appId);
                    path ??= NormalizeDiscoveredExecutable((string?)((dynamic)item!).Path);
                    if (!string.IsNullOrWhiteSpace(name)) entries.Add(new(name, path, appId?.Contains('!') == true ? "Windows app" : "Installed app"));
                }
                catch (Exception e) when (Recoverable(e)) { partial = true; }
                finally { Release(item); }
            }
            partial |= count > 5000;
        }
        catch (Exception e) when (Recoverable(e)) { partial = true; }
        finally { Release(items); Release(folder); Release(shell); }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var paths = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                foreach (string key in paths?.GetSubKeyNames() ?? [])
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        using var app = paths!.OpenSubKey(key);
                        if (NormalizeDiscoveredExecutable(app?.GetValue(null) as string) is { } path)
                        {
                            string name = FileVersionInfo.GetVersionInfo(path).ProductName ?? Path.GetFileNameWithoutExtension(path);
                            entries.Add(new(name, path, "Registered app"));
                        }
                    }
                    catch (Exception e) when (Recoverable(e)) { partial = true; }
                }
            }
            catch (Exception e) when (Recoverable(e)) { partial = true; }
        }
        // Windowed portable apps often have no installer registration. Never read window titles or arguments.
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (process.MainWindowHandle == 0 || process.Id == Environment.ProcessId) continue;
                    if (NormalizeDiscoveredExecutable(process.MainModule?.FileName) is { } path)
                        entries.Add(new(FileVersionInfo.GetVersionInfo(path).ProductName ?? Path.GetFileNameWithoutExtension(path), path, "Running app"));
                }
                catch (Exception e) when (Recoverable(e)) { /* Protected/exiting processes are not enumerable. */ }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return new(Merge(entries), partial);
    }

    public static IReadOnlyList<ApplicationEntry> Merge(IEnumerable<ApplicationEntry> entries) => entries
        .Where(e => !string.IsNullOrWhiteSpace(e.Name))
        .GroupBy(e => e.ExecutablePath ?? "unresolved:" + e.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public static string? NormalizeExecutable(string? value) => NormalizeExecutable(value, localOnly: false);

    internal static string? NormalizeDiscoveredExecutable(string? value) => NormalizeExecutable(value, localOnly: true);

    private static string? NormalizeExecutable(string? value, bool localOnly)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            // A command line, URL or shell ID is not an executable identity.
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            var fullPath = Path.GetFullPath(path);
            return (!localOnly || IsLocalDiscoveryPath(fullPath)) && File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception error) when (Recoverable(error)) { return null; }
    }

    internal static bool IsLocalDiscoveryPath(string path) =>
        Path.GetPathRoot(path) is { Length: 3 } root && char.IsAsciiLetter(root[0]) && root[1] == ':'
        && new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram;

    private static string? Property(object item, string name)
    { try { return ((dynamic)item).ExtendedProperty(name) as string; } catch (Exception e) when (Recoverable(e)) { return null; } }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static bool Recoverable(Exception e) => e is COMException or IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ArgumentException or InvalidOperationException or NotSupportedException
        or System.ComponentModel.Win32Exception or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.Xml.XmlException;
}
