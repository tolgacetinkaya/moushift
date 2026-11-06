using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Clickloom.Core;

namespace Clickloom.Windows;

/// <summary>Device-specific Circle observation with optional exclusive remapping. Starts disarmed.</summary>
public sealed partial class MouseRemappingService : IDisposable
{
    private sealed record RoutingSnapshot(string Name, Dictionary<MouseControl, InputAction> Actions, Dictionary<int, InputAction> AuxiliaryKeys, bool OwnWindow, long Generation, nint ForegroundWindow)
    { public MappingProfile? Profile { get; init; } public string? ApplicationPath { get; init; } }
    private sealed record Work(InputAction Action, bool IsRelease, bool IsHold, long Token, long Epoch, long RoutingGeneration, nint ForegroundWindow, MappingProfile? Profile = null, bool DriverlessCircle = false);
    private readonly ConcurrentQueue<Work> _queue = new();
    private readonly ConcurrentQueue<MouseControl> _observations = new();
    private readonly ConcurrentQueue<int> _auxiliaryObservations = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly InputGestureLedger<MouseControl> _gestures = new(); // Hook-thread ownership only.
    private readonly InputGestureLedger<int> _keyGestures = new(); // F13-F24 only; no ordinary key capture.
    private readonly InputGestureLedger<(string, MouseControl)> _deviceGestures = new();
    private readonly Dictionary<(string, bool), (DispatchStamp Stamp, int Delta)> _deviceWheels = new();
    private InterceptionBackend? _deviceBackend;
    private DeviceRouteTable _deviceRoutes = DeviceRouteTable.Empty;
    private volatile string? _foregroundPath;
    private readonly InputDispatchState _dispatchState = new();
    private readonly Dictionary<long, MouseControl> _heldOutputs = new(); // Worker ownership only.
    private readonly Dictionary<MouseControl, int> _outputOwners = new();
    private readonly OwnedInputTracker _pendingOneShotInputs = new(); // Accepted native DOWN ownership, worker only.
    private readonly object _settingsGate = new();
    private readonly NativeMethods.HookProc _hookProc;
    private readonly NativeMethods.HookProc _keyboardProc;
    private AppSettings _settings = AppSettings.CreateDefault();
    private RoutingSnapshot _snapshot = new("Global", new(), new(), false, 0, 0);
    private Thread? _hookThread, _workerThread;
    private Timer? _foregroundTimer;
    private nint _hook, _keyboardHook;
    private uint _hookThreadId;
    private volatile bool _enabled, _running, _disposed, _stopping;
    private long _token, _settingsRevision;
    private int _queueCount, _observationCount, _polling;
    private int _wheelVertical, _wheelHorizontal;
    private RoutingSnapshot? _lastWheelSnapshot;
    private long _lastWheelEpoch;
    private string _foreground = "Unknown", _activeProfile = "Global";
    private string? _lastError;
    private string _emergencyShortcut = "Not registered";
    private RingRequest? _ringRequest;
    private sealed record CaptureSession(long Id, nint Window, int[] AlreadyDown);
    private CaptureSession? _captureSession;
    private readonly ShortcutCaptureTracker _captureTracker = new();
    private readonly ConcurrentQueue<ShortcutCaptureUpdate> _captureUpdates = new();
    private long _captureSequence;
    private uint _emergencyKey;
    private volatile bool _ringEscapeDown;
    private sealed record RingBounds(int Left, int Top, int Right, int Bottom);
    private RingBounds _ringBounds = new(0, 0, 0, 0);

    private readonly string? _deviceLibrary;
    public MouseRemappingService(string? deviceLibrary = null) { _deviceLibrary = deviceLibrary; _hookProc = HookCallback; _keyboardProc = KeyboardCallback; }
    public bool IsEnabled => _enabled;
    public bool IsRunning => _running;
    private string _inputStatus = "No mouse button detected yet.";
    public string InputStatus => Volatile.Read(ref _inputStatus);
    public string? LastError => _lastError;
    public string ForegroundApplication => _foreground;
    public string ActiveProfileName => _activeProfile;
    public string EmergencyShortcut => _emergencyShortcut;
    public IReadOnlyList<DeviceSourceInfo> DeviceSources => _deviceBackend is { IsCapturing: true } backend ? backend.Sources : [];
    public bool HasDeviceRouting => _deviceBackend is { IsCapturing: true };
    public bool HasResolvedDeviceRoutes => HasDeviceRouting && Volatile.Read(ref _deviceRoutes).Count > 0;
    public bool CanRouteDevice(string key) => HasDeviceRouting && Volatile.Read(ref _deviceRoutes).ContainsDevice(key);
    public void UpdateDeviceInventory(IReadOnlyList<MouseConnection> connections)
    {
        bool rawChanged;
        lock (_settingsGate) rawChanged = _rawCircles.Update(connections);
        var next = DeviceRouteTable.Build(DeviceSources, connections);
        var previous = Volatile.Read(ref _deviceRoutes);
        if (previous.SameAs(next) && !rawChanged) return;
        Volatile.Write(ref _deviceRoutes, next); _dispatchState.AdvanceRouting();
        DismissRing();
    }
    public void InvalidateDeviceRoutes()
    {
        lock (_settingsGate) _rawCircles.Update([]);
        Volatile.Write(ref _deviceRoutes, DeviceRouteTable.Empty);
        if (_enabled) SetEnabled(false);
        _dispatchState.AdvanceRouting();
    }
    public event EventHandler? StateChanged;
    public event EventHandler<string>? ActionError;
    public event EventHandler<MouseControl>? InputObserved;
    public event EventHandler<int>? AuxiliaryInputObserved;
    public event EventHandler<RingRequest>? RingRequested;
    public event EventHandler<Guid>? RingTriggerReleased;
    public event EventHandler<RingPointerRequest>? RingClicked;
    public event EventHandler<RingPointerRequest>? RingPressed;
    public event EventHandler<CirclePreferenceRequest>? CirclePreferenceRequested;
    public event EventHandler<RingScrollRequest>? RingScrolled;
    public event EventHandler? RingDismissed;
    public event EventHandler<ShortcutCaptureUpdate>? ShortcutCaptured;
    public long BeginShortcutCapture(nint window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_running) throw new InvalidOperationException("Keyboard capture is not ready.");
        NativeMethods.GetWindowThreadProcessId(window, out var process);
        if (window == 0 || process != Environment.ProcessId) throw new ArgumentException("Shortcut capture must start inside this application.");
        long id = Interlocked.Increment(ref _captureSequence);
        // Snapshot outside the hook: Windows has not updated asynchronous key
        // state yet when it invokes LowLevelKeyboardProc.
        var alreadyDown = Enumerable.Range(8, 247).Where(key => key is not (0x10 or 0x11 or 0x12)
            && (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0).ToArray();
        Volatile.Write(ref _captureSession, new(id, window, alreadyDown)); return id;
    }
    public void EndShortcutCapture(long id)
    {
        var session = Volatile.Read(ref _captureSession);
        if (session?.Id == id) Interlocked.CompareExchange(ref _captureSession, null, session);
    }
    public bool IsRingOpen => Volatile.Read(ref _ringRequest) is not null;
    public bool IsCurrentRing(Guid id) => Volatile.Read(ref _ringRequest)?.Id == id;

    public void SetRingBounds(int left, int top, int width, int height) => Volatile.Write(ref _ringBounds, new(left, top, left + width, top + height));
    public void DismissRing()
        => DismissRing("application/selection");
    private void DismissRing(string reason)
    {
        if (Interlocked.Exchange(ref _ringRequest, null) is not null)
        {
            TraceCircle("close", reason);
            NotifyRingDismissed();
        }
    }
    private void NotifyRingDismissed() => ThreadPool.QueueUserWorkItem(_ => { try { RingDismissed?.Invoke(this, EventArgs.Empty); } catch { } });
    public bool ChooseRingAction(Guid requestId, int index)
        => InvokeRingAction(requestId, new RingInvocation(new[] { index }));

    public bool InvokeRingAction(Guid requestId, RingInvocation invocation)
    {
        var request = Volatile.Read(ref _ringRequest);
        if (request is null || request.Id != requestId) return false;
        if (!RingActionSelection.TryResolve(request, invocation, out var action, out bool close)) return false;
        var stamp = _dispatchState.Current;
        if (!RingActionSelection.CanDispatch(request, stamp, _enabled, NativeMethods.GetForegroundWindow())) { DismissRing(); return false; }
        if (close)
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _ringRequest, null, request), request)) return false;
            NotifyRingDismissed();
        }
        else if (!IsCurrentRing(request.Id)) return false;
        if (action.Kind is ActionKind.Ring or ActionKind.Passthrough or ActionKind.Disabled) return true;
        if (action.Kind is ActionKind.SelectCircleProfile or ActionKind.ToggleCircleAdapt)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceKey)) return false;
            Guid? target = Guid.TryParse(action.Arguments, out var selected) ? selected : null;
            try { CirclePreferenceRequested?.Invoke(this, new(request.DeviceKey, action.Kind, target)); return true; }
            catch { return false; }
        }
        return Enqueue(new(action.DeepClone(), false, false, Interlocked.Increment(ref _token), stamp.EnableEpoch, stamp.RoutingGeneration, request.TargetWindow));
    }

    private void ReleaseRingTrigger(InputGesture gesture)
    {
        if (gesture.Action?.Kind != ActionKind.Ring) return;
        var stamp = _dispatchState.Current;
        if (!Enqueue(new(gesture.Action, true, false, gesture.Token, gesture.Epoch, stamp.RoutingGeneration, 0)))
            DisarmFromHook("Ring release queue is full.");
    }
    private int _ringWheelRemainder;
    private void ScrollRing(short delta)
    {
        var request = Volatile.Read(ref _ringRequest); if (request is null) return;
        _ringWheelRemainder += delta;
        while (Math.Abs(_ringWheelRemainder) >= 120)
        {
            int step = Math.Sign(_ringWheelRemainder); _ringWheelRemainder -= step * 120;
            try { RingScrolled?.Invoke(this, new(request.Id, step)); } catch { DismissRing(); }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookThread is not null) throw new InvalidOperationException("This remapping service has already been started.");
        var diagnostics = NativeDiagnostics.SelfCheck();
        if (diagnostics.Count > 0) throw new InvalidOperationException(string.Join(" ", diagnostics));
        if (_deviceLibrary is not null || string.Equals(Environment.GetEnvironmentVariable("CLICKLOOM_DEVICE_BACKEND"), "interception", StringComparison.OrdinalIgnoreCase))
        {
            _deviceBackend = new InterceptionBackend(_deviceLibrary ?? Environment.GetEnvironmentVariable("CLICKLOOM_INTERCEPTION_DLL") ?? "");
            _deviceBackend.Faulted += (_, error) => DisarmFromHook("Device input backend stopped: " + error);
        }
        PollForeground(null);
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "Moushift actions" };
        _hookThread = new Thread(HookLoop) { IsBackground = true, Name = "Moushift mouse hook" };
        _workerThread.Start();
        _hookThread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _stopping = true;
            _wake.Set();
            if (_hookThreadId != 0) NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WmQuit, 0, 0);
            throw new TimeoutException("The mouse input thread did not become ready; remapping remains off.");
        }
        if (!_running)
        {
            _stopping = true;
            _wake.Set();
            _workerThread.Join(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException(_lastError ?? "The mouse input service could not start.");
        }
        try { _deviceBackend?.Start(ProcessDeviceInput); }
        catch
        {
            _stopping = true; _enabled = false; _wake.Set();
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WmQuit, 0, 0);
            _hookThread.Join(TimeSpan.FromSeconds(3)); _workerThread.Join(TimeSpan.FromSeconds(3));
            throw;
        }
        _foregroundTimer = new Timer(PollForeground, null, 0, 150);
    }

    public void ApplySettings(AppSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        lock (_settingsGate)
        {
            _settings = settings.DeepClone();
            _settingsRevision++;
            _dispatchState.AdvanceRouting();
        }
        PollForeground(null);
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (enabled && !_running) throw new InvalidOperationException(_lastError ?? "The hook and emergency hotkey must be available before remapping is enabled.");
        if (enabled && !CanEnable) throw new InvalidOperationException("Connect a mouse and wait for Raw Input to be ready. Exclusive remapping requires optional device input support.");
        if (enabled && _deviceBackend is not null && !_deviceBackend.IsCapturing) throw new InvalidOperationException("The device input backend stopped. Restart before enabling it.");
        if (_enabled == enabled) return;
        if (!enabled) _enabled = false;
        if (!enabled) DismissRing();
        _dispatchState.AdvanceEnableEpoch();
        if (enabled) { _lastError = null; _enabled = true; }
        _wake.Set();
        RaiseStateChanged();
    }

    private void HookLoop()
    {
        var hotkeyRegistered = false;
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, 0, 0, 0, NativeMethods.PmNoRemove);
            hotkeyRegistered = EmergencyHotkeySelector.TryRegister(
                key => NativeMethods.RegisterHotKey(0, 0x434C, 0x4000 | 0x0002 | 0x0001, key), out var emergencyKey);
            if (!hotkeyRegistered) throw new Win32Exception(Marshal.GetLastWin32Error(), "None of Ctrl+Alt+F10, Ctrl+Alt+F9, or Ctrl+Alt+F8 is available; remapping remains off.");
            _emergencyShortcut = EmergencyHotkeySelector.DisplayName(emergencyKey);
            _emergencyKey = emergencyKey;
            if (_stopping) return;
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _hookProc, NativeMethods.GetModuleHandle(null), 0);
            if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not install the mouse hook.");
            _keyboardHook = NativeMethods.SetWindowsHookEx(13, _keyboardProc, NativeMethods.GetModuleHandle(null), 0);
            if (_keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not install the optional F13-F24 input hook.");
            _running = true;
            _ready.Set();
            RaiseStateChanged();
            while (!_stopping)
            {
                var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result == 0) break;
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The input message loop failed.");
                if (message.Id == NativeMethods.WmHotKey && message.WParam == 0x434C)
                {
                    _enabled = false;
                    DismissRing("pause hotkey");
                    _dispatchState.AdvanceEnableEpoch();
                    _wake.Set();
                    RaiseStateChanged();
                    continue;
                }
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _lastError = exception is Win32Exception nativeError
                ? $"{nativeError.Message} (Windows error {nativeError.NativeErrorCode})"
                : exception.Message;
            _enabled = false;
            _dispatchState.AdvanceEnableEpoch();
            _wake.Set();
        }
        finally
        {
            _running = false;
            _enabled = false;
            _dispatchState.AdvanceEnableEpoch();
            if (_hook != 0)
            {
                if (!NativeMethods.UnhookWindowsHookEx(_hook)) _lastError = "Windows reported a mouse-hook cleanup failure.";
                _hook = 0;
            }
            if (_keyboardHook != 0)
            {
                if (!NativeMethods.UnhookWindowsHookEx(_keyboardHook)) _lastError = "Windows reported an auxiliary-key-hook cleanup failure.";
                _keyboardHook = 0;
            }
            if (hotkeyRegistered) NativeMethods.UnregisterHotKey(0, 0x434C);
            _emergencyShortcut = "Not registered";
            _ready.Set();
            _wake.Set();
            RaiseStateChanged();
        }
    }

    private nint KeyboardCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            if (data.ExtraInfo != NativeMethods.InjectionMarker)
            {
                bool release = (uint)wParam is 0x101 or 0x105;
                bool press = (uint)wParam is 0x100 or 0x104;
                var session = Volatile.Read(ref _captureSession);
                bool active = session is not null && NativeMethods.GetForegroundWindow() == session.Window;
                if ((session is not null || _captureTracker.HasOwnedKeys) && (release || press))
                {
                    _captureTracker.SetBaseline(session?.Id ?? 0, session?.AlreadyDown ?? []);
                    int captureKey = ShortcutCaptureTracker.NormalizeModifier((int)data.VirtualKey, data.ScanCode, data.Flags);
                    var decision = _captureTracker.Process(session?.Id ?? 0, captureKey, release, active,
                        _captureTracker.WasDownAtStart(captureKey), _captureTracker.ExistingModifiers);
                    if (decision.Publish && session is not null)
                    {
                        bool emergency = decision.KeyCode == _emergencyKey && decision.Modifiers == (KeyModifiers.Control | KeyModifiers.Alt);
                        bool cancelled = decision.Cancelled || emergency;
                        if (cancelled) EndShortcutCapture(session.Id);
                        if (emergency) { _enabled = false; _dispatchState.AdvanceEnableEpoch(); DismissRing(); RaiseStateChanged(); }
                        if (Interlocked.Increment(ref _observationCount) <= 64 || cancelled)
                        { _captureUpdates.Enqueue(new(session.Id, decision.KeyCode, decision.Modifiers, cancelled)); _wake.Set(); }
                        else Interlocked.Decrement(ref _observationCount);
                    }
                    if (decision.Suppress) return 1;
                }
            }
            if (data.VirtualKey == 0x1B && data.ExtraInfo != NativeMethods.InjectionMarker)
            {
                if ((uint)wParam is 0x101 or 0x105 && _ringEscapeDown) { _ringEscapeDown = false; return 1; }
                if ((uint)wParam is 0x100 or 0x104 && _ringEscapeDown) return 1;
                if ((uint)wParam is 0x100 or 0x104 && IsRingOpen) { _ringEscapeDown = true; DismissRing(); return 1; }
            }
            if (!AuxiliaryInputFilter.ShouldObserve(data.VirtualKey, data.Flags, data.ExtraInfo))
                return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            var key = (int)data.VirtualKey;
            var message = (uint)wParam;
            if (message is 0x101 or 0x105)
            {
                if (_keyGestures.Release(key, out var gesture) && gesture.Suppressed)
                {
                    ReleaseRingTrigger(gesture);
                    if (gesture.Action?.Kind == ActionKind.MouseButton && IsButton(gesture.Action.MouseOutput))
                        if (!Enqueue(new Work(gesture.Action, true, true, gesture.Token, gesture.Epoch, 0, 0))) DisarmFromHook("The auxiliary release queue was full; Moushift paused.");
                    return 1;
                }
            }
            else if (message is 0x100 or 0x104)
            {
                if (_keyGestures.TryGet(key, out var existing))
                    return existing.Suppressed ? 1 : NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
                if (Interlocked.Increment(ref _observationCount) <= 32) { _auxiliaryObservations.Enqueue(key); _wake.Set(); }
                else Interlocked.Decrement(ref _observationCount);
                var snapshot = Volatile.Read(ref _snapshot);
                var stamp = _dispatchState.Current;
                var epoch = stamp.EnableEpoch;
                if (!_enabled || snapshot.Generation != stamp.RoutingGeneration || snapshot.OwnWindow || !snapshot.AuxiliaryKeys.TryGetValue(key, out var action) || action.Kind == ActionKind.Passthrough)
                    _keyGestures.Begin(key, new(false, null, epoch, 0));
                else
                {
                    var token = Interlocked.Increment(ref _token);
                    if (action.Kind == ActionKind.Disabled || Enqueue(new(action, false, action.Kind == ActionKind.MouseButton && IsButton(action.MouseOutput), token, epoch, snapshot.Generation, snapshot.ForegroundWindow)))
                    { _keyGestures.Begin(key, new(true, action, epoch, token)); return 1; }
                    _keyGestures.Begin(key, new(false, null, epoch, 0));
                    DisarmFromHook("The auxiliary action queue was full; Moushift paused.");
                }
            }
        }
        catch (Exception exception) { DisarmFromHook("Auxiliary input processing paused: " + exception.Message); }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
            if (data.ExtraInfo == NativeMethods.InjectionMarker)
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            if ((data.Flags & 0x1) != 0)
            {
                var injected = DecodeButton((uint)wParam, data.MouseData, out var injectedUp);
                if (injected is { } input && !injectedUp)
                { Volatile.Write(ref _inputStatus, input + ": vendor/software input; not remapped in this mode."); Observe(input); }
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            }
            if (HasDeviceRouting) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            var message = (uint)wParam;
            var control = DecodeButton(message, data.MouseData, out var isUp);
            if (control is not null)
            {
                if (isUp)
                {
                    if (_gestures.Release(control.Value, out var gesture) && gesture.Suppressed)
                    {
                        if (gesture.RingClickId is { } ringId && IsCurrentRing(ringId))
                            try { RingClicked?.Invoke(this, new(ringId, data.Point.X, data.Point.Y)); } catch { DismissRing(); }
                        ReleaseRingTrigger(gesture);
                        if (gesture.Action?.Kind == ActionKind.MouseButton && IsButton(gesture.Action.MouseOutput))
                            if (!Enqueue(new Work(gesture.Action, true, true, gesture.Token, gesture.Epoch, 0, 0))) DisarmFromHook("The action queue was full; Moushift paused and released held outputs.");
                        return 1;
                    }
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }
                if (_gestures.TryGet(control.Value, out var existing))
                    return existing.Suppressed ? 1 : NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                Observe(control.Value);
                if (IsRingOpen)
                {
                    if (RingActionSelection.ForwardOriginalButton(Volatile.Read(ref _ringRequest)?.Driverless == true, control.Value))
                    {
                        _gestures.Begin(control.Value, new(false, null, _dispatchState.Current.EnableEpoch, 0));
                        TraceCircle("forward-original", "driverless overlay", control.Value.ToString(), true);
                        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                    }
                    var bounds = Volatile.Read(ref _ringBounds);
                    bool inside = bounds.Right <= bounds.Left || data.Point.X >= bounds.Left && data.Point.X < bounds.Right && data.Point.Y >= bounds.Top && data.Point.Y < bounds.Bottom;
                    var ringId = Volatile.Read(ref _ringRequest)?.Id;
                    if (RingActionSelection.OwnPointer(control.Value, inside, Volatile.Read(ref _ringRequest)?.Driverless == true))
                    {
                        if (control.Value != MouseControl.LeftButton) DismissRing("overlay button " + control.Value);
                        else if (ringId is { } pressedId) try { RingPressed?.Invoke(this, new(pressedId, data.Point.X, data.Point.Y)); } catch { DismissRing(); }
                        _gestures.Begin(control.Value, new(true, null, _dispatchState.Current.EnableEpoch, 0,
                            control.Value == MouseControl.LeftButton ? ringId : null));
                        return 1;
                    }
                    DismissRing("outside " + control.Value);
                    _gestures.Begin(control.Value, new(false, null, _dispatchState.Current.EnableEpoch, 0));
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }
                var snapshot = Volatile.Read(ref _snapshot);
                var stamp = _dispatchState.Current;
                var epoch = stamp.EnableEpoch;
                if (DriverlessCircleMode)
                {
                    _gestures.Begin(control.Value, new(false, null, epoch, 0));
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }
                snapshot.Actions.TryGetValue(control.Value, out var action);
                bool bypassOwnWindow = OwnWindowPolicy.ShouldBypass(snapshot.OwnWindow, control.Value, action,
                    Volatile.Read(ref _captureSession) is not null);
                if (!_enabled || snapshot.Generation != stamp.RoutingGeneration || bypassOwnWindow || action is null || action.Kind == ActionKind.Passthrough || (action.Kind == ActionKind.MouseButton && action.MouseOutput == control.Value))
                {
                    if (control.Value is not (MouseControl.LeftButton or MouseControl.RightButton))
                    Volatile.Write(ref _inputStatus, control.Value + ": " + (!_enabled ? "assignments paused."
                        : bypassOwnWindow ? "This assignment is paused inside the editor. Side-button Alt+Tab remains available outside shortcut capture."
                        : snapshot.Generation != stamp.RoutingGeneration ? "foreground profile is updating; try again."
                        : "bu girdide etkin bir atama yok (" + snapshot.Name + ")."));
                    _gestures.Begin(control.Value, new(false, null, epoch, 0));
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }
                var token = Interlocked.Increment(ref _token);
                if (action.Kind == ActionKind.Disabled || Enqueue(new(action, false, action.Kind == ActionKind.MouseButton && IsButton(action.MouseOutput), token, epoch, snapshot.Generation, snapshot.ForegroundWindow)))
                {
                    Volatile.Write(ref _inputStatus, control.Value + ": " + (action.Kind == ActionKind.Disabled ? "disabled." : "action queued (" + snapshot.Name + ")."));
                    _gestures.Begin(control.Value, new(true, action, epoch, token));
                    return 1;
                }
                _gestures.Begin(control.Value, new(false, null, epoch, 0));
                DisarmFromHook("The action queue was full; Moushift paused.");
            }
            else if (message is 0x20A or 0x20E)
            {
                if (IsRingOpen)
                {
                    // Raw Input can open the overlay before the matching low-level hook packet arrives.
                    if (RingActionSelection.ForwardOriginalWheel(Volatile.Read(ref _ringRequest)?.Driverless == true, message == 0x20E))
                        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                    var bounds = Volatile.Read(ref _ringBounds);
                    bool inside = bounds.Right > bounds.Left && data.Point.X >= bounds.Left && data.Point.X < bounds.Right && data.Point.Y >= bounds.Top && data.Point.Y < bounds.Bottom;
                    if (Volatile.Read(ref _ringRequest)?.Driverless == true && !inside)
                    { DismissRing("outside wheel"); return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam); }
                    if (message == 0x20A) ScrollRing(unchecked((short)(data.MouseData >> 16))); return 1;
                }
                if (!DriverlessCircleMode && HandleWheel(message == 0x20E, unchecked((short)(data.MouseData >> 16)))) return 1;
            }
        }
        catch (Exception exception) { DisarmFromHook("Mouse processing paused: " + exception.Message); }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool HandleWheel(bool horizontal, short delta)
    {
        if (delta == 0) return false;
        var input = horizontal ? (delta > 0 ? MouseControl.WheelRight : MouseControl.WheelLeft) : (delta > 0 ? MouseControl.WheelUp : MouseControl.WheelDown);
        Observe(input);
        var snapshot = Volatile.Read(ref _snapshot);
        var stamp = _dispatchState.Current;
        var epoch = stamp.EnableEpoch;
        if (_lastWheelSnapshot != snapshot || _lastWheelEpoch != epoch)
        {
            _wheelVertical = _wheelHorizontal = 0;
            _lastWheelSnapshot = snapshot;
            _lastWheelEpoch = epoch;
        }
        ref var accumulator = ref (horizontal ? ref _wheelHorizontal : ref _wheelVertical);
        if (!_enabled || snapshot.Generation != stamp.RoutingGeneration || snapshot.OwnWindow || !snapshot.Actions.TryGetValue(input, out var action) || action.Kind == ActionKind.Passthrough)
        { accumulator = 0; return false; }
        if (action.Kind == ActionKind.Disabled) { accumulator = 0; return true; }
        accumulator += delta;
        while (Math.Abs(accumulator) >= 120)
        {
            if (!Enqueue(new Work(action, false, false, Interlocked.Increment(ref _token), epoch, snapshot.Generation, snapshot.ForegroundWindow)))
            {
                accumulator = 0;
                DisarmFromHook("The wheel action queue was full; Moushift paused.");
                return false;
            }
            accumulator -= Math.Sign(accumulator) * 120;
        }
        return true;
    }

    private bool ProcessDeviceInput(DeviceSourceInfo source, MouseControl control, bool release, short delta)
    {
        var identity = (source.Key, control);
        if (release)
        {
            if (!_deviceGestures.Release(identity, out var held) || !held.Suppressed) return false;
            if (held.RingClickId is { } ringId && IsCurrentRing(ringId))
                try { if (NativeMethods.GetCursorPos(out var releasePoint)) RingClicked?.Invoke(this, new(ringId, releasePoint.X, releasePoint.Y)); } catch { DismissRing(); }
            ReleaseRingTrigger(held);
            if (held.Action?.Kind == ActionKind.MouseButton)
                if (!Enqueue(new(held.Action, true, true, held.Token, held.Epoch, 0, 0))) DisarmFromHook("Device release queue is full.");
            return true;
        }
        if (delta == 0 && _deviceGestures.TryGet(identity, out var existing)) return existing.Suppressed;
        Observe(control);
        var snapshot = Volatile.Read(ref _snapshot); var stamp = _dispatchState.Current;
        if (IsRingOpen)
        {
            if (delta != 0 && control is MouseControl.WheelUp or MouseControl.WheelDown) ScrollRing(delta);
            if (delta == 0 && NativeMethods.GetCursorPos(out var point))
            {
                var bounds = Volatile.Read(ref _ringBounds);
                bool inside = bounds.Right <= bounds.Left || point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
                var ringId = Volatile.Read(ref _ringRequest)?.Id;
                if (RingActionSelection.OwnPointer(control, inside))
                {
                    if (control != MouseControl.LeftButton) DismissRing();
                    else if (ringId is { } pressedId) try { RingPressed?.Invoke(this, new(pressedId, point.X, point.Y)); } catch { DismissRing(); }
                    _deviceGestures.Begin(identity, new(true, null, stamp.EnableEpoch, 0, control == MouseControl.LeftButton ? ringId : null));
                    return true;
                }
                DismissRing();
            }
            if (delta == 0) _deviceGestures.Begin(identity, new(false, null, stamp.EnableEpoch, 0));
            return delta != 0;
        }
        MappingProfile? profile;
        string? physicalKey = Volatile.Read(ref _deviceRoutes).PhysicalKey(source.Key);
        lock (_settingsGate) profile = ProfileResolver.ResolveDeviceInput(_settings, _foregroundPath, physicalKey);
        if (profile is null) return false;
        Volatile.Write(ref _inputStatus, (profile.DeviceName ?? source.Name) + " · " + control + " · " + profile.Name);
        var action = profile.Bindings.FirstOrDefault(binding => binding.Input == control)?.Action ?? new InputAction();
        bool passthrough = !_enabled || OwnWindowPolicy.ShouldBypass(snapshot.OwnWindow, control, action,
                Volatile.Read(ref _captureSession) is not null) || snapshot.Generation != stamp.RoutingGeneration
            || action.Kind == ActionKind.Passthrough || (action.Kind == ActionKind.MouseButton && action.MouseOutput == control);
        if (passthrough)
        {
            if (delta == 0) _deviceGestures.Begin(identity, new(false, null, stamp.EnableEpoch, 0));
            return false;
        }
        if (delta != 0)
        {
            if (action.Kind == ActionKind.Disabled) return true;
            var wheelKey = (source.Key, control is MouseControl.WheelLeft or MouseControl.WheelRight);
            _deviceWheels.TryGetValue(wheelKey, out var remainder);
            int total = (remainder.Stamp == stamp ? remainder.Delta : 0) + delta;
            while (Math.Abs(total) >= 120)
            {
                if (!Enqueue(new(action.DeepClone(), false, false, Interlocked.Increment(ref _token), stamp.EnableEpoch, stamp.RoutingGeneration, snapshot.ForegroundWindow, action.Kind == ActionKind.Ring ? profile.DeepClone() : null)))
                { DisarmFromHook("Device action queue is full."); return false; }
                total -= Math.Sign(total) * 120;
            }
            _deviceWheels[wheelKey] = (stamp, total); return true;
        }
        long token = Interlocked.Increment(ref _token);
        if (action.Kind == ActionKind.Disabled || Enqueue(new(action.DeepClone(), false, action.Kind == ActionKind.MouseButton, token, stamp.EnableEpoch, stamp.RoutingGeneration, snapshot.ForegroundWindow, action.Kind == ActionKind.Ring ? profile.DeepClone() : null)))
        { _deviceGestures.Begin(identity, new(true, action, stamp.EnableEpoch, token)); return true; }
        _deviceGestures.Begin(identity, new(false, null, stamp.EnableEpoch, 0)); DisarmFromHook("Device action queue is full."); return false;
    }

    private bool Enqueue(Work work)
    {
        if (Interlocked.Increment(ref _queueCount) > 128)
        { Interlocked.Decrement(ref _queueCount); return false; }
        _queue.Enqueue(work);
        _wake.Set();
        return true;
    }

    private void Observe(MouseControl control)
    {
        if (Interlocked.Increment(ref _observationCount) > 32)
        { Interlocked.Decrement(ref _observationCount); return; }
        _observations.Enqueue(control);
        _wake.Set();
    }

    private void DisarmFromHook(string error)
    {
        DismissRing();
        _lastError = error;
        _enabled = false;
        _dispatchState.AdvanceEnableEpoch();
        _wake.Set();
        RaiseStateChanged();
    }

    private void WorkerLoop()
    {
        var epoch = _dispatchState.Current.EnableEpoch;
        try
        {
            while (!_stopping)
            {
                while (_captureUpdates.TryDequeue(out var captured))
                {
                    Interlocked.Decrement(ref _observationCount);
                    try { ShortcutCaptured?.Invoke(this, captured); } catch { }
                }
                var currentEpoch = _dispatchState.Current.EnableEpoch;
                if (epoch != currentEpoch) { ReleaseHeldOutputs(); epoch = currentEpoch; }
                while (_observations.TryDequeue(out var observed))
                {
                    Interlocked.Decrement(ref _observationCount);
                    try { InputObserved?.Invoke(this, observed); } catch { /* Consumer failures cannot disable input cleanup. */ }
                }
                while (_auxiliaryObservations.TryDequeue(out var observedKey))
                {
                    Interlocked.Decrement(ref _observationCount);
                    try { AuxiliaryInputObserved?.Invoke(this, observedKey); } catch { }
                }
                if (!_queue.TryDequeue(out var work)) { _wake.WaitOne(100); continue; }
                Interlocked.Decrement(ref _queueCount);
                if (!InputDispatchState.CanExecute(new(work.Epoch, work.RoutingGeneration), _dispatchState.Current, _enabled, work.IsRelease))
                { Volatile.Write(ref _inputStatus, "Action cancelled: enable state or profile changed."); continue; }
                // Polling keeps hooks fast; this worker-side check also closes the
                // interval before the next foreground poll, including another HWND
                // belonging to the very same executable/profile.
                if (!work.IsRelease && (work.ForegroundWindow == 0 || NativeMethods.GetForegroundWindow() != work.ForegroundWindow))
                { Volatile.Write(ref _inputStatus, "Action cancelled: target window changed; try again."); continue; }
                try { Execute(work); }
                catch (Exception exception)
                {
                    DismissRing();
                    _lastError = exception.Message;
                    _enabled = false;
                    _dispatchState.AdvanceEnableEpoch();
                    ReleaseHeldOutputs();
                    try { ActionError?.Invoke(this, _lastError ?? exception.Message); } catch { }
                    RaiseStateChanged();
                }
            }
        }
        finally { ReleaseHeldOutputs(); }
    }

    private void Execute(Work work)
    {
        var action = work.Action;
        switch (action.Kind)
        {
            case ActionKind.Ring:
                if (work.IsRelease)
                {
                    if (Volatile.Read(ref _ringRequest) is { } open && open.TriggerToken == work.Token)
                        try { RingTriggerReleased?.Invoke(this, open.Id); } catch { DismissRing(); }
                    break;
                }
                var profile = work.Profile ?? Volatile.Read(ref _snapshot).Profile;
                if (profile is null) break;
                var request = new RingRequest(Guid.NewGuid(), work.ForegroundWindow, work.Epoch, work.RoutingGeneration, profile.RingSlots.Select(slot => slot.DeepClone()).ToArray(), work.Token, profile.DeviceKey, work.DriverlessCircle);
                _ringWheelRemainder = 0;
                Volatile.Write(ref _ringBounds, new(0, 0, 0, 0));
                Volatile.Write(ref _ringRequest, request);
                TraceCircle("open", profile.DeviceName ?? "Mouse");
                if (work.DriverlessCircle) Interlocked.Increment(ref _rawCircleTriggerCount);
                try { RingRequested?.Invoke(this, request); } catch { DismissRing(); throw; }
                break;
            case ActionKind.Shortcut: SendShortcut(action); break;
            case ActionKind.Text: SendText(action.Text ?? ""); break;
            case ActionKind.OpenResource:
                if (!SettingsValidator.IsOpenResource(action.ApplicationPath)) throw new InvalidOperationException("Unsupported resource address.");
                Process.Start(new ProcessStartInfo(action.ApplicationPath) { UseShellExecute = true });
                break;
            case ActionKind.LaunchApplication:
                if (!File.Exists(action.ApplicationPath)) throw new FileNotFoundException("The selected application no longer exists.", action.ApplicationPath);
                if (!string.Equals(Path.GetExtension(action.ApplicationPath), ".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Application actions require an existing .exe file.");
                Process.Start(new ProcessStartInfo(action.ApplicationPath) { Arguments = action.Arguments ?? "", UseShellExecute = true });
                break;
            case ActionKind.MouseButton:
                if (work.IsHold)
                {
                    if (work.IsRelease) ReleaseToken(work.Token);
                    else
                    {
                        _outputOwners.TryGetValue(action.MouseOutput, out var owners);
                        if (owners == 0 && IsPhysicalOutputHeld(action.MouseOutput)) break;
                        if (owners == 0) SendMouse(action.MouseOutput, false);
                        _outputOwners[action.MouseOutput] = owners + 1;
                        _heldOutputs[work.Token] = action.MouseOutput;
                    }
                }
                else if (IsButton(action.MouseOutput))
                {
                    if (IsPhysicalOutputHeld(action.MouseOutput)) break;
                    SendOneShot(new[] { MouseInput(action.MouseOutput, false), MouseInput(action.MouseOutput, true) });
                }
                else SendMouse(action.MouseOutput, false);
                break;
        }
    }

    private void ReleaseToken(long token)
    {
        if (!_heldOutputs.TryGetValue(token, out var output)) return;
        var owners = _outputOwners[output] - 1;
        if (owners == 0) { SendMouse(output, true); _outputOwners.Remove(output); }
        else _outputOwners[output] = owners;
        _heldOutputs.Remove(token);
    }

    private void ReleaseHeldOutputs()
    {
        var cleanupFailed = false;
        var cleanupError = TryReleaseOneShotInputs();
        if (cleanupError is not null)
        {
            cleanupFailed = true;
            _lastError = (_lastError is null ? "" : _lastError + " ") + cleanupError;
            try { ActionError?.Invoke(this, _lastError); } catch { }
        }
        foreach (var output in _outputOwners.Keys.ToArray())
        {
            try { SendMouse(output, true); _outputOwners.Remove(output); }
            catch (Exception exception)
            {
                cleanupFailed = true;
                _lastError = "An output release failed: " + exception.Message;
                try { ActionError?.Invoke(this, _lastError); } catch { }
            }
        }
        foreach (var token in _heldOutputs.Where(item => !_outputOwners.ContainsKey(item.Value)).Select(item => item.Key).ToArray()) _heldOutputs.Remove(token);
        if (cleanupFailed) { _enabled = false; RaiseStateChanged(); }
    }

    private void SendShortcut(InputAction action)
    {
        if (action.KeyCode is < 8 or > 254 || action.KeyCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5)
            throw new InvalidOperationException("Choose a non-modifier keyboard key for the shortcut.");
        var modifiers = new List<int>();
        if (action.Modifiers.HasFlag(KeyModifiers.Control)) modifiers.Add(0x11);
        if (action.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add(0x12);
        if (action.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add(0x10);
        if (action.Modifiers.HasFlag(KeyModifiers.Windows)) modifiers.Add(0x5B);
        var owned = modifiers.Where(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0).ToList();
        var inputs = owned.Select(key => KeyInput(key, false)).ToList();
        var keyAlreadyDown = (NativeMethods.GetAsyncKeyState(action.KeyCode) & 0x8000) != 0;
        if (keyAlreadyDown) throw new InvalidOperationException("The shortcut key is physically held; Moushift paused rather than releasing it.");
        inputs.Add(KeyInput(action.KeyCode, false));
        inputs.Add(KeyInput(action.KeyCode, true));
        inputs.AddRange(owned.AsEnumerable().Reverse().Select(key => KeyInput(key, true)));
        SendOneShot(inputs.ToArray());
        Volatile.Write(ref _inputStatus, "Shortcut sent to Windows: " + action.Modifiers + " + " + (action.KeyCode == 9 ? "Tab" : $"VK {action.KeyCode:X2}"));
    }

    private void SendText(string text)
    {
        if (text.Length > SettingsValidator.MaximumTextLength) throw new InvalidOperationException($"Text actions are limited to {SettingsValidator.MaximumTextLength} UTF-16 characters.");
        var inputs = text.SelectMany(character => new[] { UnicodeInput(character, false), UnicodeInput(character, true) }).ToArray();
        if (inputs.Length > 0) SendOneShot(inputs);
    }

    private void SendOneShot(NativeMethods.Input[] inputs)
    {
        if (_pendingOneShotInputs.Count > 0) throw new InvalidOperationException("An earlier output still needs release; pause Moushift before retrying.");
        var inserted = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        var nativeError = Marshal.GetLastWin32Error();
        _pendingOneShotInputs.AcceptPrefix(inputs, checked((int)inserted));
        if (inserted != inputs.Length || _pendingOneShotInputs.Count != 0)
        {
            var cleanupError = TryReleaseOneShotInputs();
            throw new Win32Exception(nativeError, $"Windows accepted {inserted} of {inputs.Length} input events. " + (cleanupError ?? "Accepted held inputs were released."));
        }
    }

    private string? TryReleaseOneShotInputs()
    {
        var releases = _pendingOneShotInputs.PendingReleases;
        if (releases.Length == 0) return null;
        var accepted = NativeMethods.SendInput((uint)releases.Length, releases, Marshal.SizeOf<NativeMethods.Input>());
        _pendingOneShotInputs.AcceptPrefix(releases, checked((int)accepted));
        return _pendingOneShotInputs.Count == 0 ? null : $"Output cleanup accepted {accepted} of {releases.Length} releases; {_pendingOneShotInputs.Count} owned inputs remain pending for cleanup retry.";
    }

    private static NativeMethods.Input KeyInput(int key, bool release)
    {
        var extended = key is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x6F;
        return new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = (ushort)key, Flags = (release ? NativeMethods.KeyUp : 0) | (extended ? 1u : 0u), ExtraInfo = NativeMethods.InjectionMarker } } };
    }
    private static NativeMethods.Input UnicodeInput(char character, bool release) => new()
    { Type = 1, Data = new() { Keyboard = new() { Scan = character, Flags = NativeMethods.Unicode | (release ? NativeMethods.KeyUp : 0), ExtraInfo = NativeMethods.InjectionMarker } } };
    private static void SendMouse(MouseControl control, bool release) => Send(new[] { MouseInput(control, release) });
    private static bool IsPhysicalOutputHeld(MouseControl control)
    {
        var key = control switch { MouseControl.LeftButton => 1, MouseControl.RightButton => 2, MouseControl.MiddleButton => 4, MouseControl.XButton1 => 5, MouseControl.XButton2 => 6, _ => 0 };
        return key != 0 && (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }
    private static NativeMethods.Input MouseInput(MouseControl control, bool release)
    {
        var flags = control switch
        {
            MouseControl.LeftButton => release ? 0x4u : 0x2u,
            MouseControl.RightButton => release ? 0x10u : 0x8u,
            MouseControl.MiddleButton => release ? 0x40u : 0x20u,
            MouseControl.XButton1 or MouseControl.XButton2 => release ? 0x100u : 0x80u,
            MouseControl.WheelUp or MouseControl.WheelDown => 0x800u,
            MouseControl.WheelLeft or MouseControl.WheelRight => 0x1000u,
            _ => throw new InvalidOperationException("Unsupported mouse output.")
        };
        var mouseData = control switch
        {
            MouseControl.XButton1 => 1u, MouseControl.XButton2 => 2u,
            MouseControl.WheelUp or MouseControl.WheelRight => 120u,
            MouseControl.WheelDown or MouseControl.WheelLeft => unchecked((uint)-120), _ => 0u
        };
        return new() { Data = new() { Mouse = new() { Flags = flags, MouseData = mouseData, ExtraInfo = NativeMethods.InjectionMarker } } };
    }
    private static void Send(NativeMethods.Input[] inputs)
    {
        var inserted = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (inserted != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows accepted {inserted} of {inputs.Length} input events. Check target permissions and retry after pausing.");
    }

    private void PollForeground(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            var ownWindow = pid == Environment.ProcessId;
            string? path = null;
            try { if (pid != 0) { using var process = Process.GetProcessById((int)pid); path = process.MainModule?.FileName; } }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { }
            _foregroundPath = path;
            string name;
            lock (_settingsGate)
            {
                var profile = ProfileResolver.Resolve(_settings, path).DeepClone();
                name = "Device profiles";
                var stamp = _dispatchState.UpdateRouting(new(hwnd, pid, path, profile.Id, _settingsRevision));
                var previous = Volatile.Read(ref _snapshot);
                // Preserve the same object/remainder until HWND, profile, process,
                // or settings identity actually changes.
                if (previous.Generation != stamp.RoutingGeneration)
                {
                    // Anonymous mouse/keyboard-hook input must never execute shared mappings.
                    var actions = new Dictionary<MouseControl, InputAction>();
                    var auxiliary = new Dictionary<int, InputAction>();
                    Volatile.Write(ref _snapshot, new(name, actions, auxiliary, ownWindow, stamp.RoutingGeneration, hwnd) { Profile = profile, ApplicationPath = path });
                    if (Volatile.Read(ref _ringRequest) is { } ring && ring.RoutingGeneration != stamp.RoutingGeneration) DismissRing("foreground/settings changed");
                }
            }
            var display = path is null ? "Unknown" : Path.GetFileName(path);
            var changed = _foreground != display || _activeProfile != name;
            _foreground = display;
            _activeProfile = name;
            if (changed) RaiseStateChanged();
        }
        catch (Exception exception) { _lastError = "Profile lookup failed: " + exception.Message; }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private static bool IsButton(MouseControl control) => control is MouseControl.LeftButton or MouseControl.RightButton or MouseControl.MiddleButton or MouseControl.XButton1 or MouseControl.XButton2;
    private static MouseControl? DecodeButton(uint message, uint data, out bool up)
    {
        up = message is 0x202 or 0x205 or 0x208 or 0x20C;
        return message switch
        {
            0x201 or 0x202 => MouseControl.LeftButton,
            0x204 or 0x205 => MouseControl.RightButton,
            0x207 or 0x208 => MouseControl.MiddleButton,
            0x20B or 0x20C when (data >> 16) == 1 => MouseControl.XButton1,
            0x20B or 0x20C when (data >> 16) == 2 => MouseControl.XButton2,
            _ => null
        };
    }

    internal static IEnumerable<string> CheckRoutingPrimitives()
    {
        if (DecodeButton(0x20B, 1u << 16, out var firstUp) != MouseControl.XButton1 || firstUp)
            yield return "XBUTTON1 down decoding failed.";
        if (DecodeButton(0x20C, 2u << 16, out var secondUp) != MouseControl.XButton2 || !secondUp)
            yield return "XBUTTON2 up decoding failed.";
        if (DecodeButton(0x20B, 3u << 16, out _) is not null)
            yield return "An unknown XBUTTON value was accepted.";
        if (DecodeButton(0x200, 0, out _) is not null)
            yield return "Mouse movement was classified as a button.";
        var unicode = UnicodeInput('\u015f', true).Data.Keyboard;
        if (unicode.VirtualKey != 0 || unicode.Scan != '\u015f' || unicode.Flags != (NativeMethods.Unicode | NativeMethods.KeyUp))
            yield return "Unicode keyboard event encoding failed.";
        var wheel = MouseInput(MouseControl.WheelLeft, false).Data.Mouse;
        if (wheel.Flags != 0x1000 || unchecked((int)wheel.MouseData) != -120)
            yield return "Horizontal wheel event encoding failed.";
        var right = MouseInput(MouseControl.RightButton, true).Data.Mouse;
        if (right.Flags != 0x10 || right.ExtraInfo != NativeMethods.InjectionMarker)
            yield return "Right-button release or injection-marker encoding failed.";
    }

    private void RaiseStateChanged() => ThreadPool.QueueUserWorkItem(_ => { try { StateChanged?.Invoke(this, EventArgs.Empty); } catch { } });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Write(ref _captureSession, null);
        DismissRing();
        _enabled = false;
        _dispatchState.AdvanceEnableEpoch();
        _foregroundTimer?.Dispose();
        _stopping = true;
        try { _deviceBackend?.Dispose(); } catch (Exception error) { _lastError = error.Message; }
        _wake.Set();
        if (_hookThreadId != 0) NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WmQuit, 0, 0);
        var hookStopped = _hookThread is null || _hookThread.Join(TimeSpan.FromSeconds(3));
        var workerStopped = _workerThread is null || _workerThread.Join(TimeSpan.FromSeconds(3));
        if (!hookStopped || !workerStopped) _lastError = "An input thread is still finishing cleanup.";
        if (hookStopped && workerStopped) { _wake.Dispose(); _ready.Dispose(); }
        RaiseStateChanged();
    }
}
