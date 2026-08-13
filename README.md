# Itoguruma

Claude Code、Codexなどの独立したAIエージェント間で、SQLiteを正本としてメッセージを交換するMCP stdioサーバーです。

## 配布物

GitHub Releasesでは、利用目的ごとに配布物を分けます。

| 配布物 | 対象 | .NET 8 SDK |
| :--- | :--- | :---: |
| `Install-Itoguruma.ps1` | 通常利用者向けインストーラ | 不要 |
| `Itoguruma-x.x.x-win-x64.zip` | 手動配置・オフライン利用向けself-containedバイナリ | 不要 |
| `Source code (zip/tar.gz)` | 開発者向けソース配布 | 必要 |

コマンドの詳細は[COMMANDS.md](COMMANDS.md)、Claude Code Hookの導入手順は[HOOKS.md](HOOKS.md)を参照してください。

## インストーラ版

GitHub Releasesから`Install-Itoguruma.ps1`をダウンロードし、PowerShellで実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1
```

インストーラは次の処理を行います。

- 最新のWindows x64バイナリZIPを取得
- `%LOCALAPPDATA%\Programs\Itoguruma`へ配置
- `%LOCALAPPDATA%\Programs\Itoguruma\data\messages.db`を共有DBとして準備
- `itoguruma`をユーザーPATHへ登録
- インストール済みのCodexとClaude Codeへ`itoguruma` MCPを登録

完了後、新しいターミナルを開き、CodexとClaude Codeを再起動してください。既存のDBは上書き・削除しません。インストーラのオプションは[コマンド一覧](COMMANDS.md#インストーラオプション)を参照してください。

## バイナリZIP版

`Itoguruma-x.x.x-win-x64.zip`を任意のディレクトリへ展開します。ZIPは.NETランタイムを同梱するため、.NET 8 SDKは不要です。

```text
bin/
├─ server/Itoguruma.Server.exe
└─ itoguruma/itoguruma.exe
examples/claude-settings.json
README.md
COMMANDS.md
```

手動登録では、`<install>`を展開先の絶対パスに置き換えます。

```powershell
codex mcp add itoguruma --env "ITOGURUMA_DB=<install>\data\messages.db" -- "<install>\bin\server\Itoguruma.Server.exe"
claude mcp add --scope user --env "ITOGURUMA_DB=<install>\data\messages.db" itoguruma -- "<install>\bin\server\Itoguruma.Server.exe"
```

## ソース版

ソースからビルドする開発者だけが.NET 8 SDKを必要とします。

```powershell
dotnet restore tests/Itoguruma.Tests/Itoguruma.Tests.csproj
dotnet build tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-restore
dotnet test tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-build
```

配布物をローカル生成する場合:

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.0
```

## 初期設定と往復確認

```powershell
itoguruma register --agent claude-main --type claude-code
itoguruma register --agent codex-main --type codex
itoguruma send --from claude-main --to codex-main --thread setup-check --body "疎通確認" --idempotency-key setup-check-1
itoguruma inbox --agent codex-main --lease-seconds 300
itoguruma ack --agent codex-main --message <messageId>
```

各セッションからMCP Toolを使う場合も、最初に`register_agent`を呼び出します。受信処理が完了したメッセージは必ずACKしてください。ACK前に受信側が停止した場合、lease期限後に再配送されます。送信再試行では同じ`idempotency_key`を使用してください。

Claude Code Hookを使う場合は、インストーラが生成する`examples/claude-settings.json`を既存設定へ統合します。設定場所、Hookごとの動作、ACK、疎通確認は[Hook設定ガイド](HOOKS.md)を参照してください。

## 主な機能

- append-onlyのメッセージ本文と、分離した`pending → leased → acked`配送状態
- lease期限切れによるat-least-once再配送
- sender単位の`idempotency_key`による重複送信防止
- WAL、FULL同期、外部キー、5秒のbusy timeout
- thread、reply-to、複数宛先
- Claude Code Hookから利用できる`itoguruma` CLI

MCP Tool → `MessagingService` → `IMessageStore` → `SqliteMessageStore`の順に分離しています。Idle状態のAgentを起こすSupervisor、Task/Project管理、Broadcast、検索、Web UI、HTTP HubはMVPの対象外です。
