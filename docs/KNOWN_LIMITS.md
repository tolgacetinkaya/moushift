# Known limits for the preview

- Generic discovery cannot expose a firmware-only control that emits no accessible event. An opaque vendor report without declared button usages is not automatically decoded.
- Manual marker placement describes the user's drawing choice, not manufacturer geometry.
- An accessible input is not automatically suppressible. Arbitrary learned HID action assignments are inactive drafts in this release.
- Driverless Click Circle observes the five standard mouse buttons by exact Raw Input device path. The original trigger still reaches Windows. It does not enable ordinary shortcut/text/mouse remaps, wheel triggers, learned custom HID triggers, or anonymous keyboard triggers. Click a Circle action to execute it; hold-to-release is an exclusive-backend feature.
- See [the driverless Circle guide](DRIVERLESS_CIRCLE.md) for source isolation, original-input behavior and physical testing. Left/right triggers can change the foreground application through their original behavior; middle/side buttons are recommended for initial testing.
- Standard device-specific interception needs the existing optional input backend configured and verified separately. It is not bundled or installed automatically.
- See [the optional input support review and rollback guide](INPUT_SUPPORT_SETUP.md) before considering a Windows driver installation. Compatibility with unlisted Windows builds is unverified.
- Connection/receiver grouping cannot guarantee the identity or power state of each paired wireless mouse. Ambiguous identities never use a guessed last-active mouse.
- Keyboard fallback accepts F13–F24 only from selected associated interfaces. Source-less injected keys are not treated as a known mouse.
- Physical press/driver/OLE/multi-monitor acceptance across all devices is incomplete. Fixture and UI tests are reported separately from real-device results.
- The Windows x64 portable ZIP includes its .NET/Windows Desktop runtime and does not require a separate .NET installation. It is an unsigned preview; keep the extracted runtime files beside the executable.
