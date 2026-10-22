# Flick — Installation, Portable Distribution and Safe Migration

## Two Windows deliverables

**Portable ZIP**: dist/Flick-v1.0.2-win-x64.zip. Unzip entirely and open Flick.exe. The app must retain the adjacent bundled tools/ffmpeg and tools/obs directories. No installer is required.

**Conventional Setup EXE**: dist/Flick-Setup.exe. Compiled with Inno Setup 6 using installer/Flick.iss, scripts/build_package.ps1 and scripts/build_installer.ps1. This is a per-user, non-elevated **preview** Windows setup; the generated EXE is *not* yet a public signed release. A clean Windows installation acceptance test is required.

## User-visible installation behavior

- Program binaries: **%LOCALAPPDATA%\Programs\Flick**.
- Start Menu: **Flick** shortcut; desktop shortcut optional (unchecked by default).
- Windows Settings > Apps > Installed Apps: Flick with normal uninstaller entry.
- Normal user launch: **Flick.exe** with the original full-squircle icon.
- Launching Flick never automatically arms game capture.
- Optional per-user startup is controlled in Flick Settings, not silently enabled by Setup.
- Uninstall removes the installed application files and registered shortcuts. It **must not** remove the user's saved clips, exports, or metadata.

## Important legacy-data safety behavior

The current release deliberately continues using **%LOCALAPPDATA%\LocalClip** for settings.json, clips_index.json, connected_sources.json and Thumbnails, for backwards compatibility. Default video originals are in the user's **Videos\LocalClip\Originals**, unless manually changed.

The historic LocalClip installer copied program binaries into %LOCALAPPDATA%\LocalClip and its generated uninstall.ps1 used a recursive delete on the same directory. **Do not run an old uninstall.ps1 on a real user profile.** Back up the old directory and user-selected media first. The new setup uses a separate program directory and does not execute or upgrade the historic uninstaller.

If an old portable installation remains:
1. Close Flick from the tray (not just the X on the window).
2. Back up **%LOCALAPPDATA%\LocalClip**, the user-selected originals/exports folder and any linked external sources. The backup must be outside the old uninstall target.
3. Install the new preview into **%LOCALAPPDATA%\Programs\Flick**. The existing settings/library remain available at their original path.
4. Verify linked folders, saved paths, replay-off startup and hotkeys.
5. If Windows startup was previously enabled for an old executable path, disable/re-enable it in Flick Settings to register the new Flick.exe location.
6. Do not delete historic app directories or data until evidence and the owner support the change.

## Build from source, no publication

~~~powershell
./scripts/setup_tools.ps1
./scripts/check_flick_release_source.ps1
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx -c Release --warnaserror
./scripts/run_tests.ps1
./scripts/build_package.ps1
./scripts/build_installer.ps1 -SkipPortable
Get-FileHash .\dist\Flick-Setup.exe -Algorithm SHA256
~~~

The build script checks that pinned OBS, FFmpeg, FFprobe and portable Flick.exe resources actually exist, uses the Inno Setup ISCC.exe compiler, and prints the resulting SHA-256. It does not download, sign, upload, publish or merge a public release. Inno Setup 6 is a source-build prerequisite; Windows runners and developers must install it from a trusted provider.

The SDK generates Flick.ico from the checked-in logo; no personal font files or generated media are required.

## What remains before public installation

- Clean install, upgrade and uninstall smoke tests on Windows 11 x64, including paths with Unicode/spaces and a standard non-admin user.
- Verify Start Menu/Desktop icons, Windows Installed Apps entry, custom install destination, executable path, startup opt-in, tray Quit, app mutex/upgrader behavior and no orphan workers.
- Verify that **all existing user data remain byte-identical** after uninstall, including a seeded legacy settings.json and clips_index.json in an isolated test account.
- Validate real gameplay clip/save/editor/export flow using the installed app, not only portable source build.
- Review dependency license/complete-source obligations, GitHub artifact hashes and whether publisher code signing is available. A preview unsigned installer may show Windows publisher warnings.
- Owner explicitly approves tagging, creating a GitHub Release and making the repository public. None of these steps happen automatically.

## In-app uninstall and optional local-data removal (v1.0.2 preview)

In Settings > General > Uninstall Flick, the app asks for confirmation and then launches the *installed* Windows Inno Setup uninstaller before quitting its own window and capture worker. A portable ZIP copy detects that it is not registered as an installation and tells the user to remove the extracted folder after quitting rather than trying to delete itself.

The same uninstaller can be started via Start Menu > Uninstall Flick or Windows Settings > Apps > Installed Apps > Flick. By default it removes only program files, Start Menu and desktop links and the startup registration that targets that exact installation. It asks a SEPARATE default-No question before erasing settings.json, clips_index.json, connected_sources.json, thumbnails and local temporary previews. Silent/unattended uninstall preserves these user-data files.

The optional profile cleanup never automatically deletes recordings, exports, linked imported media or arbitrary user-chosen directories, even if the user opts into metadata cleanup. Windows-controlled caches, browser history, SmartScreen records or the downloaded Setup EXE may remain; there is no realistic guarantee of zero operating-system traces.

The browser decides where the installer EXE is downloaded (usually Downloads). Installing the application does not mean running the EXE from Downloads: the setup installs all program binaries in the standard per-user program directory or another directory explicitly chosen in the wizard. Users may delete the original installer download once installation has succeeded.

Only native x64 Windows PCs are permitted by the installer. The recording backend is NVIDIA NVENC only and has been qualified on Windows 11 and RTX 5060; AMD-only, Intel-only and Windows ARM capture are not supported by this preview. Windows 10 behavior is unqualified and not marketed as supported.
