using System.Text.RegularExpressions;

namespace Clickloom.Windows;

/// <summary>Routes only unambiguous driver hardware identities to a single Windows mouse connection.</summary>
internal sealed class DeviceRouteTable
{
    private readonly Dictionary<string, string> _routes;
    public static readonly DeviceRouteTable Empty = new(new());
    private DeviceRouteTable(Dictionary<string, string> routes) => _routes = routes;
    public int Count => _routes.Count;
    public string? PhysicalKey(string source) => _routes.GetValueOrDefault(source);
    public bool ContainsDevice(string physicalKey) => _routes.ContainsValue(physicalKey);
    public bool SameAs(DeviceRouteTable other) => _routes.Count == other._routes.Count && _routes.All(p => other._routes.GetValueOrDefault(p.Key) == p.Value);
    public static DeviceRouteTable Build(IEnumerable<DeviceSourceInfo> sources, IEnumerable<MouseConnection> connections)
    {
        var physical = connections.Where(c => !c.IsAuxiliary).ToArray();
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var candidates = physical.Where(c => c.DevicePaths.Any(path => Matches(source.HardwareId, path))).ToArray();
            if (candidates.Length == 1) routes[source.Key] = candidates[0].Key;
        }
        return new(routes);
    }
    private static bool Matches(string source, string path)
    {
        static string Part(string value, string token) => Regex.Match(value, token + @"[_&](?:02)?([0-9A-F]{4})(?![0-9A-F])", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
        string vendor = Part(source, "VID"), product = Part(source, "PID");
        if (vendor.Length == 0 || product.Length == 0 || vendor != Part(path, "VID") || product != Part(path, "PID")) return false;
        foreach (string token in new[] { "MI", "COL" })
        {
            string expression = token + @"[_&]?([0-9A-F]{2})(?![0-9A-F])";
            var expected = Regex.Match(source, expression, RegexOptions.IgnoreCase);
            if (expected.Success && !string.Equals(expected.Groups[1].Value, Regex.Match(path, expression, RegexOptions.IgnoreCase).Groups[1].Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
