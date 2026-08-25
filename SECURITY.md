# Security policy

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## Supported deployment

Itoguruma is designed for a single Windows user and a loopback-only HTTP endpoint. Do not expose the MCP endpoint to a network interface or share its SQLite database with untrusted users.

## Secrets and authentication

Every MCP request requires the bearer token in `ITOGURUMA_AUTH_TOKEN`. Do not print, commit, log, or place the token in shared documents. Use `itoguruma auth status` to inspect presence and `itoguruma auth rotate` after suspected disclosure. The old token stops working immediately; restart and reconfigure all clients.

## Data and change requests

Messages can contain sensitive text and remain in SQLite after acknowledgement. Protect the database with operating-system access controls. Change-request paths are restricted to existing Markdown files beneath the configured `inbox/<target_project>/` directory; validation failure never falls back to an ordinary message.

The SQLite project registry is a routing trust boundary. MCP has no project mutation tools. CLI mutations require re-entry of a random five-digit code through non-redirected console input within 60 seconds, with three attempts. Codes and entered values are not logged. This blocks non-interactive automation and AI systems without terminal-input control; subjects able to control terminal input are outside the guarantee.

## Reporting vulnerabilities

Report vulnerabilities privately to the repository owner. Do not include tokens, message databases, customer data, or exploit details in a public issue.
