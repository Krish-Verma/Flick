# Local recording diagnostics

Flick records a bounded local journal when an actual application session opens. Opening an unshown WPF fixture does not start logging. Logging is independent of replay and cannot arm recording. There is no automatic upload.

## Contents and retention

The journal accepts closed enums, booleans, bounded measurements, timestamps and a structured numeric application version. Events describe engine transitions, actual encoder configuration, save requests/acknowledgments/validation/failures, and shortcut registration outcomes. Initial shortcut results are included even though registration precedes asynchronous startup.

No raw host output, exception messages, settings, window titles, executable names, paths, clip IDs, recordings, credentials or webhook configuration enter the diagnostic model. Private error details remain in the local health UI; only classified error codes enter a report. Successful-save provenance uses the saved session's immutable source kind and encoder profile, including if a new session has begun.

Retention is at most 256 events, seven days and 256 KiB of JSON. Producers append to bounded memory; a coalesced background writer persists event transitions, not frames or routine status polls. Normal quit fences a flush with a three-second deadline. Storage failure cannot prevent recording or require waiting indefinitely.

## Export and recovery

Open recording health and choose **Export Diagnostics**. The Windows Save dialog chooses the ZIP destination and confirms replacement. The ZIP contains exactly `diagnostics.json` and a static `README.txt`; it does not collect files from the recording folder. The export runs asynchronously and leaves replay and the health Close control available. An existing report is preserved if cancellation occurs before atomic promotion.

An unreadable, oversized, unsupported or externally modified journal is preserved. Flick continues with bounded in-memory diagnostics and can export that session. It does not silently reset the original file or repeatedly retry failed writes. A future valid app session can reopen an accessible log; there is no destructive recovery button in this slice.

The journal checks ownership before replacing its own file, rejects reparse-point store locations and cannot export over its JSON store. It is a single-application journal, not a cross-process transactional database. It does not claim protection against a hostile program racing a filesystem replacement.

## Verification boundary

The A2 focused suite contains 29 real temporary-filesystem journal cases and six pure policy cases: privacy canaries, strict input/schema/version checks, immutable save provenance, age/count/byte bounds, corrupt-file and dangling-link preservation, inaccessible storage, external modification, concurrent flush fences, export cancellation and version provenance across app upgrades. The Mac run passed all 35 cases; Windows CI executes the same cases. Only the two link-specific cases skip if Windows explicitly denies symbolic-link privilege. The full solution and renderer cross-build on Mac without warnings. Cross-building does not execute WPF.

Four additional real WPF fixture states cover the health export footer at wide, 800×600 and minimum 720×480 sizes, long errors, overflow, busy export and scrolling. The existing single-Application STA test checks accessibility, focus/busy bindings and that unshown fixtures never initialize a journal. Windows results and exact source/run references belong in the evidence ledger and PR. Standard Windows Save dialog interaction and physical capture are separate from these fixtures.

No release is published by this slice. A3's consented capture self-test and A4's recovery/qualification harness remain separate work.
