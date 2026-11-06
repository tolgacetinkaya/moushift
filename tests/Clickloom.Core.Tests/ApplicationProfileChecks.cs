using Clickloom.Core;
using System.Text.Json;

internal static class ApplicationProfileChecks
{
    internal static void Run()
    {
        var saved = AppSettings.CreateDefault();
        var a = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A");
        var b = ProfileResolver.CreateDeviceDefault("mouse:b", "Mouse B"); saved.Profiles.AddRange([a, b]);
        a.Bindings.Single(x => x.Input == MouseControl.MiddleButton).Action.Kind = ActionKind.Ring;
        a.CircleAutoSwitch = true; string before = JsonSerializer.Serialize(saved);
        var draft = saved.DeepClone(); const string appPath = @"C:\Apps\editor.exe";
        var app = ApplicationCircleProfiles.GetOrCreate(draft, a.DeviceKey!, appPath, "Editor");
        app.RingSlots[0].Name = "Only in Editor";
        Require(JsonSerializer.Serialize(saved) == before, "Creating/editing the draft mutated saved settings.");
        Require(draft.Profiles.Single(p => p.Id == a.Id).RingSlots[0].Name != "Only in Editor", "App Circle shared mutable slots with default.");
        Require(ApplicationCircleProfiles.GetOrCreate(draft, a.DeviceKey!, appPath.ToUpperInvariant(), "Duplicate").Id == app.Id, "Duplicate app profile was created.");
        var other = ApplicationCircleProfiles.GetOrCreate(draft, b.DeviceKey!, appPath, "Editor");
        Require(other.Id != app.Id && other.RingSlots[0].Name != "Only in Editor", "App Circle crossed mouse identities.");
        Require(ProfileResolver.ResolveCircle(draft, appPath, a.DeviceKey)?.Id == app.Id, "Foreground app did not select its Circle.");
        Require(ProfileResolver.ResolveCircle(draft, @"C:\Other.exe", a.DeviceKey)?.Id == a.Id, "Unmatched app did not use default Circle.");
        Require(ProfileResolver.ResolveDeviceInput(draft, appPath, a.DeviceKey)?.Bindings.Single(x => x.Input == MouseControl.MiddleButton).Action.Kind == ActionKind.Ring, "App lost the mouse's Circle trigger.");
        Require(SettingsValidator.Validate(draft).Count == 0, "Generated application profile is invalid.");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(draft))!;
        Require(ProfileResolver.ResolveCircle(restored, appPath, a.DeviceKey)?.RingSlots[0].Name == "Only in Editor", "Application Circle did not survive persistence.");
        // Reopening an existing app must work even when there is no room for another profile.
        while (draft.Profiles.Count < SettingsValidator.MaximumProfiles) draft.Profiles.Add(ProfileResolver.CreateDeviceDefault(Guid.NewGuid().ToString(), "Other"));
        Require(ApplicationCircleProfiles.GetOrCreate(draft, a.DeviceKey!, appPath, "Editor").Id == app.Id, "Limit blocked an existing app.");
        int count = draft.Profiles.Count; bool rejected = false;
        try { ApplicationCircleProfiles.GetOrCreate(draft, a.DeviceKey!, @"C:\New.exe", "New"); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && draft.Profiles.Count == count, "Limit allowed a partial profile mutation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
