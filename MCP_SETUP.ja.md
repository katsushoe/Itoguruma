# Itoguruma MCP設定

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## サーバー

Itogurumaをインストールしてloopbackサーバーを起動します。インストーラーはBearerトークンをWindows資格情報マネージャーへ保存し、ローカルstdioプロキシを登録します。Streamable HTTP endpointは`http://127.0.0.1:47631/mcp`のままです。

## Codex

```powershell
codex mcp add itoguruma -- "C:\Itoguruma\bin\mcp-proxy\<version>\Itoguruma.McpProxy.exe" --url "http://127.0.0.1:47631/mcp"
```

インストーラが生成する`examples/codex-hooks.json`のライフサイクル設定を、ユーザーまたはプロジェクトの`hooks.json`へ統合します。無関係なHookを上書きしないでください。

## Claude Code

```powershell
claude mcp add --transport stdio --scope user itoguruma -- "C:\Itoguruma\bin\mcp-proxy\<version>\Itoguruma.McpProxy.exe" --url "http://127.0.0.1:47631/mcp"
```

`examples/claude-settings.json`を対象プロジェクトの既存設定へ統合します。

## 接続確認

各クライアントを`register_agent`で登録し、Inboxと取得AgentのIDを指定して`get_messages`でleaseし、返された`lease_id`を指定して`ack_message`でACKします。すべてのクライアントで同じDB、URL、トークンを使用してください。

## トラブルシューティング

- 認証失敗: 値を表示せずトークンの有無を確認し、ローテーション後はクライアントを再起動します。古いクライアントは`itoguruma auth status`の世代IDとサーバーログの`AuthResult`（`expectedGeneration`、`requestGeneration`、`generationMatches`、`correlationId`）を照合します。世代IDは診断専用です。
- Inboxが空: 送受信側のDBが同じで、宛先Agentが登録済みか確認します。
- 再配送: lease期限前に処理済みメッセージをACKします。
- Hookエラー: 統合後のJSON、実行ファイルパス、登録済みProject Inboxを確認し、CLIから再現します。手順は[HOOKS.ja.md](HOOKS.ja.md#障害時の確認と復旧)を参照してください。
