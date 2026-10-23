# Flick Windows trust and Smart App Control

Status: October 23, 2026. The repository is public and the owner approved labelled
unsigned previews. Published `v1.0.4-preview.1` is unsigned. The `1.0.5-preview.1`
development candidate has not established trusted signing or Smart App Control
acceptance. GitHub Latest, passing builds and checksums are not certificates.

## Signing boundary

The existing Azure Public Trust path signs only `Flick.exe`, `Flick.dll`,
`LocalClip.Core.dll` and the Flick-built `LocalClip.CaptureHost.exe`. It snapshots
and rechecks other packaged executable hashes and the pinned FFmpeg files. The
unchanged `verify_flick_trust.ps1` then requires valid signatures on **every**
shipped EXE/DLL. Unsigned upstream code can still block that stricter gate.
Setup and the generated uninstaller require separate signing/verification;
signing the outer Setup does not sign the installed payload or uninstaller.

Preserve unmodified OBS, FFmpeg, Microsoft .NET and other upstream binaries.
Foundation permits certain unsigned OSS files inside signed packages while
restricting which binaries the project's certificate may sign. Do not stamp
unmodified upstream files with Flick's proposed Foundation certificate. Packaging
permission does not prove Windows will allow every DLL/helper.

## Prepared integration and remaining work

- `prepare_flick_signing.ps1`, `sign_flick_file.ps1` and `signed-windows-qa.yml`
  implement an Azure Public Trust candidate path requiring configured identity,
  authenticated signing tools and protected approval. The workflow does not
  publish releases and is not a Foundation workflow.
- Free Foundation signing depends on approval and provider requirements. No
  approved account/policy or completed signing request has been verified here.
  The [setup plan](SIGNPATH-RELEASE-SETUP.md) identifies metadata, origin checks,
  account setup and staged installer/uninstaller work still needed.
- Prepared source is not an executed signing result. See the
  [code signing policy](../CODE_SIGNING_POLICY.md) for release responsibilities.

## Trusted-release acceptance

1. Configure an approved real identity and human signing approval. Preserve
   dependency bytes and complete upstream license/source records.
2. Build the frozen source, inspect compiled owned-PE metadata, sign eligible
   explicit paths, and verify publisher, timestamp and final artifact bytes.
3. Audit every executable and the installed uninstaller. Test installation,
   upgrades, uninstall, update behavior and retained user files. Investigate
   unsigned/invalid dependencies instead of relaxing the all-code trust gate.
4. Test the exact candidate on Windows 11 with SAC enabled: install, launch,
   capture/audio, vendor helpers, export, updates and uninstall. Retain actual
   CodeIntegrity/Operational evidence and hashes. Qualify each GPU/setup
   separately; passing hosted CI does not qualify recording hardware.
5. Publish a new signed version after those results and release approval.
   Existing approved unsigned previews must remain accurately labelled.

Microsoft documents no per-app SAC exception. Flick's installation procedure
must not disable SAC, Defender or Code Integrity protections. See Microsoft's
[SAC overview](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview),
[signing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
and [test guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control).

Inspect the exact downloaded file through Properties > Digital Signatures or
`Get-AuthenticodeSignature`. Its publisher, trusted timestamp and valid chain
are distinct from its filename or download folder.
