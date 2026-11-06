# Optional device input support

This is a review and rollback guide for optional exclusive remapping, not an automatic installer. Clickloom does not install drivers. Driverless Click Circle is available through source-identified standard Raw Input mouse buttons; its original trigger still reaches Windows. No driver is needed for this mode.

## Current implementation

The optional exclusive per-mouse path uses an Interception Windows input driver and an x64 `interception.dll`. For exclusive remapping it must open the driver, discover input sources, confirm its filter, and match a source to one physical Windows connection. Driverless Circle activation has separate readiness checks and executes only the Circle binding from the actual source mouse's profile.

Arbitrary custom HID usages remain inactive assignment drafts even with this backend. Its supported live controls are the standard five mouse buttons and vertical/horizontal wheel events actually emitted by the device. Firmware-only DPI and sniper controls may emit no usable event.

## Compatibility decision before installation

- The upstream README states testing through Windows 10. It does not establish Windows 11 or later-build certification. Treat unlisted builds as unverified, not automatically incompatible or supported.
- A valid Authenticode signature, the presence of an x64 DLL, and successful fixture tests do not prove current kernel loading, device compatibility, or reliable forwarding.
- Review the current Windows version, HVCI/memory-integrity state, vulnerable-driver blocklist, installer origin, and embedded driver signatures. If a security policy blocks loading, stop and record the Code Integrity error. Do not disable Secure Boot, memory integrity, the driver blocklist, or signature enforcement to make this preview work.
- Prefer a separate test Windows installation and basic spare USB input devices before changing a primary workstation's input stack. A virtual-machine result is not proof for the physical Corsair wireless connection.
- Do not redistribute the vendor driver/DLL under Clickloom's proposed MIT license; Interception's separate upstream terms apply.

## Prepared installation sequence — not executed

1. Obtain the package from the official upstream release and verify the exact files before running them. Keep the same package available for rollback.
2. Close Clickloom. Save work and prepare a tested Windows recovery/restore path and alternative input access. Record existing mouse/keyboard class filters and driver registrations before making any change.
3. After a separate explicit installation decision, open an administrator PowerShell in the package's `command line installer` directory. First run the installer without arguments and confirm its help and supported flags. The local v1.0.1 artifact contains `/install` and `/uninstall` usage strings; its runtime behavior has not been exercised in this review.

   ```powershell
   .\install-interception.exe
   # Only after reviewing help and approving the system change:
   .\install-interception.exe /install
   ```

4. Inspect the exit/output. Do not reboot automatically; arrange a reboot with the user. The inspected package's messages require reboot after installation.
5. After reboot, verify basic mouse/keyboard operation before opening Clickloom. Confirm driver service state and Code Integrity event logs. Stop on any forwarding failure or security-policy rejection.
6. Open Clickloom → Settings → Input support → select the package's `library/x64/interception.dll`, then quit and reopen Clickloom. Selecting a DLL alone does not install the driver.
7. Verify that the intended Corsair Windows connection has a separate, unambiguous input route. Only then enable assignments. Test one standard control at a time outside Clickloom, check that the original action is consumed, confirm the correct foreground application Circle, and test pause/emergency behavior. Other mice must keep their own profiles.

## Prepared removal sequence — not executed

1. Pause assignments, quit Clickloom, and close applications using Interception.
2. In an administrator PowerShell in the same installer directory, review help, then run:

   ```powershell
   .\install-interception.exe /uninstall
   ```

3. Inspect the output and arrange a reboot. After reboot confirm mouse/keyboard operation and that the relevant services and class-filter entries return to their recorded baseline.
4. Keep Clickloom profiles and appearance files. Clearing the optional library preference can be done separately after backing up `device-input.json`; it does not require deleting `profiles.json` or `appearance.json`.
5. If input prevents ordinary sign-in, use the recovery path prepared before installation. Do not manually delete `mouse.sys`/`keyboard.sys` or blindly rewrite class filters: these names and registrations require comparison with the exact baseline. Removal is a plan, not a guaranteed recovery outcome on an untested OS.

## Sources and evidence limits

- [Upstream Interception README: tested platforms, administrative installation, licensing](https://github.com/oblitum/Interception#readme)
- [Official v1.0.1 release](https://github.com/oblitum/Interception/releases/tag/v1.0.1)
- [Microsoft vulnerable driver blocklist](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules)
- [Microsoft memory integrity and VBS](https://learn.microsoft.com/en-us/windows-hardware/drivers/bringup/device-guard-and-credential-guard)

Local native resource inspection is static. It has not executed the installer, loaded a kernel driver, changed system settings, rebooted Windows, or certified a physical mouse.
