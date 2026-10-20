# Flick GUI — Slice 3 Connected Libraries & Storage

**Repo:** https://github.com/Krish-Verma/Flick  
**Branch:** `feat/gui-slice3-library-import-storage-20261020`  
**Draft PR:** https://github.com/Krish-Verma/Flick/pull/4  
**Base:** `feat/gui-slice2-shell-notifications-20261020` (draft PR #3)  
**Approval boundary:** Slice 3 only. The separate backend/Slice 1/Slice 2 PRs must not be merged or released automatically.

## What was built

### Persistent connected folders

- Users explicitly select folders holding clips from SteelSeries, OBS, NVIDIA or another recording app; Flick reads supported **MP4/MKV** files via FFprobe. It neither copies nor modifies originals.
- `ClipStorage` persists imported metadata alongside original Flick recordings in `%LOCALAPPDATA%/LocalClip/clips_index.json`. User-authorized source folders and ignored paths live in `connected_sources.json`.
- Each indexed clip carries `SourceKind` (`Flick` or `Imported`), `ConnectedFolderId`, `SourceLabel`, and computed `IsAvailable`/`LibraryStatus`. Legacy files without source fields default to Flick.
- Disconnected drives/renamed external folders do **not** cause indexed metadata to disappear at startup. The library shows `Offline` rather than silently deleting entries. The user can reconnect/rescan later.
- Adding the same canonicalized folder twice returns the same registration; duplicate indexed paths are skipped. The app does not claim to deduplicate hard-linked files or unrelated path aliases on all filesystems.

### Import limits and lightweight priority

- Scanning is **explicit/manual**, not a permanent file watcher. It stays single-folder-at-a-time with one FFprobe/video metadata worker at a time, 12s ffprobe and 15s thumbnail timeouts, and user cancellation.
- Supported nested directories are scanned to depth **8**, with at most **10,000 videos and 1,500 directories per scan**, and at most **32 registered folders**. Reparse points/junctions/symlinks are not traversed. Whole-drive root selection is rejected.
- The first ~16 imported clips may receive preview thumbnails during scanning; other thumbnails are lazily generated when selected and the encoder is not capturing. Individual imported thumbnails report changes to WPF through `INotifyPropertyChanged`.
- Index writes are batched every 32 imported entries and flushed at the end, including cancellation. Visible library rows use recycling/virtualization and single batch-collection Reset notifications rather than thousands of add events.
- `MainViewModel.StartCaptureAsync` cancels current import and thumbnail tasks. Starting new import/rescan while replay is active is rejected, preserving gameplay priority. Partial completed imports remain indexed for a later manual rescan.
- When reaching the per-folder scan file limit, Flick explicitly tells the user to link smaller folders. Counts reflect discovered files; skipped invalid/probe failures are reported.

### Ownership and destructive operations

- **Flick original:** existing explicit Delete action sends the file to Windows Recycle Bin where supported.
- **Imported clip:** `Remove` removes the item from Flick's index, persists an ignored-path tombstone, and **never deletes or modifies the source original or any export**. A rescan doesn't silently re-add excluded files.
- **Unlink folder:** removes its imported references from Flick but does not remove any external videos. It does not move/export/rewrite the source clips. These are distinct actions.
- Data and source registries save via temporary JSON plus atomic rename on the same volume. A failure to write metadata must not be displayed as successful import.
- Every imported video is probed for video dimensions/duration before indexing. External multi-audio containers do not inherit Flick-specific track labels or assumptions; first track is the cautious default and users can edit the export mix later.

### User interface

- Compact **Your Clips** header with **+ Import Folder**, search, All Clips/Flick/Imported source chips, selected-state styling, and explicit Offline/Imported statuses.
- Video rows have title, source and duration with truncated labels/tooltips, and the list is virtualized for large imports. Empty results give a clear Save Replay / Import Folder path.
- Context menu preserves Rename, Open Original Folder, and Remove/Delete. Imported files use the wording `Remove`, not a destructive Delete promise.
- Bottom of the library shows clip/folder counts, Rescan and Open Folder.
- `ConnectedFoldersWindow` displays persisted source names, local paths and availability, plus Add Folder, Rescan Selected, Unlink Folder, Cancel Scan and Close. Subfolders are optional and enabled by default; no source files are duplicated.
- Settings retains the existing simple dialog until Slice 5, but Storage now has **Saved Clips Folder** and **Export Folder** independently, with an entry to Manage Connected Clip Folders. Changing output directories affects future files, not existing MKVs/MP4s.

## What this slice intentionally does not do

No multi-track timeline rewrite, in-app playback engine replacement, custom streaming service, cloud syncing, automatic import of arbitrary user folders, background full-drive scanning, upload integration, or new libobs/encoder technology. Slice 4 handles redesigned editing; Slice 5 handles final sharing/settings layout.

## Qualification status

GitHub Windows CI exercises WPF/.NET and native C++ builds, design-token/static checks and hardware-independent tests. It does NOT demonstrate actual large imported-library UX or USB disconnect behavior. AntiGravity must execute `docs/GUI-SLICE3-ANTIGRAVITY-HANDOFF.md` and return screen evidence and real file hashes.

**Release gate:** Keep PR #4 draft, do not merge or publish. Folder import, source ownership, offline persistence, export, cancellation, DPI/performance all require real Windows verification.
