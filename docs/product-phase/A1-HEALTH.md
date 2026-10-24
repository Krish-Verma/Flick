# Recording health foundation (A1)

The capture bar's status is now a button. Open **Recording Health** to see engine/save activity separately, source, actual negotiated encoder/resolution/FPS, estimated buffered duration and tracking limits, helper health, registered shortcuts, most recent recording/save errors and last validated clip duration/time. Close or Escape returns to capture controls. Errors remain available after stopping; unrelated footer messages do not erase them. The minimal save banner remains `Clip Saved` plus actual duration.

Engine state and save state are independent. A save accepted by the native host is still pending. Only an existing file that passes the existing ffprobe/media checks and durable index registration records a validated success. Routine save validation is metadata validation; full decode remains a separate media/qualification test. An older save retains the source/profile it had when requested, even if the selected source or recording session changes.

Health queries never launch a replacement helper. Polling is serialized; a response from before stop/restart is discarded. Missing/failed/malformed status is unavailable, not synthetic Off. A transient unresponsive host disables Save until valid readiness returns while preserving the last known engine state. An actual engine fault requires a new capture session. Duplicate, malformed or unmatched terminal save events cannot trigger indexing/notifications, and pending-save protection remains held while indexing starts.

This slice stores bounded current/last health in memory. Durable diagnostic logs, redacted user exports and the consented recording test are A2/A3; they are not claimed here. No telemetry, recording default, microphone consent, signing, updater, original-file deletion or public release behavior changes.

## Verification

- Cross-build on macOS with hash-verified SDK10.0.402: solution Release warnings-as-errors, zero warnings/errors. A task-local ignored ICO fixture only bypassed unavailable Windows icon generation; Windows CI must run the real generator.
- Focused IPC tests:34 passed. Health-ledger tests:16 passed. Exact integrated Windows counts/results and WPF render links will be added after CI.
- Baseline Windows run40084437463: Core271 passed/14 explicitly skipped, App5 passed; separate real-media job5/5, native compile and encoder-policy pass. This baseline is not hardware acceptance.
- Local full Core baseline:267 passed/4 platform-dependent failures/14 skipped on macOS. The four are Windows DPAPI, kernel32/process metrics and Windows-path settings. Subsequent portable runs explicitly exclude these four; Windows CI runs them unchanged.
- WPF fixtures20–26 simulate health/hotkey observations through production models and bindings. They do not claim actual capture, real hotkey conflicts or media validation. The existing STA UI test exercises the actual health disclosure, retained data, disabled Save and Escape above Settings.
- Windows PC metadata initially reachable via SSH: Windows11 build26300/RTX5060. Later SSH requests timed out and the observed1729MiB free RAM is stale; no physical tests admitted yet. Recheck access/headroom and use an isolated synthetic-window source, never private desktop content.
