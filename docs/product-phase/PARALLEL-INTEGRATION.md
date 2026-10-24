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

## Combined Windows batch and review repairs

Equivalent integration head `a4d714375155cc730fa4ea4cdacb9cc7996ca5e9`, tree `d7f22b072d3896063bbcf13327f82bb4f74a5bfa`, passed [Windows CI40101316960](https://github.com/Krish-Verma/Flick/actions/runs/40101316960): Core467 passed/17 explicit hardware-media skips; App9 passed/0 skipped; all8 generated-media cases passed; native, policy, source guards,20 non-capture harness checks and publish build passed. [Renderer40101317193](https://github.com/Krish-Verma/Flick/actions/runs/40101317193) rendered all64 numbered frames plus raster scales; downloaded artifact12563085788 SHA256 `b825df02c8661b052a43db520ba2dffe175cc2f86d735df1cba797660f2b48c7`. Its test-merge commit7a4d5ef has the identical tree. All16 required Share binding observations match actual current values, with no UpdateTarget/rebinding. The genuine MP4 is1,557,658bytes/3.021seconds and fully decoded; original/profile integrity passed. External Discord/DPI/capture remains separate.

The first batches exposed fixture defects rather than a get-only property restriction: WPF marshals worker binding notifications at ContextIdle, which the old DataBind/Render-only fixture starved. Bounded genuine dispatcher pumping plus an isolated STA regression fixed it while retaining strict UI assertions. Earlier source-guard and ambiguous Settings-footer selectors were repaired, and the minimum Share viewport grew from33 to235DIP using the actual available editor area.

Independent pixel/source review then found four concrete usability defects: reverting drafts retained an unsaved caption; expanded details crowded out the virtualized library; minimum Settings categories were unreachable; and availability refresh left Export enabled despite an offline original. The next PR49 repair adds targeted assertions, sibling header/details scrolling with a96–160DIP list reserve, category BringIntoView checks, explicit live-status labeling, derived export/edit notifications and extra minimum screenshots65–67. These later changes require the latest PR49 batch; the prior64-frame run does not qualify them.

Installer run40100257099 (tree0054e08b, before fixture/UX followups) passed real1.0.4→1.0.7 upgrade, same installation identity/path, exact parent wait, restart, retained sentinel/uninstall allowlist and four owned product-version checks. Small evidence artifact12562642666 was rehashed: `0cd0a73cff2c01d2be41e703eb5b252ad64bf6fc08a0f5925619b696cecfd887`. Separate recording/audio packages assembled; their embedded inventories were not independently downloaded in this review, and no capture executed.

Measured six-frame decode evidence from that Windows test batch:345,600retained pixel bytes (337.5KiB), managed before4,055,544bytes/after6,139,104bytes,1.1539235seconds. This is six generated320×180JPEGs retained as frozen160×90images; the managed delta includes concurrent runtime allocations. It is not peak process memory, added FFmpeg memory or gameplay overhead. No equivalent physical before/after CPU/GPU/FPS claim is made.
