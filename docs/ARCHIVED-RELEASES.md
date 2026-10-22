# Flick — Archived public-facing release entries

On October 22, 2026, the owner asked for the GitHub Releases page to show **one clear current download** instead of several superseded versions. Historical source Git tags/commits remain in Git; only the old GitHub Release entries and attached install packages are removed.

**KEEP / current private preview:**
- v1.0.2-preview.2 — Flick v1.0.2 Windows NVIDIA NVENC Preview 2.
- Installer Flick-Setup.exe SHA-256: 9582623cc417dcd48e99fb99b4b56fbff6b781675d4a19c699cf3e8e08148c0b
- Portable ZIP SHA-256: e2c5cd3b7687efd19e117d6189e7897236b01c5edbbe35c2c99c301428c1c036
- Git tag source: 346eca07170f4b4d561044a3898bf789bdf4fc3b

**REMOVE GitHub Release entries only, KEEP original Git tags and Git history:**
- v1.0.2-preview.1 — superseded Inno installer / initial in-app uninstall implementation
  - Setup SHA-256 50b8843e3b49ef0d596454158c8141b8cb262de80af82ecd2a8c155d326738c5
  - Portable ZIP SHA-256 590ba5036bd86fb4b17ca3d7b26aa6e0d87abdd9a71e342e0cbb44c012848327
- v1.0.1-preview.1 — earlier windows installer and RAM preview
  - Setup SHA-256 2bcc81a7053946208cbedd37ca538681cfea543634cf1ed30ca54a74145dffd3
  - Portable ZIP SHA-256 81bc3da2b9d318d5130e10290ef9a2117158de310276cca306f4271b38c14b7d
- v1.0.0 — older ZIP-only release / inspection package
  - LocalClip-v1.0.0-win-x64.zip SHA-256 3eb8affe429c4bce3805ba37e745b852ac12bf23455b2f81175c7a1a36ba8809
  - Full inspection package SHA-256 af1c2d3e6d17bdad202ea2c88290e78fe56ee96c791ccd03c2680a18b78bfc14

**Warning:** Reducing the Release page to one preview does NOT make the repository public or solve Windows Smart App Control. The remaining preview installer is *unsigned*, has limited hardware coverage (Windows 11 x64 + NVIDIA NVENC), and should not be presented as a signed, Smart App Control-qualified public stable app.

The archive action must verify that the newer preview exists with its exact installer digest and SHA before deleting superseded entries. Never clean up source Git tags or repository branches as part of release-list cosmetic maintenance. If the current release digest differs, STOP without changing anything.
