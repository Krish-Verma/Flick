# Flick 1.0.5-preview.1 release readiness

## Candidate and evidence

PR31: https://github.com/Krish-Verma/Flick/pull/31

Approved source: `a6d2cd880c9716e96e9957279b4848eb8d3ca076`.
Source tree: `590aede30ca884e6445f1d373fec49c56416c111`.
Tested merge: `bed13299446865444237dec2d316bafdd77d3536`, with exactly that tree.
Merged to main as `fe2ba2d66fffcefe80935ce44e32411863091d26`.

- [Windows CI40001598489](https://github.com/Krish-Verma/Flick/actions/runs/40001598489): PASS. 276 hardware-independent tests (271 Core + 5 WPF), 5 separate real FFmpeg synthetic-media workflows with zero skips, portable native policy, Windows native compilation, dependency audit and source contracts. Build:0 warnings/0 errors. Nine physical hardware tests remain unrun. The five media tests skipped in the unit-only lane passed in the dedicated lane.
- [WPF rendering40001598486](https://github.com/Krish-Verma/Flick/actions/runs/40001598486): PASS. Actual WPF controls, synthetic fixtures, encoder status and CPU warning. Artifact12522826415, SHA256`cc393a89f4b1f46d427af76014b5dd1bae8966db39287fb9285f4f72ea96b108`. This is not OS/gameplay acceptance.
- [Package/installer40001598491](https://github.com/Krish-Verma/Flick/actions/runs/40001598491): PASS. Exact FFmpeg/runtime/version/checksum gates; actual public 1.0.4 installer upgraded to 1.0.5 under the same AppId; exact-parent waiting, installed executable hash, restart, unknown data sentinel retention and clean fixture uninstall. No errors in cleanup. Installer receipt artifact12523101233 SHA256`ade49ad0e2b93b6b8d59c5266a12da65fec44a687d550739380722ac75502099`.

Package artifact12523350391: `flick-unsigned-preview-bed13299446865444237dec2d316bafdd77d3536`,597702008bytes. Complete archive SHA256`f44071b256458df954b7d846d038fb2e5490f6fd855bcb43c8540c1de36c63c0`.

| File | SHA-256 |
| --- | --- |
| Flick-Setup.exe | `B2E42A2D6A81C78B3AC62356A452D8993782761531A305CEF7DD55D3A856D7C6` |
| Flick-v1.0.5-win-x64.zip | `0C2AD4CC74F99674E60F42C272128936C76FC6C0D02E6718D9C016C67BD23A33` |
| Flick.exe | `A360645CDC3248818E7023EC1A40A210FB60C8B67A909E6A2289A319BE387E28` |
| LocalClip.CaptureHost.exe | `0A242143AABE289CF8EDAB241319900AD6DE69720F3CB7A70FAEC34ADEB938E3` |

## Implemented

Hardware encoder negotiation and bounded CPU fallback; actual encoder/profile feedback; export fallback; complete pinned OBS runtime checks; packet-based replay readiness; target-loss checks; save identity and graceful quit; damaged-index preservation; imported display orientation; exact FFmpeg9.0.2 binaries/notices; consistent product/version resources; installed update handling and previous-version upgrade qualification. The approved purple design is retained.

## Publication

[Publication40002971793](https://github.com/Krish-Verma/Flick/actions/runs/40002971793), publish job120583578175: PASS. [Release 1.0.5](https://github.com/Krish-Verma/Flick/releases/tag/v1.0.5-preview.1), ID422177395, is Latest. All three unauthenticated public downloads matched their exact expected bytes before release422126500 (1.0.4) was preserved as a hidden draft. Its source tag and assets remain intact.

Receipt artifact12522573119 SHA256`484eb001d2d529fc482f78dd75ffc7d594a56979a110d00b468ced146cf68745` records `completed:true`, all three verified downloads and `baselineDrafted:true`. Release manifest SHA256`509A66C27123B5EAFE9D1880C90744D920549E4AA2D8AEA6884EAC7192FF15C1`.

The first follow-up fixture check failed: GitHub's read-only token cannot access a draft release (tag lookup failed; direct asset GET returned 403). This is a CI archive-access issue, not a failed public download or installer-upgrade result. The repair isolates the necessary write-capable token in a no-checkout, fixed-GET-only fetch job, verifies the exact old asset, and transfers it as a workflow artifact to the read-only build/test job. No release mutation is performed by that fetch job. Its successful workflow run remains the qualification authority.

The publisher is locked to its original approved head33ccc0168c7f7bff8339cd0ea5a083c2981435cd; later documentation/fixture fixes cannot republish or replace existing assets. Candidate source and released bytes remain frozen.

## External acceptance still required

- SignPath Foundation application submitted October 23, 2026; approval, account roles/MFA and origin-verifiable provider configuration remain pending. Inactive configuration templates are in `signing/`. Generated Inno uninstaller integration requires provider validation. See SIGNPATH-RELEASE-SETUP.md.
- Authenticode verification of the complete signed distribution, signed Setup/uninstaller and physical Smart App Control acceptance. Do not bypass the strict trust gate by signing unrelated upstream binaries.
- Fresh recordings and decode/audio/framing review on NVIDIA, AMD, Intel integrated, Intel Arc, hybrid graphics and forced CPU; negative fallback-disabled test. See WINDOWS-GPU-COMPATIBILITY.md.
- Clean Windows prerequisite installation, gameplay FPS/1%low and resource measurements, high-DPI/accessibility interaction and long-running/repeated-save acceptance on intended hardware.
- Complete corresponding-source and component-license obligations for exact bundled OBS/FFmpeg/dependencies. Preserved notices, hashes and upstream source links alone do not certify every redistribution obligation.

The owner authorized clearly labelled unsigned previews while signing is pending. This is a Windows 11 x64 compatibility preview, not a signed general-availability release or a guarantee of all-PC support.
