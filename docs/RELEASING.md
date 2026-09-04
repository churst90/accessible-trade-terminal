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

## Documentation is part of the change, not part of the release

**Update the docs with every set of fixes, not once at tag time.** Standing rule, Cody,
2026-09-04 — and the reason it is written down is that this repo broke it: `CHANGES.md` had
never been given a `## [2.5.0]` header and **eighteen behaviour-changing commits carried no
changelog entry at all**, because each session had written itself up in `docs/TODO.md`'s
START HERE block instead. **A START HERE block is not a changelog entry, and writing the
first one makes it feel as though the second has been written.**

Four files, four different jobs:

| File | Holds | Written for |
|------|-------|-------------|
| `docs/TODO.md` | What is still to do, and the open design questions | The next session |
| `docs/CHANGES.md` | What was done, with its caveats — one entry per set of changes | Anyone tracing a behaviour to a release |
| `docs/WHATSNEW.md` | The CURRENT release only, in plain language | A user who just updated |
| `README.md` | What the terminal is and how to get started | Someone who has never seen it |

An item moves TODO → CHANGES when it lands, carrying any caveat with it. `WHATSNEW.md` is
the current release's story, drawn from the CHANGES entries since the last tag; history stays
in CHANGES. The technical reference is `docs/README.md`, and the root `README.md` is the
GitHub front page — general reader, no build numbers in it that can drift.

Run `python3 scripts/check_doc_drift.py` before committing. It checks the shortcut table, the
plugin count and the test count against the code, and it is the same script CI runs.

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
