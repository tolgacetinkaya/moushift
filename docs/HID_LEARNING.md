# Generic HID discovery and learning

The implementation uses Windows SetupAPI/HID parser/Raw Input. It contains no device-model adapter for this feature, and sends no HID feature/output reports. Device names may be displayed for the user; names and VID/PID are not decoding rules.

## Identity and placement

Each learned control contains a connection key, a hash of its input interface path, input kind, usage page/usage, report ID, link collection and direction where applicable. The hash is local correlation metadata, not proof of a unique physical device. Reconnecting through a different transport/port may require relearning. A shared receiver remains a shared connection.

Location is an independent normalized X/Y point and drawing elevation (`Top` or `Left`). The top artwork is preserved; the side elevation is an original generic vector illustration. Location is selected manually, never inferred from pointer movement, report byte offsets or guessed model geometry. Coordinates survive window scaling. Existing saved controls without an elevation remain on the top view. Changing elevation requires choosing a new position; points are never silently projected across views.

## Capture lifecycle

1. The selected connection supplies an exact interface allowlist; HID collections are associated through the Windows device tree.
2. Listening is explicit and temporary. The app pauses its remapping before opening the learning view. **Refresh input discovery** only scans interfaces; **Listen for button** starts a 30-second observation after a brief release guard. The view shows remaining time, selected-connection data arrival and confirmation progress. Mouse movement can establish that data arrived, but never counts as a button press. Timeouts distinguish no data from data without a confirmed button and one incomplete press.
3. Raw Input registration for the main activity observer is reused. Additional selected HID usage classes are registered only during capture and removed on stop/close. There is one registration owner per process.
4. Device headers are checked before fetching a full report. Other connections are discarded. Mouse movement and ordinary keyboard typing are not emitted as learned controls. Keyboard fallback is limited to F13–F24; HID keyboard-page usages are similarly restricted.
5. Mouse button edges/wheel direction or declared HID button usages feed a device-scoped learning session. Two matching fresh presses are required after a short activation guard; held repeats are ignored, and rapid high-resolution wheel fragments are coalesced for confirmation.
6. Native HID parsing finishes before confirmation callbacks can dispose parser memory. Stop/timeout/device change/window close release native state and registrations.
7. Save publishes only the reviewed identity, name and point. Cancelling writes nothing.

## Assignment scope

Standard learned controls annotate the existing standard mouse binding and drawing marker. Standard-button and horizontal-wheel Click Circle can run through the separate [driverless trigger path](DRIVERLESS_CIRCLE.md), retaining the original input. Other learned HID inputs appear as additional rows; action configurations are stored as **inactive drafts**, because arbitrary HID suppression/rewrite is not implemented. Exclusive remapping requirements remain separate from Circle observation.

The settings schema remains 1 with additive learned-control and learned-binding collections. Existing fields, Circle documents, identities and themes are retained. Relearning the same input preserves its ID and action references. Button reset clears its action drafts but retains learned positions. Cross-mouse binding references and invalid coordinates are rejected.

## Evidence boundaries

Fixture tests cover native capability layouts, input decoding, source isolation, repeat/arming behavior, persistence and invalid data. Offscreen WPF tests cover the learning form. Read-only native descriptor discovery has been exercised on a local Windows host. A declared usage count is not a physical button count. Physical press acceptance and exclusive remapping of newly learned HID inputs are separate and not certified by these checks.

Microsoft references: [button capabilities](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/button-capability-arrays), [HidP_GetUsages](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getusages), [Raw Input registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices).
