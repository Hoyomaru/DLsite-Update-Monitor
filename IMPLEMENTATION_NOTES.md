# 実装上の設計メモ（互換インデックス）

このファイルは、過去のリンクや参照を壊さないために残している**互換インデックス**です。

以前このファイルに記載していた安全設計の本文は、重複を減らすため次の正本へ整理しました。

- [DEVELOPMENT.md](DEVELOPMENT.md) — 内部仕様、安全不変条件、永続化、HTTP、状態遷移、開発ルール
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — コンポーネント関係、データフロー、設計判断
- [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md) — Playnite実機検証
- [RELEASE_STATUS.md](RELEASE_STATUS.md) — v0.1.0固有の検証証跡

今後はこのファイルを仕様の正本として更新せず、`DEVELOPMENT.md` と必要な `docs/*` を更新してください。

## 安全設計の要点

詳細は [DEVELOPMENT.md](DEVELOPMENT.md) の「絶対に壊してはいけない不変条件」を参照してください。

- 初回正常取得は更新扱いにせずBaselineを作る
- 比較基準は`AcknowledgedSnapshot`で、直近取得結果へ自動で進めない
- HTTP/解析失敗で既存のPending状態や確認済みSnapshotを消さない
- `Parsed → Missing`や解析不能を安易にChangedへ変換しない
- 別ProductIdへ古いBaselineを流用しない
- 初回を含めrequested/resolved ProductIdを照合する
- `tracking.tmp`を再読込・検証してからprimaryへ昇格する
- 未対応の新しいTracking Schemaを古いPluginで上書きしない
- checkと状態変更を同時実行しない
- Plugin管理外のPlaynite tagを削除しない
- Playnite本体提供runtime DLLを重複同梱しない
- DLsite HTTPを直列化し、request間隔を維持する

## 旧本文について

統合前の詳細本文が必要な場合は、Git履歴のこの整理以前のVersionを参照してください。現在の仕様判断には、必ず最新の `DEVELOPMENT.md`、`docs/ARCHITECTURE.md`、コード、テストを使用してください。
