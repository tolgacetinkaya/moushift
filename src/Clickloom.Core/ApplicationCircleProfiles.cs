namespace Clickloom.Core;

public static class ApplicationCircleProfiles
{
    // Operates only on the caller's editing draft. Existing profiles/assignments are never replaced.
    public static MappingProfile GetOrCreate(AppSettings draft, string deviceKey, string executablePath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        if (!Path.IsPathFullyQualified(executablePath) || !executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an application executable.", nameof(executablePath));
        var general = draft.Profiles.FirstOrDefault(p => p.DeviceKey == deviceKey && p.ApplicationPath is null)
            ?? throw new InvalidOperationException("Choose a mouse first.");
        var existing = draft.Profiles.FirstOrDefault(p => p.DeviceKey == deviceKey && string.Equals(p.ApplicationPath, executablePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        if (draft.Profiles.Count >= SettingsValidator.MaximumProfiles) throw new InvalidOperationException("The profile limit has been reached.");
        var profile = new MappingProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(executablePath) : name.Trim(),
            DeviceKey = deviceKey, DeviceName = general.DeviceName, ApplicationPath = executablePath,
            RingSlots = general.RingSlots.Select(s => s.DeepClone()).ToList(),
            Bindings = Enum.GetValues<MouseControl>().Select(c => new ButtonBinding { Input = c }).ToList(),
            InheritCircleTrigger = true
        };
        if (profile.Name.Length > 80) profile.Name = profile.Name[..80];
        draft.Profiles.Add(profile);
        return profile;
    }
}
