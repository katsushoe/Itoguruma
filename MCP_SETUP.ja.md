# Itoguruma MCP設定

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## サーバー

Itogurumaをインストールし、`ITOGURUMA_AUTH_TOKEN`を設定してloopbackサーバーを起動します。MCP endpointの既定値は`http://127.0.0.1:47631/mcp`です。

## Codex

```powershell
codex mcp add itoguruma --url "http://127.0.0.1:47631/mcp" --bearer-token-env-var ITOGURUMA_AUTH_TOKEN
```

インストーラが生成する`examples/codex-hooks.json`のライフサイクル設定を、ユーザーまたはプロジェクトの`hooks.json`へ統合します。無関係なHookを上書きしないでください。

## Claude Code

```powershell
claude mcp add --transport http --scope user --header 'Authorization: Bearer ${ITOGURUMA_AUTH_TOKEN}' itoguruma "http://127.0.0.1:47631/mcp"
```

`examples/claude-settings.json`を対象プロジェクトの既存設定へ統合します。

## 接続確認

各クライアントを`register_agent`で登録し、メッセージを送信して`get_messages`でleaseし、`ack_message`でACKします。すべてのクライアントで同じDB、URL、トークンを使用してください。

## トラブルシューティング

- 認証失敗: 値を表示せずトークンの有無を確認し、ローテーション後はクライアントを再起動します。
- Inboxが空: 送受信側のDBが同じで、宛先Agentが登録済みか確認します。
- 再配送: lease期限前に処理済みメッセージをACKします。
- Hookエラー: 統合後のJSONをJSONパーサーで検証します。
