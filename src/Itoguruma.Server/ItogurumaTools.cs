using System.ComponentModel;
using System.Text.Json;
using Itoguruma.Core;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Itoguruma.Server;

/// <summary>Itogurumaのメッセージ操作をMCPツールとして公開します。</summary>
[McpServerToolType]
public sealed class ItogurumaTools(MessagingService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SendMessageDescription = """
        Persist and enqueue a message idempotently.

        Error category catalog:
        | Category | Meaning | Recommended response |
        |---|---|---|
        | `sqlite/table/write/reference_key` | A sender, recipient, or reply target does not exist. | Register missing agents or correct the reply target, then retry with the same `idempotency_key`. |
        | `validation/argument` | A parameter value is missing or invalid (e.g. no recipient, unsupported `message_type`, malformed `payload_json`). | Fix the parameter named in the error and retry. |
        | `internal` | The operation failed for an unclassified internal reason. | Inspect the error content before retrying. |
        """;

    /// <summary>実行中のItogurumaバージョンを返します。</summary>
    [McpServerTool(Name = "get_version", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return the running Itoguruma version.")]
    public ToolData<VersionResult> GetVersion() => new(new("itoguruma", ProductInfo.Version));

    /// <summary>エージェントを登録または更新します。</summary>
    [McpServerTool(Name = "register_agent", UseStructuredContent = true)]
    [Description("Register or refresh an agent.")]
    public async Task<ToolData<Agent>> RegisterAgent(string agent_id, string agent_type, string? name = null,
        string? session_id = null, string? metadata_json = null,
        CancellationToken cancellationToken = default) =>
        new(await service.RegisterAgentAsync(
            agent_id, agent_type, name, session_id, metadata_json, cancellationToken));

    /// <summary>登録済みエージェントを返します。</summary>
    [McpServerTool(Name = "list_agents", ReadOnly = true, UseStructuredContent = true)]
    [Description("List registered agents.")]
    public async Task<ToolData<IReadOnlyList<Agent>>> ListAgents(CancellationToken cancellationToken = default) =>
        new(await service.ListAgentsAsync(cancellationToken));

    /// <summary>メッセージを冪等に送信します。</summary>
    [McpServerTool(Name = "send_message", Idempotent = true, UseStructuredContent = true,
        OutputSchemaType = typeof(ToolData<MessageSentResult>))]
    [Description(SendMessageDescription)]
    public async Task<CallToolResult> SendMessage(string sender_agent_id, string body, string thread_id,
        string? recipient = null, IReadOnlyList<string>? recipients = null,
        string? reply_to_message_id = null, string message_type = "message",
        string? payload_json = null, string? idempotency_key = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedRecipients = recipients is { Count: > 0 }
            ? recipients
            : recipient is not null ? [recipient] : [];
        try
        {
            var messageId = await service.SendMessageAsync(new(
                sender_agent_id, resolvedRecipients, body, thread_id, reply_to_message_id,
                message_type, payload_json, idempotency_key), cancellationToken);
            return CreateResult(new MessageSentResult(messageId));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return CreateResult(new ToolError(
                "reference_not_found",
                "sqlite/table/write/reference_key",
                "Itoguruma rejected the message because a referenced sender, recipient, or reply target does not exist.",
                "Register every sender and recipient agent, verify reply_to_message_id when supplied, then retry with the same idempotency_key.",
                true), isError: true);
        }
        catch (ArgumentException exception)
        {
            return CreateResult(new ToolError(
                "invalid_argument",
                "validation/argument",
                exception.Message,
                "Fix the invalid parameter and retry.",
                true), isError: true);
        }
    }

    /// <summary>対象エージェントの保留メッセージをリースします。</summary>
    [McpServerTool(Name = "get_messages", UseStructuredContent = true)]
    [Description("Lease pending messages for an agent.")]
    public async Task<ToolData<IReadOnlyList<Message>>> GetMessages(string agent_id, int limit = 50,
        int lease_seconds = 300, string? thread_id = null,
        CancellationToken cancellationToken = default) =>
        new(await service.GetMessagesAsync(
            agent_id, limit, TimeSpan.FromSeconds(lease_seconds), thread_id, cancellationToken));

    /// <summary>リース済みメッセージを確認済みにします。</summary>
    [McpServerTool(Name = "ack_message", Idempotent = true, UseStructuredContent = true)]
    [Description("Acknowledge a leased message.")]
    public async Task<ToolData<AcknowledgementResult>> AckMessage(string agent_id, string message_id,
        CancellationToken cancellationToken = default) =>
        new(new(await service.AckMessageAsync(agent_id, message_id, cancellationToken)));

    /// <summary>指定Threadの既読・過去分を含む履歴を時系列で返します。</summary>
    [McpServerTool(Name = "get_conversation_history", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return the full message history for a thread_id (conversation id), oldest first, " +
        "including already-acked messages. Returns an empty array for a thread_id with no messages, " +
        "including one that does not exist. Use limit and offset to page through long threads.")]
    public async Task<ToolData<IReadOnlyList<ConversationMessage>>> GetConversationHistory(string thread_id,
        int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
        new(await service.GetConversationHistoryAsync(thread_id, limit, offset, cancellationToken));

    private static CallToolResult CreateResult<T>(T data, bool isError = false)
    {
        var wrapped = new ToolData<T>(data);
        return new()
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(data, JsonOptions) }],
            StructuredContent = JsonSerializer.SerializeToElement(wrapped, JsonOptions),
            IsError = isError
        };
    }
}

/// <summary>MCP構造化出力の互換ラッパーです。</summary>
public sealed record ToolData<T>(T Data);

/// <summary>実行中サーバーの製品情報です。</summary>
public sealed record VersionResult(string Name, string Version);

/// <summary>送信済みメッセージの識別子です。</summary>
public sealed record MessageSentResult(string MessageId);

/// <summary>メッセージ確認結果です。</summary>
public sealed record AcknowledgementResult(bool Acked);

/// <summary>AIが回復方法を判断できるツールエラーです。</summary>
public sealed record ToolError(
    string ErrorCode,
    string Category,
    string Summary,
    string SuggestedAction,
    bool Retryable);
