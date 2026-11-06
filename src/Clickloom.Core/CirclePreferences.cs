namespace Clickloom.Core;

public static class CirclePreferences
{
    // Manual selection deliberately pauses Circle adaptation. Button-profile adaptation is unchanged.
    public static AppSettings Apply(AppSettings settings, string deviceKey, ActionKind kind, Guid? profileId = null)
    {
        var next = settings.DeepClone();
        var general = next.Profiles.FirstOrDefault(p => p.DeviceKey == deviceKey && p.ApplicationPath is null)
            ?? throw new ArgumentException("This mouse no longer has a General profile.");
        if (kind == ActionKind.ToggleCircleAdapt)
            general.CircleAutoSwitch = !(general.CircleAutoSwitch ?? general.AutoSwitchApplications ?? next.AutoSwitchProfiles);
        else if (kind == ActionKind.SelectCircleProfile)
        {
            if (!next.Profiles.Any(p => p.DeviceKey == deviceKey && p.Id == profileId)) throw new ArgumentException("The selected Circle belongs to another mouse or no longer exists.");
            general.ManualCircleProfileId = profileId; general.CircleAutoSwitch = false;
        }
        else throw new ArgumentException("Unsupported Circle preference.");
        return next;
    }
}
