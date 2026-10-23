# Flick Windows Smart App Control — trust and release requirements

## Current status (October 22, 2026)

**The current v1.0.4-preview.1 Setup EXE is unsigned. Smart App Control on Windows 11 may block it. The trust issue is not fixed by the design or updater changes.** A SHA-256 manifest and green GitHub Actions build prove provenance/build integrity, not trusted publisher identity or malware-free status. GitHub's Latest download label is not a Windows trust assertion.

Microsoft says there is no per-app Smart App Control exception. Do not disable the protection, change Code Integrity policy or add Defender exclusions just to run the existing Flick preview.

The separate GitHub failure notification received October 22 at 1:37 PM was from run 39850034448. It was an old Inno uninstall-script compiler error; subsequent fixed runs and releases passed. That email is not evidence of malware and has no causal relationship to the Smart App Control block.

## Real fix — trusted Authenticode signing

A future release must be Authenticode-signed and time-stamped using a valid certificate from a trusted certificate authority, plus pass Windows Smart App Control enforcement testing. The signing scope includes Flick.exe, the libobs CaptureHost executable, all loadable unsigned executable DLL/EXE dependencies, the Inno Setup installer, the Inno generated uninstaller, and transient executable helpers.

Recommended provider: Microsoft Azure Artifact Signing (previously Trusted Signing) with a **Public Trust** profile. An account, organization/person identity verification, signing certificate profile, signer role and Azure subscription are needed. The Basic plan was $9.99/month as of October 2026; consult current pricing. A self-signed key or a test/private profile does not create Windows trust.

Alternative: SignPath Foundation offers free OSS code signing subject to eligibility, public source, active maintenance, review and application approval. SignPath uses its own verified publisher, and acceptance is not guaranteed.

### Repo code already prepared

- scripts/prepare_flick_signing.ps1 validates Public Trust account variables, Azure CLI sign-in, Windows SDK SignTool and official Microsoft signing client DLL, and writes non-secret account metadata outside Git.
- scripts/sign_flick_file.ps1 signs a code file with the configured Public Trust signer and a trusted timestamp. It fails without a real signing provider.
- scripts/build_trusted_flick_preview.ps1 builds the portable package, signs all unsigned executable files, verifies signatures, re-compresses the ZIP, invokes Inno in trusted signed-uninstaller mode, and regenerates SHA-256 checksums.
- scripts/verify_flick_trust.ps1 refuses output if any executable is unsigned/untrusted or if first-party publisher signatures disagree.
- .github/workflows/signed-windows-qa.yml offers a manually invoked and GitHub-environment-protected signing workflow. It uses Azure OIDC, downloads and validates Microsoft-signed client tooling, checks setup/uninstaller signatures and uploads a private test candidate. It does not publish a public release.
- The former unsigned preview auto-publishing job is disabled. Future signed official releases are separate from test-only private artifacts.

**Note: These signing integrations have NOT run without a configured real certificate and Azure account. They must pass their first integrated run and AntiGravity testing before publication.**

### Owner prerequisites

1. Enroll with Azure Artifact Signing and complete the verified identity and Public Trust certificate profile, OR apply to SignPath Foundation. This is an owner-controlled external identity action. Never paste signing secrets/PFX into chats or Git.
2. For Azure create least-privilege Entra workload identity federation and grant certificate-profile signer role for the repository and protected deployment environment.
3. Create protected GitHub environment flick-trusted-signing requiring authorized manual review and main-only access.
4. In GitHub Settings > Secrets and variables > Actions, configure non-secret vars FLICK_SIGNING_ENDPOINT, FLICK_SIGNING_ACCOUNT, FLICK_SIGNING_PROFILE; configure AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID as secrets. Do not share them in commits.
5. From trusted main launch Build Trusted Flick Candidate for Smart App Control QA with the exact manual confirmation SIGN_FLICK_FOR_QA.
6. AntiGravity tests the signed candidate on Windows 11 with Smart App Control **On**, including installer, app, NVENC capture, helper EXE/DLL load, export, and installed uninstaller. Read CodeIntegrity/Operational event IDs 3077 (blocked) and 3076 (audit). If any binary fails, report exact file path and certificate details; do not bypass protections.
7. Verify third-party license compliance and media privacy, then tag and publish a NEW signed release, not a replaced/renamed unsigned installer. Only then mark it Latest stable and allow full public access after changing repo visibility through GitHub admin Settings.

### Affected PC inspection

In Windows Explorer, right-click Flick-Setup.exe > Properties > Digital Signatures. No tab often means unsigned. Alternatively PowerShell:

    Get-AuthenticodeSignature 'C:\Users\...\Downloads\Flick-Setup.exe' | Format-List Status,SignerCertificate,TimeStamperCertificate

Use only trusted sources when interpreting the result. The official signed release will have an actual signer/certificate and trusted timestamp; changing the download filename cannot provide this.

Sources:
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control
- https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations
- https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm
