# リリース手順

この文書はDLsite Update Monitorのリリース作業手順と、公開済みReleaseの確認記録です。

基本フローは、**ローカル自動検証 → Playnite実機Smoke Test → 検証した同一payloadのパッケージ化 → Release commit → Git tag → GitHub Release**です。

> [!IMPORTANT]
> Smoke Testで確認した`artifacts\plugin`を、パッケージ作成前に再ビルドして別バイナリへ差し替えないでください。実機で確認したものと配布物を一致させることが重要です。

## 1. 現在の公開状況

2026-09-14時点で、v1.0.0は初回正式公開版としてGitHub Releasesへ公開済みです。

```text
Version: 1.0.0
Release title: DLsite Update Monitor v1.0.0
Tag: v1.0.0
Tagged commit: a8aa212fbcaaded3411db74643c9345584e30e50
Tracking Schema: 1
Package: DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA-256: 676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

公開Release:

https://github.com/Hoyomaru/DLsite-Update-Monitor/releases/tag/v1.0.0

公開Assets:

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA256SUMS.txt
```

v0.1.0の正式公開前検証証跡は [../RELEASE_STATUS.md](../RELEASE_STATUS.md) を参照してください。v1.0.0の公開本文は [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md) に保存しています。

## 2. Versionを上げるときに更新するもの

少なくとも次を同期します。

```text
src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj
src/DLsiteUpdateMonitor.Plugin/extension.yaml
README.md
DEVELOPMENT.md
CHANGELOG.md
docs/ARCHITECTURE.md
docs/RELEASE.md
```

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

## 10. SHA-256確認

Scriptが`System.Security.Cryptography.SHA256`でhashを計算し、`SHA256SUMS.txt`へ出力します。

公開前に:

- `.pext`名
- Version
- SHA-256
- `RELEASE-SUMMARY.txt`

が意図した候補と一致することを確認してください。

### v1.0.0公開値

```text
Package:
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext

SHA-256:
676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

この値はGitHubが公開Assetに記録しているdigestとして公開後に確認した値です。

### v0.1.0の過去検証値

```text
Package:
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_0_1_0.pext

SHA-256:
c84d8fbb3fb82e5d3d5c6bd974c153b33dd8437ff96f4447a4ed0c68c7a939bf
```

v0.1.0のhashは**履歴値**です。別Versionへ流用しません。

## 11. `.pext`インストール確認

Release候補の`.pext`そのものを使ってPlayniteへinstallし、少なくとも次を確認します。

- Pluginが読み込まれる
- Versionが意図した値として認識される
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

公開後は、Release URL、Tag、公開Asset、実SHA-256をREADME / DEVELOPMENT / CHANGELOG / Release記録へ反映します。

## 13. Release commit

Release対象のコード・docs・Versionが揃っている状態でcommitします。

例:

```text
release: prepare vX.Y.Z
```

Commit後に、release候補として検証したsourceとcommitが一致しているか確認してください。

v1.0.0ではTag `v1.0.0` がRelease準備commit `a8aa212fbcaaded3411db74643c9345584e30e50`を指しています。

## 14. Git tag

Versionに対応するTagは`vX.Y.Z`形式を使用します。

例:

```text
v1.0.0
v1.0.1
v1.1.0
```

Tagは、Version metadataとRelease用docsが揃ったRelease commitへ付けます。

## 15. GitHub Release

Release title:

```text
DLsite Update Monitor vX.Y.Z
```

Release notesには最低限、次を含めます。

- 何ができるVersionか
- 主要機能 / 変更
- 安全性方針
- 注意事項 / 制限
- 対応Playnite Version
- install/update上の注意
- 非公式ツールであること
- 公開Asset名
- SHA-256または`SHA256SUMS.txt`への案内

Assets:

```text
<package>.pext
SHA256SUMS.txt
```

`RELEASE-SUMMARY.txt`を公開assetにするかは運用判断ですが、内容にローカル絶対path等が含まれる可能性があるため、公開前に確認してください。

## 16. GitHub Release後の確認

公開後は次を確認します。

- Release pageが開く
- Version / tag / titleが一致
- Tagが意図したRelease commitを指す
- `.pext`がRelease Assetとして存在
- `SHA256SUMS.txt`がRelease Assetとして存在
- asset名が正しい
- 公開AssetのSHA-256を記録
- READMEのinstall案内が現状と一致
- CHANGELOGのVersion/dateが一致
- source treeへ`.pext`が誤commitされていない

### v1.0.0でGitHub上から確認済み

```text
[x] Release page公開
[x] Release title = DLsite Update Monitor v1.0.0
[x] Tag = v1.0.0
[x] Tagがa8aa212fbcaaded3411db74643c9345584e30e50を指す
[x] .pext Asset存在
[x] SHA256SUMS.txt Asset存在
[x] .pext Asset名確認
[x] GitHub記録の.pext SHA-256確認
```

`SHA256SUMS.txt`の本文と実Asset hashの一致そのものは、この公開後確認では独立に取得・照合できていないため、確認済みとは記録しません。

## 17. v1.0.0の検証記録について

v1.0.0は公開済みですが、現時点のリポジトリ内にVersion metadata変更後の次の結果を独立した検証証跡として追加していません。

```text
[ ] Validate-Build PASS（v1.0.0 metadata）
[ ] Core tests PASS（v1.0.0 candidate）
[ ] Plugin net462 build PASS（v1.0.0 candidate）
[ ] Smoke Gate A〜F PASS（v1.0.0 candidate）
[ ] v1.0.0 .pext install test PASS
[ ] 既存tracking状態の引き継ぎ確認（v1.0.0 .pext）
```

公開済みという事実だけからこれらを逆算してPASSにはしません。将来、実施記録を追加する場合は、実際のログ・summary・手順に基づいて更新してください。

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

- 過去Versionの`.pext`を名前だけ変えて新Versionとして配布する
- 過去VersionのSHA-256を新Versionへコピーする
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
