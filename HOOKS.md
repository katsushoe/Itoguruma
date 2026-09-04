# Itoguruma lifecycle hooks

[English](HOOKS.md) | [日本語](HOOKS.ja.md)

Itoguruma checks a shared inbox during the `SessionStart`, `UserPromptSubmit`, and `Stop` lifecycle events of Codex and Claude Code. Hooks lease messages but do not acknowledge them automatically.

## Generated examples

The installer writes `examples/codex-hooks.json` and `examples/claude-settings.json` below the installation directory. Merge the relevant event entries into an existing client configuration without overwriting unrelated hooks.

## Client behavior

| Event | Behavior |
| :--- | :--- |
| `SessionStart` | Adds newly leased inbox messages to the session context. |
| `UserPromptSubmit` | Checks the inbox when the user submits a prompt. |
| `Stop` | Returns exit code `2` when a new message requires the agent to continue. |

Hooks do not interrupt an idle client or start a new turn. Messages remain in SQLite while a client is stopped. After processing a message, acknowledge it with `ack_message` or:

```powershell
itoguruma ack --agent <inboxAgentId> --consumer-agent <consumerAgentId> --message <messageId> --lease-id <leaseId>
```

## Verification

Register the receiving agent, send a test message, and trigger a configured lifecycle event. Validate edited configuration files with a JSON parser. For MCP registration and troubleshooting, see [MCP_SETUP.md](MCP_SETUP.md).
