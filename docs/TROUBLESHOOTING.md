# トラブルシューティング

DLsite Update Monitorで問題が起きたときの確認手順です。

基本方針は、**監視データを消したり作り直したりする前に、既存データ・ログ・candidate commit SHAを保全する**ことです。

## 最初に保存する情報

- Playnite Version
- DLsite Update Monitor Version / candidate commit SHA
- 実行した操作と再現手順
- 発生時刻
- 対象Game / ProductId / DLsite URL
- 表示メッセージ
- MonitoringState / LastCheckHealth
- Playnite log
- `tracking.json`
- `tracking.backup.json`
- `.corrupt-*`の有無

実機Smoke中は使用したArtifact名`DLsiteUpdateMonitor-smoke-<commit SHA>`も記録してください。

## 初回チェックで更新タグが付いた

未追跡Gameの初回正常取得は`BaselineCreated`、`MonitoringState = Clean`です。初回だけで更新tagが付くのは正常ではありません。

1. 追加checkを止める
2. tracking / backup / logsを保全
3. `監視状態をリセット`をむやみに実行しない
4. `SnapshotComparer` / `TrackingStateMachine` / `PlayniteTagService`を確認

## 403 / 429 / timeout / network error

これらは取得Healthの問題で、既存Ack / Current / Pending stateを消さない設計です。

- 403: 連続retryしない
- 429: `Retry-After`を尊重するので短時間に手動連打しない
- timeout / network: 通信・proxy・firewall・DNS等を確認

一時原因が解消したら`エラー/要確認のゲームを再確認`で、LastCheckHealthが異常な追跡済みGameだけをforce refreshできます。

エラー後にBaselineやPendingが消えていたら重大な回帰です。

## その他HTTP 4xx

403 / 404 / 410 / 429以外の4xxは`ClientError`として非retryです。入力やDLsite側応答が変わらない限り繰り返しても改善しにくいため、5xxやnetwork errorと同じretry対象にはしていません。

## DLsite URLがHTTP

`http://dlsite.com/...`またはDLsite subdomainのHTTP URLはResolverでHTTPSへcanonicalizeします。

`ftp:`等HTTP(S)以外のschemeは受け入れません。

取得後の最終URLもabsolute HTTPS + `dlsite.com` / `*.dlsite.com`でなければ`UntrustedRedirect`として拒否します。外部hostへのredirectを正常商品ページとして扱いません。

## `LinkError`で作品変更扱いになる

追跡開始後にPlaynite LinkのProductIdを別作品へ変更した可能性があります。

旧Baselineを新作品へ流用しないため、HTTP前に停止します。

本当に切り替える場合だけ:

1. 旧trackingを必要ならbackup
2. `監視状態をリセット`
3. 新しいLinkで`今すぐ確認`
4. 新ProductIdが新規Baselineになることを確認

誤変更ならLinkを元に戻して再確認してください。

## `RedirectedToDifferentProduct`

要求ProductIdと最終resolved ProductIdが一致していません。初回でもBaselineを作りません。

- 登録URLを確認
- Browserでredirect先を確認
- 別作品へ切り替える意図がある場合だけReset

resolved URLがあるのにProductIdを抽出できないケースを、source URLのProductIdで正常扱いすることもしません。

## `ParseError` / `ParseDegraded`

主な原因:

- DLsite DOM変更
- `#work_name` / `#work_outline`欠落
- FileSizeを安全に解析できない
- UpdateInfoがUnparsed
- 通常商品ページではないHTML

比較不能なら既知Ack / Current / MonitoringStateを進めません。`LastObservation`だけ診断目的で残る場合があります。

複数作品で同時発生するならDOM変更を疑い、再現HTMLをfixture化してからParserを修正してください。

## HTTP 200だが`ProductUnavailable`

`.error_box_work`を検出した利用不可ページです。既存Pendingは維持されます。

## DLsiteリンク診断

診断は読み取り専用です。

- リンクなし → Game名を表示
- 不正 → Game名 + reason
- 複数作品 → Game名 + 候補ProductId

詳細は最大40行、それ以上は残件数を表示します。

### 「不正」

確認条件:

- absolute URI
- HTTP(S)
- DLsite host
- `/product_id/<ID>`
- ID = 英字2文字 + 6/8桁数字

### 「複数作品」

1 Gameに異なるProductIdが複数あります。Pluginは勝手に1つを選びません。

## 「別のDLsite更新チェックを実行中です」

`operationLock`がcheckまたは別mutationを保護しています。現在の操作完了後に再実行してください。

対象:

- check
- acknowledge / ignore
- reset
- orphan cleanup

lockを外して並列化しないでください。

## 保存失敗と表示された

`適用済み`、`無視`、reset、orphan cleanup、batch checkは作業用cloneへ変更し、保存成功後だけlive trackingへ昇格します。

保存失敗表示後に、その操作が後の正常saveやPlaynite終了で勝手に復活した場合は重大な回帰です。

保全するもの:

- 操作前後のtracking / backup
- disk空き容量 / permission / lock状況
- log
- candidate SHA

## 「Schemaが新しすぎる」

現在Pluginより新しいSchemaVersionです。Pluginは`persistenceBlocked`となり上書きしません。

- ファイルを削除しない
- SchemaVersionだけ手動変更しない
- 新しいPluginへ戻す/更新する

## `tracking.json`読み込み警告

Load順:

1. `tracking.json`
2. `tracking.backup.json`
3. 新規DB

### primary破損 / backup正常

backupから復旧します。次saveでは破損primaryだけを`.corrupt-*`へ退避し、正常backupを保持したまま新primaryを書きます。

復旧後に正常backupが破損primaryへ置き換わっていたら重大な回帰です。

### primary / backup両方破損

新規DBを使いますが、次save前に両方を`.corrupt-*`へ保全します。

### JSONとして読めるがrecordがnull

`Games`内のnull `GameTrackingRecord`はsemantic破損として拒否し、primaryならbackup fallbackの対象です。

## `tracking.tmp`が残る

保存途中でprocess終了した可能性があります。Pluginはtmpを自動primary昇格しません。

手動置換前にJSON / Schema /内容を別copyで検証してください。

## 更新タグが消えない / おかしい

Plugin所有tagは正確に次の2名称だけです。

```text
[DLsite更新] 更新あり
[DLsite更新] 配布物変更
```

`[DLsite更新] 自分用メモ`等はPlugin所有ではありません。

State対応:

| State | Tags |
|---|---|
| Clean / Uninitialized | なし |
| PendingUpdateInfo | 更新あり |
| PendingFileChange | 配布物変更 |
| PendingUpdateAndFileChange | **2つ両方** |

`PendingUpdateAndFileChange`なのに1つしか付かない場合は現在仕様に反します。

タグ連携OFFでユーザー独自tagまで消えた場合も重大な回帰です。

## Summaryの件数

`PendingUpdateAndFileChange`は:

- `更新あり`
- `配布物変更`
- `両方変更`

の全てへ計上されます。更新と配布物は独立した変更軸として表示します。

## `監視詳細を表示`できない

- 1 Gameだけ選択してください
- tracking recordがまだなければ「監視データなし」です

表示内容は読み取り専用です。

- State / Health
- ProductId
- timestamps
- AcknowledgedSnapshot
- CurrentSnapshot
- LastObservation
- LastError
- 最近10件History

画面表示と`tracking.json`が食い違う場合はcandidate SHAとtrackingを保全してください。

## `エラー/要確認のゲームを再確認`で何も起きない

対象は追跡済みで:

```text
LastCheckHealth != Healthy
LastCheckHealth != NeverChecked
```

のGameだけです。対象が0件ならその旨を表示します。

## `孤立した追跡データを整理`

Playnite DBに存在しないGameIdをtrackingから整理する機能です。

安全条件:

- 自動削除しない
- 候補数 / ProductId / GameIdをpreview
- Yes/No確認
- `No`なら無変更
- clone上で削除
- save成功後だけliveへ反映
- 現存Game / tag / metadataは変更しない

実機Smokeでは削除してよいテスト用Gameで確認してください。

現存Gameのrecordが消えたら即中止です。

## キャッシュが古く見える

正常RemoteSnapshotを既定24時間memory cacheします。

強制取得:

- Game menu `今すぐ確認`
- main menu `全ゲームを今すぐ確認`
- `キャッシュをクリア`

設定保存でruntime servicesを再構築するためmemory cacheは新instanceになります。

## Pluginが読み込まれない

必須payload:

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

独自同梱しない:

```text
Playnite.SDK.dll
AngleSharp.dll
Newtonsoft.Json.dll
```

GitHub ActionsのWindows jobまたは`Validate-Build.ps1`でbuild / boundaryを確認してください。

## Buildが失敗する

- `.NET 8 SDK` → `dotnet --list-sdks`
- `.NET Framework 4.6.2 Targeting Pack` → VS Installer / Build Tools
- Core net462 `System.Net.Http` → Core csprojのexplicit referenceを確認

Repositoryには`global.json`があり、開発/CI commandは.NET 8 SDK系を選ぶ前提です。

## `Package-Release.ps1`が失敗する

`-ConfirmRuntimeValidated`は実機Smoke完了後だけ使用します。

`Validated plugin payload was not found` / `VALIDATION-SUMMARY.txt is missing`なら先に`Validate-Build.ps1`を実行します。

Toolboxを検出できない場合は`-ToolboxPath`を指定します。

## Playnite終了時にfinal save skipped

operation中にshutdown saveとtracking serializationを競合させないため、lockを即取得できなければfinal saveをskipします。

Batchには10件ごとと終了時のdurable checkpointがあります。

頻発して状態欠落が疑われる場合はLastSavedAtUtc / processed count / logsを確認してください。

## `適用済み` / `無視`が0件

対象recordがPendingでない可能性があります。

- PendingUpdateInfo
- PendingFileChange
- PendingUpdateAndFileChange

Clean / UninitializedはAcknowledge対象外です。

## Reset後に履歴が残る

仕様です。ResetはSnapshot / Product identity / health / timestampsをclearしますが、Reset操作自体は`MonitoringReset` Historyとして残します。

## WorkName / URLだけ変わったが更新扱いにならない

現行Fingerprint対象はProductId / UpdateInfo / FileSizeです。WorkNameやURL文字列そのものだけの変更は配布状態更新としません。

ProductIdが変わればidentity protectionが働きます。

## 診断の安全な順序

```text
1. Candidate commit SHA / Plugin Version
2. Playnite Version
3. Link診断
4. tracking / backupを保全
5. 監視詳細でState / Health / Ack / Current / Observationを確認
6. Playnite log
7. BrowserでDLsite page確認
8. 必要なら再現HTML保存
9. Unit Testへ再現case追加
```

## 問題報告テンプレート

```text
Candidate commit SHA:
Artifact name:
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
期待結果:
実際結果:
tracking.json backup: あり / なし
Playnite log: あり / なし
```

## 開発者向け: 直してはいけない方法

- failure時に`MonitoringState = Clean`へ強制
- parse不能値を0/空文字として比較
- ProductId mismatchを無視
- final URL trust checkを外す
- LocalRecord failureを同ProductId全体へ共有
- trackingへ直接上書き
- save失敗後のworking stateをliveへ残す
- backup/corrupt保全を削除
- operation lockを外して並列化
- retry対象を全HTTP errorへ拡大
- prefix一致tagを全部削除
- orphan recordを自動削除
- Smoke failureを無視してRelease
