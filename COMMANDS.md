# Itoguruma commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

This document is the canonical command and MCP tool reference. `--db` resolves from `ITOGURUMA_DB`, then the default database under the user's Local Application Data directory.

## CLI commands

| Command | Required | Optional | Result |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type` | `--name`, `--session`, `--metadata`, `--db` | Creates or refreshes an agent. |
| `itoguruma agents` | None | `--db` | Lists registered agents. |
| `itoguruma unregister` | `--agent` | `--db` | Removes an unreferenced agent. |
| `itoguruma delete-agent-history` | `--agent` and interactive confirmation | `--dry-run`, `--db` | Previews or deletes history for one exact agent ID. Dry-run needs no confirmation. |
| `itoguruma send` | `--from`, one or more `--to`, `--provider`, `--body`, `--thread` | `--reply-to`, `--message-type`, `--payload-json`, `--idempotency-key`, `--db` | Persists a message and queues deliveries. |
| `itoguruma inbox` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--db` | Leases deliverable messages. |
| `itoguruma ack` | `--agent`, `--message` | `--db` | Acknowledges a leased delivery. |
| `itoguruma history` | `--thread` | `--limit`, `--offset`, `--db` | Returns the conversation history oldest first. |
| `itoguruma inspect-change-request` | `--payload-json` | `--db` | Revalidates a CR file and reports state differences. |
| `itoguruma hook` | `--agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--db` | Converts inbox messages to lifecycle-hook output. |
| `itoguruma auth status` | None | None | Reports token presence without revealing it. |
| `itoguruma auth rotate` | Confirmation | None | Replaces the user token with 32 random bytes. |
| `itoguruma project add <project-id>` | `--inbox-agent` and interactive confirmation | `--display-name`, `--db` | Adds an enabled known project. |
| `itoguruma project update <project-id>` | Interactive confirmation | `--inbox-agent`, `--display-name`, `--db` | Updates a known project. |
| `itoguruma project enable|disable|delete <project-id>` | Interactive confirmation | `--db` | Changes availability or deletes an unreferenced project. |
| `itoguruma project list|show [project-id]` | Project ID for `show` | `--db` | Reads the canonical project registry. |
| `itoguruma version` | None | None | Prints the product version in `x.x.x` or `x.x.x.x` format. |

## MCP tools

| Tool | Required input | State-dependent result |
| :--- | :--- | :--- |
| `register_agent` | `agent_id`, `agent_type`, `project_id` | Creates an agent or refreshes its heartbeat after validating an enabled parent project. |
| `register_project_inbox` | `project_id`, `display_name` | Atomically and idempotently registers or updates a project and its inbox agent. |
| `list_agents` | None | Returns all persisted agents. |
| `list_projects` | None | Returns the canonical destination project registry without mutating it. |
| `unregister_agent` | `agent_id` | Fails while messages reference the agent. |
| `delete_agent_history` | `agent_id`, `dry_run` | Previews or transactionally deletes associated messages and deliveries. |
| `send_message` | sender, provider, body, thread, recipient(s) | Returns the persisted message ID; duplicate sender/idempotency-key pairs return the existing logical message. |
| `get_messages` | `agent_id` | Leases matching pending or expired deliveries and returns none when no match exists. |
| `ack_message` | `agent_id`, `message_id` | Acknowledges only the matching leased delivery. |
| `get_conversation_history` | `thread_id` | Returns the thread oldest first, including acknowledged messages; an unknown thread returns an empty array. |
| `inspect_change_request` | `payload_json` | Revalidates the CR file and reports payload/file state differences. |
| `get_hook_context` | `agent_id` | Leases messages and returns CLI-hook-compatible context and stop state. |
| `get_auth_status` | None | Reports token presence without revealing it. |
| `rotate_auth_token` | `confirmation=ROTATE` | Replaces the user token without returning its value; server and clients must be restarted. |

`get_version` returns the running server name and its product version in `x.x.x` or `x.x.x.x` format.

`provider`/`--provider` is required on every send and identifies the sender runtime, such as `codex` or `claude-code`. It is normalized to lowercase and must contain only ASCII letters, digits, and hyphens. Itoguruma stores the supplied value with the message and returns it through inbox leasing, redelivery, hooks, history, and Viewer. It is routing metadata supplied by an authenticated client, not proof of identity. Messages migrated from schema version 3 or earlier return `provider=unknown` without guessing a historical value.

`message_type` accepts `message`, `notification`, `system`, or `change_request`. A change request requires a registered explicit recipient, a valid payload, and an existing Markdown file under `inbox/<target_project>/` in the configured CR root.

Before runtime registration, use `register_project_inbox` for initial setup or `list_projects` for an existing parent, then pass the canonical ID as `project_id`. `metadata_json.projectId` is not an integrity source.

Before `send`/`send_message`, call `list_projects` and select the canonical destination by ordinal case-insensitive matching. Recipients are Project IDs, not runtime Agent IDs. Each input is normalized with invariant lowercase and must match `^[a-z][a-z0-9]*$`. If a valid Project ID is unknown, the send transactionally registers an enabled project and `project_inbox` Agent using the normalized ID, then delivers the message. Invalid IDs return `ITG_PROJECT_ID_INVALID` with the attempted value and up to five related registered projects. Disabled projects return `ITG_PROJECT_DISABLED`. Explicit project mutations are unavailable through MCP and require a five-digit interactive-console confirmation (60 seconds, three attempts, no redirected input/output or bypass option). Referenced projects return `ITG_PROJECT_REFERENCED` on deletion; use `disable` instead.

Project IDs are stored and returned in invariant lowercase and matched case-insensitively. Agent IDs remain case-sensitive and are used for sender identity and inbox leasing. A Project ID that differs from an existing ID only by case cannot be registered separately.

Before removing a referenced agent, call `delete_agent_history` with `dry_run=true` and review the message, delivery, and thread counts. After explicit approval, call it again with `dry_run=false`, then call `unregister_agent`. The delete includes messages sent by the exact agent ID, replies that depend on those messages, and deliveries addressed to the agent. It runs in one transaction and does not return or log message bodies or payloads. The audit record contains only the agent ID, counts, timestamp, and correlation ID. `ITG_AGENT_NOT_FOUND`, transaction conflict, and database failure are returned separately. The CLI requires the same interactive five-digit confirmation used by project mutations for an actual deletion.

## Examples

```powershell
itoguruma register --agent codex-main --type codex --project itoguruma
itoguruma project list
itoguruma send --from codex-main --to moyai --provider codex --thread review --body "Review requested" --idempotency-key review-1
itoguruma inbox --agent moyai --lease-seconds 300
itoguruma ack --agent moyai --message <messageId>
```
