# Flick UI Refresh R1 — Single-window navigation implementation

**Source branch:** feat/gui-refresh-r1-single-window-20261023. **Base:** main f895c3641e3f37b4bb4c4c02425f349480b41f8b. This is a source implementation summary; no claim of physical Windows screenshots or GPU QA.

## User-visible behavior

- MainWindow continues to own the capture source bar, replay state, clip library, editor and tray.
- Its existing main content pane is replaced by an embedded **Settings** or **Connected Clip Folders** UserControl only on explicit navigation. Neither management page creates another Flick-owned top-level Window.
- Settings remains six categories, with the same input controls and staged changes. **Save** atomically persists and returns to Clips; **Cancel** and the Clips navigation discard unsaved changes; revisiting Settings while open doesn't create another copy.
- Connected folders is reachable from Clips Import Folder or Settings Manage Folders. **Close** returns to the invoking page. Rescan/Unlink retains protective semantics. Leaving midscan requests cancellation, without stopping replay or mutating imported videos.
- The editor is not destroyed when Settings is opened. Before navigation, its currently playing MediaElement decoder and any pending local preview are stopped, and the selected poster pixels are dropped. On return, the same clip selection and trim/audio model remain and the bounded poster is restored lazily.
- The OS-native folder picker remains provider-owned. Flick-specific MessageBox actions remain until R7, when confirmations move to a themed in-window layer; no separate Flick Settings/Folder windows remain after R1.
- In-app uninstall still calls the registered Windows Inno uninstaller; this is a necessary OS-owned surface.

## Source changes

- SettingsWindow.xaml and its code-behind now implement a **UserControl** with event Action<bool> CloseRequested for Save/Cancel/Uninstall; old DialogResult and standalone Close() lifecycle are removed.
- ConnectedFoldersWindow.xaml and its code-behind now implement **UserControl**, with CloseRequested event and LeavePage() to stop only the import scan.
- MainWindow hosts those pages with WorkspacePageLayer / WorkspacePageHost and owns navigation, return origin, capture-state preservation, hotkey registration after saved settings, microphone opt-out and temporary preview cleanup.
- Source structural checker scripts/check_gui_refresh_r1.ps1 is wired to Windows GitHub CI alongside existing GUI Slices 1–6 source invariants.

## Risk and regression checks

- Ensure Save/Cancel and in-app uninstall work with a Settings **UserControl**, including old errors and recovery. No settings.json rewrite unless explicit Save succeeds.
- Ensure folder import does not accidentally drop already saved source links, that cancel never deletes originals, and there is no second scan when switching pages.
- Test entering and leaving Settings/Folders while replay is buffering on a dedicated Windows NVIDIA PC; real capture continues until explicitly stopped.
- With a selected video playing, navigate to Settings: preview must stop, MediaElement.Source null, then return to clip without changing trim or audio state. A pending FFmpeg preview must be cancelled.
- At 720x480 DIP through full desktop and 100/125/150/200% DPI, verify tab focus, scroll, nav and no hidden Save/Cancel or disabled buttons.
- Restoring from tray should return to the management page that was visible when the window was hidden, without starting video playback.
- Use scripts/check_gui_refresh_r1.ps1, full Windows WPF compilation, existing 120+ offline suite and native host build; CI cannot replace real hardware QA.
- Physical AntiGravity test instructions are in docs/GUI-REFRESH-R1-ANTIGRAVITY-HANDOFF.md; **AntiGravity is testing only**.

## Deferred to later PRs

R2 handles themed title chrome, scrollbars, menus and circular optical logo; R3/R4 video-first workspace and contextual edit controls; R5 Share/Discord; R6 shortcut recording UX; R7 all remaining app-owned confirmation overlays and final performance qualification; **R8** minimalist Clip Saved toast (actual duration only; no ID/number/title) and safely bounded custom sound choice.

**No new installer/release created.** The separate trusted-signing and Smart App Control blocker remains unchanged.
