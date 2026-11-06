using Clickloom.Core;
using System.Text.Json.Nodes;

var tests = new (string Name, Action Run)[]
{
    ("application Circle creation preserves drafts, mouse scope, triggers and persistence", ApplicationProfileChecks.Run),
    ("learning requires repeated transitions from the selected device after arming", LearnInputSession),
    ("learned positions and independent input identities survive restart", LearnedPersistence),
    ("relearning preserves assignments and validation rejects invalid positions and cross-mouse bindings", LearnedValidation),
    ("dropping distinct shortcuts onto a folder appends children and survives restart", FolderShortcutDrops),
    ("reordering a full folder does not lose any of its nine shortcuts", FullFolderReorder),
    ("Circle editing is atomic, undoable, and isolated from saved profiles", CircleTransactions),
    ("Circle folders allow nine actions and reject invalid drops without losing data", CircleDropLimits),
    ("Circle uses the same mouse trigger with application-specific content", CircleApplicationRouting),
    ("manual Circle selection and adaptation never cross mouse identities", CircleManualRouting),
    ("Circle metadata, variable folders and preferences survive restart", CircleNewPersistence),
    ("every catalog preset has a valid executable configuration", CircleCatalogContract),
    ("Circle navigation preferences change only the invoking mouse", CircleNavigationPreferences),
    ("device-only profiles never inherit shared or another mouse actions", StrictDeviceProfiles),
    ("profile reset preserves circle, identity, other profiles and last-good settings", ResetProfileButtons),
    ("defaults preserve every standard mouse control", Defaults),
    ("foreground matching and global fallback", Profiles),
    ("clones isolate mutable bindings", Clone),
    ("validator rejects malformed actions and identities", Validation),
    ("settings round trip and last-good backup", Persistence),
    ("malformed settings survive load and explicit repair", Corruption),
    ("unknown schema never silently becomes current", UnknownSchema),
    ("invalid save does not touch existing bytes", InvalidSave),
    ("auxiliary F13-F24 bindings are explicit and validated", AuxiliaryKeys),
    ("legacy settings without auxiliary bindings remain compatible", LegacyAuxiliaryKeys),
    ("shortcut and executable action validation matches native execution", NativeActionContract),
    ("device profile resolution isolates source identities", DeviceProfiles),
    ("device application precedence and duplicate validation", DeviceApplications),
    ("ring defaults clone and nested ring rejection", RingActions),
    ("device and ring settings persist with legacy defaults", DeviceRingPersistence),
    ("ring folders clone and resolve nested paths", RingFolders),
    ("ring rejects cycles and excessive folder depth", RingFolderLimits),
    ("adjustment and nested ring settings survive storage", RingAdjustmentStorage)
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL {name}: {exception}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Defaults()
{
    var settings = AppSettings.CreateDefault();
    Require(SettingsValidator.Validate(settings).Count == 0, "Defaults must validate.");
    Require(settings.Profiles[0].Bindings.Count == 9, "All controls need an explicit default.");
    Require(settings.Profiles[0].Bindings.All(binding => binding.Action.Kind == ActionKind.Passthrough), "No remapping on first launch.");
    Require(settings.Profiles[0].AuxiliaryKeys.Count == 0, "Auxiliary keyboard route must be opt-in.");
}

static void Profiles()
{
    var settings = AppSettings.CreateDefault();
    var firstGlobal = settings.Profiles[0];
    var otherGlobal = new MappingProfile { Name = "Alternatif" };
    var application = new MappingProfile { Name = "Editor", ApplicationPath = @"C:\Apps\Editor.exe" };
    application.AuxiliaryKeys.Add(new AuxiliaryKeyBinding
    {
        VirtualKey = 0x7C,
        Action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 83, Modifiers = KeyModifiers.Control }
    });
    settings.Profiles.AddRange([otherGlobal, application]);
    settings.ActiveProfileId = otherGlobal.Id;
    Require(ProfileResolver.Resolve(settings, @"c:\apps\EDITOR.exe").Id == application.Id, "Case-insensitive full path matching failed.");
    Require(ProfileResolver.Resolve(settings, @"c:\apps\EDITOR.exe").AuxiliaryKeys[0].Action.KeyCode == 83, "Application-specific auxiliary bindings were lost.");
    Require(ProfileResolver.Resolve(settings, @"D:\Other\Editor.exe").Id == otherGlobal.Id, "Do not match a different executable with the same filename.");
    Require(ProfileResolver.Resolve(settings, null).Id == otherGlobal.Id, "Unknown foreground must use global fallback.");
    settings.ActiveProfileId = application.Id;
    Require(ProfileResolver.Resolve(settings, @"D:\Other.exe").Id == firstGlobal.Id, "An app-specific editor selection must not apply globally.");
    settings.AutoSwitchProfiles = false;
    Require(ProfileResolver.Resolve(settings, application.ApplicationPath).Id == firstGlobal.Id, "Auto-switch disable ignored.");
    Require(ProfileResolver.Resolve(new AppSettings(), null).ApplicationPath is null, "Resolver must always return a safe global.");
}

static void Clone()
{
    var original = AppSettings.CreateDefault();
    original.Profiles[0].AuxiliaryKeys.Add(new AuxiliaryKeyBinding { VirtualKey = 0x7C });
    var copy = original.DeepClone();
    copy.Profiles[0].Bindings[0].Action.Kind = ActionKind.Disabled;
    copy.Profiles[0].AuxiliaryKeys[0].Action.Kind = ActionKind.Disabled;
    Require(original.Profiles[0].Bindings[0].Action.Kind == ActionKind.Passthrough, "Clone aliases an action.");
    Require(original.Profiles[0].AuxiliaryKeys[0].Action.Kind == ActionKind.Passthrough, "Clone aliases an auxiliary action.");
}

static void Validation()
{
    var settings = AppSettings.CreateDefault();
    settings.Profiles[0].Bindings[0].Action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 0 };
    Require(SettingsValidator.Validate(settings).Count > 0, "Missing shortcut key accepted.");
    settings.Profiles[0].Bindings[0].Action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 65, Modifiers = (KeyModifiers)16 };
    Require(SettingsValidator.Validate(settings).Count > 0, "Unknown modifier accepted.");
    settings.Profiles[0].Bindings[0].Action = new InputAction { Kind = ActionKind.MouseButton, MouseOutput = MouseControl.WheelUp };
    Require(SettingsValidator.Validate(settings).Count > 0, "Wheel accepted as held mouse output.");
    settings = AppSettings.CreateDefault();
    settings.Profiles.Add(settings.Profiles[0].DeepClone());
    Require(SettingsValidator.Validate(settings).Count > 0, "Duplicate profile identity accepted.");
    settings = AppSettings.CreateDefault();
    settings.Profiles[0].Bindings.Add(settings.Profiles[0].Bindings[0].DeepClone());
    Require(SettingsValidator.Validate(settings).Count > 0, "Duplicate control accepted.");
    settings = AppSettings.CreateDefault();
    settings.Profiles[0].Bindings[0].Action = null!;
    settings.Profiles.Add(null!);
    Require(SettingsValidator.Validate(settings).Count > 0, "Null JSON values accepted or crashed.");
}

static void Persistence() => WithStore((store, directory) =>
{
    var settings = AppSettings.CreateDefault();
    settings.Profiles[0].Name = "Birinci";
    settings.Profiles[0].Bindings[3].Action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 67, Modifiers = KeyModifiers.Control };
    settings.Profiles[0].AuxiliaryKeys.Add(new AuxiliaryKeyBinding
    {
        VirtualKey = 0x87,
        Action = new InputAction { Kind = ActionKind.Text, Text = "Ek tuş metni" }
    });
    store.Save(settings);
    var firstBytes = File.ReadAllBytes(store.FilePath);
    var loaded = store.Load();
    Require(loaded.Warning is null && loaded.Settings.Profiles[0].Name == "Birinci", "Round trip failed.");
    Require(loaded.Settings.Profiles[0].Bindings[3].Action.Modifiers == KeyModifiers.Control, "Shortcut not persisted.");
    Require(loaded.Settings.Profiles[0].AuxiliaryKeys[0].VirtualKey == 0x87
        && loaded.Settings.Profiles[0].AuxiliaryKeys[0].Action.Text == "Ek tuş metni", "Auxiliary mapping not persisted.");
    settings.Profiles[0].Name = "İkinci";
    store.Save(settings);
    Require(File.ReadAllBytes(store.FilePath + ".bak").SequenceEqual(firstBytes), "Backup is not the last-known-good file.");
    Require(store.Load().Settings.Profiles[0].Name == "İkinci", "Replacement did not publish.");
    Require(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Temporary file leaked.");
});

static void Corruption() => WithStore((store, directory) =>
{
    store.Save(AppSettings.CreateDefault());
    store.Save(AppSettings.CreateDefault());
    var goodBackup = File.ReadAllBytes(store.FilePath + ".bak");
    byte[] corrupt = [0xFF, 0xFE, 0x7B, 0x00];
    File.WriteAllBytes(store.FilePath, corrupt);
    var load = store.Load();
    Require(load.Warning is not null, "Corruption must be visible.");
    Require(File.ReadAllBytes(store.FilePath).SequenceEqual(corrupt), "Load modified the corrupt file.");
    Require(load.Settings.Profiles[0].Bindings.All(binding => binding.Action.Kind == ActionKind.Passthrough), "Unsafe fallback.");
    store.Save(load.Settings);
    var preserved = Directory.GetFiles(directory, "settings.json.corrupt-*");
    Require(preserved.Length == 1 && File.ReadAllBytes(preserved[0]).SequenceEqual(corrupt), "Repair lost corrupt bytes.");
    Require(File.ReadAllBytes(store.FilePath + ".bak").SequenceEqual(goodBackup), "Repair replaced good backup with corrupt bytes.");
    Require(store.Load().Warning is null, "Explicit repair did not produce valid settings.");
});

static void UnknownSchema() => WithStore((store, _) =>
{
    var future = "{\"SchemaVersion\":999,\"Profiles\":[]}";
    File.WriteAllText(store.FilePath, future);
    Require(store.Load().Warning is not null, "Future schema accepted.");
    Require(File.ReadAllText(store.FilePath) == future, "Unknown schema overwritten on load.");
});

static void InvalidSave() => WithStore((store, _) =>
{
    store.Save(AppSettings.CreateDefault());
    var original = File.ReadAllBytes(store.FilePath);
    var bad = AppSettings.CreateDefault();
    bad.Profiles[0].Bindings[0].Action = new InputAction { Kind = ActionKind.Text, Text = new string('a', SettingsValidator.MaximumTextLength + 1) };
    var rejected = false;
    try { store.Save(bad); } catch (ArgumentException) { rejected = true; }
    Require(rejected, "Invalid save succeeded.");
    Require(File.ReadAllBytes(store.FilePath).SequenceEqual(original), "Invalid save changed existing bytes.");
});

static void AuxiliaryKeys()
{
    var settings = AppSettings.CreateDefault();
    settings.Profiles[0].AuxiliaryKeys = Enumerable.Range(0x7C, 12)
        .Select(key => new AuxiliaryKeyBinding
        {
            VirtualKey = key,
            Action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 65, Modifiers = KeyModifiers.Control }
        }).ToList();
    Require(SettingsValidator.Validate(settings).Count == 0, "F13-F24 inclusive must be valid.");
    settings.Profiles[0].AuxiliaryKeys[0].VirtualKey = 0x7B;
    Require(SettingsValidator.Validate(settings).Count > 0, "F12 must not be captured as an auxiliary key.");
    settings.Profiles[0].AuxiliaryKeys[0].VirtualKey = 0x88;
    Require(SettingsValidator.Validate(settings).Count > 0, "F25 must not be accepted.");
    settings.Profiles[0].AuxiliaryKeys[0].VirtualKey = 0x7D;
    Require(SettingsValidator.Validate(settings).Count > 0, "Duplicate auxiliary key accepted.");
    settings.Profiles[0].AuxiliaryKeys[0].VirtualKey = 0x7C;
    settings.Profiles[0].AuxiliaryKeys[0].Action.KeyCode = 0;
    Require(SettingsValidator.Validate(settings).Count > 0, "Auxiliary action skipped normal action validation.");
    settings.Profiles[0].AuxiliaryKeys[0].Action = null!;
    Require(SettingsValidator.Validate(settings).Count > 0, "Null auxiliary action accepted or crashed.");
    settings.Profiles[0].AuxiliaryKeys[0] = null!;
    Require(SettingsValidator.Validate(settings).Count > 0, "Null auxiliary binding accepted or crashed.");
    settings.Profiles[0].AuxiliaryKeys = null!;
    Require(SettingsValidator.Validate(settings).Count > 0, "Null auxiliary collection accepted or crashed.");
}

static void LegacyAuxiliaryKeys() => WithStore((store, _) =>
{
    store.Save(AppSettings.CreateDefault());
    var document = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
    foreach (var profile in document["Profiles"]!.AsArray()) profile!.AsObject().Remove("AuxiliaryKeys");
    File.WriteAllText(store.FilePath, document.ToJsonString());
    var loaded = store.Load();
    Require(loaded.Warning is null && loaded.Settings.Profiles[0].AuxiliaryKeys.Count == 0,
        "An existing schema-1 file without AuxiliaryKeys must load with an empty opt-in list.");
});

static void NativeActionContract()
{
    var settings = AppSettings.CreateDefault();
    var action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 65 };
    settings.Profiles[0].Bindings[0].Action = action;
    foreach (var key in new[] { 1, 7, 16, 17, 18, 91, 92, 160, 161, 162, 163, 164, 165, 255 })
    {
        action.KeyCode = key;
        Require(SettingsValidator.Validate(settings).Count > 0, $"Non-key or modifier-only VK{key} accepted.");
    }
    foreach (var key in new[] { 8, 65, 124, 135, 254 })
    {
        action.KeyCode = key;
        Require(SettingsValidator.Validate(settings).Count == 0, $"Ordinary VK{key} rejected.");
    }
    settings.Profiles[0].Bindings[0].Action = action = new InputAction { Kind = ActionKind.LaunchApplication };
    foreach (var path in new[] { @"C:\Apps\task.cmd", @"C:\Apps\readme.txt", "app.exe", @"C:\Apps\tool" })
    {
        action.ApplicationPath = path;
        Require(SettingsValidator.Validate(settings).Count > 0, $"Unsupported application carrier {path} accepted.");
    }
    action.ApplicationPath = @"C:\NotInstalledYet\PORTABLE.EXE";
    Require(SettingsValidator.Validate(settings).Count == 0, "Portable .exe profile must validate even when executable is absent.");
}

static void WithStore(Action<SettingsStore, string> run)
{
    var directory = Path.Combine(Path.GetTempPath(), "clickloom-core-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { run(new SettingsStore(Path.Combine(directory, "settings.json")), directory); }
    finally
    {
        // This directory is created by this test, validated beneath the OS temp
        // root, and contains only its own fixture files.
        var full = Path.GetFullPath(directory);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(full).StartsWith("clickloom-core-tests-", StringComparison.Ordinal))
            Directory.Delete(full, recursive: true);
    }
}

static void DeviceProfiles()
{
    var settings = AppSettings.CreateDefault();
    var first = settings.Profiles[0].DeepClone(); first.Id = Guid.NewGuid(); first.DeviceKey = "source:A";
    var second = first.DeepClone(); second.Id = Guid.NewGuid(); second.DeviceKey = "source:B";
    settings.Profiles.AddRange([first, second]);
    Require(ProfileResolver.Resolve(settings, null, "source:A").Id == first.Id, "Source A fell through to another profile.");
    Require(ProfileResolver.Resolve(settings, null, "source:B").Id == second.Id, "Source B used A settings.");
    Require(ProfileResolver.Resolve(settings, null).Id == settings.ActiveProfileId, "Unknown source used a device profile.");
    Require(ProfileResolver.Resolve(settings, null, "source:C").Id == settings.ActiveProfileId, "Unassigned source did not use shared fallback.");
    settings.ActiveProfileId = first.Id;
    Require(ProfileResolver.Resolve(settings, null).DeviceKey is null, "Editor device selection became a global profile.");
}
static void DeviceApplications()
{
    var settings = AppSettings.CreateDefault();
    var shared = new MappingProfile { Name = "Editor", ApplicationPath = @"C:\Apps\Editor.exe" };
    var specific = shared.DeepClone(); specific.Id = Guid.NewGuid(); specific.DeviceKey = "device:A";
    settings.Profiles.AddRange([shared, specific]);
    Require(SettingsValidator.Validate(settings).Count == 0, "Same executable on separate devices was rejected.");
    Require(ProfileResolver.Resolve(settings, @"c:\apps\editor.exe", "device:A").Id == specific.Id, "Device app override lost precedence.");
    Require(ProfileResolver.Resolve(settings, shared.ApplicationPath, "device:B").Id == shared.Id, "Shared app fallback failed.");
    var duplicate = specific.DeepClone(); duplicate.Id = Guid.NewGuid(); settings.Profiles.Add(duplicate);
    Require(SettingsValidator.Validate(settings).Count > 0, "Ambiguous device/app profile accepted.");
    settings.Profiles.Remove(duplicate); specific.ApplicationPath = null;
    duplicate = specific.DeepClone(); duplicate.Id = Guid.NewGuid(); settings.Profiles.Add(duplicate);
    Require(SettingsValidator.Validate(settings).Count > 0, "Duplicate device base accepted.");
    settings.Profiles.Remove(duplicate); specific.AuxiliaryKeys.Add(new() { VirtualKey = 0x7C });
    Require(SettingsValidator.Validate(settings).Count > 0, "Source-less keyboard events were accepted as device mouse events.");
}
static void RingActions()
{
    var settings = AppSettings.CreateDefault(); settings.Profiles[0].Bindings[3].Action.Kind = ActionKind.Ring;
    Require(settings.Profiles[0].RingSlots.Count == 8 && SettingsValidator.Validate(settings).Count == 0, "Ring defaults invalid.");
    var copy = settings.DeepClone(); copy.Profiles[0].RingSlots[0].Action.KeyCode = 90;
    Require(settings.Profiles[0].RingSlots[0].Action.KeyCode == 67, "Ring actions shared mutable clone state.");
    copy.Profiles[0].RingSlots[0].Action.Kind = ActionKind.Ring;
    Require(SettingsValidator.Validate(copy).Count > 0, "Recursive ring command accepted.");
    copy = settings.DeepClone(); copy.Profiles[0].RingSlots.RemoveAt(0);
    Require(SettingsValidator.Validate(copy).Count > 0, "Malformed ring slot count accepted.");
}
static void DeviceRingPersistence() => WithStore((store, directory) =>
{
    var settings = AppSettings.CreateDefault(); var device = settings.Profiles[0].DeepClone();
    device.Id = Guid.NewGuid(); device.DeviceKey = "interception:fixture"; device.Bindings[3].Action.Kind = ActionKind.Ring;
    device.RingSlots[0].Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
    settings.Profiles.Add(device); store.Save(settings);
    var loaded = store.Load().Settings.Profiles.Single(p => p.DeviceKey == device.DeviceKey);
    Require(loaded.Bindings[3].Action.Kind == ActionKind.Ring && loaded.RingSlots[0].Action.Modifiers == KeyModifiers.Alt, "Device/ring data lost on disk.");
    var json = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
    foreach (var profile in json["Profiles"]!.AsArray()) { profile!.AsObject().Remove("DeviceKey"); profile.AsObject().Remove("RingSlots"); }
    File.WriteAllText(store.FilePath, json.ToJsonString());
    Require(store.Load().Warning is null && store.Load().Settings.Profiles[0].RingSlots.Count == 8, "Legacy profiles did not receive safe ring defaults.");
});

static void RingFolders()
{
    var settings = AppSettings.CreateDefault();
    var folder = new RingSlot { Name = "Düzenleme", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    folder.Children[2].Action = new() { Kind = ActionKind.Shortcut, KeyCode = 0x43, Modifiers = KeyModifiers.Control };
    settings.Profiles[0].RingSlots[1] = folder;
    Require(SettingsValidator.Validate(settings).Count == 0, "Valid folder rejected.");
    Require(RingNavigation.Resolve(settings.Profiles[0].RingSlots, new[] { 1, 2 })?.Action.KeyCode == 0x43, "Nested command lost.");
    Require(RingNavigation.Resolve(settings.Profiles[0].RingSlots, new[] { 0, 2 }) is null, "Command traversed as folder.");
    Require(RingNavigation.Resolve(settings.Profiles[0].RingSlots, new[] { 1, 8 }) is null, "Out of range child accepted.");
    var copy = settings.DeepClone(); copy.Profiles[0].RingSlots[1].Children[2].Name = "Değişti";
    Require(folder.Children[2].Name != "Değişti", "Clone shares folder child objects.");
}
static void RingFolderLimits()
{
    var settings = AppSettings.CreateDefault(); var slots = settings.Profiles[0].RingSlots;
    for (int depth = 0; depth <= RingNavigation.MaximumFolderDepth; depth++)
    { var folder = new RingSlot { Name = "Klasör", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() }; slots[0] = folder; slots = folder.Children; }
    Require(SettingsValidator.Validate(settings).Count != 0, "Excessive folder depth accepted.");
    settings = AppSettings.CreateDefault(); var cycle = new RingSlot { Name = "Döngü", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    cycle.Children[0] = cycle; settings.Profiles[0].RingSlots[0] = cycle;
    Require(SettingsValidator.Validate(settings).Count != 0, "Cyclic folder accepted.");
}
static void RingAdjustmentStorage() => WithStore((store, _) =>
{
    var settings = AppSettings.CreateDefault(); var folder = new RingSlot { Name = "Ses", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    folder.Children[3] = new() { Name = "Ses ayarı", Kind = RingSlotKind.Adjustment,
        IncreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAF }, DecreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAE } };
    settings.Profiles[0].RingSlots[5] = folder; store.Save(settings);
    var loaded = store.Load(); Require(loaded.Warning is null, "Nested settings did not load.");
    var slot = RingNavigation.Resolve(loaded.Settings.Profiles[0].RingSlots, new[] { 5, 3 });
    Require(slot?.Kind == RingSlotKind.Adjustment && slot.IncreaseAction.KeyCode == 0xAF && slot.DecreaseAction.KeyCode == 0xAE, "Adjustment lost directions on disk.");
});

static void ResetProfileButtons() => WithStore((store, _) =>
{
    var original = AppSettings.CreateDefault();
    var selected = original.Profiles[0];
    selected.Bindings[2].Action = new() { Kind = ActionKind.Ring };
    selected.AuxiliaryKeys.Add(new() { VirtualKey = 0x7C, Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt } });
    selected.RingSlots[0] = new() { Name = "My folder", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    var other = selected.DeepClone(); other.Id = Guid.NewGuid(); other.Name = "Application profile"; other.ApplicationPath = @"C:\Windows\notepad.exe";
    original.Profiles.Add(other); store.Save(original);
    var reset = ProfileDefaults.ResetButtons(original, selected.Id); store.Save(reset);
    var loaded = store.Load(); var result = loaded.Settings.Profiles[0];
    Require(loaded.Warning is null && result.Bindings.Count == Enum.GetValues<MouseControl>().Length && result.Bindings.All(b => b.Action.Kind == ActionKind.Passthrough) && result.AuxiliaryKeys.Count == 0, "Reset did not restore all normal inputs.");
    Require(result.Id == selected.Id && result.Name == selected.Name && loaded.Settings.ActiveProfileId == original.ActiveProfileId && loaded.Settings.AutoSwitchProfiles == original.AutoSwitchProfiles, "Reset changed profile identity or preferences.");
    Require(result.RingSlots[0].Name == "My folder" && result.RingSlots[0].Children.Count == 8, "Reset lost saved circle contents.");
    Require(loaded.Settings.Profiles[1].Bindings[2].Action.Kind == ActionKind.Ring && loaded.Settings.Profiles[1].AuxiliaryKeys.Count == 1, "Reset affected another profile.");
    Require(selected.Bindings[2].Action.Kind == ActionKind.Ring && selected.AuxiliaryKeys.Count == 1, "Preparing a reset mutated the source settings.");
    Require(new SettingsStore(store.FilePath + ".bak").Load().Settings.Profiles[0].Bindings[2].Action.Kind == ActionKind.Ring, "Reset lost the last-good backup.");
});

static void StrictDeviceProfiles() => WithStore((store, _) =>
{
    var settings = AppSettings.CreateDefault(); var shared = settings.Profiles[0];
    shared.Bindings[2].Action = new() { Kind = ActionKind.Ring };
    var sharedApp = shared.DeepClone(); sharedApp.Id = Guid.NewGuid(); sharedApp.ApplicationPath = @"C:\Apps\editor.exe"; settings.Profiles.Add(sharedApp);
    var a = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A", shared.RingSlots);
    var b = ProfileResolver.CreateDeviceDefault("mouse:b", "Mouse B", shared.RingSlots);
    a.Bindings[3].Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
    settings.Profiles.AddRange([a, b]);
    Require(b.Bindings.All(x => x.Action.Kind == ActionKind.Passthrough) && b.AuxiliaryKeys.Count == 0, "New mouse inherited shared bindings.");
    Require(ProfileResolver.ResolveDeviceOnly(settings, sharedApp.ApplicationPath, a.DeviceKey)?.Id == a.Id, "Shared application profile overrode this mouse's defaults.");
    Require(ProfileResolver.ResolveDeviceOnly(settings, null, b.DeviceKey)?.Id == b.Id && b.Bindings[3].Action.Kind == ActionKind.Passthrough, "Mouse B inherited mouse A.");
    Require(ProfileResolver.ResolveDeviceOnly(settings, null, null) is null && ProfileResolver.ResolveDeviceOnly(settings, null, "unknown") is null, "Unknown source fell back to shared actions.");
    var app = a.DeepClone(); app.Id = Guid.NewGuid(); app.ApplicationPath = sharedApp.ApplicationPath; settings.Profiles.Add(app);
    Require(ProfileResolver.ResolveDeviceOnly(settings, app.ApplicationPath, a.DeviceKey)?.Id == app.Id, "Device application profile did not take precedence.");
    a.AutoSwitchApplications = false;
    Require(ProfileResolver.ResolveDeviceOnly(settings, app.ApplicationPath, a.DeviceKey)?.Id == a.Id, "App switching off lost mouse defaults.");
    Require(b.AutoSwitchApplications == true && settings.AutoSwitchProfiles, "Mouse A changed another mouse's switching preference.");
    a.RingSlots[0].Name = "Only A";
    Require(b.RingSlots[0].Name != "Only A" && shared.RingSlots[0].Name != "Only A", "Circle slots share references across mice.");
    store.Save(settings); Require(store.Load().Settings.Profiles.Single(p => p.Id == a.Id).DeviceName == "Mouse A", "Saved mouse name was lost.");
});

static void CircleTransactions()
{
    var original = RingDefaults.Create(); var originalName = original[0].Name;
    var document = new CircleDocument(original);
    var folder = new RingSlot { Name = "Work", Kind = RingSlotKind.Folder, Icon = "\uE821" };
    Require(document.Place([0], folder, false, out var error), error);
    Require(document.Place([0, 0], original[0], true, out error), error);
    Require(document.Swap([0, 0], [1], out error), error);
    Require(document.Slots[0].Children[0].Name == original[1].Name && document.Slots[1].Name == originalName, "Assigned actions did not swap.");
    Require(original[0].Name == originalName && original[0].Kind == RingSlotKind.Command, "Draft mutated saved state.");
    document.Undo(); Require(document.Slots[0].Children[0].Name == originalName, "Undo lost child.");
    document.Undo(); Require(document.Slots[0].Children.Count == 0, "Undo insert failed.");
    document.Undo(); Require(!document.IsChanged && document.Slots[0].Name == originalName, "Undo did not restore original.");
    document.Redo(); Require(document.Slots[0].Kind == RingSlotKind.Folder, "Redo failed.");
    Require(!document.Place([2], new() { Name = "Invalid", Action = new() { Kind = ActionKind.Shortcut } }, false, out _), "Invalid replacement committed.");
    Require(document.Slots[2].Name == original[2].Name && document.CanRedo, "Rejected edit damaged contents/history.");
    Require(document.Place([2], original[3], false, out error), error); Require(!document.CanRedo, "New edit failed to clear redo.");
}
static void CircleDropLimits()
{
    var document = new CircleDocument(RingDefaults.Create());
    Require(document.Place([0], new() { Name = "Folder", Kind = RingSlotKind.Folder }, false, out var error), error);
    for (int i = 0; i < 9; i++) Require(document.Place([0, i], new() { Name = "Action " + i }, true, out error), error);
    Require(!document.Place([0, 9], new() { Name = "Extra" }, true, out _), "Tenth action accepted.");
    Require(!document.Place([0, 0], new() { Name = "Nested", Kind = RingSlotKind.Folder }, false, out _), "New nested folder accepted.");
    Require(!document.Swap([0], [0, 1], out _), "Folder moved into itself.");
    Require(document.Slots[0].Children.Count == 9 && document.Slots[0].Children[0].Name == "Action 0", "Invalid drop lost existing data.");
    Require(document.Remove([0, 0], out error), error);
    Require(document.MoveInto([1], [0, 0], out error), error);
    Require(document.Slots[0].Children[0].Name == "Paste" && document.Slots[1].Action.Kind == ActionKind.Disabled, "Move from root did not preserve eight slots.");
    document.Undo(); Require(document.Slots[1].Name == "Paste", "Move undo lost root action.");
}
static void CircleApplicationRouting()
{
    var settings = AppSettings.CreateDefault(); var general = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A");
    general.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind = ActionKind.Ring;
    general.Bindings.Single(b => b.Input == MouseControl.XButton1).Action = new() { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
    var editor = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A"); editor.ApplicationPath = @"C:\Apps\editor.exe"; editor.Name = "Editor"; editor.RingSlots[0].Name = "Editor action";
    var browser = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A"); browser.ApplicationPath = @"C:\Apps\browser.exe"; browser.Name = "Browser"; browser.RingSlots[0].Name = "Browser action";
    settings.Profiles.AddRange([general, editor, browser]);
    var effective = ProfileResolver.ResolveDeviceInput(settings, @"c:\apps\EDITOR.exe", "mouse:a")!;
    Require(effective.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind == ActionKind.Ring, "Application profile lost General trigger.");
    Require(effective.Bindings.Single(b => b.Input == MouseControl.XButton1).Action.Kind == ActionKind.Passthrough, "Unrelated General action was inherited.");
    Require(effective.RingSlots[0].Name == "Editor action", "Editor Circle contents were lost.");
    Require(ProfileResolver.ResolveDeviceInput(settings, browser.ApplicationPath, "mouse:a")!.RingSlots[0].Name == "Browser action", "Browser Circle was not selected.");
    Require(ProfileResolver.ResolveDeviceInput(settings, @"D:\Other\editor.exe", "mouse:a")!.RingSlots[0].Name == general.RingSlots[0].Name, "Unknown executable did not fall back to General.");
    editor.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind = ActionKind.Disabled;
    Require(ProfileResolver.ResolveDeviceInput(settings, editor.ApplicationPath, "mouse:a")!.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind == ActionKind.Disabled, "Explicit disable was overridden.");
    editor.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind = ActionKind.Passthrough; editor.InheritCircleTrigger = false;
    Require(ProfileResolver.ResolveDeviceInput(settings, editor.ApplicationPath, "mouse:a")!.Bindings.Single(b => b.Input == MouseControl.MiddleButton).Action.Kind == ActionKind.Passthrough, "Explicit inheritance opt-out ignored.");
    general.CircleAutoSwitch = true; general.AutoSwitchApplications = false;
    Require(ProfileResolver.ResolveDeviceInput(settings, browser.ApplicationPath, "mouse:a")!.RingSlots[0].Name == "Browser action", "Circle selection incorrectly depended on button-profile adaptation.");
}
static void CircleManualRouting()
{
    var settings = AppSettings.CreateDefault(); var a = ProfileResolver.CreateDeviceDefault("a", "A"); var b = ProfileResolver.CreateDeviceDefault("b", "B");
    var appA = a.DeepClone(); appA.Id = Guid.NewGuid(); appA.ApplicationPath = @"C:\Apps\editor.exe"; appA.RingSlots[0].Name = "A only";
    var appB = b.DeepClone(); appB.Id = Guid.NewGuid(); appB.ApplicationPath = appA.ApplicationPath; appB.RingSlots[0].Name = "B only";
    settings.Profiles.AddRange([a, b, appA, appB]); a.CircleAutoSwitch = false; a.ManualCircleProfileId = appA.Id;
    Require(ProfileResolver.ResolveCircle(settings, null, "a")!.Id == appA.Id, "Manual Circle was ignored.");
    a.ManualCircleProfileId = appB.Id;
    Require(ProfileResolver.ResolveCircle(settings, null, "a")!.Id == a.Id, "Manual selection crossed mouse identity.");
    a.CircleAutoSwitch = true;
    Require(ProfileResolver.ResolveCircle(settings, appA.ApplicationPath, "a")!.Id == appA.Id && ProfileResolver.ResolveCircle(settings, appB.ApplicationPath, "b")!.Id == appB.Id, "Two mice shared application Circle.");
    Require(ProfileResolver.ResolveDeviceInput(settings, appA.ApplicationPath, null) is null && ProfileResolver.ResolveDeviceInput(settings, appA.ApplicationPath, "unknown") is null, "Unknown source routed an assignment.");
    var effective = ProfileResolver.ResolveDeviceInput(settings, appA.ApplicationPath, "a")!; effective.RingSlots[0].Name = "Temporary";
    Require(appA.RingSlots[0].Name == "A only" && appB.RingSlots[0].Name == "B only", "Runtime snapshots shared mutable state.");
}
static void CircleNewPersistence() => WithStore((store, _) =>
{
    var settings = AppSettings.CreateDefault(); var p = ProfileResolver.CreateDeviceDefault("mouse:a", "Mouse A"); settings.Profiles.Add(p);
    p.CircleAutoSwitch = false; p.ManualCircleProfileId = p.Id; p.InheritCircleTrigger = false;
    p.RingSlots[0] = new() { Name = "Custom folder", DefinitionId = "circle.folder", Icon = "\uE821", Kind = RingSlotKind.Folder,
        Children = Enumerable.Range(0, 9).Select(i => new RingSlot { Name = "Entry " + i, Icon = "\uE734" }).ToList() };
    // Legacy nested folders are retained, not flattened or silently deleted.
    p.RingSlots[1] = new() { Name = "Legacy", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    p.RingSlots[1].Children[0] = new() { Name = "Legacy nested", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    store.Save(settings); var load = store.Load(); var restored = load.Settings.Profiles.Single(x => x.Id == p.Id);
    Require(load.Warning is null && restored.RingSlots[0].Children.Count == 9 && restored.RingSlots[0].Icon == "\uE821" && restored.RingSlots[0].DefinitionId == "circle.folder", "Circle metadata did not survive storage.");
    Require(restored.CircleAutoSwitch == false && restored.ManualCircleProfileId == p.Id && !restored.InheritCircleTrigger, "Circle preferences lost.");
    Require(restored.RingSlots[1].Children[0].Children.Count == 8, "Legacy nested folder was discarded.");
});
static void CircleCatalogContract()
{
    Require(CircleCatalog.Actions.Select(a => a.Id).Distinct().Count() == CircleCatalog.Actions.Count, "Catalog IDs collide.");
    foreach (var definition in CircleCatalog.Actions.Where(a => !a.NeedsConfiguration))
    { var settings = AppSettings.CreateDefault(); settings.Profiles[0].RingSlots[0] = definition.Create(); Require(SettingsValidator.Validate(settings).Count == 0, "Invalid catalog preset: " + definition.Id); }
    foreach (var resource in new[] { "https://example.com", @"C:\Files\report.pdf", "ms-settings:display" }) Require(SettingsValidator.IsOpenResource(resource), "Valid resource rejected.");
    foreach (var resource in new[] { "javascript:alert(1)", "relative.txt", "shell:AppsFolder", "" }) Require(!SettingsValidator.IsOpenResource(resource), "Unsupported resource scheme accepted.");
}
static void CircleNavigationPreferences()
{
    var original = AppSettings.CreateDefault(); var a = ProfileResolver.CreateDeviceDefault("a", "A"); var b = ProfileResolver.CreateDeviceDefault("b", "B");
    var app = a.DeepClone(); app.Id = Guid.NewGuid(); app.ApplicationPath = @"C:\Apps\editor.exe"; original.Profiles.AddRange([a, b, app]);
    var selected = CirclePreferences.Apply(original, "a", ActionKind.SelectCircleProfile, app.Id);
    Require(ProfileResolver.ResolveCircle(selected, null, "a")!.Id == app.Id, "Profile navigation did not select its Circle.");
    Require(a.CircleAutoSwitch is null && b.CircleAutoSwitch is null && selected.Profiles.Single(p => p.Id == b.Id).CircleAutoSwitch is null, "Preference edit mutated original or another mouse.");
    Require(selected.Profiles.Single(p => p.Id == a.Id).AutoSwitchApplications == a.AutoSwitchApplications, "Circle navigation changed button-profile adaptation.");
    var adapted = CirclePreferences.Apply(selected, "a", ActionKind.ToggleCircleAdapt);
    Require(ProfileResolver.ResolveCircle(adapted, null, "a")!.Id == a.Id, "Adapt toggle did not restore General fallback.");
    bool rejected = false; try { CirclePreferences.Apply(original, "a", ActionKind.SelectCircleProfile, b.Id); } catch (ArgumentException) { rejected = true; }
    Require(rejected, "Profile action crossed mouse identities.");
}
static void FolderShortcutDrops() => WithStore((store, _) =>
{
    var settings = AppSettings.CreateDefault(); var document = new CircleDocument(settings.Profiles[0].RingSlots);
    Require(document.Place([2], new() { Name = "My shortcuts", Kind = RingSlotKind.Folder }, false, out var error), error);
    var combinations = new[] { new InputAction { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt }, new InputAction { Kind = ActionKind.Shortcut, KeyCode = 0x53, Modifiers = KeyModifiers.Control | KeyModifiers.Shift } };
    foreach (var action in combinations)
    {
        var drop = document.DropTarget([2], false, RingSlotKind.Command);
        Require(drop.Insert && drop.Path.Length == 2, "Folder drop still resolved as replacement.");
        Require(document.Place(drop.Path, new() { Name = "Shortcut " + action.KeyCode, Action = action }, drop.Insert, out error), error);
    }
    Require(document.Slots.Count == 8 && document.Slots[2].Kind == RingSlotKind.Folder && document.Slots[2].Name == "My shortcuts" && document.Slots[2].Children.Count == 2, "Folder identity was replaced by its shortcut.");
    document.Undo(); Require(document.Slots[2].Children.Count == 1 && document.Slots[2].Kind == RingSlotKind.Folder, "Undo removed the parent folder."); document.Redo();
    settings.Profiles[0].RingSlots = document.Slots; store.Save(settings);
    var loaded = store.Load(); var children = loaded.Settings.Profiles[0].RingSlots[2].Children;
    Require(loaded.Warning is null && children.Count == 2 && children[0].Action.KeyCode == 9 && children[0].Action.Modifiers == KeyModifiers.Alt && children[1].Action.KeyCode == 0x53 && children[1].Action.Modifiers == (KeyModifiers.Control | KeyModifiers.Shift), "Different shortcuts did not survive restart inside the folder.");
});
static void FullFolderReorder()
{
    var document = new CircleDocument(RingDefaults.Create());
    document.Place([0], new() { Name = "Full", Kind = RingSlotKind.Folder, Children = Enumerable.Range(0, 9).Select(i => new RingSlot { Name = "Entry " + i }).ToList() }, false, out _);
    var target = document.DropTarget([0], false, RingSlotKind.Command);
    Require(document.MoveInto([0, 0], target.Path, out var error), error);
    Require(document.Slots[0].Children.Count == 9 && document.Slots[0].Children[^1].Name == "Entry 0" && document.Slots[0].Children.Select(x => x.Name).Distinct().Count() == 9, "Reordering the full folder lost an action.");
    Require(!document.MoveInto([1], target.Path, out _), "Full folder accepted an external tenth action.");
}
static void LearnInputSession()
{
    var key = new InputIdentity(new string('A', 64), LearnedInputKind.HidButton, 0xFF00, 8, 3, 1);
    var session = new InputLearningSession("mouse:a", 1000);
    Require(!session.Observe("mouse:b", key, true, false, 1500), "Another mouse entered the learning session.");
    Require(!session.Observe("mouse:a", key, true, false, 500), "Activation click was learned.");
    Require(!session.Observe("mouse:a", key, true, false, 1500), "Held/repeated down was accepted.");
    session.Observe("mouse:a", key, false, false, 1600);
    Require(session.Observe("mouse:a", key, true, false, 1700) && session.Result is null && session.Confirmations == 1, "First real press was not isolated.");
    session.Observe("mouse:a", key, false, false, 1800);
    Require(session.Observe("mouse:a", key, true, false, 1900) && session.Result == key, "Two matching presses did not confirm.");
    Require(!session.Observe("mouse:a", key with { Usage = 9 }, true, false, 2000), "Confirmed identity changed.");
    var wheel = key with { Kind = LearnedInputKind.Wheel, Usage = 0x238, Direction = -1 };
    session = new("mouse:a", 0); session.Observe("mouse:a", wheel, true, true, 1000);
    Require(!session.Observe("mouse:a", wheel, true, true, 1010) && session.Result is null, "High resolution wheel noise counted as two gestures.");
    session.Observe("mouse:a", wheel, true, true, 1400); Require(session.Result?.StandardControl == MouseControl.WheelLeft, "Horizontal wheel identity lost direction.");
}
static void LearnedPersistence() => WithStore((store, _) =>
{
    var settings = AppSettings.CreateDefault(); var device = ProfileResolver.CreateDeviceDefault("mouse:a", "A"); settings.Profiles.Add(device);
    var control = new LearnedControl { DeviceKey = "mouse:a", Name = "Sniper", Identity = new(new string('B', 64), LearnedInputKind.HidButton, 9, 8, 2), X = .22, Y = .63, View = MouseDrawingView.Left, ObservedAt = DateTimeOffset.UtcNow };
    var next = LearnedControls.Save(settings, control); next.Profiles.Single(p => p.Id == device.Id).LearnedBindings.Add(new() { ControlId = control.Id, Action = new() { Kind = ActionKind.Ring } });
    store.Save(next); var loaded = store.Load();
    Require(loaded.Warning is null && loaded.Settings.LearnedControls.Single().Identity.Key == control.Identity.Key && loaded.Settings.LearnedControls[0].X == .22 && loaded.Settings.LearnedControls[0].Y == .63, "Learned identity/position did not survive storage.");
    Require(settings.LearnedControls.Count == 0, "Learning mutated the source settings.");
    Require(loaded.Settings.LearnedControls[0].View == MouseDrawingView.Left, "Side elevation did not survive save/reload.");
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(control))!.AsObject(); legacy.Remove("View");
    Require(System.Text.Json.JsonSerializer.Deserialize<LearnedControl>(legacy.ToJsonString())!.View == MouseDrawingView.Top, "Old coordinates moved to the side elevation.");
    var clone = loaded.Settings.DeepClone(); clone.LearnedControls[0].Name = "Changed";
    Require(loaded.Settings.LearnedControls[0].Name == "Sniper", "Clones share learned metadata.");
    var reset = ProfileDefaults.ResetButtons(loaded.Settings, device.Id);
    Require(reset.Profiles.Single(p => p.Id == device.Id).LearnedBindings.Count == 0 && reset.LearnedControls.Count == 1, "Button reset lost learned geometry or retained the action draft.");
});
static void LearnedValidation()
{
    var settings = AppSettings.CreateDefault(); var a = ProfileResolver.CreateDeviceDefault("a", "A"); var b = ProfileResolver.CreateDeviceDefault("b", "B"); settings.Profiles.AddRange([a, b]);
    var control = new LearnedControl { DeviceKey = "a", Name = "Extra", Identity = new(new string('C', 64), LearnedInputKind.HidButton, 9, 8), X = .3, Y = .6 };
    settings = LearnedControls.Save(settings, control); settings.Profiles.Single(p => p.Id == a.Id).LearnedBindings.Add(new() { ControlId = control.Id, Action = new() { Kind = ActionKind.Ring } });
    var relearn = control.DeepClone(); relearn.Id = Guid.NewGuid(); relearn.Name = "Renamed"; relearn.X = .4;
    var saved = LearnedControls.Save(settings, relearn);
    Require(saved.LearnedControls.Single().Id == control.Id && saved.Profiles.Single(p => p.Id == a.Id).LearnedBindings.Single().ControlId == control.Id, "Relearning orphaned assignments.");
    Require(SettingsValidator.Validate(saved).Count == 0, "Valid learned controls rejected.");
    saved.LearnedControls[0].View = (MouseDrawingView)99; Require(SettingsValidator.Validate(saved).Count > 0, "Unknown drawing elevation accepted."); saved.LearnedControls[0].View = MouseDrawingView.Top;
    saved.LearnedControls[0].X = double.NaN; Require(SettingsValidator.Validate(saved).Count > 0, "NaN position accepted."); saved.LearnedControls[0].X = 2; Require(SettingsValidator.Validate(saved).Count > 0, "Off-artwork position accepted."); saved.LearnedControls[0].X = .4;
    saved.Profiles.Single(p => p.Id == b.Id).LearnedBindings.Add(new() { ControlId = control.Id }); Require(SettingsValidator.Validate(saved).Count > 0, "Another mouse used a learned input.");
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
