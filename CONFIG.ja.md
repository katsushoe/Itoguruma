# Itoguruma設定

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## 表示言語

インストーラは `Itoguruma.Language` を `config/appsettings.json` に保存します。対応値は `en` と `ja` です。Viewer UI、アプリケーションログ、コンソールメッセージはこの設定を使用します。無人インストールでは `-Language en` または `-Language ja` を指定します。省略時はインストーラで言語を選択します。

メッセージ診断は、本文、payload、認証情報を含めずサーバーログへ記録します。`[Messaging][Send]` はメッセージID、送信者、指定されたProject ID、解決後のInbox Agent ID、Thread、種類を記録します。`[Messaging][Lease]` はInbox Agent ID、取得したメッセージID、フィルター、リース期限を0件の場合も記録します。`[Messaging][Ack]` はInbox Agent ID、メッセージID、ACK結果を記録します。これらを照合すると、経路解決、リース、ACKのどの段階で問題が起きたか判別できます。

## 解決順と配置

環境変数は`src/Itoguruma.Server/appsettings.json`より優先されます。インストーラが生成する実行時ファイルは、別のインストール先を指定しない限り`C:\Itoguruma`配下に保存されます。

## 設定項目

| 環境変数／キー | 必須 | 型 | 既定値 | 制約 |
| :--- | :---: | :--- | :--- | :--- |
| `ITOGURUMA_AUTH_TOKEN` | はい | 文字列 | なし | 秘密のBearerトークン。十分に長い乱数を使用します。 |
| `ITOGURUMA_URL` | いいえ | 絶対HTTP URL | `http://127.0.0.1:47631` | 対応構成ではloopbackに限定します。 |
| `ITOGURUMA_DB` | いいえ | ファイルパス | Local Application Data配下のDB | 親ディレクトリへの書き込み権限が必要です。 |
| `ITOGURUMA_CONFIG_DIR` | いいえ | ディレクトリパス | インストール先の`config` | 生成設定の書き込み権限が必要です。 |
| `ITOGURUMA_LOG_DIR` | いいえ | ディレクトリパス | インストール先の`logs` | サーバープロセスの書き込み権限が必要です。 |
| `ITOGURUMA_CR_ROOT`／`Itoguruma:CrRoot` | CR配送時 | ディレクトリパス | なし | 共有CRルートを指定し、対象ファイルを`inbox/<target_project>/`配下へ置きます。 |
| `ITOGURUMA_SINGLE_INSTANCE_WAIT_SECONDS`／`Itoguruma:SingleInstanceWaitSeconds` | いいえ | 整数 | `5` | 0～60。再起動時に、停止中Serverが単一インスタンスロックを解放するまで待機します。 |

## 例

```powershell
$env:ITOGURUMA_AUTH_TOKEN = "<random-secret>"
$env:ITOGURUMA_URL = "http://127.0.0.1:47631"
$env:ITOGURUMA_DB = "C:\Itoguruma\data\messages.db"
```
