# Code signing policy

Status: October 23, 2026. Flick's repository and preview downloads are public.
The published `v1.0.5-preview.1` installer is unsigned. No Foundation approval,
certificate entitlement or completed signed release has been verified in this
work. The free Foundation application was submitted October 23, 2026; it is awaiting review. This policy describes the intended process, not an operational signing
service or Smart App Control certification.

## Responsibility and scope

Repository ownership is `Krish-Verma/Flick`. Maintainer and proposed signing approver: [Krish Verma](https://github.com/Krish-Verma). Provider roles and required MFA still need confirmation before activation. Development/review responsibility
follows actual repository permissions and review records. Before activating
provider enrollment, the owner must publish named signing approvers/roles,
enable required MFA and configure protected release approval. No additional
person or approval role is invented here.

Only explicitly allowlisted, eligible artifacts built from reviewed Flick source
and packaging scripts may receive the project's certificate. The application
list is `Flick.exe`, `Flick.dll`, `LocalClip.Core.dll` and
`LocalClip.CaptureHost.exe`. Installer/uninstaller eligibility and compiled
metadata must be verified with the selected provider.

Unmodified OBS, FFmpeg, Microsoft .NET and other upstream binaries retain their
original bytes/signatures. Do not sign them with a proposed Foundation identity.
Preserve notices, exact hashes and source obligations. New executable paths need
explicit ownership/policy review.

## Required evidence

- Build the reviewed source SHA from pinned verified inputs on permitted CI
  infrastructure. Retain source/build identity, dependency inventory, compiled
  product/version metadata and tests for that candidate.
- Require authorized human signing approval and least-privilege provider access.
  Keep credentials outside source/logs; do not use self-signed placeholders.
- Verify returned signatures, expected publisher, timestamps and exact file set.
  Generate final checksums after signing and preserve upstream bytes.
- Verify installer and installed uninstaller separately. Retain the strict
  all-EXE/DLL trust audit, installation/update/data-preservation tests and actual
  Windows 11 SAC acceptance as distinct gates. Signing an outer installer alone
  does not establish whole-package trust.
- Publish a new approved version with source and artifact receipts. Keep any
  approved unsigned preview accurately labelled; do not replace released files
  with changed bytes under an identical version claim.

Foundation is a proposed no-fee route subject to its
[terms](https://signpath.org/terms). Its required attribution and named roles
must be added accurately after approval. The existing Azure workflow does not
establish Foundation enrollment. See the [setup plan](docs/SIGNPATH-RELEASE-SETUP.md)
and [Windows trust requirements](docs/WINDOWS-TRUST-AND-SMART-APP-CONTROL.md).

The application's current local/network behavior appears in [Privacy](PRIVACY.md).
Report suspected signing/artifact tampering through [Security](SECURITY.md),
without publishing private files or keys.
