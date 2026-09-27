# DLsite Update Monitor v1.3.0

2026-09-27

v1.2.0のTracking Schema `1`、fail-closed比較、copy-on-write永続化、tag ownershipを維持しながら、Update Centerの差分表示と監査で確認した安全性・回復性の改善を追加するbackward-compatible feature releaseです。

## 主な変更

- Update Centerに **更新内容の差分ビュー** を追加
  - 確認済み基準と現在値の `更新情報` / `ファイル容量` を左右比較
  - 比較不能な観測を「変更あり」と誤表示しない
- tracking JSONからPlaynite管理タグを修復する **タグを再同期** を追加
- 起動時にもplugin-owned tagをtracking状態と照合して再同期
- HTTP redirectを自動追跡せず、次のGETを送る前にHTTPS + DLsite hostを検証
- redirect回数上限とHTTP response本文サイズ上限を追加
- 長い `Retry-After` でoperation lockを占有せず、`RetryNotBeforeUtc` を保存して期限後にschedulerで再確認
- `tracking.json` の `Games` 欠落 / null / 型不正を破損扱いにし、正常backupへfallback
- tracking保存成功後のPlaynite tag同期失敗を保存失敗と誤表示しないよう分離
- Playnite tag lookupをservice内でcacheし、大規模libraryでの全tag探索を削減
- nested update info / 複数file-size表記へのparser回帰testを追加
- TrackingDatabaseClonerのproperty追加漏れを検出する契約testを追加

## Safety

- Tracking Schemaは **1のまま**
- 自動download / patch適用は追加しない
- 外部hostやHTTP redirect先へ、信頼性検証前にfollow-up requestを送信しない
- parse / network failureでAcknowledgedSnapshot / CurrentSnapshot / Pending stateを破壊しない
- tracking JSONのdurable saveとPlaynite tag同期を別段階として扱う
- plugin-owned tagは既知の2タグだけを対象とし、ユーザー独自tagを削除しない

## Validation

- Release prep前 main CI:
  - Core tests: **87 passed / 0 failed / 0 skipped**
  - Static validation: **PASS**
  - Windows net462 Plugin build: **PASS**
  - payload boundary: **PASS**
- 2026-09-27 Playnite 10.56実環境で今回の機能変更を確認: **PASS**
- v1.3.0 exact release candidateを公式Playnite 10.56 Toolboxで生成: **PASS**
- Candidate source: `4fa5d07ace8d0c849d9ebc236285659252fc056e`
- Package: `DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_3_0.pext`
- SHA-256: `e463df6bc42589ec30b7415a05e965e7a396b97196b1c7d9b9aadc1d3146b0b3`
- v1.3.0 exact `.pext` install / Plugin load / Version 1.3.0 / Settings / Update Center / existing tracking / representative check: **PASS**

## Upgrade

v1.2.0からv1.3.0への更新でTracking Schema migrationはありません。既存の `tracking.json` / snapshot / historyはそのまま引き継ぎます。

License: MIT
