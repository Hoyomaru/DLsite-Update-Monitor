# リリース手順

この文書はDLsite Update MonitorのRelease作業手順と、公開済みReleaseの確認記録です。

現在の基本フローは、**自動検証 → 同一candidate payloadのPlaynite実機Smoke Test → 同一payloadのパッケージ化 → `.pext` install確認 → Version/Docs最終化 → Tag → GitHub Release**です。

> [!IMPORTANT]
> Smoke Testで確認したbinaryを、package前に理由なく再buildして別binaryへ差し替えないでください。実機で確認したものと配布物を一致させます。

## 1. 現在の公開状況

v1.1.0は2026-09-15に安定化Releaseとして公開済みです。

```text
Version: 1.1.0
Release title: DLsite Update Monitor v1.1.0
Tag: v1.1.0
Tagged commit: e846cb245b22200a455001918c86115e95c5433c
Tracking Schema: 1
Package: DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_1_0.pext
SHA-256: 9128b3ca11472b322d5307e6820b7fae902ca13d284b02645b87b9b3aace15de
```

v1.1.0はCore / Windows CI、Playnite 10.56 Smoke Test Gate A〜G、Version 1.1.0最終Artifact短縮Smoke、最終`.pext`インストール試験まで完了しています。公開AssetのGitHub側digestも上記SHA-256と一致しています。

次の変更は`Unreleased`として管理し、v1.1.0の検証結果と混同しません。

過去v0.1.0検証証跡: [../RELEASE_STATUS.md](../RELEASE_STATUS.md)

v1.1.0公開本文: [RELEASE_NOTES_1.1.0.md](RELEASE_NOTES_1.1.0.md)

v1.0.0公開本文: [RELEASE_NOTES_1.0.0.md](RELEASE_NOTES_1.0.0.md)

## 2. Release候補をfreezeする

Release前に、意図したsource / tests / docsが候補branchへ揃っていることを確認します。

最低確認対象:

- Core source
- Plugin source
- `extension.yaml`
- tests
- `.github/workflows`
- tools
- README / DEVELOPMENT / CHANGELOG / docs
- `LICENSE`
- `.gitignore`

`artifacts/`や`.pext`はsourceへcommitしません。

Smoke Test開始後にsourceを変更した場合は、**新commitを新candidateとして自動検証と必要なSmoke Gateをやり直します**。

## 3. 自動検証Gate

### GitHub Actions

`.github/workflows/core-ci.yml`で次を自動実行します。

Ubuntu:

- .NET 8 SDK
- Core restore/test
- `Static-Validate.py`

Windows:

- Plugin restore
- `.NET Framework 4.6.2`向けPlugin build
- 必須payload確認
- ルートのMIT `LICENSE`存在確認
- 禁止runtime DLL非同梱確認
- Smoke Test用payloadのArtifact化

現行候補で期待する自動Gate:

```text
Core tests: 72 passed / 0 failed / 0 skipped
Static validation: PASS
Windows Plugin build: PASS
Payload boundary: PASS
```

どれかが失敗したらSmoke Testへ進みません。

### ローカル検証

必要ならWindowsで:

```powershell
.\tools\Validate-Build.ps1
```

または:

```cmd
tools\Validate-Build.cmd
```

ローカル検証は有用ですが、CI candidateを実機で検証する場合はcandidate commit SHAを必ず記録します。

## 4. Smoke Test用payload

Windows CI jobは次をArtifactへstageします。

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
LICENSE
*.pdb（存在する場合）
```

`LICENSE`はリポジトリルートのMIT License本文をそのままstageします。実機確認したcandidateからLicense noticeを落とした別payloadをRelease用に作成しないでください。

Artifact名:

```text
DLsiteUpdateMonitor-smoke-<commit SHA>
```

このArtifactはCIで実際にbuildし、payload境界を検証したbinaryです。実機Gateでは可能な限りこのArtifactを使用します。

独自同梱しないruntime DLL:

```text
Playnite.SDK.dll
AngleSharp.dll
Newtonsoft.Json.dll
```

## 5. Playnite実機Smoke Test

[SMOKE_TEST.md](SMOKE_TEST.md)のGateを順番に実行します。

```text
Gate A: Plugin読み込み / Menu / Settings
  ↓
Gate B: 詳細DLsiteリンク診断
  ↓
Gate C: 既知1作品 / Baseline / Tracking details
  ↓
Gate D: Failure safety / Targeted recheck / URL・ProductId安全性
  ↓
Gate E: Tag ownership / Combined state / Acknowledge・Reset
  ↓
Gate F: Orphan tracking cleanup（検証用Game）
  ↓
Gate G: 5〜10作品 / restart / tag toggle / 全ライブラリ
```

Gateを飛ばして全ライブラリへ進みません。

### 即時中止条件

少なくとも次はRelease停止条件です。

- 初回checkで更新tagが付く
- HTTP/parse failureでPendingや既知Snapshotが消える
- save失敗表示後のmutationが後で勝手に確定する
- backup recovery後に正常backupが失われる
- Plugin所有外tagが削除される
- orphan cleanupで現存Gameのrecordが消える
- Notes / Links / title等の意図しないmetadata変更
- 無関係な多数Gameが更新扱いになる

問題時は`tracking.json`、backup、`.corrupt-*`、ログ、URL、candidate commit SHA、再現手順を保全します。

## 6. Smoke結果を記録する

実機Gateの記録には最低限次を残します。

```text
Candidate commit SHA:
Artifact name:
Playnite Version:
OS:
Gate A: PASS / FAIL
Gate B: PASS / FAIL
Gate C: PASS / FAIL
Gate D: PASS / FAIL
Gate E: PASS / FAIL
Gate F: PASS / FAIL
Gate G: PASS / FAIL
備考:
```

過去のv0.1.0 / v1.0.0 / v1.1.0証跡と、新candidateの結果を混ぜません。

## 7. Versionを確定する

Smoke Testで機能・互換性が確認できた後、ReleaseするVersionを決めます。

少なくとも次の2か所を同期します。

```text
src/DLsiteUpdateMonitor.Plugin/DLsiteUpdateMonitor.Plugin.csproj
src/DLsiteUpdateMonitor.Plugin/extension.yaml
```

さらに次を同期します。

- README.md
- DEVELOPMENT.md
- CHANGELOG.md
- docs/ARCHITECTURE.md
- docs/RELEASE.md
- 必要ならRelease notes

Version bump後にsource/binaryが変わる場合は、Release候補として必要な自動検証を再実行します。Version metadataだけの変更でも、配布binaryが変わる以上、最終candidateのhashと検証対象を明示してください。

## 8. `.pext`パッケージ化

実機検証したpayloadをpackageします。

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

`-ConfirmRuntimeValidated`はSmoke Testを実施したという運用上の明示確認です。未実施なのにflagだけ付けません。

Packaging scriptは必須payload（`LICENSE`を含む）、禁止runtime DLL、extension Id / Version、Toolbox、生成`.pext`件数等を検査します。

生成物:

```text
artifacts\release\<package>.pext
artifacts\release\SHA256SUMS.txt
artifacts\release\RELEASE-SUMMARY.txt
```

## 9. SHA-256確認

公開する`.pext`そのものからSHA-256を計算し、`SHA256SUMS.txt`とRelease記録へ反映します。

過去Versionのhashを新Versionへ流用しません。

v1.1.0公開値:

```text
9128b3ca11472b322d5307e6820b7fae902ca13d284b02645b87b9b3aace15de
```

v1.0.0公開値:

```text
676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

## 10. `.pext`インストール確認

Release候補の`.pext`そのものをPlayniteへinstallして確認します。

- Pluginが読み込まれる
- Versionが意図した値
- Menu / Settingsが開く
- 既存tracking状態を期待どおり引き継ぐ
- 代表的checkが動く
- Plugin所有外tag / metadataに想定外変更がない
- パッケージ元の検証済みpayloadにMIT `LICENSE`が含まれている

Build outputを直接配置したSmokeだけで、`.pext` install確認の代わりにしません。

## 11. Release docs最終化

公開直前:

### CHANGELOG

- Version / Date
- Added / Changed / Fixed / Security
- 自動検証結果
- 実際のSmoke結果

### README

- Current Version
- install/update
- current features
- known limitations
- License表記

### DEVELOPMENT / ARCHITECTURE

- internal spec
- safety invariants
- Schema / API / state
- test status

## 12. Release commit / Tag

Release用source・Version・docsが揃ったcommitへ`vX.Y.Z` tagを付けます。

例:

```text
release: prepare vX.Y.Z
vX.Y.Z
```

Tagが実際に検証したRelease candidateと対応しているか確認します。

## 13. GitHub Release

Title:

```text
DLsite Update Monitor vX.Y.Z
```

最低限含める情報:

- 主要変更
- 安全性上の変更
- Playnite対応Version
- install/update注意
- 制限事項
- 非公式ツールであること
- License（MIT）
- Asset名
- SHA-256 / `SHA256SUMS.txt`

Assets:

```text
<package>.pext
SHA256SUMS.txt
```

## 14. 公開後確認

- Release pageが開く
- Version / title / tagが一致
- tagが意図したcommitを指す
- `.pext` / SHA256SUMSが存在
- Asset名が正しい
- 公開Asset hashを確認
- README / CHANGELOGが公開状態と一致
- ルート`LICENSE`とREADMEのLicense表記が一致
- 配布`.pext`が`LICENSE`を含む検証済みpayloadから生成されている
- source treeへ`.pext`を誤commitしていない

## 15. 公開Release履歴

### v1.1.0 — 2026-09-15

- Release title: `DLsite Update Monitor v1.1.0`
- Tag: `v1.1.0`
- Tagged commit: `e846cb245b22200a455001918c86115e95c5433c`
- Runtime release-prep merge commit: `5ba71c57771fee80455f33a908619bf37c38de41`
- Package: `DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_1_0.pext`
- SHA-256: `9128b3ca11472b322d5307e6820b7fae902ca13d284b02645b87b9b3aace15de`
- Core 72 tests / Static validation / Windows Plugin build / payload boundary: **PASS**
- Playnite 10.56 Smoke Gate A〜G: **PASS**
- Version 1.1.0最終Artifact短縮Smoke: **PASS**
- 最終`.pext`install test: **PASS**

TagはRelease Notes追加後のdocs-only commitを指しています。Runtime payloadはその直前のrelease-prep merge以降変更されていません。

### v1.0.0 — 2026-09-14

公開v1.0.0のRelease/Tag/Assetは確認済みです。一方、v1.0.0 metadataでの独立したSmoke再実施結果を後から推測してPASS扱いしません。

将来のUnreleased候補も同様に、**自動CIがgreenだから実機もPASSしたとは記録しません**。Smoke Gate A〜Gを実行して初めてRuntimeValidated候補になります。
