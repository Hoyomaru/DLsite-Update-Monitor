# DLsite Update Monitor 0.1.0 — Final release status

## Final status

Version **0.1.0** has completed the planned build, automated test, staged runtime, full-library, packaging, and packaged-extension installation gates.

This document records the validated release state. No source-code change was made after the runtime-validated Milestone 3.2 payload; the only post-validation tooling change was replacing `Get-FileHash` in `tools/Package-Release.ps1` with a SHA-256 implementation based on `System.Security.Cryptography.SHA256` for compatibility with older Windows PowerShell environments.

## Build and automated-test gate

`tools\Validate-Build.cmd`: **PASS**

Validated by that gate:

- restore succeeded
- 63 Core test cases passed
- `net462` Playnite plugin build succeeded
- required payload files were present
- private copies of `Playnite.SDK.dll`, `AngleSharp.dll`, and `Newtonsoft.Json.dll` were absent
- validated payload was written to `artifacts\plugin`

## Runtime validation

Staged validation completed successfully against the 33-game DLsite-linked Playnite library:

- Gate A — extension load, menus, settings: PASS
- Gate B — DLsite link diagnosis: 33 / 33 normal
- Gate C — first observation created baseline; repeat check reported no change: PASS
- Gate D — temporary network failure preserved existing baseline; recovery returned to no-change state: PASS
- Gate E — small-batch behavior: PASS
- Gate F — full-library check: PASS

No mass false positives, destructive metadata changes, or baseline-loss behavior were reported during the staged validation.

## Release package

The validated payload was packaged with Playnite Toolbox and then installed successfully as a `.pext` package.

Package name:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_0_1_0.pext
```

SHA-256:

```text
c84d8fbb3fb82e5d3d5c6bd974c153b33dd8437ff96f4447a4ed0c68c7a939bf
```

The packaged-extension installation test also passed, including preservation of existing tracking state.

## Release decision

**DLsite Update Monitor v0.1.0 is approved as the first stable release.**

The intentionally conservative v0.1.0 scope remains unchanged: DLsite-only monitoring of `更新情報` and `ファイル容量`, acknowledged-snapshot comparison, manual checks, safe persistence, and plugin-owned tags. Automatic downloading/patching and local executable-version inference remain out of scope.
