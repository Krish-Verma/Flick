# SignPath Foundation release setup

Status: implementation plan, October 23, 2026. Flick has no verified Foundation
enrollment, configured Foundation project, approved signing request, or signed
candidate established by this work. No provider account or repository secret was
accessed. The existing signing workflow uses Azure, not SignPath.

## Provider requirements and remaining owner setup

Foundation is a potential no-fee route after project approval. Its terms require
open-source eligibility, maintained/released software, MFA and named signing
roles, code review, an accurate public code-signing/privacy policy, release
approval, and enforced product/version metadata. Only project-built eligible
binaries may receive its certificate; unmodified upstream OSS binaries may be
included without being signed by Flick. Eligibility and the generated Inno
uninstaller's treatment must be confirmed with Foundation. See the
[Foundation terms](https://signpath.org/terms) and
[application page](https://signpath.org/apply).

Before enabling a workflow, the owner must obtain approval, install/authorize the
SignPath GitHub App for `Krish-Verma/Flick`, associate the repository with a
SignPath project, and configure its origin and branch policies. Record the
organization ID, project slug, signing-policy slug and artifact-configuration
slug as non-secret configuration. Store a least-privilege submitter API token in
a protected GitHub environment; never place it in source, logs, or chat.
Name authorized human approvers in the published policy. No account creation,
terms acceptance, App installation or credential changes are performed here.

## Exact signing boundary

The proposed initial application allowlist is:

| Package-relative file | Treatment |
| --- | --- |
| `Flick.exe` | Eligible application host; confirm Foundation's treatment of its .NET apphost stub |
| `Flick.dll` | Flick WPF application built from this repository |
| `LocalClip.Core.dll` | Flick core library built from this repository |
| `tools/obs/runtime/bin/64bit/LocalClip.CaptureHost.exe` | Flick native capture host built from this repository |
| Generated Inno uninstaller | Separate explicitly reviewed packaging artifact; obtain provider acceptance |
| `Flick-Setup.exe` | Outer installer, built after signed inner application/uninstaller are available |
| FFmpeg, OBS, Microsoft .NET and other upstream EXE/DLL files | Preserve exact upstream bytes and existing signatures; never re-sign under Flick's Foundation certificate |

All owned PE files need `ProductName=Flick` and a centrally consistent product
version, verified from their **compiled** VERSIONINFO resources. Source project
properties alone are insufficient. The native host previously had no version
resource and Core previously inherited a different product/version. Validate
metadata again after packaging. Use exact paths and required match counts in
the provider configuration; a new binary needs explicit review, not a wildcard.
Provider artifact restrictions are documented in the
[SignPath reference](https://docs.signpath.io/artifact-configuration/reference).

`build_trusted_flick_preview.ps1` now applies the four-file application allowlist
to the existing Azure path, snapshots all other packaged executable hashes,
checks their bytes after signing, and rechecks the pinned FFmpeg hashes. This
is a provider-independent correction, not Foundation integration. In particular,
Authenticode signing changes a PE's SHA-256: re-signing pinned FFmpeg tools would
contradict `check_ffmpeg_runtime.ps1` rather than improve upstream integrity.

Keep two distinct release results:

1. **First-party signature/integrity audit:** eligible owned files have the
   expected trusted publisher and timestamp; all redistributed upstream files
   remain identical to their verified inputs and are inventoried by signature
   status. Unexpected files, modifications or invalid existing signatures fail.
2. **Full Windows trust acceptance:** the existing `verify_flick_trust.ps1`
   requires every packaged EXE/DLL to be trusted. Preserve that strict gate.
   An unsigned upstream dependency can block it even when packaging that file
   is allowed by Foundation. On-device Smart App Control acceptance is a further
   requirement; signing the installer alone proves neither result.

Do not exempt unsigned upstream files from the all-code gate and call the
product SAC-ready. Seek upstream-signed releases or another provider-approved
packaging strategy if the strict inventory fails.

## Reviewable integration sequence

The existing Azure SignTool callback cannot simply be pointed at the Foundation
API. Foundation's GitHub integration consumes an already uploaded Actions
artifact and verifies its workflow origin. Its documented formats support PE
files and nested ZIP contents; Inno is not listed as a deep-signing composite
format. Treat outer Setup and the uninstaller as distinct signing work.

Implement the following in a **new** main-only, manually invoked protected
workflow, keeping all prerequisite jobs on GitHub-hosted runners:

1. Build the approved source SHA with pinned tools, run unit and real-media
   gates, and record dependency hashes, executable inventory and compiled file
   metadata. Fail before submission when any owner configuration is missing.
2. Stage only the approved application files and an eligible unsigned Inno
   uninstaller. Inno's documented offline flow uses `SignedUninstaller=yes`
   without a SignTool, emits a uniquely named uninstaller into
   `SignedUninstallerDir`, and reuses its signed counterpart on a subsequent
   compile. A headless staging implementation still needs validation: accept
   only its specifically expected staging outcome and the exact emitted file;
   never ignore arbitrary compiler failures or reuse an unrelated cached stub.
3. Upload that unsigned bundle with `actions/upload-artifact` and pass the
   returned `artifact-id` to SignPath's submission action. Use the project's
   explicit artifact-configuration slug, constrained source/branch policy and
   manual approval. Download the signed result into a separate directory.
4. Verify every returned file, exact expected file set, publisher, timestamp,
   product/version and content correspondence before replacing only allowlisted
   paths. Record the signing request ID and originating workflow/run/SHA.
   Recheck all upstream hashes. Keep the signed uninstaller cache isolated to
   this source/tool/version build.
5. Compile Inno again with the verified cached signed uninstaller and already
   signed application payload. Submit the outer Setup as a second explicit
   GitHub artifact for signing. Provider policy may require another approval;
   do not assume a prior request approves later changed bytes.
6. Verify the returned outer Setup, rebuild the portable ZIP from the final
   payload, and generate final SHA256SUMS **after** every signature is applied.
   Run strict all-code trust and runtime-integrity checks. Install into an
   isolated directory and inspect the installed `unins000.exe`, then test
   uninstall/data preservation and the updater's signed artifact behavior.
7. Test those exact installer/app/helper/export/uninstaller paths on Windows 11
   with Smart App Control enabled. Publish a new signed version only after
   the release gates and hardware acceptance are complete; retain exact source,
   run, request, dependency and final artifact digest receipts.

The staged uninstaller process and provider configuration are deliberately
identified as remaining implementation/acceptance work. There is no checked-in
workflow presented as runnable without an approved account.

## Verified GitHub integration inputs

The current official action is
`signpath/github-action-submit-signing-request@v3`; resolve and pin its reviewed
commit when implementing the workflow. The required inputs are `api-token`,
`organization-id`, `project-slug`, `signing-policy-slug`, and
`github-artifact-id`. Also set the explicit `artifact-configuration-slug`,
`wait-for-completion: true`, a realistic approval timeout, and a separate
`output-artifact-directory`. Use `actions: read` and `contents: read` for artifact
origin lookup, without release-write permissions in the signing job. The official
[GitHub integration documentation](https://docs.signpath.io/trusted-build-systems/github)
describes origin checks, configuration and returned signing-request identifiers.

Use a `<zip-file>` artifact root for an uploaded ZIP and exact `<pe-file>` paths
with product/version restrictions. Validate the eventual XML against the schema
and a real unsigned artifact in the approved SignPath project before activating
release signing. See [artifact structure](https://docs.signpath.io/artifact-configuration/syntax).

The uninstaller cache behavior above is specified by Inno Setup's
[SignedUninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
and [SignedUninstallerDir](https://jrsoftware.org/ishelp/topic_setup_signeduninstallerdir.htm)
documentation. No claim is made that signing only the outer Inno PE recursively
signs its payload or installed uninstaller.
