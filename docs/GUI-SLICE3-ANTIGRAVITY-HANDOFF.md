# Flick Slice 3 — Windows Library Import & Storage Acceptance

**Source:** https://github.com/Krish-Verma/Flick  
**Branch:** `feat/gui-slice3-library-import-storage-20261020`  
**Draft PR:** https://github.com/Krish-Verma/Flick/pull/4  
**Parent:** Slice 2 PR #3. Do not work on Slice 4 or merge any PR.

## Build & Inspect

1. Check out PR #4 HEAD on the owner's Windows 11 RTX 5060 PC in an isolated clean worktree. Read `docs/GUI-SLICE3-IMPLEMENTATION-V3.2.md`, the full original `ClipperBlueprint/WINDOWS-CLIPPER-BLUEPRINT.md`, `docs/USER-GUIDE.md`, and all changed source/tests.
2. Run `scripts/check_gui_design.ps1`, `scripts/check_gui_slice2.ps1`, `scripts/check_gui_slice3.ps1`, `dotnet build LocalClip.slnx -c Release --warnaserror`, `scripts/run_tests.ps1` and `scripts/run_tests.ps1 -Hardware`. Verify latest GitHub Actions results including native MinGW/libobs compilation. Repair and rerun failures. Native cloud compilation is not NVIDIA capture proof.
3. Launch the complete rebuilt self-contained GUI, verify old capture/tray/Clip Saved sound and overlay/player/trim/export/Discord drag-to-file remain operational. Capture starts DISARMED. Do not use the historical v1.0.0 ZIP as proof of new GUI behavior.

## Import & Local Library

4. Link an existing MP4/MKV folder from a real recording app (SteelSeries Moments, OBS, NVIDIA or other local folder). Record file count, total byte size and SHA-256 hashes of samples **before and after** scanning. Verify indexing does not copy, move, rename, modify, upload or delete originals. Check first-run Import Folder button and Settings → Connected Clip Folders entry.
5. Create a test folder hierarchy with MP4/MKV plus TXT/JPG/invalid video, subfolders, duplicate filenames in different directories, Unicode/long paths and read-only videos. Compare Include Subfolders ON vs OFF. Test adding same folder twice; exact canonical paths should not duplicate. Symlinks/junctions should not cause traversal outside the selected root; whole-drive root choice must fail safely.
6. Test 100, 1,000 and 5,000 imported clips (synthetic media can be generated locally). Verify directory scan caps, progress, cancellation, partial completion retained after restart, low-priority thumbnail generation, no reindexing of known paths, responsive search/scroll with virtualization, and RAM/CPU/disk IO with import OFF versus ON. User-initiated capture while importing must cancel background work and prioritize game recording, without corrupting the index.
7. Disconnect a removable drive or temporarily rename a controlled test folder with owner permission. Restart Flick and confirm clip entries and linked folders remain in metadata, show `Offline`, and do not disappear silently. Reconnect/rescan and verify they work again, retaining custom titles, trims and export status metadata where valid. Unavailable folders must not trigger a full-drive search.
8. Remove an imported clip from the library and then rescan. It must stay excluded; its original and any exported MP4 must still exist with unchanged hashes. Unlink a source folder and confirm its entries are removed but original media remains untouched. Test actual Flick-created clip Delete/Recycling separately. Prevent non-destructive import code from weakening original deletion safety.
9. Open an imported clip in the player and export a size-capped MP4. Include single-audio, multi-audio and silent fixtures from external capture software. Verify FFprobe stream metadata and user-readable source labels; ensure no assumed independent game/Discord source labels or default double mixing. Check original video SHA-256 unchanged after trim/audio export.

## Storage, Layout, Privacy

10. In Settings choose different Saved Clips and Export directories, including drive/path with spaces and Unicode. Save, restart and make a new live clip/export. Verify future files go to the new locations, old files stay where they were, settings persist and Cancel leaves existing directories/settings untouched. Test unwritable/disconnected export target with clear failure and no data loss. No unexpected migration.
11. Visually review the compact library at window sizes ~1000x650 and 1240x760 DIPs with 100/125/150/200% scaling: Title Case, no clipped text/controls, imported source labels, offline badges, search/filters, selected-state, context menu, folder dialog overflow. Validate keyboard navigation, search focus and screen-reader/accessible names. Inspect screenshots rather than relying on CI.
12. Verify no new telemetry, cloud accounts, vendor access, watchdog processes or continuous watchers. Observe capture FPS, 1% lows, private bytes and frame times with linked folders idle: there should be no extra background scanning and no material gameplay impact. Test concurrent user actions, low disk, corrupted JSON, multiple folders, missing FFprobe/FFmpeg and cancellation. No source file deletion on any failure.

## Delivery

For every acceptance gate, supply PASS/FAIL/BLOCKED with exact commands, commit SHA, run URLs, screenshots, import counts, file hashes, errors, memory/time metrics and the target PC details. Fix any discovered defects **in PR #4 only**, rerun appropriate tests and GitHub Actions, and update docs as results change. Avoid false passes: GPU, real files, USB and GUI are not proven by compile-only CI.

Never merge/publish PRs, send clips to Discord, reboot, change drivers, interact with unrelated worktrees or delete personal source recordings without explicit owner authorization. **Wait for owner approval before Slice 4.**
