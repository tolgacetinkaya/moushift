using System.Text.Json.Serialization;

namespace Clickloom.Core;

public enum MouseControl
{
    LeftButton, RightButton, MiddleButton, XButton1, XButton2,
    WheelUp, WheelDown, WheelLeft, WheelRight
}

// TaskView is retained for the editor choice and to read the first preview's settings.
public enum ActionKind { Passthrough, Disabled, Shortcut, MouseButton, Text, LaunchApplication, Ring, OpenResource, SelectCircleProfile, ToggleCircleAdapt, TaskView }

[Flags]
public enum KeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

public sealed class InputAction : IJsonOnDeserialized
{
    public ActionKind Kind { get; set; } = ActionKind.Passthrough;
    public int KeyCode { get; set; }
    public KeyModifiers Modifiers { get; set; }
    public string Text { get; set; } = "";
    public string ApplicationPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public MouseControl MouseOutput { get; set; } = MouseControl.LeftButton;

    // Older releases can still load and execute the shortcut. The flag opts into
    // driverless routing only when the user explicitly picks the Task View action.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RunTaskView { get; set; }

    [JsonIgnore]
    public bool IsTaskViewAssignment => RunTaskView && Kind == ActionKind.Shortcut
        && KeyCode == 0x09 && Modifiers == KeyModifiers.Windows;

    public static InputAction CreateTaskViewAssignment() => new()
    {
        Kind = ActionKind.Shortcut, KeyCode = 0x09, Modifiers = KeyModifiers.Windows, RunTaskView = true
    };

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Recover settings written by the first Task View preview.
        if (Kind != ActionKind.TaskView) return;
        Kind = ActionKind.Shortcut;
        KeyCode = 0x09;
        Modifiers = KeyModifiers.Windows;
        RunTaskView = true;
    }

    public InputAction DeepClone() => new()
    {
        Kind = Kind, KeyCode = KeyCode, Modifiers = Modifiers, Text = Text,
        ApplicationPath = ApplicationPath, Arguments = Arguments, MouseOutput = MouseOutput, RunTaskView = RunTaskView
    };
}

public sealed class ButtonBinding
{
    public MouseControl Input { get; set; }
    public InputAction Action { get; set; } = new();
    public ButtonBinding DeepClone() => new() { Input = Input, Action = Action.DeepClone() };
}

public sealed class AuxiliaryKeyBinding
{
    public int VirtualKey { get; set; }
    public InputAction Action { get; set; } = new();
    public AuxiliaryKeyBinding DeepClone() => new() { VirtualKey = VirtualKey, Action = Action.DeepClone() };
}

public sealed record DirectionalShortcut(int KeyCode, KeyModifiers Modifiers)
{
    public InputAction ToAction() => new() { Kind = ActionKind.Shortcut, KeyCode = KeyCode, Modifiers = Modifiers };
}

public sealed record DirectionalCircleSettings(DirectionalShortcut? Left, DirectionalShortcut? Right,
    DirectionalShortcut? Up, DirectionalShortcut? Down);

public sealed class MappingProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string? ApplicationPath { get; set; }
    public string? DeviceKey { get; set; }
    public string? DeviceName { get; set; }
    public bool? AutoSwitchApplications { get; set; }
    public bool? CircleAutoSwitch { get; set; }
    public Guid? ManualCircleProfileId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DirectionalCircleSettings? DirectionalCircle { get; set; }
    public bool InheritCircleTrigger { get; set; } = true;
    public List<RingSlot> RingSlots { get; set; } = RingDefaults.Create();
    public List<ButtonBinding> Bindings { get; set; } = [];
    public List<AuxiliaryKeyBinding> AuxiliaryKeys { get; set; } = [];
    public List<LearnedBinding> LearnedBindings { get; set; } = [];

    public MappingProfile DeepClone() => new()
    {
        Id = Id, Name = Name, ApplicationPath = ApplicationPath, DeviceKey = DeviceKey, DeviceName = DeviceName, AutoSwitchApplications = AutoSwitchApplications,
        CircleAutoSwitch = CircleAutoSwitch, ManualCircleProfileId = ManualCircleProfileId, InheritCircleTrigger = InheritCircleTrigger,
        DirectionalCircle = DirectionalCircle,
        RingSlots = RingSlots.Select(slot => slot.DeepClone()).ToList(),
        Bindings = Bindings.Select(binding => binding.DeepClone()).ToList(),
        AuxiliaryKeys = AuxiliaryKeys.Select(binding => binding.DeepClone()).ToList(),
        LearnedBindings = LearnedBindings.Select(binding => binding.DeepClone()).ToList()
    };
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ActiveProfileId { get; set; }
    public List<MappingProfile> Profiles { get; set; } = [];
    public bool AutoSwitchProfiles { get; set; } = true;
    public bool StartMinimized { get; set; }
    public List<LearnedControl> LearnedControls { get; set; } = [];

    public AppSettings DeepClone() => new()
    {
        SchemaVersion = SchemaVersion, ActiveProfileId = ActiveProfileId,
        AutoSwitchProfiles = AutoSwitchProfiles, StartMinimized = StartMinimized,
        LearnedControls = LearnedControls.Select(c => c.DeepClone()).ToList(),
        Profiles = Profiles.Select(profile => profile.DeepClone()).ToList()
    };

    public static AppSettings CreateDefault()
    {
        var profile = new MappingProfile
        {
            Name = "Global",
            Bindings = Enum.GetValues<MouseControl>().Select(input => new ButtonBinding
            {
                Input = input, Action = new InputAction { Kind = ActionKind.Passthrough }
            }).ToList()
        };
        return new AppSettings { ActiveProfileId = profile.Id, Profiles = [profile] };
    }
}
