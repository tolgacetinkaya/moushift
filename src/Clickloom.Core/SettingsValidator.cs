namespace Clickloom.Core;

public static class SettingsValidator
{
    public const int MaximumProfiles = 64;
    public const int MaximumTextLength = 10_000;
    public const int MinimumAuxiliaryVirtualKey = 0x7C; // F13
    public const int MaximumAuxiliaryVirtualKey = 0x87; // F24

    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();
        if (settings is null) return ["Settings cannot be empty."];
        if (settings.SchemaVersion != 1) errors.Add("Unsupported settings schema.");
        if (settings.Profiles is null || settings.Profiles.Count == 0)
            return [.. errors, "At least one global profile is required."];
        if (settings.Profiles.Count > MaximumProfiles) errors.Add($"A maximum of {MaximumProfiles} profiles is supported.");
        if (!settings.Profiles.Any(profile => profile is not null && profile.ApplicationPath is null && profile.DeviceKey is null))
            errors.Add("At least one global profile is required.");
        if (!settings.Profiles.Any(profile => profile is not null && profile.Id == settings.ActiveProfileId))
            errors.Add("The selected profile was not found.");

        var profileIds = new HashSet<Guid>();
        var learnedIds = new HashSet<Guid>(); var learnedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (settings.LearnedControls is null || settings.LearnedControls.Count > 256) errors.Add("A maximum of 256 learned controls is supported.");
        else foreach (var control in settings.LearnedControls)
        {
            if (control is null) { errors.Add("A learned control cannot be null."); continue; }
            if (control.Id == Guid.Empty || !learnedIds.Add(control.Id)) errors.Add("Learned control identities must be unique.");
            if (string.IsNullOrWhiteSpace(control.DeviceKey) || control.DeviceKey.Length > 512 || control.DeviceKey.Contains('\0')) errors.Add("A learned control needs a valid source mouse.");
            if (string.IsNullOrWhiteSpace(control.Name) || control.Name.Length > 48) errors.Add("Learned button names require 1–48 characters.");
            if (!double.IsFinite(control.X) || !double.IsFinite(control.Y) || control.X is < 0 or > 1 || control.Y is < 0 or > 1) errors.Add("Button positions must be inside the mouse drawing.");
            if (control.Identity is not { } input || input.Endpoint is null || input.Endpoint.Length != 64 || !input.Endpoint.All(Uri.IsHexDigit)
                || !Enum.IsDefined(input.Kind) || input.Usage == 0 || input.Direction is < -1 or > 1
                || input.Kind == LearnedInputKind.Wheel && input.Direction == 0)
                errors.Add("Invalid learned input identity.");
            else if (!learnedKeys.Add(control.DeviceKey + "|" + input.Key)) errors.Add("This mouse input is already learned.");
        }
        var applicationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceDefaults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in settings.Profiles)
        {
            if (profile is null) { errors.Add("A profile cannot be empty."); continue; }
            if (profile.Id == Guid.Empty || !profileIds.Add(profile.Id)) errors.Add("Profile identifiers must be unique and nonempty.");
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 128)
                errors.Add("Profile names must contain 1–128 characters.");
            if (profile.DeviceKey is not null && (string.IsNullOrWhiteSpace(profile.DeviceKey) || profile.DeviceKey.Length > 512 || profile.DeviceKey.Contains('\0')))
                errors.Add("Invalid device profile identifier.");
            if (profile.DeviceKey is not null && profile.ApplicationPath is null && !deviceDefaults.Add(profile.DeviceKey))
                errors.Add("Only one global profile is allowed per device.");
            if (profile.ApplicationPath is not null)
            {
                if (!IsAbsolutePath(profile.ApplicationPath)) errors.Add("Application profiles require an absolute file path.");
                else if (!applicationPaths.Add((profile.DeviceKey ?? "") + "|" + profile.ApplicationPath)) errors.Add("Only one automatic profile is allowed per device and application.");
            }
            if (profile.RingSlots is null || profile.RingSlots.Count != 8) errors.Add("Action Ring must contain eight segments.");
            else ValidateRing(profile.RingSlots, 0, new HashSet<RingSlot>(ReferenceEqualityComparer.Instance), errors);
            if (profile.Bindings is null) { errors.Add("Profile bindings cannot be null."); continue; }
            if (profile.LearnedBindings is null || profile.LearnedBindings.Count > 256) errors.Add("Invalid learned assignments.");
            else
            {
                var bound = new HashSet<Guid>();
                foreach (var binding in profile.LearnedBindings)
                {
                    if (binding is null) { errors.Add("Invalid learned assignment."); continue; }
                    if (!bound.Add(binding.ControlId) || !(settings.LearnedControls ?? []).Any(c => c is not null && c.Id == binding.ControlId && c.DeviceKey == profile.DeviceKey))
                        errors.Add("Learned assignments must belong to this mouse.");
                    ValidateAction(binding.Action, errors);
                }
            }
            if (profile.Bindings.Count > Enum.GetValues<MouseControl>().Length) errors.Add("The profile contains too many bindings.");
            var inputs = new HashSet<MouseControl>();
            foreach (var binding in profile.Bindings)
            {
                if (binding is null) { errors.Add("A binding cannot be null."); continue; }
                if (!Enum.IsDefined(binding.Input)) errors.Add("Unsupported mouse button.");
                if (!inputs.Add(binding.Input)) errors.Add("A button cannot have multiple bindings.");
                ValidateAction(binding.Action, errors);
                if (binding.Action?.Kind is ActionKind.SelectCircleProfile or ActionKind.ToggleCircleAdapt) errors.Add("Circle navigation actions belong inside Click Circle.");
            }
            if (profile.AuxiliaryKeys is null) { errors.Add("Extra key bindings cannot be null."); continue; }
            if (profile.DeviceKey is not null && profile.AuxiliaryKeys.Count > 0)
                errors.Add("F13–F24 inputs have no source mouse identity; configure them in a shared profile.");
            if (profile.AuxiliaryKeys.Count > MaximumAuxiliaryVirtualKey - MinimumAuxiliaryVirtualKey + 1)
                errors.Add("A maximum of 12 extra key bindings is supported.");
            var auxiliaryKeys = new HashSet<int>();
            foreach (var binding in profile.AuxiliaryKeys)
            {
                if (binding is null) { errors.Add("An extra key binding cannot be null."); continue; }
                if (binding.VirtualKey is < MinimumAuxiliaryVirtualKey or > MaximumAuxiliaryVirtualKey)
                    errors.Add("Extra inputs must be in the F13–F24 range.");
                if (!auxiliaryKeys.Add(binding.VirtualKey)) errors.Add("An extra key cannot have multiple bindings.");
                ValidateAction(binding.Action, errors);
            }
        }
        return errors;
    }

    private static void ValidateRing(List<RingSlot> slots, int depth, HashSet<RingSlot> visited, List<string> errors)
    {
        if (depth > RingNavigation.MaximumFolderDepth) { errors.Add("The ring supports at most two folder levels."); return; }
        foreach (var slot in slots)
        {
            if (slot is null) { errors.Add("A ring segment cannot be null."); continue; }
            if (!visited.Add(slot)) { errors.Add("Ring folders cannot contain cycles or shared segments."); continue; }
            if (string.IsNullOrWhiteSpace(slot.Name) || slot.Name.Length > 48) errors.Add("Ring action names must contain 1–48 characters.");
            if (!Enum.IsDefined(slot.Kind)) errors.Add("Unsupported ring segment type.");
            foreach (var action in new[] { slot.Action, slot.DecreaseAction, slot.IncreaseAction })
            {
                ValidateAction(action, errors);
                if (action?.Kind == ActionKind.Ring) errors.Add("Use a folder instead of nesting another ring.");
            }
            if (slot.Children is null || slot.Kind == RingSlotKind.Folder && slot.Children.Count > RingNavigation.MaximumFolderItems
                || slot.Kind != RingSlotKind.Folder && slot.Children.Count != 0)
                errors.Add("Folders support up to nine actions; other actions cannot have child segments.");
            else if (slot.Kind == RingSlotKind.Folder) ValidateRing(slot.Children, depth + 1, visited, errors);
        }
    }

    private static void ValidateAction(InputAction? action, List<string> errors)
    {
        if (action is null) { errors.Add("An input action cannot be null."); return; }
        if (!Enum.IsDefined(action.Kind)) errors.Add("Unsupported action type.");
        const KeyModifiers supported = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Windows;
        if ((action.Modifiers & ~supported) != 0) errors.Add("Unsupported keyboard modifier.");
        if (action.Kind == ActionKind.Shortcut && (action.KeyCode is < 8 or > 254
            or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5))
            errors.Add("A shortcut requires a valid nonmodifier key.");
        if (action.Kind == ActionKind.MouseButton && action.MouseOutput is not
            (MouseControl.LeftButton or MouseControl.RightButton or MouseControl.MiddleButton
                or MouseControl.XButton1 or MouseControl.XButton2))
            errors.Add("Output must be one of the five standard mouse buttons.");
        if (action.Text is null || action.Text.Length > MaximumTextLength)
            errors.Add($"Text cannot exceed {MaximumTextLength} characters.");
        else if (action.Kind == ActionKind.Text && action.Text.Length == 0)
            errors.Add("Text to type cannot be empty.");
        if (action.ApplicationPath is null || action.ApplicationPath.Length > 32_767 || action.ApplicationPath.Contains('\0'))
            errors.Add("Invalid application path.");
        else if (action.Kind == ActionKind.LaunchApplication && (!IsAbsolutePath(action.ApplicationPath)
            || !string.Equals(Path.GetExtension(action.ApplicationPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            errors.Add("An absolute .exe path is required to launch an application.");
        if (action.Arguments is null || action.Arguments.Length > 8_192 || action.Arguments.Contains('\0'))
            errors.Add("Application arguments are invalid or too long.");
        if (action.Kind == ActionKind.OpenResource && !IsOpenResource(action.ApplicationPath))
            errors.Add("Choose an absolute file/folder path, an https/http URL, or a Windows Settings URI.");
        if (action.Kind == ActionKind.SelectCircleProfile && (!Guid.TryParse(action.Arguments, out var target) || target == Guid.Empty))
            errors.Add("Choose a Circle profile for this mouse.");
    }

    private static bool IsAbsolutePath(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 32_767 && !value.Contains('\0') && Path.IsPathFullyQualified(value);

    public static bool IsOpenResource(string? value) => value is not null && value.Length <= 32767 && !value.Contains('\0')
        && (IsAbsolutePath(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http" or "ms-settings");
}
