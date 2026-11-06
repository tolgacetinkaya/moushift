# Mouse suppression: Windows implementation comparison

Reviewed 2026-11-06. No driver, package, device configuration or vendor profile was installed/changed for this investigation.

| Implementation | Documented method | Consequence for Moushift |
| --- | --- | --- |
| [X-Mouse Button Control](https://www.highrez.co.uk/downloads/xmousebuttoncontrol.htm) | Intercepts standard Windows mouse messages; supports five standard buttons and wheel tilt; profiles can depend on application/window. | Standard buttons and tilt do not require a separate vendor adapter. This documentation does not establish exact physical-device suppression. |
| [AutoHotkey](https://github.com/AutoHotkey/AutoHotkeyDocs/blob/v2/docs/Hotkeys.htm) | Mouse hotkeys use the mouse hook. The `~` modifier lets native behavior pass through; ordinary hotkeys suppress it. | Suppression of an ordinary middle click is possible without installing a kernel driver. That alone does not supply a physical mouse identity. |
| [UCR Core Interception](https://github.com/snoothy/ucr/wiki/Core_Interception) | Uses Interception for per-device keyboard/mouse bindings and blocking; driver installation requires administrator access and a restart. | The device-filter approach supplies both source identity and suppression, but is a separate system dependency. |

Microsoft's [LowLevelMouseProc documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) permits returning a nonzero value to stop delivery. Its [MSLLHOOKSTRUCT payload](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct) has no device handle. Raw Input does provide a source, but is observation rather than cancellation of the ordinary desktop click. Combining independently delivered events by timing is not a verified guarantee when multiple mice act concurrently.

Version 0.14.6 retained the original click: global suppression alone would affect the same button on other mice. No global blocking, speculative source matching, or synthetic click replay was enabled in that version.

## Middle-button routing (0.14.9)

A controlled physical probe on this Windows installation confirmed that Raw Input source data remained available when the low-level hook suppressed the legacy middle click. The new gate blocks physical legacy middle-button edges while enabled, and routes the independent Raw Input stream by its real source. Assigned Circle cycles are consumed; other middle clicks are replayed with the existing injection marker. This deliberately replaces the middle-button input path for all mice while active, rather than attributing an anonymous hook packet to a guessed source.

The action worker owns native replay DOWN/UP state, including reference-counted physical owners, pause, disconnect and shutdown cleanup. The raw/hook threads have a bounded 25 ms delivery handoff; missing delivery pauses interception and passes the native click. It does not cancel physical Raw Input in other applications. Synthetic-input rejection and Windows integrity-level restrictions remain applicable.

Physical desktop test: Corsair Circle / MX Master native / Corsair Circle produced two exact-source Circle requests and only one legacy middle DOWN/UP pair at the separate target window, belonging to the MX Master test interval. No saved profiles or manufacturer settings were changed. Fixtures additionally cover concurrent mice, repeated edges, pause, assignment changes during a hold, neutral activation and removal/reset. This evidence is for ordinary desktop messages, not games or elevated applications.

## Horizontal wheel correction

Learning already decoded horizontal wheel packets. The live observer/router previously omitted them. Version 0.14.6 routes standard `RI_MOUSE_HWHEEL` packets to the exact mouse's Circle binding, with positive delta for right and negative delta for left, following [RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse).

Small deltas accumulate to 120. A same-direction burst triggers once; more than 300 ms without a packet starts a new burst. Direction changes start a new burst. This is a repeat heuristic, not a physical release signal: hardware with long repeat intervals may form multiple bursts. Disabled/open-overlay packets consume the burst rather than accumulating a future trigger. Foreground changes cannot transfer a partial gesture to another profile. Horizontal hook packets cannot immediately dismiss the Circle opened by that Raw Input packet.

Original horizontal scrolling also remains. Firmware-only buttons or tilt mapped by vendor software to unrelated keys are not automatically converted to horizontal wheel events. Physical acceptance on the user's mouse remains pending.
