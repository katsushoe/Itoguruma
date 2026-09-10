# Itoguruma MCP setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Server

Install Itoguruma and start the loopback server. The installer stores its bearer token in Windows Credential Manager and registers the local stdio proxy. The Streamable HTTP endpoint remains `http://127.0.0.1:47631/mcp`.

## Codex

```powershell
codex mcp add itoguruma -- "C:\Itoguruma\bin\mcp-proxy\<version>\Itoguruma.McpProxy.exe" --url "http://127.0.0.1:47631/mcp"
```

The installer generates `examples/codex-hooks.json`. Merge its lifecycle entries into the user or project `hooks.json`; do not overwrite unrelated hooks.

## Claude Code

```powershell
claude mcp add --transport stdio --scope user itoguruma -- "C:\Itoguruma\bin\mcp-proxy\<version>\Itoguruma.McpProxy.exe" --url "http://127.0.0.1:47631/mcp"
```

Merge `examples/claude-settings.json` into the target project's existing settings.

## Connection check

Register each client with `register_agent`, send a message, lease it with `get_messages` using the inbox and consumer Agent IDs, and acknowledge it with `ack_message` using the returned `lease_id`. Use the same database, server URL, and token in every client.

## Troubleshooting

- Authentication failure: confirm token presence without printing its value, then restart clients after rotation.
- Empty inbox: verify that sender and recipient use the same database and that the recipient is registered.
- Repeated delivery: acknowledge processed messages before the lease expires.
- Hook errors: validate merged JSON with a JSON parser.
