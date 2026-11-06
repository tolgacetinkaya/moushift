# Known limits for the preview

- Generic discovery cannot expose a firmware-only control that emits no accessible event. An opaque vendor report without declared button usages is not automatically decoded.
- Manual marker placement describes the user's drawing choice, not manufacturer geometry.
- An accessible input is not automatically suppressible. Arbitrary learned HID action assignments are inactive drafts in this release.
- Standard device-specific interception needs the existing optional input backend configured and verified separately. It is not bundled or installed automatically.
- Connection/receiver grouping cannot guarantee the identity or power state of each paired wireless mouse. Ambiguous identities never use a guessed last-active mouse.
- Keyboard fallback accepts F13–F24 only from selected associated interfaces. Source-less injected keys are not treated as a known mouse.
- Physical press/driver/OLE/multi-monitor acceptance across all devices is incomplete. Fixture and UI tests are reported separately from real-device results.
- The portable package is framework-dependent, unsigned and requires the .NET 10 Desktop Runtime.
