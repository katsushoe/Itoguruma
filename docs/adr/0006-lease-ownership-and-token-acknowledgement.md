# ADR 0006: Lease ownership and token-bound acknowledgement

## Status

Accepted

## Context

A Project has one Project Inbox Agent, but multiple runtime agents may compete for its messages. The previous delivery schema recorded only the inbox Agent ID and lease expiry. Any process using that inbox ID could acknowledge another process's active lease.

## Decision

Each lease receives a cryptographically random `lease_id`, records `lease_owner_agent_id`, and increments `delivery_attempt_count`. Message acquisition requires the runtime `consumer_agent_id`. Acknowledgement succeeds only when the inbox Agent ID, consumer Agent ID, message ID, active status, and lease ID all match.

Database schema version 9 adds nullable lease identity columns so existing databases migrate in place. A newly acquired or reacquired lease always replaces both identity values. MCP and CLI acquisition and acknowledgement parameters change together; legacy acknowledgement without a lease ID is not retained because it would preserve the vulnerability.

## Alternatives

- Keep acknowledgement keyed only by inbox and message: rejected because it cannot identify the lease holder.
- Use only the consumer Agent ID: rejected because a stale holder could acknowledge a later lease owned by the same runtime identity.
- Dispatch directly to normal Agents: deferred because routing policy and lease ownership are separate concerns.

## Consequences

Clients must retain the returned `lease_id` and provide it with the same `consumer_agent_id` when acknowledging. Expired leases receive a new ID, invalidating stale acknowledgements. Monitoring can expose owner and attempt information without logging the secret lease ID.

## Security and operations

The full lease ID must not be written to logs. Operators can correlate work by inbox, consumer, message ID, and attempt count. Installers must configure hooks with both inbox and consumer identities.

## Implementation and verification

The core store, MCP tools, CLI, installer hooks, user documentation, migration tests, owner-mismatch tests, token-mismatch tests, and process integration tests must remain aligned.
