namespace Clickloom.Core;

public static class ProfileDefaults
{
    /// <summary>Reset only the selected profile's inputs; preserve its identity, circle and other profiles.</summary>
    public static AppSettings ResetButtons(AppSettings settings, Guid profileId)
    {
        var next = settings.DeepClone();
        var profile = next.Profiles.Single(p => p.Id == profileId);
        profile.Bindings = Enum.GetValues<MouseControl>().Select(input => new ButtonBinding { Input = input }).ToList();
        profile.AuxiliaryKeys.Clear();
        profile.LearnedBindings.Clear();
        if (profile.ApplicationPath is not null) profile.InheritCircleTrigger = false;
        return next;
    }
}
