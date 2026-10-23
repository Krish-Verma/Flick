# R7 — One-window confirmations and consistency

All application MessageBox calls and the separate Rename Window are replaced with a shared themed layer in MainWindow. Source reset, mic opt-out, uninstall, defaults, folder import/unlink, deletion and webhook send keep their explicit consent. No/Cancel receives initial focus for consequential actions; Escape cancels, Tab cycles within the prompt, and focus returns afterwards. Rename still changes only library metadata. OS pickers, actual Windows uninstall and external players remain provider-owned exceptions. The short-lived, non-activating save toast remains an intentional notification surface.

The existing synchronous source-property confirmation contract uses a nested WPF dispatcher frame, as native modal prompts did: capture/IPC can continue while only main controls and global shortcut action dispatch are suspended. Nested prompts fail closed rather than stacking. No new window, keyboard hook, timer, media decoder or service process is added. This requires on-device reentrancy and tray interaction QA, not just compilation.

Full real Windows screenshots (100/125/150/200%), high contrast, reduced motion, focus, Snap, capture, preserved original hashes, actual FPS/1% lows, RAM/handles and approved soaks remain unrun here. R7 source implementation does not certify those hardware outcomes. Use the consolidated R3–R8 handoff.
