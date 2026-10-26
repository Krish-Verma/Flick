# Capture helper runtime fix

Candidate: 1.0.10-preview.1. This document records the implementation and regression design; executed results belong to the matching CI run and release receipt.

## Confirmed failure

The public 1.0.9 portable archive has SHA-256 `6d1a2d50d870a53b90a6a7c8f218484f8bf4dc634741124fa22a3cc4f6fd0374`. Inspection of its actual Windows import table shows `LocalClip.CaptureHost.exe` imports `libwinpthread-1.dll`; no such DLL exists in the archive. The other 545 inspected DLLs do not import the MinGW compiler runtime. Reinstalling identical package bytes cannot supply the missing dependency. Flick's managed main window can remain open independently of this helper failure.

The native build scanned the static pthread archive before g++ appended its C++ runtime and implicit pthread references, then restored dynamic linking. Static libgcc/libstdc++ flags do not by themselves guarantee a static pthread dependency. All five native build paths now force inclusion of the complete static pthread archive, scoped to that library, before restoring dynamic OBS linkage. The upstream winpthreads copyright and permission text is preserved in docs/licenses.

## Regression gates

- Inspect the actual helper's PE import table and reject unintended MinGW/MSYS runtime DLL imports.
- Prove the gate rejects a deliberately dynamically linked pthread fixture.
- Start the complete helper from a fresh working directory with a child-only PATH limited to standard Windows directories. Require matching ping and shutdown responses and exit code zero within a deadline.
- Run the same noncapture handshake against the assembled portable package and the installed upgraded helper. Require the installed helper's hash to match the packaged helper.
- Preserve imports, hashes, handshake and failure evidence in CI artifacts. Existing installer upgrade/wait/restart/data-retention checks remain enabled.

These checks perform no recording, window/display enumeration, audio capture or GPU initialization. Trusted signing, physical gameplay and hardware acceptance remain separate. Previous releases and tags must remain unchanged when the verified fix is published under the owner's standing Flick delivery authorization.
