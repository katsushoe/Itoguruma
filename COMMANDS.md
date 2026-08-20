# Itoguruma commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

This document is the canonical command and MCP tool reference. `--db` resolves from `ITOGURUMA_DB`, then the default database under the user's Local Application Data directory.

## CLI commands

| Command | Required | Optional | Result |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type` | `--name`, `--session`, `--metadata`, `--db` | Creates or refreshes an agent. |
| `itoguruma agents` | None | `--db` | Lists registered agents. |
| `itoguruma unregister` | `--agent` | `--db` | Removes an unreferenced agent. |
| `itoguruma send` | `--from`, `--to`, `--body`, `--thread` | `--reply-to`, `--message-type`, `--payload-json`, `--idempotency-key`, `--db` | Persists a message and queues deliveries. |
| `itoguruma inbox` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--db` | Leases deliverable messages. |
| `itoguruma ack` | `--agent`, `--message` | `--db` | Acknowledges a leased delivery. |
| `itoguruma hook` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--db` | Converts inbox messages to lifecycle-hook output. |
| `itoguruma auth status` | None | None | Reports token presence without revealing it. |
| `itoguruma auth rotate` | Confirmation | None | Replaces the user token with 32 random bytes. |
| `itoguruma version` | None | None | Prints the product version. |

## MCP tools

| Tool | Required input | State-dependent result |
| :--- | :--- | :--- |
| `register_agent` | `agent_id`, `agent_type` | Creates an agent or refreshes its heartbeat. |
| `list_agents` | None | Returns all persisted agents. |
| `unregister_agent` | `agent_id` | Fails while messages reference the agent. |
| `send_message` | sender, body, thread, recipient(s) | Returns the persisted message ID; duplicate sender/idempotency-key pairs return the existing logical message. |
| `get_messages` | `agent_id` | Leases matching pending or expired deliveries and returns none when no match exists. |
| `ack_message` | `agent_id`, `message_id` | Acknowledges only the matching leased delivery. |
| `get_conversation_history` | `thread_id` | Returns the thread oldest first, including acknowledged messages; an unknown thread returns an empty array. |
| `inspect_change_request` | `payload_json` | Revalidates the CR file and reports payload/file state differences. |

`message_type` accepts `message`, `notification`, `system`, or `change_request`. A change request requires a registered explicit recipient, a valid payload, and an existing Markdown file under `inbox/<target_project>/` in the configured CR root.

## Examples

```powershell
itoguruma register --agent codex-main --type codex
itoguruma send --from codex-main --to claude-main --thread review --body "Review requested" --idempotency-key review-1
itoguruma inbox --agent claude-main --lease-seconds 300
itoguruma ack --agent claude-main --message <messageId>
```
