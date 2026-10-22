# Flick Backend Stabilization — Technical Record

PR: [#1](https://github.com/Krish-Verma/Flick/pull/1); target `main`. Draft only. Keep existing v1.0.0 release unchanged until owner validates on Windows.

## Identified defects and repairs
- Capture host previously assumed `D:\Documents\ClipMax` for OBS plugins, process CWD and output dir; resolves runtime from its own executable and takes output directory from settings. Rejects invalid targets and dimensions rather than silently recording another monitor.
- Native Replay Stop now has a timeout/force-stop; OBS output channel references cleared before source release; missing audio/encoder or ambiguous saved path fails explicitly; save procedure result and pending-save timeout handled.
- WPF target discovery no longer leaves an idle host; app Stop shuts down helper. IPC reports unexpected host death and pending-request failure, checks Job Object assignment and exposes only a test-owned PID.
- FFmpeg encode, verification, ffprobe and thumbnail workers now attempt confinement and fail closed if still running and assignment fails. FFmpeg stderr is drained to avoid pipe deadlock; one export at a time limits contention. Long-running workers remain subject to owner/GPU testing.
- Export duration is bounded even with `anullsrc`; output must pass decimal-size cap, probe codec/track count/duration and full decode before publication. Temporary MP4 sits on the final volume, and overwriting no longer deletes the last good export first.
- Combined preview audio is muted by default when dedicated tracks exist to prevent double mixing. True OBS recording track labels are carried via replay events into clip metadata. UI trim/audio changes persist and invalidate ready exports. Gallery rescan discovers originals on disk.
- Recycle Bin errors no longer permanently delete clips. Installer no longer kills every process with a matching name.
- CI explicitly distinguishes offline unit/WPF tests from six Windows GPU tests, and the latter fail (not silently return) if their required host is absent. The crash test targets only its own process; full buffer test waits 31 seconds.

## Known gaps / follow-on qualifications
- Hardware-driven native build, OBS source routing, full-duration replay, multi-track audio audibility, MKV playback, Discord desktop local drag/paste, idle worker counts, real performance, and long soaks all require AntiGravity on the owner's Windows GPU machine.
- Process assignment is immediately after launch; a strictly race-free guarantee would require starting suspended/in a job. Do not describe this as a proven absolute zero-orphan guarantee.
- Hard-coded original historical benchmark numbers in TEST-RESULTS are not post-fix measurements. No personal media should be put on a public release without owner review.
- GitHub repository visibility remains a separate owner administration action; the connected connector does not provide repository-visibility updates.

## Follow-up safety change
A closed window target now causes the native host to stop the active encoder after returning the fault response, rather than retaining a hidden recording. The UI also stops capture when the native host exceeds 768 MiB private bytes. Both require on-device fault/recovery testing.
