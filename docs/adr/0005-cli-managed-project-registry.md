# ADR 0005: Project registry and automatic destination registration

## Status

Accepted

## Context

Message delivery previously required every recipient Agent or project to be registered. A project must remain a valid destination while its worker Agent is offline, and senders must be able to address a new project without a separate interactive registration step.

## Decision

SQLite `projects` is the canonical project registry. If a message recipient is neither an Agent ID nor a registered project ID, delivery transactionally creates an enabled project whose `project_id`, display name, and `inbox_agent_id` all equal the supplied recipient, creates its `project_inbox` Agent, and queues the delivery there. Existing Agent recipients retain precedence and disabled projects remain rejected.

Project IDs use ordinal case-insensitive matching across registration, lookup, mutation, deletion, and recipient resolution. The originally registered spelling remains canonical, and a unique case-insensitive index prevents IDs that differ only by case. Agent IDs remain case-sensitive.

Project list/show and explicit mutations are CLI operations. Explicit mutations require a cryptographically random five-digit code to be re-entered through a non-redirected console within 60 seconds and permit three attempts. Message delivery is the sole automatic creation path and records `project_auto_registered` plus `project_inbox_registered` audit events in the same transaction. MCP intentionally exposes no general project mutation tool.

Project mutation and inbox-registration events are written to `audit_log` with only event type, subject ID, and UTC timestamp. Referenced projects cannot be deleted and must be disabled. No message or delivery is cascade-deleted.

## Alternatives

- MCP mutations were rejected because bearer-token possession must not grant routing-registry administration.
- JSON as the runtime registry was rejected because concurrent mutation and transactional inbox creation require a single SQLite source of truth.
- Rejecting every unknown recipient was rejected because it forces a separate administrative step before first contact with a project.

## Consequences

Administrators must use an interactive local terminal for explicit mutations. Authenticated senders can create a destination implicitly by sending to a new project ID; therefore recipient identifiers must be treated as routing input, not as pre-approved authorization data. Offline workers retrieve messages using the configured `inbox_agent_id`; automatically registered projects use their project ID as that inbox ID, and inbox and runtime Agents remain distinguishable by `agent_type`.

Implementation, CLI help, command/security documentation, fixed error codes, migration, concurrency tests, and interactive-console tests must remain aligned with this ADR.
