using System.ComponentModel;
using System.Text.Json;
using Itoguruma.Core;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Itoguruma.Server;

/// <summary>Itogurumaのメッセージ操作をMCPツールとして公開します。</summary>
[McpServerToolType]
public sealed class ItogurumaTools(MessagingService service, AuthenticationTokenService tokenService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SendMessageDescription = """
        Persist and enqueue a message idempotently.
        Before sending, call list_projects and select the canonical destination Project ID using ordinal
        case-insensitive matching. Project IDs are normalized with invariant lowercase and must match
        ^[a-z][a-z0-9]*$. Valid unknown Project IDs are automatically registered.

        Error category catalog:
        | Category | Meaning | Recommended response |
        |---|---|---|
        | `sqlite/table/write/reference_key` | A sender, recipient, or reply target does not exist. | Register missing agents or correct the reply target, then retry with the same `idempotency_key`. |
        | `validation/argument` | A parameter value is missing or invalid (e.g. no recipient, unsupported `message_type`, malformed `payload_json`). | Fix the parameter named in the error and retry. |
        | `validation/provider` | The required provider is missing or invalid. | Supply the sender provider using lowercase ASCII letters, digits, or hyphens, then retry with the same `idempotency_key`. |
        | `validation/change_request` | A CR path, payload field, canonical file field, or status is invalid or inconsistent. | Correct the CR payload or canonical file; do not fall back to a normal message. |
        | `validation/project_recipient` | The recipient Project ID is malformed or the project is disabled. | Use list_projects, select the canonical Project ID, and retry with the same `idempotency_key`. |
        | `internal` | The operation failed for an unclassified internal reason. | Inspect the error content before retrying. |
        """;

    /// <summary>実行中のItogurumaバージョンを返します。</summary>
    [McpServerTool(Name = "get_version", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return the running Itoguruma version.")]
    public ToolData<VersionResult> GetVersion() => new(new("itoguruma", ProductInfo.Version));

    /// <summary>エージェントを登録または更新します。</summary>
    [McpServerTool(Name = "register_agent", UseStructuredContent = true)]
    [Description("Register or refresh an agent.")]
    public async Task<CallToolResult> RegisterAgent(string agent_id, string agent_type, string project_id, string? name = null,
        string? session_id = null, string? metadata_json = null,
        CancellationToken cancellationToken = default)
    {
        try { return CreateResult(await service.RegisterAgentAsync(
            agent_id, agent_type, project_id, name, session_id, metadata_json, cancellationToken)); }
        catch (ProjectOperationException exception)
        {
            return CreateResult(new ToolError(exception.ErrorCode, "validation/agent_registration", exception.Message,
                "Select an enabled parent with list_projects or resolve the conflicting agent registration.", false), isError: true);
        }
    }

    [McpServerTool(Name = "register_project_inbox", Idempotent = true, UseStructuredContent = true)]
    [Description("Atomically register or update a canonical Project and its Project Inbox agent.")]
    public async Task<CallToolResult> RegisterProjectInbox(string project_id, string display_name,
        CancellationToken cancellationToken = default)
    {
        try { return CreateResult(await service.RegisterProjectInboxAsync(project_id, display_name, cancellationToken)); }
        catch (ProjectOperationException exception)
        {
            return CreateResult(new ToolError(exception.ErrorCode, "validation/project_registration", exception.Message,
                "Correct the project ID or resolve the conflicting registration, then retry.", false), isError: true);
        }
    }

    /// <summary>登録済みエージェントを返します。</summary>
    [McpServerTool(Name = "list_agents", ReadOnly = true, UseStructuredContent = true)]
    [Description("List registered agents.")]
    public async Task<ToolData<IReadOnlyList<Agent>>> ListAgents(CancellationToken cancellationToken = default) =>
        new(await service.ListAgentsAsync(cancellationToken));

    /// <summary>登録済みプロジェクトを返します。</summary>
    [McpServerTool(Name = "list_projects", ReadOnly = true, UseStructuredContent = true)]
    [Description("List registered message destination projects. Call this before send_message and select the canonical Project ID.")]
    public async Task<ToolData<IReadOnlyList<Project>>> ListProjects(CancellationToken cancellationToken = default) =>
        new(await service.ListProjectsAsync(cancellationToken));

    /// <summary>エージェント登録を削除します。</summary>
    [McpServerTool(Name = "unregister_agent", UseStructuredContent = true)]
    [Description("Remove an agent registration. Fails if the agent is referenced by existing messages or deliveries.")]
    public async Task<CallToolResult> UnregisterAgent(string agent_id, CancellationToken cancellationToken = default)
    {
        try
        {
            var unregistered = await service.UnregisterAgentAsync(agent_id, cancellationToken);
            return CreateResult(new UnregisterResult(unregistered));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return CreateResult(new ToolError(
                "agent_referenced",
                "sqlite/table/write/reference_key",
                "Itoguruma rejected the removal because this agent is referenced by existing messages or deliveries.",
                "This agent has message history and cannot be removed without deleting that history first.",
                false), isError: true);
        }
    }

    /// <summary>対象エージェントに関係するメッセージ履歴を削除または事前確認します。</summary>
    [McpServerTool(Name = "delete_agent_history", Destructive = true, UseStructuredContent = true)]
    [Description("Preview or delete all message and delivery history associated with one exact agent ID. " +
        "dry_run=true does not delete data. The operation never returns message bodies or payloads.")]
    public async Task<CallToolResult> DeleteAgentHistory(string agent_id, bool dry_run,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return CreateResult(await service.DeleteAgentHistoryAsync(agent_id, dry_run, cancellationToken));
        }
        catch (AgentHistoryOperationException exception)
        {
            return CreateResult(new ToolError(
                exception.ErrorCode,
                "validation/agent",
                exception.Message,
                "Verify the exact agent_id with list_agents and retry.",
                false), isError: true);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            return CreateResult(new ToolError(
                "agent_history_conflict",
                "sqlite/transaction/conflict",
                "The agent history transaction conflicted with another database operation.",
                "Wait for the other operation to finish, then retry dry-run before deletion.",
                true), isError: true);
        }
        catch (SqliteException)
        {
            return CreateResult(new ToolError(
                "agent_history_database_failure",
                "sqlite/transaction/failure",
                "The agent history transaction failed and was rolled back.",
                "Inspect the server log by correlation time, correct the database problem, and retry dry-run.",
                true), isError: true);
        }
    }

    /// <summary>メッセージを冪等に送信します。</summary>
    [McpServerTool(Name = "send_message", Idempotent = true, UseStructuredContent = true,
        OutputSchemaType = typeof(ToolData<MessageSentResult>))]
    [Description(SendMessageDescription)]
    public async Task<CallToolResult> SendMessage(string sender_agent_id, string provider, string body, string thread_id,
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
                sender_agent_id, resolvedRecipients, body, thread_id, provider, reply_to_message_id,
                message_type, payload_json, idempotency_key), cancellationToken);
            return CreateResult(new MessageSentResult(messageId));
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return CreateResult(new ToolError(
                "reference_not_found",
                "sqlite/table/write/reference_key",
                "Itoguruma rejected the message because a referenced sender, recipient, or reply target does not exist.",
                "Register the sender agent, verify reply_to_message_id when supplied, then retry with the same idempotency_key.",
                true), isError: true);
        }
        catch (ProviderValidationException exception)
        {
            return CreateResult(new ToolError(
                "invalid_provider",
                "validation/provider",
                exception.Message,
                "Supply the sender provider using lowercase ASCII letters, digits, or hyphens, then retry with the same idempotency_key.",
                true), isError: true);
        }
        catch (ProjectOperationException exception)
        {
            return CreateResult(new ProjectRecipientToolError(
                exception.ErrorCode,
                "validation/project_recipient",
                exception.Message,
                "Call list_projects, select the intended canonical Project ID, and retry with the same idempotency_key.",
                true,
                exception.AttemptedProjectId,
                exception.Candidates), isError: true);
        }
        catch (ArgumentException exception)
        {
            var isChangeRequest = string.Equals(message_type, "change_request", StringComparison.Ordinal);
            return CreateResult(new ToolError(
                isChangeRequest ? "invalid_change_request" : "invalid_argument",
                isChangeRequest ? "validation/change_request" : "validation/argument",
                exception.Message,
                isChangeRequest
                    ? "Correct the CR payload or canonical file; do not fall back to a normal message."
                    : "Fix the invalid parameter and retry.",
                true), isError: true);
        }
    }

    /// <summary>対象エージェントの保留メッセージをリースします。</summary>
    [McpServerTool(Name = "get_messages", UseStructuredContent = true)]
    [Description("Lease pending messages from an inbox for a consumer agent and return a lease_id for acknowledgement.")]
    public async Task<ToolData<IReadOnlyList<Message>>> GetMessages(string agent_id, string consumer_agent_id, int limit = 50,
        int lease_seconds = 300, string? thread_id = null, string? message_type = null,
        CancellationToken cancellationToken = default) =>
        new(await service.GetMessagesAsync(
            agent_id, limit, TimeSpan.FromSeconds(lease_seconds), thread_id, message_type,
            consumer_agent_id, cancellationToken));

    /// <summary>CRファイルの現在状態と保存済みpayloadの整合性を検査します。</summary>
    [McpServerTool(Name = "inspect_change_request", ReadOnly = true, UseStructuredContent = true)]
    [Description("Validate a change_request payload against its canonical CR file and report status drift.")]
    public async Task<CallToolResult> InspectChangeRequest(string payload_json,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return CreateResult(await service.InspectChangeRequestAsync(payload_json, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return CreateResult(new ToolError(
                "invalid_change_request",
                "validation/change_request",
                exception.Message,
                "Correct the CR payload or canonical CR file and retry.",
                true), isError: true);
        }
        catch (InvalidOperationException exception)
        {
            return CreateResult(new ToolError(
                "change_request_not_configured",
                "configuration/change_request",
                exception.Message,
                "Configure Itoguruma:CrRoot or ITOGURUMA_CR_ROOT and restart the server.",
                true), isError: true);
        }
    }

    /// <summary>リース済みメッセージを確認済みにします。</summary>
    [McpServerTool(Name = "ack_message", Idempotent = true, UseStructuredContent = true)]
    [Description("Acknowledge a message only when inbox, consumer agent, and lease_id match the active lease.")]
    public async Task<ToolData<AcknowledgementResult>> AckMessage(string agent_id, string consumer_agent_id,
        string message_id, string lease_id,
        CancellationToken cancellationToken = default) =>
        new(new(await service.AckMessageAsync(agent_id, consumer_agent_id, message_id, lease_id, cancellationToken)));

    /// <summary>指定Threadの既読・過去分を含む履歴を時系列で返します。</summary>
    [McpServerTool(Name = "get_conversation_history", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return the full message history for a thread_id (conversation id), oldest first, " +
        "including already-acked messages. Returns an empty array for a thread_id with no messages, " +
        "including one that does not exist. Use limit and offset to page through long threads.")]
    public async Task<ToolData<IReadOnlyList<ConversationMessage>>> GetConversationHistory(string thread_id,
        int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
        new(await service.GetConversationHistoryAsync(thread_id, limit, offset, cancellationToken));

    /// <summary>CLI hookと同じ形式の受信コンテキストを返します。</summary>
    [McpServerTool(Name = "get_hook_context", UseStructuredContent = true)]
    [Description("Lease messages and format the same context produced by the CLI hook command.")]
    public async Task<ToolData<HookContextResult>> GetHookContext(string agent_id, string consumer_agent_id,
        string? hook_event_name = null, int limit = 50, int lease_seconds = 300,
        string? thread_id = null, string? message_type = null,
        CancellationToken cancellationToken = default)
    {
        var messages = await service.GetMessagesAsync(agent_id, limit, TimeSpan.FromSeconds(lease_seconds),
            thread_id, message_type, consumer_agent_id, cancellationToken);
        var context = messages.Count == 0 ? null :
            AppLocalization.Text("Itoguruma inbox messages:\n", "Itoguruma受信メッセージ:\n") +
            JsonSerializer.Serialize(messages, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return new(new(context, messages.Count > 0 &&
            string.Equals(hook_event_name, "Stop", StringComparison.Ordinal), messages));
    }

    /// <summary>認証トークンの設定状態を返します。</summary>
    [McpServerTool(Name = "get_auth_status", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return whether the user authentication token is configured without exposing its value.")]
    public ToolData<AuthStatusResult> GetAuthStatus() => new(new(tokenService.IsConfigured));

    /// <summary>認証トークンを確認文字列付きで更新します。</summary>
    [McpServerTool(Name = "rotate_auth_token", Destructive = true, UseStructuredContent = true)]
    [Description("Rotate the user authentication token when confirmation is exactly ROTATE. " +
        "The token value is never returned. Restart the server and all clients afterward.")]
    public CallToolResult RotateAuthToken(string confirmation)
    {
        if (!string.Equals(confirmation, "ROTATE", StringComparison.Ordinal))
        {
            return CreateResult(new ToolError(
                "confirmation_required", "validation/confirmation",
                "Token rotation requires confirmation to be exactly ROTATE.",
                "Review the restart and client reconfiguration impact, then retry with confirmation=ROTATE.",
                true), isError: true);
        }

        tokenService.Rotate();
        return CreateResult(new AuthRotationResult(true,
            "Restart ItogurumaServer, Codex, Claude Code, and clients that store the bearer token directly."));
    }

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

/// <summary>エージェント削除結果です。</summary>
public sealed record UnregisterResult(bool Unregistered);

/// <summary>CLI hook互換のコンテキストです。</summary>
public sealed record HookContextResult(string? Context, bool ShouldStop, IReadOnlyList<Message> Messages);

/// <summary>認証トークンの設定状態です。</summary>
public sealed record AuthStatusResult(bool Configured);

/// <summary>認証トークン更新結果です。</summary>
public sealed record AuthRotationResult(bool Rotated, string NextAction);

/// <summary>AIが回復方法を判断できるツールエラーです。</summary>
public sealed record ToolError(
    string ErrorCode,
    string Category,
    string Summary,
    string SuggestedAction,
    bool Retryable);

/// <summary>宛先Project IDの修正候補を含むツールエラーです。</summary>
public sealed record ProjectRecipientToolError(
    string ErrorCode,
    string Category,
    string Summary,
    string SuggestedAction,
    bool Retryable,
    string? AttemptedRecipient,
    IReadOnlyList<ProjectCandidate> Candidates);
