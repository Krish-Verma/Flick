# Flick 1.0.10 Preview 1 — Capture Helper Fix (UNSIGNED)

Fixes the missing `libwinpthread-1.dll` error when Flick starts its capture helper. The threading runtime is now included inside the helper. No separate DLL download, compiler installation or settings reset is needed.

The earlier package allowed Flick's main window to open while its helper could fail. Builds now inspect the helper's actual DLL imports and require it to answer and exit using only its bundled files and standard Windows paths. The installed upgraded copy receives the same check. These checks do not start recording or capture audio.

Install **Flick-Setup.exe** over the existing installation without uninstalling first. Portable users should quit Flick and replace the complete extracted folder. Clips, preferences and existing features are preserved. Installed preview-enabled copies can check Settings > General > Software updates after publication.

This remains an unsigned preview. Windows security protections remain enabled. Physical gameplay, GPU/audio, Discord and endurance testing are separate from the automated checks. Previous release entries, assets and tags are retained.

Verified checks for this exact source include the negative dependency fixture, native and portable helper startup without developer paths, installed upgraded helper startup, installer upgrade/wait/restart/data retention, app/core tests, generated-media checks and WPF rendering. No physical gameplay qualification is claimed.
