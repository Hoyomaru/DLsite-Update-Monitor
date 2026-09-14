# 実装上の設計メモ

この文書は、DLsite Update Monitorの安全性に関わる重要な実装ルールをまとめたものです。将来の変更でも、ここに記載した前提を崩す場合は十分なテストと実機検証を行ってください。

## 比較の基準

`SnapshotComparer.Compare(acknowledged, candidate)`は副作用を持ちません。

比較対象は常に、ユーザーが最後に「適用済み」または「無視」として確定した`AcknowledgedSnapshot`と、新しく取得した候補スナップショットです。

直前のチェック結果を自動的に次回の基準へ進めてはいけません。未処理の更新は、ユーザーが明示的に確認するまで保留状態として残します。

## 不確定な取得結果を変更扱いにしない

監視している項目が、既知・解析済みの値から「欠落」または「解析不能」へ変化した場合は、`Changed`や`NoChange`ではなく`Indeterminate`として扱います。

比較または検証が不確定な場合、`AcknowledgedSnapshot`や`CurrentSnapshot`を上書きしてはいけません。

## 監視状態とチェック状態を分離する

`MonitoringState`と`CheckHealth`は独立しています。

たとえば、前回の正常取得で更新を検出したあとにネットワークエラーが発生しても、保留中の更新状態を消してはいけません。

主な`MonitoringState`:

- `Uninitialized`
- `Clean`
- `PendingUpdateInfo`
- `PendingFileChange`
- `PendingUpdateAndFileChange`

主な`CheckHealth`:

- `Healthy`
- `NetworkError`
- `RateLimited`
- `AccessDenied`
- `Timeout`
- `ProductUnavailable`
- `RedirectedToDifferentProduct`
- `ParseError`
- `ParseDegraded`
- `LinkError`
- `Cancelled`

## DLsite作品IDの解決

リンク解決では、ホストが正確に`dlsite.com`、または`.dlsite.com`で終わるサブドメインであることを確認します。

作品IDはURLの`/product_id/<ID>`部分からのみ取得します。

v0.1.0では、DLsiteメタデータ系実装で一般的な「英字2文字 + 6桁または8桁の数字」を作品IDとして受け入れます。将来DLsite側の形式が変わった場合は、解決ロジックを1か所で変更できる構成です。

## 状態変更のガード

`ComparisonResult.TargetState`はnullを許可します。

`Indeterminate`や`IdentityMismatch`ではnullを返し、呼び出し側は「`MonitoringState`を変更しない」という意味として扱います。

不確定なときに既存状態を維持することを安全側の既定動作とします。

## HTMLパーサーのルール

`更新情報`の行自体が存在しない場合は、有効な`Missing`観測として扱います。

一方、行は存在するものの空欄や解析不能の場合は`Unparsed`です。

`ファイル容量`はv0.1.0の比較に必須です。容量が取得できない、または解析不能の場合は劣化スナップショットとして扱い、既存の`AcknowledgedSnapshot`と`CurrentSnapshot`を維持します。

正規化は決定的に行います。

- Unicode NFKC
- 空白の正規化
- 20xx年の日付を`yyyy-MM-dd`へ正規化
- ファイル容量を1024基準でバイトへ変換
- 比較用の正規化値とあわせて元の文字列も保持

DLsiteがHTTP 200を返しながら作品非公開・利用不可ページを表示する場合は、`.error_box_work`を検出して`ProductUnavailable`として扱います。

## TrackingStateMachine

チェック結果の健全性は、監視状態とは独立して更新します。

`RecordCheckFailure`は、次の項目を変更してはいけません。

- `AcknowledgedSnapshot`
- `CurrentSnapshot`
- `MonitoringState`

取得自体は成功していても比較が不確定な場合は、`LastObservation`と診断用のチェック状態だけを記録し、現在状態・確認済み状態は進めません。

保留中の変更を「適用済み」または「無視」にすると、`CurrentSnapshot`を`AcknowledgedSnapshot`へ複製します。スナップショット遷移は同じですが、履歴イベントは`Applied`と`Ignored`で区別します。

`監視状態をリセット`した場合は、スナップショットだけでなく登録URL・要求作品ID・解決URL・解決作品IDなどの作品識別情報もクリアします。

## 作品ID変更の保護

すでに追跡しているPlayniteゲームのDLsiteリンクが、別のRJ/RE/BJ/VJなどへ変更された場合、古いベースラインを新しい作品へ流用してはいけません。

現在のリンクから得た作品IDと追跡済み作品IDが異なる場合は、HTTP通信を行う前に`LinkError`で停止します。

別作品へ切り替える場合は、ユーザーが明示的に`監視状態をリセット`してから次回チェックで新しいベースラインを作成します。

## 永続化

`TrackingRepository`は、直接`tracking.json`を書き換えません。

基本手順:

1. `tracking.tmp`へ書き出す
2. 直ちに再読み込みして検証する
3. 正常な場合のみ本ファイルと置き換える
4. `tracking.backup.json`を保持する

対応していない新しいスキーマバージョンを検出した場合は例外として停止し、未知の形式を古い実装で上書きしません。

## HTTPクライアント

`DlsiteHttpClient`は共有クライアントを利用し、セマフォでDLsiteへの取得を直列化します。

主なルール:

- リクエスト開始間隔を確保
- 各試行にタイムアウトを設定
- 429、タイムアウト、通信エラー、5xxのみ再試行
- 403、404/410は積極的に再試行しない
- `Retry-After`がある場合は設定値より優先
- `locale=ja_JP`と`loginchecked=1`のCookieを利用
- キャンセルに対応

テストでは`HttpClient`、時計、待機処理を差し替え、実際のDLsiteへアクセスせずに検証できます。

## UpdateCheckService

初回ベースラインを作成する前にも、要求した作品IDと最終的に解決された作品IDが同一であることを確認します。

これにより、古いDLsite URLが別作品へリダイレクトされた場合に、誤った作品を初回ベースラインとして保存する問題を防ぎます。

安全に比較できる正常スナップショットだけを24時間キャッシュへ登録します。

キャッシュから取得したオブジェクトは複製して利用し、キャッシュ本体を変更しません。キャッシュされた観測の`FetchedAtUtc`は元の取得時刻を維持します。

同一バッチ内で複数のPlayniteゲームが同じ作品IDを参照している場合、正常なリモート観測を再利用して重複通信を避けます。この再利用可否はゲームごとの`CheckHealth`とは分離して判定します。

## Playnite連携

Playnite 10.56との互換性を優先し、PlayniteSDK 6.16.0へ固定しています。

Playnite本体が提供する次のランタイムDLLを拡張機能へ重複同梱しません。

- `Playnite.SDK.dll`
- `AngleSharp.dll`
- `Newtonsoft.Json.dll`

進捗処理では`GlobalProgressResult.Error`を確認します。途中保存や最終保存に失敗した場合はタグ反映を停止し、成功したように見える結果を表示しません。

「適用済み」「無視」「監視状態をリセット」など状態を変更する操作は、バッチチェックと同じ操作セマフォを共有し、`tracking.json`への同時変更を防ぎます。

タグ連携を無効にした場合は、プラグイン自身が管理する`[DLsite更新]`タグだけを削除し、ユーザーの無関係なタグには触れません。

## 回帰テスト

`UpdateCheckServiceTests`などで、少なくとも次の安全条件を検証しています。

- 初回ベースライン作成
- 初回ベースライン前の別作品リダイレクト検出
- HTTP失敗後も保留状態を維持
- パース劣化時に既知スナップショットを維持
- 正常キャッシュ利用時に2回目のHTTP通信を行わない
- HTTP 200の利用不可作品ページを`ProductUnavailable`として分類
- 追跡中の作品ID変更を明示リセットなしで受け入れない
- 最初のゲーム固有状態が劣化していても、正常なリモート観測を同作品の別ゲームで再利用可能

## .NET Framework 4.6.2での`System.Net.Http`

Coreプロジェクトは`net8.0`と`net462`の両方を対象にしています。

`HttpClient`はCore側で使用するため、`net462`ターゲットでは`System.Net.Http`をCoreプロジェクト自身が明示参照します。プラグイン側だけに参照を置く構成ではCore単体の`.NET Framework 4.6.2`ビルドが失敗します。

この参照漏れを防ぐため、静的検証にもチェックを追加しています。
