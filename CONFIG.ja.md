# Itoguruma 設定ガイド

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

このガイドはServer、CLI、Viewer、インストーラー、およびCodex/Claude Code向けに生成される設定を説明します。ServerのBearerトークンはWindows資格情報マネージャーだけに保存します。`appsettings.json`、MCP設定、環境変数、サンプルへトークンを記載しないでください。

## 設定ファイルと保存先

| ファイルまたは値 | 標準の場所 | 作成・読込元 |
| :--- | :--- | :--- |
| Server `appsettings.json` | `<install-directory>\config\appsettings.json`（既定のインストール先は`C:\Itoguruma`） | インストーラーがテンプレートから作成。Serverは`--config-dir`で選択した場所を読む。 |
| Server日次ログ | `<install-directory>\logs\itoguruma-server-YYYYMMDD.log` | Server。`--log-dir`で変更可能。 |
| SQLiteデータベース | インストール時`<install-directory>\data\messages.db`、直接起動時の既定`%LOCALAPPDATA%\Itoguruma\messages.db` | Server。インストーラーは`DatabasePath`を明示設定。 |
| Codex MCP設定 | `%USERPROFILE%\.codex\config.toml` | `itoguruma configure codex`。 |
| Codex Hook設定 | `%USERPROFILE%\.codex\hooks.json` | `itoguruma configure codex`。 |
| Claude Code MCP設定 | `%USERPROFILE%\.claude.json` | `itoguruma configure claude`。 |
| Claude Code Hook設定 | `%USERPROFILE%\.claude\settings.json` | `itoguruma configure claude`。 |
| Bearerトークン | Windows資格情報マネージャーの`Itoguruma/McpBearerToken` | インストーラーは未登録時に初期化。`itoguruma auth rotate`は明示的に置換。 |
| Viewer設定 | `%LOCALAPPDATA%\Itoguruma\viewer-settings.json` | Viewerの「設定」画面。自動更新とWindowsサインイン時の起動を保存。 |

クライアント設定コマンドは現在のユーザーの設定だけを管理し、無関係な項目を維持します。変更後は該当するAIクライアントを再起動または再読込してください。コマンド詳細は[COMMANDS.ja.md](COMMANDS.ja.md)を参照してください。

## ファイルの生成

インストーラーはServer同梱の`appsettings.json`テンプレートを設定先へコピーし、`Language`、`ServerUrl`、`DatabasePath`を書き込みます。Bearerトークンは含めません。Serverを直接起動するときは`--config-dir <directory>`と`--log-dir <directory>`を指定できます。省略時は実行ファイルのディレクトリとその`logs`サブディレクトリです。インストール済みタスクは両方を明示指定します。インストーラーはインストール後のServerをこのタスクから起動します。MSIのインストール処理から直接起動したServerは、同じユーザーでも一部のドライブのパス（別ボリュームへのジャンクション配下のCRファイルなど）を参照できないためです。タスクからの起動が正常にならない場合だけServerを直接起動し、警告を出します。この場合、次回のサインインでタスクから起動し直されるまで、一部のパスを参照できないことがあります。

Codex/Claude Codeの設定は`itoguruma configure codex|claude`が生成します。ローカルProxyのパス、MCPエンドポイント、Hookコマンドを含み、資格情報は含みません。他のPCから設定をコピーする場合は実行ファイルのパスを確認してください。

## 解決順序と優先順位

| 利用者 | 解決順序 |
| :--- | :--- |
| Server設定 | `--config-dir`で設定ファイルのルートを選び、その中の`appsettings.json`を読む。.NET標準環境変数プロバイダーにより`Itoguruma__ServerUrl`等でJSON値を上書き可能。 |
| CLIの接続先 | `--url` → `Itoguruma__ServerUrl` → `ITOGURUMA_SERVER_URL` → `appsettings.json`。設定ファイルは`ITOGURUMA_CONFIG_DIR\appsettings.json`、インストール先`config`、アプリケーションディレクトリ、ソースツリーの順に確認し、最初の非空値を使用。 |
| CLIの言語 | `ITOGURUMA_CONFIG_DIR`が設定されていて、その`appsettings.json`に`Itoguruma.Language`があれば使用。それ以外は英語。 |
| Viewerの言語 | CLIと同じ`ITOGURUMA_CONFIG_DIR`の規則。それ以外は英語。 |
| ViewerのDB | `ITOGURUMA_DB` → `%LOCALAPPDATA%\Programs\Itoguruma\data\messages.db`。Viewer専用で、ServerやCLI業務データベースは変更しない。 |

### Viewerのタスクトレイと更新

インストーラーはViewerをタスクトレイで起動します。Viewerは閉じるボタンまたは最小化操作で画面を隠し、トレイアイコンをダブルクリックすると再表示します。右クリックメニューから設定、更新確認、MCPサーバー、終了を選べます。MCPサーバーのサブメニューは、`Itoguruma:ServerUrl`の`/health`への応答からサーバーの状態（稼働中と版、応答なし、エラー、停止中）を、サブメニューと状態行の両方にLED画像付きで表示し（稼働中は緑、応答なしは黄＝プロセスはあるが`/health`が応答しない、エラーは赤＝`/health`がHTTPエラーまたは`ok`以外の状態を返した場合やメニューからの起動・停止が完了しなかった場合、停止中と未確認は灰）、「状態を確認」「起動」「停止」を提供します。「起動」はインストーラーが登録したタスク`ItogurumaServer`を実行し、タスクがない場合は`bin\server\Itoguruma.Server.exe`を直接起動します。「停止」は確認後にタスクを終了し、このインストール先のサーバープロセスが残っていれば終了させます。停止中は、Itogurumaを使うCodexとClaude Codeのすべてのセッションが、再び起動するまで切断されます。既定では自動更新とWindowsサインイン時の起動が有効です。スタートアップは現在のWindowsユーザーの`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`に登録します。Viewer設定はユーザー単位のJSONで、Serverの`appsettings.json`とは別です。

自動更新は起動時にGitHubの最新正式Releaseを確認します。現在版より新しい場合は対応するx64 MSIと`SHA256SUMS.txt`を一時フォルダーへダウンロードし、SHA-256が一致した場合だけWindows Installerを起動します。Release確認、ダウンロードまたは検証に失敗した場合も、現在のItogurumaは使用できます。更新を適用するとServerが再起動されます。

ServerとCLIが受け付けるのはHTTPのloopback接続先だけです。Serverの`ServerUrl`はパスを含まないorigin、CLIの`--url`はoriginまたは`/mcp`エンドポイントです。CLIは`/mcp`へ正規化します。リモートホスト、HTTPS、クエリ、フラグメントは使用できません。

## 主な設定

### `Itoguruma`グループ

Serverの`appsettings.json`に保存します。パッケージの既定値は次のとおりです。

| キー | 必須 | 型 | パッケージ既定値 | 省略時の動作 |
| :--- | :---: | :--- | :--- | :--- |
| [`Language`](#itoguruma-language) | いいえ | 文字列enum | `en` | 英語へフォールバック。 |
| [`ServerUrl`](#itoguruma-serverurl) | Serverでは必須 | 絶対HTTP URL | `http://127.0.0.1:47631` | Server起動失敗。CLIは別途接続先を解決。 |
| [`SingleInstanceWaitSeconds`](#itoguruma-singleinstancewaitseconds) | いいえ | 整数 | `5` | 5秒を使用。 |
| [`DatabasePath`](#itoguruma-databasepath) | いいえ | null許容パス文字列 | `null` | `%LOCALAPPDATA%\Itoguruma\messages.db`を使用。 |
| [`AuthenticationToken`](#itoguruma-authenticationtoken) | いいえ | 未使用の予約項目 | `null` | 無視。認証には資格情報マネージャーが必要。 |
| [`CrRoot`](#itoguruma-crroot) | いいえ | null許容ディレクトリパス | `null` | 正式なCR配送は無効。 |

<a id="itoguruma-language"></a>
### Itoguruma Language

- 用途：プロセスが設定言語を読み込む場合に、画面・メッセージの言語を選択します。
- 型と値：文字列。`en`は英語、`ja`は日本語。インストーラーはこの2値だけを受理し、その他の実行時文字列は英語へフォールバックします。
- 既定値・省略時：パッケージ値は`en`。未設定時は英語です。
- 設定元・優先順位：インストーラーの`-Language`またはMSIの`ITOGURUMA_LANGUAGE`が`appsettings.json`へ書き込みます。CLI/Viewerは環境変数`ITOGURUMA_CONFIG_DIR`が設定されている場合にそのファイルから読みます。Serverの言語ヘルパーも同環境変数を参照します。Serverの`--config-dir`だけではこの環境変数は設定されません。
- 例：`"Language": "ja"`。

<a id="itoguruma-serverurl"></a>
### Itoguruma ServerUrl

- 用途：Serverの待受先とCLIの既定接続先を指定します。
- 型：絶対HTTP URL。
- 既定値・省略時：パッケージ既定は`http://127.0.0.1:47631`。Serverは省略すると起動エラーです。CLIには組み込み既定値がなく、解決できなければ失敗します。
- 制約：Serverはパスなしのloopback originを要求します。CLIはloopback originまたは`/mcp`を受理し、`/mcp`へ接続します。
- 優先順位：Serverでは`Itoguruma__ServerUrl`等の環境変数がJSONを上書きします。CLIでは`--url`、`Itoguruma__ServerUrl`、`ITOGURUMA_SERVER_URL`、前述の順で探すJSONファイルです。
- インストーラー：`Install-Itoguruma.ps1 -ServerUrl`。既定値は`http://127.0.0.1:47631`で、MCP設定には末尾に`/mcp`を付加します。
- 例：`"ServerUrl": "http://127.0.0.1:47631"`。

<a id="itoguruma-singleinstancewaitseconds"></a>
### Itoguruma SingleInstanceWaitSeconds

- 用途：同じエンドポイントまたはDBロックを持つServerプロセスがある場合の起動待機時間を制限します。
- 型：0以上60以下の整数。
- 既定値・省略時：5秒。
- 無効値：整数以外または範囲外ではServer起動を中止します。
- 例：`"SingleInstanceWaitSeconds": 10`。

<a id="itoguruma-databasepath"></a>
### Itoguruma DatabasePath

- 用途：Serverが使うSQLiteデータベースファイルを指定します。
- 型：null許容のファイルパス文字列。
- 既定値・省略時：テンプレートは`null`。直接起動時は`%LOCALAPPDATA%\Itoguruma\messages.db`、インストール時は`<install-directory>\data\messages.db`です。
- 制約：Serverに親ディレクトリ作成とDB書込み権限が必要です。CLIの`--db`設定ではなく、CLI業務処理は稼働中Serverを利用します。
- 例：`"DatabasePath": "C:\\Itoguruma\\data\\messages.db"`。

<a id="itoguruma-authenticationtoken"></a>
### Itoguruma AuthenticationToken

- 用途：パッケージJSONテンプレートに残るnullの予約項目で、有効な設定入力ではありません。
- 型・既定値：`null`。文字列を設定してもServerや資格情報ストアは読み取りません。
- 省略時：トークン検索には影響しません。ServerとProxyは資格情報マネージャーの`Itoguruma/McpBearerToken`を必要とします。
- セキュリティ：このキーを設定せず、JSON・環境変数・ログ・クライアント設定へトークンを記載しないでください。未登録時の初期化はインストーラー、明示的な更新は`itoguruma auth rotate`を使います。
- 例：`"AuthenticationToken": null`のままにするか、省略します。

<a id="itoguruma-crroot"></a>
### Itoguruma CrRoot

- 用途：正式なCRファイルの検証に使う共有ルートを指定します。
- 型：null許容ディレクトリパス。
- 既定値・省略時：`null`。CR配送は無効です。
- 制約：対象ファイルはこのルートの`inbox/<target_project>/`配下に存在し、正規のCR検証に合格する必要があります。
- 環境変数による上書き：.NET標準キー`Itoguruma__CrRoot`を使用します。旧名風の`ITOGURUMA_CR_ROOT`は読み込まれません。
- 例：`"CrRoot": "F:\\Workspace\\Projects\\CRs"`（Serverから参照可能な共有ルートに置き換え）。

## ログ設定

### `Logging:LogLevel`グループ

Serverのフレームワークログの最低重要度を指定します。Serverのコンソール出力は選択された日次ログファイルへ記録されます。

| キー | 型 | パッケージ既定値 | 省略時 |
| :--- | :--- | :--- | :--- |
| [`Default`](#logging-default) | ログレベル文字列 | `Information` | フレームワーク既定値。 |
| [`Microsoft.AspNetCore`](#logging-aspnet-core) | ログレベル文字列 | `Warning` | `Default`を継承。 |

<a id="logging-default"></a>
### Logging Default

- 型：`Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`、`None`のいずれか（大文字小文字を区別しません）。
- 既定値・省略時：パッケージ値は`Information`。省略時はフレームワーク既定値。
- 例：`"Default": "Information"`。

<a id="logging-aspnet-core"></a>
### Logging ASP.NET Core

- 型：`Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`、`None`のいずれか（大文字小文字を区別しません）。
- 既定値・省略時：パッケージ値は`Warning`。省略時は`Default`を継承。
- 例：`"Microsoft.AspNetCore": "Warning"`。

## インストーラー設定

`Install-Itoguruma.ps1`の入力です。インストールまたは生成ファイルに作用し、Serverの永続環境変数ではありません。

| パラメーター | 型 | 既定値 | 制約・省略時の動作 |
| :--- | :--- | :--- | :--- |
| `-Version` | 文字列 | `latest` | 最新Release、または指定された版・タグを使用。 |
| `-PackagePath` | ファイルパス | 空 | 空ならReleaseパッケージを取得。指定時はローカルパッケージを使用。 |
| [`-InstallDirectory`](#install-directory) | ディレクトリパス | `C:\Itoguruma` | インストール先のルート。 |
| [`-PreviousInstallDirectory`](#install-directory) | ディレクトリパス | 空 | 空なら`-InstallDirectory`と同じ。異なる場合は既存インストールをそこから移行する。 |
| [`-ConfigDirectory`](#config-directory) | ディレクトリパス | `<InstallDirectory>\config` | Server設定の保存先。 |
| [`-LogDirectory`](#log-directory) | ディレクトリパス | `<InstallDirectory>\logs` | Serverログの保存先。 |
| [`-Language`](#installer-language) | 文字列enum | 空 | 空なら対話入力。`en`または`ja`。 |
| [`-ServerUrl`](#installer-server-url) | 絶対HTTP URL | `http://127.0.0.1:47631` | Serverのloopback origin制約に従う。 |
| `-ServerStopTimeoutSeconds` | 整数 | `30` | 5〜120秒。既存Serverの停止待機時間。 |
| `-NoPath` | Switch | 無効 | 指定するとユーザー`PATH`へCLIディレクトリを追加しない。 |
| [`-SkipCodex`](#skip-codex) | Switch | 無効 | 指定するとCodexのユーザー設定を変更しない。 |
| [`-SkipClaude`](#skip-claude) | Switch | 無効 | 指定するとClaude Codeのユーザー設定を変更しない。 |

<a id="install-directory"></a>
### Install Directory

- 型：ディレクトリパス。既定`C:\Itoguruma`。
- 省略時：既定ルートを使います。別途指定しない限り、バイナリ、`data`、`config`、`logs`を配下に置きます。
- 例：`-InstallDirectory 'D:\\Apps\\Itoguruma'`。
- MSI：プロパティ`ITOGURUMA_INSTALL_DIR`で指定します（例：`msiexec /i .\Itoguruma-x.x.x-win-x64.msi ITOGURUMA_INSTALL_DIR=E:\Itoguruma`）。省略時は前回のインストール先（`HKCU\Software\sazysoft\ItogurumaInstaller`の`InstallDirectory`）を使い、記録がなければ`C:\Itoguruma`です。選んだインストール先は同じ値へ記録され、以降の更新（Viewerの自動更新を含む）とアンインストールで使われます。
- 移行：前回と異なるインストール先を指定すると、Serverとトレイ常駐のViewerを停止し、`data`、`config`、`logs`を新しいルートへコピーし、`DatabasePath`が旧ルート内を指す場合は新しいルートへ書き換えます。ユーザー`PATH`の旧`bin`配下、タスク、Viewerの自動起動登録（有効な場合）、Codex・Claude Codeの設定を新しいルートへ更新し、新しいServerの起動確認後に旧ルートのバイナリと文書を削除します。旧ルートの`data`、`config`、`logs`は予備として残します。インストール先同士を入れ子にすることはできません。

<a id="config-directory"></a>
### Config Directory

- 型：ディレクトリパス。既定`<InstallDirectory>\config`。
- 省略時：既定ディレクトリを作成して`appsettings.json`を書き込みます。Serverタスクへ`--config-dir`を渡します。
- 例：`-ConfigDirectory 'D:\\AppData\\Itoguruma\\config'`。

<a id="log-directory"></a>
### Log Directory

- 型：ディレクトリパス。既定`<InstallDirectory>\logs`。
- 省略時：既定ディレクトリを作成し、Serverへ`--log-dir`を渡します。
- 例：`-LogDirectory 'D:\\AppData\\Itoguruma\\logs'`。

<a id="installer-language"></a>
### Installer Language

- 型：`en`または`ja`。既定は空文字列。
- 省略時：言語を対話入力します。MSIの公開プロパティ`ITOGURUMA_LANGUAGE`は既定`en`で、このパラメーターへ渡されます。
- 例：`-Language ja`または`msiexec /i Itoguruma.msi ITOGURUMA_LANGUAGE=ja`。

<a id="installer-server-url"></a>
### Installer Server URL

- 型：絶対HTTP URL。既定`http://127.0.0.1:47631`。
- 省略時：既定loopback Server originを設定し、Health確認には同じorigin、クライアント設定には`/mcp`を付加。
- 制約：パスを含まないloopback HTTP origin。
- 例：`-ServerUrl 'http://localhost:47631'`。

<a id="skip-codex"></a>
### Skip Codex

- 型：Switch。既定は無効。
- 省略時：Codexを検出すると`itoguruma configure codex`を実行します。指定時は設定しません。
- 例：`-SkipCodex`。

<a id="skip-claude"></a>
### Skip Claude

- 型：Switch。既定は無効。
- 省略時：Claude Codeを検出すると`itoguruma configure claude`を実行します。指定時は設定しません。
- 例：`-SkipClaude`。

## クライアント設定

現在のユーザースコープへ生成します。サーバー名は`itoguruma`、`command`はProxy実行ファイル、`args`はMCPエンドポイントです。ProxyはWindows資格情報マネージャーから資格情報を取得します。トークン用`env`項目を追加しないでください。

### Codex MCP設定

ファイル：`%USERPROFILE%\.codex\config.toml`。

| キー | 型 | 生成値・省略時の動作 |
| :--- | :--- | :--- |
| `mcp_servers.itoguruma.command` | 文字列 | `Itoguruma.McpProxy.exe`の絶対パス。未設定ならサーバー項目は利用不可。 |
| `mcp_servers.itoguruma.args` | 文字列配列 | `--url`とloopback `/mcp`の絶対URL。2要素の配列。 |

例（実際のインストール先に合わせて実行ファイルのパスを置換）：

```toml
[mcp_servers.itoguruma]
command = "C:\\Itoguruma\\bin\\mcp-proxy\\Itoguruma.McpProxy.exe"
args = ["--url", "http://127.0.0.1:47631/mcp"]
```

### Codex Hook設定

ファイル：`%USERPROFILE%\.codex\hooks.json`。生成するイベントキーは`SessionStart`、`UserPromptSubmit`、`Stop`です。各値は`hooks`ハンドラー配列を持つHookグループの配列です。

| ハンドラーキー | 型 | 生成値・省略時の動作 |
| :--- | :--- | :--- |
| `type` | 文字列enum | `command`。コマンドハンドラーを表す必須値。 |
| `command` | 文字列 | 引用符付き`itoguruma.exe`パスと`hook --project-inbox --consumer-agent codex`。 |
| `commandWindows` | 文字列 | 同じselectorとconsumerを使うWindows PowerShell呼び出し。 |
| `timeout` | 整数秒 | `15`。ハンドラーの実行期限。 |

例（イベント1つ分。configureは3イベントを生成し、既存ハンドラーを保持）：

```json
{
  "hooks": {
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "<installed-cli-path> hook --project-inbox --consumer-agent codex",
            "commandWindows": "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"& '<installed-cli-path>' hook --project-inbox --consumer-agent codex\"",
            "timeout": 15
          }
        ]
      }
    ]
  }
}
```

### Claude Code MCP設定

ファイル：`%USERPROFILE%\.claude.json`。

| キー | 型 | 生成値・省略時の動作 |
| :--- | :--- | :--- |
| `mcpServers.itoguruma.type` | 文字列enum | `stdio`。生成されるサーバー項目の必須値。 |
| `mcpServers.itoguruma.command` | 文字列 | `Itoguruma.McpProxy.exe`の絶対パス。未設定なら利用不可。 |
| `mcpServers.itoguruma.args` | 文字列配列 | `--url`とloopback `/mcp`絶対URLの2要素。 |

例（実際のインストール先に合わせてパスを置換）：

```json
{
  "mcpServers": {
    "itoguruma": {
      "type": "stdio",
      "command": "C:\\Itoguruma\\bin\\mcp-proxy\\Itoguruma.McpProxy.exe",
      "args": ["--url", "http://127.0.0.1:47631/mcp"]
    }
  }
}
```

### Claude Code Hook設定

ファイル：`%USERPROFILE%\.claude\settings.json`。生成するイベントは`UserPromptSubmit`で、値は`hooks`ハンドラー配列を持つHookグループの配列です。

| ハンドラーキー | 型 | 生成値・省略時の動作 |
| :--- | :--- | :--- |
| `type` | 文字列enum | `command`。必須値。 |
| `command` | 文字列 | 引用符付き`itoguruma.exe`パスと`hook --project-inbox --consumer-agent claude-code`。 |
| `commandWindows` | 文字列 | 同じHookを起動するWindows PowerShellコマンド。 |
| `timeout` | 整数秒 | `15`。ハンドラーの実行期限。 |

### `itoguruma configure`が生成するフィールド

推奨書き込み手段は`itoguruma configure codex|claude`です。現在のユーザー設定を使い、ItogurumaのMCP項目とItoguruma所有のHookだけを更新し、無関係なサーバーやHookを保持します。繰り返し実行しても重複を作りません。`unconfigure`はItogurumaの項目だけを削除します。インストーラーは対応する`-SkipCodex`または`-SkipClaude`がない限りこのコマンドを実行します。

## 非対応の旧環境変数名

次の名前は過去の手順に登場したか、インストール時に削除されますが、現行ランタイムは設定として読み込みません。上記の対応設定を使用してください。

| 名前 | 現在の動作 | 対応する設定 |
| :--- | :--- | :--- |
| `ITOGURUMA_AUTH_TOKEN` | 読み込まない。インストーラーはユーザー値を削除。 | Windows資格情報マネージャーと`itoguruma auth status|rotate`。 |
| `ITOGURUMA_URL` | Server、CLI、Proxyは読み込まない。 | 用途に応じて`Itoguruma:ServerUrl`、`Itoguruma__ServerUrl`、`ITOGURUMA_SERVER_URL`、CLI `--url`。 |
| `ITOGURUMA_LOG_DIR` | 読み込まない。インストーラーはユーザー値を削除。 | インストーラー`-LogDirectory`またはServer `--log-dir`。 |
| `ITOGURUMA_CR_ROOT` | 現行設定キーへ変換されない。 | `appsettings.json`の`Itoguruma:CrRoot`または`Itoguruma__CrRoot`。 |
| `ITOGURUMA_SINGLE_INSTANCE_WAIT_SECONDS` | 現行設定キーへ変換されない。 | `Itoguruma:SingleInstanceWaitSeconds`または`Itoguruma__SingleInstanceWaitSeconds`。 |

`ITOGURUMA_CONFIG_DIR`と`ITOGURUMA_DB`は有効ですが、[解決順序と優先順位](#解決順序と優先順位)に記載したCLI/Viewerの設定解決と言語読込、およびServerの言語読込に適用されます。インストーラーはServer起動前に継承されたユーザーレベル値を削除します。

## 設定例

最小限のServer設定例（トークンはWindows資格情報マネージャーに保存）：

```json
{
  "Itoguruma": {
    "Language": "en",
    "ServerUrl": "http://127.0.0.1:47631",
    "SingleInstanceWaitSeconds": 5,
    "DatabasePath": "C:\\Itoguruma\\data\\messages.db",
    "AuthenticationToken": null,
    "CrRoot": null
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

設定先とログ先を分けてインストールする例：

```powershell
.\Install-Itoguruma.ps1 -InstallDirectory 'C:\Itoguruma' -ConfigDirectory 'D:\Itoguruma\config' -LogDirectory 'D:\Itoguruma\logs' -Language en
```
