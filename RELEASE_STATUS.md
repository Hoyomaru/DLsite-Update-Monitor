# DLsite Update Monitor v0.1.0 — 最終リリース検証記録

> [!NOTE]
> この文書は、**v1.0.0正式公開前に使用していたv0.1.0の検証証跡**として保持しています。現在の正式公開Versionはv1.1.0です。以下の`.pext`名とSHA-256はv0.1.0専用であり、後続Versionへ流用しません。v1.1.0の公開内容は`docs/RELEASE_NOTES_1.1.0.md`と`CHANGELOG.md`を参照してください。

## 最終状態

バージョン **0.1.0** は、予定していたビルド、自動テスト、段階的な実機テスト、全ライブラリチェック、パッケージ作成、`.pext`インストール試験まで完了しています。

実機検証済みのMilestone 3.2以降、プラグイン本体のソースコードには変更を加えていません。検証後に行った変更は、古いWindows PowerShellでも動作するよう`tools/Package-Release.ps1`のSHA-256計算を`Get-FileHash`から`System.Security.Cryptography.SHA256`ベースへ変更したことだけです。

## ビルド・自動テスト

`tools\Validate-Build.cmd`: **PASS**

確認済み項目:

- NuGet restore成功
- Core自動テスト **63ケース PASS**
- `net462`向けPlayniteプラグインビルド成功
- 必須成果物ファイルの存在確認
- `Playnite.SDK.dll`、`AngleSharp.dll`、`Newtonsoft.Json.dll`の不要な同梱がないことを確認
- 検証済み成果物を`artifacts\plugin`へ出力

## Playnite実機検証

DLsiteリンクを登録した33作品のPlayniteライブラリで段階的に検証しました。

- Gate A — 拡張機能の読み込み、メニュー、設定画面: **PASS**
- Gate B — DLsiteリンク診断: **33 / 33 正常**
- Gate C — 初回ベースライン作成、同一状態の再チェック: **PASS**
- Gate D — 一時的な通信失敗時のベースライン保持、復旧後の再確認: **PASS**
- Gate E — 小規模バッチチェック: **PASS**
- Gate F — 全33作品のライブラリチェック: **PASS**

段階的な検証中に、大量の誤検知、破壊的なメタデータ変更、ベースライン消失は確認されませんでした。

## 配布パッケージ

Playnite Toolboxで`.pext`を作成し、そのパッケージからのインストール試験も成功しています。

パッケージ名:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_0_1_0.pext
```

SHA-256:

```text
c84d8fbb3fb82e5d3d5c6bd974c153b33dd8437ff96f4447a4ed0c68c7a939bf
```

`.pext`からのインストール後も、既存の監視状態が維持されることを確認済みです。

`.pext`はソースリポジトリには含めず、GitHub Releasesなどで別途配布する運用とします。

## リリース判断

この記録作成時点では **DLsite Update Monitor v0.1.0を安定した検証基準**として扱っていました。

その後、現行実装を初回正式公開版として整理し、公開Versionを **v1.0.0** としました。監視ロジックとTracking Schemaはv0.1.0検証時点から変更していませんが、Version metadata変更後のv1.0.0配布バイナリは別成果物です。

さらに2026-09-15に、永続化・URL信頼境界・失敗分離の安全性強化、診断・再確認・監視詳細・孤立データ整理を含む **v1.1.0** を正式公開しました。v1.1.0はv0.1.0 / v1.0.0の検証証跡とは分離し、Core 72テスト、Windows Plugin CI、Playnite 10.56 Smoke Gate A〜G、最終Artifact短縮Smoke、最終`.pext`インストール試験を独立して実施しています。

安全性を優先し、DLsiteの`更新情報`と`ファイル容量`のみを監視します。比較基準は最後にユーザーが確認済みとしたリモートスナップショットです。

自動ダウンロード、自動パッチ適用、ローカル実行ファイルからのバージョン推定はv1.1.0でも対象外です。
