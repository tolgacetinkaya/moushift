# Driverless Click Circle on Windows

The standard-button Circle trigger uses built-in Windows Raw Input and the existing non-activating Circle overlay. It needs no downloaded package, Interception library, kernel driver, device firmware write, or manufacturer-specific adapter.

## Use

1. Select a connected mouse, assign Click Circle to a standard button, and Apply. Existing Circle assignments are reused.
2. Click Enable Circle. The app always starts paused.
3. Switch to another application and press that physical mouse button once.
4. Click a Circle action or folder; click outside to dismiss. Hold-to-release selection is reserved for exclusive input support.

Only the five standard mouse buttons can trigger this mode. The original trigger also reaches Windows: a middle-button trigger can still start the foreground application's autoscroll, for example. Ordinary button remaps, wheel triggers, anonymous F13–F24 keys, and learned custom HID assignment drafts are not activated by Enable Circle.

## Input and profile isolation

- The main window owns the single mouse Raw Input registration and receives background packets even when minimized. Temporary learning reuses that owner.
- Native mouse DOWN/UP transitions are decoded before the activity indicator's 500 ms throttle. Pointer movement is not a trigger. Our tagged synthetic output and zero-device input are excluded.
- A native input device path must match exactly one non-auxiliary Windows connection. There is no VID/PID-only match, editor-selected-device fallback, or last-active-mouse guess.
- The matched device's mapping and foreground-application Circle are resolved together. General shared legacy profiles never supply a fallback. Application overrides, inherited Circle triggers, and manual Circle selection retain their existing rules.
- Only a resolved Circle binding enters the action queue. Other saved button actions remain untouched and inactive in this mode. Runtime profiles/slots are detached copies.
- Held repeats do not reopen a Circle. Press ownership survives pause and unchanged inventory polls, while disconnection removes the source/held state. Ambiguous sources are rejected.
- The service, emergency pause shortcut, Raw Input registration, and at least one mapped connection must be ready before activation. Learning pauses the service. Connection changes invalidate pending work and pause activation.
- The original trigger is not suppressed. The existing mouse hook owns left clicks within the open Circle and adjustment scrolling within its bounds; unrelated outside clicks and standard trigger buttons retain their normal behavior.
- The original non-left button is forwarded without dismissing the driverless overlay. Raw Input can open the Circle before that same button reaches the separate mouse-hook thread; treating that later hook event as an outside click would immediately close the new Circle. Closing and reopening are covered by a replay of this observed event order.
- Foreground window, settings generation and enable epoch are checked before opening a Circle or running one of its selected actions. Switching windows cancels stale work.

## Diagnostics and verification

Fixture tests cover exact-source isolation, shared-profile rejection, application/manual Circle resolution, press/release lifecycle, native packet ABI, startup readiness and overlay ownership. Existing Core and offscreen WPF suites remain applicable. Fixtures do not certify physical hardware.

When the developer explicitly supplies an absolute `CLICKLOOM_DIAGNOSTICS_FILE`, a bounded recent-event buffer records mouse button edges, a display source name, and Circle open/close reasons. It does not record ordinary keyboard input, pointer movement, raw device paths, or report bytes. Normal launches do not enable this trace.

For physical validation, use one intentional left click and one middle click separated by two seconds, then leave all buttons untouched. A rapid close must be distinguished between a physical extra click, original middle-button behavior, and foreground/settings cancellation before calling it a hardware defect. A short down/up/down interval alone is not proof of a faulty switch.

Left/right triggers retain their original application focus/context-menu effects and may therefore cancel the Circle through a real foreground change. Middle or side buttons are recommended for the first physical tests. Per-mouse identification does not remove these original effects.

The optional exclusive remapping setup guide is separate: [INPUT_SUPPORT_SETUP.md](INPUT_SUPPORT_SETUP.md).
