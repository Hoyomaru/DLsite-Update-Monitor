# リリース手順

この文書はDLsite Update Monitorのリリース作業手順です。

現在のv1.0.0では、**ローカル自動検証 → Playnite実機Smoke Test → 検証した同一payloadのパッケージ化 → Git tag / GitHub Release**という順序を前提にしています。

v1.0.0は、v0.1.0として検証していた現行実装を初回正式公開版として位置付けるReleaseです。監視ロジックとTracking Schemaはv0.1.0検証時点から変更していませんが、Version metadata変更後のbinaryは別成果物になるため、公開前検証を省略しません。

> [!IMPORTANT]
> Smoke Testで確認した`artifacts\plugin`を、パッケージ作成前に再ビルドして別バイナリへ差し替えないでください。実機で確認したものと配布物を一致させることが重要です。

## 1. v1.0.0リリース状況

2026-09-14時点:

- 正式公開Version: `1.0.0`
- Plugin csproj Version: `1.0.0`
- extension Version: `1.0.0`
- リリース位置付け: 初回正式公開版
- Tracking Schema: `1`
- v0.1.0検証基準のCore tests: 63ケース PASS
- v0.1.0検証基準のPlaynite実機Smoke Test: PASS
- v0.1.0検証基準の33作品全ライブラリ確認: PASS
- v0.1.0 `.pext`作成・インストール試験: PASS
- v1.0.0 GitHub Release: この手順で作成する対象
- GitHub Actions: 未導入

v0.1.0の検証証跡は [../RELEASE_STATUS.md](../RELEASE_STATUS.md) を参照してください。

v1.0.0公開用のRelease本文は [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md) を基準にします。

## 2. リリース前に更新するもの

Versionを上げる場合は、少なくとも次を確認します。

```text
src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj
src/DLsiteUpdateMonitor.Plugin/extension.yaml
README.md
DEVELOPMENT.md
CHANGELOG.md
docs/ARCHITECTURE.md
docs/RELEASE.md
```

v1.0.0ではcsprojとextension.yamlのVersionをどちらも`1.0.0`へ揃えます。

`Package-Release.ps1`は`extension.yaml`からVersionを取得します。csprojとのVersion一致はRelease前に明示的に確認してください。

## 3. 作業ツリーを確認する

Release候補へ意図しない変更が混ざっていないことを確認します。

特に確認するもの:

- Core source
- Plugin source
- `extension.yaml`
- tests
- tools
- README / DEVELOPMENT / CHANGELOG / docs
- `.gitignore`

`artifacts/`や`.pext`はソースへコミットしません。

## 4. 自動検証Gate

Windowsでリポジトリルートから実行します。

```powershell
.\tools\Validate-Build.ps1
```

または:

```cmd
tools\Validate-Build.cmd
```

### 必要環境

- .NET 8 SDK
- .NET Framework 4.6.2 Developer/Targeting Pack
- Visual Studio 2022 / Build Tools相当

### Scriptが確認すること

1. .NET 8 SDK存在
2. .NET Framework 4.6.2 reference assemblies存在
3. Core tests restore
4. Core tests実行
5. Plugin restore
6. `net462` Plugin build
7. 必須payloadの存在
8. 禁止runtime DLLの非同梱
9. `extension.yaml` Module / Type基本条件

成功すると:

```text
artifacts\plugin\
artifacts\TestResults\
artifacts\VALIDATION-SUMMARY.txt
```

が生成されます。

### 必須payload

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

### 同梱禁止として検査されるDLL

```text
Playnite.SDK.dll
AngleSharp.dll
Newtonsoft.Json.dll
```

自動検証が失敗したらRelease作業を止めてください。

### v1.0.0で再実行が必要な理由

v0.1.0で63ケースPASS・Plugin build PASSの証跡はありますが、csproj / extension Versionを1.0.0へ変更した後のbinaryはv0.1.0の既存binaryと同一ではありません。

そのため、ロジック変更がなくてもv1.0.0公開候補で`Validate-Build.ps1`を再実行します。

## 5. 任意の静的事前検証

Python 3がある場合:

```powershell
python .\tools\Static-Validate.py
```

これは構造上の問題を早く見つける補助であり、`dotnet test`やPlugin buildの代替ではありません。

## 6. 開発用配置

最初の実機確認では、検証用Playnite profileまたはバックアップを推奨します。

```powershell
.\tools\Install-Dev.ps1 -WhatIf
.\tools\Install-Dev.ps1
```

既定配置先:

```text
%APPDATA%\Playnite\Extensions\DLsiteUpdateMonitor\
```

既存extensionは`artifacts\install-backups`へバックアップしてから置換されます。

## 7. Playnite実機Smoke Test

[SMOKE_TEST.md](SMOKE_TEST.md)に従い、Gateを順番に実行します。

```text
Gate A: Plugin読み込み / Menu / Settings
  ↓
Gate B: DLsiteリンク診断のみ
  ↓
Gate C: 既知1作品でBaseline / 再チェック
  ↓
Gate D: failure safety / ProductId変更安全性
  ↓
Gate E: 5〜10作品
  ↓
Gate F: 全ライブラリ
```

v1.0.0はロジック変更を伴わないVersion整理ですが、公開binaryにVersion metadata変更が入るため、少なくともRelease candidateの読み込み、Version表示、主要操作、既存tracking引き継ぎを実機で再確認してください。正式配布物として扱う場合はGate A〜Fを完了するのが最も安全です。

### 即時中止条件

次のどれかが起きた場合、packagingへ進まないでください。

- 初回checkで更新tagが付く
- 403 / 429 / timeoutでPending stateが消える
- parser failureで既知Snapshotが上書きされる
- Plugin管理外tagが削除される
- Notes / Links / title等の想定外metadataが変わる
- 多数の無関係Gameが同時に更新扱いになる

問題時は`tracking.json`、backup、ログ、対象URL、再現手順を保全します。

## 8. `.pext`パッケージ化

Smoke Testに成功した**同じ`artifacts\plugin`**をpackします。

```powershell
.\tools\Package-Release.ps1 -ConfirmRuntimeValidated
```

または:

```cmd
tools\Package-Release.cmd
```

Toolboxを自動検出できない場合:

```powershell
.\tools\Package-Release.ps1 `
  -ConfirmRuntimeValidated `
  -ToolboxPath "C:\path\to\Toolbox.exe"
```

`-ConfirmRuntimeValidated`なしでは意図的に失敗します。

## 9. Packaging scriptの検査

`Package-Release.ps1`は少なくとも次を確認します。

- `artifacts\plugin`の存在
- `VALIDATION-SUMMARY.txt`の成功記録
- 必須payload
- 禁止runtime DLL非同梱
- extension Id一致
- extension Version形式
- Playnite `Toolbox.exe`
- `.pext`がちょうど1件生成されたこと

生成物:

```text
artifacts\release\<package>.pext
artifacts\release\SHA256SUMS.txt
artifacts\release\RELEASE-SUMMARY.txt
```

v1.0.0で期待するpackage名:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
```

## 10. SHA-256確認

Scriptが`System.Security.Cryptography.SHA256`でhashを計算し、`SHA256SUMS.txt`へ出力します。

公開前に:

- `.pext`名
- Version
- SHA-256
- `RELEASE-SUMMARY.txt`

が意図した候補と一致することを確認してください。

### v0.1.0の過去検証値

```text
Package:
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_0_1_0.pext

SHA-256:
c84d8fbb3fb82e5d3d5c6bd974c153b33dd8437ff96f4447a4ed0c68c7a939bf
```

このhashは**v0.1.0専用の履歴値**です。v1.0.0では必ず新しい生成物から再計算してください。

## 11. `.pext`インストール確認

Release候補の`.pext`そのものを使ってPlayniteへinstallし、少なくとも次を確認します。

- Pluginが読み込まれる
- Versionが`1.0.0`として認識される
- Menuが表示される
- 設定を開ける
- 既存の追跡状態が期待どおり引き継がれる
- 代表的なcheckが動く
- Plugin管理tag以外に想定外変更がない

Build outputを直接配置した確認だけで、`.pext` install確認の代わりにしないでください。

## 12. ドキュメント最終更新

公開直前に次を同期します。

### `CHANGELOG.md`

- Version
- Date
- Added / Changed / Fixed / Security等
- 実際に確認できた変更だけ

### `README.md`

- Current Version
- Installation / Update
- Known limitations
- user-facing behavior

### `DEVELOPMENT.md`

- 内部仕様
- 不変条件
- 新しいState/Schema/API
- test状況

### `docs/*`

影響があるものだけ更新します。

v1.0.0では [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md) もRelease作成直前に確認します。

## 13. Release commit

Release対象のコード・docs・Versionが揃っている状態でcommitします。

v1.0.0の推奨commit message:

```text
release: prepare v1.0.0
```

Commit後に、release候補として検証したsourceとcommitが一致しているか確認してください。

## 14. Git tag

v1.0.0からVersionに対応するTagを使う運用を推奨します。

v1.0.0:

```text
v1.0.0
```

Tagは、Version metadataとRelease用docsが揃ったRelease commitへ付けます。

次Version以降も`vX.Y.Z`形式を維持してください。

## 15. GitHub Release

### v1.0.0 Release title

```text
DLsite Update Monitor v1.0.0
```

### v1.0.0 Release notes

[RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md) の本文を使用します。

最低限含める情報:

- 初回正式公開版であること
- 何ができるVersionか
- 主要機能
- 安全性方針
- 注意事項 / 制限
- 対応Playnite Version
- install/update上の注意
- 非公式ツールであること
- SHA-256は`SHA256SUMS.txt`を正とすること

### Assets

少なくとも:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA256SUMS.txt
```

`RELEASE-SUMMARY.txt`を公開assetにするかは運用判断ですが、内容にローカル絶対path等が含まれる可能性があるため、公開前に確認してください。

## 16. GitHub Release後の確認

- Release pageが開く
- Version / tag / titleがすべて`1.0.0` / `v1.0.0`と一致
- `.pext`がdownloadできる
- asset名が正しい
- SHA256SUMSと実asset hashが一致
- READMEのinstall案内が現状と一致
- CHANGELOGのVersion/dateが一致
- source treeへ`.pext`が誤commitされていない

## 17. v1.0.0 Release前チェックリスト

```text
[x] Plugin csproj Versionを1.0.0へ更新
[x] extension.yaml Versionを1.0.0へ更新
[x] Version同士が一致
[x] CHANGELOG更新
[x] README更新
[x] DEVELOPMENT更新
[x] ARCHITECTURE更新
[x] RELEASE docs更新
[x] GitHub Release title / notes準備
[ ] Validate-Build PASS（v1.0.0 metadataで再実行）
[ ] Core tests PASS
[ ] Plugin net462 build PASS
[ ] 必須payload確認
[ ] 禁止runtime DLL非同梱
[ ] Smoke Gate A PASS
[ ] Smoke Gate B PASS
[ ] Smoke Gate C PASS
[ ] Smoke Gate D PASS
[ ] Smoke Gate E PASS
[ ] Smoke Gate F PASS
[ ] Smoke後にpayloadを再buildしていない
[ ] Package-Release PASS
[ ] v1.0.0 .pextが1つだけ生成
[ ] v1.0.0 SHA-256確認
[ ] v1.0.0 .pext install test PASS
[ ] 既存tracking状態の引き継ぎ確認
[ ] Release commit確認
[ ] v1.0.0 Tag作成
[ ] GitHub Release作成
[ ] Asset download確認
[ ] 公開Asset hash再確認
```

チェック済み項目は、このVersion準備commitで完了する文書/Version整合作業のみです。runtime validation項目は実機で確認してからチェックしてください。

## 18. 現在の自動化 / 手動工程

| 工程 | 現在 |
|---|---|
| Restore | `Validate-Build.ps1`で自動 |
| Core test | `Validate-Build.ps1`で自動 |
| Plugin build | `Validate-Build.ps1`で自動 |
| Payload検査 | `Validate-Build.ps1`で自動 |
| Playnite Smoke Test | 手動 |
| 全ライブラリ確認 | 手動 |
| `.pext` pack | `Package-Release.ps1`で半自動 |
| SHA-256 | `Package-Release.ps1`で自動 |
| `.pext` install test | 手動 |
| CHANGELOG更新 | 手動 |
| Commit | 手動 |
| Tag | 手動 |
| GitHub Release | 手動 |
| GitHub Actions CI | 未導入 |

## 19. Releaseでやってはいけないこと

- v0.1.0の`.pext`を名前だけ変えてv1.0.0として配布する
- v0.1.0のSHA-256をv1.0.0へコピーする
- Smoke Test前に`.pext`を最終配布物と決める
- Smokeで確認した後に別binaryをbuildし直してそのまま配布する
- test失敗を無視してpackする
- `-ConfirmRuntimeValidated`をSmoke未実施なのに付ける
- Versionをcsprojとextension.yamlで不一致にする
- source repoへ`.pext`や`artifacts/`をcommitする
- 未確認機能をRelease notesへ書く
- Credentialや個人情報をRelease assets / logsへ入れる

## 20. リリース失敗時

Release候補で問題が見つかった場合は、Tag/Releaseを急いで作らず原因修正へ戻ります。

修正後は、影響範囲に応じて自動検証とSmoke Testを**再度**実行してください。

特にCore/Plugin binaryが変わった場合、以前の実機Smoke結果をそのまま新binaryへ流用しません。
