# DLsite Update Monitor v1.1.0

DLsite Update Monitor v1.1.0 は、**追跡状態の安全性・診断性・状態の見やすさを大幅に強化した安定化リリース**です。

Playniteで管理しているDLsite作品について、DLsite商品ページの「更新情報」と「ファイル容量」の変化を監視する基本機能はそのままに、保存失敗・破損データ・リンク異常・ProductId変更などの境界条件で既存状態を壊しにくくしました。

> **Note**
> 本ツールはDLsiteおよびPlayniteの公式機能・公式サポートではない非公式ツールです。

## 主な変更点

### 安全性・永続化

- tracking状態変更を作業用cloneへ適用し、保存成功後だけlive状態へ反映
- バッチ途中の保存成功地点をdurable checkpointとして扱い、後続save失敗時の未保存状態混入を防止
- primary tracking file破損時に正常backupから復旧した後も、その正常backupを維持
- null tracking recordなど意味的に不正なJSONを検出し、backup fallbackを可能に
- tracking JSON deserializeに`MaxDepth`を設定
- 保存済みPlugin設定の範囲外数値を安全な既定値へ補正

### DLsite URL / ProductId保護

- `http://` のDLsite URLをHTTPSへ正規化
- HTTP responseの最終URLがHTTPSかつDLsite hostであることを要求
- 非HTTP(S) URLや信頼できないredirectを拒否
- resolved URLがある場合、source URLのProductIdへ暗黙fallbackしないよう変更
- ProductIdが別作品へ変わった際に旧Baselineを自動流用しない保護を強化
- 予期しないHTTP 4xxを非retry client errorとして扱い、不要な再試行を抑制

### 診断・再確認

- `エラー/要確認のゲームを再確認` メニューを追加
- DLsiteリンク診断で、問題ゲーム名・理由・曖昧な候補ProductIdを表示
- 同一ProductIdのバッチ処理でGame固有のLocalRecord failureを他Gameへ波及させないよう改善
- バッチ重複管理から取得HTML全文を外し、軽量な再利用メタデータのみ保持

### 監視状態の見える化

- ゲーム右クリックメニューに `監視詳細を表示` を追加
- 1ゲームごとに次を確認可能
  - MonitoringState / LastCheckHealth
  - ProductId
  - 初回・最終試行・最終成功日時
  - AcknowledgedSnapshot / CurrentSnapshot / LastObservation
  - 最新エラー
  - 直近10件の履歴

### タグ・変更状態

- Pluginが管理するタグを次の2名称だけに限定
  - `[DLsite更新] 更新あり`
  - `[DLsite更新] 配布物変更`
- `[DLsite更新]` prefixを使ったユーザー独自タグを誤削除しないよう修正
- 更新情報と配布物の両方が変化した場合、2タグを両方付与
- summaryでも両方変更を各カテゴリへ計上し、`両方変更`件数を表示

### メンテナンス

- `孤立した追跡データを整理` を追加
- Playniteから削除済みゲームに対応するtracking recordをPreview・確認後に削除可能
- 現存Gameの追跡情報やタグには触れない方式

### CI / 開発基盤

- GitHub Actionsを導入
- Core自動テスト: **72 passed / 0 failed / 0 skipped**
- Static validation: **PASS**
- Windows `.NET Framework 4.6.2` Plugin build: **PASS**
- payload境界検証: **PASS**
- `global.json`で.NET 8系SDKを固定

## 実機検証

Playnite **10.56** で段階的なSmoke Test Gate A〜Gを実施し、すべてPASSしています。

確認内容には次を含みます。

- Plugin読み込み / メニュー / Settings
- DLsiteリンク診断
- 初回Baseline作成と2回目同一状態チェック
- Tracking details
- LinkError時の既存Snapshot保持
- エラー対象だけの再確認
- HTTP→HTTPS正規化 / 非HTTP(S)拒否
- ProductId変更保護
- Plugin所有タグとユーザー独自タグの分離
- acknowledge / ignore / reset
- 孤立tracking record整理
- 小規模バッチ / restart / tag toggle / 全ライブラリ確認

さらにVersion `1.1.0` の最終CI Artifactで短縮Smoke Testを再実施し、Plugin読み込み・Settings・既知作品チェック・Tracking details・restart/state reloadを確認済みです。

最終 `.pext` パッケージのインストール試験も **PASS** しています。

## 配布ファイル

```text
DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e_1_1_0.pext
```

SHA-256:

```text
9128b3ca11472b322d5307e6820b7fae902ca13d284b02645b87b9b3aace15de
```

同じReleaseに添付する `SHA256SUMS.txt` でも確認できます。

最終配布`.pext`は、Version `1.1.0` の最終CI Artifactと**バイト列が同一**です。CI Artifact ZIPを再ビルド・再圧縮せず、そのまま`.pext`として使用しているため、実機で最終確認したbinaryと配布binaryが一致します。

## 動作環境

- Windows
- Playnite 10.56（実機検証基準）
- Plugin target: .NET Framework 4.6.2
- PlayniteSDK 6.16.0
- Tracking Schema: `1`

`Playnite.SDK.dll`、`AngleSharp.dll`、`Newtonsoft.Json.dll`はPlaynite本体側のruntimeを利用し、Pluginへ重複同梱しません。

## v1.1.0で引き続き行わないこと

- 自動ダウンロード
- 自動パッチ適用
- Playnite起動時の自動チェック
- 定期自動チェック
- ローカルEXEのVersion解析
- ローカルファイルのhash比較
- DLsite以外のストア監視
- DLsiteアカウントへのログインや認証情報の保存

本Pluginが判定するのは、ローカルゲーム本体の厳密なVersionではなく、**DLsite商品ページ上の配布状態の変化**です。
