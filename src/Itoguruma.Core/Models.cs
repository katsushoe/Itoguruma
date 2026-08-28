namespace Itoguruma.Core;

public sealed record Agent(string AgentId, string Name, string AgentType, string? SessionId,
    DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? MetadataJson);

public sealed record Message(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt, string DeliveryStatus, DateTimeOffset? LeaseUntil);

public sealed record SendMessageRequest(string SenderAgentId, IReadOnlyList<string> Recipients,
    string Body, string ThreadId, string Provider, string? ReplyToMessageId = null,
    string MessageType = "message", string? PayloadJson = null, string? IdempotencyKey = null);

public sealed record ConversationMessage(string MessageId, string ThreadId, string SenderAgentId,
    string Provider, string? ReplyToMessageId, string MessageType, string Body, string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record Project(string ProjectId, string DisplayName, string InboxAgentId, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ProjectMutation(string ProjectId, string? DisplayName = null,
    string? InboxAgentId = null);

public static class ProjectErrorCodes
{
    public const string UnknownProject = "ITG_PROJECT_UNKNOWN";
    public const string DisabledProject = "ITG_PROJECT_DISABLED";
    public const string ProjectReferenced = "ITG_PROJECT_REFERENCED";
    public const string ProjectCaseConflict = "ITG_PROJECT_CASE_CONFLICT";
    public const string ConfirmationFailed = "ITG_CONFIRMATION_FAILED";
    public const string ConfirmationExpired = "ITG_CONFIRMATION_EXPIRED";
    public const string ConsoleRedirected = "ITG_CONSOLE_REDIRECTED";
}

public sealed class ProjectOperationException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}
