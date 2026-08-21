# ADR 0003: CRファイルを正本とするchange_request配送

## Status

Accepted

## Context

正式な変更依頼（CR）は共有CR領域のMarkdownファイルを正本とします。従来のItogurumaは`change_request`種別を拒否するため、CR固有の入力検証、配送失敗、検索、状態不一致を通常通知と区別できませんでした。

## Decision

- `message_type=change_request`とpayloadスキーマversion 1を追加します。
- payloadは`schema_version`、`cr_path`、`source_project`、`target_project`、`priority`、`status`を必須とします。
- `ITOGURUMA_CR_ROOT`または`Itoguruma:CrRoot`を共有CR領域のルートとし、`cr_path`は`inbox/<target_project>/`直下の既存Markdownファイルに限定します。
- CRファイルを状態の正本とします。DBのpayloadは配送時の索引情報であり、`inspect_change_request`が現在状態との差異を報告します。
- 配送先は呼び出し元が登録済みAgentを明示します。プロジェクト名からAgentを推測せず、宛先不明時や検証失敗時は通常メッセージへフォールバックしません。
- `get_messages`と読み取り専用Viewerは`message_type`で絞り込めます。thread履歴、payload、配送状態、ACK時刻が監査情報です。
- 既存の送信者単位`idempotency_key`制約をCRにも適用します。

## Alternatives

- CR本文をDBへ複製する案は、正本が二重化して状態競合を生むため採用しません。
- `target_project`からAgentを暗黙決定する案は、担当変更や未登録時に誤配送するため採用しません。
- 検証失敗時に`message`へ変換する案は、正式CRの配送失敗を隠すため採用しません。

## Impact

SQLite schema version 3で`messages.message_type`の制約へ`change_request`を追加します。既存の`message`、`notification`、`system`、CLI、MCPの既定値は変更しません。

## Security conditions

パスを絶対パスへ正規化し、許可ルート外、サブディレクトリ、パストラバーサル、非Markdown、存在しないファイル、ディレクトリを拒否します。payload、配置先、CR本文の依頼元・依頼先・優先度・状態を照合します。

## Operational conditions

CR配送を有効にするサーバーは共有CRルートを設定し、送信元と担当Agentを事前登録します。状態変更後はCRファイルを更新し、`inspect_change_request`で配送時状態との差異を確認します。

## Implementation, tests, and user documentation

Coreでpayloadとファイルを検証し、ServerとCLIへ種別フィルターを公開します。正常配送、冪等再送、領域外パス、項目不一致、状態差異、種別検索を自動テストします。利用方法とエラー条件はREADMEおよびCOMMANDSに記載します。
