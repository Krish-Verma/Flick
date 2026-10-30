# Flick 1.0.11 Preview 1 — Replay and UX (UNSIGNED)

- Subtle, smooth highlights across clickable controls, with keyboard focus, high-contrast and reduced-motion support. The purple identity and existing features are preserved.
- Successful Settings Save returns to the clips dashboard. Invalid fields and failed writes keep your changes available.
- Edit starts shortcut recording directly in its box. A valid shortcut returns to the read-only view automatically; the main Save Settings applies it.
- Windows startup is repaired and independently verified on every enabled Save, including when the checkbox was already on. Windows-disabled startup is reported without overriding your Windows choices.
- **Keep replay ready in the tray** starts your saved, available capture source for tray/background and Windows-startup launches. It is enabled by default and can be turned off in General. Ordinary foreground launches still wait for a manual start or an approved game. Microphone and app-audio choices retain their existing opt-ins.
- Approved-game detection scans immediately, recovers after temporary window loss and no longer treats earlier automatic starts as permanent pauses. Manual Stop pauses automation; Start Replay resumes it. Replay is shown ready only when its buffer can actually save.
- A memory safety stop also pauses automatic replay until an explicit retry, preventing an immediate restart loop.
- Opening Flick again restores the tray instance. Tray Settings opens a visible window. Cleaner source/date titles replace internal save IDs in the library; original files, imports and custom names stay intact.

Install **Flick-Setup.exe** over the current installation after quitting Flick. Portable users should replace the complete extracted folder. Clips and preferences are retained. All previous releases, assets and tags remain available.

This is an unsigned preview. Physical gameplay, GPU/audio, Discord and endurance testing remain separate from automated startup, policy, UI and installer checks.
