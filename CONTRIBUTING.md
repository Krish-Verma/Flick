# Contributing to Flick

Flick is preparing for an open-source release. Thank you for helping improve its privacy, usability and game performance.

## Development rules

- Keep gameplay capture **opt-in**. Never add continuous background recording, unrequested telemetry, cloud uploads, account requirements or hidden Discord posting.
- Do not weaken original clip immutability, imported source preservation, local-only media processing, protected secrets or Windows per-user permissions.
- Use a short issue and a scoped pull request with exact base commit, changed files, why the change exists, new regression tests, and user-visible effects.
- Keep pinned dependencies and GPL obligations intact; do not check generated executables, binary toolchains, personal screenshots or private QA/game footage into Git.
- Do not introduce heavyweight frontends or always-on overlays for trivial UI behavior.
- Use tests for source/pure logic changes; run actual Windows/NVIDIA hardware checks when the feature requires them. Label hardware steps BLOCKED if not executed.

## Build/test commands

~~~powershell
./scripts/setup_tools.ps1
dotnet restore LocalClip.slnx
dotnet build LocalClip.slnx -c Release --warnaserror
./scripts/run_tests.ps1
./scripts/check_gui_design.ps1
./scripts/check_gui_slice6.ps1
./scripts/check_flick_release_source.ps1
~~~

Windows capture, real Discord drag/drop and installer acceptance require the owner's authorized test rig. A CI pass does not certify them.

## Licensing and review

Unless expressly identified as separately licensed, contributions of original code should be compatible with GPL-2.0-or-later. Do not copy source or artwork with incompatible or uncertain licenses. Contributors retain their copyrights; see LICENSE-NOTICE.md and THIRD-PARTY-NOTICES.md.

The main repository is currently private pending owner release approval; developer preview branches are not a request to publish user footage or change repository visibility.
