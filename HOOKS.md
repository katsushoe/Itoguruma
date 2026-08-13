# Claude Code Hook設定

ItogurumaのHookは、Claude CodeのSessionStart、UserPromptSubmit、Stopで`itoguruma hook`を実行し、共有SQLite Inboxの新着をClaude Codeへ通知します。

Codexには同等のライフサイクルHookがないため、Codex側はMCPの`get_messages`またはCLIの`itoguruma inbox`を使います。

## インストーラ版

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

## ソース版

リポジトリの[`.claude/settings.example.json`](.claude/settings.example.json)を`.claude/settings.json`へコピーします。先に次の場所へCLIをpublishしてください。

```powershell
dotnet publish src/Itoguruma.Cli -c Release -r win-x64 --self-contained true -o artifacts/itoguruma
```

## Hookごとの動作

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

## 動作確認

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
