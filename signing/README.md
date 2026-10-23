# SignPath configuration preparation

Inactive templates, checked against official documentation on October 23, 2026.
Unsigned preview releases continue. These files create no account, accept no
terms, install no GitHub App, submit no signing requests and activate no workflow.
Owner enrollment, email/terms consent and provider approval remain prerequisites.
The existing Azure signing scripts are a separate path.

## Exact artifact contracts

| Template | Uploaded Actions artifact contents | Signing scope |
| --- | --- | --- |
| `owned-application.template.xml` | Four files at the exact package-relative paths in the XML | `Flick.exe`, `Flick.dll`, `LocalClip.Core.dll`, native capture host |
| `outer-setup.template.xml` | `Flick-Setup.exe` at archive root | Outer installer PE only |

Both use a ZIP root, required single matches, `ProductName=Flick` and the same
required `productVersion` parameter. For this candidate its value is exactly
`1.0.5-preview.1`, without a `v` prefix or source-revision suffix. The build now
aligns managed/native/Setup metadata in source; compiled PE checks are still
required in Windows CI. Read the value from the reviewed canonical release
version and assert every compiled PE matches before submitting. Reject wildcard
characters or a caller-supplied substitute version. SignPath supports these
[metadata restrictions](https://docs.signpath.io/artifact-configuration/reference#file-metadata-restrictions)
and [required parameters and ZIP structure](https://docs.signpath.io/artifact-configuration/syntax).

Upload the staged files themselves with their shared package root preserved;
uploading a prebuilt ZIP would add another archive layer. Restrict staging and
returned files to the exact allowlist, reject duplicate archive paths, preserve
all upstream executable hashes, and regenerate release checksums after signing.
The XML selects signing targets; it is not a replacement for that inventory
check. Never add recursive EXE/DLL signing. Foundation requires common product
metadata and project-owned signing inputs; its rules allow upstream OSS files
inside packages without re-signing them under Flick's identity.
[Foundation terms](https://signpath.org/terms)

## Account values that are deliberately unresolved

Record the approved organization ID, project slug, signing-policy slug and two
artifact-configuration slugs in protected release configuration. Import each XML
under its own explicit provider slug after provider review. Store a least-privilege
submitter token as the `SIGNPATH_API_TOKEN` secret, never in this folder. Configure
the repository, origin/branch policy, human approvers and GitHub App with the
provider. Eligibility of the .NET apphost and generated Inno uninstaller needs
explicit confirmation; neither is made eligible by changing its metadata.

The documented submission action is
`signpath/github-action-submit-signing-request@v3`; pin its reviewed commit before
activation. It requires `api-token`, `organization-id`, `project-slug`,
`signing-policy-slug`, and the `artifact-id` returned by the preceding Actions
upload as `github-artifact-id`. Supply the explicit `artifact-configuration-slug`,
the following parameter block, `wait-for-completion: true`, an approval timeout
and a separate `output-artifact-directory`:

```yaml
parameters: |
  productVersion: "1.0.5-preview.1"
```

Keep origin-related
permissions at `actions: read` / `contents: read`, with all prerequisite jobs on
GitHub-hosted runners. Retain the returned signing-request ID, source SHA, run ID
and artifact digests. [Official GitHub integration](https://docs.signpath.io/trusted-build-systems/github)

## Inno's two-compilation uninstaller boundary

1. In an isolated Windows build, use `SignedUninstaller=yes` without a SignTool
   and a run-specific `SignedUninstallerDir`. Inno emits a uniquely named cache
   EXE and prompts for signing. A headless staging adapter must recognize only
   this documented outcome, validate the exact generated file, and reject other
   compiler failures. That adapter is not implemented here.
2. Submit only a provider-approved generated stub through its verified Actions
   origin, with a separately reviewed exact-path/metadata configuration. Record
   its unsigned digest, approved signing result, source, compiler and settings.
   Restore the verified signed file to that same isolated cache and compile
   again with unchanged Inno/icon/version settings and signed application files.
   Changes to those inputs can require a different cache file.

This cache workflow is documented by Inno's
[SignedUninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
and [SignedUninstallerDir](https://jrsoftware.org/ishelp/topic_setup_signeduninstallerdir.htm).
Do not reuse an unproven local/cache stub, invent a generic uninstaller wildcard,
or use direct local API upload to bypass Foundation's origin checks. A cache
generated outside the accepted hosted workflow lacks the required build evidence.
Provider acceptance of the stub and its exact metadata contract remain open, so
no uninstaller configuration or runnable signing workflow is supplied.

After the second compile, submit the outer Setup with its own template and
approval. A `<pe-file>` does not recursively sign an Inno payload. Verify the
returned app files and outer Setup, install into a disposable directory, inspect
the installed `unins000.exe`, and test upgrade/uninstall/data preservation.
First-party signing alone does not establish full dependency trust or Smart App
Control acceptance. See the full [release setup plan](../docs/SIGNPATH-RELEASE-SETUP.md).

## Validation status

Both XML files pass local well-formedness checks; their elements and attributes
were checked against the public
[SignPath v1 XSD](https://app.signpath.io/Web/artifact-configuration/v1.xsd).
Full XSD validation is pending: macOS's bundled libxml rejects two regular
expressions in the provider's schema before validating either template. Validate
in the provider's editor or a compatible schema engine before activation. No
account import, policy evaluation, signing request, signed-result check or
Windows installation has been performed for these templates. Keep them inactive
until those gates and the uninstaller boundary are resolved.
