# Product phase evidence ledger

Updated 2026-10-24. Planned work is not implementation evidence.

| Slice | Implemented | Automated evidence | Windows/visual | Physical hardware | Integrated/released |
|---|---|---|---|---|---|
| Phase0 | audit and plans | live GitHub inventory; plan CI40087047146 passed | baseline WPF artifact recovered | initial SSH metadata only; later timeouts | PR43 merged842d3b9; no release |
| A1 | health ledger, correlated IPC, immutable provenance and WPF disclosure | Core315 + App5 pass in Windows CI40089065601 and40089526932; media5/5; native/policy/publish pass; local311 portable passes | render40089527110 accepted;23/23 caption fits; wide/compact pixels reviewed; installer40089065499 upgrade/restart/data retention passes | Core attempt refused before launch at1525MiB available; GPU/audio/Discord not verified; SSHSession0 is notdesktopSession1 | PR44 merged01e501d2; PR45 merged8399070d; no release |
| A2 | bounded private journal, local ZIP export, cancellation/shutdown protection | focused35/35; portable346 pass /14 skip, four Windows-only cases excluded; Windows CI40091123538 Core350 +App5; separate media5/5/native/policy/publish pass | render40091123529:27/27 caption checks; four A2 images reviewed, minimum720×480; artifact12559370417 digest verified; treef45c962c matches source2d969e0; installer40091123488 install/upgrade/restart/data retention pass | Windows hosted filesystem tests passed; standard Save dialog not physically operated | PR46 merged9704b91d; no release |
| A3 | private consented test, frozen source/audio, independent cancellation/cleanup, preserved media | local63 focused /409 portable pass; Windows CI40093508148 Core413 +App5 pass;14 explicit hardware/media skips; separate media5/5/native/policy/source/publish pass | render40093508171:33 caption /17 panel checks; eight frames reviewed; artifact12560001175 digest verified; source/render treea3fda7b matches; installer40093508154 install/upgrade/restart/data retention pass | owner PC TRX413 pass /0 fail /14 skips on unchanged Core source8f73ef7; launcher exit-code receipt incomplete; no capture/audio/performance claim | PR47 merged74ac9240; no release |
| A4 | isolated silent-window qualification harness in progress | safe-refusal, fixture/media policy and real Win32 hotkey tests in progress | interactive same-owner token route under verification; no security changes | exact-session controlled capture not yet executed | branchfeat/recording-reliability-a4-20261024; no release |
| B1–B4 | pending | pending | pending | pending | pending |
| C1–C4 | pending | pending | pending | pending | pending |
| D1–D4 | pending | pending | pending | pending | pending |
| E1 | research specification authored | Steam/Moments/Medal primary docs and genuine publisher images inspected in Chrome | digest-verified WPF baseline visually reviewed | not applicable to research; no live competitor operation | integrated through PR43/44; no Share runtime changes |
| E2–E4 | pending | pending | pending | Discord desktop pending | pending |

## Measurement rules

Record exact source SHA, machine/OS/runtime/tool versions, fixture size and warm/cold/cache conditions, elapsed samples and artifact/run links. Synthetic audio tests prove mixing only; process audio capture requires independent real-source tones. WPF render scaling is a fixture test and not physical Windows display-scaling interaction. Skips do not pass physical gates.

## Independent review passes

1. Completeness: pending requirement-by-requirement audit.
2. Correctness: pending state/failure/concurrency/media validation.
3. Performance: pending equivalent before/after measurements.
4. UX: pending actual before/after WPF state renders and interaction checks.
5. Release: pending source/CI/version/install/license audit. No publication authorized by this phase.
