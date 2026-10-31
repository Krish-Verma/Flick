# Replay startup failure

## Confirmed source defect

Normal monitor startup supplies the selected display identity in `CaptureStartOptions.ExpectedMonitorId`. Refreshing capture targets shuts down an idle helper and clears its cached capabilities. Default system-audio capture did not query capabilities before the startup guard. That guard treated a normal display identity as a consented diagnostic request and rejected the request before `start_capture` reached native code.

The visible “Encoder / profile: Not recording” line is a consequence of having no negotiated profile; it is not itself a GPU-driver error. The ledger initially labels the helper Starting, and the startup failure/poll return path did not reliably publish its terminal helper state.

## Fix constraints

Verify ordinary source identity against the current helper before normal startup. Keep diagnostic no-system-audio consent, verified-live-helper checks, exact window/process identity and native missing-monitor rejection. Do not convert Buffering to Armed until the real replay buffer is decodable. Surface the real failure state without pretending the host or encoder succeeded.

## Acceptance

Exercise the real managed startup and background Replay routes with an owned metadata-only IPC helper. Cover a stopped helper after target refresh, fresh capability negotiation, ordered requests, exact monitor identity, missing capabilities, helper replacement/exit, diagnostic preverification, Buffering versus Armed, and published failure health. Retain existing native negotiation, generated-media, installer lifecycle and rendered UI checks.

A timed-out SSH connection currently prevents inspection of the affected Windows session. Automated fake-helper tests demonstrate the managed handshake and failure handling; they do not establish physical GPU/audio or gameplay success on that PC.
