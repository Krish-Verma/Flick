# Settings reliability and UI cleanup

Candidate: 1.0.9-preview.1. Publication is not authorized by coding approval.

## Cause

The 1.0.8 settings writer already wrote and read back the profile. The failure was earlier: all-or-nothing validation rejected a remembered audio endpoint marked unavailable before discovery completed or after unplugging. That prevented unrelated settings from reaching the writer. Refresh tasks also restored selections captured before awaiting discovery, overwriting newer choices. Closed WPF selectors could change values on wheel while the user intended to scroll the form.

## Change

Preserve unchanged remembered endpoint IDs when saving unrelated preferences. Keep validation for newly selected or newly enabled unavailable devices. Read current device/app selections when refresh completes. Keep failed-save drafts and show concise inline field guidance. Forward wheel movement from closed selectors to the page without editing the preference; open native dropdown navigation remains available.

Keep the existing graphite/purple identity, controls, bindings and features. Use a fixed Settings rail/footer and one selected-page scroll area. Reveal library filters/details only on request, retaining one virtualized clip list. Shorten repeated prose, use consistent sentence case, and retain detailed guidance in tooltips/disclosures and explicit confirmations.

## Verification

Actual WPF regressions exercise Save → isolated JSON → reload, unavailable devices, discovery races, invalid input and failed writes. Render fixtures cover all seven settings pages at normal and minimum sizes, Share states, clip tools, library panels, linked folders, health/consent, rename/details dialogs and save notifications. Windows execution, rendered review and installer evidence must be recorded separately after they run; authored assertions are not passing evidence.

No physical capture, gameplay, audio or Discord qualification is implied by synthetic UI fixtures. No release, tag, Windows security, repository setting or secret changes are part of this coding task.
