# Inbox連携設定

ItogurumaのHookは、Claude CodeとCodexのSessionStart、UserPromptSubmit、Stopで`itoguruma hook`を実行し、共有SQLite Inboxの新着をエージェントへ通知します。

CodexのHook仕様と設定形式は[OpenAI公式Hooksドキュメント](https://developers.openai.com/codex/hooks)を参照してください。

## Codex設定

インストーラ版は、Codexがインストール済みであればItoguruma MCPを自動登録します。インストール後にCodexを再起動し、次のコマンドでMCP登録を確認します。

```powershell
codex mcp list
```

一覧に`itoguruma`が表示されれば、Codexから`register_agent`、`get_messages`、`ack_message`、`send_message`などのMCP Toolを使用できます。MCP登録とHook設定は別です。

Codexはユーザー設定の`%USERPROFILE%\.codex\hooks.json`、またはプロジェクト設定の`.codex\hooks.json`からHookを読み込みます。インストーラは、実際のCLIとDBの絶対パスを埋め込んだ設定例を次へ生成します。

```text
%LOCALAPPDATA%\Programs\Itoguruma\examples\codex-hooks.json
```

生成されたJSONは次の構造です。

```json
{
  "description": "Check the Itoguruma inbox during the Codex lifecycle.",
  "hooks": {
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "\"<install>\\bin\\itoguruma\\itoguruma.exe\" hook --agent codex-main --db \"<install>\\data\\messages.db\"",
            "timeout": 15
          }
        ]
      }
    ],
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "\"<install>\\bin\\itoguruma\\itoguruma.exe\" hook --agent codex-main --db \"<install>\\data\\messages.db\"",
            "timeout": 15
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "\"<install>\\bin\\itoguruma\\itoguruma.exe\" hook --agent codex-main --db \"<install>\\data\\messages.db\"",
            "timeout": 15
          }
        ]
      }
    ]
  }
}
```

`<install>`は説明用表記です。インストーラが生成するファイルには実際のインストール先が入ります。既存の`hooks.json`がある場合は上書きせず、`hooks`内の各イベントへItogurumaのエントリを追加します。プロジェクト設定を使う場合、Codexは初回または設定変更後にHookの信頼確認を求めます。内容を確認して承認してください。

Codex用Agentを登録し、CLIから手動で確認・ACKする場合は次を実行します。

```powershell
itoguruma register --agent codex-main --type codex
itoguruma inbox --agent codex-main --lease-seconds 300
itoguruma ack --agent codex-main --message <messageId>
```

Codexでは、SessionStartとUserPromptSubmitの標準出力が追加のdeveloper contextとしてモデルへ渡されます。Stopで新着が見つかった場合は、`itoguruma hook`が終了コード`2`と継続理由を返し、Codexに処理継続を促します。

Hookは任意のタイミングで実行中のモデル処理へ割り込む機能ではありません。バックグラウンドHookがidle中に完了しても新しいターンは開始されません。Codexが動作していない間もメッセージはSQLiteに残り、次回のHook実行時に配送されます。

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

## Hookごとの動作

| Hook | 動作 |
| :--- | :--- |
| `SessionStart` | セッション開始時にInboxを確認し、新着をコンテキストへ追加します。 |
| `UserPromptSubmit` | ユーザーがプロンプトを送信した時点でInboxを確認し、新着をコンテキストへ追加します。 |
| `Stop` | 応答終了時に新着があれば終了コード`2`を返し、標準エラーへ新着を出して処理継続を促します。 |

Hookはメッセージを`leased`にしますが、自動ACKはしません。エージェントが処理を完了した後、MCPの`ack_message`または次のCLIでACKします。

```powershell
itoguruma ack --agent <agentId> --message <messageId>
```

ACK前にエージェントが停止した場合、lease期限後に再配送されます。

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
