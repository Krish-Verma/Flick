# Flick next product phase — master engineering plan

Updated 2026-10-24. Authoritative umbrella: [#36](https://github.com/Krish-Verma/Flick/issues/36). Direct owner brief approves implementation of A–E; this supersedes the earlier roadmap's planning-only status. Public release publication still requires fresh explicit approval.

## Verified starting point

- Public Latest: **v1.0.6-preview.1**, source `d63043519eb7348d32efd129835cad9c2bb26fe7`; source tree `74f054ae3a3a69b51e8dd147ccd3cead8badce5e`. Unsigned preview, not fully hardware-qualified.
- Main `46c3ed50cdc36049c2359b4817a0a94b9882270b` preserves that source and release evidence via #37. New isolated branch `feat/product-phase-master-20261024` starts here. Existing local main has unrelated history and is preserved.
- Open draft PRs17–28 are historical UI/publisher stacks. They are not new competing implementation lanes. Keep their refs/releases unchanged. Issues14/34 signing/Store remain separate; issue15 licensing audit remains a release qualification dependency.
- Windows WPF/.NET10 frontend; native OBS32.2.2 host; pinned FFmpeg9.0.2; atomic local settings/index; nondestructive trim/audio; real guarded MP4 clipboard/FileDrop. No account, telemetry or cloud service is introduced.

## Audit findings and decisions

1. Save success already follows file existence, positive ffprobe media and index persistence. Preserve that boundary. Native acceptance is not success.
2. Overwritten status text loses errors; polling overlaps, swallows failures and can start a replacement helper during health queries. Duplicate replay terminal events can notify twice. A finishing save can inherit a newly selected source. A1 addresses these before game automation.
3. Native audio currently separates whole-system loopback and optional microphone, not game/voice processes. OBS has `wasapi_process_output_capture`; its Windows capability and actual hooked identity must be verified. Device loopback cannot honestly isolate Discord/game, and a track label is insufficient evidence.
4. Manual sources use ordinary window APIs and exact native window identity validation. B extends these with opted-in approved profiles and deterministic lifecycle policy; never monitor/desktop fallback, never injection.
5. Library JSON lacks favorite/tag/game/marker fields; search does synchronous filesystem queries. Extend schema without losing old/imported/offline records. Keep virtualized gallery and bounded lazy thumbnails.
6. Share lacks clip preview metadata and an obvious large drag target. Preserve its existing last-mile MP4 validation, strengthen presentation and test actual file data.

## Sequence, dependencies and complexity

| Sequence | Work | Complexity | Main risk / acceptance gate |
|---|---|---|---|
| 1 | A1 health/state/correlation, A2 private logs, A3 self-test, A4 recovery | medium/high | stale events, saves during stop, false success, privacy |
| early research | E1 primary research and WPF baseline | low | screenshot fixtures are not hardware capture |
| 2 | B1–B4 approved game profiles and serialized automation | high | accidental desktop capture, restart identity, manual override |
| 3 | C1–C4 process audio and honest fallback/mixing | high | Windows/OBS hook capability, process lifetime, double mixing |
| 4 | D1–D4 organization, editing and exports | medium/high | index migration, stale async state, decode/cache budget |
| 5 | E2–E4 Share preview/native transfer/accessibility | medium | ready/stale states, real Discord file transfer |
| 6 | cross-feature regression, five reviews, candidate preparation | high qualification | hardware/performance/signing and publication gates |

Owner steering on October24 supersedes the earlier serial coding rule: implement B, C, D and E concurrently in isolated managed checkouts, each lane responsible for its complete functional workstream and focused tests. Root owns E, shared integration and all final acceptance. B/C/D may change shared files only inside their own checkout; root merges them serially and resolves contract conflicts. Run focused builds/tests during coding, then batch the integrated Windows, media, visual, installer and applicable physical tests; fix findings and repeat affected checks. This reduces repeated full package compression per small slice. Do not mistake an isolated lane pass for integrated acceptance or release qualification.

## Workstream plans and tracked slices

- [A: recording health, #38](A-plan.md) — A1–A4
- [B: game detection, #39](B-plan.md) — B1–B4
- [C: audio separation, #40](C-plan.md) — C1–C4
- [D: editing and organization, #41](D-plan.md) — D1–D4
- [E: Share screen, #42](E-plan.md) — E1–E4

Each plan defines objective, existing/new behavior, dependencies, likely files, implementation, acceptance, automated/manual tests, performance, evidence and limitations. Workstream issues mirror checkbox slices as equivalent tracked milestones; exact commits/results live in [EVIDENCE.md](EVIDENCE.md).

## Verification baseline and available environment

Existing CI: Windows warnings-as-errors WPF build, unit tests (hardware tests explicitly skip), five FFmpeg synthetic media workflows (full decode/size), native Windows compile, portable encoder policy tests; separate real WPF fixture renderer; installer/update preservation smoke. Baseline source run40084437463, render40084437461, package40084437470. Recover exact artifacts/logs before comparing counts.

Baseline render artifact12556043044 SHA256 `0a77fb4f…f3ac35` was recovered and independently hash-checked. Its PROVENANCE reports commit0a6fa3f6e366f53c39504ae35be9e3dc05e13a65 while workflow head is d630435; application/VM/theme/renderer source diff to current main is empty. These are synthetic WPF fixture screenshots, with no hardware/media recording. See research record for full hash/provenance.

Current execution host is macOS ARM64. A task-local hash-verified pinned .NET SDK enables portable Core tests/cross-builds; WPF/Win32 interaction executes in Windows Actions. Owner-authorized SSH access to KRISHPC is confirmed: Windows11, RTX5060, SDK10.0.402. The SSH process runs in Session0 while the owner's desktop runs in Session1, so SSH alone does not qualify interactive capture/Discord. A1's isolated Core package was transferred and hash-verified, but tests were refused before launch when available RAM fell to1525MiB. A later owner-authorized Linux cache reclaim reduced WSL cache6754→259MiB and Windows available RAM rose4223→9219MiB; no processes or WSL were stopped. The installed Flick1.0.5 process/profile remain untouched. Recheck resources before any further PC execution, and use only isolated temporary roots. Owner-PC Core tests subsequently executed:413 passed,0 failed,14 expected hardware/media skips. The launcher exit-code receipt remains incomplete; a second attempt stalled before launching tests. Both owned launchers were closed with no owned test processes left and installed Flick19396 unchanged. A4 adds a separate synthetic-window qualification harness before any real capture. Never infer NVIDIA/AMD/Intel behavior, gameplay FPS, endpoint isolation or Discord desktop acceptance from hosted CI.

## Performance and privacy budgets

No new persistent webviews, overlays, injection, telemetry, upload or background media decoding. Disabled game detection performs no scans; enabled detection is low-frequency and serialized. Diagnostics are bounded event-only, allowlisted/redacted and user exported. Thumbnails are lazy, cancelled on rapid switch and capped. Originals are immutable. Measure equivalent fixtures and record actual CPU/RAM/start/readiness/save/export latency; hardware FPS/1% lows/GPU remain unverified without a PC.

## Review and release policy

Perform distinct completeness (requirement ledger), correctness (state/race/failure/media tests), performance (measured traces), UX (renders/interactions/accessibility) and release (source/version/install/license/CI) reviews. Fix findings and retain evidence; rereading source alone is not independent validation.

Merge only after applicable available checks pass. Prepare a new unique preview candidate version after integrated gates; never overwrite published1.0.6 or reuse its tag. **Do not publish a new public release or replace Latest without explicit owner approval.** No stable/signed/universal-hardware claim. Signing/Store identity remain separate. Any physical acceptance blocker gets exact reproduction, owner and reason; implemented/tested/hardware-verified/released are separate statuses.


## Active parallel lanes (owner-authorized October24)

- B game detection and strict video target: `flick-game-automation-20261024`, agent logo_render.
- C real audio routing and editor/export control: `flick-audio-separation-20261024`, agent update_core.
- D library, timeline and export workflow: `flick-editor-library-20261024`, agent update_ui.
- E Share and cross-feature integration: `product-improvements-20261024`, root.

All start from reviewed A4 source `a7b091cc50629ee30dfed1b3b3c94590ec7d397e`. No lane publishes releases or merges independently. Feature branches/commits and focused evidence are retained; root batches full acceptance on the final combined source. A4's current-head Windows CI passes and its separate package is ready for controlled physical qualification.
