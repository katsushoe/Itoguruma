# ADR 0001: Streamable HTTP常駐サーバー

## Status

Accepted

## Context

stdio方式ではMCPクライアントが接続ごとにサーバープロセスを生成し、異常切断時にプロセスが残留した。Itogurumaは複数クライアントが同じSQLite正本を利用するため、接続単位のプロセス所有と相性が悪い。

## Decision

公式MCP C# SDKのステートレスStreamable HTTPを採用し、`/mcp`を単一常駐プロセスから公開する。待受は既定で`127.0.0.1`とし、URL単位およびDB単位の`Local\`名前付きMutexで多重起動を防止する。SQLiteが状態の正本であり、MCPセッション固有状態は保持しない。

## Alternatives

- stdioを継続して終了監視を強化する案：クライアントごとのプロセス生成自体が残り、障害経路を減らせないため不採用。
- 旧HTTP+SSE：MCP仕様でStreamable HTTPへ置き換えられているため不採用。

## Impact

CodexとClaude Codeの登録をHTTP URLへ変更し、インストーラーがユーザー単位のスケジュールタスクでサーバーの起動とログオン時自動起動を管理する。stdio設定との後方互換性は持たない。

## Security

Bearer認証を必須とし、Originヘッダーがある要求はloopback Originだけを許可する。インストーラーは暗号学的乱数でトークンを生成し、ユーザー環境変数へ保存する。外部ネットワークへの公開はサポートしない。

## Operations

既定URLは外部設定とし、`ITOGURUMA_URL`で変更できる。`/health`で起動確認する。更新時は同じインストール先の旧サーバーを停止して置換し、再起動する。スケジュールタスクは多重実行を無視し、異常終了時に1分間隔で最大3回再起動する。旧レジストリ`Run`エントリは移行時に削除する。

## Implementation and verification

サーバー、インストーラー、README、コマンド一覧を同一変更で更新する。HTTPツール呼び出し、認証、Origin拒否、Mutex、メッセージライフサイクルを統合テストする。
