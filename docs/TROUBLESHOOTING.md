# トラブルシューティング

DLsite Update Monitorで問題が起きたときの確認手順です。

基本方針は、**問題がある状態で監視データを消したり作り直したりする前に、既存データとログを保全する**ことです。

重大な不整合が疑われる場合は、更新チェックや`監視状態をリセット`を繰り返さず、まず状況を保存してください。

## 最初に確認すること

問題報告時は次を記録してください。

- Playnite Version
- DLsite Update Monitor Version
- 実行した操作
- 症状
- 発生時刻
- 対象ゲーム数
- 対象DLsite URL
- 表示されたエラーメッセージ
- `CheckHealth`が確認できればその値
- Playniteログ
- `tracking.json`
- `tracking.backup.json`
- `.corrupt-*`ファイルの有無

ファイルやログを他人へ共有する場合は、個人情報や不要なURL等が含まれていないか確認してください。

## 症状: 初回チェックで更新タグが付いた

### 正常仕様

未追跡ゲームの初回正常取得は`BaselineCreated`となり、`MonitoringState`は`Clean`です。初回だけで更新タグが付くのは正常仕様ではありません。

### 対処

1. 追加のチェックを止める
2. `監視状態をリセット`をむやみに実行しない
3. `tracking.json`をバックアップ
4. Playniteログを保全
5. 対象DLsite URLを記録
6. Plugin Versionを記録
7. 再現条件を確認

### 開発者向け確認箇所

- `SnapshotComparer.Compare()`のBaseline path
- `TrackingStateMachine.ApplySuccessfulSnapshot()`
- `PlayniteTagService.Apply()`
- `UpdateCheckServiceTests.FirstHealthyCheck_CreatesBaseline`

## 症状: 403 / 429 / timeout / network errorが出る

### 仕様

これらは取得Healthの問題であり、既存の`AcknowledgedSnapshot`、`CurrentSnapshot`、`MonitoringState`を消さない設計です。

### 対処

- 403: 連続retryせず、時間を置いて再確認
- 429: `Retry-After`があればPluginが尊重する。短時間に繰り返し手動実行しない
- timeout: 通信状況を確認し、必要なら設定のtimeoutを妥当な範囲で調整
- network error: DNS / proxy / firewall / internet connection等を確認

### 確認ポイント

エラー後にPending stateや以前のBaselineが消えていないことを確認します。

消えていた場合は重大な回帰の可能性があるため、データを保全して処理を止めてください。

## 症状: `LinkError`で「作品が変わった」扱いになる

### 原因候補

追跡開始後にPlayniteのDLsite Linkを別ProductIdへ変更した可能性があります。

### 仕様

旧作品のBaselineを新作品へ流用しないため、ProductId変更はHTTP request前に停止します。

### 対処

本当に別作品へ追跡対象を切り替える意図がある場合だけ:

1. 現在の旧追跡状態が不要か確認
2. 必要なら`tracking.json`をバックアップ
3. ゲーム右クリック → `監視状態をリセット`
4. 次回`今すぐ確認`
5. 新ProductIdが初回Baselineとして作成されることを確認

誤ってLinkを変更しただけなら、Linkを元に戻して再チェックしてください。

## 症状: `RedirectedToDifferentProduct`

### 原因

要求したProductIdと、DLsite側で最終的に解決されたProductIdが一致していません。

### 仕様

初回チェックでもBaselineを作成しません。

### 対処

- 登録URLが古くないか確認
- ブラウザでURLのredirect先を確認
- Playnite Linkを正しい作品URLへ修正
- 既追跡作品を意図的に切り替える場合だけReset

別ProductIdを同一作品として自動許可しないでください。

## 症状: `ParseError`

### 原因候補

- DLsite HTML構造変更
- `#work_name`が見つからない
- `#work_outline`が見つからない
- 取得HTMLが通常の商品ページではない

### 対処

1. ブラウザで対象ページが通常表示されるか確認
2. HTTP statusだけでなくページ内容も確認
3. 対象URL、時刻、ログを保存
4. 複数作品で同時発生しているならDLsite側DOM変更を疑う

### 開発者向け

`DlsitePageParserTests`へ再現HTMLをfixtureとして追加し、parserを変更する前にfailure caseをtest化してください。

HTML変更を「値が消えた」と見なして更新検出へ流さないでください。

## 症状: `ParseDegraded`

### 主な原因

- FileSize rowが見つからない
- FileSizeを安全に数値化できない
- UpdateInfo rowはあるが内容を解析できない
- acknowledged Snapshot自体が現在のvalidator条件を満たさない

### 仕様

比較不能として扱い、既知の`AcknowledgedSnapshot` / `CurrentSnapshot` / `MonitoringState`を進めません。

`LastObservation`は診断用に残る場合があります。

### 対処

- 対象DLsiteページの「ファイル容量」「更新情報」表示を確認
- DLsiteのDOM/表記変更か、一時表示異常かを切り分ける
- 既存Baselineを手動で書き換えない

## 症状: 商品ページはHTTP 200だが`ProductUnavailable`

### 仕様

DLsiteは利用不可作品でHTTP 200のエラーページを返す場合があります。

Pluginは`.error_box_work`を検出して`ProductUnavailable`に分類します。

### 対処

ブラウザで対象ページを確認し、販売停止・非公開等の表示になっているか確認してください。

既存Pending stateはそのまま維持されます。

## 症状: DLsiteリンク診断で「不正」になる

### 対応URL条件

- hostが`dlsite.com`またはそのsubdomain
- path/queryに`/product_id/<ID>`がある
- IDが英字2文字 + 6桁または8桁数字

### 対処

PlayniteのLinksに通常のDLsite商品URLを登録してください。

URL文字列中にRJ等の文字があるだけでは解決しません。現行実装は`/product_id/`部分からのみ取得します。

## 症状: DLsiteリンク診断で「複数作品リンク」になる

### 原因

1つのPlaynite Gameに異なる複数のDLsite ProductIdが登録されています。

### 対処

意図した作品を確認し、不要なDLsite Linkを整理してください。

Pluginは曖昧な状態で勝手に1作品を選びません。

## 症状: DLsiteリンクがあるのに「リンクなし」になる

### 確認事項

- URLがabsolute URLか
- hostがDLsiteか
- `/product_id/`形式か
- ProductIdが現行regexに合うか

DLsite側で将来ID形式が変わった場合、Resolver変更が必要になる可能性があります。

## 症状: 「別のDLsite更新チェックを実行中です」

### 原因

更新チェックまたは別の状態変更操作が`operationLock`を保持しています。

### 仕様

同時に`tracking.json`やtracking stateを変更しないための安全機構です。

### 対処

現在の操作を完了またはキャンセルしてから再実行してください。

Lockを外して並列実行させることは推奨しません。

## 症状: 「追跡データのSchemaが新しすぎる」

### 原因

現在のPluginより新しいSchemaVersionで作られた`tracking.json`を読み込んでいます。

### 仕様

Pluginは安全のため更新チェックと保存を無効にします。古いSchemaへ勝手にdowngradeして上書きしません。

### 対処

- trackingファイルを削除しない
- より新しいPlugin Versionへ戻す/更新する
- 必要ならデータをバックアップしてmigration手順を確認する

SchemaVersionだけを手動で書き換えて回避しないでください。

## 症状: `tracking.json`を読み込めない警告が出た

### Load fallback

1. primary `tracking.json`
2. backup `tracking.backup.json`
3. 新規DB

の順で試します。

### primary破損 / backup正常

backupが読み込まれ、警告が表示されます。

### primary/backup両方破損

新しいDBを使用しますが、次回save前に既存破損ファイルを`.corrupt-*`へ退避します。

### 対処

- 破損ファイルをすぐ削除しない
- `.corrupt-*`も含めバックアップ
- 直前のクラッシュ、disk error、手動編集の有無を確認
- recoveryが必要ならJSONを別copyで解析

## 症状: `tracking.tmp`が残っている

### 原因候補

保存処理の途中でprocessが終了した可能性があります。

### 対処

まず`tracking.json`と`tracking.backup.json`の状態を確認してください。

`tracking.tmp`をprimaryとして手動置換する前に、JSON内容とSchemaVersionが正常か確認する必要があります。

通常のPlugin loadはprimary/backupを利用し、tmpを自動昇格させる実装ではありません。

## 症状: 更新タグが消えない

### 確認事項

- `MonitoringState`がまだPendingではないか
- 「適用済み」/「無視」が成功したか
- 保存エラーが出ていないか
- `EnableTags`設定
- Tag名が`[DLsite更新] `prefixか

### 仕様

Pluginはtag反映前に自管理tagを一度外し、現在stateに応じて付け直します。

## 症状: タグ連携をOFFにしたらユーザータグまで消えた

これは正常仕様ではありません。

現行実装は`[DLsite更新] `prefixだけを削除対象にします。

発生した場合は重大な回帰として:

1. 操作を止める
2. Playnite DBバックアップを保全
3. Plugin Version / logs / affected Gameを記録
4. `PlayniteTagService.RemoveOwnTags()`を確認

してください。

## 症状: `PendingUpdateAndFileChange`なのにタグが1つしか付かない

### 仕様

現在は`PendingUpdateAndFileChange`を`[DLsite更新] 更新あり`タグへ集約します。

`更新あり`と`配布物変更`の2tagを同時に付ける設計ではありません。

状態の詳細は`tracking.json`の`MonitoringState`で確認できます。

## 症状: 「キャッシュを利用して確認」で古い結果に見える

### 仕様

正常なRemoteSnapshotを既定24時間メモリcacheします。

### 対処

最新のDLsite状態を強制取得したい場合:

- ゲーム右クリック `今すぐ確認`
- メインメニュー `全ゲームを今すぐ確認`
- または`キャッシュをクリア`

を使用します。

CacheはPlaynite再起動でも消えます。

## 症状: 設定を変えたらキャッシュが消えたように見える

設定保存時にruntime servicesを再構築し、新しい`SnapshotCache` instanceを作成します。

そのため既存memory cacheは引き継がれません。永続追跡データ`tracking.json`とは別物です。

## 症状: Pluginが読み込まれない

### 必須payload

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

### 確認

- `extension.yaml`のModuleが`DLsiteUpdateMonitor.dll`
- Typeが`GenericPlugin`
- Targetが.NET Framework 4.6.2
- Playnite 10.56検証環境との差
- Playniteログ

### Runtime DLL方針

次をPlugin側へ独自同梱しない構成です。

```text
Playnite.SDK.dll
AngleSharp.dll
Newtonsoft.Json.dll
```

`Validate-Build.ps1`でpayloadを再生成・検査してください。

## 症状: Buildが失敗する

### `.NET 8 SDK が見つかりません`

.NET 8 SDKをinstallし、`dotnet --list-sdks`を確認します。

### `.NET Framework 4.6.2 Targeting Pack`がない

Visual Studio 2022 Installer / Build Toolsから.NET Framework 4.6.2 Developer/Targeting Packを追加します。

### Core net462で`System.Net.Http`関連エラー

Core csprojのnet462条件にexplicit `System.Net.Http` referenceが必要です。Plugin側だけにreferenceを置いてもCore単体buildは通りません。

## 症状: `Package-Release.ps1`が失敗する

### `Runtime smoke test confirmation is required`

Smoke Testを完了した後だけ:

```powershell
.\tools\Package-Release.ps1 -ConfirmRuntimeValidated
```

を使います。

確認していないのにflagだけ付けて回避しないでください。

### `Validated plugin payload was not found`

先に:

```powershell
.\tools\Validate-Build.ps1
```

を実行してください。

### `VALIDATION-SUMMARY.txt is missing`

同様にValidate-Buildからやり直します。

### `Toolbox.exe could not be located`

Playniteの`Toolbox.exe`を確認して:

```powershell
.\tools\Package-Release.ps1 -ConfirmRuntimeValidated -ToolboxPath "...\Toolbox.exe"
```

を使用します。

### `.pext`が0件または複数件

Release outputを確認し、Toolbox packの結果を調査してください。Scriptは1件だけを正常とします。

## 症状: Release packageのSHA-256が記録と違う

再生成したbinary/packageならhashが変わるのは自然です。

重要なのは、**公開するassetそのものからhashを計算し、その値をReleaseと一致させること**です。

v0.1.0の既知hashを別Versionへコピーしないでください。

## 症状: Playnite終了時にfinal save skippedの警告

### 仕様

Plugin終了時に`operationLock`を即時取得できなければ、進行中の処理と競合してtracking dictionaryをserializeするより安全なため、shutdown final saveをスキップします。

バッチ処理は10件ごとと最後に保存する設計です。

### 対処

頻繁に発生して状態欠落が疑われる場合:

- 終了操作とcheckのタイミング
- 最後に保存された`LastSavedAtUtc`
- processed count
- logs

を確認してください。

## 症状: `適用済み`または`無視`を押しても0件

### 原因候補

対象recordが次のPending stateではない可能性があります。

- `PendingUpdateInfo`
- `PendingFileChange`
- `PendingUpdateAndFileChange`

`Clean`や`Uninitialized`ではAcknowledge対象になりません。

## 症状: `監視状態をリセット`後に履歴が残る

### 仕様

ResetはSnapshot、Product identity、health、timestampsをclearしますが、Reset自体を`MonitoringReset`履歴として残します。

履歴全削除機能ではありません。

## 症状: DLsiteの商品名が変わったのに更新扱いにならない

### 仕様

`WorkName`はSnapshotへ保存されますが、現在のFingerprint/変更判定対象はProductId、UpdateInfo、FileSizeです。

商品名変更だけはv0.1.0の更新検出対象ではありません。

## 症状: URLが変わったのに更新扱いにならない

URL文字列そのものはFingerprintへ含まれません。

Product identityと監視対象フィールドが同じであれば、URLだけの変化を配布状態更新として扱う設計ではありません。

ただしProductIdが変わればLinkError/identity protectionが働きます。

## 診断用の安全な確認順序

```text
1. Plugin Version確認
2. Playnite Version確認
3. Link診断
4. tracking.jsonをバックアップ
5. Game recordのMonitoringState / LastCheckHealth確認
6. Acknowledged / Current / LastObservationを区別して確認
7. Playnite log確認
8. DLsite pageをブラウザ確認
9. 必要なら再現用HTMLを保存
10. Core testへ再現case追加
```

## 問題報告テンプレート

```text
Playnite Version:
Plugin Version:
OS:
発生時刻:
操作:
対象Game数:
DLsite ProductId/URL:
MonitoringState:
LastCheckHealth:
表示メッセージ:
再現手順:
期待した結果:
実際の結果:
tracking.json backup: あり / なし
Playnite log: あり / なし
```

Credentialや個人情報を貼らないでください。

## 開発者向け: 直してはいけない方法

問題を素早く消すために次の対応をしないでください。

- failure時に`MonitoringState = Clean`へ強制する
- Parseできない値を0や空文字として比較する
- ProductId mismatchを無視する
- `tracking.json`へ直接上書きする
- backup/corrupt保全を削除する
- operation lockを外して並列化する
- retry対象を全HTTP errorへ広げる
- Plugin tag以外を一括削除する
- Smoke Test失敗を無視してReleaseする

症状を隠すのではなく、既存の安全条件を維持したまま原因を修正してください。
