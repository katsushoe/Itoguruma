# ADR 0006: Transactional agent-history deletion

## Status

Accepted

## Context

An agent registration cannot be removed while messages or deliveries reference it. Operators need a bounded way to inspect and remove history created by an incorrectly used agent ID without exposing message content or damaging unrelated history.

## Decision

Itoguruma provides symmetric MCP and CLI operations named `delete_agent_history` and `delete-agent-history`. Both require an exact, case-sensitive agent ID and support a non-mutating dry-run. Actual deletion removes deliveries addressed to the agent, messages sent by the agent, dependent reply descendants, and their deliveries in one serializable SQLite transaction. It does not unregister the agent; operators run `unregister_agent` afterward.

MCP marks the tool as destructive. The authenticated caller must obtain explicit approval before `dry_run=false`. CLI actual deletion requires the existing interactive five-digit confirmation. Responses contain only the target ID, message/delivery/thread counts, unregisterability, and a correlation ID. Audit records contain the same non-content metadata and timestamp.

Errors distinguish a missing agent, SQLite transaction conflict, and other database failure. Any failure rolls back deliveries, messages, and audit data together.

## Alternatives

- Cascade deletion from `unregister_agent` was rejected because registration removal would hide the destructive data scope and provide no preview.
- Deleting only direct sender and recipient rows was rejected because reply foreign keys could remain and make the operation fail or leave dependent history inconsistent.
- Returning message IDs or bodies in dry-run was rejected because counts are sufficient for approval and reduce sensitive-data exposure.

## Consequences and scope

The database schema adds audit count and correlation columns. Core storage, MCP, CLI, tests, command documentation, and security guidance must remain aligned. Authentication remains the loopback bearer-token boundary. Operators must dry-run, obtain explicit approval, delete, then unregister. Tests cover dry-run, successful deletion, empty history, missing agents, exact targeting, reply foreign keys, rollback, MCP annotation/authentication, CLI parity, and post-delete unregister.
