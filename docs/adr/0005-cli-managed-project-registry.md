# ADR 0005: CLI-managed known-project registry

## Status

Accepted

## Context

Message delivery previously required every recipient Agent to be running and registered. A project must remain a valid CR destination while its worker Agent is offline, but allowing authenticated MCP clients to create destinations would expand the routing trust boundary.

## Decision

SQLite `projects` is the canonical known-project registry. If a message recipient is not an Agent ID, delivery resolves an enabled matching `project_id`, transactionally creates its unique `project_inbox` Agent, and queues the delivery there. Existing Agent recipients retain precedence and behavior.

Project list/show and all mutations are CLI operations. Mutations require a cryptographically random five-digit code to be re-entered through a non-redirected console within 60 seconds and permit three attempts. The code is never accepted through arguments, environment, configuration, MCP, logs, exceptions, or audit data. MCP intentionally exposes no project mutation tool.

Project mutation and inbox-registration events are written to `audit_log` with only event type, subject ID, and UTC timestamp. Referenced projects cannot be deleted and must be disabled. No message or delivery is cascade-deleted.

## Alternatives

- MCP mutations were rejected because bearer-token possession must not grant routing-registry administration.
- JSON as the runtime registry was rejected because concurrent mutation and transactional inbox creation require a single SQLite source of truth.
- Automatic creation for every unknown recipient was rejected because it would bypass the known-project trust boundary.

## Consequences

Administrators must use an interactive local terminal for mutations. AI systems or scripts that cannot type into that terminal cannot administer projects. A subject with terminal-input control is outside this safeguard's threat model. Offline workers retrieve messages using the configured `inbox_agent_id`; inbox and runtime Agents remain distinguishable by `agent_type`.

Implementation, CLI help, command/security documentation, fixed error codes, migration, concurrency tests, and interactive-console tests must remain aligned with this ADR.
