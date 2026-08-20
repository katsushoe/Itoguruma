# Itoguruma依存パッケージ

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

## 直接依存

| プロジェクト | パッケージ | バージョン | 用途 |
| :--- | :--- | :--- | :--- |
| `Itoguruma.Core` | `Microsoft.Data.Sqlite` | `8.0.20` | SQLiteによる永続化と監視。 |
| `Itoguruma.Server` | `ModelContextProtocol.AspNetCore` | `1.3.0` | MCP Streamable HTTPサーバー統合。 |
| `Itoguruma.Tests` | `Microsoft.NET.Test.Sdk` | `17.11.1` | テストホスト統合。 |
| `Itoguruma.Tests` | `xunit` | `2.9.2` | テストフレームワーク。 |
| `Itoguruma.Tests` | `xunit.runner.visualstudio` | `2.8.2` | テスト検出・実行アダプター。private assetとして参照。 |

## パッケージソースと更新

パッケージはビルド環境で設定されたNuGetソースから解決します。直接依存のバージョンは、所有するプロジェクトファイルで更新し、restore、build、テストプロジェクト全体のtestを実行します。Release前に生成されたassetsで推移的依存の変更も確認してください。
