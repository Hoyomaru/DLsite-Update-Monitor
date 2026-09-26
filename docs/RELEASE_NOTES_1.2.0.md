# DLsite Update Monitor v1.2.0

2026-09-27 Release Candidate

v1.1.0のfail-closed / copy-on-write / tag ownership / Tracking Schema 1を維持しながら、日常の確認作業をUpdate Centerへ集約し、既定OFFのautomatic checkを追加するbackward-compatible feature releaseです。

## 主な変更

- **Update Center** で追跡中gameを一覧表示
- game名 / 作品ID検索、状態filter、複数選択操作
- Update Centerから適用済み / 無視 / 再確認 / 詳細 / DLsite page
- **追跡中のゲームを自動チェックする** 設定（既定OFF）
- 自動間隔1〜168時間、既定24時間
- Playnite起動約2分後に初回判定、その後15分poll
- manual operation中は自動checkを延期
- automatic checkはMessageBox/Global Progressを出さずtracking / tag / Update Centerへ反映
- menuから **設定を開く** を直接実行可能

## UI fixes from real Playnite validation

- Update Center construction時のXAML eventによるNullReferenceExceptionを修正
- Playnite dark themeの `TextBrush` を継承
- SettingsをPlayniteへ公開する `HasSettings = true` を追加
- automatic check sectionをsettings上部へ移動しscroll可能化

## Safety

- automatic checkは既存tracking recordだけが対象
- DLsite link削除済みrecordはscheduler対象外
- 未追跡gameを自動登録しない
- manual/automatic checkは同じbatch pathとoperation lockを共有
- Tracking Schemaは1のまま
- 既存のsnapshot / Pending state / tag ownership / persistence safetyを維持

## Validation

- Core tests / Static validation / Windows net462 Plugin build / payload boundary: PASS（Version 1.2.0 final candidateで再実行）
- 2026-09-27 Playnite 10.56: Update Center起動・rows描画・dark theme PASS
- 2026-09-27: Settings discoverability / direct settings menu / automatic check ON/OFF / Gate H runtime PASS

## Release gate remaining

Version 1.2.0 metadataを含む最終CI Artifactを同一payloadのままpackageし、生成した `.pext` 自体をPlayniteへinstallして最終確認した後に公開します。

License: MIT
