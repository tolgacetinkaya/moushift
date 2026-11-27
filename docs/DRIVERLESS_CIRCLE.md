# Driverless Click Circle on Windows

The standard-button and horizontal-wheel Circle trigger uses built-in Windows Raw Input and the existing non-activating Circle overlay. It needs no downloaded package, Interception library, kernel driver, device firmware write, or manufacturer-specific adapter.

## Use

1. Select a connected mouse, assign Click Circle to a standard button or horizontal wheel direction, and Apply. Existing Circle assignments are reused.
2. Check the assignment status. Applying a supported action to a ready mouse activates assignments. After an explicit pause, turn **Mouse actions** on in the top bar. Moushift remembers your choice on restart.
3. Switch to another application and press that physical mouse button once.
4. Click a Circle action or folder; click outside to dismiss. Hold-to-release selection is reserved for exclusive input support.

The five standard mouse buttons and standard horizontal wheel directions can open Circle. The five standard buttons can also open **Task View** directly when selected in the button's **Action** menu. An assigned middle button replaces its normal desktop click with Circle or Task View. Back and Forward can run a saved keyboard shortcut such as **Alt+Tab** in place of their native navigation click. Other mice keep their own assignments. Horizontal wheel triggers retain their original scrolling behavior. Keyboard shortcuts on other buttons, vertical wheel triggers, and learned custom HID assignment drafts require separate input support.

In **Assignments**, **Back button** is the rear thumb button and **Forward button** is the front one. Select **Default** to use normal browser navigation. F13–F24 assignments from older shared profiles are preserved in storage and are inactive.

## Quick gestures

Assign **Click Circle** to the wheel button. Open **Quick gestures** for that mouse and application profile, enable gestures, and save a shortcut for one or more directions. For example, in a Chrome profile set Right to **Ctrl+Tab** and Left to **Ctrl+Shift+Tab**.

Tap and release the wheel to open Circle. To run a directional shortcut, hold the wheel for about a quarter of a second, move in the chosen direction, then release. A long press without a clear assigned direction does nothing. Back and Forward assignments work independently of this option.

## Input and profile isolation

- The main window owns the single mouse Raw Input registration and receives background packets even when minimized. Temporary learning reuses that owner.
- Native mouse DOWN/UP transitions are decoded before the activity indicator's 500 ms throttle. Pointer movement is not a trigger. Our tagged synthetic output and zero-device input are excluded.
- A native input device path must match exactly one non-auxiliary Windows connection. There is no VID/PID-only match, editor-selected-device fallback, or last-active-mouse guess.
- The matched device's mapping and foreground-application Circle are resolved together. General shared legacy profiles never supply a fallback. Application overrides, inherited Circle triggers, and manual Circle selection retain their existing rules.
- The action queue accepts a resolved Circle binding, standard-button Task View, or a keyboard shortcut from Back or Forward. Runtime profiles and slots are detached copies.
- Held repeats do not reopen a Circle. Press ownership survives pause and unchanged inventory polls, while disconnection removes the source/held state. Ambiguous sources are rejected.
- The service, emergency pause shortcut, Raw Input registration, and at least one mapped connection must be ready before activation. Learning pauses the service. Connection changes invalidate pending work and pause activation.
- While enabled, a middle-button gate blocks physical legacy middle DOWN/UP messages. Raw Input routes each actual source. An assigned middle-button Circle or Task View consumes the cycle. Other middle clicks are replayed with the application's injection marker. No last-active-device or timestamp-based source inference is used. This forwarding is synthetic input, not an exclusive hardware filter.
- Native replay preserves held ownership across multiple mice and pause; profile changes cannot change a cycle already in progress. Activation waits for a neutral middle button. Disconnect, receiver failure and shutdown release owned output. A bounded 25 ms raw/hook handoff pauses and fails open if source delivery stalls. Delayed native DOWNs are discarded after a foreground-window change; owned UPs are still released.
- The existing mouse hook owns left clicks within the open Circle and adjustment scrolling within its bounds. Unrelated outside clicks and other standard trigger buttons retain their normal behavior.
- The original non-left button is forwarded without dismissing the driverless overlay. Raw Input can open the Circle before that same button reaches the separate mouse-hook thread; treating that later hook event as an outside click would immediately close the new Circle. Closing and reopening are covered by a replay of this observed event order.
- Foreground window, settings generation and enable epoch are checked before opening a Circle or running one of its selected actions. Switching windows cancels stale work.

## Diagnostics and verification

Fixture tests cover exact-source isolation, shared-profile rejection, application/manual Circle resolution, press/release lifecycle, native packet ABI, startup readiness and overlay ownership. Existing Core and offscreen WPF suites remain applicable. Fixtures do not certify physical hardware.

When the developer explicitly supplies an absolute `CLICKLOOM_DIAGNOSTICS_FILE`, a bounded recent-event buffer records mouse button edges, a display source name, and Circle open/close reasons. It does not record ordinary keyboard input, pointer movement, raw device paths, or report bytes. Normal launches do not enable this trace.

To verify a Back shortcut, leave a browser tab with browsing history in the foreground, assign **Alt+Tab** to Back for that mouse, and press Back once. Windows should switch windows. Return with the keyboard and check that the browser is still on the same page. A second mouse with Back set to **Default** should navigate normally.

For physical validation of Circle dismissal, use one intentional left click and one middle click separated by two seconds, then leave all buttons untouched. A rapid close must be distinguished between a physical extra click, original middle-button behavior, and foreground/settings cancellation before calling it a hardware defect. A short down/up/down interval alone is not proof of a faulty switch.

Left/right triggers retain their original application focus/context-menu effects and may therefore cancel the Circle through a real foreground change. Middle clicks and side-button shortcut assignments use desktop input suppression and forwarding. Applications using their own Raw Input can still see physical packets; applications that reject synthetic input or run at a higher integrity level may not accept replay. This mode does not claim game-level or elevated-application suppression.

The optional exclusive remapping setup guide is separate: [INPUT_SUPPORT_SETUP.md](INPUT_SUPPORT_SETUP.md).

Horizontal wheel deltas are accumulated to 120 and same-direction repeats are grouped using a 300 ms quiet interval. Original horizontal scrolling remains. See [the suppression comparison](INPUT_SUPPRESSION_RESEARCH.md) for implementation details and limitations.
