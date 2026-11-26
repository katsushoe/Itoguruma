# Itogurumaコマンド一覧

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

この文書は、`itoguruma` CLIとMCP Toolのコマンド一覧です。CLIの業務コマンドは起動中のItogurumaサーバーへ接続し、DBを直接開きません。共有`config/appsettings.json`の`Itoguruma:ServerUrl`、環境変数`Itoguruma__ServerUrl`、または各コマンドの`--url <server-url>`で接続先を指定できます。接続先はHTTP loopback originまたは`/mcp` endpointに限ります。Windows資格情報マネージャーのトークンと診断用世代IDを使用し、いずれも表示しません。

## CLIコマンド

| コマンド | 必須オプション | 主な任意オプション | 内容 |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type`, `--project` | `--name`, `--session`, `--metadata`, `--url` | サーバー経由でAgentを登録またはheartbeat更新します。 |
| `itoguruma agents` | なし | `--url` | サーバー上の登録済みAgentを一覧表示します。 |
| `itoguruma unregister` | `--agent` | `--url` | サーバー上のAgent登録を削除します。参照されているAgentは削除できません。 |
| `itoguruma delete-agent-history` | `--agent`、対話確認 | `--dry-run`、`--url` | 完全一致するAgent IDの履歴を事前確認または削除します。dry-runでは対話確認しません。 |
| `itoguruma send` | `--from`, 1個以上の`--to`, `--provider`, `--body`, `--thread` | `--reply-to`, `--message-type`, `--payload-json`, `--idempotency-key`, `--url` | サーバー経由でメッセージを永続化して配送待ちにします。 |
| `itoguruma inbox` | `--agent`, `--consumer-agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--url` | サーバーから未処理メッセージをleaseして取得します。 |
| `itoguruma ack` | `--agent`, `--consumer-agent`, `--message`, `--lease-id` | `--url` | サーバー上でlease済みメッセージをACKします。 |
| `itoguruma history` | `--thread` | `--limit`, `--offset`, `--url` | 指定Threadのメッセージ履歴を作成日時の昇順で返します。 |
| `itoguruma inspect-change-request` | `--payload-json` | `--url` | サーバー経由でCRファイルを再検証し、記録された状態との差異を返します。 |
| `itoguruma hook` | `--agent`または`--project-inbox`のどちらか一方、`--consumer-agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--url` | サーバー経由でHook入力を読み、InboxをHook出力へ追加します。`--project-inbox`は入力`cwd`のGitリポジトリに対応する登録Projectの受信箱を使います（[HOOKS.ja.md](HOOKS.ja.md)）。 |
| `itoguruma auth status` | なし | なし | トークン値を表示せず、設定有無と診断用世代IDを表示します。 |
| `itoguruma auth rotate` | なし | なし | 明示確認後に32バイトの暗号学的乱数でユーザー認証トークンをローテーションします。 |
| `itoguruma project add <project-id>` | `--inbox-agent`、対話確認 | `--display-name`、`--url` | サーバー上に有効なプロジェクトを追加します。 |
| `itoguruma project update <project-id>` | 対話確認 | `--inbox-agent`、`--display-name`、`--url` | サーバー上のプロジェクトを更新します。 |
| `itoguruma project enable|disable|delete <project-id>` | 対話確認 | `--url` | サーバー上で有効化、無効化、未参照プロジェクトの削除を行います。 |
| `itoguruma project list|show [project-id]` | `show`のみプロジェクトID | `--url` | サーバーの正本プロジェクト一覧または詳細を参照します。 |
| `itoguruma version` | なし | なし | 製品バージョンを`x.x.x`または`x.x.x.x`形式で表示します。 |
| `itoguruma --help` | なし | なし | CLI概要を表示します。 |

## CLI操作の詳細

以下の業務コマンドは、loopbackの`/mcp` endpointを解決し、Windows資格情報マネージャーからトークンを読み込みます。業務処理が成功するとMCPの`data`値を整形JSONで標準出力へ出し、終了コード`0`を返します。ローカル、通信、サーバーエラーは標準エラーへ出し、終了コード`2`を返します。`--url`は今回の実行だけ接続先を上書きします。数値の`--limit`、`--offset`、`--lease-seconds`が解析できない場合は記載した既定値に戻ります。`--limit`はサーバーで範囲検証されますが、`--offset`と`--lease-seconds`は範囲検証されないため各項目の注意に従ってください。CLIからDBファイルを直接指定する機能はありません。

### `register`

構文：`itoguruma register --agent <id> --type <type> --project <project-id> [--name <name>] [--session <id>] [--metadata <json>] [--url <url>]`。

有効な登録済みProject配下へ実行Agentを登録し、再実行時はheartbeatを更新します。`--agent`、`--type`、`--project`は必須です。`--metadata`を指定する場合はJSONとして有効である必要があります。例：`itoguruma register --agent codex-main --type codex --project itoguruma`。

成功すると`agentId`、`agentType`、`projectId`、時刻、指定した場合はsession/metadataを含むAgentレコードを返します。例：`{"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"}`。親Project不明・無効、metadata不正、既存登録との競合は構造化エラーです。先に`list_projects`で正規の親IDを選んでください。

### `agents`

構文：`itoguruma agents [--url <url>]`。状態を変更せず登録済みAgentを参照します。例：`itoguruma agents`。

成功するとAgentレコード配列を返し、空の場合は`[]`です。例：`[{"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"}]`。接続・サーバーエラーは前述のCLI共通エラー形式です。

### `unregister`

構文：`itoguruma unregister --agent <id> [--url <url>]`。`--agent`は完全一致・大文字小文字を区別するAgent IDです。登録だけを削除します。

成功時は`{"unregistered":true}`を返します（サービスが削除を行わなかった場合はfalse）。例：`itoguruma unregister --agent temporary-agent`。既存MessageやDeliveryが参照するAgentは削除できず、`agent_referenced`となります。履歴削除が必要なら`delete-agent-history`のdry-run件数を先に確認してください。

### `delete-agent-history`

構文：`itoguruma delete-agent-history --agent <id> [--dry-run] [--url <url>]`。IDは完全一致・大文字小文字を区別します。dry-runは非破壊で確認入力不要です。実削除にはリダイレクトされていない対話コンソールで、60秒以内に5桁コードを入力します（最大3回）。

まず`itoguruma delete-agent-history --agent old-agent --dry-run`を実行し、`messageCount`、`deliveryCount`、`threadCount`、`canUnregister`を確認します。明示承認後、`--dry-run`を外して再実行します。成功結果には件数と`correlationId`が含まれますが、本文やpayloadは返しません。例：`{"agentId":"old-agent","dryRun":true,"messageCount":3,"deliveryCount":2,"threadCount":1,"canUnregister":true,"correlationId":"…"}`。Agent不存在、確認コード誤り・期限切れ、リダイレクトコンソール、DB競合・障害は別のエラーコードになります。削除後、Agent登録は別途解除してください。

### `send`

構文：`itoguruma send --from <agent-id> --to <project-id> [--to <project-id> ...] --provider <runtime> --body <text> --thread <id> [--reply-to <message-id>] [--message-type message|notification|system|change_request] [--payload-json <json>] [--idempotency-key <key>] [--url <url>]`。

`--from`、1個以上の`--to`、`--provider`、`--body`、`--thread`は必須です。各`--to`は実行Agent IDではなくProject IDです。先に`list_projects`で正規IDを確認します。ProviderはASCII英字・数字・ハイフンを受理し、小文字で保存します。同じ論理送信を再試行する場合は同じ冪等性キーを再利用します。change requestには有効なCR payloadと正本ファイルが必要で、通常Messageへ置き換えません。

成功結果は`{"messageId":"<id>"}`です。同じ送信元とキーの再試行では既存Messageを返します。例：`itoguruma send --from codex-main --to moyai --provider codex --body "Review requested" --thread review --idempotency-key review-1`。送信元/返信先の参照切れ、ProviderやMessage種別不正、JSON/CR不正、宛先不正・無効、Transaction競合、DB障害は別々にエラーとなります。同じキーは同じ内容・意図の再試行だけで使ってください。

### `inbox`

構文：`itoguruma inbox --agent <inbox-agent-id> --consumer-agent <consumer-id> [--limit <1..500>] [--lease-seconds <seconds>] [--thread <id>] [--message-type <type>] [--url <url>]`。

`--agent`は受信箱、`--consumer-agent`はlease所有者を指定します。既定はlimit `50`、lease期間`300`秒です。thread/type filterは保留中または期限切れDeliveryを絞ります。lease成功によりDelivery状態と試行回数が更新されます。

成功すると`messageId`、`body`、`provider`、`deliveryStatus`、`leaseUntil`、`leaseId`、`leaseOwnerAgentId`、`deliveryAttemptCount`等を含む配列を返し、該当なしは`[]`です。例：`[{"messageId":"m-1","deliveryStatus":"leased","leaseId":"lease-1","leaseOwnerAgentId":"codex-root","deliveryAttemptCount":1}]`。limitの範囲外や不正な時間値はサーバーが拒否します。処理後のACKにはこの`leaseId`を指定し、処理前にACKしないでください。

### `ack`

構文：`itoguruma ack --agent <inbox-agent-id> --consumer-agent <consumer-id> --message <message-id> --lease-id <lease-id> [--url <url>]`。

4つのIDは全て必須です。同一consumer・lease IDの有効な配送だけACKできます。同じACKの再試行は安全です。

成功結果は`{"acked":true}`です（lease不一致ならfalse）。例：`itoguruma ack --agent moyai --consumer-agent moyai-codex-root --message m-1 --lease-id lease-1`。期限切れ・古い・不一致のleaseはACKされません。再配送を処理する前に`inbox`で取り直してください。

### `history`

構文：`itoguruma history --thread <id> [--limit <1..500>] [--offset <0以上>] [--url <url>]`。既定はlimit `100`、offset `0`です。

ACK済みを含む履歴を古い順に返します。不明なThreadは`[]`です。例：`itoguruma history --thread review --limit 20`。各レコードにはMessage/Thread/送信者ID、provider、種別、本文、payload、返信先、作成時刻が含まれます。出力には本文が含まれるため、共有先に注意してください。`limit`の1〜500範囲はサーバーが検証します。`offset`は0以上を指定してください。現行実装は負数を拒否しません。

### `inspect-change-request`

構文：`itoguruma inspect-change-request --payload-json <json> [--url <url>]`。JSONは正規CR payloadとして有効で、パスは設定済みCR root配下の対象Project Inboxにある正本を指す必要があります。

ファイルを再読込・検証し、現在の項目や状態とpayloadを比較します。CRを送信・編集する操作ではありません。成功するとパス、依頼元/依頼先、優先度、payload/ファイルの状態、一致結果を返します。例：`{"statusMatches":true,"payloadStatus":"未着手","fileStatus":"未着手"}`。payload・ファイル不正、状態差異、CR root未設定は構造化エラーです。payloadへ秘密情報を含めないでください。

### `hook`

構文：`itoguruma hook (--agent <inbox-id>|--project-inbox) --consumer-agent <consumer-id> [--limit <1..500>] [--lease-seconds <seconds>] [--thread <id>] [--message-type <type>] [--url <url>]`。Inbox selectorはどちらか一方だけ指定します。標準入力からJSONを読み、`--project-inbox`は入力`cwd`のGitリポジトリ名から登録Projectを解決します。

一致するMessageをleaseし、Hook形式のcontextを作ります。該当なしなら出力なしで終了コード`0`、通常contextは標準出力と`0`です。Messageのある`Stop`イベントでは停止状態となり、contextを標準エラーへ出して終了コード`2`です。入力例：`{"hook_event_name":"UserPromptSubmit","cwd":"C:\\work\\my-project"}`。結果例：`Messages for my-project: ...`。JSON不正は省略可能フィールドなしとして扱います。selector不正、認証/通信失敗、サーバー検証エラーは非0です。leaseされるため、Hook側で各Messageを処理し、lease IDでACKしてください。

### `auth status`

構文：`itoguruma auth status`。Windows資格情報マネージャーの現在ユーザー項目を読むローカル処理で、サーバーへ接続しません。

資格情報の有無を表示し、登録済みなら診断用世代IDも表示します。例：`Authentication token: configured.` と `Generation ID: <id>`。未設定時は`Authentication token: not configured.`です。トークン本体と派生値は表示しません。

### `auth rotate`

構文：`itoguruma auth rotate`。影響を表示し、正確に`ROTATE`と入力する必要があります。それ以外はキャンセルし終了コード`1`です。32バイトの乱数で新しいトークンと世代IDを生成し、ユーザー資格情報を置換します。成功例：`Authentication token rotated. Generation ID: <id>. The token value is not displayed.`

旧トークンは直ちに無効になります。`ItogurumaServer`スケジュールタスク、Codex、Claude Code、トークンを直接保持するクライアントを再起動してください。資格情報保存エラーは終了コード`2`です。トークンをコマンド引数、環境変数、クライアントファイルへ複製しないでください。

### `configure codex` / `configure claude`

構文：`itoguruma configure codex|claude [--url <server-url>] [--proxy-path <path>]`。`--url`省略時は設定済みloopback Server、`--proxy-path`省略時は対応バージョンのインストール済みProxyを使います。Proxy実行ファイルが存在する必要があります。

現在のユーザー設定へMCP登録とItoguruma所有Hookを作成・更新し、無関係な設定を保持します。成功時はクライアント名と変更ファイル数を表示します。例：`configure codex: updated 2 user configuration file(s).` Client/option不正、Proxy不在、JSON不正、原子的置換失敗は終了コード`2`です。複数ファイルの一部更新後に失敗した場合、変更前の内容へ戻します。トークンは設定へ書き込みません。反映には対象クライアントの再起動が必要です。

### `unconfigure codex` / `unconfigure claude`

構文：`itoguruma unconfigure codex|claude`。URLとProxyパスは不要です。

現在のユーザー設定からItogurumaのMCP登録とHookだけを削除します。例：`unconfigure claude: updated 2 user configuration file(s).` 他のサーバー、Hook、設定は保持します。Client不正、JSON不正、置換失敗は終了コード`2`で、複数ファイル更新に失敗した場合はロールバックします。削除反映にはクライアント再起動が必要です。

### `project list`

構文：`itoguruma project list [--url <url>]`。正規プロジェクト一覧を変更せずに取得します。例：`itoguruma project list`。

`projectId`、`displayName`、`inboxAgentId`、`enabled`、時刻を含むレコード配列を返します。空なら`[]`です。送信先選択にはここで返る正規IDを使います。

### `project show`

構文：`itoguruma project show <project-id> [--url <url>]`。大文字小文字を区別せず該当Projectを取得します。成功時はProjectレコード、不明IDは`ITG_PROJECT_UNKNOWN`です。`project list`から既存の正規IDを選んでください。

### `project add`

構文：`itoguruma project add <project-id> --inbox-agent <agent-id> [--display-name <name>] [--url <url>]`。送信前に5桁の対話確認が必要です。

有効なProjectとInbox対応を追加します。表示名省略時は正規化Project IDを使います。成功時はProjectレコードを返します。例：`itoguruma project add moyai --inbox-agent moyai-inbox --display-name Moyai`。Project IDは小文字化され、`^[a-z][a-z0-9_]*$`に一致する必要があります。確認にはリダイレクトされていない実コンソール、60秒以内の入力、最大3回が必要です。不正ID、大小文字だけ異なるIDとの競合、Agent競合、確認失敗・期限切れ、サーバーエラーでは登録されません。

### `project update`

構文：`itoguruma project update <project-id> [--inbox-agent <agent-id>] [--display-name <name>] [--url <url>]`。対話確認が必要です。変更フィールドを1つ以上指定します。

成功時は更新済みProjectレコードを返します。例：`itoguruma project update moyai --display-name "Moyai Project"`。ID不明、値不正、Inbox Agent競合、確認エラーがある場合は変更されません。確認前にIDと変更内容を確認してください。

### `project enable` / `project disable`

構文：`itoguruma project enable|disable <project-id> [--url <url>]`。どちらも対話確認が必要です。enableは配送対象にし、disableは履歴とInbox登録を保持したまま新規配送を止めます。

成功時は`enabled=true|false`を含むProjectレコードを返します。例：`itoguruma project disable moyai`。ID不明や確認エラーでは状態は変わりません。メッセージ履歴が残るProjectにはdisableを推奨します。

### `project delete`

構文：`itoguruma project delete <project-id> [--url <url>]`。対話確認が必要で、保持中のMessage/DeliveryがProjectを参照していない場合だけ成功します。

成功結果は`{"deleted":true}`です。参照中は`ITG_PROJECT_REFERENCED`となるため、代わりにdisableしてください。確認・検証失敗では登録を変更しません。ProjectとInbox登録を削除しますが、無関係なAgent履歴は削除しません。

### `version` / ヘルプ

構文：`itoguruma version`または`itoguruma --help`。versionは`itoguruma <version>`を表示して`0`で終了します。ヘルプはコマンド群と接続先の概要を表示します。`itoguruma configure --help`、`itoguruma unconfigure --help`、`itoguruma auth --help`は各Synopsisを表示します。不明なコマンド・optionやローカル設定不正は終了コード`2`です。

## CLI例

```powershell
itoguruma register --agent claude-main --type claude-code --project itoguruma
itoguruma register --agent codex-main --type codex --project itoguruma
itoguruma version
itoguruma agents
itoguruma unregister --agent claude-main
itoguruma send --from claude-main --to moyai --provider claude-code --thread setup --body "確認してください" --idempotency-key setup-1
itoguruma inbox --agent codex-main --consumer-agent codex-root --lease-seconds 300
itoguruma ack --agent codex-main --consumer-agent codex-root --message <messageId> --lease-id <leaseId>
itoguruma auth status
itoguruma auth rotate
```

`send`を再試行するときは、同じ論理送信に同じ`--idempotency-key`を渡してください。`inbox`で取得した処理済みメッセージは、必ず`ack`してください。

`auth rotate`は影響範囲を表示し、`ROTATE`の入力後に実行します。トークン値は標準出力やエラー出力へ表示しません。実行後は`ItogurumaServer`スケジュールタスク、Codex、Claude Code、Bearerトークンを直接保持する接続クライアントを再起動してください。

## インストーラオプション

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 [-Version <version>] [-PackagePath <zip>] [-InstallDirectory <path>] [-ConfigDirectory <path>] [-LogDirectory <path>] [-Language en|ja] [-ServerUrl <url>] [-ServerStopTimeoutSeconds <5..120>] [-NoPath] [-SkipCodex] [-SkipClaude]
```

| オプション | 既定値 | 動作・制約 |
| :--- | :--- | :--- |
| `-Version` | `latest` | 指定Releaseを取得します。 |
| `-PackagePath` | 空 | 指定時はダウンロードせずローカルZIPを使用します。 |
| `-InstallDirectory` | `C:\Itoguruma` | アプリケーションのインストール先です。 |
| `-ConfigDirectory` | `<InstallDirectory>\config` | Server設定ファイルの保存先です。 |
| `-LogDirectory` | `<InstallDirectory>\logs` | Serverの日次ログ保存先です。 |
| `-Language` | 空 | 対話入力します。値は`en`または`ja`。MSIの公開`ITOGURUMA_LANGUAGE`既定値は`en`です。 |
| `-ServerUrl` | `http://127.0.0.1:47631` | パスなしのHTTP loopback origin。生成するクライアントURLには`/mcp`を追加します。 |
| `-ServerStopTimeoutSeconds` | `30` | 5〜120秒の整数です。 |
| `-NoPath` | 無効 | 現ユーザーの`PATH`へCLI/補助ツールのディレクトリを追加しません。 |
| `-SkipCodex` | 無効 | CodexのMCP/Hook設定を省略します。 |
| `-SkipClaude` | 無効 | Claude CodeのMCP/Hook設定を省略します。 |

例：`powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 -PackagePath .\Itoguruma.zip -InstallDirectory C:\Itoguruma -ConfigDirectory D:\Itoguruma\config -LogDirectory D:\Itoguruma\logs -Language ja -SkipClaude`。

インストーラーはappsettingsとServerタスクを作成し、資格情報が未登録の場合だけ初期化します。検出したClientには、skip指定がなければ設定コマンドを実行します。Server起動前に継承された旧Itogurumaユーザー環境変数を削除します。インストールはマシン/ユーザー状態を変更するため、管理対象ファイルやClient設定の削除にはアンインストーラーを使います。
## クライアント連携の設定

`itoguruma configure codex`または`itoguruma configure claude`でユーザー単位のMCP/Hook設定を作成します。Codex MCPは`%USERPROFILE%\.codex\config.toml`、Hookは`%USERPROFILE%\.codex\hooks.json`、Claude Code MCPは`%USERPROFILE%\.claude.json`、Hookは`%USERPROFILE%\.claude\settings.json`に保存します。MCP登録名は`itoguruma`です。再実行時はItoguruma項目だけを更新し、無関係な設定を保持します。

`--url`でこの操作の接続先を上書きできます。省略時は共有`Itoguruma:ServerUrl`を読み、`/mcp`へ正規化します。`--proxy-path`はインストール済みProxyのパスを上書きします。認証トークンはクライアント設定へ埋め込みません。

`itoguruma unconfigure codex`または`itoguruma unconfigure claude`で、`itoguruma`のMCP登録と、そのClient用のItoguruma Hookだけを削除します。ProxyパスとServer URLは不要です。各ファイルを同一ディレクトリの一時ファイルから原子的に置換し、更新失敗時は先行する変更を復元します。JSON不正や置換失敗では非0で終了します。

設定後はクライアントを再起動してください。Codexでは新しい非管理Hookの確認・信頼を求められる場合があります。インストーラーは検出Clientごとにconfigure、アンインストーラーはCLI削除前にunconfigureを実行します。CLIが見つからない場合、アンインストーラーはユーザー設定を変更しません。
## MCP Tool

| Tool | 必須入力 | 主な任意入力 | 内容 |
| :--- | :--- | :--- | :--- |
| `get_version` | なし | なし | 実行中のItoguruma MCP Serverバージョンを返します。 |
| `register_agent` | `agent_id`, `agent_type`, `project_id` | `name`, `session_id`, `metadata_json` | 有効な親Projectを検証してAgentを登録またはheartbeat更新します。 |
| `register_project_inbox` | `project_id`, `display_name` | なし | ProjectとInbox Agentを原子的かつ冪等に登録または更新します。 |
| `list_agents` | なし | なし | 登録済みAgentを一覧表示します。 |
| `list_projects` | なし | なし | 変更を行わず、正規の送信先プロジェクト一覧を返します。 |
| `get_project` | `project_id` | なし | 1件の登録済みProject、または未登録を示す構造化エラーを返します。 |
| `add_project` | `project_id`, `inbox_agent_id` | `display_name` | 共通サービス経由でProjectとInboxを追加します。 |
| `update_project` | `project_id` | `display_name`, `inbox_agent_id` | 表示名またはInbox Agentを更新します。 |
| `set_project_enabled` | `project_id`, `enabled` | なし | 登録済みProjectを有効化または無効化します。 |
| `delete_project` | `project_id` | なし | 参照されていないProjectを削除し、`deleted`を返します。 |
| `unregister_agent` | `agent_id` | なし | Agent登録を削除します。既存メッセージから参照されているAgentは削除できません。 |
| `delete_agent_history` | `agent_id`, `dry_run` | なし | 関連Message・Deliveryを事前確認またはTransactionで削除します。 |
| `send_message` | `sender_agent_id`, `provider`, `body`, `thread_id`と宛先 | `reply_to_message_id`, `message_type`, `payload_json`, `idempotency_key` | 1件以上の宛先へ送信します。 |
| `get_messages` | `agent_id`, `consumer_agent_id` | `limit`, `lease_seconds`, `thread_id`, `message_type` | 配送可能なメッセージをleaseし、取得Agentを記録して`lease_id`を返します。 |
| `ack_message` | `agent_id`, `consumer_agent_id`, `message_id`, `lease_id` | なし | 取得Agentとlease IDが一致する配送だけをACKします。 |
| `get_conversation_history` | `thread_id` | `limit`, `offset` | 指定Threadの既読・過去分を含む全メッセージ履歴を、作成日時の昇順で返します。該当Threadが存在しない場合は空配列を返します。 |
| `inspect_change_request` | `payload_json` | なし | CRファイルを再検証し、payloadに記録された状態との不一致を返します。 |
| `get_hook_context` | `consumer_agent_id`、`agent_id`または`working_directory`のどちらか一方 | `hook_event_name`, `limit`, `lease_seconds`, `thread_id`, `message_type` | 取得Agent向けにメッセージをleaseし、CLI Hook互換のコンテキスト、解決した`agent_id`、停止状態を返します。`working_directory`はCLIの`--project-inbox`と同じ規則で受信箱を選びます。 |
| `get_auth_status` | なし | なし | トークン値を表示せず、設定有無と診断用世代IDを返します。 |
| `rotate_auth_token` | `confirmation=ROTATE` | なし | トークン値を返さずに更新します。実行後はサーバーとクライアントの再起動が必要です。 |

`get_version`は、稼働中サーバーの名前と`x.x.x`または`x.x.x.x`形式の製品バージョンを返します。

`provider`／`--provider`は送信ごとに必須で、`codex`、`claude-code`などの送信元実行環境を表します。小文字へ正規化され、ASCII英数字とハイフンだけを許可します。Itogurumaは指定値をメッセージへ保存し、Inbox、lease再配送、Hook、履歴、Viewerで同じ値を返します。認証済みクライアントが申告する配送メタデータであり、本人確認には使用しません。schema version 3以前から移行した既存メッセージは、過去値を推測せず`provider=unknown`として返します。

`send_message`の宛先は、単一宛先なら`recipient`、複数宛先なら`recipients`を使います。`message_type`は`message`、`notification`、`system`、`change_request`のいずれかです。CRは通常メッセージへフォールバックせず、登録済みProjectを明示的な宛先に指定します。メッセージはそのProjectに設定された単一のProject Inbox Agentへ配送されます。

CLIの業務コマンドは、共有`config/appsettings.json`の`Itoguruma:ServerUrl`、環境変数`Itoguruma__ServerUrl`、または`--url <server-url>`で指定した稼働中サーバーへ接続します。接続にはWindows資格情報マネージャーのトークンを使い、サーバーを利用できない場合は非0終了コードでエラーを返します。DBを直接開く`--db`はサポートしません。

実行Agentの登録前に、初回は`register_project_inbox`、既存Projectでは`list_projects`を使用し、正規IDを`project_id`へ指定します。`metadata_json`内の`projectId`は参照整合性の根拠になりません。

`send`／`send_message`の前に`list_projects`を呼び、大文字小文字非依存で照合した正規Project IDを選択します。宛先はProject IDであり、実行中Agent IDではありません。入力はInvariant lowercaseへ正規化し、`^[a-z][a-z0-9_]*$`に一致する必要があります。正しい形式で未登録なら、送信トランザクション内で正規IDをProject ID兼Inbox Agent IDとして有効なプロジェクトと`project_inbox` Agentを自動登録し、そのまま配送します。不正IDは`ITG_PROJECT_ID_INVALID`として、入力値と類似する登録済み候補を最大5件返します。無効プロジェクトは`ITG_PROJECT_DISABLED`を返します。CLIの明示的なプロジェクト変更は実コンソールで5桁コードを60秒以内、最大3回で再入力する必要があります。入出力リダイレクトや回避オプションは認めません。MCP変更操作は認証済みloopbackサーバーを経由します。参照済みプロジェクトの削除は`ITG_PROJECT_REFERENCED`となるため、`disable`を使用します。

Project IDはInvariant lowercaseで保存・返却し、大文字小文字を区別せず照合します。Agent IDは大文字小文字を区別し、送信元識別とInbox leaseに使用します。既存IDと大文字小文字だけが異なるプロジェクトは別登録できません。

参照済みAgentを削除する場合は、最初に`delete_agent_history`を`dry_run=true`で実行し、Message・Delivery・Thread件数を確認します。明示承認後に`dry_run=false`で再実行し、続けて`unregister_agent`を実行します。削除対象は完全一致するAgent IDが送信したMessage、そのMessageへ依存する返信、Agent宛てDeliveryです。全削除を一つのTransactionで行い、Message本文やpayloadを応答・Logへ出しません。監査記録はAgent ID、件数、時刻、Correlation IDだけを保持します。Agent不存在`ITG_AGENT_NOT_FOUND`、Transaction競合、DB障害は別々の構造化エラーです。CLIの実削除ではプロジェクト変更と同じ5桁の対話確認が必要です。

## MCP Tool操作の詳細

MCP呼び出しは`{ "data": ... }`のwrapperを返します。エラーは`isError=true`となり、利用可能な場合は`errorCode`、`category`、`summary`、`suggestedAction`、`retryable`を含みます。以下の例は`data`値です。認証にはloopbackサーバーのBearer資格情報を使います。世代IDは診断専用です。破壊的Toolは呼び出し元ワークフローによる明示承認が必要です。

### `get_version`

入力なし。読取専用で状態を変更しません。稼働Server名と版数を返します。例：`{"name":"itoguruma","version":"0.3.43"}`。業務データは返しません。

### `register_agent`

必須入力：`agent_id`、`agent_type`、`project_id`。任意入力：`name`、`session_id`、`metadata_json`。有効Project配下へAgentを登録し、既存時はheartbeatを更新します。Agentレコードを返します。例：`{"data":{"agentId":"codex-main","projectId":"itoguruma","agentType":"codex"}}`。metadata不正、親Project不明/無効、登録競合は`validation/agent_registration`です。再試行前に`list_projects`と`list_agents`を確認してください。

### `register_project_inbox`

入力：`project_id`、`display_name`。正規Projectと対応する`project_inbox` Agentを原子的・冪等に作成または更新します。Projectレコードを返します。例：`{"projectId":"cardfantasy","displayName":"Card Fantasy","inboxAgentId":"cardfantasy","enabled":true}`。ID不正や登録競合は`validation/project_registration`です。同じ要求の再実行は安全です。

### `list_agents`

入力なし。永続化されたAgentを読取専用で一覧し、ID、種別、Project、時刻、任意のsession/metadataを返します。例：`[{"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"}]`。空なら`[]`です。

### `list_projects`

入力なし。Project ID、表示名、Inbox Agent ID、有効状態、時刻を含む正規一覧を返します。例：`[{"projectId":"moyai","displayName":"Moyai","inboxAgentId":"moyai-inbox","enabled":true}]`。送信前に呼び、正規Project IDを選んでください。

### `get_project`

入力：`project_id`。大小文字を区別せず1件を取得します。Projectレコード、または`ITG_PROJECT_UNKNOWN`を含む`validation/project`エラーです。正規IDは`list_projects`で確認します。

### `add_project`

必須入力：`project_id`、`inbox_agent_id`。任意入力：`display_name`。共通Registryへ有効Projectを追加し、Projectレコードを返します。例：`{"projectId":"moyai","inboxAgentId":"moyai-inbox","enabled":true}`。ID不正、Inbox Agent不明/競合等は構造化`validation/project`エラーです。MCP呼び出し元はloopbackサーバーで認証されます。CLIの対話確認はこのMCP操作には適用されません。

### `update_project`

入力：`project_id`。任意：`display_name`、`inbox_agent_id`（少なくとも一方を指定）。登録済みProjectを更新し、新レコードを返します。ID不明、値不正、Inbox競合は`validation/project`エラーとなり、部分更新は確定しません。

### `set_project_enabled`

入力：`project_id`、`enabled` boolean。登録済みProjectを有効化・無効化し、更新レコードを返します。無効Projectには新規Messageを配送せず、履歴は維持します。Project不明は構造化エラーです。

### `delete_project`

入力：`project_id`。破壊的操作です。保持中Message/Deliveryが参照していない場合に限りProjectとInbox対応を削除します。成功値は`{"deleted":true}`です。参照中は`ITG_PROJECT_REFERENCED`を返します。履歴と影響を確認し、削除できない場合はdisableを使ってください。

### `unregister_agent`

入力：`agent_id`。完全一致するAgent登録を削除し、`{"unregistered":true|false}`を返します。Message/Deliveryが参照中なら`agent_referenced`です。明示承認がある場合だけ関連履歴を先に確認・削除してください。

### `delete_agent_history`

入力：`agent_id`、`dry_run` boolean。`dry_run=false`は破壊的です。先にtrueで実行し件数と`canUnregister`を確認します。別途明示承認後にfalseで再実行します。`agentId`、`dryRun`、Message/Delivery/Thread件数、`canUnregister`、`correlationId`を返し、本文/payloadは返しません。例：`{"agentId":"old-agent","dryRun":true,"messageCount":3,"deliveryCount":2,"threadCount":1,"canUnregister":true,"correlationId":"…"}`。Agent不明、Transaction競合、DB障害を区別します。その後、登録解除は別途実行します。

### `send_message`

入力：`sender_agent_id`、`provider`、`body`、`thread_id`、`recipient`または空でない`recipients`。任意：返信先、Message種別、payload JSON、冪等性キー。先に`list_projects`を呼び、宛先はProject IDを選びます。形式が正しい未知IDは有効なProjectとInboxをトランザクション内で登録します。成功結果は`{"messageId":"<id>"}`です。同じ送信元/キーの再試行は既存Messageを返します。`message_type`は`message`、`notification`、`system`、`change_request`。CRは正規ファイルを要求し、通常Messageへフォールバックしません。Providerは小文字のASCII英数字・ハイフンとして保存されます。

エラーは参照切れ（`sqlite/table/write/reference_key`）、引数/payload、Provider、CR、Project宛先不正・無効、Transaction競合、DB障害を区別します。Project宛先エラーには入力IDと最大5件の候補が含まれます。一時・結果不明の再試行では同じ冪等性キーを使います。別内容に同じキーを使わないでください。

### `get_messages`

必須入力：`agent_id`、`consumer_agent_id`。任意：`limit`（既定50、1〜500）、`lease_seconds`（既定300。正の値を指定してください）、`thread_id`、`message_type`。保留・期限切れDeliveryをleaseし、所有者、期限、ID、試行回数を記録します。Message配列を返し、対象なしは`[]`です。例：`[{"messageId":"m-1","deliveryStatus":"leased","leaseId":"lease-1","leaseOwnerAgentId":"codex-root","deliveryAttemptCount":1}]`。`lease_seconds`に正の範囲検証はなく、0または負数では期限が過去になり直ちに再配送可能となるため指定しないでください。所有者を確認し、処理後に同じlease IDでACKしてください。

### `ack_message`

入力：`agent_id`、`consumer_agent_id`、`message_id`、`lease_id`。冪等です。Inbox、consumer、有効leaseが一致する場合だけACKし、`{"acked":true|false}`を返します。例：`{"acked":true}`。falseならleaseが期限切れまたは不一致です。再配送を処理する前に取り直してください。

### `get_conversation_history`

入力：`thread_id`。任意：`limit`（既定100、1〜500）、`offset`（既定0、非負値を指定）。読取専用でACK済みを含む履歴を古い順に返します。`limit`範囲は検証しますが、負のoffsetは拒否しません。不明Threadは`[]`です。レコードに本文とpayloadが含まれるため、意図した相手にだけ共有してください。

### `inspect_change_request`

入力：`payload_json`。正規CR payloadと正本ファイル、現在状態を読取専用で再検証します。パス、依頼元/依頼先、優先度、payload/ファイル状態、一致結果を返します。payload/ファイル不正や状態差異は`validation/change_request`、CR root未設定は`configuration/change_request`です。ファイルの編集・配送・修復はしません。不正CRを通常Messageへ置き換えないでください。

### `get_hook_context`

必須：`consumer_agent_id`と`agent_id`または`working_directory`のどちらか一方。任意：event名、limit（既定50）、lease秒（既定300）、thread、type。Messageをleaseし、`{"context":...,"agentId":...,"shouldStop":...,"messages":[...]}`を返します。directoryは、親Git Repository名のInvariant lowercaseと一致する有効登録Projectを解決します。該当なしはleaseせず、context/agentIdがnull、停止false、空配列です。Messageがありevent名が`Stop`の場合のみ`shouldStop=true`です。lease済みMessageを処理してACKしてください。

### `get_auth_status`

入力なし、読取専用です。`{"configured":true|false,"generationId":"<id>|null"}`を返します。資格情報本体と指紋は返しません。世代IDは診断用で、認証の代わりには使えません。

### `rotate_auth_token`

入力：`confirmation`。値が正確に`ROTATE`である必要があります。破壊的操作で旧トークンを直ちに無効化し、新しい資格情報を保存します。成功結果は`{"rotated":true,"nextAction":"Restart ..."}`で、秘密値は返しません。別値は`confirmation_required`です。実行後はServerと接続Clientを再起動します。ユーザーがローテーションを承認した場合にだけ呼んでください。
