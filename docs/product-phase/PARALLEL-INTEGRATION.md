# Parallel delivery and combined acceptance — October24

The owner's latest instruction authorizes complete B/C/D/E lanes in isolated checkouts, with one integrated batch and repair cycle. A4 source `a7b091cc50629ee30dfed1b3b3c94590ec7d397e` is the common base. Public main includes A4 merge `044c63f3970a40192103882fe9d53a8f0a719b21`. No release publication occurs in this integration.

## Preserved feature refs

| Lane | Local final commit | Equivalent GitHub commit | Identical source tree |
| --- | --- | --- | --- |
| B games/strict video | `1cacda8721ef6310ed0887307a3f9d69301fb9ce` | `0133b2b93c6536539000e7433788d09ef3c737f0` | `9ad8b4ab50f4252e45f9eb85cc8dcb2228ccf63d` |
| C process audio/mix | `ff6bab3f94abaac2b9dfd878a49665aa2ff7cda5` | `84cf7ab79e66ff92b9037c61c81a3f0c1c197d0b` | `64b5ef2a743e7fac408dd6dca0ff48045f8eeb98` |
| D library/editor/export | `9e4d39a9a847e980925a653b887952d0026a92ae` | `94e8ee3760157599e059ca623bb5b912587acc56` | `d02102bfd01addb09f8dc6d0076eb3ab2152bf3e` |
| E initial Share | `3e98e1c` | `1ef70f21dee92ba455a3e44f572806f44fd9446b` | `91dd950b063b9ceaa1e0a192718b71cf3d155923` |

GitHub refs are `feat/game-automation-complete-20261024`, `feat/audio-separation-complete-20261024`, `feat/editor-library-complete-20261024`, and `feat/share-complete-20261024`. The connector recreates commits while preserving each exact tree; commit identities differ because parent/author metadata differ. Root retains local lane history and publishes the combined tree with those preserved remote feature parents.

## Integration repairs and review findings

- One canonical settings schema4; game approval remains explicit and schema-checked. Audio and organization did not independently increment it.
- Preserve both exact HWND video and exact PID process audio sources, capability bits, retained identities and five-channel cleanup. Independent compile/link of the combined native source passed against verified OBS32.2.2; only six existing unused-parameter warnings.
- Union game/diagnostic/audio/filmstrip/selected-thumbnail reservations. Selected thumbnails retain all queued task ownership through cancellation and confirmed worker cleanup; capture, self-test, export and final quit await settlement. A temporary stopping gate clears after confirmation, while an actual unconfirmed native exit keeps new work fenced. Manual start rechecks stop ownership after awaits; stop aborts the owned starting helper. Capture waits for audio and filmstrip cleanup. New work stays fenced after unconfirmed cleanup; final shutdown can proceed only when its owned request task has settled.
- Automatic replay refuses a stale/different selected Game Audio process until the owner chooses the current running game in Audio settings; it never silently changes audio consent.
- Carry full executable path and raw process creation time from diagnostic consent through native start, rejecting a changed process instance before starting.
- Export fingerprints preserve both aspect and audio source-role changes. Original MKVs remain immutable. Combined Audio is an alternative mix, never added to its own stems.
- Audio edit persistence reports saving/saved/error states. Recent exports restore their own cap without overwriting the global default.
- Share rejects stale queued thumbnail notifications before cancelling current work. Same-path repair explicitly refreshes the current still. Network/device paths, oversized files and source images over16Mi pixels are refused; final display decode stays480pixels.
- High Contrast retains a distinct keyboard focus outline. Fixed Share actions are outside the scrolling body; actual renderer containment checks include cancellation. The thumbnail is labelled as an original still; fit/canvas behavior is explained.
- Export activity remains indeterminate because production percentages identify stages, not elapsed-time or byte progress.

## Evidence before combined Windows batch

Isolated focused tests: B38, C67, D73 passed; these sets overlap and must not be added into an integrated total. Root rebuilt the combined Core source and ran portable tests: **460 passed,0 failed,17 expected media/hardware skips**, with four Windows-only platform cases excluded. Accepted local receipt: `artifacts/product-parallel/integrated-portable-built/integrated-portable-core.trx`. A prior receipt in `integrated-portable` used an earlier DLL after a failed build and is explicitly rejected in its adjacent `NOT-ACCEPTED.txt`.

Managed WPF and test projects cross-build on macOS with Windows targeting; actual WPF runtime is not available there. The combined Windows batch must execute all available Core/App tests, eight real generated-media workflows with no skips, native/policy/source checks, actual WPF renders and install/update/data-retention checks. New actual-media tests cover selected-mix spectral contribution and fit9:16/1:1 geometry/audio/full decode/original hashes. The renderer creates a genuine production-exported MP4 for Ready Share fixtures; simulated busy/failure states are labelled separately.

## Physical and publication boundary

No new physical capture, endpoint audio isolation, Discord attachment, GPU/gameplay performance, actual Windows scaling/High Contrast toggle or signed trust is claimed. SSH is Session0; A4's approved same-owner bridge failed before child creation with Windows error1314. No privilege/security bypass or installed Flick/profile changes occurred. An owner-launched desktop test remains needed. Public Latest remains unsigned1.0.6-preview.1; a new unique candidate and explicit publication approval remain separate gates.

Five distinct passes remain required: completeness ledger, correctness/state/media, measured performance and limits, actual-pixel UX/accessibility, release/source/install/legal. Each available check needs exact-source evidence before integration acceptance. See workstream implementation records and EVIDENCE.md for detailed scope.
