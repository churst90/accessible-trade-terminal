# Releasing

Releases are built and published by `.github/workflows/release.yml`. It produces
binaries for the two shippable heads and attaches them, with checksums, to a
GitHub Release.

## What gets built

| Artifact                                              | Head    | Runner  | Notes |
|-------------------------------------------------------|---------|---------|-------|
| `AccessibleTrader-WebHost-<ver>-linux-x64.zip`        | WebHost | Ubuntu  | Self-contained, no .NET needed |
| `AccessibleTrader-WebHost-<ver>-win-x64.zip`          | WebHost | Ubuntu  | Self-contained |
| `AccessibleTrader-WebHost-<ver>-osx-x64.zip`          | WebHost | Ubuntu  | Self-contained (Intel Mac) |
| `AccessibleTrader-WebHost-<ver>-osx-arm64.zip`        | WebHost | Ubuntu  | Self-contained (Apple Silicon) |
| `AccessibleTrader-Windows-<ver>-win-x64.zip`          | MAUI    | Windows | Unpackaged native app, **unsigned** |
| `AccessibleTrader-macOS-<ver>-universal.zip`          | MAUI    | macOS   | Mac Catalyst `.app`, universal, **unsigned** |
| `SHA256SUMS.txt`                                       | —       | —       | Checksums for every zip |

The **WebHost** is the cross-platform Blazor Server desktop head: unzip, run the
`AccessibleTrader.WebHost` executable, and it serves the terminal on a local
Kestrel port and opens your browser. Flags: `--no-launch` (don't auto-open the
browser), `--demo` (read-only demo mode).

The **MAUI** heads are the native Windows / macOS apps.

## Cutting a release

```bash
git tag v1.0.0
git push origin v1.0.0
```

The push triggers the workflow. When all jobs finish, the release appears at
`https://github.com/churst90/accessible-trade-terminal/releases`.

## Dry run / first-time validation

The WebHost jobs are robust. The two MAUI jobs depend on the `maui` workload and
on publish-output paths that can shift between SDK versions — validate them with
a throwaway pre-release tag before the first real release:

```bash
gh workflow run release.yml -f tag=v0.0.1-test
# ...or push a real tag: git tag v0.0.1-test && git push origin v0.0.1-test
```

Watch it with `gh run watch`. If a MAUI job fails on the "Locate + zip" step,
the publish succeeded but the artifact path differs — adjust the glob in that
step and re-run.

## Known limitations

- **Unsigned MAUI binaries.** Windows SmartScreen and macOS Gatekeeper will warn
  on first launch. Add code-signing certs as repo secrets and wire them into the
  MAUI jobs to remove the warnings.
- **Dot Pad tactile SDK is not bundled** (gitignored vendor binaries, ~850 MB).
  Builds succeed without it; Windows tactile-display support is disabled at
  runtime until a user installs the SDK per `docs/PLATFORMS.md`.
- **No Linux MAUI head** — MAUI has no Linux target. Linux users run the WebHost.
