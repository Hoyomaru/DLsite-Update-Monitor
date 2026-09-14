# DLsite Update Monitor — 開発・保守ガイド

この文書は、DLsite Update Monitor の開発継続、保守、コードレビュー、AIへの引き継ぎにおける**開発者向け正本**です。

今後の変更では、最初に [README.md](README.md)、この `DEVELOPMENT.md`、[CHANGELOG.md](CHANGELOG.md)、対象コードと関連テストを確認してください。内部構造の図解は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)、ビルドは [BUILD.md](BUILD.md)、リリース工程は [docs/RELEASE.md](docs/RELEASE.md)、実機確認は [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) を参照します。

仕様の最終判断は**現在動いている実装とテスト**を基準にします。文書とコードが食い違う場合、文書へ合わせるために動いているコードを勝手に変更せず、まず差異を調査してください。

---

## 1. 現在の状態

| 項目 | 現在の状態 |
|---|---|
| 安定版として扱うVersion | 0.1.0 |
| Plugin Version | `src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj`: `0.1.0` |
| extension Version | `src/DLsiteUpdateMonitor.Plugin/extension.yaml`: `0.1.0` |
| Playnite | 10.56で実機検証済み |
| Plugin target | .NET Framework 4.6.2 |
| Core target | .NET Framework 4.6.2 / .NET 8.0 |
| Core自動テスト | 63ケース PASS（v0.1.0検証記録） |
| 実機Smoke Test | Gate A〜F PASS |
| 全ライブラリ実機確認 | DLsiteリンク登録済み33作品でPASS |
| `.pext`作成・インストール | PASS |
| GitHub Release | 2026-09-14確認時点で未作成 |
| 公開Git tag | 確認できていない |
| GitHub Actions / CI | 未導入 |
| License | 未設定 |

v0.1.0の実機検証証跡は [RELEASE_STATUS.md](RELEASE_STATUS.md) を正とします。

---

## 2. プロジェクトの責務

本プロジェクトは、Playniteで管理されるゲームの `Game.Links` からDLsite作品URLを解決し、DLsite商品ページの配布状態を監視するGenericPluginです。

v0.1.0で比較する信号は次の2つだけです。

1. `更新情報`
2. `ファイル容量`

次は判定しません。

- ローカルにインストールされたゲーム本体のVersion
- EXEのFile Version
- ローカルファイルハッシュ
- パッチ適用状態
- DLsite以外のストア

### 比較基準

比較対象は常に、ユーザーが最後に「適用済み」または「無視」として確定した `AcknowledgedSnapshot` と、新しく取得したCandidateです。

**直前に取得した結果を自動で次回の基準へ進めてはいけません。**

```text
AcknowledgedSnapshot
        │
        │ compare
        ▼
Candidate
        │
        ├─ 同じ          → Clean
        ├─ 更新情報差分   → PendingUpdateInfo
        ├─ 容量差分       → PendingFileChange
        ├─ 両方差分       → PendingUpdateAndFileChange
        └─ 安全に比較不能 → 既存状態を進めない
```

初回の正常観測は更新ではなくBaseline作成です。

---

## 3. リポジトリ構成

```text
DLsite-Update-Monitor/
├─ src/
│  ├─ DLsiteUpdateMonitor.Core/
│  │  ├─ Http/           # DLsiteへのHTTP GET、interval、timeout、retry
│  │  ├─ Models/         # Snapshot、状態、Tracking DB
│  │  ├─ Parsing/        # HTML parser、更新情報/容量の正規化
│  │  ├─ Persistence/    # tracking.jsonの安全な読み書き
│  │  └─ Services/       # Resolver、比較、状態機械、cache、check service
│  └─ DLsiteUpdateMonitor.Plugin/
│     ├─ DLsiteUpdateMonitorPlugin.cs
│     ├─ PlayniteTagService.cs
│     ├─ PluginSettings.cs
│     ├─ PluginSettingsView.xaml
│     └─ extension.yaml
├─ tests/
│  └─ DLsiteUpdateMonitor.Core.Tests/
├─ tools/
│  ├─ Validate-Build.ps1
│  ├─ Install-Dev.ps1
│  ├─ Package-Release.ps1
│  └─ Static-Validate.py
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ RELEASE.md
│  ├─ SMOKE_TEST.md
│  └─ TROUBLESHOOTING.md
├─ README.md
├─ DEVELOPMENT.md
├─ CHANGELOG.md
├─ BUILD.md
├─ IMPLEMENTATION_NOTES.md   # 旧リンク互換の安全設計インデックス
└─ RELEASE_STATUS.md
```

`IMPLEMENTATION_NOTES.md` は重複本文を持たない互換インデックスです。開発上の正本はこの `DEVELOPMENT.md` と `docs/ARCHITECTURE.md` です。

---

## 4. アーキテクチャ

主要レイヤーは次の3つです。

### Playnite統合層 — `DLsiteUpdateMonitor.Plugin`

- GenericPlugin lifecycle
- main menu / game context menu
- settings UI
- global progress / cancel
- Game選択
- Core serviceの組み立て
- 保存タイミング
- Playnite tag同期
- 状態変更操作の排他制御

### Coreドメイン層 — `DLsiteUpdateMonitor.Core`

- DLsite URL / ProductId解決
- HTTP GET
- HTML解析
- 正規化
- RemoteSnapshot検証・fingerprint・比較
- Tracking state machine
- memory cache
- Tracking JSON persistence

CoreはPlaynite SDKへ依存せず、.NET 8のテストから検証できる構成です。

### 永続化層 — `TrackingRepository`

Playnite SDKの `GetPluginUserDataPath()` が返すディレクトリへ追跡JSONを保存します。絶対パスは固定していません。

詳細なデータフロー・図は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) を参照してください。

---

## 5. 主要コードと責務

### `DLsiteUpdateMonitorPlugin`

責務:

- Playnite APIとの接続
- Runtime service構築
- menu提供
- batch check
- saveタイミング
- tag同期
- `適用済み` / `無視` / `監視状態をリセット`
- `operationLock`による排他

重要な副作用:

- `tracking.json`保存
- Playnite tag更新
- dialog表示
- DLsiteページをOS既定ブラウザで開く

`GlobalProgressResult.Error` がある場合はタグ反映を中止します。途中保存・最終保存を含む処理例外を「成功したように見せる」変更をしないでください。

### `DlsiteLinkResolver`

入力: Playnite `Game.Links` のURL列。

ルール:

- hostは正確に `dlsite.com` または `*.dlsite.com`
- ProductIdは `/product_id/<ID>` からのみ抽出
- v0.1.0形式は英字2文字 + 6桁または8桁数字
- 異なる複数IDがあれば `Ambiguous`
- DLsite hostなのに対応IDがなければ `Invalid`

### `DlsiteHttpClient`

- HTTP GETのみ
- shared `HttpClient`
- `fetchGate`でDLsite取得を直列化
- request開始間隔を維持
- 各試行にtimeout
- cancellation対応
- retry対象を限定
- `Retry-After`を固定delayより優先

### `DlsitePageParser`

重要なDOM契約:

- タイトル: `#work_name`
- 商品情報: `#work_outline`
- HTTP 200利用不可ページ: `.error_box_work`

`更新情報`:

- row自体がない → `Missing`
- rowがあるが空/解析不能 → `Unparsed`
- text抽出はまず `td` の**direct text node**を優先し、direct textがない場合だけ `TextContent`へfallbackする
- nested label/link等を更新信号へ不用意に混ぜないための意図的な実装

`ファイル容量`:

- 現行比較では必須
- parse不能なら候補Snapshotはcomparison eligibleではない

### `UpdateInfoNormalizer`

- Unicode NFKC
- CRLF/CRをLFへ統一
- 水平空白正規化
- 20xx年の日付を `yyyy-MM-dd` へ正規化
- 過剰な空行を圧縮
- raw値も保持

### `FileSizeNormalizer`

- `B / KB / MB / GB / KiB / MiB / GiB`
- 1024基準
- byteへ変換
- `MidpointRounding.AwayFromZero`
- raw値とnormalized byte表現を保持

### `SnapshotValidator`

comparison eligible最低条件:

- ProductIdあり
- FileSizeが `Parsed`
- UpdateInfoがnullでない
- UpdateInfoが `Unparsed` でない

### `SnapshotFingerprint`

SHA-256対象:

- Snapshot schema version
- uppercase ProductId
- UpdateInfo state + normalized value
- FileSize state + byte value

取得時刻、商品名、URLはFingerprintに含めません。

### `SnapshotComparer`

副作用を持たない比較器です。

- candidateが比較不能 → `Indeterminate`
- acknowledgedがnull → `BaselineCreated`
- ProductId不一致 → `IdentityMismatch`
- UpdateInfo `Missing → Parsed` → Changed
- UpdateInfo `Parsed → Missing` → Indeterminate
- `Unparsed`を含む → Indeterminate
- FileSizeは双方Parsedのときだけ比較

`Indeterminate` / `IdentityMismatch` の `TargetState` はnullです。呼び出し側は「MonitoringStateを変更しない」という意味として扱います。

### `TrackingStateMachine`

- Baseline → acknowledged/currentをcandidate cloneへ、`Clean`
- Changed → currentだけ更新しPendingへ
- NoChange → current更新、`Clean`
- Indeterminate / IdentityMismatch → acknowledged/current/MonitoringStateを進めない
- failure → health/errorを更新するが、既知Snapshot/MonitoringStateを維持
- Acknowledge → currentをacknowledgedへclone、`Clean`
- AppliedとIgnoredはSnapshot遷移は同じだがHistory eventを区別
- Reset → SnapshotだけでなくRegisteredUrl / RequestedProductId / ResolvedUrl / ResolvedProductId等もclear

### `UpdateCheckService`

- HTTP前に既追跡ProductIdと現在LinkのProductIdを照合
- 初回Baseline前でもrequested/resolved ProductIdを照合
- healthyなRemoteSnapshotだけをcacheへ登録
- remote observationのhealthy/degradedと、各Game固有のcomparison healthを分離
- 1ゲームの壊れた旧Snapshotで、別Gameが使える正常RemoteSnapshotの再利用を阻害しない

### `SnapshotCache`

- process memoryのみ
- key: ProductId
- TTLあり（default 24h）
- Put/GetでSnapshotをclone
- Playnite再起動で消える
- cache hit時もRemoteSnapshotの `FetchedAtUtc` は**元のremote取得時刻を保持**する
- cacheのTTL判定に使う `StoredAtUtc` は別管理

### `TrackingRepository`

詳細は「永続化」を参照。

### `PlayniteTagService`

自分の `[DLsite更新] ` prefixのtagだけを操作します。ユーザーの無関係なtagへ触れません。

---

## 6. 状態モデル

### `MonitoringState`

- `Uninitialized`
- `Clean`
- `PendingUpdateInfo`
- `PendingFileChange`
- `PendingUpdateAndFileChange`

### `CheckHealth`

- `NeverChecked`
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

`MonitoringState` と `CheckHealth` は独立しています。

例:

```text
MonitoringState = PendingUpdateInfo
LastCheckHealth = RateLimited
```

これは「未処理更新は残っているが、直近checkは429だった」という有効な状態です。

Pending中にDLsite側Candidateがさらに変化した場合、`CurrentSnapshot`は新しい安全なCandidateへ更新され、必要に応じて `ChangeUpdated` historyが追加されます。

---

## 7. Snapshotの意味

### `AcknowledgedSnapshot`

ユーザーが確認済みとした比較基準。Pending中は自動更新しません。

### `CurrentSnapshot`

最新の**安全に比較できた**Candidate。Pending中の追加変化では更新されることがあります。

### `LastObservation`

直近の観測。取得自体は成功したが比較不能なdegraded Snapshotも診断目的で保持される場合があります。

`LastObservation` と `CurrentSnapshot` を同じ意味として扱わないでください。

---

## 8. 観測と比較の意味

`ObservedField<T>`:

```text
Missing  = 項目が存在しないことを有効に観測
Parsed   = 安全に解析済み
Unparsed = 項目はあるが安全に解析不能
```

UpdateInfo:

| Before | After | Result |
|---|---|---|
| Missing | Missing | Same |
| Missing | Parsed | Changed |
| Parsed | Parsed同値 | Same |
| Parsed | Parsed差分 | Changed |
| Parsed | Missing | Indeterminate |
| Unparsedを含む | any | Indeterminate |

FileSizeは双方 `Parsed` の場合だけbyte値を比較し、それ以外は `Indeterminate` です。

---

## 9. Product identity保護

作品identityは次を区別します。

1. `RegisteredUrl`
2. `RequestedProductId`
3. `ResolvedUrl`
4. `ResolvedProductId`

### Playnite Linkが別作品へ変わった場合

既追跡ProductIdと現在LinkのProductIdが違えば、**HTTP通信前**に `LinkError` で停止します。

旧Baselineは維持し、別作品へ切り替えるには明示的な `監視状態をリセット` を要求します。

### DLsite側redirectで別作品になった場合

`RedirectedToDifferentProduct` とし、初回であってもBaselineを作成しません。

---

## 10. 永続化

### `TrackingDatabase`

現行 `SchemaVersion = 1`。

主要フィールド:

- `CreatedAtUtc`
- `LastSavedAtUtc`
- `Games: Dictionary<Guid, GameTrackingRecord>`

`GameTrackingRecord` はProduct identity、MonitoringState、CheckHealth、3種Snapshot、時刻、LastError、Historyを保持します。

### 保存ファイル

```text
tracking.json
tracking.backup.json
tracking.tmp
tracking.json.corrupt-YYYYMMDD-HHmmss...
tracking.backup.json.corrupt-YYYYMMDD-HHmmss...
```

### 保存アルゴリズム

1. `tracking.tmp`へUTF-8 BOMなしでWriteThrough
2. writer / streamをflushし `Flush(true)`
3. tempを即座に再読込しJSONとSchemaを検証
4. 検証成功後のみprimaryへ昇格
5. 既存primaryがある場合はbackupを維持
6. `File.Replace`非対応/IO error時のみfallback replace

壊れたprimary/backupを検出した場合、次回save前に `.corrupt-*` へ退避して保全します。

### load failure

- primary正常 → primary使用
- primary破損 / backup正常 → backup使用 + warning
- primary/backup両方破損 → new DB。ただし破損ファイルを次回save前に保全
- Schema > current → `UnsupportedTrackingSchemaException`

Plugin側は新しすぎるSchemaで `persistenceBlocked` となり、更新チェックと保存を停止します。

**未知Schemaを古い実装で上書きしないこと。**

---

## 11. HTTP / Retry policy

固定REST APIではなく、Playnite Linkに登録されたDLsite商品URLへGETします。

固定Cookie:

```text
locale=ja_JP
loginchecked=1
```

これはユーザーのログインSession Credentialではありません。

既定値:

- Request interval: 2秒
- Timeout: 20秒
- RetryCount: 2
- 1回目retry delay: 2秒
- 2回目以降: 5秒

| Fetch status | Retry |
|---|---|
| 429 RateLimited | する |
| Timeout | する |
| NetworkError | する |
| 5xx ServerError | する |
| 403 AccessDenied | しない |
| 404 / 410 ProductUnavailable | しない |
| Cancelled | しない |

`Retry-After` が有効なら固定delayより優先します。

---

## 12. Batch / 非同期 / 排他

永続Queueはありません。1回の操作内でGameを順番に処理します。

### `operationLock`

次を同時実行させません。

- 更新チェック
- 適用済み
- 無視
- 監視状態リセット

`tracking` dictionary / `tracking.json` の同時変更防止が目的です。

### HTTP側

さらに `DlsiteHttpClient.fetchGate` がRemote GETを直列化します。

### 同一ProductIdのbatch内共有

- healthy RemoteSnapshotがある → 後続Gameはcache経由でGame固有比較
- Product-level取得自体が失敗 → 同batch内で同じProductIdを盲目的に再GETせずfailureを共有

### 保存タイミング

- 10 Game処理ごと
- batch終了時

Playnite終了時にoperationが進行中なら、競合するshutdown saveを無理に行わずskipします。途中保存を優先する設計です。

---

## 13. Settings

| Setting | Default | Range | 用途 |
|---|---:|---:|---|
| `RequestIntervalSeconds` | 2 | 1〜60 | HTTP開始間隔 |
| `TimeoutSeconds` | 20 | 5〜120 | 1試行timeout |
| `RetryCount` | 2 | 0〜5 | retry追加回数 |
| `CacheHours` | 24 | 1〜168 | memory cache TTL |
| `HistoryLimit` | 50 | 10〜500 | Game単位history上限 |
| `EnableTags` | true | bool | Playnite tag同期 |

壊れたsettingsでPlugin起動自体を失敗させるよりdefault値を使う設計です。

`EndEdit()` 後にruntime serviceを再構築します。operation中なら、そのoperationは開始時のoptionsで完了し、途中でclient/cache/state machineを差し替えません。

Runtime再構築によりmemory cacheは新instanceになります。

---

## 14. 依存関係とFramework上の注意

### Core `net462`

- `System.Net.Http` **explicit reference**
- AngleSharp 0.9.9 (`ExcludeAssets=runtime`)
- Newtonsoft.Json 10.0.3 (`ExcludeAssets=runtime`)

`HttpClient`はCore自身が使用するため、`System.Net.Http`参照はPlugin側だけではなく**Coreのnet462 target自身に必要**です。ここを削るとCore単体の.NET Framework 4.6.2 buildが失敗します。`Static-Validate.py`にもこの参照漏れを検出するチェックがあります。

### Core `net8.0`

- AngleSharp 0.9.9
- Newtonsoft.Json 10.0.3

### Plugin

- PlayniteSDK 6.16.0 (`ExcludeAssets=runtime`)
- `System.Net.Http`
- Core project reference

### Tests

- Microsoft.NET.Test.Sdk 18.10.0
- xunit.v3 4.0.0
- xunit.runner.visualstudio 4.0.0

### 配布payload

必要:

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

同梱しない:

```text
Playnite.SDK.dll
AngleSharp.dll
Newtonsoft.Json.dll
```

Playnite本体側と競合するruntime DLLを独自同梱しない方針です。

---

## 15. 絶対に壊してはいけない不変条件

将来の変更で最も重要な部分です。便利さ・速度・コード短縮を理由に弱体化しないでください。

1. **初回正常取得を更新扱いにしない。** 初回はBaselineのみ。
2. **比較基準を最後の取得結果へ自動で進めない。** `AcknowledgedSnapshot`は明示Acknowledgeまで維持。
3. **HTTP/解析failureでPendingを消さない。** Failureは`MonitoringState`、`AcknowledgedSnapshot`、`CurrentSnapshot`を変更しない。
4. **不確定な観測をChangedへ変換しない。** `Parsed → Missing`やUnparsed FileSizeはIndeterminate。
5. **別作品へ旧Baselineを流用しない。** Link変更はHTTP前に停止し明示Resetを要求。
6. **redirect先ProductIdを再確認する。** 初回Baseline前でもidentity checkする。
7. **比較不能時に状態を進めない。** null `TargetState`の意味を保つ。
8. **保存途中のJSONをprimaryへ昇格しない。** temp再読込検証後だけ置換。
9. **新しいSchemaを古いコードで上書きしない。** unsupported schemaはfail closed。
10. **checkと状態mutationを同時実行しない。** `operationLock`の意味を維持。
11. **save失敗をtagだけ成功扱いにしない。** progress error時はtag反映を停止。
12. **ユーザーの無関係なPlaynite tagを削除しない。** `[DLsite更新] `だけを所有。
13. **Playnite本体提供runtime DLLを重複同梱しない。**
14. **Remote observationのhealthとGame固有comparison healthを混同しない。**
15. **DLsiteへの並列大量アクセスへ変更しない。** 現行は直列 + interval。
16. **cache hitでremote取得時刻を書き換えない。** `FetchedAtUtc`は元観測時刻を保持。
17. **ParserのDOM欠落を安易に「値が消えた」と解釈しない。** Missing / Unparsed semanticsを維持。

これらを変更する場合は、理由、影響、回帰テスト、必要な実機Smoke Test、ドキュメント更新を同一変更へ含めてください。

---

## 16. Failure時の原則

基本方針は「わからないときは既知状態を壊さない」です。

Failure時に進めてよいもの:

- `LastAttemptAtUtc`
- `LastCheckHealth`
- `LastError`
- 状況によって `LastObservation`

進めてはいけないもの:

- `AcknowledgedSnapshot`
- `CurrentSnapshot`
- `MonitoringState`
- `LastSuccessfulCheckAtUtc`

---

## 17. テスト戦略

### Core tests

`tests/DLsiteUpdateMonitor.Core.Tests`

v0.1.0検証記録では63ケースPASS。

重要な回帰:

- 初回正常取得がBaseline
- 初回でも別作品redirectはBaselineを作らない
- HTTP failure後もPending/Snapshot維持
- parse degradedでCurrentSnapshotを上書きしない
- healthy remote observationのcache reuse
- HTTP 200利用不可ページを`ProductUnavailable`
- 既追跡ProductId変更は明示Resetを要求しHTTPを行わない
- 1 Gameの不正なacknowledged snapshotが同ProductIdの別Gameのcache reuseを汚染しない

HTTP testsでは `HttpClient` / Clock / Delayを差し替え、実DLsiteへアクセスせず検証します。

### Static validation

```powershell
python .\tools\Static-Validate.py
```

これは実build/testの代替ではありません。

### Playnite実機Smoke Test

[docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) のGate A〜Fを順番に実施します。

**Gateを飛ばして全ライブラリへ進まないでください。**

---

## 18. Build / Release

Build詳細: [BUILD.md](BUILD.md)

標準gate:

```powershell
.\tools\Validate-Build.ps1
```

Release詳細: [docs/RELEASE.md](docs/RELEASE.md)

```text
Version更新
  ↓
Core tests / Plugin build
  ↓
artifacts/plugin
  ↓
Playnite Smoke Gate A〜F
  ↓
実機確認した同じpayloadをPackage-Release
  ↓
.pext + SHA256SUMS + RELEASE-SUMMARY
  ↓
CHANGELOG / docs確認
  ↓
Commit / Tag / GitHub Release
```

**Smoke Test後に再buildした未検証binaryを配布しないでください。**

### Version管理

少なくとも次の2か所を同期します。

- `src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj`
- `src/DLsiteUpdateMonitor.Plugin/extension.yaml`

v0.1.0では両方 `0.1.0`。

現行validation scriptが両Versionの一致まで自動検査することは確認できていないため、Release前に手動でも確認します。

Version変更時はREADME / DEVELOPMENT / CHANGELOG / 必要なRelease記録も同期します。

---

## 19. 確認できる過去の重要問題

### 古いWindows PowerShellでのSHA-256計算互換性

v0.1.0最終検証記録では、実機検証済みPlugin本体ソースを変えず、Release packaging scriptのSHA-256計算を `Get-FileHash` 依存から `System.Security.Cryptography.SHA256` ベースへ変更しています。

再発防止:

- Release toolingのPowerShell互換性を変更する場合は対象環境でpackage/hash生成を確認
- 実機検証済みpayloadを勝手に再buildしない

これ以外はVersion・原因・修正を確実に復元できない過去バグを推測で追加しません。

---

## 20. 確認済み / 未確認 / 既知制限

### 確認済み

- Core test 63ケース
- net462 Plugin build
- Playnite 10.56で読み込み / menu / settings
- DLsite link診断 33 / 33
- 初回Baseline
- 同一状態再check
- 一時通信failure後の状態保持
- 小規模batch
- 33作品全ライブラリcheck
- `.pext`作成・install
- install後の既存監視状態維持

### 未確認

- PlayniteがPlugin uninstall時にuser dataを自動削除するか
- Playnite 10.56以外の互換性
- 将来のDLsite HTML変更
- GitHub Actions上のbuild/test
- 公開Git tag運用

### 既知制限

- 手動checkのみ
- DLsiteのみ
- 商品ページの2信号のみ監視
- local game Version判定なし
- FileSizeが安全にparseできない場合は比較しない
- ProductId形式は英字2文字 + 6/8桁数字

---

## 21. Debug / 障害調査

PluginはPlaynite SDKの `LogManager.GetLogger()` を使用します。独自logファイル絶対pathは定義していません。

問題時に保全する情報:

- 症状
- Playnite Version
- Plugin Version
- 操作内容
- `tracking.json`
- `tracking.backup.json`
- `.corrupt-*`
- 問題のDLsite URL
- Playnite log
- 発生時刻
- HTTP status / `CheckHealth`

共有前に個人情報や不要なURL情報が含まれていないか確認してください。

独自Debug mode設定は現行実装にありません。

詳細: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)

---

## 22. 変更時のチェックリスト

### 開発開始前

1. `README.md`
2. `DEVELOPMENT.md`
3. `CHANGELOG.md`
4. `docs/ARCHITECTURE.md`
5. 対象コード
6. 関連テスト

`IMPLEMENTATION_NOTES.md` は旧URL/旧参照互換のインデックスなので、通常の開発開始時に別途読む必要はありません。

### 実装中

- 不変条件を壊していないか
- Failure pathをHappy pathと同程度に検討したか
- identity mismatchを曖昧に受け入れていないか
- persistenceを直接上書きへ戻していないか
- user data / tagを広く変更していないか
- retry対象を安易に増やしていないか
- parser DOM変化を「実データ変更」と誤認していないか

### 実装後

1. 関連Unit Test追加/更新
2. 全Core test
3. Plugin build
4. `Validate-Build.ps1`
5. 必要なSmoke Gate
6. README更新
7. DEVELOPMENT更新
8. CHANGELOG更新
9. 必要なdocs更新
10. Version整合性確認

---

## 23. コーディング上の注意

- Core logicは可能な限りPlaynite SDKから独立
- HTTP / Clock / Delayのtest差し替え可能性を維持
- Snapshot共有参照を不用意にmutationせずclone
- 状態変化は `TrackingStateMachine` へ集約
- Product identity判定は既存Resolver / CheckServiceを利用
- parserでDOM欠落を安易に「値が消えた」と扱わない
- destructive operationを追加する場合は明示確認・test・復旧手順をセットで設計

---

## 24. ドキュメントの役割

- `README.md` — 利用者向け詳細情報 + 技術概要
- `DEVELOPMENT.md` — **内部仕様・安全条件・開発引き継ぎの正本**
- `CHANGELOG.md` — Version単位の変更履歴
- `docs/ARCHITECTURE.md` — コンポーネント関係・処理/データフロー・設計判断
- `docs/TROUBLESHOOTING.md` — 詳細な問題解決
- `docs/RELEASE.md` — Release工程
- `docs/SMOKE_TEST.md` — Playnite実機Gate
- `BUILD.md` — build / validation command
- `RELEASE_STATUS.md` — v0.1.0固有の検証証跡
- `IMPLEMENTATION_NOTES.md` — 旧リンクを壊さないための互換インデックス。仕様正本ではない

同じ説明を複数文書へ丸ごと複製せず、概要 + 正本へのリンクを優先します。

`RELEASE_STATUS.md` は一般手順ではなくv0.1.0固有の検証証跡なので、`docs/RELEASE.md`があっても維持します。

---

## 25. 次回AI / 開発者への引き継ぎ

次の開発を始める際は、最低限次を確認してください。

1. `README.md`
2. `DEVELOPMENT.md`
3. `CHANGELOG.md`
4. `docs/ARCHITECTURE.md`
5. 最新コード
6. 関連テスト

変更後はコードだけで終わらせず、仕様が変わった文書を同じPR/Commitで同期してください。

特に次の3原則を利便性のために弱めないでください。

- **安全側に停止する**
- **既存の確認済み状態を失わない**
- **作品identityを暗黙に切り替えない**
