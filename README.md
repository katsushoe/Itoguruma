# Itoguruma

Claude Code、Codexなどの独立したAIエージェント間で、SQLiteを正本としてメッセージを交換するMCP stdioサーバーです。

## 主な機能

- `register_agent` / `list_agents` / `send_message` / `get_messages` / `ack_message`
- append-onlyのメッセージ本文と、分離した`pending → leased → acked`配送状態
- lease期限切れによるat-least-once再配送
- sender単位の`idempotency_key`による重複送信防止
- WAL、FULL同期、外部キー、5秒のbusy timeout
- thread、reply-to、複数宛先
- Claude Code Hookから利用できる`agentmsg` CLI

## 必要環境

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Claude CodeまたはCodex CLI

以降のコマンドは、リポジトリのルートをカレントディレクトリとしてPowerShellで実行します。

## 1. ビルド

```powershell
dotnet restore tests/Itoguruma.Tests/Itoguruma.Tests.csproj
dotnet build tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-restore
dotnet test tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-build
dotnet publish src/Itoguruma.Server -c Release -r win-x64 --self-contained false -o artifacts/server
dotnet publish src/agentmsg -c Release -r win-x64 --self-contained false -o artifacts/agentmsg
New-Item -ItemType Directory -Force data | Out-Null
```

生成物は`artifacts/server/Itoguruma.Server.exe`と`artifacts/agentmsg/agentmsg.exe`です。Claude CodeとCodexには、同じ`data/messages.db`を指定します。

## 2. Codexへ登録

`<repo>`をこのリポジトリの絶対パスへ置き換えます。

```powershell
codex mcp add itoguruma --env "ITOGURUMA_DB=<repo>\data\messages.db" -- "<repo>\artifacts\server\Itoguruma.Server.exe"
codex mcp list
```

すでに`itoguruma`が登録されている場合は、`codex mcp remove itoguruma`を実行してから再登録します。登録後にCodexを再起動してください。

## 3. Claude Codeへ登録

リポジトリ同梱の[`.mcp.json`](.mcp.json)は、公開済みServerと`data/messages.db`を使用します。Claude Codeをこのリポジトリで起動し、MCP Serverの利用確認が表示されたら承認してください。

Hookを初めて設定する場合は、設定例をコピーします。

```powershell
Copy-Item .claude/settings.example.json .claude/settings.json
```

既存の`.claude/settings.json`がある場合は上書きせず、[設定例](.claude/settings.example.json)の`hooks`を統合してください。設定例はSessionStart、UserPromptSubmit、Stopで`claude-main`のInboxを確認します。設定後にClaude Codeを再起動してください。

## 4. Agent登録

```powershell
artifacts/agentmsg/agentmsg.exe register --db data/messages.db --agent claude-main --type claude-code
artifacts/agentmsg/agentmsg.exe register --db data/messages.db --agent codex-main --type codex
artifacts/agentmsg/agentmsg.exe agents --db data/messages.db
```

各セッションからMCP Toolを使う場合も、最初に`register_agent`を呼び出します。同じAgent IDでの再登録はheartbeat更新として扱われます。

## 5. 往復確認

まずCLIでClaude側からCodex側へ送信します。

```powershell
artifacts/agentmsg/agentmsg.exe send --db data/messages.db --from claude-main --to codex-main --thread setup-check --body "疎通確認" --idempotency-key setup-check-1
artifacts/agentmsg/agentmsg.exe inbox --db data/messages.db --agent codex-main --lease-seconds 300
```

Inbox出力の`messageId`を使ってACKし、逆方向へ返信します。

```powershell
artifacts/agentmsg/agentmsg.exe ack --db data/messages.db --agent codex-main --message <messageId>
artifacts/agentmsg/agentmsg.exe send --db data/messages.db --from codex-main --to claude-main --thread setup-check --body "受信しました" --idempotency-key setup-check-2
artifacts/agentmsg/agentmsg.exe inbox --db data/messages.db --agent claude-main --lease-seconds 300
```

受信処理が完了したメッセージは必ず`ack_message`またはCLIの`ack`でACKします。ACK前に受信側が停止した場合、lease期限後に再配送されます。送信再試行では同じ`idempotency_key`を使用してください。

## 設計と対象範囲

MCP Tool → `MessagingService` → `IMessageStore` → `SqliteMessageStore`の順に分離しています。自動テストでは双方向通信、ACK、lease再配送、プロセス再起動後の永続化、冪等送信、MCP/Hookのプロセス結合を検証します。

Idle状態のAgentを起こすSupervisor、Task/Project管理、Broadcast、検索、Web UI、HTTP HubはMVPの対象外です。
