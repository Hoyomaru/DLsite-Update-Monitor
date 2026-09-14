# DLsite Update Monitor

A Playnite 10 plugin for monitoring DLsite distribution-state changes for locally managed doujin games.

## Current implementation status

The implementation now includes:

- Playnite-independent tracking data model
- strict DLsite URL/product-ID resolver
- update-information and file-size normalization
- fixture-tested DLsite HTML parser
- acknowledged-vs-current snapshot comparison
- two-axis monitoring/check-health state model
- atomic `tracking.json` persistence with backup/recovery
- sequential HTTP client with request spacing, timeout, retry and `Retry-After`
- product snapshot cache
- end-to-end `UpdateCheckService`
- minimal Playnite `GenericPlugin` adapter
- manual all-game and per-game checks
- progress/cancellation
- same-product request deduplication inside a batch
- remote-observation reuse separated from per-game tracking health
- HTTP-200 DLsite product-unavailable page detection
- explicit protection against changing a tracked game to a different RJ/RE/BJ/VJ without reset
- reversible Playnite tag integration (disabling tags removes plugin-owned state tags)
- link diagnostics
- acknowledge / ignore / reset operations
- plugin-owned Playnite tags only
- regression tests for Core safety rules and orchestration

The Core suite currently expands to **63 executable test cases** (49 Facts + 14 Theory data rows). Windows validation is automated by `tools/Validate-Build.ps1`.

The rich result/history window is not implemented yet. The current Playnite UI intentionally stays minimal; the v0.1.0 runtime and packaged-extension validation are complete.

## Detection contract

v1 monitors only two DLsite signals:

1. `更新情報`
2. `ファイル容量`

It does **not** claim to determine the locally installed game's exact version.

Every comparison is made against `AcknowledgedSnapshot` — the last DLsite state the user marked applied or ignored. Network/parser failures never replace acknowledged/current snapshots and never clear an existing pending update.

The first successful observation creates a baseline and means **監視開始**, not "latest version confirmed".

## Build

See [BUILD.md](BUILD.md).

## Validation status

Version **0.1.0** has completed the Windows build/test gate, all staged Playnite runtime gates through the full 33-game library, Playnite Toolbox packaging, and `.pext` installation validation. The release package SHA-256 is recorded in `RELEASE_STATUS.md`.

See `RELEASE_STATUS.md` for the final release record and `RELEASE_CANDIDATE_STATUS.md` for the preceding RC record.

## Validation entry points

- `tools/Validate-Build.ps1` — mandatory restore/test/build/payload-validation gate on Windows
- `tools/Validate-Build.cmd` — CMD wrapper for the same gate
- `tools/Install-Dev.ps1` — development install with backup-before-replace behavior
- `docs/SMOKE_TEST.md` — staged Playnite runtime validation
- `RELEASE_STATUS.md` — final v0.1.0 validation and release record
- `RELEASE_CANDIDATE_STATUS.md` — preceding release-candidate record and packaging instructions
- `tools/Package-Release.ps1` / `.cmd` — package the already-validated payload with Playnite Toolbox
- `MILESTONE3_STATUS.md` — previous verification milestone
- `MILESTONE2_STATUS.md` — previous milestone record
