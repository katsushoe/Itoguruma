# Itoguruma

Claude Code、Codexなどの独立したAIエージェント間で、SQLiteを正本としてメッセージを交換するMCP stdioサーバーです。

## 機能

- `register_agent` / `list_agents` / `send_message` / `get_messages` / `ack_message`
- WAL + FULL同期、外部キー、5秒のbusy timeout
- append-onlyのメッセージ本文と、分離した配送状態
- lease期限切れによるat-least-once再配送
- thread、reply-to、複数宛先
- sender単位の`idempotency_key`による重複送信防止
- Hookから利用できる`agentmsg hook --agent <id>` CLI

## MVP受入条件

自動テストで、双方向の往復とACK、lease期限切れ再配送、Store再生成後の未処理保持、同一冪等キーの並行送信を検証します。実際のClaude Code/Codexセッションを使う最終確認は、両クライアントへMCP設定を導入した環境で行います。

## ビルドとテスト

```powershell
dotnet restore Itoguruma.slnx
dotnet build Itoguruma.slnx --no-restore
dotnet test Itoguruma.slnx --no-build
dotnet publish src/Itoguruma.Server -c Release -r win-x64 --self-contained false -o artifacts/server
dotnet publish src/agentmsg -c Release -r win-x64 --self-contained false -o artifacts/agentmsg
```

## MCP設定

`ITOGURUMA_DB`にはClaude/Codex双方から見える同じ絶対パスを設定します。このリポジトリにはClaude Code用`.mcp.json`とHook用`.claude/settings.json`が含まれます。Codexは`codex mcp add itoguruma --env ITOGURUMA_DB=<db> -- <server.exe>`でユーザー設定へ登録します。公開後に各クライアントをこのリポジトリで再起動してください。

```json
{
  "mcpServers": {
    "itoguruma": {
      "command": "dotnet",
      "args": ["run", "--project", "<repo>/src/Itoguruma.Server"],
      "env": { "ITOGURUMA_DB": "<shared-path>/messages.db" }
    }
  }
}
```

CLI例:

```powershell
dotnet run --project src/agentmsg -- register --agent codex-main --type codex
dotnet run --project src/agentmsg -- hook --agent codex-main
dotnet run --project src/agentmsg -- send --from claude-main --to codex-main --thread auth --body "確認してください" --idempotency-key request-123
```

`hook`は未処理メッセージをleaseしてJSONで標準出力します。受信処理の完了後に`ack --agent <id> --message <message_id>`を実行してください。送信側は論理的な送信要求ごとに安定した`idempotency_key`を生成し、再試行時に同じ値を渡します。

Claude Code向けのSessionStart/UserPromptSubmit/Stop設定例は[`.claude/settings.example.json`](.claude/settings.example.json)です。SessionStart/UserPromptSubmitでは新着をコンテキストへ追加し、Stop時に新着を検出した場合は終了を止めて処理を継続させます。利用前にビルドし、例を`.claude/settings.json`へ統合してください。Codex側は現行の公式設定で同等のライフサイクルHookを確認できないため、MCP Toolまたは明示的な`agentmsg hook`呼び出しを使います。

MCP Tool → `MessagingService` → `IMessageStore` → `SqliteMessageStore`の順に分離しています。Idle状態のエージェントを起こすSupervisor機能、Task/Project管理、HTTP HubはMVPの対象外です。
