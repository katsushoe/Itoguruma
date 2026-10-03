# ItogurumaライフサイクルHook

[English](HOOKS.md) | [日本語](HOOKS.ja.md)

Itogurumaは、Codexでは`SessionStart`、`UserPromptSubmit`、`Stop`、Claude Codeでは`UserPromptSubmit`で共有Inboxを確認します。Claude Codeではプロンプト送信ごとの確認で受信を見落とさないため、重複する`SessionStart`と、未ACKメッセージで応答終了を繰り返し止める`Stop`は既定の設定例に含めません。Hookはメッセージをleaseしますが、自動ではACKしません。

## 受信箱の選択

`itoguruma hook`には、受信箱の指定方法を次のどちらか一方だけ渡します。

| オプション | 受信箱 |
| :--- | :--- |
| `--project-inbox` | 作業ディレクトリを含むGitリポジトリ名をInvariant lowercaseへ変換したIDの、有効な登録Projectです。作業ディレクトリはhook入力の`cwd`から読み、ない場合はプロセスのディレクトリを使います。`git worktree`内では主リポジトリ名を使います。リポジトリ外、Project IDとして無効な名前、有効な登録Projectがない場合は何も出力せず終了コード`0`で終了します。 |
| `--agent <inboxAgentId>` | 指定した受信箱です。 |

生成されるClaude Codeの設定例は`--project-inbox --consumer-agent claude-code`を使うため、ユーザー単位の1つのHookで各プロジェクト自身の受信箱を表示します。コンテキストの見出しにはACKに必要な`agent_id`と`consumer_agent_id`を示し、メッセージの`leaseId`と組み合わせてACKします。MCPツール`get_hook_context`も`working_directory`または`agent_id`で同じ選択を受け付けます。

## 生成される設定例

インストーラは、インストール先の配下へ`examples/codex-hooks.json`と`examples/claude-settings.json`を生成します。既存のクライアント設定へ必要なイベント項目だけを統合し、無関係なHookを上書きしないでください。

## クライアントの動作

| イベント | 動作 |
| :--- | :--- |
| `SessionStart` | 新たにleaseしたInboxメッセージをセッションコンテキストへ追加します。 |
| `UserPromptSubmit` | ユーザーがプロンプトを送信した時点でInboxを確認します。 |
| `Stop` | 新着により処理継続が必要な場合、終了コード`2`を返します。 |

Hookはidle中のクライアントへ割り込まず、新しいターンも開始しません。クライアント停止中のメッセージはSQLiteに残ります。処理後は`ack_message`または次のCLIでACKします。

```powershell
itoguruma ack --agent <inboxAgentId> --consumer-agent <consumerAgentId> --message <messageId> --lease-id <leaseId>
```

## 確認

受信Agentを登録してテストメッセージを送り、設定したライフサイクルイベントを発生させます。編集した設定ファイルはJSONパーサーで検証してください。MCP登録と障害確認は[MCP_SETUP.ja.md](MCP_SETUP.ja.md)を参照してください。
