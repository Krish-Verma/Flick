# Flick 1.0.5-preview.1 release readiness

## Scope and current state

The development candidate is tracked by PR31: https://github.com/Krish-Verma/Flick/pull/31. The public Latest download remains 1.0.4-preview.1 until a separately verified publication replaces it. The candidate is not a signed general-availability release.

Implemented: hardware encoder negotiation and bounded CPU fallback; actual encoder/profile feedback; export fallback; complete pinned OBS runtime checks; packet-based replay readiness; target-loss checks; save identity and graceful quit; damaged-index preservation; imported display orientation; exact FFmpeg9.0.2 binaries/notices; consistent product/version resources; installed update handling and previous-version upgrade qualification.

## Evidence boundaries

The earlier PR31 source e39626d658b60d880db43f7002c7779bb92b0348 passed Windows CI39998014728 (255 hardware-independent tests,9 hardware tests skipped), WPF rendering39998014809, and package/installer lifecycle39998014729. Those results do not qualify the later save, storage, media and packaging hardening changes.

The final candidate must pass the Windows solution build with warnings as errors, direct/transitive dependency audit, expanded unit tests, native compilation, portable native policy tests, five real FFmpeg synthetic-media tests with no skips, WPF render fixtures, exact packaged payload/version/hash verification, and a public1.0.4→candidate installer upgrade/restart/data-retention test. Results will be recorded after execution, with source identity.

## External acceptance still required

- SignPath Foundation approval/project roles and origin-verifiable workflow configuration. No approval or publisher identity is assumed. See SIGNPATH-RELEASE-SETUP.md.
- Authenticode verification of the complete signed distribution, signed Setup/uninstaller, and physical Smart App Control acceptance. The existing strict trust audit must not be bypassed by signing unrelated upstream binaries.
- Fresh recordings and decode/audio/framing review on NVIDIA, AMD, Intel integrated, Intel Arc, hybrid graphics and forced CPU fallback; negative fallback-disabled test. See WINDOWS-GPU-COMPATIBILITY.md.
- Clean Windows prerequisite installation, actual gameplay FPS/1%low and resource measurement, high-DPI/accessibility review and long-running/repeated-save acceptance on intended hardware.
- Corresponding-source and component-license obligations for the exact bundled OBS/FFmpeg/dependencies. Preserving notices and publishing source links alone does not certify every redistribution obligation.

An unsigned preview may be explicitly published for testing under the owner's existing authorization. It must retain the preview version and unsigned disclosure and must not claim all-PC compatibility, trusted publisher status or completed hardware qualification.
