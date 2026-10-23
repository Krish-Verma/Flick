# Flick — Open Source Licensing, Publisher Trust and Trademarks

Status (October 23, 2026): Decision record and comparison ONLY. No license change, publisher enrollment, or public-release approval has been performed.

## What Vörssaint actually demonstrates

Vörssaint is an unrelated macOS utility whose project presents GPL-3.0-or-later source with separate name/logo/visual-identity trademark rules. Its DMG is signed with an Apple Developer ID and notarized by Apple. This arrangement is useful as an example of **separating source code licensing, branding permission, and OS publisher trust**. It is not a certificate we can copy to Flick, nor does the GPL make unsigned Windows executables acceptable to Windows Smart App Control.

## MIT compared to GPL-2.0-or-later (Flick today)

**MIT** allows broad modification, proprietary redistribution and relicensing of authorized original MIT source subject to preserving required copyright/license notices. MIT is GPL-compatible in typical licensing combinations, but **it does not automatically relicense upstream GPL code**, and an integrated GPL-covered combined work may still have to be distributed under GPL terms.

**Flick currently uses GPL-2.0-or-later for its own repository** and bundles OBS/libobs components governed by GNU GPL-2.0-or-later; the capture host directly links against the libobs C API. We cannot safely declare the *entire combined distributed Windows app* MIT without verifying architecture of distribution, applicable linking/combination requirements, exact source licensing and third-party corresponding-source duties. FFmpeg build options and dynamic/static dependencies are additional review inputs. Nor can we relicense any contributions for which we do not hold the necessary copyright permissions.

**Conservative decision:** Retain the current GPL-2.0-or-later repository licensing until a dependency/source-audit establishes a legally sound path. It may be possible to offer specific original, separable utilities under an MIT license while keeping a combined application GPL; that is a future reviewed choice, not an automatic settings toggle. Any changes must preserve existing users' license grants and upstream notices.

**Branding:** A name/logo/trademark policy is a **separate** governance document. An MIT/GPL source license usually does not grant rights to misrepresent an unofficial fork as an official signed Flick release. If a trademark statement is written, verify original artwork ownership, use fair and accurate wording, and avoid claiming registered trademark status without legal support.

## Signing and repeated releases

Code signing is separate from copyright licensing and does not have an OS-agnostic bypass:

1. **Microsoft Artifact Signing / a trusted CA:** one-time service enrollment / developer identity verification, then signing can run automatically on approved CI release workflows using protected signing identities. It is typically a paid service; later releases still must pass checks.
2. **SignPath Foundation OSS:** potentially no monetary signing cost if the public open-source project qualifies and is approved. SignPath requires verifiable builds and approval for each signing request, among other requirements. It is not a way to skip the approval for every release.
3. **Microsoft Store:** an MSIX package can use Microsoft Store signing, but requires Microsoft Store publication/identity, packaging and qualification, and can affect the capture host and runtime assumptions. It doesn't automatically sign the classic GitHub Inno Setup EXE.
4. **Unsigned test build:** open source / MIT / GPL / a GitHub Release / SHA-256 checksums don't grant Smart App Control trust. Do not disable SAC/Defender for users.

## Implementation path when the owner chooses a signing service

- Keep trusted-signing environment on main only with identity scoped by OIDC and human approval, not checked-in private keys.
- Verify every shipped Windows EXE/DLL, installer and uninstaller signature + timestamp, and real Windows Smart App Control enforced tests.
- Preserve exact release provenance, third-party license obligations, source build scripts, synthetic fixtures and privacy/security review.
- Publish signed tagged artifacts and mark Latest only when verified, leaving unsigned test artifacts clearly labeled; do not silently replace old files.

## Current open questions before any MIT/legal change

- Which Flick components and artwork are wholly owned by the authors? Were any contributions licensed under separate terms?
- Does the distributed OBS/libobs linked capture host form a GPL-covered combined work and what corresponding source must ship?
- Which FFmpeg build flags/components and relevant source/notice obligations apply to the pinned redistributables?
- If SignPath Foundation is requested, is Flick's full repo and distribution already publicly reviewable, active and eligible under their signing policy?

References:
- OBS libobs header: https://github.com/obsproject/obs-studio/blob/master/libobs/obs.h
- SignPath Foundation: https://signpath.org/
- SignPath OSS terms: https://github.com/SignPath/fdn-website/blob/main/docs/terms.md
- Microsoft SAC signing: https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control
- Current Flick signing blocker: docs/WINDOWS-TRUST-AND-SMART-APP-CONTROL.md

This document is technical release guidance, not a substitute for professional legal review.
