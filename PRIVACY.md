# Flick privacy

Effective for the development source reviewed October 24, 2026. Approved-game automation described here is new development work, awaiting integrated Windows qualification; it is not in the published 1.0.6 download. Flick processes recordings
and library information on your Windows computer without requiring a Flick
account. The reviewed application has no analytics, advertising, automatic crash
upload or automatic media upload client.

## Capture and local storage

Manual capture starts disarmed. You choose a monitor/window and start the replay
buffer. Optional Games settings can explicitly approve local executable paths and enable automatic replay, including while Flick is in the tray. Disabled game detection performs no background game scans. Enabled detection reads bounded ordinary window/process metadata locally every three seconds; it does not inject into games. Ambiguous, minimized, elevated or unverifiable windows cannot start automatic recording. Microphone capture is off by default and can be enabled in Settings.
Active capture processes the selected visual source and system audio, plus
microphone audio when enabled. Saving replay writes a local MKV. Trimming,
audio selection and export create a separate file, preserving the original.
Connected/imported folders are scanned locally for supported clips and thumbnails.

| Default location | Contents |
| --- | --- |
| Windows Videos folder `/LocalClip/Originals` | Saved originals |
| Windows Videos folder `/LocalClip/Exports` | Prepared share files |
| `%LOCALAPPDATA%/LocalClip/settings.json` | Preferences, capture/source paths, optional protected webhook value |
| `%LOCALAPPDATA%/LocalClip/game-replay-state.json` | Approved executable paths, process IDs and creation times paused against automatic retry; no titles or scan history |
| `%LOCALAPPDATA%/LocalClip/clips_index.json` | Clip paths/titles, timestamps, dimensions and audio/trim/export state |
| `%LOCALAPPDATA%/LocalClip/connected_sources.json` | Linked folder paths and ignored imported entries |
| `%LOCALAPPDATA%/LocalClip/Thumbnails`, `Temp`, `PreviewCache` | Generated thumbnails, temporary work and requested previews |
| `%LOCALAPPDATA%/Flick/Updates` | Requested installer downloads |

Recording/export folders can be changed. Media and metadata are ordinary local
files, not encrypted by Flick. Optional webhook credentials are protected with
Windows DPAPI for the current user before storage; recordings and other library
data are not covered by that protection. Backup/sync tools you configure can
process these folders independently of Flick.

Game profiles are stored in settings.json. Automatic microphone capture requires both the normal Audio preference and explicit consent in that game profile. Session pause state is local, bounded and retained across Flick restarts until the corresponding game process has exited; it is not included in the allowlisted diagnostics journal/export. Exact executable approval identifies a location, not a cryptographic guarantee about code at that location.

## Requested network actions

- **Updates:** Settings actions contact GitHub's public Flick release API and
  permitted GitHub asset hosts. Requests expose ordinary network information,
  including IP address and an updater User-Agent. Version comparison happens
  locally; recordings, titles, library paths and webhook secrets are not sent.
  Windows certificate verification may consult revocation services under Windows
  policy. See [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
- **Optional Discord posting:** Disabled by default. After webhook configuration
  and send confirmation, Flick uploads the prepared MP4, filename and any message
  to the selected Discord channel. Discord/channel members control their copies
  under [Discord's policy](https://discord.com/privacy). Disabling posting does
  not delete earlier posts.
- **Copy, drag and links:** Clipboard/drag-and-drop hands the prepared file to
  Windows or another app, which controls later uploads. Opening a release/help
  link hands its URL to your browser.

The reviewed code has no background update polling or automatic cloud backup.
OBS/FFmpeg are local media workers. SignPath/Azure signing is a build/release
service; the installed app does not send user recordings there.

## Retention and removal

You control originals and exports in their chosen folders. Removing an imported
item updates the library without deleting its source. Default/silent uninstall
retains user data. Explicit profile-removal consent deletes allowlisted settings,
indexes and caches, preserving originals, exports, imported sources and unknown
files. The separate update-download folder is outside that deletion allowlist.

Diagnostics/errors can contain local paths, window information and media
metadata. They are not automatically sent to maintainers. Redact reports you
choose to publish and use generated examples instead of private recordings.
Never post webhook URLs. See [Security](SECURITY.md) for reporting concerns.
Update this policy whenever the application's data flows change.
