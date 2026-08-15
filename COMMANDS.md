# Itogurumaコマンド一覧

この文書は、`itoguruma` CLIとMCP Toolのコマンド一覧です。`--db <path>`を省略した場合は、`ITOGURUMA_DB`環境変数、続いてユーザーのLocalApplicationData配下にある既定DBを使用します。

## CLIコマンド

| コマンド | 必須オプション | 主な任意オプション | 内容 |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type` | `--name`, `--session`, `--metadata`, `--db` | Agentを登録またはheartbeat更新します。 |
| `itoguruma agents` | なし | `--db` | 登録済みAgentを一覧表示します。 |
| `itoguruma send` | `--from`, `--to`, `--body`, `--thread` | `--reply-to`, `--idempotency-key`, `--db` | メッセージを永続化して配送待ちにします。 |
| `itoguruma inbox` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--db` | 未処理メッセージをleaseして取得します。 |
| `itoguruma ack` | `--agent`, `--message` | `--db` | lease済みメッセージをACKします。 |
| `itoguruma hook` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--db` | Claude Code／Codex Hook入力を読み、InboxをHook出力へ追加します。 |
| `itoguruma version` | なし | なし | 製品バージョンを表示します。 |
| `itoguruma --help` | なし | なし | CLI概要を表示します。 |

## CLI例

```powershell
itoguruma register --agent claude-main --type claude-code
itoguruma register --agent codex-main --type codex
itoguruma version
itoguruma agents
itoguruma send --from claude-main --to codex-main --thread setup --body "確認してください" --idempotency-key setup-1
itoguruma inbox --agent codex-main --lease-seconds 300
itoguruma ack --agent codex-main --message <messageId>
```

`send`を再試行するときは、同じ論理送信に同じ`--idempotency-key`を渡してください。`inbox`で取得した処理済みメッセージは、必ず`ack`してください。

## MCP Tool

| Tool | 必須入力 | 主な任意入力 | 内容 |
| :--- | :--- | :--- | :--- |
| `register_agent` | `agent_id`, `agent_type` | `name`, `session_id`, `metadata_json` | Agentを登録またはheartbeat更新します。 |
| `list_agents` | なし | なし | 登録済みAgentを一覧表示します。 |
| `send_message` | `sender_agent_id`, `body`, `thread_id`と宛先 | `reply_to_message_id`, `message_type`, `payload_json`, `idempotency_key` | 1件以上の宛先へ送信します。 |
| `get_messages` | `agent_id` | `limit`, `lease_seconds`, `thread_id` | 配送可能なメッセージをleaseして取得します。 |
| `ack_message` | `agent_id`, `message_id` | なし | lease済み配送をACKします。 |

`send_message`の宛先は、単一宛先なら`recipient`、複数宛先なら`recipients`を使います。`message_type`は`message`、`notification`、`system`のいずれかです。

## インストーラオプション

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 [-Version <version>] [-InstallDirectory <path>] [-NoPath] [-SkipCodex] [-SkipClaude]
```

| オプション | 内容 |
| :--- | :--- |
| `-Version` | `latest`または取得するReleaseバージョンを指定します。既定は`latest`です。 |
| `-InstallDirectory` | インストール先を指定します。既定は`%LOCALAPPDATA%\Programs\Itoguruma`です。 |
| `-NoPath` | `itoguruma`、`stop-codex`、`stop-claude`のユーザーPATH登録を省略します。 |
| `-SkipCodex` | CodexへのMCP登録を省略します。 |
| `-SkipClaude` | Claude CodeへのユーザースコープMCP登録を省略します。 |

`-PackagePath`はローカルのバイナリZIPを指定する検証・オフライン導入用オプションです。
