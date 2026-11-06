# Contributing

Use an issue to describe a reproducible problem or an undetected input. Include the app/Windows version, connection type, expected result and actual result. Mark hardware evidence separately from simulated/fixture results.

Do not publish personal profiles, account data, serial numbers, full device paths, proprietary application captures or unreviewed raw reports. Generic descriptor counts and redacted summaries are sufficient to begin an investigation.

Keep generic HID discovery free of vendor/model-specific protocol commands. Do not install drivers or modify device settings in tests. Never infer source identity from the last mouse moved or from an anonymous keyboard/mouse hook.

Build with .NET 10 SDK on Windows. Run `scripts/Verify.ps1` before submitting. It uses isolated settings and offscreen WPF rendering. Describe actual hardware testing separately. Do not mark a live mapping as working based on a visual preview.

Changes to storage must preserve existing profiles, application scope, theme preferences and last-good backups. New app packages/drivers require discussion; this project currently has no third-party NuGet dependencies.

Contributions are proposed under the repository's MIT license. No CLA is required.
