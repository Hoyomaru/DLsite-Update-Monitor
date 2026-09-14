# Changelog

このファイルには、DLsite Update Monitorの確認可能なVersion単位の変更履歴を記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) を参考にしますが、Git履歴・Release記録・Version情報から確認できない過去の内容は推測で追加しません。

今後Releaseを作成する際は、コード・README・DEVELOPMENTと同じ変更でこのファイルも更新してください。

## [Unreleased]

現在、次Versionとして確定した機能変更はありません。

## [1.0.0] - 2026-09-14

**初回正式公開版。**

v0.1.0で実機検証していた現行実装を、公開用Version `1.0.0` として正式に位置付けたReleaseです。監視ロジック、Tracking Schema、対応機能の範囲はv0.1.0検証時点から変更していません。

### Added

- Playnite `Game.Links`からDLsite作品IDを解決する機能
- 全ゲームおよび選択ゲームの手動更新チェック
- DLsiteリンク診断
- 初回正常取得を監視Baselineとして保存する処理
- DLsite商品ページの`更新情報`変更検出
- DLsite商品ページの`ファイル容量`変更検出
- `Clean` / `PendingUpdateInfo` / `PendingFileChange` / `PendingUpdateAndFileChange`の監視状態
- Network / rate limit / access denied / timeout / unavailable / redirect / parse / link / cancelを区別するCheckHealth
- ユーザー操作による`適用済み`、`無視`、`監視状態をリセット`
- `[DLsite更新] 更新あり`、`[DLsite更新] 配布物変更`のPlayniteタグ連携
- 同一ProductIdの正常なリモート観測を再利用するメモリキャッシュ
- `tracking.json`、`tracking.backup.json`、`tracking.tmp`を使った追跡状態の永続化
- 破損した追跡ファイルの保全
- 未対応の新しいTracking Schemaを古いPluginで上書きしない保護
- HTTP request間隔、timeout、retry、`Retry-After`対応
- 429 / timeout / network / 5xxの再試行
- HTTP 200で返るDLsite利用不可ページの検出
- ProductId変更・別作品redirectからBaselineを保護するidentity check
- Core自動テスト
- `tools/Validate-Build.ps1`によるrestore/test/build/payload検証
- `tools/Install-Dev.ps1`による開発用インストール
- `tools/Package-Release.ps1`による検証済みpayloadの`.pext`作成とSHA-256生成
- Playnite実機Smoke Test Gate A〜F

### Changed

- Plugin csprojと`extension.yaml`のVersionを`1.0.0`へ更新
- README / DEVELOPMENT / ARCHITECTURE / RELEASE文書を初回正式公開版の位置付けへ更新
- 重複していた`IMPLEMENTATION_NOTES.md`の安全設計本文を`DEVELOPMENT.md` / `docs/ARCHITECTURE.md`へ集約し、互換インデックスへ縮小
- Release packagingのSHA-256計算を、古いWindows PowerShellでも利用しやすい`System.Security.Cryptography.SHA256`ベースへ変更

### Security

- HTTP/解析失敗時に確認済みSnapshotやPending状態を上書きしないfail-closed設計
- 別ProductIdへ旧Baselineを流用しない保護
- 保存前に一時JSONを再読み込み・検証してから置換
- Plugin管理外のPlayniteタグを削除しない設計
- API Key、Access Token、PasswordなどのCredentialを必要としない構成

### Validation basis

v1.0.0は、v0.1.0として保存されている次の検証証跡を基準にしています。

- Core自動テスト: 63ケース PASS
- `.NET Framework 4.6.2`向けPlugin build PASS
- DLsiteリンク診断: 33 / 33
- 初回Baseline作成 PASS
- 同一状態再チェック PASS
- 一時通信失敗時の状態保持 PASS
- 小規模バッチ PASS
- 33作品の全ライブラリチェック PASS
- `.pext`作成・インストール試験 PASS
- インストール後の既存監視状態維持 PASS

ただし、Version metadata変更後のv1.0.0配布バイナリはv0.1.0の既存`.pext`とは別成果物です。公開前に `Validate-Build.ps1`、必要な実機Smoke Test、`Package-Release.ps1`、v1.0.0 `.pext`インストール確認を実行し、新しいSHA-256を生成してください。

## [0.1.0] - 2026-09-14

正式公開前の検証基準Version。

### Validation

保存済み検証記録では次がPASSしています。

- Core自動テスト: 63ケース
- `.NET Framework 4.6.2`向けPlugin build
- DLsiteリンク診断: 33 / 33
- 初回Baseline作成
- 同一状態再チェック
- 一時通信失敗時の状態保持
- 小規模バッチ
- 33作品の全ライブラリチェック
- `.pext`パッケージ作成
- `.pext`インストール試験
- インストール後の既存監視状態維持

検証済みパッケージ:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_0_1_0.pext
SHA-256: c84d8fbb3fb82e5d3d5c6bd974c153b33dd8437ff96f4447a4ed0c68c7a939bf
```

このSHA-256はv0.1.0専用です。v1.0.0へ流用しません。

詳細は [RELEASE_STATUS.md](RELEASE_STATUS.md) を参照してください。
