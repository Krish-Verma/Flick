# Share implementation and acceptance record

Workstream E, owner root. E1 primary research and old WPF screenshots remain in `docs/research/share-references-20261024.md`. Owner-authorized parallel delivery supersedes its earlier proposal-only wording; no new external upload or release is authorized by this implementation.

## Implemented

Share keeps the existing inline editor architecture and purple identity. A local lighter graphite layer and one clip tile replace the small path row. The tile shows the selected title, chosen duration, a bounded actual still image or quiet unavailable fallback, state and action hint. Actual MP4 size appears only for a Ready export; a chosen cap is labelled Maximum. Original-preview and prepared-MP4 text are distinct. Output paths stay out of the principal layout.

The whole tile retains the Windows pointer-movement threshold and reruns current export/file/fingerprint checks at handoff. Data is `DataFormats.FileDrop`, never a text link. Copy File remains a standard clipboard file-drop fallback; Ctrl+C works on the tile. A cancelled drag says cancelled; an OS handoff asks the user to check Discord, never claims an upload or send. Stale/missing files invalidate the tile on interaction. Optional webhook posting remains collapsed, explicit and separately confirmed.

Export/Copy/Open Folder and cancellation live outside the scrolling body. Caption/status errors wrap; real progress uses the VM's0–100 range. Empty, original-offline, needs-export, stale, exporting, cancelling, failed and Ready have distinct text and affordances. Cancellation is disabled after request while cleanup settles. Share-local accessibility colors use Windows brushes and update when High Contrast changes; no OS setting is changed. Shape selection integrates D's original/fit portrait/fit square export presets.

The still loader owns one serialized cancellable operation, accepts only a local absolute path, caps input at8MiB and decoded dimensions at480pixels, freezes output, releases file handles and rejects invalid/missing/oversized input. It starts no video decoder, polling timer, helper or upload. Switching/closing/capture/export cancels pending work; generation/selection identity prevents stale previews.

## Authored and locally checked

- App, App-test and renderer Mac crossbuilds pass with SDK10.0.402, zero warnings/errors. Windows execution is pending the combined batch; this is not an actual WPF runtime pass.
- An STA test exercises production file-data construction, real clipboard roundtrip, Unicode paths, missing-file refusal, bounded preview dimensions, handle release, corrupt/oversized images and cancellation. Its sentinel file tests transport format only; it is not represented as codec-valid video.
- Additional actual WPF fixtures create real synthetic H.264/AAC input, register it through storage, run the production MP4 exporter/full decode and then bind a genuine Ready export. Original hashes are checked. Renders cover Ready at1280×720/1920×1080/2560×1440/720×480, raster96/120/144/192DPI, accessible palette, empty/offline/stale/failure and simulated exporting/cancelling. Exact provenance distinguishes real media preparation from simulated visual states.

## Pending combined acceptance

Run the integrated Windows app/STA tests, native/media matrix, real WPF render and installer checks. Review actual pixels and fix geometry, contrast, clipping or affordance failures before acceptance. Compare to retained1.0.6 WPF baseline. Raster scaling is not a physical display change; accessible-palette fixtures do not prove actual Windows High Contrast toggling. Native drag and actual Discord desktop/browser attachment previews remain physical gates. The available SSH session cannot create an interactive test child (Windows error1314); no security bypass is permitted. Do not send messages or upload clips to another person without explicit authorization.

No public Release, tag reuse, published binary replacement or GitHub Latest change occurred. All E2–E4 acceptance remains pending integrated execution/visual review.
