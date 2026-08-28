# Security policy

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## Supported deployment

Itoguruma is designed for a single Windows user and a loopback-only HTTP endpoint. Do not expose the MCP endpoint to a network interface or share its SQLite database with untrusted users.

## Secrets and authentication

Every MCP request requires the bearer token in `ITOGURUMA_AUTH_TOKEN`. Do not print, commit, log, or place the token in shared documents. Use `itoguruma auth status` to inspect presence and `itoguruma auth rotate` after suspected disclosure. The old token stops working immediately; restart and reconfigure all clients.

## Data and change requests

Messages can contain sensitive text and remain in SQLite after acknowledgement. Protect the database with operating-system access controls. Change-request paths are restricted to existing Markdown files beneath the configured `inbox/<target_project>/` directory; validation failure never falls back to an ordinary message.

Agent-history deletion is destructive and restricted to an exact, case-sensitive agent ID. Always review a dry-run before an explicitly approved deletion. The operation is authenticated like every MCP request, executes in one SQLite transaction, returns only counts and a correlation ID, and never returns or logs message bodies or payloads. CLI deletion additionally requires interactive five-digit confirmation.

The SQLite project registry records routing destinations. An authenticated send to an unknown recipient automatically registers an enabled project and inbox using that recipient ID, so project presence is not an authorization boundary. MCP has no general project mutation tools. Explicit CLI mutations require re-entry of a random five-digit code through non-redirected console input within 60 seconds, with three attempts. Codes and entered values are not logged.

## Reporting vulnerabilities

Report vulnerabilities privately to the repository owner. Do not include tokens, message databases, customer data, or exploit details in a public issue.
