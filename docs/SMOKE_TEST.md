# Runtime smoke-test gate

Run this only **after** `tools\Validate-Build.ps1` passes all Core tests and produces `artifacts\plugin`.
Use a disposable Playnite profile or a fresh Playnite backup for the first run.

## Gate A — load only

1. Install with `tools\Install-Dev.ps1` or copy `artifacts\plugin` to a dedicated extension folder.
2. Start Playnite.
3. Confirm Playnite starts without an extension load error.
4. Confirm `DLsite Update Monitor` appears in the main menu and game context menu.
5. Open plugin settings and press Save without changing values.

**Pass condition:** no crash, no extension error, no unexpected game metadata change.

## Gate B — link diagnosis only

1. Run `DLsiteリンク診断`.
2. Record counts for normal / missing / invalid / ambiguous links.
3. Do not run an update check yet.
4. Confirm titles, Links, Notes, Sources, genres and existing user tags are unchanged.

**Pass condition:** diagnosis is read-only.

## Gate C — one known game

Pick exactly one game whose Playnite Links contains a valid DLsite product URL.

1. Before checking, note whether the game has any `[DLsite更新]` tag.
2. Run `今すぐ確認` once.
3. Confirm the summary reports **監視開始: 1** for a previously untracked game.
4. Confirm no `[DLsite更新] 更新あり` or `[DLsite更新] 配布物変更` tag is added on the first observation.
5. Locate the plugin user-data directory and confirm `tracking.json` was created.
6. Inspect only the selected game's record and confirm:
   - `AcknowledgedSnapshot` exists.
   - `CurrentSnapshot` exists.
   - both fingerprints are equal.
   - `MonitoringState` is `Clean`.
   - `LastCheckHealth` is `Healthy`.
7. Run `今すぐ確認` again without changing anything.
8. Confirm no update tag appears and state stays `Clean`.

**Pass condition:** first observation is baseline only; identical second observation remains clean.

## Gate D — failure safety

Use a test copy of the game entry or temporarily replace its DLsite Link with a malformed DLsite product URL.

1. Run the check.
2. Confirm it reports LinkError / error-required-review.
3. Restore the valid link.
4. Confirm the previous acknowledged/current snapshots were not erased.

If a pending update state is available during later testing, repeat an error check and confirm the pending state is preserved.

Also verify identity safety on a disposable copy of a game:

1. Start with a tracked game and record its current RJ ID.
2. Change only the Playnite DLsite link to a **different** valid RJ ID.
3. Run `今すぐ確認`.
4. Confirm the plugin stops with a LinkError instructing you to reset monitoring and does not create a baseline for the new product.
5. Run `監視状態をリセット`, then check again.
6. Confirm the new RJ is now accepted as a fresh baseline.

If you have a known unavailable/withdrawn DLsite work that still returns an HTTP 200 error page, confirm it is reported as **ProductUnavailable**, not as a generic parser failure.

**Pass condition:** failures never clear a valid baseline or pending state, and product identity never changes implicitly.

## Gate E — 5–10 games

1. Select 5–10 games with valid links.
2. Run `今すぐ確認`.
3. Confirm progress/cancel UI remains responsive.
4. Confirm each new game creates a baseline, not an update alert.
5. If two games intentionally reference the same product ID, confirm only one remote observation is used in that batch.
6. Restart Playnite and repeat a cached check.
7. Temporarily disable `更新状態をPlayniteタグへ反映` in plugin settings. Confirm existing `[DLsite更新]` tags disappear while unrelated tags remain untouched. Re-enable it and confirm pending-state tags are restored.

**Pass condition:** persisted state reloads correctly, tag integration is reversible, and no mass false-positive appears.

## Gate F — full library

Only after A–E pass:

1. Back up Playnite.
2. Run full-library check.
3. Review `エラー/要確認` before acting on update tags.
4. Do not package a `.pext` until the full-library result is plausible.

## Stop conditions

Stop testing immediately and preserve `tracking.json`, logs and the exact offending DLsite URL if any of these occur:

- first check creates an update tag;
- a 403/429/timeout clears a pending state;
- parser failure overwrites a known snapshot;
- non-plugin tags are removed;
- Notes/Links/title are modified;
- many unrelated games become updates at once.
