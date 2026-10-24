# Share research and proposed delivery slices — October 24, 2026

Phase 0 research only. No application code, settings, branches, workflows or GitHub
resources were changed. The recommendations below require the lead's published
plan/approval before implementation. Scope is **Share only**; retain the library,
player, trim/audio tools, capture controls, original logo and purple identity.

## Verified Flick baseline

Inspected `main` at `46c3ed50cdc36049c2359b4817a0a94b9882270b`. Share is the inline
`SharePanel` in `MainWindow.xaml`, not a separate ShareWindow. Its bottom-right
context drawer is capped at 600 DIPs. The default view is a small form: title,
size cap, Export Clip, one status line, and optional Discord posting. After
readiness, source adds a small drag row showing a path, Copy File and Open Folder.
There is no Share thumbnail, selected-clip title or selected-duration summary.
The current progress bar is indeterminate even though the VM exposes staged
`ExportProgress`. Long errors are ellipsized in the default layout.

Downloaded the latest supplied 1.0.6 render artifact through the GitHub connector:

- [Run 40084437461](https://github.com/Krish-Verma/Flick/actions/runs/40084437461),
  artifact `12556043044`, created `2026-10-24T10:04:50Z`.
- Verified ZIP SHA-256:
  `0a77fb4f371ff6af5e94c29d9e8feb81fcb74dd08831d3b9ef4752c440f3ac35`.
- Run head `d63043519eb7348d32efd129835cad9c2bb26fe7`; rendered merge SHA from
  PROVENANCE.txt `0a6fa3f6e366f53c39504ae35be9e3dc05e13a65`.
  GitHub Git-commit reads confirm **both have tree
  `74f054ae3a3a69b51e8dd147ccd3cead8badce5e`**. They are different commits with
  identical source trees, not an unexplained screenshot/source mismatch.
- Source comparison from run head to current main shows no differences in the
  app, theme, renderer, ReadyShareFile or media code.
- Local evidence: `artifacts/research/baseline/`, including PROVENANCE.txt,
  the ZIP, screenshots and research-provenance.json.
- Visually reviewed `04-share.png` (1240×720), `14-share-compact.png` (800×600)
  and `16-share-expanded-compact.png` (800×600). Also retained Custom-cap view15.

These are actual compiled WPF **client-area** renders with synthetic clip names,
thumbnail placeholders and monitor. They do not show a decoded video, a Ready
export, an error, a native title bar, an actual drag/paste, or an export run.
The current renderer never starts capture or uploads media. Rasterized DPI
samples are not physical per-monitor-DPI acceptance.

### Existing behavior to preserve

`ClipExporter` creates H.264/AAC MP4 under the chosen byte cap, probes duration
and streams, runs full decoding, then promotes a temporary output. The VM's
`TryGetReadyShareFile` and `ReadyShareFile` provide a last-mile guard matching
current clip/edits/cap, destination, naming, file existence and stored size.
That guard is **not a new decode/content-hash certification at every drag**.

`CopyExportedFileToClipboard` uses `Clipboard.SetFileDropList`; native drag uses
`DataObject(DataFormats.FileDrop, paths)` and `DragDropEffects.Copy`, after the
Windows pointer-movement threshold and a fresh readiness check. Opening Share
only navigates; it never exports or posts. Optional webhook posting requires
separate destination confirmation. Keep these boundaries.

## Current primary references

Sources checked October 24, 2026. Official reference screenshots below are linked
at their original URLs, not recreated, recolored or represented as Flick builds.
Documentation was readable; the web adapter did not expose image pixels, and an
in-app browser was unavailable. The third-party screenshot assets were located
but no live Windows competitor session or pixel-accurate competitor audit was
performed. The Flick baseline images above **were** directly visually inspected.

### Steam Game Recording

[Steam's recording FAQ](https://help.steampowered.com/en/faqs/view/23B7-49AD-4A28-9590)
separates saving inside Steam, exporting an MP4, copying to clipboard, device
transfer and a hosted share link. Its share-link description specifies two-day
expiry. Steam also explains that recordings/timeline storage is a raw format
and that an exported video is a separate clip output.

The documentation's broad clipboard language does not identify its Windows
clipboard formats or certify paste behavior in any particular Discord build.
Do not infer Steam's implementation is CF_HDROP, an uploaded link, or a universal
clipboard video format. [Official product page](https://store.steampowered.com/sale/gamerecording)
was available only as a limited extracted page in this environment.

Design inference: distinguish preparation, a locally usable file and remote
sharing destinations; keep the clip's identity alongside its sharing actions.

### SteelSeries Moments

The official [How to Record Gameplay on PC guide](https://steelseries.com/tr-tr/blog/how-to-record-gameplay-on-pc-881)
(December 19, 2022) shows a Share Clip sequence: choose a platform, choose a size,
wait for preparation, then drag the prepared clip into a Discord channel. This
is useful evidence for a visible, draggable finished-clip object. Its historical
8/50/100 MB discussion must not become current destination-limit copy.

Genuine publisher screenshots linked from that guide:

- [Destination selection](https://media.steelseriescdn.com/blog/posts/how-to-record-gameplay-on-pc-easy-free/moments-share-6a_77dd24ce3049408381d3509df0d00d4d.png).
- [Preparation/size step](https://media.steelseriescdn.com/blog/posts/how-to-record-gameplay-on-pc-easy-free/moments-share-6b_710d6d7c1183411782905abf79744ef5.png).
- [Ready clip/Discord sharing step](https://media.steelseriescdn.com/blog/posts/how-to-record-gameplay-on-pc-easy-free/moments-share-6c_3e2966890f234cf89d8df0744095caea.png).

The [current Moments product page](https://steelseries.com/discord) still describes
manual export and drag-and-drop sharing. The old tutorial is reference evidence,
not proof of today's exact installed app appearance or performance.

### Medal

The official [sharing guide](https://support.medal.tv/support/solutions/articles/48001169792)
(updated September 17, 2025) starts from a preview and a Post/Copy Link menu,
with destination choices and a favorite/default sharing option. Its
[genuine screenshot](https://s3.amazonaws.com/cdn.freshdesk.com/data/helpdesk/attachments/production/48389122635/original/Ao5y3uCLXJKpqYO5QT0VtUmM0cZD2voSpQ.png?1758143457=)
is embedded in that article. The current [expiring-links guide](https://support.medal.tv/support/solutions/articles/48001259493)
describes uploaded temporary links while retaining the video locally.

Design inference: clear clip context and the next action help; a cloud Post or
Copy Link workflow is a different product contract from Flick's local Copy File.
Do not introduce a hosted link, account, auto-upload or Medal-like social flow.

### Windows file data and Discord

Microsoft documents [SetFileDropList](https://learn.microsoft.com/en-us/dotnet/api/system.windows.clipboard.setfiledroplist?view=windowsdesktop-10.0)
as putting file-drop data on the clipboard and
[DataFormats.FileDrop](https://learn.microsoft.com/en-us/dotnet/api/system.windows.dataformats.filedrop?view=windowsdesktop-10.0)
as the Windows shell file-drag format. Flick copies a local file reference, not
a text URL or universal encoded-video clipboard object. The file must remain
available and the destination must support that data format.

Discord's [current attachment FAQ](https://support.discord.com/hc/en-us/articles/25444343291031-File-Attachments-FAQ)
(updated September 30, 2026) supports MP4 attachments, gives a 20 MB non-Nitro
limit and explicitly notes ongoing limit experiments. It does not establish
Flick-to-Discord Ctrl+V acceptance. Preserve configurable caps and destination
caveats; require actual Windows desktop and browser attachment tests. A completed
OS drag or clipboard operation cannot prove Discord uploaded/sent anything.

## Proposed Share experience

Operate mode: a player has selected/trimmed a clip and wants to send a useful
file with minimal interruption. The central object is **the clip being prepared
and then the finished MP4**, not a form or a filesystem path.

- Brighter Share-local graphite layers with restrained lavender action/focus;
  retain the purple/synthwave family. Reuse existing type/icon vocabulary and
  soft dividers. No global palette replacement, blur, decorative gradients,
  copied competitor artwork, new account or new cloud workflow.
- Put thumbnail and title at the top of the content hierarchy. Reuse the bounded
  local thumbnail; show a quiet unavailable-preview fallback. Support landscape,
  portrait and wide media without stretching. Title can wrap to two lines;
  full path belongs in tooltip/details, with Open Folder as the practical escape.
- Show selected duration, MP4 format and output readiness. Before export, show
  the chosen **maximum** size; actual output bytes appear only after validation.
  Never substitute original recording size or an invented output estimate.
- Make one large, visibly grabbable tile the stable focal object. Before Ready it
  previews the selected clip but is not draggable. After Ready the same object
  presents the prepared MP4, actual size, duration and explicit drag hint, plus
  prominent Copy File and secondary Open Folder. Clicking a thumbnail must not
  silently post or export. Retain explicit preparation action.
- Keep optional webhook posting collapsed and separate from file delivery.
  Copy acknowledgement means copied; drag completion means handed to the OS.
  Neither is labelled Uploaded, Sent or delivered to Discord.

Keep the inline editor drawer initially. At the supported minimum workspace,
use a compact horizontal identity tile and stacked controls; at larger sizes
allow more thumbnail space. Measure a Share-only height budget inside the preview
allocation, preserving transport and the four bottom tools outside it. The
current shared `PreviewSurface` minus100-DIP scroll budget will need a scoped
Share treatment; do not expand Trim/Audio or simply add a tall nested panel.
Use a fixed header/close and accessible action area with one bounded scroll body
when content overflows. Avoid a new modal/window unless real layout evidence
shows the existing architecture cannot support the task.

## Five implementation slices, after plan approval

| Slice | Deliverable | Acceptance |
| --- | --- | --- |
| E1 — State and metadata contract | Explicit no-selection/offline/needs-export/exporting/cancelling/ready/stale/error states; clip title, thumbnail, selected duration and actual validated size; readiness derived from existing rules | Changing clip, trim, gain, cap or destination cannot leave an old tile shareable; source-only readiness tests do not masquerade as decoder tests |
| E2 — Share composition | Share-local graphite/lavender styling, identity/thumbnail tile, clear cap and preparation action; unchanged other surfaces | Actual WPF renders at 1240×720,1440×900 and800×600; no duplicate headings/path dominance or transport clipping; new styles have high-contrast fallbacks |
| E3 — File handoff | Large Ready tile retaining native movement threshold, fresh last-mile guard, Copy File and Open Folder; meaningful keyboard alternative | STA test observes FileDrop containing the expected real MP4 path; no UnicodeText-link substitution; no export/post on open; missing/stale file refuses handoff; clipboard contention reports retryable failure |
| E4 — Recovery and accessibility | Readable progress/stage/cancel, errors with recovery, unavailable thumbnail/source, long Unicode title/path, custom-cap validation, focus/HC/compact rules | Keyboard reaches every action, visible focus, screen-reader name/help and restrained status announcements; disabled state explains why; cancel and retry preserve source bytes |
| E5 — Visual/media/manual acceptance | Real state render fixtures and real generated-media export/copy evidence; manual Windows target matrix | Distinguish synthetic WPF state renders, actual FFmpeg validation, OS clipboard tests and physical Discord acceptance; publish no claim of upload or hardware performance without matching evidence |

No slice changes capture defaults, recording performance, updater, signing,
licensing, logo artwork, library architecture or Trim/Audio behavior.

## Verification and performance matrix

Automated evidence should include needs-export; running/cancelling; valid Ready
with real generated MP4; missing/corrupt/stale artifact; failure with retry; custom
250 MB; optional-posting expansion; no selection/offline; missing thumbnail;
long Unicode title/path; portrait/wide thumbnail; supported compact layout;
keyboard focus and high contrast. Do not set a Ready flag over fake video bytes
and call the resulting render proof of a validated export.

Use actual production export of generated ≤8-second/720p media, independent
ffprobe/full decode, cap checks and immutable-original hashes. For UI snapshots,
real bound controls can display labelled fixtures; document which background
operations were not run. WPF offscreen rendering cannot establish native drag,
clipboard acceptance, actual MediaElement decoding, Windows title-bar behavior,
physical DPI switching or gameplay cost.

On owner Windows, test FileDrop copy/paste and native drag to (a) Discord desktop,
(b) supported Discord browser, and (c) Explorer as an OS handoff control. Confirm
an attachment preview, correct file/duration/audio and expected limit handling;
stop before sending to another person unless separately authorized. Test cancelled
external drops and a file removed between readiness and handoff. Record exact
Windows/Discord/browser versions; do not extrapolate a desktop pass to a browser.

Measure Share closed/open/ready/repeated open-close memory, private bytes,
handles, CPU and responsiveness against 1.0.6 with the same clip and viewport.
The tile should reuse a bounded thumbnail, never start a second video decoder,
spin an idle timer, or encode in response to opening the panel. Enforced memory
or gameplay-FPS promises require actual traces, not a successful XAML render.
Physical DPI 100/125/150/200 testing uses a supported logical viewport; an 800×600
DIP window at 200% is not an 800×600 physical display claim.

## Skill and assumptions

Applied Impeccable 4.7.0 at
`/var/folders/yh/r2x8n8jn18j4z8cv_g6_xjzm0000gn/T/flick-impeccable-iuezhz6c/.agents/skills/impeccable/SKILL.md`.
Ran its context command once in this worktree against MainWindow; read Shape and
Operate. It found no PRODUCT.md/DESIGN.md and did not recognize the existing WPF
visual implementation. The actual source/render baseline takes precedence over
that absence. As a research subagent without its own user interview, I used the
lead's explicit brief and preserved the incumbent identity/architecture. No new
product/design authority or UI implementation was written. The lead presents
this proposal before feature coding.
