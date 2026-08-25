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
        lease incoming messages with get_messages, process them, then acknowledge each message with ack_message.
        Reuse an idempotency_key when retrying the same logical send. For formal change requests, use a validated
        canonical CR file and a change_request message; never downgrade a failed change request to a normal message.
        Use get_conversation_history when prior acknowledged messages are needed. Do not acknowledge a message
        before its requested work or response has been handled.
        """;

    /// <summary>Itogurumaを安全に利用するための再利用可能なガイドを返します。</summary>
    [McpServerPrompt(Name = "itoguruma_guide")]
    [Description("Explain Itoguruma's purpose and the standard agent-to-agent messaging workflow.")]
    public static string GetGuide() => """
        Use Itoguruma to coordinate independent AI agents across project boundaries.

        Workflow:
        1. Call register_agent for the sending and receiving agents.
        2. Call send_message with sender_agent_id, recipient, the actual provider, body, and a stable thread_id.
        3. The recipient calls get_messages to lease pending work.
        4. Complete the request or send the required response before calling ack_message.
        5. Call get_conversation_history when the full thread, including acknowledged messages, is needed.

        Retry the same logical send with the same idempotency_key. Formal change requests must reference a
        validated canonical CR file and use message_type=change_request. If CR validation or delivery fails,
        report the error and retry the CR after correction; do not send it as a normal message.
        """;
}
