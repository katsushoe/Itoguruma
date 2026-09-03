# ADR 0005: Project registry and automatic destination registration

## Status

Accepted

## Context

Message delivery previously required every recipient Agent or project to be registered. A project must remain a valid destination while its worker Agent is offline, and senders must be able to address a new project without a separate interactive registration step.

## Decision

SQLite `projects` is the canonical project registry. Message recipients are Project IDs, not runtime Agent IDs. A recipient is normalized with invariant lowercase and must match `^[a-z][a-z0-9]*$`. If the normalized recipient is not registered, delivery transactionally creates an enabled project whose `project_id`, display name, and `inbox_agent_id` all equal the normalized recipient, creates its `project_inbox` Agent, and queues the delivery there. Malformed IDs and disabled projects are rejected.

Project IDs use ordinal case-insensitive matching across registration, lookup, mutation, deletion, and recipient resolution. The invariant-lowercase spelling is canonical, and a unique case-insensitive index prevents IDs that differ only by case. Agent IDs remain case-sensitive and may contain identifiers that Project IDs prohibit, but they are used only for sender identity and inbox leasing.

Project list/show and administrative mutations remain CLI operations. MCP exposes `register_project_inbox` as the single explicit, idempotent initialization operation and `list_projects` as read-only discovery. The registration transaction creates or updates both registry rows, repairs an orphaned inbox of the correct type, and rejects an existing agent of another type. Runtime `register_agent` requires an enabled existing parent `project_id`; the database stores that relationship, while legacy rows remain nullable after migration. `metadata_json.projectId` has no integrity role. Administrative mutations require a cryptographically random five-digit code to be re-entered through a non-redirected console within 60 seconds and permit three attempts.

Server instructions, the reusable MCP guide, and `send_message` tool metadata direct AI clients to call `list_projects` before sending and to use ordinal case-insensitive matching. When a malformed destination is rejected, the structured error includes the attempted recipient and up to five related registered projects, including enabled state. Prefix relationships are ranked before bounded edit distance. Low-similarity projects are omitted, and the server never selects or retries a destination automatically.

Project mutation and inbox-registration events are written to `audit_log` with only event type, subject ID, and UTC timestamp. Referenced projects cannot be deleted and must be disabled. No message or delivery is cascade-deleted.

## Alternatives

- Direct runtime-Agent recipients were rejected because the same untyped recipient field cannot distinguish an Agent ID such as `moyai-codex-root` from a malformed Project ID.
- MCP mutations were rejected because bearer-token possession must not grant routing-registry administration. Read-only project discovery does not mutate routing state.
- JSON as the runtime registry was rejected because concurrent mutation and transactional inbox creation require a single SQLite source of truth.
- Rejecting every unknown recipient was rejected because it forces a separate administrative step before first contact with a project.

## Consequences

Administrators must use an interactive local terminal for explicit mutations. Authenticated senders can create a destination implicitly by sending to a new valid Project ID; therefore recipient identifiers must be treated as routing input, not as pre-approved authorization data. Offline workers retrieve messages using the configured `inbox_agent_id`; automatically registered projects use their Project ID as that inbox ID, and inbox and runtime Agents remain distinguishable by `agent_type`.

Implementation, CLI help, command/security documentation, fixed error codes, migration, concurrency tests, and interactive-console tests must remain aligned with this ADR.
