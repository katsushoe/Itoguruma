# Itogurumaコマンド一覧

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

この文書は、`itoguruma` CLIとMCP Toolのコマンド一覧です。`--db <path>`を省略した場合は、`ITOGURUMA_DB`環境変数、続いてユーザーのLocalApplicationData配下にある既定DBを使用します。

## CLIコマンド

| コマンド | 必須オプション | 主な任意オプション | 内容 |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type` | `--name`, `--session`, `--metadata`, `--db` | Agentを登録またはheartbeat更新します。 |
| `itoguruma agents` | なし | `--db` | 登録済みAgentを一覧表示します。 |
| `itoguruma unregister` | `--agent` | `--db` | Agent登録を削除します。既存メッセージから参照されているAgentは削除できません。 |
| `itoguruma send` | `--from`, 1個以上の`--to`, `--provider`, `--body`, `--thread` | `--reply-to`, `--message-type`, `--payload-json`, `--idempotency-key`, `--db` | メッセージを永続化して配送待ちにします。 |
| `itoguruma inbox` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--db` | 未処理メッセージをleaseして取得します。 |
| `itoguruma ack` | `--agent`, `--message` | `--db` | lease済みメッセージをACKします。 |
| `itoguruma history` | `--thread` | `--limit`, `--offset`, `--db` | 指定Threadのメッセージ履歴を作成日時の昇順で返します。 |
| `itoguruma inspect-change-request` | `--payload-json` | `--db` | CRファイルを再検証し、記録された状態との差異を返します。 |
| `itoguruma hook` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--db` | Claude Code／Codex Hook入力を読み、InboxをHook出力へ追加します。 |
| `itoguruma auth status` | なし | なし | 値を表示せず、ユーザー認証トークンの設定有無を表示します。 |
| `itoguruma auth rotate` | なし | なし | 明示確認後に32バイトの暗号学的乱数でユーザー認証トークンをローテーションします。 |
| `itoguruma project add <project-id>` | `--inbox-agent`、対話確認 | `--display-name`、`--db` | 有効な既知プロジェクトを追加します。 |
| `itoguruma project update <project-id>` | 対話確認 | `--inbox-agent`、`--display-name`、`--db` | 既知プロジェクトを更新します。 |
| `itoguruma project enable|disable|delete <project-id>` | 対話確認 | `--db` | 有効化、無効化、未参照プロジェクトの削除を行います。 |
| `itoguruma project list|show [project-id]` | `show`のみプロジェクトID | `--db` | 正本のプロジェクト一覧または詳細を参照します。 |
| `itoguruma version` | なし | なし | 製品バージョンを`x.x.x`または`x.x.x.x`形式で表示します。 |
| `itoguruma --help` | なし | なし | CLI概要を表示します。 |

## CLI例

```powershell
itoguruma register --agent claude-main --type claude-code
itoguruma register --agent codex-main --type codex
itoguruma version
itoguruma agents
itoguruma unregister --agent claude-main
itoguruma send --from claude-main --to codex-main --provider claude-code --thread setup --body "確認してください" --idempotency-key setup-1
itoguruma inbox --agent codex-main --lease-seconds 300
itoguruma ack --agent codex-main --message <messageId>
itoguruma auth status
itoguruma auth rotate
```

`send`を再試行するときは、同じ論理送信に同じ`--idempotency-key`を渡してください。`inbox`で取得した処理済みメッセージは、必ず`ack`してください。

`auth rotate`は影響範囲を表示し、`ROTATE`の入力後に実行します。トークン値は標準出力やエラー出力へ表示しません。実行後は`ItogurumaServer`スケジュールタスクを再起動し、新しいターミナルを開いてCodexとClaude Codeを再起動してください。Claude CodeやHataoriなどBearerトークンを設定へ直接保持するクライアントは、新しいユーザー環境変数を参照するよう再設定が必要です。

## MCP Tool

| Tool | 必須入力 | 主な任意入力 | 内容 |
| :--- | :--- | :--- | :--- |
| `get_version` | なし | なし | 実行中のItoguruma MCP Serverバージョンを返します。 |
| `register_agent` | `agent_id`, `agent_type` | `name`, `session_id`, `metadata_json` | Agentを登録またはheartbeat更新します。 |
| `list_agents` | なし | なし | 登録済みAgentを一覧表示します。 |
| `unregister_agent` | `agent_id` | なし | Agent登録を削除します。既存メッセージから参照されているAgentは削除できません。 |
| `send_message` | `sender_agent_id`, `provider`, `body`, `thread_id`と宛先 | `reply_to_message_id`, `message_type`, `payload_json`, `idempotency_key` | 1件以上の宛先へ送信します。 |
| `get_messages` | `agent_id` | `limit`, `lease_seconds`, `thread_id`, `message_type` | 配送可能なメッセージをleaseして取得します。 |
| `ack_message` | `agent_id`, `message_id` | なし | lease済み配送をACKします。 |
| `get_conversation_history` | `thread_id` | `limit`, `offset` | 指定Threadの既読・過去分を含む全メッセージ履歴を、作成日時の昇順で返します。該当Threadが存在しない場合は空配列を返します。 |
| `inspect_change_request` | `payload_json` | なし | CRファイルを再検証し、payloadに記録された状態との不一致を返します。 |
| `get_hook_context` | `agent_id` | `hook_event_name`, `limit`, `lease_seconds`, `thread_id`, `message_type` | メッセージをleaseし、CLI Hook互換のコンテキストと停止状態を返します。 |
| `get_auth_status` | なし | なし | 値を表示せず、ユーザー認証トークンの設定有無を返します。 |
| `rotate_auth_token` | `confirmation=ROTATE` | なし | トークン値を返さずに更新します。実行後はサーバーとクライアントの再起動が必要です。 |

`get_version`は、稼働中サーバーの名前と`x.x.x`または`x.x.x.x`形式の製品バージョンを返します。

`provider`／`--provider`は送信ごとに必須で、`codex`、`claude-code`などの送信元実行環境を表します。小文字へ正規化され、ASCII英数字とハイフンだけを許可します。Itogurumaは指定値をメッセージへ保存し、Inbox、lease再配送、Hook、履歴、Viewerで同じ値を返します。認証済みクライアントが申告する配送メタデータであり、本人確認には使用しません。schema version 3以前から移行した既存メッセージは、過去値を推測せず`provider=unknown`として返します。

`send_message`の宛先は、単一宛先なら`recipient`、複数宛先なら`recipients`を使います。`message_type`は`message`、`notification`、`system`、`change_request`のいずれかです。CRは通常メッセージへフォールバックせず、登録済み担当Agentを明示的な宛先に指定します。

未登録Agentの宛先が有効な`project_id`と一致する場合、`send`／`send_message`はトランザクション内で`project_inbox` Agentを作成し、設定済み受信箱へ配送します。未知プロジェクトは`ITG_PROJECT_UNKNOWN`、無効プロジェクトは`ITG_PROJECT_DISABLED`を返します。プロジェクト変更はMCPから実行できず、実コンソールで5桁コードを60秒以内、最大3回で再入力する必要があります。入出力リダイレクトや回避オプションは認めません。参照済みプロジェクトの削除は`ITG_PROJECT_REFERENCED`となるため、`disable`を使用します。

## インストーラオプション

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 [-Version <version>] [-InstallDirectory <path>] [-NoPath] [-SkipCodex] [-SkipClaude]
```

| オプション | 内容 |
| :--- | :--- |
| `-Version` | `latest`または取得するReleaseバージョンを指定します。既定は`latest`です。 |
| `-InstallDirectory` | インストール先を指定します。既定は`C:\Itoguruma`です。 |
| `-ServerUrl` | 常駐MCPサーバーの待受URLを指定します。既定は`http://127.0.0.1:47631`です。 |
| `-NoPath` | `itoguruma`、`stop-codex`、`stop-claude`のユーザーPATH登録を省略します。 |
| `-SkipCodex` | CodexへのMCP登録を省略します。 |
| `-SkipClaude` | Claude CodeへのユーザースコープMCP登録を省略します。 |

サーバーは`ITOGURUMA_URL`、`ITOGURUMA_DB`、`ITOGURUMA_AUTH_TOKEN`、`ITOGURUMA_CR_ROOT`を使用します。認証トークンは必須です。CR配送を使う場合は`ITOGURUMA_CR_ROOT`に共有CR領域のルートを設定します。

`-PackagePath`はローカルのバイナリZIPを指定する検証・オフライン導入用オプションです。
