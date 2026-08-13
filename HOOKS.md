# Inbox連携設定

ItogurumaのHookは、Claude CodeのSessionStart、UserPromptSubmit、Stopで`itoguruma hook`を実行し、共有SQLite Inboxの新着をClaude Codeへ通知します。

Codexには同等のライフサイクルHookがないため、Codex側はMCPの`get_messages`またはCLIの`itoguruma inbox`を使います。

## Codex設定

インストーラ版は、Codexがインストール済みであればItoguruma MCPを自動登録します。インストール後にCodexを再起動し、次のコマンドで登録を確認します。

```powershell
codex mcp list
```

一覧に`itoguruma`が表示されれば、Codexから`register_agent`、`get_messages`、`ack_message`、`send_message`などのMCP Toolを使用できます。

CodexにはClaude CodeのSessionStart、UserPromptSubmit、Stopに相当するHookがありません。Inboxを定期的に確認させる場合は、プロジェクトの`AGENTS.md`へ次のような運用ルールを追加します。既存の`AGENTS.md`がある場合は、内容を上書きせず追記してください。

```markdown
## Itoguruma Inbox

- セッション開始時に、Itoguruma MCPの`register_agent`で`codex-main`を登録または更新する。
- 各ターンの開始時に、`get_messages`を`agent_id: codex-main`で呼び出して新着を確認する。
- メッセージの処理が完了した後にだけ、`ack_message`でACKする。
- ACK前に処理できなかったメッセージはACKせず、lease期限後の再配送に任せる。
- `send_message`を再試行するときは、同じ`idempotency_key`を使用する。
```

CLIから手動で確認・ACKする場合は次を実行します。

```powershell
itoguruma register --agent codex-main --type codex
itoguruma inbox --agent codex-main --lease-seconds 300
itoguruma ack --agent codex-main --message <messageId>
```

`AGENTS.md`はCodexへの作業指示であり、外部イベントによる実行中ターンへの割り込みやidle状態からの自動wakeを提供するものではありません。Codexが動作していない間もメッセージはSQLiteに残り、次回のInbox確認時に配送されます。

## Claude Code: インストーラ版

インストーラは、実際のCLIとDBの絶対パスを埋め込んだ設定例を次へ生成します。

```text
%LOCALAPPDATA%\Programs\Itoguruma\examples\claude-settings.json
```

プロジェクトに`.claude/settings.json`がない場合は、Claude Codeを使うプロジェクトのルートで次を実行します。

```powershell
New-Item -ItemType Directory -Force .claude | Out-Null
Copy-Item "$env:LOCALAPPDATA\Programs\Itoguruma\examples\claude-settings.json" .claude\settings.json
```

既存の`.claude/settings.json`がある場合は上書きせず、生成例の`hooks`内にあるSessionStart、UserPromptSubmit、Stopを既存JSONへ統合してください。設定後にClaude Codeを再起動します。

インストール先を変更した場合、設定例も指定したインストール先の`examples\claude-settings.json`に生成されます。

## Claude Code: ソース版

リポジトリの[`.claude/settings.example.json`](.claude/settings.example.json)を`.claude/settings.json`へコピーします。先に次の場所へCLIをpublishしてください。

```powershell
dotnet publish src/Itoguruma.Cli -c Release -r win-x64 --self-contained true -o artifacts/itoguruma
```

## Claude Code: Hookごとの動作

| Hook | 動作 |
| :--- | :--- |
| `SessionStart` | Claude Codeのセッション開始時にInboxを確認し、新着をコンテキストへ追加します。 |
| `UserPromptSubmit` | ユーザーがプロンプトを送信した時点でInboxを確認し、新着をコンテキストへ追加します。 |
| `Stop` | 応答終了時に新着があれば終了コード`2`を返し、標準エラーへ新着を出して処理継続を促します。 |

Hookはメッセージを`leased`にしますが、自動ACKはしません。Claude Codeが処理を完了した後、MCPの`ack_message`または次のCLIでACKします。

```powershell
itoguruma ack --agent claude-main --message <messageId>
```

ACK前にClaude Codeが停止した場合、lease期限後に再配送されます。

## Claude Code: 動作確認

Claude Code用Agentを登録し、テストメッセージを送ります。

```powershell
itoguruma register --agent sender-test --type test
itoguruma register --agent claude-main --type claude-code
itoguruma send --from sender-test --to claude-main --thread hook-check --body "Hook疎通確認" --idempotency-key hook-check-1
```

Claude Codeを起動するか、プロンプトを送信します。コンテキストに`Itoguruma inbox messages:`とメッセージが追加されれば成功です。

CLIだけで設定を確認する場合:

```powershell
'{"hook_event_name":"UserPromptSubmit"}' | itoguruma hook --agent claude-main
```

## トラブルシューティング

- `itoguruma`が見つからない: 新しいターミナルで`Get-Command itoguruma`を確認します。
- 新着が表示されない: Claude Codeと送信側が同じDBを使っているか確認します。
- 同じメッセージが再表示される: 処理後にACKされているか確認します。
- JSONエラーになる: `.claude/settings.json`をJSONパーサーで確認し、既存の`hooks`を上書きしていないか確認します。
