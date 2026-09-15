# ビルドと検証

## 対象環境

- Playnite安定版: 10.56
- プラグイン対象: .NET Framework 4.6.2
- PlayniteSDK: 6.16.0
- Coreテスト対象: .NET 8.0

プラグインでは、Playnite本体が提供する`AngleSharp 0.9.9`と`Newtonsoft.Json 10.0.3`を利用します。拡張機能側へ競合するDLLを同梱しない構成です。

## Windowsで必要なもの

1. Visual Studio 2022 Build Tools または Visual Studio 2022
2. .NET Framework 4.6.2 Developer/Targeting Pack
3. .NET 8 SDK
4. 実機確認用のPlaynite 10.56

## Coreテストの復元と実行

```powershell
cd <repository-root>
dotnet restore .\DLsiteUpdateMonitor.sln
dotnet test .\tests\DLsiteUpdateMonitor.Core.Tests\DLsiteUpdateMonitor.Core.Tests.csproj -c Release
```

プラグインをビルド・インストールする前に、すべてのCoreテストが成功することを確認してください。

## プラグインのビルド

```powershell
dotnet build .\src\DLsiteUpdateMonitor.Plugin\DLsiteUpdateMonitor.Plugin.csproj -c Release
```

出力先:

```text
src\DLsiteUpdateMonitor.Plugin\bin\Release\
```

少なくとも次のファイルが必要です。

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

依存関係の方針を明示的に変更して再検証しない限り、`Playnite.SDK.dll`、`AngleSharp.dll`、`Newtonsoft.Json.dll`を拡張機能へ独自同梱しないでください。

## 開発用インストール

Release出力を専用のPlaynite拡張機能フォルダへ配置します。例:

```text
%APPDATA%\Playnite\Extensions\DLsiteUpdateMonitor\
```

Playniteを再起動し、アドオン一覧にプラグインが表示され、`DLsite Update Monitor`のメニューが利用できることを確認してください。

`tools\Install-Dev.ps1`を使う場合は、既存の開発用拡張フォルダをバックアップしてから置き換えます。

```powershell
.\tools\Install-Dev.ps1 -WhatIf
.\tools\Install-Dev.ps1
```

## 初回の実機検証順序

最初はPlayniteのバックアップ、または検証用プロファイルを使用してください。

1. `DLsiteリンク診断`だけを実行し、ゲームのメタデータが変更されないことを確認します。
2. DLsiteリンクが正しいゲームを1本選び、`今すぐ確認`を実行します。
3. プラグインのユーザーデータフォルダに`tracking.json`が作成されることを確認します。
4. 初回取得が「監視開始」のベースラインとして扱われ、更新タグが付かないことを確認します。
5. 同じゲームをもう一度確認し、「変更なし」になることを確認します。
6. `監視詳細を表示`でBaseline / Current / LastObservationとHealthを確認します。
7. エラー時に既存ベースラインや保留状態が消えないことを確認します。
8. ここまで成功したら5〜10作品で小規模チェックを行います。
9. 小規模チェックが成功してから全ライブラリを確認します。

詳細は [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) を参照してください。

## Windowsでの一括検証

リポジトリのルートから次を実行します。

```powershell
.\tools\Validate-Build.ps1
```

または:

```cmd
tools\Validate-Build.cmd
```

このスクリプトは以下を確認します。

- 必要なSDK・Targeting Pack
- NuGetパッケージの復元
- すべてのCoreテスト
- `net462`向けPlayniteプラグインのビルド
- 必須成果物
- `Playnite.SDK.dll`、`AngleSharp.dll`、`Newtonsoft.Json.dll`が誤って同梱されていないこと
- `extension.yaml`の基本整合性

成功した成果物は`artifacts\plugin`へ出力されます。

## GitHub Actions CI

`.github/workflows/core-ci.yml`では、push / pull request時の自動検証を行います。

- Ubuntu: .NET 8でCoreテスト + `Static-Validate.py`
- Windows: `net462` Playniteプラグインのrestore/build
- Windows: 必須成果物と「同梱禁止DLL」の境界検証
- Windows: 実機Smoke Test用payloadのArtifact作成

Smoke Test用Artifactには、CIで実際にbuild・検証した次のファイルだけをステージします。

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
*.pdb（存在する場合）
```

Artifact名は`DLsiteUpdateMonitor-smoke-<commit SHA>`です。実機検証では、可能な限りこのArtifactと同じcommitを使い、Smoke Test後に別バイナリへ再ビルドしないでください。

## 任意の静的事前検証

Python 3がある場合は、C#の実ビルド前に軽量な構造チェックを実行できます。

```powershell
python .\tools\Static-Validate.py
```

主に以下を確認します。

- csproj / XAMLのXML構造
- ProjectReferenceのパス
- `extension.yaml`の基本条件
- C#の括弧・文字列・コメント境界
- 固定している依存パッケージのバージョン
- xUnitテストケース数の静的推定
- `net462`向け`System.Net.Http`参照

これは`dotnet test`の代替ではありません。

## `.pext`の作成

`tools\Validate-Build.cmd`が成功し、[docs/SMOKE_TEST.md](docs/SMOKE_TEST.md)の実機検証が完了したあと、**実機で検証した`artifacts\plugin`をそのまま**Playnite Toolboxでパッケージします。スモークテスト成功後からパッケージ作成までの間に、別バイナリへ再ビルドしないでください。

```powershell
.\tools\Package-Release.ps1 -ConfirmRuntimeValidated
```

または:

```cmd
tools\Package-Release.cmd
```

スクリプトはPlaynite公式の`Toolbox.exe pack <extensionfolder> <targetfolder>`を使用し、`artifacts\release`に`.pext`を作成します。あわせて`SHA256SUMS.txt`と`RELEASE-SUMMARY.txt`を生成します。

Toolboxを自動検出できない場合は、`-ToolboxPath`で`Toolbox.exe`を指定してください。
