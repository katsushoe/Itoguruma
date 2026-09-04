# ItogurumaライフサイクルHook

[English](HOOKS.md) | [日本語](HOOKS.ja.md)

Itogurumaは、CodexとClaude Codeの`SessionStart`、`UserPromptSubmit`、`Stop`で共有Inboxを確認します。Hookはメッセージをleaseしますが、自動ではACKしません。

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
