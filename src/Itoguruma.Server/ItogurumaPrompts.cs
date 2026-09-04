using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Itoguruma.Server;

/// <summary>Itogurumaの目的と標準的な利用手順をMCPクライアントへ提供します。</summary>
[McpServerPromptType]
public sealed class ItogurumaPrompts
{
    /// <summary>MCP初期化時にクライアントへ通知するサーバー利用指針です。</summary>
    public const string ServerInstructions = """
        Itoguruma is a message relay for independent AI agents working in separate projects.
        Use it for direct cross-project questions, notifications, progress checks, and formal change requests;
        it does not execute another agent's work or replace that project's task manager.
        Register each agent before messaging. Send with the actual execution provider and a stable thread_id,
        lease incoming messages with get_messages using the inbox and consumer Agent IDs, retain each lease_id,
        process them, then acknowledge each message with ack_message using the same consumer Agent ID and lease_id.
        Reuse an idempotency_key when retrying the same logical send. For formal change requests, use a validated
        canonical CR file and a change_request message; never downgrade a failed change request to a normal message.
        Use get_conversation_history when prior acknowledged messages are needed. Do not acknowledge a message
        before its requested work or response has been handled.
        Before send_message, call list_projects and select the canonical destination Project ID using
        ordinal case-insensitive matching. Project IDs are repository names normalized with invariant lowercase
        and must match ^[a-z][a-z0-9]*$. Never infer a destination from a display name, runtime Agent ID,
        client, session, or sender_agent_id. A valid unknown Project ID is automatically registered.
        """;

    /// <summary>Itogurumaを安全に利用するための再利用可能なガイドを返します。</summary>
    [McpServerPrompt(Name = "itoguruma_guide")]
    [Description("Explain Itoguruma's purpose and the standard agent-to-agent messaging workflow.")]
    public static string GetGuide() => """
        Use Itoguruma to coordinate independent AI agents across project boundaries.

        Workflow:
        1. Call register_project_inbox for initial project setup, then call register_agent with its canonical project_id for runtime agents.
        2. Call list_projects and select the canonical destination Project ID using case-insensitive matching.
        3. Call send_message with sender_agent_id, the selected Project ID, the actual provider, body, and a stable thread_id.
        4. The recipient calls get_messages with its inbox Agent ID and consumer Agent ID to lease pending work.
        5. Complete the request or send the required response before calling ack_message with the returned lease_id.
        6. Call get_conversation_history when the full thread, including acknowledged messages, is needed.

        Project IDs are repository names normalized with invariant lowercase and must match
        ^[a-z][a-z0-9]*$. Do not use a display name, runtime Agent ID, client, session, or sender_agent_id as
        a destination. A valid unknown Project ID is automatically registered. If send_message returns
        candidates, choose only when the intended project is unambiguous; otherwise ask the user.

        Retry the same logical send with the same idempotency_key. Formal change requests must reference a
        validated canonical CR file and use message_type=change_request. If CR validation or delivery fails,
        report the error and retry the CR after correction; do not send it as a normal message.
        """;
}
