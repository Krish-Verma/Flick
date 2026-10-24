# Approved game replay — implementation and acceptance

Development branch `feat/game-automation-complete-20261024`, based on `a7b091cc50629ee30dfed1b3b3c94590ec7d397e`. This is authored B1–B4 implementation, not a release or hardware acceptance receipt. The owner authorized parallel isolated feature delivery; final integration and full Windows qualification belong to the integration lead.

## Behavior

Settings → Games is optional and off by default. Find Running Windows is an explicit metadata discovery action. Approval records the observed full local executable path, a user-visible name, a replay quality preset, duration and separate microphone consent. At most 32 profiles are accepted. No basename-only, relative, wildcard, device or network-path approval is inferred. Approval identifies a location; it does not attest the executable's publisher or hash.

All Games controls edit a deep staged copy. Cancel retains the live settings. Save validates the complete list and confirms enabling, disabling or changing active game configuration. Enabling with no enabled approved game is refused. Automatic microphone capture requires both Audio → Record Microphone and that game's explicit microphone permission. The automatic settings/target snapshot is never saved over manual source, video, duration or microphone preferences.

Enabled detection uses a single three-second dispatcher timer with one awaited operation at a time. Disabled, invalid and empty configurations never call its native enumerator. The existing explicit/manual source picker remains separate. Ordinary Win32 metadata is queried on a worker, bounded to 1024 top-level windows and bounded title/path buffers; cancellation or truncation cannot yield a capture decision. There is no process injection, screenshot scan, overlay, telemetry or upload.

Exactly one eligible approved process/window may start. Multiple instances, multiple eligible windows or another approved running process make selection ambiguous; no foreground or first-window tiebreak exists. Elevated, hidden, minimized, cloaked, tool/desktop and unverifiable windows are ineligible. Alt+Tab does not revoke a still-visible approved window. An active automatic recording also rescans for ambiguity and stops rather than retargeting.

Runtime identity is full executable path + PID + raw creation FILETIME + HWND. Titles are transient display/consent snapshots. An atomic bounded local reservation is persisted **before** automatic startup. Manual stop, startup/native fault, minimize/identity loss and app restart cannot silently retry that still-running game process. Old reservations are pruned only when a direct process probe confirms exit or a changed process instance; a missing/minimized window is not exit. Unknown process status stays suppressed. This is deliberately conservative: restoring a minimized game after it stopped requires a game restart or manual replay start.

Manual replay reserves ownership even after an engine fault, so the automatic timer cannot replace a faulted manual session. Manual capture starts/stops also reserve observed approved game instances. A failure to verify or persist the manual-start pause state refuses startup while automation remains enabled. Stop still releases recording if a later state write fails and pauses automation for that app session; the UI reports the storage problem. Review Games settings before restarting in that failure case.

Pending saves/index registration block new automatic starts. Stopping retains the native bounded pending-save finalization path and original files. Metadata scanning/automatic startup reserve capture controls, imports/exports, diagnostics and the updater/quit callback. Stop/quit cancel and await an in-flight automatic startup. UI and tray Stop remain available during automatic startup. Diagnostics, updates and shutdown retain their A1–A3 guards; the integration lead must review their combined C/D/E reservations.

## Exact native source

The stock OBS `window_capture` resolver is no longer used for window video. `strict_window_source.hpp` registers `flick_exact_window_capture` as a synchronous video/custom-draw/sRGB source. It has no audio flag or nested process-audio source. Monitor capture and the existing scene, scaling, replay encoders and save pipeline remain separate.

The source validates full path/PID/creation time, holds a query/synchronize handle to the original process object, and passes the exact HWND to the pinned `libobs-winrt.dll` C API. Every tick/render gates that held process's liveness, HWND ownership and visibility. There is no string resolver, automatic different-HWND reacquisition, BitBlt or monitor fallback. Stock WinRT device-loss callbacks are replaced by a terminal release callback: device loss frees the capture and requires a new recording session, rather than rebuilding against a potentially recycled HWND. Queued graphics-thread destruction frees the capture before its process handle; the existing `obs_wait_for_destroy_queue` barrier precedes canvas/device reset.

Libobs itself owns WinRT apartment/dispatcher initialization on its graphics thread; this adapter does not create a second apartment/thread. It dynamically loads the same absolute pinned runtime DLL and retains its module lifetime through capture destruction/shutdown. Missing APIs, unsupported WGC or failed initialization refuse recording. HDR uses the DLL's forced-SDR path; actual visual quality remains a hardware gate.

Primary source references: [OBS 32.2.2 C ABI](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs-winrt/winrt-capture.h), [WinRT lifecycle](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs-winrt/winrt-capture.cpp), [graphics-thread initialization](https://github.com/obsproject/obs-studio/blob/32.2.2/libobs/obs-video.c), [upstream source lifecycle](https://github.com/obsproject/obs-studio/blob/32.2.2/plugins/win-capture/window-capture.c). The existing GPL/source/notice obligations remain; this adapter does not relicense OBS or certify distribution compliance.

Actual PE inspection of the verified A4 payload found all required C exports in x64 `libobs-winrt.dll`, SHA-256 `a77ae0ec5c2c23685d3c6da15b6cb3339ce3eb7b528e52e969252b8c3f2639f1`. Parent artifact 12561380299 outer SHA-256 `5264db2d08292357491f0c64db6e185667fe35851f64de1db99969f86eaed791`; its inner payload is hash-verified by the integration lead. Inspecting exports does not load the DLL or prove capture works.

## Shared integration contracts

- `CaptureWindowIdentity` appends optional `ExecutablePath` and `ProcessCreationFileTime`, preserving four-argument callers. Wire keys are `expected_window_path` and `expected_window_creation_time`; `exact_window_binding` is a separate required capability for automatic and newly selected manual window capture.
- `TargetWindow` / `TargetItem` add `ExecutablePath` and `ProcessCreationFileTime`. Enumeration serializes `executable_path` and `process_creation_time`; the managed parser accepts C's `exe_path` alias too. The integrator should select one canonical spelling.
- Settings schema 4 adds `GameDetectionEnabled` / `ApprovedGameProfiles`. Only an explicitly valid schema accepts saved automation. Old/future/malformed/duplicate game configuration defaults disabled and retains the damaged original under the existing explicit-save recovery guard. Reconcile a single final schema with C/D before integration.
- `%LOCALAPPDATA%/LocalClip/game-replay-state.json` contains only approved process identity reservations, bounded to 128 entries / 512 KiB on read. It has no window titles or scan history and is not added to diagnostic export. Explicit profile-removal uninstall deletes this known file; ordinary uninstall retains it.
- `MainViewModel` becomes partial. New UI bindings: `GameDetectionStatus`, `IsAutomaticGameCapture`, `AutomaticGameSourceText`, `ActiveReplayDurationSeconds`. `ApplyGameDetectionPreferencesAsync` is called after app initialization and explicit Settings Save. The source bar displays the automatic profile while preserving its hidden manual selection.

## Local evidence and remaining gates

| Gate | Evidence |
| --- | --- |
| Focused Core policy, migration, suppression, source-wire and GPU preference tests | 38 passed, 0 failed, 0 skipped on macOS .NET 10.0.402; filter `GameDetectionTests` or `GpuCompatibilitySettingsTests` |
| WPF application and App tests cross-build | Passed, 0 warnings/errors with Windows targeting; no WPF execution on macOS |
| New STA staging/cancellation assertions | Executed in final App9 suite40102045722 |
| Native Windows x64 compile/link | Passed with llvm-mingw 20261020 against pinned OBS 32.2.2 `obs.dll`; no executable run. `-Wall -Wextra` reports six existing unused-parameter warnings in main.cpp, none in new headers |
| Exact DLL export availability | PE table inspected, no code loaded; hash above |
| Broader existing settings recovery fixture | Its literal `C:\Videos` path is invalid under macOS path semantics and fails there; unchanged Windows fixture awaits Windows batch. No production workaround was added for the host-only test limitation |
| Full integration, WPF pixels, media, installer/upgrade, cross-feature races | Passed final CI/media/render/installer batch; see FINAL-REPORT.md |
| Physical exact-window capture and overhead | Pending; no Windows PC execution by B lane |

The checkboxes in B-plan remain unqualified until their Windows interaction/recording evidence is recorded. Local compilation and pure policy tests do not establish real game/anti-cheat support, correct WGC frames, hotkey delivery or device-loss behavior.

## Required Windows acceptance

1. Actual Settings Games flow at 1240×760 and 720×480, 100/125/150/200% DPI: long names/full paths, 32 profiles, keyboard/focus, scrolling, enable/disable confirmation, Cancel with zero writes, per-game microphone off by default. Never press Find Running Windows in a fixture claiming zero scans.
2. Use owned synthetic windows/processes: exact approved path; identical basename/title/class from a different directory/process; multiple approved windows/games; source exit/restart; deliberately stale HWND/PID/creation identity. Decode the saved moving fixture and prove the unapproved window is never selected.
3. Auto start → Save → manual Stop; minimize/restore; Alt+Tab; crash/fault; relaunch Flick while the same process lives; exit/restart the game. Check persisted suppression, original/index retention, one helper, and no automatic retry loop.
4. Cancel during source discovery/startup; save in flight; open/run/cancel recording diagnostics; disable microphone; import/export; update confirmation and quit. Verify operation settlement and no late restart/callback mutation.
5. Device reset/loss, source resize, hybrid adapters/HDR/exclusive fullscreen/elevated refusal and monitor rearrangement. Source loss must stop, not search for a replacement. This native adapter is new code and needs its own actual-Windows acceptance despite prior A4 stock-source receipts.
6. Measure idle disabled/enabled CPU/private bytes, scan latency and recording/start/readiness/save latency on equivalent fixtures. Disabled feature must have zero background enumerator calls; no universal GPU, gameplay-FPS or anti-cheat claim follows from one machine.

Windows API basis: [EnumWindows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows), [full executable path](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew), [process creation time](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes), [recycled HWND caveat](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow), [direct capture item](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow).

### Integrated app-audio restart boundary

Separate App Audio remains consented to an exact running process instance. Automatic replay refuses to start if the selected Game Audio process differs from the newly detected game or has restarted. Games shows a waiting message directing the user to select the current game in Settings → Audio. This does not silently rebind audio or switch to device loopback. Voice selections also require refresh after their selected app restarts; the native identity/activation gate refuses stale instances.
