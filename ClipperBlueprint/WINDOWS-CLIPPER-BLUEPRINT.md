# Windows Clipping App Blueprint for Anti Gravity

Final planning handoff. Prepared October 13, 2026. Working name: LocalClip, a placeholder rather than a finalized public brand.

Build a free Windows application that preserves the useful SteelSeries Moments workflow: manually save recent gameplay with a keyboard shortcut, review and trim the clip, adjust captured audio, and create a watermark-free file small enough to drag into Discord. Its distinguishing requirement is predictable resource use and reliable cleanup. Start with the owner's PC, prove the capture engine works, and then prepare a free public Windows release.

The plan has passed three reviews: owner-requirement coverage, technical correctness and failure handling, and scope/acceptance completeness. Performance budgets remain proposed engineering targets; no application has been implemented or benchmarked as part of this planning work.

The owner has delegated the planning layer. The decisions below resolve ordinary implementation choices; do not reopen them merely to offer a different framework. Raise a question only when a verified constraint materially changes the features, safety, licensing, or cost. This is an implementation blueprint, not authorization to publish publicly or change unrelated host configuration.

## Owner decisions and project boundaries

### Confirmed requirements

- Windows only. Personal testing comes first; a free public Windows release follows successful validation.
- Free to download and use. No subscription, paid feature tier, mandatory donation, watermark, trial timer, or account requirement. Development effort and agent token costs are accepted; paid runtime services are not.
- Manual capture and keyboard shortcuts. No automatic game detection or automatic kill and win highlights in the first release.
- Separate game, microphone, and Discord audio where supported, with an explicit combined-audio fallback.
- Local export should feel like Moments: choose a clip, trim if necessary, select Discord export, then drag or copy the ready file into Discord.
- Direct channel posting is welcome if straightforward, but it must not replace or complicate local sharing. Implement it as the optional milestone described below.
- Avoid unnecessary engineering. No social platform, cloud storage, plugin marketplace, or general-purpose video editor.

### Meaning of manual capture

Manual saving of the preceding 30 seconds requires capture to have been running beforehand. Therefore, provide two independent actions:

1. Start or stop replay capture, using a button or configurable shortcut.
2. Save the most recent replay, using a separate configurable shortcut.

The app starts disarmed. Launching a game does not activate capture. Pressing Save while disarmed explains how to start replay; it never claims to recover footage that was not recorded. This interpretation keeps the requested replay behavior without introducing automatic recording.

Suggested initial shortcuts are Ctrl+Shift+F9 for capture toggle and Ctrl+Shift+F8 for Save Replay. They are defaults, not immutable keys; detect registration conflicts and let the user change them.

### Context that motivates the design

The owner values Moments' local Discord compression and watermark-free drag-and-drop sharing, and normally saves clips with a hotkey. Their PC has previously shown multiple SteelSeries capture services around 2 GB each. Windows also identified much larger, increasing committed-memory allocations in capture processes during low-memory events. An exact proprietary root cause was not established. This project must not promise that replacing Moments eliminates every driver or system problem.

The initial test machine is an NVIDIA RTX 5060, Ryzen 5 5600, approximately 16 GB of system RAM, and Windows 11. Recheck hardware and OS details before testing. The PC also hosts development workloads; resource measurements must distinguish this app from other processes. Do not change WSL memory, services, SSH, Tailscale, sleep settings, graphics drivers, or other applications to make this app appear lightweight.

Create implementation work in a fresh, dedicated project directory selected for this app. Do not place it inside an unrelated repository. Do not alter, uninstall, disable, or kill existing capture/audio applications without explicit owner approval. Ask the owner to avoid simultaneous recorders during controlled comparison tests rather than silently stopping them.

## Product scope and completion levels

### Version 1 local core

Deliver manual replay capture, configurable shortcuts, a small gallery, playback, trimming, audio selection and gain, Discord-size export, file drag/copy, a tray menu, storage selection, diagnostics, resource safeguards, and an installable or portable Windows build.

The core must work offline. Network availability is irrelevant to capturing, editing, exporting, or copying a file.

### Version 1 optional channel posting

After the local core passes its acceptance tests, add opt-in direct posting to an explicitly configured Discord server text-channel webhook. Treat it as a separate completion gate. If no owner-authorized webhook is available, complete and test the integration against a mock server, document the missing live test, and leave the feature disabled. Do not call the entire direct-posting milestone validated without a real authorized test.

### Later free public release

Package the validated core with documentation, source, dependency notices, reproducible build instructions, and checksums. Broaden hardware compatibility through tests rather than implying every GPU and game works. Public distribution requires owner approval of the repository, name, license, release destination, and actual artifacts.

### Explicit non goals

Do not build AI highlights, automatic game detection, a social feed, accounts, hosted share links, subscriptions, streaming, mobile apps, macOS/Linux clients, browser capture, cloud sync, custom codec implementations, cinematic effects, multi-clip montages, or anti-cheat bypasses. Do not add telemetry, ads, always-on preview, or a web server. No full-session recorder is required in version 1; keeping only a bounded recent replay is sufficient.

## User experience

### First run

1. Explain that replay starts only when enabled, and that the shortcut saves footage already in the buffer.
2. Let the user choose a window or monitor, save folder, microphone, and audio mode. Show which sources are enabled and what they contain.
3. Offer the two shortcuts, checking conflicts immediately.
4. Show the default capture profile and resource budget in plain language.
5. Leave capture off until the user starts it. Starting with Windows is optional, off by default, and never arms capture automatically.

### Main window

Use one straightforward native window with a capture status bar, clip list/grid, and selected-clip panel. Do not require tabbing through unrelated product sections.

- Capture status shows Off, Starting, Buffering, Ready to Save, Paused, or Error, with the selected target and actual buffered duration.
- The gallery shows filename/title, capture time, duration, source label, thumbnail, and export status. Provide search, rename, Open Folder, and explicit deletion.
- The selected clip has playback, a start/end range, available audio sources with mute and gain controls, an Export for Discord button, and a visible Ready File drag handle once an export is validated.
- Provide Copy File and Open Export Folder alternatives; dragging is not the only supported path.
- Optional Send to Channel appears only after the user configures a destination. Do not imply that the app can select arbitrary DMs or channels from the Discord client.

Keep the original recording separate from the share copy. Display that distinction. Changing trim, audio settings, or size target invalidates the previous share copy's Ready state for the current edits.

### Tray behavior

The tray menu includes Open, Start/Stop Replay, Save Replay, and Quit. Closing the window may hide it to the tray, but explain this on first close. Quit stops owned workers and releases the encoder. Minimize is not Stop; Stop is not Quit. Make capture state visible through the tray icon and a short, non-spammy notification.

### Manual target selection

The user selects a window or monitor. Prefer non-injecting Windows window capture for the initial release, with explicitly selected monitor capture as an alternative. No silent fallback from a game/window to the whole desktop: that could record private content. If the target closes, display disappears, or capture is blocked, pause and tell the user.

Monitor capture records everything visible on that monitor. Show a privacy warning. Do not promise to exclude notifications or overlays from monitor capture.

Selecting a new target while armed requires an intentional buffer reset, explained before the change. Identify windows using current handle, process ID, and process start time; do not treat a recycled handle/PID as the previous target. Remembering a previous choice is allowed, but revalidate it and never start capture automatically.

## Technical architecture

### Primary implementation decision

Use a native Windows shell in C# and WPF on .NET 10, with a small native C++ capture host around a pinned stable libobs runtime. Use FFmpeg/FFprobe as owned subprocesses for export, preview preparation, metadata probing, and verification. Build x64 first. .NET 10 is supported through November 2028 according to the current [Microsoft lifecycle page](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core); use a pinned supported patch release at implementation time.

Initial supported operating system is a maintained Windows 11 x64 installation. Verify Windows build, capture APIs, audio-loopback support, and the chosen runtime at startup. Do not claim Windows 10, ARM64, or unsupported OS compatibility just because a component can compile for it. Personal capture is validated on the RTX 5060 first; AMD and Intel support require actual encoder tests before their names appear as supported configurations.

OBS is designed with a reusable backend and separate frontends; embed its backend rather than requiring the full OBS UI or NVIDIA App. [OBS backend design](https://docs.obsproject.com/backend-design)

The native host provides crash containment and deterministic disposal for the engine. It is not a platform for plugins or multiple agents. Keep the boundary small. Do not implement a second capture backend in parallel.

### Components

| Component | Responsibility | Lifetime |
| --- | --- | --- |
| LocalClip.App | UI, tray, shortcuts, settings, clip index, export coordinator | One instance per user |
| LocalClip.CaptureHost | libobs sources, encoder, replay buffer, target state | One host only while capture is armed or finishing a save |
| OBS mux helper | Finalizes saved replay files if required by the pinned engine | Short lived and tracked |
| FFmpeg/FFprobe helpers | One export or preview task; metadata validation | Bounded, owned subprocesses |

No requirement to fit everything into one process: a small, owned helper is acceptable. The requirement is that workers have known roles, bounded lifetimes, and complete cleanup, not that Task Manager displays one row.

The flow is selected window/monitor and audio sources -> capture host -> encoded replay buffer -> saved original -> app trim/audio choices -> validated share MP4 -> local drag/copy, with optional webhook sending afterward. UI messages never transport raw video frames.

### Capture integration proof before full UI

Pin one stable OBS release and its matching headers, modules, runtime dependencies, and build recipe. Current online documentation may describe a newer version; do not mix it with older binary modules. Discover supported source and encoder IDs from the pinned runtime. Do not assume a module filename proves a source can initialize.

Prove actual GPU encoding, audio capture, replay saving, and shutdown in a small diagnostic host first. Verify the encoder selected at runtime. Shipping requires that the user does not install or run the OBS application separately.

Time-box the backend integration investigation to approximately 12 engineering hours. If embedding is demonstrably blocked by redistribution, packaging, or an incompatible runtime, write one short decision record with the evidence. The fallback is Windows Graphics Capture plus a hardware-capable encoding pipeline, not an unbounded sequence of framework swaps. Estimate the added effort and obtain owner agreement before changing the architecture materially. [Windows capture APIs](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)

### Small internal protocol

Use private inherited stdio pipes for parent/child control, newline-delimited JSON with protocol version, request ID, and bounded message size. Reserve stdout for structured messages and stderr for diagnostics. Maximum control message size: 64 KiB. No TCP port, local HTTP server, or general RPC framework.

Commands cover capabilities, start, stop, save replay, status, and shutdown. Events cover started, buffering, save completed, stopped, and error. Use explicit success/error replies and timeouts. Dispatch capture events onto the UI safely; drain both subprocess output streams to prevent deadlocks. Sample status at a low rate, initially once every two seconds.

Install the engine's logging callback so third-party diagnostic output cannot corrupt protocol stdout. Serialize replies through one writer; callbacks must not interleave JSON messages. A malformed, oversized, or incompatible message causes a visible protocol failure and controlled shutdown, not an automatic retry loop. Keep parent liveness and shutdown detection testable.

### Ownership and shutdown

Use a single-instance user-scoped lock. A second launch activates the existing UI rather than starting another worker. Start workers using explicit executable paths, controlled working directories, and escaped argument arrays, not shell command interpolation.

Place owned capture and export process trees in Windows Job Objects with kill-on-close where compatible with the pinned runtime. Stop gracefully first, wait for completion, and force-stop only the app's own job after a bounded timeout. Test helper inheritance, parent crashes, and runtime modules that create children; do not assume all descendants are covered. Job Objects support process grouping and limits but cannot cap every driver/GPU allocation. [Microsoft Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)

No searches for and termination of arbitrary obs, ffmpeg, NVIDIA, SteelSeries, or Discord processes. Never start a replacement capture host until the previous owned host has exited.

## Capture behavior and defaults

### Default profile

| Setting | Initial value |
| --- | --- |
| Replay duration | 30 seconds |
| Capture resolution | Up to 1920 x 1080; preserve aspect ratio; do not upscale |
| Frame rate | 30 FPS |
| Video codec | H.264 hardware encoder, NVIDIA NVENC on the owner's PC |
| Rate control | Constrained, approximately 15 Mbps, verified against actual runtime support |
| Keyframe interval | Approximately two seconds |
| Audio | 48 kHz, AAC, approximately 128 kbps per stored track |
| Encoded replay buffer budget | 256 MiB |
| Capture on app launch | Off |
| Maximum active capture hosts | One |
| Maximum active heavy export/preview jobs | One |

Use UI presets for 720p/1080p and 30/60 FPS, with 30 seconds default and a simple duration range of 15 to 120 seconds. Warn that longer/higher-quality replays need more memory and may exceed the buffer budget. Do not silently expand the budget to preserve duration.

Initial guaranteed scope is SDR capture and export on validated targets. Do not advertise HDR or every exclusive-fullscreen game as supported. Detect known unsupported capture conditions where possible and explain them. HDR-to-SDR support becomes available only after the corresponding capture/color conversion tests pass; never silently treat HDR pixel data as SDR.

Hardware encoder failure is visible. Do not automatically switch to an expensive CPU encoder while the user is gaming. Offer an explicitly selected low-quality software compatibility profile only after explaining and testing its overhead.

### Bounded replay storage

Keep compressed audio/video packets in memory, not the entire raw frame history. For example, 30 seconds at 15 Mbps contains about 56.25 MB of encoded video, before audio, keyframes, engine, UI, and driver overhead. This arithmetic is not a total-process RAM estimate.

Use the engine's time and packet-size limits and check observed behavior. Keyframe boundaries and save-time references can create temporary overhead; a packet cap is not a universal application memory cap. If the cap shortens a replay, show the actual retained duration rather than claiming 30 seconds. [OBS replay-buffer source](https://github.com/obsproject/obs-studio/blob/master/plugins/obs-ffmpeg/obs-ffmpeg-mux.c)

Do not continuously write a full-session recording to disk. Only saved replays, bounded logs, thumbnails, and explicitly requested preview/export artifacts belong on disk.

### Save semantics

Saving does not stop capture. A hotkey request snapshots only already available footage; early requests create a shorter clip with its actual duration. Use unique filenames and prevent collisions even for repeated presses in one second. Do not overwrite earlier clips.

Allow one save finalization at a time, with at most one pending save request. Coalesce or visibly reject excess repeats. Never queue unlimited snapshots that keep old buffers alive. Associate requests with their capture generation and reject stale completion events after target changes.

Preserve the hotkey's intended end timestamp. If a pending request cannot retain that moment without exceeding the buffer/snapshot budget, reject it clearly rather than saving a later, different moment. Saving and concurrent export must have independent cancellation identifiers; cancelling an export must not cancel a replay save.

On normal Stop, finish an accepted save where possible before releasing the host. If force termination interrupts a save, leave it incomplete rather than showing it as a valid clip. An unsaved in-memory replay cannot survive a power failure, crash, or reboot; do not imply otherwise.

### State transitions

Capture follows Off -> Starting -> Buffering -> Armed -> Stopping -> Off. Paused and Faulted are explicit states reachable from Starting, Buffering, or Armed. Saving is a bounded operation associated with an armed generation, not an uncontrolled second recording session. The UI says Ready to Save only after enough footage exists for the requested duration; Save can still intentionally produce a shorter clip while Buffering.

Paused releases active capture/audio resources instead of merely hiding status. It retains settings, not a promise of a full recent replay; manual resume starts a new Buffering generation. Store no unbounded list of earlier capture sessions. The immutable saved originals remain accessible while capture is paused.

Pause capture on lock, sleep, unavailable target, or unrecoverable device/encoder error. Resume requires manual action after unlock/resume or a fault in version 1. Do not change Windows power/lock behavior. Never restart endlessly. A capture failure tells the user that the unsaved buffer was lost and preserves completed clips.

## Audio requirements

### Supported modes

Preferred mode stores independent game, microphone, and Discord tracks, plus a normal combined track as the default playback track. Sources are individually enabled, with microphone off until the user enables it. Labels must reflect actual captured sources, not assumed routing.

For game and Discord separation, prefer supported per-process loopback for the selected process trees. Windows exposes process-tree loopback on suitable builds; processes outside that tree are excluded, and a process with no audio streams yields silence. [Microsoft process-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)

Discord has multiple processes; validate the actual audio-producing tree rather than assuming the first process named Discord is correct. Selecting a video window and selecting its audio source are related but distinct operations. The first-run test must prove that music from an unrelated app is excluded in separated mode.

If per-process capture does not work, offer output-device loopback plus microphone. Label it System audio plus microphone, not separate game and Discord. In that mode game and Discord cannot be independently muted afterward. Do not install a virtual audio driver or require Sonar. Existing separately routed devices may be selected explicitly, but must not be reconfigured silently.

### Avoid duplicate sound

Never mix a full system-loopback track and the same game's/Discord's process-loopback tracks simultaneously into the combined track. Disable system loopback in separated mode. Keep the microphone out of a duplicate monitored-output capture where possible; warn if the owner's routing already monitors it into the selected output.

App playback and export previews must not be recaptured into a replay. Prefer source isolation. Where combined monitor/system capture cannot exclude preview audio, suspend local preview playback or warn and offer to pause capture; do not silently record its sound twice.

### Editing and synchronization

Use a shared timestamp domain and bounded audio queues; handle silence, clock drift, and disconnects without accumulating packets indefinitely. Audio-device changes must be explicit and logged. Do not silently substitute a newly connected microphone.

Store the original as MKV initially for multi-track handling. Include a mixed track for normal playback and named source tracks where available. The exported Discord MP4 has one stereo AAC mix generated from the user's mute and gain settings. Discord sharing must not depend on the viewer selecting among several audio tracks.

Use an explicit track map in clip metadata: combined, game, microphone, Discord, or system fallback, recording which tracks actually exist. Never infer a source from track index alone after a runtime/configuration change. If a previously enabled source disappears, notify the user and preserve the remaining sources with an honest missing-source label.

Version 1 uses simple source gain and mute controls, not a timeline DAW. Original playback may use the mixed track. Provide an explicit Preview Audio Changes action if needed: render a temporary selected mix while copying compatible video, then audition it. Do not run an encoder on every slider movement. Keep one bounded preview cache and share the single heavy-job slot with exports. A compatible MP4 preview proxy can be created on demand if Windows playback cannot open the original MKV; do not assume native playback supports every container/codec.

Changing gains must not clip excessively. Use safe default unity gain, conservative output limiting for summed audio, and an optional reset. Automated tone tests and a real game/voice test must check A/V drift, track labeling, mute behavior, and duplicate audio.

## Trimming and Discord export

### Non destructive edits

Store trim points and mix choices as metadata. Never cut or recompress the only original file in place. A changed trim range invalidates the ready share artifact. Validate start >= 0, end > start, and end <= original duration within timestamp tolerance. Preserve the user's selection and never shorten it silently to meet a size limit.

The final compressed export must honor trim boundaries accurately rather than merely copying from the nearest keyframe. Preview seeking may be approximate; the export must not silently include excluded content. Strip unnecessary metadata such as absolute source paths from shared files.

### Size presets

Offer editable size presets and a custom target. Start with 20 MB as the dated Discord Free preset, 50 MB, 100 MB, and Custom. These are target sizes, not a claim about every account or webhook. Discord currently documents a 20 MB free limit as of August 2026, but limits change; verify labels when shipping and retain custom values. A webhook's allowance must not be inferred from the sender's personal Nitro plan. [Discord attachment FAQ](https://support.discord.com/hc/en-us/articles/25444343291031-File-Attachments-FAQ)

Define MB in the UI as 1,000,000 bytes; use MiB only for memory budgets. Maintain an exact byte cap internally. Leave a margin of at least the larger of 5 percent of the cap or 256 KiB.

Use an internal default video-rate floor near 500 kbps and an audio floor near 64 kbps for the first algorithm, subject to visual testing. For a smaller bitrate budget, suggest lower resolution and shorter owner-selected trim; do not loop forever or silently degrade below the configured floor. Include container overhead in the margin and verify bytes after writing fast-start metadata.

### Export algorithm

1. Probe the input and apply selected trim/audio settings. Compute actual selected duration from media timestamps.
2. If an existing compatible share copy matches the source revision, trim, mix, codec, profile version, and size target, revalidate and reuse it. Otherwise create a new artifact.
3. For an untouched compatible single-mix MP4 already below the target, copying or lossless remuxing is allowed. Mixed-track MKV originals and edited clips still need the correct share representation.
4. Choose H.264 video, stereo AAC audio, MP4, compatible pixel format, and fast-start metadata. Default export is SDR. Validate HDR-to-SDR handling before supporting HDR input; never produce an unexplained washed-out export.
5. Calculate a bitrate budget from available bytes and duration, subtracting audio and margin. Start with source resolution up to 1080p; reduce to 720p or lower when the budget makes higher resolution visibly poor. Tell the user the resulting resolution.
6. Encode through the hardware path where it produces acceptable output and respects the size budget. For quality at small targets, provide a slower software two-pass option invoked only by explicit user choice. Restrict software thread use so an export does not monopolize the PC.
7. Measure output bytes, probe streams and duration, and fully decode short exports for corruption checking before Ready. Check for expected audio streams even when the selected mix is silent.
8. If oversized, reduce the bitrate using measured overshoot and retry a bounded number of times, initially two additional attempts. Never label an oversized artifact Ready or Uploadable.
9. If the desired clip cannot fit at the minimum allowed quality after those attempts, offer a larger target or user-controlled shorter trim. Explain the constraint rather than truncating or reporting success.
10. Atomically promote the verified temporary artifact to a unique final share file. Preserve the original and previous completed exports unless the user explicitly removes them.

Use FFmpeg/FFprobe with pinned versions and array arguments. Parse bounded progress/error output; do not call through an interpolated shell command. [FFmpeg command documentation](https://ffmpeg.org/ffmpeg.html)

### Local sharing

Implement native file drag-out and clipboard file-list copy of the completed MP4, not a URL, thumbnail, text path, or promise to create a file later. The file must exist and remain available throughout the drop/paste. Keep Open Folder as a fallback if a Discord build does not accept clipboard file lists. Test against the actual Discord desktop client, including normal-user elevation compatibility. [WPF drag and drop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)

The user still presses Send in Discord for local sharing. Do not simulate keyboard input, click channels, intercept Discord storage, or access its session credentials. Local export and drag/copy continue to work even if the optional integration is unavailable.

### Export job safety

One active heavy job and at most two pending user-requested jobs. A preview request never creates a hidden infinite queue. Cancellation stops only the job's owned child process and removes only known incomplete app artifacts. Cancellation, encoder failure, or an oversized result cannot modify the original or masquerade as a completed export. Measure memory and latency while replay capture and one export overlap.

## Optional direct Discord posting

### Supported approach

Use incoming webhooks for an explicitly configured server text channel. The owner or a server administrator obtains the webhook through Discord; creating it requires the relevant permission. It posts under a webhook identity, not as the owner's normal user account. This does not provide arbitrary channel access or DMs. [Discord webhook API](https://docs.discord.com/developers/resources/webhook)

Do not scrape or request a Discord user token, automate a normal user account, or introduce a bot service. OAuth and a browsable channel picker are out of scope. Support ordinary text channels first; forum/media channels and thread creation are later features.

### User flow

After the local export is Ready, Send to Channel shows a named destination, clip thumbnail, size, and optional message. The user explicitly confirms Send. Configure one destination initially, with edit, test connection, and remove actions. A connection test must not post an unsolicited message; ask for a deliberate test send if testing an actual post.

Store the webhook secret in Windows Credential Manager or a user-scoped DPAPI-protected store. Keep it out of normal settings, clipboard history generated by the app, logs, repository, screenshots, diagnostic archives, crash reports, and share metadata. Mask it after entry. Validate an exact supported Discord HTTPS hostname and expected API path; do not permit arbitrary URLs or follow redirects that could disclose the secret. Use current-user protection, not the machine-wide DPAPI flag; OS protection does not defend against malware already running as the same user. [Windows DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)

Send the same validated MP4 using streamed multipart upload with wait=true, explicit empty allowed-mentions parsing, bounded request timeout, and cancellation. Report success only after a confirmed response; retain the returned message identifier locally to provide a receipt where supported.

Respect Discord's Retry-After/retry_after and rate-limit response headers; do not hardcode a universal requests-per-second rule. Treat authentication, permission, missing-webhook, and oversized-file errors as actionable, not reasons to retry endlessly. [Discord rate limits](https://docs.discord.com/developers/topics/rate-limits)

An ambiguous timeout after sending could mean a message was already posted. Do not blindly retry and create duplicates; show an uncertain-delivery state and require a deliberate retry after the user checks the channel. Do not assume the endpoint offers general idempotency guarantees.

If direct posting is rejected, retain the ready local file and offer drag/copy. Compression targets do not bypass server permissions or upload caps. No credentials or webhook are needed to use the core.

## Resource budgets and regression rules

The following are engineering targets and initial safety thresholds, not measured results or promises of zero overhead. Record actual private bytes, working set, GPU allocation where available, CPU, and child counts separately. Task Manager physical RAM and committed memory are not interchangeable.

| Condition | Initial target or rule |
| --- | --- |
| Off with gallery closed | <= 150 MiB combined app-owned private bytes; no capture host or encoder |
| 1080p30 default capture, gallery closed | <= 600 MiB combined app-owned private bytes after warm-up |
| Capture packet buffer | 256 MiB budget with observed keyframe/save overhead recorded |
| Active native capture host | Warn if sustained > 768 MiB private bytes; stop safely if sustained > 1 GiB |
| Capture plus one export | Target <= 1.5 GiB combined app-owned private bytes; bounded export safety limit established by tests |
| CPU during capture | Target < 3 percent total-machine CPU averaged over a representative interval |
| Game performance | Target <= 5 percent average-FPS loss and <= 10 percent p95 frame-time increase in controlled tests |
| Save latency | Target <= 2 seconds to a usable local saved replay on the owner's SSD |
| Export latency | Target <= 10 seconds for a typical 30-second hardware-encoded share copy; slower quality mode may take longer |
| Inactive worker count after stop | Zero capture hosts and zero lingering capture/mux helpers within five seconds after finalization |

The maximum-time allowance for a user-requested finalization may be longer than the normal worker cleanup target; expose Finishing Save, bound it, and record any timeout. A finalization delay does not justify an immortal worker.

Use a short sampling window rather than responding to a one-sample spike. If Windows available physical memory is sustained below approximately 1 GiB, do not start a new export and offer/pause replay safely, with a clear notification. Log the action without dumping unrelated process command lines. No forced WSL shutdown, arbitrary application termination, pagefile edits, or automatic system restarts.

Start with approximately 15 seconds for sustained private-memory warnings and 10 seconds for low-system-memory detection; expose these as internal constants, not a complex public tuning panel. Establish a separate owned-export fail-safe ceiling, initially approximately 1 GiB private bytes for its process tree, through fixture tests. Fail an outlier export cleanly rather than allowing unlimited memory growth. Record app UI, capture host, mux helper, export, and validation probes in aggregate measurements; use bounded probe concurrency, initially two.

If a safety stop loses the unsaved buffer, say so. Memory policing is a fail-safe, not a substitute for fixing a leak. Once a guard trips, leave the app paused until manual acknowledgement instead of generating repeated restarts.

For endurance tests, record five-second resource samples. After warm-up, the final 30-minute median private-byte usage must not exceed the comparable first 30-minute median by more than 100 MiB without an explained bounded cause. There must be no persistent worker-count, handle-count, or memory slope across repeated start/stop and target-change cycles. A budget increase needs a recorded justification; never raise a cap silently until the test passes.

## Storage and recovery

### Folder layout

Default metadata location: %LOCALAPPDATA%\LocalClip. Default media location: the user's Windows Videos known folder under LocalClip, overridable before capture. Do not hardcode C: or D: as the only media drive and do not assume Documents/Videos are unredirected.

Use Originals, Exports, and a clearly app-owned bounded Temp directory under the selected media root where appropriate. Put settings.json, the small clip index, thumbnail cache, and rotating logs in app data. Original captures and final exports must never be treated as disposable caches.

Use atomic JSON settings with a schema version and a small SQLite clip index if gallery persistence/search warrants it. SQLite is an index, not a second source of truth for video bytes. Provide a targeted rescan of the selected media root to recover orphaned originals after a metadata failure; do not scan the whole machine. Do not add an ORM or migration framework just for a handful of tables.

### Clip metadata

Record clip ID, app-managed relative file reference, creation time, actual duration, dimensions/FPS, audio-track descriptions, capture target label, trim/mix choices, and export revision/profile. Media probing verifies facts; do not populate successful dimensions or audio labels from assumptions. Avoid storing window titles beyond what is necessary; diagnostics must redact personal paths and titles by default.

### Disk pressure and external drives

Before saving/exporting, check the destination is available, writable, and has room for the estimated file plus a reserve, initially 2 GiB. For app-caused disk pressure, stop new jobs and show an actionable storage warning. Never delete files automatically to continue capturing. The external HDD may be chosen by the owner, but a disconnect must fail visibly and must not silently relocate clips to the SSD.

Default cleanup only removes known app-owned incomplete temporary files and bounded cache entries, not originals or completed exports. Cap thumbnail cache at approximately 200 MiB and total preview/temp artifacts at approximately 1 GiB, excluding a currently active bounded job. Rotate logs, initially five 5 MiB files. Do not keep per-frame logs.

Original deletion is explicit, preferably recoverable through the Windows Recycle Bin. Explain when a chosen drive cannot support recoverable deletion. Do not follow directory junctions/symlinks outside managed cache roots during cleanup. Uninstall preserves recordings, exports, and user settings unless the user explicitly chooses their removal.

Changing the media folder does not move or delete the old library automatically. Keep existing clip references valid and tell the user which location receives new recordings. Treat missing or externally edited files as recoverable library states, not reasons to delete index entries or unrelated paths silently. Offer an explicit repair/rescan.

### Crash and completion handling

Write outputs to unique temporary names, validate, then atomically rename on the same volume where possible. A crash before rename leaves an incomplete artifact, not a gallery clip falsely marked successful. Record completed originals in the index only after validation. On startup reconcile known pending jobs and valid unindexed media. Never upload or resume a pending post automatically after a restart.

Keep accepted originals immutable. Save edit metadata separately. Unsaved rolling footage is volatile by design; already saved footage should remain usable outside this application.

## Privacy and security

- No network access is needed for the core. No telemetry, analytics, account SDK, advertising framework, or automatic cloud uploads.
- No administrator requirement for ordinary capture/edit/share. Do not bypass secure-desktop or protected-content restrictions.
- No game-process injection in the initial capture path, kernel driver, anti-cheat bypass, or hidden desktop fallback.
- Allow microphone permission and source choice explicitly. Stop microphone acquisition when capture stops.
- Use explicit safe subprocess argument arrays, bounded logs/messages, cancellation, and allowlisted owned executable paths.
- Accept local media paths only in the core. Reject URLs supplied as media paths and constrain FFmpeg protocols to the required local file/pipe access; no incidental remote media fetching or listening sockets.
- Media decode/export errors must fail safely. Pin supported patched dependencies and include their provenance.
- Do not capture private secrets into diagnostic archives. Diagnostics are generated locally, shown to the user, and shared only on request.
- Optional webhook credentials use an OS-protected store and never enter the source repository or normal JSON configuration.
- Capturing Discord voices may involve other participants' privacy; include a concise reminder and obey the user's source choices.

A bounded app can still encounter Windows, GPU-driver, audio-driver, or power failures. Process isolation and resource guards reduce app-caused harm; they do not guarantee system uptime or that a driver allocation is included in the application's private-byte total.

## Dependency and distribution policy

Use current supported stable versions, pinned in the repository, with hashes/provenance for downloaded binaries. Verify release provenance/signatures when available. Do not ask users to disable SmartScreen, antivirus, driver security, or Windows protections to install the app.

Choose a GPL-compatible open-source license for the integrated application, provisionally GPL-2.0-or-later, subject to checking the exact pinned dependencies. OBS/FFmpeg code and build options carry obligations; free binaries are not a substitute for the appropriate source, notices, and build materials. Do not redistribute an FFmpeg build with nonfree components. Audit hardware SDK redistributables and transitive module licenses before public release. [OBS license text](https://github.com/obsproject/obs-studio/blob/master/COPYING), [FFmpeg licensing](https://ffmpeg.org/legal.html)

Provide a Windows x64 portable ZIP first and a straightforward per-user installer after validation. The .NET runtime and allowed native dependencies may be bundled for a self-contained experience. An installer does not enable capture or Windows startup without consent. Do not publish a build that depends on developer machine absolute paths.

Downloads and all implemented features remain free. No paid signing certificate or hosting subscription is a mandatory prerequisite to personal use. Public unsigned builds can trigger reputation warnings; document that honestly, publish SHA-256 checksums and source, and never claim those are equivalent to trusted code signing. Ask before introducing any maintenance expense or publicly releasing unsigned software. Use an owner-approved free distribution destination where available; do not promise indefinite third-party hosting terms.

No automatic updater is required for the first personal build. The public build may have an explicitly invoked Check for Updates that opens the official release page, without a persistent updater service or silent replacement of native components.

Include a small capability/compatibility report with the installed build: supported OS, capture backend, active encoder, separated or fallback audio mode, HDR status, dependency versions, and owner-tested games/targets. Clearly distinguish tested support from an engine's theoretical capabilities.

## Implementation organization

Keep one repository and a small solution:

```text
LocalClip/
  src/
    LocalClip.App/             Windows shell and coordinators
    LocalClip.Core/            Settings, clip/export models and pure rules
    LocalClip.CaptureHost/     Small C++ libobs integration
  tests/
    LocalClip.Core.Tests/      Deterministic unit and workflow tests
    integration/              Host lifecycle, media and IPC checks
  tools/                      Pinned dependency setup, build and packaging
  docs/
    ARCHITECTURE.md
    BUILD.md
    TEST-RESULTS.md
    IMPLEMENTATION-STATE.md
    USER-GUIDE.md
    PRIVACY.md
  LICENSE
  THIRD-PARTY-NOTICES.md
  dependencies.lock.json
```

Use a small amount of MVVM where it helps WPF UI testing. Do not create microservices, CQRS, event sourcing, a plugin framework, a custom dependency-injection container, separate backend/frontend repositories, or a general IPC framework. Runtime isolation and media validation are justified reliability work, not reasons to grow the product scope.

Settings cover target choice, capture profile, replay budget, shortcuts, source audio, media folder, share-size presets, startup preference, and optional destination label. Secrets are separate. Provide Restore Defaults without deleting media.

## Delivery milestones and effort

Estimates are planning ranges in engineering hours, not guaranteed wall-clock or token counts. They assume one initial Windows/NVIDIA machine and reuse of the capture engine. Public support across arbitrary hardware is a different level of work.

| Milestone | Deliverable and gate | Cumulative approximate effort |
| --- | --- | --- |
| M0 | Clean app workspace, pinned dependency/license inventory, capture integration investigation | 6 to 12 hours |
| M1 | Real target/audio capture, replay save, bounded worker cleanup, initial resource trace | 20 to 40 hours |
| M2 | Usable local app with gallery, trim/mix, validated Discord export, file drag/copy | 80 to 160 hours |
| M3 | Optional webhook UI, protected secrets, failure handling, mock tests, authorized live post | Approximately 12 to 24 additional hours |
| M4 | Hardened free public Windows release, endurance/compatibility evidence and packaging | 200 to 400 hours total, depending on issues |

Do not spend the first milestone building a polished gallery or release website while capture is unproven. Do not describe M1 as production-ready. The core may be usable before the public release is ready; report those states separately.

At each milestone update durable implementation state with the exact commit/build, completed tests, real measurements, unresolved defects, and next safe action. Keep a reproducible working build. Commit in coherent increments only inside this project; public pushes/releases require owner authorization.

## Acceptance tests

### Automated core tests

- Trim validation, custom size conversion and MB/MiB distinction, bitrate budgeting, export fingerprint invalidation, bounded retry, queue limits, and cancellation.
- Test export caps around small and large byte boundaries; an oversized output can never return Ready.
- Atomic settings/index recovery, stale IPC responses, process ownership, and recovery after an interrupted save/export.
- Unicode/space-containing paths, unusual filenames, long supported paths, unavailable drive, permission error, and disk-full simulation inside a dedicated test directory.
- Audio-source mapping and mix generation, including zero-volume tracks, combined fallback, and no duplicate system/process mixing.
- Webhook secret redaction, URL validation, no mentions, 429 handling, 401/403/404/413 failures, cancellation, and ambiguous delivery. Mock tests make no real posts.

### Real media integration tests

- Capture a real selected window and a selected monitor using the actual hardware encoder.
- Save a full 30-second replay, a short warm-up replay, and repeated hotkey clips; every completed original opens outside the app.
- Use distinct known sounds in game, microphone, Discord, and an unrelated music source. Verify separated tracks, fallback labeling, mute/gain, and absence of duplicated sound.
- Produce short synthetic fixtures and real clips with high motion and silence. Verify final output size, selected duration, compatible H.264/AAC MP4, full decode, and original preservation.
- Validate audio/video timing to a target tolerance of 100 ms over a 30-second clip, measuring drift rather than trusting playback impressions alone.
- Check exported trim boundaries with marked-frame test input, allowing approximately one output frame of tolerance where the engine supports it.
- Test local drag into Discord, Copy File/paste where accepted, and Open Folder fallback. Confirm no watermark, unwanted audio track, private metadata, or nonfunctional embed.

### Lifecycle and failure tests

- At least 50 manual start/stop cycles and 20 target switches; worker count returns to baseline and memory is stable after warm-up.
- Save during Stop, press Save repeatedly, cancel export, close/reopen UI, run a second app instance, and quit with a pending job.
- Kill only a dedicated test capture host, force a parent test crash, and kill a dedicated export process. Verify no orphan helpers and no original corruption.
- Test target closure, resolution changes, alt-tab, selected-monitor disconnect, microphone disconnect, audio-device changes, lock/unlock, and sleep/resume. No silent whole-desktop fallback or automatic capture restart.
- Restart Windows only with owner permission and no unrelated active work. App startup remains disarmed; completed media remains intact. Do not reboot as part of an unattended test suite.
- Disconnect an external test drive only with owner permission, or simulate the failure safely. Preserve unknown contents and report the failed destination.

### Resource and compatibility tests

- Run a two-hour personal capture soak and an eight-hour release soak. Include clip saves and selected target changes; record private bytes, working set, handle counts, owned child count, CPU and available OS memory every five seconds.
- Compare equivalent repeated gaming scenarios with capture off/on, controlling other recording tools with owner cooperation. Record average FPS and frame-time percentiles, not just a single Task Manager screenshot.
- Measure capture plus one export and idle after Stop. State whether budgets passed; a failed measurement is a defect or justified decision, not a number to omit.
- Validate HDR-to-SDR explicitly before claiming HDR support. Test one fast-paced game on the owner's PC; use additional GPUs/games before claiming broader compatibility.
- Test the public package on a clean Windows installation or suitable Windows VM for installation/offline UI, plus real GPU hardware for capture. A VM without a usable hardware encoder does not validate gaming capture.

## Definition of done and required evidence

The local core is complete when the owner can manually arm replay, save a real clip using the hotkey, trim and choose audio, obtain a verified watermark-free Discord-sized MP4, and drag/paste it into Discord, with stable resource use and no orphan workers after Stop/Quit. A mock capture implementation or UI-only build does not satisfy this.

Deliver the source, runnable packaged build, dependency/build instructions, user guide, limitations, and test report. The report includes exact versions, a command/test inventory, pass/fail results, memory/CPU/latency measurements, representative media checks, and owner-facing manual test instructions. Do not include private recordings or credentials in a public test artifact.

Optional direct posting is complete only after its own tests and an owner-authorized real post; otherwise explicitly mark live validation pending. Public readiness additionally requires dependency/license review, the release soak, clean-install checks, source/checksum artifacts, and owner publication approval.

Do not report completion if full-duration capture silently fails, audio separation is merely simulated, exports exceed their cap, originals are overwritten, workers accumulate, or observed RAM growth is unbounded. Unsupported capture targets and combined-audio fallback are acceptable only if visibly labeled and documented, not disguised as success.

## Implementation agent instructions

Own the implementation end to end within this scope. Begin by reading this entire blueprint and checking the dedicated workspace instructions. Inventory the actual development tools, approved capture runtime, and hardware capabilities. Create a short implementation checklist and deliver M0/M1 before the complete gallery.

Use measured evidence to choose supported engine settings. Keep the product small and native. Preserve the manual-only default, offline core, original recordings, and free/no-watermark policy. Do not introduce automatic game detection to simplify audio selection; manual target/source choice is intentional.

Resolve routine design choices yourself. Ask the owner only for actual permissions, hardware interaction, external credentials/destinations, publication, required audio fallback acceptance when it changes the promised behavior, or a material architecture/budget deviation. Never fabricate test results or assume you can post to a channel because a webhook-looking string exists.

Continue through safe in-scope milestones without repeatedly asking whether to proceed. When blocked, report the exact failed check, what remains safe to do, and the smallest owner action needed. Leave a durable state file so another agent can resume without the original chat. Do not confuse code completion with hardware validation or public release approval.

## Final planning decisions

- Platform: Windows x64, owner PC first.
- Price: free download and use, all features free.
- Activation: manually arm and save through separate configurable shortcuts.
- Core capture: one libobs host with non-injecting window/monitor capture initially; no automatic game detection.
- Audio: real separated sources where supported, honest system-audio fallback otherwise.
- Sharing: local verified MP4 first; optional supported channel webhook afterward.
- UI: small native app with an inbuilt clip editor/share panel, not an external compressor workflow.
- Storage: owner-selected local folders, immutable originals, bounded disposable caches, no automatic original deletion.
- Reliability: bounded queues, one capture worker, process ownership, lifecycle tests, resource measurement, and visible fault handling.
- Distribution: free Windows package/source after validation and owner-approved publication; no backend service.

There are no unresolved product-scope questions that block implementation. The working name, exact stable dependency versions, verified encoder/source availability, live webhook destination, and eventual publication details are implementation/release checks, not reasons to postpone the prototype.
