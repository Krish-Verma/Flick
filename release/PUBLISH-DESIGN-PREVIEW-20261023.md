# Approved Flick 1.0.4 publication

The owner approved the final design, requested a current Windows download in GitHub Releases marked Latest, and requested removal of the older Release entries after verification. Source tags and history remain.

- Approved source: `f5e701cdacfd57e477a2cced7dcf1e9f3bd835db`
- Main integration: `10ecff8a682ccc663a4dcf65ccb738b1e412e090` (same tree)
- Source tree: `d5815f0a3cf596ec39b4f535e8017a1597e09f3d`
- Windows CI: 39993642384, 223 tests passed and 7 hardware tests skipped
- WPF rendering: 39993642984
- Packaging and installer lifecycle: 39993642339
- Artifact: 12518963077, `sha256:abc718c7df1b4509e8eea7c7bc82ab006a2393a46aa07af5624205a7e90baba5`
- Setup SHA-256: `DA1A0BBFB6B1D5803FC0D54387682C24F43D6039BAE6A2C999822628E7828A5F`

The workflow verifies the actual archive digest, all asset digests and unauthenticated public downloads before deleting only the three Release IDs in `release/archived-releases-20261023.json`. The GitHub Latest catalogue flag is separate from the visible UNSIGNED PREVIEW title and semantic preview version; the app retains explicit preview-channel and unsigned-install consent. Physical Windows/GPU acceptance and trusted signing remain pending.
