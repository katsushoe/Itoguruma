# ADR 0007: Windows credential-backed MCP proxy

## Status

Accepted.

## Context

The Streamable HTTP server previously required clients and the scheduled server process to inherit `ITOGURUMA_AUTH_TOKEN`. User environment propagation is unreliable across already-running desktop processes and exposes configuration through ambient process state.

## Decision

Keep the loopback Streamable HTTP server and bearer boundary. Store the bearer token as the generic Windows credential `Itoguruma/McpBearerToken`. Codex and Claude launch the packaged stdio proxy; the proxy reads the credential and forwards JSON-RPC requests to Streamable HTTP with the bearer header. Runtime paths and endpoints are supplied by the generated configuration file or explicit command arguments, not environment variables.

## Alternatives

- Direct HTTP with a literal header was rejected because it writes the secret into client configuration.
- A fully stdio server was rejected because Itoguruma is a shared, persistent service.
- An unauthenticated loopback proxy was rejected because any process in the user session could call it.

## Consequences

Windows Credential Manager is part of the trust boundary. Install and token rotation update one per-user credential. The proxy never prints the token. The server remains loopback-only, validates origins, and performs fixed-time bearer comparison. Packaging, client registration, upgrade, tests, and user documentation must include the proxy. Codex and Claude compatibility tests must cover initialization, discovery, representative reads and writes, and errors through the proxy.
