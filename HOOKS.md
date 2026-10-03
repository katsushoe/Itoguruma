# Itoguruma lifecycle hooks

[English](HOOKS.md) | [日本語](HOOKS.ja.md)

Itoguruma checks a shared inbox during the `SessionStart`, `UserPromptSubmit`, and `Stop` lifecycle events of Codex, and during the `UserPromptSubmit` event of Claude Code. Because checking on every prompt submission does not miss messages, the default Claude Code example omits the redundant `SessionStart` hook and the `Stop` hook, which would repeatedly block turn completion while messages remain unacknowledged. Hooks lease messages but do not acknowledge them automatically.

## Selecting the inbox

Pass exactly one inbox selector to `itoguruma hook`:

| Option | Inbox |
| :--- | :--- |
| `--project-inbox` | The enabled registered project whose ID is the name of the Git repository containing the working directory, in invariant lowercase. The working directory is read from `cwd` in the hook input, or the process directory when absent. Inside a `git worktree`, the main repository name is used. When the directory is outside a repository, the name is not a valid Project ID, or no enabled project is registered, the hook prints nothing and exits with `0`. |
| `--agent <inboxAgentId>` | The specified inbox. |

The generated Claude Code example uses `--project-inbox --consumer-agent claude-code`, so one user-level hook shows each project's own inbox. The context header names `agent_id` and `consumer_agent_id`, which are required with the message `leaseId` for acknowledgement. The MCP tool `get_hook_context` accepts the same selection through `working_directory` or `agent_id`.

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
