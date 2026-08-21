# Itoguruma packages

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

## Direct dependencies

| Project | Package | Version | Purpose |
| :--- | :--- | :--- | :--- |
| `Itoguruma.Core` | `Microsoft.Data.Sqlite` | `8.0.20` | SQLite persistence and monitoring. |
| `Itoguruma.Server` | `ModelContextProtocol.AspNetCore` | `1.3.0` | MCP Streamable HTTP server integration. |
| `Itoguruma.Tests` | `Microsoft.NET.Test.Sdk` | `17.11.1` | Test host integration. |
| `Itoguruma.Tests` | `xunit` | `2.9.2` | Test framework. |
| `Itoguruma.Tests` | `xunit.runner.visualstudio` | `2.8.2` | Test discovery and execution adapter; private asset. |
| `Itoguruma.Installer` | `WixToolset.Sdk` | `7.0.0` | x64 MSI build. |
| `Itoguruma.Installer` | `WixToolset.UI.wixext` | `7.0.0` | MSI language selection UI. |
| `Itoguruma.Installer` | `WixToolset.Util.wixext` | `7.0.0` | Logged MSI custom-action execution. |

## Sources and updates

Packages resolve from the NuGet sources configured for the build environment. Update direct versions in the owning project file, restore, build, and run the complete test project. Review transitive changes in the generated assets before release.
