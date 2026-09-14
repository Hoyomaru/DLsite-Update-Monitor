# DLsite Update Monitor v1.0.0

DLsite Update Monitor の**初回正式リリース**です。

Playniteで管理しているDLsite作品について、DLsite商品ページの`更新情報`と`ファイル容量`を監視し、前回ユーザーが確認済みとした状態から変化があった作品を確認しやすくします。

> [!IMPORTANT]
> 本ツールはDLsiteおよびPlayniteの公式機能・公式サポートではない非公式ツールです。

## 公開情報

2026-09-14にGitHub Releaseとして正式公開しました。

```text
Release title: DLsite Update Monitor v1.0.0
Tag: v1.0.0
Tagged commit: a8aa212fbcaaded3411db74643c9345584e30e50
Package: DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA-256: 676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

Release Assetsには次の2ファイルを公開しています。

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
SHA256SUMS.txt
```

Release: https://github.com/Hoyomaru/DLsite-Update-Monitor/releases/tag/v1.0.0

## 主な機能

- Playnite `Game.Links` からDLsite作品IDを解決
- 全ゲーム / 選択ゲームの手動更新チェック
- DLsiteリンク診断
- 初回チェック時のBaseline作成
- `更新情報`の変更検出
- `ファイル容量`の変更検出
- 更新状態のPlayniteタグ反映
- `適用済み` / `無視` / `監視状態をリセット`
- 同一作品への重複通信を抑えるメモリキャッシュ
- rate limit / timeout / 一時通信エラーへの限定的なretry
- `tracking.json`のbackup・一時ファイル・破損保全
- 別作品へのlink変更やredirectから既存Baselineを保護

## 安全性を優先した動作

DLsiteページの取得や解析に問題がある場合、既知の正常状態を新しい値で上書きしません。

- 初回正常取得は「更新あり」にせずBaselineを作成
- HTTP/解析失敗で既存のPending状態を消さない
- 安全に比較できない値は変更として確定しない
- 別ProductIdへ旧Baselineを流用しない
- Plugin管理外のPlayniteタグを削除しない
- 未対応の新しいTracking Schemaを古いVersionで上書きしない

## 動作環境

- Windows
- Playnite 10.56（実機検証基準）
- .NET Framework 4.6.2
- PlayniteSDK 6.16.0

## インストール

Release Assetsから次の`.pext`をダウンロードし、Playniteへインストールしてください。

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_0_0.pext
```

公開AssetのSHA-256:

```text
676dc558e4c66265bab27a2d28a01cd53e541692a1627ab8661b13d4aa0cf9e0
```

同じReleaseに添付されている`SHA256SUMS.txt`も確認用に利用できます。

## v1.0.0で行わないこと

- 自動ダウンロード
- 自動パッチ適用
- Playnite起動時の自動チェック
- 定期自動チェック
- ローカルEXEのVersion解析
- ローカルファイルのhash比較
- DLsite以外のストア監視
- DLsiteアカウントへのログインや認証情報の保存

本Pluginが判定するのは、ローカルゲーム本体の厳密なVersionではなく、**DLsite商品ページ上の配布状態の変化**です。

## v0.1.0からの位置付け

v0.1.0は正式公開前の検証基準として、Core自動テスト63ケース、Playnite実機Smoke Test、DLsiteリンク登録済み33作品の確認、`.pext`インストール試験まで実施していました。

v1.0.0はその現行実装を初回正式公開版として位置付け、Plugin Versionと公開ドキュメントを1.0.0へ揃えたReleaseです。Tracking Schemaは引き続き`1`です。

v1.0.0のGitHub Release・Tag・公開Assetは公開後に確認済みです。一方、Version metadata変更後のv1.0.0について、`Validate-Build.ps1`、Smoke Gate A〜F、`.pext` install testを再実施した結果は、現時点のリポジトリ内に独立した検証証跡として追加されていません。公開済みという事実だけから、それらを推測でPASS扱いしません。

過去の検証記録は`RELEASE_STATUS.md`、開発・安全設計は`DEVELOPMENT.md`、リリース工程は`docs/RELEASE.md`を参照してください。
