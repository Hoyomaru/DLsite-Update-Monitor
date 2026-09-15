# Changelog

このファイルには、DLsite Update Monitorの確認可能なVersion単位の変更履歴を記録します。

形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) を参考にしますが、Git履歴・Release記録・Version情報から確認できない過去の内容は推測で追加しません。

今後Releaseを作成する際は、コード・README・DEVELOPMENTと同じ変更でこのファイルも更新してください。

## [Unreleased]

次回Release向けの変更はここへ記録します。

## [1.1.0] - 2026-09-15

v1.1.0は、永続化・URL信頼境界・失敗分離の安全性強化と、診断・再確認・監視詳細・孤立データ整理を中心とした安定化Releaseです。

### Added

- GitHub ActionsによるCoreテスト、静的検証、Windows `net462` Plugin build、payload境界検証
- CIでbuildした実機Smoke Test用payload Artifact
- `エラー/要確認のゲームを再確認`メニュー
- 問題ゲーム名・理由・候補ProductIdを表示する詳細リンク診断
- 1ゲームのBaseline / Current / LastObservation / Health / Error / Historyを表示する`監視詳細を表示`
- Playniteから削除済みゲームの孤立tracking recordをPreview・確認後に整理する機能
- TrackingDatabaseのdeep clone helperと回帰テスト

### Changed

- tracking状態変更を作業用cloneへ適用し、保存成功後だけlive状態へ昇格する方式へ変更
- バッチ途中保存成功時点をdurable checkpointとして扱い、後続保存失敗時に未保存変更を確定しないよう変更
- primary破損 / backup正常から復旧した後の保存で、正常backupを破損primaryに置き換えないよう変更
- DLsiteの`http://` URLをHTTPSへ正規化
- HTTP responseの最終URLがHTTPSかつDLsite hostであることを要求
- parserの作品ID判定で、resolved URLがある場合にsource URLのProductIdへ暗黙fallbackしないよう変更
- 同一ProductIdバッチ共有をRemote Product failureだけに限定し、Game固有のLocalRecord failureを共有しないよう変更
- バッチ重複管理から取得HTML全文を外し、軽量な再利用メタデータだけを保持
- 予期しないHTTP 4xxを非retry client errorとして扱うよう変更
- 保存済みPlugin設定の範囲外数値を起動時に安全な既定値へ補正
- `PendingUpdateAndFileChange`では`[DLsite更新] 更新あり`と`[DLsite更新] 配布物変更`の両方を付与
- summaryで両方変更を更新情報・配布物の双方へ計上し、`両方変更`件数も表示
- Plugin所有タグの判定をprefix全体ではなく2つの正確な既知タグ名へ限定
- 設定画面の`v0.1`固定文言をVersion非依存の説明へ変更
- `Validate-Build.ps1`の固定テスト件数表示を廃止
- repository SDK選択を`global.json`で.NET 8系へ固定

### Fixed

- 保存失敗と表示された`適用済み` / `無視` / reset / batch変更が、後続saveで意図せず永続化される問題
- backupからの復旧後、次saveで正常backupが破損primaryへ置き換わる問題
- 同一ProductIdの先頭Gameが旧ProductId不一致だった場合、後続の正常GameまでLinkError扱いされる問題
- JSONとしては読めるがnull tracking recordを含むprimaryがbackup fallbackを回避する問題
- 範囲外の保存済み設定値によってPlugin constructorが失敗し得る問題
- HTTPまたは外部hostへのredirect結果を正常なDLsite観測として扱い得るURL信頼境界
- `[DLsite更新] `prefixを使ったユーザー独自タグをPluginが削除し得る問題
- Combined stateが`配布物変更`のsummary/tagから見えなくなるUI上の不整合

### Security

- tracking JSON deserializeに`MaxDepth`を設定
- 非HTTPS / 非DLsiteの最終取得先を拒否
- DLsite登録URLをHTTPSへ正規化
- 保存失敗時に未保存のlive stateを残さないcopy-on-write方式へ変更
- Plugin所有タグを正確な2名称へ限定

### Validation

v1.1.0の実装候補に対する自動検証:

- Coreテスト: **72 passed / 0 failed / 0 skipped**
- Static validation: **PASS**
- Windows `net462` Plugin restore/build: **PASS**
- 必須payload / 禁止DLL境界検証: **PASS**

Playnite 10.56実機Smoke Test Gate A〜G: **すべてPASS**

実機Smoke Testで使用した実装候補commitは`a7596bc077b8506512e526ded3e80a108fbdb94f`、Artifact ZIP SHA-256は`3c800fe84a2d28175f7d2baa1e3cbcc5f3b1f433b3c0b7a8cc10475678aa2f94`です。`release/1.1.0`では、この検証済み実装を`main`へ取り込んだ後、PluginのVersion metadataだけを`1.1.0`へ更新しました。Version 1.1.0の最終CI Artifactでも短縮Smoke Testを実施し、最終`.pext`インストール試験までPASSしています。

### Published

2026-09-15にGitHub Releaseとして正式公開しました。

```text
Release title: DLsite Update Monitor v1.1.0
Tag: v1.1.0
Tagged commit: e846cb245b22200a455001918c86115e95c5433c
Package: DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_1_0.pext
SHA-256: 9128b3ca11472b322d5307e6820b7fae902ca13d284b02645b87b9b3aace15de
```

Release Assetsには`.pext`と`SHA256SUMS.txt`を公開し、GitHubが報告する`.pext` digestも上記SHA-256と一致しています。公開本文は [docs/RELEASE_NOTES_1.1.0.md](docs/RELEASE_NOTES_1.1.0.md) に保存しています。

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

### Published

2026-09-14にGitHub Releaseとして正式公開しました。

```text
Release title: DLsite Update Monitor v1.0.0
Tag: v1.0.0
Tagged commit: a8aa212fbcaaded3411db74643c9345584e30e50
Package: DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA-256: 676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

Release Assetsには`.pext`と`SHA256SUMS.txt`を公開しています。

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

Version metadata変更後のv1.0.0配布バイナリはv0.1.0の既存`.pext`とは別成果物です。v1.0.0の公開Asset・Tag・Releaseは確認済みですが、Version `1.0.0`での`Validate-Build.ps1`、Smoke Gate A〜F、`.pext` install testの再実施結果は、現時点のリポジトリ内に独立した検証証跡として追加されていません。公開済みという事実だけから、それらを推測でPASS扱いしません。

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
