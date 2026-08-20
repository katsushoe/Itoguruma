# Itoguruma設定

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## 解決順と配置

環境変数は`src/Itoguruma.Server/appsettings.json`より優先されます。インストーラが生成する実行時ファイルは、別のインストール先を指定しない限り`%LOCALAPPDATA%\Programs\Itoguruma`配下に保存されます。

## 設定項目

| 環境変数／キー | 必須 | 型 | 既定値 | 制約 |
| :--- | :---: | :--- | :--- | :--- |
| `ITOGURUMA_AUTH_TOKEN` | はい | 文字列 | なし | 秘密のBearerトークン。十分に長い乱数を使用します。 |
| `ITOGURUMA_URL` | いいえ | 絶対HTTP URL | `http://127.0.0.1:47631` | 対応構成ではloopbackに限定します。 |
| `ITOGURUMA_DB` | いいえ | ファイルパス | Local Application Data配下のDB | 親ディレクトリへの書き込み権限が必要です。 |
| `ITOGURUMA_CONFIG_DIR` | いいえ | ディレクトリパス | インストール先の`config` | 生成設定の書き込み権限が必要です。 |
| `ITOGURUMA_LOG_DIR` | いいえ | ディレクトリパス | インストール先の`logs` | サーバープロセスの書き込み権限が必要です。 |
| `ITOGURUMA_CR_ROOT`／`Itoguruma:CrRoot` | CR配送時 | ディレクトリパス | なし | 共有CRルートを指定し、対象ファイルを`inbox/<target_project>/`配下へ置きます。 |

## 例

```powershell
$env:ITOGURUMA_AUTH_TOKEN = "<random-secret>"
$env:ITOGURUMA_URL = "http://127.0.0.1:47631"
$env:ITOGURUMA_DB = "$env:LOCALAPPDATA\Programs\Itoguruma\data\messages.db"
```
