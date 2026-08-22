# ADR 0004: Providerを送信要求の必須メタデータとして保存する

## Status

Accepted

## Context

CodexとClaude Codeは同時に複数タスクを実行し、Providerをまたぐ配送と同一Provider内の配送の両方を行います。プロジェクトIDにProviderを1件だけ登録する方式では、後から登録したProviderが上書きされ、実際の送信元と一致しません。

Providerはローカル環境でHataoriが配送・起動判断に使うメタデータであり、セキュリティ主体の証明には使用しません。Provider別またはタスク別トークンによる厳密な本人確認は、この用途には複雑すぎます。

## Decision

- MCP `send_message`の`provider`とCLI `send --provider`を必須入力にします。
- `provider`はASCII英数字とハイフンへ制限し、小文字へ正規化してメッセージに保存します。
- Agent登録の`agent_type`からProviderを推測・取得しません。Providerの同時利用でAgent登録が競合しません。
- Inbox、lease再配送、Hook、Thread履歴、Viewerは保存済み`provider`を返します。
- DB schema version 4の`messages.provider`を継続使用します。schema version 3以前の既存メッセージは、推測せず`unknown`として移行します。

## Alternatives

- Agent登録の`agent_type`から自動判定する案は、同じプロジェクトで複数Providerが同時稼働すると上書き競合するため不採用とします。
- Provider別・タスク別トークンで認証主体から導出する案は、ローカル配送メタデータの用途に対して複雑すぎるため不採用とします。
- HTTP接続元やUser-Agentから推測する案は、Providerを保証できないため不採用とします。

## Impact

メッセージDTO、MCP Tool schema、CLI入力、SQLite保存、Hook、Viewer、テスト、利用者文書が対象です。従来の`send_message`と`itoguruma send`呼び出しは`provider`を追加する必要があります。

## Security conditions

`provider`は認証済みクライアントによる自己申告値であり、本人確認・認可判断には使用しません。Bearerトークン、Agent ID、Providerの暗号学的な結び付けは保証しません。

## Operational conditions

Codexは`provider=codex`、Claude Codeは`provider=claude-code`を各送信要求へ指定します。新しいProviderは同じ書式規則で指定できます。

## Implementation and verification

Coreの送信・取得経路を共通実装とし、MCPとCLIへ同じ必須条件を適用します。必須schema、正規化、永続化、lease再配送、履歴、既存DB移行、無効値拒否を自動テストします。
