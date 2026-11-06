namespace Clickloom.Core;

public static class ProfileResolver
{
    public static MappingProfile? ResolveCircle(AppSettings settings, string? foregroundExecutable, string? deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        var profiles = settings.Profiles.Where(p => p.DeviceKey == deviceKey).ToArray();
        var general = profiles.FirstOrDefault(p => p.ApplicationPath is null);
        if (general is null) return null;
        if (general.CircleAutoSwitch ?? general.AutoSwitchApplications ?? settings.AutoSwitchProfiles)
            return profiles.FirstOrDefault(p => p.ApplicationPath is not null && string.Equals(p.ApplicationPath, foregroundExecutable, StringComparison.OrdinalIgnoreCase)) ?? general;
        return profiles.FirstOrDefault(p => p.Id == general.ManualCircleProfileId) ?? general;
    }

    public static InputAction ResolveDeviceBinding(AppSettings settings, MappingProfile profile, MouseControl control)
    {
        var action = profile.Bindings.FirstOrDefault(b => b.Input == control)?.Action ?? new();
        if (action.Kind == ActionKind.Passthrough && profile.ApplicationPath is not null && profile.InheritCircleTrigger)
        {
            var general = settings.Profiles.FirstOrDefault(p => p.DeviceKey == profile.DeviceKey && p.ApplicationPath is null);
            if (general?.Bindings.FirstOrDefault(b => b.Input == control)?.Action is { Kind: ActionKind.Ring } trigger)
                return trigger;
        }
        return action;
    }

    // Source identity is mandatory. Editor selection and shared legacy profiles never route device input.
    public static MappingProfile? ResolveDeviceInput(AppSettings settings, string? executable, string? deviceKey)
    {
        var mapping = ResolveDeviceOnly(settings, executable, deviceKey);
        if (mapping is null) return null;
        var effective = mapping.DeepClone();
        effective.Bindings = Enum.GetValues<MouseControl>().Select(c => new ButtonBinding { Input = c, Action = ResolveDeviceBinding(settings, mapping, c).DeepClone() }).ToList();
        effective.RingSlots = (ResolveCircle(settings, executable, deviceKey) ?? mapping).RingSlots.Select(s => s.DeepClone()).ToList();
        return effective;
    }
    public static MappingProfile? ResolveDeviceOnly(AppSettings settings, string? foregroundExecutable, string? deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        var profiles = settings.Profiles.Where(p => string.Equals(p.DeviceKey, deviceKey, StringComparison.Ordinal));
        var defaults = profiles.FirstOrDefault(p => p.ApplicationPath is null);
        if ((defaults?.AutoSwitchApplications ?? settings.AutoSwitchProfiles) && !string.IsNullOrWhiteSpace(foregroundExecutable))
        {
            var application = profiles.FirstOrDefault(p => string.Equals(p.ApplicationPath, foregroundExecutable, StringComparison.OrdinalIgnoreCase));
            if (application is not null) return application;
        }
        return defaults;
    }

    public static MappingProfile CreateDeviceDefault(string deviceKey, string deviceName, IEnumerable<RingSlot>? circle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        var profile = AppSettings.CreateDefault().Profiles[0];
        profile.Name = "Default"; profile.DeviceKey = deviceKey; profile.DeviceName = deviceName;
        profile.AutoSwitchApplications = true;
        if (circle is not null) profile.RingSlots = circle.Select(s => s.DeepClone()).ToList();
        return profile;
    }

    public static MappingProfile Resolve(AppSettings settings, string? foregroundExecutable, string? deviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var profiles = settings.Profiles ?? [];
        if (settings.AutoSwitchProfiles && !string.IsNullOrWhiteSpace(foregroundExecutable))
        {
            var deviceApplication = !string.IsNullOrWhiteSpace(deviceKey) ? profiles.FirstOrDefault(profile => profile is not null
                && string.Equals(profile.DeviceKey, deviceKey, StringComparison.Ordinal)
                && string.Equals(profile.ApplicationPath, foregroundExecutable, StringComparison.OrdinalIgnoreCase)) : null;
            if (deviceApplication is not null) return deviceApplication;
            var application = profiles.FirstOrDefault(profile => profile is not null
                && profile.DeviceKey is null
                && !string.IsNullOrWhiteSpace(profile.ApplicationPath)
                && string.Equals(profile.ApplicationPath, foregroundExecutable,
                    StringComparison.OrdinalIgnoreCase));
            if (application is not null) return application;
        }
        if (!string.IsNullOrWhiteSpace(deviceKey))
        {
            var deviceDefault = profiles.FirstOrDefault(profile => profile is not null && profile.ApplicationPath is null
                && string.Equals(profile.DeviceKey, deviceKey, StringComparison.Ordinal));
            if (deviceDefault is not null) return deviceDefault;
        }

        // An application-specific profile selected in the editor must not become a
        // system-wide fallback when a different executable has the foreground.
        return profiles.FirstOrDefault(profile => profile is not null
                   && profile.Id == settings.ActiveProfileId && profile.ApplicationPath is null && profile.DeviceKey is null)
               ?? profiles.FirstOrDefault(profile => profile is not null && profile.ApplicationPath is null && profile.DeviceKey is null)
               ?? AppSettings.CreateDefault().Profiles[0];
    }
}
