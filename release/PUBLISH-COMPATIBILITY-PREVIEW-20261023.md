# Approved Flick 1.0.5 unsigned compatibility preview publication

The owner authorized another public unsigned preview while trusted signing is prepared. This one-off publication uses the exact release branch `release/publish-compatibility-preview-20261023` and only runs for its same-repository pull request into `main`.

The lead must replace every pin below from the final successful candidate before creating that publication pull request. A placeholder or malformed value makes the workflow fail before any release mutation.

| Evidence | Immutable pin |
| --- | --- |
| Approved source commit | `a6d2cd880c9716e96e9957279b4848eb8d3ca076` |
| Approved source tree | `590aede30ca884e6445f1d373fec49c56416c111` |
| Successful Windows CI run | `40001598489` |
| Successful WPF rendering run | `40001598486` |
| Successful package and installer lifecycle run | `40001598491` |
| Package artifact ID | `12523350391` |
| Complete artifact archive SHA-256 digest | `sha256:f44071b256458df954b7d846d038fb2e5490f6fd855bcb43c8540c1de36c63c0` |
| Independently reviewed Setup SHA-256 | `B2E42A2D6A81C78B3AC62356A452D8993782761531A305CEF7DD55D3A856D7C6` |

The workflow checks workflow identities, successful run conclusions, source commit/tree, artifact build tree and the complete downloaded archive digest. It publishes exactly `Flick-Setup.exe`, `Flick-v1.0.5-win-x64.zip` and `SHA256SUMS.txt`, first as a verified draft and then as the new current download. All three unauthenticated public downloads must match their reviewed bytes before the baseline changes. Publication receipts retain source, artifact provenance, file hashes, the new release ID and completion phase. The public notes append exact source/tree/run/artifact/hash evidence and a link to the pinned [code signing policy](../CODE_SIGNING_POLICY.md).

The title remains **Flick 1.0.5 — UNSIGNED COMPATIBILITY PREVIEW** and the semantic tag remains `v1.0.5-preview.1`. GitHub's catalogue `prerelease` flag is false solely to support the owner's requested Latest label. This does not provide publisher trust, remove Windows protections, qualify untested hardware or change the app's preview opt-in and explicit unsigned-install confirmation. The SignPath application has been submitted; approval and a trusted signed release remain unverified.

After full public verification and another fresh catalogue/tag/asset check, only release **422126500** (`v1.0.4-preview.1`) changes to **draft**. Its source tag, release entry and three asset files are preserved for authenticated upgrade CI and reversible recovery. No release, asset or Git tag is deleted or moved.

A separate dependent job has only `contents: read`. After the baseline is hidden, that job uses the same authenticated `gh release download` path as future upgrade CI and verifies the old Setup's exact size and SHA-256. If GitHub does not permit that read-only token to retrieve the draft fixture, the workflow fails; the lead must inspect the result and restore baseline visibility or provide another least-privilege fixture route before claiming the future upgrade gate works. The job cannot change release visibility itself.

| Preserved baseline asset | Asset ID | Bytes | SHA-256 |
| --- | --- | --- | --- |
| `Flick-Setup.exe` | `669859579` | `241947803` | `da1a0bbfb6b1d5803fc0d54387682c24f43d6039bae6a2c999822628e7828a5f` |
| `Flick-v1.0.4-win-x64.zip` | `669859568` | `353057887` | `64ad4c0c755ed3364032acbdbb940125c988b02efbcba599d8ecc5a4d11207da` |
| `SHA256SUMS.txt` | `669859576` | `415` | `fa3fd419cec8821b6f8ac8f32797c70c548f0041096619a52c32ccb012c9630a` |

The preserved tag and release target must still resolve to `f5e701cdacfd57e477a2cced7dcf1e9f3bd835db`; the baseline title and all asset IDs, names, sizes and digests must match. Any unexpected release catalogue, existing candidate tag, changed baseline or failed download aborts the operation.

The workflow deliberately refuses a blind retry after partial publication. Inspect its receipt and current GitHub state first; it does not overwrite assets, move an existing tag, delete the new release or automatically roll back a verified download. Restoring the preserved baseline to public status remains a separate explicit recovery action.

Release notes come from the pinned candidate's `release/PREVIEW-RELEASE-NOTES.md`. Hosted CI validates source, packaging and installer lifecycle behavior; physical NVIDIA, AMD, Intel, hybrid and CPU recording acceptance and trusted SignPath signing remain separately recorded gates.
