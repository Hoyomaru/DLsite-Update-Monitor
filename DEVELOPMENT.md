# DLsite Update Monitor — 開発・保守ガイド

この文書は、DLsite Update Monitor の開発継続、保守、コードレビュー、AIへの引き継ぎにおける**開発者向け正本**です。

今後の変更では、最初に [README.md](README.md)、この `DEVELOPMENT.md`、[CHANGELOG.md](CHANGELOG.md)、対象コードと関連テストを確認してください。内部構造は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)、ビルドは [BUILD.md](BUILD.md)、Release工程は [docs/RELEASE.md](docs/RELEASE.md)、実機確認は [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) を参照します。

仕様の最終判断は**現在の実装とテスト**を基準にします。公開済みv1.0.0の履歴と、現在のUnreleased候補の実装を混同しないでください。

---

## 1. 現在の状態

| 項目 | 現在の状態 |
|---|---|
| 現在の正式Version | 1.0.0 |
| GitHub Release | `DLsite Update Monitor v1.0.0` 公開済み |
| Git tag | `v1.0.0` |
| Plugin / extension Version | `1.0.0`（Unreleased候補ではまだVersion bumpしていない） |
| Playnite実機基準 | 10.56 |
| Plugin target | .NET Framework 4.6.2 |
| Core target | .NET Framework 4.6.2 / .NET 8.0 |
| 現行候補Coreテスト | 72件 PASS / 0 failed / 0 skipped（GitHub Actions） |
| 現行候補Static validation | PASS |
| 現行候補Windows Plugin build | PASS |
| 現行候補payload境界検証 | PASS |
| 現行候補Playnite Smoke Test | 未実施。Gate A〜Gをこれから実施 |
| Tracking Schema | 1 |
| GitHub Actions / CI | 導入済み |
| License | 未設定 |

公開v1.0.0の過去実機検証根拠は [RELEASE_STATUS.md](RELEASE_STATUS.md) に保存されています。現在のUnreleased候補は、v1.0.0公開後に永続化・URL信頼境界・失敗分離・診断/UIを変更しているため、過去のSmoke Test結果をそのまま流用しません。

公開Asset:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA-256: 676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

---

## 2. プロジェクトの責務

本プロジェクトは、Playniteの`Game.Links`からDLsite作品を解決し、DLsite商品ページ上の次の2信号を監視するGenericPluginです。

1. `更新情報`
2. `ファイル容量`

次は判定しません。

- ローカルゲーム本体のVersion
- EXEのFile Version
- ローカルファイルハッシュ
- パッチ適用状態
- DLsite以外のストア

比較基準は常に、ユーザーが最後に確認済みとした`AcknowledgedSnapshot`です。

```text
AcknowledgedSnapshot
        │ compare
        ▼
Candidate
        ├─ 同じ          → Clean
        ├─ 更新情報差分   → PendingUpdateInfo
        ├─ 容量差分       → PendingFileChange
        ├─ 両方差分       → PendingUpdateAndFileChange
        └─ 比較不能       → 既存MonitoringStateを維持
```

初回の正常観測は更新ではなくBaseline作成です。

---

## 3. リポジトリ構成

```text
DLsite-Update-Monitor/
├─ .github/workflows/            # Core / Windows Plugin CI
├─ src/
│  ├─ DLsiteUpdateMonitor.Core/
│  │  ├─ Http/                   # GET、interval、timeout、retry、最終URL検証
│  │  ├─ Models/                 # Snapshot、状態、Tracking DB
│  │  ├─ Parsing/                # HTML parser、正規化
│  │  ├─ Persistence/            # tracking.json安全読み書き
│  │  └─ Services/               # Resolver、比較、state machine、cache、clone、check service
│  └─ DLsiteUpdateMonitor.Plugin/
│     ├─ DLsiteUpdateMonitorPlugin.cs
│     ├─ PlayniteTagService.cs
│     ├─ PluginSettings.cs
│     ├─ PluginSettingsView.xaml
│     └─ extension.yaml
├─ tests/DLsiteUpdateMonitor.Core.Tests/
├─ tools/
├─ docs/
├─ README.md
├─ DEVELOPMENT.md
├─ CHANGELOG.md
├─ BUILD.md
├─ IMPLEMENTATION_NOTES.md
└─ RELEASE_STATUS.md
```

`IMPLEMENTATION_NOTES.md`は旧リンク互換のインデックスです。内部仕様の正本はこの文書と`docs/ARCHITECTURE.md`です。

---

## 4. レイヤーと責務

### Playnite統合層 — `DLsiteUpdateMonitor.Plugin`

- GenericPlugin lifecycle
- main menu / game context menu
- settings UI
- global progress / cancel
- Game選択
- Core serviceの組み立て
- batch orchestration
- 永続化checkpoint
- Playnite tag同期
- 監視詳細表示
- リンク診断
- エラー対象だけの再確認
- 孤立tracking recordの確認付き整理
- `operationLock`によるmutation排他

### Coreドメイン層 — `DLsiteUpdateMonitor.Core`

- DLsite URL / ProductId解決
- HTTP GET + retry + trust boundary
- HTML解析 / 正規化
- RemoteSnapshot validation / fingerprint / comparison
- Tracking state machine
- memory cache
- TrackingDatabase deep clone
- Tracking JSON persistence / recovery

CoreはPlaynite SDKへ依存せず、.NET 8テストから検証できます。

---

## 5. 主要コンポーネント

### `DLsiteUpdateMonitorPlugin`

主な責務:

- Playnite API接続
- runtime service構築
- menu提供
- batch check
- tag同期
- `適用済み` / `無視` / `監視状態をリセット`
- tracking detail / diagnostics
- orphan cleanup
- operation排他

### copy-on-write / durable checkpoint

状態を変更する操作では、共有中の`tracking`を先に直接変更しません。

```text
tracking
  ↓ deep clone
working
  ↓ mutation
repository.Save(working)
  ↓ success only
tracking = working
```

`Acknowledge`、`Ignore`、`Reset`、孤立record整理はこの原則に従います。

Batch checkでは10ゲームごとに保存し、**最後に正常保存できたcheckpointだけ**をlive状態へ昇格します。後続saveが失敗した場合、未保存の変更を後のshutdown save等で復活させてはいけません。

`GlobalProgressResult.Error`がある場合はタグ反映を中止します。

### `DlsiteLinkResolver`

入力: `Game.Links`のURL列。

ルール:

- absolute URIのみ
- schemeはHTTP / HTTPSのみ
- hostは正確に`dlsite.com`または`*.dlsite.com`
- HTTPはHTTPSへcanonicalize
- ProductIdは`/product_id/<ID>`から抽出
- IDは英字2文字 + 6桁または8桁数字
- 異なる複数IDがあれば`Ambiguous`
- DLsite hostでも対応ProductIdを抽出できなければ`Invalid`

### `DlsiteHttpClient`

- GETのみ
- shared `HttpClient`
- `fetchGate`でDLsite取得を直列化
- request開始間隔を維持
- timeout / cancellation対応
- retry対象を限定
- `Retry-After`を優先
- responseの最終`RequestUri`を検証

最終URLは**absolute HTTPS + `dlsite.com` / `*.dlsite.com`**でなければ`UntrustedRedirect`として受け入れません。

HTTP分類:

- 429 → retry
- timeout → retry
- network → retry
- 5xx → retry
- 403 → retryしない
- 404 / 410 → `ProductUnavailable`, retryしない
- その他4xx → `ClientError`, retryしない
- cancellation → retryしない

### `DlsitePageParser`

主要DOM契約:

- タイトル: `#work_name`
- 商品情報: `#work_outline`
- HTTP 200利用不可ページ: `.error_box_work`

`更新情報`は`Missing / Parsed / Unparsed`を区別します。`FileSize`は現行比較では必須で、安全にparseできなければcomparison eligibleではありません。

resolved URLが与えられた場合、作品identityはそのURLから抽出し、ProductIdが取れない最終URLをsource URLのProductIdで暗黙に補完しません。

### `SnapshotComparer`

副作用を持ちません。

- acknowledged == null → `BaselineCreated`
- ProductId不一致 → `IdentityMismatch`
- candidate比較不能 → `Indeterminate`
- UpdateInfo / FileSize差分 → Changed
- `Indeterminate` / `IdentityMismatch`の`TargetState`はnull

### `TrackingStateMachine`

- Baseline → acknowledged/currentをcandidate cloneへ、`Clean`
- Changed → current更新、Pendingへ
- NoChange → current更新、`Clean`
- failure / Indeterminate / IdentityMismatch →既知Ack/Current/MonitoringStateを進めない
- Acknowledge → currentをacknowledgedへclone、`Clean`
- Applied / IgnoredはHistory eventを区別
- Reset → Snapshotだけでなく作品identityもclear

### `UpdateCheckService`

- HTTP前に現在Link ProductIdと既追跡ProductIdを照合
- healthy RemoteSnapshotだけをcacheへ登録
- Remote観測のhealthとGame固有comparison healthを分離
- `ProductCheckFailureScope`でfailure範囲を表す

```text
LocalRecord   = そのGame record固有の失敗
RemoteProduct = 同じProductIdで共有可能なRemote側失敗
```

Link変更等の`LocalRecord`失敗を、同一ProductIdを参照する別Gameへ伝播させてはいけません。

### `SnapshotCache`

- process memoryのみ
- key: ProductId
- TTL default 24h
- Put/Getでclone
- Playnite再起動で消える
- cache hitでもRemoteSnapshotの元`FetchedAtUtc`を保持

### `TrackingDatabaseCloner`

TrackingDatabase / GameTrackingRecord / snapshots / errors / historyをdeep cloneします。copy-on-writeの境界なので、新しいmutable fieldをModelへ追加した場合はclonerへの追加と回帰テストを忘れないでください。

### `PlayniteTagService`

Pluginが所有するのは次の**正確な2名称だけ**です。

```text
[DLsite更新] 更新あり
[DLsite更新] 配布物変更
```

同じprefixのユーザー作成tagは所有しません。

Stateとの対応:

| MonitoringState | Tag |
|---|---|
| Clean / Uninitialized | なし |
| PendingUpdateInfo | 更新あり |
| PendingFileChange | 配布物変更 |
| PendingUpdateAndFileChange | **2tag両方** |

---

## 6. 状態モデル

### `MonitoringState`

- `Uninitialized`
- `Clean`
- `PendingUpdateInfo`
- `PendingFileChange`
- `PendingUpdateAndFileChange`

### `CheckHealth`

MonitoringStateとは独立しています。例えばPending中に429が発生した場合、Pendingは維持し`LastCheckHealth`だけがRateLimitedへ変わります。

### Snapshotの意味

`AcknowledgedSnapshot`: ユーザーが確認済みとした比較基準。

`CurrentSnapshot`: 最新の**安全に比較できた**Candidate。

`LastObservation`: 直近の観測。比較不能なdegraded Snapshotも診断用に保持される場合があります。

---

## 7. 永続化

現行`TrackingDatabase.SchemaVersion = 1`です。

保存ファイル:

```text
tracking.json
tracking.backup.json
tracking.tmp
*.corrupt-YYYYMMDD-HHmmss...
```

### 保存アルゴリズム

1. `LastSavedAtUtc`を更新
2. `tracking.tmp`へdurable write
3. tempを即再読込
4. JSON / Schema / 明確なsemantic破損を検証
5. 成功したtempだけprimaryへ昇格
6. 既存primaryはbackupとして保持
7. save失敗時は呼び出し元DBの`LastSavedAtUtc`も元へ戻す

deserializeには`MaxDepth = 64`を設定します。`Games` dictionary内にnull recordがあれば破損扱いとし、primaryであればbackup fallbackの対象になります。

### primary破損 / backup正常

backupから復旧した直後の次saveでは、**正常backupを破損primaryで上書きしません**。

```text
corrupt primary → primary.corrupt-*
healthy backup  → そのまま保持
new save        → new primary
```

### primary / backup両方破損

新規DBを使いますが、次save前に両破損ファイルを`.corrupt-*`へ保全します。

### 新しすぎるSchema

`UnsupportedTrackingSchemaException`を投げ、Plugin側は`persistenceBlocked`になります。未知Schemaを古い実装で上書きしてはいけません。

---

## 8. Batch / 排他

### `operationLock`

次のmutating operationを同時実行させません。

- 更新チェック
- 適用済み / 無視
- reset
- 孤立tracking record整理

### 同一ProductId共有

Batch dictionaryにはfull HTML / full `ProductCheckResult`を保持せず、再利用可否・health・messageだけの軽量`BatchProductResult`を保持します。

- healthy reusable snapshot → cache経由で後続Game固有比較
- `RemoteProduct` failure → 後続同ProductIdへ共有
- `LocalRecord` failure → 共有しない

### 保存checkpoint

- 10 Gameごと
- batch終了時

途中saveが成功した時点だけlive checkpointを更新します。

---

## 9. Settings

| Setting | Default | Range |
|---|---:|---:|
| `RequestIntervalSeconds` | 2 | 1〜60 |
| `TimeoutSeconds` | 20 | 5〜120 |
| `RetryCount` | 2 | 0〜5 |
| `CacheHours` | 24 | 1〜168 |
| `HistoryLimit` | 50 | 10〜500 |
| `EnableTags` | true | bool |

保存済みsettingsがdeserializeできても数値が範囲外なら、その項目を既定値へ補正してPlugin起動を継続します。

`EndEdit()`後にruntime serviceを再構築します。operation中なら即時rebuildを避けます。設定保存でmemory cacheは新instanceになります。

---

## 10. UI / 診断機能

### リンク診断

件数だけでなく、問題のあるGame名と理由を表示します。Ambiguousの場合は候補ProductIdも表示し、詳細は最大40行 + 残件数です。読み取り専用です。

### エラー/要確認の再確認

`LastCheckHealth != Healthy && != NeverChecked`の追跡済みGameだけをforce refreshします。

### 監視詳細

1 Gameだけを対象に、MonitoringState / Health / ProductId / timestamps / Ack / Current / LastObservation / LastError / 最近10件のHistoryを読み取り専用で表示します。

### 孤立tracking record整理

Playnite DBに存在しないGameIdを候補化し、ProductId / GameIdをpreviewしてYes/No確認後に削除します。自動削除は禁止です。削除もclone + save success後swapで行います。

---

## 11. 依存関係

### Core `net462`

- `System.Net.Http` explicit reference
- AngleSharp 0.9.9 (`ExcludeAssets=runtime`)
- Newtonsoft.Json 10.0.3 (`ExcludeAssets=runtime`)

### Core `net8.0`

- AngleSharp 0.9.9
- Newtonsoft.Json 10.0.3

### Plugin

- PlayniteSDK 6.16.0 (`ExcludeAssets=runtime`)
- System.Net.Http
- Core project reference

### Tests

- Microsoft.NET.Test.Sdk 18.10.0
- xunit.v3 4.0.0
- xunit.runner.visualstudio 4.0.0

Pluginのnet462 runtimeではPlaynite本体提供のAngleSharp / Newtonsoft.Jsonを利用するため、package versionを安全性だけを理由に無検証で上げたりruntime DLLを同梱したりしないでください。変更する場合はPlaynite runtime互換性も実機検証対象です。

---

## 12. Build / CI / Release

### GitHub Actions

`.github/workflows/core-ci.yml`:

- Ubuntu: Core restore/test + Static validation
- Windows: Plugin restore/build
- Windows: 必須payloadと禁止DLLの境界検証
- Windows: 実機Smoke用`DLsiteUpdateMonitor-smoke-<commit SHA>` Artifact

現行候補の自動検証は72 tests PASS、Static PASS、Windows build PASS、payload PASSです。

### ローカルgate

```powershell
.\tools\Validate-Build.ps1
```

### payload

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

### 実機Gate

[docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) のGate A〜Gを順番に実施します。実機確認したpayloadを、理由なく再buildして別binaryへ差し替えないでください。

Release flow:

```text
Automated tests/build
  ↓
exact candidate payload
  ↓
Playnite Smoke Gate A〜G
  ↓
同じpayloadをpackage
  ↓
.pext install確認
  ↓
Version / CHANGELOG / docs最終化
  ↓
Tag / GitHub Release
```

---

## 13. 絶対に壊してはいけない不変条件

1. **初回正常取得を更新扱いにしない。**
2. **比較基準を最後の取得結果へ自動で進めない。**
3. **HTTP/解析failureでPending / Ack / Currentを消さない。**
4. **不確定な観測をChangedへ変換しない。**
5. **別作品へ旧Baselineを流用しない。**
6. **最終取得先をHTTPS + DLsite hostに限定する。**
7. **resolved URLのidentityをsource URLでごまかさない。**
8. **LocalRecord failureを別Gameへ共有しない。**
9. **保存途中のJSONをprimaryへ昇格しない。**
10. **保存失敗したmutationをlive stateへ残さない。**
11. **backup復旧後の正常backupを破損primaryで潰さない。**
12. **新しいSchemaを古いコードで上書きしない。**
13. **mutation operationを同時実行しない。**
14. **save失敗をtagだけ成功扱いにしない。**
15. **Plugin所有外のtagを削除しない。** 所有は正確な2名称だけ。
16. **Playnite本体提供runtime DLLを重複同梱しない。**
17. **Remote observation healthとGame固有comparison healthを混同しない。**
18. **DLsiteへの並列大量アクセスへ変更しない。**
19. **cache hitでRemoteSnapshotの`FetchedAtUtc`を書き換えない。**
20. **DOM欠落を安易に実データ削除と解釈しない。**
21. **孤立recordを自動削除しない。** preview + 明示確認を維持。

これらを変更する場合は理由、影響、回帰テスト、必要なSmoke Test、文書更新を同じ変更へ含めてください。

---

## 14. Failure時の原則

基本方針は「わからないときは既知状態を壊さない」です。

Failure時に進めてよいもの:

- `LastAttemptAtUtc`
- `LastCheckHealth`
- `LastError`
- 状況によって`LastObservation`

進めてはいけないもの:

- `AcknowledgedSnapshot`
- `CurrentSnapshot`
- `MonitoringState`
- `LastSuccessfulCheckAtUtc`

さらに**保存に失敗した場合は、上記failure記録を含め未保存mutationをlive stateへ確定しない**ことがPlugin orchestration上の追加条件です。

---

## 15. テスト戦略

`tests/DLsiteUpdateMonitor.Core.Tests`の現行候補は72件です。

重要回帰には次を含みます。

- 初回Baseline
- redirect ProductId mismatch
- HTTP failureでPending維持
- degraded parseでCurrentを上書きしない
- cache reuse
- HTTP 200 unavailable
- ProductId変更はreset要求
- same ProductIdでLocalRecord failureを共有しない
- HTTP→HTTPS canonicalization
- external / HTTP final resolved URL rejection
- unexpected 4xx nonretry
- corrupt primary→backup recovery→save後もbackup recoverable
- null tracking record fallback
- deep database clone

GitHub ActionsはCoreだけでなくWindows Plugin buildも行いますが、Playnite UI / Database統合は実機Smoke Testが必要です。

---

## 16. 公開v1.0.0の履歴との区別

`RELEASE_STATUS.md`に残る63件 / Gate A〜F / 33作品等は、v0.1.0正式公開前に得られた過去検証証跡です。

現在のUnreleased候補は別の実装です。現時点で確認済みなのは:

```text
[x] Core 72/72
[x] Static validation
[x] Windows net462 Plugin build
[x] payload boundary
[ ] Playnite Smoke Gate A〜G
[ ] candidate .pext install
```

実機Gateが終わるまでは、公開v1.0.0と同等の検証済みReleaseとは扱いません。

---

## 17. 変更時チェックリスト

### 開発開始前

1. README
2. DEVELOPMENT
3. CHANGELOG
4. ARCHITECTURE
5. 対象コード
6. 関連テスト

### 実装中

- failure pathをhappy pathと同程度に検討したか
- copy-on-write境界を壊していないか
- identity / trust boundaryを弱めていないか
- Local / Remote failure scopeを混同していないか
- persistenceを直接上書きへ戻していないか
- user data / tagの操作範囲を広げていないか
- retry対象を安易に増やしていないか
- destructive operationへ明示確認があるか

### 実装後

1. 関連Unit Test
2. 全Core test
3. Windows Plugin build
4. payload boundary
5. 必要なSmoke Gate
6. README / DEVELOPMENT / CHANGELOG / ARCHITECTURE同期
7. Version整合性

---

## 18. Debug / 障害調査

問題時に保全する情報:

- 症状 / 再現手順
- Playnite Version
- Plugin Version / candidate commit SHA
- `tracking.json`
- `tracking.backup.json`
- `.corrupt-*`
- 問題のDLsite URL
- Playnite log
- 発生時刻
- HTTP status / CheckHealth

実機Smoke中の中止条件は [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) を正とします。

---

## 19. ドキュメントの役割

- `README.md` — 利用者向け情報 + 現在の開発状態
- `DEVELOPMENT.md` — **内部仕様・安全条件・引き継ぎの正本**
- `CHANGELOG.md` — Version / Unreleased変更履歴
- `docs/ARCHITECTURE.md` — コンポーネント関係・フロー・設計判断
- `docs/TROUBLESHOOTING.md` — 障害対応
- `docs/RELEASE.md` — Release工程
- `docs/SMOKE_TEST.md` — Playnite実機Gate
- `BUILD.md` — build / CI / artifact
- `RELEASE_STATUS.md` — v0.1.0正式公開前の歴史的検証証跡
- `IMPLEMENTATION_NOTES.md` — 旧リンク互換インデックス

---

## 20. 次回AI / 開発者への引き継ぎ

次の3原則を利便性のために弱めないでください。

- **安全側に停止する**
- **既存の確認済み状態を失わない**
- **作品identityを暗黙に切り替えない**

現在のUnreleased候補については、コード実装と自動検証は完了しており、次の主要gateは**同一candidate payloadによるPlaynite 10.56実機Smoke Test A〜G**です。
