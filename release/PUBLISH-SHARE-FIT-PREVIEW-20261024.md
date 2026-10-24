# Approved Flick 1.0.7 unsigned product preview publication

Approval: APPROVED

Human authorization: The repository owner answered “Publish as Latest unsigned preview” to the concrete proposal to publish verified 1.0.7-preview.1 as public GitHub Latest, with gameplay/hardware testing pending and all previous releases retained. Authorization received in this chat on 2026-10-24 and recorded at 2026-10-24 18:39:17 UTC.

## Concrete proposed action

Publish the reviewed v1.0.7-preview.1 as the public GitHub Latest download, titled Flick 1.0.7 — UNSIGNED PRODUCT FEATURES PREVIEW, with exactly Flick-Setup.exe, Flick-v1.0.7-win-x64.zip and SHA256SUMS.txt.

Keep ALL old release entries, visibility/statuses, metadata, assets and Git tags unchanged. Retain public1.0.6 and draft1.0.4/1.0.5. Create only the new draft, verify uploaded asset IDs/sizes/digests, then publish that new release as Latest. Download every new public asset without credentials and verify exact bytes/SHA-256, tag target, Latest and preserved state. Retain receipts on success or failure.

The candidate also includes simple live RAM and CPU labels sampled about once per second, plus Settings → General → Display → Show RAM and CPU. The saved toggle hides the footer readings and stops optional sampling when off; staged checkbox changes apply only after Save. No measured RAM/CPU overhead claim is made.

GitHub Latest requires catalogue prerelease=false. Semantic preview.1, explicit UNSIGNED title/disclosures and application preview-channel consent remain. This is not a stable, signed or universally hardware-qualified release.

## Exact evidence — final verified Share fit and resource-display candidate

| Evidence | Pin |
| --- | --- |
| Tested source | ac5a2fbd2db337dec27e5bda415c3cb8783bc7f5 |
| Exact main/tag target | cd4ad99867da9c5c6e5a828d95667159aac4b306 |
| Identical complete tree | ac18a266d45df19730bcd704670b3d4db352efe1 |
| Windows CI run | 40118162737 |
| Windows CI attempt | 1 |
| WPF render run | 40118162706 |
| WPF render attempt | 1 |
| Package run | 40118162700 |
| Package attempt | 1 |
| Package artifact ID | 12569693603 |
| Complete artifact archive digest | sha256:2fbf398126f30cf91b5af9a34ae258090d2515154b156c5d98b1f11dfcbf288f |
| Setup SHA-256 | 9d2b4574cff0515ae464459a02d7eb8c8b041734d40c2fb1ac5d1c29fc3a8703 |
| Portable ZIP SHA-256 | 9b9ca7fc0b071ee902dcec350b98c6c0eee2461a69722c189dc1b7feb0cda9a1 |
| Checksum file SHA-256 | f76fbfb4a5d8be17d570aeb80ba5e367f5e1fbab262d80d7cb0325eeab997a74 |
| Publication notes SHA-256 | eb5cc4e2abc277f42f3109884788eef602db733f3e344d014dc81d78776a8c50 |
| Preserved catalogue SHA-256 | 91b66728609b8f5336c8e61433eade5bc4594608edb2f6ed06c90381169d2b3d |

## Honest limits and authority

Exact-source hosted build/tests/media/render/installer evidence must pass and be pinned above. The maintainer is taking real-game/hardware tests separately. Physical capture/process-audio/microphone/endpoint/Discord behavior, game performance and endurance remain disclosed as pending, not falsely passed and not an invented blocker to an explicitly approved unsigned developer preview.

No signing, Store certification, credential collection, security/privilege/driver change or physical capture is authorized by this publishing proposal. Smart App Control may block unsigned bytes; keep Windows protections enabled. License/notices/corresponding-source responsibilities are unchanged.

## Preparation and activation

1. Fill every immutable pin from the final Share fit and resource-display source. An earlier1.0.7 artifact is not interchangeable with a different tree. Inspect/hash the complete package archive and all three release files first.
2. Review release-notes.md and preserved-releases.json against live GitHub state. If either changes, update its exact SHA-256 in this record and the template.
3. Present the concrete pinned source/artifacts, notes, action and limits to the human owner for ONE fresh explicit approval. Prior1.0.6 approval, coding authority or this pending record does not grant it.
4. Only after that authorization: change this record to Approval: APPROVED; record the owner's exact authorized scope and time; compute the final record's UTF-8 SHA-256. Fill APPROVAL_RECORD_SHA256 in the template and set PUBLISH_APPROVED to the literal string true. Both gates must pass. The document's own digest is not written back into the document.
5. Copy the reviewed files to a NEW dedicated branch, leaving historical publishers untouched:
   - publish-approved-share-fit-preview.template.yml → .github/workflows/publish-approved-share-fit-preview.yml
   - APPROVAL-PENDING.md → release/PUBLISH-SHARE-FIT-PREVIEW-20261024.md
   - release-notes.md → release/NOTES-SHARE-FIT-PREVIEW-20261024.md
   - preserved-releases.json → release/PRESERVED-RELEASES-SHARE-FIT-20261024.json
6. Dedicated branch release/publish-share-fit-preview-20261024 targets main. Opening its same-repository PR is the ACTUAL publication trigger. Do not open it before approval. Only opened/attempt1 runs; synchronize/reopen/rerun cannot automatically republish.
7. APPROVED_SOURCE is the exact source of successful CI/render/package runs. APPROVED_MAIN is the exact current main release/tag target. Both complete trees and workflow subtree IDs must match APPROVED_TREE. Main is rechecked immediately before creating and publishing the new release. Keep this publisher PR unmerged until completion; merging it adds a workflow absent from the approved runtime tree.
8. The contents-write/actions-read job has NO CHECKOUT. It runs reviewed inline workflow code only. It reads bounded/hash-pinned approval/notes/catalogue/version data, and never executes PR scripts, artifact EXEs, tests, builds, capture or signing. Its only PATCH target is the newly created release ID. No old release, asset or Git ref mutation is implemented.
9. No PAT or signing secret is required for this route. The same-repository PR triggers Actions using repository GITHUB_TOKEN. Existing maintainer tooling needs its normal permission to install the new workflow; no permission change is made here.

If a check fails after draft creation/publication, a new draft/public entry or tag may already exist. Inspect publication-receipt/publication.json and live state. Never blindly rerun, replace assets, delete/move tags, or loosen an expected hash to recover. No old-release rollback/mutation is implemented.

This publication record was activated only after the explicit human authorization above. The approved action creates one new release and its tag, preserves older releases/tags, and does not change secrets, repository settings or test-PC state.

## Verified candidate acceptance

- Implementation merged in PR51 and PR52; exact main and tested source have the identical complete tree above.
- Windows CI40118162737: Core492/App9 passed,17 explicit gated skips; separate generated-media8/8 and20 non-capture harness self-checks passed. Native, policy and source gates pass.
- Actual WPF run40118162706:95 PNGs;30 Share and85 resource layout observations pass. Independent visual review passed, including General Display and minimum Share. Counter values in presentation fixtures are synthetic.
- Package40118162700: four owned PE versions verified; real1.0.4 upgrade,31.63-second exact-parent wait, restart, data retention and uninstall passed. Installer evidence archive12570506860 independently rehashed.
- Read-only verification40119390076 (PR53, deliberately unmerged) rehashed the complete original archive, checked all four manifest binaries, and split the unchanged three files. Local separate downloads independently match the original Setup/portable/manifest hashes and installed apphost. No candidate executable ran locally.
- Public-release permission is APPROVED for the exact candidate and publication scope recorded above. Signing, physical/gameplay/audio/Discord/endurance acceptance and the repository's separate source/license redistribution review are not certified by these checks.
