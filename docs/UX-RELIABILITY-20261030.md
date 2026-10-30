# UX and replay reliability — October 30, 2026

Candidate: 1.0.11-preview.1. This record distinguishes implementation from executed qualification; final receipts are attached to the matching source and release.

| Reported flow | Cause found | Implemented behavior |
| --- | --- | --- |
| Hover on clickable controls | Whole-control opacity dimmed text and graphics; some templates switched to an unrelated fill abruptly | 125 ms background-only brightening, preserving each control's base color, with immediate reduced-motion feedback and system-color high-contrast outline |
| Save Settings | Closing the settings workspace could restore a compact editor/tool rather than the dashboard; validation and OS registration failures needed separate handling | Verified saves return to the clips dashboard and close contextual tools; invalid fields/failed writes preserve the draft; startup warnings do not discard unrelated preferences |
| Start with Windows | Registration only ran when a toggle changed; repeat Save could not repair a missing/stale entry | Every enabled explicit Save repairs and reads back the exact quoted owned app command, including --startup and optional --minimized; Windows-disabled startup is reported without overriding Windows approval |
| Background replay | Running in the tray and recording were unrelated; the app could appear available with no buffer | General's Keep replay ready in the tray starts only the saved, currently available source during background/logon operation; ordinary foreground launches remain manual/approved-game driven |
| Game replay after restart/minimize | Automatic starts and failed attempts were persisted in the same suppression set as explicit Stop | Version 2 records only deliberate game pauses; transient window loss recovers; one exact foreground game window can resolve a same-process multi-window case; different game processes remain ambiguous |
| Manual Stop/Start | Pause intent and startup work could outlive each other | Stop cancels pending starts and pauses automation; explicit Start resumes verified eligible sessions; quit/source changes do not create user pauses; recovered settings must be reviewed before automatic recording |
| Reopening the tray app | Second instance exited without activating the first; tray Settings navigated in a hidden window | Same-user/session, payload-free activation restores the existing instance; startup launches do not steal focus; tray Settings restores before navigating |
| Clip names/source restore | Internal save IDs were used as library titles; source refresh could retain an obsolete object; monitor indices could move | Readable source/date titles and legacy display fallback preserve original files/custom names; refresh restores only a unique saved source and prefers saved monitor device identity |

## Qualification plan and boundaries

- Focused policy/title/source tests run against actual Core sources. Windows CI also runs settings persistence, startup registration/readback/rollback, isolated named-event activation and actual WPF hover/raster tests.
- Actual WPF rendering covers existing app surfaces plus hover, disabled, reduced-motion, high-contrast, successful Save-to-dashboard and invalid-draft cases at supported sizes. Fixtures are inert: no capture, real app discovery, global hotkeys or owner profile/registry changes.
- Native PE dependency and clean-path ping/shutdown checks remain required for portable and installed helpers; installer upgrade/wait/restart/data-retention checks remain enabled.
- The existing Clip Saved banner remains only Clip Saved plus actual duration. Microphone and exact process-audio opt-ins are unchanged. No unknown window or desktop is substituted for an unavailable saved source.
- This remains an unsigned preview. Hosted checks do not establish physical gameplay, GPU/audio isolation, Discord transfer, logon timing, endurance or universal hardware acceptance. Previous releases/assets/tags are preserved on publication.

## Design references

Impeccable4.8.0 was consulted from upstream revision df3a4abe7d0840d5f60324812c432e00b66a4e75, applying Operate-mode restraint, native audit, animation, craft-floor and polish guidance. Existing purple/graphite identity and architecture are preserved. Mechanical web detectors are not evidence of WPF quality; actual WPF rendering and interaction assertions are required.

Primary platform guidance: [Microsoft visual feedback](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback), [mouse interactions](https://learn.microsoft.com/en-us/windows/apps/develop/input/mouse-interactions), [Run registry keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), and [.NET named wait-handle scoping](https://learn.microsoft.com/en-us/dotnet/api/system.threading.namedwaithandleoptions?view=net-10.0).
