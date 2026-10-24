# Consented recording test

Open recording health, select a source and choose **Run Recording Test**. The same-window confirmation names the frozen source and audio choices. Declining creates no helper or recording. Audio is off by default for this test, independently of normal preferences. **Include configured audio in test** explicitly includes the selected computer output and the microphone only if it was enabled in normal settings. The confirmation describes both separately.

## Ownership and safety

The app reserves the test before its first asynchronous operation. It refuses active/starting/stopping replay, pending saves, imports, exports, scans, uploads, source discovery, update preparation and quit. Source changes and normal replay hotkeys cannot start another session during the test. Cancel remains available while recording and changes to a disabled finishing state during cleanup. Closing the health disclosure does not cancel the test. Quit cancels and awaits cleanup before exiting.

Saving Settings with the microphone switched off recognizes the private test as active capture and cancels it. Capture cleanup starts independently of slow validation/index settlement; the UI only reports the microphone released after confirmed helper exit. Update and uninstall actions refuse active test work. The confirmation keeps its heading and safe actions visible while a long exact source/device description scrolls.

An immutable capture-only plan holds the consent ID, source identity, negotiated preferences and audio device choices. Confirmation cannot authorize a different plan. Window binding requires HWND, PID, exact title and executable; display binding requires its enumerated device ID. Native code checks these expectations instead of falling back to another window or monitor.

The test owns a new helper and Windows Job Object. It never reuses or stops the normal replay helper. Before capture, an idle capability request must confirm the new audio and identity protocol; an older helper is refused before it can access audio. Normal recording calls keep their existing default system-audio behavior. The test's no-audio mode creates no WASAPI output or microphone source; OBS retains a single AAC bus labelled Silent Diagnostic Audio because its replay output requires an audio encoder. Actual captured silence still needs capture-fixture qualification.

## Media and completion

A unique directory under the configured originals folder contains the test recording. The controller waits for real Armed/target-valid state and retained video, not just a startup acknowledgment. The short-buffer threshold is bounded by the frozen replay duration. A unique save ID must match the returned terminal event and exact new regular file in that directory. Acceptance of the save command alone is insufficient.

The existing media probe and library registration validate the file and commit the index. Cancellation propagates through probing, thumbnail generation and the pre-commit boundary. A cancellation racing a completed atomic index write retains the registered clip. Success additionally requires confirmed exit of the private helper. Test originals are retained on failure/cancellation for recovery; this operation does not delete existing media or change normal preferences.

The result shows the actual encoder/resolution/FPS and measured startup, readiness, save, validation and cleanup times. An unconfirmed helper exit blocks new capture/tests and update installation until Flick is closed and reopened. This is separate from validating the saved clip.

The work budget is90 seconds with smaller phase deadlines and a separate bounded cleanup budget. Backend cancellation terminates and awaits the owned helper; it does not merely abandon a timed-out IPC task. All capture, storage and cleanup work runs off the WPF dispatcher. Stale progress from a finished test cannot overwrite a later result.

## Verification boundary

Controller tests use fake recording backends plus real temporary directories; they do not establish GPU capture, audible device success or performance. Adapter tests cover capability refusal, cancellation/exit settlement and terminal-save precedence. Portable native policy tests cover identity and audio routing, while Windows CI compiles the real native host. Real WPF fixtures exercise actual consent with automatic No, audio-off/on summaries, Cancel/finishing and results; their clips and phase timings are synthetic.

On Mac SDK10.0.402, the final focused recording-diagnostic suite passed63/63 and the portable Core suite passed409 cases with14 expected hardware/media skips; four Windows-only cases were excluded. The native policy compiled and passed with warnings treated as errors. The full solution and renderer cross-build without warnings. Deterministic tests hold an uninterruptible validation commit and prove cancellation/deadline stops the helper before releasing that commit, while the runner still waits for all owned work. A terminal-callback lock-order regression verifies concurrent shutdown does not deadlock.

Registration currently proves positive media metadata and durable indexing. Full decoding, captured silence, real audio audibility, gameplay performance and Discord transfer are separate qualification gates. Exact results/source/artifact references belong in the PR and evidence ledger. A4 supplies the isolated capture/recovery harness; no broad hardware test may record the owner's desktop/system audio by default.

No public release or Latest change is part of this slice.
