using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Clickloom.Windows;

internal static class PackageApplicationPath
{
    // Documented AppModel queries; reads only packages registered for the current user.
    internal static string? Resolve(string? appId)
    {
        if (appId is null || appId.Split('!') is not [var family, var application]) return null;
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(family, ref count, null, ref length, 0) != 122 || count == 0 || count > 512 || length > 1_048_576) return null;
        var names = new nint[count]; nint buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            if (GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) != 0) return null;
            foreach (var ptr in names.Take((int)count))
            {
                string? fullName = Marshal.PtrToStringUni(ptr); if (fullName is null) continue;
                uint size = 0;
                if (GetPackagePathByFullName(fullName, ref size, null) != 122 || size > 32768) continue;
                var path = new StringBuilder((int)size);
                if (GetPackagePathByFullName(fullName, ref size, path) != 0) continue;
                string root = path.ToString();
                using var reader = XmlReader.Create(Path.Combine(root, "AppxManifest.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
                var manifest = XDocument.Load(reader);
                var app = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == application);
                string? executable = (string?)app?.Attribute("Executable");
                if (string.IsNullOrWhiteSpace(executable) || Path.IsPathFullyQualified(executable)) continue;
                string candidate = Path.GetFullPath(Path.Combine(root, executable));
                if (candidate.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                    && ApplicationCatalog.NormalizeExecutable(candidate) is { } resolved) return resolved;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count, [Out] nint[]? names, ref uint length, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
}
