# Controlled recording and recovery qualification

Workstream A4 under issues38/36. Implementation and execution evidence are separate. This tool is a developer qualification artifact, not part of the installed consumer application. No new public Release is published by this workflow.

## Safety and ownership

`tools/Flick.RecordingHarness` references Core and WPF, never the real App/MainWindow/settings loader. It records only its own visible synthetic moving-pattern window, bound to its exact HWND, PID, title and executable. Computer audio and microphone are always off. The native helper emits an explicitly silent diagnostic AAC bus; validation must inspect decoded audio rather than assume silence from its name. A new unique output directory contains all original recordings, a separate library index, thumbnails, media validation and receipts. Existing directories and reparse components are refused; originals are never deleted.

The default command refuses capture. `--self-check` exercises non-capture validation and reports exact case counts. Real execution requires `--run-capture`, explicit absolute helper/media/output paths and the expected actual encoder ID. Session0 or a noninteractive desktop is refused before WPF/capture construction. Hosted CI build/self-check passes do not qualify a physical encoder.

The separate CI payload includes a self-contained Windows x64 harness, the matching compiled capture helper, the full pinned OBS/FFmpeg runtimes, legal notices and SHA256/source manifest. Verify the artifact SHA256 against the CI artifact receipt before extraction, verify source tree against the reviewed PR, then verify every manifest file. Do not substitute installed Flick binaries or run broad legacy hardware tests against a real profile.

## Owner-PC execution

Use only an explicitly owned staging directory. Keep the qualification manifest at its root, with `harness`, `tools` and `scripts` beneath it. Choose an absent output named `Flick-RecordingQualification-` plus32 lowercase hexadecimal GUID characters within that stage. The launcher rejects unlisted files, changed bytes, path escapes and reparse components. Launch only after available RAM is at least3072MiB and CPU is below60%; a failed admission is a refusal, not a test pass.

`scripts/run_recording_qualification.ps1` requires `-RunCapture`, `-ManifestPath`, `-HarnessPath`, `-HostPath`, `-FfmpegPath`, `-FfprobePath`, `-OutputDirectory` and `-Encoder`. For the available NVIDIA PC the primary expected encoder is `obs_nvenc_h264_tex`; a separately selected `obs_x264` run is CPU evidence, never NVIDIA evidence. Paths must point to the verified stage. The launcher does not install Flick or operate the owner's existing application.

The SSH process may run in Session0 while Explorer runs in Session1. The bridge uses only the same owner's existing Explorer primary token, checks SID and session, and creates the explicit harness on `winsta0\\default`. It does not change token/session fields, adjust privileges or ACLs, register services/tasks, request credentials or bypass Windows security. If existing access is insufficient, preserve the refusal; an owner-launched interactive run remains the specific missing gate. Microsoft documents the existing-token session and required access for [CreateProcessAsUser](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasuserw) and [DuplicateTokenEx](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-duplicatetokenex).

The Session0 launcher retains the exact process handle returned at creation, never a process-name kill list. The harness owns its helper Job inside its own session; [Windows Jobs require all assigned processes to share the Job's session](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject). An outer deadline writes an owned cancellation sentinel and grants45seconds for cleanup, then may terminate only that retained harness handle. Forced termination is failure, not successful cleanup. Preserve launcher admission/PID/exit-code/after-resource receipts separately from every capture case.

## Required interpretation

Successful capture cases require the actual expected encoder/profile, readiness, matching saved path/ID, positive media metadata and persisted isolated index, full FFmpeg decode, recognizable fixture colors and motion, decoded silent audio, and confirmed helper exit. Recovery cases exercise cancellation and controlled source loss, retaining originals and refusing false success. Every suite case must execute; skipped/unsupported gates are explicit limitations.

The independent Windows App test registers only temporarily available Ctrl+Alt+Shift+F17–F24 chords on owned message-only windows. It tests a real registration conflict, restoration of the prior pair and release on disposal. It injects no keyboard events and does not use Flick's normal shortcuts. This proves [Win32 hotkey registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) and rollback, not physical keyboard delivery during gameplay.

The available physical PC is Windows11Pro build26300 / NVIDIA RTX5060. A result there cannot establish AMD/Intel, other drivers, HDR, exclusive fullscreen, changing displays, device/headphone/microphone/Discord routing, gameplay FPS/1% lows, or Windows publisher trust. Those gates retain their own evidence requirements. No driver, BIOS, security or unrelated configuration changes are allowed to obtain a pass.

## Recovery guide for the app

1. Open Recording Health and read the retained recording/save error, actual source/profile and shortcut registration state.
2. Confirm that the intended source still exists and is available. Choose it explicitly if its identity changed. Check the clip folder's accessibility and free space.
3. Let pending saves settle. Use the consented Recording Test only when replay and editing/export/update operations permit it. Its confirmation names the exact source and audio choice; leave audio off for a silent test.
4. A failed test keeps its original recording when created. A successful library entry is not proof of audible endpoint capture; inspect the clip before relying on it.
5. Cancel once and wait for cleanup. If helper exit cannot be confirmed, further capture/update operations stay blocked until Flick restarts. Do not launch another recorder over uncertain cleanup.
6. Export the bounded redacted diagnostic ZIP when troubleshooting. It contains no clips, titles, paths, settings, webhook secrets or raw native logs. Share it only by your own choice.

## Execution ledger

PR48 merged044c63f3970a40192103882fe9d53a8f0a719b21. Sourcea7b091cc50629ee30dfed1b3b3c94590ec7d397e / treeb9da941d52ee87317d6664a7a1928aeed05e30ac. CI40095551548 passes413Core,6App (including real shortcut conflict/rollback/disposal),20non-capture harness checks and5real synthetic media tests plus native/policy/source/publish. Unit artifact12560384232 SHA2566c1d34ec4f7fcf0c2d73b1e4f7858f518099239d09b7a27a5915d70f700f4885 verified with exact TRX/JSON counts. Installer/package40095551585 passes. Qualification artifact12561380299 SHA2565264db2d08292357491f0c64db6e185667fe35851f64de1db99969f86eaed791; inner ZIP1e50cfaf274152d49ac82ff271675d7354067c3858105405e966ba4d86332af1; all2525files independently rehashed. CI merge8a5ae411 matches exact source tree. Actual WindowsPS5.1 script parsing/Add-Type compile passed without bridge invocation.

Physical attempt was refused before any child creation: fresh admission10682MiB/13%CPU passed, but CreateProcessAsUser returned Windows error1314 (required privilege unavailable to SSH). HarnessPid is null, no forced termination or cleanup error. A separate no-capture `--help` readiness attempt confirmed1314 and also created no process. All seven physical capture cases remain unexecuted. Do not grant privileges, change security, register a task/service or supply credentials to overcome this. The remaining gate is an owner-launched executable in the normal interactive desktop. A3 accepted evidence is recorded in A-plan/EVIDENCE; no A4 GPU/audio/performance success is claimed.
