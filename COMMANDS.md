# Itoguruma commands

[English](COMMANDS.md) | [日本語](COMMANDS.ja.md)

This document is the canonical command and MCP tool reference. Business commands connect to the running Itoguruma server and never open its database directly. Configure `Itoguruma:ServerUrl` in the shared `config/appsettings.json`, set `Itoguruma__ServerUrl`, or override it per invocation with `--url <server-url>`. The endpoint must be an HTTP loopback origin or its `/mcp` endpoint. Calls use the Windows Credential Manager token and diagnostic generation ID; neither value is printed.

## CLI commands

| Command | Required | Optional | Result |
| :--- | :--- | :--- | :--- |
| `itoguruma register` | `--agent`, `--type`, `--project` | `--name`, `--session`, `--metadata`, `--url` | Creates or refreshes an agent through the server. |
| `itoguruma agents` | None | `--url` | Lists registered agents through the server. |
| `itoguruma unregister` | `--agent` | `--url` | Removes an unreferenced agent through the server. |
| `itoguruma delete-agent-history` | `--agent` and interactive confirmation | `--dry-run`, `--url` | Previews or deletes history for one exact agent ID. Dry-run needs no confirmation. |
| `itoguruma send` | `--from`, one or more `--to`, `--provider`, `--body`, `--thread` | `--reply-to`, `--message-type`, `--payload-json`, `--idempotency-key`, `--url` | Persists a message and queues deliveries through the server. |
| `itoguruma inbox` | `--agent`, `--consumer-agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--url` | Leases deliverable messages through the server. |
| `itoguruma ack` | `--agent`, `--consumer-agent`, `--message`, `--lease-id` | `--url` | Acknowledges a leased delivery through the server. |
| `itoguruma history` | `--thread` | `--limit`, `--offset`, `--url` | Returns the conversation history oldest first. |
| `itoguruma inspect-change-request` | `--payload-json` | `--url` | Revalidates a CR file and reports state differences through the server. |
| `itoguruma hook` | Exactly one of `--agent` or `--project-inbox`, and `--consumer-agent` | `--limit`, `--lease-seconds`, `--thread`, `--message-type`, `--url` | Converts inbox messages to lifecycle-hook output through the server. `--project-inbox` uses the inbox of the registered project for the Git repository of the input `cwd` (see [HOOKS.md](HOOKS.md)). |
| `itoguruma auth status` | None | None | Reports credential presence and its diagnostic generation ID without revealing the token. |
| `itoguruma auth rotate` | Confirmation | None | Replaces the user token with 32 random bytes. |
| `itoguruma project add <project-id>` | `--inbox-agent` and interactive confirmation | `--display-name`, `--url` | Adds an enabled known project through the server. |
| `itoguruma project update <project-id>` | Interactive confirmation | `--inbox-agent`, `--display-name`, `--url` | Updates a known project through the server. |
| `itoguruma project enable|disable|delete <project-id>` | Interactive confirmation | `--url` | Changes availability or deletes an unreferenced project through the server. |
| `itoguruma project list|show [project-id]` | Project ID for `show` | `--url` | Reads the canonical project registry through the server. |
| `itoguruma version` | None | None | Prints the product version in `x.x.x` or `x.x.x.x` format. |

## CLI operation details

Every business command below first resolves the loopback `/mcp` endpoint and reads the token from Windows Credential Manager. Successful business commands print the MCP `data` value as indented JSON and exit `0`; local, transport, and server errors exit `2` and go to standard error. `--url` overrides the endpoint for the current invocation. Required string options must have a value. Numeric `--limit`, `--offset`, and `--lease-seconds` values that cannot be parsed fall back to the documented default. The server range-checks `--limit`, but does not range-check `--offset` or `--lease-seconds`; see their operation notes. The CLI does not offer a database-file override.

### `register`

Syntax: `itoguruma register --agent <id> --type <type> --project <project-id> [--name <name>] [--session <id>] [--metadata <json>] [--url <url>]`.

Registers or refreshes one runtime agent and its heartbeat under an enabled registered project. `--agent`, `--type`, and `--project` are required; `--metadata` must be valid JSON when supplied. Example: `itoguruma register --agent codex-main --type codex --project itoguruma`.

Success returns the agent record, including `agentId`, `agentType`, `projectId`, timestamps, and optional session/metadata. Example: `{"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"}`. Unknown/disabled projects, invalid metadata, and conflicting registrations are structured server errors; select the canonical parent with `list_projects` first.

### `agents`

Syntax: `itoguruma agents [--url <url>]`. Reads registered agents without changing state. Example: `itoguruma agents`.

Success returns an array of agent records; an empty registry returns `[]`. Example: `[ {"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"} ]`. Connection and server errors use the common CLI error behavior above.

### `unregister`

Syntax: `itoguruma unregister --agent <id> [--url <url>]`. Removes only the agent registration; `--agent` is an exact, case-sensitive Agent ID.

Success returns `{"unregistered":true}` (false if the service reports no removal). Example: `itoguruma unregister --agent temporary-agent`. Existing messages or deliveries can prevent removal (`agent_referenced`); use `delete-agent-history` only after reviewing its dry-run counts.

### `delete-agent-history`

Syntax: `itoguruma delete-agent-history --agent <id> [--dry-run] [--url <url>]`. IDs are exact and case-sensitive. Dry-run is non-destructive and does not request confirmation; an actual deletion requires a five-digit code typed into an interactive, non-redirected console within 60 seconds (up to three attempts).

First run `itoguruma delete-agent-history --agent old-agent --dry-run`; review `messageCount`, `deliveryCount`, `threadCount`, and `canUnregister`. After explicit approval, repeat without `--dry-run`. Success returns those counts and a `correlationId`; message bodies and payloads are never returned. Example: `{"agentId":"old-agent","dryRun":true,"messageCount":3,"deliveryCount":2,"threadCount":1,"canUnregister":true,"correlationId":"…"}`. Missing agents, expired/incorrect confirmation, redirected console, database conflicts, and database failures return separate error codes. After deletion, unregister the agent separately.

### `send`

Syntax: `itoguruma send --from <agent-id> --to <project-id> [--to <project-id> ...] --provider <runtime> --body <text> --thread <id> [--reply-to <message-id>] [--message-type message|notification|system|change_request] [--payload-json <json>] [--idempotency-key <key>] [--url <url>]`.

`--from`, at least one `--to`, `--provider`, `--body`, and `--thread` are required. Each `--to` is a Project ID, not a runtime Agent ID. Call `list_projects` and use the canonical ID. Provider accepts ASCII letters, digits, and hyphens and is stored lowercase. For retries of the same logical send, reuse the same idempotency key. A change request requires a valid CR payload and canonical file; it is never downgraded to a normal message.

Success returns `{"messageId":"<id>"}`; replaying the same sender/key returns the existing logical message. Example: `itoguruma send --from codex-main --to moyai --provider codex --body "Review requested" --thread review --idempotency-key review-1`. Errors include missing sender/reply references, invalid provider or message type, malformed JSON/CR, invalid or disabled destination, transaction conflict, and database failure. Use the same key only when retrying the same content and intent.

### `inbox`

Syntax: `itoguruma inbox --agent <inbox-agent-id> --consumer-agent <consumer-id> [--limit <1..500>] [--lease-seconds <seconds>] [--thread <id>] [--message-type <type>] [--url <url>]`.

`--agent` identifies the inbox; `--consumer-agent` records the agent that owns each lease. Defaults: limit `50`, lease duration `300` seconds. Optional thread/type filters narrow the pending or expired deliveries. A successful lease changes delivery state and increments its attempt count.

Success returns an array of message and delivery records, including `messageId`, `body`, `provider`, `deliveryStatus`, `leaseUntil`, `leaseId`, `leaseOwnerAgentId`, and `deliveryAttemptCount`; no matches return `[]`. Example: `[{"messageId":"m-1","deliveryStatus":"leased","leaseId":"lease-1","leaseOwnerAgentId":"codex-root","deliveryAttemptCount":1}]`. Limits outside 1–500 are rejected by the server. `lease_seconds` is not range-checked; use a positive value to avoid immediately expiring leases. Treat `leaseId` as required input to the matching ACK and do not acknowledge work before handling it.

### `ack`

Syntax: `itoguruma ack --agent <inbox-agent-id> --consumer-agent <consumer-id> --message <message-id> --lease-id <lease-id> [--url <url>]`.

All four identifiers are required. ACK succeeds only for the delivery leased to that exact consumer and lease ID; it is safe to retry the same ACK.

Success returns `{"acked":true}` (false means the lease no longer matches). Example: `itoguruma ack --agent moyai --consumer-agent moyai-codex-root --message m-1 --lease-id lease-1`. A stale, expired, or mismatched lease is not acknowledged; re-fetch with `inbox` before processing a redelivery.

### `history`

Syntax: `itoguruma history --thread <id> [--limit <1..500>] [--offset <0 or greater>] [--url <url>]`. Defaults: limit `100`, offset `0`.

Returns message records ordered oldest first, including acknowledged messages. Unknown threads return `[]`. Example: `itoguruma history --thread review --limit 20`. Each record includes message/thread/sender IDs, provider, type, body, payload, reply ID, and creation time. The CLI output can therefore contain message content; share it only with intended recipients. The server enforces `limit` 1–500. Use a non-negative `offset`; the current implementation does not reject negative offsets.

### `inspect-change-request`

Syntax: `itoguruma inspect-change-request --payload-json <json> [--url <url>]`. The JSON must be a valid formal change-request payload whose file path points to the canonical CR file beneath the configured target-project inbox.

This operation re-reads and validates the file and compares its current fields/status with the payload; it does not send or edit the CR. Success returns the path, source/target project, priority, payload/file status, and whether they match. Example: `{"statusMatches":true,"payloadStatus":"未着手","fileStatus":"未着手"}`. Malformed payload, missing/invalid file, state drift, or an unconfigured CR root returns a structured error. Do not put secrets in the payload.

### `hook`

Syntax: `itoguruma hook (--agent <inbox-id>|--project-inbox) --consumer-agent <consumer-id> [--limit <1..500>] [--lease-seconds <seconds>] [--thread <id>] [--message-type <type>] [--url <url>]`. Exactly one inbox selector is required. Input is JSON read from standard input; `--project-inbox` resolves the registered project from the input `cwd` Git repository name.

The command leases matching messages and formats hook context. No matching message returns no output and exit `0`; ordinary context is written to standard output and exits `0`. A `Stop` event with messages sets stop state, writes context to standard error, and exits `2`. Example input: `{"hook_event_name":"UserPromptSubmit","cwd":"C:\\work\\my-project"}`. Example result: `Messages for my-project: ...`. Invalid JSON is treated as absent optional fields; missing selectors, authentication/transport failure, and server validation errors return nonzero. A lease is created, so the consuming Hook must process and ACK each message using its lease ID.

### `auth status`

Syntax: `itoguruma auth status`. This local operation reads the current user's Windows Credential Manager entry; it does not contact the server.

Success prints whether a credential is configured and, when present, its diagnostic generation ID. Example: `Authentication token: configured.` followed by `Generation ID: <id>`. A missing credential prints `Authentication token: not configured.`. The token and any token-derived value are never printed.

### `auth rotate`

Syntax: `itoguruma auth rotate`. This local operation displays the impact and requires the exact text `ROTATE`; any other response cancels with exit `1`. It generates a new 32-byte random token and generation ID, replacing the user's credential. Example success: `Authentication token rotated. Generation ID: <id>. The token value is not displayed.`

Rotation immediately invalidates the old token. Restart the `ItogurumaServer` scheduled task, Codex, Claude Code, and any client that stores the token directly. Credential-store failure exits `2`; never copy a token into command arguments, environment variables, or client files.

### `configure codex` and `configure claude`

Syntax: `itoguruma configure codex|claude [--url <server-url>] [--proxy-path <path>]`. `--url` defaults to the configured loopback server; `--proxy-path` defaults to the version-matched installed proxy. The proxy executable must exist.

Creates or updates the user's MCP entry and Itoguruma-owned Hook handlers while preserving unrelated settings. Success prints the client and number of files changed, for example `configure codex: updated 2 user configuration file(s).` Invalid client/options, missing proxy, invalid JSON, or failed atomic replacement exits `2`; prior file contents are restored after a partial update failure. No token is written to these files. Restart the target client after configuration.

### `unconfigure codex` and `unconfigure claude`

Syntax: `itoguruma unconfigure codex|claude`. No URL or proxy path is required.

Removes only the Itoguruma MCP entry and Itoguruma Hook handlers from the current user's files. Example result: `unconfigure claude: updated 2 user configuration file(s).` Unrelated servers, hooks, and settings are preserved. Invalid client, invalid JSON, or replacement failure exits `2`; failed multi-file changes are rolled back. Restart the client to apply removal.

### `project list`

Syntax: `itoguruma project list [--url <url>]`. Reads the canonical project registry without mutation. Example: `itoguruma project list`.

Returns project records (`projectId`, `displayName`, `inboxAgentId`, `enabled`, timestamps); empty registry returns `[]`. Use this result to select canonical recipient IDs before sending.

### `project show`

Syntax: `itoguruma project show <project-id> [--url <url>]`. Reads one case-insensitively matched project. Success returns its project record. Unknown IDs return `ITG_PROJECT_UNKNOWN`; use `project list` to choose an existing ID.

### `project add`

Syntax: `itoguruma project add <project-id> --inbox-agent <agent-id> [--display-name <name>] [--url <url>]`. Requires five-digit interactive confirmation before the request.

Adds an enabled project and its inbox mapping; omitted display name defaults to the normalized project ID. Success returns the project record. Example: `itoguruma project add moyai --inbox-agent moyai-inbox --display-name Moyai`. Project IDs normalize to lowercase and must match `^[a-z][a-z0-9_]*$`. Confirmation requires a real, non-redirected console, entry within 60 seconds, and at most three attempts. Invalid/case-conflicting ID, duplicate or conflicting agent, expired/failed confirmation, and server failures are reported without changing the registry.

### `project update`

Syntax: `itoguruma project update <project-id> [--inbox-agent <agent-id>] [--display-name <name>] [--url <url>]`. Requires interactive confirmation. Provide at least one field to change.

Success returns the updated project record. Example: `itoguruma project update moyai --display-name "Moyai Project"`. Unknown IDs, invalid fields, conflicting inbox agents, or confirmation errors leave the project unchanged. Review the ID and proposed fields before confirming.

### `project enable` and `project disable`

Syntax: `itoguruma project enable|disable <project-id> [--url <url>]`. Both require interactive confirmation. Enable makes the project eligible for delivery; disable prevents new delivery while retaining history and the inbox registration.

Success returns the updated project record with `enabled=true|false`. Example: `itoguruma project disable moyai`. Unknown IDs and confirmation errors do not change state. Prefer disable when message history still references a project.

### `project delete`

Syntax: `itoguruma project delete <project-id> [--url <url>]`. Requires interactive confirmation and only succeeds when retained messages/deliveries do not reference the project.

Success returns `{"deleted":true}`. Referenced projects return `ITG_PROJECT_REFERENCED`; disable them instead. Confirmation or validation failures leave the registry unchanged. Deletion removes the project and inbox registration, not unrelated agent history.

### `version` and help

Syntax: `itoguruma version` or `itoguruma --help`. Version prints `itoguruma <version>` and exits `0`. Help prints command families and endpoint guidance and exits `0`; `itoguruma configure --help`, `itoguruma unconfigure --help`, and `itoguruma auth --help` print their respective synopsis. Unknown commands/options and malformed local configuration exit `2`.

## Examples

```powershell
itoguruma register --agent codex-main --type codex --project itoguruma
itoguruma project list
itoguruma send --from codex-main --to moyai --provider codex --thread review --body "Review requested" --idempotency-key review-1
itoguruma inbox --agent moyai --consumer-agent moyai-codex-root --lease-seconds 300
itoguruma ack --agent moyai --consumer-agent moyai-codex-root --message <messageId> --lease-id <leaseId>
```
## Installer options

Synopsis: `powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 [-Version <version>] [-PackagePath <zip>] [-InstallDirectory <path>] [-ConfigDirectory <path>] [-LogDirectory <path>] [-Language en|ja] [-ServerUrl <url>] [-ServerStopTimeoutSeconds <5..120>] [-NoPath] [-SkipCodex] [-SkipClaude]`.

| Parameter | Default | Behavior and constraints |
| :--- | :--- | :--- |
| `-Version` | `latest` | Downloads the selected release version. |
| `-PackagePath` | Empty | Uses a local ZIP instead of downloading when provided. |
| `-InstallDirectory` | `C:\Itoguruma` | Installation root for application files. |
| `-ConfigDirectory` | `<InstallDirectory>\config` | Server configuration directory. |
| `-LogDirectory` | `<InstallDirectory>\logs` | Daily server log directory. |
| `-Language` | Empty | Prompts interactively; accepted values are `en` and `ja`. MSI defaults its public `ITOGURUMA_LANGUAGE` property to `en`. |
| `-ServerUrl` | `http://127.0.0.1:47631` | HTTP loopback origin with no path. The generated client URL adds `/mcp`. |
| `-ServerStopTimeoutSeconds` | `30` | Integer from 5 through 120 seconds. |
| `-NoPath` | Off | Does not add CLI/helper directories to the current user's `PATH`. |
| `-SkipCodex` | Off | Skips Codex MCP/Hook configuration. |
| `-SkipClaude` | Off | Skips Claude Code MCP/Hook configuration. |

Example: `powershell -ExecutionPolicy Bypass -File .\Install-Itoguruma.ps1 -PackagePath .\Itoguruma.zip -InstallDirectory C:\Itoguruma -ConfigDirectory D:\Itoguruma\config -LogDirectory D:\Itoguruma\logs -Language ja -SkipClaude`.

The installer creates the appsettings file and scheduled Server task, initializes a credential only when missing, and runs the client configuration command for detected clients unless skipped. It removes obsolete inherited user-level Itoguruma environment values before starting the Server. Installation changes machine/user state; use the uninstaller to remove managed files and optionally remove client entries.

## Configure client integrations

Run itoguruma configure codex or itoguruma configure claude to write user-level MCP and Hook settings. Codex MCP settings are stored in %USERPROFILE%\.codex\config.toml and Hooks in %USERPROFILE%\.codex\hooks.json. Claude Code MCP settings are stored in %USERPROFILE%\.claude.json and Hooks in %USERPROFILE%\.claude\settings.json. The MCP entry is named itoguruma. Configure can be repeated; it replaces the Itoguruma entry and merges its lifecycle Hooks while preserving unrelated servers, Hooks, and settings.

Pass `--url` to override the server URL for this operation; otherwise the CLI resolves shared `Itoguruma:ServerUrl` and normalizes it to `/mcp`. Pass `--proxy-path` to override the installed proxy path. Neither option embeds an authentication token in client settings.

Run `itoguruma unconfigure codex` or `itoguruma unconfigure claude` to remove only the `itoguruma` MCP entry and Itoguruma Hook commands for that client. No proxy path or server URL is required. Each file is written using a same-directory temporary file and atomic replacement. If an update fails, prior file contents are restored. Invalid JSON or failed replacements return a nonzero exit code.

Restart the client after configuration. Codex may ask you to review and trust new non-managed Hooks. The installer invokes `configure` after detecting each client; the uninstaller invokes `unconfigure` before removing the CLI. When the CLI is missing, the uninstaller leaves user settings unchanged.
## MCP tools

`get_auth_status` returns whether the user credential is configured and its non-secret diagnostic generation ID. The generation ID is not used for authentication.

| Tool | Required input | State-dependent result |
| :--- | :--- | :--- |
| `register_agent` | `agent_id`, `agent_type`, `project_id` | Creates an agent or refreshes its heartbeat after validating an enabled parent project. |
| `register_project_inbox` | `project_id`, `display_name` | Atomically and idempotently registers or updates a project and its inbox agent. |
| `list_agents` | None | Returns all persisted agents. |
| `list_projects` | None | Returns the canonical destination project registry without mutating it. |
| `get_project` | `project_id` | Returns one registered project or a structured unknown-project error. |
| `add_project` | `project_id`, `inbox_agent_id` | Adds a project and its inbox through the shared service. |
| `update_project` | `project_id` | Updates the display name and/or inbox agent. |
| `set_project_enabled` | `project_id`, `enabled` | Enables or disables a registered project. |
| `delete_project` | `project_id` | Deletes an unreferenced project and returns `deleted`. |
| `unregister_agent` | `agent_id` | Fails while messages reference the agent. |
| `delete_agent_history` | `agent_id`, `dry_run` | Previews or transactionally deletes associated messages and deliveries. |
| `send_message` | sender, provider, body, thread, recipient(s) | Returns the persisted message ID; duplicate sender/idempotency-key pairs return the existing logical message. |
| `get_messages` | `agent_id`, `consumer_agent_id` | Leases matching pending or expired deliveries, records the consumer, and returns a per-lease `lease_id`. |
| `ack_message` | `agent_id`, `consumer_agent_id`, `message_id`, `lease_id` | Acknowledges only the delivery leased by the matching consumer and lease ID. |
| `get_conversation_history` | `thread_id` | Returns the thread oldest first, including acknowledged messages; an unknown thread returns an empty array. |
| `inspect_change_request` | `payload_json` | Revalidates the CR file and reports payload/file state differences. |
| `get_hook_context` | `consumer_agent_id` and exactly one of `agent_id` or `working_directory` | Leases messages for the consumer and returns CLI-hook-compatible context, the resolved `agent_id`, and stop state. `working_directory` selects the inbox by the same rule as CLI `--project-inbox`. |
| `get_auth_status` | None | Reports credential presence and its diagnostic generation ID without revealing the token. |
| `rotate_auth_token` | `confirmation=ROTATE` | Replaces the user token without returning its value; server and clients must be restarted. |

`get_version` returns the running server name and its product version in `x.x.x` or `x.x.x.x` format.

Project management MCP tools mirror the CLI registry operations. CLI mutations retain their interactive five-digit confirmation. MCP callers are authenticated by the local server token and should treat `delete_project` as destructive.

`provider`/`--provider` is required on every send and identifies the sender runtime, such as `codex` or `claude-code`. It is normalized to lowercase and must contain only ASCII letters, digits, and hyphens. Itoguruma stores the supplied value with the message and returns it through inbox leasing, redelivery, hooks, history, and Viewer. It is routing metadata supplied by an authenticated client, not proof of identity. Messages migrated from schema version 3 or earlier return `provider=unknown` without guessing a historical value.

`message_type` accepts `message`, `notification`, `system`, or `change_request`. A change request requires a registered Project as its explicit recipient, a valid payload, and an existing Markdown file under `inbox/<target_project>/` in the configured CR root. The message is delivered to the single Project Inbox Agent configured for that Project.

Before runtime registration, use `register_project_inbox` for initial setup or `list_projects` for an existing parent, then pass the canonical ID as `project_id`. `metadata_json.projectId` is not an integrity source.

Before `send`/`send_message`, call `list_projects` and select the canonical destination by ordinal case-insensitive matching. Recipients are Project IDs, not runtime Agent IDs. Each input is normalized with invariant lowercase and must match `^[a-z][a-z0-9_]*$`. If a valid Project ID is unknown, the send transactionally registers an enabled project and `project_inbox` Agent using the normalized ID, then delivers the message. Invalid IDs return `ITG_PROJECT_ID_INVALID` with the attempted value and up to five related registered projects. Disabled projects return `ITG_PROJECT_DISABLED`. CLI project mutations require a five-digit interactive-console confirmation (60 seconds, three attempts, no redirected input/output or bypass option); MCP mutations use the authenticated loopback server. Referenced projects return `ITG_PROJECT_REFERENCED` on deletion; use `disable` instead.

Project IDs are stored and returned in invariant lowercase and matched case-insensitively. Agent IDs remain case-sensitive and are used for sender identity and inbox leasing. A Project ID that differs from an existing ID only by case cannot be registered separately.

Before removing a referenced agent, call `delete_agent_history` with `dry_run=true` and review the message, delivery, and thread counts. After explicit approval, call it again with `dry_run=false`, then call `unregister_agent`. The delete includes messages sent by the exact agent ID, replies that depend on those messages, and deliveries addressed to the agent. It runs in one transaction and does not return or log message bodies or payloads. The audit record contains only the agent ID, counts, timestamp, and correlation ID. `ITG_AGENT_NOT_FOUND`, transaction conflict, and database failure are returned separately. The CLI requires the same interactive five-digit confirmation used by project mutations for an actual deletion.

## MCP tool operation details

MCP calls return a wrapper `{ "data": ... }`; errors set `isError=true` and return structured error data with `errorCode`, `category`, `summary`, `suggestedAction`, and `retryable` when available. Examples below show the `data` value. Authentication uses the local bearer credential; the generation ID is diagnostic metadata only. Destructive tools must be explicitly approved by the calling workflow.

### `get_version`

Input: none. Read-only; does not change state. Returns running server identity and version, e.g. `{ "name":"itoguruma", "version":"0.3.43" }`. No business data is exposed.

### `register_agent`

Inputs: required `agent_id`, `agent_type`, `project_id`; optional `name`, `session_id`, `metadata_json`. Registers or refreshes heartbeat for the agent under an enabled project. Data is the resulting Agent record. Example: `{ "data":{"agentId":"codex-main","projectId":"itoguruma","agentType":"codex"} }`. Invalid metadata, disabled/unknown parent, or conflicting identity returns `validation/agent_registration`; inspect `list_projects` and `list_agents` before retrying.

### `register_project_inbox`

Inputs: `project_id`, `display_name`. Idempotently creates or updates the canonical Project and its matching `project_inbox` agent atomically. Returns the Project record. Example: `{ "projectId":"cardfantasy","displayName":"Card Fantasy","inboxAgentId":"cardfantasy","enabled":true }`. Invalid ID or registration conflict returns `validation/project_registration`; retrying the same request is safe.

### `list_agents`

Input: none. Read-only list of persisted Agents, including IDs, type, project, timestamps, and optional session/metadata. Example: `[{"agentId":"codex-main","agentType":"codex","projectId":"itoguruma"}]`; no records returns `[]`.

### `list_projects`

Input: none. Read-only canonical registry with project ID, display name, inbox agent ID, enabled state, and timestamps. Example: `[{"projectId":"moyai","displayName":"Moyai","inboxAgentId":"moyai-inbox","enabled":true}]`. Call before sending and choose the canonical Project ID.

### `get_project`

Input: `project_id`. Reads one project using case-insensitive matching. Returns a Project record or `validation/project` with `ITG_PROJECT_UNKNOWN`; use `list_projects` to find its canonical ID.

### `add_project`

Inputs: required `project_id`, `inbox_agent_id`; optional `display_name`. Adds an enabled project through the shared registry. Returns its Project record. Example: `{ "projectId":"moyai","inboxAgentId":"moyai-inbox","enabled":true }`. Invalid IDs, unknown/conflicting inbox agents, and registration errors return structured `validation/project` errors. MCP callers are authenticated by the local server; the CLI's interactive confirmation is not part of this MCP operation.

### `update_project`

Inputs: `project_id`; optional `display_name`, `inbox_agent_id` (provide at least one). Updates an existing project and returns its new record. Unknown project, invalid fields, or conflicting inbox identity returns `validation/project`; no partial update is committed.

### `set_project_enabled`

Inputs: `project_id`, `enabled` boolean. Enables or disables an existing project and returns its record. Disabled projects cannot receive new messages; existing history remains. Unknown project returns a structured project error.

### `delete_project`

Input: `project_id`. Destructive: deletes the project and inbox mapping only when retained messages/deliveries do not reference them. Success data is `{ "deleted":true }`. Referenced projects return `ITG_PROJECT_REFERENCED`; review history and use disable when deletion is not valid.

### `unregister_agent`

Input: `agent_id`. Removes one exact agent registration and returns `{ "unregistered":true|false }`. Existing messages or deliveries cause `agent_referenced`; review/delete associated history first only when authorized.

### `delete_agent_history`

Inputs: `agent_id`, `dry_run` boolean. Destructive when `dry_run=false`. First call with `true`; review counts and `canUnregister`. After separate explicit approval, repeat with `false`. Returns `agentId`, `dryRun`, message/delivery/thread counts, `canUnregister`, and `correlationId`; bodies/payloads are never returned. Example: `{ "agentId":"old-agent","dryRun":true,"messageCount":3,"deliveryCount":2,"threadCount":1,"canUnregister":true,"correlationId":"…" }`. Errors distinguish unknown agent, transaction conflict, and database failure. Then unregister separately.

### `send_message`

Inputs: `sender_agent_id`, `provider`, `body`, `thread_id`, and either `recipient` or non-empty `recipients`; optional reply ID, message type, payload JSON, idempotency key. Call `list_projects` first; recipients are Project IDs. Valid unknown IDs create an enabled Project and inbox transactionally. Success returns `{ "messageId":"<id>" }`; same sender/key retry returns the existing message. `message_type` is `message`, `notification`, `system`, or `change_request`; CRs require a valid canonical file and never fall back. Provider is normalized lowercase ASCII letters/digits/hyphens.

Errors distinguish missing references (`sqlite/table/write/reference_key`), argument/payload validation, provider validation, CR validation, invalid/disabled Project recipient, transaction conflict, and database failure. Error data includes recovery guidance and, for Project recipient errors, the attempted ID and up to five candidates. Retry transient/uncertain outcomes with the same idempotency key; do not reuse it for different content.

### `get_messages`

Inputs: `agent_id`, `consumer_agent_id`; optional `limit` (default 50, valid 1–500), `lease_seconds` (default 300; pass a positive value), `thread_id`, `message_type`. Leases pending/expired deliveries and records lease owner, expiry, ID, and attempt count. Returns message records; no eligible delivery returns `[]`. Example: `[{"messageId":"m-1","deliveryStatus":"leased","leaseId":"lease-1","leaseOwnerAgentId":"codex-root","deliveryAttemptCount":1}]`. The implementation does not range-check `lease_seconds`; zero or negative values make the expiry immediate or past and can cause immediate redelivery. Validate the lease owner and ACK using the exact lease ID only after processing.

### `ack_message`

Inputs: `agent_id`, `consumer_agent_id`, `message_id`, `lease_id`. Idempotent; acknowledges only when inbox, consumer, and active lease match. Returns `{ "acked":true|false }`. Example: `{ "acked":true }`. False means the lease is stale or does not match; re-fetch before handling a redelivery.

### `get_conversation_history`

Input: `thread_id`; optional `limit` (default 100, valid 1–500), `offset` (default 0; use a non-negative value). Read-only; returns all message history oldest first, including acknowledged messages. The server enforces the limit range but does not reject negative offsets. Unknown thread returns `[]`. Records contain body and payload, so treat output as potentially sensitive and share only as intended.

### `inspect_change_request`

Input: `payload_json`. Read-only revalidation of a formal CR payload against its canonical file and current status. Returns path, source/target, priority, payload/file status, and match result. Invalid payload/file or status drift returns `validation/change_request`; missing CR root returns `configuration/change_request`. It does not edit, deliver, or repair the CR. Never downgrade an invalid CR to a normal message.

### `get_hook_context`

Required `consumer_agent_id` and exactly one of `agent_id` or `working_directory`; optional event name, limit (default 50), lease seconds (default 300), thread, and type. Leases messages and returns `{ "context":..., "agentId":..., "shouldStop":..., "messages":[...] }`. A directory resolves to the enabled registered Project whose ID matches the containing Git repository name in invariant lowercase; no match returns null `context`/`agentId`, false stop, and an empty list without leasing. `shouldStop` is true only for matching messages on event `Stop`. Process and ACK each leased message.

### `get_auth_status`

Input: none. Read-only. Returns `{ "configured":true|false, "generationId":"<id>|null" }`; never returns the credential or a token fingerprint. Generation ID is diagnostic and is not accepted as authentication.

### `rotate_auth_token`

Input: `confirmation`, which must equal `ROTATE`. Destructive: invalidates the previous token immediately and stores a new token. Success returns `{ "rotated":true, "nextAction":"Restart ..." }`; the secret is never returned. Any other confirmation returns `confirmation_required`. Restart the server and every connected client; do not invoke unless the user authorized rotation.
