# Itoguruma MCP setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Server

Install Itoguruma, set `ITOGURUMA_AUTH_TOKEN`, and start the loopback server. The MCP endpoint is `http://127.0.0.1:47631/mcp` by default.

## Codex

```powershell
codex mcp add itoguruma --url "http://127.0.0.1:47631/mcp" --bearer-token-env-var ITOGURUMA_AUTH_TOKEN
```

The installer generates `examples/codex-hooks.json`. Merge its lifecycle entries into the user or project `hooks.json`; do not overwrite unrelated hooks.

## Claude Code

```powershell
claude mcp add --transport http --scope user --header 'Authorization: Bearer ${ITOGURUMA_AUTH_TOKEN}' itoguruma "http://127.0.0.1:47631/mcp"
```

Merge `examples/claude-settings.json` into the target project's existing settings.

## Connection check

Register each client with `register_agent`, send a message, lease it with `get_messages`, and acknowledge it with `ack_message`. Use the same database, server URL, and token in every client.

## Troubleshooting

- Authentication failure: confirm token presence without printing its value, then restart clients after rotation.
- Empty inbox: verify that sender and recipient use the same database and that the recipient is registered.
- Repeated delivery: acknowledge processed messages before the lease expires.
- Hook errors: validate merged JSON with a JSON parser.
