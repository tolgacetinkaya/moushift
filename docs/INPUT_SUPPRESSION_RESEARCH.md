# Mouse suppression: Windows implementation comparison

Reviewed 2026-11-06. No driver, package, device configuration or vendor profile was installed/changed for this investigation.

| Implementation | Documented method | Consequence for Moushift |
| --- | --- | --- |
| [X-Mouse Button Control](https://www.highrez.co.uk/downloads/xmousebuttoncontrol.htm) | Intercepts standard Windows mouse messages; supports five standard buttons and wheel tilt; profiles can depend on application/window. | Standard buttons and tilt do not require a separate vendor adapter. This documentation does not establish exact physical-device suppression. |
| [AutoHotkey](https://github.com/AutoHotkey/AutoHotkeyDocs/blob/v2/docs/Hotkeys.htm) | Mouse hotkeys use the mouse hook. The `~` modifier lets native behavior pass through; ordinary hotkeys suppress it. | Suppression of an ordinary middle click is possible without installing a kernel driver. That alone does not supply a physical mouse identity. |
| [UCR Core Interception](https://github.com/snoothy/ucr/wiki/Core_Interception) | Uses Interception for per-device keyboard/mouse bindings and blocking; driver installation requires administrator access and a restart. | The device-filter approach supplies both source identity and suppression, but is a separate system dependency. |

Microsoft's [LowLevelMouseProc documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc) permits returning a nonzero value to stop delivery. Its [MSLLHOOKSTRUCT payload](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-msllhookstruct) has no device handle. Raw Input does provide a source, but is observation rather than cancellation of the ordinary desktop click. Combining independently delivered events by timing is not a verified guarantee when multiple mice act concurrently.

Moushift therefore retains the original click in its current exact-source driverless mode. Global suppression would affect the same button on other mice. No global blocking, speculative source matching, or synthetic click replay was enabled as part of 0.14.6. Middle-button autoscroll is still a known limitation, not a completed fix.

## Horizontal wheel correction

Learning already decoded horizontal wheel packets. The live observer/router previously omitted them. Version 0.14.6 routes standard `RI_MOUSE_HWHEEL` packets to the exact mouse's Circle binding, with positive delta for right and negative delta for left, following [RAWMOUSE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawmouse).

Small deltas accumulate to 120. A same-direction burst triggers once; more than 300 ms without a packet starts a new burst. Direction changes start a new burst. This is a repeat heuristic, not a physical release signal: hardware with long repeat intervals may form multiple bursts. Disabled/open-overlay packets consume the burst rather than accumulating a future trigger. Foreground changes cannot transfer a partial gesture to another profile. Horizontal hook packets cannot immediately dismiss the Circle opened by that Raw Input packet.

Original horizontal scrolling also remains. Firmware-only buttons or tilt mapped by vendor software to unrelated keys are not automatically converted to horizontal wheel events. Physical acceptance on the user's mouse remains pending.
