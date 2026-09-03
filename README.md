# Itoguruma

[English](README.md) | [日本語](README.ja.md)

Itoguruma is a persistent MCP Streamable HTTP server that lets independent AI agents, including Codex and Claude Code, exchange messages through a shared SQLite database.

## Getting Started

Install the latest release MSI, restart your terminal and AI clients, and verify a round trip:

```powershell
msiexec /i .\Itoguruma-x.x.x-win-x64.msi
itoguruma register --agent codex-main --type codex --project itoguruma
itoguruma register --agent claude-main --type claude-code --project itoguruma
itoguruma send --from codex-main --to claude-main --provider codex --thread setup --body "Hello" --idempotency-key setup-1
itoguruma inbox --agent claude-main --lease-seconds 300
```

## Installation

GitHub Releases provide the recommended x64 MSI, `Install-Itoguruma.ps1`, and a self-contained ZIP. The MSI prompts for English or Japanese and supports silent installation with `ITOGURUMA_LANGUAGE=en` or `ITOGURUMA_LANGUAGE=ja`. Installers require no .NET SDK. Source builds require the .NET 8 SDK:

```powershell
dotnet restore tests/Itoguruma.Tests/Itoguruma.Tests.csproj
dotnet build tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-restore
dotnet test tests/Itoguruma.Tests/Itoguruma.Tests.csproj -c Release --no-build
```

## Configuration

The server requires `ITOGURUMA_AUTH_TOKEN`; it also supports `ITOGURUMA_URL`, `ITOGURUMA_DB`, `ITOGURUMA_CONFIG_DIR`, `ITOGURUMA_LOG_DIR`, and `ITOGURUMA_CR_ROOT`. See [CONFIG.md](CONFIG.md).

## Usage

Messages move through `pending`, `leased`, and `acked` delivery states. A lease that expires before acknowledgement becomes deliverable again. Reuse the same `idempotency_key` when retrying the same logical send. See [COMMANDS.md](COMMANDS.md).

Every sender supplies its runtime using the required `provider`/`--provider` field. Itoguruma stores that value on the message, so lease redelivery and conversation history retain the send-time value without coupling Provider identity to agent registration.

Formal change requests use a validated Markdown file under the configured shared CR root and a `change_request` message containing its path and index metadata. Invalid requests never fall back to ordinary messages.

## Documentation

- [Configuration](CONFIG.md)
- [Commands and MCP tools](COMMANDS.md)
- [MCP client setup](MCP_SETUP.md)
- [Lifecycle hook setup](HOOKS.md)
- [Package dependencies](PACKAGES.md)
- [Security](SECURITY.md)
- [Architecture decisions](docs/adr/0001-streamable-http-server.md)
- [Message provider attribution decision](docs/adr/0004-message-provider-attribution.md)

## Security

Keep the bearer token secret, bind the service to loopback, and rotate a compromised token with `itoguruma auth rotate`. See [SECURITY.md](SECURITY.md).

## License

No license is currently granted for reuse or redistribution beyond the downloadable release terms provided by the repository owner.
