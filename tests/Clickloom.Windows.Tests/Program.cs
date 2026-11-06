using Clickloom.Core;
using Clickloom.Windows;

if (args.Contains("--hid-discovery"))
{
    var endpoints = HidInventory.Enumerate(out var diagnostics); var connections = MouseConnectionCatalog.Enumerate();
    foreach (string diagnostic in diagnostics) Console.WriteLine(diagnostic);
    Console.WriteLine($"Read-only HID discovery: {endpoints.Count} accessible collections across all vendors");
    foreach (var connection in connections.Where(c => !c.IsAuxiliary))
    { var matched = endpoints.Where(e => e.PhysicalKey.Equals(connection.Key, StringComparison.OrdinalIgnoreCase)).ToArray(); Console.WriteLine($"{connection.Name}: {matched.Length} associated collections, {matched.Sum(e => e.Buttons.Sum(c => c.Count))} declared usages"); }
    return 0;
}

if (args.Contains("--connections"))
{
    foreach (var device in MouseConnectionCatalog.Enumerate())
        Console.WriteLine($"{device.Name} | {device.Manufacturer} | {device.Transport} | {device.Status} | {device.DevicePaths.Length} interfaces");
    return 0;
}

if (args.Contains("--validate-settings"))
{
    var loaded = new SettingsStore(args[Array.IndexOf(args, "--validate-settings") + 1]).Load();
    var errors = SettingsValidator.Validate(loaded.Settings);
    if (loaded.Warning is not null || errors.Count != 0) throw new InvalidOperationException(loaded.Warning ?? string.Join("; ", errors));
    Console.WriteLine("PASS recovered settings: " + loaded.Settings.Profiles.Count + " profiles, " + loaded.Settings.Profiles.Sum(p => p.Bindings.Count(b => b.Action.Kind != ActionKind.Passthrough)) + " assignments");
    return 0;
}

if (args.Contains("--trace-shortcuts")) return ShortcutTraceProbe.Run();

if (args.Contains("--native-shortcut")) return NativeShortcutProbe.Run();
if (args.Contains("--listen-mouse"))
{
    using var listener = new MouseRemappingService();
    listener.InputObserved += (_, input) =>
    {
        if (input is MouseControl.XButton1 or MouseControl.XButton2 or MouseControl.MiddleButton)
            Console.WriteLine($"MOUSE {input}: {listener.InputStatus}");
    };
    listener.Start();
    Console.WriteLine("READY: 45s read-only side/middle-button observation; remapping disabled; no ordinary keyboard text recorded.");
    Thread.Sleep(45000);
    Console.WriteLine("DONE"); return 0;
}

if (args.Contains("--hid-inventory"))
{
    foreach (var endpoint in HidInventory.Enumerate())
        Console.WriteLine($"{endpoint.Vendor:X4}:{endpoint.ProductId:X4} {endpoint.Product} usage={endpoint.UsagePage:X4}:{endpoint.Usage:X4} input={endpoint.InputLength} output={endpoint.OutputLength} feature={endpoint.FeatureLength}");
    return 0;
}

// These tests exercise the actual dispatch stamps and held-gesture ledger used
// by the native service, without starting hooks or injecting keyboard/mouse input.
var tests = new (string Name, Action Run)[]
{
    ("generic descriptor parsing preserves vendor pages, report IDs and collections", GenericHidCapabilities),
    ("raw mouse learning decodes edges and horizontal wheel without movement", GenericRawLearning),
    ("keyboard learning excludes ordinary typing", KeyboardLearningPrivacy),
    ("driver routes require one physical match and reject identical mouse ambiguity", PhysicalDeviceRoutes),
    ("connection inventory groups interfaces without claiming mouse power", ConnectionGrouping),
    ("native INPUT ABI and event encoding", NativeLayout),
    ("service starts disarmed and cannot arm without hooks", DisarmedService),
    ("invalid settings cannot enable or reach native polling", InvalidSettings),
    ("disposal rejects subsequent service operations", Disposal),
    ("foreground and mapping generations reject pending actions", RoutingGeneration),
    ("routing identity tracks windows and settings without poll churn", RoutingIdentity),
    ("release survives profile switch but not emergency pause", ReleaseGeneration),
    ("newly enabled session cannot execute old work", EnableGeneration),
    ("passthrough down keeps passthrough up after a config change", PassthroughGesture),
    ("suppressed down keeps its action and up after pause", SuppressedGesture),
    ("auxiliary key repeat cannot replace held gesture", AuxiliaryRepeat),
    ("unmatched up does not manufacture a gesture", UnknownRelease),
    ("auxiliary source accepts vendor injection and rejects our own", AuxiliarySources),
    ("unaccepted output prefix owns no keys", UnacceptedPrefix),
    ("Unicode odd prefix retains the matching release", UnicodePrefix),
    ("complete accepted key pairs retain no ownership", CompletePairs),
    ("partial shortcut releases only accepted keys in reverse order", ShortcutPrefix),
    ("partial cleanup retains unresolved output ownership", PartialCleanup),
    ("accepted mouse down retains matching button up", MousePrefix),
    ("emergency hotkey chooses F10 first and stops after success", EmergencyFirstAvailable),
    ("emergency hotkey falls back to the available key", EmergencyFallback),
    ("busy emergency hotkeys fail closed without a binding", EmergencyAllBusy),
    ("device packet rewrite preserves motion and unhandled edges", DevicePacket),
    ("device wheel suppression preserves the other axis", DeviceWheels),
    ("physical device gesture ownership is independent", DeviceGestures),
    ("missing optional driver library fails without installation", MissingDeviceLibrary),
    ("shortcut capture does not consume unrelated typing", CaptureIdle),
    ("focused Alt Tab capture consumes both modifier and key edges", CaptureAltTab),
    ("capture end retains only owned releases and repeats", CaptureEnd),
    ("preexisting modifiers are not adopted or released", CaptureExistingModifier),
    ("modifier changes update the currently held chord", CaptureModifierOrder),
    ("capture session replacement preserves physical ownership", CaptureReplace),
    ("escape cancels capture without leaking repeat or release", CaptureEscape),
    ("capture baseline releases and normalized modifier sides stay accurate", CaptureBaseline),
    ("side-button Alt Tab continues when focus returns to our app", OwnWindowSwitching),
    ("shortcut capture and ordinary editor buttons retain their protection", OwnWindowEditing),
    ("ring resolves commands and repeated adjustments without folder execution", RingActions),
    ("ring rejects changed foreground session and profile", RingDispatch),
    ("ring pointer consumption cannot leak a side-button click", RingPointer)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed; no hooks started or input injected.");
return failures == 0 ? 0 : 1;

static void ConnectionGrouping()
{
    var inputs = new[] {
        new MouseDeviceInfo("generic", "razer-one", "1532", "00A4", 5),
        new MouseDeviceInfo("generic", "razer-two", "1532", "00A4", 5),
        new MouseDeviceInfo("generic", "virtual", "", "", 5),
        new MouseDeviceInfo("generic", "other-mouse", "1B1C", "2B0E", 5) };
    var devices = MouseConnectionCatalog.Build(inputs, d => d.DevicePath switch {
        "virtual" => new("virtual", "Virtual mouse", "Windows", true),
        "other-mouse" => new("corsair", "Corsair mouse", "USB", false),
        _ => new("razer", "Razer Mouse Dock Pro", "USB", false) });
    if (devices.Count != 3 || devices.Single(d => d.Key == "razer").DevicePaths.Length != 2)
        throw new Exception("Multiple interfaces were counted as multiple physical devices.");
    var receiver = devices.Single(d => d.Key == "razer");
    if (!receiver.IsReceiver || !receiver.Status.Contains("unknown") || devices[^1].Key != "virtual"
        || devices.Single(d => d.Key == "corsair").IsReceiver)
        throw new Exception("Receiver presence was confused with mouse power or a physical input.");
    if (MouseConnectionCatalog.Build([], _ => throw new Exception()).Count != 0)
        throw new Exception("An unplugged mouse remained in the inventory.");
}

static void PhysicalDeviceRoutes()
{
    var a = new MouseConnection("a", "Razer", "Razer", "USB", true, false, [@"\\?\HID#VID_1532&PID_00A4&MI_00#a", @"\\?\HID#VID_1532&PID_00A4&MI_01#a"]);
    var b = new MouseConnection("b", "Corsair", "Corsair", "USB", true, false, [@"\\?\HID#VID_1B1C&PID_1B81&MI_00#b"]);
    var sources = new[] { new DeviceSourceInfo("source-a", "A", @"HID\VID_1532&PID_00A4&REV_0100&MI_00", false), new DeviceSourceInfo("source-a2", "A extra", @"HID\VID_1532&PID_00A4&MI_01", false), new DeviceSourceInfo("source-b", "B", @"HID\VID_1B1C&PID_1B81&MI_00", false), new DeviceSourceInfo("missing", "Missing", @"HID\VID_1532&PID_00A4&MI_02", false) };
    var routes = DeviceRouteTable.Build(sources, [a, b]);
    Require(routes.Count == 3 && routes.PhysicalKey("source-a") == "a" && routes.PhysicalKey("source-a2") == "a" && routes.PhysicalKey("source-b") == "b" && routes.PhysicalKey("missing") is null, "Physical routes crossed mice or ignored interfaces.");
    var identical = a with { Key = "another-a", DevicePaths = [@"\\?\HID#VID_1532&PID_00A4&MI_00#other"] };
    var ambiguous = DeviceRouteTable.Build(sources, [a, identical, b]);
    Require(ambiguous.PhysicalKey("source-a") is null && ambiguous.PhysicalKey("source-b") == "b", "Identical device ambiguity selected a random mouse.");
    Require(DeviceRouteTable.Build(sources, []).Count == 0 && routes.SameAs(DeviceRouteTable.Build(sources, [b, a])), "Refresh order changed routing or removed devices kept routes.");
}

static void NativeLayout()
{
    var errors = NativeDiagnostics.SelfCheck();
    Require(errors.Count == 0, string.Join(Environment.NewLine, errors));
}

static void DisarmedService()
{
    using var service = new MouseRemappingService();
    Require(!service.IsRunning && !service.IsEnabled, "Construction must never remap input.");
    Throws<InvalidOperationException>(() => service.SetEnabled(true), "Arming succeeded without the hook/emergency hotkey.");
    Require(!service.IsEnabled, "Failed arming enabled the service.");
}

static void InvalidSettings()
{
    using var service = new MouseRemappingService();
    var invalid = AppSettings.CreateDefault();
    invalid.Profiles[0].AuxiliaryKeys.Add(new AuxiliaryKeyBinding { VirtualKey = 0x7B });
    Throws<ArgumentException>(() => service.ApplySettings(invalid), "Unsupported auxiliary input reached runtime.");
    Throws<ArgumentNullException>(() => service.ApplySettings(null!), "Null settings accepted.");
    Require(!service.IsRunning && !service.IsEnabled, "Rejected settings changed runtime state.");
}

static void Disposal()
{
    var service = new MouseRemappingService();
    service.Dispose();
    service.Dispose();
    Throws<ObjectDisposedException>(() => service.SetEnabled(false), "Disposed service accepted enable-state changes.");
    Throws<ObjectDisposedException>(() => service.ApplySettings(AppSettings.CreateDefault()), "Disposed service accepted settings.");
    Require(!service.IsRunning && !service.IsEnabled, "Disposed service remained active.");
}

static void RoutingGeneration()
{
    var state = new InputDispatchState();
    var queued = state.Current;
    Require(InputDispatchState.CanExecute(queued, state.Current, enabled: true, isRelease: false), "Current action rejected.");
    state.AdvanceRouting(); // Foreground changed from app A to app B.
    Require(!InputDispatchState.CanExecute(queued, state.Current, true, false), "App A action could execute in app B.");
    var secondWindow = state.Current;
    state.AdvanceRouting(); // A second document/window may belong to the same process.
    Require(!InputDispatchState.CanExecute(secondWindow, state.Current, true, false), "Old-window work survived foreground window change.");
    Require(InputDispatchState.CanExecute(state.Current, state.Current, true, false), "Fresh work rejected after routing update.");
}

static void ReleaseGeneration()
{
    var state = new InputDispatchState();
    var held = state.Current;
    state.AdvanceRouting();
    Require(InputDispatchState.CanExecute(held, state.Current, true, true), "Profile switch lost the old held output's UP.");
    Require(!InputDispatchState.CanExecute(held, state.Current, true, false), "Profile switch allowed old press work.");
    state.AdvanceEnableEpoch();
    Require(!InputDispatchState.CanExecute(held, state.Current, true, true), "Old release could affect new-session output; pause cleanup owns it.");
    Require(!InputDispatchState.CanExecute(state.Current, state.Current, false, true), "Disabled service executed an action.");
}

static void RoutingIdentity()
{
    var state = new InputDispatchState();
    var profile = Guid.NewGuid();
    var first = new InputRoutingIdentity((nint)123, 42, @"C:\Apps\Editor.exe", profile, 1);
    var stamp = state.UpdateRouting(first);
    Require(state.UpdateRouting(first) == stamp, "Unchanged foreground poll invalidated current work.");
    var newWindow = state.UpdateRouting(first with { WindowHandle = (nint)124 });
    Require(newWindow.RoutingGeneration != stamp.RoutingGeneration, "Another window in same process/profile did not invalidate queued action.");
    var settingsEdit = state.UpdateRouting(first with { WindowHandle = (nint)124, SettingsRevision = 2 });
    Require(settingsEdit.RoutingGeneration != newWindow.RoutingGeneration, "Settings update kept stale action generation.");
    var newProfile = state.UpdateRouting(first with { WindowHandle = (nint)124, SettingsRevision = 2, ProfileId = Guid.NewGuid() });
    Require(newProfile.RoutingGeneration != settingsEdit.RoutingGeneration, "Profile switch kept stale action generation.");
    Require(newProfile.EnableEpoch == stamp.EnableEpoch, "Routing update must preserve held-output enable ownership.");
}

static void EnableGeneration()
{
    var state = new InputDispatchState();
    var beforePause = state.Current;
    state.AdvanceEnableEpoch();
    state.AdvanceEnableEpoch(); // Pause, then re-enable.
    Require(!InputDispatchState.CanExecute(beforePause, state.Current, true, false), "Re-enable revived stale queued work.");
    Require(!InputDispatchState.CanExecute(state.Current, state.Current, false, false), "Disabled service accepted current work.");
}

static void PassthroughGesture()
{
    var ledger = new InputGestureLedger<MouseControl>();
    ledger.Begin(MouseControl.LeftButton, new InputGesture(false, null, 1, 0));
    // A new profile now disables left click; release must use the original DOWN.
    Require(ledger.Release(MouseControl.LeftButton, out var gesture), "Held passthrough gesture lost.");
    Require(!gesture.Suppressed && gesture.Action is null, "UP was reinterpreted through a new mapping.");
    Require(!ledger.TryGet(MouseControl.LeftButton, out _), "Released gesture remained latched.");
}

static void SuppressedGesture()
{
    var ledger = new InputGestureLedger<MouseControl>();
    var action = new InputAction { Kind = ActionKind.MouseButton, MouseOutput = MouseControl.RightButton };
    ledger.Begin(MouseControl.XButton1, new InputGesture(true, action, 7, 42));
    // Pausing must release synthetic outputs without clearing physical suppression
    // latches: the matching physical UP is still consumed when it arrives.
    Require(ledger.Release(MouseControl.XButton1, out var gesture), "Suppressed gesture disappeared.");
    Require(gesture.Suppressed && gesture.Epoch == 7 && gesture.Token == 42
        && ReferenceEquals(gesture.Action, action), "Captured action/token was replaced before UP.");
}

static void AuxiliaryRepeat()
{
    var ledger = new InputGestureLedger<int>();
    var original = new InputGesture(true, new InputAction { Kind = ActionKind.Shortcut, KeyCode = 67 }, 5, 100);
    ledger.Begin(0x7C, original);
    Require(ledger.TryGet(0x7C, out var repeated) && ReferenceEquals(repeated, original), "Repeated key down cannot recover original suppression.");
    Throws<InvalidOperationException>(() => ledger.Begin(0x7C, new InputGesture(false, null, 6, 200)), "Autorepeat replaced a held action.");
    Require(ledger.Release(0x7C, out var released) && ReferenceEquals(released, original), "Key UP did not release the original gesture.");
    ledger.Begin(0x7C, new InputGesture(false, null, 6, 201));
    Require(ledger.Release(0x7C, out var next) && !next.Suppressed, "A subsequent physical press did not start a new gesture.");
}

static void UnknownRelease()
{
    var ledger = new InputGestureLedger<int>();
    Require(!ledger.Release(0x7C, out _), "Unmatched key UP manufactured a captured action.");
    ledger.Begin(0x7C, new InputGesture(false, null, 0, 0));
    Require(!ledger.Release(0x7D, out _), "Another key's UP released the held gesture.");
    Require(ledger.TryGet(0x7C, out _), "Unknown UP discarded another gesture.");
}

static void AuxiliarySources()
{
    Require(AuxiliaryInputFilter.ShouldObserve(0x7C, 0, 0), "Physical F13 must be observable.");
    Require(AuxiliaryInputFilter.ShouldObserve(0x87, 0x10, 123), "Manufacturer-software F24 must be observable even when injected.");
    Require(!AuxiliaryInputFilter.ShouldObserve(0x7C, 0x10, NativeMethods.InjectionMarker), "Our injected F13 could recursively invoke itself.");
    Require(!AuxiliaryInputFilter.ShouldObserve(0x87, 0, NativeMethods.InjectionMarker), "Own output marker must always prevent recursion.");
    foreach (var key in new uint[] { 0x41, 0x79, 0x7B, 0x88 })
        Require(!AuxiliaryInputFilter.ShouldObserve(key, 0x10, 123), "Ordinary/emergency key was captured by auxiliary route.");
}

static void UnacceptedPrefix()
{
    var tracker = new OwnedInputTracker();
    tracker.AcceptPrefix([Key(17, false), Key(65, false)], 0);
    Require(tracker.Count == 0 && tracker.PendingReleases.Length == 0, "A rejected SendInput batch must not grant ownership of planned keys.");
}

static void UnicodePrefix()
{
    var tracker = new OwnedInputTracker();
    tracker.AcceptPrefix([Unicode('\u015f', false), Unicode('\u015f', true)], 1);
    var release = tracker.PendingReleases;
    Require(release.Length == 1 && release[0].Type == 1
        && release[0].Data.Keyboard.VirtualKey == 0 && release[0].Data.Keyboard.Scan == '\u015f'
        && release[0].Data.Keyboard.Flags == (NativeMethods.Unicode | NativeMethods.KeyUp), "Odd Unicode prefix lost the exact matching UP.");
    tracker.AcceptPrefix(release, 1);
    Require(tracker.Count == 0, "Accepted Unicode UP did not settle ownership.");
}

static void CompletePairs()
{
    var tracker = new OwnedInputTracker();
    NativeMethods.Input[] sequence = [Key(17, false), Key(65, false), Key(65, true), Key(17, true), Unicode('x', false), Unicode('x', true)];
    tracker.AcceptPrefix(sequence, sequence.Length);
    Require(tracker.Count == 0, "Successful complete chord/text batch left phantom ownership.");
}

static void ShortcutPrefix()
{
    var tracker = new OwnedInputTracker();
    NativeMethods.Input[] chord = [Key(17, false), Key(16, false), Key(65, false), Key(65, true), Key(16, true), Key(17, true)];
    tracker.AcceptPrefix(chord, 2); // Windows accepted only Ctrl and Shift DOWN.
    var releases = tracker.PendingReleases;
    Require(releases.Length == 2 && releases[0].Data.Keyboard.VirtualKey == 16
        && releases[1].Data.Keyboard.VirtualKey == 17, "Partial chord cleanup must reverse accepted modifiers, excluding unaccepted A.");
    Require(releases.All(input => (input.Data.Keyboard.Flags & NativeMethods.KeyUp) != 0), "Cleanup includes a DOWN event.");
}

static void PartialCleanup()
{
    var tracker = new OwnedInputTracker();
    tracker.AcceptPrefix([Key(17, false), Key(16, false)], 2);
    var cleanup = tracker.PendingReleases;
    tracker.AcceptPrefix(cleanup, 1);
    Require(tracker.Count == 1 && tracker.PendingReleases[0].Data.Keyboard.VirtualKey == 17,
        "Partial cleanup forgot the unreleased Ctrl key.");
    tracker.AcceptPrefix(tracker.PendingReleases, 1);
    Require(tracker.Count == 0, "Cleanup retry failed to settle the remaining key.");
}

static void MousePrefix()
{
    var tracker = new OwnedInputTracker();
    NativeMethods.Input down = new() { Data = new() { Mouse = new() { Flags = 0x80, MouseData = 2, ExtraInfo = NativeMethods.InjectionMarker } } };
    tracker.AcceptPrefix([down], 1);
    var releases = tracker.PendingReleases;
    Require(releases.Length == 1 && releases[0].Type == 0 && releases[0].Data.Mouse.Flags == 0x100
        && releases[0].Data.Mouse.MouseData == 2 && releases[0].Data.Mouse.ExtraInfo == NativeMethods.InjectionMarker,
        "XBUTTON2 DOWN lost its matching tagged UP.");
    tracker.AcceptPrefix(releases, 1);
    Require(tracker.Count == 0, "Mouse UP failed to settle owned output.");
}

static void EmergencyFirstAvailable()
{
    var attempts = new List<uint>();
    var registered = EmergencyHotkeySelector.TryRegister(key => { attempts.Add(key); return true; }, out var selected);
    Require(registered && selected == 0x79, "Available F10 was not selected.");
    Require(attempts.SequenceEqual(new uint[] { 0x79 }), "Registration continued after first success.");
    Require(EmergencyHotkeySelector.DisplayName(selected) == "Ctrl+Alt+F10", "Displayed hotkey differs from registered key.");
}

static void EmergencyFallback()
{
    var attempts = new List<uint>();
    var registered = EmergencyHotkeySelector.TryRegister(key => { attempts.Add(key); return key == 0x78; }, out var selected);
    Require(registered && selected == 0x78 && attempts.SequenceEqual(new uint[] { 0x79, 0x78 }), "F10 conflict did not choose F9 and stop.");
    Require(EmergencyHotkeySelector.DisplayName(selected) == "Ctrl+Alt+F9", "F9 fallback display is misleading.");
    attempts.Clear();
    registered = EmergencyHotkeySelector.TryRegister(key => { attempts.Add(key); return key == 0x77; }, out selected);
    Require(registered && selected == 0x77 && attempts.SequenceEqual(new uint[] { 0x79, 0x78, 0x77 }), "F10/F9 conflicts did not choose F8.");
    Require(EmergencyHotkeySelector.DisplayName(selected) == "Ctrl+Alt+F8", "F8 fallback display is misleading.");
}

static void EmergencyAllBusy()
{
    var attempts = new List<uint>();
    var registered = EmergencyHotkeySelector.TryRegister(key => { attempts.Add(key); return false; }, out var selected);
    Require(!registered && selected == 0, "All-busy registration claimed an emergency binding.");
    Require(attempts.SequenceEqual(new uint[] { 0x79, 0x78, 0x77 }), "Fallback order changed or unsupported keys were attempted.");
    Require(!attempts.Contains(0x7B), "Debugger-reserved F12 must not be attempted.");
    Require(EmergencyHotkeySelector.DisplayName(selected) == "Not registered"
        && EmergencyHotkeySelector.DisplayName(0x7B) == "Not registered", "Unavailable/reserved binding was displayed as registered.");
}

static NativeMethods.Input Key(ushort key, bool up) => new()
{
    Type = 1,
    Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? NativeMethods.KeyUp : 0, ExtraInfo = NativeMethods.InjectionMarker } }
};

static NativeMethods.Input Unicode(char value, bool up) => new()
{
    Type = 1,
    Data = new() { Keyboard = new() { Scan = value, Flags = NativeMethods.Unicode | (up ? NativeMethods.KeyUp : 0), ExtraInfo = NativeMethods.InjectionMarker } }
};

static void RingActions()
{
    var slots = RingDefaults.Create(); slots[0] = new() { Name = "Klasör", Kind = RingSlotKind.Folder, Children = RingNavigation.EmptyFolder() };
    slots[0].Children[1] = new() { Name = "Ses", Kind = RingSlotKind.Adjustment,
        IncreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAF }, DecreaseAction = new() { Kind = ActionKind.Shortcut, KeyCode = 0xAE } };
    var request = new RingRequest(Guid.NewGuid(), 123, 2, 3, slots);
    Require(!RingActionSelection.TryResolve(request, new(new[] { 0 }), out _, out _), "Folder executed as command.");
    Require(RingActionSelection.TryResolve(request, new(new[] { 0, 1 }, 1), out var up, out var close) && up.KeyCode == 0xAF && !close, "Increase closed ring or selected wrong action.");
    Require(RingActionSelection.TryResolve(request, new(new[] { 0, 1 }, -1), out var down, out close) && down.KeyCode == 0xAE && !close, "Decrease selected wrong action.");
    Require(!RingActionSelection.TryResolve(request, new(new[] { 0, 1 }), out _, out _), "Click fired adjustment without scrolling.");
    Require(!RingActionSelection.TryResolve(request, new(new[] { 1 }, 1), out _, out _), "Scroll fired ordinary command.");
    Require(RingActionSelection.TryResolve(request, new(new[] { 1 }), out _, out close) && close, "Ordinary command did not close.");
}
static void RingDispatch()
{
    var request = new RingRequest(Guid.NewGuid(), 123, 2, 3, RingDefaults.Create());
    Require(RingActionSelection.CanDispatch(request, new(2, 3), true, 123), "Current ring rejected.");
    Require(!RingActionSelection.CanDispatch(request, new(2, 3), false, 123), "Paused ring executed.");
    Require(!RingActionSelection.CanDispatch(request, new(3, 3), true, 123), "Old enable epoch executed.");
    Require(!RingActionSelection.CanDispatch(request, new(2, 4), true, 123), "Changed profile executed.");
    Require(!RingActionSelection.CanDispatch(request, new(2, 3), true, 456), "Changed target received ring action.");
}
static void RingPointer()
{
    Require(RingActionSelection.OwnPointer(MouseControl.LeftButton, true), "Overlay click leaked to underlying app.");
    Require(RingActionSelection.OwnPointer(MouseControl.XButton1, false), "Back click leaked while ring open.");
    Require(RingActionSelection.OwnPointer(MouseControl.XButton2, false), "Forward click leaked while ring open.");
    Require(!RingActionSelection.OwnPointer(MouseControl.LeftButton, false), "Outside click lost ordinary behavior.");
    var ringId = Guid.NewGuid(); var ledger = new InputGestureLedger<MouseControl>();
    ledger.Begin(MouseControl.LeftButton, new(true, null, 1, 0, ringId));
    Require(ledger.Release(MouseControl.LeftButton, out var held) && held.Suppressed && held.RingClickId == ringId, "Ring click lost matching swallowed release.");
}

static void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

static void GenericHidCapabilities()
{
    var bytes = new byte[144];
    BitConverter.GetBytes((ushort)0xFF42).CopyTo(bytes, 0); bytes[2] = 7; BitConverter.GetBytes((ushort)3).CopyTo(bytes, 6); bytes[12] = 1;
    BitConverter.GetBytes((ushort)8).CopyTo(bytes, 56); BitConverter.GetBytes((ushort)12).CopyTo(bytes, 58);
    BitConverter.GetBytes((ushort)9).CopyTo(bytes, 72); BitConverter.GetBytes((ushort)20).CopyTo(bytes, 72 + 56);
    var caps = HidCapabilities.ParseButtonCaps(bytes);
    Require(caps.Count == 2 && caps[0].UsagePage == 0xFF42 && caps[0].ReportId == 7 && caps[0].Collection == 3 && caps[0].Count == 5 && caps[1].Minimum == 20 && caps[1].Maximum == 20, "Descriptor decoding assumed a brand or fixed button count.");
    bool rejected = false; try { HidCapabilities.ParseButtonCaps(new byte[71]); } catch (ArgumentException) { rejected = true; } Require(rejected, "Truncated native capability accepted.");
    Require(HidCapabilities.EndpointId("path:a") == HidCapabilities.EndpointId("PATH:A") && HidCapabilities.EndpointId("path:a") != HidCapabilities.EndpointId("path:b"), "Endpoint IDs collide or depend on case.");
}
static void GenericRawLearning()
{
    var packet = new byte[24]; BitConverter.GetBytes((ushort)(0x10 | 0x20 | 0x800)).CopyTo(packet, 4); BitConverter.GetBytes((short)-120).CopyTo(packet, 6);
    BitConverter.GetBytes(1234).CopyTo(packet, 12); BitConverter.GetBytes(-456).CopyTo(packet, 16);
    var signals = RawInputDecoder.Mouse("mouse:a", new string('A', 64), packet);
    Require(signals.Count == 3 && signals[0].Identity.StandardControl == MouseControl.MiddleButton && signals[0].Pressed && !signals[1].Pressed && signals[2].Identity.StandardControl == MouseControl.WheelLeft, "Mouse edges/wheel direction decoded incorrectly.");
    Require(signals.All(s => s.DeviceKey == "mouse:a") && RawInputDecoder.Mouse("a", "endpoint", new byte[23]).Count == 0, "Source identity or packet bound was lost.");
    Require(RawInputDecoder.Mouse("a", "endpoint", new byte[24]).Count == 0, "Pointer motion created a learned button.");
}
static void KeyboardLearningPrivacy()
{
    var packet = new byte[16]; BitConverter.GetBytes((ushort)0x41).CopyTo(packet, 6);
    Require(RawInputDecoder.Keyboard("a", "endpoint", packet) is null, "Ordinary typing entered button learning.");
    BitConverter.GetBytes((ushort)0x7C).CopyTo(packet, 6); var down = RawInputDecoder.Keyboard("a", "endpoint", packet);
    BitConverter.GetBytes((ushort)1).CopyTo(packet, 2); var up = RawInputDecoder.Keyboard("a", "endpoint", packet);
    Require(down is { Pressed: true } && up is { Pressed: false } && down.Identity.Kind == LearnedInputKind.KeyboardKey, "F13 transitions were not preserved.");
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void DevicePacket()
{
    var packet = new InterceptionBackend.MouseStroke { State = 0x81, Flags = 4, X = 17, Y = -5, Information = 123 };
    var seen = new List<(MouseControl, bool)>();
    var result = InterceptionBackend.Rewrite(packet, (input, release, delta) => { seen.Add((input, release)); return input == MouseControl.LeftButton; });
    Require(result.State == 0x80 && result.X == 17 && result.Y == -5 && result.Flags == 4 && result.Information == 123, "Mapped button damaged motion or another button.");
    Require(seen.Contains((MouseControl.XButton1, true)) && seen.Contains((MouseControl.LeftButton, false)), "Packet edge decode failed.");
}
static void DeviceWheels()
{
    var packet = new InterceptionBackend.MouseStroke { State = 0xC00, Rolling = -30, X = 3 };
    var seen = new List<MouseControl>();
    var result = InterceptionBackend.Rewrite(packet, (input, up, delta) => { seen.Add(input); Require(delta == -30, "Signed delta changed."); return input == MouseControl.WheelDown; });
    Require(result.State == 0x800 && result.Rolling == -30 && result.X == 3, "One axis consumed the other axis or motion.");
    Require(seen.Contains(MouseControl.WheelLeft), "Horizontal wheel was not routed.");
    result = InterceptionBackend.Rewrite(result, (_, _, _) => true);
    Require(result.State == 0 && result.Rolling == 0 && result.X == 3, "Consumed wheel did not preserve motion.");
}
static void DeviceGestures()
{
    var ledger = new InputGestureLedger<(string, MouseControl)>();
    ledger.Begin(("A", MouseControl.LeftButton), new(true, new InputAction { Kind = ActionKind.Ring }, 1, 2));
    ledger.Begin(("B", MouseControl.LeftButton), new(false, null, 1, 3));
    Require(ledger.Release(("B", MouseControl.LeftButton), out var second) && !second.Suppressed, "Source B inherited A suppression.");
    Require(ledger.Release(("A", MouseControl.LeftButton), out var first) && first.Suppressed && first.Action!.Kind == ActionKind.Ring, "Source A lost its physical release.");
}
static void MissingDeviceLibrary()
{
    Throws<FileNotFoundException>(() => new InterceptionBackend(""), "Implicit native library search was allowed.");
}

static void CaptureIdle()
{
    var state = new ShortcutCaptureTracker();
    Require(!state.Process(0, 65, false, false, false, 0).Suppress, "Idle capture consumed typing.");
    Require(!state.Process(0, 65, true, false, true, 0).Suppress && !state.HasOwnedKeys, "Idle capture owned an unrelated key.");
}
static void CaptureAltTab()
{
    var state = new ShortcutCaptureTracker();
    var modifier = state.Process(1, 0xA4, false, true, false, 0);
    Require(modifier.Suppress && modifier.Publish && modifier.KeyCode == 0 && modifier.Modifiers == KeyModifiers.Alt, "Alt was not reflected immediately.");
    var tab = state.Process(1, 9, false, true, false, 0);
    Require(tab.Suppress && tab.Publish && tab.KeyCode == 9 && tab.Modifiers == KeyModifiers.Alt, "Alt+Tab was not captured before OS dispatch.");
    Require(state.Process(1, 9, true, true, false, 0).Suppress && state.Process(1, 0xA4, true, true, false, 0).Suppress && !state.HasOwnedKeys, "Chord releases leaked.");
}
static void CaptureEnd()
{
    var state = new ShortcutCaptureTracker();
    state.Process(1, 0xA2, false, true, false, 0); state.Process(1, 67, false, true, false, 0);
    Require(state.Process(0, 0xA2, false, false, true, 0).Suppress, "Owned repeat leaked after focus loss.");
    Require(state.Process(0, 67, true, false, false, 0).Suppress && state.Process(0, 0xA2, true, false, false, 0).Suppress, "Focus loss lost owned UP.");
    Require(!state.Process(0, 68, false, false, false, 0).Suppress, "Another field's typing was captured.");
}
static void CaptureExistingModifier()
{
    var state = new ShortcutCaptureTracker();
    Require(!state.Process(1, 0xA2, false, true, true, KeyModifiers.Control).Suppress, "An already delivered modifier was adopted.");
    var captured = state.Process(1, 67, false, true, false, KeyModifiers.Control);
    Require(captured.Modifiers == KeyModifiers.Control && captured.KeyCode == 67, "Preexisting modifier was not represented.");
    Require(state.Process(1, 67, true, true, false, 0).Suppress && !state.Process(1, 0xA2, true, true, false, 0).Suppress, "Preexisting modifier release was swallowed.");
}
static void CaptureModifierOrder()
{
    var state = new ShortcutCaptureTracker(); state.Process(1, 65, false, true, false, 0);
    var shifted = state.Process(1, 0xA0, false, true, false, 0);
    Require(shifted.KeyCode == 65 && shifted.Modifiers == KeyModifiers.Shift, "Modifier pressed after a held primary was lost.");
    state.Process(1, 65, true, true, false, 0);
    var next = state.Process(1, 0xA2, false, true, false, 0);
    Require(next.KeyCode == 0, "A released primary incorrectly became a new chord.");
}
static void CaptureReplace()
{
    var state = new ShortcutCaptureTracker(); state.Process(1, 0xA4, false, true, false, 0);
    var next = state.Process(2, 9, false, true, false, 0);
    Require(next.KeyCode == 9 && next.Modifiers == KeyModifiers.Alt, "Replacement field lost a physically held captured modifier.");
    Require(state.Process(2, 0xA4, true, false, false, 0).Suppress, "Replacement field leaked the old field's release.");
}
static void CaptureEscape()
{
    var state = new ShortcutCaptureTracker(); var cancel = state.Process(1, 27, false, true, false, 0);
    Require(cancel.Suppress && cancel.Cancelled, "Escape did not cancel the focused capture.");
    Require(state.Process(0, 27, false, false, true, 0).Suppress && state.Process(0, 27, true, false, false, 0).Suppress && !state.HasOwnedKeys, "Cancelled capture leaked Escape repeat/up.");
}

static void CaptureBaseline()
{
    var state = new ShortcutCaptureTracker(); state.SetBaseline(1, new[] { 0xA2 });
    Require(state.ExistingModifiers == KeyModifiers.Control && state.WasDownAtStart(0xA2), "Pre-focus modifier snapshot missing.");
    Require(!state.Process(1, 0xA2, true, true, true, state.ExistingModifiers).Suppress, "Already-delivered release was swallowed.");
    Require(state.ExistingModifiers == KeyModifiers.None && !state.WasDownAtStart(0xA2), "Released baseline modifier remained active.");
    Require(ShortcutCaptureTracker.NormalizeModifier(0x11, 0x1D, 0) == 0xA2 && ShortcutCaptureTracker.NormalizeModifier(0x11, 0x1D, 1) == 0xA3, "Control sides were not normalized.");
    Require(ShortcutCaptureTracker.NormalizeModifier(0x10, 0x36, 0) == 0xA1 && ShortcutCaptureTracker.NormalizeModifier(0x12, 0x38, 1) == 0xA5, "Shift/Alt sides were not normalized.");
}

static void OwnWindowSwitching()
{
    var action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
    foreach (var button in new[] { MouseControl.XButton1, MouseControl.XButton2 })
    {
        Require(!OwnWindowPolicy.ShouldBypass(false, button, action, false), "External window switching was bypassed.");
        Require(!OwnWindowPolicy.ShouldBypass(true, button, action, false), "Returning to Clickloom changed the mapped button into passthrough.");
        action.Modifiers = KeyModifiers.Alt | KeyModifiers.Shift;
        Require(!OwnWindowPolicy.ShouldBypass(true, button, action, false), "Reverse window switching was bypassed.");
        action.Modifiers = KeyModifiers.Alt;
    }
}
static void OwnWindowEditing()
{
    var action = new InputAction { Kind = ActionKind.Shortcut, KeyCode = 9, Modifiers = KeyModifiers.Alt };
    Require(OwnWindowPolicy.ShouldBypass(true, MouseControl.XButton1, action, true), "A shortcut field could be switched away by a mapped side button.");
    Require(OwnWindowPolicy.ShouldBypass(true, MouseControl.LeftButton, action, false), "Editor left-click was remapped.");
    Require(OwnWindowPolicy.ShouldBypass(true, MouseControl.XButton1, new InputAction { Kind = ActionKind.Disabled }, false), "Editor safety bypass was lost.");
    action.Modifiers = KeyModifiers.Control;
    Require(OwnWindowPolicy.ShouldBypass(true, MouseControl.XButton1, action, false), "A regular editing shortcut gained an unintended exception.");
    Require(OwnWindowPolicy.ShouldBypass(true, MouseControl.XButton1, null, false), "An unassigned side button gained an exception.");
}
