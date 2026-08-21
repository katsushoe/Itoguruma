# Itoguruma

[English](README.md) | [日本語](README.ja.md)

Itogurumaは、Claude CodeやCodexなどの独立したAIエージェントが、共有SQLiteデータベースを介してメッセージを交換する常駐型MCP Streamable HTTPサーバーです。

## はじめに

最新ReleaseのMSIをインストールし、ターミナルとAIクライアントを再起動して往復通信を確認します。

```powershell
msiexec /i .\Itoguruma-x.x.x-win-x64.msi
itoguruma register --agent codex-main --type codex
itoguruma register --agent claude-main --type claude-code
itoguruma send --from codex-main --to claude-main --thread setup --body "Hello" --idempotency-key setup-1
itoguruma inbox --agent claude-main --lease-seconds 300
```

## インストール

GitHub Releasesは推奨のx64 MSI、`Install-Itoguruma.ps1`、自己完結型ZIPを提供します。MSIでは英語または日本語を選択でき、無人インストールでは`ITOGURUMA_LANGUAGE=en`または`ITOGURUMA_LANGUAGE=ja`を指定できます。インストーラに.NET SDKは不要です。ソースからビルドする場合は.NET 8 SDKが必要です。

```powershell
dotnet restore tests/Itoguruma.Tests/Itoguruma.Tests.csproj
dotnet build tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-restore
dotnet test tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-build
```

## 設定

サーバーには`ITOGURUMA_AUTH_TOKEN`が必須です。`ITOGURUMA_URL`、`ITOGURUMA_DB`、`ITOGURUMA_CONFIG_DIR`、`ITOGURUMA_LOG_DIR`、`ITOGURUMA_CR_ROOT`も使用できます。詳細は[CONFIG.ja.md](CONFIG.ja.md)を参照してください。

## 使用方法

メッセージは`pending`、`leased`、`acked`の配送状態を遷移します。ACK前にleaseが失効すると再配送されます。同じ論理送信を再試行するときは、同じ`idempotency_key`を使用してください。詳細は[COMMANDS.ja.md](COMMANDS.ja.md)を参照してください。

正式な変更依頼（CR）は、設定した共有CRルート内の検証済みMarkdownファイルを正本とします。無効なCRを通常メッセージへ自動変換しません。

## ドキュメント

- [設定](CONFIG.ja.md)
- [コマンドとMCP Tool](COMMANDS.ja.md)
- [MCPクライアント設定](MCP_SETUP.ja.md)
- [Hook設定](HOOKS.ja.md)
- [依存パッケージ](PACKAGES.ja.md)
- [セキュリティ](SECURITY.ja.md)
- [アーキテクチャ判断](docs/adr/0001-streamable-http-server.md)

## セキュリティ

Bearerトークンを秘密情報として管理し、サービスはloopbackに限定してください。侵害が疑われる場合は`itoguruma auth rotate`でローテーションします。詳細は[SECURITY.ja.md](SECURITY.ja.md)を参照してください。

## ライセンス

現時点では、リポジトリ所有者が配布ページで明示する条件を除き、再利用または再配布のライセンスは付与されていません。
